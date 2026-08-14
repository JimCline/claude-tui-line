using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>SPEC-V2-FRAMEWORK.md §2.2: how a pane's children divide its axis.</summary>
public enum PaneSplit
{
    None,
    Horizontal,
    Vertical,
}

/// <summary>
/// A pane's own resolved border — independent of any other pane's (§2.2). Null
/// <see cref="Style"/> means the pane renders no border, the same convention
/// <see cref="ResolvedConfig"/> uses at the surface level.
/// </summary>
public sealed record PaneBorder(ColorResolution.ColorExpr Color, BoxBorder? Style, PaneBorderEdges Edges);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.10: which of a pane's own four border edges actually draw. Static
/// config, resolved once at config-load time from the pane's own <c>edges</c>/shorthand
/// declaration or an ancestor's subtree-scoped shorthand (§2.10.1 rule 1) — unlike
/// <see cref="PaneBorder.Color"/>, never value-derived at render time.
/// </summary>
public sealed record PaneBorderEdges(bool Top, bool Right, bool Bottom, bool Left)
{
    public static readonly PaneBorderEdges All = new(true, true, true, true);
    public static readonly PaneBorderEdges None = new(false, false, false, false);
}

/// <summary>SPEC-V2-FRAMEWORK.md §3.1: where a pane's content sits when shorter than its siblings.</summary>
public enum PaneValign
{
    Top,
    Middle,
    Bottom,
}

public static class PaneValignParsing
{
    private static readonly (string Token, PaneValign Value)[] Accepted =
    {
        ("top", PaneValign.Top),
        ("middle", PaneValign.Middle),
        ("bottom", PaneValign.Bottom),
    };

    public static IReadOnlyList<string> AcceptedTokens { get; } = Accepted.Select(a => a.Token).ToArray();

    private static PaneValign? ParseCore(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in Accepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    public static PaneValign Parse(string? value) => ParseCore(value) ?? PaneValign.Top;

    /// <summary>
    /// True when <paramref name="value"/> was present but matched none of the recognized tokens —
    /// distinct from an absent field, which also defaults to <see cref="PaneValign.Top"/>. §9.4's
    /// config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    public static bool IsUnrecognized(string? value) => !string.IsNullOrWhiteSpace(value) && ParseCore(value) is null;
}

/// <summary>SPEC-V2-FRAMEWORK.md §3.1: horizontal alignment of a pane's content within its own width.</summary>
public enum PaneAlign
{
    Left,
    Center,
    Right,
}

public static class PaneAlignParsing
{
    private static readonly (string Token, PaneAlign Value)[] Accepted =
    {
        ("left", PaneAlign.Left),
        ("center", PaneAlign.Center),
        ("right", PaneAlign.Right),
    };

    public static IReadOnlyList<string> AcceptedTokens { get; } = Accepted.Select(a => a.Token).ToArray();

