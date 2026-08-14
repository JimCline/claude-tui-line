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

**Recorded as unverified where it is unverified.** That `Style.TryParse("dim")` yields
`Foreground == Color.Default` is inferred from the signature, not observed, and §6.6 says so in a
block quote rather than burying it. The defect holds either way — two resolvers and two fallbacks
for one key is the defect — but the symptom is a prediction about a third-party library, and
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
