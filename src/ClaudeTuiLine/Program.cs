using System.Text.Json;
using ClaudeTuiLine;
using Spectre.Console;

return await RunAsync();

static async Task<int> RunAsync()
{
    try
    {
        string? rawInput;
        try
        {
            rawInput = await Console.In.ReadToEndAsync().ConfigureAwait(false);
        }
        catch
        {
            rawInput = null;
        }

        var input = ParseInput(rawInput);

        // Fired as soon as cwd is known (i.e. right after stdin is parsed), not awaited yet —
        // it overlaps the config load, the Engram telemetry read, and segment building below.
        var gitProbeTask = GitBranch.ProbeAsync(input.Cwd);

        var (topLevel, pane) = SafeLoadAll();

        EngramResult? engram;
        try
        {
            engram = EngramTelemetry.Build(input.SessionId, DateTimeOffset.UtcNow);
        }
        catch
        {
            engram = null;
        }

        string? gitBranch;
        try
        {
            gitBranch = await gitProbeTask.ConfigureAwait(false);
        }
        catch
        {
            gitBranch = null;
        }

        var ctx = new ItemContext(input, gitBranch, engram, () => RemoteUrl.Probe(input.Cwd));

        var columnsEnv = Environment.GetEnvironmentVariable("COLUMNS");

        // SurfaceLayout.ComputeWidth is the one place chromeReserve is subtracted (SPEC.md §6
        // "MEASURED"; SPEC-V2-FRAMEWORK.md §2.5: COLUMNS read exactly once, at the surface-sizing
        // root). Everything below this takes a plain width and has no knowledge of COLUMNS.
        var surfaceWidth = SurfaceLayout.ComputeWidth(columnsEnv, topLevel.ChromeReserve);

        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = topLevel.ColorSystem,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(Console.Out),
        });

        // SPEC-V2-FRAMEWORK.md §2.4 rule 5: panes are sized by this file's own arithmetic, not by
        // Spectre — Profile.Width stays at the sentinel for all pane rendering so Spectre never
        // re-wraps a row the pane pipeline deliberately laid out.
        console.Profile.Width = int.MaxValue / 2;

        // §5/§6.5: one values/tokens resolution, up front, shared by sizing, rendering, and border
        // colour alike — needed even on the legacy path below, since a border's colour rule can
        // name a builtin's "from" independent of whether any item is placed.
        var tokens = topLevel.Colors;
        var cacheDir = ItemCache.ResolveCacheDir();
        IReadOnlyDictionary<string, string?> values;
        try
        {
            values = await ItemValueResolver.ResolveAsync(pane, ctx, tokens, rawInput, cacheDir).ConfigureAwait(false);
        }
        catch
        {
            values = ItemValueResolver.Resolve(pane, ctx, tokens);
        }

        // §11: splits only size once a real width is known. A configured split with COLUMNS unset
        // has no budget to divide, so it falls back to the same single-leaf-pane pipeline a
        // no-surface config uses — an engineering default for a combination no acceptance case or
        // required test covers, not a spec-mandated behavior. §8: a root leaf pane with top-level
        // items configured (no surface) also routes here — the split-tree pipeline already renders
        // a single non-split leaf correctly, so a leaf's own items render the same way whether or
        // not it happens to sit inside a split, rather than needing a second items pipeline in the
        // legacy branch below.
        var useSplitPipeline = surfaceWidth is int
            && (pane.Items.Count > 0 || (pane.Split != PaneSplit.None && pane.Children.Count > 0));

        if (useSplitPipeline)
        {
            var resolvedRoot = SizeResolver.Resolve(pane, surfaceWidth!.Value, ctx, values);
            StampPaneWidths(resolvedRoot, input.Cwd, cacheDir);
            var rootContribution = PaneTreeRenderer.Render(resolvedRoot, ctx, values, tokens);
            foreach (var row in rootContribution.Buffer.Rows)
            {
                console.MarkupLine(row.Markup);
            }

            return 0;
        }

        var segments = SegmentBuilder.Build(ctx);
        if (segments.Count == 0)
        {
            return 0;
        }

        // Border reserve is a property of the box being drawn (2 verticals + 2 padding cells),
        // not of the terminal — it reduces the surface to the pane's own content budget
        // (SPEC-V2-FRAMEWORK.md §2.5: leaf inner width = outer - borderReserve).
        var borderReserve = pane.Border.Style is not null ? PaneBorderRenderer.BorderReserve : 0;
        var contentWidth = surfaceWidth is int sw ? Math.Max(0, sw - borderReserve) : (int?)null;

        // §2.6: the single root pane defaults to `overflow` (v1-identical passthrough) so an
        // unconfigured surface renders exactly as it always has.
        var overflow = pane.Overflow ?? OverflowMode.Overflow;
        var buffer = PaneRenderer.RenderLeaf(segments, contentWidth, overflow, pane.Ellipsis);

        // §2.4: row padding to the pane's own width (trimmed back at the very end) only applies
        // once a pane width is actually known; with no COLUMNS there is no width to pad to, so
        // the buffer's own rows are used as-is.
        IReadOnlyList<string> rows = contentWidth is int width
            ? Compositor.ComposeRoot(new[] { new Compositor.PaneContribution(buffer, width, HasBackground: false) })
            : buffer.Rows.Select(r => r.Markup).ToList();

        // SPEC.md §3/§6b: one predicate — did §6 take its single-line fallback at this content
        // width? — decides border suppression; renderingPanel decides Panel construction. Only a
        // rendered panel guarantees Spectre won't re-break a row the pane pipeline deliberately
        // left overwide (fallback row, or an unsplit oversized segment outside a panel).
        var fallback = RowLayout.IsFallbackWidth(contentWidth);
        var suppressBorder = pane.Border.Style is not null && fallback;
        var renderingPanel = pane.Border.Style is not null && !suppressBorder;

        if (renderingPanel)
        {
            var boxBorder = pane.Border.Style!;
            var borderColor = ColorResolution.ResolveBorderColor(pane.Border.Color, values, tokens);
            var panel = new Panel(new Markup(string.Join('\n', rows)))
                .Padding(1, 0)
                .Border(boxBorder)
                .BorderStyle(new Style(borderColor));

            panel.Width = surfaceWidth!.Value;

            console.Write(panel);
        }
        else
        {
            foreach (var row in rows)
            {
                console.MarkupLine(row);
            }
        }

        return 0;
    }
    catch
    {
        return 0;
    }
}

