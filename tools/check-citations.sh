#!/usr/bin/env bash
# Every §N.M cited in the spec must resolve to a heading in the same document.
#
# §13.3 found four that did not, including §7 cited 27 times, and all four had
# survived many careful readings — prose citing a missing section reads
# correctly, because the sentence carries the meaning and the number is
# decoration until someone tries to follow it. So this is checked mechanically
# or it is not checked.
#
# Exit 0 clean, 1 with dangling references listed, 2 if the spec is unreadable.
#
# -E everywhere on purpose: `sed 's/x\+//'` is a GNU extension that BSD sed
# accepts and silently does not apply, so the first version of this script
# stripped nothing, matched nothing, and reported all 69 cited sections as
# dangling. A check whose failure mode is "everything is broken" is at least
# loud; the same bug in the other direction reports clean forever.

set -uo pipefail

SPEC="${1:-$(dirname "$0")/../SPEC-V2-FRAMEWORK.md}"

if [[ ! -f "$SPEC" ]]; then
    echo "check-citations: no such file: $SPEC" >&2
    exit 2
fi

# Headings define; every other occurrence of §N.M cites. The two sets cannot be
# confused here because headings are written "### 9.4.1" with no § — if that
# ever changes, exclude heading lines from the citation side, or every section
# becomes self-defining and this check goes vacuously green.
# Backticked references are *mentioned*, not cited. §13.3 discusses broken
# references by number and would otherwise trip this check on the very numbers
# it exists to have found — a checker that cannot describe its own findings.
# The document already draws this line: real citations are bare (§9.4), and
# §13.3's table of dangling numbers is written `§10.6`. The cost is a false
# negative if someone backticks a genuine citation; that is the safe direction,
# since the alternative is a permanently red check that gets ignored.
cited=$(sed -E 's/`[^`]*`//g' "$SPEC" \
        | grep -oE '§[0-9]+(\.[0-9]+)*' \
        | grep -vE '^§[0-9]+$' \
        | sed -E 's/^§//' | sort -u)

defined=$(grep -oE '^#+ [0-9]+(\.[0-9]+)*' "$SPEC" \
          | sed -E 's/^#+ //' | sort -u)

if [[ -z "$defined" ]]; then
    echo "check-citations: found no numbered headings at all — the extraction is broken," >&2
    echo "not the document. Refusing to report every citation as dangling." >&2
    exit 2
fi

dangling=$(comm -23 <(echo "$cited") <(echo "$defined"))

if [[ -z "$dangling" ]]; then
    echo "check-citations: all $(echo "$cited" | wc -l | tr -d ' ') cited sections resolve"
    exit 0
fi

echo "check-citations: cited but never defined as a heading:" >&2
while read -r ref; do
    [[ -z "$ref" ]] && continue
    n=$(grep -cE "§${ref}([^0-9]|$)" "$SPEC")
    echo "  §${ref} — cited ${n}×" >&2
done <<< "$dangling"
echo >&2
echo "Fix by giving the content a heading (§13.3's default), or — when that number" >&2
echo "is already taken on a different axis — by rewriting the citations." >&2
exit 1
