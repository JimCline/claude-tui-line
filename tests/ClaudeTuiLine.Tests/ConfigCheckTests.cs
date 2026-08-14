namespace ClaudeTuiLine.Tests;

public class ConfigCheckTests
{
    [Fact]
    public void ValidMinimalConfig_ProducesNoDiagnostics()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownItemSelector_ReportsUnknownItemId()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "not-a-real-builtin" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-item-id" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DerivedFromUnknownSource_ReportsUnknownItemId()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "custom", From = "not-a-real-source" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-item-id" && d.Path == "/items/0/from" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DerivedFromAnotherDerivedItem_ReportsFromDerivedSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "b" },
                new() { Id = "b", From = "directory" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "from-derived-source" && d.Path == "/items/0/from" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void LinkPlaceholderNamingNothing_ReportsUnknownLinkTargetAsWarning()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory", Link = "https://example.com/{missing}" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-link-target" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ColorRuleFromNamingNothing_ReportsUnknownColorSourceAsWarning()
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

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color-source" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ColorTokenNamingNothing_ReportsUnknownColorTokenAsWarning()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "@missing-token" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color-token" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void UnrecognizedLiteralColor_ReportsUnknownColorAsError()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "not-a-real-color" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedThresholdColor_ReportsUnknownColorAtThresholdPath()
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
                        Rule = new ColorRuleJsonConfig
                        {
                            Thresholds = new List<ThresholdJsonConfig> { new() { Min = 80, Color = "not-a-real-color" } },
                            Default = "green",
                        },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/items/0/color/thresholds/0/color" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedMatchColor_ReportsUnknownColorAtMatchPath()
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
                        Rule = new ColorRuleJsonConfig
                        {
                            Match = new List<MatchJsonConfig> { new() { EqualsValue = "x", Color = "not-a-real-color" } },
                            Default = "green",
                        },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/items/0/color/match/0/color" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ColorsTableEntryWithBadDefault_ReportsUnknownColorAtColorsPath()
    {
        var config = new UserConfig
        {
            Colors = new Dictionary<string, ColorRuleJsonConfig>
            {
                ["accent"] = new ColorRuleJsonConfig { Default = "not-a-real-color" },
            },
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-color" && d.Path == "/colors/accent/default" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HexLiteralUnderDefaultColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "#ff8800" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void HexLiteralUnderTruecolorColorSystem_ProducesNoColorDownConvertedDiagnostic()
    {
        var config = new UserConfig
        {
            ColorSystem = "truecolor",
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "#ff8800" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void NamedColorLiteralUnderDefaultColorSystem_ProducesNoColorDownConvertedDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "green" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void HexLiteralUnderEightBitColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            ColorSystem = "256",
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "#ff8800" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void NumericLiteralAboveFifteenUnderDefaultColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "207" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" &&
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("256-color"));
    }

    [Fact]
    public void NumericLiteralAtOrBelowFifteenUnderDefaultColorSystem_ProducesNoColorDownConvertedDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "9" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void NonStandardNamedColorUnderDefaultColorSystem_ReportsColorDownConverted()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "cyan" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "color-down-converted" && d.Path == "/items/0/color" &&
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("256-color"));
    }

    [Fact]
    public void DecorationOnlyLiteral_ProducesNoColorDownConvertedDiagnosticUnderAnyColorSystem()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Item = "directory", Color = new ColorExprJsonConfig { Literal = "dim" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "color-down-converted");
    }

    [Fact]
    public void SingleElementArgvWithWhitespaceWithoutShell_ReportsCommandShape()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "git status" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "command-shape" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ShellTrueWithMultiElementArgv_ReportsCommandShellArgv()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Shell = true, Command = new List<string> { "echo", "hi" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "command-shell-argv" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ShellTrueWithSingleElementArgv_ProducesNoCommandDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Shell = true, Command = new List<string> { "git status" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code is "command-shape" or "command-shell-argv");
    }

    [Fact]
    public void MultiElementArgvWithoutShell_ProducesNoCommandDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "git", "status" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code is "command-shape" or "command-shell-argv");
    }

    [Fact]
    public void UnrecognizedPaneSize_ReportsUnknownEnumValue()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Size = "huge",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/size" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PaneSizeAuto_ReportsDeprecatedSizeAlias()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Size = "auto",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "deprecated-size-alias" && d.Path == "/surface/pane/size" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void PaneSizeFill_ProducesNoDeprecatedSizeAliasDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Size = "fill",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "deprecated-size-alias");
    }

    [Fact]
    public void UnrecognizedItemCase_ReportsUnknownEnumValue()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", From = "directory", Case = "sideways" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/items/0/case" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedTopLevelBorderStyle_ReportsUnknownEnumValue()
    {
        var config = new UserConfig
        {
            Border = new BorderConfig { Style = "sparkly" },
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/border/style" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrecognizedValign_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Valign = "upside-down",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/valign" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'upside-down' is not a valign — expected top, middle, or bottom");
    }

    [Fact]
    public void UnrecognizedAlign_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Align = "inward",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/align" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'inward' is not a align — expected left, center, or right");
    }

    [Fact]
    public void UnrecognizedSplit_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "diagonal",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'diagonal' is not a split — expected none, horizontal, or vertical");
    }

    [Fact]
    public void ExplicitSplitNone_ProducesNoUnknownEnumValueDiagnosticForSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "none",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/split");
    }

    [Fact]
    public void UnrecognizedDistribute_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Distribute = "min-row",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/distribute" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'min-row' is not a distribute — expected greedy, min-rows, or even");
    }

    [Fact]
    public void ExplicitDistributeGreedy_ProducesNoUnknownEnumValueDiagnosticForDistribute()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Distribute = "greedy",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/distribute");
    }

    [Fact]
    public void UnrecognizedHeight_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>
                    {
                        new() { Height = "shrink", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/children/0/height" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'shrink' is not a height — expected content or fill");
    }

    [Fact]
    public void ExplicitHeightContent_ProducesNoUnknownEnumValueDiagnosticForHeight()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>
                    {
                        new() { Height = "content", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/surface/pane/children/0/height");
    }

    [Fact]
    public void UnrecognizedColorSystem_ReportsUnknownEnumValueNamingAcceptedSet()
    {
        var config = new UserConfig
        {
            ColorSystem = "24bit",
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/colorSystem" &&
            d.Severity == DiagnosticSeverity.Error && d.Message == "'24bit' is not a colorSystem — expected standard, 256, or truecolor");
    }

    [Fact]
    public void ExplicitColorSystemStandard_ProducesNoUnknownEnumValueDiagnosticForColorSystem()
    {
        var config = new UserConfig
        {
            ColorSystem = "standard",
            Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-enum-value" && d.Path == "/colorSystem");
    }

    [Fact]
    public void MinSizeExceedsMaxSize_ReportsMinExceedsMax()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    MinSize = 50,
                    MaxSize = 20,
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "min-exceeds-max" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FixedChildrenExceedFixedParent_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "20",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "15", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "15", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "fixed-sizes-exceed-parent" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MinSizeSumExceedsParentMaxSize_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    MaxSize = 20,
                    Children = new List<PaneConfig>
                    {
                        new() { MinSize = 15, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { MinSize = 15, Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "fixed-sizes-exceed-parent" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FixedChildrenWithinFixedParent_ProducesNoStructuralDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Size = "50",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "10", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "10", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "fixed-sizes-exceed-parent");
    }

    [Fact]
    public void OverflowOverflowInsideSplit_ReportsOverflowForbiddenPosition()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>
                    {
                        new() { Overflow = "overflow", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "overflow-forbidden-position" && d.Path == "/surface/pane/children/0/overflow" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OverflowOverflowOnSoleRootPane_ProducesNoOverflowDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Overflow = "overflow",
                    Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "overflow-forbidden-position");
    }

    [Fact]
    public void ContentSizedPaneWithNoItems_ReportsPaneNoItemsAsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "content" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "pane-no-items" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void FillSizedPaneWithNoItems_ReportsPaneNoItemsAsWarning()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "fill" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "pane-no-items" && d.Path == "/surface/pane" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void FixedSizedPaneWithNoItems_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "10" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void PercentSizedPaneWithNoItems_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "50%" },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void SplitPaneWithNoOwnItems_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void ContentSizedPaneWithExplicitMinSize_ProducesNoPaneNoItemsDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig { Size = "content", MinSize = 5 },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "pane-no-items");
    }

    [Fact]
    public void OverflowOnSplitNode_ReportsLeafOnlyKeyOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Overflow = "truncate",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "leaf-only-key-on-split" && d.Path == "/surface/pane/overflow" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void EllipsisOnSplitNode_ReportsLeafOnlyKeyOnSplit()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Ellipsis = "...",
                    Children = new List<PaneConfig>
                    {
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "leaf-only-key-on-split" && d.Path == "/surface/pane/ellipsis" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void OverflowOnLeafChildInsideSplit_ProducesNoLeafOnlyKeyDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "vertical",
                    Children = new List<PaneConfig>
                    {
                        new() { Overflow = "truncate", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "leaf-only-key-on-split");
    }

    [Fact]
    public void SingleHorizontalSplitChildExceedsFixedParent_ReportsFixedSizesExceedParent()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Size = "10",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "20", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "fixed-sizes-exceed-parent" && d.Path == "/surface/pane/children/0" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HorizontalSplitChildrenWithinParentBound_ProducesNoStructuralDiagnostic()
    {
        var config = new UserConfig
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = "horizontal",
                    Size = "10",
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "5", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                        new() { Size = "5", Items = new List<PaneItemJsonConfig> { new() { Item = "directory" } } },
                    },
                },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "fixed-sizes-exceed-parent");
    }

    // ---- SPEC-V2-FRAMEWORK.md §4.2.1: argv-placeholder declaration faults ----

    [Fact]
    public void ArgvPlaceholderNamingUnknownId_ReportsUnknownItemId()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{not-a-real-id}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-item-id" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderNamingDerivedItem_ReportsPlaceholderDerivedSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "a", From = "directory" },
                new() { Id = "cmd", Command = new List<string> { "tool", "{a}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-derived-source" && d.Path == "/items/1/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderNamingAnotherCommandItem_ReportsPlaceholderCommandSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "other-cmd", Command = new List<string> { "echo", "hi" } },
                new() { Id = "cmd", Command = new List<string> { "tool", "{other-cmd}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-command-source" && d.Path == "/items/1/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderBareSelfReference_ReportsPlaceholderSelfReference()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-self-reference" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ArgvPlaceholderNamedSelfReference_ReportsPlaceholderSelfReferenceNotCommandSource()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{cmd}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-self-reference" && d.Path == "/items/0/command" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == "placeholder-command-source");
    }

    [Fact]
    public void ShellTrueWithTwoIdsManglingToSameEnvVar_ReportsPlaceholderEnvCollision()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "agent-short", From = "directory" },
                new() { Id = "agent.short", From = "directory" },
                new() { Id = "cmd", Shell = true, Command = new List<string> { "echo \"$CLAUDE_TUI_LINE_VAL_AGENT_SHORT {agent-short} {agent.short}\"" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "placeholder-env-collision" && d.Path == "/items/2/command" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NonShellCommandWithTwoIdsManglingToSameEnvVar_ProducesNoEnvCollisionDiagnostic()
    {
        // §4.2: the env-collision check is scoped to shell:true — non-shell substitutes directly
        // into argv and has no shared environment namespace for two ids to collide in.
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig>
            {
                new() { Id = "agent-short", From = "directory" },
                new() { Id = "agent.short", From = "directory" },
                new() { Id = "cmd", Command = new List<string> { "tool", "{agent-short}", "{agent.short}" } },
            },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "placeholder-env-collision");
    }

    [Fact]
    public void ArgvPlaceholderNamingKnownBuiltin_ProducesNoArgvPlaceholderDiagnostic()
    {
        var config = new UserConfig
        {
            Items = new List<PaneItemJsonConfig> { new() { Id = "cmd", Command = new List<string> { "tool", "{directory}" } } },
        };

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code is "unknown-item-id" or "placeholder-derived-source" or
            "placeholder-command-source" or "placeholder-self-reference" or "placeholder-env-collision");
    }

    // ---- §9.4.2: unknown-key diagnostic. Fixtures are parsed JSON, not object initializers —
    // extension data only exists on the deserialized shape. ----

    private static UserConfig Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.UserConfig)!;

    [Fact]
    public void UnknownKeyOnItem_ReportsWithSuggestion()
    {
        var config = Parse("""{"items":[{"item":"context","colour":"aqua"}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Severity == DiagnosticSeverity.Warning &&
            d.Path == "/items/0/colour" && d.Message == "unknown key 'colour' on an item — did you mean 'color'?");
    }

    [Fact]
    public void UnknownKeyOnItem_AbbreviationPrefixRule_ReportsWithSuggestion()
    {
        var config = Parse("""{"items":[{"ttl":5}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/ttl" &&
            d.Message == "unknown key 'ttl' on an item — did you mean 'ttlSeconds'?");
    }

    [Fact]
    public void UnknownKeyOnItem_NoQualifyingSuggestion_OmitsDidYouMean()
    {
        var config = Parse("""{"items":[{"zzzzzz":1}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/zzzzzz" &&
            d.Message == "unknown key 'zzzzzz' on an item");
    }

    [Fact]
    public void UnknownKeyOnItem_CaseOnlyMismatch_IsReportedAndSuggested()
    {
        var config = Parse("""{"items":[{"Color":"red"}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/Color" &&
            d.Message == "unknown key 'Color' on an item — did you mean 'color'?");
    }

    [Fact]
    public void UnknownKeyAtTopLevel_ReportsWithSuggestion()
    {
        var config = Parse("""{"colour":"red"}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/colour" &&
            d.Message == "unknown key 'colour' on the top-level config — did you mean 'colors'?");
    }

    [Fact]
    public void UnknownKeyOnNestedPane_Reports()
    {
        // §9.4.2: "maxLines" is a vocabulary mismatch ("Lines" vs "Rows"), not a typo or an
        // abbreviation — EditDistance("maxLines", "maxRows") == 4 clears neither the distance
        // bound (<=2) nor the prefix rule, and that is intended: no rule here addresses the
        // vocabulary-confusion class, so the bare diagnostic naming no key is correct.
        var config = Parse("""{"surface":{"pane":{"split":"vertical","children":[{"maxLines":3}]}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" &&
            d.Path == "/surface/pane/children/0/maxLines" &&
            d.Message == "unknown key 'maxLines' on a pane");
    }

    [Fact]
    public void UnknownKeyOnPane_GenuineTie_NamesBothCandidatesOrdinallySorted()
    {
        // "xalign" is distance 1 from both "align" and "valign" (neither is a prefix relation
        // of the other), so this is a real tie over PaneConfig's actual known-key set — not a
        // synthetic case — and both candidates must be named, ordinally sorted.
        var config = Parse("""{"surface":{"pane":{"split":"vertical","xalign":"x"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" &&
            d.Path == "/surface/pane/xalign" &&
            d.Message == "unknown key 'xalign' on a pane — did you mean 'align' or 'valign'?");
    }

    [Fact]
    public void UnknownKeyOnItem_ShortKeyBelowPrefixFloor_OmitsDidYouMean()
    {
        // §9.4.2 D3: "c" is a bare prefix of several known keys (case, color, colors,
        // colorSystem, children) but the shorter string is below the three-character floor,
        // so none of them qualify.
        var config = Parse("""{"items":[{"c":1}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/c" &&
            d.Message == "unknown key 'c' on an item");
    }

    [Fact]
    public void UnknownKeyOnBorderObject_ReportsWithSuggestion()
    {
        var config = Parse("""{"border":{"styl":"rounded"}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/border/styl" &&
            d.Message == "unknown key 'styl' on a border — did you mean 'style'?");
    }

    [Fact]
    public void BorderShorthandString_ProducesNoUnknownKeyDiagnostics()
    {
        var config = Parse("""{"border":"outline"}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key");
    }

    [Fact]
    public void UnknownKeyOnColorRuleInsideItem_ReportsWithSuggestion()
    {
        var config = Parse("""{"items":[{"item":"x","color":{"from":"y","defualt":"red"}}]}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.Contains(diagnostics, d => d.Code == "unknown-key" && d.Path == "/items/0/color/defualt" &&
            d.Message == "unknown key 'defualt' on a color rule — did you mean 'default'?");
    }

    [Fact]
    public void ColorsTableTokenNames_AreNeverUnknownKeys()
    {
        var config = Parse("""{"colors":{"my-weird-name":{"from":"x","default":"red"}}}""");

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key");
    }

    [Fact]
    public void CleanConfig_ProducesNoUnknownKeyDiagnostic()
    {
        var config = Parse("""
        {
            "border": {"enabled": true, "style": "rounded"},
            "layout": {"chromeReserve": 3},
            "surface": {
                "maxRows": 8,
                "pane": {
                    "split": "vertical",
                    "children": [
                        {"size": "fill", "items": [{"item": "directory"}]},
                        {"size": "fill", "items": [{"item": "model"}]}
                    ]
                }
            },
            "colors": {"accent": {"from": "directory", "default": "blue"}}
        }
        """);

        var diagnostics = ConfigChecker.Check(config);

        Assert.DoesNotContain(diagnostics, d => d.Code == "unknown-key");
    }

    [Fact]
    public void MultipleUnknownKeysOnOneObject_ComeOutInOrdinalKeyOrder()
    {
        var config = Parse("""{"items":[{"zoo":1,"alpha":2}]}""");

        var diagnostics = ConfigChecker.Check(config)
            .Where(d => d.Code == "unknown-key")
            .ToList();

        var alphaIndex = diagnostics.FindIndex(d => d.Path == "/items/0/alpha");
        var zooIndex = diagnostics.FindIndex(d => d.Path == "/items/0/zoo");

        Assert.True(alphaIndex >= 0 && zooIndex >= 0 && alphaIndex < zooIndex);
    }

    [Fact]
    public void UnknownKeyDiagnostics_AreNeverErrorSeverity()
    {
        var config = Parse("""
        {
            "colour": "red",
            "items": [{"item":"x","colour":"aqua","ttl":5,"zzzzzz":1,"Color":"red","color":{"from":"y","defualt":"red"}}],
            "border": {"styl":"rounded"},
            "surface": {"pane": {"split":"vertical","children":[{"maxLines":3}]}}
        }
        """);

        var unknownKeyDiagnostics = ConfigChecker.Check(config).Where(d => d.Code == "unknown-key").ToList();

        Assert.NotEmpty(unknownKeyDiagnostics);
        Assert.All(unknownKeyDiagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void BorderConfig_KnownKeySet_ExcludesShorthand()
    {
        // TASK-21-SPEC.md §9.3: the executable form of §3's NEEDS-EVIDENCE item. On this runtime,
        // [JsonIgnore] does not remove Shorthand from .Properties (it stays present, IsExtensionData
        // == false) — confirmed by the raw-properties assertion below — so ConfigCheck.cs's
        // KnownKeys() also filters it out by name; the second assertion is that effective set,
        // which is what actually reaches the unknown-key/suggestion diagnostic.
        var rawNames = ConfigJsonContext.Default.BorderConfig.Properties.Select(p => p.Name).ToList();
        Assert.Contains("Shorthand", rawNames);

        var knownNames = ConfigJsonContext.Default.BorderConfig.Properties
            .Where(p => !p.IsExtensionData && p.Name != "Shorthand")
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(
            new[] { "enabled", "color", "style", "edges", "collapse" }.OrderBy(n => n, StringComparer.Ordinal),
            knownNames.OrderBy(n => n, StringComparer.Ordinal));
    }
}
