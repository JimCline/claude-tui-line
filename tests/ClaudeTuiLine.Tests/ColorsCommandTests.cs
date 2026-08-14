using Spectre.Console;

namespace ClaudeTuiLine.Tests;

public class ColorsCommandTests
{
    [Fact]
    public void Build_RecommendsTheSixteenThemeColorsAsThemeMappedPlusThreeDecorationKeywords()
    {
        var result = ColorsCommand.Build();

        var themeMapped = result.Recommended.Where(c => c.ThemeMapped).Select(c => c.Name).ToList();
        var notThemeMapped = result.Recommended.Where(c => !c.ThemeMapped).Select(c => c.Name).ToList();

        Assert.Equal(19, result.Recommended.Count);
        Assert.Equal(ColorResolution.StandardColorNames.Count, themeMapped.Count);
        Assert.All(themeMapped, name => Assert.Contains(name, ColorResolution.StandardColorNames));
        Assert.Equal(new[] { "default", "dim", "bold" }, notThemeMapped);
    }

    [Fact]
    public void Build_TheSixteenThemeColorsRoundTripToAForegroundOtherThanColorDefault()
    {
        var result = ColorsCommand.Build();

        foreach (var entry in result.Recommended.Where(c => c.ThemeMapped))
        {
            var resolved = ColorResolution.ResolveLiteral(entry.Name);
            Assert.NotNull(resolved);
            Assert.NotEqual(Color.Default, resolved!.Value);
        }
    }

    // §9.6.3.1: "default"/"dim"/"bold" parse successfully but aren't colors, so a bare non-null
    // check would pass for a real color that got misclassified. Pinning the exact set that
    // resolves to Color.Default is what would catch that.
    [Fact]
    public void Build_ExactlyDefaultDimAndBoldRoundTripToColorDefault()
    {
        var result = ColorsCommand.Build();

        var resolvedToDefault = result.Recommended
            .Where(c => ColorResolution.ResolveLiteral(c.Name) == Color.Default)
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "bold", "default", "dim" }, resolvedToDefault);
    }

    [Fact]
    public void Build_CarriesTheSameVersionAsItemsCommand()
    {
        Assert.Equal(AssemblyVersionInfo.InformationalVersion, ColorsCommand.Build().Version);
    }

    [Fact]
    public void Build_AlsoAcceptedExplainsTheWiderPaletteAndColorSystemFallback()
    {
        var result = ColorsCommand.Build();

        Assert.False(string.IsNullOrWhiteSpace(result.AlsoAccepted));
        Assert.Contains("#rrggbb", result.AlsoAccepted);
        Assert.Contains("colorSystem", result.AlsoAccepted);
    }

    [Fact]
    public void RenderMarkupLines_TagsEachNameWithItselfAsTheMarkupStyle()
    {
        var result = ColorsCommand.Build();

        var lines = ColorsCommand.RenderMarkupLines(result);

        Assert.Equal(result.Recommended.Select(c => $"[{c.Name}]{c.Name}[/]"), lines);
    }

    [Fact]
    public void RenderMarkupLines_EveryLineParsesAsValidSpectreMarkup()
    {
        var lines = ColorsCommand.RenderMarkupLines(ColorsCommand.Build());

        Assert.All(lines, line => Markup.Remove(line));
    }

    [Fact]
    public void Build_SerializesToJsonWithTheSpecifiedPropertyNames()
    {
        var result = ColorsCommand.Build();
        var json = System.Text.Json.JsonSerializer.Serialize(result, ColorsJsonContext.Default.ColorsResultJson);

        Assert.Contains("\"version\":", json);
        Assert.Contains("\"recommended\":", json);
        Assert.Contains("\"name\":", json);
        Assert.Contains("\"themeMapped\":", json);
        Assert.Contains("\"alsoAccepted\":", json);
    }
}
