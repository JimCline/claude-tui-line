using System.Text.Json;
using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-101-calibrate-chrome-reserve.md §12.9. No existing golden/render-invariant test in this
/// suite calls RunAsync (the only caller of <see cref="CalibrateCommand.MaybeAppendNudge"/>) — they
/// all exercise ComputeRows/DrawRows or lower directly — so §12.5's "quiet path stays byte-identical"
/// invariant is verified here, at the level MaybeAppendNudge is actually reachable, rather than by
/// touching unrelated golden tests that never call it.
/// </summary>
[Collection(nameof(CalibrationEnvVarCollection))]
public class CalibrationPromptTests : IDisposable
{
    private readonly string? _originalOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");
    private readonly string? _originalNoNudge = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE");
    private readonly string _configPath;

    public CalibrationPromptTests()
    {
        var dir = Directory.CreateTempSubdirectory("ctl-calibration-prompt-tests-");
        _configPath = Path.Combine(dir.FullName, "claude-tui-line.json");
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _configPath);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG", _originalOverride);
        Environment.SetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE", _originalNoNudge);

        var dir = Path.GetDirectoryName(_configPath)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // 1. Version round-trips from the real captured payload — the field the synthetic fixture lacks.
    [Fact]
    public void StatusInput_RealCapturedPayload_VersionRoundTrips()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "real_captured_workspace.json"));
        var input = JsonSerializer.Deserialize(json, StatusInputJsonContext.Default.StatusInput);

        Assert.Equal("2.1.233", input!.Version);
    }

    // 2. Trigger table — one case per §12.3 rule.
    [Fact]
    public void Rule1_PromptDisabled_NoPrompt()
    {
        var appended = InvokeNudge("2.1.300", calibrationPromptEnabled: false);
        Assert.False(appended);
    }

    [Fact]
    public void Rule2_CalibrationInProgress_NoPrompt()
    {
        WriteState(new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule3_NoRecord_FirstRunPrompts()
    {
        Assert.False(File.Exists(RecordPath));

        var appended = InvokeNudge("2.1.300");

        Assert.True(appended);
        Assert.True(File.Exists(RecordPath));
    }

    [Fact]
    public void Rule4_NullVersion_NeverMatchesARecordedVersionAndNeverPrompts()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = null, DismissedVersion = null });

        var appended = InvokeNudge(null);

        Assert.False(appended);
    }

    [Fact]
    public void Rule5_VersionMatchesCalibratedVersion_NoPrompt()
    {
        WriteRecord(new CalibrationRecord { CalibratedVersion = "2.1.300" });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule6_VersionMatchesDismissedVersion_NoPrompt()
    {
        WriteRecord(new CalibrationRecord { DismissedVersion = "2.1.300" });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule7_PromptWindowExpiredForSameVersion_NoPrompt()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.300", PromptFirstSeen = DateTimeOffset.UtcNow.AddDays(-8) });

        var appended = InvokeNudge("2.1.300");

        Assert.False(appended);
    }

    [Fact]
    public void Rule8_NewVersionWithinWindow_Prompts()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.299", PromptFirstSeen = DateTimeOffset.UtcNow.AddDays(-1), CalibratedVersion = "2.1.299" });

        var appended = InvokeNudge("2.1.300");

        Assert.True(appended);
    }

    // 3. Invariant 1: quiet path (prompt disabled) never appends a row.
    [Fact]
    public void Invariant1_PromptDisabled_NoRowEverAppended_RegardlessOfRecordState()
    {
        Assert.False(InvokeNudge("2.1.300", calibrationPromptEnabled: false));

        WriteRecord(new CalibrationRecord());
        Assert.False(InvokeNudge("2.1.300", calibrationPromptEnabled: false));
    }

    // 4. Invariant 2: exactly one row appended, and it is the only output written.
    [Fact]
    public void Invariant2_PromptActive_AppendsExactlyOneRow()
    {
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            CalibrateCommand.MaybeAppendNudge("2.1.300", calibrationPromptEnabled: true, surfaceWidth: 100);
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    // 5. Nudge width.
    [Fact]
    public void Nudge_IsAtMost48CharactersAndDoesNotEndInWhitespace()
    {
        var line = CaptureNudgeLine("2.1.300", surfaceWidth: 100);

        Assert.NotNull(line);
        Assert.True(line!.Length <= 48);
        Assert.False(char.IsWhiteSpace(line[^1]));
    }

    [Fact]
    public void Nudge_SuppressedBelowSurfaceWidth50()
    {
        var line = CaptureNudgeLine("2.1.300", surfaceWidth: 49);

        Assert.Null(line);
    }

    // 6. Record write frequency: two consecutive renders at the same version write the record once.
    [Fact]
    public void ConsecutiveRendersAtSameVersion_WriteRecordExactlyOnce()
    {
        InvokeNudge("2.1.300");
        var firstWrite = File.GetLastWriteTimeUtc(RecordPath);

        Thread.Sleep(20);
        InvokeNudge("2.1.300");
        var secondWrite = File.GetLastWriteTimeUtc(RecordPath);

        Assert.Equal(firstWrite, secondWrite);
    }

    // 7. --dismiss.
    [Fact]
    public void Dismiss_CopiesPromptedForVersion_SuppressesThatVersion_ButNotALaterOne()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.300", PromptFirstSeen = DateTimeOffset.UtcNow });

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);
        Assert.Equal(0, exitCode);

        Assert.False(InvokeNudge("2.1.300"));
        Assert.True(InvokeNudge("2.1.301"));
    }

    [Fact]
    public void Dismiss_WithNoPromptedForVersion_WritesNothing()
    {
        Assert.False(File.Exists(RecordPath));

        var exitCode = CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(RecordPath));
    }

    // 8. Prompt window — covered structurally by Rule7 above; asserted again against the boundary.
    [Fact]
    public void PromptWindow_OlderThanSevenDays_Suppresses()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "9.9.9", PromptFirstSeen = DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(-1) });

        Assert.False(InvokeNudge("9.9.9"));
    }

    // 9. Cache dir independence.
    [Fact]
    public void DeletingCacheDir_DoesNotResurrectPromptOrLoseDismissal()
    {
        WriteRecord(new CalibrationRecord { PromptedForVersion = "2.1.300", PromptFirstSeen = DateTimeOffset.UtcNow });
        CalibrateCommand.RunCli(false, null, false, null, false, false, false, false, dismiss: true);

        var cacheDir = ClaudeTuiLine.ItemCache.ResolveCacheDir();
        if (Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
        }

        Assert.False(InvokeNudge("2.1.300"));
    }

    // 10. Calibration in progress suppresses the prompt, regardless of record state.
    [Fact]
    public void CalibrationInProgress_SuppressesPrompt_EvenOnFirstRunRecord()
    {
        WriteState(new CalibrationState { Phase = "verify", Candidate = 4, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });

        Assert.False(InvokeNudge("2.1.300"));
        Assert.False(File.Exists(RecordPath));
    }

    private string RecordPath => ClaudeTuiLineShared.ConfigPath.ResolveCalibrationRecordPath()!;

    private static bool InvokeNudge(string? version, bool calibrationPromptEnabled = true) =>
        CaptureNudgeLine(version, surfaceWidth: 100, calibrationPromptEnabled) is not null;

    private static string? CaptureNudgeLine(string? version, int surfaceWidth, bool calibrationPromptEnabled = true)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            CalibrateCommand.MaybeAppendNudge(version, calibrationPromptEnabled, surfaceWidth);
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? null : lines[0];
    }

    private void WriteState(CalibrationState state)
    {
        var path = ClaudeTuiLineShared.ConfigPath.ResolveCalibrationStatePath()!;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, CalibrateJsonContext.Default.CalibrationState);
        File.WriteAllBytes(path, bytes);
    }

    private void WriteRecord(CalibrationRecord record)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, CalibrateJsonContext.Default.CalibrationRecord);
        File.WriteAllBytes(RecordPath, bytes);
    }
}
