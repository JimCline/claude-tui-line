using System.Globalization;
using System.Text.Json;

namespace ClaudeTuiLine;

/// <summary>
/// Cumulative token counters for one session, summed from its transcript JSONL. Counts only —
/// SPEC token-usage-item.md §11 forbids deriving a cost figure from these.
/// </summary>
public sealed record TokenTotals(
    long InputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long OutputTokens)
{
    public long InputSide => InputTokens + CacheCreationTokens + CacheReadTokens;
}

/// <summary>
/// SPEC token-usage-item.md §4-§7: reads a session's transcript JSONL directly off disk (no
/// network, no subprocess) and sums its usage blocks. Modelled on
/// <see cref="EngramTelemetry"/>'s session-id-keyed JSONL reader — <c>projectsRootOverride</c>
/// mirrors <see cref="EngramTelemetry.Build"/>'s <c>telemetryPath</c> testability seam.
/// token-usage-indexer.md §1.2/§5.1: cached through <see cref="ItemCache"/> with the same 30s TTL
/// as <see cref="RemoteUrl"/> and <see cref="CommandProvider"/> — a full transcript parse on every
/// render is the per-render cost those two providers' TTL already exists to avoid.
/// </summary>
internal static class TokenUsage
{
    // Matches RemoteUrl.CacheTtl / CommandProvider.DefaultTtlSeconds: same per-render parse-cost
    // tradeoff, per token-usage-indexer.md §5.4's ruling that the TTL is a v1 defect fix, not a
    // v2 enhancement.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyDictionary<string, string> NoExportedEnv = new Dictionary<string, string>();

    /// <returns>null when the transcript cannot be located or read, or contains no qualifying
    /// usage lines — never throws.</returns>
    public static TokenTotals? Probe(string? sessionId, string cacheDir, string? projectsRootOverride = null)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var key = ItemCache.KeyFor("token-usage", new[] { sessionId }, cwd: null, paneWidth: null, NoExportedEnv);
        var cached = ItemCache.TryRead(cacheDir, key);
        if (cached is { } fresh && DateTimeOffset.UtcNow - fresh.CapturedAt < CacheTtl)
        {
            return DecodeTotals(fresh.Value);
        }

        var path = ResolveTranscriptPath(sessionId, projectsRootOverride);
        var totals = path is null ? null : ParseTranscript(path, sessionId);
        ItemCache.Write(cacheDir, key, new CacheEntry(EncodeTotals(totals), DateTimeOffset.UtcNow, ExitCode: 0));
        return totals;
    }

    private static string? EncodeTotals(TokenTotals? totals) =>
        totals is null
            ? null
            : string.Join(',',
                totals.InputTokens.ToString(CultureInfo.InvariantCulture),
                totals.CacheCreationTokens.ToString(CultureInfo.InvariantCulture),
                totals.CacheReadTokens.ToString(CultureInfo.InvariantCulture),
                totals.OutputTokens.ToString(CultureInfo.InvariantCulture));

    private static TokenTotals? DecodeTotals(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var parts = value.Split(',');
        if (parts.Length != 4)
        {
            return null;
        }

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var input)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cacheCreate)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cacheRead)
            || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var output))
        {
            return null;
        }

        return new TokenTotals(input, cacheCreate, cacheRead, output);
    }

    // §5.1: glob ~/.claude/projects/*/<sessionId>.jsonl. The project-slug directory name is never
    // derived — its encoding rules are unverified — so every immediate subdirectory is tested and
    // the first hit wins.
    private static string? ResolveTranscriptPath(string sessionId, string? projectsRootOverride)
    {
        string projectsRoot;
        if (projectsRootOverride is not null)
        {
            projectsRoot = projectsRootOverride;
        }
        else
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                return null;
            }

            projectsRoot = Path.Combine(home, ".claude", "projects");
        }

        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(projectsRoot);
        }
        catch
        {
            return null;
        }

        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, sessionId + ".jsonl");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // §7.1: full single-pass streaming parse, FileShare.ReadWrite because the harness appends to
    // the transcript while this reads it (§2.2). §6.2's dedup-by-message.id is what makes the
    // straightforward sum correct despite duplicate lines.
    private static TokenTotals? ParseTranscript(string path, string sessionId)
    {
        long inputTokens = 0, cacheCreation = 0, cacheRead = 0, outputTokens = 0;
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var any = false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                // §7.1: cheap pre-filter before invoking the JSON parser — only usage-bearing
                // lines matter, and most lines are not.
                if (!line.Contains("\"usage\"", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryAccumulateLine(line, sessionId, seenMessageIds, ref inputTokens, ref cacheCreation, ref cacheRead, ref outputTokens))
                {
                    any = true;
                }
            }
        }
        catch
        {
            return null;
        }

        return any ? new TokenTotals(inputTokens, cacheCreation, cacheRead, outputTokens) : null;
    }

    private static bool TryAccumulateLine(
        string line,
        string sessionId,
        HashSet<string> seenMessageIds,
        ref long inputTokens,
        ref long cacheCreation,
        ref long cacheRead,
        ref long outputTokens)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // §6.4/§2.2: a malformed or truncated line — including the harness's actively-
            // appended trailing line — is skipped silently, never an error.
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // §6.4: only "assistant" lines carry the usage shape this reads.
            if (!TryGetString(root, "type", out var type) || !string.Equals(type, "assistant", StringComparison.Ordinal))
            {
                return false;
            }

            // §6.4: skip a line whose top-level sessionId disagrees with the payload's — insurance
            // against a forked/branched conversation's copied history. Absent ⇒ no filtering.
            if (root.TryGetProperty("sessionId", out var sidEl) && sidEl.ValueKind == JsonValueKind.String)
            {
                var lineSessionId = sidEl.GetString();
                if (!string.IsNullOrEmpty(lineSessionId) && !string.Equals(lineSessionId, sessionId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // §6.3/§2.1: test for boolean true explicitly. A present `false` and an absent
            // property are equivalent (both count); a truthiness test would wrongly conflate them.
            // Applied before §6.2's dedup, per §6.3's ordering rule.
            if (root.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True)
            {
                return false;
            }

            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // §6.2: one line per message.id — duplicates are byte-identical, so first-wins is
            // correct and cheapest. An absent/empty id cannot be a duplicate of anything and is
            // always counted.
            if (TryGetString(message, "id", out var messageId) && messageId.Length > 0)
            {
                if (!seenMessageIds.Add(messageId))
                {
                    return false;
                }
            }

            inputTokens += GetLong(usage, "input_tokens");
            cacheCreation += GetLong(usage, "cache_creation_input_tokens");
            cacheRead += GetLong(usage, "cache_read_input_tokens");
            outputTokens += GetLong(usage, "output_tokens");

            return true;
        }
    }

    private static long GetLong(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var value)
            ? value
            : 0;

    private static bool TryGetString(JsonElement obj, string property, out string value)
    {
        if (obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
