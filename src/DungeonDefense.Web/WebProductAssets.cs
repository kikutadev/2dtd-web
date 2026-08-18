using DungeonDefense.Core;
using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>
/// Browser URL resolver for host-neutral production asset identities.
/// Product semantics choose the asset; the Web host only chooses its public URL.
/// Explicit ThemeVariant is used for Shop previews; gameplay remains base unless an entitlement-backed runtime theme is introduced.
/// </summary>
internal static class WebProductAssets
{
    public static string Resolve(ProductAssetRef asset)
    {
        var folder = asset.Category switch
        {
            ProductAssetCategory.Tile => "tiles",
            ProductAssetCategory.Core => "core",
            ProductAssetCategory.Gate => "gates",
            ProductAssetCategory.Trap => "traps",
            ProductAssetCategory.Facility => "facilities",
            ProductAssetCategory.Unit => string.IsNullOrWhiteSpace(asset.Variant) ? "units" : "units/directional",
            ProductAssetCategory.Icon => "icons",
            ProductAssetCategory.Effect => "fx",
            ProductAssetCategory.Prop => "props",
            ProductAssetCategory.Invasion => "invasion",
            ProductAssetCategory.UiChrome => "ui",
            ProductAssetCategory.Character => "characters",
            _ => throw new ArgumentOutOfRangeException(nameof(asset), asset.Category, "Unknown product asset category."),
        };
        var suffix = string.IsNullOrWhiteSpace(asset.Variant) ? string.Empty : $"-{asset.Variant}";
        var root = string.IsNullOrWhiteSpace(asset.ThemeVariant)
            ? "assets/production"
            : $"assets/production/themes/{asset.ThemeVariant}";
        return $"{root}/{folder}/{asset.Id}{suffix}.png";
    }

    public static string Unit(string definitionId, UnitFacing facing = UnitFacing.East, string? themeVariant = null)
        => ProductAssetIdentity.Unit(definitionId, facing) is { } asset
            ? Resolve(ApplyDungeonTheme(definitionId, asset, themeVariant))
            : Resolve(new ProductAssetRef(ProductAssetCategory.Unit, "HU-01", CombatMotionPresentation.FacingSuffix(facing)));

    public static string Unit(string definitionId, string facing, string? themeVariant = null)
    {
        var canonical = ProductAssetIdentity.UnitCanonical(definitionId)
            ?? new ProductAssetRef(ProductAssetCategory.Unit, "HU-01");
        var asset = canonical with { Variant = facing };
        return Resolve(ApplyDungeonTheme(definitionId, asset, themeVariant));
    }

    public static string Tile(TileKind kind, string? themeVariant = null)
        => Resolve(ProductAssetIdentity.Tile(kind) with { ThemeVariant = themeVariant });

    public static string Core(bool damaged, string? themeVariant = null)
        => Resolve(ProductAssetIdentity.Core(damaged) with { ThemeVariant = themeVariant });

    public static string? Trap(string definitionId, string? themeVariant = null)
        => ProductAssetIdentity.Trap(definitionId) is { } asset ? Resolve(asset with { ThemeVariant = themeVariant }) : null;

    public static string? Facility(string definitionId, string? themeVariant = null)
        => ProductAssetIdentity.Facility(definitionId) is { } asset ? Resolve(asset with { ThemeVariant = themeVariant }) : null;

    public static string Effect(string assetId, string? themeVariant = null)
        => Resolve(new ProductAssetRef(ProductAssetCategory.Effect, assetId, ThemeVariant: themeVariant));

    public static string Gate(string? themeVariant = null)
        => Resolve(ProductAssetIdentity.DescentGate() with { ThemeVariant = themeVariant });

    public static string ThemeProp(string assetId, string themeVariant)
        => Resolve(new ProductAssetRef(ProductAssetCategory.Prop, assetId, ThemeVariant: themeVariant));

    private static ProductAssetRef ApplyDungeonTheme(string definitionId, ProductAssetRef asset, string? themeVariant)
        => definitionId.StartsWith("monster.", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(themeVariant)
            ? asset with { ThemeVariant = themeVariant }
            : asset;
}
