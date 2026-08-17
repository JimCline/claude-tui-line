using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>
/// Per-id construction logic for every builtin item: for each id, a composite default-segment
/// builder (color/format baked in, used by <see cref="Build"/> for the no-<c>items</c>-configured
/// pipeline) and a raw-value resolver (used when an id is explicitly selected into a pane's
/// <c>items</c> list — see <see cref="LeafItems"/>). <see cref="ItemRegistry"/> is what enumerates
/// these ids and their default order; this class only knows how to build one id's output once
/// asked.
/// </summary>
public static class SegmentBuilder
{
    // The raw ANSI SGR reset (ESC [ 0 m) appended after a command provider's own escaped text —
    //  rather than an embedded raw byte, so this stays a plain, diff-legible ASCII source
    // line instead of an invisible control character between quotes.
    private const string RawSgrReset = "[0m";

    public static IReadOnlyList<Segment> Build(ItemContext ctx)
    {
        var segments = new List<Segment>();
        foreach (var id in ItemRegistry.DefaultIds)
        {
            var segment = ItemRegistry.Find(id)!.BuildDefaultSegment(ctx);
            if (segment is not null)
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    private static Segment SingleColor(string tag, string plain) =>
        new($"[{tag}]{Markup.Escape(plain)}[/]", plain);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4: <c>model-short</c> is a new registry row, not a format string —
    /// it gives a shorter form of the model name than the full <c>model</c> item. Strips a leading "Claude "
    /// prefix when present ("Claude Opus 4.5" → "Opus 4.5"); otherwise passes the display name
    /// through unchanged. Mirrors <see cref="BuildModel"/>'s suppress-on-absent behavior: an
    /// absent/empty display name resolves to null (suppressed), never an empty string.
    /// </summary>
    public static string? ResolveModelShort(ModelInfo? model)
    {
        if (model is null || string.IsNullOrEmpty(model.DisplayName))
        {
            return null;
        }

        const string prefix = "Claude ";
        return model.DisplayName.StartsWith(prefix, StringComparison.Ordinal)
            ? model.DisplayName[prefix.Length..]
            : model.DisplayName;
    }

    /// <summary>
    /// Builds one plain-text item segment with an optional single color tag — the same
    /// markup convention as <see cref="SingleColor"/>, exposed for §3's per-item leaf
    /// rendering path where a color comes from item config rather than a fixed provider tag. This
    /// is the one place a <c>command</c> item's raw stdout (or a derived item's value pulled from
    /// one) turns into a <see cref="Segment"/>, so it is also the one place that text is
    /// sanitized (SPEC-V2-FRAMEWORK.md §3.2 rule 2): <see cref="Segment.Plain"/> is the sole width
    /// metric and must be escape-free, so it is stripped with <see cref="AnsiStrip.Strip"/>
    /// unconditionally, never left to carry a script's raw ANSI bytes into a width calculation.
    /// <see cref="Segment.Markup"/> instead best-effort *preserves* the script's own raw bytes —
    /// <see cref="Markup.Escape"/> only neutralizes Spectre's own <c>[</c> tag syntax, so an
    /// unescaped ESC byte passes through untouched and the script's own colours/links still render
    /// — with a trailing raw SGR reset appended so an unterminated colour (e.g. a bare
    /// <c>\e[31m</c>) cannot bleed into the next segment the way an unterminated OSC 8 link would.
    /// </summary>
    public static Segment BuildItemSegment(string plain, string? color)
    {
        var strippedPlain = AnsiStrip.Strip(plain);
        var rawMarkup = Markup.Escape(plain) + Markup.Escape(RawSgrReset);

        return string.IsNullOrEmpty(color)
            ? new Segment(rawMarkup, strippedPlain)
            : new Segment($"[{color}]{rawMarkup}[/]", strippedPlain);
    }

    /// <summary>
    /// SPEC-85-ADDENDUM-spans-threading.md §12/D-F: one compound part's <see cref="StyledSpan"/>
    /// markup as a clean <c>"[color]text[/]"</c> (or unstyled) wrap — unlike
    /// <see cref="BuildItemSegment(string,string?)"/>, no trailing raw SGR reset is baked in.
    /// That reset exists to stop a command's raw stdout bleeding into an adjacent segment, which
    /// does not apply here (a compound part's text is never raw script output); its escaped-bracket
    /// form also breaks <see cref="SegmentTruncation.TryGetSimpleWrap"/>'s exact-suffix match, so a
    /// span truncated mid-part would silently lose its colour.
    /// </summary>
    internal static string BuildSpanMarkup(string plain, string? color)
    {
        var escaped = Markup.Escape(plain);
        return string.IsNullOrEmpty(color) ? escaped : $"[{color}]{escaped}[/]";
    }

    /// <summary>
    /// Builds one item segment from its own already-tagged markup, with an optional outer colour
    /// wrapped around it. SPEC-V2-FRAMEWORK.md §6: a config <c>color</c> nests around an item's
    /// internal markup rather than replacing it — Spectre gives the inner tags their own span and
    /// leaves the outer colour to claim whatever text they don't.
    /// </summary>
    public static Segment BuildItemSegment(string plain, string markup, string? color, IReadOnlyList<StyledSpan>? spans = null) =>
        string.IsNullOrEmpty(color)
            ? new Segment(markup, plain, spans)
            : new Segment($"[{color}]{markup}[/]", plain, null);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §3.3: assembles a compound item's per-part spans into one
    /// <see cref="Segment"/> — the sole place that establishes the <see cref="Segment.Spans"/>
    /// invariant (§5.1: concatenated span <c>Plain</c>/<c>Markup</c> equal the segment's own).
    /// No separator is inserted between spans.
    /// </summary>
    public static Segment BuildCompoundSegment(IReadOnlyList<StyledSpan> spans) =>
        new(string.Concat(spans.Select(s => s.Markup)), string.Concat(spans.Select(s => s.Plain)), spans);

    /// <summary>
    /// SPEC-87 §12.9: applies a selecting item's colour as a floor onto a compound's spans — only
    /// where a span carries no colour of its own (its markup is exactly its escaped plain text,
    /// the shape <see cref="BuildSpanMarkup"/> produces for a null colour). A part's own colour,
    /// explicit or value-derived, is never overridden.
    /// </summary>
    internal static Segment ApplyColorFloor(Segment compound, string? floorColor)
    {
        if (string.IsNullOrEmpty(floorColor) || compound.Spans is not { Count: > 0 } spans)
        {
            return compound;
        }

        var changed = false;
        var merged = new List<StyledSpan>(spans.Count);
        foreach (var span in spans)
        {
            if (span.Markup == Markup.Escape(span.Plain))
            {
                merged.Add(new StyledSpan(span.Plain, BuildSpanMarkup(span.Plain, floorColor)));
                changed = true;
            }
            else
            {
                merged.Add(span);
            }
        }

        return changed ? BuildCompoundSegment(merged) : compound;
    }

    internal static Segment? BuildDirectory(string? cwd) =>
        string.IsNullOrEmpty(cwd) ? null : SingleColor("teal", Basename(cwd));

    internal static string? ResolveDirectory(string? cwd) =>
        string.IsNullOrEmpty(cwd) ? null : Basename(cwd);

    internal static Segment? BuildGitBranch(string? branch) =>
        string.IsNullOrEmpty(branch) ? null : SingleColor("green", branch);

    internal static string? ResolveGitBranch(string? branch) =>
        string.IsNullOrEmpty(branch) ? null : branch;

    internal static Segment? BuildRepo(RepoInfo? repo) =>
        repo is null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name)
            ? null
            : SingleColor("dim", $"{repo.Owner}/{repo.Name}");

    internal static string? ResolveRepo(RepoInfo? repo) =>
        repo is null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name)
            ? null
            : $"{repo.Owner}/{repo.Name}";

