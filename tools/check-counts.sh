#!/usr/bin/env bash
# A sentence that announces how many items follow must agree with the list that follows it.
#
# Found twice by hand: §8's segment count read as a superset of what it listed, and §12.2
# said "Three rules, none optional" above four rules. The second is the instructive one —
# the extra rule was the one whose failure mode is explicitly silent, and a reader
# reconciling the two documents would have concluded it was the editing error and dropped
# it. Both survived many readings, for the same reason dangling citations do: the sentence
# reads correctly either way, and nobody counts.
#
# Scope, deliberately narrow. A line qualifies only when it
#   - ends in a colon, outside a fenced code block,
#   - names a count as a word or digit, and
#   - is followed by an actual list.
# The count taken is the LAST one before the colon, because that is the one qualifying the
# noun the list enumerates. Lines with no list under them are skipped rather than guessed
# at, which is what keeps arithmetic prose ("four values of T, two panes:") out of the
# report. Hedged counts ("at least three:") are excluded — a lower bound is not a count.
#
# The bar this has to clear is not "finds things" but "is worth reading every time". A
# check that cries wolf gets ignored, and an ignored check is worse than none: it occupies
# the slot a real one would have. If this starts reporting prose, tighten it or delete it.
#
# Exit 0 clean, 1 with mismatches listed, 2 if nothing could be scanned.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

FILES=("$@")
if [[ ${#FILES[@]} -eq 0 ]]; then
    # shellcheck disable=SC2207
    FILES=($(git ls-files '*.md' 2>/dev/null))
fi

if [[ ${#FILES[@]} -eq 0 ]]; then
    echo "check-counts: no markdown files to scan — refusing to report clean." >&2
    exit 2
fi

awk '
function num(w) {
    if (w ~ /^[0-9]+$/) return w + 0
    if (w == "two")   return 2
    if (w == "three") return 3
    if (w == "four")  return 4
    if (w == "five")  return 5
    if (w == "six")   return 6
    if (w == "seven") return 7
    if (w == "eight") return 8
    if (w == "nine")  return 9
    if (w == "ten")   return 10
    return 0
}
# The marker class of a list line, or "" if it is not one. Ordered and unordered are
# different classes so a bulleted aside nested under a numbered list cannot inflate a count.
function marker(s,   t) {
    t = s
    sub(/^[ \t]+/, "", t)
    if (t ~ /^[0-9]+\.[ \t]/) return "ol"
    if (t ~ /^[-*+][ \t]/)    return "ul"
    return ""
}
function indent(s,   t) {
    t = s
    if (match(t, /^[ \t]*/)) return RLENGTH
    return 0
}
function flush(   ) {
    if (pending && found > 0 && found != want)
        printf "%s:%d: says %d, lists %d — %s\n", file, lead_line, want, found, lead_text
    pending = 0
}
BEGIN { fence = 0; pending = 0 }
FNR == 1 { flush(); fence = 0; file = FILENAME }
{
    if ($0 ~ /^[ \t]*(```|~~~)/) { fence = !fence; if (pending && found > 0) flush(); next }
    if (fence) next

    m = marker($0)

    if (pending) {
        if ($0 ~ /^[ \t]*$/) {
            blanks++
            if (blanks >= 2 && found > 0) flush()
        } else if (m != "" && indent($0) == item_indent && m == item_marker) {
            found++; blanks = 0
        } else if (found == 0 && m != "") {
            item_indent = indent($0); item_marker = m; found = 1; blanks = 0
        } else if (found > 0 && indent($0) > item_indent) {
            blanks = 0                      # continuation of the current item
        } else {
            flush()
        }
    }

    if (pending) next

    # A lead-in: ends in a colon, names a count, and is not hedged into a bound.
    if ($0 ~ /:[ \t]*$/ && $0 !~ /^[ \t]*(#|>)/) {
        line = tolower($0)
        if (line ~ /(at least|at most|up to|more than|fewer than|no more than|roughly|about|around|some)/) next

        # Everything below removes numbers that are names rather than counts. Without this
        # the check reports "SHA-256 of each captured artifact:" as promising 256 items,
        # which is the noise that would get it ignored.
        gsub(/`[^`]*`/, " ", line)          # inline code
        gsub(/§[0-9]+(\.[0-9]+)*/, " ", line)   # section references
        gsub(/[0-9]+(\.[0-9]+)+/, " ", line)    # dotted numbers generally
        gsub(/[a-z]-[0-9]+/, " ", line)         # sha-256, utf-8
        gsub(/[0-9]+[-–—][0-9]+/, " ", line)    # ranges: "defects 3-6"
        sub(/^[ \t]*([0-9]+\.|[-*+])[ \t]+/, "", line)  # the lead-in is itself a list item

        # The FIRST count qualifying a plural noun is the one the list answers to. Later
        # numerals in the same sentence are almost always incidental. Requiring a following
        # word also drops "…Segment 13." — a numeral ending a clause counts nothing.
        want = 0
        if (match(line, /(^|[ \t(])(two|three|four|five|six|seven|eight|nine|ten|[0-9]+)[ \t]+[a-z]/)) {
            tok = substr(line, RSTART, RLENGTH)
            gsub(/^[ \t(]|[ \t]+[a-z]$/, "", tok)
            want = num(tok)
        }
        if (want > 0) {
            pending = 1; found = 0; blanks = 0; item_marker = ""; item_indent = 0
            lead_line = FNR; lead_text = $0
            sub(/^[ \t]+/, "", lead_text)
            if (length(lead_text) > 78) lead_text = substr(lead_text, 1, 75) "..."
        }
    }
}
END { flush() }
' "${FILES[@]}" > /tmp/check-counts.$$ 2>/dev/null

status=0
if [[ -s /tmp/check-counts.$$ ]]; then
    echo "check-counts: a stated count disagrees with the list beneath it:" >&2
    sed 's/^/  /' /tmp/check-counts.$$ >&2
    echo >&2
    echo "Fix the number, or the list. Which one is wrong is a judgement — but they cannot" >&2
    echo "both stand, and the reader who notices will not know which to trust." >&2
    status=1
else
    echo "check-counts: every stated count matches its list (${#FILES[@]} files)"
fi

rm -f /tmp/check-counts.$$
exit $status
