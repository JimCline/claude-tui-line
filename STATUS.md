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
| Sizing | **`distribute: "min-rows"`** (§2.3 / §2.3.1) — optimal pane sizing by searching the achievable row count `T`, not the allocation | Independent build+test: exit 0, 0 warnings, 1067/1067. Carries a brute-force optimality oracle at `COLUMNS=112` and `60`, so the green suite is a behavioural check on the allocator. p90 `0.073ms` across widths 100–240 vs a 12.6ms budget; 45 packer calls on the live config |

## In flight

- **Phase 5 CLI** (§9) — with the implementor, and now the **critical path**: §12.1 makes the
  binary the oracle for every authoring command, so Phase 6 below is blocked on it entirely.
  Ordered `--check` → `--items` → `--preview` → `--colors`, and the ordering is reasoned:
  `--check` first because §7 makes a bad config *silent*, so it is the only one that prevents
  damage rather than adding convenience. It reports by **JSON Pointer** and must **reuse
  `ReferenceExtractors`** rather than growing a second config walk — a checker that passes while
  the resolver drops an id is defect 11 re-opened somewhere new and equally silent. `--check`
  is also where the silent-acceptance defects 3–6 become visible without changing runtime
  behaviour. `--preview` needs `--config <path>` so a candidate can be seen **without being
  installed**, which is what lets migrate show a result before writing. **`--version` was added
  to this scope (§9.7)** after a cross-reference check found §12.6 surfacing a `cliVersion` that
  §9 had never defined — the implementor had no way to know the field was expected. It brings a
  test with it: the version lives in both the `.csproj` and the hand-written `plugin.json`, which
  cannot be generated, so an assertion that the two match is the only thing standing between that
  drift and a user reporting a version corresponding to nothing. **§9.8 settles what "fixed sizes
  cannot fit the parent" means**, raised by the implementor rather than found by me: `--check`
  consults no width at all, because §12.6's `validate` calls it from an MCP server with no
  terminal, and because degrading at a narrow width is what the §2 ladder is *for*. The invariant
  is a contradiction no width can resolve — fixed children against a bounded parent, `minSize`
  above `maxSize` — and where the parent is `fill`/`content` it says nothing. The width-dependent
  finding is real but belongs to `--preview`, as a note that never touches the exit code.
- **Phase 6 authoring commands** (§12.3–12.5) — `migrate`, `edit`, `revert` **written and
  committed, blocked on §9**. Each opens by asking the binary for `--items`, and **stops** if the
  CLI is absent rather than falling back to a list written from memory: §12.1 forbids that
  because a prompt-file item list is a second registry, and a remembered id that no longer exists
  resolves to nothing and is silently suppressed. The README says plainly that these three do not
  work yet. **Not verified end-to-end** — nothing can be until §9 exists.
- **The backup ledger** (§12.2) — `docs/backup-ledger.md`, one definition shared by all four
  commands. A first draft of these commands used timestamped `settings.json.backup-*` copies and
  had exactly the bug §12.2 exists to prevent: migrate → edit → migrate again captures
  *claude-tui-line's own* command as the thing to restore, so revert restores the tool the user is
  escaping, and the escape hatch closes as it becomes needed. Replaced with the `origin` /
  `checkpoint` distinction, `origin` written exactly once ever, SHA-256 recorded per artifact and
  a mismatch **reported rather than overwritten**. Also unexercised: no command has run against a
  real ledger yet.

- ~~**Defects 11 and 13**~~ — **both fixed, committed as `5ed9ccb` and pushed.** Verified
  independently of the report claiming it: clean Release build, three consecutive full-suite runs
  at **1069/1069**, and both fixes confirmed present in the tree (`[ThreadStatic]` at
  `SizeResolver.cs:332`, the non-parallel collection at `MinRowsDistributeTests.cs:12`) rather
  than merely reported. Isolated, the true packer count is **14** and stable across 8 runs — so
  the 45 this file recorded as a baseline was itself contaminated, just less than the 314 was.
  Defect 11's fix: `CollectIds` restructured into a
  `ReferenceExtractors` array with one lambda per §5 reference form, so adding §4.2's argv
  placeholder or §3.3's compound parts is an append rather than an edit — that array is now the
  single definition of what ids a config references, and **`--check` must reuse it rather than
  grow a second config walk**. Colour-token `from` was checked and is *not* affected, closing the
  question STATUS previously left open at defect 11: the link resolver was the only broken form.
  One consequence of defect 11 still wants a spec decision: a `link` naming `{remote-url}` now
  genuinely resolves `remote-url`, which shells out to git. That is the fix working as intended,
  but it makes an item that is opt-in *for cost reasons* reachable without the user placing it.
  §3.2 may need to say so.

