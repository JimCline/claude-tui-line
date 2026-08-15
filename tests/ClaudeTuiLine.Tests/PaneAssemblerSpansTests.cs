namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-85-ADDENDUM-spans-threading.md §12.10: verifies Spans actually reaches the production
/// Segment PaneAssembler builds for a compound item, exercised through PaneAssembler.RenderItemRows
/// (not by hand-constructing a Segment) — the acceptance gate for the addendum's Gap 1 (D-A/D-B/D-C).
/// </summary>
public class PaneAssemblerSpansTests
{
    private static readonly ItemContext Ctx = new(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);
    private static readonly IReadOnlyDictionary<string, ColorResolution.ColorRule> Tokens = new Dictionary<string, ColorResolution.ColorRule>();

    private static string Stripped(string markup) => AnsiStrip.Strip(Spectre.Console.Markup.Remove(markup));

    private static PaneItem CompoundItem(string? color = "grey", string? link = null) => new(
        Item: null, Format: null, Color: color is null ? null : new ColorResolution.ColorExpr.Literal(color), Overflow: null,
        Id: "agent-badge", Link: link,
        Parts: new[]
        {
            new PaneItemPart(Text: "agent:", Item: null, From: null, Extract: null, Case: null, Format: null,
                Color: new ColorResolution.ColorExpr.Literal("grey")),
            new PaneItemPart(Text: null, Item: null, From: "agent", Extract: "[^:]+$", Case: "upper", Format: null,
                Color: new ColorResolution.ColorExpr.Literal("aqua")),
        });

    private static IReadOnlyDictionary<string, string?> Values => new Dictionary<string, string?> { ["agent"] = "team:worker-7" };

    // §12.10 item 1: this test must fail against the pre-addendum tree (ItemDecision carried no
    // Spans, and PaneAssembler's single-line path called the 3-arg BuildItemSegment, which never
    // received them) and pass once Spans is threaded through.
    [Fact]
    public void CompoundItem_ProductionSegment_CarriesPerPartSpans()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens, new RenderNoteCollector());

        var segment = Assert.Single(Assert.Single(units).Segments);
        Assert.NotNull(segment.Spans);
        Assert.Equal(2, segment.Spans!.Count);
        Assert.Equal("agent:", segment.Spans[0].Plain);
        Assert.Equal("WORKER-7", segment.Spans[1].Plain);
        Assert.Equal(string.Concat(segment.Spans.Select(s => s.Plain)), segment.Plain);
    }

    // D-B: the item-level `color: grey` must not wrap the whole compound a second time — it is
    // already consumed per-part as each part's default, so the outer wrap must be absent, not
    // merely redundant.
    [Fact]
    public void CompoundItem_ItemLevelColor_DoesNotDoubleWrapOutsideParts()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens, new RenderNoteCollector());
        var segment = Assert.Single(Assert.Single(units).Segments);

        Assert.Equal(string.Concat(segment.Spans!.Select(s => s.Markup)), segment.Markup);
        Assert.Equal("agent:WORKER-7", Stripped(segment.Markup));
    }

    // D-C: a `link` on the compound wraps the whole thing from outside, per §12.3's revised
    // invariant ("modulo an OSC 8 link wrap") — Spans still decomposes the pre-link style markup.
    [Fact]
    public void CompoundItem_WithLink_WrapsWholeCompound_SpansSurviveUnderneath()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem(link: "https://example/{}") });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens, new RenderNoteCollector());
        var segment = Assert.Single(Assert.Single(units).Segments);

        // {} substitutes the compound's own resolved value — its concatenated Plain (LeafItems.cs:41).
        Assert.True(OscHyperlink.TryUnwrap(segment.Markup, out var url, out var inner));
        Assert.Equal("https://example/agent:WORKER-7", url);
        Assert.NotNull(segment.Spans);
        Assert.Equal(string.Concat(segment.Spans!.Select(s => s.Markup)), inner);
    }

    // D-F (found via the real-CLI narrow-width acceptance check, not hand-built spans): LeafItems.cs
    // used to build each part's StyledSpan.Markup via SegmentBuilder.BuildItemSegment(text, color),
    // which bakes an escaped raw SGR reset after the text — breaking
    // SegmentTruncation.TryGetSimpleWrap's exact "[/]"-suffix match and silently dropping the
    // colour of any span whose truncation cut lands inside it (as opposed to a span that survives
    // whole, which keeps its original markup unrestyled and so masked the bug).
    [Fact]
    public void CompoundItem_TruncatedProductionSegment_BothSpansKeepColour()
    {
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "auto",
            new PaneBorder(new ColorResolution.ColorExpr.Literal("grey"), null, PaneBorderEdges.All),
            OverflowMode.Truncate, "…", null, new[] { CompoundItem() });

        var units = PaneAssembler.RenderItemRows(pane, 80, Ctx, Values, Tokens, new RenderNoteCollector());
        var segment = Assert.Single(Assert.Single(units).Segments);

        // "agent:" (6) + "WORKER-7" (8) = 14 plain chars; innerWidth 9 with a 1-wide ellipsis
        // cuts at plain index 8 — inside the second (aqua) span, forcing RestyleSlice's
        // partial-slice path rather than its whole-span-survives shortcut.
        var truncated = SegmentTruncation.Truncate(segment, innerWidth: 9, ellipsis: "…");

        Assert.NotNull(truncated.Spans);
        Assert.Equal(3, truncated.Spans!.Count);
        Assert.Contains("[grey]", truncated.Spans[0].Markup);
        Assert.Contains("[aqua]", truncated.Spans[1].Markup);
    }
}
