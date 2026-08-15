# §3.1 — the block model: what a block is, and where packing stops

Task #75. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main`.

This is the first task in the ruled sequence **§3.1 blocks → #27 marker splice → #31 `maxLines`**. §6
below is written specifically to discharge that ordering: it names the seam #27 will attach to, so
#27 becomes a mechanical change rather than a rediscovery.

**Amended once (A1):** D4 was written as a product call I had made but not routed, and §9 flagged it
as the one decision worth showing Jim before it shipped. **Jim has since confirmed D4 as written.**
It is now a settled ruling, not an open question — see D4 and §9. Nothing else changed.

---

## 1. Scope correction, before anything else

**`valign` is already implemented and is NOT part of this task.** §3.1's prose introduces it
alongside the block model, which makes it look like one unit of work. It is not — it shipped
already:

- `Pane.cs:33` `enum PaneValign`, `:40` `PaneValignParsing`, `:44-46` the three tokens, `:65`
  `Parse` defaulting to `Top`
- `Config.cs:180-181` the `valign` JSON property, `:665` the parse call
- `ConfigCheck.cs:405-407` unrecognized-value diagnostic, `AcceptedCommand.cs:40` the accepted tokens
- `PaneTreeRenderer.cs:197-203` `PadHeight`, `Compositor.cs:79-82` and `BorderGrid.cs:158-161` the
  sibling-padding splits
- `Compositor.cs:17-19` says so explicitly: *"`Valign` (§3.1) … defaults to `PaneValign.Top`, which
  reproduces the pre-Phase-3"* behaviour

**Do not re-specify, re-implement, or "finish" `valign`.** If something about it is wrong that is a
separate bug report. This task is the block model only.

That leaves §3.1's actual unbuilt content as: **what makes an item a block, and what packing does
when it meets one.**

---

## 2. What §3.1 actually rules, and what it leaves open

§3.1 is `SPEC-V2-FRAMEWORK.md:2495-2518` — 24 lines. It settles three things and only three:

1. **A multi-row block occupies its own rows.** It never shares a row with a neighbouring item and
   never has items packed beside it (`:2497-2500`).
2. **Packing runs before wrapping** (`:2507`), and the order is not interchangeable.
3. **Block count is a property of what the provider returned** — one line or several — **never of
   the width it was later granted** (`:2514-2515`), so wrapping cannot promote an item to a block.

Everything below is a decision §3.1 does not make. Each is ruled here because leaving any of them to
the implementor produces a defensible-but-different statusline.

### 2.1 The ordering rule is about classification, not about two physical passes

This is the most important thing in this document and the easiest to get wrong.

`RowLayout.Wrap(IReadOnlyList<Segment> segments, int? availableWidth, bool allowFallback)`
(`RowLayout.cs:33-83`) today **fuses** packing and wrapping: it greedily fills rows from a flat
segment list. Reading `:2507` as "these must become two separate passes" invites a rewrite of
`Wrap`, which is unnecessary and would violate the one-implementation rule.

For a group of single-row items, greedy fill *is* pack-then-wrap — the two are indistinguishable
because no item can occupy more than one row before wrapping. The ordering rule bites in exactly
one place: **deciding which items are blocks, which must happen before any width is consulted.**

**Ruled: `RowLayout.Wrap` is unchanged and remains the single implementation of packing-and-wrapping.
The block model is a layer above it that decides what to hand it.** Classification happens first,
from provider output; `Wrap` is then called once per packed group and once per block line. That
satisfies `:2507` and `:2514` completely.

---

## 3. The decisions

### D1 — A block is an item whose rendered value contains a line break, after the §4.0.1 cap

Block-ness is `lineCount > 1`, where lines come from splitting the provider's returned value, and
where the §4.0.1 `maxLines` cap has **already** been applied. §4.0.1 (`:2958+`) fixes this ordering:
the cap applies *at the provider, before §3.1 packs the block*. So this layer never sees more lines
than the cap allows and never applies the cap itself.