Notes carried forward from `min-rows`, now in **Done and verified**: implementation is
`PaneDistribute` → `ResolveVerticalMinRows` → `SolveMinRows` → `MinWidthForRowCount` →
`WaterFillSurplus`, and `RowCountAt` calls the real packer rather than a re-derived twin — the
property that makes the search trustworthy. Its latency test measures the allocator in-process
rather than through `bench.sh`, deliberately: §2.3.1 condition 2 measures the allocator, §10
item 10 measures the binary, and routing through `bench.sh` would need a published artifact.
**`R = 108`, not the 109 the worked example claimed** — implemented against the formula, prose
flagged rather than silently reconciled; §2.3.1 is now corrected.

Recently landed, pending the last of its per-defect checks:

- **OSC 8 hyperlinks** (§3.2, §3.2.1) — committed as `1028f5d` and pushed to `main`, remote ref
  confirmed. Brings `remote-url`, derived items (`from` / `extract` / `case`), the `ItemContext`
  refactor (§3), one shared `AnsiStrip`, and `OscHyperlink.EscapeForRender`. 1063/1063 tests;
  Native AOT publish independently rebuilt to a matching SHA-256
  `4860066b…54e4a4`. **That verifies the artifact matches the source, not that each defect's fix
  behaves** — defects 1, 7, 8, 9 and 10 stay open below until checked against rendered bytes
  from the built binary.

## Queued, in order

1. **Phase 5 CLI** (§9) — `--check` (with `--json`), `--preview`, `--items`, `--colors`,
   `--version`.
2. **Phase 6 authoring surface** (§12) — backup ledger **first**, then `migrate`, `revert`,
   `edit`.
3. **Config diagnostics** — see the open defects below. Defect 11's fix is §5's resolution set,
   which items 4 and 5 depend on.
4. **`{item-id}` placeholders in a `command` item's argv** (§4.2) — hand a framework-resolved
   value to a user's own script without re-deriving it. Reuses §3.2's link-template resolver
   rather than adding a syntax. **argv-only expansion**; under `shell: true` the values go to
   the environment as `CLAUDE_TUI_LINE_VAL_<ID>` instead, because substituting into a `sh -c`
   string is command injection and a branch name is attacker-influenceable. **Ordered after
   defect 11 deliberately** — both are the same root cause (§5 enumerating displayed items
   rather than referenced ones), and building this first would reproduce defect 11 in a second
   place with the same silence.
5. **Compound items** (§3.3) — `parts`, so one item can hold several sources with a colour each
   and **no separator between them**: a dim `agent:` label against an aqua value, which is
   impossible today because `color` paints the whole item and splitting the label into its own
   item inserts ` | `. Not a new render path — a compound produces the same one `Segment` with
   multiple styled spans that builtins already use for `ctx:62% (125k/200k)`, and §4.1's
   `match` + `colors` must compile to that same span list rather than a parallel one. Depends on
   item 3 for the same reason item 4 does: a part's `item` / `from` is the sixth way to name an
   item by id, and §5's set has to enumerate it. New hazard needing its own test: `truncate`
   cutting mid-span must close the SGR or colour bleeds into the border.
