namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4/§5: the one payload every item-resolution call site threads, instead of
/// its own growing (input, gitBranch, engram, ...) parameter list — the render's
/// <see cref="StatusInput"/>, its already-probed git branch and Engram telemetry, plus environment
/// values that cost a subprocess to learn (<see cref="RemoteUrl"/> today, whatever comes next).
/// Adding a new such value is a change to this class alone: <see cref="ItemRegistry.ItemDefinition"/>'s
/// delegate signature, and every call site between Program.cs and a leaf item, never changes again.
/// </summary>
public sealed class ItemContext
{
    public StatusInput Input { get; }
    public string? GitBranch { get; }
    public EngramResult? Engram { get; }

    private readonly Lazy<string?> _remoteUrl;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §3.2: <c>git remote get-url origin</c> costs a subprocess, so this is
    /// probed at most once per render and only when something actually reads it — an item placed,
    /// a link template's <c>{remote-url}</c>, or a colors-table <c>from</c> naming it — rather than
    /// unconditionally on every render the way <see cref="GitBranch"/> already is.
    /// </summary>
    public string? RemoteUrl => _remoteUrl.Value;

    public ItemContext(StatusInput input, string? gitBranch, EngramResult? engram, Func<string?> remoteUrlProbe)
    {
        Input = input;
        GitBranch = gitBranch;
        Engram = engram;
        _remoteUrl = new Lazy<string?>(remoteUrlProbe, System.Threading.LazyThreadSafetyMode.None);
    }
}
