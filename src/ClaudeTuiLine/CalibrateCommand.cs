using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClaudeTuiLineShared;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-101-calibrate-chrome-reserve.md: `--calibrate` measures the real chrome budget on this
/// machine/Claude Code version and writes it to `layout.chromeReserve`. Two files back this: a
/// transient calibration-in-progress state (§6.1, 30-minute expiry) and a durable record of what
/// was calibrated/prompted/dismissed (§12.2, no expiry) — deliberately not merged, since a corrupt
/// transient file should cost only a re-run, while losing the record resurrects the first-run
/// prompt and forgets a dismissal.
/// </summary>
public static class CalibrateCommand
{
    private const int PromptWindowDays = 7;

    // §3.1: the ruler. Character at 1-based emitted column i is the ASCII digit (i mod 10).
    public static string BuildRulerRow(int columns)
    {
        if (columns <= 0)
        {
            return string.Empty;
        }

        var chars = new char[columns];
        for (var i = 1; i <= columns; i++)
        {
            chars[i - 1] = (char)('0' + (i % 10));
        }

        return new string(chars);
    }

    // §3.3: P = COLUMNS - 1 - ((COLUMNS - 1 - d) mod 10); chromeReserve = COLUMNS - P - 1 (§3.2).
    // No indent term anywhere in this arithmetic — see §3.2's warning. If you find yourself adding
    // one, you have reintroduced SPEC-98's bug.
    public static int ResolveReserveFromDigit(int columns, int digit)
    {
        var mod = ((columns - 1 - digit) % 10 + 10) % 10;
        var p = (columns - 1) - mod;
        return columns - p - 1;
    }

    // §3.4/§3.5: never ends in whitespace, plain ASCII except the one non-ASCII glyph (─, U+2500;
    // see SPEC-101 §9 E1 — fall back to ASCII '-' if a real pane shows it mangled or wide).
    public static string BuildVerifyRow(int width, char marker)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (width == 1)
        {
            return marker.ToString();
        }

