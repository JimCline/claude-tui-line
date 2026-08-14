using System.Diagnostics;
using System.Globalization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4/§5: resolves one <c>command</c> item's value against
/// <see cref="ItemCache"/>. A fresh cache entry (within <see cref="PaneItem.TtlSeconds"/>, default
/// 30) is returned without spawning anything. A miss or expired entry spawns the command —
/// argv by default, or <c>sh -c "&lt;string&gt;"</c> when <see cref="PaneItem.Shell"/> — bounded by
/// <see cref="PaneItem.TimeoutMs"/> (default 150) and killed with its whole process tree on
/// timeout, mirroring <see cref="GitBranch"/>. Any failure to produce a fresh value — timeout,
/// nonzero exit, a missing binary — falls back to the last cached value even if expired
/// ("stale-on-failure"); only a command that has never once succeeded resolves to null
/// (suppressed). A clean run with empty output is a legitimate value (empty), cached like any
/// other, so a command that is quiet by design does not respawn every render.
/// </summary>
public static class CommandProvider
{
    private const int DefaultTtlSeconds = 30;
    private const int DefaultTimeoutMs = 150;

    public static async Task<string?> ResolveAsync(
        PaneItem item, string? rawStdinJson, string? cwd, string cacheDir, bool paneWidthEligible)
    {
        if (item.Id is not { Length: > 0 } id || item.Command is not { Count: > 0 } command)
        {
            return null;
        }

        var ttl = TimeSpan.FromSeconds(item.TtlSeconds is > 0 ? item.TtlSeconds.Value : DefaultTtlSeconds);
        var timeout = TimeSpan.FromMilliseconds(item.TimeoutMs is > 0 ? item.TimeoutMs.Value : DefaultTimeoutMs);
        var key = ItemCache.KeyFor(id, command, cwd);

        var cached = ItemCache.TryRead(cacheDir, key);
        if (cached is { } fresh && DateTimeOffset.UtcNow - fresh.CapturedAt < ttl)
        {
            return fresh.Value;
        }

        var previousPaneWidth = paneWidthEligible ? cached?.PaneWidth : null;
        var spawned = await RunAsync(item, command, rawStdinJson, cwd, timeout, previousPaneWidth).ConfigureAwait(false);
        if (spawned is not { } result)
        {
            return cached?.Value;
        }

        ItemCache.Write(cacheDir, key, new CacheEntry(result.Value, DateTimeOffset.UtcNow, result.ExitCode, cached?.PaneWidth));
        return result.Value;
    }

    private readonly record struct SpawnResult(string? Value, int ExitCode);

    private static async Task<SpawnResult?> RunAsync(
        PaneItem item, IReadOnlyList<string> command, string? rawStdinJson, string? cwd, TimeSpan timeout, int? previousPaneWidth)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        if (!string.IsNullOrEmpty(cwd))
        {
            psi.WorkingDirectory = cwd;
        }

        if (item.Shell)
        {
            psi.FileName = "sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command[0]);
        }
        else
        {
            psi.FileName = command[0];
            foreach (var arg in command.Skip(1))
            {
                psi.ArgumentList.Add(arg);
            }
        }

        psi.Environment["CLAUDE_TUI_LINE_ITEM_ID"] = item.Id;
        if (previousPaneWidth is { } width)
        {
            psi.Environment["CLAUDE_TUI_LINE_PANE_WIDTH"] = width.ToString(CultureInfo.InvariantCulture);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("command did not start");
        }
        catch
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(timeout);

            await process.StandardInput.WriteAsync(rawStdinJson ?? "").ConfigureAwait(false);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            var firstLine = stdout.Split('\n', 2)[0].TrimEnd('\r');
            return new SpawnResult(firstLine.Length == 0 ? null : firstLine, process.ExitCode);
        }
        catch
        {
            TryKill(process);
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
