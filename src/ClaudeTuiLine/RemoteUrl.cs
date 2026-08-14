using System.Diagnostics;

namespace ClaudeTuiLine;

/// <summary>
/// Live probe for the current repo's <c>origin</c> remote, matching
/// <c>git remote get-url origin</c> — chosen over reading <c>.git/config</c> directly because it
/// honors the user's own <c>url.&lt;base&gt;.insteadOf</c> rewrites. Synchronous: nothing awaits
/// this (see <see cref="ItemContext.RemoteUrl"/>'s lazy property), so there is no async wrapper to
/// block on — <see cref="Process.WaitForExit(int)"/> is already the blocking-with-timeout primitive
/// this needs.
/// </summary>
public static class RemoteUrl
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static string? Probe(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd))
        {
            return null;
        }

        return Run(cwd);
    }

    private static string? Run(string cwd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(cwd);
        psi.ArgumentList.Add("remote");
        psi.ArgumentList.Add("get-url");
        psi.ArgumentList.Add("origin");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        }
        catch
        {
            return null;
        }

        try
        {
            // git remote get-url's output is one short line, well under a pipe buffer, so it is
            // safe to wait for exit before reading: the child never blocks on a full stdout pipe.
            var exited = process.WaitForExit((int)Timeout.TotalMilliseconds);
            if (!exited)
            {
                TryKill(process);
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                return null;
            }

            var raw = stdout.Trim();
            return raw.Length == 0 ? null : Normalize(raw);
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

    internal static string Normalize(string raw)
    {
        var url = raw.EndsWith(".git", StringComparison.Ordinal) ? raw[..^4] : raw;

        if (url.StartsWith("git@", StringComparison.Ordinal))
        {
            var colonIndex = url.IndexOf(':', "git@".Length);
            if (colonIndex > 0)
            {
                var host = url["git@".Length..colonIndex];
                var path = url[(colonIndex + 1)..];
                return $"https://{host}/{path}";
            }
        }

        return url;
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
            // best-effort cleanup only
        }
    }
}
