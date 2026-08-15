namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §8: resolves a leaf pane's configured <c>items</c> list against
/// <paramref name="values"/> — the single up-front resolution pass's output (<see
/// cref="ItemValueResolver"/>), keyed by each item's own id (a builtin's <see
/// cref="PaneItem.Item"/>, or a <c>command</c> item's <see cref="PaneItem.Id"/>). An id with no
/// entry in <paramref name="values"/> resolves to null (suppressed), exactly like a
/// genuinely-unrecognized id.
/// </summary>
public static class LeafItems
{
    /// <param name="Value">The raw value — what a §6 color-threshold rule reads.</param>
    /// <param name="Display">
    /// SPEC-V2-FRAMEWORK.md §4: the rendered segment — <c>Plain</c> for width measurement,
    /// <c>Markup</c> for any colour the item applies to itself. An explicit per-item
    /// <c>format</c> in config applies to <paramref name="Value"/> and replaces the row's default
    /// entirely, including its internal markup; otherwise it's the builtin's own registry-row
    /// default (<see cref="ItemRegistry.ItemDefinition.BuildDefaultSegment"/>), or
    /// <paramref name="Value"/> unchanged for an id with no registry row (a <c>command</c> item).
    /// </param>
    public sealed record ResolvedItem(PaneItem Config, string? Value, Segment? Display);

    public static IReadOnlyList<ResolvedItem> Resolve(
        IReadOnlyList<PaneItem> items,
        IReadOnlyDictionary<string, string?> values,
        ItemContext ctx,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens = null)
    {
        var resolved = new List<ResolvedItem>(items.Count);
        foreach (var item in items)
        {
            if (item.Parts is { } parts)
            {
                var compound = BuildCompound(item, parts, values, ctx, tokens);
                // SPEC-V2-FRAMEWORK.md §3.3/§5.5: a compound has no registry/command id to key
                // `values` by, so its own Value is the concatenated plain text of its surviving
                // spans (or null when that's empty) rather than a values-dictionary lookup — this
                // is what makes it suppress as one unit through the shared `Value is null`
                // predicate every other caller of Resolve already uses.
                var compoundValue = compound is { Plain.Length: > 0 } ? compound.Plain : null;
                resolved.Add(new ResolvedItem(item, compoundValue, compoundValue is null ? null : compound));
                continue;
            }

            var key = item.Id ?? item.Item;
            var value = key is { } id ? values.GetValueOrDefault(id) : null;
            var display = ResolveDisplay(item, key, value, ctx);
            resolved.Add(new ResolvedItem(item, value, display));
        }

        return resolved;
    }

    private static Segment? ResolveDisplay(PaneItem item, string? key, string? value, ItemContext ctx)
    {
        if (item.Format is not null)
        {
            return value is null ? null : SegmentBuilder.BuildItemSegment(ApplyFormat(item.Format, value), null);
        }

        var registryDisplay = key is { } id ? ItemRegistry.Find(id)?.BuildDefaultSegment(ctx) : null;
        return registryDisplay ?? (value is null ? null : SegmentBuilder.BuildItemSegment(value, null));
    }

    // SPEC-V2-FRAMEWORK.md §3.3: assembles a compound item's parts into one Segment, in order —
    // resolve each part's raw text (step 1), drop a literal adjacent to an empty value part
    // evaluated against original positions (step 2), assemble one StyledSpan per surviving part
    // (step 3), then concatenate with no separator (step 4).
    private static Segment? BuildCompound(
        PaneItem item,
        IReadOnlyList<PaneItemPart> parts,
        IReadOnlyDictionary<string, string?> values,
        ItemContext ctx,
        IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens)
    {
        var resolvedTokens = tokens ?? EmptyTokens;
        var texts = new string?[parts.Count];
        var registrySegments = new Segment?[parts.Count];
        // A value part (`item`/`from`) is "empty" per §3.3 when it resolves to null or "" — a
        // literal part (`text`) is never empty in this sense, even when its own text is "".
        var isEmptyValuePart = new bool[parts.Count];

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            string? text;
            if (part.Text is { } literal)
            {
                text = literal;
            }
            else if (part.From is { Length: > 0 } from)
            {
                var raw = values.GetValueOrDefault(from);
                if (raw is { } toExtract && part.Extract is { Length: > 0 } pattern)
                {
                    raw = ItemValueResolver.ExtractValue(toExtract, pattern);
                }

                text = raw is null ? null : ItemValueResolver.ApplyCase(raw, part.Case);
                isEmptyValuePart[i] = string.IsNullOrEmpty(text);
            }
            else if (part.Item is { Length: > 0 } partItemId)
            {
                if (ItemRegistry.Find(partItemId) is { } def)
                {
                    registrySegments[i] = def.BuildDefaultSegment(ctx);
                    text = registrySegments[i]?.Plain;
                }
                else
                {
                    text = values.GetValueOrDefault(partItemId);
                }

                isEmptyValuePart[i] = string.IsNullOrEmpty(text);
            }
            else
            {
                text = null;
                isEmptyValuePart[i] = true;
            }

            if (text is { Length: > 0 } && part.Format is not null)
            {
                text = ApplyFormat(part.Format, text);
            }

            texts[i] = text;
        }

        var surviving = new bool[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Text is null)
            {
                surviving[i] = !isEmptyValuePart[i];
                continue;
            }

            var prevEmpty = i > 0 && isEmptyValuePart[i - 1];
            var nextEmpty = i < parts.Count - 1 && isEmptyValuePart[i + 1];
            surviving[i] = !prevEmpty && !nextEmpty;
        }

        var spans = new List<StyledSpan>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (!surviving[i] || texts[i] is not { Length: > 0 } text)
            {
                continue;
            }

            var part = parts[i];
            var color = ColorResolution.Resolve(part.Color ?? item.Color, values, resolvedTokens);
            string markup;
            if (color is not null)
            {
                markup = SegmentBuilder.BuildSpanMarkup(text, color);
            }
            else if (registrySegments[i] is { } registrySegment)
            {
                markup = registrySegment.Markup;
            }
            else
            {
                markup = SegmentBuilder.BuildSpanMarkup(text, null);
            }

            spans.Add(new StyledSpan(text, markup));
        }

        return spans.Count == 0 ? null : SegmentBuilder.BuildCompoundSegment(spans);
    }

    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> EmptyTokens =
        new Dictionary<string, ColorResolution.ColorRule>();

    public static string ApplyFormat(string? format, string value) =>
        (string.IsNullOrEmpty(format) ? "{}" : format).Replace("{}", value);
}
