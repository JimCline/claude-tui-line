using System.Text.RegularExpressions;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>
/// SPEC-12.6-mcp-tools.md §10 V4 / §7.1: the only core (ClaudeTuiLine.*) member the MCP server
/// may reference is <c>ConfigLoader.ResolveConfigPath(...)</c>. E1 found no reusable SHA-256
/// helper or atomic writer in the core, so A2's conditional second/third allow-list member was not
/// added — the list stays at one.
/// </summary>
public sealed class AllowListTests
{
    private static readonly Regex CoreReference = new(@"\bClaudeTuiLine\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    [Fact]
    public void V4_OnlyResolveConfigPathIsReferencedFromTheCore()
    {
        var srcDir = FindSrcClaudeTuiLineMcp();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in CoreReference.Matches(text))
            {
                var member = match.Groups[1].Value;
                if (member != "ConfigLoader")
                {
                    offenders.Add($"{file}: ClaudeTuiLine.{member}");
                    continue;
                }

                // Must be followed by ".ResolveConfigPath(" — no other ConfigLoader member allowed.
                var afterMatch = text[(match.Index + match.Length)..];
                if (!afterMatch.TrimStart().StartsWith(".ResolveConfigPath("))
                {
                    offenders.Add($"{file}: ClaudeTuiLine.ConfigLoader.<something other than ResolveConfigPath>");
                }
            }
        }

        Assert.True(offenders.Count == 0, "§7.1 allow-list violated:\n" + string.Join("\n", offenders));
    }

    private static string FindSrcClaudeTuiLineMcp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ClaudeTuiLineMcp")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("could not locate src/ClaudeTuiLineMcp from the test output directory");
        }

        return Path.Combine(dir.FullName, "src", "ClaudeTuiLineMcp");
    }
}
