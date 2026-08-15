namespace ClaudeTuiLineShared;

/// <summary>
/// SPEC-83: config path resolution lives here, not in <c>ClaudeTuiLine.ConfigLoader</c>, so that
/// both the AOT/self-contained CLI and the framework-dependent MCP server can share one
/// implementation without the MCP publish tripping NETSDK1151. SPEC-12.6-mcp-tools.md §7.1's
/// argument stands unchanged: a second copy of this rule would let the server validate one file
/// and write another, with every tool reporting success.
/// </summary>
public static class ConfigPath
{
    /// <summary>
    /// SPEC.md §6b "Config path resolution": <paramref name="configPathOverride"/>, when set and
    /// non-empty, is the config file path. Otherwise <c>$HOME/.claude/claude-tui-line.json</c>
    /// (or null if <paramref name="home"/> is itself unset/empty).
    /// </summary>
    public static string? ResolveConfigPath(string? configPathOverride, string? home)
    {
        if (!string.IsNullOrEmpty(configPathOverride))
        {
            return configPathOverride;
        }

        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".claude", "claude-tui-line.json");
    }

    public static string? ResolveConfigPath() =>
        ResolveConfigPath(
            Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG"),
            Environment.GetEnvironmentVariable("HOME"));
}
