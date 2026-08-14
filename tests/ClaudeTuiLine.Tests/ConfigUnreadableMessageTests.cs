using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.2.2: the five-rung degradation ladder for the render path's one
/// diagnostic row. Each rung is exercised at a width chosen to make the rung above it not fit,
/// so a regression in the fitting arithmetic shows up as the wrong rung firing, not just the
/// wrong text.
/// </summary>
public class ConfigUnreadableMessageTests
{
    [Fact]
    public void Rung1_WholeRowFitsExactly()
    {
        const string path = "/Users/x/my.json";
        const string reason = "unexpected ',' at line 12";
        var full = $"claude-tui-line: {path}: {reason}";

        Assert.Equal(full, ConfigUnreadableMessage.Format(path, reason, full.Length));
    }

    [Fact]
    public void Rung1_NullWidth_NeverTruncatesEvenAVeryLongRow()
    {
        const string path = "/Users/x/a-fairly-long-directory-name/deep/nested/path/my-config-file.json";
        const string reason = "unexpected end of input while parsing a JSON object at line 412, column 9001";
        var full = $"claude-tui-line: {path}: {reason}";

        Assert.Equal(full, ConfigUnreadableMessage.Format(path, reason, null));
    }

    [Fact]
    public void Rung2_ElidesPathMiddle_KeepingLeadingSlashAndFileName()
    {
        const string path = "/Users/x/deeply/nested/workspace/config/my.json";
        const string reason = "unexpected ',' at line 12";
        var full = $"claude-tui-line: {path}: {reason}";

        // Narrow enough that the whole path can't survive, wide enough that eliding its middle
        // (keeping the leading '/' and the file name "my.json") still fits.
        var width = full.Length - 20;
        var result = ConfigUnreadableMessage.Format(path, reason, width);

        Assert.Equal(width, result.Length);
        Assert.StartsWith("claude-tui-line: /", result);
        Assert.EndsWith(": " + reason, result);
        Assert.Contains("my.json", result);
        Assert.Contains("…", result);
        Assert.DoesNotContain("deeply/nested/workspace", result);
    }

    [Fact]
    public void Rung3_PathDroppedEntirely_WhenElisionCannotKeepFileNameAndReason()
    {
        const string path = "/Users/x/deeply/nested/workspace/config/my-rather-long-config-file-name.json";
        const string reason = "unexpected ',' at line 12";
        var pathless = $"claude-tui-line: {reason}";

        // Wide enough for the pathless row, but too narrow for even the minimal elided path
        // (leading char + ellipsis + full file name) alongside the reason.
        var width = pathless.Length;
        var result = ConfigUnreadableMessage.Format(path, reason, width);

        Assert.Equal(pathless, result);
    }

    [Fact]
    public void Rung4_ReasonTruncatedWithEllipsis_WhenPathlessRowStillTooLong()
    {
        const string path = "/Users/x/my.json";
        const string reason = "unexpected end of input while parsing a deeply nested JSON object literal";
        var width = 40; // >= 18, but shorter than "claude-tui-line: " + the full reason.

        var result = ConfigUnreadableMessage.Format(path, reason, width);

        Assert.Equal(width, result.Length);
        Assert.StartsWith("claude-tui-line: ", result);
        Assert.EndsWith("…", result);
        Assert.DoesNotContain(path, result);
    }

    [Fact]
    public void Rung5_BareToolNameOnly_BelowEighteenColumns()
    {
        const string path = "/Users/x/my.json";
        const string reason = "unexpected ',' at line 12";

        Assert.Equal("claude-tui-", ConfigUnreadableMessage.Format(path, reason, 11));
        Assert.Equal("claude-tui-line", ConfigUnreadableMessage.Format(path, reason, 17));
    }

    [Fact]
    public void Rung5_ZeroWidth_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, ConfigUnreadableMessage.Format("/Users/x/my.json", "unexpected ',' at line 12", 0));
    }
}
