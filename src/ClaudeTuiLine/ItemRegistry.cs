namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §4: the single enumeration point for every builtin item id — 14 default
/// segments plus <c>model-short</c> and <c>remote-url</c>. Nothing else in the codebase enumerates
/// builtin ids: the default pipeline (<see cref="SegmentBuilder.Build"/>) iterates
/// <see cref="DefaultIds"/>, and a pane's <c>items</c>/color-token config (<see cref="LeafItems"/>)
/// looks an id up here directly. Per-id construction logic itself lives in
/// <see cref="SegmentBuilder"/>; this table only says which ids exist, what order they render in
/// by default, and how to reach each one's two distinct outputs — the raw value used for §6
/// color-threshold rules (<see cref="ItemDefinition.ResolveValue"/>), and the rendered segment —
/// both <c>Plain</c> (the sole width metric, §2.4) and <c>Markup</c> (colour, including any
/// internal per-fragment colouring an item applies to itself) — shared byte-for-byte by the
/// default segment and an explicit <c>items</c> selection alike
/// (<see cref="ItemDefinition.BuildDefaultSegment"/>). Every row reads through one
/// <see cref="ItemContext"/> instead of its own (input, gitBranch, engram, ...) parameter list, so
/// adding a new environment value (§3.2's <c>remote-url</c> today) never touches this delegate
/// signature again.
/// </summary>
public static class ItemRegistry
{
    /// <summary>
    /// Whether a row's own internal colour is a fixed provider identity with no information
    /// content (<see cref="Decorative"/> — an item-level <c>color</c> config replaces it entirely)
    /// or value-derived, where recolouring would destroy meaning (<see cref="Semantic"/> — an
    /// item-level <c>color</c> nests around it instead, claiming only text the row left unclaimed).
    /// </summary>
    public enum ItemColorKind
    {
        Decorative,
        Semantic,
    }

    public sealed record ItemDefinition(
        string Id,
        string Reports,
        Func<ItemContext, string?> ResolveValue,
        Func<ItemContext, Segment?> BuildDefaultSegment,
        ItemColorKind ColorKind,
        Func<ItemContext, string?>? DefaultLinkTemplate = null);