    internal const string WorktreeFormat = "worktree:{}";

    internal static Segment? BuildWorktree(WorktreeInfo? worktree) =>
        ResolveWorktree(worktree) is { } raw ? SingleColor("purple", LeafItems.ApplyFormat(WorktreeFormat, raw)) : null;

    internal static string? ResolveWorktree(WorktreeInfo? worktree)
    {
        if (worktree is null || string.IsNullOrEmpty(worktree.Name))
        {
            return null;
        }

        return string.IsNullOrEmpty(worktree.Branch) ? worktree.Name : $"{worktree.Name}({worktree.Branch})";
    }

    internal const string PullRequestFormat = "PR {}";

    internal static Segment? BuildPullRequest(PrInfo? pr) =>
        ResolvePullRequest(pr) is { } raw ? SingleColor("olive", LeafItems.ApplyFormat(PullRequestFormat, raw)) : null;

    internal static string? ResolvePullRequest(PrInfo? pr)
    {
        if (pr?.Number is not { } number)
        {
            return null;
        }

        return $"#{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}{ReviewStateSuffix(pr.ReviewState)}";
    }

    private static string ReviewStateSuffix(string? reviewState) =>
        string.IsNullOrEmpty(reviewState)
            ? string.Empty
            : reviewState switch
            {
                "approved" => " [approved]",
                "changes_requested" => " [changes]",
                "draft" => " [draft]",
                _ => $" [{reviewState}]",
            };

