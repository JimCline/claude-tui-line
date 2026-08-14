# claude-tui-line — status

Running progress against `SPEC-V2-FRAMEWORK.md`. Updated as work lands.

**Last updated:** 2026-08-14

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
  work yet. **Not verified end-to-end** — nothing can be until §9 exists. **Walked over cases on
  14 Aug and six defects fixed**, one of them severe: `/edit`'s checkpoint did not contain the file
  `/edit` modifies, so its rollback path was a no-op that reported success. See the session-log
  entry below; the prose is fixed, still unexercised.
- **The backup ledger** (§12.2) — `docs/backup-ledger.md`, one definition shared by all four
  commands. A first draft of these commands used timestamped `settings.json.backup-*` copies and
  had exactly the bug §12.2 exists to prevent: migrate → edit → migrate again captures
  *claude-tui-line's own* command as the thing to restore, so revert restores the tool the user is
  escaping, and the escape hatch closes as it becomes needed. Replaced with the `origin` /
  `checkpoint` distinction, `origin` written exactly once ever, SHA-256 recorded per artifact and
  a mismatch **reported rather than overwritten**. Also unexercised: no command has run against a
  real ledger yet. **Amended 14 Aug:** an entry now carries `claude-tui-line.json` as a third
  artifact alongside `settings.json` and the user's script, recorded whenever a config exists
  rather than when the running command intends to change it — without it `/edit` had no
  recoverable backup at all. The hash-check rule was also wrong in a way that would have made
  `revert` refuse on every run; both are in the session log.

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
| 14 | **`shell: true` with a multi-element argv silently drops every argument after the first** | `CommandProvider.RunAsync:74` passes `command[0]` and nothing else to `sh -c`, so `{"command":["kubectl","config","current-context"],"shell":true}` runs `sh -c "kubectl"` and renders bare kubectl's usage text. **The only defect found so far that renders *wrong output* rather than nothing** — §7's contract is "bad config yields nothing at render, `--check` says why", and this escapes that pair entirely: the user gets no signal, not even an absence to notice. Cargo-culting `shell: true` onto a working argv array is an easy way to reach it | Ruled, both halves specified in §4.1. Render: suppress the item instead of spawning. Check: `error`, code `command-shell-argv`, fires only at `count > 1` (a single-element array is what the string form normalizes to, so it is correct and must not be reported). Found while ruling on the implementor's `command-shape` question — not by looking for it |
| 15 | **Border colour resolves two different ways, and the two do not accept the same language** | `PaneTreeRenderer.cs:76` calls `ColorResolution.Resolve` and gets a **spec string** used as a markup tag (fallback `"grey"`); `Program.cs:145` calls `ResolveBorderColor` → `ResolveLiteral` and gets a Spectre **`Color`** via `Style.TryParse(...).Foreground` (fallback `Color.Grey`). Both are live; which runs depends on the shape of the user's config. A markup tag carries decorations and a `Color` cannot, so `dim`/`bold` — documented in §6.1, accepted by `Style.TryParse` — are **predicted** to render on the tree path and vanish on the single-pane path, with no diagnostic, because both paths *succeed*. Item colour has no such split (`PaneAssembler.cs:66` uses `Resolve`), which is why it stayed invisible. Directly contradicts §6.5's own closing line, "one resolution point beats two" | Ruled in §6.6, task #15. `Resolve` becomes the sole border resolver; `ResolveBorderColor` survives as a thin adapter but returns a **`Style`**, not a `Color` — `ResolveLiteral`'s return type is the actual bug. **Step 1 is verifying the symptom**, not fixing: that `Style.TryParse("dim")` yields `Foreground == Color.Default` is inferred from the signature, never observed, and must not enter a test as an assumption. Found by asking what `--colors` may print, then listing `ResolveLiteral`'s callers — nobody was auditing borders |

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

All design, no implementation: the implementor holds `src/` for §9, so I stayed out of it. Twelve
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

- **Ran a consistency pass over every unbuilt section**, prompted by the finding that §9 was the
  deepest section in the spec and still contradicted itself twice. §2.10 and §3.3 first
  (`7e157c0`, `1a59cbe`), then §4.2 (`1c89d8c`), which was the last one unchecked. §4.2 turned up
  three things: reject conditions with no diagnostic codes, the same omission §3.3 had; an
  apparent §3.2.1 contradiction that resolved into a real distinction — a dangling `{other-id}` is
  a *warning* in a `link` and an *error* in a `command` item's argv, because §3.2.1 defines what a
  dropped link does and nothing defines what an unexpanded argv entry becomes; and a new
  `placeholder-env-collision`, since `CLAUDE_TUI_LINE_VAL_<ID>` mangling is many-to-one and
  `agent-short` and `agent.short` both land on `AGENT_SHORT`. The same commit fixed §9.8's third
  bullet, which still charged gutters and border reserve as independent addends — the double-count
  `7e157c0` had already corrected in its first bullet.

**Every section of the spec has now been consistency-checked.** A grep sweep of every flag and
diagnostic code found no dangling references, but grep was never going to be enough: none of the
inconsistencies it missed were dangling identifiers. They were two sections defining one condition
with different severities, and one formula corrected at one of its two use sites. Two came from the
implementor building two sections at once, which is the argument for a separate pair of eyes on the
spec rather than just a reader.

### Second batch — the consistency pass turning up real defects

The pass stopped being bookkeeping and started finding things.

- **§5.1, `remote-url` caching** (`8ee5744`) — closes the last unspecified queued item. It reuses
  `ItemCache` rather than getting its own store. The ruling that bites: a probe that finds nothing
  is a *cached result*, not a cache miss, or git gets re-spawned every render in exactly the case
  where the spawn can never return anything.
- **§2.11, empty-pane collapse** (`d506075`, corrected in `28c1bd2`) — defect 12. "Empty" has two
  causes and only one is safe to act on. Collapsing a pane that the *sizing loop itself* emptied
  frees width, which un-drops the items, which un-empties the pane, which reclaims the width — a
  cycle with no fixpoint, breaking §2.3's convergence argument. I also had to retract my own
  fixed-size bullet: §2.4 already ruled the opposite and its reasoning was better than mine.
- **§9.4.1, the severity test** (`ac1c959`, then `fdad2ac`) — and this one is the cautionary tale
  of the night. The first version contradicted the paragraph three lines below it: applied
  strictly, "delivered vs deleted" demotes every typo to a warning, because an unknown `size`
  falls back to `fill` and renders fine. Two tiers now — *not in the language* is always an error,
  and only *in the language with a missing referent* gets judged by consequence. **A section can
  contradict itself within its own body**, which is a shorter distance than any of the other
  inconsistencies found this session had to travel.
- **§2.10's `collapse: false` formula was arithmetically wrong** (`28c1bd2`), and this is the one
  that would have shipped. It charged `(N−1)×(g+2)`, but `N` separate boxes own `2N` edge columns
  and that expression covers `2(N−1)` — it drops the outermost pair and under-reserves by 2 at
  every `N`. Found because the implementor asked which of two arithmetics to build `--check`
  against instead of picking one. The durable fix is that **nothing restates the arithmetic,
  including this document**: one named function, called by both the renderer and the checker.
- **One condition, four positions.** A dangling `link` was an error in §9.4, a graceful drop in
  §3.2.1, "rejected" in §3.2, and a warning in the §4.2 text I wrote myself. All four now say
  warning. A third severity — "notice" — was also in §6.2 by accident, in a system §9.4 defines as
  having exactly two.

### Third batch — a stale defect, and the verification rule catching a *retrieval*

- **§2.8's "the second pass currently frees nothing, and that is a live defect" was stale.** The
  fix landed and the prose never caught up. `ResolveVertical` re-measures *with* the grant
  (`SizeResolver.cs:129`), `MeasureRequest` turns it into an inner cap and returns
  `LongestWrappedRowWidth` rather than the cap, `:130` clamps monotonically against the previous
  request, and `:141` reallocates from the reduced set, bounded at `MaxPasses = 3`. So the loop
  can once again produce a shrinking re-measurement — the thing the banner-to-text deletion had
  removed the only source of. §2.8 now says so.
  - **Still owed, and now recorded rather than assumed:** §10.6's fixpoint tests drive the loop
    through `measureOverride`, so they certify the monotone clamp and the pass cap **against a
    stub**. Nothing yet asserts that the *real* measurement frees six columns at `COLUMNS=112`.
    A defect can be fixed and its test still be measuring something else.

- **A retrieval subagent fabricated a spec section, and the fabrication was caught by content
  rather than by suspicion.** Asked to print §9.6 verbatim, it returned ~15 lines of fluent,
  correctly-formatted prose describing a "handshake", a `tools` table, and the root output of a
  `_parse_file` call — none of which exist anywhere in this project. The real §9.6 is 26 lines
  about `--json` shapes. The tell was not that the output looked wrong; it read perfectly well.
  The tell was that it described a protocol this tool does not have.

  This extends the rule at the top of this file. "Verified independently of the report claiming
  it" was written for *implementation* reports — a claim that code works. It turns out to be
  needed just as much for *retrieval* reports, where the failure is quieter: a fabricated file
  quote has no build to fail and no test to go red, and it would have been laundered into the
  spec as a real inconsistency to go and fix. **Prose that will be quoted or edited gets read
  directly; delegated retrieval is for locating and counting, not for transcribing.**

  Worth noting what did *not* catch it: two runners independently agreeing on line numbers in
  `SizeResolver.cs` is real corroboration, and that report held up. The difference is that code
  could be cross-checked against a second runner's grep of the same symbols. A single verbatim
  print of prose has nothing to cross-check against except the file itself.

- **Six diagnostic codes did not exist, including the two biggest.** §9.6 declares `code` values a
  permanent compatibility surface, but nothing gathered them, so they lived scattered across six
  sections and nobody could see the holes. Writing the registry (new §9.6.1) found that **every
  unknown enum value and every unknown colour name** — between them the entire reason §9.4 exists,
  and the exact diagnostics being implemented this week — had **no code string anywhere**, while
  §3.3 already cited "the existing `unknown-item-id` and unknown-colour codes" as though one had
  been defined. All three §9.4.1 warnings were also unnamed.

  Named now, before anyone had to invent one mid-build: `unknown-enum-value`, `unknown-color`,
  `overflow-forbidden-position`, `unknown-link-target`, `unknown-color-source`,
  `unknown-color-token`. Rulings that came with them —
  - **One code for all six enum keys, not one per key.** The JSON Pointer already names the key
    and the repair is identical in every case. §4.1 splits `command-shape` from
    `command-shell-argv` under the *same* rule because there the repairs differ. One rule, opposite
    answers, which is how you can tell it is doing work rather than dressing up a decision.
  - **None of the three item-id warnings reuses `unknown-item-id`.** §9.5 ruled that sharing the
    reference walk is not sharing the verdict; the corollary is that the code must carry the
    verdict, or a consumer branching on it can't tell error from warning. One code with two
    severities is a code whose meaning is not fixed.
  - **`unknown-color` is spelled the American way on purpose** — codes match the config key
    (`"color"`), this document says colour, so the mismatch looks like a typo and will tempt a
    tidy-up that silently breaks a shipped surface. Written down so it survives.
  - **Tool-protocol codes are a separate table.** `stale-revision` and `cli-not-found` describe a
    failed *invocation* and have no `path`; they must never be confused with a `diagnostics` entry
    pointing into the user's config.

- **The registry immediately found two more, and both turned out to be warnings that should not
  exist.** §9.4's prose named two warnings no code covered, so the first act of the new registry
  was to catch its own omissions. Checking them against the code rather than adding rows:
  - *"A `command` item with no `timeoutMs`"* — **removed.** The premise was an unbounded
    subprocess in a once-a-second loop. There is no unbounded subprocess:
    `CommandProvider.DefaultTimeoutMs = 150`, whole process tree killed. The warning would have
    sent every author to set a key already set for them. `commands/migrate.md` rested on the same
    false premise and has been corrected — the advice to choose the values deliberately survives,
    since 150 ms is genuinely tight for a migrated script, but the *reason* is now the true one.
  - *"A pane with no items"* — **narrowed** to `content`/`fill` only, code `pane-no-items`. §2.11's
    own division does the work: those collapse, so the declaration achieved nothing; an empty
    `fixed`/`percent` pane keeps its extent and is a legitimate **spacer**. Unqualified, this
    warning fired on working intent.

  The shared lesson, now in §9.4: **a diagnostic's premise is a claim about the implementation and
  goes stale like any other.** Both were true when written; one stopped being true when a default
  landed, the other when §2.11 was ruled. Neither was reachable by grep — nothing dangled, and both
  read as sensible prose right up until they were checked against the thing they asserted.

### Fourth batch — `--items`, and the §1 failure inside the paragraph warning about it

Ran the registry technique on the *item* ids next, to see whether the list was duplicated the way
the diagnostic codes were. It is not: `ItemRegistry.cs` is a genuine single enumeration point, the
only `.cs` file that names all sixteen, and its `DefaultIds` derives the opt-in set rather than
restating it. That check came back clean.

What it did surface was §9's `--items` bullet, which specified five fields against a record that
carries two. Checking each against `ItemRegistry.ItemDefinition` — now §9.6.2:

- **"which config keys it accepts" — wrong shape.** Keys do not vary by item id; every builtin
  takes `format`/`color`/`overflow`/`link` and nothing else. What varies is the *kind* — §4.1's
  `command`, §4.3's `from`/`extract`/`case`, §3.3's `parts`. A per-row key column would store one
  per-kind fact sixteen times. That is the §1 duplication failure, specified by the sentence
  immediately above the paragraph that warns about it — the shortest distance an inconsistency has
  travelled in this document yet. `--items --json` now emits two sections, `items` and `kinds`.
  §12.6's `list_items` row had it right all along ("**and the schema for defining a new one**"),
  which is what confirmed the ruling: a per-row column cannot describe a row that does not exist.
- **"its default format" — unimplementable as written.** There is no format string. Rendering is a
  builder function per row, so there is no `"⎇ {}"` anywhere to print, and satisfying the field
  would mean adding a template layer beneath every builder so a CLI flag has something to name.
  Replaced with `example`, rendered through the same `BuildDefaultSegment` the renderer calls,
  against one canned synthetic context. It answers the authoring question better anyway: *does
  this item already carry its own decoration, or do I need a `format`?*
- **Two fields were missing.** `reports` (the description) and `default` (in the default pipeline
  vs opt-in). `reports` goes on `ItemDefinition` as a **required** positional field, so adding a
  row without describing it fails to compile. `default` matters to §12.2: mapping an element onto
  `remote-url` or `model-short` and not placing it yields a config that passes `--check` and
  renders short.

`commands/migrate.md` step 0 claimed `--items` was "the only authority on what exists and what keys
each item takes" — the authority claim survives, the per-item half does not, and it now tells the
migrator to read both sections and to treat `default: false` as a placement obligation.

**The technique generalised.** Gathering identifiers found missing codes; comparing a spec's field
list against the record that has to supply it found a field that cannot exist and two that were
never asked for. Same move both times: put the scattered thing next to the single thing that owns
it, and the holes stop being invisible.

### Fifth batch — `--colors`, and a registry that should not be built (§9.6.3)

Ran the same check on `--items`' sibling flag, which was specified to "print every accepted colour
name". It cannot, and this one is structural rather than an oversight.

**There is no colour registry in `src/`, and there should not be one.**
`ColorResolution.ResolveLiteral` is two lines — it hands the string to `Spectre.Console.Style.TryParse`
and takes the `Foreground`. So the accepted set is Spectre's whole table plus `#rrggbb` hex, which
is infinite. The sixteen are named only in prose; no `.cs` file lists them. Both ways of giving
`--colors` something to enumerate are wrong: hardcoding the accepted names is a second registry of
a table we do not own, drifting against a library upgrade with no care on our side able to prevent
it; reflecting over Spectre's `Color` statics has no drift but means reflection over a library type
in an AOT binary, to print 256 swatches nobody wants to read.