6. **`maxRows` degrade ladder** (§2.8).
7. **Phase 7 MCP server** (§12.6) — ambient access, so "make the border green" works mid-
   conversation without the user knowing a slash command exists. Seven tools, **read/write**:
   `list_items`, `list_colors`, `get_config`, `set_config`, `validate`, `preview`, `revert` —
   enough for the model to carry a request from words to a rendered statusline unaided.
   `set_config` validates before it commits and never writes a config that fails `--check`;
   `preview` returns rendered rows so the model checks its work by looking rather than by
   asserting. **Deliberately last**: it wraps the CLI, so it cannot be designed before the CLI
   exists. Stateless, and the renderer stays a one-shot AOT binary regardless.
   **Spec now carries the wire contract** (§12.6.1–12.6.8): per-tool arguments and returns;
   failures as readable results rather than JSON-RPC protocol errors, with `--check` diagnostics
   passed through unflattened; the ruling that the server's environment is *not* the user's shell,
   so §5's search order can silently resolve a different file — hence `configPath`/`source` on
   every read and an explicit `configPath` override on every tool; `preview` never inferring a
   width; unconfirmed `revert` returning the ledger instead of acting; compare-and-swap on
   `set_config` so an interleaved hand edit is refused rather than clobbered; the three files a
   tool may write and no others. Nothing here is implemented — it is unblocked design ahead of
   the CLI landing.

## Open defects

| # | Defect | Impact | Status |
|---|---|---|---|
| 0 | **Width is derived by parsing markup, not from `Plain`** — `Markup.Remove(...).Length` at three sites | Violates the invariant the layout rests on. `Markup.Remove` strips Spectre tags, not ANSI, so any row carrying raw escapes measures long and the border lands early — silently. Genuinely pre-existing | **Fixed and verified.** `PaneRow(Markup, Width)` threads measured width through the pipeline; the dead `MeasureRow`/`FromMarkupRows` deleted. Confirmed by independent rebuild, SHA `55baa073…59ac9` |
| 1 | OSC 8 hyperlinks are counted as visible text | Border lands ~50 columns early; row goes ragged. Reproduced: `sgr-stripped=77, sgr+osc-stripped=27, budget=77, RAGGED` at `COLUMNS=80` | **Fixed and verified.** A linked row at `COLUMNS=112` pads out to the right border exactly as an unlinked one does — the OSC bytes contribute no width. Read from rendered bytes of the built binary, two configs (`git-branch`, `directory`) |
| 2 | `surface.maxRows` is entirely unenforced | 8 rows emitted at `COLUMNS=112` and 14 at `COLUMNS=60` against a configured 6 | Queued (5) |
| 3 | `ConfigLoader.TryReadConfig` swallows a malformed config | Exit 0, zero bytes on stderr, a completely different statusline renders. A JSON typo gives the user nothing to debug against | Queued (4) |
| 4 | `"auto"` and any unrecognized `size` silently resolve to `fill` | Same silent-acceptance class as #3 | Queued (4) |
| 5 | Unrecognized `case` value passes through unchanged | Same class; deliberately deferred to (4) rather than special-cased | Queued (4) |
| 6 | **An unrecognized colour name silently renders uncoloured** — `"color": "orange"` gives exit 0, empty stderr, and no SGR at all | Same silent-acceptance class as #3–#5, and the one most likely to be hit by a model authoring config (§4.1), which will reach for plausible names like `orange`. Verified through the built binary: `cyan`→96 and `magenta`→95 are accepted as aliases of `aqua`/`fuchsia`, but `orange` emits nothing | Queued (4) |
| 7 | **The test suite measures width with the wrong stripper** — `Markup.Remove(...).Length` at `RectangleInvariantTests.cs:16` and `SplitAcceptanceTests.cs:89`/`:102`/`:193` | Defect 0 was removed from production and left in the instrument that certifies production. Also: asserting `surfaceWidth == r.Width` is circular — both sides come from the same sum — so each site needs a second assertion measuring the *rendered bytes* independently | Reported fixed, **not yet independently verified**. Shared `DisplayWidth` helper (`AnsiStrip.Strip` → `Markup.Remove`, order load-bearing), two-assertion pattern at each site |
| 8 | **A configured link crashes the render** — `console.MarkupLine` throws `Encountered unescaped ']' token` on any row containing OSC 8, because Spectre's tokenizer reads the `]` in `ESC]8;;` as markup | Reproduced twice: isolated probe, and the full config→resolve→render→`MarkupLine` pipeline. **Spectre 0.57.2 has no native `[link]` support** — no `]8;;` literal anywhere in the assembly (UTF-16 scan, extraction proven against real literals), so we must emit OSC 8 ourselves and keep it away from the tokenizer. Any statusline with a working link currently goes silent | **Fixed and verified.** `EscapeForRender` at the three output sites. A configured link now renders `ESC]8;;<url> ESC\ <styled text> ESC]8;; ESC\` — correct URL, correct ST terminator, correct close — with exit 0 and empty stderr. Read from rendered bytes of the built binary |
| 9 | **`RemoteUrl.Normalize` cannot signal "not a recognized remote"** — non-nullable return, local paths pass through unchanged | A local-path remote yields `link: "/Users/x/repos/foo/tree/main"` — a link to nowhere. §3.2.1's drop-the-link ruling has no way to fire while the return type is non-nullable | Reported fixed, **not yet independently verified**. Returns `string?`; `ssh://git@host:2222/...` drops the port per ruling 8; `http://` restored per ruling 12 |
| 10 | **`Program.RunAsync` wraps everything in `catch { return 0; }`** | Any render exception becomes an empty statusline, clean exit, silent stderr — indistinguishable from "nothing configured". This is why #8 survived 1059 passing tests, and why three link-configured fixtures read as "no link" rather than "the renderer is throwing". Catching is right at `refreshInterval: 1`; exiting 0 with zero bytes is not | Ruled (11) — visible marker on stdout, one-line detail on stderr, keep exit 0 |

