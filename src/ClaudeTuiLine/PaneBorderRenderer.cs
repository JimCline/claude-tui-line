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
    // 2 verticals + 2 padding cells (§2.5's borderReserve), the same figure the pre-split
    // single-pane pipeline has always used.
    public const int BorderReserve = 4;

    /// <param name="suppressed">
    /// SPEC-V2-FRAMEWORK.md §2.3: a pane whose resolved width falls under
    /// <see cref="RowLayout.MinUsableWidth"/> suppresses its own border first rather than being
    /// dropped outright. Suppression keeps the same reserved geometry (so the pane's already-
    /// resolved outer width stays correct) but draws blank chrome instead of glyphs — one code
    /// path for both cases, not a separate borderless layout.
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
        var outerWidth = width + BorderReserve;

        string Part(BoxBorderPart part) => suppressed ? " " : style.GetPart(part);

        string Colored(string glyphs) =>
            suppressed ? Markup.Escape(glyphs) : $"[{colorMarkup}]{Markup.Escape(glyphs)}[/]";

        var left = Colored(Part(BoxBorderPart.Left));
        var right = Colored(Part(BoxBorderPart.Right));

        if (omitEdges)
        {
            return contentRows.Select(row => new PaneRow(left + " " + row.Markup + " " + right, row.Width + SizeResolver.OwnBorderReserve(border))).ToList();
        }

        var top = Colored(Part(BoxBorderPart.TopLeft) + Repeat(Part(BoxBorderPart.Top), width + 2) + Part(BoxBorderPart.TopRight));
        var bottom = Colored(Part(BoxBorderPart.BottomLeft) + Repeat(Part(BoxBorderPart.Bottom), width + 2) + Part(BoxBorderPart.BottomRight));

        var rows = new List<PaneRow>(contentRows.Count + 2) { new(top, outerWidth) };
        rows.AddRange(contentRows.Select(row => new PaneRow(left + " " + row.Markup + " " + right, row.Width + BorderReserve)));
        rows.Add(new PaneRow(bottom, outerWidth));
        return rows;
    }

    private static string Repeat(string glyph, int count) => string.Concat(Enumerable.Repeat(glyph, count));
}
