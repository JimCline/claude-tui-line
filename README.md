# claude-tui-line

A statusline framework for [Claude Code](https://claude.com/claude-code). You compose your
statusline out of panes and items — built-in ones, or your own shell and Python scripts — and it
renders inside a bordered TUI surface instead of a single line of concatenated text.

Written in C# on .NET 10 with [Spectre.Console](https://spectreconsole.net/), published as a
Native AOT binary so it starts fast enough to run on every render.

> **Status: pre-1.0 and moving.** The rendering engine is built and tested; the CLI, the authoring
> commands, and the MCP tools are not. See [STATUS.md](STATUS.md) for what is done and what is
> left, and [SPEC-V2-FRAMEWORK.md](SPEC-V2-FRAMEWORK.md) for the architecture.

## Why a framework

Claude Code's `statusLine` hook hands your program a JSON blob on stdin and prints whatever you
write to stdout. That is enough to build anything, which in practice means everyone rewrites the
same string-slicing and ANSI-escaping by hand, and it stops being maintainable the moment you want
two columns.

This takes over the layout. You describe *what* you want on the statusline; the framework decides
how many rows it needs, how wide each pane is, where to wrap, where to truncate, and what to drop
when the terminal gets narrow.

## Install

Either way, you need the [.NET 10 SDK](https://dotnet.microsoft.com/download) — this compiles
from source rather than shipping a binary.

### As a plugin

```
/plugin marketplace add JimCline/claude-tui-line
/plugin install claude-tui-line@claude-tui-line
/claude-tui-line:setup
```

`setup` checks your toolchain, builds into the plugin's data directory, **backs up whatever
statusline you already have** before touching anything, writes the new `statusLine` setting, and
shows you a rendered preview.

Three more commands cover the rest of the lifecycle — adopt an existing statusline, change it in
conversation, and go back:

| command | what it does |
|---|---|
| `/claude-tui-line:migrate` | Reads your current statusline script and maps each element onto an item — built-in, a `command` item wrapping your own logic, or reported as unmappable. Shows you the result before writing anything. |
| `/claude-tui-line:edit` | "Add git diff stat at the end of the first pane, adds green and deletes red." Edits the config, validates it, and shows you before-and-after renders. |
| `/claude-tui-line:revert` | Restores the statusline you had before any of this. |

All four share one backup ledger at `~/.claude/claude-tui-line/backups/`, which records the state
from *before claude-tui-line was ever installed* separately from every state since. That
distinction is the point: migrate, edit, migrate again, and a naive "back up whatever is there now"
would capture claude-tui-line's own command as the thing to restore. `revert` targets the original
by default, so the escape hatch survives any number of changes. See
[docs/backup-ledger.md](docs/backup-ledger.md).

> These three need the CLI (`--items`, `--check`, `--preview`), which is **not built yet** — they
> will tell you so and stop rather than guessing. `setup` works today.

### By hand

```bash
git clone https://github.com/JimCline/claude-tui-line.git
cd claude-tui-line
dotnet publish src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release -o publish
```

Then point Claude Code at the binary, in `~/.claude/settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "/absolute/path/to/claude-tui-line/publish/claude-tui-line",
    "refreshInterval": 1
  }
}
```

`refreshInterval: 1` means the binary runs once per second, so **startup cost is render cost** —
which is why this is AOT-compiled rather than a script.

If you already have a statusline script, back it up before you replace it. Installing as a plugin
instead gets you `/claude-tui-line:migrate`, which does the backup and maps your script's elements
onto items — though it needs the CLI, which is not built yet.

## Configuration

The config is a single JSON file, looked up in this order:

1. `$CLAUDE_TUI_LINE_CONFIG` — an explicit path, if set
2. `$HOME/.claude/claude-tui-line.json`
3. no config — built-in defaults

A minimal one:

```json
{
  "surface": {
    "pane": {
      "border": { "enabled": true, "style": "rounded", "color": "grey" },
      "items": [
        { "item": "directory" },
        { "item": "git-branch" },
        { "item": "model" },
        { "item": "context" }
      ]
    }
  }
}
```

### Panes

A pane either holds items or splits into child panes. Splits nest, so you can build columns of
rows of columns.

```json
{
  "surface": {
    "pane": {
      "split": "vertical",
      "gutter": 1,
      "children": [
        { "size": "fill",    "items": [ { "item": "directory" }, { "item": "git-branch" } ] },
        { "size": "content", "items": [ { "item": "model" }, { "item": "context" } ] }
      ]
    }
  }
}
```

Pane keys:

| key | accepted values |
|---|---|
| `split` | `"vertical"`, `"horizontal"` — makes this a container for `children` |
| `children` | child panes |
| `size` | a bare column count (`"24"`), a percentage (`"40%"`), `"content"`, or `"fill"` (also the default). `"auto"` is a deprecated alias for `"fill"` — note that it does **not** mean `"content"` |
| `minSize` / `maxSize` | integers — clamps on the resolved width |
| `distribute` | `"min-rows"` — size siblings to minimise total rows rather than greedily |
| `gutter` | integer — columns between children |
| `align` | `"left"` (default), `"center"`, `"right"` |
| `valign` | `"top"` (default), `"middle"`, `"bottom"` |
| `overflow` | `"wrap"`, `"truncate"`, `"overflow"` |
| `ellipsis` | the marker used when truncating |
| `maxRows` | integer — cap on rows this pane may occupy |
| `border` | `enabled`, `style`, `color` |
| `items` | the items to render, for a leaf pane |

Border `style` is one of `"rounded"` (default), `"square"`, `"heavy"`, `"double"`, `"ascii"`, or
`"none"`.

`size: "content"` measures the pane's own text and asks for exactly that much. `distribute:
"min-rows"` is the interesting one: rather than letting each pane grab what it wants in order, it
searches for the width split that makes the *whole statusline* as short as possible.

> Unrecognised values are currently accepted silently rather than rejected — an unknown `size`
> falls back to `"fill"`, an unknown `style` to `"rounded"`. A `--check` command that reports
> these instead of swallowing them is the next thing being built.

### Items

These ship built in. The ones marked *(opt-in)* render only where you place them yourself;
everything else is in the **default set** — the list you get when a pane omits `items`:

| | |
|---|---|
| `directory` | the current working directory |
| `git-branch` | current branch |
| `repo` | the workspace repo, as `owner/name` |
| `worktree` | worktree name and branch |
| `pr` | pull request number and review state |
| `model` | model display name |
| `model-short` | abbreviated model name *(opt-in)* |
| `effort` | reasoning effort level |
| `thinking` | whether extended thinking is on |
| `output-style` | active output style |
| `context` | context window usage |
| `rate-limits` | five-hour and seven-day usage |
| `agent` | active agent name |
| `engram` | Engram memory activity |
| `vim` | vim mode, when enabled |
| `remote-url` | the git remote URL *(opt-in)* |

`model-short` and `remote-url` are opt-in rather than default — `remote-url` because resolving it
shells out to git, which you should only pay for if you asked for it.

Each item entry accepts:

```json
{ "item": "context", "format": "ctx {}", "color": "aqua", "overflow": "truncate" }
```

`format`'s `{}` is the item's value.

### Custom items

Anything you can run from a shell can be an item. Give it an `id`, a `command`, and how long to
cache it:

```json
{
  "id": "diffstat",
  "command": ["git", "--no-pager", "diff", "--shortstat"],
  "ttlSeconds": 5,
  "timeoutMs": 200,
  "format": "± {}",
  "color": "olive"
}
```

`command` is an argv array, passed to the process verbatim — no shell, so no quoting hazards and
no command injection. Set `"shell": true` only if you genuinely need shell features, and
understand what you're opting into. `ttlSeconds` caches the result so a slow command doesn't run
on every render; `timeoutMs` bounds how long a render will wait for it.

Python, a script in your repo, `curl` — anything that writes a line to stdout works.

### Derived items

An item can take another item's value and reshape it, without that source item being displayed:

```json
{
  "id": "agent-short",
  "from": "agent",
  "extract": "[^:]+$",
  "case": "upper",
  "color": "aqua"
}
```

The pipeline runs `from` → `extract` → `case` → `format`, in that order. `extract` is a regex
applied to the raw provider value, so it sees the underlying data rather than the rendered text.

### Colours

Sixteen named colours, plus `default`, `dim`, and `bold`:

```
black   maroon  green   olive   navy    purple  teal    silver
grey    red     lime    yellow  blue    fuchsia aqua    white
```

These are theme-mapped — your terminal decides what `blue` actually looks like. `tools/colors.sh`
prints them all rendered in your own terminal.

Anywhere a colour is named you can also use a **256-palette name** (`deepskyblue1`, `orange3`), a
**bare palette index** (`"207"`, `"141"` — the number as a string, with no `color` prefix), or a
**hex literal** (`"#ff8800"`). How faithfully they render depends on the colour profile, which
defaults to the conservative one:

```json
{ "colorSystem": "truecolor" }
```

`standard` (the default) renders everything through the 16 theme colours, so a hex literal is
approximated by the nearest of them — it works, it just won't be the exact shade you asked for.
`256` and `truecolor` widen that. The default is deliberate: the 16 are the only colours that
follow the user's terminal theme, so a statusline built from them stays readable when the theme
changes, and one built from hex does not.

Colour can also be computed from a value. Define a named rule and reference it:

```json
{
  "colors": {
    "model-tone": {
      "from": "model",
      "match": [
        { "contains": "Sonnet", "color": "blue" },
        { "contains": "Opus",   "color": "yellow" },
        { "contains": "Fable",  "color": "fuchsia" }
      ],
      "default": "grey"
    }
  }
}
```

`thresholds` does the same for numbers — `{ "min": 80, "color": "red" }` — which is how `context`
and `rate-limits` shade themselves as they fill up. A pane border and the text inside it can
reference the same rule, so they change colour together.

### Hyperlinks

Items can carry an OSC 8 hyperlink, which terminals that support it render as clickable text:

```json
{ "item": "git-branch", "link": "{remote-url}/tree/{}" }
```

`{}` is this item's own value; `{other-id}` is another item's — and the referenced item does not
need to be displayed anywhere. Terminals without OSC 8 support just show the text.

## Layout, briefly

Width is the hard constraint. The usable surface is `COLUMNS` minus a small reserve for Claude
Code's own chrome — 3 columns, adjustable with a top-level `"layout": { "chromeReserve": 3 }` if
your terminal or Claude Code version reserves a different amount. Every sizing decision follows
from that number, so raising it shrinks everything uniformly rather than clipping one pane. Panes are measured, not guessed:
an item's *plain* text determines its width, and colour markup never does — so adding colour can
never change the layout.

When content genuinely doesn't fit, it degrades in a defined order rather than overflowing: wrap,
then truncate, then drop.

## Contributing

The architecture lives in [SPEC-V2-FRAMEWORK.md](SPEC-V2-FRAMEWORK.md) and is the source of truth
— it is written to be argued with, and sections are cited by number in commit messages and code
comments. [STATUS.md](STATUS.md) tracks what is built.

```bash
dotnet build src/ClaudeTuiLine/ClaudeTuiLine.csproj
dotnet test  tests/ClaudeTuiLine.Tests/ClaudeTuiLine.Tests.csproj
./tools/check-citations.sh
```

There is no solution file, so build and test commands name their project explicitly.

`check-citations.sh` verifies that every `§N.M` the spec cites resolves to a heading in the spec.
That sounds like housekeeping and is not: four references were cited and undefined — one of them
27 times — and every one had survived many careful readings, because prose citing a missing section
reads perfectly well. The sentence carries the meaning; the number is decoration until someone
tries to follow it. Run it after editing the spec. CI runs all three.

## Licence

MIT — see [LICENSE](LICENSE).
