using Spectre.Console;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.9: the two-pane acceptance target for splits, run against the exact
/// JSON from the spec through the real config loader and the real split pipeline
/// (<see cref="SizeResolver"/> + <see cref="PaneTreeRenderer"/>) — the same path Program.cs
/// takes. Per §2.9's own instruction, these assert behaviour/invariants (the cap binds,
/// requests are monotone non-increasing, every composed row is exactly the surface width) rather
/// than the specific integers in the spec's worked trace,
/// since those are consequences of real width measurement the spec explicitly declines to
/// freeze ("these integers are derived, not asserted").
/// </summary>
public class SplitAcceptanceTests
{
    private const string ConfigJson = """
    {
      "colors": { "model-accent": { "default": "blue" } },
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
    """;

    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadAcceptanceConfig()
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

    private static SizeResolver.ResolvedPane Resolve(string columns)
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth(columns, topLevel.ChromeReserve);
        Assert.True(surfaceWidth is int, "the acceptance config must produce a real surface width");
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        return SizeResolver.Resolve(pane, surfaceWidth!.Value, Ctx, values);
    }

    [Fact]
    public void Columns112_RootSplitsIntoExactlyTwoChildren()
    {
        var resolved = Resolve("112");

        Assert.Equal(2, resolved.Children.Count);
    }

    [Fact]
    public void Columns112_EveryComposedRowIsExactlyTheSurfaceWidth()
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values);

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors);

        Assert.All(rendered.Buffer.Rows, r => Assert.Equal(surfaceWidth, Markup.Remove(r.Markup).Length));
    }

    [Fact]
    public void Columns60_EveryComposedRowIsExactlyTheSurfaceWidth()
    {
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values);

        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors);

        Assert.All(rendered.Buffer.Rows, r => Assert.Equal(surfaceWidth, Markup.Remove(r.Markup).Length));
    }

    [Fact]
    public void Columns60_MaxSizeCapBindsBeforeAnyRemeasurement()
    {
        // §2.3 step 4's cap = (surface - gutter) - reserve, where reserve is the fill sibling's
        // own floor (MinUsableWidth + its own border reserve) — a structural bound independent
        // of the content pane's own measurement, per the spec's own §2.9 trace: "floor(left) =
        // MinUsableWidth 20 + borderReserve 4 = 24".
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var fillFloor = RowLayout.MinUsableWidth + PaneBorderRenderer.BorderReserve;
        var expectedCap = surfaceWidth - pane.Gutter - fillFloor;

        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values);
        var right = resolved.Children[1];

        Assert.True(right.OuterWidth <= expectedCap,
            $"right pane ({right.OuterWidth}) must never exceed the step-4 cap ({expectedCap})");
    }

    [Fact]
    public void Columns60_FixpointActuallyRanASecondPass_NotJustThePassOneCap()
    {
        // If the fixpoint stopped after pass 1, right would be granted exactly the step-4 cap
        // (see Columns60_MaxSizeCapBindsBeforeAnyRemeasurement). A strictly smaller final width
        // is only reachable if pass 2 re-measured under that cap and the packed items wrapped
        // narrower than it — proof, not assumption, that the loop iterated at least twice.
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var fillFloor = RowLayout.MinUsableWidth + PaneBorderRenderer.BorderReserve;
        var passOneCap = surfaceWidth - pane.Gutter - fillFloor;

        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values);
        var right = resolved.Children[1];

        Assert.True(right.OuterWidth < passOneCap,
            $"right pane ({right.OuterWidth}) must be strictly below the pass-1 cap ({passOneCap}) to prove re-measurement occurred");
    }

    [Fact]
    public void Columns60_FreedColumnsReachTheFillSibling_LeftGrantExceedsItsPassOneFloor()
    {
        // §2.3/§2.9: freed space must reach the sibling, not sit unused inside the anchor. The
        // anchor staying under its own cap (see the two tests above) is consistent with a
        // re-measure that is a complete no-op; the assertion that actually proves the freed
        // columns landed somewhere is the sibling's own final grant exceeding what it got in
        // pass 1 — pass 1 leaves a `fill` pane at exactly its own floor.
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("60", topLevel.ChromeReserve)!.Value;
        var fillFloor = RowLayout.MinUsableWidth + PaneBorderRenderer.BorderReserve;

        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values);
        var left = resolved.Children[0];

        Assert.True(left.OuterWidth > fillFloor,
            $"left pane ({left.OuterWidth}) must exceed its pass-1 floor-driven grant ({fillFloor}) once the right pane's wrap-aware re-measure frees columns back to it");
    }

    [Fact]
    public void Columns60_RequestsAreMonotoneNonIncreasing_FinalGrantNeverExceedsColumns112Grant()
    {
        var right112 = Resolve("112").Children[1];
        var right60 = Resolve("60").Children[1];

        Assert.True(right60.OuterWidth <= right112.OuterWidth,
            "a narrower surface must never grant the anchor pane more than a wider one did");
    }

    [Fact]
    public void Columns112_EveryPaneSpansTheFullSurfaceHeight()
    {
        // §2.2: a vertical split's children share its height, so every child's border box must
        // extend the full surface height rather than just its own natural content height. Checked
        // via the right pane's own border-vertical columns, not the row count 7.
        var (topLevel, pane) = LoadAcceptanceConfig();
        var surfaceWidth = SurfaceLayout.ComputeWidth("112", topLevel.ChromeReserve)!.Value;
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        var resolved = SizeResolver.Resolve(pane, surfaceWidth, Ctx, values);
        var rendered = PaneTreeRenderer.Render(resolved, Ctx, values, topLevel.Colors);

        var surfaceRowCount = rendered.Buffer.Rows.Count;
        var rightPane = resolved.Children[1];
        var rightStartCol = resolved.Children[0].OuterWidth + pane.Gutter;

        var rowsWithRightPaneBorder = rendered.Buffer.Rows.Count(r =>
        {
            var plain = Markup.Remove(r.Markup);
            return plain[rightStartCol] != ' ' && plain[rightStartCol + rightPane.OuterWidth - 1] != ' ';
        });

        Assert.Equal(surfaceRowCount, rowsWithRightPaneBorder);
    }
}
