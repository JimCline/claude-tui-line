# SPEC v2 — pane surface + status item framework

Goal, in the user's words:

> *"a statusline TUI framework where users can add pre-built status items, wire in their own
> (even if it means calling shell or py scripts for their placeholders) and it all is rendered
> inside the TUI surface."*

> *"The render surface inside our TUI shell should support panes, split vertically,
> horizontally, etc. The first iteration is one pane that fills the status line."*

v1 (SPEC.md) is a faithful rebuild of one hardcoded statusline. v2 turns it into a framework:
the render surface becomes a **pane tree**, and the 14 captured segments become **registry
rows** that a user-defined item joins as a peer rather than as a special case bolted on.

Prerequisite: SPEC.md Phase 1 (the `chromeReserve` width fix) must be verified in a live
session first. Panes make width errors catastrophic instead of cosmetic — a pane that
miscomputes its width by two columns corrupts every pane to its right — so the surface must be
measured correctly before anything is composed on it.

## 1. The load-bearing rule

**One implementation per behavior; one registry per enumerable kind.** Adding a status item
costs *one registry row* plus whatever is genuinely unique about it, and zero edits to the
renderer, the compositor, the layout engine, or any test harness. If adding an item means
touching `SegmentBuilder`'s control flow, the abstraction has not landed.

Concretely: `SegmentBuilder.Build`'s current 14 hand-written `if` blocks collapse into a loop
over a resolved item list. The per-item logic that survives lives in the row.

## 2. The render surface and panes

### 2.1 What the surface is

The statusline is a rectangle: **width** = `COLUMNS - chromeReserve` (Phase 1), **height** =
however many lines we print, since Claude Code renders one row per stdout line. Panes
partition that rectangle.

### 2.2 The pane tree

A pane is either a **leaf** (holds items, renders them) or a **split** (holds children).

```
Pane
    Split      none | horizontal | vertical
    Children   Pane[]          (split only)
    Size       "auto" | "40%" | 24        (share of the PARENT's split axis)
    Border     border config, per pane, independent of any other pane.
               An IMPLICIT border applies to leaves only — see §8.
    Overflow   wrap | truncate | overflow    (§2.6) — inherited by this pane's items
    Ellipsis   string, default "…"           marker for truncation, "" for a hard clip
    MaxRows    int, default unbounded        rows this pane may occupy
    Items      item[]          (leaf only)
```

- **`vertical`** splits side by side — children divide the parent's **width**, each spanning
  the parent's full height. This is the hard one; it requires real compositing (§2.4).
  Concretely: `height(vertical split) = max(height(children))`, and **every** child is then
  rendered at exactly that height, its border drawn around the full extent rather than around
  its content. `valign` distributes the leftover inner rows above and below the content; it
  never changes a pane's extent. A box that stops short of its siblings is this rule broken.
- **`horizontal`** splits top to bottom — children divide the parent's **height**, each
  spanning the parent's full width. This is nearly free, because rows are already the output
  unit.

Naming follows tmux/vim convention: a "vertical split" produces a vertical divider.

**Leaf-or-split is decided exactly once, at config resolution, and normalized before anything
downstream sees the tree.** A non-empty `children` with `split` absent normalizes to
`vertical` — a user who wrote `children` has stated the intent to split unambiguously, and
side-by-side is the statusline-shaped default. `--check` notes the omission. Conversely a pane
with `split` set and no children is a leaf, and the stray `split` is dropped.

This is a normalization rule, not merely a default, and the distinction is the point: the
ambiguous state must not survive into the renderer, so no code downstream needs a rule for it and
none can invent a different one. The failure this prevents is two modules disagreeing about
whether the same pane is a container — one resolving borders as a leaf while the other renders it
as a split, producing a pane whose items silently never render. There is ONE predicate, derived
during resolution, and border resolution and render branching both read it rather than each
recomputing the question from raw config fields.

### 2.3 Sizing

Along the split axis, each child declares `size`:

- **integer** — exactly that many cells (or rows), clamped to what remains.
- **`"content"`** — **the anchor**: exactly what the pane's content intrinsically needs, and
  no more. The pane declares its width; the layout does not impose one.
- **`"NN%"`** — that share of the parent's axis extent, rounded down.
- **`"fill"`** / **`"auto"`** (default) — an equal share of whatever is left. This is the pane
  that *absorbs* the consequences of everyone else's sizing, and wraps its content into
  whatever it ends up with.

Resolution is deterministic, in this order: **fixed → content → percent → fill**. Explicit
numbers win over derived ones; derived-but-firm wins over relative; `fill` takes the remainder,
split left-to-right with the **last** `fill` child absorbing the rounding remainder.

`minSize` / `maxSize` bound a `content` pane, because an anchor with no ceiling can starve
everything beside it. A `content` pane is clamped to `maxSize` (and to what actually remains);
its content then degrades under its own §2.6 rules rather than the layout stretching to obey.

`maxSize` is not enough on its own, though, because it is a fixed number the config author has
to guess. The layout also has to stop an anchor from starving its siblings *as a function of
how much room actually exists*. That is the **viability floor**: the width below which giving a
pane space is pointless.

```
floor(p):
    p.minSize set             ->  p.minSize                (author said so; always wins)
    p is a vertical split     ->  Σ floor(children) + gutters
    p is a horizontal split   ->  max(floor(children))
    p.size is fixed           ->  the fixed size
    p.size is "content"       ->  p.minSize, else 0
    p.size is fill/percent    ->  MinUsableWidth (20) + (p bordered ? borderReserve : 0)
```

Note which kinds get the default 20: **`fill` and percent only.** A `content` pane's floor is 0
absent an explicit `minSize`, and a fixed pane's floor is what it declared. Imposing 20 on a
`content` pane would be wrong on its own terms — a pane sized to its content is by definition
usable at that size — and it would forbid the very degrade §2.9 depends on.

A split's floor follows its orientation, for the same reason its allocation does: the axis a
split **divides** sums, the axis it **shares** maxes. A vertical split's children divide width,
so its width floor is their sum plus gutters; a horizontal split's children each span the full
width, so its width floor is the largest of theirs. Getting this backwards over-states the floor
of a nested horizontal split, which caps a `content` sibling harder than the room warrants and
shows up much later as an unexplained degrade rather than as an arithmetic bug.

**Allocation within one split, one pass:**

1. `avail` = this pane's inner width − Σ gutters.
2. Grant every **fixed** pane its size. `rem` = `avail` − Σ fixed.
3. `reserve` = Σ `floor(p)` over every unresolved percent/`fill` sibling.
4. For each **`content`** pane, in declaration order:
   - `cap`   = `rem` − `reserve` − Σ (`minSize` else 0) of the `content` panes after it
   - `grant` = clamp(measured, `p.minSize`, min(`p.maxSize`, `cap`))
   - `rem` −= `grant`
5. **Percent** panes: `pct × avail` — the pre-allocation figure, not `rem` — clamped to `rem`.
6. **`fill`** panes split `rem` evenly; leftover cells go to the leftmost.

Step 4 is the rule that keeps an anchor from eating the surface, and it has **no trigger
condition**. The cap is applied on every pass unconditionally; it simply does not bind when
space is ample. At the §2.9 demo's wide case the cap is 108 − 24 = 84 against an ask of 43, so
it is inert; at the narrow case it is 56 − 24 = 32 against the same ask, so it binds and forces
the degrade. Same formula, no branch. Any prose elsewhere in this spec that reads like a
special "the surface is too tight" mode is describing this formula's *outcome* — there is no
second code path, and implementing one violates §1.

No child may resolve below 1 cell; children that would are **dropped entirely** rather than
rendered at a nonsense width, and the freed space is redistributed. A **`fill` or percent** pane
whose resolved inner width falls under `MinUsableWidth` (20) suppresses its own border first
(SPEC.md §6b narrow-width suppression, now applied per pane rather than per surface), and is
dropped only if it still does not fit.

`MinUsableWidth` governs `fill` and percent panes **only** — for border suppression exactly as
it does for the viability floor above, and for the same reason: *a pane sized to its own content
is by definition usable at that size.* A `content` pane is never squeezed; it requested its width
— borderReserve included — and was granted it, so suppressing the border it already paid for
would leave it holding dead space where its box should be. A fixed pane is the same case: the
author named the number. Suppression exists for a pane squeezed below viability by *someone
else's* sizing, which is precisely what `fill` and percent are. One predicate governs both rules;
letting the floor and the suppression disagree about what `MinUsableWidth` means is what produced
a `content` pane granted 12 cells for 8 columns of text and no border at all.

**Over-constrained splits.** If `rem − reserve` goes negative, the split cannot honour its own
floors — three bordered panes in a 40-column terminal, say. The cap must not be allowed to go
negative and sub-floor panes must not be produced silently. This routes into the same drop loop
as the <1-cell case: drop the **last** child, recompute from step 1, repeat. One loop, two
triggers — not a new mechanism. If it reduces to a single child, that child takes the full
width, and §2.6's surface-level fallback applies only if it is by then the sole pane of the
*surface*. This is a layout outcome, not an error, and nothing is logged.

**Intrinsic width is measured, never estimated.** A pane reports what it needs by actually
assembling its content: the width it would need to render unwrapped on a single row. That is
worth knowing before using it — a `content`-sized pane asks for its *entire* unwrapped width,
which for the 14-segment statusline is far more than any terminal has. `content` is for anchors
whose natural size is small and meaningful; `fill` is for everything else. That is exactly the
division in §2.9.

**Sizing iterates to a fixpoint, and the fixpoint is guaranteed to exist.** A single pass is
not enough: an anchor that asks for 39 and is clamped to 25 wraps, and its longest wrapped row
may be only 18 — stopping there strands 7 columns inside the anchor while the `fill` sibling is
cramped. Freed space must reach the sibling.

So:

1. Every `content` pane reports its preferred intrinsic width. Resolve all sizes by running the
   six-step allocation above.
2. Any `content` pane granted **less** than it asked for — which now includes being capped by
   step 4 on account of a sibling's floor, not only by `maxSize` or by exhaustion —
   re-reports its intrinsic width *under the width it was actually granted*, i.e. after
   wrapping at that width — i.e. its longest wrapped row (§2.9).
3. Re-resolve. Repeat while any request changed.

**A pane that degrades stays degraded for this render.** When an anchor is capped to 32, falls
back to text needing 12, and its `fill` sibling consequently balloons from 13 to 44, the anchor
must **not** re-expand on noticing the slack. The monotone clamp below forbids it, and this is
the exact case that clamp exists for: re-expansion here is a genuine two-cycle, not a
hypothetical one.

**Termination is enforced, not assumed: a pane's request may never increase between passes.**
If a re-measurement returns a larger width than that pane's previous request, it is clamped to
the previous request. Requests are therefore non-negative integers that strictly decrease until
they stop changing, so the loop converges — in practice in two passes. Cap it at **3** as a
backstop and use the last resolved sizes if the cap is hit.

The clamp is what makes this safe, and it is deliberately not a good-behavior assumption about
content: a renderer that asked for *more* space when given *less* would cycle forever, so the
layout refuses the request rather than trusting every present and future renderer not to make
it.

An earlier draft of this section banned re-measurement outright, reasoning that it could
oscillate. That was over-cautious and it was wrong about the mechanism. The loop runs **within
a single render**, and a single render is deterministic — same inputs, same output — so it
cannot produce flicker across renders no matter how many passes it takes. Only a changing input
can do that, which is not something layout gets to prevent.

`gutter` (default `0`) is the number of blank cells between siblings in a vertical split,
subtracted from the available extent before children are sized.

**`distribute` — sizing every pane correctly still gives the wrong surface.**

Everything above resolves each pane's width from that pane's own declaration. Every pane can be
individually correct and the surface still wrong, because the quantity a reader cares about is
not any pane's width — it is how many rows the whole surface occupies. A `content` pane capped
just below its natural width wraps to two rows while its `fill` sibling sits half empty, and
neither pane is in a position to notice: the cap was satisfied, the fill was satisfied, and the
surface grew by a row that a different split would not have cost.

No static configuration fixes this, and that is a measured claim rather than a design opinion.
Sweeping `maxSize` for a real two-pane config across terminal widths 100–240 produced no value
that wins everywhere: `42` holds a flat 4 rows at every width; removing the cap reaches 3 rows
at 160+ but costs **7** at 100, because the right pane takes its natural width and the left
wraps four times paying for it. Any single number is a bet on one terminal width, and the user
resizes.

So a split may declare how it divides extent among its children:

```json
{ "split": "vertical", "distribute": "min-rows", "children": [ ... ] }
```

- **`greedy`** (default) — the resolution above, unchanged. It stays the default because it is
  what every existing config already means, and a layout policy is not something to change under
  a user who did not ask.
- **`min-rows`** — choose the allocation minimising the surface's total row count.
- **`even`** — equal extents, for a layout that must not move as content changes. Stability is
  sometimes worth more than tightness; a status bar that reflows on every token count is
  harder to read than one that wastes a column.

`min-rows` binds only the extent left over after the existing rules have had their say. Fixed and
percent panes are *not* candidates — they were given an exact answer and the policy does not
overrule it — and `minSize`/`maxSize` remain hard bounds on every candidate considered. The
policy chooses among allocations that are already legal; it never invents one that isn't.

Ties break toward the **most even** allocation. Two allocations costing the same rows are equally
good by the stated objective, and evenness is the tiebreaker a reader will perceive as
deliberate rather than arbitrary.

**The search must be over breakpoints, not widths.** The naive reading of "minimise rows" is to
lay the surface out at every candidate width and count, which at 180 columns is ~180 full layouts
per render against a 44 ms budget currently spending 13 ms. That would trade a feature for the
performance property this project was rebuilt to get. It is also unnecessary: rows-as-a-function-
of-width is a **monotonically non-increasing step function**, and it can only step where a pane's
greedy packing gains or loses a row — at most once per item. A pane with 14 items has at most 14
breakpoints, so the candidate set is *tens* of allocations, not hundreds, and the optimum is
guaranteed to sit at a breakpoint because the function is flat between them.

An implementation that samples every width is therefore not a slower version of this — it is a
different algorithm with the same output and the wrong cost, and it must not ship on the grounds
that the tests pass. **Latency is re-measured against the p90 budget as an acceptance condition
for this feature, not as follow-up work.**

#### 2.3.1 The allocation algorithm

Search the **answer**, not the input. The naive framing — try allocations, count rows — searches
a space that grows with the number of panes. Inverting it collapses the problem: instead of
asking "how few rows does this allocation cost?", ask "is a surface of `T` rows achievable at
all?" That question is cheap, and it is monotone in `T`, which is the whole game.

**The two functions this rests on.**

For a candidate pane `i`, `rows_i(w)` is its greedy row count at width `w` — the existing packer,
called unchanged. It is non-increasing in `w`. Define its inverse:

```
minWidth(i, T) = min { w : rows_i(w) <= T }
```

the narrowest width at which pane `i` fits in `T` rows or fewer. Compute it by binary search over
`[minSize_i, maxSize_i]`, which is valid precisely because `rows_i` is monotone. That is
`O(log w)` packings per pane per `T`, and it is what keeps the search over breakpoints rather
than widths: the binary search *lands on* a breakpoint without ever enumerating the widths
between them.

**Feasibility.** A vertical split places panes side by side, so the surface is as tall as its
tallest pane. Therefore a surface of `T` rows is achievable exactly when every candidate can be
made to fit in `T` rows with the extent available:

```
feasible(T)  ⇔  Σ minWidth(i, T)  ≤  R
```

where `R` is the extent remaining after fixed and percent panes and gutters have taken theirs. If
any `minWidth(i, T)` exceeds that pane's `maxSize`, `T` is infeasible regardless of `R` — the
bound was declared hard and the policy does not overrule it.

**The search.** `minWidth(i, T)` is non-increasing in `T`, so `feasible(T)` is monotone: once a
row count is achievable, every larger one is too. Scan `T` upward from 1 and stop at the first
feasible value. `T` is bounded by the largest item count in any candidate pane — around 20 in
practice — so a linear scan is both adequate and clearer than a binary search over `T`. The
result is exact, not a heuristic.

**This is O(N) in the number of panes, which is why it is the algorithm and not merely one.** A
search over allocations would be exponential in `N` and would have forced restricting the feature
to two-pane splits. Searching over `T` instead makes the pane count almost free: each additional
pane adds one term to a sum. The two-pane case that motivated the feature is just `N = 2`, with no
special path.

**Distributing the surplus.** The winning `T` leaves `S = R − Σ minWidth(i, T)` unallocated.
Giving a pane more width can never increase its row count, so distributing `S` is always safe and
is purely the tiebreak from above made concrete — spend it on evenness:

- Water-fill: repeatedly give one cell to the currently-narrowest candidate, subject to its
  `maxSize`. Equivalently, level the widths up to a common ceiling and hand any remainder left to
  right.
- **A `content` pane is capped at its natural width.** Extra columns beyond what its content needs
  are not a benefit to it — fitting its content is the entire meaning of `content` — so surplus
  past that point flows to the `fill` siblings instead.
- If every candidate is capped and surplus remains, the split leaves it unconsumed, exactly as
  `greedy` would. The policy chooses among legal allocations; it does not invent a place to put
  extent that no pane asked for.

**Worked, against the config that motivated this.** Two candidates, `R = 108` at `COLUMNS = 112`
— 112 less the 3-column `chromeReserve` is a 109-column surface, less 1 for the gutter between
the two panes, exactly as §2.9 computes it.
`T = 1`: the left pane's ten items need far more than 108 columns on one row, so
`minWidth(left, 1)` alone exceeds `R` — infeasible. `T = 2`: still infeasible. `T = 3`:
`minWidth(left, 3) + minWidth(right, 3) ≤ 108` — feasible, so `T = 3` wins and the surplus is
water-filled. Three packings-with-binary-search per pane per `T`, four values of `T`, two panes:
tens of packer calls, which is the cost this section demanded.

**Acceptance conditions**, both of which must be demonstrated rather than argued:

1. **Optimality** — on a config small enough to brute-force, the allocation this returns must
   equal the best found by exhaustively laying out every legal width. The brute force belongs in
   the test, never in the shipped path; it exists to prove the fast algorithm agrees with the
   slow, obviously-correct one.
2. **Latency** — p90 re-measured against the budget (§5) with `min-rows` active across widths
   100–240. A regression here fails the feature, per the paragraph above.

### 2.4 Compositing — the part that must not be improvised

A leaf pane renders to a **`PaneBuffer`**: an ordered list of rows, each carrying its markup
*and* its measured plain width. A split composes its children's buffers into one buffer. The
root buffer is printed, one row per line.

Extending the v1 invariant, which stays in force:

> **`RowLayout` is the sole authority on line breaks within a pane. The compositor is the sole
> authority on the surface. Spectre must never re-break either one.**

Compositing rules, all load-bearing:

1. **Every row of a pane buffer is padded to exactly the pane's width** before it is joined to
   a sibling. A short row is not "close enough" — one missing cell shears every column to its
   right for that row only, which is the ugliest possible failure and the hardest to spot in a
   screenshot.
2. **Sibling buffers in a vertical split are padded to a common height** with full-width blank
   rows. Ragged heights corrupt the same way ragged widths do.
3. **Width is measured on ANSI-stripped text**, never on the markup string, using the same
   `Plain.Length` metric as v1 (§11 keeps wcwidth out of scope deliberately).
4. **Trailing whitespace is trimmed once, at the very end, on the composed root rows** — and
   only when the rightmost contributing pane has no background color set, because with a
   background those cells are visible. This is what preserves byte-parity in the single-pane
   case (§2.7).
5. **`Profile.Width` stays at the sentinel** for all pane rendering. Panes are sized by our
   arithmetic; Spectre is used for styling and border glyphs, not for layout. The one thing
   that must never happen is Spectre deciding a break we did not ask for.

**Emptiness — nothing to say means nothing drawn, but only where the user did not ask for a
shape.** SPEC.md:353 required "no segments ⇒ zero output even with border enabled". That rule
survives the pane rewrite and it applies at two levels, differently:

