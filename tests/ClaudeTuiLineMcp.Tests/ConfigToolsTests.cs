using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeTuiLineMcp;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>SPEC-12.6-mcp-tools.md §10 V1, V2, V3, V5, plus a happy-path round trip.</summary>
public sealed class ConfigToolsTests : IDisposable
{
    private readonly TestCliFixture _cli = new();
    private readonly string _configPath;
    private readonly BackupLedger _ledger;

    public ConfigToolsTests()
    {
        _configPath = Path.Combine(_cli.TempRoot, "claude-tui-line.json");
        var backupRoot = Path.Combine(_cli.TempRoot, "backups");
        var settingsPath = Path.Combine(_cli.TempRoot, "settings.json");
        _ledger = new BackupLedger(backupRoot, settingsPath);
    }

    public void Dispose() => _cli.Dispose();

    private static JsonElement AsJson(object result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

    [Fact]
    public async Task V1_RevisionChangesOnAFormattingOnlyEdit()
    {
        File.WriteAllText(_configPath, """{"border":{"style":"rounded"}}""");
        var first = AsJson(await ConfigTools.GetConfig(_configPath));
        var firstRevision = first.GetProperty("revision").GetString();

        // Formatting-only edit: same content, different whitespace/key order is not attempted
        // here (JSON key order isn't semantically distinguishable via this config), but adding
        // whitespace alone must still change the byte-level revision (§4 item 3).
        File.WriteAllText(_configPath, """{"border": {"style": "rounded"}}""");
        var second = AsJson(await ConfigTools.GetConfig(_configPath));
        var secondRevision = second.GetProperty("revision").GetString();

        Assert.NotEqual(firstRevision, secondRevision);
    }

    [Fact]
    public async Task V2_NoConfigFileReturnsRevisionAbsentNotErrorNotNull()
    {
        var result = AsJson(await ConfigTools.GetConfig(_configPath));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("absent", result.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task V3_NoCliFailsBothToolsWithSearchedPaths()
    {
        _cli.RemoveCli();

        var getResult = AsJson(await ConfigTools.GetConfig(_configPath));
        Assert.False(getResult.GetProperty("ok").GetBoolean());
        Assert.Equal("cli-not-found", getResult.GetProperty("code").GetString());
        Assert.True(getResult.GetProperty("searchedPaths").GetArrayLength() > 0);

        var configNode = JsonNode.Parse("""{"border":{"style":"rounded"}}""")!;
        var setResult = AsJson(await ConfigTools.SetConfig(_ledger, configNode, _configPath));
        Assert.False(setResult.GetProperty("ok").GetBoolean());
        Assert.Equal("cli-not-found", setResult.GetProperty("code").GetString());
        Assert.True(setResult.GetProperty("searchedPaths").GetArrayLength() > 0);
    }

    [Fact]
    public async Task V5_StaleBaseRevisionRefusesWithCurrentConfigAndRevisionInPayload()
    {
        File.WriteAllText(_configPath, """{"border":{"style":"rounded"}}""");
        var staleRevision = ConfigFile.ComputeRevision(File.ReadAllBytes(_configPath));

        // Someone else changes the file after the caller's stale read.
        File.WriteAllText(_configPath, """{"border":{"style":"double"}}""");
        var currentBytes = File.ReadAllBytes(_configPath);
        var currentRevision = ConfigFile.ComputeRevision(currentBytes);

        var candidate = JsonNode.Parse("""{"border":{"style":"heavy"}}""")!;
        var result = AsJson(await ConfigTools.SetConfig(_ledger, candidate, _configPath, staleRevision));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("stale-revision", result.GetProperty("code").GetString());
        Assert.Equal(currentRevision, result.GetProperty("revision").GetString());
        var payloadConfig = result.GetProperty("config");
        Assert.Equal("double", payloadConfig.GetProperty("border").GetProperty("style").GetString());

        // The refusal must not have written anything.
        Assert.Equal(currentBytes, File.ReadAllBytes(_configPath));
    }

    [Fact]
    public async Task SetConfig_HappyPath_ValidatesChecksAndWritesAtomically()
    {
        var candidate = JsonNode.Parse("""{"border":{"style":"rounded"}}""")!;
        var result = AsJson(await ConfigTools.SetConfig(_ledger, candidate, _configPath));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.True(File.Exists(_configPath));
        var written = JsonNode.Parse(File.ReadAllBytes(_configPath))!.AsObject();
        Assert.Equal("rounded", written["border"]!["style"]!.GetValue<string>());

        // A checkpoint entry was written before the config write.
        var ledgerPath = Path.Combine(_ledger.BackupRoot, "ledger.jsonl");
        Assert.True(File.Exists(ledgerPath));
    }

    [Fact]
    public async Task SetConfig_InvalidCandidateIsRejectedAndLeavesExistingConfigUntouched()
    {
        File.WriteAllText(_configPath, """{"border":{"style":"rounded"}}""");
        var originalBytes = File.ReadAllBytes(_configPath);

        // The fake CLI treats this literal marker as an invalid config.
        var candidate = JsonNode.Parse("""{"note":"FORCE_INVALID"}""")!;
        var result = AsJson(await ConfigTools.SetConfig(_ledger, candidate, _configPath));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal(originalBytes, File.ReadAllBytes(_configPath));
    }
}
