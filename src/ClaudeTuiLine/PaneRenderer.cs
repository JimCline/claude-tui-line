namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: renders a leaf pane's items to a <see cref="PaneBuffer"/>.
/// Overflow modes govern only a single segment wider than the pane's own inner width — general
/// multi-segment row packing is untouched and stays on the unmodified <see cref="RowLayout.Wrap"/>
/// in every mode. <c>overflow</c> mode is therefore a literal passthrough to
/// <see cref="RowLayout.Wrap"/>, which is what makes it parity-preserving by construction
/// (§2.7's golden gate exercises exactly this path).
/// </summary>
public static class PaneRenderer
{
    public static PaneBuffer RenderLeaf(IReadOnlyList<Segment> items, int? innerWidth, OverflowMode overflow, string ellipsis, RenderNoteCollector notes, bool allowFallback = true)
    {
        if (innerWidth is not int width)
        {
            // No COLUMNS: RowLayout.Wrap's own null-width contract (single unwrapped row)
            // applies identically regardless of overflow mode — there is no pane width to
            // measure "wider than the pane" against.
            return new PaneBuffer(RowLayout.Wrap(items, null, allowFallback));
        }

        var prepared = overflow switch
        {
            OverflowMode.Truncate => items
                .Select(s =>
                {
                    if (s.Plain.Length <= width)
                    {
                        return s;
                    }

                    notes.Add($"segment truncated to fit {width} columns");
                    return TruncateSegment(s, width, ellipsis);
                })
                .ToList(),
            OverflowMode.Wrap => items
                .SelectMany(s => s.Plain.Length > width ? WrapSegment(s, width) : new List<Segment> { s })
                .ToList(),
            _ => items, // Overflow: v1-identical, oversized segments pass through untouched.
        };

        return new PaneBuffer(RowLayout.Wrap(prepared, width, allowFallback));
    }

    // §2.6 truncate: cut to fit, ending with the marker; the marker's own width is budgeted
    // against innerWidth, and is dropped entirely (hard clip, no sacrificed cell) when
    // innerWidth is not greater than the marker's width.
    private static Segment TruncateSegment(Segment segment, int innerWidth, string ellipsis)
    {
        if (innerWidth <= 0)
        {
            return Restyle(segment, string.Empty);
        }

        if (innerWidth <= ellipsis.Length)
        {
            // Too narrow for the marker at all: what survives is a clipped prefix of the real
            // link text itself (no separate ellipsis appended), so it stays linked like any other
            // truncated-but-real content.
            return Restyle(segment, segment.Plain[..Math.Min(innerWidth, segment.Plain.Length)]);
        }

        var contentBudget = innerWidth - ellipsis.Length;
        var clipped = segment.Plain[..Math.Min(contentBudget, segment.Plain.Length)];
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
    private static List<Segment> WrapSegment(Segment segment, int innerWidth)
    {
        var chunks = new List<Segment>();
        if (innerWidth <= 0)
        {
            chunks.Add(Restyle(segment, string.Empty));
            return chunks;
        }

        for (var i = 0; i < segment.Plain.Length; i += innerWidth)
        {
            var length = Math.Min(innerWidth, segment.Plain.Length - i);
            chunks.Add(Restyle(segment, segment.Plain.Substring(i, length)));
        }

        return chunks;
    }

    // §3.2 rule 3: an OSC 8 link (see OscHyperlink) wraps a segment's *style* markup, not its
    // Plain text, so restyling a linked segment must unwrap the link, restyle the inner styled
    // markup with RestyleSimple, and re-wrap around the same URL — rather than trying to
    // pattern-match the link's raw escape bytes as if they were an ordinary style prefix/suffix.
    // Every call site (truncation, wrap-chunking) goes through this rather than RestyleSimple
    // directly, so a link is preserved (and, for a wrapped segment, reopened per continuation
    // row) without each caller needing its own link-awareness.
    private static Segment Restyle(Segment original, string newPlain)
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
    private static Segment RestyleSimple(Segment original, string newPlain)
    {
        var newMarkup = TryGetSimpleWrap(original, out var prefix, out var suffix)
            ? prefix + Spectre.Console.Markup.Escape(newPlain) + suffix
            : Spectre.Console.Markup.Escape(newPlain);

        return new Segment(newMarkup, newPlain);
    }

    private static bool TryGetSimpleWrap(Segment segment, out string prefix, out string suffix)
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
