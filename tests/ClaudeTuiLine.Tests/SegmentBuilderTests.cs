using ClaudeTuiLine;
using Spectre.Console;

namespace ClaudeTuiLine.Tests;

public class SegmentBuilderTests
{
    private static string Expect(string tag, string plain) => $"[{tag}]{Markup.Escape(plain)}[/]";

    private static StatusInput Empty() => new();

    private static ItemContext Ctx(StatusInput input, string? gitBranch = null, EngramResult? engram = null) =>
        new(input, gitBranch, engram, remoteUrlProbe: () => null);

    // --- Segment 1: Directory ---

    [Fact]
    public void Directory_Present_UsesBasenameInTeal()
    {
        var input = Empty();
        input.Cwd = "/Users/jimcline/git/repos/claude-tui-line";

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("claude-tui-line", seg.Plain);
        Assert.Equal(Expect("teal", "claude-tui-line"), seg.Markup);
    }

    [Fact]
    public void Directory_Absent_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));
        Assert.Empty(segments);
    }

    [Fact]
    public void Directory_HostileBasename_IsMarkupEscaped()
    {
        var input = Empty();
        input.Cwd = "/tmp/[hacked]";

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("[hacked]", seg.Plain);
        Assert.Equal(Expect("teal", "[hacked]"), seg.Markup); // brackets doubled ([[hacked]]), not passed through raw
    }

    // --- Segment 2: Git branch ---

    [Fact]
    public void GitBranch_Present_Green()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), gitBranch: "main"));

        var seg = Assert.Single(segments);
        Assert.Equal("main", seg.Plain);
        Assert.Equal(Expect("green", "main"), seg.Markup);
    }

    [Fact]
    public void GitBranch_HostileName_IsMarkupEscaped()
    {
        const string hostile = "[red]x[/]";
        var segments = SegmentBuilder.Build(Ctx(Empty(), gitBranch: hostile));

        var seg = Assert.Single(segments);
        Assert.Equal(hostile, seg.Plain);
        Assert.Equal(Expect("green", hostile), seg.Markup);
    }

    [Fact]
    public void GitBranch_Empty_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), gitBranch: ""));
        Assert.Empty(segments);
    }

    // --- Segment 3: GitHub repo ---

    [Fact]
    public void Repo_Present_DimOwnerSlashName()
    {
        var input = Empty();
        input.Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "jimcline", Name = "claude-tui-line" } };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("jimcline/claude-tui-line", seg.Plain);
        Assert.Equal(Expect("dim", "jimcline/claude-tui-line"), seg.Markup);
    }

    [Fact]
    public void Repo_MissingOwner_SuppressesSegment()
    {
        var input = Empty();
        input.Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = null, Name = "claude-tui-line" } };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(segments);
    }

    // --- Segment 4: Worktree ---

    [Fact]
    public void Worktree_NameOnly()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("worktree:feature-x", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x"), seg.Markup);
    }

    [Fact]
    public void Worktree_NameAndBranch()
    {
        var input = Empty();
        input.Worktree = new WorktreeInfo { Name = "feature-x", Branch = "main" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("worktree:feature-x(main)", seg.Plain);
        Assert.Equal(Expect("purple", "worktree:feature-x(main)"), seg.Markup);
    }

    // --- Segment 5: PR ---

    [Theory]
    [InlineData("approved", "PR #42 [approved]")]
    [InlineData("changes_requested", "PR #42 [changes]")]
    [InlineData("draft", "PR #42 [draft]")]
    [InlineData("merged", "PR #42 [merged]")] // unknown state falls through to the raw state
    [InlineData(null, "PR #42")]
    public void PullRequest_StateMapping(string? reviewState, string expectedPlain)
    {
        var input = Empty();
        input.Pr = new PrInfo { Number = 42, ReviewState = reviewState };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal(expectedPlain, seg.Plain);
        Assert.Equal(Expect("olive", expectedPlain), seg.Markup);
    }

    [Fact]
    public void PullRequest_NumberAbsent_NoSegment()
    {
        var input = Empty();
        input.Pr = new PrInfo { ReviewState = "approved" };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(segments);
    }

    // --- Segment 6: Model ---

    [Fact]
    public void Model_Present_Navy()
    {
        var input = Empty();
        input.Model = new ModelInfo { DisplayName = "Claude Opus 4.5" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("Claude Opus 4.5", seg.Plain);
        Assert.Equal(Expect("navy", "Claude Opus 4.5"), seg.Markup);
    }

    // --- Segment 7: Effort ---

    [Fact]
    public void Effort_Present_Dim()
    {
        var input = Empty();
        input.Effort = new EffortInfo { Level = "high" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("effort:high", seg.Plain);
        Assert.Equal(Expect("dim", "effort:high"), seg.Markup);
    }

    // --- Segment 8: Thinking ---

    [Fact]
    public void Thinking_EnabledTrue_ShowsPurple()
    {
        var input = Empty();
        input.Thinking = new ThinkingInfo { Enabled = true };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("thinking", seg.Plain);
        Assert.Equal(Expect("purple", "thinking"), seg.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Thinking_NotTrue_NoSegment(bool? enabled)
    {
        var input = Empty();
        input.Thinking = new ThinkingInfo { Enabled = enabled };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(segments);
    }

    // --- Segment 9: Output style ---

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData("DEFAULT")]
    public void OutputStyle_DefaultCaseInsensitive_Suppressed(string name)
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = name };

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(segments);
    }

    [Fact]
    public void OutputStyle_NonDefault_ShowsDim()
    {
        var input = Empty();
        input.OutputStyle = new OutputStyleInfo { Name = "concise" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("style:concise", seg.Plain);
        Assert.Equal(Expect("dim", "style:concise"), seg.Markup);
    }

    // --- Segment 10: Context (threshold boundaries + token counts) ---

    [Theory]
    [InlineData(49.0, "green")]
    [InlineData(50.0, "olive")]
    [InlineData(79.0, "olive")]
    [InlineData(80.0, "maroon")]
    public void Context_ThresholdBoundaries_NoTokenCounts(double usedPercentage, string expectedTag)
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo { UsedPercentage = usedPercentage };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        var pct = (int)usedPercentage;
        Assert.Equal($"ctx:{pct}%", seg.Plain);
        Assert.Equal($"ctx:[{expectedTag}]{pct}%[/]", seg.Markup);
    }

    [Fact]
    public void Context_WithTokenCounts_AppendsDimUsage()
    {
        var input = Empty();
        input.ContextWindow = new ContextWindowInfo
        {
            UsedPercentage = 62.0,
            TotalInputTokens = 125000,
            ContextWindowSize = 200000,
        };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("ctx:62% (125k/200k)", seg.Plain);
        Assert.Equal("ctx:[olive]62%[/] [dim](125k/200k)[/]", seg.Markup);
    }

    [Fact]
    public void Context_Absent_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));
        Assert.Empty(segments);
    }

    // --- Segment 11: Rate limits ---

    [Theory]
    [InlineData(49.0, "green")]
    [InlineData(50.0, "olive")]
    [InlineData(79.0, "olive")]
    [InlineData(80.0, "maroon")]
    public void RateLimits_FiveHourOnly_ThresholdBoundaries(double pct, string expectedTag)
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo { FiveHour = new RateWindowInfo { UsedPercentage = pct } };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        var v = (int)pct;
        Assert.Equal($"5h:{v}%", seg.Plain);
        Assert.Equal($"5h:[{expectedTag}]{v}%[/]", seg.Markup);
    }

    [Fact]
    public void RateLimits_BothWindows_JoinedWithDimSlash()
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo
        {
            FiveHour = new RateWindowInfo { UsedPercentage = 30.0 },
            SevenDay = new RateWindowInfo { UsedPercentage = 85.0 },
        };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("5h:30% / 7d:85%", seg.Plain);
        Assert.Equal("5h:[green]30%[/] [dim]/[/] 7d:[maroon]85%[/]", seg.Markup);
    }

    [Fact]
    public void RateLimits_SevenDayOnly()
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo { SevenDay = new RateWindowInfo { UsedPercentage = 60.0 } };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("7d:60%", seg.Plain);
        Assert.Equal("7d:[olive]60%[/]", seg.Markup);
    }

    [Fact]
    public void RateLimits_NeitherWindow_NoSegment()
    {
        var input = Empty();
        input.RateLimits = new RateLimitsInfo();

        var segments = SegmentBuilder.Build(Ctx(input));
        Assert.Empty(segments);
    }

    // --- Segment 12: Agent ---

    [Fact]
    public void Agent_Present_Purple()
    {
        var input = Empty();
        input.Agent = new AgentInfo { Name = "implementor" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("agent:implementor", seg.Plain);
        Assert.Equal(Expect("purple", "agent:implementor"), seg.Markup);
    }

    // --- Segment 13: Engram ---

    [Fact]
    public void Engram_FactsOnly()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), engram: new EngramResult(42, null)));

        var seg = Assert.Single(segments);
        Assert.Equal("engram:42", seg.Plain);
        Assert.Equal("[dim]engram:42[/]", seg.Markup);
    }

    [Fact]
    public void Engram_VerbOnly()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), engram: new EngramResult(null, "✱ captured")));

        var seg = Assert.Single(segments);
        Assert.Equal("✱ captured", seg.Plain);
        Assert.Equal(Expect("purple", "✱ captured"), seg.Markup);
    }

    [Fact]
    public void Engram_FactsAndVerb_Combined()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty(), engram: new EngramResult(7, "◉ recalled")));

        var seg = Assert.Single(segments);
        Assert.Equal("engram:7 ◉ recalled", seg.Plain);
        Assert.Equal("[dim]engram:7[/] [purple]◉ recalled[/]", seg.Markup);
    }

    [Fact]
    public void Engram_Null_NoSegment()
    {
        var segments = SegmentBuilder.Build(Ctx(Empty()));
        Assert.Empty(segments);
    }

    // --- Segment 14: Vim mode ---

    [Fact]
    public void VimMode_Present_OliveBracketed()
    {
        var input = Empty();
        input.Vim = new VimInfo { Mode = "NORMAL" };

        var segments = SegmentBuilder.Build(Ctx(input));

        var seg = Assert.Single(segments);
        Assert.Equal("[NORMAL]", seg.Plain);
        Assert.Equal(Expect("olive", "[NORMAL]"), seg.Markup);
    }

    // --- Ordering across all 14 ---

    [Fact]
    public void AllSegmentsPresent_PreserveCaptureOrder()
    {
        var input = new StatusInput
        {
            Cwd = "/tmp/proj",
            Workspace = new WorkspaceInfo { Repo = new RepoInfo { Owner = "o", Name = "n" } },
            Worktree = new WorktreeInfo { Name = "wt" },
            Pr = new PrInfo { Number = 1 },
            Model = new ModelInfo { DisplayName = "M" },
            Effort = new EffortInfo { Level = "low" },
            Thinking = new ThinkingInfo { Enabled = true },
            OutputStyle = new OutputStyleInfo { Name = "custom" },
            ContextWindow = new ContextWindowInfo { UsedPercentage = 10 },
            RateLimits = new RateLimitsInfo { FiveHour = new RateWindowInfo { UsedPercentage = 10 } },
            Agent = new AgentInfo { Name = "a" },
        };

        var segments = SegmentBuilder.Build(Ctx(input, gitBranch: "main", engram: new EngramResult(1, "verb")));

        Assert.Equal(13, segments.Count); // all but vim mode (not set on this fixture)
        Assert.Equal("proj", segments[0].Plain);          // directory
        Assert.Equal("main", segments[1].Plain);           // git branch
        Assert.Equal("o/n", segments[2].Plain);             // repo
        Assert.Equal("worktree:wt", segments[3].Plain);     // worktree
        Assert.Equal("PR #1", segments[4].Plain);           // pr
        Assert.Equal("M", segments[5].Plain);                // model
        Assert.Equal("effort:low", segments[6].Plain);       // effort
        Assert.Equal("thinking", segments[7].Plain);         // thinking
        Assert.Equal("style:custom", segments[8].Plain);     // output style
        Assert.StartsWith("ctx:", segments[9].Plain);         // context
        Assert.StartsWith("5h:", segments[10].Plain);         // rate limits
        Assert.Equal("agent:a", segments[11].Plain);          // agent
        Assert.StartsWith("engram:1", segments[12].Plain);    // engram
        // no vim mode in this fixture, so 14th slot (vim) is simply absent
    }
}
