namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.2/§2.4: renders an already-sized <see cref="SizeResolver.ResolvedPane"/>
/// tree — leaf or split — to a single composited, border-wrapped contribution. A split recurses
/// into its children (each already given its outer width by <see cref="SizeResolver"/>) and
/// composes them; a leaf renders its own content. Both then take the pane's own border,
/// uniformly, so a bordered split draws a border around its composed children exactly as a
/// bordered leaf draws one around its content — this is what makes the §2.9 split root's
/// (unconfigured, therefore default-enabled per <see cref="ConfigLoader"/>) border visible around
/// the whole two-pane surface, not just around each child.
/// </summary>
public static class PaneTreeRenderer
{
    public static Compositor.PaneContribution Render(
        SizeResolver.ResolvedPane node,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        RenderNoteCollector notes,
        int? targetOuterHeight = null,
        IDictionary<Pane, int>? rowCounts = null,
        bool collapse = false,
        bool excludeLeft = false,
        bool excludeRight = false,
        int rowStart = 0,
        int colStart = 0,
        BorderGrid.Grid? grid = null)
    {
        var pane = node.Source;

        // §2.10.2: under collapse:true, a side charged to a shared boundary (excludeLeft/
        // excludeRight) is never drawn as part of THIS pane's own box — it contributes zero width
        // here and its glyph comes entirely from the boundary-column contribution the parent
        // vertical split inserts instead (below). Every other edge renders exactly as it always
        // has, since an uncontested edge's grid-resolved glyph is identical to its own.
        var edges = pane.Border.Edges;
        var effectiveBorder = collapse
            ? pane.Border with { Edges = new PaneBorderEdges(edges.Top, edges.Right && !excludeRight, edges.Bottom, edges.Left && !excludeLeft) }
            : pane.Border;
        var borderReserve = SizeResolver.OwnBorderReserve(effectiveBorder);
        var innerWidth = Math.Max(0, node.OuterWidth - borderReserve);
        var suppressed = SizeResolver.ShouldSuppressBorder(pane, node.OuterWidth);

        // §2.8.1/§2.8.2: node.ClipRows, when set, is the degrade ladder's authoritative row
        // budget for this (always leaf) pane — annotated onto this render attempt's ResolvedPane
        // tree, never onto the shared Pane (§2.5.1 purity). Height suppression is decided directly
        // from that budget here, rather than from the row count the cap produces, to avoid a
        // circular dependency between "how much content to keep" and "will the border be
        // suppressed". A pane whose own declared maxRows is under 3 is an author choice (keeps its
        // border, loses content) and is never suppressed by this mechanism.
        var bordered = pane.Border.Style is not null;
        var ownDeclaredTiny = pane.MaxRows is int declaredMax && declaredMax < 3;
        bool heightSuppressed;
        int? maxContentRows;
        if (node.ClipRows is int budget)
        {
            heightSuppressed = bordered && budget < 3 && !ownDeclaredTiny;
            maxContentRows = (heightSuppressed || !bordered) ? Math.Max(0, budget) : Math.Max(0, budget - 2);
        }
        else
        {
            maxContentRows = null;
            heightSuppressed = false; // decided below, from the natural post-render row count.
        }

        IReadOnlyList<PaneRow> contentRows;
        if (node.Children.Count == 0)
        {
            contentRows = PaneAssembler.RenderLeafRows(pane, innerWidth, ctx, values, tokens, notes, maxContentRows, node.ItemsEmptied);
        }
        else if (pane.Split == PaneSplit.Vertical)
        {
            // §2.2: a vertical split's children divide its width but share its height — every
            // child spans the split's full height, so a shorter child is re-rendered with the
            // tallest sibling's height as its own target before this loop composes them side by
            // side, rather than being padded around afterward (which would pad outside its border
            // instead of growing the border to match).
            //
            // §2.10.2: under collapse:true every interior boundary charges exactly one column
            // (never two), and that column's glyph is a synthetic contribution owned by the split,
            // not by either neighbour — computed via BorderGrid.Build ahead of this render and
            // spliced in here as a 1-column strip in place of the ordinary blank gutter.
            var innerRow0 = rowStart + (bordered && edges.Top ? 1 : 0);
            var boundaryStep = collapse ? 1 : pane.Gutter;
            var childColStarts = new int[node.Children.Count];
            var runningCol = colStart + (bordered && edges.Left && !excludeLeft ? 1 : 0);
            for (var i = 0; i < node.Children.Count; i++)
            {
                childColStarts[i] = runningCol;
                runningCol += node.Children[i].OuterWidth + (i < node.Children.Count - 1 ? boundaryStep : 0);
            }

            var natural = node.Children.Select((c, i) =>
            {
                var childExcludeLeft = collapse && i > 0;
                var childExcludeRight = collapse && i < node.Children.Count - 1;
                return Render(c, ctx, values, tokens, notes, rowCounts: rowCounts, collapse: collapse,
                    excludeLeft: childExcludeLeft, excludeRight: childExcludeRight,
                    rowStart: innerRow0, colStart: childColStarts[i], grid: grid);
            }).ToList();
            var childHeight = natural.Count == 0 ? 0 : natural.Max(c => c.Buffer.Rows.Count);

            var contributions = new List<Compositor.PaneContribution>();
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (i > 0)
                {
                    if (collapse)
                    {
                        contributions.Add(BoundaryColumn(grid, innerRow0, childHeight, childColStarts[i] - 1));
                    }
                    else if (pane.Gutter > 0)
                    {
                        contributions.Add(new Compositor.PaneContribution(new PaneBuffer(Array.Empty<PaneRow>()), pane.Gutter, HasBackground: false));
                    }
                }

                // §2.8.3: a "content"-height child keeps its own natural border box instead of
                // being re-rendered to the band height — Compositor.ComposeRoot's own Valign-based
                // padding (below) then places that shorter box within the band, unbordered.
                var childIsContentHeight = node.Children[i].Source.Height == PaneHeight.Content;
                var childExcludeLeft = collapse && i > 0;
                var childExcludeRight = collapse && i < node.Children.Count - 1;
                var contribution = natural[i].Buffer.Rows.Count < childHeight && !childIsContentHeight
                    ? Render(node.Children[i], ctx, values, tokens, notes, childHeight, rowCounts, collapse,
                        childExcludeLeft, childExcludeRight, innerRow0, childColStarts[i], grid)
                    : natural[i];
                contributions.Add(contribution);
            }

            contentRows = PadToWidth(Compositor.ComposeRoot(contributions), innerWidth);
        }
        else
        {
            var innerCol0 = colStart + (bordered && edges.Left && !excludeLeft ? 1 : 0);
            var cursorRow = rowStart + (bordered && edges.Top ? 1 : 0);
            var rows = new List<PaneRow>();
            foreach (var child in node.Children)
            {
                var contribution = Render(child, ctx, values, tokens, notes, rowCounts: rowCounts, collapse: collapse,
                    rowStart: cursorRow, colStart: innerCol0, grid: grid);
                rows.AddRange(contribution.Buffer.Rows);
                cursorRow += contribution.Buffer.Rows.Count;
            }

            contentRows = PadToWidth(rows, innerWidth);
        }

