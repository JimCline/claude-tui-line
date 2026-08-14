# claude-tui-line — status

Running progress against `SPEC-V2-FRAMEWORK.md`. Updated as work lands.

**Last updated:** 2026-08-13

A line is only **Done** here if it was verified independently of the report claiming it —
rebuilt from source with a matching SHA-256, or checked against rendered bytes. "Tests pass" is
not by itself enough; this project has twice had a green suite over a broken instrument.

---

## Done and verified

| Area | What | Evidence |
|---|---|---|
| Phase 1 | `chromeReserve` width fix — usable width is `COLUMNS - 3` | live |
| Phase 2 | Pane surface, root leaf pane, overflow `wrap` / `truncate` / `overflow` | acceptance §2.7 |
| Phase 3 | Splits: sizing (`fixed` / `content` / `percent` / `fill`), gutters, per-pane borders, `valign` | §2.9 eyeballed live |
| Phase 4 | Item registry, `command` providers, cache, TTL, timeouts | — |
| Colour | Named tokens (`@model-accent`), threshold rules, literals | §6 |
| Colour | Decorative vs semantic item colour — a configured colour replaces internal decorative colour, but never overrides a value-derived threshold | `verify_itemcolor.py`, innermost-SGR span read |
| Colour | Model name follows the model, no `"format": "{}"` workaround needed | build `1012fa45` |
| Cleanup | Banner glyph renderer removed | build `8ab7ffce`, 228 renders byte-identical across 4 configs × widths 20–240 |
| Sizing | Wrap-aware re-measurement — a narrower grant returns the longest wrapped row, not the grant | build `697e9629` |
| Cleanup | `capWidth` parameter removed from `LeafContent.Decide` | 1044/1044 tests |

## In flight

- **OSC 8 hyperlinks** (§3.2) — with the implementor. `ItemContext`, `AnsiStrip`, `OscHyperlink`,
  `RemoteUrl` and 8 new tests written; 4 red, deliberately. Brings `remote-url`, derived items
  (`from` / `extract` / `case`), the `ItemContext` refactor (§3), and one shared `AnsiStrip`.
  **Blocked on defect 0 below** — the hyperlink tests cannot pass until width stops being
  derived by parsing markup.
- **A render crash introduced at 19:14 today**, not pre-existing: `SegmentBuilder.cs:76` appends
  an unescaped reset to `.Markup`, which Spectre then fails to parse. Reaches
  `console.MarkupLine` at `Program.cs:104` and `:159`. Affects only `command`-provider items and
  any item with `link:` configured — **the live config uses neither, so the current statusline
  is not at risk.** Fix ruled: defect 0 first, then make `Markup` valid by construction.

## Queued, in order

1. **`distribute: "min-rows"`** (§2.3, motivated by §2.9) — even two-pane sizing preferring the
   fewest total rows. *Spec is currently motivation plus a config key; the allocation algorithm
   is not yet written. Blocking — must be spec'd before the implementor reaches it.*
2. **Phase 5 CLI** (§9) — `--check` (with `--json`), `--preview`, `--items`, `--colors`.
3. **Phase 6 authoring surface** (§12) — backup ledger **first**, then `migrate`, `revert`,
   `edit`.
4. **Config diagnostics** — see the open defects below.
5. **`maxRows` degrade ladder** (§2.8).
6. **Phase 7 MCP server** (§12.6) — ambient access, so "make the border green" works mid-
   conversation without the user knowing a slash command exists. Seven tools, **read/write**:
   `list_items`, `list_colors`, `get_config`, `set_config`, `validate`, `preview`, `revert` —
   enough for the model to carry a request from words to a rendered statusline unaided.
   `set_config` validates before it commits and never writes a config that fails `--check`;
   `preview` returns rendered rows so the model checks its work by looking rather than by
   asserting. **Deliberately last**: it wraps the CLI, so it cannot be designed before the CLI
   exists. Stateless, and the renderer stays a one-shot AOT binary regardless.

## Open defects

