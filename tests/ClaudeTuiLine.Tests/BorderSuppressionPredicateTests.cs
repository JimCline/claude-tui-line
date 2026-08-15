using Xunit.Abstractions;

namespace ClaudeTuiLine.Tests;

// SPEC-2.3-suppression-predicate.md §4: ShouldSuppressBorder now tests the pane's own
// PRE-suppression inner width (grant minus its own border reserve) rather than outer width — the
// 4-column disagreement band (§1) that #71's DropFloor made load-bearing (§2) is closed. Every
// expected value below is derived from the spec's own arithmetic (MinUsableWidth=20,
// OwnBorderReserve = 2 + left-edge + right-edge), not observed by running this change's
// implementation, per §8 item 4's explicit requirement.
public class BorderSuppressionPredicateTests
{
    private static readonly StatusInput Input = new()
    {
        Model = new ModelInfo { DisplayName = "Claude Opus 4.5" },
        Effort = new EffortInfo { Level = "high" },
        Thinking = new ThinkingInfo { Enabled = true },
        ContextWindow = new ContextWindowInfo { UsedPercentage = 42 },
    };

    private static readonly ItemContext Ctx = new(Input, gitBranch: null, engram: null, remoteUrlProbe: () => null);

    private readonly ITestOutputHelper _output;

