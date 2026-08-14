namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.2: the OSC 8 hyperlink escape sequence this project emits for its own
/// links — <c>ESC ] 8 ; ; URL ST text ESC ] 8 ; ; ST</c>. Kept as one shared Wrap/TryUnwrap pair so
/// <see cref="LeafContent"/> (attaches a link) and <see cref="PaneRenderer"/> (must preserve one
/// across a wrap or drop it before a truncation ellipsis) agree on the exact byte sequence.
/// </summary>
public static class OscHyperlink
{
    private const string Esc = "";
    private const string St = Esc + "\\";
    private const string OpenPrefix = Esc + "]8;;";
    public const string Close = Esc + "]8;;" + St;

    public static string Wrap(string url, string text) => $"{OpenPrefix}{url}{St}{text}{Close}";

    /// <summary>
    /// True when <paramref name="text"/> is exactly one OSC 8 open/text/close triple with nothing
    /// outside it — the shape <see cref="Wrap"/> always produces. Splits it into the URL and the
    /// wrapped inner text so a caller can re-wrap a substring of the text without hand-parsing the
    /// escape sequence itself.
    /// </summary>
    public static bool TryUnwrap(string text, out string url, out string inner)
    {
        url = string.Empty;
        inner = string.Empty;

        if (!text.StartsWith(OpenPrefix, StringComparison.Ordinal) || !text.EndsWith(Close, StringComparison.Ordinal))
        {
            return false;
        }

        var afterPrefix = text[OpenPrefix.Length..];
        var stIndex = afterPrefix.IndexOf(St, StringComparison.Ordinal);
        if (stIndex < 0)
        {
            return false;
        }

        var innerStart = OpenPrefix.Length + stIndex + St.Length;
        var innerEnd = text.Length - Close.Length;
        if (innerEnd < innerStart)
        {
            return false;
        }

        url = afterPrefix[..stIndex];
        inner = text[innerStart..innerEnd];
        return true;
    }
}