**Today no item can ever be a block**, because `CommandProvider.cs:160` does
`stdout.Split('\n', 2)[0].TrimEnd('\r')` — a hardcoded one-line cap at exactly the site §4.0.1
designates for the configurable one. See §7.

### D2 — Strip exactly one trailing newline before splitting. This is not optional.

`echo foo` emits `"foo\n"`. A naive `Split('\n')` yields `["foo", ""]` — **two** lines, so the item
becomes a block with a blank second row, and it is pushed onto its own rows away from its
neighbours. Every well-behaved shell command would become a block the moment #31 lifts the cap.

**Ruled:** strip **one** trailing `\n` (and a `\r` before it) if present, then split on `\n`, then
`TrimEnd('\r')` each line — preserving the per-line `\r` handling `CommandProvider.cs:160` already
does. `"foo\n"` is **one** line and is **not** a block. `"foo\n\n"` is two lines (one trailing
newline stripped, a genuine blank line remains) and **is** a block.

This is the single highest-risk detail in the task: it is invisible until #31 lands, and when it
surfaces it will look like a packing bug rather than a parsing bug.

### D3 — Interior blank lines are preserved

A script that emits a blank line is expressing layout. Collapsing blank lines is a silent mutation
of provider output, and this codebase has no channel to report it. A blank line is a row, padded to
the pane width per §2.4 rule 1 like any other.

### D4 — No separator is emitted on either side of a block

**Confirmed by Jim (A1). Settled — implement as written.**

§3.3 (`:2641`) establishes the separator as a *between-items* construct. §3.1 says a block never
shares a row with a neighbouring item. Together these leave a separator between a single-row item
and an adjacent block with nowhere to live.

**Ruled: a block suppresses the separator on both of its sides.** Placing it at the end of the
preceding row produces a dangling `foo | ` with nothing after it — the classic trailing-separator
artifact, and a reader cannot tell it from a failed item. A separator's purpose is to disambiguate
two items *sharing a row*; a block is already unambiguously separated by occupying its own rows, so
the separator has no work to do.

Consequence: a pane whose items are `A`, `BLOCK`, `C` renders `A` on its own packed row with no
trailing separator, the block's rows, then `C` on its own packed row with no leading separator.

### D5 — Block count is not row count, and the spec's own sentence invites conflating them

`:2514` says block count is a property of what the provider returned. That is about
**classification**. It does **not** mean a block occupies exactly that many rows.

**A block's lines still wrap.** A 3-line block whose second line exceeds the inner width renders 4+
rows. Ruled: each line of a block is wrapped independently via `RowLayout.Wrap`, and the block's
rows are those wrapped results concatenated in order.

The invariant `:2515` actually states is narrower and must be preserved exactly: **wrapping can
never change whether something is a block.** A one-line item that wraps to three rows is still a
single-row item and still packs with its neighbours. This is the pack-then-wrap consequence
`:2510-2512` spells out, and it is the whole reason the order is not interchangeable.

### D6 — No empty packed group

Two adjacent blocks, or a block as the first or last item, must not produce an empty packed group
and therefore must not emit a blank row. Only groups containing at least one surviving item produce
rows.

### D7 — A suppressed item is not a block and contributes nothing

The §3 missing-field rule suppresses an item whose value resolved to empty. Suppression happens
**before** classification: a suppressed item is not a zero-line block, it is absent, and it does not
split a packed group in two. Two single-row items either side of a suppressed item pack onto the
same row with one separator between them.

### D8 — `allowFallback` semantics are unchanged

`RowLayout.cs:27-31` documents the single-unwrapped-row fallback as a property of the surface
(applies only when the surface has exactly one pane). Blocks do not change it: the fallback is about
wrapping, and it applies per line exactly as it does today.

---

## 4. What must not change

