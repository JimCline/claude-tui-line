using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5: one cache entry for a <c>command</c> item. <see cref="PaneWidth"/> is
/// the inner width the item's pane resolved to on the render that last stamped it — a separate
/// concern from <see cref="Value"/>/<see cref="CapturedAt"/>/<see cref="ExitCode"/>, which come
/// from the provider spawn itself. It is written on every render (including cache hits), which is
/// why it is stamped through <see cref="ItemCache.StampPaneWidth"/> rather than folded into
/// <see cref="ItemCache.Write"/> — a long-TTL value entry must not drag a stale width along with it.
/// </summary>
public sealed record CacheEntry(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("capturedAt")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("paneWidth")] int? PaneWidth);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CacheEntry))]
internal partial class CacheEntryJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §5: the per-item command-provider cache. One file per cache key — not one
/// shared map — because two sessions refreshing two different items would otherwise
/// read-modify-write the same file and last-write-wins would silently discard whichever refresh
/// lost the race, leaving that item stale until its own next respawn. Per-key files make
/// last-write-wins correct at the granularity the value actually has. A torn or unparsable cache
/// file is treated as empty (a miss), never an error, per §7.
/// </summary>
public static class ItemCache
{
    public static string ResolveCacheDir(string? cacheOverride, string? xdgCacheHome, string? home)
    {
        if (!string.IsNullOrEmpty(cacheOverride))
        {
            return Path.Combine(cacheOverride, "items");
        }

        var baseDir = !string.IsNullOrEmpty(xdgCacheHome)
            ? xdgCacheHome
            : string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".cache");

        return baseDir is null
            ? Path.Combine(Path.GetTempPath(), "claude-tui-line", "items")
            : Path.Combine(baseDir, "claude-tui-line", "items");
    }

    public static string ResolveCacheDir() =>
        ResolveCacheDir(
            Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CACHE"),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            Environment.GetEnvironmentVariable("HOME"));

    /// <summary>
    /// §5: <c>id</c> + hash of the resolved argv + <c>cwd</c>. <c>cwd</c> is part of the key
    /// because a command like <c>git status --short</c> means different things in different
    /// sessions, and this cache is shared by every session on the machine.
    /// </summary>
    public static string KeyFor(string id, IReadOnlyList<string> argv, string? cwd)
    {
        var joined = string.Join('', argv) + '' + (cwd ?? "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        return $"{SanitizeId(id)}-{hash[..16]}";
    }

    private static string SanitizeId(string id)
    {
        var chars = id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return chars.Length == 0 ? "_" : new string(chars);
    }

    public static CacheEntry? TryRead(string cacheDir, string key)
    {
        try
        {
            var path = Path.Combine(cacheDir, key + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize(text, CacheEntryJsonContext.Default.CacheEntry);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Atomic: temp file in the same directory, then rename — never a partial read of a concurrent write.</summary>
    public static void Write(string cacheDir, string key, CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var path = Path.Combine(cacheDir, key + ".json");
            var tempPath = Path.Combine(cacheDir, $".{key}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, CacheEntryJsonContext.Default.CacheEntry));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // §7: a cache write failure just means the next render re-spawns; never an error.
        }
    }

    /// <summary>
    /// §4/§5: updates only <see cref="CacheEntry.PaneWidth"/> on an already-written entry. A
    /// no-op when no entry exists yet (nothing to stamp onto) — the item's own fetch is what
    /// creates the entry; this only ever runs after that, in the post-sizing pass.
    /// </summary>
    public static void StampPaneWidth(string cacheDir, string key, int paneWidth)
    {
        if (TryRead(cacheDir, key) is not { } existing)
        {
            return;
        }

        Write(cacheDir, key, existing with { PaneWidth = paneWidth });
    }
}
