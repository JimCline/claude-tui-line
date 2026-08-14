using System.Text.RegularExpressions;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5/§6.5/§8: the single up-front resolution pass — one dictionary of every
/// item id's current value, built once per render before sizing begins, and shared by
/// <see cref="LeafItems"/>, <see cref="SizeResolver"/>, and <see cref="ColorResolution"/> alike so
/// nothing downstream re-fetches or re-derives a value. An id is collected — and therefore
/// resolved — whether or not a pane actually places it: a <c>colors</c>-table token's <c>from</c>
/// (§6.3), an inline rule's explicit <c>from</c>, or a derived item's <see cref="PaneItem.From"/>
/// may name a builtin no pane displays. A builtin resolves through <see cref="ItemRegistry"/>
/// regardless of placement; a <c>command</c> item resolves only when some placed
/// <see cref="PaneItem"/> actually carries it, since a command has no registry entry to drive it
/// independent of placement. A derived item (§8: <see cref="PaneItem.From"/>/
/// <see cref="PaneItem.Extract"/>/<see cref="PaneItem.Case"/>) resolves in a final pass, once every
/// builtin/command value above is settled.
/// </summary>
public static class ItemValueResolver
{
    /// <summary>
    /// Builtins-only, synchronous counterpart to <see cref="ResolveAsync"/> — same id collection
    /// and the same <see cref="ItemRegistry"/> resolution, minus command-item execution. For
    /// callers (tests, the fixpoint sizing harness) that need a values dictionary without paying
    /// for async command spawns.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Resolve(
        Pane root,
        ItemContext ctx,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens = null)
    {
        var items = new List<(PaneItem Item, bool Eligible)>();
        var colorExprs = new List<ColorResolution.ColorExpr>();
        Walk(root, items, colorExprs);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in CollectIds(items, colorExprs, tokens))
        {
            if (ItemRegistry.Find(id) is { } def)
            {
                values[id] = def.ResolveValue(ctx);
            }
        }

        ResolveDerived(items, values);
        return values;
    }

    /// <summary>
    /// The production resolver: builtins as above, plus every placed <c>command</c> item, spawned
    /// concurrently (§5 gives each its own TTL/timeout — nothing serializes them against each
    /// other). <paramref name="tokens"/> is the parsed <c>colors</c> table, needed here only to
    /// widen id collection (§6.3), not to resolve any colour itself (§6.5 resolves colour
    /// separately, from this method's returned values).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string?>> ResolveAsync(
        Pane root,
        ItemContext ctx,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens,
        string? rawStdinJson,
        string cacheDir)
    {
        var items = new List<(PaneItem Item, bool Eligible)>();
        var colorExprs = new List<ColorResolution.ColorExpr>();
        Walk(root, items, colorExprs);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var commandTasks = new List<(string Id, Task<string?> Task)>();

        foreach (var id in CollectIds(items, colorExprs, tokens))
        {
            if (ItemRegistry.Find(id) is { } def)
            {
                values[id] = def.ResolveValue(ctx);
                continue;
            }

            var placed = items.FirstOrDefault(e => e.Item.Id == id && e.Item.Command is { Count: > 0 });
            if (placed.Item is not null)
            {
                commandTasks.Add((id, CommandProvider.ResolveAsync(placed.Item, rawStdinJson, ctx.Input.Cwd, cacheDir, placed.Eligible)));
            }
        }

        await Task.WhenAll(commandTasks.Select(t => t.Task)).ConfigureAwait(false);
        foreach (var (id, task) in commandTasks)
        {
            values[id] = task.Result;
        }

        ResolveDerived(items, values);
        return values;
    }

    private static void Walk(Pane pane, List<(PaneItem Item, bool Eligible)> items, List<ColorResolution.ColorExpr> colorExprs)
    {
        colorExprs.Add(pane.Border.Color);

        var eligible = !SizeResolver.IsContentSized(pane);
        foreach (var item in pane.Items)
        {
            items.Add((item, eligible));
            if (item.Color is { } color)
            {
                colorExprs.Add(color);
            }
        }

        foreach (var child in pane.Children)
        {
            Walk(child, items, colorExprs);
        }
    }

    private static IReadOnlyList<string> CollectIds(
        List<(PaneItem Item, bool Eligible)> items,
        List<ColorResolution.ColorExpr> colorExprs,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (item, _) in items)
        {
            if ((item.Id ?? item.Item) is { } id)
            {
                ids.Add(id);
            }

            if (item.From is { Length: > 0 } from)
            {
                ids.Add(from);
            }
        }

        foreach (var expr in colorExprs)
        {
            if (expr is ColorResolution.ColorExpr.Inline { Rule.From: { Length: > 0 } from })
            {
                ids.Add(from);
            }
        }

        if (tokens is not null)
        {
            foreach (var rule in tokens.Values)
            {
                if (rule.From is { Length: > 0 } from)
                {
                    ids.Add(from);
                }
            }
        }

        return ids.ToList();
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §8: computes every derived item's (<see cref="PaneItem.From"/>) value
    /// from a snapshot of <paramref name="values"/> taken before any derived result is written
    /// back, then merges all of them in afterward. This makes chaining — a derived item's
    /// <see cref="PaneItem.From"/> naming another derived item — structurally impossible rather
    /// than merely discouraged: regardless of declaration order, every derived item can only ever
    /// see a builtin/command value.
    /// </summary>
    private static void ResolveDerived(List<(PaneItem Item, bool Eligible)> items, Dictionary<string, string?> values)
    {
        var snapshot = new Dictionary<string, string?>(values, StringComparer.Ordinal);
        var derived = new List<(string Id, string? Value)>();

        foreach (var (item, _) in items)
        {
            if (item.From is not { Length: > 0 } from || item.Id is not { Length: > 0 } id)
            {
                continue;
            }

            var value = snapshot.GetValueOrDefault(from);
            if (value is not null && item.Extract is { Length: > 0 } pattern)
            {
                value = ExtractValue(value, pattern);
            }

            if (value is not null)
            {
                value = ApplyCase(value, item.Case);
            }

            derived.Add((id, value));
        }

        foreach (var (id, value) in derived)
        {
            values[id] = value;
        }
    }

    // §8: the first capture group when the pattern has one, otherwise the whole match; no match
    // suppresses the derived item entirely (null), the same convention as an absent field (§3).
    private static string? ExtractValue(string source, string pattern)
    {
        var match = Regex.Match(source, pattern);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    // §8: any case value other than "upper"/"lower" passes the text through unchanged.
    private static string ApplyCase(string value, string? caseMode) => caseMode?.ToLowerInvariant() switch
    {
        "upper" => value.ToUpperInvariant(),
        "lower" => value.ToLowerInvariant(),
        _ => value,
    };
}
