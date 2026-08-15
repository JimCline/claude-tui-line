using Spectre.Console;
using Spectre.Console.Rendering;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.2/§2.5: draws one pane's own border as markup rows around its
/// already-sized content, independent of any sibling's border. Hand-drawn glyph by glyph
/// (<see cref="BoxBorder.GetPart"/>) rather than via Spectre's <see cref="Panel"/> widget, so the
/// pipeline stays in pure markup form until the single final render call (§2.4 rule 5).
/// </summary>
public static class PaneBorderRenderer
{
    /// <param name="suppressed">
    /// SPEC-V2-FRAMEWORK.md §2.3: a pane whose resolved width falls under
    /// <see cref="RowLayout.MinUsableWidth"/> suppresses its own border first rather than being
    /// dropped outright. SPEC-2.3-suppression-predicate.md §6.3: suppression reclaims the
    /// reserve for content — reserve, padding, and corner glyphs are all zeroed, and the caller
    /// (<see cref="PaneTreeRenderer"/>) has already widened <paramref name="innerWidth"/> to the
    /// pane's full outer width, so <paramref name="contentRows"/> arrive pre-sized to it.
    /// </param>
    /// <param name="omitEdges">
    /// SPEC-V2-FRAMEWORK.md §2.8.2: the height-axis twin of <paramref name="suppressed"/>. A
    /// bordered pane whose row budget falls under 3 cannot close its box, so it drops the top and
    /// bottom edge rows entirely (reclaiming both for content) rather than drawing blank chrome in
    /// their place — unlike <paramref name="suppressed"/>, this changes row count, not just glyphs.
    /// Left/right verticals still wrap each content row, independently suppressed or not.
    /// </param>
    public static IReadOnlyList<PaneRow> Wrap(IReadOnlyList<PaneRow> contentRows, int innerWidth, PaneBorder border, string colorMarkup, bool suppressed = false, bool omitEdges = false)
    {
        if (border.Style is null)
        {
            return contentRows;
        }

        var width = Math.Max(0, innerWidth);
        var style = border.Style;
        var edges = border.Edges;
        var reserve = suppressed ? 0 : SizeResolver.OwnBorderReserve(border);
        var outerWidth = width + reserve;
        var horizontalSpan = suppressed ? width : width + 2;

        string Part(BoxBorderPart part) => suppressed ? " " : style.GetPart(part);

        string Colored(string glyphs) =>
            suppressed ? Markup.Escape(glyphs) : $"[{colorMarkup}]{Markup.Escape(glyphs)}[/]";

        // §2.10: an edge that is off is not drawn — the top/bottom rows are omitted entirely when
        // Top/Bottom is off (matching OwnRowReserve), and a corner is omitted whenever the vertical
        // edge it would meet is off, while the horizontal run itself still spans the full
        // padding+content width regardless, so outerWidth comes out exactly right in every
        // edge-on/off combination without needing a junction table. §2.8.2's omitEdges (row budget
        // under 3) forces top/bottom off the same way regardless of the edge config.
        //
        // SPEC-2.3-suppression-predicate.md §6.3: when suppressed, corners and left/right glyphs
        // are omitted (not drawn as blank) so horizontalSpan/outerWidth above, which already
        // exclude the reserve, aren't overshot by them.
        var leftGlyph = edges.Left && !suppressed ? Colored(Part(BoxBorderPart.Left)) : "";
        var rightGlyph = edges.Right && !suppressed ? Colored(Part(BoxBorderPart.Right)) : "";

        var rows = new List<PaneRow>(contentRows.Count + 2);

        if (edges.Top && !omitEdges)
        {
            var topLeft = edges.Left && !suppressed ? Part(BoxBorderPart.TopLeft) : "";
            var topRight = edges.Right && !suppressed ? Part(BoxBorderPart.TopRight) : "";
            rows.Add(new PaneRow(Colored(topLeft + Repeat(Part(BoxBorderPart.Top), horizontalSpan) + topRight), outerWidth));
        }

        rows.AddRange(contentRows.Select(row => suppressed
            ? row
            : new PaneRow(leftGlyph + " " + row.Markup + " " + rightGlyph, row.Width + reserve)));

        if (edges.Bottom && !omitEdges)
        {
            var bottomLeft = edges.Left && !suppressed ? Part(BoxBorderPart.BottomLeft) : "";
            var bottomRight = edges.Right && !suppressed ? Part(BoxBorderPart.BottomRight) : "";
            rows.Add(new PaneRow(Colored(bottomLeft + Repeat(Part(BoxBorderPart.Bottom), horizontalSpan) + bottomRight), outerWidth));
        }

        return rows;
    }

    private static string Repeat(string glyph, int count) => string.Concat(Enumerable.Repeat(glyph, count));
}