    internal static Segment? BuildModel(ModelInfo? model) =>
        model is null || string.IsNullOrEmpty(model.DisplayName) ? null : SingleColor("navy", model.DisplayName);

    internal static string? ResolveModel(ModelInfo? model) =>
        string.IsNullOrEmpty(model?.DisplayName) ? null : model!.DisplayName;

    internal static Segment? BuildModelShort(ModelInfo? model) =>
        ResolveModelShort(model) is { } shortName ? SingleColor("navy", shortName) : null;

    internal static Segment? BuildRemoteUrl(string? remoteUrl) =>
        string.IsNullOrEmpty(remoteUrl) ? null : SingleColor("cyan", remoteUrl);

    internal static string? ResolveRemoteUrl(string? remoteUrl) =>
        string.IsNullOrEmpty(remoteUrl) ? null : remoteUrl;

    internal const string EffortFormat = "effort:{}";

    internal static Segment? BuildEffort(EffortInfo? effort) =>
        ResolveEffort(effort) is { } raw ? SingleColor("dim", LeafItems.ApplyFormat(EffortFormat, raw)) : null;

    internal static string? ResolveEffort(EffortInfo? effort) =>
        string.IsNullOrEmpty(effort?.Level) ? null : effort!.Level;

    internal static Segment? BuildThinking(ThinkingInfo? thinking) =>
        thinking?.Enabled == true ? SingleColor("purple", "thinking") : null;

    internal static string? ResolveThinking(ThinkingInfo? thinking) =>
        thinking?.Enabled == true ? "thinking" : null;

    internal const string OutputStyleFormat = "style:{}";

    internal static Segment? BuildOutputStyle(OutputStyleInfo? style) =>
        ResolveOutputStyle(style) is { } raw ? SingleColor("dim", LeafItems.ApplyFormat(OutputStyleFormat, raw)) : null;

    internal static string? ResolveOutputStyle(OutputStyleInfo? style) =>
        IsDefaultOrEmptyStyle(style) ? null : style!.Name;

    private static bool IsDefaultOrEmptyStyle(OutputStyleInfo? style) =>
        style is null || string.IsNullOrEmpty(style.Name) || string.Equals(style.Name, "default", StringComparison.OrdinalIgnoreCase);

