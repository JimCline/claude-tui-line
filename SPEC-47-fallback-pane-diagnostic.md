# SPEC-47 — the fallback pane must diagnose itself, and the catch-all must report a reason

> **STATUS: REVISION 2. Fix (B) is IMPLEMENTED (branch `task-47-fallback-diagnostic`, clean build).
> Fix (A) is RESPECIFIED — E1 came back and it invalidated §4's original shape.**
>
> **Revision 2 changes, all confined to §4 and its dependents:** E1 reported that **no literal-only
> `PaneItem` construction exists anywhere**, and that `Format`-with-null-`Item` renders *nothing*
> rather than literal text (`LeafItems.cs:55-64`, `:178-179`). That was E1's explicitly-anticipated
> "(A) needs a different shape" branch. **The diagnostic-item form of (A) is withdrawn** (§4.3), and
> replaced by a structural guard that buys the same insurance without touching the config surface
> (§4.4). §5's tests 7-8, §6's E1, §7 and §8 follow. **§3 (fix B) is unchanged and already shipped.**

> **Scope.** Issue #47 — `SPEC-V2-FRAMEWORK.md` §2.11.3's claim about the `SafeLoadAll` fallback
> pane is stale. This spec supersedes the framing that produced that staleness and specifies the
> fix. It is a **new mechanism** (`LoadRenderConfig`'s diagnostic channel gets a new producer), not
> a clarification of §2.11.3, which is why it is its own file.
>
> **Out of scope:** §2.11.3's replacement text itself. §7 says what it must and must not claim, but
> the framework edit lands with the implementation, not ahead of it.

---

## 1. The defect, located precisely

### 1.1 What the earlier framing got wrong

The investigation described this as a `Config.cs` problem — a catch-all "inside
`ResolveTopLevel`/`ResolveRootPane`". **It is not in `Config.cs` at all.** Neither
`ResolveTopLevel`, `ResolveRootPane`, `SafeLoadAll`, nor `Load` contains a `catch` block; the only
two in `Config.cs` (`:938`, `:964`) belong to unrelated methods.

The catch-all is **`Program.LoadRenderConfig`, `Program.cs:894-898`**, wrapping the calls to those
two methods. That relocation is what makes this fixable cheaply: the handler is already inside the
function that owns the diagnostic channel, so it does not need to be threaded anywhere.

This mattered enough to state, because a spec written against the `Config.cs` framing would have
put a guard in the wrong file and left the real path untouched.

### 1.2 The mechanism

`LoadRenderConfig` (`Program.cs:856`) returns a 5-tuple whose fourth element is the diagnostic
channel:

```csharp
static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath,
        string? UnreadableReason, int UnreadableReasonProtectedLength)
LoadRenderConfig(string? explicitConfigPath)
```

`Main` short-circuits on it at `Program.cs:40-45`: if `unreadableReason is not null`, print one row
and `return 0` — nothing below runs.

There are **three** `BuildFallbackConfig()` call sites, and they do not agree:

| site | condition | reason returned | outcome |
|---|---|---|---|
| `:867-869` | `ParseError` | `ComposeUnreadableReason(result)` | short-circuits, prints the reason |
| `:876-877` | `NoFile` **and** `--config` was asserted | `"no such file"`, protected length `0` | short-circuits, prints the reason |
| **`:896-897`** | **anything thrown by `ResolveTopLevel`/`ResolveRootPane`** | **`null`** | **falls through to the renderer** |

The third row is the defect. With `unreadableReason` null, `Program.cs:40`'s guard does not fire,
the fallback pane reaches the renderer, and `PaneCollapse.Collapse` (`PaneCollapse.cs:21-62`) sees
`Split=None`, `MinSize=null`, `size="auto"`, `Items.Count == 0` → `IsStructurallyEmpty` → `null` →
`Program.cs:177-181` → `Array.Empty<PaneRow>()`.

**Zero rows. No diagnostic. No exit code.** Strictly worse than the unreadable-config path, which
at least prints something.

`BuildFallbackConfig` (`:921-940`) builds the pane with `Array.Empty<PaneItem>()` (`:938`), which
is what makes it structurally empty. `PaneCollapse` is behaving **correctly** — it was handed a
pane with nothing in it. The defect is upstream of collapse, in two independent places, which is
why this spec has two fixes rather than one.

### 1.3 Dormant, not live — and why that is not a reason to leave it

Nothing reachable from a user config value throws inside `ResolveTopLevel` (`Config.cs:579-590`) or
`ResolveRootPane` (`Config.cs:641-657`); everything defaults or null-coalesces. Verified one level
deep.

So the bug cannot fire today. It is gated by an **invariant with no enforcement** — "nothing in
this call graph throws" — which no test asserts, no analyser checks, and no reviewer is prompted to
re-verify when either method grows. The first `throw` or unguarded index added anywhere under those
two methods converts a silent-zero-rows failure from impossible to shipping, with nothing in
between.

**Rejected: document it in §2.11.3 as a known-dormant risk and change no code.** That was one of
the two options originally on the table. It is rejected because §2.11.3 would then describe a
fallback pane that works while the code ships one that does not — the exact class of staleness
issue #47 was filed about. A spec that documents a defect instead of fixing it, in the section
whose staleness *is* the ticket, reproduces the problem in a third form.

---

## 2. What both fixes have in common

The fallback pane exists to make failure **visible**. A fallback that renders nothing has failed at
the only job it has. Both fixes below restore that property, at different levels:

- **(B), §3 — the catch-all reports a reason.** Makes the third row behave like the first two, so
  all three failure paths converge on the one shipped diagnostic channel. This is the substantive
  fix. **Implemented.**
- **(A), §4 — the fallback pane cannot be produced without a diagnostic.** Makes the coupling
  structural rather than conventional, so the fourth call site cannot reintroduce the defect. This
  is insurance, and §4.1 is honest about what it is worth once (B) lands. **Respecified in
  Revision 2 — the original diagnostic-item form is withdrawn.**

**Rejected: a guard or exemption inside `PaneCollapse`.** Special-casing "the fallback pane"
requires collapse to recognise a pane by identity, which nothing in `Pane` supports and which would
put knowledge of `Program`'s error handling inside the layout engine. Collapse's rule — an empty
pane collapses — is correct and general; do not carve a hole in it to compensate for a caller that
built the wrong pane.

---

## 3. Fix (B) — the catch-all returns a reason

> **Unchanged in Revision 2, and already implemented.** Retained in full because the Reviewer
> validates against this spec's current state, and because §4.4 is written in terms of it.

### 3.1 The change

`Program.cs:894-898`. The bare `catch` discards the exception, so it must bind it:

```csharp
catch (Exception ex)
{
    var (fallbackTopLevel, fallbackPane) = BuildFallbackConfig();
    return (fallbackTopLevel, fallbackPane, configPath, ComposeResolutionFailureReason(ex), 0);
}
```

Add a sibling to `ComposeUnreadableReason` (`:909-919`):

```csharp
static string ComposeResolutionFailureReason(Exception ex)
```

returning a reason string with **protected length `0`**.

**Protected length is 0, not a computed value.** `ComposeUnreadableReason` protects the `"line N"`
prefix (`:917-918`) because that prefix is the one irreplaceable thing — the file position a reader
jumps to (§9.2.2, `:901-908`). A resolution failure has no file position; there is nothing in its
reason that survives truncation better than anything else. `:877`'s `"no such file"` is the
existing precedent for a literal reason at protected length 0.

### 3.2 What the reason string says

**NEEDS-JIM is not required here** — this follows from the existing rung ladder rather than being a
free choice. The reason must:

- **Name the failure class**, so the row is not mistaken for a parse error. It is not one: the
  config parsed fine and then resolution threw.
- **Carry `ex.Message`**, because that is the only content-bearing part. `ComposeUnreadableReason`
  emits .NET's own wording verbatim (`:911`) rather than substituting a house message; follow it.
- **Not carry a stack trace or exception type name.** The output channel is one row, degraded
  under width pressure through five rungs. A stack trace is unshowable and a type name displaces
  message text that is more useful.

Recommended shape, mirroring `:877`'s brevity and `:911`'s verbatim-message rule:

```
config could not be resolved: {ex.Message}
```

### 3.3 The null-`configPath` hazard — the one thing that will break this

**This is the part most likely to be got wrong, and it is a live NullReferenceException, not a
style point.**

`configPath` **can be null** when the catch fires. `Program.cs:880` sets it null on the §9.2.1
row-1 path (no `--config`, nothing at any searched path). Resolution then runs against `config ==
null` and pure defaults — and if *that* throws, the catch is entered with `configPath == null`.

`Program.cs:43` then executes:

```csharp
ConfigUnreadableMessage.Format(configPath!, unreadableReason, ...)
```

The `!` is a null-forgiving operator on a value that is genuinely null. Today that assertion is
sound, because every path that sets a non-null reason also has a non-null `configPath`. **Fix (B)
breaks that coupling** — it is the first producer of a reason on a path where `configPath` may be
null. Ship (B) without handling this and the dormant silent-zero-rows bug is replaced by a dormant
crash, which is not an improvement.

**Ruling: `ConfigUnreadableMessage.Format` must accept a null path**, and render the path-less form
when given one. Do not add a placeholder string, do not synthesise a path, and do not skip the
diagnostic when the path is null.

The reason this is the right shape rather than an imposition: **the ladder already has a path-less
rung.** Rung 3 drops the path entirely under width pressure, so a path-less rendering is already
specified, already implemented, and already shipped. A null path should enter the ladder at that
rung instead of the top. That reuses tested behaviour rather than inventing a sixth rung.

### 3.4 Why not rename `UnreadableReason`

The field's name and the §9.2.1 comment at `:852-855` both say *unreadable*, and (B) puts a
resolution failure through it. Renaming to something like `DiagnosticReason` would be more honest.

**Do not rename it in this change.** Three reasons: the name appears in shipped §9.2.1 vocabulary,
so the rename is a framework edit with its own review surface; tuple element names are advisory in
C#, so the rename is *not* compile-enforced and a partial rename would leave two names for one
thing; and the change is cosmetic while §3.1 is behavioural — bundling them makes the behavioural
change harder to review. Note it as follow-up work in the implementation report; do not do it here.

---

## 4. Fix (A) — the fallback pane cannot be produced without a diagnostic

> **RESPECIFIED IN REVISION 2.** §4.2-§4.4 below replace the original §4.2-§4.4, which specified a
> literal-text diagnostic `PaneItem`. E1 disproved that shape's premise.

### 4.1 What this is worth, stated honestly

Once (B) lands, **all three `BuildFallbackConfig` call sites return a non-null reason, so
`Program.cs:40` short-circuits on every one of them and the fallback pane is never rendered at
all.** Fix (A) therefore guards a code path that is provably unreachable at the moment it ships.

That is not an argument against it, but it is the honest framing, and the implementation report
should not claim (A) fixes an observable bug — it does not, once (B) is in.

What (A) buys: `BuildFallbackConfig` returns a `Pane` through a signature that promises nothing
about whether the caller will render it. The fourth call site someone adds — plausibly for a new
failure mode, plausibly without noticing the reason-must-be-non-null coupling — gets zero rows and
no diagnostic, exactly as `:896` does today.

**Revision 2 sharpens what "buys" means.** The thing worth removing is not the empty `Items` list;
it is the **unenforced coupling** between "you called `BuildFallbackConfig`" and "you must also
return a non-null reason." That coupling lives in a reviewer's head. §4.4 moves it into the type
system, which is both cheaper and stronger than what §4.3 originally proposed.

### 4.2 E1's answer, and what it rules out

**E1: there is no literal-text `PaneItem`, and there is no way to make one.**

- The only production construction of `PaneItem` is `Config.cs:830-846`, built straight from parsed
  JSON. Nothing anywhere constructs a literal-only item.
- The inferred carrier does not work. `LeafItems.ResolveDisplay` (`:55-64`) and `ApplyFormat`
  (`:178-179`): with `Item` null, `value` is null, and
  `if (item.Format is not null) return value is null ? null : ...` returns **null**. So
  `Format`-with-null-`Item` renders **nothing** — the precise opposite of what the original §4.3
  required, and exactly the "renders as empty" failure that section warned the guess could produce.

E1's stated escape hatch fired as written: *"If no literal-only construction exists anywhere →
report that, because it means a pane item may always require a resolvable `Item`, and (A) needs a
different shape."* It does, and this is that shape.

**Withdrawn: the diagnostic `PaneItem`.** Not deferred, not gated on further evidence — withdrawn,
because the two ways to build it are both worse than the problem:

1. **A new `PaneItem` member** (`Text`/`Literal`). `PaneItem` is a *config-shaped* record
   deserialized from user JSON at `Config.cs:830-846`. A new member is a new config key by
   construction — it needs a `--check` disposition, a `--schema` entry, a README row, and an
   `unknown-key` interaction. **That is a permanent config-surface expansion to serve a provably
   unreachable path.** The cost is not the field; it is that the field is now part of the public
   config contract forever.
2. **A new registry item kind** whose value is a fixed diagnostic string. No config-surface *shape*
   change, but strictly worse in one respect: registry ids are globally nameable, so the id becomes
   something a user can write in their own config, and it would resolve. A diagnostic that renders
   in a healthy config is a worse defect than the one being guarded against.

Both trade a real, permanent surface for insurance on a path §4.1 already establishes is
unreachable. That is the wrong side of the trade.

### 4.3 What replaces it: make the diagnostic non-optional at the type level

**Ruling: the fallback pane keeps `Array.Empty<PaneItem>()` and changes not at all. Instead, make
it impossible to obtain a fallback pane without also producing a reason.**

Introduce one helper that returns the whole 5-tuple, and make `BuildFallbackConfig` private to it:

```csharp
static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath,
        string? UnreadableReason, int UnreadableReasonProtectedLength)
FallbackResult(string? configPath, string reason, int protectedLength)
```

- `reason` is **non-nullable and has no default.** This is the whole mechanism.
- The three existing sites become `return FallbackResult(configPath, <their reason>, <their length>)`
  — `:867-869`, `:876-877`, and (B)'s `:896-897`.
- `BuildFallbackConfig` is called only from inside `FallbackResult`. Make it a local function or
  mark it clearly as such; the point is that no new code can reach the pane without going through
  the reason parameter.

**Why this is the right form.** The defect at `:896` was never "the pane is empty" — it was "a
caller produced a fallback and forgot the reason." An empty pane is the *symptom*; the missing
reason is the *cause*, and it is the missing reason that suppresses the diagnostic. A required
parameter makes the cause unrepresentable. The fourth call site cannot compile without deciding
what to say, which is precisely the decision `:896` failed to make.

This is the same lesson as SPEC-87 §12.6.4 and SPEC-88's §2.4 finding, in a third setting:
**a default value converts a compile error into a silent behavioural gap**, and the whole value of
(A) is refusing to let a future author skip a decision. Note the direct consequence for the original
§4.2, which proposed `BuildFallbackConfig(string? message = null)` — that signature has exactly the
optional-with-a-default shape this rules against, and would have left the fourth call site free to
omit the message, guarding nothing.

### 4.4 What this deliberately does not do

- **It does not make the fallback pane render anything.** It cannot; E1 established there is no
  mechanism. If a future change makes the fallback pane reachable *with* a reason — which would
  itself be a §7 violation of `Program.cs:40-45`'s short-circuit contract — the pane still renders
  zero rows. **That is accepted**, because the reason is printed on the row above and the pane's
  content would be a second copy of it.
- **It does not add a `PaneItem`, a config key, a registry id, or a schema entry.** §7 pins this.
- **It does not change `BuildFallbackConfig`'s output.** The `ResolvedConfig` at `:923-929` and the
  `Pane` at `:930-938` are byte-identical to `main`.

If someone later has an independent reason to introduce literal pane content — a real feature, with
its own config-surface justification — then §4.2's option 1 becomes available and (A) could be
revisited. **Do not introduce it for #47's sake.**

---

## 5. Verification

Tests 1-6 are unchanged and cover fix (B). Tests 7-8 are replaced per §4.

1. **The defect, made falsifiable.** Force `ResolveTopLevel` or `ResolveRootPane` to throw (a test
   seam or a deliberately-invalid injected `UserConfig`), with `--config` pointing at a readable
   file. **On `main` this renders zero rows.** Assert exactly that first, so the test is known to
   be capable of failing, then assert the fixed build prints one row containing `ex.Message`.
2. **Exit code unchanged.** The same scenario returns `0`, matching `Program.cs:44`. This spec
   changes what is *printed*, not the process contract. A test asserting a non-zero exit would be
   pinning a behaviour this spec does not introduce.
3. **One row, never more.** Assert the output contains no `\n` beyond a single trailing newline.
   `Program.cs:43` is one `WriteLine` and `Format` builds one string; if `ex.Message` contains a
   newline — which it can — this test is what catches it. **If it fails, the fix is to strip or
   replace newlines in `ComposeResolutionFailureReason`, not to change the output channel.**
4. **Null `configPath` (§3.3).** Force the throw with **no** `--config` and nothing at any searched
   path, so `configPath` is null at `:880`. Assert: no `NullReferenceException`, one row, the
   reason present, no path fragment and no placeholder where a path would be. **This is the test
   that fails hardest if §3.3 is skipped**; write it before the fix.
5. **The two existing paths are byte-identical.** For a parse error (`:867`) and an asserted
   missing file (`:876`), assert the rendered row is byte-for-byte identical to `main`'s. Both
   rungs and the protected-length behaviour must be untouched — §9.2.2's line-number protection is
   shipped behaviour and this spec must not perturb it.
6. **Width degradation for the new reason.** Render the resolution-failure row at the widths that
   exercise each of the five rungs. Assert it degrades rather than crashing or emitting an empty
   row, and that at the tightest width it still emits the bare `claude-tui-line` form.
7. **All three failure paths produce a non-null reason (§4.3).** Parameterize one test over the
   three conditions — parse error, asserted missing file, resolution throw — and assert each
   produces a non-null `UnreadableReason` and prints exactly one row. This is the behavioural half
   of (A); it would have failed on `main` for the third case, and it is what a regression would
   trip.
8. **`BuildFallbackConfig`'s output is unchanged (§4.4).** The `ResolvedConfig` and `Pane` returned
   are identical to `main`'s, including `Items.Count == 0`. **The original test 8 asserted the same
   thing for a different reason** — then, that a default argument preserved behaviour; now, that
   (A) does not touch the pane at all.
9. `tools/check-all.sh` passes.

**The structural half of (A) is not testable and must be a code-review item.** "No caller can
obtain a fallback pane without supplying a reason" is a property of the signature, and a test that
tried to assert it would have to fail to compile. **Call it out explicitly in the implementation
report** so the Reviewer checks the signature rather than looking for a test that cannot exist. A
test asserting `reason` is non-nullable via reflection would be theatre — it would pass on a
signature with a default value, which is exactly the failure mode §4.3 exists to prevent.

---

## 6. NEEDS-EVIDENCE

- **E1 — how is a literal-text `PaneItem` constructed?** ✅ **ANSWERED, and it invalidated §4's
  original shape.** There is no literal-only construction anywhere; the only production
  construction is `Config.cs:830-846` from parsed JSON; and the inferred `Format`-with-null-`Item`
  carrier renders **nothing** (`LeafItems.cs:55-64`, `:178-179` — `Item` null → `value` null →
  `ApplyFormat` returns null). §4.2 records this and §4.3 is the replacement design. **The
  Medium-confidence guess §8 flagged was wrong, and the gate is why it cost nothing.**

- **E2 — can `ConfigUnreadableMessage.Format` take a null path, entering at the path-less rung?**
  §3.3. Report `Format`'s signature with file:line, whether `configPath` is annotated non-nullable,
  and the rung-3 code that drops the path — specifically whether rung 3 is reachable as an entry
  point or only as a width-driven fallthrough. *If rung 3 is cleanly reachable* → a null path
  selects it and §3.3 is a small change. *If the rungs are a single ordered chain with no entry
  point* → **report back before implementing**; forcing an entry may be more invasive than §3.3
  assumes, and the alternative (suppress the diagnostic when the path is null) is a behaviour
  regression I do not want chosen without a second look.
  — **Status unknown to this spec.** (B) is reported as implemented with a clean build, but a clean
  build does not answer E2: the hazard is a runtime null dereference, not a compile error. **The
  implementation report must state explicitly what happened to §3.3**, and test 4 is what proves it.

**Neither E1 nor E2 gates §3.1-§3.2.** That remains true and is why (B) shipped first.

---

## 7. What must NOT change

- **`Program.cs:40-45`'s short-circuit contract** — one row, `return 0`, nothing below it runs.
  §9.2.1's *"the render path's only output channel is this one row"* (`:37-39`) is the rule this
  spec extends a third producer onto, not one it relaxes.
- **`ComposeUnreadableReason` (`:909-919`)** — its output, its `"line N"` protected length, and
  §9.2.2's rationale at `:901-908`. Fix (B) adds a sibling; it does not modify this.
- **The five-rung degradation ladder** in `ConfigUnreadableMessage`, and its behaviour for the two
  existing reason producers. E2 may add a path-less **entry point**; it must not reorder, remove,
  or reword a rung. Test 5 pins this.
- **`PaneCollapse.Collapse`'s empty-pane rule** (`PaneCollapse.cs:21-62`). Correct and general —
  §2 rejects carving an exemption into it.
- **Exit code `0`** on every fallback path (test 2).
- **`BuildFallbackConfig`'s output.** The `ResolvedConfig` at `:923-929` and the `Pane` at
  `:930-938` are unchanged — §4.4 and test 8. Revision 1 permitted a message-carrying variant;
  Revision 2 withdraws that permission.
- **`PaneItem` (`Pane.cs:230`).** **No new member.** §4.2 rules out `Text`/`Literal`, and adding one
  for #47's sake is a config-surface expansion this spec explicitly rejects. Likewise no new
  `ItemRegistry` id.
- **The `:876` `"no such file"` reason and its protected length `0`.** Untouched.
- **`SPEC-V2-FRAMEWORK.md` §2.11.3's replacement wording is not free-form.** It must describe
  **both** paths — short-circuited-before-collapse and, after (B), also-short-circuited — and must
  **not** state or imply that any single mechanism universally protects the fallback pane. Naming
  one mechanism as universal is precisely the staleness #47 was filed about; replacing it with a
  differently-scoped universal claim reproduces the defect. If the wording cannot be made accurate
  without enumerating the call sites, enumerate them.
  **Revision 2 adds one constraint:** the wording must **not** claim the fallback pane renders a
  diagnostic. It does not and cannot (§4.4). It is protected by never being reached, which is a
  different claim and the only true one.

---

## 8. Confidence, and what I did not verify

**High** on §1's diagnosis. The three call sites, the null-reason third row, and the collapse
consequence are read directly from `Program.cs:856-940` and confirmed against the independent
`PaneCollapse` trace.

**High** on §3.1-§3.2. It makes the third call site do what the other two already do, using a
sibling of a function that already exists, with `:877` as precedent for a literal reason at
protected length 0. Now also implemented.

**High** on §3.3 being a real hazard. `Program.cs:880` sets `configPath` null and `:43` dereferences
it with `!`; (B) is the first producer to decouple those. I have **not** read
`ConfigUnreadableMessage`, so the *fix* is E2-gated even though the *hazard* is not. **A clean build
is not evidence this was handled** — see §6's E2 note.

**High** on §4.3, which is Revision 2's replacement. It rests on a signature property rather than on
any inference about rendering, so E1's class of error cannot recur here. The residual risk is that
someone later adds a default value to `reason` for convenience, which §7 and §5's code-review note
are written to prevent.

**Revision 1's §4.3 was Medium-confidence on an unverified inference, and it was wrong.** Recorded
rather than quietly deleted, because the gate is the reusable lesson: the guess was flagged as a
guess, marked NEEDS-EVIDENCE, and the Implementor stopped at it instead of building on it. That is
the mechanism working, and the cost of the error was one dispatch.

**Low stakes throughout.** Every path here is already a failure path; the worst outcome of a wrong
call is a differently-worded diagnostic on a path that today prints nothing.

**No Ultra-Advisor escalation recommended.** No security, concurrency, migration, or public-interface
surface. The only reversibility concern is §3.4's rename, which this spec declines to do — and §4.2
now adds a second one it also declines: a new `PaneItem` member would be permanent config surface,
which is the one genuinely hard-to-reverse thing #47 could have produced.

**Two things I did not verify and am flagging rather than assuming:** whether `ex.Message` can
contain a newline in practice (test 3 covers it either way), and whether the bare `catch` at `:894`
is also swallowing exception types that should not be caught at all — `OutOfMemoryException`,
`StackOverflowException`, cancellation. That is a real question about the handler this spec is
editing, but it is a separate change with a separate blast radius, and I am deliberately not
widening #47 to include it. Worth its own ticket.