        if (targetOuterHeight is int targetHeight)
        {
            var targetInnerHeight = Math.Max(0, targetHeight - SizeResolver.OwnRowReserve(pane));
            contentRows = PadHeight(contentRows, targetInnerHeight, innerWidth, pane.Valign);
        }

        if (node.ClipRows is null)
        {
            var naturalTotal = contentRows.Count + (bordered ? 2 : 0);
            heightSuppressed = bordered && naturalTotal < 3 && !ownDeclaredTiny;
        }

        var borderColorMarkup = ColorResolution.Resolve(pane.Border.Color, values, tokens) ?? "grey";
        var borderedRows = PaneBorderRenderer.Wrap(contentRows, innerWidth, effectiveBorder, borderColorMarkup, suppressed, heightSuppressed);
        if (rowCounts is not null)
        {
            rowCounts[pane] = borderedRows.Count;
        }

        return new Compositor.PaneContribution(new PaneBuffer(borderedRows), node.OuterWidth, HasBackground: false, pane.Valign);
    }

    // §2.10.2: the 1-column strip a vertical split inserts in place of a shared boundary's blank
    // gutter — every row's glyph/colour comes from the grid built once ahead of this render, never
    // from either neighbour's own Wrap call, since a shared column has no single owner.
    private static Compositor.PaneContribution BoundaryColumn(BorderGrid.Grid? grid, int rowStart, int height, int col)
    {
        var rows = new List<PaneRow>(height);
        for (var r = 0; r < height; r++)
        {
            if (grid is not null && grid.TryGet(rowStart + r, col, out var cell))
            {
                rows.Add(new PaneRow($"[{cell.ColorMarkup}]{BorderGrid.Glyph(cell.Style, cell.Mask)}[/]", 1));
            }
            else
            {
                rows.Add(new PaneRow(" ", 1));
            }
        }

        return new Compositor.PaneContribution(new PaneBuffer(rows), 1, HasBackground: false);
    }

    // Pads this pane's OWN inner content (before its border is drawn around it) up to
    // targetHeight, the same before/after valign split Compositor.PadRows uses for a sibling
    // contribution — applied one layer down, to a pane's own rows, so the border in
    // PaneBorderRenderer.Wrap spans the full height instead of just the natural content.
    private static IReadOnlyList<PaneRow> PadHeight(IReadOnlyList<PaneRow> rows, int targetHeight, int width, PaneValign valign)
    {
        var deficit = Math.Max(0, targetHeight - rows.Count);
        var (before, after) = valign switch
        {
            PaneValign.Middle => (deficit / 2, deficit - deficit / 2),
            PaneValign.Bottom => (deficit, 0),
            _ => (0, deficit),
        };

        var blankRow = new PaneRow(new string(' ', width), width);
        var padded = new List<PaneRow>(rows.Count + deficit);
        padded.AddRange(Enumerable.Repeat(blankRow, before));
        padded.AddRange(rows);
        padded.AddRange(Enumerable.Repeat(blankRow, after));
        return padded;
    }

    // Rows returned by a nested Compositor.ComposeRoot call may be shorter than this split's own
    // inner width — its rule-4 trim is a no-op only when the rightmost sibling's last cell is
    // non-blank (SPEC.md rule 4 exception), which is not guaranteed for every child. Re-padding
    // here (harmless when already exact) is what keeps this split's own border cells aligned
    // regardless of what a nested composition trimmed away.
    private static IReadOnlyList<PaneRow> PadToWidth(IReadOnlyList<PaneRow> rows, int width) =>
        rows.Select(r => r.Width >= width ? r : new PaneRow(r.Markup + new string(' ', width - r.Width), width)).ToList();
}