- **An empty surface emits nothing at all** — no border, no blank row, zero bytes. A statusline
  is a permanent fixture at `refreshInterval: 1`; an empty box occupies terminal rows forever
  in exchange for no information, which is strictly worse than absence.
- **An empty `content` or `fill` pane collapses**, taking its gutter with it, and its siblings
  divide the extent it released. Rendering a two-column box around nothing is the same trade as
  above, in miniature. **§2.11 defines which emptiness counts** — not every empty pane qualifies,
  and the one that does not is the one the sizing loop emptied itself.
- **An empty `fixed` or `percent` pane keeps its extent and its border.** The user named a
  number, and the same principle that keeps those panes out of `min-rows` candidacy (§2.3)
  keeps them here: an explicit instruction is not overruled because the framework judged the
  result pointless. Reserving declared space is often exactly the intent — a pane that holds
  its position while its content comes and goes.

This deliberately accepts that a collapsing pane shifts its siblings. That shift is visible and
self-explanatory; a permanent empty box is neither, and a user who wants stability has both
`fixed` and `distribute: "even"` (§2.3) to ask for it in the config rather than receiving it as
an accident of the compositor.

### 2.5 A pane's content sees only its own pane

**Content is laid out against the pane's inner width, never against `COLUMNS`.** A pane is the
entire world its content knows about.

A pane's **inner width** is its resolved outer width minus its own chrome:

```
inner = outer - (bordered ? borderReserve : 0)        borderReserve = 4  (2 verticals + 2 padding)
```

`chromeReserve` is subtracted exactly once, at the root, to get the surface width. It never
appears again anywhere below that.

Everything downstream takes inner width as a parameter:

- `RowLayout.Wrap` packs segments to the **pane's** inner width. Greedy packing, the separator
  budget, and the row-break decisions are all pane-local.
- The narrow-width rules are evaluated **per pane**: a `fill` or percent pane whose inner width
  falls below `MinUsableWidth` (20) suppresses its own border and re-measures (§2.3 — `content`
  and fixed panes never suppress, having sized themselves); a wide terminal split into
  four columns hits these thresholds constantly, so they are ordinary operating conditions now,
  not an edge case for tiny terminals.
- Truncation (§2.6) measures against the pane's inner width.
- `command` providers receive `CLAUDE_TUI_LINE_PANE_WIDTH` = the inner width of the pane the
  item lives in, so a user script can size its own output to the space it actually has.

**Enforcement, not just intent:** `COLUMNS` is read exactly once, in the surface-sizing code at
the root. No leaf-rendering code path may reach it — not `RowLayout`, not `SegmentBuilder`, not
any provider. Leaf rendering is a pure function of `(items, innerWidth)`.

That purity is directly testable, and §10 requires it: **the same leaf pane at the same inner
width renders identically whether it is the root pane of an 80-column terminal or the third
child of a split in a 200-column one.** If those two outputs differ, something below the
compositor is still reading the surface width, and that is the defect the rule exists to
prevent.

### 2.6 Overflow — wrap or truncate, chosen per pane

Content that does not fit its pane is a routine condition, not an error, and how it resolves is
the **user's choice**, configured per pane and overridable per item.

Two levels of "does not fit" exist and must not be conflated:

- **Segment packing** — several items do not fit on one row. Existing greedy packing flows them
  onto additional rows. Every mode does this; it is not what `overflow` selects.
- **A single value wider than the pane** — no packing can help. This is what `overflow` decides.

**`overflow` values:**

| value | a segment wider than the pane inner width | row can exceed pane width? |
|---|---|---|
| `"wrap"` | hard-broken across continuation rows; nothing is lost | never |
| `"truncate"` | cut to fit, ending with the marker; the tail is lost | never |
| `"overflow"` | emitted whole, spilling past the pane | yes — legacy only |

**`"wrap"`** — the pane grows taller instead of losing text. In a vertical split a taller pane
forces its siblings to pad to the same height (§2.4), so one wrapping pane grows the whole
surface; `maxRows` is what bounds that.

**`"truncate"`** — the pane keeps its height and loses the tail of the value. The marker is
`ellipsis`, default `…`, and setting it to `""` gives a hard clip that sacrifices no cell —
which is what a very narrow pane usually wants. The marker's own width is budgeted against the
inner width, and if the inner width is not greater than the marker width the marker is dropped
rather than allowed to consume the whole pane.

**`"overflow"`** — v1's behavior, preserved for byte-parity and nothing else. It is **only
legal when the surface has exactly one pane.** Inside any split it corrupts the neighbor to its
right, so `--check` rejects it there and the renderer treats it as `"truncate"`.

**Defaults**, which are deliberately context-sensitive:

- Single root pane ⇒ `"overflow"`. This is the compatibility default and is what makes the
  §2.7 parity gate achievable at all — widths 21–24 exercise exactly this path.
- Any pane inside a split ⇒ `"truncate"`. Corrupting a neighbor is never an acceptable default;
  losing the tail of one over-long value is.

**Vertical overflow** is governed by the same marker. When wrapping produces more rows than the
pane's `maxRows` (or the surface's, §2.8), the surplus rows are dropped and the last surviving
row ends with the marker, so truncation is always visible rather than silent.

**The `MinUsableWidth` single-line fallback is a property of the SURFACE, not of a pane.**

`RowLayout` emits one unwrapped, deliberately over-wide row when its available width is under
20. That is correct v1 behavior for a whole terminal too narrow to pack into, and it is the
`overflow` mode expressed at the layout level.

Inside a split it is a defect. §2.3 explicitly permits a pane to resolve below 20 columns — it
suppresses its border and keeps rendering — so a narrow pane is a routine outcome of splitting
a wide terminal, not a tiny-terminal edge case. If such a pane took the single-line fallback it
would emit one long row straight through its neighbour.

The rule:

- **Surface has exactly one pane** ⇒ the fallback applies as in v1. Parity depends on it.
- **Surface has more than one pane** ⇒ no pane ever takes the fallback. A pane under 20 columns
  packs and then wraps or truncates per its own `overflow` mode, exactly like any other pane.
  Narrow is not special; it is just small.

Consequently **the overflow mode is applied after `RowLayout` unconditionally**, never only to
rows that packed "normally". Any path that hands a row onward without measuring it against the
pane width is the bug this rule exists to prevent.

**Two implementation traps, both non-negotiable:**

1. **Break on plain text, never inside an escape sequence.** Width is measured and cut on the
   ANSI-stripped string; the markup is re-applied to each resulting piece.
2. **Style is re-emitted on every continuation row.** A hard-broken segment must carry its
   color onto each row it occupies. A style that opens on row 1 and is never reopened leaves
   rows 2+ unstyled — or worse, bleeds into the pane beside it.

### 2.7 Iteration 1 — one pane filling the statusline

Ships the entire pane machinery with exactly one leaf pane at the root, and is verified by a
single hard claim:

> **Output is byte-identical to the pre-pane build across the full width sweep**, border on and
> border off.

Any diff at any width is a compositor bug, not a behavior change, and is fixed rather than
re-baselined. Splits ship only after that claim holds — a compositor debugged against a known
output is cheap; one debugged against a moving target is not.

### 2.8 Height

`surface.maxRows` (default `8`) bounds the whole surface, and a pane's own `maxRows` bounds it
individually. Nothing about a statusline should be able to eat half the terminal because one
pane got chatty — a live risk once `overflow: "wrap"` exists, since wrapping trades width for
height. **`maxRows` is a hard ceiling: the surface never emits more rows than it allows.**

#### There is no height fixpoint, and there must not be one

The obvious symmetry — a row-budget fixpoint mirroring §2.3's width fixpoint — does not work,
and reaching for it is the trap this subsection exists to close.

Width converges because every pass makes requests monotonically smaller. Height has no such
direction. The lever that reduces a wrapping pane's height is *more width*, and in a vertical
split more width for pane A is less width for pane B, which then wraps taller — and since
`height(vertical split) = max(height(children))` (§2.2), the surface may not shrink at all. Two
coupled dimensions pulling opposite ways, with no monotone quantity, is not something to iterate
to convergence once per second on the user's critical path.

So height is resolved by a **deterministic degrade ladder**: an ordered sequence of steps, each
of which *strictly* reduces row count, applied only until the budget is met. It terminates
because the ladder is finite and every rung is strictly reducing.

1. **Measure.** Rows within budget — stop. This is the overwhelmingly common case and must cost
   nothing.
2. **Demote `wrap` to `truncate`**, one pane at a time, in **reverse declaration order**, re-measuring
   after each. Later-declared panes degrade first; the first-declared pane is the author's
   primary content and is the last thing to lose fidelity. This overrides an explicit
   `overflow: "wrap"`, which is a real surprise and is accepted deliberately — a surface that
   silently grows without bound is the worse surprise, and `maxRows` is the author stating which
   of the two they want.
3. **Drop trailing items** from the tallest pane, one at a time, re-measuring after each, using the
   existing §3.1 block ordering rather than a second priority scheme.
4. **Clip**, as the last resort.

#### Clipping must close the border

A bordered pane clipped mid-box emits a top edge and two verticals with no bottom edge. That
does not read as "truncated", it reads as "crashed" — the failure mode §7 exists to prevent, so
the ladder must not produce it.

When step 4 clips a bordered pane, the **last emitted row becomes that pane's bottom edge**,
replacing the content row that would otherwise have occupied it. The box always closes. The
`ellipsis` marker goes on the last surviving *content* row, so a clipped surface still never
looks like a complete one.

Clipped rows remain subject to §2.4: every emitted row is exactly `COLUMNS - chromeReserve`
display columns. Degrading height never licenses a ragged row.

#### A pane may shrink-wrap its height

`height(vertical split) = max(height(children))` (§2.2), and every pane fills its band. A pane
with two rows of content beside a pane with three therefore draws a three-row box with one row
of nothing inside it. `valign` does not help: it positions content *within* the box, so it
relocates the blank row rather than removing it.

A pane may instead declare **`height: "content"`** (default `"fill"`), and its border box closes
immediately under its last content row. The band rows it does not occupy are surface background,
outside any border.

The vocabulary is deliberately the same as §2.3's width `size` — `content` means "as tall as
what is in me", `fill` means "take the band" — because it is the same question asked on the
other axis, and a second spelling for one idea is §1's rule being broken for the sake of a
prettier word.

**`valign` keeps its meaning and gains a second subject.** Under `fill` it places the content in
the box; under `content` the box is exactly the content, so `valign` places the *box* in the
band. One concept — where does the short thing sit inside the tall space — and no new knob.

Three things this does not do, each worth stating because each is a plausible expectation:

- **It does not make the statusline shorter.** Total rows are still `max` over the children; the
  left pane's three rows still cost three rows. This changes where border glyphs are drawn, not
  how much vertical space the surface occupies. Anyone reaching for it to save a row wants
  §2.3's `distribute: "min-rows"` instead.
- **It does not interact with the degrade ladder.** `maxRows` bounds the surface; shrink-wrapping
  reduces no pane's content and so can never bring a surface inside a budget it was outside.
- **It is not the empty-pane rule.** §2.4 collapses a pane with *no* content. This is a pane with
  *less* content than its siblings, which is the ordinary case rather than the degenerate one.

**This must be authored into §2.10, not bolted onto it afterwards.** Under the shared-edge model
two adjacent panes share one column of vertical edge; when one box is shorter, that column is
shared for part of its run and belongs solely to the taller pane for the rest, and the junction
where the shorter box closes against it is a glyph case the grid does not otherwise produce.
That is tractable — the glyph table gains rows — but a border grid designed on the assumption
that every column is a full-height rectangle will not accommodate it later without being redone.

The user's stated next test, and what "splits work" means concretely:

> *Left pane: my current statusline. Right pane: the model, effort, thinking and context
> segments, with the pane border and the model text sharing one colour.*

```json
{
  "surface": {
    "maxRows": 8,
    "pane": {
      "split": "vertical",
      "gutter": 1,
      "children": [
        { "size": "fill", "overflow": "wrap",
          "border": { "enabled": true, "color": "grey" } },
        { "size": "content", "overflow": "wrap",
          "border": { "enabled": true, "color": "@model-accent" },
          "items": [ { "item": "model", "color": "@model-accent" },
                     { "item": "effort" }, { "item": "thinking" }, { "item": "context" } ] }
      ]
    }
  }
}
```

**The model pane is the anchor; the statusline pane absorbs.** The right pane is `content`, so
it declares its own width from the text it must draw. The left pane is `fill` with
`overflow: "wrap"`, so it takes whatever remains and reflows into it. Nothing in this config
names a column count — change the model name or the terminal width and the layout re-derives
itself.

The left pane omits `items`, so it gets the default list — all 14 builtins, i.e. today's
statusline, reflowed to whatever width the anchor leaves it.

**The arithmetic at `COLUMNS=112`**, as the shipped build actually resolves it:

```
surface   = 112 - 3 (chromeReserve)             = 109
           - 1 (gutter)                         = 108
right     = content: its 4 items unwrapped on one row + borderReserve 4 = 66
left      = fill: 108 - 66 = 42  → inner = 42 - 4 = 38
```

The anchor asks for its whole unwrapped row, gets it, and the statusline reflows into the 38
that remain. **This is the case that motivates `distribute` (§2.3).** Both panes are individually
correct — `content` got exactly what it asked for, `fill` got the remainder — and the surface is
still wrong: the right pane spends 66 columns on one row while the left pane wraps into six.
A layout can satisfy every declared size and still be the wrong layout, because the quantity the
reader cares about is total rows.

**The same config at `COLUMNS=60`**, which is where the §2.3 cap earns its place:

```
surface   = 60 - 3 - 1 (gutter)                          = 56
floor(left) = MinUsableWidth 20 + borderReserve 4        = 24      (it is `fill`, so it has one)
pass 1    right asks 66; cap = rem 56 - reserve 24       = 32      (§2.3 step 4)
          right granted 32  →  its inner = 32 - 4        = 28
```

Note what did *not* happen: right asked for 66 and a naive reading would clamp only against the
remaining 56, granting it and leaving the left pane nothing usable. It is the §2.3 step-4 cap —
`rem − reserve`, reserving the `fill` sibling's floor — that pulls the grant down to 32.
**That cap is the mechanism; there is no "surface is too tight" predicate anywhere.**

**The second pass has to free something, and for a while it did not.** Under the 28-column
inner grant the right pane wraps to three rows whose longest is 22, so it needs 26 columns, not
32. §2.3 requires it to re-report that and hand the 6 columns back to its sibling — "freed space
must reach the sibling" — and for a period the build re-reported the width it had been granted
instead, squeezing the left pane to its 20-column floor so that `jimcline/claude-tui-line`
wrapped mid-word while six columns sat unused inside the anchor.

The cause is worth recording, because it was introduced by a deletion rather than by a bug: the
degradation that used to shrink a re-measured request was the banner-to-text fallback, and when
the banner renderer was removed nothing else implemented one. The fixpoint loop survived intact
while the only thing that made it converge to something *smaller* went away, leaving machinery
that runs and cannot change its answer. **A re-measurement under a narrower grant must return the
longest wrapped row, not the grant.** That is the wrap-aware measurement `distribute` also needs,
so it is specified once, here, and used by both.

**That requirement is now implemented, and the deletion above is repaired.** `ResolveVertical`
re-measures with the grant rather than without it (`measure(child, result.Grants[i])`),
`MeasureRequest` turns the grant into an inner cap and returns `LongestWrappedRowWidth` — the
longest row the segments actually wrap to at that cap, not the cap — the result is clamped
monotonically against the previous pass's request, and the split then reallocates from the
reduced requests, bounded at three passes. So a shrinking re-measurement is once again something
the loop can produce, which is exactly what the banner-to-text fallback used to be the only
source of.

Read directly from those paths; what that reading does **not** establish is the end-to-end
number. §10.6's fixpoint tests drive the loop through `measureOverride`, so they certify the
monotone clamp and the pass cap against a stub, not that the *real* measurement frees six
columns at `COLUMNS=112`. That assertion is still owed, and §10.6's rule applies to it — assert
that the sibling gains what the anchor gives up, not that `right == 32`.

**These integers are measured, not asserted.** They come from rendering the config above at those
two widths. Tests must assert **behaviour and invariants** — the cap binds at `COLUMNS=60`, the
re-measure returns the longest wrapped row, requests are monotone non-increasing, and every
composed row is exactly the surface width — and **not** `right == 32`.

**This costs height.** The left pane packs its segments into the 38 columns the anchor leaves it
at `COLUMNS=112`, so it runs six rows; add the border and the surface is eight rows tall. That is
the real price of a vertical split at this width, and it is what `distribute: "min-rows"` exists
to reclaim.

**What this test actually proves**, and why it is the right acceptance gate: the two panes have
**different natural heights** — one row of model text against however many rows the statusline
packs into whatever the anchor leaves it. Every compositing rule in §2.4 fires at once. Ragged
padding, height mismatch, `valign`, and a per-pane border all fail visibly here and are nearly
invisible in a single-pane test.

### 2.10 Borders are a grid the compositor owns, in one of two visual languages

A border is **not a property of a pane**. It is a property of the **boundary**, and the
compositor is the only thing that draws one. **Panes stop drawing boxes**: a pane renders
*content only*, into its own inner rectangle, and the compositor overlays a single resolved
**border grid** across the finished surface. This is not a new authority — it is §2.4's existing
rule ("the compositor is the sole authority on the surface") finally applied to borders too, and
it deletes the per-pane border-wrapping path along with the re-padding defence that path needed.

That much is unconditional. What is a choice is whether a boundary between two siblings is drawn
as **one line or two** — and both are legitimate visual languages, so the framework offers both
rather than ruling one out:

```json
"border": { "collapse": false }
```

- **`"collapse": false` (default)** — *separate boxes*. Each pane's edges are its own; adjacent
  panes are two complete boxes with the gutter between them. **This is what ships today**, and
  it is the default because a change of visual language is not something a framework should
  perform on an existing config during an upgrade.
- **`"collapse": true`** — *shared edges*. Adjacent edges resolve to one physical line that both
  panes touch. This is CSS's `border-collapse: collapse`, which is where the name comes from.

**The reason to collapse is width, not taste.** With gutter `g`, a separate boundary spends
`g + 2` columns — A's right edge, the gutter, B's left edge — while a collapsed one spends
exactly `1`. Every interior boundary hands back `g + 1` columns to content. On a statusline at
`COLUMNS=112` that is the difference between a pane wrapping and not, which is why this is a
config knob and not a stylesheet.

Everything below — per-edge selection, the junction table, the reserve decomposition, the
degrade order — applies to **both** models. Only the collapsing rule and the arithmetic differ.

**Per-edge selection.** Each pane declares which of its four edges it wants:

```json
"border": { "edges": { "top": true, "right": false, "bottom": true, "left": true },
            "color": "grey", "style": "rounded" }
```

with Excel-style shorthands, which are the friendly form and expand to exactly the above:

```
"border": "all"      every edge of every pane, dividers included
"border": "outline"  the outer boundary of this pane/split only, no interior dividers
"border": "inside"   interior dividers only, no outer boundary
"border": "none"
```

**Collapsing** applies only under `collapse: true`. A physical line is drawn if **any** adjacent
pane asks for it. Where two panes disagree about colour or style, the **first requester in tree
declaration order wins** — chosen because it is deterministic and explainable, not because it is
clever. `--check` (§9) reports every conflict it resolves, since a silently-dropped colour is
exactly the kind of thing a user will otherwise spend an evening on.

Under `collapse: false` no conflict can arise, because neighbours never contend for one line:
each pane's edges are drawn as it declared them, in its own colour and style. That is a second
reason it is the safe default — the mode that needs a tie-break rule is the one the user opts
into.

**Junctions.** At each grid position, the glyph is a pure function of which of the four
directions carry a line. Implement it as one 16-entry table per style, keyed by the `NESW`
neighbour mask — `0b0011 -> ┐`, `0b1111 -> ┼`, `0b0000 -> ` ` `, and so on. Never a chain of
special cases; the table generalises to any nesting depth for free, and a rounded style simply
supplies rounded glyphs for the four corner masks.

**Sizing.** `borderReserve` was a constant 4 that quietly conflated two different things: the
edge glyphs and the content padding. Split them.

