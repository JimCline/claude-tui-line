namespace ClaudeTuiLine;

/// <summary>
/// One statusline segment. <see cref="Plain"/> is the visible text (used to measure width
/// for wrapping); <see cref="Markup"/> is the Spectre markup used to render it.
/// </summary>
public sealed record Segment(string Markup, string Plain);
