namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: cutting a <see cref="Segment"/> to fit a width, on either axis.
/// Moved out of <see cref="PaneRenderer"/> (SPEC-2.6-vertical-marker-splice.md §9.4) so
/// <see cref="RowLayout"/> can call it too without inverting the PaneRenderer → RowLayout
/// dependency direction.
/// </summary>
internal static class SegmentTruncation
{
    /// SPEC-V2-FRAMEWORK.md §2.6, the two riders, in one place for both axes: an empty `ellipsis`
    /// is a hard clip that spends no cell, and a marker not strictly narrower than the space it
    /// would sit in is dropped rather than allowed to consume it.
    internal static bool MarkerFits(int innerWidth, string ellipsis) =>
        ellipsis.Length > 0 && ellipsis.Length < innerWidth;

    // §2.6 truncate: cut to fit, ending with the marker; the marker's own width is budgeted
    // against innerWidth, and is dropped entirely (hard clip, no sacrificed cell) when
    // innerWidth is not greater than the marker's width.
    internal static Segment Truncate(Segment segment, int innerWidth, string ellipsis)
    {
        if (innerWidth <= 0)
        {
            return Restyle(segment, string.Empty);
        }

        if (!MarkerFits(innerWidth, ellipsis))
        {
            return Restyle(segment, segment.Plain[..SafeCutIndex(segment.Plain, Math.Min(innerWidth, segment.Plain.Length))]);
        }

        var contentBudget = innerWidth - ellipsis.Length;
        var clipped = segment.Plain[..SafeCutIndex(segment.Plain, Math.Min(contentBudget, segment.Plain.Length))];
        var newPlain = clipped + ellipsis;

        if (!OscHyperlink.TryUnwrap(segment.Markup, out var url, out var innerMarkup))
        {
            return RestyleSimple(segment, newPlain);
        }

        // §3.2 rule 3 / ruling d: a truncated link closes itself before the ellipsis — the
        // ellipsis keeps whatever colour applies but is never clickable ("clicking '…' must
        // never navigate").
        var innerSegment = new Segment(innerMarkup, segment.Plain);
        var restyledContent = RestyleSimple(innerSegment, clipped);
        var restyledEllipsis = RestyleSimple(innerSegment, ellipsis);
        return new Segment(OscHyperlink.Wrap(url, restyledContent.Markup) + restyledEllipsis.Markup, newPlain);
    }

    // §2.6 wrap, trap 1 (break on plain text only) and trap 2 (style re-emitted on every
    // continuation row): chunks the oversized segment's Plain text into innerWidth-wide pieces
    // and restyles each piece independently, so every resulting Segment carries its own style.
    internal static List<Segment> WrapToWidth(Segment segment, int innerWidth)
    {
        var chunks = new List<Segment>();
        if (innerWidth <= 0)
        {
            chunks.Add(Restyle(segment, string.Empty));
            return chunks;
        }

        var i = 0;
        while (i < segment.Plain.Length)
        {
            var end = SafeCutIndex(segment.Plain, Math.Min(i + innerWidth, segment.Plain.Length));
            chunks.Add(Restyle(segment, segment.Plain[i..end]));
            i = end;
        }

        return chunks;
    }

    // §13.2 (defect 16): Plain is UTF-16, so a non-BMP character (most emoji) is stored as a
    // surrogate pair — two code units. A cut index landing between them would split the pair into
    // two lone surrogates, which is invalid UTF-16 on the wire, not just a clipped glyph. Advancing
    // past the pair keeps both units on the retained side; every truncate/wrap cut point in this
    // class routes through here rather than slicing Plain directly.
    internal static int SafeCutIndex(string plain, int index) =>
        index > 0 && index < plain.Length && char.IsLowSurrogate(plain[index]) && char.IsHighSurrogate(plain[index - 1])
            ? index + 1
            : index;

    // §3.2 rule 3: an OSC 8 link (see OscHyperlink) wraps a segment's *style* markup, not its
    // Plain text, so restyling a linked segment must unwrap the link, restyle the inner styled
    // markup with RestyleSimple, and re-wrap around the same URL — rather than trying to
    // pattern-match the link's raw escape bytes as if they were an ordinary style prefix/suffix.
    // Every call site (truncation, wrap-chunking) goes through this rather than RestyleSimple
    // directly, so a link is preserved (and, for a wrapped segment, reopened per continuation
    // row) without each caller needing its own link-awareness.
    internal static Segment Restyle(Segment original, string newPlain)
    {
        if (!OscHyperlink.TryUnwrap(original.Markup, out var url, out var innerMarkup))
        {
            return RestyleSimple(original, newPlain);
        }

        var innerSegment = new Segment(innerMarkup, original.Plain);
        var restyledInner = RestyleSimple(innerSegment, newPlain);
        return new Segment(OscHyperlink.Wrap(url, restyledInner.Markup), newPlain);
    }

    // Re-styles a modified Plain string. Segment.Markup is not always a simple single-tag wrap
    // around Plain (SegmentBuilder.cs builds composite/concatenated markup for some segments) —
    // so this only rewrites the simple case, verified by exact substring/prefix/suffix matching,
    // and gracefully degrades to unstyled markup otherwise rather than guessing wrong.
    internal static Segment RestyleSimple(Segment original, string newPlain)
    {
        var newMarkup = TryGetSimpleWrap(original, out var prefix, out var suffix)
            ? prefix + Spectre.Console.Markup.Escape(newPlain) + suffix
            : Spectre.Console.Markup.Escape(newPlain);

        return new Segment(newMarkup, newPlain);
    }

    internal static bool TryGetSimpleWrap(Segment segment, out string prefix, out string suffix)
    {
        prefix = string.Empty;
        suffix = string.Empty;

        var escapedPlain = Spectre.Console.Markup.Escape(segment.Plain);
        var index = segment.Markup.IndexOf(escapedPlain, StringComparison.Ordinal);
        if (index < 0 || index != segment.Markup.LastIndexOf(escapedPlain, StringComparison.Ordinal))
        {
            return false; // absent, or ambiguous (more than one occurrence) — do not guess.
        }

        var candidatePrefix = segment.Markup[..index];
        var candidateSuffix = segment.Markup[(index + escapedPlain.Length)..];

        if (candidatePrefix.Length == 0 && candidateSuffix.Length == 0)
        {
            return true; // fully unstyled: Markup is exactly the escaped Plain, no tags at all.
        }

        if (!candidatePrefix.EndsWith(']') || candidateSuffix != "[/]")
        {
            return false;
        }

        prefix = candidatePrefix;
        suffix = candidateSuffix;
        return true;
    }
}