1. **`RowLayout.Wrap`'s signature and body.** It stays the one implementation of
   packing-and-wrapping. The block layer calls it; it does not fork or reimplement it.
2. **`valign` in its entirety.** §1.
3. **§4.0.1's cap site.** This layer never applies `maxLines`; it consumes already-capped output.
4. **`Segment`'s contract.** `Plain` stays escape-free (§3.2's rule 1). Splitting a value into lines
   must not disturb the `Plain`/`Markup` split or the width metric.
5. **Existing single-row behaviour is byte-identical.** A config in which no item returns more than
   one line must render exactly as it does today. This is verification item 1 and it is the
   regression gate for the whole task.
6. **§2.4 rule 1.** Every row, including a block's blank rows, is padded to the pane's inner width.

---

## 5. The cache key — this task does NOT extend it

`:1432` names `RenderLeaf`'s input tuple `(items, innerWidth, overflow, ellipsis, notes,
allowFallback)` as §2.5.1's purity/cache key.

**Block-ness is derived from the items' resolved values, which the tuple already carries. §3.1 adds
no new input, so the tuple, its §10 test, and the cache key are all untouched.**

Stated explicitly because #27 — the very next task — *does* extend the tuple, and an implementor
working through both in sequence should not carry that change backwards into this one. If it turns
out `items` carries config items rather than resolved values, then provider output is already
outside the key and that is a **pre-existing** defect to report, not one to fix here.
**Assumption I did not verify:** that `items` in the tuple is post-resolution.

---

## 6. The seam #27 attaches to — required, and the reason this task is sequenced first

I ruled in `SPEC-2.6-vertical-marker-splice.md` §5(b) that the marker splice sequences after this
task, because once blocks exist, "the last kept row" may come from a block that `Wrap` never
produced as part of a packed group. That ruling obliges this task to leave a defined attachment
point, and this section is it.

**Ruled: the block layer must concatenate all packed-group rows and block rows into one ordered row
list at exactly one place, and that concatenation site is the designated row-budget seam.** Clipping
to `maxRows` and splicing the ellipsis marker happen there, against that list, without regard to
which producer contributed the last surviving row.

Two requirements follow, and both are on *this* task:

- **One concatenation site, not several.** If groups and blocks are appended to a shared list from
  more than one place, #27 has no single point to attach to and will end up with a row-budget check
  per producer — which is how the two axes got out of step in the first place (§2.6's *"One rule,
  applied twice"*).
- **The last row must remain re-renderable at that site.** The concatenated list may be `PaneRow`-typed
  — `RowLayout.Wrap` composes to `PaneRow` internally and §4 rule 1 forbids changing that, so a
  Segment-backed row list downstream of `Wrap` does not exist and this section must not demand one
  (**A2**: it originally did; that was a defect, corrected against the #75 implementation). What this
  task must leave instead is **provenance**: at the concatenation site, each producer unit's
  `RenderLeaf` inputs must be in scope alongside the row range it contributed, so #27 can re-invoke
  that one unit with a row budget and splice the marker while the content is still `Segment`s inside
  `Wrap`.

This does **not** ask this task to implement any row budgeting. It asks it to not foreclose #27.

---

## 7. Relationship to #31, restated so it is not re-litigated

`CommandProvider.cs:160`'s `Split('\n', 2)[0]` means **no item can produce more than one line
today**. So:

- This task is fully implementable and testable now — a block's lines can be constructed in tests
  without any provider change, and D1-D8 are all exercisable at the layer they live in.
- But **nothing in production will render as a block until #31 lifts that cap.** That is expected
  and is why the sequence is §3.1 → #27 → #31: the model and the marker must both be correct before
  anything can reach them.
- **#31 must not ship the provider half without this task landed**, for the reason already ruled:
  a configurable cap on a quantity that cannot exceed 1 gives a green suite with the producer never
  firing.

D2's trailing-newline rule is the specific thing that will bite at the #31 boundary. Its test
(item 4) must exist before #31 lands.

---

## 8. Verification

1. **Byte-parity regression gate.** A config where every item returns exactly one line must produce
   output byte-identical to `main` before this change. Run it across the existing pane fixtures, not
   one config. If this fails, the block layer is intercepting the single-row path it should be
   passing through untouched.
2. **A block occupies its own rows.** Items `A`, `BLOCK`(2 lines), `C`, pane wide enough to fit all
   three on one row. Assert `A` is alone on row 1, the block's two lines are rows 2-3, `C` is alone
   on row 4. The width is deliberately generous so the *only* reason they are not packed together is
   the block rule.
3. **No separator adjacent to a block** (D4). Same config with a non-empty separator configured.
   Assert row 1 ends with `A` and no trailing separator characters, and row 4 begins with `C` and no
   leading separator. Assert on the exact row strings, not on a substring match.
4. **Trailing newline is not a block** (D2). A provider value of `"foo\n"` with two neighbouring
   single-row items. Assert all three pack onto **one** row with separators between them, and that
   no blank row is emitted anywhere in the pane. This is the test that fails loudly if D2 is skipped,
   and it must be written even though nothing can produce `"foo\n"` until #31.
5. **A blank line inside a block is preserved** (D3). Value `"a\n\nb"`. Assert three rows, the middle
   one empty but padded to the pane's inner width per §2.4 rule 1.
6. **Wrapping does not promote to a block** (D5, `:2515`). One item whose single line is three times
   the inner width, plus two short neighbours. Assert the long item still packs with its neighbours —
   the first row carries the long item's head *and* a separator *and* the next item where they fit —
   rather than the long item being pushed to its own rows. **This is the test that distinguishes
   pack-then-wrap from wrap-then-pack**, which `:2510-2512` names as the whole reason the order
   matters. It is the single most valuable item here.
7. **A block's line wraps** (D5). A 2-line block whose first line exceeds the inner width. Assert the
   block's total rows exceed its line count and that its rows remain contiguous with no packed item
   interleaved.
8. **No blank row between adjacent blocks** (D6). Two consecutive block items, and separately a block
   as the pane's first and as its last item. Assert no empty row at any junction.
9. **A suppressed item does not split a packed group** (D7). Items `A`, `EMPTY`, `C` where `EMPTY`
   resolves to nothing. Assert one row reading `A<sep>C` — one separator, not two, and not a gap.
10. **Single concatenation site** (§6). Not a runtime test: a structural check that the row list is
    assembled in exactly one place, with each producer unit's `RenderLeaf` inputs and contributed row
    range in scope there (provenance).
    Record the file and line in the completion report so #27 can attach to it without re-deriving it.

---

## 9. Confidence, and what I am not deciding

**High on §1** (valign is done) — read directly off eight call sites plus `Compositor.cs:17-19`'s own
statement that it implements §3.1.

**High on §2.1**, which I consider the load-bearing insight: §3.1's ordering rule constrains
classification, not pass structure, so `Wrap` survives intact. If an implementor reports that a real
two-pass split is unavoidable, that is a spec gap worth routing back — it would mean I have misread
how `Wrap` fuses the two.

**High on D1, D2, D5, D6, D7, D8.** D2 and D5 are the two I would most expect to be implemented
wrongly, and both have a dedicated verification item.

**Medium on D3** (preserve interior blank lines). Defensible either way; I chose preservation because
collapsing is a silent mutation and this path has no note channel. Cheap to reverse.

**D4 — settled (A1).** This section previously flagged D4 as a product call I had made rather than
routed, and named it the one decision worth showing Jim before it shipped. It was shown to him and
**he confirmed it as written**. Implement it; do not route it back. The reasoning in D4 stands as
the rationale, but the decision no longer rests on my judgment alone.

**Not escalation-worthy.** No security, migration, concurrency, or public-interface exposure; the
blast radius is bounded by verification item 1's byte-parity gate; and every decision above is
locally reversible.
