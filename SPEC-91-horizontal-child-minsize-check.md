# SPEC-91 — `CheckHorizontalSplitChildren` has no `minSize` counterpart

Issue #91. Ruling on whether `--check` should diagnose a declared-horizontal split whose child
declares a `minSize` exceeding the parent's bound, and if so at what severity and under what code.

Surfaced as an incidental finding during #88 (`SPEC-88-responsive-split-fallback.md` §12(2)), which
documented it and deliberately left it out of scope.

**All `src/` and `tests/` citations in this document are anchored to commit `8437c37`** (`#88:
split:"flex" responsive split fallback`), the tip of `main` at the time of Amendment A1. Cite that
commit when checking for drift.

---

## Amendment A1 — after #88 landed

**What changed and why.** This spec was first written while #88 was in flight, so it held #91 and
cited `ConfigCheck.cs` at pre-#88 line numbers. #88 merged as `8437c37`, which inserted a `Flex`
branch into `CheckStructuralSizes` and added `CheckFlexSplitBounds`, shifting every line number in
this file by roughly +38. A1 does five things:

1. **NE-3 is resolved** (§14) — #88 has landed. §9's hold is discharged and #91 may be implemented.
2. **All `ConfigCheck.cs` and `ConfigCheckTests.cs` citations re-anchored** to `8437c37`.
3. **§9 substantially rewritten.** The situation is no longer "#88 is in flight." It is "#88 landed,
   but `SPEC-88`'s text still forbids this change and a merged test still asserts the bug exists."
   That is a sharper hazard than the original section described, and §9.1/§9.2 now address it
   directly.
4. **V6 activated** (§13), with a new V6b for an interaction only visible in #88's merged code.
5. **§15's incidental finding is now task #92**, and A1 records that it propagates further than the
   original §15 said — into #88's flex combinator, not only the vertical path.

**One thing A1 does not change: every ruling in §1.** The evidence that decided them is framework
text and unaffected by #88.

---

## 1. Ruling

**Add the check.** Emit a diagnostic, at severity **`Error`**, under the **existing**
`fixed-sizes-exceed-parent` code, from `CheckHorizontalSplitChildren` in
`src/ClaudeTuiLine/ConfigCheck.cs` (`:951-969`).

| Question | Ruling | §  |
|---|---|---|
| Add at all, or document as accepted? | **Add** | §3, §4 |
| Severity | **`Error`** | §6 |
| Diagnostic code | **Reuse `fixed-sizes-exceed-parent`** — no new code | §7 |
| Backward-compat risk | **Accept and ship as error** | §8 |
| Sequencing | **After #88** — satisfied; #88 merged at `8437c37` | §9 |

**This is a small change with an outsized coordination hazard.** The code edit is roughly six lines.
§9 is the part that will cause damage if skipped, and it is why this spec is long.

---

## 2. What the gap actually is

The dispatch framed this as "`minSize` is unvalidated." That is not accurate, and the accurate
framing is what makes the ruling determinate.

`minSize` is already validated in two places in `ConfigCheck.cs`:

- `:866-870` — `minSize > maxSize` on a single pane. Code `min-exceeds-max`, severity `Error`.
- `:940-948` (inside `CheckSplitBounds`) — children's `minSize` **sum** plus boundary cost exceeds
  the parent's `maxSize`. Code `fixed-sizes-exceed-parent`, severity `Error`.

So the real gap is narrower and more defensible: **`CheckStructuralSizes` routes on declared split
direction, and only one of its branches checks `minSize`.**

`CheckStructuralSizes` (`:862-903`), post-#88:

| Declared split | Routes to | Checks fixed | Checks `minSize` |
|---|---|---|---|
| `Vertical` (`:877-887`) | `CheckSplitBounds` (`:925`) | yes (sum) | **yes** (sum) |
| `Horizontal` (`:888-894`) | `CheckHorizontalSplitChildren` (`:951`) | yes (per child) | **no** ← the gap |
| `Flex` (`:895-901`) | `CheckFlexSplitBounds` (`:911`) | via both of the above, AND-combined | inherits the gap |

The asymmetry is in the second row, and #88's `Flex` branch inherits it (§9.3). Nothing in the code
or the framework justifies checking a floor in one direction and not the other.

### 2.1 Why the two branches have different *forms* (sum vs per-child), and why that is correct

This is not an inconsistency and must not be "fixed." The comment at `:879-882` explains it:

> `§2.8` (horizontal width allocation) is out of scope for this phase — `SizeResolver` itself
> doesn't divide width among a horizontal split's children, so summing their fixed/`minSize`
> against the parent would claim a contention that isn't there yet. Revisit this scoping once §2.8
> lands.

And `:953-955`:

> A horizontal split gives every child the full parent width (§2.8 not yet implemented), so there is
> no sum to check — only whether any single child's own fixed size already exceeds the width it will
> be given.

So:

- **Vertical** — children share the constrained axis. They contend. **Sum** is the right check.
- **Horizontal** — each child receives the parent's *full* extent. They do not contend.
  **Per-child** is the right check.

