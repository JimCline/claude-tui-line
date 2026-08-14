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
        Func<ItemContext, string?> ResolveValue,
        Func<ItemContext, Segment?> BuildDefaultSegment,
        ItemColorKind ColorKind);

    // Declaration order is also the default rendering order (SegmentBuilder.Build iterates
    // DefaultIds in this order) — kept as one list rather than a separate ordering table so the
    // two can never drift apart.
    private static readonly ItemDefinition[] Items =
    {
        new("directory", ctx => SegmentBuilder.ResolveDirectory(ctx.Input.Cwd), ctx => SegmentBuilder.BuildDirectory(ctx.Input.Cwd), ItemColorKind.Decorative),
        new("git-branch", ctx => SegmentBuilder.ResolveGitBranch(ctx.GitBranch), ctx => SegmentBuilder.BuildGitBranch(ctx.GitBranch), ItemColorKind.Decorative),
        new("repo", ctx => SegmentBuilder.ResolveRepo(ctx.Input.Workspace?.Repo), ctx => SegmentBuilder.BuildRepo(ctx.Input.Workspace?.Repo), ItemColorKind.Decorative),
        new("worktree", ctx => SegmentBuilder.ResolveWorktree(ctx.Input.Worktree), ctx => SegmentBuilder.BuildWorktree(ctx.Input.Worktree), ItemColorKind.Decorative),
        new("pr", ctx => SegmentBuilder.ResolvePullRequest(ctx.Input.Pr), ctx => SegmentBuilder.BuildPullRequest(ctx.Input.Pr), ItemColorKind.Decorative),
        new("model", ctx => SegmentBuilder.ResolveModel(ctx.Input.Model), ctx => SegmentBuilder.BuildModel(ctx.Input.Model), ItemColorKind.Decorative),
        new("effort", ctx => SegmentBuilder.ResolveEffort(ctx.Input.Effort), ctx => SegmentBuilder.BuildEffort(ctx.Input.Effort), ItemColorKind.Decorative),
        new("thinking", ctx => SegmentBuilder.ResolveThinking(ctx.Input.Thinking), ctx => SegmentBuilder.BuildThinking(ctx.Input.Thinking), ItemColorKind.Decorative),
        new("output-style", ctx => SegmentBuilder.ResolveOutputStyle(ctx.Input.OutputStyle), ctx => SegmentBuilder.BuildOutputStyle(ctx.Input.OutputStyle), ItemColorKind.Decorative),
        new("context", ctx => SegmentBuilder.ResolveContext(ctx.Input.ContextWindow), ctx => SegmentBuilder.BuildContext(ctx.Input.ContextWindow), ItemColorKind.Semantic),
        new("rate-limits", ctx => SegmentBuilder.ResolveRateLimits(ctx.Input.RateLimits), ctx => SegmentBuilder.BuildRateLimits(ctx.Input.RateLimits), ItemColorKind.Semantic),
        new("agent", ctx => SegmentBuilder.ResolveAgent(ctx.Input.Agent), ctx => SegmentBuilder.BuildAgent(ctx.Input.Agent), ItemColorKind.Decorative),
        new("engram", ctx => SegmentBuilder.ResolveEngram(ctx.Engram), ctx => SegmentBuilder.BuildEngram(ctx.Engram), ItemColorKind.Semantic),
        new("vim", ctx => SegmentBuilder.ResolveVim(ctx.Input.Vim), ctx => SegmentBuilder.BuildVimMode(ctx.Input.Vim), ItemColorKind.Decorative),
        new("model-short", ctx => SegmentBuilder.ResolveModelShort(ctx.Input.Model), ctx => SegmentBuilder.BuildModelShort(ctx.Input.Model), ItemColorKind.Decorative),
        new("remote-url", ctx => SegmentBuilder.ResolveRemoteUrl(ctx.RemoteUrl), ctx => SegmentBuilder.BuildRemoteUrl(ctx.RemoteUrl), ItemColorKind.Decorative),
    };

    private static readonly IReadOnlyDictionary<string, ItemDefinition> ById =
        Items.ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase);

    // model-short and remote-url are both opt-in-only (never part of the default 14-segment
    // pipeline): remote-url specifically because ItemContext.RemoteUrl's probe is lazy and must
    // stay unfired for a render that never references it (§3.2) — including it here would probe
    // on every render regardless of placement.
    public static readonly IReadOnlyList<string> DefaultIds =
        Items.Where(i => i.Id is not ("model-short" or "remote-url"))
            .Select(i => i.Id)
            .ToList();

    public static ItemDefinition? Find(string id) => ById.TryGetValue(id, out var def) ? def : null;
}
