using System.Text;

namespace ClaudeTuiLine;

/// <summary>
/// Greedy row-wrapping against the terminal width, per CAPTURE.md's "Wrapping" section.
/// </summary>
public static class RowLayout
{
    private const string SeparatorMarkup = " [dim]|[/] ";

    // Public: SPEC-V2-FRAMEWORK.md §2.3's content-pane intrinsic-width measurement (the width a
    // pane's items would need unwrapped on one row) must reproduce this exact packing arithmetic
    // rather than approximate it — one implementation of "how wide would this pack unwrapped."
    public const int SeparatorWidth = 3;

    // Canonical fallback threshold per SPEC.md §6/§6b (bash-exact, `-lt 20`): below 20 columns
    // of usable width, emit a single unwrapped line.
    public const int MinUsableWidth = 20;

    /// <param name="availableWidth">
    /// The width this call has to pack into, already reduced by whatever the caller's surface
    /// and box arithmetic decided (SPEC.md §6 "MEASURED") — <see cref="RowLayout"/> has no
    /// knowledge of COLUMNS, chromeReserve, or border reserve, and is indifferent to where this
    /// number came from. Null means no usable width is known at all.
    /// </param>
    /// <param name="allowFallback">
    /// SPEC-V2-FRAMEWORK.md §2.6: the single-unwrapped-row fallback is a property of the
    /// surface, not of a pane — it only applies when the surface has exactly one pane. Callers
    /// rendering a pane inside a split pass <c>false</c> so a narrow pane packs and wraps or
    /// truncates like any other pane instead of emitting one overwide row.
    /// </param>
    public static IReadOnlyList<PaneRow> Wrap(IReadOnlyList<Segment> segments, int? availableWidth, bool allowFallback = true)
    {
        var rows = new List<PaneRow>();
        if (segments.Count == 0)
        {
            return rows;
        }

        if (availableWidth is null || (allowFallback && availableWidth < MinUsableWidth))
        {
            var fallbackMarkup = string.Join(SeparatorMarkup, segments.Select(s => s.Markup));
            var fallbackWidth = segments.Sum(s => s.Plain.Length) + SeparatorWidth * (segments.Count - 1);
            rows.Add(new PaneRow(fallbackMarkup, fallbackWidth));
            return rows;
        }

        var rowBuilder = new StringBuilder();
        var rowWidth = 0;
        var rowStarted = false;

        foreach (var seg in segments)
        {
            var segWidth = seg.Plain.Length;

            if (!rowStarted)
            {
                rowBuilder.Append(seg.Markup);
                rowWidth = segWidth;
                rowStarted = true;
            }
            else if (rowWidth + SeparatorWidth + segWidth <= availableWidth.Value)
            {
                rowBuilder.Append(SeparatorMarkup).Append(seg.Markup);
                rowWidth += SeparatorWidth + segWidth;
            }
            else
            {
                rows.Add(new PaneRow(rowBuilder.ToString(), rowWidth));
                rowBuilder.Clear();
                rowBuilder.Append(seg.Markup);
                rowWidth = segWidth;
            }
        }

        if (rowStarted)
        {
            rows.Add(new PaneRow(rowBuilder.ToString(), rowWidth));
        }

        return rows;
    }

    /// <summary>
    /// True when <see cref="Wrap"/> would take its single-line fallback for the given
    /// <paramref name="availableWidth"/> — null, or below <see cref="MinUsableWidth"/>. Callers
    /// that render a bordered container around the wrapped rows (SPEC.md §6b's narrow-width
    /// suppression) consult this instead of duplicating the threshold, and must pass the same
    /// <paramref name="availableWidth"/> they gave <see cref="Wrap"/>.
    /// </summary>
    public static bool IsFallbackWidth(int? availableWidth) => availableWidth is null || availableWidth < MinUsableWidth;
}
