# The backup ledger

**Every command that writes `settings.json` *or* `claude-tui-line.json` follows this. No
exceptions, no abbreviations.**

> That scope line used to read "writes to `settings.json`", and the narrower version hid a real
> defect for as long as it stood. `/claude-tui-line:edit` never touches `settings.json` — it edits
> `claude-tui-line.json` — yet it was instructed to checkpoint through this procedure, whose entry
> captured only the file `/edit` does not modify. So `/edit`'s "restore the checkpoint and report
> the failure" recovery path restored a `statusLine` key nobody had changed, left the broken config
> exactly where it was, and reported success. See "The config file is an artifact too" below.

This is SPEC-V2-FRAMEWORK.md §12.2, restated as a procedure. It lives in one file because four
commands need it and four copies would drift.

## Why not just timestamped backups

The obvious design is "copy `settings.json` to `settings.json.backup-<timestamp>` before writing."
It is wrong, and it fails on the second use rather than the first.

Migrate. Then edit. Then migrate again. A naive "back up whatever is there now" captures
**claude-tui-line's own command** as the thing to restore. Revert then cheerfully restores the tool
the user is trying to escape, and the further they get from their original statusline the harder it
becomes to reach. The escape hatch quietly closes exactly as it becomes needed.

So the ledger distinguishes the state that existed *before this tool ever touched the machine* from
every state since. That distinction is the entire reason this is a ledger and not a file-copy.

## Where it lives

```
~/.claude/claude-tui-line/backups/
  ledger.json
  <timestamp>-settings.json
  <timestamp>-<original-script-basename>
```

Under the user's Claude directory, **not** under `${CLAUDE_PLUGIN_DATA}`. A backup that a plugin
reinstall can delete is not a backup.

Create the directory if it does not exist. If you cannot create or write to it, **stop the command
you are running** and report why. Never proceed with a write on the theory that the backup can be
taken afterwards.

## `ledger.json`

A JSON array, **append-only**. Read it, append one entry, write the whole array back. Never edit or
remove an existing entry.

```json
[
  {
    "kind": "origin",
    "timestamp": "2026-08-13T04:12:07Z",
    "statusLine": { "type": "command", "command": "/Users/someone/.claude/statusline.sh", "refreshInterval": 1 },
    "settingsCopy": "20260813-041207-settings.json",
    "settingsSha256": "9f2b…",
    "scriptOriginalPath": "/Users/someone/.claude/statusline.sh",
    "scriptCopy": "20260813-041207-statusline.sh",
    "scriptSha256": "4c81…",
    "note": "state before claude-tui-line was first installed"
  }
]
```

`statusLine` holds the previous value **verbatim**, including keys you do not recognise. If there
was no `statusLine` key at all, record `"statusLine": null` — that is a real, restorable state, and
it is different from not knowing.

Omit the three `script*` fields when the previous command was not a script on disk.

### The config file is an artifact too

An entry also carries the claude-tui-line config, under the same three-field shape:

```json
"configOriginalPath": "/Users/someone/.claude/claude-tui-line.json",
"configCopy":         "20260813-041207-claude-tui-line.json",
"configSha256":       "b7e0…"
```

Record these **whenever a config file exists** at the path §5's search order resolves to
(`$CLAUDE_TUI_LINE_CONFIG` first, then `~/.claude/claude-tui-line.json`) — not only when the
command is about to change it.

**When no config file exists, record the absence rather than omitting the fields:**

```json
"configOriginalPath": "/Users/someone/.claude/claude-tui-line.json",
"configCopy":         null
```

`configOriginalPath` is the path the search order resolved to — where a config *would* have been —
and a null `configCopy` says we looked there and found nothing. `configSha256` is omitted, since
there is nothing to hash.

