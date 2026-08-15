# Report: SPEC-47 fallback-pane diagnostic

Branch `task-47-fallback-diagnostic`, worktree
`/Users/jimcline/git/repos/claude-tui-line-task-47-fallback-diagnostic`, based on local `main`@`8437c37`
(matches dispatch's stated HEAD).

## Fix (B) — implemented, §3 complete

- `src/ClaudeTuiLine/Program.cs`: catch-all at the third `BuildFallbackConfig` call site (was
  `:894-898`) now binds the exception and returns `ComposeResolutionFailureReason(ex)` as the
  reason instead of `null`. Added `ComposeResolutionFailureReason(Exception ex) =>
  $"config could not be resolved: {ex.Message}"` as a sibling of `ComposeUnreadableReason`, per
  §3.1/§3.2's recommended shape.
- `src/ClaudeTuiLine/ConfigUnreadableMessage.cs`: `Format`'s `path` parameter changed from
  `string` to `string?` (§3.3/E2). When `path is null`, rungs 1-2 (full path, elided path) are
  skipped entirely and rendering enters directly at rung 3 (pathless) — no new rung added, no
  placeholder path synthesized, matching the ruling in §3.3.
- `src/ClaudeTuiLine/Program.cs:43` (the `Main` call site): dropped the `configPath!`
  null-forgiving operator now that `Format` accepts null legitimately.
- §3.4: did **not** rename `UnreadableReason`/`ComposeUnreadableReason` — per spec, noted as
  follow-up only.

E2's answer (from evidence-gathering): `ConfigUnreadableMessage.Format` at
`ConfigUnreadableMessage.cs:20`, path was non-nullable `string`; the five rungs are a single
linear fallthrough chain with no existing entry point into rung 3 — confirmed by reading the
method directly. The `if (path is not null)` wrapper around rungs 1-2 is what creates that entry
point; verified by rebuild (see below), not just by inspection, since the flow-analysis interacts
with the existing `width!.Value` null-forgiving uses inside the method (one additional `!` was
needed at the reason-budget line once the two path rungs moved inside a conditional — CS8629,
fixed, rebuilt clean).

## Fix (A) — NOT implemented, blocked by E1

E1 asked whether a literal-text `PaneItem` can be constructed as `Format` set with `Item` null,
and said explicitly: *"If no literal-only construction exists anywhere → report that, because it
means a pane item may always require a resolvable `Item`, and (A) needs a different shape."*

That is what the evidence came back as. Two searches:

1. Every `new PaneItem(...)` in `src/` and `tests/` (28 matches) — the only production
   construction is `Config.cs:830-846`'s `ToPaneItems`, which passes `i.Item`/`i.Format` straight
   from parsed JSON; no literal-only construction exists anywhere in the codebase.
2. Traced how `PaneItem.Item`/`.Format` are actually consumed, in `LeafItems.cs`'s
   `ResolveDisplay` (`:55-64`) and `ApplyFormat` (`:178-179`):
   ```csharp
   private static Segment? ResolveDisplay(PaneItem item, string? key, string? value, ItemContext ctx)
   {
       if (item.Format is not null)
       {
           return value is null ? null : SegmentBuilder.BuildItemSegment(ApplyFormat(item.Format, value), null);
       }
       ...
   }
   public static string ApplyFormat(string? format, string value) =>
       (string.IsNullOrEmpty(format) ? "{}" : format).Replace("{}", value);
   ```
   `Format` is a `{}`-placeholder template applied to a *resolved* value — confirmed independently
   by `SPEC-V2-FRAMEWORK.md:5209` (§9.5.1): *"A format string substitutes `{}` with the item's own
   value and nothing else."* When `item.Format is not null` but `value is null` (which is what a
   null `Item` produces — nothing to resolve), `ResolveDisplay` returns **null**: the item is
   suppressed, not rendered as literal text. `Format`-with-null-`Item` does not work; it renders
   nothing.

