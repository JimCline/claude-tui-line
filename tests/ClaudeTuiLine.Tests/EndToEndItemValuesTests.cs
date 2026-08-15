using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5: every other test in this suite hand-builds its own <c>values</c>
/// dictionary and passes it directly into SizeResolver/PaneTreeRenderer/PaneAssembler/LeafItems, so
/// none of them exercise the production wiring that actually populates <c>values</c> from a real
/// config's <c>items</c> array — <c>ConfigLoader.LoadAll</c> -&gt; <c>ItemValueResolver.ResolveAsync</c>
/// -&gt; <c>SizeResolver.Resolve</c> -&gt; <c>PaneTreeRenderer.Render</c>, the exact chain
/// <c>Program.cs</c> itself runs. This drives that chain end to end, for a configured item id other
/// than <c>model-short</c>, and asserts its resolved value actually reaches the rendered text.
/// </summary>
public class EndToEndItemValuesTests
{
    private static readonly StatusInput Input = new() { Model = new ModelInfo { DisplayName = "Claude Opus 4.5" } };

    private const string ConfigJson = """
    {
      "surface": {
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "children": [
            { "size": "fill", "border": { "enabled": true }, "items": [ { "item": "model" } ] },
            { "size": "content", "border": { "enabled": true }, "items": [ { "item": "model-short" } ] }
          ]
        }
      }
    }
    """;

    [Fact]
    public async Task ConfiguredNonModelShortItem_RenderedThroughRealProductionPath_ProducesNonEmptyText()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ConfigJson);
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

        var ctx = new ItemContext(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);
        var values = (await ItemValueResolver.ResolveAsync(
            pane, ctx, topLevel.Colors, rawStdinJson: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(), surfaceWidth: null,
            new RenderNoteCollector())).Values;

        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, ctx, values, new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, ctx, values, topLevel.Colors, new RenderNoteCollector());

        var plainText = string.Join('\n', rendered.Buffer.Rows.Select(r => Markup.Remove(r.Markup)));

        Assert.Contains("Claude Opus 4.5", plainText);
    }
}