    internal static Segment? BuildContext(ContextWindowInfo? ctx)
    {
        var pctInt = RoundHalfToEven(EffectiveContextPercentage(ctx));
        var tag = ColorResolution.ResolveStandardThreshold(pctInt);
        var plain = ResolveContextDisplayText(ctx)!;

        if (ctx?.UsedPercentage is not null && ctx.TotalInputTokens is { } totalInput && ctx.ContextWindowSize is { } size)
        {
            var markup = $"ctx:[{tag}]{pctInt}%[/] [dim]({totalInput / 1000}k/{size / 1000}k)[/]";
            return new Segment(markup, plain);
        }
        else
        {
            var markup = $"ctx:[{tag}]{pctInt}%[/]";
            return new Segment(markup, plain);
        }
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §4: context's registry-row text function — the composite display text
    /// (the "ctx:" label plus the token-count parenthetical when both fields are present), shared
    /// by <see cref="BuildContext"/>'s markup and the configured-<c>items</c> path via
    /// <see cref="ItemRegistry"/>. Can't be a format string: the parenthetical needs
    /// <see cref="ContextWindowInfo.TotalInputTokens"/>/<see cref="ContextWindowInfo.ContextWindowSize"/>,
    /// neither of which is present in <see cref="ResolveContext"/>'s raw value.
    /// </summary>
    internal static string? ResolveContextDisplayText(ContextWindowInfo? ctx)
    {
        var pctInt = RoundHalfToEven(EffectiveContextPercentage(ctx));
        return ctx?.UsedPercentage is not null && ctx.TotalInputTokens is { } totalInput && ctx.ContextWindowSize is { } size
            ? $"ctx:{pctInt}% ({totalInput / 1000}k/{size / 1000}k)"
            : $"ctx:{pctInt}%";
    }

    /// <summary>
    /// The context-window percentage to render when the harness has reported none.
    /// A session that has sent nothing has genuinely used none of its window, so an
    /// absent percentage is zero rather than unknown — see SPEC context-zero-render §2.5.
    /// </summary>
    internal static double EffectiveContextPercentage(ContextWindowInfo? ctx) =>
        ctx?.UsedPercentage ?? 0.0;

    /// <summary>
    /// The bare percentage, with no "ctx:" label and no token-count detail — unlike
    /// <see cref="ResolveContextDisplayText"/>'s composite, this is meant to also parse as a number
    /// so a numeric §6 "thresholds" rule can be applied to it when it's selected into a pane's
    /// <c>items</c> list.
    /// </summary>
    internal static string? ResolveContext(ContextWindowInfo? ctx) =>
        RoundHalfToEven(EffectiveContextPercentage(ctx))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static Segment? BuildRateLimits(RateLimitsInfo? rateLimits)
    {
        string? fivePlain = null, fiveMarkup = null;
        if (rateLimits?.FiveHour?.UsedPercentage is { } fivePct)
        {
            var v = RoundHalfToEven(fivePct);
            var tag = ColorResolution.ResolveStandardThreshold(v);
            fivePlain = $"5h:{v}%";
            fiveMarkup = $"5h:[{tag}]{v}%[/]";
        }

        string? sevenPlain = null, sevenMarkup = null;
        if (rateLimits?.SevenDay?.UsedPercentage is { } sevenPct)
        {
            var v = RoundHalfToEven(sevenPct);
            var tag = ColorResolution.ResolveStandardThreshold(v);
            sevenPlain = $"7d:{v}%";
            sevenMarkup = $"7d:[{tag}]{v}%[/]";
        }

        if (fivePlain is null && sevenPlain is null)
        {
            return null;
        }

        var plain = fivePlain is not null && sevenPlain is not null
            ? $"{fivePlain} / {sevenPlain}"
            : fivePlain ?? sevenPlain!;
        var markup = fiveMarkup is not null && sevenMarkup is not null
            ? $"{fiveMarkup} [dim]/[/] {sevenMarkup}"
            : fiveMarkup ?? sevenMarkup!;

        return new Segment(markup, plain);
    }

    /// <summary>
    /// The maximum used-percentage across the two windows, as a bare parseable number — unlike
    /// <see cref="BuildRateLimits"/>'s composite label text, this is what a numeric §6 "thresholds"
    /// rule sourced from rate-limits evaluates against, mirroring how <see cref="ResolveContext"/>
    /// exposes context's bare percentage for the same purpose.
    /// </summary>
    internal static string? ResolveRateLimits(RateLimitsInfo? rateLimits)
    {
        var five = rateLimits?.FiveHour?.UsedPercentage;
        var seven = rateLimits?.SevenDay?.UsedPercentage;

        if (five is null && seven is null)
        {
            return null;
        }

        var max = Math.Max(five ?? double.NegativeInfinity, seven ?? double.NegativeInfinity);
        return RoundHalfToEven(max).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal const string AgentFormat = "agent:{}";

    internal static Segment? BuildAgent(AgentInfo? agent) =>
        ResolveAgent(agent) is { } raw ? SingleColor("purple", LeafItems.ApplyFormat(AgentFormat, raw)) : null;

    internal static string? ResolveAgent(AgentInfo? agent) =>
        string.IsNullOrEmpty(agent?.Name) ? null : agent!.Name;

    internal static Segment? BuildEngram(EngramResult? engram)
    {
        if (engram is null || (engram.Facts is null && engram.Verb is null))
        {
            return null;
        }

        string? factsPlain = engram.Facts is { } facts ? $"engram:{facts}" : null;
        string? factsMarkup = factsPlain is not null ? $"[dim]{factsPlain}[/]" : null;

        string? verbPlain = engram.Verb;
        string? verbMarkup = verbPlain is not null ? $"[purple]{Markup.Escape(verbPlain)}[/]" : null;

        var plain = factsPlain is not null && verbPlain is not null
            ? $"{factsPlain} {verbPlain}"
            : factsPlain ?? verbPlain!;
        var markup = factsMarkup is not null && verbMarkup is not null
            ? $"{factsMarkup} {verbMarkup}"
            : factsMarkup ?? verbMarkup!;

        return new Segment(markup, plain);
    }

    // Facts + verb are two independent optional fields, same "two labels disambiguate two
    // values" reasoning as ResolveRateLimits — reuses the composite's plain text rather than
    // inventing a reduced scalar.
    internal static string? ResolveEngram(EngramResult? engram) => BuildEngram(engram)?.Plain;

    internal const string VimFormat = "[{}]";

    internal static Segment? BuildVimMode(VimInfo? vim) =>
        ResolveVim(vim) is { } raw ? SingleColor("olive", LeafItems.ApplyFormat(VimFormat, raw)) : null;

    internal static string? ResolveVim(VimInfo? vim) =>
        string.IsNullOrEmpty(vim?.Mode) ? null : vim!.Mode;

    private static int RoundHalfToEven(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

    private static string Basename(string path)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/";
        }

        var idx = trimmed.LastIndexOf('/');
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
    }
}
