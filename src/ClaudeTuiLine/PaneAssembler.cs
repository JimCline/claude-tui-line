namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.1: renders one leaf pane's content — the default 14-segment list when
/// <c>items</c> is empty, or the resolved <c>items</c> sequence otherwise — to rows exactly
/// <paramref name="innerWidth"/> wide. A pane's content is a vertical sequence of packed
/// single-row groups, in config order. Every pane rendered through this path is inside a split, so the
/// §2.6 single-line fallback never applies here (<c>allowFallback: false</c>) — narrowness is not
/// special, a narrow pane just wraps or truncates like any other.
/// </summary>
public static class PaneAssembler
{
    public static IReadOnlyList<PaneRow> RenderLeafRows(
        Pane pane,
        int innerWidth,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        var rawRows = pane.Items.Count == 0
            ? RenderDefaultRows(pane, innerWidth, ctx)
            : RenderItemRows(pane, innerWidth, ctx, values, tokens);

        return rawRows.Select(row => AlignRow(row, innerWidth, pane.Align)).ToList();
    }

    private static IReadOnlyList<PaneRow> RenderDefaultRows(Pane pane, int innerWidth, ItemContext ctx)
    {
        var segments = SegmentBuilder.Build(ctx);
        var overflow = ResolveOverflow(pane);
        var buffer = PaneRenderer.RenderLeaf(segments, innerWidth, overflow, pane.Ellipsis, allowFallback: false);
        return buffer.Rows;
    }

    private static IReadOnlyList<PaneRow> RenderItemRows(
        Pane pane,
        int innerWidth,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        var overflow = ResolveOverflow(pane);
        var rows = new List<PaneRow>();
        var packedGroup = new List<Segment>();

        void FlushGroup()
        {
            if (packedGroup.Count == 0)
            {
                return;
            }

            var buffer = PaneRenderer.RenderLeaf(packedGroup, innerWidth, overflow, pane.Ellipsis, allowFallback: false);
            rows.AddRange(buffer.Rows);
            packedGroup.Clear();
        }

        foreach (var resolved in LeafItems.Resolve(pane.Items, values, ctx))
        {
            if (resolved.Value is null)
            {
                continue;
            }

            var decision = LeafContent.Decide(resolved, values);
            var color = ColorResolution.Resolve(resolved.Config.Color, values, tokens);
            packedGroup.Add(SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, color));
        }

        FlushGroup();
        return rows;
    }

    // §2.6: "overflow" is only legal for a surface's single root pane; inside any split it would
    // corrupt the neighbor to its right, so a pane rendered through this path (always inside a
    // split) coerces an explicit "overflow" down to "truncate" rather than honoring it.
    private static OverflowMode ResolveOverflow(Pane pane) =>
        pane.Overflow is OverflowMode mode && mode != OverflowMode.Overflow ? mode : OverflowMode.Truncate;

    private static PaneRow AlignRow(PaneRow row, int targetWidth, PaneAlign align)
    {
        var deficit = Math.Max(0, targetWidth - row.Width);

        var newMarkup = align switch
        {
            PaneAlign.Center => new string(' ', deficit / 2) + row.Markup + new string(' ', deficit - deficit / 2),
            PaneAlign.Right => new string(' ', deficit) + row.Markup,
            _ => row.Markup + new string(' ', deficit),
        };

        return new PaneRow(newMarkup, row.Width + deficit);
    }
}