```
reserve(p)  =  (edges p is charged for)  +  padLeft + padRight
```

Under `collapse: true` a shared edge is charged **once, to the split**, never twice to the two
neighbours — that double-charge is the whole bug this section exists to avoid. So for a vertical
split with `N` children and interior dividers on:

```
collapse: true    avail = splitInnerWidth − (N − 1)              // one column per boundary
```

**`collapse: false` is not a parent-side boundary formula at all, and an earlier draft of this
section was wrong to write one.** It said `avail = splitInnerWidth − (N − 1) × (g + 2)` and
claimed that was the arithmetic in force. It is neither.

It is not what the code does. `SizeResolver` charges the split's own border once to the split,
subtracts a flat `gutter × (N − 1)`, and lets each child's own border cost ride inside that
child's recursive floor and request. Border reserve is **per pane**, not per boundary.

And it does not add up. Under `collapse: false` the panes are separate boxes, so `N` children own
`2N` edge columns; `(N − 1) × (g + 2)` charges only `2(N − 1)` of them. It drops the outermost
pair — the leftmost child's left edge and the rightmost child's right edge — and under-reserves by
exactly 2 columns at every `N`. A surface built on it emits rows two columns too wide, which is
the §2.4 ragged-row violation, arriving from the sizing model rather than from a rendering bug.

So: **under `collapse: false` the boundary cost is the gutters alone, `gutter × (N − 1)`, and each
pane's edges are its own `reserve(p)`, accounted where that pane is measured.** That is what the
renderer already does. This section introduces one new formula — the `collapse: true` line above —
and changes nothing else about §2.3.

**Nothing outside `SizeResolver` may restate this arithmetic, including this document.** The
formula above is here to explain the model, not to be transcribed: the boundary cost lives in one
named function that the renderer calls, and §9.8's structural checks call **that same function**
rather than a copy. Two expressions of one arithmetic is how the draft above came to be wrong
without anything noticing, and it is the §1 rule with the serial numbers filed off — when
`collapse` lands, the renderer changes and a checker holding its own copy silently does not.

Under `collapse: true` the divider **occupies the gutter**: `gutter` must be ≥ 1 and defaults to
1; a `gutter` greater than 1 centres the line with blanks either side (`  │  `). Everything else
in §2.3 — the floor table, the six-step allocation, the fixpoint — is untouched and takes the
per-pane `reserve(p)` wherever it currently reads `borderReserve`.

**`height: "content"` is cheap under `collapse: false` and expensive under `collapse: true`.**
Separate boxes are independent, so a pane whose box closes early introduces no new glyph case at
all — its neighbour's edges are unaffected because they were never shared. Collapsed edges are
where the difficulty lives: a shorter box makes one column shared for part of its run and solely
the taller pane's for the rest, plus a junction where the short box closes against it. The
practical consequence is a sequencing one — **`height: "content"` can ship against the default
model without waiting for collapsing**, and only its collapsed-mode junctions need the grid.

**Edges are static config, and this is load-bearing.** Colours may be derived from a provider's
value (§6); edges may **never** be. An edge has extent, so a value-derived edge would put a
provider's output inside the sizing loop, and the §2.3 fixpoint's convergence argument does not
survive that. Static edges are known before sizing begins, which is precisely why this whole
feature needs no new sizing machinery.

**Degrade.** §2.3's narrow-width suppression gets sharper here: a squeezed pane drops its
**vertical** edges first, because columns are what is scarce and horizontals cost none. Losing
one line beats losing the whole box.

**Back-compatibility.** A surface with a single pane has no neighbours, so nothing collapses and
its four outer edges render exactly as today. The golden parity gate covers only the
no-`surface` single-pane config and is therefore unaffected by this section — if it moves, that
is a defect in the reserve decomposition, not an expected consequence.

#### 2.10.1 Five things the above does not say, ruled

Walking §2.10 over cases turned up five gaps, four of them in the §7.1 class — the config is
valid, `--check` is silent, and the surface renders something plausible and wrong.

**1. `outline` and `inside` are subtree instructions, not this node's four booleans.** The
shorthands are introduced as expanding "to exactly the above", meaning the declaring pane's
`{top,right,bottom,left}`. That is true of `all` and `none` and false of the other two: "no
interior dividers" and "interior dividers only" describe a *split's descendants*, which no
number of booleans on the split itself can express. Read literally, `"border": "outline"` on a
split sets the split's own four edges and leaves every child bordered as before — the outer box
plus every interior line, the exact opposite of what was asked, with nothing to report it. So:

- On a **split**, `outline` turns the split's own four edges on and every descendant's off;
  `inside` turns the split's own four off and interior dividers on.
- On a **leaf**, `outline` ≡ `all` and `inside` ≡ `none`, because a leaf has no interior.
  `inside` on a leaf silences the pane's border and is almost never meant: warning
  `border-inside-on-leaf` (§9.6.1).
- **A descendant's own explicit `border` overrides what an ancestor's shorthand set for it** —
  nearest declaration wins. Without this, `outline` is a mute an author cannot locally escape.

**2. `collapse` is a surface-level key and nothing else.**

```json
"surface": { "border": { "collapse": true } }
```

§2.10 makes the compositor overlay **one** resolved border grid. One grid cannot be half
collapsed: the boundary between a `collapse: true` subtree and a `collapse: false` sibling has no
defined width — one column by the first rule, `g + 2` by the second — and the sizing model has no
way to ask which. Accepting the key on a pane and quietly ignoring it is the §7.1 failure in its
purest form, so it is an **error**, code `collapse-not-surface-level`, not a silent ignore.

**3. The degrade rule does not work under `collapse: true`.** "A squeezed pane drops its vertical
edges first" assumes the edge is the pane's own column. Under collapsing it is not: the line is
drawn if **any** adjacent pane asks for it, so one pane dropping its right edge frees exactly zero
columns while the neighbour still wants its left. The sizing model would credit a column it never
recovered, and a row comes out wide — §2.4's ragged-row violation, arriving from the degrade step.

Under `collapse: true`, therefore, **degrade operates on boundaries, not on panes**: it drops an
interior divider entire, freeing exactly the 1 column that divider costs, outermost edges last.
Under `collapse: false` the existing per-pane rule stands untouched, because there a pane's
vertical edge really is its own column.

**4. A junction can have arms in more than one style.** "One 16-entry table per style" presumes a
single style at the position, but the tie-break resolves colour and style **per edge**, so four
arms may arrive from four panes carrying three styles. Ruling: the junction glyph comes from the
style of the **first requester in tree declaration order among the arms present at that
position** — the same tie-break as the edge itself, applied to the junction as if it were one more
participant. One rule rather than two, and `collapsed-edge-conflict` already reports that the
disagreement happened.

**5. `reserve(p)` is width-only, and horizontal edges cost rows.** Once edges are selectable
individually, `top` and `bottom` are no longer a pair that exists or does not — and the reserve
decomposition above says nothing about rows, while §2.8 reasons throughout about a box that
"closes under its last content row", written when both horizontals were always present. Name the
counterpart:

```
rowReserve(p)  =  (top ? 1 : 0) + (bottom ? 1 : 0)
```

under the same regime as `reserve(p)` — one named function, no transcription (see the paragraph
above). §2.8's `height: "content"` shrink-wrap adds `rowReserve(p)` to the pane's content row
count. §2.8's worked example, which spends `borderReserve 4` inline, stays correct only for the
all-edges case and is to be read as `reserve(p)` for that pane rather than as a constant.

### 2.11 An empty pane collapses — but only the kind of empty that is knowable before sizing

§2.4 already rules this: an empty `content` or `fill` pane collapses and takes its gutter with it;
an empty `fixed` or `percent` pane keeps its extent and its border, because the author named a
number and reserving declared space is often the intent. **That division stands and this section
does not touch it** — at `refreshInterval: 1` a fixed pane that collapsed and re-expanded as you
moved in and out of a repository would make the whole line jump once a second, and `content` is
already how an author asks for the other behaviour.

Defect 12 is that the renderer does not implement §2.4's half of it: a `content`-sized pane
holding nothing still draws its border, so a surface split into "git things" and "model things"
shows an empty box eating twenty columns the moment you `cd` out of a repository. The box is not
the bug; claiming the width is.

What §2.4 leaves unsaid is **which** emptiness qualifies, and that is the part with a trap in it.

**A pane becomes empty two ways, and conflating them does not terminate.**

- **Structurally empty** — `items: []`, or every item in it resolved to no value. §5 resolves
  every value *before* sizing begins, so this is knowable in a pre-pass, from the resolved
  dictionary alone, with no width in hand.
- **Emptied by degradation** — §2.8's ladder dropped its trailing items, or §2.3's over-constrained
  loop dropped the pane's contents to make the surface fit. This is a *function of the width* and
  is only knowable inside the sizing loop.

**Collapse applies to the first and never to the second**, and the reason is convergence rather
than taste. Collapsing a degradation-emptied pane frees its width; the freed width lets §2.8
un-drop the items it just dropped; the un-dropped items un-empty the pane; the pane reclaims its
width; the surface is over budget again and the ladder drops them once more. That is a cycle with
no fixpoint, and §2.3's convergence argument — every rung strictly reducing — is exactly what it
breaks. A pane the ladder emptied keeps its border. If the right answer there is to remove the
pane entirely, that is a rung the ladder should own, decided with the width in hand, not a
side-effect of a collapse rule reaching into the loop.

**What collapsing does**, for the structural case, on the `content` and `fill` panes §2.4 makes
eligible:

- The pane occupies **no width** and draws **no border**.
- **The parent drops the corresponding boundary with it.** §2.10's cost is a function of the child
  count, so a collapsed child means `N − 1`, not `N` with one child rendered blank. Leaving the
  gutter or divider behind is how a collapse turns into a visible seam with nothing on either side
  of it.
- **A split whose children all collapse collapses itself**, resolved bottom-up in the same pre-pass.
  One pass suffices because emptiness only ever propagates upward.
- **If the root collapses, the surface emits nothing** — zero rows, not an empty box and not a
  blank line. There is genuinely nothing to say, and §7's exit-0-with-valid-stdout still holds.

**Only `items: []` is a diagnostic, and the runtime case is not.** §9.4 warns about a pane with no
items because an author wrote an empty pane and probably did not mean to. A pane whose items are
all valueless outside a repository is working exactly as designed, gets no diagnostic, and could
not get one anyway: `--check` does not resolve values (§9.8), so the two cases are not even
distinguishable from where the validator stands.

#### 2.11.1 An explicit `minSize` suppresses collapse

**Two sections currently answer this differently, which is worse than either being wrong.**
§2.3's floor table reads `p.minSize set -> p.minSize (author said so; always wins)`. The rule
above says a collapsed pane "occupies no width". For a `content` pane with `minSize: 12` holding
nothing, one says twelve columns and the other says zero, and a reader consulting either comes
away confident.

**`minSize` wins, and collapse does not apply.** The pane keeps its floor and its border. Three
reasons, in order of weight:

- It is §2.4's own test. A `fixed` pane keeps its extent because "the author named a number and
  reserving declared space is often the intent." `minSize` is a number the author named. The
  distinction §2.4 draws is not really `content`-versus-`fixed`, it is *declared extent versus
  inferred extent*, and `minSize` is declared.
- It is the only vocabulary for "size to content, but hold this much." Let collapse override it
  and that sentence becomes inexpressible — the author's remaining option is `fixed`, which gives
  up content sizing entirely.
- It inverts safely. To get collapse, omit `minSize`; there is no way to be surprised into a pane
  that vanishes, only into one that stays.

**A follow-on: §9.4's `pane-no-items` warning must not fire on these.** Its stated rationale is
"it collapses, so the declaration did nothing" — which is false for a `content`/`fill` pane with
an explicit `minSize`, since that pane holds its space and is a legitimate spacer for exactly the
reason `fixed` and `percent` are. The registry row in §9.6.1 is amended accordingly. A diagnostic
whose reason has stopped applying is a false positive that teaches authors to ignore the checker.

#### 2.11.2 An item that did not answer is not an empty item

§5 resolves values before sizing, so the pre-pass sees one thing: a value or no value. But a
`command` item (§4) reaches no-value by two different roads — it ran and returned nothing, or it
**timed out, exited nonzero, or was killed** at the 150 ms budget (§7). The rule above collapses
the pane either way, and at `refreshInterval: 1` that means a pane that vanishes and returns as a
script drifts across its timeout: the whole line jumping once a second, which is the precise
failure §2.11 invokes two paragraphs earlier as the reason `fixed` panes do not collapse. It came
back in through the door this section had just closed.

So the resolver distinguishes **absent** — resolved, no value — from **unavailable** — did not
answer. **A pane holding an unavailable item does not collapse for that render.** It keeps its
extent and its border and renders whatever else it has, possibly nothing.

In §7.1's terms: "the tool did not answer in time" is not the author saying this pane is empty. A
layout that reshapes on a 150 ms timeout is present, plausible, wrong, and has nobody to report
it — the user sees a statusline that twitches and no reason anywhere for why.

This distinction is not new machinery. §5's TTL cache already has to know the difference to decide
what is cacheable, and §9.4.1's two-tier severity already separates "reported nothing" from
"failed". What is new is that the collapse pre-pass must read it.

## 3. Item model

An item resolves to a **block**: zero or more rows. Zero rows means "suppressed" — the existing
rule that a missing field renders nothing, never `null`. One row is the ordinary case and is
what every v1 segment is. More than one row is a user-defined item (§4) whose command emitted
multiple lines.

This generalization is worth taking now rather than later: a one-row-only item model would have
to be unwound the first time a user's script prints two lines.

```
StatusItemDefinition
    Id           string          stable key, used in config and cache
    Provider     provider        how the VALUE is obtained (§4)
    Format       string          "{}" placeholder, e.g. "ctx:{}%" — default "{}"
    Color        string          Spectre color name, or a threshold rule (§6)
    Align        left|center|right   within the pane — default left
    Overflow     string          optional per-item override of the pane's §2.6 mode
    Enabled      bool            default true
```

The provider is the only axis that distinguishes one item from another. Everything downstream —
formatting, colouring, packing, wrapping — is identical for a builtin and for a user's shell
script, which is what makes a user-defined item a first-class item rather than a bolt-on.

**A provider takes one `ItemContext`, never a growing parameter list.** The context carries the
session payload plus environment values probed lazily and memoized for the process — git branch,
remote URL, and whatever the next item needs. This is the §1 rule applied to the registry's own
signature: threading each new input as its own parameter means the Nth item that needs a new
probe re-edits all N rows, which is precisely the cost the registry exists to remove. The
signature widens once and then never again.

Laziness is not a nicety here. `refreshInterval: 1` makes startup cost the render cost, so a
probe that spawns a subprocess must run only when an item actually asks for it, and at most once
per render.

### 3.1 Blocks and packing

Row packing (§2.6) operates on single-row items. A multi-row block **occupies its own rows**:
it never shares a row with a neighbouring item and never has items packed beside it. A pane's
content is therefore a vertical sequence of packed single-row groups and standalone blocks, in
config order.

Panes also gain `valign` (`top` | `middle` | `bottom`, default `top`), which decides where the
content sits when a pane is shorter than its siblings — the padding from §2.4 goes below, split,
or above accordingly. Without it, a 1-row model pane beside a 6-row statusline sits awkwardly at
the top of its box — visibly so in §2.9.

### 3.2 Hyperlinks

An item may carry a `link` whose value is a URL template. The item's text is emitted wrapped in
an OSC 8 hyperlink, so a terminal that supports them makes it clickable and one that does not
shows the text unchanged.

```json
{ "item": "git-branch", "link": "{remote-url}/tree/{}" }
```

`{}` is the item's own value; `{other-id}` is another item's resolved value. No second lookup
mechanism — the same registry that resolves items resolves these.

**A referenced item does not have to be displayed, and this is the normal case rather than an
edge case.** The example above is exactly it: `remote-url` is a 40-plus-column URL that no one
wants occupying a statusline, and the entire point of referencing it is to make the short branch
name clickable instead. If `{other-id}` resolved only against items already placed in a pane,
the feature would work solely in the configuration nobody wants, and the example printed above
would not work at all. Resolution goes to the registry, which does not know or care what is on
screen.

A referenced id that names nothing must be distinguishable from one that names a real item,
because silently dropping the link makes a typo and a working config produce identical output.
`--check` **reports** a `link` template naming an unknown id — as a `warning`, per §9.4.1, since
the item still renders its text and only the decoration is lost. At render time the link is
dropped per the best-effort rule below, but that path is the fallback, not the diagnosis.

**Zero-width is the whole problem.** `\e]8;;URL\e\\text\e]8;;\e\\` costs no columns and is
roughly 40–80 characters, so anything that measures it as text puts the pane border that many
columns early and §2.4's rectangle invariant breaks. Three rules, none optional:

1. **`Plain` carries the link text only; `Markup` carries the sequence.** This is the split that
   already keeps SGR out of the width metric (§6); hyperlinks join it rather than getting a
   parallel mechanism.
2. **OSC is not CSI and must be scanned separately.** A CSI scan ends at the first letter; an
   OSC 8 runs to a string terminator — `\e\\` or BEL. Scanning an OSC as a CSI stops inside the
   URL and counts its tail as visible text. §10's existing "a hard break never lands inside an
   escape sequence" was written and tested against SGR only and does **not** cover this.
3. **A wrapped or truncated link closes itself.** A continuation row re-opens the hyperlink, and
   a truncated one emits the closing `\e]8;;\e\\` before the ellipsis. An unterminated OSC 8
   leaks: the terminal keeps hyperlinking subsequent output, including the user's next prompt.

**Derived items** cover the case where the thing to link is not an item but a fragment of one:

```json
{ "id": "issue", "from": "git-branch", "extract": "[A-Za-z]{2,}-[0-9]+", "case": "upper",
  "link": "https://linear.app/example/issue/{}", "color": "blue" }
```

`from` names a source item, `extract` is a regex whose first match becomes the value, and an
empty match suppresses the item under the existing missing-field rule (§3). This is deliberately
one general mechanism rather than a registry row per tracker: an issue id scraped from a branch
name and any future scraped fragment cost a config row, not a code change — the §1 rule.

`remote-url` is a new registry row: origin normalized to https, `git@host:path` rewritten,
`.git` stripped. It probes via `git remote get-url origin` rather than reading git config
directly, so `insteadOf` rewrites are honored. It is a value like any other, so it is linkable,
formattable, and testable on its own rather than being logic buried inside the branch item.

**Its cost follows the reference, not the placement, and that needs saying out loud.** Resolution
is demand-driven — §5 resolves exactly the ids in the resolution set — so `remote-url` costs
nothing at all until something names it. But once a `link` template names it, the subprocess runs
**every render**, which at `refreshInterval: 1` is a `git` process per second, whether or not the
URL is ever shown. That is the feature working as designed: the whole point of the worked example
is to reference it without displaying it. It does mean "opt-in because it shells out" is a weaker
guard than it looks, since a `link` opts in on the user's behalf without the id appearing in any
pane. The obligation this creates is on the provider, not the config: **a provider whose cost is a
subprocess caches within a render and should cache across renders under a TTL**, exactly as a
`command` item must (§4). An item is not entitled to spawn a process per second because a
decoration referenced it.

**A raw OSC 8 emitted by a `command` provider (§4) is measured the same way**, since a user's
script can emit one directly — which is exactly how this defect was found. `Segment.Plain` is
contracted escape-free, so sanitizing belongs where Plain is built from raw command output, not
at the measurement sites. Preserving the script's own colour and links in `Markup` is desirable
but secondary: correct width first, fidelity if the markup path can carry raw ANSI without a
fight.

Preserving that fidelity brings rule 3's leak with it in a second form. A script that opens an
SGR and never resets bleeds into every segment after it, exactly as an unterminated OSC 8 bleeds
into the user's next prompt. **A segment built from raw command text terminates its own styling
regardless of what the script emitted.**

The strip helper's OSC scan has two ways to be wrong in opposite directions. Its terminator is
`\e\\` **or** BEL, and missing BEL leaves a shell script's link counted as text; and it must be
non-greedy, since two OSC sequences on one row otherwise collapse into one match that swallows
the visible text between them and measures the row far too narrow.

