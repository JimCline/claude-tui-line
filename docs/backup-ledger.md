# The backup ledger

**Every command that writes to `settings.json` follows this. No exceptions, no abbreviations.**

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

When restoring, re-hash the live file and compare against the ledger entry. A mismatch means the
user hand-edited it since the backup was taken.

**Report the mismatch and let the user decide.** Do not overwrite their edit to make the numbers
agree — the whole point of recording the hash is to notice this, and silently proceeding discards
the only information the check produced.

## The procedure, in order

Any command that is about to write `settings.json`:

1. Ensure `~/.claude/claude-tui-line/backups/` exists and is writable. Stop if not.
2. Read `ledger.json` (treat a missing file as `[]`).
3. Read the live `settings.json` and its current `statusLine` value.
4. Copy `settings.json` into the backup directory with a timestamped name; hash it.
5. If `statusLine.command` names a script on disk, copy that too; hash it.
6. Append **one** entry — `origin` if and only if no `origin` entry exists **and** the current
   `statusLine` does not already point at a claude-tui-line binary; otherwise `checkpoint`.
7. Write `ledger.json` back.
8. **Only now** write the new `statusLine` into `settings.json`, atomically, preserving other keys.
9. Report the backup path and the kind of entry you appended.
