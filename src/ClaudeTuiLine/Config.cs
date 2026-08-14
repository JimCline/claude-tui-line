using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace ClaudeTuiLine;

public sealed class UserConfig
{
    [JsonPropertyName("border")]
    public BorderConfig? Border { get; set; }

    [JsonPropertyName("layout")]
    public LayoutConfig? Layout { get; set; }

    [JsonPropertyName("items")]
    public List<PaneItemJsonConfig>? Items { get; set; }

    [JsonPropertyName("surface")]
    public SurfaceConfig? Surface { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §6.2: opt-in rendering profile; default "standard" keeps the golden parity baseline byte-identical by construction.</summary>
    [JsonPropertyName("colorSystem")]
    public string? ColorSystem { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §6.3: named, reusable colour tokens, referenced elsewhere as <c>"@name"</c>.</summary>
    [JsonPropertyName("colors")]
    public Dictionary<string, ColorRuleJsonConfig>? Colors { get; set; }
}

public sealed class BorderConfig
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("color")]
    [JsonConverter(typeof(ColorExprJsonConverter))]
    public ColorExprJsonConfig? Color { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }
}

public sealed class LayoutConfig
{
    [JsonPropertyName("chromeReserve")]
    public int? ChromeReserve { get; set; }
}

/// <summary>SPEC-V2-FRAMEWORK.md §2.2/§8: the pane tree, present only once splits are configured.</summary>
public sealed class SurfaceConfig
{
    [JsonPropertyName("maxRows")]
    public int? MaxRows { get; set; }

    [JsonPropertyName("pane")]
    public PaneConfig? Pane { get; set; }
}

public sealed class PaneConfig
{
    [JsonPropertyName("split")]
    public string? Split { get; set; }

    [JsonPropertyName("children")]
    public List<PaneConfig>? Children { get; set; }

    [JsonPropertyName("size")]
    [JsonConverter(typeof(PaneSizeConverter))]
    public string? Size { get; set; }

    [JsonPropertyName("minSize")]
    public int? MinSize { get; set; }

    [JsonPropertyName("maxSize")]
    public int? MaxSize { get; set; }

    [JsonPropertyName("border")]
    public BorderConfig? Border { get; set; }

    [JsonPropertyName("overflow")]
    public string? Overflow { get; set; }

    [JsonPropertyName("ellipsis")]
    public string? Ellipsis { get; set; }

    [JsonPropertyName("maxRows")]
    public int? MaxRows { get; set; }

    [JsonPropertyName("gutter")]
    public int? Gutter { get; set; }

    [JsonPropertyName("valign")]
    public string? Valign { get; set; }

    [JsonPropertyName("align")]
    public string? Align { get; set; }

    [JsonPropertyName("items")]
    public List<PaneItemJsonConfig>? Items { get; set; }
}