#### 3.2.1 Resolved questions

- **`case`** is `upper` or `lower`. An unrecognized value passes through unchanged today. That is
  the same silent-acceptance flaw as `"auto"` resolving to `fill` (§2.2) and is owned by the
  config-diagnostics work, not fixed here.
- **A `{other-id}` that does not resolve drops the link, not the item.** The text still renders,
  plainly. The missing-field rule governs an item's own `{}` value; a decoration's unmet
  dependency must not delete information.
- **`from` names a real registry or command id only.** Pointing it at another derived item does
  not resolve and suppresses the item. The alternative — a single order-dependent pass — makes
  config line order silently load-bearing, so reordering two lines loses a link with no error.
  Chaining can be added later behind a topological sort; order-dependence cannot be withdrawn
  once configs rely on it.
- **A truncated linked segment closes the link before the ellipsis and keeps its colour.** The
  ellipsis is the pane's artifact, not part of the target; clicking `…` must never navigate.

#### 3.2.2 The renderer's restyle path is the hazard

`PaneRenderer.TryGetSimpleWrap` matches only markup ending in exactly `[/]`. Wrapping coloured
markup in an OSC 8 open/close breaks that match, and the restyle then degrades to unstyled plain
on any wrap or truncate of an oversized linked-and-coloured segment — dropping the colour **and**
the link, with no error. A link-aware restyle that detects, strips, and reapplies the OSC 8
wrapper around the existing SGR logic makes rule 3's per-row re-open fall out of wrapping for
free; truncation still needs its own branch so the closer precedes the ellipsis.

The test that matters here asserts the link *survives* a wrap of a coloured segment. A row that
is merely the right width passes even when both the colour and the link have been silently
thrown away.

### 3.3 Compound items — several sources, several colours, one item

`format` decorates one value and `color` paints all of it, so a label cannot be dimmer than the
value it labels. Splitting the label into its own item does not work either: the separator is a
*between-items* construct, so `agent:` and `ORCHESTRATOR` come out as `agent: | ORCHESTRATOR`.
There is no way to say "these fragments are one thing" while colouring them differently.

A **compound item** declares `parts` instead of a value, and renders them concatenated with
**no separator between them**:

```json
{ "id": "agent-badge", "parts": [
    { "text": "agent:", "color": "grey" },
    { "from": "agent", "extract": "[^:]+$", "case": "upper", "color": "aqua" }
] }
```

A part carries the same vocabulary a pane item already carries — `extract`, `case`, `format`,
`color` — because a part *is* an item fragment, and inventing a second vocabulary for it would
put two spellings on one behaviour. What a part adds is exactly one source, and it must be
exactly one of:

- `text` — a literal
- `item` — a registry or `command` id, rendered as that item renders
- `from` — a derived value, per §3.2's derivation rules

**This is not a second rendering path.** A compound resolves to the same thing every item
resolves to: one `Segment`, one `Plain` string that is the concatenation of the parts, and
markup carrying a colour change per part. `Segment` already holds multiple styled spans — that
capability is what builtins use to colour `62%` inside `ctx:62% (125k/200k)`. Compounds expose
it to config; they do not add machinery. §4.1's `match` + `colors` compiles to the same span
list, and must keep doing so: `match` stays the better surface for carving *one* string, `parts`
is the general form for composing *several*, and both produce one list of `(text, colour)` spans
that the renderer cannot tell apart.

Rules, each closing a silent failure:

- **Width is unaffected.** `Plain` is the concatenation; markup never contributes width. Defect
  0's invariant is untouched, and this is the reason compounds are safe to add to a layout
  engine that sizes by measurement.
- **`truncate` cuts by `Plain` across the concatenation**, and the surviving spans keep their
  markup — including closing any span the cut lands inside. A truncation that severs a colour
  span mid-way and emits an unclosed SGR bleeds colour into the border. This is the one
  genuinely new implementation hazard here and needs its own test.
- **A literal is bound to its adjacent values and disappears with them.** A literal part is
  dropped when **any value part adjacent to it resolved to empty**, evaluated against the
  *original* array positions rather than against what earlier removals left behind. `agent:` does
  not survive an absent agent, and ` ✓` does not survive an absent PR. This is §4.1's "the literal
  text bound to it goes too", with array adjacency doing the job the format string's adjacency
  does there.

  > This rule previously read "the one after it if there is one, otherwise the one before", which
  > left a hole in the failure it claims to close. A value wrapped in a literal pair —
  > `[{"text":"("},{"from":"pr"},{"text":")"}]` — drops `)` and keeps `(`, so an absent PR renders
  > a bare `(` on the statusline. That is the **render-wrong** class, the same one defect 14 sits
  > in: not an absence the user can notice, but visible output that is simply incorrect, with no
  > diagnostic because nothing failed. Looking both ways is also the simpler implementation, since
  > it never needs to know whether a *following* literal exists in order to decide about a
  > preceding one.
  >
  > Evaluating against original positions is what keeps this order-independent. Deciding each
  > literal against the array as earlier drops have already mutated it would make the result depend
  > on traversal direction, which is the same objection this section raises against nesting.
- **If every value part is empty, the item renders nothing** and collapses per §2.4. A compound
  of only literals is a constant, which is legal and occasionally what someone wants.
- **Item-level `color` is the default** for parts that do not set one.
- **A part may not contain `parts`.** One level, for §3.2.1's reason: nesting makes resolution
  order observable, and an order-dependent config is one whose behaviour depends on how the
  parser happened to walk it.
- **A part may not carry `link`.** `link` stays at item level and wraps the whole compound.
  Per-part links mean nesting OSC 8 inside a styled span, which is §3.2.2's restyle hazard with
  more edges; it is deferred deliberately rather than overlooked.
- **Semantic colour precedence is unchanged.** A part naming a semantic item keeps its
  value-derived threshold colour unless that part sets `color` — §6's rule, applied one level in.
- **`--check` rejects** a part with zero or more than one source (`part-source-count`), a part
  containing `parts` or `link` (`part-forbidden-key`), a part naming an unknown id, and an unknown
  colour. A part that names nothing is not an empty part; it is a config the author got wrong.

  The last two reuse the existing `unknown-item-id` and `unknown-color` codes rather than taking
  part-specific ones — it is the same condition in a new position, and the JSON Pointer already
  says where. The codes are named here rather than left to the implementation because §9.6 fixes
  a code's meaning permanently once it ships, so an ad-hoc name chosen mid-build is a name the
  project keeps. All four are `error` severity: zero-or-many sources and a forbidden key have no
  defined meaning, which is §9.4's line.

**This is the sixth construct that names an item by id**, after pane `items`, colour-token
`from`, derived `from`, link `{other-id}`, and §4.2's argv placeholders. §5's resolution set must
enumerate it, and defect 11 is what happens when it does not.

## 4. Providers

Two kinds in v2. The provider is the *only* thing that differs between a built-in item and a
user-defined one — everything downstream is identical.

**`builtin`** — a function of the parsed stdin JSON plus render context (git branch, engram
telemetry). These are the 14 captured segments, each registered as one row in a single static
table keyed by id: `directory`, `git-branch`, `repo`, `worktree`, `pr`, `model`, `effort`,
`thinking`, `output-style`, `context`, `rate-limits`, `agent`, `engram`, `vim` — plus
`model-short`, a shortened form for narrow anchor panes, which appears only if it is explicitly
configured, since adding it to the default list would change the parity baseline.

The registry is the ONLY place the set of builtins is enumerated. No second list anywhere —
not in tests, not in config validation, not in docs generation.

**Every registry row carries a default `format`, and it is the only place an item's display
text is constructed.** A row resolves a *raw value* (`62`, `high`, `approved`) and a `format`
that turns it into display text (`ctx:62% (125k/200k)`, `effort:high`, `PR #42`). This is the
same `format` a `command` item declares below and the same one a `{ "item": "<id>" }` entry may
override (§8) — one mechanism, not a builtin-only shortcut.

Both are load-bearing, because the raw value and the display text have different consumers:
colour rules read the **raw value**, since `from: "context"` with numeric thresholds needs `62`
and not `ctx:62% (125k/200k)`, while `contains: "Sonnet"` matches the model string itself. The
surface renders the **display text**. Resolving one from the other at use time is what keeps
them from drifting.

**A row's two outputs, and how a composite item supplies them.** Most rows produce display text
by applying a format string to the raw value — `"effort:{}"`, `"PR {}"`, `"[{}]"`, or plain `{}`
where the value is already display-ready. Some items cannot: `context` displays
`ctx:62% (125k/200k)`, where the parenthetical is built from two further fields and appears only
when both are present, while its raw value must stay the bare number `62` so a numeric threshold
rule (§6.4) can compare against it. No format string can wrap data that was never passed through
it, and none can express a conditional.

So a row supplies **either** a format string applied to the value, **or** a text function with
access to the full resolved input — never both, and the registry exposes a single accessor that
hides which one a row used. This is not a second display mechanism competing with the first: the
behaviour is "how item X displays", exactly one implementation of it lives in item X's row, and
both the default list and an explicit `items` array call the same accessor. Format strings are
the common case, not the rule; a text function is the general case, not an escape hatch.

**The accessor returns markup, not plain text.** Splitting display into "text from the registry
row, colour from somewhere else" reintroduces the exact duplication this section exists to remove
— one path colours, the other does not, and the plain text matches so a text-only equivalence
test reports success. Some items colour *within* themselves: `ctx:62% (125k/200k)` colours only
`62%`, and `5h:30% / 7d:85%` colours each window independently against its own threshold. That
granularity is finer than any item-level colour expression can express, it is part of the
captured baseline, and it must survive. So a row's accessor yields both `Plain` and `Markup`
(§2.4 keeps `Plain` as the sole width metric), and an item rendered from an explicit `items`
array is coloured identically to the same item in the default list.

An item-level `color` from config (§6) applies on top, and where an item colours its own
fragments the config colour governs the parts the item did not claim. A config colour never
silently discards an item's internal threshold colouring.

**Thresholds are one evaluator with two callers.** §6.4's numeric comparison is a single
implementation, used both by a config-declared `thresholds` token and by an item's own internal
rule. That — not relocating the 50/80 rule into config — is what "expressed *through* this
mechanism" requires: moving the rule out of the item would flatten per-fragment colouring and
break parity, which is a worse outcome than the duplication it would remove.

For a `from: "rate-limits"` rule the raw value is the **maximum across the windows** (`85` for
`5h:30% / 7d:85%`), since a threshold on usage means the most-constraining limit. Its display
text keeps both windows. An item whose raw value cannot be a single number cannot be a numeric
threshold source, and `--check` reports that rather than the rule silently never firing.

A per-item `format` in config (§8) always overrides to apply to the **raw value**, replacing the
row's default text entirely — **including the row's own internal markup**. A format string
restructures the text, and per-fragment tags are bound to the structure it replaces, so they
cannot survive it. `{ "item": "context", "format": "ctx:{}%" }` yields plain text carrying only
the item-level `color`, if any. This is the predictable reading: an override replaces, it never
merges. For a composite item that means the user trades the richer default
away — `{ "item": "context", "format": "ctx:{}%" }` renders `ctx:62%` and loses the
`(125k/200k)`. That is the correct behaviour and it is predictable: `{}` is always the raw value,
in every position, for every item.

Note what this rules out. "Let the configured form be narrower than the default form" is not
available, because the default list renders through this same accessor — a narrower configured
`context` would render a narrower `context` in the default statusline too, and break golden
parity against the captured bash output. The parity baseline is not a preference here; it is what
makes the unification checkable.

The consequence that matters: **the default list is not a separate rendering path.** A leaf with
no `items` renders the 14 builtins through exactly the code an explicit `items` array uses, with
the same formats. If a configured `{ "item": "context" }` and the default list can produce
different text for the same item, there are two display implementations and the framework is
being bypassed by its own baseline — which also means the golden parity test is guarding a path
the framework does not use, and is no longer evidence about the framework at all.

**`command`** — an external process producing one line on stdout.

```json
{
  "id": "k8s",
  "command": ["kubectl", "config", "current-context"],
  "format": "k8s:{}",
  "color": "cyan",
  "ttlSeconds": 30,
  "timeoutMs": 150
}
```

- **Argv array, not a shell string.** `["python3", "/path/thing.py"]`. This is the default and
  the documented form: no quoting rules, no word splitting, no injection surface.
- **`shell: true` is in scope, not optional.** Pipes are a normal thing to want in a statusline
  script, and telling a user to wrap their one-liner in a file is the kind of friction that
  makes a framework unused. The rule: `command` is an **array** normally; when `shell: true` is
  set, `command` is a **string** run as `sh -c "<string>"`. Both forms accepted, decided by the
  `shell` flag rather than by sniffing the JSON type. Everything downstream
  — stdin, env, cwd, timeout, cache key, stale-on-failure — is identical for both forms.

  **The JSON type is not itself the fault, and `--check` must not report it as one.**
  `CommandJsonConverter` normalizes both forms to a single argv list, so `"command": "date"` and
  `"command": ["date"]` are the same value by the time anything runs, and both execute correctly.
  A diagnostic on the *shape* would fire on a config that works — which §9.4 forbids more
  strongly than it requires any diagnostic at all.

  The config that cannot work is narrower, and it survives normalization: **an argv of exactly
  one element whose text contains whitespace, with `shell` not `true`**. That is
  `"command": "git status"` — normalized to `["git status"]` and exec'd as a binary literally
  named `git status`, which does not exist. `["git status"]` written directly is the identical
  value and the identical failure, so the check is on the **value**, not on the source shape,
  and catching both is correct rather than a false positive. `--check` reports it as an
  **`error`**, code `command-shape`: exec fails, §7 suppresses the item, and §9.4.1's test puts
  a suppressed item in the deleted bucket.

  Checking the value is also what keeps this a check-only change. The converter discards the
  source shape deliberately — its own comment: *"so `PaneItemJsonConfig.Command` stays one plain
  type end to end"* — and neither reviving that shape through a provenance field nor walking the
  raw JSON a second time is warranted. Both recover a fact the check does not need.

  **The mirror-image fault is real and worse.** `CommandProvider.RunAsync` passes `command[0]`
  and nothing else to `sh -c`, so `shell: true` with `["echo", "hi"]` runs `sh -c "echo"` and
  **silently discards every element after the first**. The item renders — it just renders the
  output of a command the author did not write, with no error anywhere. `--check` reports an
  array of **more than one element** under `shell: true` as an **`error`**, code
  `command-shell-argv`.

  This one also changes the **render** path, which no other diagnostic in this section does.
  §7's contract is a pair: a bad config yields nothing at render time, and `--check` says why.
  Rendering the *wrong output* is outside that pair — it is the single outcome the design has no
  answer for, because the user gets no signal at all, not even an absence to notice. So
  `CommandProvider` suppresses the item rather than spawning a command it knows is missing
  arguments, which puts the fault back inside §7 where `--check` can explain it. `shell: true`
  with `["kubectl", "config", "current-context"]` is an easy config to write by cargo-culting the
  flag, and silently returning bare `kubectl`'s usage text is a bad way to find that out.

  Two conditions, two codes, because the fixes differ: `command-shape` is repaired by adding
  `shell: true` or by splitting the string into real argv, `command-shell-argv` by joining the
  array into one string or by dropping the `shell` flag. Codes are cheap and §9.6 makes their
  meanings permanent, so a consumer that wants to distinguish these should not have to parse
  a message to do it.

  The element count is load-bearing: a **single**-element array under `shell: true` is exactly
  what the string form normalizes to, so it is correct and must not be reported. Only `> 1` is
  detectable as a fault, and only `> 1` loses anything.
- **stdin**: the command receives the *same session JSON* Claude Code sent us, verbatim. This
  is what makes user scripts first-class — they see everything the builtins see.
- **env**: `COLUMNS` is exported and `CLAUDE_TUI_LINE_ITEM_ID` is set to the item id.
  `CLAUDE_TUI_LINE_PANE_WIDTH` carries the inner width of the pane the item lives in, so a
  script can size its own output — but note the circularity, because as first written this
  variable was not implementable. Values are resolved *before* sizing (§5), so at spawn time no
  pane has a width yet; and for a `content` pane the demand is backwards anyway, since the item
  determines the pane's width rather than the reverse. The resolution:
  - The variable carries the pane's inner width **from the previous render**, recorded in the
    cache entry at render time and read back on the next spawn. A statusline redraws every
    second and layouts are stable, so this is correct on all but the first tick after a resize.
  - It is **absent on the first render**, and absent for items in a **`content`-sized pane**. A
    script must treat it as optional and behave sensibly without it.
  - Omitting it for `content` panes is what makes this safe rather than merely stale: it
    removes any path where a script's output width feeds the width it is told about. A `fill`,
    percent, or fixed pane absorbs its remainder independently of its own content, so no
    self-feedback exists there.
  - It is advisory. Pane-level `overflow` (§2.6) is the authoritative mechanism for content
    that does not fit, and it works whether or not a script consulted this variable.
- **cwd**: the session's `.cwd`, so `git`-flavored commands behave as the user expects.
- **Output**: stdout with the trailing newline stripped. Each line becomes one row of the item's
  block (§3.1), capped at `maxLines` (default 4) so a runaway script cannot flood the surface;
  excess lines are dropped and `--check` reports the cap was hit. Empty output ⇒ item suppressed.
  Nonzero exit ⇒ treated as empty (see §7). ANSI in the output is passed through but its width
  is measured stripped, so a script may color itself.

  *This previously read "first line of stdout", which contradicted §3's block model — §3 defines
  a multi-row block as "a user-defined item whose command emitted multiple lines", and §3.1
  already specifies how such a block packs. Only one of the two could be true; the block model
  is the one the rest of the spec is built on, so the single-line reading is the one that goes.*

### 4.1 User-defined items are first-class, and that is the extension point

A `command` item is not a lesser thing that gets placed where a builtin would go. Its `id` is
registered in the same namespace, so it may be referenced anywhere a builtin id may be: as
`{ "item": "<id>" }` in a pane, as `from:` in a colour token (§6.3), and as `{other-id}` inside
a `link` template (§3.2). **This is the answer to "there is no builtin for what I want"** — the
user, or a model acting for them, defines one and it behaves identically from that point on.

If it did not work this way the builtin list would be a ceiling, and §1's rule would be doing the
opposite of its job: a registry only the project can add rows to is a hardcoded list with extra
steps.

#### Colouring parts of one item

`"color"` paints the whole item. Some items need more than one colour inside them — the case that
forces this is a diff stat, where additions and deletions being the same colour defeats the point
of showing them. §4 already establishes that builtins colour their own fragments
(`ctx:62% (125k/200k)` colours only the `62%`); what was missing was any way for a *user-defined*
item to do the same without hand-writing escape sequences into a shell script.

A command item may declare a `match` — a regex with **named groups** — splitting its output into
named parts that `format` positions and `colors` paints:

```json
{
  "id": "git-diff-stat",
  "command": ["git", "diff", "--shortstat"],
  "match": "(?:(?<added>\\d+) insertion)?.*?(?:(?<removed>\\d+) deletion)?",
  "format": "+{added} -{removed}",
  "colors": { "added": "green", "removed": "red" },
  "ttlSeconds": 5
}
```

Keeping the colour in config rather than in the script is what makes it checkable: the names are
validated, the colours resolve through §6 like any other colour, and the user can recolour
without editing a shell command. A script that prefers to emit its own ANSI still may — that path
is unchanged — but it is then opaque to `--check` and cannot be recoloured from config.

Rules, each closing a way this could otherwise fail silently:

- **`{}` still means the whole matched output.** `match` adds names; it does not withdraw the
  default. An item with a `match` and a `format` of `{}` renders exactly as it would without one.
- **A group that did not participate renders empty, and the literal text bound to it goes too.**
  `+{added}` contributes nothing when `added` is unset, rather than rendering a bare `+`. This is
  §7's rule — a missing field renders nothing, never `null` — applied one level down.
- **A regex that does not match suppresses the item**, exactly as empty output does. Not "fall
  back to the raw text": a failed `match` means the output was not the shape the config claimed,
  and rendering past that is how a statusline displays something wrong with full confidence.
