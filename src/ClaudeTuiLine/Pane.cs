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
public sealed record PaneBorder(ColorResolution.ColorExpr Color, BoxBorder? Style);

/// <summary>SPEC-V2-FRAMEWORK.md §3.1: where a pane's content sits when shorter than its siblings.</summary>
public enum PaneValign
{
    Top,
    Middle,
    Bottom,
}

public static class PaneValignParsing
{
    public static PaneValign Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "middle" => PaneValign.Middle,
        "bottom" => PaneValign.Bottom,
        _ => PaneValign.Top,
    };
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
    public static PaneAlign Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "center" => PaneAlign.Center,
        "right" => PaneAlign.Right,
        _ => PaneAlign.Left,
    };
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
    public static PaneDistribute Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "min-rows" => PaneDistribute.MinRows,
        _ => PaneDistribute.Greedy,
    };
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
    PaneDistribute Distribute = PaneDistribute.Greedy);