| 11 | **`{other-id}` in a `link` template resolves only when that item is also placed in a pane** | §3.2's own worked example, `{ "item": "git-branch", "link": "{remote-url}/tree/{}" }`, produces **no link at all**. The spec says the registry resolves these and forbids "a second lookup mechanism"; the code reads a map of already-rendered items, which is that mechanism. Lands on the primary use case — `remote-url` is referenced precisely so it need *not* be displayed. Fails silently and identically to a typo'd id, so the two are indistinguishable | Verified through the built binary, 5 discriminating configs: `{}` works alone (`.../x/main`, `.../d/claude-tui-line`); `{remote-url}` fails unplaced, **succeeds when placed** (`https://github.com/JimCline/claude-tui-line/tree/main`); `{nosuchitem}` fails identically. Queued behind `min-rows`. **Scope narrowed:** a derived item's
`from` is *not* affected — `{"id":"agent-short","from":"agent",...}` renders `CDTUI` with `agent`
nowhere in the config (fixture `{"agent":{"name":"cdtui-implementor"}}`, COLUMNS=100). So the
defect is the link resolver specifically, and whatever mechanism makes derived `from` work
unplaced is a model to copy rather than replace. Colour-token `from` untested — check before
assuming which side it lands on |
| 12 | **An empty pane still renders its borders** | `{"items":[{"item":"repo"}]}` in this repo emits 674 bytes — top and bottom border, no content row. Collides with SPEC.md:353 *"no segments ⇒ zero output even with border enabled."* Separately, `repo` yielding nothing here may be correct de-duplication (repo name and directory name are both `claude-tui-line`) — that part is unconfirmed **Ruled** — §2.4 now carries it. SPEC.md:353 survives, applied at two levels: an empty *surface* emits zero bytes; an empty `content`/`fill` pane collapses with its gutter; an empty `fixed`/`percent` pane keeps extent and border, because the user named a number and §2.3's principle of not overruling explicit sizing applies here too. Queued behind defect 11. Whether `repo` yielding nothing here is correct de-duplication is still unconfirmed and tracked separately |
| 13 | **`Columns112_LiveConfig_PackerInvocationCountStaysBounded` reads a racy process-global counter** — it reported 314 packer invocations against a `1..300` bound where a clean tree reported 45 | **Diagnosed: a test defect, not a sizer regression — there was never a 45 → 314 jump to explain.** `MinRowsPackerInvocationCount` (`SizeResolver.cs:324`) is a plain `static int`, incremented un-interlocked at `:501` by *every* min-rows render in the assembly; the test zeroes it at `MinRowsDistributeTests.cs:153` and reads it at `:155`. The repo has no `xunit.runner.json`, no `[CollectionDefinition]` and no `DisableTestParallelization`, so xUnit's default holds and test classes run **in parallel** — the test measures its own packer calls *plus whatever else was in flight*. 45 was a quiet scheduling window, 314 a busy one; defect 11's two new end-to-end `HyperlinkTests` perturbed the schedule, which is why it surfaced then. Being un-interlocked it can under-count as well as over-count | Diagnosed here, fix with the implementor: mark the counter `[ThreadStatic]` — sound *because* §5 resolve-once-per-render makes `SolveMinRows` synchronous, so the thread that zeroed the counter is the thread that reads it, and that precondition goes in a comment — plus a non-parallel `[CollectionDefinition]` for the instrumented class as belt-and-braces |

