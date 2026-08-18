using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

public sealed record ShopProductVisualState(
    string ProductId,
    CosmeticCategory Category,
    string TargetId,
    string AssetVariant,
    bool Owned,
    bool Equipped,
    bool Available,
    bool PurchaseAvailable,
    string DisplayPrice);

public sealed record ShopCategoryVisualState(
    CosmeticCategory Category,
    IReadOnlyList<ShopProductVisualState> Products);

/// <summary>Host-neutral Shop read model combining catalog, campaign ownership and platform price metadata.</summary>
public static class ShopPresentation
{
    public static ShopCategoryVisualState Build(
        CosmeticCategory category,
        CampaignState state,
        CosmeticCatalog catalog,
        IReadOnlyList<PlatformStoreProduct>? platformProducts = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        var store = (platformProducts ?? []).ToDictionary(x => x.PlatformSku, StringComparer.Ordinal);

        var products = catalog.Products
            .Where(x => x.Category == category)
            .Select(product =>
            {
                var key = CampaignCosmeticState.TargetKey(product.Category, product.TargetId);
                var storeProduct = string.IsNullOrWhiteSpace(product.PlatformSku) ? null : store.GetValueOrDefault(product.PlatformSku);
                return new ShopProductVisualState(
                    product.Id, product.Category, product.TargetId, product.AssetVariant,
                    state.Cosmetics.Owns(product.Id),
                    string.Equals(state.Cosmetics.EquippedProduct(key), product.Id, StringComparison.Ordinal),
                    product.IsAvailable,
                    !product.IsDefault && storeProduct is { Available: true },
                    product.IsDefault ? string.Empty : storeProduct?.DisplayPrice ?? string.Empty);
            })
            .ToArray();
        return new ShopCategoryVisualState(category, products);
    }
}

/// <summary>Resolves the active world-theme variant without exposing store or UI concerns to simulation code.</summary>
public static class CosmeticThemePresentation
{
    public const string DungeonTargetId = "dungeon";

    public static string? ActiveWorldTheme(CampaignState state, CosmeticCatalog catalog)
    {
        var product = CampaignCosmeticService.Equipped(state, catalog, CosmeticCategory.DungeonTheme, DungeonTargetId);
        if (product is null || product.IsDefault || string.Equals(product.AssetVariant, "base", StringComparison.Ordinal)) return null;
        return product.AssetVariant;
    }
}
