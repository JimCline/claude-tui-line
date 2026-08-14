namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4.1 (defect 14): <c>shell: true</c> only ever forwards
/// <c>command[0]</c> to <c>sh -c</c>, so an argv of more than one element under
/// <c>shell: true</c> would silently drop every element after the first and run the wrong
/// command. <see cref="CommandProvider"/> suppresses that item instead of spawning it, while
/// a single-element argv under <c>shell: true</c> — what the string form of <c>command</c>
/// normalizes to — and any argv under <c>shell: false</c> resolve normally.
/// </summary>
public class CommandProviderTests
{
    [Fact]
    public async Task ShellTrueWithMultiElementCommand_ResolvesToNull()
    {
        var item = new PaneItem(null, null, null, null, Id: "defect14-shell-multi", Command: new[] { "echo", "hi" }, Shell: true);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), paneWidthEligible: false);

        Assert.Null(value.Value);
    }

    [Fact]
    public async Task ShellTrueWithSingleElementCommand_ResolvesNormally()
    {
        var item = new PaneItem(null, null, null, null, Id: "defect14-shell-single", Command: new[] { "echo hi" }, Shell: true);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), paneWidthEligible: false);

        Assert.Equal("hi", value.Value);
    }

    [Fact]
    public async Task ShellFalseWithMultiElementCommand_ResolvesNormally()
    {
        var item = new PaneItem(null, null, null, null, Id: "defect14-noshell-multi", Command: new[] { "echo", "hi" }, Shell: false);

        var value = await CommandProvider.ResolveAsync(
            item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), paneWidthEligible: false);

        Assert.Equal("hi", value.Value);
    }
}
