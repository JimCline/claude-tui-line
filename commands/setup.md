---
description: Build claude-tui-line and wire it up as this machine's statusline, backing up whatever is there now
---

# Set up claude-tui-line as the statusline

Work through these steps in order. **Stop and report** at the first one that fails rather than
carrying on — a half-installed statusline is worse than none, because Claude Code will run a
broken command once a second.

## 1. Check the toolchain

```bash
(cd "${CLAUDE_PLUGIN_ROOT}/src/ClaudeTuiLine" && dotnet --version)
```

Needs .NET 10 or newer. If `dotnet` is missing or older, stop and tell the user to install the
.NET 10 SDK from https://dotnet.microsoft.com/download. Do not attempt to install it yourself.

Run it from the project directory, not from wherever the user happens to be. `dotnet --version`
reports the SDK selected *for the current directory*, which a `global.json` anywhere above it can
pin — so checking in one directory and building in another can pass a check for an SDK the build
never uses, and report the resulting failure as a build error rather than a toolchain one.

## 2. Build the binary

Build into the plugin's own data directory so it survives plugin updates and never collides with
a working tree the user may also be building in.

**Check that `CLAUDE_PLUGIN_DATA` is actually set before using it in a path**, and fall back if it
is not:

```bash
BIN_DIR="${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin"
dotnet publish "${CLAUDE_PLUGIN_ROOT}/src/ClaudeTuiLine/ClaudeTuiLine.csproj" \
  -c Release -o "$BIN_DIR"
```

The guard is not defensive padding. Unset, `"${CLAUDE_PLUGIN_DATA}/bin"` expands to **`/bin`**, and
the command becomes a release build published into the system binary directory — which either fails
on permissions or, with a privileged shell, succeeds. Step 4 would then write
`"command": "/bin/claude-tui-line"` into settings.json and step 5 would confirm it renders. An unset
variable in a path expansion does not announce itself; it quietly names a different, real,
usually-worse directory. Use `$BIN_DIR` everywhere below rather than re-expanding the variable, so
there is one place this can be wrong.

The result is `$BIN_DIR/claude-tui-line`. Confirm it exists and is executable before going further,
and report which directory it went to — if the fallback was used, the user should hear that from
you rather than discover it. Report the build's exit code; if it is nonzero, show the error lines
and stop.

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
    "command": "<the absolute path to $BIN_DIR/claude-tui-line>",
    "refreshInterval": 1
  }
}
```

Expand it to a real absolute path — settings.json does not interpolate shell or plugin variables,
so a literal `${CLAUDE_PLUGIN_DATA}` or `$BIN_DIR` is written through verbatim and Claude Code runs
a command that does not exist. Step 5 is what catches that, and only if you follow it as written.

Write the file per **"Writing `settings.json`"** in `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md`:
only that key, atomically, everything else preserved.

## 5. Show the user what they will get — by running what you actually wrote

Read the `statusLine.command` value back out of `~/.claude/settings.json` and run **that string,
verbatim**:

```bash
echo '{"cwd":"'"$PWD"'","model":{"display_name":"Claude Opus 5"}}' \
  | COLUMNS=80 <the exact command string now in settings.json>
```

80 is written out rather than measured, and must stay that way (§12.1.1): you have no terminal, so
`tput cols` returns terminfo's static 80 while looking like it adapted. Report "at 80 columns".

**That payload is a placeholder, and the fix for it is not to make it bigger here.** SPEC §12.7.1
rules that this preview should run against §9.3.1's complete fixture, so the user sees the whole
statusline rather than a `cwd` and a model name. It cannot yet: the fixture lives inside the binary
with no way to pipe it out, and step 5 must run `statusLine.command` verbatim, which rules out
`--preview`'s empty-stdin fallback. Until the binary can emit it, this literal stays exactly as it
is. Do not hand-roll a fuller one — §9.3 requires exactly one synthetic payload, and a second
standing fixture in a command file is the defect §12.7.1 names, written once more.

**Not `${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line`.** That path was already proven in step 2 and is
not what is in doubt. The one untested thing after step 4 is *the expansion* — whether the absolute
path you substituted is the path that exists. Testing the variable instead of the value verifies
the half that was never at risk: if the expansion is wrong, or the literal `${CLAUDE_PLUGIN_DATA}`
was written through unexpanded, this preview still renders perfectly, setup still reports success,
and the user gets a blank statusline with nothing anywhere pointing at why. Reading the value back
out costs one step and is the only version of this check that can fail when the install is broken.

`/claude-tui-line:revert` already verifies this way, printing and running the command it restored.
Two commands answering the same question differently is worse than either being wrong alone, and
this is the one that runs on every install.

Show the output, and **split the two failures the way `/claude-tui-line:revert` does** — same
observation, same conclusion, because this is the paragraph above's rule applied to itself:

- **A nonzero exit, or anything on stderr** → a real finding. Say it plainly and now.
- **Empty stdout, exit 0** → *inconclusive*, not a symptom. Two ordinary causes have nothing to do
  with the install: this renders at 80 columns rather than the user's width, and the payload is
  minimal. Say it produced no output, say both reasons, and hand them the one-liner to run in their
  own terminal rather than chasing it here.

**This does not weaken what step 5 is for.** The failure it exists to catch — an unexpanded
`${CLAUDE_PLUGIN_DATA}` or a wrong absolute path written into settings.json — cannot present as
empty stdout, because a command that does not exist does not run: the shell exits nonzero and puts
"command not found" on stderr, landing in the first bucket. The bucket that got softer is the one
that never held this check's quarry.

Then say that the sample payload is **minimal**: real payloads carry workspace, session and usage
fields this one does not, so items that depend on them render absent here and will appear once it
is live. Otherwise a correct install reads as a half-broken one, and the user's first act is to
debug something that is working — which is the same reason empty stdout is inconclusive above,
stated for the partial case instead of the total one.

## 6. Tell them what happens next

Report, briefly:

- where the binary lives
- where the backup went, and that `/claude-tui-line:revert` restores it — it reads the ledger at
  `~/.claude/claude-tui-line/backups/ledger.jsonl`, and targets the `origin` entry by default no
  matter how many changes come after.

  **If step 3 wrote a `checkpoint` rather than an `origin`**, say so and give its timestamp. Bare
  `/claude-tui-line:revert` then does *not* restore what you just backed up — it restores the
  older `origin`, correctly and by design, and the user who read "your statusline is backed up"
  will not expect that. Tell them the argument to pass to get this state back. The default is
  right; leaving the difference unsaid is what is wrong
- that config goes in `~/.claude/settings.json`'s sibling, `~/.claude/claude-tui-line.json`, and
  that with no config file the built-in defaults apply
- that `$CLAUDE_TUI_LINE_CONFIG` overrides that path if they want to keep configs elsewhere

- that `/claude-tui-line:edit` changes the statusline in plain English, so they do not have to
  hand-write JSON to try something

Do not invent configuration examples in your summary. Point them at the project README, which
documents every pane key, all sixteen built-in items, custom `command` items, derived items,
colours, and hyperlinks.
