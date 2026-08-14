namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.2.2: formats the one diagnostic row the render path draws when an
/// asserted config could not be read — a degradation ladder of five rungs, each tried only when
/// the one above it does not fit the available width. The prefix is always the literal string
/// "claude-tui-line", never argv[0], and the ellipsis is always the built-in default, never a
/// configured one, since the settings that would supply either live in the file that failed.
/// </summary>
public static class ConfigUnreadableMessage
{
    private const string Prefix = "claude-tui-line: ";
    private const string BareName = "claude-tui-line";

    public static string Format(string path, string reason, int? width)
    {
        var full = Prefix + path + ": " + reason;
        if (Fits(full.Length, width))
        {
            return full;
        }

        var pathBudget = width!.Value - Prefix.Length - ": ".Length - reason.Length;
        var elidedPath = ElidePath(path, pathBudget);
        if (elidedPath is not null)
        {
            return Prefix + elidedPath + ": " + reason;
        }

        var pathless = Prefix + reason;
        if (Fits(pathless.Length, width))
        {
            return pathless;
        }

        var reasonBudget = width.Value - Prefix.Length;
        if (reasonBudget >= 1)
        {
            return Prefix + TruncateWithEllipsis(reason, reasonBudget);
        }

        var bareBudget = Math.Clamp(width.Value, 0, BareName.Length);
        return BareName[..bareBudget];
    }

    private static bool Fits(int length, int? width) => width is null || length <= width.Value;

    // §9.2.2 rung 2: elides the middle of the path to exactly the given budget, keeping the
    // leading '/' or '~' and the file name intact — the two ends a reader needs to recognize
    // which file this is. Returns null when the budget is too narrow to keep both.
    private static string? ElidePath(string path, int budget)
    {
        var fileName = Path.GetFileName(path);
        var minimum = 1 + ConfigLoader.DefaultEllipsis.Length + fileName.Length;
        if (budget < minimum)
        {
            return null;
        }

        var headLength = budget - ConfigLoader.DefaultEllipsis.Length - fileName.Length;
        return path[..headLength] + ConfigLoader.DefaultEllipsis + fileName;
    }

    // Mirrors PaneRenderer.TruncateSegment's policy: too narrow for the marker at all clips the
    // real content instead of appending an ellipsis that would consume the entire budget itself.
    private static string TruncateWithEllipsis(string text, int budget)
    {
        if (budget <= ConfigLoader.DefaultEllipsis.Length)
        {
            return text[..Math.Min(budget, text.Length)];
        }

        var contentBudget = budget - ConfigLoader.DefaultEllipsis.Length;
        return text[..Math.Min(contentBudget, text.Length)] + ConfigLoader.DefaultEllipsis;
    }
}