**Ruling: `--colors` prints a curated recommendation** — the sixteen plus `default`/`dim`/`bold` —
and that list is a genuine new artefact, since it exists nowhere in code today. A curated list can
rot in a way a derived one cannot, so every entry is asserted to round-trip through `ResolveLiteral`
non-null. **The list is allowed to be hand-written precisely because that test refuses to let it be
wrong** — without it, a renamed colour would go on being recommended and fail as a silently
uncoloured item under §7.

**The failure that made this urgent is the `kinds` failure again in different clothes.** §12's
tools read `--colors` as authority, and §12.2 told the migrator to preserve colours "by name, from
`--colors`". A bare list of nineteen names reads as exhaustive — so a model asked for `#ff8800`,
which parses fine and always has, would consult the list, not find it, and refuse or silently
substitute. **An authority list that omits a valid form causes the tool to refuse valid work.**
`--colors --json` now carries `recommended` and `alsoAccepted` as separate keys, and each
recommended entry carries `themeMapped: true` — the reason to prefer it, stated, so a model can
weigh it against a user who explicitly wants a specific shade instead of merely obeying it.

`commands/migrate.md` corrected to match: standard ANSI in the original maps to a recommended name
(theme-mapped, same behaviour as before), but a *specific* shade in the original maps to a 256 name
or hex, with `colorSystem` flagged in the report — reproducing a truecolor escape by nearest name
is a downgrade, and the old instruction mandated it.

**And tracing `ResolveLiteral`'s callers to write that section found defect 15** — see below. The
`--colors` question had nothing to do with borders.

### Defect 15 — two border-colour resolvers, found sideways (§6.6, task #15)

§6.5 closes with "one resolution point beats two." Border colour has two, one layer below where
that sentence was looking:

- `PaneTreeRenderer.cs:76` → `ColorResolution.Resolve` → a **spec string** used as a markup tag,
  falling back to `"grey"`.
- `Program.cs:145` → `ResolveBorderColor` → `ResolveLiteral` → a Spectre **`Color`** via
  `Style.TryParse(...).Foreground`, falling back to `Color.Grey`.

Both live in production; which runs depends on the shape of the user's config. Item colour has no
such split — `PaneAssembler.cs:66` calls `Resolve` like the tree path does — and that is exactly
why this stayed invisible, since the divergence exists on one key only.

**It is a capability gap, not just duplication.** A markup tag carries decorations; a `Color`
cannot. `ResolveLiteral` takes `.Foreground` and discards the rest, so `dim` and `bold` — both
documented in §6.1, both accepted by `Style.TryParse` — are predicted to render on one path and
vanish on the other, with no diagnostic, because both paths *succeeded*. The two fallbacks agree
in appearance today by coincidence, not construction.

**Since verified, and it is wider than predicted.** The implementor built a throwaway console app
against the pinned Spectre 0.57.2 for §9.6.3.1's round-trip assertion — a different section — and
in the process settled this: `TryParse("dim")`, `("bold")` and `("default")` all return true with
`Foreground == Color.Default`, while `("olive")` returns a real colour. Then every remaining
decoration keyword behaved the same way: `italic`, `underline`, `invert`, `conceal`,
`strikethrough`, and both blinks. So the rule is not three special cases but one — **a
decoration-only spec has no colour component to contribute** — and `ResolveLiteral` drops the whole
class, not just the two §6.1 happens to document. `border.color: "italic"` fails identically.

That also fixes the shape of the test: driving this with a *colour* passes on both paths, because a
colour is the one input where the two resolvers agree. It has to be driven with a decoration, and
it has to assert the decoration survives — `Color.Default` is what the broken path returns
*successfully*, so "it resolved" passes against the bug (§10.1, arriving through a second door).

The original entry below is kept as written, because how the gap was recorded is the reusable part.

**Recorded as unverified where it was unverified.** That `Style.TryParse("dim")` yields
`Foreground == Color.Default` was inferred from the signature, not observed, and §6.6 said so in a
block quote rather than burying it. The defect held either way — two resolvers and two fallbacks
for one key is the defect — but the symptom was a prediction about a third-party library, and
§9.4's lesson (a claim about the implementation goes stale, or was never checked) applies to spec
prose exactly as it applies to diagnostics. Task #15 makes verifying it step 1, ahead of the fix,
so it cannot enter a test as an assumption.

**The fix is the project's usual shape:** `Resolve` becomes the sole border-colour resolver;
`ResolveBorderColor` survives as a thin adapter but returns a **`Style`**, not a `Color`, so
decorations reach `Panel.BorderStyle`. `ResolveLiteral`'s return type is the actual bug.

**How it was found is the transferable part.** Nobody audited borders. The question was "what is
`--colors` allowed to print?", which led to `ResolveLiteral`, which led to listing its callers —
and the answer was one caller, while the *sibling* function had another, taking a different type.
§9.8's rule was written about a checker transcribing the renderer's arithmetic; this is the same
drift with both copies inside the renderer, where no checker/renderer boundary suggests looking.

### The audit caught one of mine (§9.3 / §9.6.2)

Moving on to `--preview` and reading §9.3 turned up a duplication **I had introduced two commits
earlier**. §9.6.2 said `--items`' `example` renders "against one canned synthetic `ItemContext`
fixture" — and §9.3 already specifies a fixed, non-randomised synthetic payload for `--preview`.
Two synthetic payloads, for the same purpose, written into the same document within an hour, one
of them inside the section correcting a duplication.

Fixed in both places: **one `StatusInput` constant**, which `--items` wraps with canned
`GitBranch`/`Engram`/`RemoteUrl` — the only fields `ItemContext` adds beyond the payload, and they
need constants because they come from probing the machine, which `--items` must not do.

Worth recording plainly rather than quietly repairing. The consequence was not hypothetical: an
`example` built from a different payload would show an item one way under `--items` and another
under `--preview`, and `/migrate` reads both as authority in the same session with no way to
notice they came from different inputs. **Two authorities disagreeing is worse than either being
wrong alone**, because a single wrong answer is at least consistently wrong.

The transferable bit is that knowing the failure mode by name did not prevent committing it. What
caught it was the mechanical habit — read the neighbouring section before specifying against it —
and not the understanding.

### Auditing the unimplemented specs instead of hunting more defects