| # | Defect | Impact | Status |
|---|---|---|---|
| 0 | **Width is derived by parsing markup, not from `Plain`** — `Markup.Remove(...).Length` at three sites | Violates the invariant the layout rests on. `Markup.Remove` strips Spectre tags, not ANSI, so any row carrying raw escapes measures long and the border lands early — silently. Genuinely pre-existing | **Fixed and verified.** `PaneRow(Markup, Width)` threads measured width through the pipeline; the dead `MeasureRow`/`FromMarkupRows` deleted. Confirmed by independent rebuild, SHA `55baa073…59ac9` |
| 1 | OSC 8 hyperlinks are counted as visible text | Border lands ~50 columns early; row goes ragged. Reproduced: `sgr-stripped=77, sgr+osc-stripped=27, budget=77, RAGGED` at `COLUMNS=80` | Blocked on #8 — no link currently survives to output at all |
| 2 | `surface.maxRows` is entirely unenforced | 8 rows emitted at `COLUMNS=112` and 14 at `COLUMNS=60` against a configured 6 | Queued (5) |
| 3 | `ConfigLoader.TryReadConfig` swallows a malformed config | Exit 0, zero bytes on stderr, a completely different statusline renders. A JSON typo gives the user nothing to debug against | Queued (4) |
| 4 | `"auto"` and any unrecognized `size` silently resolve to `fill` | Same silent-acceptance class as #3 | Queued (4) |
| 5 | Unrecognized `case` value passes through unchanged | Same class; deliberately deferred to (4) rather than special-cased | Queued (4) |
| 6 | **An unrecognized colour name silently renders uncoloured** — `"color": "orange"` gives exit 0, empty stderr, and no SGR at all | Same silent-acceptance class as #3–#5, and the one most likely to be hit by a model authoring config (§4.1), which will reach for plausible names like `orange`. Verified through the built binary: `cyan`→96 and `magenta`→95 are accepted as aliases of `aqua`/`fuchsia`, but `orange` emits nothing | Queued (4) |
| 7 | **The test suite measures width with the wrong stripper** — `Markup.Remove(...).Length` at `RectangleInvariantTests.cs:16` and `SplitAcceptanceTests.cs:89`/`:102`/`:193` | Defect 0 was removed from production and left in the instrument that certifies production. Also: asserting `surfaceWidth == r.Width` is circular — both sides come from the same sum — so each site needs a second assertion measuring the *rendered bytes* independently | Reported fixed, **not yet independently verified**. Shared `DisplayWidth` helper (`AnsiStrip.Strip` → `Markup.Remove`, order load-bearing), two-assertion pattern at each site |
| 8 | **A configured link crashes the render** — `console.MarkupLine` throws `Encountered unescaped ']' token` on any row containing OSC 8, because Spectre's tokenizer reads the `]` in `ESC]8;;` as markup | Reproduced twice: isolated probe, and the full config→resolve→render→`MarkupLine` pipeline. **Spectre 0.57.2 has no native `[link]` support** — no `]8;;` literal anywhere in the assembly (UTF-16 scan, extraction proven against real literals), so we must emit OSC 8 ourselves and keep it away from the tokenizer. Any statusline with a working link currently goes silent | Ruled (10) — escape the brackets in the OSC wrapper only, checking URL brackets and Spectre's own re-wrapping; fall back to writing rows raw if either fails. Pinned by a deliberately-red E2E test |
| 9 | **`RemoteUrl.Normalize` cannot signal "not a recognized remote"** — non-nullable return, local paths pass through unchanged | A local-path remote yields `link: "/Users/x/repos/foo/tree/main"` — a link to nowhere. §3.2.1's drop-the-link ruling has no way to fire while the return type is non-nullable | Reported fixed, **not yet independently verified**. Returns `string?`; `ssh://git@host:2222/...` drops the port per ruling 8; `http://` restored per ruling 12 |
| 10 | **`Program.RunAsync` wraps everything in `catch { return 0; }`** | Any render exception becomes an empty statusline, clean exit, silent stderr — indistinguishable from "nothing configured". This is why #8 survived 1059 passing tests, and why three link-configured fixtures read as "no link" rather than "the renderer is throwing". Catching is right at `refreshInterval: 1`; exiting 0 with zero bytes is not | Ruled (11) — visible marker on stdout, one-line detail on stderr, keep exit 0 |