- **`--check` validates the cross-references, not just the syntax** — that the regex compiles,
  and that every `{name}` in `format` and every key in `colors` names a group that exists in it.
  A `colors` key with no matching group is §3.2.1's dangling-`{other-id}` defect wearing a
  different hat, and gets the same treatment: reported, not ignored.
- **Per-part colour is static; thresholds stay value-derived.** `colors` assigns a fixed colour
  per part. A part whose colour must depend on its value uses a §6.3 token with `from:`, and §6's
  precedence is unchanged — a configured colour replaces a decorative one, never a value-derived
  threshold.

### 4.2 Handing a resolved value to a user's own command

§4.1 makes a user-defined item first-class, but a `command` item can only see what its own
process can discover. Everything the framework has *already resolved* — the model name, the
agent, the context percentage — is invisible to it, so a script that wants to react to one has
to re-derive it, usually by parsing the same stdin JSON the framework already parsed. That is
a second implementation of a behaviour that exists, which §1 forbids.

**A `command` item's argv may contain `{item-id}` placeholders, expanded before the process is
spawned.**

```json
{ "id": "agent-badge", "command": ["~/bin/agent-badge.sh", "{agent}", "{model}"],
  "ttlSeconds": 5 }
```

This is deliberately *not* a new syntax. It is the same `{}` / `{other-id}` vocabulary §3.2
already defines for link templates, resolved by the same resolver against the same registry —
one mechanism wearing a second hat, rather than a fourth way to name a value. It inherits
§3.2.1's constraint unchanged: a placeholder names a **registry id or a `command` id, never a
derived item**, and a `command` item's placeholders may not name another `command` item.
Command-to-command references would need a topological sort, which §3.2.1 deferred; allowing
them here would smuggle in the order-dependence that rule exists to prevent.

**Expansion is argv-only, and this is a security boundary rather than a convenience.**
`CommandProvider` passes each argv entry through `ProcessStartInfo.ArgumentList`, where it
reaches the child verbatim with no shell involved — a value containing `;`, `$(...)`, or a
newline is data. When `shell: true`, `command[0]` is instead handed to `sh -c`, and substituting
a resolved value into that string is command injection: a branch named `; rm -rf ~` would
execute. Values reaching a statusline are *attacker-influenceable* in the ordinary case — a
branch name comes from whoever opened the pull request.

So, for `shell: true`, the framework substitutes nothing and instead exports each referenced
value into the child environment as `CLAUDE_TUI_LINE_VAL_<ID>`, with the id upper-cased and
non-alphanumerics replaced by `_`. The script reads `"$CLAUDE_TUI_LINE_VAL_AGENT"`, quoted,
and the value stays data. This joins the `CLAUDE_TUI_LINE_ITEM_ID` and
`CLAUDE_TUI_LINE_PANE_WIDTH` variables `CommandProvider` already sets.

Only *referenced* values are exported, never all of them. Exporting everything would force
every item to resolve on every render, and `remote-url` is lazily probed (`ItemContext`)
precisely because it costs a subprocess — an eager export would reintroduce that cost
unconditionally and silently.

**These placeholders must join the up-front resolution set (§5), and this is the load-bearing
part.** §5 requires every value be resolved once before sizing begins, and enumerates the set as
"every item referenced by any pane's `items`, plus every item named by a colour token's `from`".
That enumeration is incomplete today: it omits link-template `{other-id}` references, which is
the mechanical cause of defect 11 — `{remote-url}` resolves only when `remote-url` happens to be
placed in a pane and therefore lands in the dictionary for a different reason. Adding argv
placeholders to that set without fixing the enumeration would reproduce the same bug in a second
place, with the same silence.

**`--check` errors on an argv placeholder naming an unknown id (`unknown-item-id`), a derived
item (`placeholder-derived-source`), or — from a `command` item — another `command` item
(`placeholder-command-source`).** All three are errors under §9.4's discriminator, and that
deserves saying plainly, because §3.2.1 rules that a dangling `{other-id}` in a **link** drops
the link and renders the text anyway. Same syntax, different severity, and the difference is not
inconsistency:

- A link is decoration over text that stands on its own, and §3.2.1 defines what an unmet
  dependency does. Satisfiable — a **warning**.
- An argv placeholder is *data handed to another process*, and nothing defines what an unresolved
  one expands to. The literal `{gitbranch}`? An empty string? A dropped argv entry? Each is a
  different command line, the script sees a different `$1`, and the spec picks none of them.
  Unsatisfiable — an **error**.

§9.5 lists both as id diagnostics because both come out of `ReferenceExtractors`; they share a
walk, not a severity.

**Two referenced ids that mangle to the same environment variable are also an error
(`placeholder-env-collision`).** The `CLAUDE_TUI_LINE_VAL_<ID>` rule upper-cases and replaces
non-alphanumerics, so `agent-short` and `agent.short` both become `AGENT_SHORT`. Whichever the
framework exports second wins and the script reads a value belonging to the other item, with
nothing anywhere reporting it — the same silence this section's substitution ban exists to avoid,
arriving by the route the ban opened.

**A placeholder naming a known id that is empty at render time substitutes the empty string, and
the argv entry survives.** `{"command": ["mytool", "--branch", "{git-branch}"]}` outside a repo
runs `mytool --branch ""`, not `mytool --branch`.

This case needs ruling for the reason this section already gave about the *unknown* case, four
paragraphs up: "The literal `{gitbranch}`? An empty string? A dropped argv entry? Each is a
different command line, the script sees a different `$1`." That argument is exactly as true of an
id that is known and simply empty — and there, unlike the unknown case, **`--check` cannot help**,
because emptiness is a runtime condition. The section made the argument and then applied it only
where a checker could act, which left the harder half unspecified.

Preserving arity is the choice that cannot go silently wrong. Dropping the entry shifts every
positional after it, so `mytool --branch {git-branch} --format json` outside a repo becomes
`mytool --branch --format json` and the tool binds `--format` as the *value* of `--branch`. That
is the render-wrong class again: a command that runs, exits 0, and reports something the user
never asked for. An empty argument can be wrong, but it cannot re-bind a flag to the wrong value.

The same rule under `shell: true`: the variable is exported **empty rather than unset**, so a
quoted `"$CLAUDE_TUI_LINE_VAL_GIT_BRANCH"` expands to one empty argument and arity is preserved
there too. Unset would leave the script's own `${VAR:-default}` free to fire, which silently
converts "this render had no branch" into "this render had whatever the script's default is" —
a different value, indistinguishable downstream.

**The item is not suppressed for an empty placeholder.** A command that handles empty input is a
reasonable thing to write, and suppressing would delete behaviour the author chose. This is the
opposite call from defect 14's `shell: true` argv fault, and deliberately: there the config has no
defined meaning, whereas here it has one and the value simply happens to be empty.

## 5. Execution model — the hard part

The process is spawned **every second**. Naive shelling out per item per tick would destroy
the 12.6ms budget that justified Native AOT in the first place.

**Values are resolved exactly once per render, before sizing begins.** Every item referenced by
any pane's `items`, plus every item named by a colour token's `from` (§6), a derived item's
`from` (§3.2), a link template's `{other-id}` (§3.2), a `command` item's argv placeholder
(§4.2), or a compound part's `item` / `from` (§3.3) — **each of them even when the referenced
item is never displayed** — is fetched in a
single up-front phase — builtins synchronously, `command` providers concurrently through the
cache with one shared timeout window — producing a plain synchronous dictionary. Sizing reads
that dictionary. Post-sizing colour resolution reads the same dictionary. No provider ever runs
twice in one render.

**The enumeration above is the whole feature, and getting it wrong fails silently.** An earlier
version of this paragraph listed only pane `items` and colour-token `from`, and that omission is
the mechanical cause of defect 11: a link's `{remote-url}` resolved only when `remote-url`
happened to be placed in a pane and therefore entered the dictionary for an unrelated reason,
so the feature appeared to work in tests and did nothing in the configuration users actually
want. Any future construct that names an item by id is incomplete until it is added to this
list. A reference that resolves in one config and silently drops in another is the failure mode
this list exists to prevent — so it is not "the set of displayed items", it is **the set of
referenced items**, and those are not the same set.

This is a **correctness requirement, not an optimisation**, and it is the reason the §2.3
fixpoint terminates. That fixpoint's convergence argument assumes a pane's intrinsic measurement
is a deterministic function of the width it was granted. Fetch values inside the loop and that
assumption dies: a clock, a counter, a flaky command returning a different string on pass 2
makes the measurement non-deterministic, breaks the monotone-decreasing invariant the 3-pass cap
is built on, and produces oscillation that no cap can fix — only mask. Resolving up front makes
the values *constant for the render*, which is exactly the hypothesis the fixpoint needs. The
latency win — never spawning a process three times for three passes — follows for free.

Items in panes later dropped by §2.3's over-constrained loop will have been fetched
unnecessarily. That waste is accepted deliberately: recovering it means moving fetching back
inside the sizing loop, which is the thing this rule exists to forbid.

**Cache with TTL.** Each `command` item's result is cached. Within `ttlSeconds` (default 30)
the cached value is used and **no process is spawned at all**. The steady-state cost of a
custom item is a map lookup, not a fork.

- Cache location: `$XDG_CACHE_HOME/claude-tui-line/items/`, else
  `~/.cache/claude-tui-line/items/`. Overridable by `CLAUDE_TUI_LINE_CACHE` for tests.
- Cache key: `id` + hash of the resolved argv + `cwd`. `cwd` is in the key because a command
  like `git status --short` means different things in different sessions, and the cache is
  shared by every session on the machine.
- **One file per cache key**, named for the key — *not* a single `items.json` holding every
  entry. This is load-bearing, not filesystem taste. With one shared file, two sessions
  refreshing two different items each read-modify-write the whole map, and last-write-wins
  silently discards the other's refresh: the losing item stays stale, re-spawns next tick, and
  a busy machine thrashes forever without ever erroring. Per-key files make last-write-wins
  correct *per item*, which is the granularity the value actually has. Reading five keys is
  five opens — microseconds against a 13ms budget.
- Entry: `{ value, capturedAt, exitCode, paneWidth }`. `paneWidth` is the inner width this
  item's pane resolved to on the render that wrote the entry, and is what feeds
  `CLAUDE_TUI_LINE_PANE_WIDTH` on the next spawn (§4). It is written on every render, including
  cache hits where no process was spawned, so it tracks a resize rather than going stale with
  the value.
- Writes are atomic: temp file in the same directory, then rename. Concurrent statusline
  processes will still race on the *same* key; there last-write-wins is genuinely correct and
  no locking is used. A torn or unparsable cache file is treated as empty, never an error.

**Timeouts and concurrency.** On a cache miss, all due commands are spawned **concurrently**
and awaited with an individual `timeoutMs` (default 150). Total added latency is therefore one
timeout window, not the sum. On timeout the process is killed with its whole tree, the same way
`GitBranch` already does it.

**Stale-on-failure.** If a command times out or fails, the last cached value is used **even if
expired**, so a flaky command degrades to a slightly old value instead of flickering out. If
there is no cached value at all, the item is suppressed. The next tick retries.

**Never block the render.** A pathological command cannot exceed its timeout, and the render
proceeds with whatever is available. Exit code is always 0 and stdout is always valid.

### 5.1 Built-in probes are cached in the same store, not a second one

`remote-url` shells out to git. `ItemContext` makes the probe lazy so it only costs a subprocess
when something actually references it — but *referencing* it is the normal case for anyone using
`{remote-url}` in a link, and then it costs a subprocess **every second, forever**, for a value
that changes approximately never.

**It uses `ItemCache`.** Not a parallel cache for built-ins: the store already does file-per-key,
atomic temp-and-rename, silent-on-failure, shared across every session on the machine. A second
cache with its own eviction and its own bugs is the §1 violation this project keeps paying for
elsewhere, and there is nothing about a built-in probe that a `command` item's cache does not
already handle.

Four rulings, of which the second is the one that bites:

- **Key on `cwd`, not the repository root.** Two directories in one repo get two entries holding
  the same URL, which looks wasteful and is correct: finding the repo root costs the subprocess
  this exists to avoid, so paying it to deduplicate the cache would spend the saving to achieve
  the saving.

- **A probe that finds nothing is a cached result, not a cache miss.** Outside a git repo, or in
  a repo with no remote, `Probe` returns null — and null is the *answer*, cached like any other.
  Treating absent-value as not-yet-probed re-spawns git on every render in exactly the case where
  the spawn can never return anything, which is the pathological case wearing the costume of the
  normal one. This mirrors §5's existing rule that a clean run with empty output is a legitimate
  value.

- **A fixed TTL of 300 seconds, with no config key.** `PaneItem.TtlSeconds` exists and is
  tempting, but `remote-url` is frequently referenced *only* from a link template, where there is
  no pane entry to carry it — and if it is referenced from a pane entry and a link at once, two
  TTLs contend for one cache entry with no principled winner. A fixed default has no such
  ambiguity. Five minutes bounds how long a click can go to a stale repository after `git remote
  set-url`, against 300× fewer subprocesses; widening this to a config key later is easy, and
  withdrawing one after configs depend on it is not.

- **No lock, and no stampede mitigation.** Several sessions can miss together and each spawn git.
  At a 300-second TTL that is N spawns per five minutes rather than N per second, and the fix for
  it — a lock file in the render path — adds a failure mode strictly worse than the cost it
  removes.

The laziness stays exactly where it is. The cache goes *inside* the `Lazy`, so an unreferenced
`remote-url` still touches neither git nor the filesystem: referenced → `Lazy` fires → cache read
→ hit returns, miss probes and writes. Reversing those two would make every render pay a file
read for a value nobody asked for.

## 6. Coloring

Anywhere this spec accepts a colour — an item's `color`, a border's `color` (§2.10) — the value
is a **colour expression**: a literal, a token reference, or an inline rule. One grammar, three
forms, valid in every position. A border and an item can therefore be driven by the *same*
expression, which is the point of the whole section.

### 6.1 Literals

A plain string names a colour:

- **The 16 standard names** — `blue`, `yellow`, `fuchsia`, `grey`, `maroon`, `olive`, … These
  resolve **through the user's terminal theme**. `blue` is whatever the user's colour scheme
  calls blue. This is the right default for a statusline that has to sit inside somebody else's
  terminal and not clash with it.
- **The 256 palette names and indices** — `steelblue1`, `gold1`, `hotpink`, or `color(213)`.
- **Hex** — `#ff5fd7`.

The last two are **absolute**: they ignore the terminal theme and render the same everywhere.
That difference is the reason to keep all three rather than replacing the 16 with the wider
set — an author picks theme-following or exact, per colour, and both are legitimate. Do not
"upgrade" the 16 names to their 256-palette equivalents.

### 6.2 Colour system, and why widening is opt-in

```json
"colorSystem": "standard" | "256" | "truecolor"     // top-level, default "standard"
```

**The default is `standard`, and that is load-bearing.** Raising the colour system changes the
SGR bytes Spectre emits for the builtin segments, which is exactly what the golden parity
baseline pins. Making the wider palette opt-in means the default render stays byte-identical to
the captured bash statusline **by construction**, and the parity gate keeps its meaning without
anyone having to regenerate it. §13 listed palette widening as out of scope precisely because
it "needs its own decision"; this is that decision, and it is the config knob rather than a
profile raise.

Under `standard`, a 256-name or hex colour is **down-converted to the nearest of the 16** — it
is not an error and does not suppress the item. `--check` reports it as a **`warning`** so an
author who wrote `#ff5fd7` and got approximately-magenta knows why. (An earlier draft called this
a "notice"; there are exactly two severities and that was a third one by accident. It is a warning
under §9.4.1's test — the item is delivered, reduced. It is reported at all, rather than dismissed
as untidy, because §6.1 promises hex and 256-palette colours are *absolute*, and under `standard`
that promise does not hold.)

A baseline regenerated because it was inconvenient has stopped meaning anything. If widening
ever *does* become the default, that is a separate decision requiring a visually confirmed
re-capture recorded explicitly as a loss of coverage — not a side effect of a colour change.

### 6.3 Named tokens

A top-level `colors` table defines reusable, value-driven colour tokens. Referenced with a
leading `@`:

```json
{
  "colors": {
    "model-accent": {
      "from": "model",
      "match": [
        { "contains": "Sonnet", "color": "blue"    },
        { "contains": "Opus",   "color": "yellow"  },
        { "contains": "Fable",  "color": "fuchsia" }
      ],
      "default": "grey"
    }
  },
  "surface": { "pane": { "children": [
    { "border": { "color": "@model-accent" },
      "items":  [ { "item": "model", "color": "@model-accent" } ] }
  ] } }
}
```

The token exists so that the border and the text cannot drift. Written as two inline rules,
they are two copies of one mapping and someone eventually updates one of them.

- **`from` is required in the `colors` table** and names the item id whose value drives the
  rule. A token has no owning item — a *border* has no value of its own — so there is nothing
  to default to. This is the `from` that §5 refers to: the named item is fetched even when it
  is never displayed in any pane.
- **Tokens are flat.** A token's `color` values must be literals (§6.1); a token may not
  reference another token. This makes reference cycles impossible by construction rather than
  by detection, and one level of indirection is all the drift problem needs.
- An unknown `@name` resolves to no colour, silently, per §7. `--check` reports it.

### 6.4 Rule forms

Two rule shapes. Both may be written inline as an item's `color`, or in the `colors` table as a
token. Inline, `from` defaults to **the item the colour is attached to**; in the table it is
required.

**`thresholds`** — numeric, first satisfied wins, evaluated in declaration order:

```json
"color": { "thresholds": [ {"min": 80, "color": "maroon"}, {"min": 50, "color": "olive"} ],
           "default": "green" }
```

Applied when the value parses as a number. The existing 50/80 rule for `context` and
`rate-limits` must be expressed *through* this mechanism, not kept as a parallel code path —
same rule as §1.

**`match`** — string, first match wins, evaluated in declaration order. Each entry carries
exactly one predicate:

- `"contains": "Sonnet"` — case-insensitive substring.
- `"equals": "vim"` — case-insensitive full match.

**Substring is the default idiom on purpose.** Model display strings carry version numbers that
change — `Sonnet 5`, `Sonnet 5.1` — while the family name does not. An `equals` rule against a
model name is a bug with a delayed fuse: it works until the day the version bumps and then
silently falls through to `default`, which looks like nothing happened rather than like a
failure.

No regex. A statusline re-renders every second and a pathological pattern would be a
per-second stall with no way for the user to see why.

**`default`** is optional in both forms. Absent, a value that matches nothing takes **no
colour** — it does not inherit the previous rule's colour and does not suppress the item. A
missing, failed, or empty source value takes the `default` branch.

### 6.5 When colours resolve

**Colour resolution happens in the same up-front phase as value resolution (§5), immediately
after it, and before sizing begins.** It produces a resolved colour map alongside the value
dictionary; sizing and the final render pass both read already-resolved colours.

Nothing a colour expression needs depends on layout — a token reads an item value, and item
values are all known once §5's fetch phase completes. So there is no reason to defer, and
deferring costs something real: a placeholder colour threaded through measurement is a
placeholder that can leak into output on any path that skips the later fixup. Resolve once,
early, and let every downstream consumer read a finished value.

This is safe for the §2.3 fixpoint for the reason §5 gives — colour affects `Segment.Markup`
and never `Segment.Plain`, which is the width metric — but the fixpoint's safety is not why
the ordering is specified. It is specified because one resolution point beats two.

### 6.6 Defect 15: border colour has two resolvers, and they do not accept the same language

§6.5 ends "one resolution point beats two." Border colour has two, one layer below where that
sentence was looking, and they disagree about what a colour *is*.

| | pane-tree path | single-pane path |
|---|---|---|
| call site | `PaneTreeRenderer.cs:76` | `Program.cs:145` |
| resolver | `ColorResolution.Resolve` | `ColorResolution.ResolveBorderColor` → `ResolveLiteral` |
| produces | a **spec string**, used as a markup tag | a Spectre **`Color`**, via `Style.TryParse(...).Foreground` |
| consumed by | `PaneBorderRenderer.Wrap` | `Panel.BorderStyle(new Style(...))` |
| fallback | `?? "grey"` | `?? Color.Grey` |