**The OSC-8-in-the-sizer hypothesis was wrong and is closed.** It predicted the sizing path counted escape bytes as width, which would have made the *layout* wrong rather than merely slow. Tested directly: two configs identical except one item carrying `link`, rendered through the built binary at COLUMNS 112, 90 and 70, compared byte-for-byte after stripping OSC 8 then CSI (that order — stripping CSI first leaves the URI payload looking like text). **Identical at all three widths**: same row counts, same pane widths, same bytes. Defect 1's fix holds in the sizing path as well as the render path. Recorded because the reasoning was sound and the conclusion still false — `RowCountAt` → `PaneRenderer.RenderLeaf` genuinely is a caller defect 1 was never checked against; that just wasn't where this went wrong.

Noted for later, filed rather than done: `SolveMinRows` scans `t` linearly from 1 to `maxT` while its own comment states `feasible(T)` is monotone — the property that licenses bisection. And `RowCountAt` rebuilds `CandidateSegments` on every probe, though §5 guarantees values are constant for the render, so `rows_i(w)` is pure within a render and every repeat probe is provably redundant. Both were embargoed while defect 13 looked like a regression they could mask; with no regression to mask **the embargo is lifted**. They remain optimizations rather than fixes, so they land as their own change with their own before/after numbers — not folded into defect 11.

## Not started

- **Per-edge borders** (§2.10) — Excel-style per-edge selection, the 16-entry junction table, and
  the `reserve(p)` decomposition. Spec'd, unbuilt. Sits in the compositor border path.
- **`border: { "collapse": ... }`** (§2.10) — both visual languages, not one. `false` (**default**,
  and what ships today) is separate boxes; `true` collapses adjacent edges to one shared line.
  The payoff is width: a separate boundary spends `gutter + 2` columns, a collapsed one spends 1,
  so every interior boundary hands back `gutter + 1` columns. Default stays `false` because
  changing an existing config's visual language on upgrade is not a framework's call, and because
  `true` is the mode that needs the colour/style tie-break rule.
- **`height: "content"`** (§2.8) — a pane's border box closes under its last content row instead
  of filling its band, so a 2-row pane beside a 3-row one stops drawing a blank row inside its
  border. `valign` gains a second subject (it places the box in the band rather than the content
  in the box); no new knob. **Ships independently against the default `collapse: false`** — with
  separate boxes a short box introduces no new glyph case, since the neighbour's edges were never
  shared. Only its collapsed-mode junctions need the border grid. Does *not* reduce total rows;
  that is `distribute: "min-rows"`, already shipped.
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

- ~~README, LICENSE~~ — **done.** `LICENSE` is MIT. `README.md` documents install, the config
  file and its lookup order, panes and every pane key, all sixteen built-in items, custom
  `command` items, derived items, colours and colour rules, and hyperlinks. Every string value in
  it was read out of the parsers rather than assumed — `align`/`valign`, `size` forms, `overflow`,
  border `style`, the env var, and the published binary name (`claude-tui-line`, from
  `AssemblyName`). It documents only what exists: no CLI flags are described, because `Program.cs`
  has none yet. The silent-acceptance defects (3–6) are disclosed in a note rather than papered
  over.
- ~~Plugin scaffolding~~ — **done, but unexercised.** `.claude-plugin/plugin.json` and
  `.claude-plugin/marketplace.json` (the plugin *is* the repo root, so its marketplace entry uses
  `"source": "./"`), plus `commands/setup.md` at the plugin root — **not** inside
  `.claude-plugin/`, which holds only the two manifests. Both schemas were confirmed against the
  Claude Code docs rather than guessed. `/claude-tui-line:setup` checks for .NET 10, publishes
  into `${CLAUDE_PLUGIN_DATA}/bin`, **backs up `settings.json` and any script it points at before
  writing anything and stops if the backup fails**, edits `settings.json` in place preserving
  other keys, then renders a preview. **Structurally verified, not installed end-to-end.** Both
  manifests parse as JSON; `marketplace.json` carries the required `name`, `owner.name`, and
  `plugins[]` with `source: "./"`; `commands/` sits at the plugin root with all four command
  files, and `.claude-plugin/` holds only the two manifests. Every relative path the README
  points at exists, `tools/colors.sh` and `docs/backup-ledger.md` included. What remains
  unverified is the part only a real install exercises — `/plugin marketplace add` against this
  repo cannot be tried until it is public.