    public BorderSuppressionPredicateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) LoadConfig(string configJson)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, configJson);
        try
        {
            return ConfigLoader.LoadAll(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // §8 item 1 (the headline test): a bordered fill pane, full edges (reserve 4), granted 22 in a
    // 32-column split (fixed sibling takes 10, gutter 0). Pre-#73 this pane was dropped outright
    // (outer-width predicate: 22 >= 20, so suppression never fired; unsuppressed floor 24 > 22).
    // Post-#73: pre-suppression inner width is 22 - 4 = 18 < 20, so suppression fires and the
    // floor drops to 20; 22 >= 20 survives.
    [Fact]
    public void Item1_Band20To23_SurvivesAndPredicateAgreesSuppressionFires()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill" },
                { "size": "10", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 32, Ctx, values, notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(22, resolved.Children[0].OuterWidth);
        Assert.Empty(notes.Notes);

        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);
        Assert.Equal(4, reserve);
        Assert.True(SizeResolver.ShouldSuppressBorder(fillPane, 22 - reserve),
            "grant 22's pre-suppression inner width (18) is under MinUsableWidth, so the " +
            "predicate that let this pane survive must independently agree it suppresses");

        // SPEC-2.3-suppression-predicate.md §6/N1: whether the RENDERED content actually then
        // occupies the reclaimed inner width 22 (vs. staying laid out at 18, with the 4 freed
        // columns spent on blank padding instead) is a second, independent defect the spec poses
        // as NEEDS-EVIDENCE. I read PaneBorderRenderer.Wrap and confirmed the reservation is not
        // reclaimed today — and its own doc comment states that is deliberate ("keeps the same
        // reserved geometry ... one code path for both cases, not a separate borderless layout"),
        // not an oversight. Reversing a documented design choice is outside this predicate fix's
        // scope; not asserted here, reported upward instead (see task report).
    }

    // §8 item 2: same pane, granted 19. Pre-suppression inner width 15 < 20 still suppresses, and
    // the post-suppression floor (20) still exceeds the grant — genuinely too narrow, drops with
    // the below-floor note. Pins that row 1 of the spec's own table (§2) is unchanged by this fix.
    // The fixed sibling is listed first here (unlike items 1/3): the drop-retry loop always drops
    // whichever pane is LAST in its current list regardless of which one actually failed its
    // floor check (see FloorSumExceedsBudget_Greedy et al.), so the fill pane must be last for
    // its own grant/floor to appear in the note.
    [Fact]
    public void Item2_Below20_StillDrops()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "13", "border": { "enabled": false } },
                { "size": "fill" }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 32, Ctx, values, notes);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Single(resolved.Children);
        Assert.Contains(notes.Notes, n => n.Message == "pane 2 dropped: 19 columns is under its 20-column floor at 32 columns");
    }


    // §8 item 3: same pane, granted 24 (its full unsuppressed floor: MinUsableWidth 20 + reserve
    // 4). Pre-suppression inner width is exactly 20 — not under it — so suppression does not
    // fire and the pane keeps its border. The boundary this fix must not move.
    [Fact]
    public void Item3_AtFloor_KeepsBorder()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill" },
                { "size": "8", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 32, Ctx, values, notes);

        Assert.Equal(2, resolved.Children.Count);
        Assert.Equal(24, resolved.Children[0].OuterWidth);
        Assert.Empty(notes.Notes);

        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);
        Assert.False(SizeResolver.ShouldSuppressBorder(fillPane, 24 - reserve),
            "grant 24's pre-suppression inner width (20) is not under MinUsableWidth");
    }

    // §8 item 5: a pane with edges {left:false, right:false} has reserve 2, not 4, so its own
    // suppression band is one column narrower and starts one column earlier than a fully-edged
    // pane's. Drop/survive outcomes cannot discriminate a reserve regression here (the suppressed
    // floor is the constant MinUsableWidth regardless of reserve, so any grant that clears the
    // true, larger unsuppressed floor also clears a wrongly-computed smaller one) — the
    // discriminating check is OwnBorderReserve's own return value, fed into ShouldSuppressBorder
    // rather than a hardcoded 4, so a regression to "24" or "4" here fails this test either by
    // making the reserve assertion itself wrong, or by moving the boundary the two assertions
    // below pin.
    [Fact]
    public void Item5_ReserveVariant_BoundaryMovesWithReserve()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 0,
              "children": [
                { "size": "fill", "border": { "edges": { "left": false, "right": false } } },
                { "size": "10", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var fillPane = pane.Children[0];
        var reserve = SizeResolver.OwnBorderReserve(fillPane);

        Assert.Equal(2, reserve);
        Assert.False(SizeResolver.ShouldSuppressBorder(fillPane, 22 - reserve),
            "reserve 2: grant 22's inner width (20) is not under MinUsableWidth");
        Assert.True(SizeResolver.ShouldSuppressBorder(fillPane, 21 - reserve),
            "reserve 2: grant 21's inner width (19) is under MinUsableWidth, one column earlier " +
            "than a fully-edged (reserve 4) pane's own band");
    }

    // §8 item 6: SPEC-2.3-suppression-predicate.md §4's "collapse mismatch" — before this fix,
    // DropFloor's suppression check had no excludeLeft/excludeRight while Floor/DropFloor's own
    // floor computation did, so under collapse:true the allocator could reason about a pane with
    // edge-excluded reserve while the suppression check reasoned about one without. A 3-child
    // collapse:true vertical split with two fixed outer panes (exempt from the tooSmall check,
    // per #67a) isolates the middle fill child: its own excludeLeft/excludeRight are both true
    // (it faces a shared boundary on each side), so its reserve is 2 (padding only, no verticals)
    // rather than the 4 a non-excluded read would wrongly use. Granted 21, its unsuppressed floor
    // (with the correct exclude-aware reserve) is 22 — 21 doesn't clear it — but its
    // pre-suppression inner width (21 - 2 = 19) is under MinUsableWidth, so suppression fires and
    // it survives at the lower floor (20). Before this fix, DropFloor's suppression check would
    // have tested grant (21) directly against outer-width's own bar (20) and also not fired
    // (21 >= 20), landing on the *unsuppressed*, exclude-aware floor of 22 — 21 < 22 would have
    // dropped it. Surviving here is exactly the allocator and the predicate agreeing.
    [Fact]
    public void Item6_Collapse_AllocatorAndPredicateAgreeOnExcludeAwareReserve()
    {
        const string configJson = """
        {
          "surface": {
            "maxRows": 20,
            "pane": {
              "split": "vertical",
              "gutter": 1,
              "children": [
                { "size": "5", "border": { "enabled": false } },
                { "size": "fill" },
                { "size": "5", "border": { "enabled": false } }
              ]
            }
          }
        }
        """;

        var (_, pane) = LoadConfig(configJson);
        var values = ItemValueResolver.Resolve(pane, Ctx, new Dictionary<string, ColorResolution.ColorRule>());
        var notes = new RenderNoteCollector();

        var resolved = SizeResolver.Resolve(pane, 33, Ctx, values, notes, collapse: true);

        _output.WriteLine(string.Join(Environment.NewLine, notes.Notes.Select(n => n.Message)));

        Assert.Equal(3, resolved.Children.Count);
        Assert.Equal(21, resolved.Children[1].OuterWidth);
        Assert.Empty(notes.Notes);

        var middlePane = pane.Children[1];
        var excludeAwareReserve = SizeResolver.OwnBorderReserve(middlePane, excludeLeft: true, excludeRight: true);
        Assert.Equal(2, excludeAwareReserve);
        Assert.True(SizeResolver.ShouldSuppressBorder(middlePane, 21 - excludeAwareReserve),
            "grant 21's exclude-aware pre-suppression inner width (19) is under MinUsableWidth");
    }
}