`SPEC-88` §4.5.3 states the same duality and calls it "§2.3's sum-vs-max duality, mirrored into
`--check`", mapping `CheckSplitBounds` to `sideBySideFloor` and `CheckHorizontalSplitChildren` to
`stackedFloor`.

**The form is right in both branches. Only the coverage differs**, and only in the horizontal one.

---

## 3. Why this is a gap and not a deliberate exclusion

Worth settling explicitly, because `:879-882` *is* a deliberate scoping decision and it would be
easy to read it as covering this too. It does not.

That comment declines to **sum** a horizontal split's children — it says summing "would claim a
contention that isn't there yet." It says nothing about a *per-child* check, and the method it
guards performs one, on `FixedSize`, immediately below. The scoping decision is about the
*arithmetic form*, not about which size keys are in scope.

Two independent confirmations that this is understood as a defect and not a design position, both
in `SPEC-88`:

- §6 V11-V13 region — "**The one thing this ruling knowingly under-reports** … it has **no `minSize`
  counterpart**." Recorded as a *pre-existing gap*, "**Accepted, and deliberately not fixed here**",
  and noted as **failing open**.
- §12 incidental finding (2) — "structurally impossible and goes **undiagnosed today**",
  "**Flagged for the Orchestrator to route separately.**"

#88 declined to fix it because closing it "changes what `--check` reports for existing
declared-horizontal configs and could newly reject configs in the field, which is a product decision
and not this task's." That is a scope decision about *#88*, not a ruling that the gap should stand.
This spec is the separate routing #88 asked for.

---

## 4. The invariant, and why it is width-independent

`--check`'s contract is set by framework §9.8 (`SPEC-V2-FRAMEWORK.md:5963-6018`), which is unusually
explicit and decides most of this spec.

§9.8 rules that `--check` **never consults a width** — it does not read `COLUMNS` and does not
resolve sizes, because §12.6's `validate` MCP tool calls it from a process with no terminal at all,
and "a validator whose answer depends on the caller's window is not a validator."

It further rules that degrading at a narrow width is **designed behaviour, not a defect**, so the
diagnostic must be *structural*:

> So the diagnostic is **structural**, and the invariant is narrow but real: a contradiction no
> terminal width can resolve.

**The condition #91 adds satisfies that invariant exactly.** Let a declared-horizontal split have a
declared bound `B` — its own fixed size or its `maxSize` (`:956`, `parentBound =
SizeResolver.FixedSize(split) ?? split.MaxSize`). Every child receives the parent's full extent, so
every child receives exactly `B` columns. A child declaring `minSize = M` with `M > B` therefore
cannot be satisfied — **at any terminal width**, because `B` came from the config, not from
`COLUMNS`.

This is the same substitution §9.8 `:6000-6003` blesses for the existing checks:

> So the width the check compares against is the renderer's, computed by the renderer's code, with
> the parent's **declared** size or `maxSize` standing in for the terminal width. That substitution
> is what keeps this width-independent: same function, a number from the config rather than from
> `COLUMNS`.

And where there is no declared bound, §9.8 `:6016-6018` rules that `--check` must stay silent:

> Where the parent is `fill` or `content`, there is no bound to contradict and `--check` says
> nothing. That is not a gap.

The existing `if (parentBound is not int bound) { yield break; }` guard at `:957-960` already
implements this, and the new check sits inside it, inheriting the behaviour for free.

---

## 5. The change

**File:** `src/ClaudeTuiLine/ConfigCheck.cs`
**Method:** `CheckHorizontalSplitChildren` (`:951-969`)
**Nothing else in `src/` is touched.** In particular `CheckFlexSplitBounds` (`:911-923`) is not
edited, even though its behaviour changes as a consequence (§9.3).

### 5.1 Current code, as merged at `8437c37`

```csharp
    private static IEnumerable<Diagnostic> CheckHorizontalSplitChildren(Pane split, string path)
    {
        // A horizontal split gives every child the full parent width (§2.8 not yet implemented), so
        // there is no sum to check — only whether any single child's own fixed size already exceeds
        // the width it will be given.
        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is not int bound)
        {
            yield break;
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            if (SizeResolver.FixedSize(split.Children[i]) is int childFixed && childFixed > bound)
            {
                yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"this pane's fixed size ({childFixed}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
            }
        }
    }
```

### 5.2 Required code

Only the loop body changes. The signature, the `parentBound` expression, the `yield break` guard,
and the existing diagnostic's path, code, severity and message text are all unchanged.

```csharp
    for (var i = 0; i < split.Children.Count; i++)
    {
        var child = split.Children[i];

        if (SizeResolver.FixedSize(child) is int childFixed && childFixed > bound)
        {
            yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"this pane's fixed size ({childFixed}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
        }
        else if (child.MinSize is int childMin && childMin > bound)
        {
            yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"this pane's minSize ({childMin}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
        }
    }
```

The method's leading comment must also be updated, since it currently says "only whether any single
child's own **fixed size** already exceeds the width it will be given." Replace "fixed size" with
"fixed size or floor". Do not otherwise rewrite it — its §2.8 explanation is still correct and still
load-bearing. **This is a different comment from the one at `:879-882`**, which §12 forbids touching.

