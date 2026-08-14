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
        ItemContext ctx)
    {
        var resolved = new List<ResolvedItem>(items.Count);
        foreach (var item in items)
        {
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

    public static string ApplyFormat(string? format, string value) =>
        (string.IsNullOrEmpty(format) ? "{}" : format).Replace("{}", value);
}
