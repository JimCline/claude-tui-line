using System.Text.Json.Serialization;
using Spectre.Console;

namespace ClaudeTuiLine;

/// <summary>SPEC-V2-FRAMEWORK.md §9.4/§9.6: a config problem's severity — see §9.6.1's code registry.</summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.6: one entry of <c>--check</c>'s <c>diagnostics</c> array. <see cref="Code"/>
/// is a permanent surface (§9.6.1's registry) — never invented here ad hoc.
/// </summary>
public sealed record Diagnostic(string Path, DiagnosticSeverity Severity, string Code, string Message);

/// <summary>SPEC-V2-FRAMEWORK.md §9.6: the JSON projection of one <see cref="Diagnostic"/>.</summary>
public sealed record DiagnosticJson(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>SPEC-V2-FRAMEWORK.md §9.6: <c>--check --json</c>'s success/checked shape.</summary>
public sealed record CheckResultJson(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<DiagnosticJson> Diagnostics);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.6: the failure envelope for exit 2 (<c>"usage"</c>) and exit 3
/// (<c>"config-unreadable"</c>) — a distinct shape from <see cref="CheckResultJson"/> so that
/// <c>diagnostics</c> is structurally absent rather than present as an empty array.
/// </summary>
public sealed record CheckFailureJson(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path,
    [property: JsonPropertyName("message")] string Message);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CheckResultJson))]
[JsonSerializable(typeof(CheckFailureJson))]
public partial class CheckJsonContext : JsonSerializerContext
{
}

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.4: <c>--check</c>'s config diagnostics. §9.5 requires every id diagnostic
/// to come from <see cref="ItemValueResolver.ScanReferences"/> rather than a second walk; §9.8
/// requires the structural size diagnostics to call <see cref="SizeResolver"/>'s own boundary-cost
/// arithmetic rather than transcribe it. This never touches a width — no diagnostic here may depend
/// on <c>COLUMNS</c> or resolved sizes (§9.8).
/// </summary>
public static class ConfigChecker
{
    public static IReadOnlyList<Diagnostic> Check(UserConfig? config)
    {
        var topLevel = ConfigLoader.ResolveTopLevel(config);
        var root = ConfigLoader.ResolveRootPane(config, topLevel);
        var rootPath = config?.Surface?.Pane is not null ? "/surface/pane" : "";
        var scan = ItemValueResolver.ScanReferences(root, rootPath, topLevel.Colors);

        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(CheckReferences(scan, topLevel.Colors));
        diagnostics.AddRange(CheckColorLiterals(scan, topLevel.Colors, topLevel.ColorSystem));
        diagnostics.AddRange(CheckCommandShape(root, rootPath));
        diagnostics.AddRange(CheckEnums(config));
        diagnostics.AddRange(CheckLeafOnlyKeysOnSplits(config));
        diagnostics.AddRange(CheckStructuralSizes(root, rootPath));
        diagnostics.AddRange(CheckOverflowPosition(root, rootPath));
        diagnostics.AddRange(CheckEmptyPanes(root, rootPath));
        return diagnostics;
    }

    // ---- §9.4.1/§9.5: id and colour-token references, via ItemValueResolver.ScanReferences ----

    private static IEnumerable<Diagnostic> CheckReferences(ReferenceScan scan, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        foreach (var reference in scan.References)
        {
            var known = scan.SelfDeclaredIds.Contains(reference.Id) || ItemRegistry.Find(reference.Id) is not null;
            if (!known)
            {
                var (severity, code) = reference.Form switch
                {
                    ReferenceForm.ItemSelector => (DiagnosticSeverity.Error, "unknown-item-id"),
                    ReferenceForm.DerivedFrom => (DiagnosticSeverity.Error, "unknown-item-id"),
                    ReferenceForm.LinkPlaceholder => (DiagnosticSeverity.Warning, "unknown-link-target"),
                    ReferenceForm.ColorFrom => (DiagnosticSeverity.Warning, "unknown-color-source"),
                    _ => (DiagnosticSeverity.Error, "unknown-item-id"),
                };
                yield return new Diagnostic(reference.Path, severity, code, $"no item named '{reference.Id}'");
            }
            else if (reference.Form == ReferenceForm.DerivedFrom && scan.DerivedItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "from-derived-source",
                    $"'{reference.Id}' is itself a derived item; a derived item cannot source from another derived item");
            }
        }

