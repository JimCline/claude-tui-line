using ClaudeTuiLineShared;
using Spectre.Console;

namespace ClaudeTuiLine;

// SPEC-47 §5.0.2: relocated out of Program.cs's top-level statements so the config-loading path
// is reachable from tests via InternalsVisibleTo — those local functions were implicitly private
// regardless of what InternalsVisibleTo grants. Pure move: no signature or behavior changes here.
internal static class ConfigResolution
{
    // SPEC-V2-FRAMEWORK.md §9.2.1: the render path's config-loading step. An explicit --config, or a
    // config found by the §5 search order, that turns out unreadable (missing when asserted, or
    // present but unparseable) is reported via UnreadableReason rather than silently replaced by
    // defaults; only "no --config, nothing at any searched path" (row 1) is a legitimate default.
    internal static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath, string? UnreadableReason, int UnreadableReasonProtectedLength) LoadRenderConfig(string? explicitConfigPath)
    {
        var configPath = explicitConfigPath ?? ConfigPath.ResolveConfigPath();
        UserConfig? config = null;

        if (configPath is not null)
        {
            var result = ConfigLoader.ReadConfigForCheck(configPath);

            if (result.Status == ConfigReadStatus.ParseError)
            {
                var (fallbackTopLevel, fallbackPane) = BuildFallbackConfig();
                var (reason, protectedLength) = ComposeUnreadableReason(result);
                return (fallbackTopLevel, fallbackPane, configPath, reason, protectedLength);
            }

            if (result.Status == ConfigReadStatus.NoFile)
            {
                if (explicitConfigPath is not null)
                {
                    var (fallbackTopLevel, fallbackPane) = BuildFallbackConfig();
                    return (fallbackTopLevel, fallbackPane, configPath, "no such file", 0);
                }

                configPath = null; // §9.2.1 row 1: nothing at the searched path, and none asserted.
            }
            else
            {
                config = result.Config;
            }
        }

        try
        {
            var topLevel = ConfigLoader.ResolveTopLevel(config);
            var rootPane = ConfigLoader.ResolveRootPane(config, topLevel);
            return (topLevel, rootPane, configPath, null, 0);
        }
        catch
        {
            var (fallbackTopLevel, fallbackPane) = BuildFallbackConfig();
            return (fallbackTopLevel, fallbackPane, configPath, null, 0);
        }
    }

    // SPEC-V2-FRAMEWORK.md §9.2.2: "line <n>, <path within the document>: <message>", read from
    // JsonException's typed LineNumber/Path rather than scraped from Message text .NET is free to
    // reword. LineNumber is 0-indexed in the CLR API; +1 matches the line a text editor shows, since
    // that's the file position this exists to let a reader jump to. Only "line <n>" is irreplaceable
    // — the Pointer is what a reader sees the moment they open the file at that line, and the message
    // is .NET's own wording — so the protected length covers the line number alone; the caller's
    // rung-4 truncation eats the Pointer and message first, and the line number only once nothing
    // else is left to give up.
    internal static (string Reason, int ProtectedLength) ComposeUnreadableReason(ConfigReadResult result)
    {
        var message = result.ErrorMessage ?? "could not be parsed";
        if (result.ErrorLineNumber is not { } lineNumber || result.ErrorJsonPath is not { } jsonPath)
        {
            return (message, 0);
        }

        var lineNumberText = $"line {lineNumber + 1}";
        return ($"{lineNumberText}, {jsonPath}: {message}", lineNumberText.Length);
    }

    private static (ResolvedConfig TopLevel, Pane RootPane) BuildFallbackConfig()
    {
        var fallbackTopLevel = new ResolvedConfig(
            new ColorResolution.ColorExpr.Literal("grey"),
            BoxBorder.Rounded,
            PaneBorderEdges.All,
            ConfigLoader.DefaultChromeReserve,
            ColorSystemSupport.Standard,
            new Dictionary<string, ColorResolution.ColorRule>());
        var fallbackPane = new Pane(
            PaneSplit.None,
            Array.Empty<Pane>(),
            "auto",
            new PaneBorder(fallbackTopLevel.BorderColor, fallbackTopLevel.Style, fallbackTopLevel.Edges),
            null,
            ConfigLoader.DefaultEllipsis,
            null,
            Array.Empty<PaneItem>());
        return (fallbackTopLevel, fallbackPane);
    }
}
