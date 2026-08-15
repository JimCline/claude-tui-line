using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace ClaudeTuiLineMcp;

/// <summary>
/// SPEC-12.6-mcp-tools.md §2.2/§10 (first slice): <c>get_config</c> and <c>set_config</c> only.
/// <see cref="BackupLedger"/> is taken as a method parameter rather than looked up internally so
/// tests can call these methods directly with a temp-directory ledger and never go through
/// <see cref="BackupLedgerFactory"/> (§9.7/V6).
/// </summary>
[McpServerToolType]
public sealed class ConfigTools
{
    [McpServerTool(Name = "get_config")]
    [Description(
        "Return the current claude-tui-line config, the path it was read from, and a revision " +
        "hash for compare-and-swap with set_config. revision is \"absent\" when no config file " +
        "exists yet. Fails with code \"cli-not-found\" if the claude-tui-line CLI cannot be " +
        "located — point the user at /claude-tui-line:setup rather than improvising.")]
    public static async Task<object> GetConfig(
        [Description("Explicit config file path, overriding the normal search order.")] string? configPath = null)
    {
        var presence = await CliRunner.ProbePresenceAsync().ConfigureAwait(false);
        if (!presence.Found)
        {
            return McpResults.CliNotFound(presence.SearchedPaths);
        }

        var (resolvedPath, source) = ResolveConfigPath(configPath);
        if (resolvedPath is null)
        {
            return new { ok = true, config = (object?)null, configPath = (string?)null, source, revision = "absent" };
        }

        var bytes = File.Exists(resolvedPath) ? File.ReadAllBytes(resolvedPath) : null;
        var config = bytes is null ? null : JsonNode.Parse(bytes);
        var revision = ConfigFile.ComputeRevision(bytes);

        return new { ok = true, config, configPath = resolvedPath, source, revision };
    }

