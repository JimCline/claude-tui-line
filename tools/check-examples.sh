#!/usr/bin/env bash
# Every rendered example this documentation shows must be one the binary actually produces.
#
# The other two checks compare the documentation against itself: check-citations.sh resolves
# §-references to sections in the same tree, check-counts.sh compares a sentence to the list
# under it. Both are closed-world, and a closed-world check cannot catch a document's false
# claim about code — the document can be perfectly self-consistent and still describe a
# renderer that does something else.
#
# That is not hypothetical. §9.6.2's shape carried `"example": "⎇ main"` through four
# revisions. No C# ever emitted that glyph; the branch item renders a bare name in green,
# which CAPTURE.md line 60 confirms is also what the original bash did. It survived because
# an illustration invented to explain one rule was later cited as evidence for a different
# rule, and by then nothing marked it as invented. Writing an illustrative value costs
# nothing; verifying one means reading a builder — so examples default to invented unless
# something forces the check. This is that something.
#
# It closes the loop only because --items' `example` is rendered live off
# BuildDefaultSegment against the shared fixture (§9.3.1) rather than stored. The JSON
# cannot drift from the builders, so a disagreement here is always the document's.
#
# Two rules, both exact. Neither guesses at what a line "looks like":
#
#   A. Any `"example": "…"` inside a fenced block must be one of the live example values.
#      The JSON key is unmistakable and it is the form §9.6.2's normative shape uses.
#      Fenced only, because a fenced block asserts and prose discusses. STATUS.md records
#      the ⎇ defect by quoting `"example": "⎇ main"` in a sentence about it having been
#      removed — the first run of this check flagged that line, correctly by the letter and
#      wrongly by the point. A retrospective that cannot quote the value it is retiring
#      cannot describe a defect at all, and the check would have been switched off within a
#      week for saying so. The original defect was in §9.6.2's fenced shape, so nothing is
#      given up here.
#
#   B. Inside a fenced block that reproduces `--items` plain output, a row whose first
#      column is a known item id must carry that item's live example in its second.
#      The block must identify itself — see ANCHOR below. A prose table that merely
#      resembles the layout is not scanned, because elsewhere the document legitimately
#      shows items with a `format` or `extract` applied, and those are *not* the default
#      render. §4.3's `worktree:api(feature/ABC-123)` is the case in point: correct, and
#      nothing this check should ever object to.
#
# Columns are split on two-or-more spaces, so the padding widths in §9.6.2.2's illustration
# are not load-bearing — deliberately. That table is a convenience view; --json is the
# contract (§9.6.2.2), and pinning its whitespace here would freeze the half that was
# explicitly left free.
#
# Exit 0 clean, 1 with mismatches listed, 2 if the binary or the examples could not be
# obtained — never a silent pass. A check that reports clean because it could not run is
# the failure mode this whole file exists to prevent.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

ANCHOR='Item kinds: builtin'

die() { echo "check-examples: $*" >&2; exit 2; }

command -v jq >/dev/null 2>&1 || die "jq is required to read --items --json, and is not installed.
  Refusing to report clean without having compared anything. Install jq, or set
  CLAUDE_TUI_LINE_BIN to a built binary on a machine that has it."

# The binary is the oracle; this script never reimplements a builder. CLAUDE_TUI_LINE_BIN
# lets CI reuse the artifact it just built (and lets this be tested against a stub) instead
# of paying for a second build.
if [[ -n "${CLAUDE_TUI_LINE_BIN:-}" ]]; then
    [[ -x "$CLAUDE_TUI_LINE_BIN" ]] || die "CLAUDE_TUI_LINE_BIN is set to '$CLAUDE_TUI_LINE_BIN', which is not executable."
    items_json=$("$CLAUDE_TUI_LINE_BIN" --items --json 2>/dev/null)
elif command -v dotnet >/dev/null 2>&1; then
    items_json=$(dotnet run --project src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release -- --items --json 2>/dev/null)
else
    die "no binary. Set CLAUDE_TUI_LINE_BIN, or install the .NET SDK so this can build one."
