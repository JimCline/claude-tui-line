using System.Globalization;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC token-usage-item.md §13.3: T1-T11, T13, T14. T12 (the fixture's exact rendered example)
/// lives in ItemsCommandTests, alongside the other --items example assertions.
/// </summary>
public class TokenUsageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheDir;

    public TokenUsageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claude-tui-line-tokenusage-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _cacheDir = Path.Combine(_tempDir, "__cache__");
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup only
        }
    }

    private void WriteTranscript(string projectDirName, string sessionId, params string[] lines)
    {
        var projectDir = Path.Combine(_tempDir, projectDirName);
        Directory.CreateDirectory(projectDir);
        File.WriteAllLines(Path.Combine(projectDir, sessionId + ".jsonl"), lines);
    }

    private static string UsageLine(string sessionId, string messageId, long input, long cacheCreate, long cacheRead, long output, bool? sidechain = null, string? sessionIdOverride = null)
    {
        var sid = sessionIdOverride ?? sessionId;
        var sidechainField = sidechain is null ? string.Empty : $"\"isSidechain\":{(sidechain.Value ? "true" : "false")},";
        return "{\"type\":\"assistant\"," + sidechainField + "\"sessionId\":\"" + sid + "\",\"message\":{\"id\":\"" + messageId
            + "\",\"usage\":{\"input_tokens\":" + input + ",\"cache_creation_input_tokens\":" + cacheCreate
            + ",\"cache_read_input_tokens\":" + cacheRead + ",\"output_tokens\":" + output + "}}}";
    }

    [Fact]
    public void Probe_DuplicateMessageIds_CountsOnce()
    {
        const string sessionId = "11111111-1111-4111-8111-111111111111";
        var line = UsageLine(sessionId, "msg-1", 2, 10, 500, 20);
        WriteTranscript("proj", sessionId, line, line, line);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(2, totals!.InputTokens);
        Assert.Equal(10, totals.CacheCreationTokens);
        Assert.Equal(500, totals.CacheReadTokens);
        Assert.Equal(20, totals.OutputTokens);
    }

    [Fact]
    public void Probe_SidechainTrueAndFalse_OnlyFalseCounted()
    {
        const string sessionId = "22222222-2222-4222-8222-222222222222";
        var trueLine = UsageLine(sessionId, "msg-a", 100, 0, 0, 999, sidechain: true);
        var falseLine = UsageLine(sessionId, "msg-b", 5, 0, 0, 7, sidechain: false);
        WriteTranscript("proj", sessionId, trueLine, falseLine);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(5, totals!.InputTokens);
        Assert.Equal(7, totals.OutputTokens);
    }

    [Fact]
    public void Probe_SidechainFalse_IsCounted()
    {
        const string sessionId = "33333333-3333-4333-8333-333333333333";
        var line = UsageLine(sessionId, "msg-only", 12, 0, 0, 34, sidechain: false);
        WriteTranscript("proj", sessionId, line);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(12, totals!.InputTokens);
        Assert.Equal(34, totals.OutputTokens);
    }

    [Fact]
    public void Probe_SidechainAbsent_IsCounted()
    {
        const string sessionId = "44444444-4444-4444-8444-444444444444";
        var line = UsageLine(sessionId, "msg-only", 12, 0, 0, 34);
        WriteTranscript("proj", sessionId, line);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(12, totals!.InputTokens);
        Assert.Equal(34, totals.OutputTokens);
    }

    [Fact]
    public void Probe_MissingTranscript_ReturnsNull()
    {
        var totals = TokenUsage.Probe("nonexistent-session-id", _cacheDir, _tempDir);

        Assert.Null(totals);
    }

    [Fact]
    public void Probe_FindsTranscriptUnderArbitraryDirectoryName()
    {
        const string sessionId = "66666666-6666-4666-8666-666666666666";
        var line = UsageLine(sessionId, "msg-1", 10, 0, 0, 20);
        WriteTranscript("zzz-unrelated", sessionId, line);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(10, totals!.InputTokens);
        Assert.Equal(20, totals.OutputTokens);
    }

    [Fact]
    public void Probe_TruncatedFinalLine_SkippedWithoutAffectingCompleteLines()
    {
        const string sessionId = "77777777-7777-4777-8777-777777777777";
        var goodLine = UsageLine(sessionId, "msg-1", 10, 0, 0, 20);
        const string truncated = "{\"type\":\"assistant\",\"sessionId\":\"77777777-7777-4777-8777-777777777777\",\"message\":{\"id\":\"msg-2\",\"usage\":{\"input_tokens\":5";
        WriteTranscript("proj", sessionId, goodLine, truncated);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(10, totals!.InputTokens);
        Assert.Equal(20, totals.OutputTokens);
    }

    [Fact]
    public void Probe_NonAssistantAndUsagelessLines_Ignored()
    {
        const string sessionId = "88888888-8888-4888-8888-888888888888";
        const string nonAssistant = "{\"type\":\"user\",\"sessionId\":\"88888888-8888-4888-8888-888888888888\",\"message\":{\"id\":\"msg-u\",\"usage\":{\"input_tokens\":999,\"output_tokens\":999}}}";
        const string usageless = "{\"type\":\"assistant\",\"sessionId\":\"88888888-8888-4888-8888-888888888888\",\"message\":{\"id\":\"msg-n\"}}";
        var goodLine = UsageLine(sessionId, "msg-good", 10, 0, 0, 20);
        WriteTranscript("proj", sessionId, nonAssistant, usageless, goodLine);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(10, totals!.InputTokens);
        Assert.Equal(20, totals.OutputTokens);
    }

    [Fact]
    public void Probe_ForeignSessionId_Skipped()
    {
        const string sessionId = "99999999-9999-4999-8999-999999999999";
        var foreignLine = UsageLine(sessionId, "msg-foreign", 999, 0, 0, 999, sessionIdOverride: "00000000-0000-4000-8000-000000000001");
        var ownLine = UsageLine(sessionId, "msg-own", 10, 0, 0, 20);
        WriteTranscript("proj", sessionId, foreignLine, ownLine);

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(10, totals!.InputTokens);
        Assert.Equal(20, totals.OutputTokens);
    }

    [Theory]
    [InlineData(999L, "999")]
    [InlineData(1_000L, "1k")]
    [InlineData(68_000L, "68k")]
    [InlineData(999_999L, "1000k")]
    [InlineData(1_000_000L, "1.0M")]
    [InlineData(24_100_000L, "24.1M")]
    [InlineData(0L, "0")]
    public void Compact_FormatsAtEachBoundary(long input, string expected)
    {
        Assert.Equal(expected, SegmentBuilder.Compact(input));
    }

    [Fact]
    public void BuildTokenUsage_ZeroInputSide_OmitsCacheClause()
    {
        var totals = new TokenTotals(0, 0, 0, 0);

        var segment = SegmentBuilder.BuildTokenUsage(totals);

        Assert.NotNull(segment);
        Assert.Equal("tok:0/0", segment!.Plain);
    }

    [Fact]
    public void TokenUsage_IsExcludedFromDefaultIds_AndCountStaysFourteen()
    {
        Assert.DoesNotContain("token-usage", ItemRegistry.DefaultIds);
        Assert.Equal(14, ItemRegistry.DefaultIds.Count);
    }

    [Fact]
    public void Compact_MillionsBranch_UsesInvariantCultureUnderCommaDecimalCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("24.1M", SegmentBuilder.Compact(24_100_000L));
            Assert.Equal("1.0M", SegmentBuilder.Compact(1_000_000L));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> NoExportedEnv = new Dictionary<string, string>();

    private static string CacheKeyFor(string sessionId) =>
        ItemCache.KeyFor("token-usage", new[] { sessionId }, cwd: null, paneWidth: null, NoExportedEnv);

    [Fact]
    public void Probe_CacheHitWithinTtl_DoesNotReReadTranscript()
    {
        const string sessionId = "aaaaaaaa-1111-4111-8111-111111111111";
        WriteTranscript("proj", sessionId, UsageLine(sessionId, "msg-1", 10, 0, 0, 20));

        var first = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);
        Assert.NotNull(first);
        Assert.Equal(10, first!.InputTokens);

        // Overwrite the transcript with different totals. A cache hit within the TTL must
        // return the first call's cached value, never re-read this file.
        WriteTranscript("proj", sessionId, UsageLine(sessionId, "msg-2", 999, 0, 0, 999));

        var second = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(second);
        Assert.Equal(10, second!.InputTokens);
        Assert.Equal(20, second.OutputTokens);
    }

    [Fact]
    public void Probe_CacheMissAfterTtlExpiry_ReReadsTranscript()
    {
        const string sessionId = "aaaaaaaa-2222-4222-8222-222222222222";
        WriteTranscript("proj", sessionId, UsageLine(sessionId, "msg-1", 10, 0, 0, 20));
        var first = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);
        Assert.NotNull(first);

        // Backdate the cache entry past the 30s TTL so the next Probe treats it as expired.
        var key = CacheKeyFor(sessionId);
        ItemCache.Write(_cacheDir, key, new CacheEntry("10,0,0,20", DateTimeOffset.UtcNow - TimeSpan.FromSeconds(31), ExitCode: 0));

        WriteTranscript("proj", sessionId, UsageLine(sessionId, "msg-2", 999, 0, 0, 999));

        var second = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(second);
        Assert.Equal(999, second!.InputTokens);
        Assert.Equal(999, second.OutputTokens);
    }

    [Fact]
    public void Probe_CacheKeyedPerSession_DifferentSessionsDoNotShareCache()
    {
        const string sessionA = "aaaaaaaa-3333-4333-8333-333333333333";
        const string sessionB = "bbbbbbbb-3333-4333-8333-333333333333";
        WriteTranscript("proj", sessionA, UsageLine(sessionA, "msg-a", 10, 0, 0, 20));
        WriteTranscript("proj", sessionB, UsageLine(sessionB, "msg-b", 30, 0, 0, 40));

        var totalsA = TokenUsage.Probe(sessionA, _cacheDir, _tempDir);
        var totalsB = TokenUsage.Probe(sessionB, _cacheDir, _tempDir);

        Assert.NotNull(totalsA);
        Assert.NotNull(totalsB);
        Assert.Equal(10, totalsA!.InputTokens);
        Assert.Equal(30, totalsB!.InputTokens);
    }

    [Fact]
    public void Probe_NullResultForMissingTranscript_IsAlsoCachedWithinTtl()
    {
        const string sessionId = "aaaaaaaa-4444-4444-8444-444444444444";

        var first = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);
        Assert.Null(first);

        // The transcript now exists, but the prior null result is still within the TTL — a
        // cached miss must not be re-resolved before it expires.
        WriteTranscript("proj", sessionId, UsageLine(sessionId, "msg-1", 10, 0, 0, 20));

        var second = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.Null(second);
    }

    [Fact]
    public void Probe_CorruptCacheEntry_TreatedAsMissAndReParses()
    {
        const string sessionId = "aaaaaaaa-5555-4555-8555-555555555555";
        WriteTranscript("proj", sessionId, UsageLine(sessionId, "msg-1", 10, 0, 0, 20));

        Directory.CreateDirectory(_cacheDir);
        File.WriteAllText(Path.Combine(_cacheDir, CacheKeyFor(sessionId) + ".json"), "not valid json{{{");

        var totals = TokenUsage.Probe(sessionId, _cacheDir, _tempDir);

        Assert.NotNull(totals);
        Assert.Equal(10, totals!.InputTokens);
        Assert.Equal(20, totals.OutputTokens);
    }
}