Five specs went to the implementor tonight and it is still on §9; finding a sixth thing for the
queue is worth less than making sure what is already queued is right. So the next pass was over my
own pending specs, where a gap costs nothing now and a round trip later. Two results from §3.3
(compound items, task #6):

**Clean:** `part-source-count` and `part-forbidden-key` are both in §9.6.1's registry, at the rows
for §3.3. The registry did not lose them when it was built.

**Not clean — §3.3's literal-binding rule had a hole in the exact failure it claims to close.**
It read: when a value part is empty, drop "the literal part next to it — the one after it if there
is one, otherwise the one before." Walk that over a value wrapped in a literal pair:

```json
[ {"text":"("}, {"from":"pr"}, {"text":")"} ]
```

An absent PR drops `)` and keeps `(`. The statusline renders a bare `(`. **That is the
render-wrong class — the second instance after defect 14**, and the one §7's contract does not
cover: not an absence the user might notice, but visible output that is simply incorrect, with no
diagnostic anywhere because nothing failed.

Now: **a literal is dropped when any adjacent value part is empty**, evaluated against original
array positions. It handles the bracket pair, still handles `agent:` and ` | ` separators
identically, and is the simpler implementation — it never needs to know whether a *following*
literal exists in order to decide about a preceding one. Evaluating against original positions
rather than the partially-mutated array is what keeps it order-independent, which is the same
objection §3.3 already raises against nested parts.

**Caught by walking the rule over cases rather than reading it.** The prose is persuasive; the
counter-example takes three parts and appears the moment you try one. Worth remembering that a
rule stated as closing a failure is a claim to test, not a claim to accept — the same posture
§9.4's stale warnings needed, applied to a rule that was never true rather than one that stopped
being true.

### §4.2 made the right argument and applied it to only half the cases (task #5)

Same pass over argv placeholders. `--check`'s three codes and §5's six-construct enumeration are
both complete and consistent — that part is clean. The gap is elsewhere, and it is a nice example
of a section containing its own missing ruling.

§4.2 justifies making an **unknown** placeholder id an error with this: "The literal
`{gitbranch}`? An empty string? A dropped argv entry? Each is a different command line, the script
sees a different `$1`, and the spec picks none of them." That argument is exactly as true of an id
that is **known and simply empty at render time** — `{git-branch}` outside a repo. And there,
unlike the unknown case, `--check` cannot help, because emptiness is a runtime condition. The
section made the argument and then applied it only where a checker could act, leaving the harder
half unspecified.

**Ruled: substitute the empty string; the argv entry survives.** Arity is the thing that must not
change. Dropping the entry shifts every positional after it, so
`mytool --branch {git-branch} --format json` outside a repo becomes `mytool --branch --format json`
and the tool binds `--format` as the *value* of `--branch` — **a third render-wrong case**, and the
nastiest of the three, because the command runs, exits 0, and reports something the user never
asked for. An empty argument can be wrong; it cannot re-bind a flag to the wrong value.

Under `shell: true` the variable is exported **empty rather than unset**, for a parallel reason:
unset lets the script's own `${VAR:-default}` fire, silently converting "no branch this render"
into "whatever the script's default is", which is indistinguishable downstream.

**And the item is not suppressed** — the opposite call from defect 14, deliberately. There the
config has no defined meaning; here it has one and the value merely happens to be empty.
Suppressing would delete behaviour the author chose.

The pattern across §3.3 and §4.2 is the same: both sections argue well about the case a checker
can catch, and go quiet on the case only the renderer sees. That is where the render-wrong class
keeps coming from — and none of it is reachable by `--check`. Generalised into §7.1 next.

### The class had four members and no home in the spec (§7.1)

Four separate rulings tonight all turned out to be the same failure wearing different clothes:
defect 14's `sh -c "kubectl"`, defect 15's two border resolvers, §3.3's orphaned `(`, and §4.2's
re-bound flag. Each got fixed where it was found. None of them was *named* anywhere.

That matters because **§7 only describes two outcomes** — the config is fine, or it renders less
than intended and `--check` explains why. Every one of those four produces a third: output that is
present, plausible, and wrong. The user cannot notice it (nothing is missing), `--check` cannot
report it (nothing failed), and the render path cannot degrade around it (from the inside it looks
like success). A spec whose failure-semantics section does not admit the category will keep
producing members of it one ruling at a time, which is precisely what happened.

So §7.1 now names it, tables the four instances, and states the rule they produced: **when a
config has a defined meaning and only the value is absent, prefer the option that cannot silently
produce different-but-plausible output** — preserve arity over dropping an entry, export empty
over leaving unset, drop a bound literal over emitting it alone. When the config itself has no
defined meaning the call inverts to suppression (defect 14), which puts the fault back where
`--check` can speak.

The part worth keeping is the question it hands to every future rule, because three of the four
were found by asking it rather than by anything failing: not "is this right?" but **"what does
this do when the value is missing at render time, and who would ever find out?"** If the answer
to the second half is "nobody", the rule is not finished.

Two pending specs remained un-walked at that point — §2.10 per-edge borders (task #8) and §2.11
empty-pane collapse (task #4). Both got the same treatment next.

### §2.10 and §2.11 walked — seven gaps, and one section contradicting another (§2.10.1, §2.11.1, §2.11.2)

These two were the last unimplemented specs nobody had walked over cases. Seven findings, five of
them §7.1-class.

**§2.10, five gaps (§2.10.1):**

1. **`outline` and `inside` cannot be what the section says they are.** It calls all four
   shorthands expansions of the declaring pane's four booleans. True of `all` and `none`; false
   of the other two, because "no interior dividers" describes a split's *descendants* and no
   booleans on the split itself can say it. Read literally, `"border": "outline"` renders the
   outer box **plus** every interior line — the exact opposite — silently. Ruled: on a split they
   are subtree instructions; on a leaf `outline` ≡ `all` and `inside` ≡ `none`; a descendant's own
   explicit `border` overrides an ancestor's shorthand, or `outline` is a mute nobody can escape.
2. **`collapse` never had a stated home.** Ruled surface-level only: the compositor resolves
   **one** grid, and the boundary between a collapsed subtree and an uncollapsed sibling has two
   different widths with no way to ask which. Accepting it on a pane and ignoring it is the §7.1
   failure exactly, so it is an error (`collapse-not-surface-level`), not a silent ignore.
3. **The degrade rule frees nothing under `collapse: true`.** "A pane drops its vertical edges
   first" assumes the edge is that pane's column; collapsed, the line is drawn if *any* neighbour
   wants it, so the pane drops its edge, the line stays, and the sizing model credits a column it
   never got back — a ragged row (§2.4) arriving from the degrade step. Ruled: under collapsing,
   degrade operates on **boundaries**, dropping a divider entire for its 1 column.
4. **A junction can have arms in three styles.** "One table per style" presumes one style at the
   position, but the tie-break resolves style *per edge*. Ruled: the junction takes the first
   requester in tree order among its arms — the same tie-break, one rule instead of two.
5. **`reserve(p)` is width-only and horizontal edges cost rows.** The decomposition splits
   `borderReserve` into edges + padding on the width axis and says nothing about rows, while §2.8
   reasons throughout about a box closing under its last content row — written when both
   horizontals always existed. Ruled: `rowReserve(p)`, same one-function-no-transcription regime.

**§2.11, two gaps — and the first is two sections disagreeing (§2.11.1, §2.11.2):**

6. **§2.3's floor table and §2.11 give different answers for the same pane.** The table reads
   `p.minSize set -> p.minSize (author said so; always wins)`; §2.11 says a collapsed pane
   "occupies no width". A `content` pane with `minSize: 12` holding nothing is twelve columns by
   one and zero by the other, and a reader consulting either leaves confident. Same shape as
   defect 15 and worse than either being wrong alone. Ruled **`minSize` wins**: it is §2.4's own
   declared-versus-inferred test, it is the only way to say "size to content but hold this much",
   and it inverts safely — omit `minSize` to get collapse, so nobody is surprised into a pane
   that vanishes.
   - **Follow-on false positive:** §9.4's `pane-no-items` justifies itself with "it collapses, so
     the declaration did nothing" — which stops being true for those panes. Registry row amended.
     A diagnostic whose reason has lapsed is how authors learn to ignore a checker.
7. **A timed-out command item collapses the pane, once a second.** §5 hands the pre-pass "value or
   no value", but a `command` item reaches no-value two ways: it returned nothing, or it never
   answered inside the 150 ms budget. Collapsing on the second means a pane that vanishes and
   returns as a script drifts across its timeout — the line jumping once a second, which is the
   *precise* failure §2.11 names two paragraphs earlier as why `fixed` panes don't collapse. It
   came back through the door the section had just shut. Ruled: **absent** (resolved, no value) is
   distinguished from **unavailable** (did not answer), and a pane holding an unavailable item
   keeps its extent for that render.

Four new registry codes in §9.6.1: `collapse-not-surface-level`, `border-inside-on-leaf`,
`color-down-converted` (§6.2's approximation warning, which the implementor correctly flagged as
missing), and the amended `pane-no-items`.

**What the pass is worth, stated plainly.** Seven gaps in two sections that both read as finished
prose, found by asking §7.1's question of each rule rather than by anything failing. None would
have surfaced in a test, because in every case the code would have done something defensible. The
technique is now cheap enough to be routine: walk the rule over concrete cases, and ask who finds
out when it is wrong.

### Phase 6 walked — the backup that did not contain the file it was backing up

With every pending spec walked, the last unaudited surface was Phase 6: the four prompt files that
write `settings.json` and restore backups. Highest stakes in the project — a gap here loses the
user's statusline — and unexercised, because they all gate on a CLI that does not exist yet. Six
findings, one of them the worst thing found so far.

**1. `/edit`'s checkpoint did not contain the file `/edit` modifies, and its recovery path was a
no-op that reported success.** `/edit` changes `claude-tui-line.json` and nothing else. The ledger
entry captures `settings.json` and, optionally, the user's original script — never the config. So
step 7's "restore the checkpoint from step 4 and report the failure" restored a `statusLine` key
nobody had touched, left the broken config exactly where it was, said it had recovered, and the
pre-edit config was gone. Every individual instruction was followed correctly.

What hid it was a scope line: the ledger opened "**Every command that writes to `settings.json`
follows this**", and `/edit` does not write `settings.json`. By the ledger's own scope, `/edit`
should never have been pointed at it — and the contradiction between that line and `/edit`'s step 4
is exactly where the missing artifact lived. Fixed by widening the scope to both files and giving
the entry a second artifact class (`configOriginalPath`/`configCopy`/`configSha256`), recorded
whenever a config exists rather than only when the running command intends to change it — because
the command that needs a backup is not the one that took it.

**2. The hash check would have disabled the escape hatch permanently.** The ledger said "re-hash
the **live file** and compare against the ledger entry", and `revert.md` said "re-hash the
**artifacts the ledger entry points at**" — two documents, two different files, one shared
rationale. Two authorities disagreeing again, the defect-15 shape.

And the ledger's reading is not merely different, it is fatal: at revert time the live
`settings.json` is *supposed* to differ from the backup, because claude-tui-line is installed now
and was not then. A revert following it literally reports "the user hand-edited it" on every run
and stops. The escape hatch refuses precisely when it is reached for. Replaced with a four-row
table naming which files are checked and what a mismatch means for each.

**3. The rationale in that instruction was true — about a file nobody was checking.** "A mismatch
means the user hand-edited it since the backup" describes the user's **original script**, at its
original path. `revert` restores a `statusLine` verbatim, which points at a *path*, not at
contents. `revert.md` handled the script being **missing** and said nothing about it being
**modified** — and modified is the silent one: the revert succeeds, the statusline renders, and it
is simply not the statusline that was backed up. Now three cases, with the third asking the user.

**4. `revert` checkpointed before it verified.** Step 3 appended a ledger entry (mutating); step 4
verified hashes and could stop. In an append-only ledger that forbids removing an entry, an aborted
revert left a permanent record of a change that never happened. Swapped, and the general form is
now stated in the ledger procedure: every step that can abort comes before every step that writes.

**5. Timestamp arguments would not have matched.** Ledger entries key on full ISO
(`2026-08-13T04:12:07Z`); artifact filenames use the compact form (`20260813-041207`) — and the
compact form is what the previous command *printed in its report*, so it is what the user pastes.
Match liberally now: either form, or an unambiguous prefix.

**6. `revert` and the config file needed an explicit non-interaction.** Now that entries carry a
config copy, "restore everything" is a tempting reading. Ruled explicitly: revert does not restore
the config. It answers "put my old statusline back", not "undo my layout work", and rolling
someone's layout back as a side effect of unpointing `statusLine` destroys work they never asked
about. `/edit` owns config rollback.

**The generalisation, which is not about backups.** A procedure can be correct at every step and
still not do its job, when the artifact it operates on is not the artifact the caller modifies. The
check that catches it is one question asked of the call site rather than of the procedure: *does
what this saves include what this command is about to change?* Nothing inside the ledger could have
answered that, which is why it survived a careful reading of the ledger.

### §12.6 walked — and one fix from earlier tonight had already propagated (§12.6.7, §12.6.9)

Last specified-but-unwalked surface: the MCP wire contract. Seven tools, two of which write the
user's files. Five findings.

**1. "Three files, and no others" was missing one the same section requires.** §12.6.7 enumerates
what an MCP tool may write, and `revert` restoring the copied script to `scriptOriginalPath` is
not among them — while §12.5 and §12.6 both require exactly that write, because a restored command
pointing at nothing leaves the user with no statusline and no obvious cause. An implementor
obeying the list literally would skip it and produce the failure the restore exists to prevent.
Added as a fourth entry, and scoped hard: only from `revert`, only from the entry being restored,
only when no file is there, never overwriting. "Restore the script" is otherwise an arbitrary-path
write driven by the contents of a data file, which is a much larger permission than the other
three and should not arrive by implication.

**2. Only `get_config` could say which file it touched.** §12.6.2 spends a section on the hazard
— the server's environment is not the user's shell, so §5's search order can resolve a different
file with nothing erroring — and then requires "the model is expected to state the path when it
reports what it changed". But `configPath` appeared on exactly one tool's return. A model calling
`set_config` without a prior `get_config` had *written somewhere it could not name*; `preview`
could render a file that was not the user's and hand back rows with nothing to compare them to.
Now: every tool that resolves a path returns `configPath` and `source`. Resolving a path and
reporting which one you resolved are one obligation.

**3. `confirm: true` with no `target` had two readings.** The table marks `target` optional; the
text says restoring takes an explicit one. Ruled an error (`target-required`), returning the
listing — and **deliberately diverging from `/claude-tui-line:revert`, which defaults to
`origin`**. A human typed the slash command and read its name; an ambient tool call was not asked
for, and one boolean should not roll a config back to before this tool existed. The asymmetry is
the argument, so the cheap call stays the one that only looks.

**4. Compare-and-swap had a hole exactly where two agents collide.** `baseRevision` is optional so
a first write needs no ceremony — which covers the caller omitting it and leaves the fresh-machine
case with no CAS at all: two sessions both find no config, both write, second discards first,
silently. Ruled `revision: "absent"` for a missing file, with `set_config` refusing if one now
exists. Create becomes atomic inside the mechanism already there.

**5. `preview` could show a plausible render of a config `set_config` would reject.** §7 makes a
bad config degrade silently rather than fail, and §12.6 tells the model that looking at rows is
how it checks its work — so "the preview looked right" was evidence for an invalid candidate. It
now returns `diagnostics[]` for an inline config. The check has already run; withholding it made
preview quietly weaker than the loop it anchors.

**And one thing that fixed itself.** `set_config` checkpoints before mutating, through §12.2 — so
before tonight it carried `/edit`'s defect exactly: a checkpoint that did not contain the config
it was about to overwrite. Amending §12.2 closed it here without a second edit and without anyone
noticing it was open. That is the one-definition rule paying out rather than merely being
asserted, and it is worth recording as evidence that the rule is load-bearing: the alternative
design, where each command describes its own backup, would have needed this found twice.

### The implementor's scoping question found the gap in my own section (§6.2.1)

`color-down-converted` landed, and the implementor flagged what it had *not* covered: Spectre's
numeric `colorNNN` form, deliberately left out because my example was hex-only and it did not
want to invent the interaction. Exactly the right instinct — and following it up found that the
narrow implementation matched §6.2's prose while the §9.6.1 registry row already said something
broader ("a `colorSystem` that **cannot render it**", a tier comparison). Two authorities, and
this time the *implementation* had been written against the weaker one.

The real gap is that every paragraph of §6.2 discusses `standard`. So the case nobody had
written down is **hex under `256`** — an author who widened the palette one step, which is the
one situation where you would expect the warning to have been thought about. §6.2.1 now states
the rule as a minimum-system table with the comparison made explicit: fires when the literal's
minimum exceeds the resolved system, which is three cases rather than one.

Two smaller rulings inside it:

- The row's own rationale — "approximated to the nearest of the sixteen" — is **false** in the
  new case, where it is approximated to the nearest of 256. That is the digest rule about a
  diagnostic carrying its stated reason as an implicit condition, hit again: broadening a check
  without broadening its message ships a diagnostic that lies to a third of the authors who
  trigger it. The message now names which palette.
- A hex literal landing exactly on a 256 entry still fires under `256`, ruled deliberately.
  Suppressing it means shipping 256 RGB triples nothing here would ever verify, and the warning
  is *correct* regardless: someone who means entry 207 should write `color207`, which says so
  and survives a colour-system change. The diagnostic points at a real improvement rather than a
  phantom defect, which is the only reason a technically-avoidable warning is allowed to stay.

### `setup` walked — the only command that runs today, and it verified the wrong half

Task #12 was marked complete, which is not the same as audited. `setup` is the one command that
works without the CLI, it is the first thing a new user runs, and it is the one that performs the
backup the whole project's safety story rests on. Five findings.

**The one that matters is step 5.** It rendered a preview by running
`${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line` — the variable. But step 4 writes settings.json, which
does *not* interpolate plugin variables, so it must write an **expanded absolute path**, and that
expansion is the only thing in the whole procedure still untested at step 5. The binary was proven
in step 2. So a wrong expansion — a typo, or the literal `${CLAUDE_PLUGIN_DATA}` written through
unexpanded — produces a perfect preview, a success report, and a blank statusline, with nothing
anywhere pointing at the cause. §7.1's class exactly, in the install path: present, plausible,
wrong. Step 5 now reads `statusLine.command` back out of the file and runs *that string*.

`/revert` step 7 already got this right — it prints and runs the command it restored. Two commands
answering the same question different ways, and the weaker one ran on every install.

**The other four:**

- Step 6 told the user their statusline was backed up and that `revert` restores it. True when
  step 3 wrote an `origin`; **false when it wrote a `checkpoint`**, which is the documented case
  where setup runs on a machine that already had one. Bare `revert` then correctly restores the
  older `origin` — not what setup just saved. The default is right; saying nothing about it is
  not. Step 6 now reports the kind and hands over the timestamp.
- Step 1 ran `dotnet --version` in the user's cwd while step 2 builds elsewhere. `global.json`
  makes that two different SDKs, so the check can pass for a toolchain the build never uses and
  the failure then presents as a build error. Now runs in the project directory.
- The sample payload carries `cwd` and `model` only, so items depending on workspace/session
  fields render absent. A correct install reads as a broken one and the user's first act is to
  debug something that works. Now said out loud.
- Ordering (checkpoint before any write) was already correct. Recorded as a clean result.

**And the config-absence marker, found by walking setup into the ledger.** The ledger's config
section said to omit all three fields when no config exists and "note it in `note`" — while citing
`"statusLine": null` as its precedent. It is not that precedent. Three missing fields are
indistinguishable from an entry written before configs were captured at all, and free text is not
something a rollback can branch on: *no config was here* and *this ledger cannot say* arrive as the
same answer and call for opposite actions. Now `configOriginalPath` plus an explicit
`configCopy: null`. Third time in this project that absence needed a distinguished value rather
than a missing key, after §12.6.9's `revision: "absent"` — assume the fourth has the same shape.

That marker then propagated into `/edit` twice, and the second was a defect the first created:

1. Step 2 offers to seed a config when none exists, then step 4 checkpoints. So the checkpoint
   records the *seeded* file, and a failed edit rolls back to a config the user did not have when
   the command started — "no config, defaults apply" becomes unreachable by rollback. Same
   ordering defect I fixed in `/revert` by swapping steps 3 and 4: a write ahead of the
   checkpoint. Now the checkpoint is taken first, and step 7's rollback deletes rather than
   restores when `configCopy` is null.
2. Step 4's guard — "stop if the entry has no `configCopy`" — would then fire on exactly that
   legitimate null. Adding a distinguished value turned a correct check into a false positive one
   edit later, which is the same trap as tonight's `color-down-converted` message: broaden the
   data and the things reading it silently stop meaning what they said. Now it distinguishes
   `null` (looked, found nothing — fine) from missing (never looked — stop).

### `migrate` walked — one defect, one clean result, and a rule written out three times

The last command. It held up better than the others, which is worth saying plainly.

**The defect is step 8.** After writing the config it said to point `statusLine.command` at the
binary "following the ledger procedure". The backup was already taken at step 2 — deliberately,
before this command reads anything — so running the procedure again appends a second entry whose
config capture is *the config you just wrote*. That is a restore point for a state that existed
for one instant and that nobody would ever want back: migrated config, original statusline. The
phrase had two readings and the wrong one produces a plausible, permanent, useless ledger entry.
Now it says the backup is done and cites the write rules instead. Also spelled out that config
must be written before `statusLine` is repointed, since the other order gives the user a second of
built-in defaults they never approved at step 7.

**The clean result is step 6.** It pipes one payload into both the original script and
`--preview`, which would be worthless if `--preview` ignored stdin — two renders compared across
two different inputs, differences and matches both meaningless. §9.3 already rules it: stdin has
data, use it; stdin empty or a TTY, use the fixed synthetic payload **and say so on stderr**. It
even names `/migrate` as the reason. Checked and correct.

**And the third copy.** Chasing my own dangling reference to "the ledger's writing rules" found
that there is no such section — the rules were written out inline in `setup`, `revert` and
`migrate`, three times, in three wordings. Three copies is how two of them drift. `backup-ledger.md`
now has one **Writing `settings.json`** section with the four rules, and the commands cite it
while keeping only the emphasis specific to them: `setup` on expansion, `revert` on the
wholesale-copy temptation that is strongest exactly when a user asks to go back.

That is every command and every doc walked.

### The manifests, and an unset variable that names `/bin`

`plugin.json` and `marketplace.json` had never been walked, and a broken manifest means the plugin
does not install at all — the earliest possible failure. Both are sound: names match across the
two, `source: "./"` is right for a marketplace living in the repo it ships, and the `commands/`
pointer resolves.

The walk did settle one thing worth verifying rather than assuming: **`CLAUDE_PLUGIN_DATA` is
real.** `setup` leans on it entirely, and the whole "works today" claim rests on it existing.
Confirmed from an official Anthropic plugin that both uses it and documents it as the plugin's
persistent data directory, surviving plugin updates. It is much newer than `CLAUDE_PLUGIN_ROOT`
(two files against 112 across the installed marketplaces), which is why it was worth checking.

**And checking it found the sharper problem.** Unset, `"${CLAUDE_PLUGIN_DATA}/bin"` expands to
**`/bin`**. So on any Claude Code that does not set it, step 2 was a release build published into
the system binary directory — failing on permissions, or succeeding under a privileged shell —
after which step 4 writes `"command": "/bin/claude-tui-line"` and step 5 cheerfully confirms it
renders. An unset variable inside a path expansion does not announce itself; it silently names a
different, real, usually worse directory. Now guarded with `:-$HOME/.claude/claude-tui-line`,
resolved once into `$BIN_DIR`, and the fallback is reported to the user rather than used quietly.

Same family as the expansion defect in step 5, and it is the reason that one was worth fixing:
both are the install writing a path nothing downstream ever re-checks.

### §8 walked — a count that reads as a superset, and `auto` ruled two ways

First of the early sections, written before most of what now cites them. Four findings, one live
on the implementor's critical path tonight.

**`size: "auto"` had two contradictory rulings.** §2.2 lists it as a documented synonym for
`fill`; §3.2.1 cited "`auto` resolving to `fill`" as an example of the *silent-acceptance flaw*
the diagnostics work exists to fix. `--check` cannot satisfy both — warn or don't — and it is
being written right now, so this was one edit away from being decided by whichever line the
implementor read first.

Ruled: **`auto` stays legal, as a deprecated alias, and warns** (`deprecated-size-alias`). Not
because deprecation is tidy, but because of what makes this value specifically dangerous — it is
the only one in the vocabulary whose plain-English reading names *a different value that also
exists*. "Auto" sounds like "size to its content", which is `content`, the exact opposite of
taking an equal share of the leftovers. So the author who most needs telling is the one who wrote
`auto` meaning `content`, and their only symptom is a layout slightly off at some widths. The
registry row therefore requires the message to name `content` as the other candidate: told merely
"deprecated", that author re-spells it `fill` and keeps the layout they did not want.

§3.2.1's aside is corrected in place — `case: "title"` is the honest example of a value with no
meaning, and §7.1 rules the two in opposite directions, so citing one as an instance of the other
pointed the fix backwards.

**The count.** Three sites said "all 14 builtins" for the default list. There are **sixteen**
builtins; fourteen are in the default set. Read literally — and "all" invites exactly that — the
default set gains `model-short` and `remote-url`, and `remote-url` shells out to git on every
render, breaking the promise the README makes in as many words. §9.6.2's `default` flag is the
one definition; all three sites now defer to it rather than restating a number.

**Two smaller ones.** §8 said an unknown builtin id "is suppressed silently" and stopped there —
true, and half the rule: `unknown-item-id` is an error at `--check`, which is the half that makes
the silence recoverable rather than permanent. And `layout.chromeReserve` appears in the very
first example config in the spec while the README documents no such key; now documented, with
what raising it actually does.

**One clean result:** the `(SPEC.md §6b)` cross-reference resolves — `SPEC.md` is still in the
repo. Checked because a dangling pointer in the sentence defining per-render config reload would
be worth knowing about.

### §5 walked — the cache had a genuine multi-terminal defect (§5.0.1)

"The hard part", and the first walk tonight to find something that would misbehave at runtime
rather than mislead a reader.

**`paneWidth` was stored in the value cache entry.** The value is keyed by `id` + argv + `cwd`
and deliberately shared by every session on the machine — that is the whole design. The pane
width is a property of one terminal. So two sessions in the same repo at different terminal
widths collide on a record where last-write-wins is *not* correct: each overwrites the other's
width every second, and each spawn is handed the other terminal's width. A `command` item that
adapts its output to the space available then formats for a pane it is not in — present,
plausible, wrong, with nothing in the render suggesting it. Two terminals open on one repo is not
an exotic setup.

What makes it a good find is that **§5 already contains the argument that forbids it.** The
"one file per cache key, not one shared `items.json`" bullet reasons exactly this way about two
sessions read-modify-writing one map. The same reasoning one level down says the value and the
width cannot share a record either, and the section did not follow its own rule to the next
granularity. Generalised in §5.0.1: **data with different sharing scopes must not share a
last-write-wins record.**

Fix: a separate `widths/` store keyed by the cache key **plus `COLUMNS`** — two sessions at the
same terminal width agree, so between those two last-write-wins is correct, which is the test §5
already applies to values. And **written only when the width changes**, which incidentally
repairs a claim §5 made and then contradicted four bullets later: "the steady-state cost of a
custom item is a map lookup, not a fork" was false while every command item rewrote a file every
second. Steady state is now zero writes.

**Two interlocks that were missing.**

- §5's stale-on-failure said a command with no cached value "is suppressed", flatly. §2.11.2 —
  written last night — needs that suppression *marked unavailable*, because a pane holding an
  unavailable item must not collapse. Left flat, the two states merge one layer down and a
  command that is 200 ms slow on one tick silently restructures the statusline: pane collapses,
  neighbours resize, everything reflows, on a timing accident, with no diagnostic. The marker is
  what stops a transient failure being read as a layout instruction.
- "Exit code is always 0 and stdout is always valid" was written absolutely, and §9.4 defines
  exit codes 0/1/2/3. Not actually a contradiction — one is the render path, the other the CLI —
  but nothing said so, and §9.4 is being implemented right now. Scoped explicitly: same binary,
  two callers, and the contract belongs to the caller. §9.1's "the render path is untouched" is
  this same boundary drawn from the other side.

### §4 walked — the section forbidding second registries kept one, and `--check` had no boundary

Four findings, and the first two are both cases of §4 breaking its own rule one paragraph after
stating it.

- **§4 enumerated the builtins in prose, immediately after declaring the registry the only place
  they are enumerated** — and the prose list was wrong: it omitted `remote-url`, which §5.1
  specifies and §9.3 names as one of the canned `ItemContext` fields. It also opened with "the 14
  captured segments", a number that collides by coincidence with §8's default-set size. Rewritten:
  the list stays, demoted explicitly to orientation, with `--items` named as the authority and the
  instruction that if the two disagree the prose is the one that is wrong. Membership in the
  default set is now stated as a **flag on the row, never a count**, and the coincidence of the two
  sizes is called out as a fact about today's rows that nothing may be derived from.

  The same count was restated in three more places. §8's definition, §9.6.2's `default` field, and
  the README all now state the predicate instead. The README's version was the worst of them: it
  said "sixteen ship built in, fourteen in the default set" directly above a table that marks each
  opt-in row — so the sentence added nothing except a second thing to keep true.

- **§4 still recorded `paneWidth` in the cache entry**, which is the design §5.0.1 removed two
  hours earlier. Two sections describing one mechanism, disagreeing about which file it lives in,
  with the implementation not yet written — the reader who gets there first decides. Rewritten to
  cite the widths store, and the new keying turned out to *improve* §4's own claim: the old text
  admitted the value is "correct on all but the first tick after a resize", i.e. wrong for one
  tick. Keying by `COLUMNS` makes a resize a key miss instead, so that tick reports **absent**
  rather than a confidently stale number — and absent is a state a script can branch on.

- **"Nonzero exit ⇒ treated as empty" was the exact behaviour §2.11.2 exists to forbid**, written
  into the section that produces the value. §2.11.2's whole argument is that an item that did not
  answer is not an empty item, because collapsing on a 150 ms timeout makes the line jump once a
  second. Fixed to `absent` vs `unavailable`, with the old wording quoted in place — it is a
  reading someone will arrive at again from §7, which genuinely does treat both as "renders
  nothing". §7 governs what the user sees; §2.11.2 governs what the layout is told.

- **`--check` was said to report a runtime fact, and that exposed a missing ruling.** The `maxLines`
  bullet ended "excess lines are dropped and `--check` reports the cap was hit" — a diagnostic with
  no code in §9.6.1, which per that registry means it does not exist, and which `--check` could
  only produce by executing the user's commands. Nothing in the spec said whether it does.

  Ruled in a new **§9.1.1**: `--check` never executes a `command` item; `--preview` always does.
  Three reasons for the first half, and the first is the one that matters — `/edit`, `/migrate`,
  and §12.6's tools all run `--check` on a config a model has just written, so "validate this
  file" must not mean "run the commands in this file". The other two: `--check`'s answer must be a
  function of the config alone (§9.8 already argues this for width; machine state and wall-clock
  are the same argument, and its exit code is the gate a write is accepted on), and a validation
  must not cost what a render costs. The consequence is a boundary worth having in writing:
  **`--check` cannot report anything about a command's output**, which is why every `command`
  diagnostic in §9.6.1 is a statement about the declaration.

  `--preview` is the deliberate opposite, since §12.3 and §12.4 both put a preview in front of the
  user as the evidence for accepting a write, and evidence assembled by skipping the interesting
  half is not evidence. The `maxLines` notice moved there, on **stderr** — the channel §9.3 already
  uses for "this payload is synthetic", and not stdout, which stays byte-comparable so `/migrate`
  can diff a preview against the original script.

  That ruling then landed on §12.6, which had just made `preview` accept an **inline** config: a
  caller can hand the server a config that was never written to disk and have its commands spawned.
  Added as §12.6.9's fourth ruling — it still runs them, for the same evidence argument, but the
  ordering is now stated because the tool surface invites the opposite guess. `check` is the
  cautious call; `preview` is not, and a caller entitled to preview a config is a caller entitled
  to write it. §12.6.4's look-before-you-leap default only works if the model knows which call is
  which.

### §9.4 walked, and four rulings the implementor was blocked on

**§9.4's own findings — two, both about lists that had gone quietly short.**

- **Tier 1's enum list named six keys and missed three.** `size`, `style`, `align`, `valign`,
  `overflow`, `case` were listed; `split`, `distribute`, and `colorSystem` were not, and all three
  have closed value sets whose failures are silent and consequential. A misspelled `split` turns a
  container into a non-container, so its `children` become a key nothing reads and half the
  statusline disappears. A misspelled `distribute` reverts to greedy sizing — the exact layout the
  author wrote the key to avoid, differing by a row count rather than by an absence.
  `"colorSystem": "24bit"` is the cruellest: it falls back to `standard` and then produces
  `color-down-converted` warnings on the literals the author widened the profile *for*, so the
  diagnostic they receive is correct, unexplainable from where they are standing, and points away
  from the typo.

  The list was also restated in four places — §9.4's severity bullet, §9.4.1's tier 1, the
  "one code across all six" paragraph, and the §9.6.1 registry row. Now stated as a **predicate**:
  any key whose accepted values are a closed set. Same treatment as §4 got an hour earlier, and
  the same failure it prevents — a key added with a closed value set and nobody thinking to touch
  §9.4. The "one code, not one per key" ruling survives and is now argued rather than assumed: a
  per-key code would make adding a key a change to the §9.6 compatibility surface. Added: the
  message must name the accepted set, because a code and a pointer localise the fault and only
  the accepted set repairs it.

- **`--config` and the render path were two rules pointing in opposite directions**, meeting on one
  real command line: a `statusLine.command` of `claude-tui-line --config ~/my.json` whose file is
  later deleted or saved with a trailing comma. §9.2 says never silently fall back; §5 and §9.1 say
  the render path exits 0 and never blocks, because Claude Code runs it once a second and has
  nowhere to show a failure. Both cannot be followed literally and neither may simply lose.

  Falling back to defaults is the false resolution — it satisfies §5's letter and produces §7.1's
  outcome in its purest form. New **§9.2.1**: exit 0, and **render the reason** as one plain row,
  because the statusline is the only output channel that path has. stderr is discarded, the exit
  code is not displayed, and a log file nobody knows about is not a channel.

  The rule that took the most thought is that the test is **whether a config was asserted, not
  whether one was found**. A file at the searched path that exists but does not parse gets the same
  treatment as a named one: writing it asserts a config exactly as naming it does. Defaults are the
  right answer only to the *absence* of an assertion. That row is probably a defect in the render
  path today. Tracked as task #17, deliberately including the `--config` wiring, because half of
  this ships a defect: `--config` parsed on the render path without §9.2.1 means a deleted config
  renders defaults once a second forever.

**Four rulings the implementor held on rather than guessed — every one of them right to hold.**

- **`colorNNN` does not parse, and §6.2.1 was recommending it.** They reflected over Spectre
  0.57.2 rather than trusting a docs search that came back inconclusive: `Style.TryParse("color207")`
  **fails**; `Style.TryParse("207")` resolves to palette entry 207. My table was not using shorthand
  — I believed the prefixed spelling was accepted. The failure mode is the bad one: `color207` does
  not error, it fails to parse into a colour and the item renders uncoloured. So the diagnostic
  whose entire job is to warn about colour going wrong was, in its repair advice, telling authors
  to write a value that silently produces no colour. Table corrected to bare palette indices, the
  finding recorded in place, and README gained the form — it documented names and hex only, so the
  escape hatch the diagnostic points at was undocumented.

- **Named-token tiering: build the sixteen-name constant.** Spectre exposes `R`/`G`/`B` and nothing
  else — no palette index, no name readback — confirmed by reflection over the public surface. So
  there is no computed discriminator between `red` and `grey37`, and the choice was a name list or
  abandoning the row. Signed off, and my earlier ruling was narrower than it read: I forbade
  enumerating the *extended* palette, which is Spectre's, ~240 entries, changes with the library.
  The sixteen are closed by the ANSI standard. What decides it is that **the constant has to exist
  anyway** — §9.6.3 requires `--colors` to print exactly these sixteen and notes that no file in
  `src/` enumerates them today. One constant, two consumers; two lists would let `--colors` print a
  palette the tier check disagrees about.

- **`--check --json` emits JSON at exit 3.** Their working assumption — nothing could be checked,
  so nothing to serialize, so prose on stderr — is right about the diagnostics and wrong about the
  contract. `/edit` and §12.6 call this on configs a model just wrote, which makes unparseable a
  *likely* outcome rather than a remote one, and that is where prose on stdout costs the most. A
  flag that guarantees a format except when something goes wrong does not guarantee a format. Ruled
  into §9.6 with the `{ ok: false, code, path, message }` envelope §9.6.1's second table already
  defines, `config-unreadable` and `usage` added as rows. **`diagnostics` is absent, not `[]`** —
  `[]` means "checked, found nothing", so a consumer testing `length === 0` would call a broken
  config clean. Third instance tonight of absence needing a distinguished value.

- **`--config` on the render path: defer both halves together** — see §9.2.1 above.

Their correction to my characterization is accepted: the code already used `!= TrueColor` rather
than `== Standard`, so hex-under-256 was firing before this round. That was me being wrong about
the code, not the spec being wrong. Suite at 1110/1110.

*Historical note:* two references to `color207` survive earlier in this log, in the §6.2.1 entry.
They are left as written — the log records what was believed at the time — but the advice in them
is wrong and §6.2.1 is the authority.

### §9.7 and §9.8 walked — a source of truth that does not contain the value

§9.8 itself is sound; the two findings are in what it hands off and in what §9.7 assumes.

- **§9.7 names the `.csproj` as the source of truth for a version the `.csproj` does not declare.**
  There is no `<Version>` element in it. MSBuild then supplies `1.0.0` — silently, with no warning,
  indistinguishable from a deliberate choice — while `plugin.json` says `0.1.0`. So `--version`
  as specified would ship reporting a number that is not this project's, which is verbatim the
  outcome §9.7's two-homes ruling exists to prevent. A defaulted version is worse than a stale
  one: stale is a real number from a real release; `1.0.0` here means nothing at all. §9.7 now
  requires the element, and notes that its own drift test fails on this first — which is the test
  working, before there is a release to be confused by.

  Also checked and recorded as a clean result: `marketplace.json` carries no version and must not
  gain one. Duplicating it there would add a copy the drift test does not cover, which is the
  two-registry problem returning under a third name.

- **§9.8's parting instruction had nowhere to land.** It argues that width-dependent facts belong
  to `--preview` and to nothing else, and says to report them "as a note alongside the rows" — but
  §9.6's `--preview --json` shape is `columns`, `usableColumns`, `rows`, with no field for one.
  So the human form of `--preview` would report what it dropped and the JSON form would not, and
  the JSON form is the one a model reads. The information §9.8 had just finished arguing for would
  reach only the caller with no programmatic need for it.

  New **§9.8.1**: render notes become one named channel with two renderings — stderr in the human
  form, `notes[]` in JSON. §4's `maxLines` notice, which I had assigned to stderr by name two hours
  earlier, is folded into it rather than keeping a private arrangement. Three rulings on the shape:
  a note carries **no `code`** (§9.6.1's codes exist so a consumer can branch on a fault, and there
  is no branch to take on "truncated at 109 columns" other than showing a person; codes would grow
  a permanent compatibility surface for no consumer); a note **never** appears in `diagnostics` and
  vice versa (they answer different questions, and merging them makes a config working exactly as
  §2's ladder specifies read as broken); and notes never affect the exit code, restated so the JSON
  form cannot quietly acquire a different rule from the human one.

  This is the fourth time tonight that a command's human form and its `--json` form were specified
  to report different things, and the fourth time the JSON form was the poorer one. Worth naming as
  a pattern rather than fixing case by case: the prose gets written for the human reader, the JSON
  shape gets written as an afterthought two sections away, and the consumer who cannot fall back to
  reading English is the one who loses.

### Open, and honest about it

- **The colour system has tests for none of what makes it a colour system.** Narrowed from
  "a parser and no tests" by reading the source. Two halves, and only one is still open:
  - *Resolved by reading.* §6.1's three literal forms all parse: `ColorResolution.ResolveLiteral`
    delegates to Spectre's `Style.TryParse`, which accepts standard-16 names, 256-palette names,
    and `#rrggbb` alike — parsing was **never** limited to 16, and §6's "widen the palette" was
    always a rendering-profile change, not a parsing one. `Config.cs:384` confirms the profile
    side: `256` → `EightBit`, `truecolor` → `TrueColor`, everything else → `Standard`, wired into
    the console at `Program.cs:58-61`. README widened accordingly.
  - *Still open.* §6.2's **down-conversion** — what a hex literal actually looks like under the
    default `standard` profile — remains unverified. Every one of the 11 `ColorSystemSupport`
    references in the test suite pins `Standard`, and not one of those tests uses a hex or
    256-palette literal, so the down-conversion path is exercised by nothing. Handed to the
    implementor alongside `--colors` (§9), which is the natural place for it: the command has to
    render each colour under the active profile anyway.

  The general point, worth keeping: **a parser delegating to a library may already accept more
  than the project's own documentation claims.** Here the code was ahead of the docs, and reading
  it turned a feature-gap into a documentation-gap.

### §10 walked — the suite's strongest assertions all pass on a blank surface

§10 was eleven bullets written before the CLI existed, so the first job was the six missing
bullets (12–17): `--check` spawns no process while `--preview` does, the four config-assertion
cases on both paths, `--json` parsed at all four exit codes, every recommended colour literal
proven to parse, the version drift test, and notes staying out of both `diagnostics` and the exit
code. Each is one of tonight's rulings given something that can fail.

The find is **§10.1**, and it is about the eleven bullets that were already there. Bullets 2, 3, 4
and 6 are all width assertions, and **every one of them is satisfied by a surface on which every
item resolved to nothing.** Rows still equal in width, still under the cap, still
position-independent; the `fill` sibling still gets the exact remainder; the anchor's measured
content width plus chrome still equals its resolved width, because zero plus chrome is a perfectly
consistent answer. A pane tree rendering entirely empty passes the rectangle invariant flawlessly
— and the rectangle invariant is described in this document as the one assertion that catches
ragged padding, height mismatch and overflow together.

That is §7.1's render-wrong class arriving in the test suite rather than in the output, which
makes it worse than the instances of it fixed earlier tonight: the suite is the instrument that is
supposed to detect the class. The failures this project is most exposed to — a provider returning
nothing because the payload changed shape, an item id quietly unresolved, a cache handing back an
empty string — all move the surface from correct to blank **without moving a single width**.

So every layout test now carries a blank-surface control: same tree, every item forced empty,
asserting the invariants still hold *and* that the two runs are distinguishable. Bullet 3 already
required the rectangle invariant be shown to fail against a deliberately broken compositor, which
is the right instinct pointed at the other half of the problem — **a broken compositor is a
control for the padding the assertion measures; a blank surface is a control for the content the
assertion does not measure.** Passing one says nothing about the other, and the document had only
ever asked for one.

Two smaller repairs, both to bullets that existed:

- **Bullet 8** tested command providers against five real scripts but asserted nothing about the
  resulting state. Empty output, nonzero exit and timeout all render as no text, so an assertion
  phrased against the output cannot tell §4's `absent` from its `unavailable` — while §2.11.2's
  collapse rule reads exactly that distinction. The suite would have scored the
  collapse-on-timeout defect as passing.
- **Bullet 9** covered the value cache and knew nothing about §5.0.1's separate widths store. Added
  the three-render sequence, with the assertion made against the *child's environment* rather than
  the rendered row, because the failure being guarded against is `CLAUDE_TUI_LINE_PANE_WIDTH` being
  present and wrong — which nothing about the row can see.

### §11 walked — the phase list stopped being updated and nothing noticed

Started as two one-line fixes, turned into the section's own walk. **Phase 5 enumerated the CLI
surface as three flags** where §9 specifies five, and the two it omitted (`--colors`, `--version`)
are the two §12's commands need in order to offer the user a choice rather than guess at one.
**Phase 7 was missing entirely**, so the phasing section and the task list disagreed about how many
phases this project has.

Pulling on that produced §11.1. Phases 2, 3 and 4 each enumerated their features and **every one
of those enumerations is stale**: §2.8's `height: "content"`, §2.10's per-edge borders and
`border.collapse`, §2.11's collapse rule, §3.2's hyperlinks, §3.3's compound items, §4.2's argv
placeholders, §5.0.1's widths store and §5.1's probe caching are all specified and **no phase
mentions any of them**. Read literally, Phase 4 is "item registry + command providers, cache, TTL,
timeouts" and is therefore done, while three of those eight sections are unbuilt work sitting
inside its boundary.

The proof it is not load-bearing: **§3.2 shipped anyway.** Hyperlinks were specified, built, tested
and merged with no phase ever assigned, and nothing anywhere noticed. That answers the question of
whether this list is what sequences the work.

So the rule this document already applies to the item registry and to the CLI surface, now applied
to itself — **a phase names a capability boundary and cites the sections inside it; it does not
enumerate features.** Phases 2, 3 and 4 rewritten accordingly: 2 is the single-pane surface
(§2.1–§2.8), 3 is splits and everything that exists only because there is more than one pane
(§2.9's re-measurement, §2.10's border grid, §2.11's collapse), 4 is the whole item layer (§3, §4,
§5 — "how a value gets from a provider to a pane"). The dependency claims underneath were correct
and are untouched; that question — what must exist before what, and why — is the reason the
section is worth keeping at all. What is *outstanding* is the tracker's job, because the tracker is
what work is dispatched from and therefore the only one of the two anybody has a reason to update.

Two smaller things fell out:

- **The eyeball step is §10.1's control run by hand.** "Each phase is wired into the live session
  and eyeballed" has been described here as the only step that caught the last defect; §10.1 now
  says why it keeps being that step. A person notices a blank statusline. A suite whose strongest
  assertions are about width does not.
- **Phase 7 cannot use that acceptance** — an MCP tool emits JSON consumed by a model, and there is
  nothing to look at. Its acceptance is a round trip: a model given only the tool descriptions
  produces a config that `--check` passes and that renders what was asked for. That is the property
  §12.6 needs and the one no unit test states.

### §13 walked — a four-bullet section hiding a live defect

Two bullets were phrased in ways that read as licence rather than as boundaries:

- **"no resize event"** does not mean the terminal is never resized. The process exits and is
  re-run, and a new width arrives as a different `COLUMNS` on the next tick — a case §5.0.1 and §4
  handle carefully and at length. A sentence under a heading reading *out of scope* must not be
  readable as permission to skip work the document does elsewhere.
- **"no input"** does not mean nothing we emit can be clicked; §3.2 emits OSC 8 links exactly so
  the terminal can make them clickable. The real boundary is that the interaction is entirely the
  terminal's — we write a string and never learn that anyone clicked it. Left unsaid, §13 and §3.2
  contradicted each other.

Also added §9.1.1's boundary here, since this is where boundaries belong: no diagnostic can ever
be about a `command` item's *output*, because `--check` never executes one.

**§13.1** is the real work. `Plain.Length` staying the width metric is still right — a wcwidth
table is a real dependency, §2.7's parity baseline is stated in terms of it, and what this thing
renders is overwhelmingly ASCII. What was missing is the consequence, and the consequence is bad:
**a pane containing wide characters draws wider than the compositor believes, and the rectangle
invariant does not notice**, because §10 bullet 3 measures with the same metric the renderer sizes
with. The assertion this document calls the one that catches ragged padding, height mismatch and
overflow together reports success on a row that visibly overruns its border. That is §10 bullet
7's own warning — *both sides can share a wrong constant* — landing in the measuring instrument
rather than in the bash comparison it was written about, and §10.1's shape a second time: the
suite is not wrong about what it measures, it is silent about what it does not.

The limitation is now recorded as a **test asserting the known-wrong behaviour**, citing §13.1. A
stated limitation only survives a refactor if breaking it breaks something: anyone introducing a
width-aware measurer now fails that test and has to come and decide, rather than finding out later
that the parity baseline moved.

**§13.2 — defect 16, found while verifying §13.1 against the source.** `Plain.Length`
approximating width is accepted; `Plain` being *cut* at code-unit boundaries was never decided.
`PaneRenderer.WrapSegment` cuts with `Plain.Substring(i, innerWidth)` and `TruncateSegment` with
`Plain[..contentBudget]`, and neither checks whether the cut lands between a high and a low
surrogate. A non-BMP character straddling the boundary is split into two lone surrogates —
**invalid UTF-16 on its way to stdout**, not a clipped glyph.

The two are independent, and the emoji case proves it: `🎉` is 2 code units and 2 columns, so the
width metric is accidentally correct there and the slice is broken anyway. §2.6's trap list
requires a hard break never land inside an escape sequence; the equivalent sentence was never
written about a character, so the guard exists for one and not the other and nobody ever asked for
the test. Filed as task #20, both paths, since both cut and only the wrap path is usually
remembered.

### §4.2 walked — a security fix that quietly emptied the cache key

Walked ahead of building it (task #5), which is the right order: everything below would otherwise
have been decided by whoever wrote the code first. Four rulings.

**§4.2.3 is the one that matters.** §5 keys the value cache on `id` + hash of the **resolved argv**
+ `cwd`. That is complete for the argv path, because placeholder values are *in* the argv. For
`shell: true` §4.2 deliberately substitutes nothing — the argv is the identical `sh -c '…'` string
every render and the values arrive through the environment — so **the key no longer covers them.**
A script reading `$CLAUDE_TUI_LINE_VAL_MODEL` is cached under a key that ignores the model: switch
models and it reports the old one for up to `ttlSeconds`, silently.

The security fix caused it, and correctly. Moving an input from argv into the environment was
necessary, and it removed that input from a key defined by *which channel* an input travels in
rather than by *what the child can see*. `CLAUDE_TUI_LINE_PANE_WIDTH` was always in the same
position — exported to every `command` item, absent from the key — so a script formatting itself to
the pane width caches one answer and reuses it at every other width, defeating the exact feature the
variable exists to provide.

Ruled: **the key covers every input the child process can see** — resolved argv, `cwd`, every
`CLAUDE_TUI_LINE_VAL_*`, and the pane width — stated as a property so the next input added is
covered by the rule rather than by someone remembering. §5's bullet now says that and cites §4.2.3
rather than restating it. The cost is named too: a resize becomes a cache miss for every `command`
item, which is correct, and if it ever hurts, the lever is exporting the width only to items that
ask for it — **not** dropping it from the key. Naming the right lever matters, because the wrong one
is the one that looks like a performance fix.

Three more:

- **§4.2.1, bare `{}`.** "The same `{}` / `{other-id}` vocabulary §3.2 defines" carries across one
  member that does not survive the trip: `{}` means *this item's own value*, and a `command` item's
  own value is what it prints, which does not exist yet. New error `placeholder-self-reference`.
- **§4.2.1, literal braces.** `jq '{name: .name}'` is an ordinary argv entry that a naive reading
  turns into a placeholder naming nothing — which this section makes an *error*, so a working
  command becomes a config the framework refuses. Guessing is unavailable, since `jq '{a}'` is
  valid jq shorthand *and* a well-formed reference. Grammar ruled: `{{` and `}}` are literal
  braces; `{…}` is a placeholder only if empty or matching the id charset, so the common case
  needs no escaping and the author never has to learn any of this; everything else passes
  through. `'{a}'` errors, correctly — genuinely ambiguous, caught by `--check` before it ships,
  and the diagnostic can name the repair.
- **§4.2.2, an unavailable source is not an empty one.** The empty-placeholder rule covers an item
  that answered with nothing, not one that *did not answer*. Substituting empty for an
  `unavailable` source collapses §4's distinction one section after it was made, and does it at the
  worst point — handing it to a process that cannot tell "no branch" from "git didn't finish in
  150ms" and will act on the first reading. A `command` item whose placeholder names an
  `unavailable` source is itself `unavailable` and is not spawned.

### §3 walked — and the biggest find is a diagnostic that never existed

Five things in §3/§3.1, and one of them generalised into §9.

**§9.4.2 — every key-name typo in every config object is silent.** §9.4.1 covers unknown *values*
of known keys; nothing anywhere covered an unknown *key*, and the deserializer ignores one by
default. `{"item": "context", "colour": "aqua"}` parses cleanly, renders uncoloured, and is
reported by nothing. So do `"ttl"` for `ttlSeconds` and `"maxLines"` for `maxRows`.

Worse than the unknown-value case in one specific way: an unknown value has a known key to attach
a message to, whereas an unknown key makes the *absence of an effect* the only symptom — and
absence is what a user attributes to having misunderstood the feature. And it lands where §12 is
most exposed: `/edit` and the §12.6 tools have a model write JSON and gate the write on `--check`,
and **a plausible-but-wrong key is the likeliest thing a model gets wrong** — likelier than an
unknown enum value, because the enum sets are short and printed while the key vocabulary is long
and adjacent to every other JSON schema the model has seen. The gate was blind to exactly that.

Ruled: `unknown-key`, **warning** (§9.4.1's test — the rest of the config does mean what it says,
and only this key is dead), message naming the nearest known key, because this is the diagnostic
where the gap between identifying a fault and repairing it is widest. Two riders: the known-key
set is **derived from the config types, never listed** — a hand-maintained list fails toward
reporting valid configs as unknown, and a warning that fires on correct input is one users learn
to ignore — and **§12's gate must surface warnings, not only errors**, since a model-written
config never trips this on purpose.

The rest of §3:

- **Zero rows conflated `absent` with `unavailable` at the type level.** "An item resolves to a
  block: zero or more rows; zero rows means suppressed" gives an item that answered with nothing
  and an item that *did not answer* the identical representation — while §4 distinguishes them
  and §2.11.2's collapse rule reads that distinction. The item model erased it before the
  compositor could honour it, which is the worst place for it, since nothing downstream can
  recover what the type does not carry. A resolved item now carries `present` | `absent` |
  `unavailable` in §4's own vocabulary, with `present` + zero rows ruled not constructible.
- **Two struct fields that do not exist and should not.** `Align` was listed as per-item "within
  the pane": `PaneItemJsonConfig` has no `align`, `PaneAssembler` aligns whole rows by the
  *pane's*, so the document advertised a capability a config cannot express — `color207` again,
  where what is recommended silently does nothing. It is also incoherent, since items pack
  several to a row and three items sharing a row cannot each align within the pane. `Enabled` was
  a second mechanism for "don't render this" where "don't place it" already exists, which is what
  §1 forbids. Both removed rather than left; a struct in a spec reads as the set of things that
  work.
- **Packing runs before wrapping, and it was undefined.** "Packing operates on single-row items"
  plus "a multi-row block occupies its own rows" leaves open which an item is when it is
  single-row *until the pane wraps it*. The readings differ visibly: pack-then-wrap flows a full
  row onto continuation rows; wrap-then-pack promotes any long item to a block, so items stop
  sharing a row and the pane's whole shape changes because one value grew. Ruled pack-first — an
  item's block count is a property of what the provider returned, never of the width it was later
  granted — which is also what §2.6's traps already assume in requiring continuation rows to
  carry their style.
- **"Memoized for the process" vs §5.1's cross-render cache** were two lifetimes for one
  mechanism. Now "for this render", with §5.1 named as the design and per-process memoization as
  the floor.

### §2.3 walked — one defect wearing three keys, and a total function that eats diagnostics

§2.3 and §2.3.1 were the last big unwalked sections. Four findings, and the fourth explains the
other three.

- **`distribute` has three values and two of them do not exist.** §2.3 declares
  `greedy | min-rows | even`; §9.4.1's closed-set list said `distribute ("min-rows")`;
  `PaneDistributeParsing.Parse` maps `"min-rows"` and sends everything else to greedy. Three
  authorities, three answers. The sharp end is §2.4, which recommends `distribute: "even"` *by
  name* to a user who wants a layout that holds still — and what they get for writing it is
  greedy, the reflowing layout that sentence exists to steer them away from. The recommendation
  and the failure are the same act. Once `unknown-enum-value` lands as specced, `even` also
  becomes a `--check` **error on a value this document recommends**. Ruled all three into the
  language; `even` divides the remaining extent equally and ignores intrinsic measurement
  entirely, which is the point rather than a shortcut — content-independent widths are what makes
  a layout stop moving. Tasks #22/#23.
- **`min-rows` replaces the fixpoint; it does not run on top of it** — and the spec never said so,
  presenting the fixpoint as unconditional and then presenting min-rows as though it slotted in
  beside it. The code branches correctly on a fact this document did not contain. It has to be a
  branch: `rows_i(w)` is already wrap-aware, so composing the fixpoint afterwards re-measures every
  `content` pane at the width min-rows deliberately granted it, shrinks it to its longest wrapped
  row, and the monotone clamp makes that permanent — **the surface comes out taller than the `T`
  the search proved achievable**, with every pane still individually legal. Stated as the property:
  exactly one width-resolution policy runs per split. Also found the seam gap this implies — §10.6's
  three fixpoint tests reach the resolver through `measureOverride`, which the min-rows path does
  not take, so none of them can run against it. Task #25, with `minWidth`'s missing upper bound
  (it is `R`, never intrinsic — a `content` pane narrower than its content is exactly what the
  search needs to consider).
- **§2.3.2, keys that are valid, spelled right, and meaningless where they are written.**
  `{"split": "horizontal", "distribute": "min-rows"}` is a legal value of a legal key that nothing
  reads; the resolver returns before the branch. Same for `gutter` on a horizontal split. §9.4.1
  can't see it (key known, value legal) and §9.4.2 can't either (key not unknown) — a third
  silence, and the one with the best alibi, because the config reviews clean. Ruled
  `key-not-applicable` (warning), message names where the key *does* apply, because an author who
  wrote it there has the axis convention backwards. Declined the symmetric fix of giving `gutter`
  a row meaning on horizontal splits: a gutter row is a permanent terminal row spent on nothing at
  `refreshInterval: 1`. Task #24.

- **§9.4.3 — why none of §9.4.1 is implementable as the code is shaped.** The best find of the
  walk, and it came out of asking why a section that has been in the document for weeks reports
  nothing. `PaneAlign.Parse`, `PaneValign.Parse` and `PaneDistributeParsing.Parse` are **total
  functions into their enums**: a `_ =>` arm answers every input, so by the time any caller holds
  the result, the fact that the input was not in the language has been *consumed* — by the one
  function positioned to notice it. `--check` cannot report what it cannot be told, and no care
  inside `--check` recovers it, because the information is gone before `--check` runs.

  `OverflowMode.Parse` in the same codebase returns `OverflowMode?` and answers `null`. Both
  shapes are already here, and **the three keys §9.4.1 singles out as failing silently are exactly
  the three with the total shape.** That is the mechanism, not a coincidence — one defect with
  three instances rather than three defects. Ruled: a closed-set parse reports the unrecognised
  case and the *caller* chooses; the renderer's caller substitutes the default so §7 is untouched
  and no output changes, `--check`'s caller reports the diagnostic. Explicitly rejected the
  inverse — a total parse plus a validator that re-reads the raw strings — as the two-authorities
  defect in its worst form, where you discover the drift via a `--check` that passes a config the
  renderer ignores. Task #22, and it blocks #23: adding `even` to a switch fixes one key on one
  day; changing the shape is what stops the next key arriving pre-broken.

### §2.4–§2.9 walked — the spec walk is complete, and it ended by auditing itself

Last unwalked stretch. Every section of the spec has now been walked.

- **§2.5.1 — three rulings intersect on `CLAUDE_TUI_LINE_PANE_WIDTH` and nobody looked at the
  intersection.** §2.5 exports the pane's inner width to `command` providers; §4.2.3 (mine, two
  sessions ago) put every input the child can see into the cache key; §2.3 resolves sizes by a
  fixpoint of up to three passes whose entire purpose is changing those widths. Compose them and a
  `command` item is **a cache miss on every pass** — the process spawns up to three times per
  render at `refreshInterval: 1`, with a correct statusline and no symptom. It is also genuinely
  circular in a `content` pane, whose width is *defined as* the measurement of its content; the
  monotone clamp does not save that, because it constrains the pane's request and not the script's
  output. Ruled: one spawn per render at the first-pass grant, and a `content` pane's items are
  measured with the variable **unset**. The rule underneath, for whatever pane kind comes next —
  *a pane exports its width only if its width does not depend on what the export returns.* Task #26.
- **§2.4.1 — "the rightmost contributing pane" assumes a surface has one.** A root horizontal split
  does not: its children stack, so each row's rightmost cells come from a different child with its
  own background. Decided once, the backgrounded pane's colour band ends short on exactly the rows
  that carry it. Per-row now. Same edit records that the trim is not a layout step — it removes
  padding from some rows and not others, so §10's rectangle invariant is a property of the composed
  buffer asserted *before* the trim, and a test measuring the emitted line passes only on a surface
  where no row had trailing space. §10.1's problem, one layer down. Task #27.
- **§2.6's vertical marker was never budgeted.** The horizontal case explicitly reserves the
  marker's width; the vertical case said "ends with the marker", and the last surviving row is
  routinely full — it is the row that forced the wrap being truncated. Appending puts it over the
  pane width, which §2.4 rule 1 names as the ugliest failure available. One rule now, applied twice;
  stating it once per axis is how the two got to disagree.
- **§2.8 — §2.6 and the degrade ladder were two authorities on `maxRows`.** §2.6 reads as though
  pane-level `maxRows` clips during layout, which would mean a pane exceeding *its own* budget gets
  rung 4 — the harshest — while a surface exceeding its budget gets rungs 2 and 3 first. Same key,
  same meaning one level up, opposite severity. The ladder owns both budgets; §2.6 describes what
  rung 4 does, not when it fires. Also broke rung 3's tie (equal heights are the *normal* state in a
  vertical split, since §2.4 pads siblings to a common height — leaving it unbroken makes a ladder
  justified by determinism depend on enumeration order), and closed the gap in the subsection
  titled **"Clipping must close the border"**: under three rows a bordered box *cannot* close, so
  the rung written to prevent the "crashed" render produces it. Border suppresses under 3 rows —
  the height-axis twin of `MinUsableWidth` — unless the author declared that `maxRows` themselves.
  Task #29.

- **§13.3 — a section number is a reference, and four of them resolved to nothing.** The find of
  the walk, and it came out of noticing that **§2.9 is cited nine times and does not exist**. That
  prompted checking every `§N.M` against the document's own headings:

  | cited | times | |
  |---|---|---|
  | `§7` | 27 | failure behaviour — `§7.1` was a subsection of nothing |
  | `§2.9` | 9 | the two-pane worked example, unheaded at the tail of §2.8 |
  | `§10.6` | 3 | bullet 6 of §10's list, cited in subsection form |
  | `§4.3` | 1 | derived items |

  This is §9.6.1's own registry rule — "a code that is not in it does not exist" — which the
  document applies to diagnostic codes and had never applied to itself. Not hypothetical: §11
  defines a phase's acceptance as **"Acceptance is §2.9"**, and §9.4's whole severity argument
  turns on "§7 makes the renderer cope."

  The distribution is the part worth keeping. **The most-cited reference in the document is the
  most thoroughly missing**, and that is causal rather than ironic — a reference used constantly is
  one every reader already knows the meaning of, so nobody follows it, so nobody learns it goes
  nowhere. Frequency of citation is *negatively* correlated with the chance anyone checks. The
  single-citation dangle is the one a reader would most likely have caught.

  §7 and §2.9 fixed in place. §10.6 and §4.3 plus a three-line CI check are task #28 — and the
  check is the point, because all four survived many careful readings: prose citing a section reads
  correctly whether or not the section is there.

- Also corrected: §2.3 restated §2.9's measured anchor width as **43** where §2.9 measures **66**,
  for the same pane in the same config at the same width. §2.9 says outright that its integers are
  *measured, not asserted*. A second copy of a measured output is a hand-maintained duplicate, and
  the copy that goes stale is always the one no test reads.

### §9.4.3 resolved, and the round-trip that resolved it is the finding

`--check`/`--check --json` landed (Program.cs, ConfigCheck.cs; build clean, 1122/1122). Task #22 is
**done** — the implementor had already reshaped all three parsers to `ParseCore → T?` with a public
`IsUnrecognized`, independently arriving at exactly what §9.4.3 rules for, with
`ConfigCheck.CheckPaneEnums` as the second caller. #23 (`even`) is unblocked.

But we spent a full round-trip disagreeing about what the code said, and **both readings were of
real files**: §9.4.3 quotes `Pane.cs` at `8306620`, and the fix lives in an uncommitted working
tree. This document's own two-authorities defect, with the second authority being **time** — and
it is the variant with no possible symptom, because nobody is wrong. The two readings simply do not
refer to the same thing, and neither side can tell from its own evidence. §9.4.3 now pins the
revision it quotes and is kept in past tense rather than deleted; the rule it states governs closed
sets not yet written.

Two rulings the implementor asked for, both now in §9.6:

- **`path` is absent for `code: "usage"`**, not `""`. Same reasoning as `diagnostics` being absent
  rather than `[]` one field over — `""` is not "no path", it is the claim that the path is the
  empty string, and it survives a null check and concatenates into `could not read `. Getting the
  two fields different would be worse than getting either wrong, since a consumer that learned the
  `diagnostics` rule would assume it generalises.
- **No catch-all around `--check` is correct** — an internal exception reported as a clean config is
  §7.1's render-wrong class in the one command that exists to find defects. But the consequence
  needed writing down: the alternative to swallowing is a runtime exit code the registry does not
  define. So **{0,1,2,3} is the contract, not the range**; anything else is a crash in this tool
  rather than a verdict about the config. A caller testing `exit == 0` is right by accident; one
  switching on the four falls through the bottom exactly when it should be loudest.

### §9.3.1 and §9.6.2.1 — two gaps that were blocking `--items`, both mine to fill

The implementor stopped and reported rather than freehanding, which was right: both were missing
spec content that ships to users as documentation, not code questions.

- **§9.3 mandated a fixed synthetic `StatusInput` and never said what is in it.** "A fixed
  non-randomised payload" is not a value; two people implementing that sentence produce two
  fixtures, and this one is shared with `--preview` precisely so the two can't disagree. §9.3.1 now
  gives it as a literal, with four rules that each rule out a fixture someone would otherwise write:
  **every field populated** (real payloads omit `pr` and `vim`, and an item whose example renders
  empty reads as an item that produces nothing — completeness beats realism here); **redundant
  fields must agree** (34% *is* 68k of 200k; nothing enforces it, and an inconsistent fixture
  depicts a state Claude Code cannot produce, which `--preview` then renders faithfully);
  **deliberately away from thresholds** — the implementor's instinct was to sit `context` at ~82% so
  the example exercises the colour ladder, and I went the other way, because this is the resting
  baseline users compare against and `--colors`/`--preview` are where the ladder belongs; and
  **visibly synthetic**, since §9.3's "admit it is invented" goes to stderr, the stream most likely
  to be discarded.
- **`reports` was required on all sixteen items and demonstrated on one.** §9.6.2.1 writes all
  sixteen. The three `Semantic` ones carry a second sentence naming what drives the colour, because
  for those three the colour *is* information: `rate-limits` follows the **higher** of its two
  windows, `engram` is a state rather than a magnitude. Both change what a sensible `thresholds`
  override looks like and were otherwise only learnable by reading the implementation.
- Recorded alongside it: **`example` is rendered, never stored** — the `"⎇ main"` in §9.6.2's shape
  is `BuildDefaultSegment`'s output, not a table entry, so that field needed zero hand-authored
  values. And **a builder disagreeing with a `reports` string is a finding, not a string to
  reword** — rewording converts behavioural drift into documentation.

### §13.3 closed — four dangling references, three different fixes, and a CI check

Task #28 done. `tools/check-citations.sh` compares every cited `§N.M` against the document's own
headings; **68 of 68 resolve**, and it was proven able to fail by injecting a `§99.9` into a copy
before being trusted. Wired into a new `.github/workflows/ci.yml` (the repo had no CI at all),
running ahead of build and test so a broken reference is still reported when the build is red.

Three things worth keeping from closing the four:

- **The remedy was not uniform, which "four dangling references" conceals.** §7 and §2.9 had
  content and no heading → promoted in place. §4.3 had content under the *wrong* heading — derived
  items were introduced inside §3.2's hyperlink example, reading as part of linking rather than as
  one of §9.6.2's four item kinds → promoted to a real §4.3, which is what the single citation was
  already describing.
- **§10.6 reversed my own earlier ruling.** The first draft of §13.3 said to promote §10's bullets
  to subsections rather than rewrite the citations, on the principle that bullets renumber silently
  while headings are visible when they move. The principle holds; the application was wrong —
  **§10.1 already exists as a heading and is not bullet 1**, so promoting would have given `§10.1`
  two meanings in one document. That converts a dangling reference into an *ambiguous* one, which
  is strictly worse: a dangling reference at least fails when followed. Citations now read "§10
  requirement 6", §10 says so explicitly, and the general rule keeps a precondition — promote when
  the number is free.
- The script itself shipped a bug worth noting: `sed 's/x\+//'` is a GNU extension that BSD sed
  accepts and silently does not apply, so the first run reported all 69 citations dangling. Loud,
  and therefore harmless. The same mistake in the other direction reports clean forever, which is
  why the script now refuses to run if it extracts zero headings.

### Open, and needs Jim: GitHub Actions is billing-blocked

The CI workflow is correct and has never run. Both jobs on `088a759` were refused before starting:

> The job was not started because recent account payments have failed or your spending limit needs
> to be increased.

So `main` now shows a red ✗ that says nothing about the code — which is the failure class this
document spends its time on, a signal that cannot mean what it appears to mean. It resolves in the
Billing & plans settings, and the workflow will work unchanged once it does; nothing about it is
worth rewriting in the meantime, and deleting it would discard correct work over a one-minute fix.

Branch protection does **not** require status checks, so this does not block pushes — verified by
the fact that the push carrying it succeeded.

In the meantime `./tools/check-citations.sh` runs locally in under a second and is now documented
in the README's contributing section, so the check has value today rather than only after billing
is sorted.

### Audits run tonight that found nothing

Both recorded because a clean result is only worth having if it is written down — otherwise the
next person re-runs it, or worse, assumes it was never checked.

- **Every CLI flag the plugin commands and README invoke is defined in §9.** `--check`, `--json`,
  `--items`, `--colors`, `--preview`, `--columns`, `--config`, `--version` — all present. The only
  other flags in the docs are git's own, inside a config example.
- **§12.6 is fully specified, not merely mandated.** Checked specifically for the gap that blocked
  `--items` twice tonight — a section that requires something to exist without saying what it is.
  §12.6 has a wire contract, an environment ruling, per-tool rows, CAS for concurrent writes, an
  explicit allowlist of files a tool may write, and version reporting. It is implementable as
  written. Worth knowing given it is cited 26 times, which by §13.3's own finding makes it the
  least likely thing in the document to have been checked.

### The two-spec audit — and it was not the defect I went looking for (v2 §14)

`SPEC.md` (v1, 24KB) sits at the repo root beside `SPEC-V2-FRAMEWORK.md`, and `README.md` pointed
contributors only at the latter. That is the shape of every two-authorities defect found tonight,
so I checked whether v1 was stale enough to mislead someone.

**It is not stale.** v2 cites it as live authority in four places — §6b (config re-read every
render), §6 (`Plain.Length` is the width metric), `SPEC.md:353` (an empty surface emits zero
bytes), and Phase 1 as a prerequisite. Archiving it would have broken four working citations.

**The actual finding was the opposite shape.** `SPEC.md` §10 requirement 2 carried the entire
build-and-deploy discipline — one command produces the artifact, identity by hash rather than
mtime, and *writing into `publish/` is a deploy that replaces a program the user is running, so it
requires approval*. `SPEC-V2-FRAMEWORK.md` contained no occurrence of the string `publish/` or
`deploy` at all. A contributor following the README's single pointer would never encounter the
rule, and the reason it has held anyway is that it gets restated by hand in session messages —
which reads like the rule working and is in fact the rule being carried by something that does not
persist.

Relocated to **v2 §14**, moved rather than copied, with §14.4 recording the generalisation:

- *"Is this document stale?"* is the wrong audit question — it only finds authorities that
  disagree. Ask *what does this document say that no reader of the current one will ever see?* A
  superseded rule surfaces the moment two readers compare notes; an unreachable one has no symptom
  at all.
- A rule enforced by repetition is not enforced. The test is whether a competent stranger reading
  only what the README points at arrives at the same behaviour.
- `check-citations.sh` cannot catch this class. It is a closed-world check on references *within*
  one document; this defect is about what is absent from that world entirely.

Also fixed on the way through: `SPEC.md` now carries a status header saying which of its rulings
still stand and that new rules do not go there; the README's Contributing section names all three
documents and their standing; and STATUS.md's own citation of "SPEC.md §10.2" is gone — v1's §10 is
a bare numbered list with no subsections, so `§10.2` was the same §10.N ambiguity §13.3 ruled on,
one document over.

### §12.2 promised three rules and had four — and the fourth is the silent one

Found while applying §14.4's question to the rest of the repo. `SPEC-V2-FRAMEWORK.md` §12.2 read
**"Three rules, none optional:"** above a list of four. The fourth is *"an entry captures every
artifact, not the one its command intends to change"* — the rule whose violation is explicitly
silent: the rollback runs, restores something real, reports success, and leaves the damaged file
untouched. A reader reconciling §12.2 against `docs/backup-ledger.md`, which had a section headed
"The three rules", would have concluded the fourth was an editing error and dropped precisely the
one worth keeping.

No behavioural drift had happened yet — the doc carried the same requirement as procedure step 6.
The two documents were describing the same thing at different ranks, which is how they would have
drifted. Both now state four rules; step 6 cites rule 4 instead of re-arguing it, so the rationale
has one home.

The doc/spec split here is deliberate and stated, unlike §14's: `docs/backup-ledger.md` says in its
own header that it is §12.2 restated as a procedure, and it exists in one file because four command
prompts read it at runtime and four copies would drift. That is the right shape. The count was the
defect, not the split.

### `tools/check-counts.sh` — the second mechanical doc check

Two instances of "a stated count disagrees with the list under it" found by hand (§8's segment
count, then §12.2's) is the threshold at which it stops being a reading problem. The script finds
lead-in sentences that name a count and compares it to the list beneath.

It found two more, and both were real:

- **`SPEC-V2-FRAMEWORK.md` §2.4** said emptiness "applies at two levels, differently" above three
  bullets. Defensible — there really are two levels — but the pane level splits on whether the user
  named a size, and a reader counting bullets against the sentence has no way to see that. Now says
  two levels, three cases, and names the split.
- **`STATUS.md`** said "Two things worth keeping from closing the four" above three bullets, in the
  section documenting `check-citations.sh`. A third bullet was appended and the number was not.

**The tuning was most of the work, and it is the part worth recording.** The first version reported
twelve sites, ten of them noise: it took the *last* numeral before the colon, so `SHA-256` promised
256 items, `§9.6` promised six, and `defects 3–6` promised six. A check with that hit rate gets
ignored, and an ignored check is worse than no check — it occupies the slot a real one would have.
The rules that fixed it: strip inline code, section references, dotted numbers, hyphenated names and
ranges; take the **first** count rather than the last, since later numerals in a sentence are
incidental; require the count to be followed by a word, which drops numerals that end a clause; skip
any lead-in with no list under it at all, which is what keeps arithmetic prose out. Twelve reports
became two, and both were defects.

Proven able to fail before being trusted, the same as `check-citations.sh` — run against a
reconstruction of the §12.2 defect, where it reports `says 3, lists 4` and correctly stays silent
about a neighbouring "Two severities:" list that is right. Wired into CI beside the citation check.

### §12.5 carried the one instruction the procedure doc says is wrong — and Phase 6 is live

The same audit, applied to the command prompts. `commands/revert.md` is 121 lines of rulings;
§12.5 was two paragraphs. That gap is not itself a defect — the spec should hold decisions and the
prompt the procedure — but four decisions were missing from it, and one of them was actively
harmful.

**§12.5 said "it verifies the SHA-256 of what it restores against the ledger."** That is the
collapsed instruction `docs/backup-ledger.md` calls out by name as wrong, in a section written
specifically to correct it: it does not say *which* file, and the obvious reading takes the live
`settings.json` — which is **supposed** to differ at revert time, because claude-tui-line is
installed now and was not when the backup was taken. An implementor building revert from §12.5
alone produces a command that reports "you hand-edited this" and stops **on every single run**.
The escape hatch refusing exactly when it is reached for, and the report would read like the check
working.

The doc had been fixed. The spec section had not, and the spec is what §12.6's MCP `revert` tool
and the CLI would be built from. Task #9 (Phase 6) is in progress right now, which is the only
reason this was worth interrupting the audit for.

Three more decisions promoted into §12.5, all of which existed only inside the command prompt:

- **The restored script has three cases, not one.** §12.5 had "missing → restore the copy". The
  third case — *present, but the hash differs* — is the one with no symptom: the revert succeeds,
  the statusline renders, and it is not the statusline that was backed up. Report and let the user
  choose; never overwrite their edit.
- **An unreadable ledger stops the command.** No reconstructing a statusline from the repo or the
  conversation — a fabricated statusline the user believes is theirs is worse than none. Offer to
  remove the `statusLine` key, which is an honest state.
- **Revert deliberately does not restore `claude-tui-line.json`.** This one was a contradiction
  waiting to happen: §12.2 rule 4 makes every entry capture the config, so restoring it is
  *available*, and a spec-only reader would plausibly do it — destroying layout work the user never
  asked to touch. `/edit` owns config rollback; revert answers "put my old statusline back".

Also tightened: `commands/revert.md` cited "Rule 1" of a file that now has two numbered lists both
starting at 1. It meant the one in "Writing `settings.json`" and now says so. Same ambiguity class
as §10.N, one document over — and unlike section numbers, no script catches "Rule N".

### §12.7 — `setup` had no spec section at all

§12 documented `migrate`, `edit`, `revert` and the MCP server. It did not document `setup` — the
command the README sends every new user to, and the only one that can create the `origin` entry.
Not a thin section: **no section**. Five decisions lived exclusively in `commands/setup.md`, which
the CLI and the MCP server do not read:

- **The SDK check must run in the project directory.** `dotnet --version` reports the SDK selected
  for the *current* directory, and a `global.json` above it can pin another. Check in one place,
  build in another, and a passing toolchain check is for an SDK the build never uses — after which
  the failure gets reported as a build error rather than a toolchain one.
- **An unset path variable names a real directory, and a worse one.** Unset,
  `"${CLAUDE_PLUGIN_DATA}/bin"` expands to `/bin`, making the build a release publish into the
  system binary directory. `settings.json` then points at `/bin/claude-tui-line`, and the preview
  confirms it renders. The `:-` fallback is load-bearing, not padding.
- **Verify by running the value, not the variable.** Read `statusLine.command` back out of
  `settings.json` and run that string verbatim. The build already proved the path; the untested
  thing is *the expansion*, because `settings.json` does not interpolate variables. Test the
  variable and the preview renders perfectly while the install is broken — and §12.5's revert
  already verifies the other way, which is two commands answering one question differently.
- **Say the preview payload is minimal**, or a correct install reads as half-broken and the user's
  first act is debugging something that works.
- **If the backup was a `checkpoint`, say so and give the timestamp.** Bare revert restores the
  older `origin` — correct by design, and not what someone told "your statusline is backed up"
  expects.

Numbered `12.7` and therefore printed after the MCP server despite running first, because §12.6
already owns nine subsections and renumbering to satisfy reading order would break every existing
citation. The section says so in its first line rather than leaving the reader to wonder.

It also states how `setup`'s build destination relates to §14: a third location, deliberately.
§14 governs `publish/`, the deploy target a developer's own live statusline runs, which is why
writing there needs approval; `setup` writes into the plugin's data directory, which belongs to the
installed plugin and cannot collide with a working tree.

### The sweep finished on `migrate` and `edit` — and the ledger doc contradicted a command it governs

Applying §14.4's question to the last two command prompts. Both were thin sections over long
prompts, which is the shape that produced §12.5 and §12.7.

**The contradiction.** `docs/backup-ledger.md` opened with *"Every command that writes
`settings.json` **or** `claude-tui-line.json` follows this. No exceptions, no abbreviations."*
`commands/migrate.md` step 8 said *"Do not run the ledger procedure again here."* Both documents
are handed to the same model in the same invocation, and step 2 tells it to read the ledger doc in
full. Migrate writes two files; read literally, the doc demands a second entry, and that entry
captures the config migrate has *just written* — a permanent restore point (rule 1 forbids removing
it) for a state that existed for one instant and that nobody would ever want back: migrated config,
original `statusLine`.

Migrate's reasoning was right and its framing was wrong. It read as an exception claimed by a
caller against a document that says it has none, which is the weakest possible position for a rule
to be in. The fix was to find the principle that makes it not an exception: **one entry per
invocation, taken before the first write — not one per file.** That is the same ruling as rule 4
("an entry captures every artifact") seen from the other end. An entry holds everything, so nothing
needs a second entry. Now in the doc's scope line, in §12.2 rule 4, and cited rather than argued at
migrate's own call site.

**Four more decisions promoted into §12.3.** The sharpest is the timeout: `ttlSeconds` and
`timeoutMs` default to 30 s and 150 ms, and the user's original statusline was a program that could
take as long as it liked. Every fragment lifted out of it becomes a `command` provider under a
budget it never ran under, and over-budget means killed and rendered as nothing (§7) — a loss that
looks identical to never having migrated the element. Also: colour preserved *by kind* (ANSI names
stay theme-mapped, specific shades become 256/hex and need `colorSystem`); write to the path §5's
search order resolves to, never the default by assumption, or the renderer reads the file you did
not write while every step reports success; and write the config *before* repointing `statusLine`,
since the binary starts running once a second the instant that key changes.

**§12.4 said nothing about what recovery is.** `commands/edit.md` carries the whole of it. Promoted:
a rollback restores the entry's `configCopy` over the config, **not** its `statusLine` — restoring
a key `/edit` never touched changes nothing, leaves the broken config in place, and looks exactly
like a fix. A `configCopy` of `null` rolls back by *deleting* the file. Rule 4 is verified at the
call site and the command stops if the fields are missing, because nothing inside the procedure can
answer whether what it saved covers what its caller is about to change. Seeding a config is a write,
so the checkpoint precedes it — otherwise the rollback target is the seeded file and "no config,
defaults apply" stops being reachable. And an edit is verified by previewing at 80 and 60 columns
as well as the terminal's width, because a passing `--check` proves nothing about layout.

§12.2 also gained the ledger's encoding of absence — `configOriginalPath` present with
`configCopy: null` — which lived only in the procedure doc. *Nothing was here* and *this ledger
cannot say* are opposite facts calling for opposite recoveries (delete the file, or leave it
alone), and three omitted fields cannot distinguish them from an entry written before configs were
captured at all. Third instance in this project of absence needing a distinguished value rather
than a missing key, after `"statusLine": null` and §12.6.9's `revision: "absent"`. Assume the
fourth.

That closes the sweep: every document in the repo has now been read against the question *what does
this say that no reader of the current one will ever see?*

### `--items` came back with four blockers, and the spec was wrong on two of them

The implementor built `--items --json` (1129/1129 green) and stopped on four things rather than
guess. Two were spec defects, one was a genuine gap, one was an unconfirmed assumption. Ruling on
all four is what unblocked Phase 5's remaining flags.

**The glyph that was never there.** §9.6.2's shape showed `"example": "⎇ main"` for `git-branch`,
framed as real `BuildDefaultSegment` output. `BuildGitBranch` is `SingleColor("green", branch)` and
always has been. **CAPTURE.md line 60 settles which side was wrong** — the bash statusline renders
segment 2 as a bare branch name in green — so the code was right and the glyph existed only in this
document.

It existed in four places by then: §4.3's extract-ordering example, §9.6.2's shape, §9.6.2.1's
description of that shape, and §9.6.2.1's argument for why `example` replaced "default format",
which used the glyph as its *proof* that a builder emits decoration no format string could express.
That last one is the finding. **An illustration invented to explain one rule was later cited as
evidence for a different rule**, and nothing by then marked it as invented. The argument survives —
`worktree` really does emit `worktree:NAME(BRANCH)` — but it survived by luck, resting on a false
instance. Neither mechanical check can catch this: both compare documents against themselves, and
this was a claim about the code. An example naming a specific behaviour is an assertion about the
implementation and ages exactly like one.

Worth noting §9.6.2.1's own rule is what produced the find: *"if the implementation disagrees with
one of these strings, that is a finding, not a string to quietly correct."* It paid for itself
inside one session, and it paid out against the spec rather than the code.

**A fixture that guaranteed one item could never render.** §9.3.1's literal sets
`output_style.name: "default"`, and `BuildOutputStyle` deliberately suppresses any style name equal
to `"default"` — correctly, since it is noise on a statusline. Together they meant `--items` would
report `output-style` with an empty example permanently, and a user would correctly conclude the
item produces nothing. That directly contradicts the fixture's own rule 1: *whatever an item needs
in order to render, the fixture has.*

`"default"` is the **realistic** value, which is exactly how it got written — "what would a real
payload hold here?" is the right question fifteen times and the wrong one once. The rule is not
populate every field; it is **populate every field with a value that survives the renderer**, and
the only fields where those differ are the ones with suppression logic behind them, which is
invisible from the payload side. Now `"Explanatory"`, with a paragraph forbidding the revert.

The same audit caught a milder version two lines down: the Engram canned value read "whatever shape
makes `engram` render *present*" — a constraint stated as an outcome, leaving the value to whoever
implements it. Pinned to the implementor's own choice (3 memories, `◉ recalled`), which converts an
accident into a decision.

**Bare `--items` had no specified form.** §9.6 promises every one of the four flags a non-JSON
default and demonstrates one only for `--preview`. The implementor left the CLI unwired rather than
invent a layout — right call. New §9.6.2.2 rules it: a three-column table that is a *view of*
`ItemsCommand.Build()`'s result rather than a second registry walk; groups labelled by what
`default` means rather than by its name, since the person reading plain text is the one who does
not know yet; no key lists, because a truncated §4.1 schema reads as complete; `Plain` only, never
ANSI, even on a TTY, because colour-when-interactive is two surfaces that drift. And the ruling
that keeps it from calcifying: **the plain form is a convenience view and the JSON is the
contract** — columns may come and go without that being a compatibility break. Every
human-readable output nobody labels that way eventually becomes a second frozen surface.

**`version` confirmed** as the same string `--version` prints, through the one
`AssemblyVersionInfo` accessor the implementor extracted. A consumer comparing the two must never
see two answers — §9.7's drift test premise, one surface lower.

### Then the same defect happened twice more, inside the fix for it

Having written that an invented example ages like an implementation assertion, I extracted every
string literal `SegmentBuilder` actually emits and swept the spec against it. Two of the
disagreements it found were **mine, from the previous twenty minutes**:

- §9.3.1's newly-pinned Engram value said it renders as `◉ recalled`. `BuildEngram` renders
  `engram:3 ◉ recalled` — `◉ recalled` is the `Verb` going *in*, not the segment coming out.
- §9.6.2.2's brand-new `--items` table showed `directory` as `~/code/acme-web`. `BuildDirectory`
  is `Basename(cwd)`, so it renders `acme-web`.

Everything older was clean — `ctx:62% (125k/200k)`, `5h:30% / 7d:85%`, `effort:high`, `PR #42`,
`worktree:NAME(BRANCH)` all match the builders exactly. So this is not a decay problem, and it is
not carelessness either. **Writing an illustrative value is frictionless; verifying one means
reading a builder.** Every example therefore defaults to invented unless something forces the
check, which is why the failure reproduced immediately in the hands of the person who had just
finished describing it.

§9.3.1 now separates the two registers explicitly: `Facts` and `Verb` are the fixture and are
normative; `engram:3 ◉ recalled` is a description of what the builder does with them, and **if it
ever disagrees, the builder is the fact and the clause is the finding.**

**The mitigation is mechanical and now specified.** `--items`' `example` field is
`BuildDefaultSegment` run against §9.3.1's fixture — so once the flag is wired, a check that runs
`--items --json` and compares this document's example values against it is a document-versus-*code*
check. That is precisely the class `check-citations.sh` and `check-counts.sh` cannot reach, both
being closed-world. Third mechanical check, real target, and the case for it is three failures in
one session rather than a hypothetical.

### The third check exists, and its first run was a false positive

`tools/check-examples.sh` is built and wired into CI. It is the first check here that runs the
code: `--items --json`, then two exact rules over tracked markdown — a fenced `"example": "…"` must
be a value some item renders, and a row inside a block that reproduces `--items` plain output must
carry that item's live example. The block has to identify itself by its own `Item kinds:` pointer
line rather than by resembling a table, because elsewhere the document legitimately shows items with
a `format` or `extract` applied and those are *not* the default render. §4.3's
`worktree:api(feature/ABC-123)` is the case that would otherwise have been reported as a defect on
day one.

Proven to fail before being trusted: against a fixture carrying a stale `⎇ main`, a stale
`~/code/acme-web`, and an Engram string missing its verb glyph, it reports all three and exits 1.
With no binary, or with an empty item list, it exits 2 — never a clean report it did not earn.

**Reviewing it caught a bug in it.** `dotnet run` writes restore and build chatter to *stdout*, not
stderr, so the fallback path would have handed `jq` MSBuild output with the JSON glued to it. Local
testing used a stub binary and never went near that path. Fixed by quieting the build and keeping
everything from the first line that opens the object — deliberately not `tail -1`, which works only
while the serializer emits one line and would break silently-to-the-reader the day anyone sets
`WriteIndented`.

**Still unverified: the `dotnet run` path has never actually run.** No binary has been built in this
tree (the implementor holds uncommitted work in `src/` and building would race it), and CI has never
executed at all — Actions billing is blocked, which is why `main` shows a meaningless red ✗. So the
extraction logic is tested against both single-line and pretty-printed JSON with chatter prepended,
but the real invocation is not. It fails loudly (exit 2) rather than passing vacuously if it is
wrong, which is the property worth having until someone can run it.

**Its first run against the real tree flagged this file, and the flag was wrong.** The passage above
recording that `"example": "⎇ main"` had been removed is a sentence *about* a retired value, not a
claim that it renders. Correct by the letter, wrong by the point — and a check that forbids a
retrospective from quoting the value it is retiring makes the project unable to describe its own
defects, which is how a check gets switched off. The distinction that survived is that **a fenced
block asserts and prose discusses**, so rule 1 is fenced-only. The original defect lived in a fenced
shape, so the narrowing gave up nothing.

That the first execution produced a false positive rather than a finding is the more useful outcome.
The three real instances had already been fixed by hand; what was untested was whether the guard
could sit in CI without crying wolf. It could not, until it was narrowed. **The bar for a check is
not "finds things" but "is worth reading every time"** — `check-counts.sh` says so in its own header,
and this is the second check to have to earn it.

Closes task #30. All three doc checks are now in CI, and the closed-world gap named in §13.3 is
closed for the sixteen builtins' default renders — and only for those. Every other worked example in
the specification remains an unverified assertion about the implementation.

### Getting ahead of `--colors` instead of reviewing it afterwards

`--items` came back with four blockers, two of which were spec defects. Rather than wait for the
same thing on `--colors`, §9.6.3 got the audit first — and it had the identical hole §9.6.2.2 was
written to fill, plus one consequence sharper than that case. §9.6.3.1 is the result.

**Bare `--colors` prints ANSI, and it is the deliberate exception to §9.6.2.2's plain-only rule.**
§2 requires each swatch rendered in its own colour; §9.6.2.2 says bare `--items` is never styled,
even on a TTY. Both correct, and they collide on the same implementer within days — with the
absolute-sounding rule being the one most recently implemented. The principle that makes both
follow: *is the styling the value, or a coat of paint on the value?* An item's example survives
stripping; a swatch stripped of its colour is nineteen rows reading `olive`, `teal`, `fuchsia`,
which is the guessing the command exists to end.

Two more found by reading the code against the section:

- **The curated list already exists** as `ColorResolution.StandardColorNames`, serving §6.2.1.
  §9.6.3 said it "exists nowhere today" — stale. `--colors` is its second consumer, not its author.
- **The round-trip test proves less than the section claims**, exactly on the three entries that
  are not colours. `ResolveLiteral` returns `style.Foreground`, non-nullable, so "non-null result"
  silently degrades to "did the parse succeed" — right for the sixteen, vacuous for `default`,
  `dim` and `bold`, which parse as decorations yielding `Color.Default`. Assertion split, and the
  count of the three pinned so a real colour cannot join them via a rename.

### The renderer writes to stderr for two commands that never read it

§7: render notes go to stderr in the human form so "stdout stays byte-comparable either way, **so
`/migrate` can diff a preview against the original script's output**." That split was designed for
`/migrate` and `/edit`. Neither had ever been told. Both captured stdout alone.

The cost is not cosmetic. A dropped pane and a `maxLines` cap each arrive as nothing but a token
missing from the diff, and the obvious response — re-map the element — is wrong for both. One needs
the width reported to the user, the other needs the cap raised. The note is the only thing that says
which. `/edit` now captures on both the "before" and "after" previews and diffs them, because that
comparison is the sole difference between "my edit dropped a pane" and "a pane was already being
dropped at 60 columns" — and `--check` reports neither, since neither is a config error.

Same class as everything else this week: **what does this say that no reader of the current document
will ever see?** The answer was in §7, one line, describing a benefit to a file that never got the
memo.

### A resolving citation can still point at the wrong section

§9.6.3 cited §12.2 for the colour ruling. §12.2 is the ledger; the ruling is §12.3's.
`check-citations.sh` passed the whole time — it proves a section exists, never that it says what the
citing sentence claims.

Measured before proposing a fourth check: only **15 of 630 citations** carry a quoted phrase that
could be verified mechanically, and several of those quotes are deliberate paraphrase a checker
would flag wrongly. A check covering a fortieth of the surface while crying wolf on part of it is
below the bar `check-counts.sh` sets in its own header. Left manual, and written into §13.3 so the
next person knows it was a decision rather than an oversight.

**Then it happened again, minutes later, in my own new text.** §9.6.3.1 got a sentence citing
"§13.1's rule" about hard-coded counts; §13.1 is *What `Plain.Length` costs, stated* and has no such
rule. Same session, immediately after writing the warning above, while looking for exactly this.
So the mitigation is not care — it is a habit: **`grep` the cited heading and read its title.**
Five seconds, no judgement, and it catches the common form, which is not a subtly misread section
but a wholly unrelated one whose number was close to hand. The fix was to *delete* the citation
rather than repoint it: no section states that rule, and the pointer was there for borrowed
authority rather than for anything a reader would follow.

### The `--colors` round-trip assertion was scoped wrong

§9.6.3.1 said `default`, `dim` and `bold` are "exactly the three entries that resolve to
`Color.Default`". True of the nineteen rows `--colors` prints; false of Spectre's parser, which
accepts every decoration keyword it has (`italic`, `underline`, `invert`, `conceal`,
`strikethrough`, the blinks) and should resolve each to the same value for the same reason.

An implementer testing "everything that parses" rather than iterating the nineteen rows gets a
failure that looks like a defect in the ruling and is not. Sent to the implementor before they
pinned the count, along with the confirmation that `StandardColorNames` is exactly the sixteen ANSI
names — so nineteen is `Count + 3` and not a literal, in the test or in the prose.

The implementor had already gone further than asked on the half of this that *was* right: rather
than trust §9.6.3.1's reasoning about `Style.Foreground` being non-nullable — which the section
itself flagged as read off a cast rather than off the library — they built a throwaway console app
against the pinned Spectre 0.57.2 and ran it. `Style` is a value type, `Foreground` reflects as a
non-nullable `Color`, and `{default, dim, bold}` empirically resolve to `Color.Default` while all
sixteen names do not. The section's "confirm this before relying on it" line is now discharged, and
by evidence rather than by agreement.

### The bare form of a command is unspecified until someone rules on it — three for three

`--items`, `--colors`, and `--preview` each specified a JSON envelope and left the bare form to
whoever implemented it first. Three sections now exist because of that (§9.6.2.2, §9.6.3.1,
§9.3.2), and the third was caught only because the second had just been written.

`--preview` was the sharp one, and I created its risk myself. Having ruled that bare `--colors`
auto-detects and loses its colour under a pipe, I sent that to the implementor as a ruling rather
than as the output of a test — and they were an hour from applying it, correctly and by analogy, to
`--preview`, where it is wrong.

The test §9.6.3.1 applies without naming: **a form may degrade only while some other form still
carries the whole payload.**

- `--colors`: `--json` carries the names, which are the whole payload. Degrade.
- `--preview`: §9.8.1 pins `rows[]` as `{text, width}` — plain, deliberately, because `text` is the
  diffable form. Degrade the bare form too and *no* form carries styling to a non-terminal caller.
  Which is every caller that matters: `/edit`, `/migrate`, and the MCP tools all capture through a
  harness. "Colour it by value" is among the most common `/edit` requests.

Ruled: `--preview` uses **the render path's console configuration itself**, not a matching one —
§1's rule, and it disposes of `NO_COLOR` and everything like it without the section needing to know
what any of them do. `migrate.md` step 6 had been assuming escapes since it was written, which is
the tell that the behaviour needed a ruling.

Then the same test, applied a third time, said *unstyled* for the MCP `preview` tool (§12.6.10) —
its consumer relays to a chat surface where escapes are noise in the middle of a sentence, so
nothing is lost that the channel could have carried. One principle, three commands, three different
answers, no exceptions claimed.

### `preview` over MCP could not report a dropped pane at all

§12.6.9 argues for four paragraphs that the tool must return `diagnostics[]`, because §7 makes a
bad config render silently degraded and "the preview looked right" is otherwise evidence for a
config `set_config` will reject. Every word applies to render notes; none of it was applied to
them. §9.8.1's separation rule — a note never appears in `diagnostics` — meant the information was
not degraded but absent.

`notes[]` added, **per render rather than per response**: §12.6.3 renders at 80 and 60 by default,
so a response-level array cannot say which width dropped the pane, which is the note's whole
content. Diagnostics are width-independent and stay at the response level. A `maxLines` cap is
width-independent and therefore appears in every render's list — the same three copies the CLI
already prints across three widths, and one finding rather than three.

### The wrong-citation habit paid on first use

§13.3 now prescribes reading the *title* of a cited heading, not just confirming the number
resolves. Applied once, it found two wrong citations in §9.6.3.1 — both `§2`, both for things §9's
own bullet list says outright, neither newly written, both passing `check-citations.sh` the whole
time. Cheapness is the property: it costs nothing, so it gets done on citations nobody suspects,
and an unsuspected citation is the only kind this hides in.

### The README's item table was the one item list nothing checked

§9 forbids an item list embedded in a skill or command's prose, and the audit that looked for
violations found the command prompts clean — 1–3 mentions each, all translation examples, and both
`migrate.md` and `edit.md` say in as many words not to copy a list out of the README. What the
audit found instead was the README's own table: sixteen ids with descriptions, asserting both the
membership of the set and which two of them are opt-in.

**It is not the forbidden second registry, and deleting it would be the wrong fix.** Nothing
automated reads it; it is what a person reads before deciding to build, and a README that cannot
say what ships is worse than one that can. But "not forbidden" is not "checked" — the table made
two claims the binary knows the answer to, and nothing compared them. Rule C in
`check-examples.sh` now does: id-set equality in both directions, plus each row's `(opt-in)` marker
against `default: false`. The table is marked with an HTML comment because a prose table has no
in-band string to anchor on the way `--items` plain output does, and the README's other tables must
stay unscanned.

This costs a README row per new item. That is not §1's zero-edit promise breaking — §1 exempts
whatever is genuinely unique about the new thing, and one line saying what it reports is exactly
that. The check does not write the row; it refuses to let anyone not notice.

**The negative test caught a live bug in the check itself.** The first draft read the flag with
`jq '.default // true'`. jq's `//` fires on `false` as well as `null`, so every opt-in item read
back as a default one and the opt-in half of the rule was dead — passing on a correct README and
passing exactly as hard on a wrong one. It surfaced only because the drift cases were driven in all
four directions, and two of them failed to fail. This is §10.1's rule again: a check is not
verified by watching it pass.

### `check-examples.sh`'s `dotnet run` path had never once executed

Reported by the implementor, who ran it with `CLAUDE_TUI_LINE_BIN` unset for the first time. SDK
10.0.301's `dotnet run` does not claim `--nologo`, so it was forwarded past `--` into the app's own
argv at every position tried. The app answered `unrecognized argument: '--nologo'` — as a valid
JSON error object, which is why this presented as `--items --json was not the expected shape`
rather than as anything resembling a build failure. `-v quiet` alone suppresses the banner; the
flag is gone. Every previous run of this check had gone through the stub.

## Standing constraints

- Back up anything of the user's before replacing it. The live
  `~/.claude/statusline-command.sh` (17,273 bytes) is intact and has never been replaced;
  timestamped backups live in `../claude-tui-line-backups/`.
- The user's work statusline under `~/Downloads/` is **read-only** — reference material for the
  hyperlink work (§3.2), never modified.
- The implementor never touches anything under `~/.claude`, never writes into `publish/`, and
  never commits without approval. `publish/` is the deploy target the live statusline executes;
  builds for verification go to the SDK-default output under `src/ClaudeTuiLine/bin/Release/`.
  SPEC-V2-FRAMEWORK.md §14 carries the full reconciliation — it directs one command at `publish/`,
  and that command is a deploy, not a build. (It lived at SPEC.md §10 requirement 2 until
  2026-08-14; see the audit note below for why it moved.)
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