This mirrors `"statusLine": null` exactly, which is the point: an earlier version of this section
invoked that precedent and then did something weaker, telling authors to omit all three fields and
mention it in free-text `note`. Those are not the same. Three fields missing is indistinguishable
from an entry written before configs were captured at all, and prose in `note` is not something a
rollback can branch on. So a command asking "was there a config here?" gets *no config was here*
and *this ledger cannot say* as the same answer, and the two call for opposite actions — delete the
file, or leave it alone. This is the third place in this project where absence needed a
distinguished value rather than a missing key (see also SPEC §12.6.9's `revision: "absent"`); it is
worth assuming it will be the shape of the fourth.

**This is what makes `/claude-tui-line:edit` recoverable at all.** `/edit` changes the config and
nothing else. An entry holding only `settings.json` is a backup of the one file that command
cannot break, and restoring it is a no-op that looks like a recovery — the config stays broken,
the report says restored, and the pre-edit config is gone. A backup that does not contain the
thing the command modifies is not a backup, however carefully the rest of the procedure is
followed.

The two artifacts are restored independently. Reverting the statusline does not touch the config
(see `revert`, step 6), and rolling back an edit does not touch `settings.json`.

Timestamps are UTC, ISO 8601. Compute hashes with `shasum -a 256 <file>` and store the bare hex.

Artifact filenames are second-resolution, so two writes in the same second collide. If the name
you are about to write already exists, append a counter (`-2`, `-3`) rather than writing over it —
rule 1 below is absolute, and a naming scheme that silently overwrites would breach it by accident
rather than by decision.

## The two kinds

- **`origin`** — the state before claude-tui-line ever touched this machine. **Written exactly once,
  ever.** Before writing one, read the ledger; if an `origin` entry already exists, you must not
  write another, no matter how long ago it was or how wrong it looks. Append a `checkpoint`
  instead.
- **`checkpoint`** — any state captured since. Written freely, as often as anything writes.

**An `origin` must never record a `statusLine` that already points at a claude-tui-line binary.**
Check before writing one. A user can arrive at that state without this tool ever having run — by
hand-editing `settings.json` and only later invoking `/claude-tui-line:setup` — and the naive
"no `origin` exists, so this is the origin" rule would then record claude-tui-line's own command
as the state to escape *to*. Because `origin` is written exactly once ever, that is unfixable
afterwards: the escape hatch is poisoned at the moment it is created, which is worse than the
second-use failure this whole design exists to prevent.

When the live `statusLine` already points at a claude-tui-line binary and no `origin` exists,
append a **`checkpoint`** and leave `origin` unwritten. A missing `origin` is an honest state and
the commands already handle it — `revert` lists the checkpoints, flags which ones point at a
claude-tui-line binary, and lets the user choose. A *false* `origin` gets no such handling,
because nothing downstream has any reason to doubt it.

Reverting is itself a change: it appends a `checkpoint` for the state it replaced, and it does
**not** consume or remove the `origin`. Reverting a revert has to be possible.

## The three rules

1. **Nothing in the backup directory is ever overwritten or deleted by any command.** Not stale
   entries, not superseded copies, not the directory itself. Pruning is the user's to do.

2. **The user's original script is copied, never moved and never modified.** Take the copy even
   though installing does not touch the script — restoring a command that points at a file the user
   has since deleted is a broken revert with no obvious cause.

3. **Only the `statusLine` key of `settings.json` is read or written.** Preserve every other key and
   the file's existing formatting. Write atomically: temp file in the same directory, then rename,
   so an interrupted write cannot leave a truncated `settings.json` — which would break far more
   than the statusline.

## Checking a hash before restoring

The hashes answer two different questions about two different files, and an earlier version of
this section collapsed them into one instruction — "re-hash the live file and compare against the
ledger entry" — which is wrong in a way that disables the escape hatch.

**Applied to `settings.json`, that check fails every time by construction.** At revert time the
live `settings.json` is *supposed* to differ from the backed-up copy: claude-tui-line is installed
now and was not then. A revert following it literally would report "the user hand-edited it" on
every run and stop, which is the escape hatch refusing precisely when it is reached for.

So, explicitly:

| file | checked? | what a mismatch means |
|---|---|---|
| the **backup copies** in the backup directory | **yes**, before restoring | the backup store is damaged — stop, do not restore from it |
| the **live `settings.json`** | **no** | nothing; it differs by design |
| the **live `claude-tui-line.json`** | **no**, at revert time | nothing; revert does not touch it |
| the user's **original script**, at its original path | **yes**, when the entry records one | the user edited it since the backup — restoring the command restores a pointer to different code |

That last row is where the original instruction's rationale actually belongs, and it is the case
nobody was checking. A `statusLine` restored verbatim points at a path, not at contents; if the
script at that path has changed, the revert succeeds, the statusline runs, and it is not the
statusline that was backed up. Nothing about the outcome says so.

**Report the mismatch and let the user decide.** Do not overwrite their edit to make the numbers
agree — the whole point of recording the hash is to notice this, and silently proceeding discards
the only information the check produced. For a modified script, the choice is between the live
version and the backed-up copy, and they need to see that it is a choice.

## Writing `settings.json`

Every command here eventually writes that file, and they must all write it the same way. This is
the one definition; the commands cite it rather than restating it.

1. **Write only the `statusLine` key.** Never copy a backed-up `settings.json` wholesale over the
   live one. The user may have changed unrelated settings since the backup was taken, and a
   wholesale copy reverts those too — silently, and with no mention in any report, because the
   command believes it restored one key.
2. **Atomically** — temp file in the same directory, then rename. A statusline command runs once a
   second, so a torn write is read almost immediately.
3. **Preserve every other key and the file's formatting.** Edit it; do not regenerate it. A
   reformatted settings.json makes the real change unreviewable and buries anything unintended.
4. **`"statusLine": null` restores by removing the key.** There genuinely was no statusline, and
   the absence of the key is the faithful reproduction of that state — not an empty object, and
   not a no-op.

## The procedure, in order

Any command that is about to write `settings.json` or `claude-tui-line.json`:

1. Ensure `~/.claude/claude-tui-line/backups/` exists and is writable. Stop if not.
2. Read `ledger.json` (treat a missing file as `[]`).
3. Read the live `settings.json` and its current `statusLine` value.
4. Copy `settings.json` into the backup directory with a timestamped name; hash it.
5. If `statusLine.command` names a script on disk, copy that too; hash it.
6. **Resolve the config path (§5's search order) and, if a file is there, copy and hash it too.**
   Do this whichever file you came here to write — an entry that captures only the artifact this
   particular command happens to modify cannot recover the other one, and the command that needs
   it will not be the one that took the backup. **If no file is there, still record
   `configOriginalPath` with `configCopy: null`** — the resolved path plus an explicit "nothing was
   here", never three omitted fields.
7. Append **one** entry — `origin` if and only if no `origin` entry exists **and** the current
   `statusLine` does not already point at a claude-tui-line binary; otherwise `checkpoint`.
8. Write `ledger.json` back.
9. **Only now** write: the new `statusLine` into `settings.json` atomically, preserving other keys,
   and/or the new `claude-tui-line.json`.
10. Report the backup path and the kind of entry you appended.

**Every step that can abort comes before every step that writes**, and that ordering is the point
rather than a convenience. A command that mutates the ledger and then discovers it must stop
leaves a permanent entry for a change that never happened — permanent because rule 1 forbids
removing it.
