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
#   C. A markdown table preceded by `<!-- items-table -->` is an *enumeration* of the
#      builtin items, and must be exactly the live set — no id missing, none listed that
#      no longer exists, and each row's `(opt-in)` marker agreeing with `default: false`.
#      Rules A and B check that a documented value is real; this one checks that a
#      documented *list* is complete, which is the only failure a per-row check cannot
#      see. The README's table is the case that motivated it.
#
#      §9 forbids an item list embedded in a skill or command's prose, and both prompts
#      say in as many words not to copy one out of the README — so nothing automated
#      trusts this table. What it is, is the list a person reads before deciding to
#      build, and a README that cannot say what ships is worse than one that can. The
#      resolution is neither to delete it nor to trust it: make the binary the oracle
#      here too. TABLE_MARKER below, and it must be an HTML comment because a prose
#      table has no in-band string of its own to anchor on the way --items output does —
#      and the README's other tables (config keys, colours) must stay unscanned.
#
#      This does mean adding an item costs a README row. That is not the §1 zero-edit
#      promise being broken: §1 exempts "whatever is genuinely unique" about the new
#      thing, and a one-line description of what it reports is exactly that. The check
#      does not write the row, it refuses to let you not notice.
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
TABLE_MARKER='items-table'

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
    # `dotnet run` writes restore and build chatter to STDOUT, not stderr, so redirecting
    # stderr alone would hand jq MSBuild output with the JSON glued to the front of it.
    # Quiet the build, then keep everything from the first line that opens the object.
    # Deliberately not `tail -1`: that works only while the serializer emits one line, and
    # would break the day anyone sets WriteIndented — for a reason nobody would look for
    # here. MSBuild does not emit a line beginning with `{`.
    #
    # No `--nologo`: SDK 10.0.301's `dotnet run` does not claim that flag, so it is
    # forwarded past `--` into the app's own argv at any position, and the app answers
    # `unrecognized argument: '--nologo'` — a valid JSON error object, which is why this
    # presented as "--items --json was not the expected shape" rather than as a build
    # failure. `-v quiet` alone suppresses the banner.
    items_json=$(dotnet run --project src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release \
        -v quiet -- --items --json 2>/dev/null | sed -n '/^[[:space:]]*{/,$p')
else
    die "no binary. Set CLAUDE_TUI_LINE_BIN, or install the .NET SDK so this can build one."
fi

[[ -n "$items_json" ]] || die "--items --json produced no output."

# `.default` is emitted as a bool by §9.6.2's shape. Absent means default, but NOT via
# `.default // true`: jq's `//` fires on `false` as well as `null`, so every opt-in item
# would read back as a default one and rule C's opt-in half would be dead — passing on a
# correct README and passing just as hard on a wrong one. `has` asks the question that was
# meant. The bug was live here until the negative test for a flipped flag failed to fail.
pairs=$(printf '%s' "$items_json" \
    | jq -r '.items[] | [.id, .example, (if has("default") then .default else true end)] | @tsv' 2>/dev/null) \
    || die "--items --json was not the expected shape (no .items[] with .id and .example)."

[[ -n "$pairs" ]] || die "--items --json listed no items — refusing to report clean."

FILES=("$@")
if [[ ${#FILES[@]} -eq 0 ]]; then
    # shellcheck disable=SC2207
    FILES=($(git ls-files '*.md' 2>/dev/null))
fi
[[ ${#FILES[@]} -gt 0 ]] || die "no markdown files to scan — refusing to report clean."

printf '%s\n' "$pairs" > "/tmp/check-examples-pairs.$$"

awk -v anchor="$ANCHOR" -v marker="$TABLE_MARKER" '
# A placeholder is a description of a value, not a claim about one. Only these two forms:
# an angle-bracketed slot, or an elision. Anything else is an assertion and gets checked.
function is_placeholder(s) {
    return s ~ /^<.*>$/ || s == "..." || s == "\342\200\246"
}
function trim(s) { sub(/^[ \t]+/, "", s); sub(/[ \t]+$/, "", s); return s }

# Closing a marked table is where absence becomes visible: a row that is never written
# has no line to report against, so the finding is reported against the marker.
function close_table(   id) {
    if (!intable) return
    for (id in known)
        if (!(id in claimed))
            printf "%s:%d: items table omits `%s`, which --items lists\n", file, tableline, id
    for (id in claimed) delete claimed[id]
    intable = 0
}

# The pairs file: id \t live example \t default. @tsv escapes any embedded tab, so a
# three-way split cannot be confused by an example containing one.
FNR == NR {
    if (split($0, f, "\t") >= 3) {
        live[f[1]] = f[2]
        known[f[1]] = 1
        isdefault[f[1]] = f[3]
        seen_example[f[2]] = 1
    }
    next
}

FNR == 1 { close_table(); file = FILENAME; fence = 0; nbuf = 0 }

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

    # --- Rule C: a marked table must enumerate the live set exactly. ---------------------
    if (!fence && index($0, "<!--") > 0 && index($0, marker) > 0) {
        intable = 1; tableline = FNR; tablerows = 0; tablegap = 0
        next
    }
    if (intable && !fence) {
        if ($0 !~ /^[ \t]*\|/) {
            # A blank line has to be allowed between the marker and the table: most
            # markdown parsers need one, and an HTML comment butted against the header
            # row stops the table rendering at all. Two is the whole budget, so a marker
            # that names nothing is reported as omitting every item rather than drifting
            # down the file and swallowing an unrelated table.
            if (tablerows == 0 && ++tablegap <= 2) {
                # still looking for the header row
            } else
                close_table()
        } else {
            tablerows++
            row = $0
            sub(/^[ \t]*\|/, "", row); sub(/\|[ \t]*$/, "", row)
            n = split(row, col, /\|/)
            c1 = trim(col[1])
            # Only a lone backticked token is an assertion about an id. The header and the
            # `|---|---|` separator fall out here, as does any row of prose.
            if (n >= 2 && c1 ~ /^`[^`]+`$/) {
                id = substr(c1, 2, length(c1) - 2)
                if (!(id in known)) {
                    printf "%s:%d: items table lists `%s`, which --items does not\n", file, FNR, id
                } else {
                    claimed[id] = 1
                    optin = (index(col[2], "(opt-in)") > 0)
                    if (optin && isdefault[id] == "true")
                        printf "%s:%d: `%s` is marked (opt-in); --items reports default: true\n", file, FNR, id
                    else if (!optin && isdefault[id] != "true")
                        printf "%s:%d: `%s` is not marked (opt-in); --items reports default: false\n", file, FNR, id
                }
            }
        }
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

# A table that runs to the last line of the last file still has to be closed, or the one
# arrangement where the omission check never fires is the one nobody would think to test.
END { close_table() }
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