Both are live in production; which one runs is decided by the shape of the user's config, not by
anything the user can see. Item colour has no such split — `PaneAssembler.cs:66` calls `Resolve`
and the spec goes into markup, which is why this stayed invisible: the divergence exists only on
the border key.

**The consequence is a capability gap, not just a duplication.** A markup tag carries decorations;
a `Color` cannot. `ResolveLiteral` takes `.Foreground` off the parsed style and discards everything
else, so a decoration-only spec — `dim` and `bold`, both of which §6.1 documents and both of which
`Style.TryParse` accepts — survives one path and is dropped by the other. `border.color: "dim"` is
therefore expected to render dim in a split config and undim in a single-pane one, with no
diagnostic on either, because both paths *succeeded*.

> **Unverified, and it must be checked before the fix is written:** the claim that
> `Style.TryParse("dim")` returns a style whose `Foreground` is `Color.Default` is inferred from
> the signature, not observed. Assert it directly. The defect stands either way — two resolvers
> with two fallbacks for one key is the defect — but the *symptom* named above is a prediction
> about Spectre's behaviour, and §9.4's lesson applies to spec prose as much as to diagnostics.

**Fix: one resolver, and `ResolveLiteral`'s return type is the actual bug.** `ColorResolution.Resolve`
becomes the sole border-colour resolver, as it already is for items. `ResolveBorderColor` stays,
but only as a thin adapter over it, and it must return a **`Style`** rather than a `Color` —
`Style.TryParse(spec)` whole, not `.Foreground` — so decorations survive into
`Panel.BorderStyle`. The two fallbacks collapse into the one constant they only coincidentally
agree on today.

The general form is §9.8's rule, which was written about a checker transcribing the renderer's
arithmetic: **two expressions of one thing drift silently.** This is the same failure with both
copies inside the renderer, which is worse, because there is no checker/renderer boundary to
suggest looking. It was found by asking what `--colors` is allowed to print — a question about a
CLI flag that had nothing to do with borders.

Inherited from v1 and non-negotiable: **the statusline never errors, never pollutes stdout,
always exits 0.** A misconfigured item, a missing binary, a script that writes garbage, a
malformed pane tree — each degrades locally. One bad item suppresses itself; one bad pane
renders empty; neither takes out the line.

Config validation failures are silent by design. A `--check` CLI flag (§9) is how a user finds
out they typo'd something, because a statusline is the wrong surface for error text.

### 7.1 The third outcome: output that is wrong rather than absent

The pairing above — degrade silently at render, explain at check time — assumes two outcomes. A
config is either fine, or it produces *less* than intended and `--check` says why. **There is a
third, and it is the only one with no signal at all: output that is present, plausible, and
wrong.**

The user cannot notice it, because there is nothing missing to notice. `--check` cannot report it,
because nothing failed. And the render path cannot degrade around it, because from the inside it
looks like success. Four instances are known, three of them found by walking rules over cases
rather than by anything failing:

| where | the wrong output |
|---|---|
| defect 14 (§4.1) | `shell: true` with a multi-element argv runs `sh -c "kubectl"` and renders bare kubectl's usage text |
| defect 15 (§6.6) | `border.color: "dim"` renders dim through one border resolver and undim through the other |
| §3.3 | an absent value between two literals left a bare `(` on the line |
| §4.2 | a dropped argv entry re-binds the next flag, so `--branch` takes `--format` as its value |

**The rule these produced, for every future ruling on a runtime-missing value:** when a config has
a defined meaning and only the *value* is absent, prefer the option that cannot silently produce
different-but-plausible output, even when another option looks tidier. Concretely, that has meant
preserving arity over dropping an entry, exporting empty over leaving unset, and dropping a
bound literal over emitting it alone. When the config itself has no defined meaning, the call
inverts — suppress the item (defect 14), which puts the fault back where `--check` can explain it.

**Where these keep coming from is worth naming.** Both §3.3 and §4.2 argued carefully about the
case a checker can catch and went quiet on the case only the renderer sees; §6.6 had two code
paths nobody had reason to compare. So the question to ask of any new rule is not "is this
right?" but **"what does this do when the value is missing at render time, and who would ever
find out?"** If the answer to the second half is "nobody", the rule is not finished.

## 8. Config

Iteration 1 — one pane, which is also what an omitted `surface` block means:

```json
{
  "border": { "enabled": true, "color": "grey", "style": "rounded" },
  "layout": { "chromeReserve": 3 },
  "items":  [ { "item": "directory" }, { "item": "git-branch" }, { "item": "context" } ]
}
```

Later, with splits:

```json
{
  "surface": {
    "maxRows": 4,
    "pane": {
      "split": "vertical",
      "gutter": 1,
      "children": [
        { "size": "auto", "overflow": "wrap",
          "items": [ { "item": "directory", "overflow": "truncate" },
                     { "item": "git-branch" } ] },
        { "size": "32%", "border": { "enabled": true, "color": "blue" },
          "overflow": "truncate", "ellipsis": "…", "maxRows": 2,
          "items": [ { "item": "context" },
                     { "id": "k8s", "command": ["kubectl", "config", "current-context"],
                       "format": "k8s:{}", "color": "cyan", "ttlSeconds": 30 } ] }
      ]
    }
  }
}
```

- `surface` **absent** ⇒ a single root leaf pane holding top-level `items`, with the top-level
  `border`. This is what keeps every existing config rendering exactly as it does today.
- `surface` **present** ⇒ top-level `items` is ignored; the pane tree is authoritative.
- **`border.enabled` defaults to true on a leaf and false on a split container.** An explicit
  `enabled` always wins; the default applies only where the author wrote nothing. One predicate,
  leaf vs split — *not* a special case for the root. A split container is a layout device, and
  adding a split to a config must never silently add chrome: under a uniform default, nesting
  three deep would spend 12 columns on boxes before any content existed. The border stays on the
  panes that hold content, which is also where its color is worth setting.
- `items` **absent** in a leaf ⇒ the default list: all 14 builtins in CAPTURE.md order.
- `items` **present** ⇒ exactly those, in that order. An unknown builtin id is suppressed
  silently.
- A `{ "item": "<id>" }` entry may override `format`/`color`/`overflow` on a builtin.
- `overflow` and `ellipsis` are inherited by a pane's items unless an item overrides
  `overflow` itself. A long path set to `truncate` inside an otherwise wrapping pane is the
  motivating case: wrap everything, but do not let one directory name cost three rows.

Config is still read on every render, so an edit takes effect within a second (SPEC.md §6b).

## 9. CLI surface

The binary currently does exactly one thing. v2 needs three more, none of which may interfere
with the statusline path (no args ⇒ render, exactly as now):

- `--check` — validate config, print human-readable diagnostics for unknown ids, bad colors,
  malformed panes, sizes that cannot fit, and `overflow: "overflow"` on a pane inside a split
  (§2.6); exit nonzero on error.
- `--preview [--columns N]` — render to stdout at a fixed width, for iterating on a config
  without waiting for the real statusline. Prints each row's measured width alongside, so
  overflow and ragged compositing are visible rather than inferred.
- `--items` — emit the registry as JSON, in **two sections**: `items`, one row per builtin id, and
  `kinds`, the key vocabulary of each way an item can be written. See §9.6.2 for the shape and for
  why the keys are not per-row.
- `--colors` — print the **recommended** palette **rendered in its own colour**, so the choice is
  made by looking rather than by guessing what `olive` is. `--colors --json` emits the same list
  unstyled for a program, and must declare that it is a recommendation rather than the accepted
  set — see §9.6.3. The palette is theme-mapped (§6.2), so printing it through the user's own
  terminal is the only honest preview; a swatch in documentation shows the author's theme.

`--items` exists because §12's authoring tools need to know what the framework can do, and any
other way of knowing it is a copy. A skill or command with an item list embedded in its prose is
a second registry that drifts the moment a row is added — the §1 failure, relocated into
documentation where nothing type-checks it. The binary is the only thing that knows its own
capabilities, so the authoring surface asks it rather than remembering.

`--check` also takes `--json`, emitting the same diagnostics as structured records with a
`path` (JSON Pointer into the config), `severity`, `code`, and `message`. A human reads the
prose form; a program that has just written a config reads this one and knows *which key* it
got wrong without parsing English.

### 9.1 The render path is untouched, and that is a hard constraint

**No arguments ⇒ read the payload on stdin and render, byte for byte as today.** The statusline
runs once a second; every millisecond of argument parsing is paid 86,400 times a day. Parse
argv only far enough to notice it is empty, and take the existing path when it is.

No flag may change what the no-flag path emits. A flag that alters rendering is not a CLI
feature, it is a second renderer, and §12.6's "two adapters over one core" applies here first.

### 9.2 `--config <path>` — global, and the reason the authoring surface works

Overrides the §5 search order for every subcommand *and* for the render path. Absent, the search
order is unchanged.

This flag is load-bearing for §12, not a convenience. `/migrate` and `/edit` must validate and
preview a **candidate** config without installing it, because §12.3 requires showing the user a
result before anything is written. Without `--config` the only way to preview a config is to make
it live, which inverts the whole safety property those commands are built on.

A `--config` path that does not exist, or does not parse, is an error (§9.4) and never a silent
fallback to the searched config. Reporting on a different file than the one named is worse than
failing.

### 9.3 Where `--preview` gets its payload

`--preview` renders the same pipeline the statusline renders, so it needs the same stdin JSON.

- **stdin has data** → use it. This is what `/migrate` uses to compare against the original
  script on identical input.
- **stdin is empty or a TTY** → use a built-in synthetic payload, **and say so on stderr**. A
  preview built from invented values that does not admit it is invented is how a user concludes
  an item is broken when it was never given anything to show.

The synthetic payload is a fixed constant, not randomised, so two previews are comparable.

**It is also the only synthetic payload in the binary.** `--items` needs one too, for §9.6.2's
`example` field, and it uses this one rather than defining its own — wrapped in an `ItemContext`
with canned `GitBranch`/`Engram`/`RemoteUrl`, since those come from probing the machine rather
than from stdin. Two constants would let `--items` and `--preview` disagree about what an item
looks like, which is worse than either being wrong alone: `/migrate` consults both in the same
session and has no way to notice they were built from different inputs.

`--columns N` sets the width. Absent, use `COLUMNS`, then a default of 100. The usable width
is still `N - chromeReserve`; preview must not quietly render 3 columns wider than reality, or
it will disagree with the statusline exactly at the width where wrapping starts to matter.

### 9.4 Exit codes and severities

| exit | meaning |
|---|---|
| 0 | success; for `--check`, no `error`-severity diagnostics |
| 1 | `--check` found at least one `error` diagnostic |
| 2 | usage error — unknown flag, missing argument, mutually exclusive flags |
| 3 | the config could not be read or parsed at all, so nothing could be checked |

3 is separate from 1 deliberately. "Your config has four problems" and "I could not read your
config" call for different next actions, and a program that gets 1 will try to fix a JSON Pointer
that does not exist.

Two severities, and the split resolves defects 3–6 (silent acceptance):

- **`error`** — the config does not do what it says. An unknown item id; an unknown value for
  `size`, `style`, `align`, `valign`, `overflow`, or `case`; an unknown colour name; `overflow:
  "overflow"` on a pane inside a split (§2.6); a pane whose fixed sizes cannot fit its parent.
- **`warning`** — it is satisfiable, but probably not what was meant. A `content` or `fill` pane
  with no items (`pane-no-items`); a `link` naming an id that resolves to nothing
  (`unknown-link-target`); under `collapse: true`, two panes asking for different colours or
  styles on one shared edge (`collapsed-edge-conflict`, §2.10).

**Two warnings that used to be on that list have been removed, both for firing on configs that
are fine.** They are recorded rather than quietly dropped, because each was wrong in a way worth
not repeating.

- **`command` item with no `timeoutMs`.** The premise was that an unbounded subprocess inside a
  once-a-second loop presents as a frozen statusline. There is no unbounded subprocess:
  `CommandProvider` defaults to `DefaultTimeoutMs = 150` and kills the whole process tree. The
  warning would have sent every author to set a key that was already set for them. Choosing the
  value deliberately is still good practice — 150 ms is tight for a real script — but good
  practice is what documentation is for, not what a diagnostic is for.
- **A pane with no items**, unqualified. Narrowed rather than removed, because §2.11's own
  division already separates the two cases: an empty `content`/`fill` pane collapses, so writing
  it accomplished nothing and the author almost certainly meant something else. An empty
  `fixed`/`percent` pane keeps its extent — that is a **spacer**, a legitimate and deliberate
  construct, and warning on it would be a false positive on working intent.

The shared lesson: a diagnostic's premise is a claim about the implementation, and it goes stale
exactly like any other. Both of these were true when written and stopped being true when a
default landed and when §2.11 was ruled.

The dangling `link` moved from `error` to `warning` here, and that correction is the reason the
next paragraph exists.

That last one refines the rule rather than bending it. Two colours cannot both hold on one
physical line, so it looks unsatisfiable — but §2.10 *defines* the resolution (first requester in
tree declaration order), so the config has a well-defined meaning and renders deterministically.
**Unsatisfiable means no defined meaning, not "some of what you typed had no effect."** It is a
warning because a written `color` was silently discarded, and §2.10 is right that this is the kind
of thing a user otherwise spends an evening on; it is not an error because the spec sanctions the
outcome. Under `collapse: false` it cannot arise at all — neighbours never contend for one line.

#### 9.4.1 The test, in the form that actually decides cases

"No defined meaning" is not enough on its own, and a ruling on the five `ReferenceExtractors`
forms is what exposed it. §7 and §3.2.1 define a fallback for *everything* — a dangling `link`
drops the link, a dangling derived `from` suppresses the item, a colour rule falls through to its
`default`. If "the spec wrote down a fallback" made a thing satisfiable, nothing would ever be an
error and we would be back at "does the renderer cope" under a new name.

**First, two tiers, because the consequence test only governs the second one.**

- **Tier 1 — not in the language.** An unknown value for `size`, `style`, `align`, `valign`,
  `overflow`, or `case` (`unknown-enum-value`); an unknown colour name (`unknown-color`);
  `overflow: "overflow"` in a position §2.6 forbids (`overflow-forbidden-position`). These are
  **always errors**, and consequence never enters into it. The document is not a valid instance
  of the schema, and how gracefully the renderer absorbs a token that is not in the language is
  beside the point — the paragraph below on unknown enum values is the argument, and it stands
  unchanged.

  **One code across all six enum keys, not one per key.** The JSON Pointer in `path` already
  names the key exactly, so a per-key code would only repeat it, and the repair is identical in
  every case: replace the value with one from the recognized set. Compare §4.1, which *does*
  split `command-shape` from `command-shell-argv` — there the repairs genuinely differ. Same
  rule, opposite answers, which is how you can tell the rule is doing work rather than
  decorating a decision already made.

  **`unknown-color`, spelled the American way, on purpose.** Every code string matches the
  config key it is about, and the key is `"color"`. This document says colour throughout, so the
  mismatch looks like a typo and will eventually tempt someone into "fixing" it. §9.6 makes that
  a breaking change to a shipped compatibility surface. It is spelled to match the config, and
  it stays that way. The same goes for `unknown-color-source` and `unknown-color-token`.

  `overflow: "overflow"` gets its own code rather than folding into `unknown-enum-value` because
  the value is not unknown — it is a perfectly good member of the enum that this position
  forbids, and the repair is to pick a different mode rather than to correct a misspelling. This
  is the same distinction, and the same reasoning, that gives `from-derived-source` its own code
  below.
- **Tier 2 — in the language, referent missing.** A `from`, a `link` `{other-id}`, an `@name`, an
  `{ "item": ... }` selector. The grammar accepts any string in these positions, so validity is
  not the question; existence is. **The consequence test decides these, and only these.**

Getting this backwards is easy and would be quietly destructive: applied to tier 1, the
consequence test demotes every typo to a warning, because an unknown `size` falls back to `fill`
and an unknown colour falls back to no colour and in both cases the pane renders fine. That is
precisely the silent acceptance (defects 3–6) this whole section exists to end.

For tier 2, the question is not whether a fallback exists. It is **what the fallback does to the
thing the config asked for**:

- **Delivered in reduced form → `warning`.** The author asked for something and got it, minus a
  decoration. `{ "item": "git-branch", "link": "{nope}/tree/{}" }` renders `main`, unlinked. A
  reasonable author might not even mind. This is the "some of what you typed had no effect" case.
- **Deleted → `error`.** The author asked for something and got nothing. Nobody writes an item
  into a config intending it never to appear, so the fallback is not a lesser version of the
  request; it is the request's negation.

Applied to the reference forms, which is where the boundary is easiest to get wrong:

| form | fallback | severity |
|---|---|---|
| `{ "item": "<id>" }` selector naming nothing | the item never renders | **error**, `unknown-item-id` |
| derived `from` naming nothing | the item never renders | **error**, `unknown-item-id` |
| derived `from` naming another derived item | the item never renders (§3.2.1) | **error**, `from-derived-source` |
| `link` `{other-id}` naming nothing | text renders, link dropped | **warning**, `unknown-link-target` |
| colour rule `from` naming nothing (inline or `colors` table) | text renders, colour falls back | **warning**, `unknown-color-source` |
| `@name` naming no token (§6.3) | text renders, colour lost | **warning**, `unknown-color-token` |
| `command` argv `{id}` placeholder (§4.2) | undefined — the child gets a command line nobody wrote | **error**, §4.2's three codes |

**Three of these name an item id and none of them reuses `unknown-item-id`, deliberately.** §9.5
already ruled that sharing the reference walk is not sharing the verdict; the corollary is that
the *code* has to carry the verdict too, or a consumer branching on it cannot tell an error from
a warning without also reading `severity`. One code with two severities is a code whose meaning
is not fixed, which §9.6 forbids. `unknown-color-token` is doubly distinct: its `@name` resolves
against the `colors` table, a different namespace from item ids entirely, so conflating the two
lookups would report a good config as dangling.

The two `from` forms landing in *different* buckets is the point, and the tempting mistake is to
group by reference syntax — every one of them is a `from`-or-`{}` lookup through one null-safe
path, so they look alike from inside the resolver. Group by consequence instead. A colour is decoration
over text that stands without it; a derivation is the item's *only* source of value, so losing it
loses the item. Same code path, opposite outcomes.

`from-derived-source` gets its own code rather than reusing `unknown-item-id` because the id is
not unknown — it exists, and this construct forbids it. §3.2.1 defers chaining behind a
topological sort, so a user who writes it has asked for something deliberately unbuilt, and a
message saying "no item named 'agent-short'" would be a lie about a config whose real problem is
that the item is there.

**The line between them is satisfiable versus unsatisfiable — not "does the renderer cope."**
That distinction cannot be the test, because §7 makes the renderer cope with everything: it
degrades rather than failing, by design. If coping meant `warning`, nothing would ever be an
`error`, including the unknown enum values ruled on below. So the question is whether a width, a
terminal, or a state exists in which the config means what it says. If none does, the user wrote
something that cannot be true, and that is an `error` however gracefully it is absorbed.

`minSize` greater than `maxSize` **is an error**, and was listed as a warning here in an earlier
draft. There is no width at which both constraints hold; the renderer clamps to *something*, but
the intent is unachievable everywhere. §9.8 gives it the code `min-exceeds-max` and this line is
the correction, not a second opinion.

**Unknown enum values are errors even though the renderer accepts them.** Same principle, and it
is not a contradiction: `--check` is diagnostic and changes no runtime behaviour, so calling a
typo an error breaks nobody's running statusline while making it visible. The renderer keeps its
fallback. Silence was the defect; the fix is a diagnostic, not a runtime change.

`--check` reports what is **invalid or unresolvable**, never what is untidy. No diagnostics for
formatting, ordering, or unused-but-valid keys. A checker that warns about things that work gets
ignored, including on the day it is right.

### 9.5 `--check` reuses `ReferenceExtractors`

