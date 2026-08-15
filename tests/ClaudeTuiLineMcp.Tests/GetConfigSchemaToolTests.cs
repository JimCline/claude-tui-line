using System.Text.Json;

namespace ClaudeTuiLineMcp.Tests;

/// <summary>SPEC-84-mcp-schema-explorer.md §7 V7-V10.</summary>
public sealed class GetConfigSchemaToolTests : IDisposable
{
    private readonly TestCliFixture _cli = new();

    public void Dispose() => _cli.Dispose();

    private static JsonElement AsJson(object result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

    // §7 V7: AllowListTests V4/V4b (no ProjectReference to ClaudeTuiLine.csproj, no
    // ClaudeTuiLine.* reference outside ConfigLoader.ResolveConfigPath) are asserted by that
    // file directly and run unmodified as part of the same test project; this test exists only
    // to document that get_config_schema's CLI-spawn design was chosen specifically to keep
    // those static checks passing, not to re-implement them.
    [Fact]
    public async Task V7_GetConfigSchemaSpawnsTheCliRatherThanLinkingTheCoreInProcess()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema());

        Assert.True(result.GetProperty("version").GetString() == "test");
    }

    [Fact]
    public async Task V8_NoCliFailsWithCliNotFoundAndSearchedPaths()
    {
        _cli.RemoveCli();

        var result = AsJson(await ConfigTools.GetConfigSchema());

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("cli-not-found", result.GetProperty("code").GetString());
        Assert.True(result.GetProperty("searchedPaths").GetArrayLength() > 0);
    }

    [Fact]
    public async Task V9_SectionsFilterReturnsOnlyVersionAndTheRequestedSections()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(new[] { "accepted" }));

        Assert.Equal("test", result.GetProperty("version").GetString());
        Assert.True(result.TryGetProperty("accepted", out _));
        Assert.False(result.TryGetProperty("items", out _));
        Assert.False(result.TryGetProperty("colors", out _));
        Assert.False(result.TryGetProperty("structures", out _));
        Assert.False(result.TryGetProperty("kindSupport", out _));
    }

    [Fact]
    public async Task V10_AnUnrecognizedSectionNameFailsWithTheValidNamesListed()
    {
        var result = AsJson(await ConfigTools.GetConfigSchema(new[] { "colours" }));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown-section", result.GetProperty("code").GetString());
        var message = result.GetProperty("message").GetString()!;
        Assert.Contains("colours", message);
        Assert.Contains("items", message);
        Assert.Contains("colors", message);
        Assert.Contains("accepted", message);
        Assert.Contains("structures", message);
        Assert.Contains("kindSupport", message);
    }
}