So §4.3's presumed construction (`Format` set, `Item` null) does not produce visible text — it
produces silence, which is the exact failure mode (A) exists to prevent. This is a genuine gap in
the spec, not an ambiguity I can resolve by picking the "obviously intended" alternative: making a
`PaneItem` carry literal text requires either a new field on the record, a new `Item`
kind/provider that always resolves to a fixed string, or some other shape §4 doesn't specify, and
choosing among those is a design decision, not an implementation one.

**Stopping here per contract rather than guessing.** §3 (Fix B) is complete, builds clean, and is
ready for verification. §4 (Fix A) needs the Architect to pick a construction shape before I write
code for it — recommend routing E1's finding back for a design decision.

## Build

`dotnet build src/ClaudeTuiLine -c Release` — exit 0, no errors (verified after fixing one
CS8629 introduced by the §3.3 restructuring).

## Blocked: §5 tests 1/2/3/4/6 need a seam that doesn't exist

§5 test 1 says to force `ResolveTopLevel`/`ResolveRootPane` to throw via "a test seam or a
deliberately-invalid injected `UserConfig`." Neither option is available today:

- **No injected `UserConfig` can force a throw.** §1.3 already established (and I re-confirmed)
  that nothing reachable from a user config value throws in either method — everything defaults or
  null-coalesces. That's *why* the bug is dormant.
- **No test seam exists, and the architecture makes adding one non-trivial.** `LoadRenderConfig`,
  `ComposeResolutionFailureReason`, `ComposeUnreadableReason`, and `BuildFallbackConfig` are all
  local functions inside `Program.cs`'s top-level-statements file — they're implicitly private to
  that scope; local functions can't carry access modifiers, so `InternalsVisibleTo` (already wired
  for `ClaudeTuiLine.Tests`, confirmed at `AssemblyInfo.cs:3`) doesn't reach them regardless. The
  only prior test coverage of this diagnostic path (`PreviewCliTests.cs`) shells out to the built
  CLI binary as a subprocess — which also can't force the throw, for the same §1.3 reason.

Making tests 1/2/3/4/6 executable requires **one of**:
1. A fault-injection hook added to `Config.cs`'s `ResolveTopLevel`/`ResolveRootPane` (or something
   they call) — production code shape change purely for testability, not described anywhere in §3
   or §4.
2. Restructuring `LoadRenderConfig` and its siblings out of `Program.cs`'s top-level statements
   into a proper `internal` class member so tests can call them directly and inject a throwing
   dependency — also a structural change beyond §3's diff.
3. Accepting weaker coverage: test `ComposeResolutionFailureReason`'s message shape and
   `ConfigUnreadableMessage.Format`'s new null-path rung-3 entry (§3.3) as isolated units (both
   *are* reachable — `ConfigUnreadableMessageTests.cs` already covers `Format` directly), but never
   exercise the actual `catch` block at `Program.cs:894` end-to-end.

I did not pick one — this is a testability/architecture call, not something §3's literal diff
answers, so no new test file was written this pass. Option 3's reachable slice (`Format`'s new
null-path rung-3 entry, isolated from the `catch` block) is the one piece I could write without a
decision — I held off on writing even that alone so the whole §5 pass lands together once you pick
1/2/3, rather than in two disjointed commits. Let me know which and I'll write the rest
immediately; this is the only thing left before a full §5 pass and cdtui-worker verification.

## Not yet run

No test suite run yet — smoke-test only per dispatch ("Smoke-test only — full verification is
cdtui-worker's job"). No commit made pending your read of the Fix (A) gap — let me know whether to
commit Fix (B) alone on this branch now or wait.

## What the Reviewer/cdtui-worker should look hardest at

- `ConfigUnreadableMessage.cs`'s restructured `Format`: confirm the `width!.Value`/`width.Value`
  null-forgiving uses are all still sound now that rungs 1-2 are conditionally skipped (I re-derived
  this by hand — reachability of line 47 requires `width` non-null via the pathless-`Fits`-false
  branch, but worth a second look).
- Test 4 (§5) — the null-`configPath` test — is not yet written; it's the one the spec says
  "fails hardest if §3.3 is skipped." No tests were added at all in this pass since I stopped at
  the E1 gap before reaching §5's full suite; only a build-level smoke check was done.
