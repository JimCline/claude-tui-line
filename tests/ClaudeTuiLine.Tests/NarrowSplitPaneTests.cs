using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: the <see cref="RowLayout.MinUsableWidth"/> single-line fallback is
/// a property of the SURFACE (only a surface with exactly one pane ever takes it), not of any
/// individual pane's width. A pane inside a split renders through
/// <see cref="PaneAssembler.RenderLeafRows"/>, which always calls
/// <see cref="PaneRenderer.RenderLeaf"/> with <c>allowFallback: false</c> — so even a pane far
/// narrower than <see cref="RowLayout.MinUsableWidth"/> just packs/wraps/truncates normally
/// instead of collapsing onto one overwide row.
/// </summary>
public class NarrowSplitPaneTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null);

    private static Pane ItemsPane(OverflowMode overflow, params PaneItem[] items) =>
        new(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, overflow, "…", null, items);

    [Fact]
    public void RenderLeafRows_NarrowerThanMinUsableWidth_PacksAcrossRowsInsteadOfOneOverwideRow()
    {
        Assert.True(10 < RowLayout.MinUsableWidth, "the test width must actually be below the fallback threshold");

        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude AAAAA" } };
        var item = new PaneItem("model-short", null, null, null);
        var pane = ItemsPane(OverflowMode.Truncate, item, item);

        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var values = new Dictionary<string, string?> { ["model-short"] = ItemRegistry.Find("model-short")!.ResolveValue(ctx) };
        var rows = PaneAssembler.RenderLeafRows(pane, 10, ctx, values, new Dictionary<string, ColorResolution.ColorRule>());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Width <= 10, $"row '{r.Markup}' exceeds the pane's own width"));
    }

    [Fact]
    public void RowLayoutWrap_SameSegmentsWithFallbackAllowed_CollapsesToOneOverwideRow()
    {
        // The counterfactual this proves split panes are spared from: the root-pane path's
        // allowFallback: true default would join both segments into a single row wider than the
        // available width instead of breaking them across rows.
        var segment = SegmentBuilder.BuildItemSegment("AAAAA", null);

        var rows = RowLayout.Wrap(new[] { segment, segment }, 10, allowFallback: true);

        Assert.Single(rows);
        Assert.True(rows[0].Width > 10);
    }
}