- ~~Genericize the hardcoded home paths~~ — **done.** The fixture cwd is now
  `/Users/example/git/repos/claude-tui-line` across `bench/fixture.json:2`,
  `tests/.../fixtures/full.json:2`, and three test files; `CAPTURE.md:9` likewise. The
  substitution preserved both the path depth and the final segment, because the rendered
  `directory` item derives from that path and a shorter path would have moved the golden-parity
  baseline. Verified rather than assumed: full suite 1069/1069, exit 0, parity gate included.

## Session log — overnight, 13→14 Aug 2026

All design, no implementation: the implementor holds `src/` for §9, so I stayed out of it. Nine
commits, all pushed.

- **Genericized the hardcoded home path** (`6dcb68f`) — the last pre-public item. Preserved path
  depth and final segment so the golden-parity baseline could not move; suite 1069/1069.
- **Specified the MCP wire contract**, §12.6.1–12.6.8 (`d18b9b8`). Phase 7 was the last surface
  not specified to §9's depth. Four hazards ruled on, the sharpest being that an MCP stdio server
  does not inherit the user's shell environment — so §5's search order resolves a *different file*
  in each place, and nothing errors.
- **Added `--version`, §9.7** (`f14cd4b`). Found by checking cross-references: §12.6 surfaced a
  `cliVersion` that §9 never defined. Brings a test, because the number lives in both the `.csproj`
  and a hand-written `plugin.json` that cannot be generated.
- **Ruled `--check` width-independent, §9.8** (`dff1fdc`) — raised by the implementor, who was
  right that no existing invariant matched. Decisive argument: §12.6's `validate` calls `--check`
  from a process with no terminal.
- **Resolved a §9.4/§9.8 contradiction** (`eb42fe8`) — also the implementor's catch. `minSize >
  maxSize` is an `error`. The severity discriminator had been left implicit and was therefore
  re-derivable to opposite conclusions; it is now stated: **satisfiable vs unsatisfiable, not
  "does the renderer cope."** Coping cannot be the test, because §7 makes the renderer cope with
  everything.
- **Fixed a poisoned-`origin` defect in the backup ledger** (`cb9f2bb`) — the most serious find of
  the night, in the one component whose entire purpose is being trustworthy. Writing an `origin`
  because none exists yet is insufficient; a user who hand-wires the binary and *then* runs setup
  gets the tool recorded as its own escape hatch, permanently. Only `setup.md` was exposed —
  `migrate.md` already guarded the case for an unrelated reason, which is why it hid.
- **Fixed `migrate.md` ignoring `$CLAUDE_TUI_LINE_CONFIG`** (`dff1fdc`) and a stale backup-naming
  instruction in `setup.md` (`6dcb68f`), both found by re-reading committed prompts rather than
  trusting them.
- **Verified the plugin scaffolding structurally**; STATUS now says what was actually checked
  rather than "parses by inspection."

A grep sweep of every flag and diagnostic code in the spec found no other dangling references. The
two real inconsistencies both came from the implementor trying to build two sections at once —
which is the argument for having a separate pair of eyes on the spec, not just a reader.

## Standing constraints

- Back up anything of the user's before replacing it. The live
  `~/.claude/statusline-command.sh` (17,273 bytes) is intact and has never been replaced;
  timestamped backups live in `../claude-tui-line-backups/`.
- The user's work statusline under `~/Downloads/` is **read-only** — reference material for the
  hyperlink work (§3.2), never modified.
- The implementor never touches anything under `~/.claude`, never writes into `publish/`, and
  never commits without approval. `publish/` is the deploy target the live statusline executes;
  builds for verification go to the SDK-default output under `src/ClaudeTuiLine/bin/Release/`.
  SPEC.md §10.2 carries the full reconciliation — it directs one command at `publish/`, and that
  command is a deploy, not a build.
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
