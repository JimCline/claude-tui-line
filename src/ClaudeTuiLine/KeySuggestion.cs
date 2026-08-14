namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.4.2: picks the nearest known key to suggest for an unknown one.
/// A confidently wrong suggestion is worse than none, so a candidate must clear one of two
/// bars: a small bounded edit distance, or a prefix relation (the abbreviation class —
/// "ttl" for "ttlSeconds" — which no distance bound small enough to be safe would reach).
/// </summary>
internal static class KeySuggestion
{
    /// <summary>
    /// The nearest qualifying candidate, or null when none qualifies. Comparison is
    /// case-sensitive (Ordinal), matching the binder's PropertyNameCaseInsensitive = false.
    /// Ties are broken by <paramref name="candidates"/> order.
    /// </summary>
    public static string? Suggest(string unknown, IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrEmpty(unknown))
        {
            return null;
        }

        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0)
            {
                continue;
            }

            var distance = EditDistance(unknown, candidate);
            var qualifies = (distance <= 2 && distance * 2 < unknown.Length)
                || unknown.StartsWith(candidate, StringComparison.Ordinal)
                || candidate.StartsWith(unknown, StringComparison.Ordinal);

            if (qualifies && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Levenshtein distance, case-sensitive, two-row DP.</summary>
    public static int EditDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
