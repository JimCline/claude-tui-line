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

    public sealed record ResolvedPane(Pane Source, int OuterWidth, IReadOnlyList<ResolvedPane> Children);

    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values) =>
        ResolveNode(root, outerWidth, ctx, values, measureOverride: null);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §10.6's three fixpoint tests need a "content" pane whose reported
    /// request is independent of real segment measurement — a stub that requests more
    /// width when granted less (the monotone-clamp test), or that changes its request every pass
    /// (the pass-cap test). <paramref name="measureOverride"/>, when supplied, replaces
    /// <see cref="MeasureRequest"/> for every content-kind pane in the tree; production callers
    /// never pass it, so real rendering is unaffected.
    /// </summary>
    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int> measureOverride) =>
        ResolveNode(root, outerWidth, ctx, values, measureOverride);

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

    private static ResolvedPane ResolveNode(Pane pane, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int>? measureOverride)
    {
        if (pane.Split == PaneSplit.None || pane.Children.Count == 0)
        {
            return new ResolvedPane(pane, outerWidth, Array.Empty<ResolvedPane>());
        }

        if (pane.Split == PaneSplit.Horizontal)
        {
            var horizontalChildren = pane.Children
                .Select(c => ResolveNode(c, outerWidth, ctx, values, measureOverride))
                .ToList();
            return new ResolvedPane(pane, outerWidth, horizontalChildren);
        }

        var alloc = ResolveVertical(pane, outerWidth, ctx, values, measureOverride);
        var resolvedChildren = new List<ResolvedPane>(alloc.Children.Count);
        for (var i = 0; i < alloc.Children.Count; i++)
        {
            resolvedChildren.Add(ResolveNode(alloc.Children[i], alloc.Grants[i], ctx, values, measureOverride));
        }

        return new ResolvedPane(pane, outerWidth, resolvedChildren);
    }

    // ---- vertical axis: the graded fixpoint ----

    private static AllocResult ResolveVertical(Pane split, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int>? measureOverride)
    {
        Func<Pane, int?, int> measure = measureOverride ?? ((c, w) => MeasureRequest(c, w, ctx, values));

        var initialChildren = split.Children;
        var requests = initialChildren
            .Select(c => measure(c, null))
            .ToArray();

        var result = AllocateWithDrop(split, initialChildren, splitOuterWidth, requests);
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
            result = AllocateWithDrop(split, result.Children, splitOuterWidth, requests);
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
            _ => RowLayout.MinUsableWidth + (p.Border.Style is not null ? PaneBorderRenderer.BorderReserve : 0),
        };
    }

    // One run of the six-step allocation (§2.3), operating on whatever child list/request set the
    // caller currently has — a single pass, no fixpoint, no dropping.
    private static AllocResult AllocateOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, IReadOnlyList<int> requests)
    {
        var innerAvail = Math.Max(0, splitOuterWidth - (split.Border.Style is not null ? PaneBorderRenderer.BorderReserve : 0));
        var gutterTotal = split.Gutter * Math.Max(0, children.Count - 1);
        var avail = Math.Max(0, innerAvail - gutterTotal);

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
    private static AllocResult AllocateWithDrop(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, IReadOnlyList<int> requests)
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

            current = current.Take(current.Count - 1).ToList();
            currentRequests = currentRequests.Take(current.Count).ToList();
        }
    }

    // ---- intrinsic measurement: the same fits-or-degrade decision the renderer uses (LeafContent) ----

    private static int MeasureRequest(Pane pane, int? grantedOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        var borderReserve = pane.Border.Style is not null ? PaneBorderRenderer.BorderReserve : 0;
        var innerCap = grantedOuterWidth is int g ? Math.Max(0, g - borderReserve) : (int?)null;
        return MeasureInnerContentWidth(pane, innerCap, ctx, values) + borderReserve;
    }

    private static int MeasureInnerContentWidth(Pane pane, int? innerCap, ItemContext ctx, IReadOnlyDictionary<string, string?> values)
    {
        if (pane.Items.Count == 0)
        {
            var segments = SegmentBuilder.Build(ctx);
            return innerCap is int defaultCap ? LongestWrappedRowWidth(segments, defaultCap) : UnwrappedWidth(segments);
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

        return innerCap is int cap ? LongestWrappedRowWidth(packedGroup, cap) : UnwrappedWidth(packedGroup);
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