    private static PaneAlign? ParseCore(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in Accepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    public static PaneAlign Parse(string? value) => ParseCore(value) ?? PaneAlign.Left;

    /// <summary>
    /// True when <paramref name="value"/> was present but matched none of the recognized tokens —
    /// distinct from an absent field, which also defaults to <see cref="PaneAlign.Left"/>. §9.4's
    /// config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    public static bool IsUnrecognized(string? value) => !string.IsNullOrWhiteSpace(value) && ParseCore(value) is null;
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3.1: how a vertical split's <c>content</c>/<c>fill</c> children divide
/// their share of the width. <see cref="Greedy"/> is the existing single-pass allocation (§2.3),
/// unchanged and still the default; <see cref="MinRows"/> searches for the width split that
/// minimizes the tallest candidate's row count instead.
/// </summary>
public enum PaneDistribute
{
    Greedy,
    MinRows,
}

public static class PaneDistributeParsing
{
    private static readonly (string Token, PaneDistribute Value)[] Accepted =
    {
        ("greedy", PaneDistribute.Greedy),
        ("min-rows", PaneDistribute.MinRows),
    };

    public static IReadOnlyList<string> AcceptedTokens { get; } = Accepted.Select(a => a.Token).ToArray();

    private static PaneDistribute? ParseCore(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in Accepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    public static PaneDistribute Parse(string? value) => ParseCore(value) ?? PaneDistribute.Greedy;

    /// <summary>
    /// True when <paramref name="value"/> was present but matched neither recognized token —
    /// distinct from an absent field, which also defaults to <see cref="PaneDistribute.Greedy"/>.
    /// §9.4's config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    public static bool IsUnrecognized(string? value) => !string.IsNullOrWhiteSpace(value) && ParseCore(value) is null;
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.8.3: whether a pane's border box fills the shared band a vertical
/// split's children divide (§2.2's <c>height(vertical split) = max(height(children))</c>) or
/// closes immediately under its own last content row. The same vocabulary as §2.3's width
/// <c>size</c> key — <c>content</c>/<c>fill</c> — because it is the same question on the other
/// axis.
/// </summary>
public enum PaneHeight
{
    Content,
    Fill,
}

public static class PaneHeightParsing
{
    private static readonly (string Token, PaneHeight Value)[] Accepted =
    {
        ("content", PaneHeight.Content),
        ("fill", PaneHeight.Fill),
    };

    public static IReadOnlyList<string> AcceptedTokens { get; } = Accepted.Select(a => a.Token).ToArray();

    private static PaneHeight? ParseCore(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        foreach (var (token, val) in Accepted)
        {
            if (token == normalized)
            {
                return val;
            }
        }

        return null;
    }

    public static PaneHeight Parse(string? value) => ParseCore(value) ?? PaneHeight.Fill;

    /// <summary>
    /// True when <paramref name="value"/> was present but matched neither recognized token —
    /// distinct from an absent field, which also defaults to <see cref="PaneHeight.Fill"/>.
    /// §9.4's config diagnostics need this distinction; the renderer's fallback does not.
    /// </summary>
    public static bool IsUnrecognized(string? value) => !string.IsNullOrWhiteSpace(value) && ParseCore(value) is null;
}

/// <summary>
/// One entry of a leaf pane's <c>items</c> list (§8). <see cref="Item"/> selects a builtin by id;
/// a <c>command</c> item instead carries its own <see cref="Id"/> plus <see cref="Command"/>/
/// <see cref="Shell"/>/<see cref="TtlSeconds"/>/<see cref="TimeoutMs"/> (§4/§5) and leaves
/// <see cref="Item"/> null — the two forms are mutually exclusive within one entry.
/// <see cref="Command"/> is always argv-shaped; when <see cref="Shell"/> is true it holds exactly
/// one element, the script string to run via <c>sh -c</c>, rather than a literal argv list.
/// A third, derived form also carries its own <see cref="Id"/> but leaves <see cref="Item"/> and
/// <see cref="Command"/> both null: <see cref="From"/> names another item's id as this one's raw
/// value source, optionally narrowed by <see cref="Extract"/> (a regex; the first capture group,
/// or the whole match when the pattern has none) and <see cref="Case"/> (<c>"upper"</c>/
/// <c>"lower"</c>; any other value passes the text through unchanged). <see cref="Link"/> (§3.2)
/// is a URL template — <c>{}</c> substitutes this item's own resolved value, <c>{other-id}</c>
/// another item's — wrapping the item's rendered text in an OSC 8 hyperlink.
/// </summary>
public sealed record PaneItem(
    string? Item,
    string? Format,
    ColorResolution.ColorExpr? Color,
    OverflowMode? Overflow,
    string? Id = null,
    IReadOnlyList<string>? Command = null,
    bool Shell = false,
    int? TtlSeconds = null,
    int? TimeoutMs = null,
    string? Link = null,
    string? From = null,
    string? Extract = null,
    string? Case = null);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.2's pane tree node: a leaf (<see cref="Items"/>) or a split
/// (<see cref="Children"/>). <see cref="Overflow"/> is left unresolved (null) here when not
/// explicitly configured, because its default is context-sensitive (§2.6: root pane vs. inside
/// a split) and is resolved by the renderer, not the parser. <see cref="MinSize"/>/
/// <see cref="MaxSize"/> bound a <c>content</c>-sized pane (§2.3); <see cref="Gutter"/> is the
/// blank-cell spacing this pane inserts between its own direct children.
/// </summary>
public sealed record Pane(
    PaneSplit Split,
    IReadOnlyList<Pane> Children,
    string Size,
    PaneBorder Border,
    OverflowMode? Overflow,
    string Ellipsis,
    int? MaxRows,
    IReadOnlyList<PaneItem> Items,
    int? MinSize = null,
    int? MaxSize = null,
    int Gutter = 0,
    PaneValign Valign = PaneValign.Top,
    PaneAlign Align = PaneAlign.Left,
    PaneDistribute Distribute = PaneDistribute.Greedy,
    PaneHeight Height = PaneHeight.Fill);
