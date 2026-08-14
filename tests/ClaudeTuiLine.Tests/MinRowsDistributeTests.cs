using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SizeResolver.MinRowsPackerInvocationCount is [ThreadStatic], which already keeps
// Columns112_LiveConfig_PackerInvocationCountStaysBounded's reset-render-read sequence isolated
// from every other test class regardless of xUnit's default cross-collection parallelism —
// this collection is belt-and-braces on top of that, not the fix itself. Keeps the isolation
// intact even if a future change (sizing going async, the counter losing [ThreadStatic]) would
// otherwise reopen the cross-test contamination that a shared, unsynchronized counter once
// produced here.
[CollectionDefinition("MinRowsDiagnostics", DisableParallelization = true)]
public class MinRowsDiagnosticsCollection
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3.1: <c>distribute: "min-rows"</c> against the same two-pane config
/// <see cref="SplitAcceptanceTests"/> exercises for <c>greedy</c>, with <c>"distribute": "min-rows"</c>
/// added to the root pane. Acceptance condition 1 (§2.3.1): "on a config small enough to
/// brute-force, the allocation this returns must equal the best found by exhaustively laying out
/// every legal width."
/// </summary>
[Collection("MinRowsDiagnostics")]
public class MinRowsDistributeTests
{
    private const string ConfigJson = """
    {
      "colors": { "model-accent": { "default": "blue" } },
      "surface": {
        "maxRows": 8,
        "pane": {
          "split": "vertical",
          "gutter": 1,
          "distribute": "min-rows",
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
    """;

    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private readonly ITestOutputHelper _output;

    public MinRowsDistributeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, ConfigJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Renders one candidate leaf in isolation at a given outer width, through the same public
    // pipeline the real root goes through (SizeResolver.Resolve + PaneTreeRenderer.Render) rather
    // than any internal min-rows helper — an independent ground truth, not a re-check of the code
    // under test.
    private static int RowsAt(Pane candidate, int outerWidth, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        var resolved = SizeResolver.Resolve(candidate, outerWidth, Ctx, values, new RenderNoteCollector());
        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, tokens, new RenderNoteCollector());
        return rendered.Buffer.Rows.Count;
    }

    [Fact]
    public void Columns112_MinRows_MatchesBruteForceOptimalRowCount()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        var left = pane.Children[0];
        var right = pane.Children[1];
        var r = surfaceWidth - pane.Gutter;

        // left is `fill` (uncapped) and right has no configured maxSize, so for this config the
        // real allocator never leaves width unspent (§2.3.1: surplus flows to an uncapped
        // candidate until none remains) — every allocation it could ever produce has
        // w1 + w2 == r. That makes a single sweep over w1, with w2 = r - w1, exhaustive over the
        // allocations actually in competition, without leaning on rows_i's own monotonicity to
        // justify skipping any of them.
        var lo1 = RowLayout.MinUsableWidth + PaneBorderRenderer.BorderReserve;
        var hi1 = r;

        var bestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, values, topLevel.Colors);
            var rows2 = RowsAt(right, w2, values, topLevel.Colors);
            bestScore = Math.Min(bestScore, Math.Max(rows1, rows2));
        }

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values, new RenderNoteCollector());
        var achievedRows1 = RowsAt(left, resolved.Children[0].OuterWidth, values, topLevel.Colors);
        var achievedRows2 = RowsAt(right, resolved.Children[1].OuterWidth, values, topLevel.Colors);
        var achievedScore = Math.Max(achievedRows1, achievedRows2);

        Assert.Equal(bestScore, achievedScore);
    }

    [Fact]
    public void Columns60_MinRows_MatchesBruteForceOptimalRowCount()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        var left = pane.Children[0];
        var right = pane.Children[1];
        var r = surfaceWidth - pane.Gutter;

        var lo1 = RowLayout.MinUsableWidth + PaneBorderRenderer.BorderReserve;
        var hi1 = r;

        var bestScore = int.MaxValue;
        for (var w1 = lo1; w1 <= hi1; w1++)
        {
            var w2 = r - w1;
            var rows1 = RowsAt(left, w1, values, topLevel.Colors);
            var rows2 = RowsAt(right, w2, values, topLevel.Colors);
            bestScore = Math.Min(bestScore, Math.Max(rows1, rows2));
        }

        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values, new RenderNoteCollector());
        var achievedRows1 = RowsAt(left, resolved.Children[0].OuterWidth, values, topLevel.Colors);
        var achievedRows2 = RowsAt(right, resolved.Children[1].OuterWidth, values, topLevel.Colors);
        var achievedScore = Math.Max(achievedRows1, achievedRows2);

        Assert.Equal(bestScore, achievedScore);
    }

    [Fact]
    public void Columns112_LiveConfig_PackerInvocationCountStaysBounded()
    {
        var (topLevel, pane) = LoadConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);

        SizeResolver.MinRowsPackerInvocationCount = 0;
        SizeResolver.Resolve(pane, surfaceWidth, Ctx, values, new RenderNoteCollector());
        var count = SizeResolver.MinRowsPackerInvocationCount;

        _output.WriteLine($"min-rows packer invocations for the live two-pane config at COLUMNS=112: {count}");

        // §2.3.1's own worked example, for this exact config shape: "tens of packer calls".
        Assert.InRange(count, 1, 300);
    }
}
