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
    /// dropped outright. Suppression keeps the same reserved geometry (so the pane's already-
    /// resolved outer width stays correct) but draws blank chrome instead of glyphs — one code
    /// path for both cases, not a separate borderless layout.
    /// </param>
    public static IReadOnlyList<PaneRow> Wrap(IReadOnlyList<PaneRow> contentRows, int innerWidth, PaneBorder border, string colorMarkup, bool suppressed = false)
    {
        if (border.Style is null)
        {
            return contentRows;
        }

        var width = Math.Max(0, innerWidth);
        var style = border.Style;
        var edges = border.Edges;
        var outerWidth = width + SizeResolver.OwnBorderReserve(border);
        var horizontalSpan = width + 2;

        string Part(BoxBorderPart part) => suppressed ? " " : style.GetPart(part);

        string Colored(string glyphs) =>
            suppressed ? Markup.Escape(glyphs) : $"[{colorMarkup}]{Markup.Escape(glyphs)}[/]";

        // §2.10: an edge that is off is not drawn — the top/bottom rows are omitted entirely when
        // Top/Bottom is off (matching OwnRowReserve), and a corner is omitted whenever the vertical
        // edge it would meet is off, while the horizontal run itself still spans the full
        // padding+content width regardless, so outerWidth comes out exactly right in every
        // edge-on/off combination without needing a junction table.
        var leftGlyph = edges.Left ? Colored(Part(BoxBorderPart.Left)) : "";
        var rightGlyph = edges.Right ? Colored(Part(BoxBorderPart.Right)) : "";

        var rows = new List<PaneRow>(contentRows.Count + 2);

        if (edges.Top)
        {
            var topLeft = edges.Left ? Part(BoxBorderPart.TopLeft) : "";
            var topRight = edges.Right ? Part(BoxBorderPart.TopRight) : "";
            rows.Add(new PaneRow(Colored(topLeft + Repeat(Part(BoxBorderPart.Top), horizontalSpan) + topRight), outerWidth));
        }

        rows.AddRange(contentRows.Select(row => new PaneRow(leftGlyph + " " + row.Markup + " " + rightGlyph, row.Width + SizeResolver.OwnBorderReserve(border))));

        if (edges.Bottom)
        {
            var bottomLeft = edges.Left ? Part(BoxBorderPart.BottomLeft) : "";
            var bottomRight = edges.Right ? Part(BoxBorderPart.BottomRight) : "";
            rows.Add(new PaneRow(Colored(bottomLeft + Repeat(Part(BoxBorderPart.Bottom), horizontalSpan) + bottomRight), outerWidth));
        }

        return rows;
    }

    private static string Repeat(string glyph, int count) => string.Concat(Enumerable.Repeat(glyph, count));
}
