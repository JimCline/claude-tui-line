namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3: <c>distribute: "even"</c> — divides the extent left after fixed and
/// percent panes are subtracted equally among the remaining content/fill candidates, ignoring
/// intrinsic measurement and the content/fill distinction entirely. Also covers that
/// <c>distribute: "greedy"</c> is now selectable explicitly and behaves identically to the
/// (unchanged) implicit default.
/// </summary>
public class EvenDistributeTests
{
    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SizeResolver.ResolvedPane Resolve(Pane pane, ResolvedConfig topLevel, int outerWidth)
    {
        var values = ItemValueResolver.Resolve(pane, Ctx, topLevel.Colors);
        return SizeResolver.Resolve(pane, outerWidth, Ctx, values, new RenderNoteCollector());
    }

    [Fact]
    public void Even_CleanDivision_SplitsRemainingExtentEquallyAmongCandidates()
    {
        const string json = """
        {
          "colors": {},
          "surface": {
            "maxRows": 8,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "children": [
                { "size": "10", "border": { "enabled": false, "color": "grey" } },
                { "size": "content", "border": { "enabled": false, "color": "grey" }, "overflow": "wrap" },
                { "size": "fill", "border": { "enabled": false, "color": "grey" }, "overflow": "wrap" }
              ]
            }
          }
        }
        """;
        var (topLevel, pane) = LoadConfig(json);

        // outerWidth chosen so (outerWidth - BoundaryCost - fixed(10)) divides evenly by the two
        // remaining candidates (content, fill): BoundaryCost = gutter(1) * (3-1) = 2, so 100 - 2 - 10 = 88.
        var resolved = Resolve(pane, topLevel, 100);

        Assert.Equal(10, resolved.Children[0].OuterWidth);
        Assert.Equal(44, resolved.Children[1].OuterWidth);
        Assert.Equal(44, resolved.Children[2].OuterWidth);
    }

    [Fact]
    public void Even_RemainderGoesToTheLeftmostCandidate()
    {
        const string json = """
        {
          "colors": {},
          "surface": {
            "maxRows": 8,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "children": [
                { "size": "10", "border": { "enabled": false, "color": "grey" } },
                { "size": "content", "border": { "enabled": false, "color": "grey" }, "overflow": "wrap" },
                { "size": "fill", "border": { "enabled": false, "color": "grey" }, "overflow": "wrap" }
              ]
            }
          }
        }
        """;
        var (topLevel, pane) = LoadConfig(json);

        // 101 - BoundaryCost(2) - fixed(10) = 89, odd: each candidate gets 44 and the leftover
        // cell goes to the leftmost candidate (the content pane, the first of the two).
        var resolved = Resolve(pane, topLevel, 101);

        Assert.Equal(10, resolved.Children[0].OuterWidth);
        Assert.Equal(45, resolved.Children[1].OuterWidth);
        Assert.Equal(44, resolved.Children[2].OuterWidth);
    }

    [Fact]
    public void Greedy_ExplicitAndImplicitDefault_ProduceIdenticalGrants()
    {
        const string implicitJson = """
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
        const string explicitJson = """
        {
          "colors": { "model-accent": { "default": "blue" } },
          "surface": {
            "maxRows": 8,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "greedy",
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

        var (implicitTop, implicitPane) = LoadConfig(implicitJson);
        var (explicitTop, explicitPane) = LoadConfig(explicitJson);

        Assert.Equal(PaneDistribute.Greedy, implicitPane.Distribute);
        Assert.Equal(PaneDistribute.Greedy, explicitPane.Distribute);

        var implicitResolved = Resolve(implicitPane, implicitTop, 80);
        var explicitResolved = Resolve(explicitPane, explicitTop, 80);

        Assert.Equal(implicitResolved.Children[0].OuterWidth, explicitResolved.Children[0].OuterWidth);
        Assert.Equal(implicitResolved.Children[1].OuterWidth, explicitResolved.Children[1].OuterWidth);
    }

    [Fact]
    public void Even_ContentPaneGetsItsShareRegardlessOfItsItems()
    {
        const string oneItemJson = """
        {
          "colors": { "model-accent": { "default": "blue" } },
          "surface": {
            "maxRows": 8,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "children": [
                { "size": "content", "overflow": "wrap",
                  "border": { "enabled": false, "color": "grey" },
                  "items": [ { "item": "model", "color": "@model-accent" } ] },
                { "size": "fill", "overflow": "wrap",
                  "border": { "enabled": false, "color": "grey" } }
              ]
            }
          }
        }
        """;
        const string fourItemJson = """
        {
          "colors": { "model-accent": { "default": "blue" } },
          "surface": {
            "maxRows": 8,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "distribute": "even",
              "children": [
                { "size": "content", "overflow": "wrap",
                  "border": { "enabled": false, "color": "grey" },
                  "items": [ { "item": "model", "color": "@model-accent" },
                             { "item": "effort" }, { "item": "thinking" }, { "item": "context" } ] },
                { "size": "fill", "overflow": "wrap",
                  "border": { "enabled": false, "color": "grey" } }
              ]
            }
          }
        }
        """;

        var (oneItemTop, oneItemPane) = LoadConfig(oneItemJson);
        var (fourItemTop, fourItemPane) = LoadConfig(fourItemJson);

        // BoundaryCost = gutter(1) * (2-1) = 1; 61 - 1 = 60, split evenly two ways -> 30 each,
        // regardless of the wildly different item lists on the content pane.
        var oneItemResolved = Resolve(oneItemPane, oneItemTop, 61);
        var fourItemResolved = Resolve(fourItemPane, fourItemTop, 61);

        Assert.Equal(30, oneItemResolved.Children[0].OuterWidth);
        Assert.Equal(30, oneItemResolved.Children[1].OuterWidth);
        Assert.Equal(30, fourItemResolved.Children[0].OuterWidth);
        Assert.Equal(30, fourItemResolved.Children[1].OuterWidth);
        Assert.Equal(oneItemResolved.Children[0].OuterWidth, fourItemResolved.Children[0].OuterWidth);
    }
}
