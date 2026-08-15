# §2.6 — the vertical ellipsis marker is a trailing splice, not a replacement row

Task #27 follow-up. Written against `/Users/jimcline/git/repos/claude-tui-line` (main) and
`/Users/jimcline/git/repos/claude-tui-line-task-27` @ `4c90382` (impl2's diff).

Placed in the main repo root, not the worktree, so it survives the worktree being removed.

**Amended once (A1):** §5(b) originally left the ordering against §3.1 undecided because I had not
read §3.1. I have now, and it is ruled — **this task is sequenced after the §3.1 block model.** See
§5(b), which is the one part of this file that changes what happens next.

**Short answer: full-row-replacement is not spec-compliant.** §2.6 rules the question directly, in
a paragraph written specifically to stop a reading like this one. impl2's instinct to route rather
than guess was right, and its bug fix is correct and should land — but its characterisation of the
gap as "not wrong, just coarser" understates it on both the spec and the behaviour.

---

## 1. The ruling, from §2.6's own text

`SPEC-V2-FRAMEWORK.md:1494-1509`:

> **Vertical overflow** is governed by the same marker. When wrapping produces more rows than the
> pane's `maxRows` (or the surface's, §2.8), the surplus rows are dropped and the last surviving
> row ends with the marker, so truncation is always visible rather than silent.
>
> **"Ends with the marker" means the marker replaces the row's last cells, never that it is appended
> to them.** The horizontal case two paragraphs up budgets the marker's width against the inner width
> and says so; the vertical case said only "ends with", and appending is the reading that sentence
> invites. […]
>
> So the marker is budgeted identically on both axes, with the same two riders: if the inner width is
> not greater than the marker width the marker is dropped rather than allowed to consume the row, and
> an `ellipsis` of `""` is a hard clip that spends no cell. One rule, applied twice — stating it
> once per axis is how the two got to disagree.

Three things follow, and none of them is a judgment call:

1. **The marker occupies cells, not a row.** "Replaces the row's **last cells**" is unambiguous
   about the unit. Full-row-replacement budgets the marker in rows.
2. **The last surviving row keeps its content.** The clause presupposes a row that has content for
   the marker to sit at the end of. A row whose entire content is the marker does not "end with" it.
3. **The two axes are explicitly one rule.** The closing sentence — "One rule, applied twice —
   stating it once per axis is how the two got to disagree" — is a standing instruction against
   exactly the outcome in the worktree: two axes, two implementations, two behaviours.

`:1686` independently confirms the unit: *"`ellipsis` marker goes on the last surviving **content**
row"* — on it, not instead of it.