    // Declaration order is also the default rendering order (SegmentBuilder.Build iterates
    // DefaultIds in this order) — kept as one list rather than a separate ordering table so the
    // two can never drift apart. Reports strings are SPEC-V2-FRAMEWORK.md §9.6.2.1's table,
    // transcribed verbatim (its markdown code-span backticks are the document's own styling, not
    // literal characters) — required here rather than in a lookup table beside it, so a row added
    // without a description fails to compile instead of shipping as a bare id.
    private static readonly ItemDefinition[] Items =
    {
        new("directory", "the working directory", ctx => SegmentBuilder.ResolveDirectory(ctx.Input.Cwd, ctx.ItemSettings?.Directory), ctx => SegmentBuilder.BuildDirectory(ctx.Input.Cwd, ctx.ItemSettings?.Directory), ItemColorKind.Decorative),
        new("git-branch", "the current branch, or nothing outside a repo", ctx => SegmentBuilder.ResolveGitBranch(ctx.GitBranch), ctx => SegmentBuilder.BuildGitBranch(ctx.GitBranch), ItemColorKind.Decorative),
        new("repo", "the workspace repo as owner/name", ctx => SegmentBuilder.ResolveRepo(ctx.Input.Workspace?.Repo), ctx => SegmentBuilder.BuildRepo(ctx.Input.Workspace?.Repo), ItemColorKind.Decorative),
        new("worktree", "the worktree's name and branch, when the session is in one", ctx => SegmentBuilder.ResolveWorktree(ctx.Input.Worktree), ctx => SegmentBuilder.BuildWorktree(ctx.Input.Worktree), ItemColorKind.Decorative),
        new("pr", "the pull request number and its review state", ctx => SegmentBuilder.ResolvePullRequest(ctx.Input.Pr, ctx.ItemSettings?.Pr), ctx => SegmentBuilder.BuildPullRequest(ctx.Input.Pr, ctx.ItemSettings?.Pr), ItemColorKind.Decorative),
        new("model", "the model's display name", ctx => SegmentBuilder.ResolveModel(ctx.Input.Model), ctx => SegmentBuilder.BuildModel(ctx.Input.Model), ItemColorKind.Decorative),
        new("effort", "the reasoning effort level", ctx => SegmentBuilder.ResolveEffort(ctx.Input.Effort), ctx => SegmentBuilder.BuildEffort(ctx.Input.Effort), ItemColorKind.Decorative),
        new("thinking", "whether extended thinking is on", ctx => SegmentBuilder.ResolveThinking(ctx.Input.Thinking), ctx => SegmentBuilder.BuildThinking(ctx.Input.Thinking), ItemColorKind.Decorative),
        new("output-style", "the active output style", ctx => SegmentBuilder.ResolveOutputStyle(ctx.Input.OutputStyle), ctx => SegmentBuilder.BuildOutputStyle(ctx.Input.OutputStyle), ItemColorKind.Decorative),
        new("context", "how much of the context window is in use. Its colour follows that percentage through the configured thresholds, so it warms as the window fills. Renders 0% when the harness has reported no usage yet, so it never disappears from a fresh session.", ctx => SegmentBuilder.ResolveContext(ctx.Input.ContextWindow), ctx => SegmentBuilder.BuildContext(ctx.Input.ContextWindow, ctx.ItemSettings?.Context), ItemColorKind.Semantic),
        new("token-usage", "the session's cumulative token spend, summed from the local transcript — input-side (including cached reads), output, and the cache-hit rate. Distinct from context, which reports how full the window is right now rather than what the session has spent in total. Opt-in because resolving it parses a file that grows with the session",
            ctx => SegmentBuilder.ResolveTokenUsage(ctx.TokenUsage),
            ctx => SegmentBuilder.BuildTokenUsage(ctx.TokenUsage),
            ItemColorKind.Decorative),
        new("rate-limits", "usage against the five-hour and seven-day limits. Its colour follows the higher of the two through the thresholds, since the nearer limit is the one that will stop you", ctx => SegmentBuilder.ResolveRateLimits(ctx.Input.RateLimits), ctx => SegmentBuilder.BuildRateLimits(ctx.Input.RateLimits, ctx.ItemSettings?.RateLimits), ItemColorKind.Semantic),
        new("agent", "the name of the active agent, when the session is running one", ctx => SegmentBuilder.ResolveAgent(ctx.Input.Agent), ctx => SegmentBuilder.BuildAgent(ctx.Input.Agent), ItemColorKind.Decorative),
        new("engram", "recent Engram memory activity. Its colour reflects whether the store is reachable and active rather than a magnitude, so it is a state indicator and not a gauge", ctx => SegmentBuilder.ResolveEngram(ctx.Engram), ctx => SegmentBuilder.BuildEngram(ctx.Engram), ItemColorKind.Semantic),
        new("vim", "the current vim mode, when vim mode is enabled", ctx => SegmentBuilder.ResolveVim(ctx.Input.Vim), ctx => SegmentBuilder.BuildVimMode(ctx.Input.Vim), ItemColorKind.Decorative),
        new("model-short", "an abbreviated model name, for panes too narrow for the full one", ctx => SegmentBuilder.ResolveModelShort(ctx.Input.Model), ctx => SegmentBuilder.BuildModelShort(ctx.Input.Model), ItemColorKind.Decorative),
        new("remote-url", "the git remote's URL. Opt-in rather than default because resolving it shells out to git", ctx => SegmentBuilder.ResolveRemoteUrl(ctx.RemoteUrl), ctx => SegmentBuilder.BuildRemoteUrl(ctx.RemoteUrl), ItemColorKind.Decorative),
        new("repo-host", "the host the workspace repo lives on, from the session payload rather than a git probe", ctx => SegmentBuilder.ResolveRepoHost(ctx.Input.Workspace?.Repo), ctx => SegmentBuilder.BuildRepoHost(ctx.Input.Workspace?.Repo), ItemColorKind.Decorative),
        new("linear", "the Linear ticket id extracted from the current git branch, uppercased; links to the issue when itemSettings.linear.workspace is set",
            ctx => SegmentBuilder.ResolveLinear(ctx.GitBranch),
            ctx => SegmentBuilder.BuildLinear(ctx.GitBranch),
            ItemColorKind.Decorative,
            DefaultLinkTemplate: ctx => LinearDefaultLink(ctx)),
    };

    private static string? LinearDefaultLink(ItemContext ctx) =>
        ctx.ItemSettings?.Linear?.Workspace is { Length: > 0 } ws
            ? $"https://linear.app/{ws}/issue/{{}}"
            : null;

    private static readonly IReadOnlyDictionary<string, ItemDefinition> ById =
        Items.ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase);

    // §9.6.2: `--items` enumerates every row, unlike DefaultIds below which excludes the two
    // opt-in-only ones — exposed in the same declaration order for the same reason DefaultIds is.
    public static readonly IReadOnlyList<ItemDefinition> All = Items;

    // model-short, remote-url, repo-host, linear, and token-usage are all opt-in-only (never part
    // of the default 14-segment pipeline): remote-url because ItemContext.RemoteUrl's probe is
    // lazy and must stay unfired for a render that never references it (§3.2) — including it here
    // would probe on every render regardless of placement. repo-host is excluded for a different
    // reason — it fires no subprocess, but a bare hostname is noise in a rendered statusline; its
    // purpose is to be referenced by a link template, not displayed on its own. linear is excluded
    // for a third reason distinct from both: most branches carry no ticket id, so a default
    // placement would render nothing on the majority of renders, and adding a default segment
    // moves the ~28 whole-statusline assertions in SegmentBuilderTests.cs for no benefit.
    // token-usage is excluded for a fifth reason: resolving it reads and parses the session's
    // transcript JSONL, a file that reaches multiple megabytes over a long session, so it must
    // stay unread for any render that does not reference it — the same laziness argument as
    // remote-url, but paid in file I/O rather than a subprocess. A fresh session also has no
    // transcript on disk yet, so a default placement would render nothing on exactly the renders a
    // new user sees first.
    public static readonly IReadOnlyList<string> DefaultIds =
        Items.Where(i => i.Id is not ("model-short" or "remote-url" or "repo-host" or "linear" or "token-usage"))
            .Select(i => i.Id)
            .ToList();

    public static ItemDefinition? Find(string id) => ById.TryGetValue(id, out var def) ? def : null;
}