Every diagnostic about an id — an unknown item, a `from` naming nothing, a `link` placeholder
that resolves to nothing, an argv placeholder that does — is derived from the **same**
`ReferenceExtractors` table §5 uses to build the resolution set. `--check` does not get its own
config walk.

Sharing the walk is not sharing the verdict. Severity is assigned per construct by §9.4's rule,
so the same dangling id is a warning in a `link` and an error in a `command` item's argv — §4.2
works that difference through. The extractor answers *which ids this config names*; nothing more.

This is not tidiness. Defect 11 was a resolution set that had fallen behind the config surface,
and it was invisible because nothing cross-checked it. A second walk in the checker recreates
exactly that: a `--check` that passes while the resolver drops an id, disagreeing silently, in
the one tool whose entire job is to not be silent. Adding a reference form must remain a single
append that both the resolver and the checker inherit.

### 9.6 JSON shapes

`--json` applies to `--check`, `--items`, `--preview`, and `--colors`.

```json
{ "ok": false,
  "diagnostics": [
    { "path": "/surface/pane/children/1/items/0/item", "severity": "error",
      "code": "unknown-item-id", "message": "no item named 'gitbranch'" }
  ] }
```

```json
{ "columns": 112, "usableColumns": 109,
  "rows": [ { "text": "…", "width": 109 } ] }
```

`--preview --json` returns each row's text **and** its measured width, rather than printing
widths in a gutter as the human form does — a model parsing rows should not have to strip
decoration to get them, and the width is the number that makes overflow visible rather than
inferred.

**`code` values are a compatibility surface.** Once a code ships, its meaning is fixed; a new
condition gets a new code rather than a widened old one. `/edit` and the §12.6 tools branch on
these, and a code that quietly changes meaning makes every consumer wrong at once.

#### 9.6.1 The code registry

**This table is the registry. A code that is not in it does not exist**, and a new condition adds
a row here in the same change that specifies it. That rule is not ceremony: writing it out found
that the two *largest* error classes in §9.4 — every unknown enum value and every unknown colour
name, which between them are the whole reason §9.4 exists — had no code string anywhere, while
§3.3 already referred to "the existing `unknown-item-id` and unknown-colour codes" as though one
had been defined. Scattered across six sections, that is invisible. Gathered into one table it is
the first thing you notice.

Severity is fixed per code, and that is what lets a consumer branch on `code` alone. Where one
condition would otherwise carry two severities in two constructs, it is two codes (§9.4.1).

**Config diagnostics** — entries in the `diagnostics` array, each with a JSON Pointer `path`:

| code | condition | severity | §  |
|---|---|---|---|
| `unknown-enum-value` | `size`/`style`/`align`/`valign`/`overflow`/`case` value not in the language | error | 9.4.1 |
| `unknown-color` | colour name matching no palette entry | error | 9.4.1 |
| `overflow-forbidden-position` | `overflow: "overflow"` where §2.6 forbids it | error | 9.4.1 |
| `unknown-item-id` | `{ "item": … }` selector, derived `from`, or argv `{id}` naming nothing | error | 9.4.1 |
| `from-derived-source` | derived `from` naming another derived item — present, but forbidden here | error | 9.4.1 |
| `unknown-link-target` | `link` `{other-id}` naming nothing; link dropped, text survives | warning | 9.4.1 |
| `unknown-color-source` | colour rule `from` naming nothing, inline or in the `colors` table | warning | 9.4.1 |
| `unknown-color-token` | `@name` naming no entry **in the `colors` table** — a different namespace from item ids | warning | 6.3 |
| `command-shape` | one-element argv containing whitespace, without `shell: true` | error | 4.1 |
| `command-shell-argv` | `shell: true` with more than one argv element; arguments would be dropped | error | 4.1 |
| `placeholder-derived-source` | argv `{id}` placeholder naming a derived item | error | 4.2 |
| `placeholder-command-source` | argv `{id}` placeholder naming another `command` item | error | 4.2 |
| `placeholder-env-collision` | two ids mangling to one `CLAUDE_TUI_LINE_VAL_<ID>` under `shell: true` | error | 4.2 |
| `part-source-count` | a compound part with zero, or more than one, source | error | 3.3 |
| `part-forbidden-key` | a compound part carrying `parts` or `link` | error | 3.3 |
| `fixed-sizes-exceed-parent` | declared fixed sizes cannot fit the parent at any width | error | 9.8 |
| `min-exceeds-max` | `minSize` greater than `maxSize` on one pane — unachievable everywhere | error | 9.8 |
| `collapsed-edge-conflict` | adjacent panes disagree about a shared edge under `border.collapse` | warning | 2.10 |
| `collapse-not-surface-level` | `border.collapse` declared on a pane — the compositor resolves one grid for the whole surface, so a per-pane value has no defined meaning | error | 2.10.1 |
| `border-inside-on-leaf` | `"border": "inside"` on a leaf pane — a leaf has no interior, so this silences its border entirely | warning | 2.10.1 |
| `color-down-converted` | a hex or 256-palette literal under a `colorSystem` that cannot render it — it will be approximated to the nearest of the sixteen | warning | 6.2 |
| `leaf-only-key-on-split` | `overflow` or `ellipsis` declared on a split — only leaf panes consult them and they do **not** inherit, so the declaration does nothing. Exactly those two keys; `align`/`valign` are not in scope for this code | warning | 2.6 |
| `pane-no-items` | a `content` or `fill` pane declaring no items **and no explicit `minSize`** — it collapses, so the declaration did nothing. **Not** `fixed`/`percent`, nor a `content`/`fill` pane with a `minSize`: all three hold their extent and are legitimate spacers (§2.11.1) | warning | 9.4 |

**Tool-protocol codes** — a different channel, and consumers must not confuse the two. These
appear as a top-level `{ "ok": false, "code": … }` describing a failed *invocation*, never as an
entry in `diagnostics` describing a place in the user's config. They have no `path`, because
there is no config position to point at:

| code | condition | § |
|---|---|---|
| `stale-revision` | the config changed under a §12.6 tool between read and write | 12.6 |
| `cli-not-found` | the binary could not be located; the error names every path tried | 12.6 |

### 9.6.2 `--items`: two sections, and why the keys are not per-row

This section replaces the field list this bullet used to carry. That list named five things —
"every item's id, what it reports, its default format, whether its colour is decorative or
semantic (§6), and which config keys it accepts" — and checking it against `ItemRegistry.cs`
found that two of the five were wrong to ask for. Both are worth recording, because one of them
is the §1 failure appearing *inside* the paragraph that warns about it.

**The shape.**

```json
{
  "version": "…",
  "items": [
    { "id": "git-branch", "reports": "the current branch, or nothing outside a repo",
      "color": "decorative", "default": true, "example": "⎇ main" }
  ],
  "kinds": {
    "builtin":  { "required": ["item"],    "optional": ["format", "color", "overflow", "link"] },
    "derived":  { "required": ["id", "from"], "optional": ["extract", "case", "format", "color", "overflow", "link"] },
    "command":  { "required": ["id", "command"], "optional": ["shell", "ttlSeconds", "timeoutMs", "format", "color", "overflow", "link"] },
    "compound": { "required": ["id", "parts"], "optional": ["color", "overflow", "link"] }
  }
}
```

**Why `kinds` is a section and not a column.** The accepted keys do not vary by item id. Every
builtin takes `format`, `color`, `overflow`, and `link`, and nothing else; what varies is *how the
item is written* — §4.1's `command`, §4.3's `from`/`extract`/`case`, §3.3's `parts`. Putting a key
list on each row would store one per-kind fact sixteen times and grow a seventeenth copy with the
next item added. That is precisely the drift the very next paragraph of §9 warns about, and it
would have been committed by the sentence above it. The corroboration that two sections is right
was already in the document: §12.6's `list_items` row says the tool must return "what each emits,
what options it takes — **and the schema for defining a new one** (§4.1)". A per-row key column
cannot express "the schema for defining a new one", because a new one has no row yet.

**Why "default format" is gone.** There is no default format string to report. A row's rendering
is a builder function (`ItemDefinition.BuildDefaultSegment`), not a template — `git-branch` emits
its own glyph in C#, and no `"⎇ {}"` exists anywhere to print. Satisfying the field as written
would mean introducing a format-string layer under every builder so that a CLI flag has something
to name, which is the CLI dictating the render architecture to serve its own output. `example`
replaces it and answers the question an authoring tool actually has — *do I need to add a `format`
of my own, or does this item already carry its own decoration?* — which a template would answer
only indirectly. `--items` has no stdin payload, so the example is rendered against a canned
`ItemContext`, and that is the reason the field is honest: it is the same `BuildDefaultSegment`
the renderer calls, not a string re-typed into a table.

**That `ItemContext` is built on §9.3's synthetic payload and must not be a second constant.**
This paragraph originally said "one canned synthetic `ItemContext` fixture" and left it there,
which was this document's own §1 failure committed inside the section correcting one — §9.3
already defines a fixed, non-randomised synthetic `StatusInput` for `--preview`, and an `example`
built from a different one would show an item one way under `--items` and another under
`--preview`, with `/migrate` reading both as authority in the same session. So: **one
`StatusInput` constant**, and `--items` wraps it with canned values for the fields `ItemContext`
adds beyond the payload — `GitBranch`, `Engram`, `RemoteUrl`. Those three are the only new
constants, and they exist because those values come from probing the machine rather than from
stdin, which `--items` must not do.

**Two fields the old list omitted.** `reports` and `default` were both missing and are both
load-bearing. `reports` is the description, and it belongs on `ItemDefinition` as a **required**
positional field rather than a lookup table beside it — required is the whole point, because it
makes "add a row without describing it" fail to compile instead of silently shipping an item that
`--items` announces as a bare id. `default` distinguishes the fourteen items in the default
pipeline from `model-short` and `remote-url`, which are opt-in (`ItemRegistry.DefaultIds` already
knows). §12.2's migration depends on that bit: an item that will not render unless explicitly
placed is a different mapping decision from one that appears on its own, and a tool that cannot
tell them apart will map a branch readout onto `remote-url` and produce a config that renders
short with nothing wrong in it.

### 9.6.3 `--colors` prints a recommendation, because the accepted set is not ours and is not finite

This bullet used to say "print every accepted colour name". It cannot, and the reason is
structural rather than a matter of effort.

**There is no colour registry in this codebase, and there should not be one.**
`ColorResolution.ResolveLiteral` is two lines: it hands the string to
`Spectre.Console.Style.TryParse` and takes the resulting `Foreground`. The accepted set is
therefore Spectre's entire table — roughly 256 named colours — plus `#rrggbb` hex, which is
infinite. No file in `src/` enumerates the sixteen; they are named only in prose. `--colors`
consequently has nothing to enumerate, and the two ways of giving it something are both wrong:

- **Hardcode our own list of accepted names.** This is a second registry of a table we do not
  own, drifting against a third-party library on every upgrade — worse than the §1 case, because
  no amount of care on our side keeps it true.
- **Reflect over Spectre's `Color` statics.** Same table, so no drift — but it is reflection over
  a library type in a Native AOT binary, and printing 256 swatches answers a question nobody
  asked. A person scanning for a colour is not helped by 256 of them.

So `--colors` prints a **curated recommendation**: the sixteen theme colours plus `default`,
`dim`, and `bold`. That list is a genuine new thing and must live in exactly one place in code,
because it exists nowhere today.

**Its correctness condition is testable, and the test is the point.** A curated list can rot in a
way a derived one cannot — a Spectre upgrade could rename a colour and the list would go on
recommending it, with the failure landing on the user as a silently uncoloured item (§7). So every
entry is asserted to round-trip: each name through `ResolveLiteral`, non-null result. The list is
allowed to be hand-written precisely because that test refuses to let it be wrong.

**`--colors --json` must say which set it is.** This matters more than it sounds, and it is the
same failure as §9.6.2's missing `kinds` section wearing different clothes. §12's authoring tools
treat `--colors` as authority the way they treat `--items`, and §12.2 instructs the migrator to
preserve colours "by name, from `--colors`". A bare list of nineteen names reads as exhaustive, so
a model asked for `#ff8800` — which parses fine and always has — will consult the list, not find
it, and refuse or silently substitute. The output therefore carries the distinction explicitly:

```json
{
  "recommended": [ { "name": "olive", "themeMapped": true } ],
  "alsoAccepted": "Any Spectre.Console color name (256-palette, e.g. deepskyblue1) or #rrggbb hex.
                   These parse everywhere a name is accepted; how faithfully they render depends on
                   `colorSystem` (§6.2), which defaults to `standard` and approximates them to the
                   nearest of the sixteen."
}
```

`themeMapped` is the reason to recommend these nineteen at all, and it is the honest form of the
advice: the sixteen follow the user's terminal theme, so a statusline built from them stays
readable when the theme changes, and one built from hex does not. That is a recommendation with a
stated reason, which a model can weigh against a user who explicitly wants `#ff8800` — unlike a
bare list, which it can only obey or violate.

### 9.7 `--version`, and the two places a version number wants to live

`--version` prints the version and exits 0. `--items --json` carries the same string as a
top-level `version` field, which is what §12.6.8 surfaces as `cliVersion` — an MCP server that
spawns a possibly-stale binary needs to be able to say which binary it got, and a model
diagnosing "I set that and nothing happened" should be able to see a version without a second
call.

The hazard is that a version number now has **two** homes: the `.csproj`, which the assembly is
built from, and `.claude-plugin/plugin.json`, which is a hand-written manifest and cannot be
generated. That is a second registry in the §1 sense, and it will drift — silently, because
nothing reads both.

Rulings:

- The **`.csproj` is the source** for what the binary reports. `--version` returns the assembly's
  informational version. It must not be a string literal in `Program.cs`, and it must not be read
  from `plugin.json` at runtime: an AOT binary published into `${CLAUDE_PLUGIN_DATA}/bin` has no
  guarantee the manifest is adjacent, and a version that reads as empty off a missing file is
  worse than one that is merely stale.
- A **test asserts the two match**, comparing the assembly version against `plugin.json`'s
  `version`. This is the whole mitigation, and it is cheap: without it the drift is invisible
  until a user reports a version that does not correspond to anything.

Consistent with §12.6.8, this is reporting, not gating — nothing refuses to run on a mismatch.
The test fails the build; the binary does not fail the user.

### 9.8 `--check` is width-independent, and what "cannot fit" therefore means

§9.4 lists *a pane whose fixed sizes cannot fit its parent* as an `error`, which invites an
implementation that runs the real `SizeResolver` at the current `COLUMNS` and reports when the
drop-and-retry path would fire. **That is the wrong reading**, for two independent reasons, either
of which is sufficient.

**`--check` never consults a width.** Not "no `--columns` override" — it does not read `COLUMNS`,
and it does not resolve sizes. §12.6's `validate` tool calls it from a stdio MCP server, a process
with no terminal at all (§12.6.2, §12.6.3). A width-dependent `--check` would there validate
against a width that does not exist, and would return a verdict that changes depending on which
terminal happened to spawn the client. A validator whose answer depends on the caller's window is
not a validator.

**Degrading at a narrow width is designed behaviour, not a defect.** The whole §2 ladder — wrap,
then truncate, then drop — exists so that a config too big for the current terminal produces a
sensible line rather than a broken one. Reporting that as an `error` flags a config that is
working exactly as specified, and §9.4 already ruled that a validator which warns about things
that work correctly gets ignored on the occasions it is right.

So the diagnostic is **structural**, and the invariant is narrow but real: a contradiction no
terminal width can resolve.

- Children's **fixed** sizes, plus the boundary cost, exceeding the parent's own **bounded** size
  — where bounded means the parent is itself fixed, or carries a `maxSize`.
  Code: `fixed-sizes-exceed-parent`.

  **The check calls `SizeResolver`'s own boundary-cost function. It does not compute one, and it
  does not transcribe a formula out of §2.10.** That rule earns its keep twice over. Gutters and
  border reserve are not independent addends — under `collapse: true` the divider *occupies* the
  gutter — so a hand-written sum double-counts and reports a config that fits as one that cannot,
  and a false `error` is the worst outcome available here, because exit 1 sends the user to fix
  something that already works. And §2.10 itself carried a wrong `collapse: false` formula for
  some time, under-reserving by 2 columns at every `N`; a checker that had been built from the
  prose would have inherited the error and disagreed with the renderer in the one tool whose job
  is to agree with it.

  So the width the check compares against is the renderer's, computed by the renderer's code, with
  the parent's **declared** size or `maxSize` standing in for the terminal width. That substitution
  is what keeps this width-independent: same function, a number from the config rather than from
  `COLUMNS`.

  This is computable without a width because §2.10 makes edges **static config**, never derived
  from a provider value. That rule was written for the §2.3 fixpoint's convergence argument, and
  it is what makes this diagnostic possible at all.
- `minSize` greater than `maxSize` on the same pane. Code: `min-exceeds-max`. This is an
  **error**; §9.4 listed it as a warning in an earlier draft and has been corrected there rather
  than here.
- Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's `maxSize`.
  Same code as the first: it is the same contradiction with the floor rather than the exact size —
  and therefore the same arithmetic, taken from §2.10. Two bullets computing one boundary two ways
  is how the double-count gets back in through the door the bullet above just closed.

Where the parent is `fill` or `content`, there is no bound to contradict and `--check` says
nothing. That is not a gap — there is genuinely no width-independent claim to make, and inventing
one would produce exactly the noise the previous paragraph rules out.

**The width-dependent information is real and belongs to `--preview`.** "At 80 columns this config
drops your right pane" is worth knowing; it is simply not a config error. `--preview` has a width
by construction, so it reports what it dropped or truncated at each width it rendered — as a note
alongside the rows, never as a diagnostic and never affecting the exit code.

## 10. Testing requirements

The v1 lesson was expensive and is now policy:

1. **Parity gate for iteration 1** (§2.7): byte-identical to the pre-pane build across the
   full width sweep, border on and off. This is the whole justification for landing panes as a
   no-op first.
2. **Pane content is position-independent** (§2.5): the same leaf pane at the same inner width
   renders identically as the root of an 80-column surface and as the third child of a split in
   a 200-column one. A failure here means something below the compositor is reading `COLUMNS`.
3. **The rectangle invariant**: for any pane tree, at any width, every composed root row has
   the *same* ANSI-stripped width, and that width never exceeds `COLUMNS - chromeReserve`.
   This one assertion catches ragged padding, height mismatch, and overflow together. It must
   be shown to FAIL against a deliberately broken compositor (drop the padding step) before it
   is trusted.

   **There is exactly one escape-stripping implementation in the repo and every test drives
   it.** It handles CSI and OSC, each scanned by its own rule (§3.2). A second copy is not a
   duplication nuisance but a lying instrument: an SGR-only stripper scores an OSC-broken row
   as clean, and a pattern missing its `ESC` prefix leaves the escape byte counted as text.
   Both of those existed here and both passed a green suite.
4. **Every overflow mode obeys the rectangle invariant** (§2.6). Same pane, same too-long
   value, same width, three modes: `wrap` emits more rows and loses no characters; `truncate`
   emits the same row count and ends with the marker; `overflow` exceeds the width and is
   therefore rejected in a split. Also assert the two traps directly — a hard break never lands
   inside an escape sequence, and every continuation row of a styled segment carries the style.
   `ellipsis: ""` must clip without sacrificing a cell, and a marker wider than the pane must be
   dropped rather than fill it. A wrapped link-and-colour segment must keep **both** across every
   continuation row, and a truncated one must close its link before the ellipsis (§3.2.2) — a
   row of the correct width passes even when the restyle path has silently discarded both.
5. **Re-measurement under a narrower grant returns the longest wrapped row**, not the grant
   (§2.9). This needs a test that asserts the *sibling's* final inner width, because a
   re-measure that simply echoes its grant passes any test phrased as "the anchor did not
   exceed its cap".