The paragraph at 1498 was written to foreclose *appending* (which overflows the pane width and
triggers §2.4's shearing failure). Full-row-replacement is a third reading it did not enumerate. It
avoids the overflow the paragraph warns about, but by discarding the content the same sentence says
must remain. **Not compliant, and not a coarser version of compliant — a different unit.**

---

## 2. What impl2 got right, and must land unchanged

The `#27` diff fixes a real bug and its degenerate-case handling matches §2.6 exactly:

- **Threading `innerWidth` into `ClipRows`.** Without it the marker could exceed the pane's inner
  width — the §2.4 shearing failure, on the vertical axis. This is the actual bug and it is fixed.
- **Both riders, correctly.** `ellipsis.Length == 0` → hard clip spending no cell; marker dropped
  when the inner width is not greater than the marker width. These are §2.6:1506-1508 verbatim, and
  they carry over to the spliced implementation **unchanged**.

**Land the diff.** This ruling is a follow-up task, not a blocker, exactly as the Orchestrator
scoped it.

---

## 3. Two behavioural defects that disappear under the splice

Offered as corroboration that §2.6 says what it says for a reason — not as independent grounds.

**(a) Non-monotone in the pane's width.** `ClipRows` as written:

```csharp
if (ellipsis.Length == 0 || ellipsis.Length >= innerWidth) { return rows.Take(cap).ToList(); }
var kept = rows.Take(cap - 1).ToList();
kept.Add(new PaneRow(escaped, ellipsis.Length));
```

At `innerWidth == ellipsis.Length` the guard fires and the pane keeps `cap` rows of real content.
At `innerWidth == ellipsis.Length + 1` it keeps `cap − 1`. **One column wider yields strictly less
content** whenever `cap ≤ 4` — content cells go from `innerWidth × cap` to `innerWidth × (cap−1)`.
A degradation ladder that inverts under a one-unit change is the same defect class §10 requirement
6's monotone-clamp test exists to catch on the sizing axis.

**(b) `cap: 1` renders an empty pane.** `rows.Take(0)` plus a marker row is a pane containing `…`
and nothing else. The one row the author was still granted is spent announcing that rows were lost.

Under the splice both vanish structurally: `cap` rows are always kept, and the marker costs
`ellipsis.Length` cells of one row regardless of `cap`. That is the point of budgeting in cells.

**(c) Styling divergence, minor but real.** The marker row is `Markup.Escape(ellipsis)` — plain and
unstyled. `TruncateSegment` restyles the marker to inherit the segment's colour and closes any OSC 8
hyperlink before it, per §3.2 (`:2621-2622`: *"clicking `…` must never navigate"*). Reusing
`TruncateSegment` inherits that ruling for free; a second implementation re-opens it.

---

## 4. Where the fix goes — do NOT extend `PaneRow`

impl2's blocker is real and correctly diagnosed: `PaneBuffer.cs:8` is
`public sealed record PaneRow(string Markup, int Width)`, with no plain-text field, so there is no
markup-safe splice point **at the layer `ClipRows` runs at**. But that layer is a choice, not a
constraint.

`PaneAssembler.RenderLeafRows` builds `rawRows` via `PaneRenderer.RenderLeaf(...)` →
`RowLayout.Wrap(prepared, width, allowFallback)`, and *then* clips the opaque result. The row budget
arrives too late. The fix is to let it arrive on time.

**Ruled: pass the row budget down into the render path so the last kept row's content is truncated
while it is still `Segment`s, and reuse `PaneRenderer.TruncateSegment` for the marker.** `RowLayout`
already knows how segments map to rows; stopping at `cap` rows and handing the final row's segments
to the existing truncation is the whole change.

**Rejected — extending `PaneRow` with a plain-text field.** It duplicates state that `Segment`
already carries (`Segment.Plain` / `Segment.Markup`), and the two copies would have to be kept in
agreement by every row producer forever. `PaneRow` being opaque is a property worth keeping: it is
what makes a rendered row final.

**Rejected — a second marker-splicing implementation at the row layer.** §2.6's closing sentence
forbids it in as many words, and Jim's standing architecture rule ("one implementation per
behaviour") says the same thing.

**AMENDED (A2) — this seam is now stale; the budget resolves at §3.1's concatenation site instead.**
§3.1 (task #75) landed and, per its own §6, requires exactly one concatenation site for the
pane's row list — `PaneAssembler.RenderItemRows`'s local `rows` list, fed from two call sites
(`FlushGroup`'s packed-group flush and the block-line loop). That is now where the row budget must
be resolved and where clipping/splicing happens, **not** a single flat `RowLayout.Wrap` call per
pane as this section originally assumed — §5(b) predicted exactly this collision and deferred to
§3.1's text, which has since landed. Concretely: the budget is checked against `rows` as it is
being assembled, and when the cap lands inside a producer unit's contribution, that one unit
(the packed group or block whose `RenderLeaf` inputs are in scope at that call site — see
SPEC-3.1 §6's amended provenance requirement) is the one re-invoked with the row budget, so the
final kept row is truncated and the marker spliced while its content is still `Segment`s inside
`Wrap`. The mechanism below (stop at `cap` rows, reuse `TruncateSegment` on the last kept row's
segments) is unchanged — only where it attaches moved, from a single pane-wide `Wrap` call to this
per-producer-unit call at the concatenation site.

### The ambiguity §2.6 does not settle, ruled here

"Replaces the row's **last cells**" is written for the routine case, which §2.6 names: *"The last
surviving row is routinely a full row — it is the row that was full enough to force the wrap."*
When that row is **not** full, "last cells" is ambiguous between *immediately after the content* and
*flush to the right edge, with a gap*.

**Ruled: immediately after the content**, i.e. clip to `innerWidth − ellipsis.Length` (a no-op when
the content is already shorter) and place the marker directly after. Reasons: it is what
`TruncateSegment` already does, so reuse is exact; a gap before the marker reads as content the pane
chose not to show; and `Pane.Align` (applied by `AlignRow`, downstream) owns horizontal placement of
the finished row — a marker pinned right would fight a `center` or `right` alignment. For a full row
the two readings coincide, which is why §2.6 never had to choose.

**The marker must be spliced before `AlignRow` runs**, so alignment sees content-plus-marker as one
row. This is the current call order and it must not change.

---

## 5. Two couplings the implementor must not discover late

**(a) `RenderLeaf`'s input tuple is spec'd, and this change extends it.** `:1432` names it as
`(items, innerWidth, overflow, ellipsis, notes, allowFallback)` — §2.5.1's purity/cache-key tuple,
and `:1437` states that §10's test fixes **all** of them. Adding a row budget makes it a seventh
member and therefore part of the cache key. **Two panes identical except for their row cap must not
share a cache entry.** Whoever implements this must update §2.5.1's tuple, §10's test, and the cache
key together, or the seam becomes a correctness bug that only shows under caching. This is the
highest-risk part of the change and it is not in the part anyone is looking at.

**(b) AMENDED (A1) — ruled: this task is sequenced AFTER the §3.1 block model.**

The first draft left this open pending §3.1's text. §3.1 (`:2494-2519`) settles it:

> Row packing (§2.6) operates on single-row items. A multi-row block **occupies its own rows**: it
> never shares a row with a neighbouring item and never has items packed beside it. A pane's content
> is therefore a vertical sequence of packed single-row groups and standalone blocks, in config
> order.
>
> **Packing runs before wrapping, and the order is not interchangeable.** […] Pack first. An item's
> block count is a property of **what the provider returned** — one line or several — never of the
> width it was later granted, so wrapping cannot promote an item to a block.

Today every row comes from wrapping one flat segment list, which is why §4's seam ("stop `Wrap` at
`cap` rows") is well-defined. Once §3.1 lands, a pane's rows are a **concatenation of packed groups
and standalone blocks**, so "the last kept row" is no longer necessarily produced by `Wrap` at all —
it may be the last row of a block that `Wrap` never saw. **The seam this task adds would have to be
rebuilt on the new row-production shape.**

Because §2 already fixed the shearing bug, this splice carries no user-visible urgency and there is
no reason to build it twice. Build §3.1's block model first, then this, on the final shape.

If the Orchestrator overrides that and lands this first, then §4's seam is **provisional**, and the
§3.1 task must carry an explicit item to re-site it — otherwise the marker silently stops being
applied to any pane whose last row comes from a block, which no existing test would catch.

---

## 6. What must not change

1. **Both §2.6 riders**, exactly as impl2 implemented them.
2. **`PaneRow(string Markup, int Width)`** — no new fields.
3. **`TruncateSegment` is the one implementation** of "cut to fit, ending with the marker". This
   task calls it; it does not fork it, and it does not copy its logic to the row layer.
4. **§3.2's hyperlink ruling** — a truncated link closes before the marker; the marker is never
   clickable. Inherited automatically by reusing `TruncateSegment`; re-verify it survives.
5. **Splice before align**, per §4.
6. **`cap <= 0` returns no rows.** Unchanged.

---

## 7. Verification

1. **`cap: 1`, content overflowing, `innerWidth` comfortably above the marker.** Assert the pane
   renders **one** row containing real content ending in `…`. Today it renders `…` alone. This is
   the headline behaviour change and the clearest statement of the ruling.
2. **Monotonicity across the guard boundary.** Same pane at `innerWidth == ellipsis.Length` and at
   `ellipsis.Length + 1`. Assert content is non-decreasing as width increases. Fails today by
   construction — this is defect (a) as an assertion, and it should be written to fail on the
   current implementation first.
3. **Row count is `cap`, not `cap − 1`.** Any overflowing pane with `cap >= 2`. Guards against a
   partial fix that splices the marker but keeps dropping the last content row.
4. **Both riders survive.** `ellipsis: ""` → `cap` rows, no cell spent, no marker. `ellipsis` wider
   than `innerWidth` → `cap` rows of real content, no marker. Both must behave identically before
   and after this change; they are already correct and this is a regression guard.
5. **Marker styling and hyperlink safety.** A clipped row whose last segment is a coloured OSC 8
   link: assert the link closes before the marker, the marker carries the colour, and the marker is
   not inside the link. Pins §3.2 on the vertical axis, which nothing currently tests.
6. **Alignment interaction.** A short final row under `align: right` with the marker spliced. Assert
   the marker sits immediately after the content and the whole row is then aligned — §4's ruling on
   the non-full row. This is the test that distinguishes the two readings of "last cells".
7. **No row exceeds `innerWidth`.** Every row of every clipped pane. This is the §2.4 shearing
   invariant that motivated the original bug fix, re-asserted on the spliced path.
8. **A pane whose last surviving row comes from a multi-row block** still gets the marker. Only
   writable once §3.1 lands, and it is the test that proves §5(b)'s re-siting was actually done.

---

## 8. Confidence

**High on the ruling.** §2.6:1498 is not ambiguous and was written to pre-empt a weaker reading of
the same sentence; §1686 corroborates the unit independently. I did not have to reason from
principle — the text decides it.

**High on §4's placement** *given today's row production*, and that qualifier is the substance of
§5(b): the placement is right for the current shape and would need re-siting after §3.1.

**Medium on §4's sub-ruling for the non-full row.** §2.6 does not settle it and both readings are
defensible; I chose the one that reuses `TruncateSegment` exactly and does not fight `align`. If
someone has a reason to prefer flush-right, it is a small change and I would not argue hard.

**§5(a) is the risk I would most expect to be missed** — it is a cache-correctness change hiding
inside a rendering change, and nothing in the task description points at it.

**Not escalation-worthy.**
