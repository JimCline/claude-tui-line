using System.Diagnostics;
using System.Text.Json;

namespace ClaudeTuiLine.Tests;

// SPEC-V2-FRAMEWORK.md §9.4/§9.3: RunPreview (Program.cs) has no public entry point reflection can
// address (see PreviewJsonRowsTests.cs's header comment for why), so — same as that file — these
// tests exercise the built CLI as a subprocess. PreviewJsonRowsTests.cs only covers the --json
// happy path against a valid/empty config with synthetic stdin. This file closes the remaining
// RunPreview paths that had no test entry point at all: the config-unreadable exit-3 gate (both
// --json and bare, both parse-error and missing-file), the bare (non-JSON) render+diagnostics
// output, and the --columns/COLUMNS/default-100 fallback chain.
public class PreviewCliTests
{
    [Fact]
    public void Preview_ConfigParseError_Json_ReturnsExit3WithConfigUnreadablePayload()
    {
        var configPath = WriteTempConfig("{ this is not valid json");
        try
        {
            var (exitCode, stdout, stderr) = RunCli("--preview", "--json", "--config", configPath);

            Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stderr: {stderr}");
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("config-unreadable", root.GetProperty("code").GetString());
            Assert.Equal(configPath, root.GetProperty("path").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ConfigParseError_Bare_ReturnsExit3WithStderrMessage()
    {
        var configPath = WriteTempConfig("{ this is not valid json");
        try
        {
            var (exitCode, stdout, stderr) = RunCli("--preview", "--config", configPath);

            Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stdout: {stdout}");
            Assert.Empty(stdout);
            Assert.Contains(configPath, stderr);
            Assert.StartsWith("claude-tui-line:", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ExplicitConfigMissingFile_Json_ReturnsExit3WithNoSuchFileMessage()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"preview-cli-missing-{Guid.NewGuid():N}.json");

        var (exitCode, stdout, stderr) = RunCli("--preview", "--json", "--config", configPath);

        Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stderr: {stderr}");
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("config-unreadable", root.GetProperty("code").GetString());
        Assert.Equal(configPath, root.GetProperty("path").GetString());
        Assert.Contains("no such file", root.GetProperty("message").GetString());
    }

    [Fact]
    public void Preview_ExplicitConfigMissingFile_Bare_ReturnsExit3WithStderrMessage()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"preview-cli-missing-{Guid.NewGuid():N}.json");

        var (exitCode, stdout, stderr) = RunCli("--preview", "--config", configPath);

        Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stdout: {stdout}");
        Assert.Empty(stdout);
        Assert.Contains(configPath, stderr);
        Assert.Contains("no such file", stderr);
    }

    [Fact]
    public void Preview_Bare_WritesRenderedRowsToStdoutAndDiagnosticsToStderr()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, stdout, stderr) = RunCli("--preview", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.False(string.IsNullOrWhiteSpace(stdout));
            Assert.Contains("preview at 60 columns (from --columns)", stderr);
            Assert.Contains("no stdin given", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ColumnsOmitted_FallsBackToColumnsEnvVarWhenSet()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, _, stderr) = RunCli(new Dictionary<string, string?> { ["COLUMNS"] = "45" }, "--preview", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("preview at 45 columns (from COLUMNS)", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_ColumnsOmitted_FallsBackToDefaultOf100WhenNoColumnsEnvVar()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, _, stderr) = RunCli(new Dictionary<string, string?> { ["COLUMNS"] = null }, "--preview", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("preview at 100 columns (from the default of 100)", stderr);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"preview-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] cliArgs) => RunCli(null, cliArgs);

    static (int ExitCode, string StdOut, string StdErr) RunCli(IReadOnlyDictionary<string, string?>? envOverrides, params string[] cliArgs)
    {
        var bin = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_BIN");
        ProcessStartInfo psi;
        if (!string.IsNullOrWhiteSpace(bin) && File.Exists(bin))
        {
            psi = new ProcessStartInfo(bin)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
        }
        else
        {
            var repoRoot = FindRepoRoot();
            var csproj = Path.Combine(repoRoot, "src", "ClaudeTuiLine", "ClaudeTuiLine.csproj");
            psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--project");
            psi.ArgumentList.Add(csproj);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("--");
        }

        foreach (var arg in cliArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        // Tests must not inherit whatever COLUMNS happens to be set in the host shell — the
        // fallback-chain tests below need deterministic control over whether it's present at all.
        psi.Environment.Remove("COLUMNS");
        if (envOverrides is not null)
        {
            foreach (var (key, value) in envOverrides)
            {
                if (value is null)
                {
                    psi.Environment.Remove(key);
                }
                else
                {
                    psi.Environment[key] = value;
                }
            }
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start claude-tui-line process");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SPEC-V2-FRAMEWORK.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException($"could not locate repo root (SPEC-V2-FRAMEWORK.md not found above {AppContext.BaseDirectory})");
    }
}