6. **Intrinsic sizing is verified by derivation, not by a golden number** (§2.3): change the
   model string, change `COLUMNS` — the anchor's resolved width must equal its measured content
   width plus its own chrome every time, and the `fill` sibling must equal the exact remainder.
   Assert `maxSize` clamps rather than stretches.
   **The fixpoint needs three tests of its own**, because it is the subtlest thing in the
   layout: (a) *convergence* — an anchor clamped below its unwrapped width re-measures to its
   longest wrapped row and the freed columns actually land in the `fill` sibling, verified by
   asserting the sibling's final inner width, not merely that the anchor shrank; (b) *the monotone clamp* — a
   deliberately misbehaving stub renderer that requests MORE width when granted less must be
   clamped to its previous request and the loop must still terminate; (c) *the pass cap* — a
   stub that changes its request every pass must stop at 3 passes and render with the last
   resolved sizes rather than looping.
7. **No pipe-diff-only verification.** Byte-parity against bash remains a useful regression
   check on the builtin path, but it cannot validate anything about the render surface, because
   both sides can share a wrong constant. Every width claim needs an invariant, not a diff.
8. **Command providers are tested with real processes**, not mocks — a script that succeeds,
   one that exits nonzero, one that hangs past its timeout, one that emits 400 characters, one
   that emits nothing. The timeout path is the most likely to be wrong and the least likely to
   be exercised by accident.
9. **Cache behavior tested directly**: a hit within TTL spawns no process (assert on a marker
   the script writes), an expired entry re-spawns, a corrupt cache degrades to a miss, and a
   failed command falls back to an expired value.
10. **Perf regression**: median render latency stays under the v1 measurement of ~12.6ms with
   zero command items configured, and the added cost of N cached command items is a lookup.
   Measure with the existing bench harness, including its self-calibration.
11. **Revert always finds the original** (§12.2). The test is the sequence that breaks a naive
    implementation: migrate, edit, migrate again, revert — and assert the restored command is
    the user's, not claude-tui-line's. Assert too that a second `origin` is refused, that no
    command deletes or overwrites a backup file, and that a hand-edited `settings.json` is
    reported rather than clobbered. This is the one area where a bug destroys something the
    user cannot rebuild, so it is tested against the filesystem in a temp HOME, not mocked.

## 11. Phasing

1. **Phase 1** — `chromeReserve` width fix. *Done; awaiting live confirmation.*
2. **Phase 2** — pane surface, root leaf pane only, including the §2.6 overflow modes. The
   default stays `"overflow"` so the §2.7 parity claim holds; `wrap` and `truncate` ship
   opt-in and fully tested, so that splits land on machinery already proven rather than on
   code written the same week it first matters.
3. **Phase 3** — splits: sizing, gutters, per-pane borders, `valign`, multi-row blocks.
   **Acceptance is §2.9**, eyeballed live.
4. **Phase 4** — item registry + `command` providers, cache, TTL, timeouts.
5. **Phase 5** — the CLI surface: `--check` (with `--json`), `--preview`, `--items`.
6. **Phase 6** — the authoring surface (§12): the backup ledger first, then `migrate`, `revert`,
   and `edit`.

Phase 6 depends on Phase 5 and not the other way round: §12's commands are prompts driving the
binary, so every one of them is guesswork until `--items` and `--check --json` exist to be
driven. Within Phase 6 the ledger comes first for the same reason — `migrate` and `revert` are
both defined in terms of it, and a migrate that ships before the ledger is a tool that replaces
a user's statusline with no way back.

Splits now come **before** the item registry, reordered because the two-pane test needs no
providers at all — it needs only a compositor. Getting a visible result out of
the surface work early is worth more than getting the registry in early, and §2.9 exercises the
compositor far harder than any registry change would.

Each phase is wired into the live session and eyeballed before the next begins. That is the
only step that caught the last defect.

## 12. Authoring surface — plugin commands and LLM-driven editing

The framework is configured by a JSON file, and the person configuring it should not have to
learn that file. Three plugin commands cover the lifecycle: adopt an existing statusline, change
it in conversation, and go back.

### 12.1 The binary is the oracle; the commands are prompts

None of this is renderer work. The commands are prompt files that drive a model, and the model
already has file editing — what it lacks is *what the framework can do* and *whether what it
just wrote is right*. Both come from the binary (§9), never from prose:

- **What exists** — `--items`, not a list written into a skill. An item list in a command file
  is a second registry, and it goes stale on the next row added. This is the §1 rule applied to
  documentation, where nothing type-checks the copy.
- **Whether it is valid** — `--check --json`, which names the offending key by JSON Pointer. A
  model that has just written a config needs to know *which* key it got wrong.
- **What it looks like** — `--preview --columns N`, at the user's real width and at a narrow
  one, because most layout mistakes only appear when something has to wrap.

The loop is therefore fixed and the same for every authoring command: **query, edit, check,
preview, show the user.** A model writing config from memory and declaring success is the
failure mode this structure exists to prevent — §7 makes a bad config silent, so an unverified
edit produces a wrong statusline with no error anywhere.

### 12.2 The backup ledger

Shared by every command that writes. It lives at `~/.claude/claude-tui-line/backups/` — under
the user's Claude directory rather than plugin data, because a backup that a plugin reinstall
can delete is not a backup.

`ledger.json` is append-only. Each entry records the UTC timestamp, the previous
`statusLine.command` verbatim, a copy of any script that command referenced, the SHA-256 of
each captured artifact, and a `kind`:

- **`origin`** — the state before claude-tui-line ever touched this machine. **Written exactly
  once, ever.** If an `origin` entry exists, no command may write another.
- **`checkpoint`** — any state captured since.

That distinction is the whole point of the ledger rather than a timestamped file. Migrate, edit,
migrate again, and a naive "back up whatever is there now" captures *claude-tui-line's own*
command as the thing to restore; revert then cheerfully restores the tool the user is trying to
escape. `origin` is written once and revert targets it by default, so the escape hatch survives
any number of intervening changes.

**An `origin` may never record a `statusLine` already pointing at a claude-tui-line binary**, and
"no `origin` exists yet" is not sufficient grounds to write one. A user can reach that state
without this tool having run — hand-editing `settings.json`, then invoking `setup` afterwards —
and the once-ever rule makes the resulting false `origin` permanent. That is strictly worse than
the second-use failure above: there, the escape hatch degrades; here, it is poisoned at creation
and nothing downstream has cause to doubt it. In that case append a `checkpoint` and leave
`origin` unwritten. A missing `origin` is honest and already handled — §12.5 lists the
checkpoints and flags which point at a claude-tui-line binary.

Three rules, none optional:

1. **Nothing in the backup directory is ever overwritten or deleted by any command.** Reverting
   is itself a change and appends a `checkpoint`; it does not consume the `origin`.
2. **The user's original script is copied, never moved and never modified.** Restoring a command
   that points at a file the user has since deleted is a broken revert, which is why the copy is
   taken even though installing does not touch the script.
3. **Only the `statusLine` key of `settings.json` is read or written.** Writes are atomic — temp
   file in the same directory, then rename, per §5 — and preserve unrelated keys and formatting.
   A recorded SHA-256 that no longer matches means the user edited it by hand since; that is
   reported, and it is theirs to resolve, not the tool's to overwrite.

### 12.3 `/claude-tui-line:migrate`

Adopts an existing statusline. An existing statusline is an arbitrary program — the user's real
one is 280 lines of bash — so this is a model's job, not a parser's, and the command's value is
in constraining what the model is allowed to conclude.

Every element found in the source maps into exactly one of three tiers:

1. **A builtin item**, when `--items` offers an equivalent — a branch readout becomes
   `git-branch`.
2. **A `command` provider (§4) wrapping the original logic**, when it does not. This tier is why
   migration can be lossless: worst case every element shells out to a snippet of the user's own
   script, and they still gain panes, borders, sizing, and colour rules over logic that already
   worked.
3. **Unmappable**, which is *reported to the user*, never silently dropped.

Tier 3 existing is what makes tiers 1 and 2 trustworthy. A migration that cannot say what it
failed to carry across will quietly lose an element, and the user will not notice until the day
they needed it.

**Fidelity is checked, not asserted.** After generating the config, run the original script and
`--preview` against the same stdin payload and compare the escape-stripped text. This is not
byte-parity — the layout differs by design, that is the point — it is a *content* check: every
visible token the original produced must appear in the new render or be on the tier-3 list.
Anything else is a silent drop wearing a success message.

**Nothing is written until the user says yes.** The command shows the proposed config, the
side-by-side preview, and the tier-3 list, and only then writes — recording `origin` first if
this is the first time.

### 12.4 `/claude-tui-line:edit`

Conversational editing: "move context into the right pane", "make the border follow the model".
Mechanically it is §12.1's loop plus a `checkpoint` written before the first edit of a session,
so undoing one bad idea never requires going all the way back to `origin`.

Two constraints on the model, both learned from this project's own failures:

- **Re-read `--items` rather than trusting recall.** Item ids and accepted keys change between
  versions; a remembered id resolves to nothing and is silently suppressed (§7).
- **Never widen the request.** Reformatting the whole config while adding one item makes the
  diff unreviewable and buries an unintended change where nobody will look for it.

### 12.5 `/claude-tui-line:revert`

Restores from the ledger — `origin` by default, a named `checkpoint` on request. It restores
**both** the `statusLine.command` and, if the recorded script is missing from its original
location, the copied script, because restoring a command that points at nothing leaves the user
with no statusline at all and no obvious cause.

It verifies the SHA-256 of what it restores against the ledger and reports a mismatch rather
than proceeding. It appends a `checkpoint` for the state it replaced, so reverting a revert is
possible. And it prints the restored command, because a user reaching for revert is already
having a bad time and deserves to see exactly what they got back.

### 12.6 The MCP server — ambient access, added after the CLI

Slash commands require the user to know a command exists. The MCP server exists so that
"make the statusline border green" works in the middle of an unrelated conversation, which is
the access pattern people actually have. It ships **after** §12.3–12.5, and the ordering is
deliberate: the CLI is what it wraps, so building it first would mean designing a transport
around an interface that does not exist yet.

**It is stateless, and that is the whole design.** stdio MCP keeps one process alive — the
client spawns it and talks JSON-RPC over the pipe, so it must survive between calls — but it
holds nothing between them. Every call re-reads the config from disk and re-derives its answer
from the same code the CLI runs. Two consequences, both the point:

- **It cannot drift from the CLI**, because it has no independent knowledge to drift with. An
  MCP server that cached the item list would become a second registry, which is §1's failure
  wearing a different hat.
- **It cannot serve a stale config.** A user who hand-edits the file between two calls gets the
  file they wrote, not a remembered parse of the file they replaced.

Nothing about it touches the renderer's shape. The statusline stays a one-shot AOT binary
spawned once per second; the MCP server is a separate process with separate lifetime needs, and
conflating the two is a mistake worth naming because it argues against a design for no reason.

Whether it re-spawns the CLI per call or calls the same internal functions directly is an
implementation detail and either is acceptable. **What is not acceptable is a third
implementation of any behaviour.** CLI and MCP are two adapters over one core.

**Mutating tools carry the §12.2 obligations unchanged.** Ambient access means a config can now
change without the user having watched a diff go by, so an MCP edit writes a `checkpoint` before
it mutates and returns the resulting diff for the model to show. The safety property that makes
`/edit` acceptable is the ledger, not the fact that a human typed a slash.

#### The tool surface

The goal is that a model can carry a request from "make the model name pink when I'm on Fable"
all the way to a rendered statusline without the user touching anything. That needs the full
loop — discover, change, validate, show — so the surface is not read-only.

| Tool | Wraps | Purpose |
|---|---|---|
| `list_items` | `--items --json` | What items exist, what each emits, what options it takes — **and the schema for defining a new one** (§4.1), so a model that finds no builtin fit knows it can author a `command` item rather than giving up. |
| `list_colors` | `--colors --json` | The palette (§6), so a colour request resolves to a real name. |
| `get_config` | reads the config path | The current config plus which path it came from (§5 search order) — the model must not guess which file is live. |
| `set_config` | writes + `--check` | Write a full config. Validates **before** committing; a config that fails `--check` is rejected and the old one stays. Checkpoints first. |
| `validate` | `--check --json` | Check a candidate config without writing it. |
| `preview` | `--preview` | Render to a given width and return the rows, so the model can *see* the result rather than assert it. |
| `revert` | the §12.2 ledger | Roll back to the origin, or to a named checkpoint. |

Two rules make this safe enough to hand to a model:

**`set_config` never commits an invalid config.** It validates, and on failure returns the
diagnostics instead of writing. A statusline is a thing the user stares at all day; breaking it
silently through an ambient tool call is the worst outcome available, and it costs one
validation to make impossible.

**`preview` is how the model checks its work, not `set_config`'s return value.** A write that
succeeded is not evidence the result looks right. The loop is the same one §12.1 fixes for the
slash commands — *query → edit → check → preview* — and the MCP tools exist so the model can run
that loop itself. This is why `preview` returns rendered rows rather than a success flag.

#### The worked example this surface has to satisfy

> *"Add git diff stat to my status line at the end of the first pane, make adds green and
> deletes red."*

Nothing here is a new tool; it is the existing loop applied to a request that touches all three
hard parts — an item that may not exist, a position in a tree, and colour *inside* one item.

1. `list_items` — no builtin diff stat. The response carries the §4.1 command-item schema, so the
   absence is a fork in the road rather than a dead end.
2. `get_config` — returns the whole config, which is why it returns the whole config: the model
   has to navigate to "the first pane" itself, and `surface.pane.children[0].items` is only
   locatable if the tree is in front of it.
3. `set_config` — writes a `git-diff-stat` command item with `match`, `format`, and per-part
   `colors`, and appends `{ "item": "git-diff-stat" }` to that pane's `items`. Validation runs
   before the commit; a bad regex or an unknown colour name comes back as diagnostics and the
   user's working statusline is untouched.
4. `preview` — renders it, so the model can see `+42 -17` in the right pane in the right colours
   instead of reporting success on the strength of a 200 response.

The user typed one sentence and never saw a config file. That is the bar for this surface: if a
request of this shape needs the user to open an editor, the tools have not done their job.

#### 12.6.1 The wire contract

Every tool returns a JSON object. Failures come back as a **result the model can read**, not as a
JSON-RPC protocol error:

```json
{ "ok": false, "code": "stale-revision", "message": "...", "diagnostics": [] }
```

Protocol errors stay reserved for genuine protocol faults — malformed request, unknown tool. The
reason is that the model's recovery path runs through reading the failure, and many clients
surface a protocol error to the user as an opaque box while handing the model nothing to act on.
That converts a fixable typo into a dead end.

`diagnostics` is `--check`'s array passed through **unchanged** — same `pointer`, same `code`,
same `severity` (§9.6). A wrapper that flattens a diagnostic into a sentence destroys the only
part that is actionable: §9.6 made `code` a compatibility surface, and the JSON Pointer is what
tells a model *which key* it got wrong rather than that something is wrong.

| Tool | Arguments | Returns |
|---|---|---|
| `list_items` | — | `items[]`, the §4.1 command-item schema, `cliVersion` |
| `list_colors` | — | `colors[]` (§6) |
| `get_config` | `configPath?` | `config`, `configPath`, `source`, `revision` |
| `set_config` | `config`, `configPath?`, `baseRevision?` | `ok`, `diagnostics[]`, `revision`, `checkpoint` |
| `validate` | `config` \| `configPath` | `ok`, `diagnostics[]` |
| `preview` | `columns?`, `config?`, `configPath?` | `renders[]` — each `{ columns, rows[] }` |
| `revert` | `confirm?`, `target?` | unconfirmed: `entries[]`. confirmed: `restored` |

#### 12.6.2 The server's environment is not the user's shell

This is the hazard most likely to produce a baffling bug report. MCP clients commonly spawn stdio
servers with a minimal environment inherited at client start. `$CLAUDE_TUI_LINE_CONFIG` set in the
user's shell may simply not be visible to the server — so §5's search order, run identically in
both places, resolves to a *different file*. Nothing errors. The model edits a config, the user
sees no change, and every layer honestly reports success.

- Resolution runs **per call** and is never cached; §12.6's statelessness already requires this,
  and this is the case that shows why it matters.
- `get_config` returns `configPath` and `source` — `"env"`, `"default"`, or `"none"` — and the
  model is expected to state the path when it reports what it changed.
- Every tool that reads or writes config takes an optional explicit `configPath` that overrides
  resolution outright. This is §9.2's `--config` at the tool layer and exists for the same reason:
  without it, the only way to act on a specific file is to hope the search order agrees with you.

#### 12.6.3 `preview` takes its width; it never infers one

Same root cause as §12.6.2. `COLUMNS` in the server's environment describes nothing — the server
has no terminal. A preview rendered at an inferred width is a faithful preview of a layout the
user will never see, which is worse than no preview, because it will be believed.

`preview` therefore takes `columns` explicitly. Given none, it renders at **80 and 60** and
returns both, labelled — the widths where layout decisions actually become visible, and the same
pair §12.4 step 7 requires of the slash command. It applies `chromeReserve` exactly as the
renderer does (§9.3): a preview three columns wider than reality is a preview of a different
layout.

#### 12.6.4 `revert` without confirmation is the listing

`revert` is the one ambient tool that can undo work the user did deliberately, and ambient means
nobody watched a diff go by first.

Called without `confirm: true`, it **writes nothing and returns the ledger** — every entry, its
kind (`origin` / `checkpoint`), its timestamp, and what restoring it would do. Called with
`confirm: true` and an explicit `target`, it restores.

There is deliberately no separate `list_backups` tool. The cheapest call is the one that shows the
options first, so a model cannot skip look-before-you-leap by reaching for a shorter one.

#### 12.6.5 Concurrent writes: compare-and-swap, not last-writer-wins

Ambient access means an MCP call, a slash command, and a hand edit in an editor can now interleave.
Last-writer-wins silently discards whichever change was not last — and that is usually the user's,
because the model writes faster.

`get_config` returns a `revision`, a hash of the file's bytes as read. `set_config` takes an
optional `baseRevision`; if supplied and no longer matching, the write is **refused** with
`code: "stale-revision"` and the model re-reads instead of clobbering.

It is optional rather than required so a first write to a machine with no config file works
without ceremony. A model that read the config is expected to hand back what it was given; one
that did not is writing blind, and the §12.2 checkpoint is what keeps that recoverable.

#### 12.6.6 When the CLI is not there

Every tool fails with `code: "cli-not-found"`, naming the paths searched. It **never** falls back
to a remembered item list — §12.1's rule does not relax by moving to a different transport, and
the failure mode it guards against (a remembered id that resolves to nothing, renders as nothing,
and reports no error) is identical here. A model receiving `cli-not-found` should point the user
at `/claude-tui-line:setup` rather than improvise.

Whether the server spawns the CLI or links the core is left open by §12.6, and this rule survives
either choice: if it spawns, it reports the paths it tried; if it links, `cli-not-found` cannot
arise and the tool simply works.

#### 12.6.7 The complete list of files an MCP tool may write

Three, and no others:

1. the config file, at the resolved or explicitly-given path — whole-file, atomically
2. the ledger and its artifacts under `~/.claude/claude-tui-line/backups/` — append-only, per §12.2
3. `~/.claude/settings.json` — **only** the `statusLine` key, **only** from `revert`, atomically,
   preserving every other key and the file's formatting

No temp files outside the target's own directory, since an atomic rename requires the same
filesystem. No logs, no caches, no state directory. The server is stateless, and a state file is
the first step toward exactly the drift §12.6 exists to prevent.

#### 12.6.8 Version reporting, not version gating

`list_items` reports `cliVersion` alongside the items. It does **not** refuse to operate on a
mismatch.

The skew is real — a plugin update can leave a rebuilt server pointed at a stale binary — but it
is narrow, because `/claude-tui-line:setup` builds both from one tree. A gate that refuses to run
is a worse outcome than the skew it prevents for a user whose statusline is working fine. Report
it, and let the model raise it if something looks wrong.

## 13. Out of scope for v2

- ~~True-color / 256-color palettes.~~ **Resolved — see §6.2.** The decision this bullet was
  waiting on is the opt-in `colorSystem` knob, defaulting to `standard`, which keeps the parity
  baseline valid by construction instead of trading it away.
- Long-running provider daemons, watch-mode providers, push updates.
- Interactive elements. A statusline is a render target, not a TUI app — there is no input, no
  focus, no resize event.
- Per-item wcwidth. `Plain.Length` remains the width metric (SPEC.md §6), deliberately.
