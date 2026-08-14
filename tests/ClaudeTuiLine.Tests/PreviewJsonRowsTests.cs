using System.Diagnostics;
using System.Text.Json;

namespace ClaudeTuiLine.Tests;

// SPEC-V2-FRAMEWORK.md §9.3.4/§9.6: rows[].text is ANSI-stripped, rows[].width always equals
// text.Length, and rows[].contentWidth is present only on content rows of a single-pane (bordered)
// render and absent — not null, omitted from the JSON entirely — on that pane's top/bottom border
// lines. Unlike ItemsCommand/ColorsCommand, --preview's JSON is built inside RunPreview, a
// top-level local function in Program.cs with no public, directly callable entry point (top-level
// statements compile local functions in a way reflection can't cleanly address even with
// InternalsVisibleTo), so these tests exercise the built CLI as a subprocess — the same mechanism
// tools/check-examples.sh already uses to check this document's JSON shapes against real output.
public class PreviewJsonRowsTests
{
    [Fact]
    public void Preview_SingleBorderedPane_TopAndBottomRowsOmitContentWidthAndContentRowsIncludeIt()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, stdout, stderr) = RunCli("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            Assert.Equal(60, root.GetProperty("columns").GetInt32());

            var rowList = root.GetProperty("rows").EnumerateArray().ToList();
            Assert.True(rowList.Count >= 3, $"expected a bordered pane to report a top row, at least one content row, and a bottom row; got {rowList.Count}. stdout: {stdout}");

            foreach (var row in rowList)
            {
                var text = row.GetProperty("text").GetString();
                Assert.Equal(text!.Length, row.GetProperty("width").GetInt32());
            }

            var topRow = rowList[0];
            var bottomRow = rowList[^1];
            Assert.False(topRow.TryGetProperty("contentWidth", out _), "a border row must omit contentWidth entirely, not carry it as null");
            Assert.False(bottomRow.TryGetProperty("contentWidth", out _), "a border row must omit contentWidth entirely, not carry it as null");

            foreach (var contentRow in rowList.Skip(1).SkipLast(1))
            {
                Assert.True(contentRow.TryGetProperty("contentWidth", out var contentWidth), "a content row between the top and bottom border must carry contentWidth");
                Assert.True(contentWidth.GetInt32() < contentRow.GetProperty("width").GetInt32(), "a content row's pre-border width must be smaller than the bordered line's own width");
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_SplitPaneConfig_EveryRowCarriesItsOwnWidthMatchingItsText()
    {
        var configPath = WriteTempConfig("""
        {
          "pane": {
            "split": "vertical",
            "children": [
              { "border": { "enabled": true }, "items": [ { "item": "model-short" } ] },
              { "border": { "enabled": true }, "items": [ { "item": "git-branch" } ] }
            ]
          }
        }
        """);
        try
        {
            var (exitCode, stdout, stderr) = RunCli("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            using var doc = JsonDocument.Parse(stdout);
            var rowList = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();

            Assert.NotEmpty(rowList);
            foreach (var row in rowList)
            {
                var text = row.GetProperty("text").GetString();
                Assert.Equal(text!.Length, row.GetProperty("width").GetInt32());
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Preview_Json_SerializesRowsWithTheSpecifiedPropertyNames()
    {
        var configPath = WriteTempConfig("{}");
        try
        {
            var (exitCode, stdout, stderr) = RunCli("--preview", "--json", "--columns", "60", "--config", configPath);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stderr}");
            Assert.Contains("\"columns\":", stdout);
            Assert.Contains("\"usableColumns\":", stdout);
            Assert.Contains("\"rows\":", stdout);
            Assert.Contains("\"text\":", stdout);
            Assert.Contains("\"width\":", stdout);
            Assert.Contains("\"contentWidth\":", stdout);
            Assert.Contains("\"notes\":", stdout);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"preview-json-rows-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] cliArgs)
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
