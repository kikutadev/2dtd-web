using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>
/// Browser URL resolver for host-neutral production asset identities.
/// Product semantics choose the asset; the Web host only chooses its public URL.
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
            _ => throw new ArgumentOutOfRangeException(nameof(asset), asset.Category, "Unknown product asset category."),
        };
        var suffix = string.IsNullOrWhiteSpace(asset.Variant) ? string.Empty : $"-{asset.Variant}";
        return $"assets/production/{folder}/{asset.Id}{suffix}.png";
    }

    public static string Unit(string definitionId, UnitFacing facing = UnitFacing.East)
        => ProductAssetIdentity.Unit(definitionId, facing) is { } asset
            ? Resolve(asset)
            : Resolve(new ProductAssetRef(ProductAssetCategory.Unit, "HU-01", CombatMotionPresentation.FacingSuffix(facing)));

    public static string Unit(string definitionId, string facing)
    {
        var canonical = ProductAssetIdentity.UnitCanonical(definitionId)
            ?? new ProductAssetRef(ProductAssetCategory.Unit, "HU-01");
        return Resolve(canonical with { Variant = facing });
    }
}
