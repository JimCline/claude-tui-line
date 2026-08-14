namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3: resolves a pane tree's sizes. Vertical splits divide width and run
/// the full bounded fixpoint (intrinsic measurement, the six-step single-pass allocation, the
/// over-constrained drop loop, monotone-clamped re-measurement capped at three passes) — this is
/// the axis both §2.9 acceptance cases and every §10 fixpoint test exercise. Horizontal
/// splits divide nothing along height in this phase: every child simply inherits the split's full
/// width and renders to its own natural row count, because §2.8 (the row budget a real height
/// division would need to divide) is out of scope for Phase 3 — see the phase-3 report for this
/// as a disclosed scope boundary, not an oversight.
/// </summary>
public static class SizeResolver
{
    private const int MaxPasses = 3;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.5.1: leaf rendering is a pure function of (items, innerWidth) — no
    /// surface-derived value may reach it through the <see cref="Pane"/> record itself, since a
    /// <c>Pane</c> is shared across terminal widths and heights. <see cref="ClipRows"/> and
    /// <see cref="ItemsEmptied"/> are §2.8.1 degrade-ladder output for one render attempt against
    /// one surface budget, so they live here instead, alongside the rest of that attempt's layout —
    /// <see cref="HeightLadder"/> annotates a freshly resolved tree with them, never a shared
    /// <c>Pane</c>.
    /// </summary>
    public sealed record ResolvedPane(Pane Source, int OuterWidth, IReadOnlyList<ResolvedPane> Children, int? ClipRows = null, bool ItemsEmptied = false);

    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, RenderNoteCollector notes) =>
        ResolveNode(root, outerWidth, ctx, values, measureOverride: null, notes);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §10.6's three fixpoint tests need a "content" pane whose reported
    /// request is independent of real segment measurement — a stub that requests more
    /// width when granted less (the monotone-clamp test), or that changes its request every pass
    /// (the pass-cap test). <paramref name="measureOverride"/>, when supplied, replaces
    /// <see cref="MeasureRequest"/> for every content-kind pane in the tree; production callers
    /// never pass it, so real rendering is unaffected.
    /// </summary>
    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int> measureOverride, RenderNoteCollector notes) =>
        ResolveNode(root, outerWidth, ctx, values, measureOverride, notes);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.3: <see cref="RowLayout.MinUsableWidth"/> governs viability for
    /// <c>fill</c> and percent panes only — the same panes <see cref="Floor"/> treats as having a
    /// real minimum. A <c>content</c> or fixed-size pane got exactly the width it asked for, so it
    /// is never "squeezed" and never suppresses its border no matter how narrow that width is —
    /// see <see cref="PaneBorderRenderer"/>'s <c>suppressed</c> parameter.
    /// </summary>
    public static bool ShouldSuppressBorder(Pane pane, int outerWidth)
    {
        if (pane.Border.Style is null || outerWidth >= RowLayout.MinUsableWidth)
        {
            return false;
        }

        var kind = ClassifySize(pane.Size).Kind;
        return kind is SizeKind.Fill or SizeKind.Percent;
    }

    /// <summary>SPEC-V2-FRAMEWORK.md §4: whether a pane is "pane-width eligible" for a command item's cache stamp — a <c>content</c>-sized pane never is, since its width is a function of its own content rather than an independent layout grant.</summary>
    public static bool IsContentSized(Pane pane) => ClassifySize(pane.Size).Kind == SizeKind.Content;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4: whether a pane's size classifies as <see cref="SizeKind.Fill"/> —
    /// the catch-all for <c>"fill"</c>, <c>"auto"</c>, and anything unrecognized. <c>pane-no-items</c>
    /// needs this alongside <see cref="IsContentSized"/>: a <c>fixed</c>/<c>percent</c> pane with no
    /// items is a deliberate spacer (§2.11) and keeps its declared extent, but a <c>content</c>/
    /// <c>fill</c> pane with no items collapses to zero, so its declaration did nothing.
    /// </summary>
    internal static bool IsFillSized(Pane pane) => ClassifySize(pane.Size).Kind == SizeKind.Fill;

    // §2.10: 2 verticals + 2 padding cells when a pane draws a border — the same figure the
    // pre-split single-pane pipeline has always used.
    private const int BorderWidthReserve = 4;

    // §2.10.1 rule 5: top + bottom.
    private const int BorderRowReserve = 2;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10: <c>reserve(p)</c> — the width a pane itself consumes for its own
    /// border, charged wherever that pane is measured (its own <see cref="Floor"/>/request, or a
    /// split subtracting its own border before dividing width among children). Named so every call
    /// site shares one definition instead of repeating the same ternary.
    /// </summary>
    internal static int OwnBorderReserve(Pane pane) => OwnBorderReserve(pane.Border);

    internal static int OwnBorderReserve(PaneBorder border) => border.Style is not null ? BorderWidthReserve : 0;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 5: <c>rowReserve(p)</c> — the row-count counterpart to
    /// <see cref="OwnBorderReserve(Pane)"/>, charged wherever a pane's row budget is computed.
    /// </summary>
    internal static int OwnRowReserve(Pane pane) => pane.Border.Style is not null ? BorderRowReserve : 0;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10: what a vertical split reserves for itself before dividing width
    /// among its <paramref name="childCount"/> children — its own <see cref="OwnBorderReserve"/>
    /// plus the gutters between children (<c>collapse: false</c>'s arithmetic, the only kind
    /// implemented today; each child's own edges are its own <see cref="OwnBorderReserve"/>,
    /// already folded into that child's <see cref="Floor"/>/request, not charged again here).
    /// Named per §9.5's rule applied to arithmetic instead of references: §9.8's "can this ever
    /// fit" check calls this exact function rather than holding a second copy that can drift from
    /// what the allocator below actually runs.
    /// </summary>
    internal static int BoundaryCost(Pane split, int childCount) => OwnBorderReserve(split) + split.Gutter * Math.Max(0, childCount - 1);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.8: a pane's own declared fixed-cell size, when its <see cref="Pane.Size"/>
    /// classifies as <see cref="SizeKind.Fixed"/> — the structural checks need this exact
    /// classification, the same one <see cref="ClassifySize"/> already runs for the allocator, not a
    /// second parse of the same string.
    /// </summary>
    internal static int? FixedSize(Pane pane) => ClassifySize(pane.Size) is { Kind: SizeKind.Fixed } spec ? spec.FixedValue : null;

    private static ResolvedPane ResolveNode(Pane pane, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int>? measureOverride, RenderNoteCollector notes)
    {
        if (pane.Split == PaneSplit.None || pane.Children.Count == 0)
        {
            return new ResolvedPane(pane, outerWidth, Array.Empty<ResolvedPane>());
        }

        if (pane.Split == PaneSplit.Horizontal)
        {
            var horizontalChildren = pane.Children
                .Select(c => ResolveNode(c, outerWidth, ctx, values, measureOverride, notes))
                .ToList();
            return new ResolvedPane(pane, outerWidth, horizontalChildren);
        }

        var alloc = pane.Distribute switch
        {
            PaneDistribute.MinRows => ResolveVerticalMinRows(pane, outerWidth, ctx, values),
            PaneDistribute.Even => ResolveVerticalEven(pane, outerWidth),
            _ => ResolveVertical(pane, outerWidth, ctx, values, measureOverride, notes),
        };
        var resolvedChildren = new List<ResolvedPane>(alloc.Children.Count);
        for (var i = 0; i < alloc.Children.Count; i++)
        {
            resolvedChildren.Add(ResolveNode(alloc.Children[i], alloc.Grants[i], ctx, values, measureOverride, notes));
        }

        return new ResolvedPane(pane, outerWidth, resolvedChildren);
    }

    // ---- vertical axis: the graded fixpoint ----

    private static AllocResult ResolveVertical(Pane split, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int>? measureOverride, RenderNoteCollector notes)
    {
        Func<Pane, int?, int> measure = measureOverride ?? ((c, w) => MeasureRequest(c, w, ctx, values));

        var initialChildren = split.Children;
        var requests = initialChildren
            .Select(c => measure(c, null))
            .ToArray();

        var result = AllocateWithDrop(split, initialChildren, splitOuterWidth, requests, notes);
        requests = requests.Take(result.Children.Count).ToArray();

        for (var pass = 1; pass < MaxPasses; pass++)
        {
            var nextRequests = new int[result.Children.Count];
            var changed = false;

            for (var i = 0; i < result.Children.Count; i++)
            {
                var child = result.Children[i];
                if (ClassifySize(child.Size).Kind != SizeKind.Content)
                {
                    nextRequests[i] = requests[i];
                    continue;
                }

                var remeasured = measure(child, result.Grants[i]);
                var clamped = Math.Min(remeasured, requests[i]); // monotone: a request may never grow between passes.
                nextRequests[i] = clamped;
                changed |= clamped != requests[i];
            }

            if (!changed)
            {
                break;
            }

            requests = nextRequests;
            result = AllocateWithDrop(split, result.Children, splitOuterWidth, requests, notes);
            requests = requests.Take(result.Children.Count).ToArray();
        }

        return result;
    }

    private enum SizeKind { Fixed, Percent, Content, Fill }

    private readonly record struct SizeSpec(SizeKind Kind, int FixedValue, double Pct);

    private readonly record struct AllocResult(IReadOnlyList<Pane> Children, IReadOnlyList<int> Grants);

    private static SizeSpec ClassifySize(string? size)
    {
        var trimmed = (size ?? "auto").Trim();

        if (int.TryParse(trimmed, out var fixedVal))
        {
            return new SizeSpec(SizeKind.Fixed, fixedVal, 0);
        }

        if (trimmed.EndsWith('%') && double.TryParse(trimmed[..^1], out var pct))
        {
            return new SizeSpec(SizeKind.Percent, 0, pct / 100.0);
        }

        if (string.Equals(trimmed, "content", StringComparison.OrdinalIgnoreCase))
        {
            return new SizeSpec(SizeKind.Content, 0, 0);
        }

        // "fill", "auto", and anything unrecognized.
        return new SizeSpec(SizeKind.Fill, 0, 0);
    }

    /// <summary>
    /// True when <paramref name="size"/> was present but matched none of §2.3's recognized forms
    /// (an integer, a percent, <c>"content"</c>, <c>"fill"</c>, or <c>"auto"</c>) — distinct from an
    /// absent field, both of which <see cref="ClassifySize"/> silently falls back to
    /// <see cref="SizeKind.Fill"/>. §9.4's config diagnostics need this distinction; the renderer's
    /// fallback does not.
    /// </summary>
    internal static bool IsUnrecognizedSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return false;
        }

        var trimmed = size.Trim();
        if (int.TryParse(trimmed, out _))
        {
            return false;
        }

        if (trimmed.EndsWith('%') && double.TryParse(trimmed[..^1], out _))
        {
            return false;
        }

        return !string.Equals(trimmed, "content", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "fill", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// §2.3/§9.4: true when <paramref name="size"/> is the <c>"auto"</c> synonym for <c>"fill"</c> —
    /// legal and unchanged in behavior, but its plain-English reading suggests sizing to content
    /// (what <c>"content"</c> actually does), the opposite of what it resolves to.
    /// </summary>
    internal static bool IsDeprecatedSizeAlias(string? size) =>
        !string.IsNullOrWhiteSpace(size) && string.Equals(size.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    // §2.3 floor(p): the minimum outer width a pane can be reduced to before it is dropped.
    // A vertical split's children divide the available width, so its floor is Σ floor(children) +
    // gutters. A horizontal split's children stack and each takes the full width, so its floor is
    // max(floor(children)) — untested in Phase 3 since no acceptance or required test nests a
    // horizontal split inside a vertical one.
    private static int Floor(Pane p)
    {
        if (p.MinSize is int min)
        {
            return min;
        }

        if (p.Split != PaneSplit.None && p.Children.Count > 0)
        {
            if (p.Split == PaneSplit.Horizontal)
            {
                return p.Children.Max(Floor);
            }

            var gutters = p.Gutter * Math.Max(0, p.Children.Count - 1);
            return p.Children.Sum(Floor) + gutters;
        }

        var spec = ClassifySize(p.Size);
        return spec.Kind switch
        {
            SizeKind.Fixed => spec.FixedValue,
            SizeKind.Content => 0,
            _ => RowLayout.MinUsableWidth + OwnBorderReserve(p),
        };
    }

    // One run of the six-step allocation (§2.3), operating on whatever child list/request set the
    // caller currently has — a single pass, no fixpoint, no dropping.
    private static AllocResult AllocateOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, IReadOnlyList<int> requests)
    {
        var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count));

        var kinds = children.Select(c => ClassifySize(c.Size)).ToArray();
        var grants = new int[children.Count];
        var rem = avail;

        // Step 2: fixed.
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fixed)
            {
                grants[i] = kinds[i].FixedValue;
                rem -= grants[i];
            }
        }

        // Step 3: reserve.
        var reserve = 0;
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind is SizeKind.Percent or SizeKind.Fill)
            {
                reserve += Floor(children[i]);
            }
        }

        // Step 4: content, declaration order.
        var contentIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Content)
            {
                contentIndices.Add(i);
            }
        }

        for (var ci = 0; ci < contentIndices.Count; ci++)
        {
            var i = contentIndices[ci];
            var laterMinSum = 0;
            for (var cj = ci + 1; cj < contentIndices.Count; cj++)
            {
                laterMinSum += children[contentIndices[cj]].MinSize ?? 0;
            }

            var cap = rem - reserve - laterMinSum;
            var minSize = children[i].MinSize ?? 0;
            var maxSize = children[i].MaxSize ?? int.MaxValue;
            var upperBound = Math.Max(minSize, Math.Min(maxSize, cap));
            var grant = Math.Clamp(requests[i], minSize, upperBound);
            grants[i] = grant;
            rem -= grant;
        }

        // Step 5: percent.
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Percent)
            {
                var raw = (int)Math.Round(kinds[i].Pct * avail, MidpointRounding.AwayFromZero);
                var grant = Math.Clamp(raw, 0, Math.Max(0, rem));
                grants[i] = grant;
                rem -= grant;
            }
        }

        // Step 6: fill, leftover to the leftmost.
        var fillIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fill)
            {
                fillIndices.Add(i);
            }
        }

        if (fillIndices.Count > 0)
        {
            var remClamped = Math.Max(0, rem);
            var each = remClamped / fillIndices.Count;
            var leftover = remClamped - each * fillIndices.Count;
            for (var fi = 0; fi < fillIndices.Count; fi++)
            {
                grants[fillIndices[fi]] = each + (fi == 0 ? leftover : 0);
            }
        }

        return new AllocResult(children, grants);
    }

    // §2.3's over-constrained handling: a non-fixed pane granted under 1 cell means the split
    // cannot honor everyone at once — drop the last child and recompute from step 1. Bounded: each
    // iteration strictly shrinks the child list, so this always terminates.
    private static AllocResult AllocateWithDrop(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, IReadOnlyList<int> requests, RenderNoteCollector notes)
    {
        var current = children;
        var currentRequests = requests;

        while (true)
        {
            var result = AllocateOnePass(split, current, splitOuterWidth, currentRequests);

            var tooSmall = false;
            for (var i = 0; i < result.Grants.Count; i++)
            {
                if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
                {
                    tooSmall = true;
                    break;
                }
            }

            if (!tooSmall || current.Count <= 1)
            {
                return result;
            }

            // §9.8.2: the dropped pane is always the current list's last child, whose 1-based
            // position in it equals current.Count before this truncation — stable across repeated
            // drops because only the tail is ever removed.
            notes.Add($"pane {current.Count} dropped: no width remained at {splitOuterWidth} columns");
            current = current.Take(current.Count - 1).ToList();
            currentRequests = currentRequests.Take(current.Count).ToList();
        }
    }

    // ---- vertical axis: min-rows distribution (§2.3.1) ----

    /// <summary>
    /// Test-only diagnostic: counts calls to <see cref="RowCountAt"/> — one packer invocation
    /// each — so a test can assert the actual cost of a min-rows resolve instead of reasoning
    /// about it. Production callers never read this.
    ///
    /// <see cref="ThreadStaticAttribute"/> is sound here specifically because §5 resolves once
    /// per render, synchronously: nothing on the min-rows path (<see cref="SolveMinRows"/>
    /// included) ever awaits, so the thread that zeroes this counter is guaranteed to be the same
    /// thread that later reads it, even though xUnit runs other test classes concurrently on
    /// other threads. If sizing ever gains an <c>await</c>, that guarantee breaks and this
    /// attribute becomes wrong rather than merely redundant — re-check this reasoning before
    /// introducing one on the min-rows path.
    /// </summary>
    [ThreadStatic]
    internal static int MinRowsPackerInvocationCount;

    // Structurally mirrors AllocateWithDrop's drop-retry loop rather than sharing it: the two
    // allocate differently enough (AllocateWithDrop threads a per-child request array that
    // min-rows has no equivalent of) that forcing a shared implementation would risk the
    // unchanged greedy path for a small amount of duplication.
    private static AllocResult ResolveVerticalMinRows(Pane split, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var current = split.Children;

        while (true)
        {
            var result = AllocateMinRowsOnePass(split, current, splitOuterWidth, ctx, values);

            var tooSmall = false;
            for (var i = 0; i < result.Grants.Count; i++)
            {
                if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
                {
                    tooSmall = true;
                    break;
                }
            }

            if (!tooSmall || current.Count <= 1)
            {
                return result;
            }

            current = current.Take(current.Count - 1).ToList();
        }
    }

    // One run of the min-rows allocation (§2.3.1): fixed and percent panes take their width
    // first, mirroring AllocateOnePass's own step order and matching R's own prose definition —
    // "the extent remaining after fixed and percent panes and gutters have taken theirs" — then
    // every content/fill pane is a candidate for the row-count search.
    private static AllocResult AllocateMinRowsOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count));

        var kinds = children.Select(c => ClassifySize(c.Size)).ToArray();
        var grants = new int[children.Count];
        var rem = avail;

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fixed)
            {
                grants[i] = kinds[i].FixedValue;
                rem -= grants[i];
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Percent)
            {
                var raw = (int)Math.Round(kinds[i].Pct * avail, MidpointRounding.AwayFromZero);
                var grant = Math.Clamp(raw, 0, Math.Max(0, rem));
                grants[i] = grant;
                rem -= grant;
            }
        }

        var candidateIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind is SizeKind.Content or SizeKind.Fill)
            {
                candidateIndices.Add(i);
            }
        }

        if (candidateIndices.Count > 0)
        {
            var r = Math.Max(0, rem);
            var solved = SolveMinRows(children, candidateIndices, r, ctx, values);
            for (var ci = 0; ci < candidateIndices.Count; ci++)
            {
                grants[candidateIndices[ci]] = solved[ci];
            }
        }

        return new AllocResult(children, grants);
    }

    // SPEC-V2-FRAMEWORK.md §2.3.1: T is the achievable row count, searched from 1 upward — the
    // first T for which every candidate's minWidth(i, T) fits within r wins, since feasible(T) is
    // monotone (a larger T only relaxes each candidate's minWidth). Bounded by the most rows any
    // candidate could ever need — its own rendered segment count, since no row holds more than
    // one segment fewer than the next — never by an arbitrary constant. Falls back to every
    // candidate at its own floor when no T up to that bound is feasible (the split as a whole is
    // over-constrained), the same outcome AllocateOnePass's own step 4 falls back to; the outer
    // drop-retry loop in ResolveVerticalMinRows exists for exactly this case.
    private static int[] SolveMinRows(IReadOnlyList<Pane> children, IReadOnlyList<int> candidateIndices, int r, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var n = candidateIndices.Count;
        var candidates = candidateIndices.Select(i => children[i]).ToList();
        var lo = candidates.Select(Floor).ToArray();
        var hi = new int[n];
        for (var ci = 0; ci < n; ci++)
        {
            hi[ci] = Math.Max(Math.Min(candidates[ci].MaxSize ?? r, r), lo[ci]);
        }

        var maxT = 1;
        foreach (var candidate in candidates)
        {
            maxT = Math.Max(maxT, CandidateSegments(candidate, ctx, values).Count);
        }

        for (var t = 1; t <= maxT; t++)
        {
            var minWidths = new int[n];
            var feasible = true;
            var sum = 0;

            for (var ci = 0; ci < n; ci++)
            {
                var w = MinWidthForRowCount(candidates[ci], lo[ci], hi[ci], t, ctx, values);
                if (w is not int width)
                {
                    feasible = false;
                    break;
                }

                minWidths[ci] = width;
                sum += width;
            }

            if (feasible && sum <= r)
            {
                var surplus = r - sum;
                return WaterFillSurplus(candidates, minWidths, hi, surplus, ctx, values);
            }
        }

        return lo;
    }

    // minWidth(i, T): the narrowest outer width at which candidate i's real rendering — the same
    // packer every other render path calls — achieves T rows or fewer. rows_i(w) is non-increasing
    // in w (more width never adds rows), so binary search over [lo, hi] is valid; returns null when
    // even hi cannot reach T rows.
    private static int? MinWidthForRowCount(Pane candidate, int lo, int hi, int t, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        if (RowCountAt(candidate, hi, ctx, values) > t)
        {
            return null;
        }

        var low = lo;
        var high = hi;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (RowCountAt(candidate, mid, ctx, values) <= t)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    // rows_i(w): candidate i's real row count at outer width w, via the same packer every leaf
    // actually renders through (PaneAssembler.RenderLeafRows's own path) — §2.3.1 requires "the
    // existing packer, called unchanged", not a re-derived width twin.
    private static int RowCountAt(Pane candidate, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        MinRowsPackerInvocationCount++;

        var innerWidth = Math.Max(0, outerWidth - OwnBorderReserve(candidate));
        var segments = CandidateSegments(candidate, ctx, values);
        var overflow = PaneAssembler.ResolveOverflow(candidate);
        // This is a probe of a candidate width during the row-count search, not a rendered row of
        // the final surface — its own truncations are not the render's, so they get a throwaway
        // collector rather than the one flowing to the caller's real result.
        var buffer = PaneRenderer.RenderLeaf(segments, innerWidth, overflow, candidate.Ellipsis, new RenderNoteCollector(), allowFallback: false);
        return buffer.Rows.Count;
    }

    // SPEC-V2-FRAMEWORK.md §2.3.1: the winning T's leftover width is spent purely on evenness —
    // repeatedly handing one cell to the currently-narrowest eligible candidate — since giving any
    // candidate more width than its own minWidth(i, T) can never raise its row count above T. A
    // content candidate is capped at its own natural (unwrapped) width, matching the existing
    // MeasureRequest call greedy itself uses: extra columns past that point are not a benefit to
    // it, so surplus flows on to whichever candidates can still use it. If every candidate is
    // capped and surplus remains, it is left unspent, exactly as greedy leaves an unconsumed
    // remainder.
    private static int[] WaterFillSurplus(IReadOnlyList<Pane> candidates, int[] minWidths, int[] hi, int surplus, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var n = candidates.Count;
        var widths = (int[])minWidths.Clone();
        var cap = new int[n];

        for (var ci = 0; ci < n; ci++)
        {
            cap[ci] = ClassifySize(candidates[ci].Size).Kind == SizeKind.Content
                ? Math.Min(hi[ci], MeasureRequest(candidates[ci], null, ctx, values))
                : hi[ci];
        }

        var remaining = surplus;
        while (remaining > 0)
        {
            var narrowest = -1;
            for (var ci = 0; ci < n; ci++)
            {
                if (widths[ci] >= cap[ci])
                {
                    continue;
                }

                if (narrowest == -1 || widths[ci] < widths[narrowest])
                {
                    narrowest = ci;
                }
            }

            if (narrowest == -1)
            {
                break;
            }

            widths[narrowest]++;
            remaining--;
        }

        return widths;
    }

    // ---- vertical axis: even distribution (§2.3) ----

    // Structurally mirrors ResolveVerticalMinRows's drop-retry loop for the same reason: §2.3's
    // over-constrained handling ("no child may resolve below 1 cell ... dropped entirely") applies
    // regardless of which policy divides the remaining extent.
    private static AllocResult ResolveVerticalEven(Pane split, int splitOuterWidth)
    {
        var current = split.Children;

        while (true)
        {
            var result = AllocateEvenOnePass(split, current, splitOuterWidth);

            var tooSmall = false;
            for (var i = 0; i < result.Grants.Count; i++)
            {
                if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
                {
                    tooSmall = true;
                    break;
                }
            }

            if (!tooSmall || current.Count <= 1)
            {
                return result;
            }

            current = current.Take(current.Count - 1).ToList();
        }
    }

    // One run of the even allocation (§2.3): fixed and percent panes take their width first,
    // mirroring AllocateOnePass's/AllocateMinRowsOnePass's own step order, then every content/fill
    // pane splits what remains equally, leftover to the leftmost (matching AllocateOnePass's step
    // 6) — ignoring intrinsic measurement and the content/fill distinction entirely, which is the
    // property that keeps the layout from moving as content changes. A content candidate still
    // degrades under its own §2.6 rules at whatever width it lands on; this only decides the width,
    // not what happens to it afterward.
    private static AllocResult AllocateEvenOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth)
    {
        var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count));

        var kinds = children.Select(c => ClassifySize(c.Size)).ToArray();
        var grants = new int[children.Count];
        var rem = avail;

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fixed)
            {
                grants[i] = kinds[i].FixedValue;
                rem -= grants[i];
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Percent)
            {
                var raw = (int)Math.Round(kinds[i].Pct * avail, MidpointRounding.AwayFromZero);
                var grant = Math.Clamp(raw, 0, Math.Max(0, rem));
                grants[i] = grant;
                rem -= grant;
            }
        }

        var candidateIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind is SizeKind.Content or SizeKind.Fill)
            {
                candidateIndices.Add(i);
            }
        }

        if (candidateIndices.Count > 0)
        {
            var remClamped = Math.Max(0, rem);
            var each = remClamped / candidateIndices.Count;
            var leftover = remClamped - each * candidateIndices.Count;
            for (var ci = 0; ci < candidateIndices.Count; ci++)
            {
                grants[candidateIndices[ci]] = each + (ci == 0 ? leftover : 0);
            }
        }

        return new AllocResult(children, grants);
    }

    // ---- intrinsic measurement: the same fits-or-degrade decision the renderer uses (LeafContent) ----

    private static int MeasureRequest(Pane pane, int? grantedOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var borderReserve = OwnBorderReserve(pane);
        var innerCap = grantedOuterWidth is int g ? Math.Max(0, g - borderReserve) : (int?)null;
        return MeasureInnerContentWidth(pane, innerCap, ctx, values) + borderReserve;
    }

    private static int MeasureInnerContentWidth(Pane pane, int? innerCap, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var segments = CandidateSegments(pane, ctx, values);
        return innerCap is int cap ? LongestWrappedRowWidth(segments, cap) : UnwrappedWidth(segments);
    }

    // The segment list a pane actually renders — the default 14 builtins when no items are
    // configured, or its resolved items otherwise — shared by the existing content-width
    // measurement above and the min-rows row-count search below so the two never drift apart.
    // Color is never resolved here: RowLayout.Wrap's row-break decision only ever inspects
    // Segment.Plain.Length.
    private static IReadOnlyList<Segment> CandidateSegments(Pane pane, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        if (pane.Items.Count == 0)
        {
            return SegmentBuilder.Build(ctx);
        }

        var packedGroup = new List<Segment>();

        foreach (var resolved in LeafItems.Resolve(pane.Items, values, ctx))
        {
            if (resolved.Value is null)
            {
                continue;
            }

            var decision = LeafContent.Decide(resolved, values);
            packedGroup.Add(SegmentBuilder.BuildItemSegment(decision.Text, null));
        }

        return packedGroup;
    }

    // The same unwrapped-single-row arithmetic RowLayout.Wrap's packing loop produces, so a
    // content pane's intrinsic request equals what it would actually render at that width.
    private static int UnwrappedWidth(IReadOnlyList<Segment> segments)
    {
        if (segments.Count == 0)
        {
            return 0;
        }

        return segments.Sum(s => s.Plain.Length) + RowLayout.SeparatorWidth * (segments.Count - 1);
    }

    // SPEC-V2-FRAMEWORK.md §2.3/§2.9: a content pane re-measured under a narrower grant reports
    // the width of its longest wrapped row at that width, not the grant itself — freed columns
    // must reach the sibling rather than sit unused inside the anchor. Reproduces RowLayout.Wrap's
    // exact row-break decision (rowWidth + SeparatorWidth + segWidth <= cap) rather than calling
    // it directly, because this only needs each row's width, not its rendered markup.
    private static int LongestWrappedRowWidth(IReadOnlyList<Segment> segments, int cap)
    {
        if (segments.Count == 0)
        {
            return 0;
        }

        var maxWidth = 0;
        var rowWidth = 0;
        var rowStarted = false;

        foreach (var seg in segments)
        {
            var segWidth = seg.Plain.Length;

            if (!rowStarted)
            {
                rowWidth = segWidth;
                rowStarted = true;
            }
            else if (rowWidth + RowLayout.SeparatorWidth + segWidth <= cap)
            {
                rowWidth += RowLayout.SeparatorWidth + segWidth;
            }
            else
            {
                maxWidth = Math.Max(maxWidth, rowWidth);
                rowWidth = segWidth;
            }
        }

        return Math.Max(maxWidth, rowWidth);
    }
}
