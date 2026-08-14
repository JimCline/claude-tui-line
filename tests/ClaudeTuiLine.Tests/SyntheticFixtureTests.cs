using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.3.1: "The type permits any combination, so the agreement is asserted by
/// test, not by this paragraph and not by the comment in SyntheticFixture.cs." Three redundant-field
/// pairs in the fixture must independently agree with each other.
/// </summary>
public class SyntheticFixtureTests
{
    [Fact]
    public void UsedPercentage_AgreesWith_TotalInputTokensOverContextWindowSize()
    {
        var contextWindow = SyntheticFixture.Input.ContextWindow!;
        var expected = 100.0 * contextWindow.TotalInputTokens!.Value / contextWindow.ContextWindowSize!.Value;

        Assert.Equal(expected, contextWindow.UsedPercentage!.Value);
    }

    [Fact]
    public void CannedGitBranch_AgreesWith_WorktreeBranch()
    {
        var ctx = SyntheticFixture.CreateItemContext();

        Assert.Equal(SyntheticFixture.Input.Worktree!.Branch, ctx.GitBranch);
    }

    [Fact]
    public void CannedRemoteUrl_AgreesWith_WorkspaceRepoAndWorktreeName()
    {
        var ctx = SyntheticFixture.CreateItemContext();
        var repo = SyntheticFixture.Input.Workspace!.Repo!;

        Assert.Equal($"https://github.com/{repo.Owner}/{repo.Name}", ctx.RemoteUrl);
        Assert.Equal(repo.Name, SyntheticFixture.Input.Worktree!.Name);
    }
}