### 5.3 Three details of the above, each deliberate

**(a) It reuses the already-computed `bound`, and does not compute its own.**

`bound` is `SizeResolver.FixedSize(split) ?? split.MaxSize`. Both disjuncts are correct ceilings for
the floor comparison: if the parent is fixed at `B` the child gets exactly `B`; if the parent has
`maxSize B` the child gets at most `B`. In both cases `M > B` is unsatisfiable.

Note this differs from `CheckSplitBounds`, whose *sum* min-check consults only `split.MaxSize`
(`:940`) and ignores a fixed parent. That asymmetry is **task #92** — see §15. **Do not "align" the
two here** (§12).

**(b) `child.MinSize` is read directly; there is no `SizeResolver` call.**

The fixed branch must go through `SizeResolver.FixedSize` because "fixed size" is *derived* — a
pane's fixed extent depends on how its `size` token parses. `minSize` is not derived: it is a
declared integer key read straight off the pane (`Config.cs:158`), and `CheckSplitBounds:942`
already reads it directly as `c.MinSize ?? 0`. Introducing a resolver call here would invent an
indirection the codebase does not have.

This is **not** the §9.8 `:5990-5998` "call `SizeResolver`'s own boundary-cost function" rule. That
rule governs **boundary cost**, which is genuinely derived arithmetic that a hand-written sum
double-counts. There is no boundary cost in this check at all — each child gets the full parent
extent, so there is no divider to reserve. Adding one would be wrong.

**(c) `else if`, not a second independent `if` — at most one diagnostic per child.**

Where a child declares *both* a fixed size and a `minSize` over bound, only the fixed one is
reported.

The reason is not brevity. `{"size": N, "minSize": M}` with `M > N` is an **explicitly unresolved
question in another spec**: `SPEC-2.3-drop-predicate.md:323` lists it as surfaced by its §3(d) with
"**Three defensible answers**" and does not settle it. Emitting two diagnostics for that pane would
be #91 taking an implicit position on which declaration governs — pre-empting a ruling that belongs
to that spec.

`else if` costs nothing in detection power. It suppresses the `minSize` diagnostic **only** when the
fixed diagnostic already fired on the same child, so every structurally-impossible child still
produces exactly one error. A child with `size: 5, minSize: 30` under `bound: 10` still reports, via
the `minSize` branch, because the fixed branch did not fire.

This also matches `SPEC-88` V13(b)'s stated preference for "exactly **one** diagnostic" per fault.

---

## 6. Severity: `Error`

Consistent with every comparable diagnostic, and required by §9.8's own logic.

All three structural checks §9.8 enumerates are errors:

- `fixed-sizes-exceed-parent` — framework `:5419` table: `error`
- `min-exceeds-max` — framework `:5420` table: `error`; §9.8 `:6008-6010` notes §9.4 called it a
  warning "in an earlier draft and has been **corrected**"
- the `minSize`-sum case — framework `:6011`, same code as the first, therefore also `error`

The severity axis in this project tracks **"unachievable everywhere"**, not "how confident the check
is" and not "how new the check is." The framework's gloss for `min-exceeds-max` is literally
"unachievable everywhere | error". A child floor above its parent's declared ceiling is unachievable
everywhere.

`DiagnosticSeverity` has exactly two members (`ConfigCheck.cs:9-13`, `Error` and `Warning`). There
is no third, softer channel, and §8.3 explains why `Warning` must not be pressed into service as one.

---

## 7. Diagnostic code: reuse `fixed-sizes-exceed-parent`. Do not mint a new one

Framework §9.8 `:6011-6014` already decides this, for the exactly-parallel `minSize`-sum case:

> Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's `maxSize`.
> **Same code as the first: it is the same contradiction with the floor rather than the exact size**
> — and therefore the same arithmetic, taken from §2.10.

So the framework's stated rule is: **a floor-based contradiction takes the same code as the
exact-size contradiction.** #91's condition is that rule applied to the per-child form. Reuse is not
a convenience here; it is the framework's ruling.

Four further reasons:

1. **The codebase already does it.** `fixed-sizes-exceed-parent` is emitted from four sites as of
   `8437c37` — `:921` (flex composite), `:935` (fixed sum), `:945` (**`minSize` sum**), `:966`
   (fixed per-child). A `minSize`-derived contention already ships under this code. Minting a new
   code for #91 would leave `:945` as the inconsistent one.
2. **§9.6.1's registry.** Codes are a compatibility surface — §9.8 `:6053` cites §9.6.1's rule that
   "a code that is not in it does not exist." A new code requires a new §9.4 registry row and
   becomes permanently supported API. Reuse requires neither.
3. **Code reuse is the established norm.** `key-not-applicable` is emitted from ten sites,
   `part-source-count` from three, `unknown-item-id` from four. Codes here identify a *kind of
   fault*, not a call site.
4. **`SPEC-88` §7 pins it** — "**The `fixed-sizes-exceed-parent` code's meaning and severity**
   (§9.6). Reused, not redefined, and no new code is registered." #91 stays inside that constraint.

**The messages remain distinguishable** — a consumer branches on `code`, and a human reads the
`message`, which names `minSize` explicitly and differs from the fixed variant's wording.