public sealed class PaneItemJsonConfig
{
    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("color")]
    [JsonConverter(typeof(ColorExprJsonConverter))]
    public ColorExprJsonConfig? Color { get; set; }

    [JsonPropertyName("overflow")]
    public string? Overflow { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §4: a <c>command</c> item's own id — distinct from <see cref="Item"/>, which selects a builtin.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("command")]
    [JsonConverter(typeof(CommandJsonConverter))]
    public IReadOnlyList<string>? Command { get; set; }

    [JsonPropertyName("shell")]
    public bool? Shell { get; set; }

    [JsonPropertyName("ttlSeconds")]
    public int? TtlSeconds { get; set; }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §3.2: a URL template wrapping this item's rendered text in an OSC 8 hyperlink.</summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    /// <summary>SPEC-V2-FRAMEWORK.md §3.2: names another item's id as this item's raw-value source, making this a derived item.</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>A regex applied to the <see cref="From"/> value; the first capture group, or the whole match when the pattern has none.</summary>
    [JsonPropertyName("extract")]
    public string? Extract { get; set; }

    /// <summary><c>"upper"</c>/<c>"lower"</c>; any other value passes the (post-<see cref="Extract"/>) text through unchanged.</summary>
    [JsonPropertyName("case")]
    public string? Case { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3: <c>size</c> is either a JSON number (exact cell count) or a JSON
/// string (<c>"auto"</c>/<c>"content"</c>/<c>"fill"</c>/<c>"NN%"</c>). Normalizes either token to
/// its string form so <see cref="PaneConfig.Size"/> stays a single plain type end to end.
/// </summary>
internal sealed class PaneSizeConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetInt32().ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4: <c>command</c> is a JSON array (argv, the default/documented form) or
/// a JSON string (only meaningful with <c>shell: true</c>, run as <c>sh -c "&lt;string&gt;"</c>).
/// Both forms normalize to a list here so <see cref="PaneItemJsonConfig.Command"/> stays one plain
/// type end to end; the shell form becomes a single-element list holding the script string, and
/// the <c>shell</c> flag is what tells the execution layer (§5) which of the two it is holding.
/// </summary>
internal sealed class CommandJsonConverter : JsonConverter<IReadOnlyList<string>?>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return value is null ? null : new[] { value };
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } s)
            {
                items.Add(s);
            }
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string>? value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Config is read-only; command is never serialized back out.");
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6: a "color" config value is either a JSON string (a literal like
/// <c>"blue"</c>/<c>"#ff5fd7"</c>, or a token reference like <c>"@model-accent"</c>) or a JSON
/// object (an inline rule). <see cref="ColorExprJsonConverter"/> normalizes both shapes into this
/// one holder, exactly one field populated, so binding does not re-sniff the JSON token type.
/// </summary>
public sealed class ColorExprJsonConfig
{
    public string? Literal { get; set; }

    public ColorRuleJsonConfig? Rule { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6.3/§6.4: the shared shape of a <c>colors</c>-table token and an inline
/// rule — identical JSON, differing only in whether <c>from</c> is required (table) or defaults to
/// the owning item (inline), which <see cref="ConfigLoader"/> enforces at binding time.
/// </summary>
public sealed class ColorRuleJsonConfig
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("thresholds")]
    public List<ThresholdJsonConfig>? Thresholds { get; set; }

    [JsonPropertyName("match")]
    public List<MatchJsonConfig>? Match { get; set; }

    [JsonPropertyName("default")]
    public string? Default { get; set; }
}

public sealed class ThresholdJsonConfig
{
    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>SPEC-V2-FRAMEWORK.md §6.4: each entry carries exactly one predicate — <see cref="Contains"/> (case-insensitive substring) or <see cref="EqualsValue"/> (case-insensitive full match), no regex.</summary>
public sealed class MatchJsonConfig
{
    [JsonPropertyName("contains")]
    public string? Contains { get; set; }

    [JsonPropertyName("equals")]
    public string? EqualsValue { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §6: reads a "color" value as either a JSON string (literal or
/// <c>"@token"</c>) or a JSON object (inline rule), producing one <see cref="ColorExprJsonConfig"/>
/// shape. The nested <see cref="ColorRuleJsonConfig"/> deserialization reuses the caller's
/// <see cref="JsonSerializerOptions"/> so it still resolves through the AOT source-gen context.
/// </summary>
internal sealed class ColorExprJsonConverter : JsonConverter<ColorExprJsonConfig?>
{
    public override ColorExprJsonConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ColorExprJsonConfig { Literal = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        var rule = JsonSerializer.Deserialize(ref reader, ConfigJsonContext.Default.ColorRuleJsonConfig);
        return rule is null ? null : new ColorExprJsonConfig { Rule = rule };
    }

    public override void Write(Utf8JsonWriter writer, ColorExprJsonConfig? value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Config is read-only; color is never serialized back out.");
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(UserConfig))]
[JsonSerializable(typeof(ColorRuleJsonConfig))]
public partial class ConfigJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Fully-resolved border configuration: every default has already been applied,
/// and <see cref="Style"/> is null exactly when the border should not render at all
/// (covers both <c>enabled: false</c> and <c>style: "none"</c>). <see cref="ChromeReserve"/> is
/// a layout setting, not a border one, but is resolved from the same config file/read as the
/// border settings (SPEC.md §6b "Config file"), so it travels on the same record.
/// <see cref="ColorSystem"/> (§6.2) defaults to <see cref="ColorSystemSupport.Standard"/>, keeping
/// the golden parity baseline byte-identical by construction. <see cref="Colors"/> (§6.3) is the
/// parsed <c>colors</c> token table, keyed by name without the leading <c>@</c>.
/// </summary>
public sealed record ResolvedConfig(
    ColorResolution.ColorExpr BorderColor,
    BoxBorder? Style,
    int ChromeReserve,
    ColorSystemSupport ColorSystem,
    IReadOnlyDictionary<string, ColorResolution.ColorRule> Colors);

public static class ConfigLoader
{
    private static readonly ColorResolution.ColorExpr DefaultColorExpr = new ColorResolution.ColorExpr.Literal("grey");
    private static readonly BoxBorder DefaultBoxBorder = BoxBorder.Rounded;

    // SPEC.md §6 "MEASURED": the real Claude Code truncation boundary is COLUMNS - 3.
    public const int DefaultChromeReserve = 3;

    /// <summary>
    /// SPEC.md §6b "Config path resolution": <paramref name="configPathOverride"/>, when set and
    /// non-empty, is the config file path. Otherwise <c>$HOME/.claude/claude-tui-line.json</c>
    /// (or null if <paramref name="home"/> is itself unset/empty).
    /// </summary>
    public static string? ResolveConfigPath(string? configPathOverride, string? home)
    {
        if (!string.IsNullOrEmpty(configPathOverride))
        {
            return configPathOverride;
        }

        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".claude", "claude-tui-line.json");
    }

    public static string? ResolveConfigPath() =>
        ResolveConfigPath(
            Environment.GetEnvironmentVariable("CLAUDE_TUI_LINE_CONFIG"),
            Environment.GetEnvironmentVariable("HOME"));

    public const string DefaultEllipsis = "…";

    /// <summary>
    /// Reads the config file exactly once and resolves both the top-level border/layout settings
    /// and the root pane from that single parse — the entry point <c>Program.cs</c> uses per
    /// render. <see cref="LoadBorderConfig"/> and <see cref="LoadRootPane"/> remain as independent,
    /// separately-testable single-purpose loaders (each still reads its own file), but a caller
    /// that needs both must use this instead of calling them back to back, which would read the
    /// file from disk twice.
    /// </summary>
    public static (ResolvedConfig TopLevel, Pane RootPane) LoadAll(string? configPath)
    {
        var config = configPath is null ? null : TryReadConfig(configPath);
        var topLevel = ResolveTopLevel(config);
        var rootPane = ResolveRootPane(config, topLevel);
        return (topLevel, rootPane);
    }

    public static ResolvedConfig LoadBorderConfig(string? configPath = null)
    {
        var config = configPath is null ? null : TryReadConfig(configPath);
        return ResolveTopLevel(config);
    }

    private static ResolvedConfig ResolveTopLevel(UserConfig? config)
    {
        var resolvedBorder = ResolveBorder(config?.Border, isSplitContainer: false);
        var chromeReserve = config?.Layout?.ChromeReserve ?? DefaultChromeReserve;
        var colorSystem = ParseColorSystem(config?.ColorSystem);
        var colors = ParseColorTable(config?.Colors);

        return new ResolvedConfig(resolvedBorder.Color, resolvedBorder.Style, chromeReserve, colorSystem, colors);
    }

    /// <summary>SPEC-V2-FRAMEWORK.md §6.2: unrecognized/absent values fall back to "standard", the golden-parity-preserving default.</summary>
    private static ColorSystemSupport ParseColorSystem(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "256" => ColorSystemSupport.EightBit,
        "truecolor" => ColorSystemSupport.TrueColor,
        _ => ColorSystemSupport.Standard,
    };

    /// <summary>SPEC-V2-FRAMEWORK.md §6.3: a table entry's <c>from</c> is required and never defaulted — left null (and so silently degrading, §7) when omitted.</summary>
    private static IReadOnlyDictionary<string, ColorResolution.ColorRule> ParseColorTable(Dictionary<string, ColorRuleJsonConfig>? colors) =>
        colors?.ToDictionary(kv => kv.Key, kv => ParseColorRule(kv.Value, kv.Value.From), StringComparer.Ordinal)
        ?? (IReadOnlyDictionary<string, ColorResolution.ColorRule>)new Dictionary<string, ColorResolution.ColorRule>();

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §8: <c>surface</c> absent ⇒ a single root leaf pane holding the
    /// top-level <c>items</c>, with the top-level border. <c>surface</c> present ⇒ top-level
    /// <c>items</c> is ignored; the pane tree is authoritative.
    /// </summary>
    public static Pane LoadRootPane(string? configPath, ResolvedConfig topLevel)
    {
        var config = configPath is null ? null : TryReadConfig(configPath);
        return ResolveRootPane(config, topLevel);
    }

    public static Pane ResolveRootPane(UserConfig? config, ResolvedConfig topLevel)
    {
        var surfacePane = config?.Surface?.Pane;
        if (surfacePane is null)
        {
            return new Pane(
                PaneSplit.None,
                Array.Empty<Pane>(),
                "auto",
                new PaneBorder(topLevel.BorderColor, topLevel.Style),
                null,
                DefaultEllipsis,
                null,
                ToPaneItems(config?.Items));
        }

        return ResolvePane(surfacePane);
    }

    private static Pane ResolvePane(PaneConfig cfg)
    {
        var children = cfg.Children?.Select(ResolvePane).ToList() ?? (IReadOnlyList<Pane>)Array.Empty<Pane>();
        var split = NormalizeSplit(ParseSplit(cfg.Split), children.Count);
        var isSplitContainer = split != PaneSplit.None;

        return new Pane(
            split,
            children,
            cfg.Size ?? "auto",
            ResolveBorder(cfg.Border, isSplitContainer),
            OverflowModeParsing.Parse(cfg.Overflow),
            cfg.Ellipsis ?? DefaultEllipsis,
            cfg.MaxRows,
            ToPaneItems(cfg.Items),
            cfg.MinSize,
            cfg.MaxSize,
            cfg.Gutter ?? 0,
            PaneValignParsing.Parse(cfg.Valign),
            PaneAlignParsing.Parse(cfg.Align));
    }

    private static PaneSplit ParseSplit(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "horizontal" => PaneSplit.Horizontal,
        "vertical" => PaneSplit.Vertical,
        _ => PaneSplit.None,
    };

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.2: <c>split</c> and <c>children</c> must agree before a pane is
    /// stored — every downstream reader (border defaulting, layout, rendering) treats "has
    /// children" and "is a split container" as the same question, so this is the one place that
    /// question gets decided. Non-empty children with no explicit <c>split</c> normalizes to
    /// <c>vertical</c> (side-by-side is the statusline-shaped default for a stated split intent);
    /// an explicit <c>split</c> with no children drops back to a leaf.
    /// </summary>
    private static PaneSplit NormalizeSplit(PaneSplit parsedSplit, int childCount) =>
        childCount == 0 ? PaneSplit.None : (parsedSplit == PaneSplit.None ? PaneSplit.Vertical : parsedSplit);

    private static IReadOnlyList<PaneItem> ToPaneItems(List<PaneItemJsonConfig>? items) =>
        items?.Select(i => new PaneItem(
            i.Item,
            i.Format,
            ParseColorExpr(i.Color, i.Id ?? i.Item),
            OverflowModeParsing.Parse(i.Overflow),
            i.Id,
            i.Command,
            i.Shell ?? false,
            i.TtlSeconds,
            i.TimeoutMs,
            i.Link,
            i.From,
            i.Extract,
            i.Case)).ToList()
        ?? (IReadOnlyList<PaneItem>)Array.Empty<PaneItem>();

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.9 ruling: absent an explicit <c>border.enabled</c>, a leaf pane
    /// defaults to bordered (v1 behavior) and a split container defaults to borderless, so adding
    /// a split to a config never silently adds chrome.
    /// </summary>
    private static PaneBorder ResolveBorder(BorderConfig? border, bool isSplitContainer)
    {
        var enabled = border?.Enabled ?? !isSplitContainer;
        var color = ParseColorExpr(border?.Color, owningItemId: null) ?? DefaultColorExpr;

        BoxBorder? style = DefaultBoxBorder;
        if (!string.IsNullOrEmpty(border?.Style))
        {
            style = border!.Style!.ToLowerInvariant() switch
            {
                "rounded" => BoxBorder.Rounded,
                "square" => BoxBorder.Square,
                "heavy" => BoxBorder.Heavy,
                "double" => BoxBorder.Double,
                "ascii" => BoxBorder.Ascii,
                "none" => null,
                _ => DefaultBoxBorder,
            };
        }

        if (!enabled)
        {
            style = null;
        }

        return new PaneBorder(color, style);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §6.3/§6.4: a literal string, a <c>"@name"</c> token reference, or an
    /// inline rule object. <paramref name="owningItemId"/> is the inline-rule <c>from</c> default
    /// (§6.4) — an item's own id for an item's <c>color</c>, or null for a border, which has no
    /// value of its own to default to.
    /// </summary>
    private static ColorResolution.ColorExpr? ParseColorExpr(ColorExprJsonConfig? cfg, string? owningItemId) => cfg switch
    {
        null => null,
        { Literal: { Length: > 0 } lit } => lit[0] == '@'
            ? new ColorResolution.ColorExpr.TokenRef(lit[1..])
            : new ColorResolution.ColorExpr.Literal(lit),
        { Rule: { } rule } => new ColorResolution.ColorExpr.Inline(ParseColorRule(rule, rule.From ?? owningItemId)),
        _ => null,
    };

    /// <summary>
    /// A threshold/match entry with no <c>color</c> of its own specifies nothing usable, so it is
    /// dropped rather than carried forward as an empty colour spec (§7's silent-degrade convention).
    /// <paramref name="from"/> is the caller's resolved choice — a table entry passes its own
    /// (possibly null) <c>from</c> unchanged, while an inline rule has already applied its default.
    /// </summary>
    private static ColorResolution.ColorRule ParseColorRule(ColorRuleJsonConfig cfg, string? from) =>
        new(
            cfg.Thresholds?.Where(t => !string.IsNullOrEmpty(t.Color)).Select(t => new ColorResolution.ThresholdRule(t.Min, t.Color!)).ToList(),
            cfg.Match?.Where(m => !string.IsNullOrEmpty(m.Color)).Select(m => new ColorResolution.MatchRule(m.Contains, m.EqualsValue, m.Color!)).ToList(),
            cfg.Default,
            from);

    private static UserConfig? TryReadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize(text, ConfigJsonContext.Default.UserConfig);
        }
        catch
        {
            return null;
        }
    }
}
