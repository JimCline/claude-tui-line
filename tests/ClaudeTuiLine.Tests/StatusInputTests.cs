using System.Text.Json;
using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

public class StatusInputTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    [Fact]
    public void FullFixture_DeserializesEveryField()
    {
        var json = ReadFixture("full.json");
        var input = JsonSerializer.Deserialize(json, StatusInputJsonContext.Default.StatusInput);

        Assert.NotNull(input);
        Assert.Equal("/Users/example/git/repos/claude-tui-line", input!.Cwd);
        Assert.Equal("jimcline", input.Workspace?.Repo?.Owner);
        Assert.Equal("claude-tui-line", input.Workspace?.Repo?.Name);
        Assert.Equal("feature-x", input.Worktree?.Name);
        Assert.Equal("main", input.Worktree?.Branch);
        Assert.Equal(42, input.Pr?.Number);
        Assert.Equal("approved", input.Pr?.ReviewState);
        Assert.Equal("Claude Opus 4.5", input.Model?.DisplayName);
        Assert.Equal("high", input.Effort?.Level);
        Assert.True(input.Thinking?.Enabled);
        Assert.Equal("concise", input.OutputStyle?.Name);
        Assert.Equal(62.5, input.ContextWindow?.UsedPercentage);
        Assert.Equal(125000, input.ContextWindow?.TotalInputTokens);
        Assert.Equal(200000, input.ContextWindow?.ContextWindowSize);
        Assert.Equal(30.0, input.RateLimits?.FiveHour?.UsedPercentage);
        Assert.Equal(85.0, input.RateLimits?.SevenDay?.UsedPercentage);
        Assert.Equal("implementor", input.Agent?.Name);
        Assert.Equal("NORMAL", input.Vim?.Mode);
        Assert.Equal("session-abc123", input.SessionId);
    }

    [Fact]
    public void EmptyObject_AllFieldsAbsent()
    {
        var json = ReadFixture("empty.json");
        var input = JsonSerializer.Deserialize(json, StatusInputJsonContext.Default.StatusInput);

        Assert.NotNull(input);
        Assert.Null(input!.Cwd);
        Assert.Null(input.Workspace);
        Assert.Null(input.Worktree);
        Assert.Null(input.Pr);
        Assert.Null(input.Model);
        Assert.Null(input.Effort);
        Assert.Null(input.Thinking);
        Assert.Null(input.OutputStyle);
        Assert.Null(input.ContextWindow);
        Assert.Null(input.RateLimits);
        Assert.Null(input.Agent);
        Assert.Null(input.Vim);
        Assert.Null(input.SessionId);
    }

    [Fact]
    public void InvalidJson_ThrowsJsonException()
    {
        var json = ReadFixture("invalid.json");

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, StatusInputJsonContext.Default.StatusInput));
    }

    [Fact]
    public void UnknownFields_AreIgnoredWithoutThrowing()
    {
        var json = ReadFixture("unknown_fields.json");
        var input = JsonSerializer.Deserialize(json, StatusInputJsonContext.Default.StatusInput);

        Assert.NotNull(input);
        Assert.Equal("/tmp", input!.Cwd);
    }
}
