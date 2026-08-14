namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.5.1 step 4: proves <see cref="ItemValueResolver.ScanReferencesViaExtractors"/>
/// (built from <see cref="ItemValueResolver.ReferenceExtractors"/>/<see cref="ItemValueResolver.ColorTokenExtractors"/>)
/// produces the same <see cref="ReferenceScan"/> as the existing hand-rolled
/// <see cref="ItemValueResolver.ScanReferences"/>, over a corpus harvested from
/// <see cref="ConfigCheckTests"/>'s real configs — required before step 5 deletes the hand-rolled
/// walk. <see cref="ReferenceScan.References"/> and the self-declared/derived id
/// sets are compared as sets rather than sequences: the two implementations visit the same reference
/// occurrences in different orders (per-entry in the old walk, per-form/bucket in the new one), which
/// is not itself a defect.
/// </summary>
public class ItemValueResolverEquivalenceTests
{
    private static (ReferenceScan Old, ReferenceScan New) Scan(UserConfig config)
    {
        var topLevel = ConfigLoader.ResolveTopLevel(config);
        var root = ConfigLoader.ResolveRootPane(config, topLevel);
        var rootPath = config.Surface?.Pane is not null ? "/surface/pane" : "";

        var oldScan = ItemValueResolver.ScanReferences(root, rootPath, topLevel.Colors);
        var newScan = ItemValueResolver.ScanReferencesViaExtractors(root, rootPath, topLevel.Colors);
        return (oldScan, newScan);
    }

    private static void AssertEquivalent(ReferenceScan oldScan, ReferenceScan newScan)
    {
        AssertSetEqual(oldScan.References, newScan.References);
        AssertSetEqual(oldScan.SelfDeclaredIds, newScan.SelfDeclaredIds);
        AssertSetEqual(oldScan.DerivedItemIds, newScan.DerivedItemIds);
        AssertSetEqual(oldScan.ColorTokenReferences, newScan.ColorTokenReferences);
        Assert.Equal(oldScan.ColorExprs.Select(t => t.Path), newScan.ColorExprs.Select(t => t.Path));
    }

    private static void AssertSetEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var expectedSet = expected.ToHashSet();
        var actualSet = actual.ToHashSet();
        Assert.True(
            expectedSet.SetEquals(actualSet),
            $"Expected: [{string.Join(", ", expectedSet)}]\nActual:   [{string.Join(", ", actualSet)}]");
    }

    // Every config below is either copied verbatim from ConfigCheckTests.cs (both its passing and
    // its diagnostic-asserting cases — §9.5.1's ruling covers both halves) or, where marked, hand-built
    // because no passing case in that file exercises the gap.

    [Fact]
    public void ValidMinimalConfig()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void UnknownItemSelector()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "not-a-real-builtin" } },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void DerivedFromUnknownSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "custom", From = "not-a-real-source" } },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void DerivedFromAnotherDerivedItem()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "b" },
                new() { Id = "b", From = "directory" },
            },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void LinkPlaceholderNamingNothing()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory", Link = "https://example.com/{missing}" } },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void ColorRuleFromNamingNothing()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig { From = "missing-source", Default = "green" },
                    },
                },
            },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void ColorTokenNamingNothing()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "@missing-token" } },
            },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    [Fact]
    public void ColorsTableEntryWithBadDefault()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["accent"] = new ColorRuleJsonConfig { Default = "not-a-real-color" },
            },
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    // Hand-built: ConfigCheckTests.cs's one Colors-table case sets only Default, never From, so it
    // never exercises a colors-table token's own from-reference (bucket 5 of ReferenceExtractors).
    [Fact]
    public void ColorsTableTokenFromNamingNothing()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["accent"] = new ColorRuleJsonConfig { From = "not-a-real-source", Default = "green" },
            },
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }

    // Hand-built: none of ConfigCheckTests.cs's passing cases combine every reference form into one
    // config, and none exercise a valid (non-dangling) colors-table from or @name token reference.
    [Fact]
    public void EveryReferenceFormResolvesValidly()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["accent"] = new ColorRuleJsonConfig { From = "directory", Default = "green" },
            },
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "custom", From = "directory" },
                new() { Item = "custom", Link = "https://example.com/{custom}" },
                new()
                {
                    Item = "directory",
                    Color = new ColorExprJsonConfig
                    {
                        Rule = new ColorRuleJsonConfig { From = "directory", Default = "green" },
                    },
                },
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "@accent" } },
            },
        };

        var (oldScan, newScan) = Scan(config);
        AssertEquivalent(oldScan, newScan);
    }
}
