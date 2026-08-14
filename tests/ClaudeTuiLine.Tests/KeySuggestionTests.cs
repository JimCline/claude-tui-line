namespace ClaudeTuiLine.Tests;

public class KeySuggestionTests
{
    [Fact]
    public void EditDistance_EmptyAgainstNonEmpty_IsFullLength()
    {
        Assert.Equal(3, KeySuggestion.EditDistance("", "abc"));
    }

    [Fact]
    public void EditDistance_IdenticalStrings_IsZero()
    {
        Assert.Equal(0, KeySuggestion.EditDistance("abc", "abc"));
    }

    [Fact]
    public void EditDistance_ColorColour_IsOne()
    {
        Assert.Equal(1, KeySuggestion.EditDistance("color", "colour"));
    }

    [Fact]
    public void EditDistance_ColorColro_IsTwo()
    {
        Assert.Equal(2, KeySuggestion.EditDistance("color", "colro"));
    }

    [Fact]
    public void EditDistance_CaseOnlyMismatch_IsOne()
    {
        Assert.Equal(1, KeySuggestion.EditDistance("Color", "color"));
    }

    [Fact]
    public void Suggest_NoCandidateWithinThreshold_ReturnsNull()
    {
        Assert.Null(KeySuggestion.Suggest("zzzzzz", new[] { "color", "format" }));
    }

    [Fact]
    public void Suggest_HalfLengthBound_TooShortUnknown_ReturnsNull()
    {
        Assert.Null(KeySuggestion.Suggest("ab", new[] { "color" }));
    }

    [Fact]
    public void Suggest_HalfLengthBound_ExactlyAtBoundary_ReturnsNull()
    {
        // distance 2 against a 4-char unknown: 2*2 < 4 is false, so this must not qualify.
        Assert.Null(KeySuggestion.Suggest("abcd", new[] { "abxy" }));
    }

    [Fact]
    public void Suggest_PrefixCandidateLongerThanUnknown_Qualifies()
    {
        Assert.Equal("ttlSeconds", KeySuggestion.Suggest("ttl", new[] { "ttlSeconds" }));
    }

    [Fact]
    public void Suggest_PrefixUnknownLongerThanCandidate_Qualifies()
    {
        Assert.Equal("colorSystem", KeySuggestion.Suggest("colorSystemX", new[] { "colorSystem" }));
    }

    [Fact]
    public void Suggest_TieBreak_FirstCandidateInOrderWins()
    {
        // TASK-21-SPEC.md §9.2 names this example as returning "ab", but by the §5 algorithm
        // EditDistance("aa","ab") == 1 and 1*2 < 2 is false (the half-length bound), and neither
        // "aa"/"ab" nor "aa"/"ac" is a prefix of the other — so neither candidate qualifies and the
        // correct result is null. Using a pair where both distance-1 candidates clear the bound
        // instead, to actually exercise the tie-break rule the test name describes.
        Assert.Equal("abce", KeySuggestion.Suggest("abcd", new[] { "abce", "abcf" }));
    }

    [Fact]
    public void Suggest_EmptyUnknown_ReturnsNull()
    {
        Assert.Null(KeySuggestion.Suggest("", new[] { "color" }));
    }
}