// §4/§5: after sizing converges, stamps every non-content-sized leaf's actual inner width onto
// its own command items' cache entries, so the next render's TTL-expired respawn (CommandProvider)
// sees the pane it will actually render into rather than whatever width the last stamp recorded.
static void StampPaneWidths(SizeResolver.ResolvedPane node, string? cwd, string cacheDir)
{
    var pane = node.Source;
    if (!SizeResolver.IsContentSized(pane))
    {
        var borderReserve = pane.Border.Style is not null ? PaneBorderRenderer.BorderReserve : 0;
        var innerWidth = Math.Max(0, node.OuterWidth - borderReserve);

        foreach (var item in pane.Items)
        {
            if (item.Id is { Length: > 0 } id && item.Command is { Count: > 0 } command)
            {
                ItemCache.StampPaneWidth(cacheDir, ItemCache.KeyFor(id, command, cwd), innerWidth);
            }
        }
    }

    foreach (var child in node.Children)
    {
        StampPaneWidths(child, cwd, cacheDir);
    }
}

static StatusInput ParseInput(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return new StatusInput();
    }

    try
    {
        return JsonSerializer.Deserialize(raw, StatusInputJsonContext.Default.StatusInput) ?? new StatusInput();
    }
    catch
    {
        return new StatusInput();
    }
}

static (ResolvedConfig TopLevel, Pane RootPane) SafeLoadAll()
{
    try
    {
        return ConfigLoader.LoadAll(ConfigLoader.ResolveConfigPath());
    }
    catch
    {
        var fallbackTopLevel = new ResolvedConfig(
            new ColorResolution.ColorExpr.Literal("grey"),
            BoxBorder.Rounded,
            ConfigLoader.DefaultChromeReserve,
            ColorSystemSupport.Standard,
            new Dictionary<string, ColorResolution.ColorRule>());
        var fallbackPane = new Pane(
            PaneSplit.None,
            Array.Empty<Pane>(),
            "auto",
            new PaneBorder(fallbackTopLevel.BorderColor, fallbackTopLevel.Style),
            null,
            ConfigLoader.DefaultEllipsis,
            null,
            Array.Empty<PaneItem>());
        return (fallbackTopLevel, fallbackPane);
    }
}
