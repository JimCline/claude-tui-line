namespace ClaudeTuiLineMcp.Tests;

/// <summary>
/// Stands up a fake "claude-tui-line" CLI binary under a temp CLAUDE_PLUGIN_DATA directory, so
/// tests can exercise CliRunner's spawn path without touching the real compiled CLI. The fake
/// answers `--items --json` with a trivial success payload (the presence probe) and `--check
/// --config &lt;path&gt; --json` by inspecting the candidate file: a config file containing the literal
/// marker "FORCE_INVALID" is reported invalid, everything else reported valid.
///
/// Also saves/restores CLAUDE_PLUGIN_DATA, HOME and CLAUDE_TUI_LINE_CONFIG so tests never leak
/// environment state into one another (the whole assembly runs serialized — see AssemblyInfo.cs).
/// </summary>
public sealed class TestCliFixture : IDisposable
{
    private readonly string? _savedPluginData;
    private readonly string? _savedHome;
    private readonly string? _savedConfigOverride;

    public string TempRoot { get; }
    public string HomeDir { get; }
    public string BinPath { get; }

    public TestCliFixture()
    {
        _savedPluginData = Environment.GetEnvironmentVariable("CLAUDE_PLUGIN_DATA");
        _savedHome = Environment.GetEnvironmentVariable("HOME");
        _savedConfigOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");

        TempRoot = Directory.CreateTempSubdirectory("claude-tui-line-mcp-test-").FullName;
        HomeDir = Path.Combine(TempRoot, "home");
        Directory.CreateDirectory(HomeDir);

        var binDir = Path.Combine(TempRoot, "plugin-data", "bin");
        Directory.CreateDirectory(binDir);
        BinPath = Path.Combine(binDir, "claude-tui-line");

        File.WriteAllText(BinPath, FakeCliScript);
        File.SetUnixFileMode(
            BinPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Environment.SetEnvironmentVariable("CLAUDE_PLUGIN_DATA", Path.Combine(TempRoot, "plugin-data"));
        Environment.SetEnvironmentVariable("HOME", HomeDir);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", null);
    }

    /// <summary>Points CLAUDE_PLUGIN_DATA and HOME at a bin-less directory, so CliLocator finds nothing.</summary>
    public void RemoveCli()
    {
        File.Delete(BinPath);
    }

    private const string FakeCliScript = """
        #!/bin/sh
        if [ "$1" = "--items" ]; then
          echo '{"items":[],"kinds":{}}'
          exit 0
        fi
        if [ "$1" = "--check" ]; then
          candidate="$3"
          if grep -q "FORCE_INVALID" "$candidate" 2>/dev/null; then
            echo '{"ok":false,"diagnostics":[{"path":"/surface","severity":"error","code":"forced-invalid","message":"forced invalid for testing"}]}'
            exit 1
          fi
          echo '{"ok":true,"diagnostics":[]}'
          exit 0
        fi
        exit 0
        """;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_PLUGIN_DATA", _savedPluginData);
        Environment.SetEnvironmentVariable("HOME", _savedHome);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _savedConfigOverride);

        try
        {
            Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup; a leftover temp dir under the OS temp root is not user data.
        }
    }
}
