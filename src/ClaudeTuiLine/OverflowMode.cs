namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.6: governs only the case of a single segment/value wider than its
/// pane. General multi-segment row packing (<see cref="RowLayout.Wrap"/>) is identical and
/// unaffected under every mode.
/// </summary>
public enum OverflowMode
{
    /// <summary>Hard-broken across continuation rows; nothing is lost. Never exceeds pane width.</summary>
    Wrap,

    /// <summary>Cut to fit, ending with the ellipsis marker; the tail is lost. Never exceeds pane width.</summary>
    Truncate,

    /// <summary>v1 behavior: emitted whole, spilling past the pane. Legal only when the surface has exactly one pane.</summary>
    Overflow,
}

public static class OverflowModeParsing
{
    /// <summary>
    /// Parses a config string ("wrap" | "truncate" | "overflow", case-insensitive). Returns null
    /// for an absent or unrecognized value — SPEC-V2-FRAMEWORK.md §2.6's default is deliberately
    /// context-sensitive (root pane vs. inside a split), so resolving "no value" to a concrete
    /// mode is the caller's job, not the parser's.
    /// </summary>
    public static OverflowMode? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "wrap" => OverflowMode.Wrap,
        "truncate" => OverflowMode.Truncate,
        "overflow" => OverflowMode.Overflow,
        _ => null,
    };
}