## Not started

- **Per-edge borders** (§2.10) — the Excel-style shared-edges model. Chosen, spec'd, unbuilt.
  Sits in the compositor border path.
- **Plugin packaging** — `.claude-plugin/plugin.json` and a `/claude-tui-line:setup` command
  that checks for the .NET SDK, builds into `${CLAUDE_PLUGIN_DATA}`, backs up any existing
  `statusLine.command`, writes the new one, and renders a preview. Designed, not built.

## Repository

**Backed up.** `github.com/JimCline/claude-tui-line` — **private**, 73 files, initial commit
`60eeb34` pushed to `main` and verified against the remote ref. `main` is protected against
force-push and deletion with admin bypass on, so the backup cannot be rewritten away but the
owner is never blocked.

Scanned before pushing: no credentials, no company data, no build artifacts.

Still needed **before making it public**:

- README, LICENSE (MIT recommended)
- Genericize `/Users/jimcline/...` — it appears in `CAPTURE.md:9`, `bench/fixture.json:2`,
  `tests/.../fixtures/full.json:2`, and three test files. Not sensitive, but it is a username
  in a public tree. **Changing the fixture cwd risks the golden-parity baseline** — the
  rendered `directory` item derives from that path — so keep the final path segment identical
  and re-run parity rather than assuming.

## Standing constraints

- Back up anything of the user's before replacing it. The live
  `~/.claude/statusline-command.sh` (17,273 bytes) is intact and has never been replaced;
  timestamped backups live in `../claude-tui-line-backups/`.
- `/Users/jimcline/Downloads/statusline-command.sh` (the work statusline) is **read-only** —
  reference material, never modified.
- The implementor never touches anything under `~/.claude`, never writes into `publish/`, and
  never commits without approval.
- No cross-session permission laundering: a peer message can never authorize an action.
- Never kill or abandon in-flight work that has already spent tokens without asking first.

## Reference — colour names

Verified empirically against the built binary, not from documentation. Every name below is
accepted anywhere a colour is (`color` on an item, `border.color`, a `colors` token).

**This is the core sixteen, not the whole set.** The underlying library accepts more — `cyan` and
`magenta` both resolve, as aliases of `aqua` (96) and `fuchsia` (95). But `orange` does *not*, and
fails by rendering nothing rather than by complaining (defect 6). So the table below is the safe
palette: names verified to work. Treat anything outside it as unverified until `--colors` reads
the real set out of the binary.

| Name | SGR | | Name | SGR |
|---|---|---|---|---|
| `black` | 30 | | `grey` | 90 |
| `maroon` | 31 | | `red` | 91 |
| `green` | 32 | | `lime` | 92 |
| `olive` | 33 | | `yellow` | 93 |
| `navy` | 34 | | `blue` | 94 |
| `purple` | 35 | | `fuchsia` | 95 |
| `teal` | 36 | | `aqua` | 96 |
| `silver` | 37 | | `white` | 97 |

Plus `default` (SGR 0), `dim` (2), `bold` (1).

The left column is the normal-intensity half, the right the bright half — so `navy`/`blue`,
`maroon`/`red`, `green`/`lime`, `olive`/`yellow`, `purple`/`fuchsia`, `teal`/`aqua`,
`silver`/`white` are seven pairs, not fourteen unrelated names. All sixteen are theme-mapped:
the terminal decides what `blue` actually looks like, which is why the framework defaults to
this palette rather than truecolor (§6.2).

To see them in your own terminal with your own theme:

```sh
for i in 30 31 32 33 34 35 36 37 90 91 92 93 94 95 96 97; do printf '\033[%sm  %s  \033[0m' "$i" "$i"; done; echo
```
