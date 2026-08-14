---
description: Restore the statusline from the backup ledger — the original by default, or a named checkpoint
argument-hint: "[optional: a checkpoint timestamp; omit to restore the original]"
---

# Revert to a backed-up statusline

This is the escape hatch. It should work when the user is annoyed and does not want a conversation.
Requested checkpoint, if any: **$ARGUMENTS**

## 1. Read the ledger

Read `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md` for the format, then read
`~/.claude/claude-tui-line/backups/ledger.json`.

**Missing, empty, or unreadable** → say so plainly and stop. Do not reconstruct a statusline from
memory, from this repo, or from anything in the conversation — a fabricated statusline the user
believes is theirs is worse than none. Tell them where the ledger would have been, and offer to
remove the `statusLine` key entirely so Claude Code falls back to no statusline. That is a clean,
honest state.

## 2. Choose the entry

- **No argument** → the `origin` entry. This is the default *because* it is the state before
  claude-tui-line ever touched the machine, and it stays reachable no matter how many migrates and
  edits have happened since. Do not substitute "the most recent checkpoint" as a convenience; the
  newest backup is frequently claude-tui-line's own command, and restoring that is not a revert.
- **An argument** → the `checkpoint` whose timestamp matches. If none matches, list the available
  entries with their timestamps and their recorded `statusLine.command`, and ask. Do not guess at
  the nearest one.
- **No `origin` entry exists** (possible if the ledger was created by an older version) → say so,
  list every `checkpoint` with its timestamp and command, and let the user choose. Flag which ones
  point at a `claude-tui-line` binary, so they are choosing between recognisable things rather than
  between timestamps.

Show the user which entry you are about to restore, and its recorded command, before restoring it.

## 3. Checkpoint the current state first

Yes, even now — and this is required, not a courtesy. Reverting is itself a change: follow the
ledger procedure and append a **`checkpoint`** for the state you are about to replace, so reverting
a revert is possible. The user may revert, dislike the old one more, and want today's config back.

This never consumes or removes the `origin`. The `origin` entry survives every operation.

If the checkpoint cannot be written, ask the user whether to proceed anyway rather than deciding
for them.

## 4. Verify before restoring

Re-hash the artifacts the ledger entry points at and compare against the recorded SHA-256.

**On a mismatch, stop and report it.** Do not proceed, and do not overwrite anything to make the
numbers agree. A mismatch means the file changed since it was captured — the whole reason the hash
was recorded is to notice this, and silently proceeding discards the only information the check
produced. Show the user both hashes and let them decide.

## 5. Restore

Restore the entry's `statusLine` value verbatim into `~/.claude/settings.json` — **only** that key,
written atomically, every other key and the file's formatting preserved. Never copy the backed-up
settings file wholesale over the live one; the user may have changed unrelated settings since, and
a wholesale copy silently reverts those too.

If the entry recorded a script and that script is **missing** from its original path, restore the
copy there as well, and say that you did. A restored command pointing at a file that no longer
exists leaves the user with no statusline and no obvious cause.

If the entry's `statusLine` was `null` — there genuinely was no statusline before — remove the key.
That is the correct restoration of that state.

## 6. Leave claude-tui-line's own things alone

Do **not** delete `~/.claude/claude-tui-line.json`. It is the user's work, it costs nothing to
keep, and it is what makes coming back cheap. Do not delete the built binary either. Tell them both
are still there and that re-pointing `statusLine.command` at the binary brings it all back.

Do not delete anything in the backup directory, ever.

## 7. Confirm concretely

Print the restored command verbatim — a user reaching for revert is already having a bad time and
deserves to see exactly what they got back — then render it:

```bash
echo '{"cwd":"'"$PWD"'","model":{"display_name":"Claude Opus 5"}}' \
  | COLUMNS=$(tput cols) <the restored command>
```

If it produces nothing, or errors, **say so**. That is a real finding about the backup and the user
needs it now rather than next session. Do not report a revert as successful on the strength of
having written the file.

Then, briefly: what was restored, which ledger entry it came from, where the new checkpoint went,
and that `claude-tui-line.json` is untouched.
