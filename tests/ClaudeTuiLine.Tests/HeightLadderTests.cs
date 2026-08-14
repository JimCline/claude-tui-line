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
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null);
    private static readonly PaneBorder Bordered = new(new ColorResolution.ColorExpr.Literal("grey"), BoxBorder.Rounded);
    private static readonly ItemContext Ctx = new(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens = new Dictionary<string, ColorResolution.ColorRule>();

    private static string Filler(int length) => new('X', length);

    private static PaneItem Item(string id) => new(null, null, null, null, Id: id);

    private static Pane Leaf(PaneBorder border, OverflowMode? overflow, IReadOnlyList<PaneItem> items, int? maxRows = null, int? clipRows = null) =>
        new(PaneSplit.None, Array.Empty<Pane>(), "auto", border, overflow, "…", maxRows, items, ClipRows: clipRows);

    // ---- PaneBorderRenderer.Wrap: omitEdges (§2.8.2) ----

    [Fact]
    public void Wrap_OmitEdges_DropsTopAndBottomButKeepsSideBars()
    {
        var content = new List<PaneRow> { new("Q", 1) };

        var wrapped = PaneBorderRenderer.Wrap(content, 1, Bordered, "grey", suppressed: false, omitEdges: true);

        Assert.Single(wrapped);
        Assert.Equal(1 + PaneBorderRenderer.BorderReserve, wrapped[0].Width);
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

    [Fact]
    public void RenderLeafRows_MaxContentRowsClips_ReplacesLastRowWithEllipsis()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item($"i{i}")).ToList();
        var values = items.ToDictionary(i => i.Id!, i => (string?)Filler(20));
        var pane = Leaf(NoBorder, OverflowMode.Truncate, items);

        var rows = PaneAssembler.RenderLeafRows(pane, 10, Ctx, values, Tokens, new RenderNoteCollector(), maxContentRows: 3);

        Assert.Equal(3, rows.Count);
        Assert.Equal("…", rows[2].Markup.TrimEnd());
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

    // ---- PaneTreeRenderer: height suppression under 3 rows (§2.8.2) ----

    [Fact]
    public void Render_ClipRowsUnderThree_SuppressesBorderAndSpendsWholeBudgetOnContent()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };
        var pane = Leaf(Bordered, OverflowMode.Truncate, items, clipRows: 2);

        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector());
        var contribution = PaneTreeRenderer.Render(resolved, Ctx, values, Tokens, new RenderNoteCollector());

        Assert.Equal(2, contribution.Buffer.Rows.Count);
        Assert.All(contribution.Buffer.Rows, r => Assert.Contains("X", r.Markup));
    }

    [Fact]
    public void Render_OwnDeclaredTinyMaxRows_KeepsBorderAndDropsContentInstead()
    {
        var items = new[] { Item("a"), Item("b") };
        var values = new Dictionary<string, string?> { ["a"] = Filler(20), ["b"] = Filler(20) };
        var pane = Leaf(Bordered, OverflowMode.Truncate, items, maxRows: 2, clipRows: 2);

        var resolved = SizeResolver.Resolve(pane, 10, Ctx, values, new RenderNoteCollector());
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
}