    [McpServerTool(Name = "set_config")]
    [Description(
        "Write a full claude-tui-line config. Validates via --check before committing; a " +
        "candidate that fails validation is rejected and the config on disk is left untouched. " +
        "Checkpoints every artifact (settings.json's statusLine, its referenced script if any, " +
        "and the config) before writing, so a failed checkpoint (code \"checkpoint-failed\") means " +
        "nothing was written. If baseRevision is supplied and no longer matches the file on disk, " +
        "the write is refused with code \"stale-revision\" and the refusal's payload carries the " +
        "CURRENT config and revision — re-derive the intended change against that payload; do not " +
        "resend the original config with a freshly-fetched revision, since that defeats the " +
        "refusal and silently clobbers the intervening write. Fails with \"cli-not-found\" if the " +
        "CLI cannot be located.")]
    public static async Task<object> SetConfig(
        BackupLedger ledger,
        [Description("The full config object to write.")] JsonNode config,
        [Description("Explicit config file path, overriding the normal search order.")] string? configPath = null,
        [Description("The revision last read via get_config (or \"absent\" for a first write). Optional; when supplied and stale, the write is refused.")] string? baseRevision = null)
    {
        var (resolvedPath, source) = ResolveConfigPath(configPath);
        if (resolvedPath is null)
        {
            return McpResults.Failure("checkpoint-failed", "no config path could be resolved: $HOME is not set and no explicit configPath was given.", null);
        }

        var existingBytes = File.Exists(resolvedPath) ? File.ReadAllBytes(resolvedPath) : null;
        var currentRevision = ConfigFile.ComputeRevision(existingBytes);

        if (baseRevision is not null && baseRevision != currentRevision)
        {
            var currentConfig = existingBytes is null ? null : JsonNode.Parse(existingBytes);
            return new
            {
                ok = false,
                code = "stale-revision",
                message = "the config on disk has changed since baseRevision was read; re-derive the intended change against the config in this payload rather than resending the original.",
                config = currentConfig,
                revision = currentRevision,
                configPath = resolvedPath,
                source,
            };
        }

        var candidateBytes = JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions { WriteIndented = true });
        var candidatePath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(resolvedPath)) ?? ".",
            $".{Path.GetFileName(resolvedPath)}.mcp-candidate.{Guid.NewGuid():N}.tmp");

        File.WriteAllBytes(candidatePath, candidateBytes);
        try
        {
            var checkResult = await CliRunner.RunCheckAsync(candidatePath).ConfigureAwait(false);
            if (!checkResult.CliFound)
            {
                return McpResults.CliNotFound(checkResult.SearchedPaths);
            }

            var diagnostics = checkResult.Payload?["diagnostics"]?.DeepClone() ?? new JsonArray();
            var checkOk = checkResult.Payload?["ok"]?.GetValue<bool>() ?? false;

            if (!checkOk)
            {
                return new
                {
                    ok = false,
                    code = "invalid-config",
                    message = "the candidate config failed validation; it was not written.",
                    diagnostics,
                    configPath = resolvedPath,
                    source,
                };
            }

            // SPEC-12.6-mcp-tools.md §5 (N1/N2 consequences), A2: validate -> checkpoint -> write.
            // The checkpoint runs only after validation succeeds (a rejected candidate needs no
            // checkpoint) and strictly before the real file is touched.
            var checkpoint = ledger.WriteCheckpoint(resolvedPath);
            if (!checkpoint.Ok)
            {
                return McpResults.Failure("checkpoint-failed", checkpoint.FailedMessage ?? "the checkpoint could not be written; nothing was changed.", checkpoint.FailedPath);
            }

            ConfigFile.WriteAtomic(resolvedPath, candidateBytes);
            var newRevision = ConfigFile.ComputeRevision(candidateBytes);

            return new
            {
                ok = true,
                diagnostics,
                revision = newRevision,
                checkpoint = checkpoint.EntryJson,
                configPath = resolvedPath,
                source,
            };
        }
        finally
        {
            try
            {
                File.Delete(candidatePath);
            }
            catch
            {
                // best-effort cleanup of a transient validation file; nothing downstream depends on it.
            }
        }
    }

    /// <summary>SPEC-84-mcp-schema-explorer.md §5.1: the section names an agent may request via <see cref="GetConfigSchema"/>'s <c>sections</c> filter.</summary>
    private static readonly string[] ValidSchemaSections = { "items", "colors", "accepted", "structures", "kindSupport" };

    [McpServerTool(Name = "get_config_schema")]
    [Description(
        "The claude-tui-line config schema, read live from the installed binary: every item kind and "
        + "its required/optional keys, every config key's accepted values, the recommended colour "
        + "names, and the structural shape of a config document (root, pane, split, item, colour "
        + "rule). Use this before writing or editing a config instead of consulting documentation — "
        + "documentation can be stale, this is what the binary actually accepts.")]
    public static async Task<object> GetConfigSchema(
        [Description("Limit the response to these sections: items, colors, accepted, structures, kindSupport. Omit for all.")]
        string[]? sections = null)
    {
        var result = await CliRunner.RunSchemaAsync().ConfigureAwait(false);
        if (!result.CliFound)
        {
            return McpResults.CliNotFound(result.SearchedPaths);
        }

        if (result.ExitCode != 0 || result.Payload is not JsonObject envelope)
        {
            return McpResults.Failure(
                "schema-unavailable",
                $"claude-tui-line --schema --json exited {result.ExitCode} or produced output that did not parse as JSON; the schema is not available.",
                null);
        }

        if (sections is null || sections.Length == 0)
        {
            return envelope;
        }

        var unknown = sections.Where(s => !ValidSchemaSections.Contains(s, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            return McpResults.Failure(
                "unknown-section",
                $"unrecognized section(s): {string.Join(", ", unknown)}. Valid sections: {string.Join(", ", ValidSchemaSections)}.",
                null);
        }

        var filtered = new JsonObject { ["version"] = envelope["version"]?.DeepClone() };
        foreach (var section in sections)
        {
            filtered[section] = envelope[section]?.DeepClone();
        }

        return filtered;
    }

    // SPEC-V2-FRAMEWORK.md §12.6.2: an explicit configPath argument overrides resolution outright.
    // Otherwise mirrors ConfigPath.ResolveConfigPath()'s search order ($CLAUDE_TUI_LINE_CONFIG,
    // then $HOME/.claude/claude-tui-line.json) so source can report which branch fired — "env",
    // "default", or "none" per §12.6.2; "explicit" is this implementation's extension for the
    // caller-supplied-path case, which §12.6.2's enumeration does not itself cover.
    private static (string? Path, string Source) ResolveConfigPath(string? explicitConfigPath)
    {
        if (!string.IsNullOrEmpty(explicitConfigPath))
        {
            return (explicitConfigPath, "explicit");
        }

        var envOverride = Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG");
        var home = Environment.GetEnvironmentVariable("HOME");
        var resolved = ClaudeTuiLineShared.ConfigPath.ResolveConfigPath(envOverride, home);

        if (resolved is null)
        {
            return (null, "none");
        }

        var source = !string.IsNullOrEmpty(envOverride) ? "env" : "default";
        return (resolved, source);
    }
}

internal static class McpResults
{
    public static object CliNotFound(IReadOnlyList<string> searchedPaths) => new
    {
        ok = false,
        code = "cli-not-found",
        message = "the claude-tui-line CLI could not be found; point the user at /claude-tui-line:setup.",
        searchedPaths,
    };

    public static object Failure(string code, string message, string? path) => new
    {
        ok = false,
        code,
        message,
        path,
    };
}