        return marker + new string('─', width - 2) + marker;
    }

    /// <summary>
    /// §5.1: called immediately after the stdin read and before ParseInput. Returns an exit code
    /// when a probe row was emitted (the caller must return it immediately, without touching
    /// stdin's parsed contents further), or null when no calibration is active and the caller
    /// should proceed with the normal render.
    /// </summary>
    public static int? TryRunHookProbe()
    {
        var state = LoadState();
        if (state is null || IsExpired(state))
        {
            return null;
        }

        var columnsEnv = Environment.GetEnvironmentVariable("COLUMNS");
        if (!int.TryParse(columnsEnv, out var columns) || columns <= 0)
        {
            Console.Out.WriteLine("claude-tui-line: calibration needs COLUMNS");
            return 0;
        }

        // §5.4: the hook records the COLUMNS it saw so a later --saw (a different terminal
        // process, possibly a different width) computes the reserve from what was actually
        // rendered rather than from its own environment.
        if (state.Phase == "ruler" && state.ObservedColumns is null)
        {
            state.ObservedColumns = columns;
            WriteState(state);
        }

        var showLabel = columns >= 70;
        var rows = new List<string>();

        if (state.Phase == "verify" && state.Candidate is int r)
        {
            if (showLabel)
            {
                rows.Add("ctl calibrate: does row A end in \"A\", row B in \"…\"?");
            }

            rows.Add(BuildVerifyRow(Math.Max(0, columns - r), 'A'));
            rows.Add(BuildVerifyRow(Math.Max(0, columns - r + 1), 'B'));
        }
        else
        {
            if (showLabel)
            {
                rows.Add("ctl calibrate: report last readable digit");
            }

            rows.Add(BuildRulerRow(columns));
        }

        foreach (var row in rows)
        {
            Console.Out.WriteLine(row);
        }

        return 0;
    }

    /// <summary>
    /// §12: called late in RunAsync, immediately before rows are written to stdout — the opposite
    /// end from <see cref="TryRunHookProbe"/>, and mutually exclusive with it via §12.3 rule 2.
    /// </summary>
    public static void MaybeAppendNudge(string? version, bool calibrationPromptEnabled, int? surfaceWidth)
    {
        // §12.7 rule 1: config toggle checked first (already in memory) so an opted-out user pays
        // nothing beyond the boolean check.
        if (!calibrationPromptEnabled)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_NO_NUDGE")))
        {
            return;
        }

        var state = LoadState();
        if (state is not null && !IsExpired(state))
        {
            return; // rule 2: a calibration is already in progress.
        }

        var now = DateTimeOffset.UtcNow;
        var record = LoadRecord();
        bool shouldPrompt;

        if (record is null)
        {
            shouldPrompt = true; // rule 3: first run on this machine.
            WriteRecord(new CalibrationRecord { PromptedForVersion = version, PromptFirstSeen = now });
        }
        else if (version is null)
        {
            shouldPrompt = false; // rule 4: unknown version can't establish a change.
        }
        else if (version == record.CalibratedVersion)
        {
            shouldPrompt = false; // rule 5: already calibrated for this version.
        }
        else if (version == record.DismissedVersion)
        {
            shouldPrompt = false; // rule 6: declined for this version.
        }
        else if (record.PromptedForVersion == version
                 && record.PromptFirstSeen is DateTimeOffset firstSeen
                 && now - firstSeen > TimeSpan.FromDays(PromptWindowDays))
        {
            shouldPrompt = false; // rule 7: ignored for the window; treat as a decline.
        }
        else
        {
            shouldPrompt = true; // rule 8.

            // §12.3: record writes are rare, not per-render — only when the version changed.
            if (record.PromptedForVersion != version)
            {
                record.PromptedForVersion = version;
                record.PromptFirstSeen = now;
                WriteRecord(record);
            }
        }

        if (!shouldPrompt)
        {
            return;
        }

        // §12.4: suppressed (not truncated) below width 50 — the nudge reports a possibly-wrong
        // reserve, so it can be truncated by the exact condition it is reporting.
        if (surfaceWidth is not int width || width < 50)
        {
            return;
        }

        Console.Out.WriteLine("calibrate widths: claude-tui-line --calibrate");
    }

    public static int RunCli(bool json, string? saw, bool noEllipsis, string? set, bool confirm, bool reject, bool cancel, bool status, bool dismiss)
    {
        var subFlagCount = new[] { saw is not null, noEllipsis, set is not null, confirm, reject, cancel, status, dismiss }.Count(b => b);
        if (subFlagCount > 1)
        {
            return WriteError(json, "only one of --saw/--no-ellipsis/--set/--confirm/--reject/--cancel/--status/--dismiss may be given");
        }

        if (status)
        {
            return RunStatus(json);
        }

        if (dismiss)
        {
            return RunDismiss(json);
        }

        if (cancel)
        {
            ClearState();
            Print(json, "cancelled", "calibration cancelled; nothing was written.");
            return 0;
        }

        if (saw is not null)
        {
            return RunSaw(json, saw);
        }

        if (noEllipsis)
        {
            return RunNoEllipsis(json);
        }

        if (set is not null)
        {
            return RunSet(json, set);
        }

        if (confirm)
        {
            return RunConfirm(json);
        }

        if (reject)
        {
            return RunReject(json);
        }

        // Bare --calibrate: (re)start. §6: restarts/overwrites any calibration in progress.
        var state = new CalibrationState { Phase = "ruler", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) };
        WriteState(state);
        Print(
            json,
            "ruler",
            "calibration started. Cause a statusline redraw in a live Claude Code session (the " +
            "statusline updates on activity, not on file change), then look at the ruler row and " +
            "report the last digit you can read before it is cut off: " +
            "claude-tui-line --calibrate --saw <digit>. If nothing was cut off: " +
            "claude-tui-line --calibrate --no-ellipsis");
        return 0;
    }

    private static int RunSaw(bool json, string sawArg)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "ruler")
        {
            return WriteError(json, "--saw requires an active ruler-phase calibration; run --calibrate first.");
        }

        if (state.ObservedColumns is not int observedColumns)
        {
            return WriteError(json, "no ruler has rendered yet — cause a statusline redraw in Claude Code first, then retry.");
        }

        if (!int.TryParse(sawArg, out var digit) || digit < 0 || digit > 9)
        {
            return WriteError(json, "--saw requires a single digit 0-9");
        }

        var candidate = ResolveReserveFromDigit(observedColumns, digit);
        state.Phase = "verify";
        state.Candidate = candidate;
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        WriteState(state);

        Print(
            json,
            "verify",
            $"candidate reserve: {candidate}. Cause another statusline redraw, then check row A (should " +
            "end in \"A\", no ellipsis) and row B (should end in an ellipsis). Both as expected: " +
            "claude-tui-line --calibrate --confirm. Otherwise: claude-tui-line --calibrate --reject");
        return 0;
    }

    private static int RunNoEllipsis(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "ruler")
        {
            return WriteError(json, "--no-ellipsis requires an active ruler-phase calibration; run --calibrate first.");
        }

        // §6.5: a reserve of 0 is legitimate but also what a stale state file, a non-redrawn
        // statusline, or a terminal wider than COLUMNS looks like — report it, write nothing.
        ClearState();
        Print(
            json,
            "no-ellipsis",
            "reported reserve: 0. Nothing was written. A reserve of 0 is possible, but this also " +
            "matches a stale calibration, a statusline that never redrew, or a terminal wider than " +
            "COLUMNS — re-run --calibrate if you're not sure this is real.");
        return 0;
    }

    private static int RunSet(bool json, string setArg)
    {
        if (!int.TryParse(setArg, out var value) || value < 0 || value > 9)
        {
            return WriteError(json, "--set requires a digit 0-9");
        }

        var state = LoadState() ?? new CalibrationState();
        state.Phase = "verify";
        state.Candidate = value;
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        WriteState(state);

        Print(
            json,
            "verify",
            $"candidate reserve: {value} (set manually). Cause a statusline redraw, then check row A/row B " +
            "as usual: claude-tui-line --calibrate --confirm or claude-tui-line --calibrate --reject");
        return 0;
    }

    private static int RunConfirm(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "verify" || state.Candidate is not int candidate)
        {
            return WriteError(json, "--confirm requires an active verify-phase calibration; run --calibrate first.");
        }

        var (ok, error, oldValue) = WriteChromeReserveToConfig(candidate);
        if (!ok)
        {
            return WriteError(json, error ?? "could not write config");
        }

        ClearState();

        // §12.6: --confirm implicitly dismisses by setting calibratedVersion — copied from the
        // record's promptedForVersion, never discovered independently (the CLI does not know the
        // Claude Code version; it only ever arrives on the hook's stdin).
        var record = LoadRecord() ?? new CalibrationRecord();
        record.CalibratedVersion = record.PromptedForVersion;
        record.CalibratedReserve = candidate;
        WriteRecord(record);

        Print(json, "confirmed", $"chromeReserve: {(oldValue?.ToString() ?? "unset, default 4")} -> {candidate}");
        return 0;
    }

    private static int RunReject(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state) || state.Phase != "verify" || state.Candidate is not int candidate)
        {
            return WriteError(json, "--reject requires an active verify-phase calibration; run --calibrate first.");
        }

        // §6.6: print a diagnosis, never auto-advance the candidate — a failed verify is a broken
        // assumption, and stepping the candidate ±1 would hide exactly the signal this is for.
        ClearState();
        Print(
            json,
            "rejected",
            $"verify failed. Row A truncated: the true reserve is larger than {candidate} — retry with " +
            $"claude-tui-line --calibrate --set {candidate + 1}. Row B NOT truncated: the true reserve is " +
            $"smaller — retry with claude-tui-line --calibrate --set {candidate - 1}. Both wrong, or " +
            "neither row visible: the ellipsis may no longer replace the boundary cell, or the statusline " +
            "did not redraw — see SPEC-98 §2.");
        return 0;
    }

    private static int RunStatus(bool json)
    {
        var state = LoadState();
        if (state is null || IsExpired(state))
        {
            Print(json, "none", "no calibration in progress.");
            return 0;
        }

        Print(json, state.Phase ?? "unknown", $"phase={state.Phase}, candidate={(state.Candidate?.ToString() ?? "none")}");
        return 0;
    }

    private static int RunDismiss(bool json)
    {
        // §12.6: --dismiss copies promptedForVersion from the record; it must never attempt to
        // discover the Claude Code version itself.
        var record = LoadRecord();
        if (record?.PromptedForVersion is not string version)
        {
            Print(json, "nothing-to-dismiss", "no active prompt to dismiss.");
            return 0;
        }

        record.DismissedVersion = version;
        WriteRecord(record);
        Print(json, "dismissed", $"dismissed the calibration prompt for version {version}.");
        return 0;
    }

    private static (bool Ok, string? Error, int? OldValue) WriteChromeReserveToConfig(int newValue)
    {
        var configPath = ConfigPath.ResolveConfigPath();
        if (configPath is null)
        {
            return (false, "no config path could be resolved ($HOME is not set)", null);
        }

        JsonObject obj;
        int? oldValue = null;

        if (File.Exists(configPath))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(configPath);
            }
            catch (Exception ex)
            {
                return (false, $"could not read {configPath}: {ex.Message}", null);
            }

            // §6.4: JsonNode, never a typed model — a typed round-trip silently drops unknown keys.
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(bytes);
            }
            catch (Exception ex)
            {
                return (false, $"{configPath} does not parse ({ex.Message}); fix it before calibrating — refusing to overwrite a config we could not read.", null);
            }

            obj = parsed as JsonObject ?? new JsonObject();
            if (obj["layout"] is JsonObject existingLayout
                && existingLayout["chromeReserve"] is JsonValue existingValue
                && existingValue.TryGetValue<int>(out var existingInt))
            {
                oldValue = existingInt;
            }
        }
        else
        {
            obj = new JsonObject();
        }

        if (obj["layout"] is not JsonObject layout)
        {
            layout = new JsonObject();
            obj["layout"] = layout;
        }

        layout["chromeReserve"] = newValue;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var outBytes = System.Text.Encoding.UTF8.GetBytes(obj.ToJsonString(options));
        ConfigWriter.WriteAtomic(configPath, outBytes);
        return (true, null, oldValue);
    }

    private static bool IsExpired(CalibrationState state) =>
        state.ExpiresAt is DateTimeOffset expires && DateTimeOffset.UtcNow > expires;

    private static CalibrationState? LoadState()
    {
        var path = ConfigPath.ResolveCalibrationStatePath();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize(bytes, CalibrateJsonContext.Default.CalibrationState);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(CalibrationState state)
    {
        var path = ConfigPath.ResolveCalibrationStatePath();
        if (path is null)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, CalibrateJsonContext.Default.CalibrationState);
        ConfigWriter.WriteAtomic(path, bytes);
    }

    private static void ClearState()
    {
        var path = ConfigPath.ResolveCalibrationStatePath();
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // best-effort: a calibration state file is transient by design (§6.1/§6.7's own expiry).
        }
    }

    private static CalibrationRecord? LoadRecord()
    {
        var path = ConfigPath.ResolveCalibrationRecordPath();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize(bytes, CalibrateJsonContext.Default.CalibrationRecord);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteRecord(CalibrationRecord record)
    {
        var path = ConfigPath.ResolveCalibrationRecordPath();
        if (path is null)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, CalibrateJsonContext.Default.CalibrationRecord);
        ConfigWriter.WriteAtomic(path, bytes);
    }

    private static void Print(bool json, string phase, string message)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new CalibrateResultJson(true, phase, message), CalibrateJsonContext.Default.CalibrateResultJson));
        }
        else
        {
            Console.Out.WriteLine($"claude-tui-line: {message}");
        }
    }

    private static int WriteError(bool json, string message)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new CalibrateResultJson(false, "error", message), CalibrateJsonContext.Default.CalibrateResultJson));
        }
        else
        {
            Console.Error.WriteLine($"claude-tui-line: {message}");
        }

        return 2;
    }
}

public sealed class CalibrationState
{
    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("observedColumns")]
    public int? ObservedColumns { get; set; }

    [JsonPropertyName("candidate")]
    public int? Candidate { get; set; }
}

public sealed class CalibrationRecord
{
    [JsonPropertyName("calibratedVersion")]
    public string? CalibratedVersion { get; set; }

    [JsonPropertyName("calibratedReserve")]
    public int? CalibratedReserve { get; set; }

    [JsonPropertyName("promptedForVersion")]
    public string? PromptedForVersion { get; set; }

    [JsonPropertyName("promptFirstSeen")]
    public DateTimeOffset? PromptFirstSeen { get; set; }

    [JsonPropertyName("dismissedVersion")]
    public string? DismissedVersion { get; set; }
}

public sealed record CalibrateResultJson(bool Ok, string Phase, string Message);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CalibrationState))]
[JsonSerializable(typeof(CalibrationRecord))]
[JsonSerializable(typeof(CalibrateResultJson))]
public partial class CalibrateJsonContext : JsonSerializerContext
{
}