### 7.1 One honesty problem this creates, and the amendment that fixes it

The §9.4 registry gloss at framework `:5419` reads:

> | `fixed-sizes-exceed-parent` | declared fixed sizes cannot fit the parent at any width | error | 9.8 |

That gloss is **already inaccurate on `main`**, because `:945` files a `minSize` contention under it
today. #91 makes it more inaccurate. See §11 for the required amendment. This is a documentation
defect #91 inherits and should fix, not one it creates.

---

## 8. Backward compatibility — the real question, and the ruling

The dispatch is right that this is the crux: closing the gap can newly reject configs that pass
`--check` today.

### 8.1 The blast radius, stated precisely

`RunCheck` (`Program.cs:660-704`) ends:

```csharp
var hasError = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
...
return hasError ? 1 : 0;
```

and `--check --json` reports `ok: !hasError`. So a new **error** genuinely gates: a
previously-passing config newly **exits 1** and newly reports **`ok: false`**, including through
§12.6's `validate` MCP tool.

The affected config is narrow, and every clause is required:

1. a pane with `split: "horizontal"` **declared** (not defaulted — absent `split` normalizes to
   `vertical`), **and**
2. that pane has a **declared bound** — its own fixed size, or a `maxSize`; a `fill`/`content`
   parent is untouched (§4), **and**
3. a **direct child** declares `minSize` **strictly greater** than that bound.

Plus, post-#88, the `flex` case in §9.3 — which is a *different* population and is discussed there.

### 8.2 Why the risk is acceptable

**The decisive argument: these configs were never working.**

§9.8's stated fear — `:5994` — is precisely calibrated:

> a false `error` is the worst outcome available here, because exit 1 sends the user to fix
> something that already works.

That is the harm to avoid, and #91 does not cause it. A child floor above its parent's declared
ceiling is unsatisfiable at *every* width (§4). Such a config is **already** misrendering today; the
dispatch says so ("degrades visibly only at render time"), and `SPEC-88` calls the current behaviour
**failing open**. `--check` newly reporting it is the validator becoming correct about a config that
was already broken — not a working config being newly rejected.

