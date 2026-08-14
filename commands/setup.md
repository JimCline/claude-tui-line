---
description: Build claude-tui-line and wire it up as this machine's statusline, backing up whatever is there now
---

# Set up claude-tui-line as the statusline

Work through these steps in order. **Stop and report** at the first one that fails rather than
carrying on — a half-installed statusline is worse than none, because Claude Code will run a
broken command once a second.

## 1. Check the toolchain

```bash
dotnet --version
```

Needs .NET 10 or newer. If `dotnet` is missing or older, stop and tell the user to install the
.NET 10 SDK from https://dotnet.microsoft.com/download. Do not attempt to install it yourself.

## 2. Build the binary

Build into the plugin's own data directory so it survives plugin updates and never collides with
a working tree the user may also be building in:

```bash
dotnet publish "${CLAUDE_PLUGIN_ROOT}/src/ClaudeTuiLine/ClaudeTuiLine.csproj" \
  -c Release -o "${CLAUDE_PLUGIN_DATA}/bin"
```

The result is `${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line`. Confirm it exists and is executable
before going further. Report the build's exit code; if it is nonzero, show the error lines and
stop.

## 3. Back up whatever statusline is already configured

**This step is mandatory and must happen before step 4 writes anything.**

Read `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md` and follow it in full. Do not improvise a
timestamped file copy — the ledger exists because the obvious design fails on the *second* use,
capturing claude-tui-line's own command as the thing to restore, and revert then restores the tool
the user was trying to escape.

For this command the entry is usually **`origin`**: the state before claude-tui-line ever touched
this machine, written exactly once ever. Two conditions make it a `checkpoint` instead, and the
ledger requires checking both — an `origin` already exists (setup has run here before), **or** the
current `statusLine` already points at a claude-tui-line binary. The second happens when someone
wires the binary up by hand and runs setup afterwards; recording that as `origin` would make the
tool its own escape hatch, permanently, since `origin` is written once ever.

Record `"statusLine": null` if there is no existing key. That is a real, restorable state, and it
is different from not knowing what was there.

Report the backup path and which kind of entry you appended. If the backup cannot be written,
**stop** — do not proceed to step 4.

## 4. Point Claude Code at the binary

Edit `~/.claude/settings.json` to set:

```json
{
  "statusLine": {
    "type": "command",
    "command": "<absolute path to ${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line>",
    "refreshInterval": 1
  }
}
```

Expand `${CLAUDE_PLUGIN_DATA}` to a real absolute path — settings.json does not interpolate plugin
variables. Write **only** the `statusLine` key, atomically (temp file in the same directory, then
rename), preserving every other key and the file's formatting. Edit it; do not rewrite it.

## 5. Show the user what they will get

Render a preview by feeding the binary a sample payload, rather than asking the user to squint at
their own statusline and guess whether it changed:

```bash
echo '{"cwd":"'"$PWD"'","model":{"display_name":"Claude Opus 5"}}' \
  | COLUMNS=$(tput cols) "${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line"
```

Show the output. If it is empty, that is a symptom worth chasing rather than reporting as success
— check stderr and say so plainly.

## 6. Tell them what happens next

Report, briefly:

- where the binary lives
- where the backup went, and that `/claude-tui-line:revert` restores it — it reads the ledger at
  `~/.claude/claude-tui-line/backups/ledger.json`, and targets the `origin` entry by default no
  matter how many changes come after
- that config goes in `~/.claude/settings.json`'s sibling, `~/.claude/claude-tui-line.json`, and
  that with no config file the built-in defaults apply
- that `$CLAUDE_TUI_LINE_CONFIG` overrides that path if they want to keep configs elsewhere

- that `/claude-tui-line:edit` changes the statusline in plain English, so they do not have to
  hand-write JSON to try something

Do not invent configuration examples in your summary. Point them at the project README, which
documents every pane key, all sixteen built-in items, custom `command` items, derived items,
colours, and hyperlinks.