fi

[[ -n "$items_json" ]] || die "--items --json produced no output."

pairs=$(printf '%s' "$items_json" | jq -r '.items[] | [.id, .example] | @tsv' 2>/dev/null) \
    || die "--items --json was not the expected shape (no .items[] with .id and .example)."

[[ -n "$pairs" ]] || die "--items --json listed no items — refusing to report clean."

FILES=("$@")
if [[ ${#FILES[@]} -eq 0 ]]; then
    # shellcheck disable=SC2207
    FILES=($(git ls-files '*.md' 2>/dev/null))
fi
[[ ${#FILES[@]} -gt 0 ]] || die "no markdown files to scan — refusing to report clean."

printf '%s\n' "$pairs" > "/tmp/check-examples-pairs.$$"

awk -v anchor="$ANCHOR" '
# A placeholder is a description of a value, not a claim about one. Only these two forms:
# an angle-bracketed slot, or an elision. Anything else is an assertion and gets checked.
function is_placeholder(s) {
    return s ~ /^<.*>$/ || s == "..." || s == "\342\200\246"
}
function trim(s) { sub(/^[ \t]+/, "", s); sub(/[ \t]+$/, "", s); return s }

# The pairs file: id \t live example.
FNR == NR {
    tab = index($0, "\t")
    if (tab > 0) {
        id = substr($0, 1, tab - 1)
        live[id] = substr($0, tab + 1)
        known[id] = 1
        seen_example[substr($0, tab + 1)] = 1
    }
    next
}

FNR == 1 { file = FILENAME; fence = 0; nbuf = 0 }

{
    # --- Rule A: every "example": "…" must be a value the binary emits. ------------------
    line = fence ? $0 : ""
    while (match(line, /"example"[ \t]*:[ \t]*"/)) {
        rest = substr(line, RSTART + RLENGTH)
        q = index(rest, "\"")
        if (q == 0) break
        val = substr(rest, 1, q - 1)
        if (val != "" && !is_placeholder(val) && !(val in seen_example))
            printf "%s:%d: \"example\": \"%s\" — no item renders that\n", file, FNR, val
        line = substr(rest, q + 1)
    }

    # --- Rule B: rows inside a block that identifies itself as --items output. -----------
    if ($0 ~ /^[ \t]*(```|~~~)/) {
        if (fence) {
            if (anchored)
                for (i = 1; i <= nbuf; i++) {
                    row = buf[i]
                    n = split(row, col, /  +/)
                    if (n >= 2 && (col[1] in known)) {
                        got = trim(col[2])
                        if (got != live[col[1]])
                            printf "%s:%d: %s shows \"%s\", renders \"%s\"\n", file, bufline[i], col[1], got, live[col[1]]
                    }
                }
            fence = 0; nbuf = 0; anchored = 0
        } else {
            fence = 1; nbuf = 0; anchored = 0
        }
        next
    }

    if (fence) {
        if (index($0, anchor) > 0) anchored = 1
        row = $0
        sub(/^[ \t]+/, "", row)
        nbuf++; buf[nbuf] = row; bufline[nbuf] = FNR
    }
}
' "/tmp/check-examples-pairs.$$" "${FILES[@]}" > "/tmp/check-examples.$$" 2>/dev/null

status=0
if [[ -s "/tmp/check-examples.$$" ]]; then
    echo "check-examples: a documented example disagrees with what the binary renders:" >&2
    sed 's/^/  /' "/tmp/check-examples.$$" >&2
    echo >&2
    echo "The binary is the fact and the document is the finding — --items' examples are" >&2
    echo "rendered live off the builders (§9.3.1), so they cannot be the stale side. Fix the" >&2
    echo "document. If you believe a builder is wrong, that is a separate change with a test." >&2
    status=1
else
    count=$(printf '%s\n' "$pairs" | wc -l | tr -d ' ')
    echo "check-examples: every documented example matches the binary ($count items, ${#FILES[@]} files)"
fi

rm -f "/tmp/check-examples.$$" "/tmp/check-examples-pairs.$$"
exit $status
