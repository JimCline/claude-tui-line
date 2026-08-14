---
description: Adopt an existing statusline — map each of its elements onto claude-tui-line, backing the original up first
---

# Migrate an existing statusline to claude-tui-line

The user already has a statusline that works. Your job is to reproduce it, not to improve it —
they can ask for improvements afterwards, and they cannot ask for their old one back if you lose
it.

**Stop and report** at the first step that fails. A half-migrated statusline runs once a second.

## 0. Confirm the binary can answer questions

This command is a prompt; the binary is the oracle. Before anything else:

```bash
<claude-tui-line binary> --items --json
```

If that fails or is unrecognised, **stop**. Report that migration needs a build of the CLI
(SPEC-V2-FRAMEWORK.md §9) and that this version does not have one. Do not fall back to a list of
items written from memory or copied out of the README — an item list in a prompt file is a second
registry, it goes stale on the next item added, and a remembered id that no longer exists resolves
to nothing and is *silently suppressed*. You would produce a config that looks right and renders
short, with no error anywhere.

Keep the `--items` output. It is the only authority on what exists and what keys each item takes.
Also run `--colors --json` and keep that; it is the same rule for the palette.

## 1. Find what they have now

Read `~/.claude/settings.json` and look for `statusLine`.

- **No `statusLine` key** → nothing to migrate. Point them at `/claude-tui-line:setup`. Stop.
- **Already points at a `claude-tui-line` binary** → they are migrated. Say so and stop; do not
  re-migrate over a config they may have edited since.
- Otherwise note the command and continue.

## 2. Take the backup

Read and follow `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md` in full, now, before reading the
user's script and long before writing anything.

If no `origin` entry exists yet, this is it — the state before claude-tui-line ever touched this
machine, written exactly once ever. Getting this right here is what makes revert work three
migrations from now.

If the backup cannot be written, **stop**. Do not continue on the theory that you can reconstruct
the original later.

## 3. Inventory what the script emits

Read the script. Do not run it yet.

List every distinct thing it puts on the line, in order — each field, separator, prefix, colour,
and any hyperlink. Be exhaustive. This inventory is the contract for the rest of the migration: an
element not on this list is an element you will silently lose.

For each element record:

- what it displays
- where the value comes from (the stdin JSON, a shell command, an environment variable, a file)
- how it is formatted — prefix/suffix text, colour, truncation, **and any condition on it being
  shown at all**

That last one matters more than it looks. Many statuslines only show a field when it is non-empty,
or only inside a git repo. That is behaviour, not decoration, and dropping it produces a line that
looks wrong precisely when the user notices.

## 4. Map every element into exactly one of three tiers

1. **A built-in item**, when `--items` offers an equivalent. Match on what the value *is*, not what
   the script calls it — a `$branch` variable is `git-branch` regardless of naming. If a built-in
   has the right value in the wrong shape, this tier still applies via a derived item: `from` names
   the source, `extract` is a regex over the raw value, `case` folds it, `format` wraps it, applied
   in that order.

2. **A `command` item wrapping the original logic**, when no built-in fits. This tier is why
   migration can be lossless: worst case every element shells out to a snippet of the user's own
   script, and they still gain panes, borders, sizing, and colour rules over logic that already
   worked. Prefer an argv array to `"shell": true`; reach for `shell` only when pipes or expansion
   are genuinely needed, and say so in your report when you do. Give every command item a
   `ttlSeconds` and a `timeoutMs` — this runs once a second, and an unbounded command in that loop
   presents to the user as a frozen statusline.

3. **Unmappable** — reported to the user, never silently dropped.

**Tier 3 existing is what makes tiers 1 and 2 trustworthy.** A migration that cannot say what it
failed to carry across will quietly lose an element, and the user will not find out until the day
they needed it. Do not stretch tier 2 to avoid an empty tier-3 list.

Preserve colours by name, from `--colors`, where the original used raw ANSI. If the original varied
a colour by value — red above a threshold, a different colour per model — that is a colour rule
with `match` or `thresholds`, not a fixed colour. An OSC 8 escape becomes a `link` on the item.

## 5. Draft the config and check it — still without writing

Build the config in memory or in a temp file. One pane, items in their original order: a single
pane reproduces a one-line statusline most faithfully, and that is the goal. Do not split panes,
add borders, or reorder to look nicer. This is a port, and a port that also redesigns is a port
nobody can verify.

Then validate it:

```bash
<binary> --check --json --config <temp path>
```

`--check` names the offending key by JSON Pointer. Fix and re-check until clean. Do not skip this
because the config looks obviously fine — a bad config is *silent* (§7), so this is the only thing
standing between a typo and a statusline that renders short forever.

## 6. Check fidelity against the original — content, not bytes

Run both against the same stdin payload:

```bash
payload='{"cwd":"'"$PWD"'","model":{"display_name":"Claude Opus 5"}}'
echo "$payload" | COLUMNS=$(tput cols) <original command>
echo "$payload" | COLUMNS=$(tput cols) <binary> --preview --columns $(tput cols) --config <temp path>
```

This is **not** a byte-parity check — the layout differs by design, that is the point. It is a
*content* check: strip escape sequences from both, and every visible token the original produced
must either appear in the new render or be on the tier-3 list. Anything else is a silent drop
wearing a success message.

Also preview at `--columns 80` and `--columns 60`. Most layout mistakes only appear when something
has to wrap.

If the original script errors on this synthetic payload, say so rather than treating its empty
output as a match. Two blank lines are not parity.

## 7. Show the user, and wait

**Nothing is written until the user says yes.** Show them:

1. the proposed config
2. the side-by-side render from step 6
3. the tier-3 list — and if it is empty, say "everything mapped" rather than omitting the section;
   an absent section reads as an oversight

Then ask. If they say no, you have written nothing outside the backup directory and there is
nothing to undo.

## 8. Write, then report

Write `~/.claude/claude-tui-line.json`, and point `statusLine.command` at the binary following the
ledger procedure (only the `statusLine` key, atomically, other keys preserved).

Report, in this order:

1. **The backup path and which ledger entry kind you appended** — first, before anything else, and
   that `/claude-tui-line:revert` restores it.
2. **The mapping table** — one row per inventoried element: what it was, what it became, which
   tier.
3. **The tier-3 list**, as its own section.
4. **What you were unsure about** — any element where you guessed at intent, any `shell: true`, any
   `ttlSeconds` you invented, any colour you approximated. This is the section that earns the
   user's trust in the other three.

Do not describe the migration as verified beyond what step 6 actually showed.