        foreach (var tokenRef in scan.ColorTokenReferences)
        {
            if (!tokens.ContainsKey(tokenRef.Name))
            {
                yield return new Diagnostic(tokenRef.Path, DiagnosticSeverity.Warning, "unknown-color-token",
                    $"no color named '{tokenRef.Name}' in the colors table");
            }
        }
    }

    // ---- §9.4.1/§6/§6.2: literal colour values, reusing the ColorExprs the same walk already collected ----

    private static IEnumerable<Diagnostic> CheckColorLiterals(ReferenceScan scan, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens, ColorSystemSupport colorSystem)
    {
        foreach (var (expr, path) in scan.ColorExprs)
        {
            foreach (var d in CheckColorExprLiterals(expr, path, colorSystem))
            {
                yield return d;
            }
        }

        foreach (var (name, rule) in tokens)
        {
            foreach (var d in CheckColorRuleLiterals(rule, $"/colors/{name}", colorSystem))
            {
                yield return d;
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckColorExprLiterals(ColorResolution.ColorExpr expr, string path, ColorSystemSupport colorSystem)
    {
        switch (expr)
        {
            case ColorResolution.ColorExpr.Literal lit:
                foreach (var d in CheckLiteralSpec(lit.Spec, path, colorSystem))
                {
                    yield return d;
                }

                break;
            case ColorResolution.ColorExpr.Inline inline:
                foreach (var d in CheckColorRuleLiterals(inline.Rule, path, colorSystem))
                {
                    yield return d;
                }

                break;
        }
    }

    private static IEnumerable<Diagnostic> CheckColorRuleLiterals(ColorResolution.ColorRule rule, string path, ColorSystemSupport colorSystem)
    {
        if (rule.Default is { Length: > 0 } def)
        {
            foreach (var d in CheckLiteralSpec(def, path + "/default", colorSystem))
            {
                yield return d;
            }
        }

        if (rule.Thresholds is { } thresholds)
        {
            for (var i = 0; i < thresholds.Count; i++)
            {
                foreach (var d in CheckLiteralSpec(thresholds[i].Color, $"{path}/thresholds/{i}/color", colorSystem))
                {
                    yield return d;
                }
            }
        }

        if (rule.Match is { } match)
        {
            for (var i = 0; i < match.Count; i++)
            {
                foreach (var d in CheckLiteralSpec(match[i].Color, $"{path}/match/{i}/color", colorSystem))
                {
                    yield return d;
                }
            }
        }
    }

    // §6.2.1: every literal form names a minimum color system it needs — hex is truecolor; a bare
    // palette index ≤15 is standard, ≥16 is 256; a name is standard only if it's one of the sixteen
    // ANSI standard names (STATUS.md), else 256, since anything outside that closed set is only
    // "verified to parse", not verified to be part of the safe standard-16 palette. A spec that
    // resolves to Color.Default (e.g. "default"/"dim"/"bold" — decoration, not a palette index) has
    // no palette dependency and so never needs more than standard.
    private static IEnumerable<Diagnostic> CheckLiteralSpec(string spec, string path, ColorSystemSupport colorSystem)
    {
        var resolved = ColorResolution.ResolveLiteral(spec);
        if (resolved is null)
        {
            yield return UnknownColor(path, spec);
            yield break;
        }

        var minimum = MinimumColorSystem(spec.Trim(), resolved.Value);
        if (ColorSystemRank(colorSystem) < ColorSystemRank(minimum))
        {
            yield return new Diagnostic(path, DiagnosticSeverity.Warning, "color-down-converted",
                $"'{spec}' is a {LiteralTierLabel(minimum)} literal; this terminal's color system ({PaletteLabel(colorSystem)}) will approximate it to the nearest supported color");
        }
    }

    private static ColorSystemSupport MinimumColorSystem(string trimmed, Color resolved)
    {
        if (resolved == Color.Default)
        {
            return ColorSystemSupport.Standard;
        }

        if (trimmed.StartsWith('#'))
        {
            return ColorSystemSupport.TrueColor;
        }

        if (int.TryParse(trimmed, out var index))
        {
            return index <= 15 ? ColorSystemSupport.Standard : ColorSystemSupport.EightBit;
        }

        return ColorResolution.StandardColorNames.Contains(trimmed) ? ColorSystemSupport.Standard : ColorSystemSupport.EightBit;
    }

    // Spectre's own ColorSystemSupport ordinal order is an implementation detail of a library we
    // don't control; this ranking is ours, so a comparison here can't silently break under an
    // upstream reorder.
    private static int ColorSystemRank(ColorSystemSupport system) => system switch
    {
        ColorSystemSupport.Standard => 0,
        ColorSystemSupport.EightBit => 1,
        ColorSystemSupport.TrueColor => 2,
        _ => 0,
    };

    private static string LiteralTierLabel(ColorSystemSupport minimum) => minimum switch
    {
        ColorSystemSupport.TrueColor => "truecolor",
        ColorSystemSupport.EightBit => "256-color",
        _ => "standard",
    };

    private static string PaletteLabel(ColorSystemSupport system) => system switch
    {
        ColorSystemSupport.Standard => "16 standard colors",
        ColorSystemSupport.EightBit => "256-color palette",
        ColorSystemSupport.TrueColor => "truecolor",
        _ => system.ToString(),
    };

    private static Diagnostic UnknownColor(string path, string spec) =>
        new(path, DiagnosticSeverity.Error, "unknown-color", $"'{spec}' is not a recognized color");

    // ---- §4.1: command shape / shell argv, via a plain item walk (not an id reference) ----

    private static IEnumerable<Diagnostic> CheckCommandShape(Pane root, string rootPath)
    {
        foreach (var (item, path) in ItemValueResolver.WalkItems(root, rootPath))
        {
            if (item.Command is not { Count: > 0 } command)
            {
                continue;
            }

            if (item.Shell)
            {
                if (command.Count > 1)
                {
                    yield return new Diagnostic(path + "/command", DiagnosticSeverity.Error, "command-shell-argv",
                        "shell:true only runs the first argv element; the remaining elements would be silently dropped");
                }
            }
            else if (command.Count == 1 && command[0].Any(char.IsWhiteSpace))
            {
                yield return new Diagnostic(path + "/command", DiagnosticSeverity.Error, "command-shape",
                    $"'{command[0]}' contains whitespace but shell is not true; it will run as a single binary name, not split into arguments");
            }
        }
    }

    // ---- §9.4.1: unknown enum values — any key with a closed value set, one shared code across all of them ----

    // §1.1.1: every enumerable kind's accepted-token list now lives with its parser (see e.g.
    // BorderStyleParsing.AcceptedTokens) so the diagnostic reads the same object the parser looks
    // up, instead of a second hand-copied list. `size` has no closed token set — a mix of literals
    // and form descriptions — so it stays a plain array here, per §1.1.1.
    private static readonly string[] SizeValues = { "an integer", "a percentage", "content", "fill", "auto" };

    private static IEnumerable<Diagnostic> CheckEnums(UserConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(config?.Border?.Style) && !BorderStyleParsing.TryParse(config.Border.Style!, out _))
        {
            yield return UnknownEnumValue("/border/style", config.Border.Style, "style", BorderStyleParsing.AcceptedTokens);
        }

        if (ConfigLoader.IsUnrecognizedColorSystem(config?.ColorSystem))
        {
            yield return UnknownEnumValue("/colorSystem", config?.ColorSystem, "colorSystem", ConfigLoader.ColorSystemAcceptedTokens);
        }

        if (config?.Surface?.Pane is { } surfacePane)
        {
            foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
            {
                foreach (var d in CheckPaneEnums(pane, path))
                {
                    yield return d;
                }
            }
        }
        else if (config?.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var d in CheckItemEnums(items[i], $"/items/{i}"))
                {
                    yield return d;
                }
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckPaneEnums(PaneConfig pane, string path)
    {
        if (SizeResolver.IsUnrecognizedSize(pane.Size))
        {
            yield return UnknownEnumValue(path + "/size", pane.Size, "size", SizeValues);
        }

        if (SizeResolver.IsDeprecatedSizeAlias(pane.Size))
        {
            yield return new Diagnostic(path + "/size", DiagnosticSeverity.Warning, "deprecated-size-alias",
                "\"auto\" resolves to \"fill\"; \"content\" is the separate value that sizes a pane to its content");
        }

        if (PaneValignParsing.IsUnrecognized(pane.Valign))
        {
            yield return UnknownEnumValue(path + "/valign", pane.Valign, "valign", PaneValignParsing.AcceptedTokens);
        }

        if (PaneAlignParsing.IsUnrecognized(pane.Align))
        {
            yield return UnknownEnumValue(path + "/align", pane.Align, "align", PaneAlignParsing.AcceptedTokens);
        }

        if (OverflowModeParsing.IsUnrecognized(pane.Overflow))
        {
            yield return UnknownEnumValue(path + "/overflow", pane.Overflow, "overflow", OverflowModeParsing.AcceptedTokens);
        }

        if (!string.IsNullOrWhiteSpace(pane.Border?.Style) && !BorderStyleParsing.TryParse(pane.Border.Style!, out _))
        {
            yield return UnknownEnumValue(path + "/border/style", pane.Border.Style, "style", BorderStyleParsing.AcceptedTokens);
        }

        if (ConfigLoader.IsUnrecognizedSplit(pane.Split))
        {
            yield return UnknownEnumValue(path + "/split", pane.Split, "split", ConfigLoader.SplitAcceptedTokens);
        }

        if (PaneDistributeParsing.IsUnrecognized(pane.Distribute))
        {
            yield return UnknownEnumValue(path + "/distribute", pane.Distribute, "distribute", PaneDistributeParsing.AcceptedTokens);
        }

        if (pane.Items is { } items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var d in CheckItemEnums(items[i], $"{path}/items/{i}"))
                {
                    yield return d;
                }
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckItemEnums(PaneItemJsonConfig item, string path)
    {
        if (OverflowModeParsing.IsUnrecognized(item.Overflow))
        {
            yield return UnknownEnumValue(path + "/overflow", item.Overflow, "overflow", OverflowModeParsing.AcceptedTokens);
        }

        if (ItemValueResolver.IsUnrecognizedCase(item.Case))
        {
            yield return UnknownEnumValue(path + "/case", item.Case, "case", ItemValueResolver.CaseAcceptedTokens);
        }
    }

    private static Diagnostic UnknownEnumValue(string path, string? value, string fieldName, IReadOnlyList<string> accepted) =>
        new(path, DiagnosticSeverity.Error, "unknown-enum-value", $"'{value}' is not a {fieldName} — expected {FormatAccepted(accepted)}");

    private static string FormatAccepted(IReadOnlyList<string> accepted) => accepted.Count switch
    {
        0 => "",
        1 => accepted[0],
        2 => $"{accepted[0]} or {accepted[1]}",
        _ => string.Join(", ", accepted.Take(accepted.Count - 1)) + ", or " + accepted[^1],
    };

    // ---- §2.6/§7.1: "overflow"/"ellipsis" are leaf-only keys; a split node's own value is unconsulted ----

    private static IEnumerable<Diagnostic> CheckLeafOnlyKeysOnSplits(UserConfig? config)
    {
        if (config?.Surface?.Pane is not { } surfacePane)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkRawPanes(surfacePane, "/surface/pane"))
        {
            if (pane.Children is not { Count: > 0 })
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pane.Overflow))
            {
                yield return new Diagnostic(path + "/overflow", DiagnosticSeverity.Warning, "leaf-only-key-on-split",
                    "\"overflow\" has no effect on a split; only a leaf pane has content that can overflow");
            }

            if (!string.IsNullOrWhiteSpace(pane.Ellipsis))
            {
                yield return new Diagnostic(path + "/ellipsis", DiagnosticSeverity.Warning, "leaf-only-key-on-split",
                    "\"ellipsis\" has no effect on a split; only a leaf pane's own overflow marker uses it");
            }
        }
    }

    private static IEnumerable<(PaneConfig Pane, string Path)> WalkRawPanes(PaneConfig pane, string path)
    {
        yield return (pane, path);
        if (pane.Children is { } children)
        {
            for (var i = 0; i < children.Count; i++)
            {
                foreach (var entry in WalkRawPanes(children[i], $"{path}/children/{i}"))
                {
                    yield return entry;
                }
            }
        }
    }

    // ---- §9.8: structural size checks, width-independent, via SizeResolver's own arithmetic ----

    private static IEnumerable<Diagnostic> CheckStructuralSizes(Pane root, string rootPath)
    {
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.MinSize is int min && pane.MaxSize is int max && min > max)
            {
                yield return new Diagnostic(path, DiagnosticSeverity.Error, "min-exceeds-max",
                    $"minSize ({min}) exceeds maxSize ({max})");
            }

            if (pane.Children.Count == 0)
            {
                continue;
            }

            if (pane.Split == PaneSplit.Vertical)
            {
                // §2.8 (horizontal width allocation) is out of scope for this phase — SizeResolver
                // itself doesn't divide width among a horizontal split's children, so summing their
                // fixed/minSize against the parent would claim a contention that isn't there yet.
                // Revisit this scoping once §2.8 lands.
                foreach (var d in CheckSplitBounds(pane, path))
                {
                    yield return d;
                }
            }
            else if (pane.Split == PaneSplit.Horizontal)
            {
                foreach (var d in CheckHorizontalSplitChildren(pane, path))
                {
                    yield return d;
                }
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckSplitBounds(Pane split, string path)
    {
        var boundaryCost = SizeResolver.BoundaryCost(split, split.Children.Count);

        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is int bound)
        {
            var fixedSum = split.Children.Sum(c => SizeResolver.FixedSize(c) ?? 0);
            if (fixedSum + boundaryCost > bound)
            {
                yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"children's fixed sizes ({fixedSum}) plus boundary cost ({boundaryCost}) exceed this pane's own bound ({bound})");
            }
        }

        if (split.MaxSize is int maxBound)
        {
            var minSum = split.Children.Sum(c => c.MinSize ?? 0);
            if (minSum + boundaryCost > maxBound)
            {
                yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"children's minSize sum ({minSum}) plus boundary cost ({boundaryCost}) exceed this pane's maxSize ({maxBound})");
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckHorizontalSplitChildren(Pane split, string path)
    {
        // A horizontal split gives every child the full parent width (§2.8 not yet implemented), so
        // there is no sum to check — only whether any single child's own fixed size already exceeds
        // the width it will be given.
        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is not int bound)
        {
            yield break;
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            if (SizeResolver.FixedSize(split.Children[i]) is int childFixed && childFixed > bound)
            {
                yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"this pane's fixed size ({childFixed}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
            }
        }
    }

    // ---- §2.6: "overflow" is only legal on the sole pane of a single-pane surface ----

    private static IEnumerable<Diagnostic> CheckOverflowPosition(Pane root, string rootPath)
    {
        if (root.Split == PaneSplit.None && root.Children.Count == 0)
        {
            yield break;
        }

        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Children.Count == 0 && pane.Overflow == OverflowMode.Overflow)
            {
                yield return new Diagnostic(path + "/overflow", DiagnosticSeverity.Error, "overflow-forbidden-position",
                    "\"overflow\" is only legal when the surface has exactly one pane");
            }
        }
    }

    // ---- §9.4/§2.11/§2.11.1: a content/fill leaf pane with no items and no explicit minSize collapses;
    // an explicit minSize suppresses collapse (§2.3's floor table: "author said so; always wins"), so
    // it's as legitimate a spacer as a fixed/percent pane ----

    private static IEnumerable<Diagnostic> CheckEmptyPanes(Pane root, string rootPath)
    {
        foreach (var (pane, path) in WalkPanes(root, rootPath))
        {
            if (pane.Children.Count == 0 && pane.Items.Count == 0 && pane.MinSize is null &&
                (SizeResolver.IsContentSized(pane) || SizeResolver.IsFillSized(pane)))
            {
                yield return new Diagnostic(path, DiagnosticSeverity.Warning, "pane-no-items",
                    "this pane declares no items and its size collapses to zero, so the declaration does nothing");
            }
        }
    }

    private static IEnumerable<(Pane Pane, string Path)> WalkPanes(Pane pane, string path)
    {
        yield return (pane, path);
        for (var i = 0; i < pane.Children.Count; i++)
        {
            foreach (var entry in WalkPanes(pane.Children[i], $"{path}/children/{i}"))
            {
                yield return entry;
            }
        }
    }
}
