using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2: OSC 8 hyperlinks. Rule 1 (Plain carries link text only, Markup
/// carries the OSC 8 bytes) is exercised indirectly by every test below asserting on
/// <c>Segment.Plain</c>/<c>Markup</c> separately. Rule 2 (OSC scanned separately from CSI) is
/// covered by the raw-command-passthrough tests. Rule 3 (a wrapped link reopens per continuation
/// row; a truncated link closes itself before the ellipsis) and ruling d ("clicking '…' must never
/// navigate") are covered directly.
/// </summary>
public class HyperlinkTests
{
    private static readonly PaneBorder NoBorder = new(new ColorResolution.ColorExpr.Literal("grey"), null);

    [Fact]
    public void WrapOfLinkedSegment_EveryContinuationRow_ReopensAndClosesTheLink()
    {
        const string url = "https://example.com/path";
        var text = new string('A', 25);
        var markup = OscHyperlink.Wrap(url, Markup.Escape(text));
        var segment = new Segment(markup, text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 10, OverflowMode.Wrap, "…");

        Assert.Equal(3, buffer.Rows.Count);
        foreach (var row in buffer.Rows)
        {
            Assert.True(OscHyperlink.TryUnwrap(row.Markup, out var rowUrl, out _), $"row '{row.Markup}' is not a self-contained link");
            Assert.Equal(url, rowUrl);
        }
    }

    [Fact]
    public void WrapOfLinkedColoredSegment_EveryContinuationRow_KeepsBothColorAndLink()
    {
        const string url = "https://example.com/path";
        var text = new string('B', 22);
        var coloredMarkup = $"[green]{Markup.Escape(text)}[/]";
        var segment = new Segment(OscHyperlink.Wrap(url, coloredMarkup), text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 10, OverflowMode.Wrap, "…");

        Assert.True(buffer.Rows.Count > 1, "fixture must actually wrap across multiple rows to exercise reopening");
        foreach (var row in buffer.Rows)
        {
            Assert.True(OscHyperlink.TryUnwrap(row.Markup, out var rowUrl, out var inner));
            Assert.Equal(url, rowUrl);
            Assert.StartsWith("[green]", inner);
            Assert.EndsWith("[/]", inner);
        }
    }

    [Fact]
    public void TruncateOfLinkedSegment_ClosesLinkBeforeAppendingUnlinkedEllipsis()
    {
        const string url = "https://example.com/path";
        var text = new string('C', 20);
        var segment = new Segment(OscHyperlink.Wrap(url, Markup.Escape(text)), text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 10, OverflowMode.Truncate, "…");

        Assert.Single(buffer.Rows);
        var row = buffer.Rows[0].Markup;

        var closeIndex = row.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);
        Assert.True(closeIndex >= 0, "expected the link to be closed within the truncated row");

        // Ruling d: clicking "…" must never navigate — nothing after the close reopens a link.
        var afterClose = row[(closeIndex + OscHyperlink.Close.Length)..];
        Assert.Contains("…", afterClose);
        Assert.DoesNotContain("]8;;", afterClose);
    }

    [Fact]
    public void LeafContent_LinkTemplateReferencingMissingOtherId_SuppressesLinkButKeepsItemText()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var item = new PaneItem("model-short", null, null, null, Link: "https://x/{missing-id}");
        var values = new Dictionary<string, string?> { ["model-short"] = ItemRegistry.Find("model-short")!.ResolveValue(ctx) };

        var resolved = LeafItems.Resolve(new[] { item }, values, ctx).Single();
        var decision = LeafContent.Decide(resolved, values);

        Assert.False(OscHyperlink.TryUnwrap(decision.Markup, out _, out _));
        Assert.Equal("Opus 4.5", decision.Text);
    }

    [Fact]
    public void LeafContent_LinkTemplateReferencingPresentOtherId_WrapsMarkupInLink()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var item = new PaneItem("model-short", null, null, null, Link: "https://x/{other}");
        var values = new Dictionary<string, string?>
        {
            ["model-short"] = ItemRegistry.Find("model-short")!.ResolveValue(ctx),
            ["other"] = "resolved-value",
        };

        var resolved = LeafItems.Resolve(new[] { item }, values, ctx).Single();
        var decision = LeafContent.Decide(resolved, values);

        Assert.True(OscHyperlink.TryUnwrap(decision.Markup, out var url, out _));
        Assert.Equal("https://x/resolved-value", url);
    }

    [Fact]
    public void DerivedItem_ExtractAndCaseApply_AndChainingToAnotherDerivedItemIsSuppressed()
    {
        var input = new StatusInput { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };
        var ctx = new ItemContext(input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

        var derived1 = new PaneItem(null, null, null, null, Id: "derived1", From: "model", Extract: "^(\\w+)", Case: "upper");
        // §8: ResolveDerived snapshots values before any derived result is written back, so a
        // derived item naming another derived item's id can never see that id's value.
        var derived2 = new PaneItem(null, null, null, null, Id: "derived2", From: "derived1");
        var pane = new Pane(PaneSplit.None, Array.Empty<Pane>(), "content", NoBorder, null, "…", null, new[] { derived1, derived2 });

        var values = ItemValueResolver.Resolve(pane, ctx);

        Assert.Equal("CLAUDE", values["derived1"]);
        Assert.Null(values["derived2"]);
    }

    [Fact]
    public void BuildItemSegment_RawUnterminatedAnsiColor_PlainIsStrippedAndMarkupSelfResets()
    {
        var segment = SegmentBuilder.BuildItemSegment("[31mHello", null);

        Assert.Equal("Hello", segment.Plain);
        Assert.EndsWith("[0m", segment.Markup);
    }

    [Fact]
    public void CommandItemSegment_UnterminatedRawAnsiColor_ResetPrecedesNextSegmentInRenderedOutput()
    {
        var redSegment = SegmentBuilder.BuildItemSegment("[31mHello", null);
        var plainSegment = SegmentBuilder.BuildItemSegment("World", null);

        var rows = RowLayout.Wrap(new[] { redSegment, plainSegment }, 1000);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        foreach (var row in rows)
        {
            console.MarkupLine(row);
        }

        var raw = writer.ToString();
        var resetIndex = raw.IndexOf("[0m", StringComparison.Ordinal);
        var worldIndex = raw.IndexOf("World", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0, "expected the raw SGR reset to appear in rendered output");
        Assert.True(worldIndex >= 0, "expected the neighboring segment's text to appear in rendered output");
        Assert.True(resetIndex < worldIndex, "the reset must close the unterminated red before the next segment's text");
    }
}
