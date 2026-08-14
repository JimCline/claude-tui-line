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
        var text = new string('A', 75);
        var markup = OscHyperlink.Wrap(url, Markup.Escape(text));
        var segment = new Segment(markup, text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 30, OverflowMode.Wrap, "…");

        Assert.Equal(3, buffer.Rows.Count);
        foreach (var row in buffer.Rows)
        {
            Assert.True(OscHyperlink.TryUnwrap(row.Markup, out var rowUrl, out _), $"row '{row.Markup}' is not a self-contained link");
            Assert.Equal(url, rowUrl);
        }
    }

    [Fact]
    public void WrapOfLinkedSegment_AtFallbackWidth_CollapsesToOneRow_LinkIntactAndUnsplit()
    {
        const string url = "https://example.com/path";
        var text = new string('A', 25);
        var markup = OscHyperlink.Wrap(url, Markup.Escape(text));
        var segment = new Segment(markup, text);

        // width=10 is below RowLayout.MinUsableWidth(20), so RenderLeaf's per-chunk wrap
        // pre-pass still runs (producing 3 self-contained link chunks, as above at width=30),
        // but RowLayout.Wrap's own fallback then collapses them onto one overwide row.
        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 10, OverflowMode.Wrap, "…");

        Assert.Single(buffer.Rows);
        var row = buffer.Rows[0].Markup;

        // OscHyperlink.OpenPrefix is private, so the introducer is derived from the public
        // Close constant (Close = OpenPrefix + ST, and ST is 2 bytes) rather than duplicated.
        var introducer = OscHyperlink.Close[..^2];
        var opens = new List<int>();
        var closes = new List<int>();
        for (var i = row.IndexOf(introducer, StringComparison.Ordinal); i >= 0; i = row.IndexOf(introducer, i + 1, StringComparison.Ordinal))
        {
            var isClose = i + OscHyperlink.Close.Length <= row.Length
                && string.CompareOrdinal(row.Substring(i, OscHyperlink.Close.Length), OscHyperlink.Close) == 0;
            (isClose ? closes : opens).Add(i);
        }

        Assert.NotEmpty(opens);
        Assert.Equal(opens.Count, closes.Count);

        for (var k = 0; k < opens.Count; k++)
        {
            var closeEnd = closes[k] + OscHyperlink.Close.Length;
            var slice = row[opens[k]..closeEnd];
            Assert.True(OscHyperlink.TryUnwrap(slice, out var rowUrl, out _), $"OSC 8 wrap #{k} is not well-formed at the fallback join: '{slice}'");
            Assert.Equal(url, rowUrl);
        }
    }

    [Fact]
    public void WrapOfLinkedColoredSegment_EveryContinuationRow_KeepsBothColorAndLink()
    {
        const string url = "https://example.com/path";
        var text = new string('B', 65);
        var coloredMarkup = $"[green]{Markup.Escape(text)}[/]";
        var segment = new Segment(OscHyperlink.Wrap(url, coloredMarkup), text);

        var buffer = PaneRenderer.RenderLeaf(new[] { segment }, 30, OverflowMode.Wrap, "…");

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
        var segment = SegmentBuilder.BuildItemSegment("\x1b[31mHello", null);

        Assert.Equal("Hello", segment.Plain);

        // Round-trip through Spectre's own markup parser rather than inspecting the raw markup
        // string directly: Segment.Markup is markup *source*, escaped so it parses without
        // throwing, and it is what actually reaches the terminal (via a real console render)
        // that must carry the real ESC[0m reset — not the source's own escaped bytes.
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        console.Markup(segment.Markup);

        Assert.EndsWith("\x1b[0m", writer.ToString());
    }

    [Fact]
    public void DisplayWidth_MeasuresLinkedRow_StripsBothOsc8AndMarkupTagsToPlainText()
    {
        const string url = "https://example.com/path";
        const string text = "click";
        var markup = OscHyperlink.Wrap(url, $"[green]{Markup.Escape(text)}[/]");

        Assert.Equal(text, DisplayWidth.Strip(markup));
        Assert.Equal(text.Length, DisplayWidth.Measure(markup));
    }

    [Fact]
    public void EndToEnd_ConfiguredLink_RealPipelineRowReachesConsoleMarkupLineAsOsc8()
    {
        // Full pipeline, not a unit slice: ConfigLoader -> ItemValueResolver -> SizeResolver ->
        // PaneTreeRenderer -> the same OscHyperlink.EscapeForRender + console.MarkupLine(row.Markup)
        // calls Program.cs makes for every row of the split pipeline (Program.cs:104). Top-level
        // Main isn't reachable from a test project, so this reproduces its exact statements
        // in-process instead.
        const string url = "https://example.com/org/repo";
        const string configJson = """
        {
          "surface": {
            "pane": {
              "items": [ { "item": "remote-url", "link": "{}" } ]
            }
          }
        }
        """;

        var path = Path.GetTempFileName();
        File.WriteAllText(path, configJson);
        ResolvedConfig topLevel;
        Pane pane;
        try
        {
            (topLevel, pane) = ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }

        var ctx = new ItemContext(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => url);
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values);
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = int.MaxValue / 2;

        foreach (var row in rendered.Buffer.Rows)
        {
            console.MarkupLine(OscHyperlink.EscapeForRender(row.Markup));
        }

        var raw = writer.ToString();

        // Not just "]8;;" appears somewhere: the literal OSC 8 open sequence with the URL embedded
        // contiguously, and the literal close sequence, in that order — the exact bytes a terminal's
        // hyperlink support keys off, not a loose substring.
        var openPrefix = OscHyperlink.Close[..^2];
        var st = OscHyperlink.Close[^2..];
        var expectedOpen = $"{openPrefix}{url}{st}";

        var openIndex = raw.IndexOf(expectedOpen, StringComparison.Ordinal);
        var closeIndex = raw.IndexOf(OscHyperlink.Close, StringComparison.Ordinal);

        Assert.True(openIndex >= 0, $"expected the exact OSC 8 open sequence for '{url}' in rendered output: {raw}");
        Assert.True(closeIndex > openIndex, $"expected the OSC 8 close sequence after the open sequence in rendered output: {raw}");
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
            console.MarkupLine(row.Markup);
        }

        var raw = writer.ToString();
        var resetIndex = raw.IndexOf("[0m", StringComparison.Ordinal);
        var worldIndex = raw.IndexOf("World", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0, "expected the raw SGR reset to appear in rendered output");
        Assert.True(worldIndex >= 0, "expected the neighboring segment's text to appear in rendered output");
        Assert.True(resetIndex < worldIndex, "the reset must close the unterminated red before the next segment's text");
    }
}
