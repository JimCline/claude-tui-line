using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.8.1/§2.8.2: the degrade ladder and its two supporting mechanisms —
/// PaneBorderRenderer's height-axis border suppression and PaneAssembler's row-count clip. Items
/// here always resolve from a hand-built <c>values</c> dictionary (never through
/// ItemValueResolver/config), keyed by each PaneItem's <see cref="PaneItem.Id"/>, and are always
/// longer than the pane's own width so §2.6 truncation guarantees exactly one row per item —
/// row counts stay predictable without depending on RowLayout.Wrap's multi-segment packing.
/// </summary>
public class HeightLadderTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All);
    private static readonly PaneBorder Bordered = new(new ColorResolution.ColorExpr.Literal("grey"), BoxBorder.Rounded, PaneBorderEdges.All);
    private static readonly ItemContext Ctx = new(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens = new Dictionary<string, ColorResolution.ColorRule>();

    private static string Filler(int length) => new('X', length);

    // Block-line segments (SegmentBuilder.BuildItemSegment's 2-arg overload) always carry a
    // trailing raw SGR-reset marker regardless of color — see SegmentBuilder.cs's own doc
    // comment — so any test comparing a block row's Markup verbatim must strip tags/ANSI first,
    // same as PaneAssemblerBlockTests.cs's convention.
    private static string Stripped(string markup) => AnsiStrip.Strip(Spectre.Console.Markup.Remove(markup));

    private static PaneItem Item(string id) => new(null, null, null, null, Id: id);

    private static Pane Leaf(PaneBorder border, OverflowMode? overflow, IReadOnlyList<PaneItem> items, int? maxRows = null) =>
        new(PaneSplit.None, Array.Empty<Pane>(), "auto", border, overflow, "…", maxRows, items);

    // ---- PaneBorderRenderer.Wrap: omitEdges (§2.8.2) ----

    [Fact]
    public void Wrap_OmitEdges_DropsTopAndBottomButKeepsSideBars()
    {
        var content = new List<PaneRow> { new("Q", 1) };

        var wrapped = PaneBorderRenderer.Wrap(content, 1, Bordered, "grey", suppressed: false, omitEdges: true);

        Assert.Single(wrapped);
        Assert.Equal(1 + SizeResolver.OwnBorderReserve(Bordered), wrapped[0].Width);
        Assert.Contains("Q", wrapped[0].Markup);
    }

    [Fact]
    public void Wrap_OmitEdgesFalse_KeepsExistingThreeRowShape()
    {
        var content = new List<PaneRow> { new("Q", 1) };

        var wrapped = PaneBorderRenderer.Wrap(content, 1, Bordered, "grey", suppressed: false, omitEdges: false);

        Assert.Equal(3, wrapped.Count);
    }

    // ---- PaneAssembler.RenderLeafRows: maxContentRows clip + ellipsis marker (§2.8.1 rung 4 / §2.8.2) ----

    // SPEC-2.6-vertical-marker-splice.md §1/§9.3: the marker is a trailing cell-splice onto the
    // last surviving row's own content, never a full-row replacement — so the capped row keeps
    // whatever of its content still fits ahead of the marker rather than being blanked to just "…".
    [Fact]
    public void RenderLeafRows_MaxContentRowsClips_SplicesEllipsisOntoLastRowsContent()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 3);

        Assert.Equal(3, rows.Count);
        Assert.NotEqual("…", rows[2].Markup.TrimEnd());
        Assert.EndsWith("…", rows[2].Markup.TrimEnd());
        Assert.Equal(10, rows[2].Width);
    }

    // SPEC-2.6-vertical-marker-splice.md §7 test 1: cap:1 with overflowing content and a
    // comfortable innerWidth renders ONE row of real content ending in the marker — not the
    // marker alone. This is the headline behaviour change from the full-row-replacement approach.
    [Fact]
    public void RenderLeafRows_CapOne_RendersRealContentEndingInMarker()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 1);

        Assert.Single(rows);
        Assert.Contains('X', rows[0].Markup);
        Assert.EndsWith("…", rows[0].Markup.TrimEnd());
    }

    // SPEC-2.6-vertical-marker-splice.md §7 test 7 / §2.4 shearing invariant: no produced row may
    // exceed innerWidth, including the spliced row.
    [Fact]
    public void RenderLeafRows_SplicedRow_NeverExceedsInnerWidth()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 3);

        Assert.All(rows, r => Assert.True(r.Width <= 10));
    }

    // SPEC-2.6-vertical-marker-splice.md §9.7 test 9: the markerRequired boundary case. Two units
    // whose row counts sum to exactly cap, followed by a third unit with at least one row — the
    // owning (second) unit must still carry the marker even though nothing of its own overflowed.
    // Two single-line items (each their own RenderItemRows unit, since blocks are their own
    // units) plus a third packed item drive this without depending on block-model wiring.
    [Fact]
    public void RenderLeafRows_MarkerRequiredBoundary_LastKeptUnitCarriesMarkerEvenThoughItFit()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };
        var values = new Dictionary<string, string?>
        {
            ["a"] = "line-a\n\nrest-a",
            ["b"] = "line-b\n\nrest-b",
            ["c"] = Filler(20),
        };
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 6);

        Assert.Equal(6, rows.Count);
        Assert.EndsWith("…", rows[5].Markup.TrimEnd());
        Assert.DoesNotContain(rows, r => r.Markup.Contains('X'));

        // Negative control: drop the third unit so total == cap exactly — no marker anywhere.
        var itemsNoThird = new[] { Item("a"), Item("b") };
        var valuesNoThird = new Dictionary<string, string?>
        {
            ["a"] = "line-a\n\nrest-a",
            ["b"] = "line-b\n\nrest-b",
        };
        var paneNoThird = Leaf(NoBorder, OverflowMode.Truncate, itemsNoThird);
        var rowsNoThird = PaneAssembler.RenderLeafRows(paneNoThird, 10, Ctx, valuesNoThird, Tokens, new RenderNoteCollector(), maxContentRows: 6);

        Assert.Equal(6, rowsNoThird.Count);
        Assert.All(rowsNoThird, r => Assert.NotEqual("…", r.Markup.TrimEnd()));
        Assert.All(rowsNoThird, r => Assert.False(r.Markup.TrimEnd().EndsWith('…')));
    }

    // SPEC-2.6-vertical-marker-splice.md §7 test 6: alignment interaction. The spliced row's own
    // Width can be narrower than innerWidth (the marker doesn't necessarily fill the row) —
    // AlignRow's padding must still apply to that shorter width, on the correct side for the
    // pane's declared Align, not just to the (unreachable) full-width case.
    [Fact]
    public void RenderLeafRows_SplicedRow_StillHonorsRightAlign_PaddingOnTheCorrectSide()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?>
        {
            ["a"] = "line-a\n\nrest-a", // block: "line-a", "" (blank), "rest-a" — one unit per line
            ["b"] = Filler(20), // forces a 4th unit that must be dropped entirely by the cap
        };
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items) with { Align = PaneAlign.Right };

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 3);

        Assert.Equal(3, rows.Count);
        Assert.Equal("    line-a", Stripped(rows[0].Markup)); // 4-space left pad, Right align
        Assert.Equal(new string(' ', 10), Stripped(rows[1].Markup)); // blank interior row, fully padded
        Assert.Equal("   rest-a…", Stripped(rows[2].Markup)); // spliced row (width 7) left-padded to 10
        Assert.Equal(10, rows[2].Width);
        Assert.DoesNotContain('X', rows[2].Markup);
    }

    [Fact]
    public void RenderLeafRows_MaxContentRowsZero_ProducesNoRows()
    {
        var items = new[] { Item("i0") };
        var values = new Dictionary<string, string?> { ["i0"] = Filler(20) };
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 0);

        Assert.Empty(rows);
    }

    [Fact]
    public void RenderLeafRows_MaxContentRowsAboveNaturalCount_IsANoop()
    {
        var items = new[] { Item("i0"), Item("i1") };
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var uncapped = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector());
        var capped = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 10);

        Assert.Equal(uncapped.Count, capped.Count);
    }

    // ---- PaneAssembler.RenderLeafRows: §2.6 marker width budget on the vertical axis ----

    [Fact]
    public void RenderLeafRows_MaxContentRowsClips_EllipsisNotWiderThanInnerWidth_KeepsCapRowsOfContentInstead()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 1, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 3);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.NotEqual("…", row.Markup.TrimEnd()));
    }

    [Fact]
    public void RenderLeafRows_MaxContentRowsClips_EmptyEllipsis_KeepsCapRowsOfContentInstead()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items) with { Ellipsis = "" };

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 3);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.NotEqual(string.Empty, row.Markup.TrimEnd()));
    }

    // ---- PaneTreeRenderer: height suppression under 3 rows (§2.8.2) ----

    [Fact]
    public void Render_ClipRowsUnderThree_SuppressesBorderAndSpendsWholeBudgetOnContent()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };
        var pane = Leaf(Bordered, OverflowMode.Truncate, items);

        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector()) with { ClipRows = 2 };
        var contribution = PaneTreeRenderer.Render(resolved, Ctx, values, Tokens, new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
        Assert.All(contribution.Buffer.Rows, r => Assert.Contains("X", r.Markup));
    }

    [Fact]
    public void Render_OwnDeclaredTinyMaxRows_KeepsBorderAndDropsContentInstead()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };
        var pane = Leaf(Bordered, OverflowMode.Truncate, items, maxRows: 2);

        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector()) with { ClipRows = 2 };
        var contribution = PaneTreeRenderer.Render(resolved, Ctx, values, Tokens, new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
        Assert.All(contribution.Buffer.Rows, r => Assert.DoesNotContain("X", r.Markup));
    }

    // ---- HeightLadder.Resolve: end-to-end ladder behavior (§2.8.1) ----

    [Fact]
    public void Resolve_AlreadyInBudget_LeavesTreeUnmodified()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };
        var pane = Leaf(NoBorder, null, items);

        var (resolved, contribution) = HeightLadder.Resolve(pane, 10, surfaceMaxRows: 8, Ctx, values, Tokens, new RenderNoteCollector());

        Assert.Equal(2, resolved.Source.Items.Count);
        Assert.True(contribution.Buffer.Rows.Count <= 8);
    }

    [Fact]
    public void Resolve_Rung2_DemotesWrapToTruncate()
    {
        var item = Item("a");
        var values = new Dictionary<string, string?> { ["a"] = Filler(50) };
        var pane = Leaf(NoBorder, OverflowMode.Wrap, new[] { item });

        var (resolved, contribution) = HeightLadder.Resolve(pane, 10, surfaceMaxRows: 3, Ctx, values, Tokens, new RenderNoteCollector());

        Assert.Equal(OverflowMode.Truncate, resolved.Source.Overflow);
        Assert.True(contribution.Buffer.Rows.Count <= 3);
    }

    [Fact]
    public void Resolve_Rung3_DropsTrailingItemsUntilInBudget()
    {
        var items = Enumerable.Range(0, 4).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var (resolved, contribution) = HeightLadder.Resolve(pane, 10, surfaceMaxRows: 2, Ctx, values, Tokens, new RenderNoteCollector());

        Assert.Equal(2, resolved.Source.Items.Count);
        Assert.Equal(new[] { "i0", "i1" }, resolved.Source.Items.Select(i => i.Id));
        Assert.True(contribution.Buffer.Rows.Count <= 2);
    }

    [Fact]
    public void Resolve_Rung3_TieBreaksByReverseDeclarationOrder()
    {
        var itemsA = Enumerable.Range(0, 3).Select(i => Item($"a{i}")).ToList();
        var itemsB = Enumerable.Range(0, 3).Select(i => Item($"b{i}")).ToList();
        var values = itemsA.Concat(itemsB).ToDictionary(i => i.Id!, i => (string?)Filler(20));

        var leafA = Leaf(NoBorder, OverflowMode.Truncate, itemsA);
        var leafB = Leaf(NoBorder, OverflowMode.Truncate, itemsB);
        var root = new Pane(PaneSplit.Horizontal, new[] { leafA, leafB }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());

        var (resolved, contribution) = HeightLadder.Resolve(root, 10, surfaceMaxRows: 5, Ctx, values, Tokens, new RenderNoteCollector());

        // §2.8.1: "ties break by reverse declaration order" — the later-declared child (leafB,
        // index 1) degrades first, so leafA keeps all 3 of its items while leafB loses one.
        Assert.Equal(3, resolved.Source.Children[0].Items.Count);
        Assert.Equal(2, resolved.Source.Children[1].Items.Count);
        Assert.True(contribution.Buffer.Rows.Count <= 5);
    }

    [Fact]
    public void Resolve_Rung4_ClipsWhenItemDroppingCannotApply()
    {
        // Zero items routes through PaneAssembler's default-segments path, which is ineligible
        // for rung 3 (it requires Items.Count > 0) — this exercises rung 4 (clip) directly.
        var pane = Leaf(Bordered, null, Array.Empty<PaneItem>());

        var (_, contribution) = HeightLadder.Resolve(pane, 10, surfaceMaxRows: 4, Ctx, new Dictionary<string, string?>(), Tokens, new RenderNoteCollector());

        Assert.True(contribution.Buffer.Rows.Count <= 4);
    }

    [Fact]
    public void Resolve_Rung3_TieBreaksByReverseDeclarationOrderAcrossDifferentSplits()
    {
        var itemsA = Enumerable.Range(0, 3).Select(i => Item($"a{i}")).ToList();
        var itemsB = Enumerable.Range(0, 3).Select(i => Item($"b{i}")).ToList();
        var values = itemsA.Concat(itemsB).ToDictionary(i => i.Id!, i => (string?)Filler(20));

        var leafA = Leaf(NoBorder, OverflowMode.Truncate, itemsA);
        var leafB = Leaf(NoBorder, OverflowMode.Truncate, itemsB);
        var splitA = new Pane(PaneSplit.Horizontal, new[] { leafA }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());
        var splitB = new Pane(PaneSplit.Horizontal, new[] { leafB }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());
        var root = new Pane(PaneSplit.Horizontal, new[] { splitA, splitB }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());

        var (resolved, contribution) = HeightLadder.Resolve(root, 10, surfaceMaxRows: 5, Ctx, values, Tokens, new RenderNoteCollector());

        // §2.8.1: leafA and leafB are NOT siblings — each is the sole child of its own wrapping
        // split, so a sibling-scoped tie-break would have no basis to prefer one over the other.
        // The spec's surface-wide reverse pre-order DFS still picks the later-declared leaf
        // (leafB, reached second in the whole tree) to degrade first.
        Assert.Equal(3, resolved.Source.Children[0].Children[0].Items.Count);
        Assert.Equal(2, resolved.Source.Children[1].Children[0].Items.Count);
        Assert.True(contribution.Buffer.Rows.Count <= 5);
    }

    [Fact]
    public void Resolve_Rung3_CanEmptyMultiplePanesInOneRender()
    {
        var itemA = Item("a");
        var itemB = Item("b");
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };

        var leafA = Leaf(NoBorder, OverflowMode.Truncate, new[] { itemA });
        var leafB = Leaf(NoBorder, OverflowMode.Truncate, new[] { itemB });
        var root = new Pane(PaneSplit.Horizontal, new[] { leafA, leafB }, "auto", NoBorder, null, "…", null, Array.Empty<PaneItem>());

        var (resolved, contribution) = HeightLadder.Resolve(root, 10, surfaceMaxRows: 0, Ctx, values, Tokens, new RenderNoteCollector());

        // Rung 3's while-loop re-selects the tallest eligible leaf fresh each iteration, so it is
        // not limited to emptying a single pane per render — once leafB (the tie-break loser) is
        // exhausted it moves on to leafA.
        Assert.Empty(resolved.Source.Children[0].Items);
        Assert.Empty(resolved.Source.Children[1].Items);
        Assert.True(resolved.Children[0].ItemsEmptied);
        Assert.True(resolved.Children[1].ItemsEmptied);
        Assert.Equal(0, contribution.Buffer.Rows.Count);
    }

    [Fact]
    public void Resolve_EmptiedLeafCascadesBorderSuppressionToParentSplit()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };
        var leaf = Leaf(Bordered, OverflowMode.Truncate, items);
        var root = new Pane(PaneSplit.Horizontal, new[] { leaf }, "auto", Bordered, null, "…", null, Array.Empty<PaneItem>());

        var (resolved, contribution) = HeightLadder.Resolve(root, 10, surfaceMaxRows: 2, Ctx, values, Tokens, new RenderNoteCollector());

        // §2.8.1/§2.8.2: rung 3 empties the leaf's items entirely to fit surfaceMaxRows. The
        // emptied leaf then collapses under §2.8.2's <3-row rule, and because that rule is
        // evaluated per pane rather than leaf-only (item 4), the bordered root SPLIT collapses the
        // same way once its own content (the leaf's now-zero rows) plus its own border falls
        // under 3.
        Assert.Empty(resolved.Source.Children[0].Items);
        Assert.True(resolved.Children[0].ItemsEmptied);
        Assert.Empty(contribution.Buffer.Rows);
    }

    [Fact]
    public void RenderLeafRows_ItemsEmptied_ProducesZeroRowsRegardlessOfDefaultSegments()
    {
        var pane = Leaf(NoBorder, OverflowMode.Truncate, Array.Empty<PaneItem>());

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, new Dictionary<string, string?>(), Tokens, new RenderNoteCollector(), itemsEmptied: true);

        Assert.Empty(rows);
    }

    [Fact]
    public void Render_EmptiedLeafBudgetAtLeastThree_KeepsBorderWithEmptyInterior()
    {
        var pane = Leaf(Bordered, OverflowMode.Truncate, Array.Empty<PaneItem>());
        var values = new Dictionary<string, string?>();

        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector()) with { ClipRows = 4, ItemsEmptied = true };
        var contribution = PaneTreeRenderer.Render(resolved, Ctx, values, Tokens, new RenderNoteCollector());

        // §2.8.2 addendum: "emptied" != "collapsed" — at a budget that doesn't itself trigger the
        // <3-row suppression rule, an emptied pane keeps its border box even with zero items.
        Assert.True(contribution.Buffer.Rows.Count >= 2, "a non-suppressed bordered pane must render at least its top/bottom border rows");
        Assert.All(contribution.Buffer.Rows, r => Assert.DoesNotContain("X", r.Markup));
    }

    [Fact]
    public void Render_EmptiedLeafBudgetUnderThree_SuppressesSameAsDeclaredEmptyAtSameBudget()
    {
        var pane = Leaf(Bordered, OverflowMode.Truncate, Array.Empty<PaneItem>());
        var values = new Dictionary<string, string?>();

        var resolvedDeclaredEmpty = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector()) with { ClipRows = 2 };
        var resolvedEmptied = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector()) with { ClipRows = 2, ItemsEmptied = true };

        var declaredEmptyContribution = PaneTreeRenderer.Render(resolvedDeclaredEmpty, Ctx, values, Tokens, new RenderNoteCollector());
        var emptiedContribution = PaneTreeRenderer.Render(resolvedEmptied, Ctx, values, Tokens, new RenderNoteCollector());

        // §2.8.2: the <3-row suppression decision is keyed on ClipRows (the budget) alone —
        // flipping ItemsEmptied must not change whether the border suppresses.
        Assert.Equal(declaredEmptyContribution.Buffer.Rows.Count, emptiedContribution.Buffer.Rows.Count);
    }

    [Fact]
    public void Pane_HasNoClipRowsOrItemsEmptiedMembers_TheyLiveOnResolvedPaneInstead()
    {
        var paneMembers = typeof(Pane).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("ClipRows", paneMembers);
        Assert.DoesNotContain("ItemsEmptied", paneMembers);

        var resolvedPaneMembers = typeof(SizeResolver.ResolvedPane).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("ClipRows", resolvedPaneMembers);
        Assert.Contains("ItemsEmptied", resolvedPaneMembers);
    }
}