So this is **not** the §9.4 antipattern §9.8 `:5980-5981` warns about ("a validator which warns
about things that work correctly gets ignored on the occasions it is right"). It is the opposite:
removing a case where the validator stays silent about something that does not work.

**Supporting points:**

- **No false positives are possible.** The check fires only on declared numbers, compared against a
  declared bound, with no width and no derived arithmetic. Contrast §9.8 `:5990-5998`, where the
  double-counting risk came entirely from hand-written boundary arithmetic — which this check does
  not perform (§5.3(b)).
- **The signal is high.** `minSize` is a number the author deliberately named. Framework `:2363`
  ("`minSize` is a number the author named") and §2.11.1 both treat an explicit `minSize` as a
  strong declaration of intent. Reporting a contradiction between two things the author explicitly
  declared is about as low-noise as a diagnostic gets.
- **No in-tree test breaks on the declared-horizontal path.** Verified at `8437c37`: no fixture in
  `ConfigCheckTests.cs` declares a horizontal split with a child `minSize`. The nearest negative
  test, `HorizontalSplitChildrenWithinParentBound_ProducesNoStructuralDiagnostic` (`:1841-1862`),
  uses `Size` only. **`SPEC-88`'s V13(c) is a separate matter — see §9.2.**
- **The repair is obvious and local.** The message names the child's `minSize`, the parent's bound,
  and why every child gets the full parent width.

### 8.3 Rejected: ship as `Warning` first, promote to `Error` later

This is the conventional rollout answer and it is **wrong here**. Rejected for three reasons:

1. **It misuses the severity axis.** Severity here means "is this achievable at any width" (§6), not
   "how recently was this check added." A `Warning` would assert something false about the config.
2. **There is no deprecation channel to build it on.** The enum has two members. There is no version
   negotiation, no `--strict`, and no per-code suppression anywhere in `ConfigCheck.cs`. A
   soft-launch would require inventing all of that for one six-line check.
3. **It would contradict the framework twice over.** §9.8 `:6008-6010` records that
   `min-exceeds-max` was *already corrected from warning to error* — the project has explicitly
   moved in the opposite direction on a directly analogous floor diagnostic. And a warning that a
   config is structurally impossible is exactly the "validator that warns about things that work"
   §9.4 says gets ignored.

### 8.4 Rejected: document as accepted behaviour, change nothing

Rejected. It leaves an asymmetry with no principled defence (§2), it has now been logged as a defect
three times (#88 twice, #91 once), and it keeps `--check` silent about a config that cannot render —
which is the one thing §9.8 says the structural checks exist to catch.

---

## 9. Coordination with #88 — READ BEFORE IMPLEMENTING

#88 merged at `8437c37`, so the **ordering** constraint is satisfied. Three coordination hazards
remain, and two of them are worse now than while #88 was in flight, because they are no longer
hypothetical.

### 9.1 `SPEC-88`'s text still forbids this change — SPEC-91 governs

`SPEC-88` §7 "What must NOT change" still reads, unamended as of this writing:

> **`CheckSplitBounds` and `CheckHorizontalSplitChildren` themselves.** §4.5.3 adds a `Flex` branch
> to their *caller* and calls both unchanged. In particular, **do not add a `minSize` check to
> `CheckHorizontalSplitChildren`** to close the gap §4.5.3 documents — that changes declared-
> horizontal behaviour and is out of scope (§12). **V13(c) guards this.**

**#91's implementor will read that as a direct prohibition on this spec, and so will a Reviewer
validating the diff.** Resolve it as follows:

**That prohibition was scoped to #88's own diff and is discharged.** `SPEC-88` §12(2) — the same
document — asks for exactly this work to be "**routed separately**" and says "**if it is fixed
later**", which is now. §7's list is what *#88* must not change while landing `flex`; it is not a
standing prohibition binding all future work. **`SPEC-91` governs this change.**

**The `SPEC-88` text nevertheless needs amending** so the next reader is not misled — see §9.5. That
amendment is cdtui-architect's to make, not #91's implementor's, and **#91 is not blocked waiting on
it.** If a Reviewer flags the conflict, this section is the answer.

### 9.2 V13(c) is now a merged, passing test that #91 will turn red — this is expected

`SPEC-88` V13(c):

> **(c) No regression.** The same two configs declared `"vertical"` and `"horizontal"` produce
> **byte-identical diagnostics to current `main`**.

That test shipped with #88 and is green on `main` today. #91 deliberately changes what a
declared-`"horizontal"` config reports.

**So V13(c) is not a test #91 must keep passing. It is a test whose baseline #91 invalidates.** It
must be **re-baselined** against post-#91 behaviour, not merely re-run.

> **The trap, stated plainly for the implementor:** if V13(c) fails and you make it pass by
> weakening or reverting #91's check, you have silently undone this spec and the diff will still be
> green. A failing V13(c) is the expected outcome. Re-baseline it; do not revert.

The implementor must **locate V13(c) first**, before writing any code, so its failure is anticipated
rather than discovered. It is not among the `fixed-sizes-exceed-parent` assertions in
`ConfigCheckTests.cs` (`:1074`, `:1099`, `:1124`, `:1837`, `:1862`), so it is likely in the flex
test file — `SplitFlexTests.cs` is where #88's other flex tests live. Suggested:
`grep -rn "byte-identical\|V13" tests/ --include=*.cs`. **If V13(c) cannot be located, stop and
report** rather than proceeding on the assumption it does not exist.

### 9.3 #91 changes `flex` behaviour, through code #91 does not touch

This is the part most likely to be missed. #88's `CheckFlexSplitBounds` (`:911-923`) is:

```csharp
    var sideBySide = CheckSplitBounds(split, path, collapse).ToList();
    var stacked = CheckHorizontalSplitChildren(split, path).ToList();

    if (sideBySide.Count == 0 || stacked.Count == 0)
    {
        yield break;
    }

    yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
        $"this flex split's children exceed its parent's bound in both arrangements: side by side ({sideBySide[0].Message}) and stacked ({stacked[0].Message})");
```

`stacked` is `CheckHorizontalSplitChildren`'s output. **#91 makes that method produce diagnostics it
previously did not**, so a `flex` pane whose children have `minSize` over bound now satisfies the
AND where it previously short-circuited at `stacked.Count == 0`. `SPEC-88` §12(2) predicted this
("§4.5.3's under-report closes for free"). It is the correct behaviour — such a pane is impossible
in both arrangements, exactly #88's AND criterion — but it is **a behaviour change to `flex` caused
by #91, in a method #91 does not edit**, and it is untested unless #91 adds V6.

There is also a **message-composition consequence**: `stacked[0].Message` will now sometimes be
#91's new `minSize` wording, which gets interpolated into the flex composite message. V6b covers it.

**Critically, this must not be confused with #88's V13(a)**, the headline case that must stay clean:
`{"split":"flex","maxSize":40}` with two `minSize: 30` children. There each child's `minSize` of 30
is **under** the bound of 40, so #91's per-child check does **not** fire, `stacked` stays empty, and
the AND still yields nothing. **V13(a) still passes after #91.** The distinction is `minSize > bound`
per child (fires) versus `Σ minSize > bound` (does not, in the per-child form) — §2.1's duality
doing exactly its job.

### 9.4 Ordering — satisfied

The original ruling was "#88 first, then #91." #88 merged at `8437c37`. **#91 may now be
implemented.** No worktree conflict remains, though the implementor should branch from `8437c37` or
later so that `CheckFlexSplitBounds` is present — implementing #91 against a pre-#88 base would
silently skip §9.3 entirely.

### 9.5 `SPEC-88` amendments still outstanding — cdtui-architect's, not #91's

`SPEC-88` is unamended (919 lines, unchanged). Three places are now stale, and a fourth is separately
known:

- §7 — the "do not add a `minSize` check" prohibition, discharged by this spec (§9.1).
- §6 V13(c) — baseline superseded (§9.2).
- §12(2) — incidental finding resolved; should point to this spec.
- (Separately, the §3.4.2 / V11 amendment from the Ultra-Advisor's world-(C) ruling.)

**#91's implementor must not make these edits.** They belong to `SPEC-88`'s author. #91 proceeds
without them.

---

## 10. Forward compatibility with §2.8

The check rests on "a horizontal split gives every child the full parent width," which holds only
because §2.8 is unimplemented (`:879-882`, `:953-955`). It is fair to ask whether #91 builds on sand.

**It does not. The check stays sound when §2.8 lands.** Under §2.8, width is *divided* among a
horizontal split's children, so each child receives some share `s_i ≤ B`. A child with `minSize = M`
where `M > B` therefore satisfies `M > B ≥ s_i` — still unsatisfiable, a fortiori.

So §2.8 cannot make this check *wrong*, only **incomplete**: once children contend, a sum check
would also be needed. At that point both branches converge on sum-plus-per-child, and the per-child
`minSize` check #91 adds survives unchanged as one half of it.

This is the same forward-compatibility the existing per-child `FixedSize` check already enjoys, so
#91 adds no new coupling to §2.8's eventual landing.

---

## 11. Framework amendments

Two, both to `SPEC-V2-FRAMEWORK.md`. Both correct documentation that is **already** inaccurate on
`main`; #91 does not create either defect, but should not compound them.

### 11.1 §9.8's bullet list does not cover the per-child form at all

§9.8 `:5986-6014` enumerates the structural checks in three bullets, **all of which are sum checks**,
written for the vertical/shared-axis model. The existing per-child horizontal `FixedSize` check
(`:962-968`) is **already an implementation extension beyond this list.** §9.8 does not describe it,
and a reader reconstructing `--check` from §9.8 alone would not produce it.

**Required amendment.** §9.8's enumeration must state the sum/per-child duality explicitly, so that
both existing per-child behaviour and #91's addition are normatively described. Recommended shape —
add after the three bullets, before the `fill`/`content` paragraph at `:6016`:

> **The arithmetic form follows the axis, not the size key.** Where a split's children *share* the
> constrained axis they contend, and the check is a **sum** plus the boundary cost — the three
> bullets above. Where each child receives the parent's **full** extent — a declared-horizontal
> split, for as long as §2.8 is unimplemented — there is no contention and therefore no sum, and the
> check is **per child**: any single child whose own declared fixed size, **or whose own declared
> `minSize`**, exceeds the parent's bound is impossible on its own. Both per-child cases are the same
> contradiction as the first bullet and carry the same code, `fixed-sizes-exceed-parent`. There is no
> boundary cost in the per-child form: each child receives the full extent, so no divider is
> reserved, and adding one would reintroduce exactly the double-count the first bullet's rule closes.

The final sentence is not padding — it pre-empts an implementor reading §9.8's emphatic "call
`SizeResolver`'s own boundary-cost function" rule and wrongly applying it here (§5.3(b)).

### 11.2 §9.4's registry gloss understates the code

`:5419` currently reads:

> | `fixed-sizes-exceed-parent` | declared fixed sizes cannot fit the parent at any width | error | 9.8 |

Already inaccurate on `main` (`:945` files a `minSize` contention under it). Amend the gloss to name
floors:

> | `fixed-sizes-exceed-parent` | declared fixed sizes **or floors** cannot fit the parent at any width | error | 9.8 |

Severity, code and section reference are unchanged.

**Note for the implementor:** per framework `:445`, editing a documented closed set can fail
`tools/check-all.sh`'s doc-token check until the corresponding table is updated. This amendment edits
a table cell's prose rather than adding a member, so it is not expected to trip that check — but see
§14 NE-2.

---

## 12. What must NOT change

- **`CheckSplitBounds` (`:925-949`) — entirely.** In particular its min-check's use of
  `split.MaxSize` alone at `:940`. That is **task #92** (§15), not #91.
- **`CheckFlexSplitBounds` (`:911-923`) — entirely**, including its AND short-circuit and its
  composite message format. Its *behaviour* changes as a consequence of #91 (§9.3); its *code* does
  not.
- **`CheckStructuralSizes`'s routing (`:862-903`)**, including all three branches and the
  `:879-882` §2.8 scoping comment. #91 changes what one branch *reports*, never which branch runs.
- **The existing fixed per-child diagnostic** at `:962-968` — its condition, path
  (`{path}/children/{i}`), code, severity and **message text verbatim**. §13 V4 guards this.
- **`parentBound`'s expression and the `yield break` guard** (`:956-960`). A `fill`/`content` parent
  must continue to produce nothing (§4, framework `:6016-6018`).
- **`DiagnosticSeverity`** (`:9-13`) — no third member.
- **The `fixed-sizes-exceed-parent` code's meaning, severity, and registry row** beyond §11.2's
  prose widening. No new code is registered by #91.
- **`RunCheck`** (`Program.cs:660-704`) — the exit-code and `ok` mapping are unchanged.
- **`--check`'s width-independence.** No `COLUMNS` read, no `SizeResolver` resolve pass, no
  boundary-cost call added.
- **`SizeResolver`, `PaneCollapse`, `Config.cs`, `Pane.cs`** — untouched.
- **`SPEC-88-responsive-split-fallback.md`** — not #91's file to edit (§9.5).

---

## 13. Verification

V1-V5 and V7-V10 go in `tests/ClaudeTuiLine.Tests/ConfigCheckTests.cs`, beside the existing
horizontal cases at `:1817-1862`. V6/V6b belong wherever #88's V13 flex tests live (§9.2).

- **V1 — the new diagnostic fires (`maxSize` parent).** `{"split":"horizontal","maxSize":40}` with
  one child `{"minSize":50}` → exactly one diagnostic: code `fixed-sizes-exceed-parent`, severity
  `Error`, path `/surface/pane/children/0`, message naming `50` and `40`.
- **V2 — the new diagnostic fires (fixed parent).** `{"split":"horizontal","size":"10"}` with one
  child `{"minSize":20}` → same shape, path `/surface/pane/children/0`. Guards §5.3(a) — a test
  using only `maxSize` would pass against an implementation that wrongly consulted `split.MaxSize`
  alone, mirroring `CheckSplitBounds:940`. **This test is the one that distinguishes the correct
  implementation from that plausible wrong one.**
- **V3 — no bound, no diagnostic.** A `fill` or `content` horizontal parent with a child
  `{"minSize":999}` → **no** `fixed-sizes-exceed-parent`. Guards framework `:6016-6018`.
- **V4 — the fixed check is unchanged.** `:1817-1837` and `:1841-1862` pass **unmodified**, and
  their emitted message strings are byte-identical to `8437c37`.
- **V5 — at most one diagnostic per child (§5.3(c)).** A horizontal parent bounded at 10 with one
  child `{"size":"20","minSize":30}` → **exactly one** diagnostic for `/surface/pane/children/0`,
  its message naming the **fixed size (20)**, not the `minSize`. Paired with: a child
  `{"size":"5","minSize":30}` under the same bound → exactly one diagnostic, message naming the
  **`minSize` (30)**. The pair proves `else if` suppresses only the redundant case and never loses a
  detection.
- **V6 — `flex` inherits the fix (§9.3).** `{"split":"flex","maxSize":40}` with two `minSize: 50`
  children → the AND now produces **exactly one** diagnostic, code `fixed-sizes-exceed-parent`,
  severity `Error`, path = **the split's own path** (not `…/children/{i}`, per
  `CheckFlexSplitBounds:921`). **And, in the same run, #88's V13(a) must still pass**:
  `{"split":"flex","maxSize":40}` with two `minSize: 30` children still reports `ok: true` and **no**
  diagnostic. If V6 and V13(a) cannot both be green, the implementation is wrong — see §9.3.
- **V6b — the composite message quotes the new wording.** For V6's config, the flex diagnostic's
  message must name **both** arrangements and its "stacked (…)" clause must contain #91's `minSize`
  message text. Guards the interpolation at `:922` against a stale-message regression.
- **V7 — no boundary cost leaked in.** V1's message must name the parent's bound as `40`, not a
  boundary-adjusted number. Guards §5.3(b).
- **V8 — exit code.** `--check` on V1's config exits **1**, and `--check --json` reports
  `ok: false`.
- **V9 — V13(c) re-baselined, not reverted (§9.2).** After re-baselining, V13(c)'s declared-
  `"vertical"` half must still be byte-identical to `8437c37`; only its declared-`"horizontal"` half
  changes, and only for configs carrying a child `minSize` over bound.
- **V10 — full suite.** `dotnet test tests/ClaudeTuiLine.Tests` — no failures other than V13(c)'s
  expected, deliberate re-baseline (see §14 NE-1).
- **V11 — `tools/check-all.sh`** passes, or fails only in ways already failing on `main` (§14 NE-2).
  §11's amendments must be made in the **same change** as the code, per framework `:445`.

---

## 14. NEEDS-EVIDENCE

I do not run anything. Each item states what to run and what each outcome decides.

- **NE-1 — which existing tests fail, and are they only the expected ones?**
  *Partially resolved by inspection at `8437c37`.* No fixture in `ConfigCheckTests.cs` declares a
  horizontal split with a child `minSize`; the nearest negative test (`:1841-1862`) uses `Size` only.
  **`SPEC-88`'s V13(c) is expected to fail (§9.2) and must be re-baselined.**
  **To run:** `dotnet test tests/ClaudeTuiLine.Tests` after the change.
  → **Only V13(c) fails:** expected; re-baseline per §9.2 and V9.
  → **Anything else fails:** stop and report which test and its fixture. **Do not "fix" it** — a
  failure outside V13(c) most likely means this spec is wrong, not the test.

- **NE-2 — does `tools/check-all.sh` accept §11's amendments?**
  Run before and after. `check-all.sh` was **failing on `main`** before #88 for unrelated reasons
  (`check-citations` on undefined §-refs, `check-counts` on `SPEC-2.3-drop-predicate.md:172`), so the
  bar is **no new failures**, not exit 0. Re-establish the baseline at `8437c37` first, since #88 may
  have changed it.
  → **No new failures:** proceed.
  → **A new failure naming §9.4's table or §9.8:** report the exact message; §11.2's shape needs
  revisiting.

- **NE-3 — RESOLVED.** #88 landed on `main` as `8437c37`. #91 is unblocked; branch from `8437c37`
  or later (§9.4).

- **NE-4 — are there real-world configs in the blast radius?**
  §8.1 defines the affected shape; §9.3 adds the `flex` population. If any example, fixture, README
  snippet, or `docs/` config matches, it will newly fail `--check` and must be fixed in the same
  change.
  Suggested: search `.json`/`.md` for a `"split": "horizontal"` or `"split": "flex"` pane carrying
  `size` or `maxSize` whose children declare `minSize`.
  → **None found:** §8.2's risk assessment stands unqualified.
  → **Any found:** report them. A repo-shipped config in the blast radius is evidence the pattern
  occurs in the field, and would justify re-opening §8.3's staged-rollout question **as a product
  call for Jim rather than an engineering one**.

---

## 15. Task #92 — the shared root cause (was: incidental finding)

**`CheckSplitBounds`'s `minSize` sum check ignores a fixed parent.** `:929` computes
`parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize` and uses it for the *fixed* sum check,
but the *`minSize`* sum check at `:940` consults `split.MaxSize` alone. So a **fixed** vertical
parent — say `size: "40"` — whose children's `minSize` sum is 50 goes undiagnosed, because
`split.MaxSize` is null and the block is skipped. Framework `:6011` has the same narrow wording
("the parent's `maxSize`"), so the code may be faithfully implementing a too-narrow bullet.

**A1 update: this propagates further than first stated.** It is not confined to the vertical path.
Because `CheckFlexSplitBounds` (`:913`) delegates to `CheckSplitBounds`, a **`flex` pane with a fixed
size** (not `maxSize`) and a child whose `minSize` exceeds it will, even after #91:

- fire `CheckHorizontalSplitChildren` (per-child, uses `FixedSize(split) ?? MaxSize` — sees the
  fixed size), but
- **not** fire `CheckSplitBounds`'s min branch (gated on `MaxSize`, which is null),

so the AND yields **nothing**, for a pane impossible in both arrangements. **#91 does not close that
case**, and the `maxSize`-parent case works only because `Σ minSize ≥ any single minSize`.

So #92 is a **shared root cause feeding both the vertical sum check and #88's flex combinator**, not
a standalone vertical-path issue.

**Deliberately out of scope for #91**, for the reason #88 used to punt this one: it is a pre-existing
gap in a different code path, fixing it newly rejects a different population of field configs, and it
deserves its own ruling. §12 forbids touching it here.

**Caution for #91's implementor and reviewer:** do not "strengthen" V6 by switching it to a
fixed-size flex parent. That variant will fail, and the failure is #92, **not a #91 defect**.

I have **not** verified this against `SizeResolver.FixedSize`'s behaviour for every `size` token
form, so it remains a strong suspicion rather than a confirmed defect.

---

## 16. Decisions, and what I did not decide

**Decided, with confidence:**

1. **Add the check** (§3) — the asymmetry has no principled defence and framework §9.8's invariant
   covers the case exactly.
2. **Severity `Error`** (§6) — severity tracks "unachievable at any width"; framework `:6008-6010`
   shows the project already corrected an analogous floor diagnostic *from* warning *to* error.
3. **Reuse `fixed-sizes-exceed-parent`** (§7) — framework `:6011-6014` rules floor-based
   contradictions take the same code, and `:945` already does exactly this.
4. **Accept the backward-compat risk, no staged rollout** (§8) — the newly-rejected configs are
   unsatisfiable at every width and already misrender, so this is not the "false error" §9.8 warns
   against. `Warning`-first rejected on three independent grounds (§8.3).
5. **`else if`, one diagnostic per child** (§5.3(c)) — chosen to avoid pre-empting
   `SPEC-2.3-drop-predicate.md:323`'s open ruling.
6. **`SPEC-91` governs over `SPEC-88` §7's stale prohibition** (§9.1) — that prohibition was scoped
   to #88's diff, and #88's own §12(2) routed this work onward.
7. **V13(c) is re-baselined, not satisfied** (§9.2).

**Flagged, not decided — for the Orchestrator or Jim:**

- **The residual product question (§8.2, NE-4).** My ruling is that no *working* config is newly
  rejected, because the affected configs cannot render correctly at any width. That is sound, but it
  is an engineering judgment about a **user-visible gating change**: someone with such a config who
  tolerates the degraded render newly gets exit 1. I judge that correct — a validator silent about
  an impossible config is worse — but if NE-4 finds the pattern in practice, **this becomes Jim's
  call, not mine.**
- **The `SPEC-88` amendments (§9.5)** are another architect's file. Specified, not made.
- **Task #92 (§15)** needs its own ruling; A1 raises its priority from "incidental" to "shared root
  cause."

**Confidence: high** on §1's rulings — framework §9.8 and `:6011-6014` decide most of this directly,
and the change is small, additive, and provably free of false positives.

**The risk in this task is not the ruling; it is the coordination.** §9.1 (a live prohibition in
another spec), §9.2 (a merged test that must fail), and §9.3 (a behaviour change in untouched code)
are where an implementor or reviewer can do real damage.
