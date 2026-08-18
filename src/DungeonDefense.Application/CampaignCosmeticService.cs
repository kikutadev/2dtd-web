using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record CosmeticCommandResult(bool Success, string? Error = null)
{
    public static CosmeticCommandResult Ok() => new(true);
    public static CosmeticCommandResult Reject(string error) => new(false, error);
}

public sealed record PlatformStoreProduct(string PlatformSku, string DisplayPrice, bool Available);

/// <summary>Platform commerce boundary. Core/Application never references StoreKit or browser store APIs.</summary>
public interface IPlatformStore
{
    IReadOnlyList<PlatformStoreProduct> LoadProducts(IReadOnlyList<string> platformSkus);
    bool Purchase(string platformSku);
    IReadOnlyList<string> RestorePurchases();
    IReadOnlyList<string> CurrentEntitlements();
}

public static class CampaignCosmeticService
{
    public static void GrantDefaults(CampaignState state, CosmeticCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var product in catalog.Products.Where(x => x.IsDefault))
        {
            state.Cosmetics.Grant(product.Id);
            state.Cosmetics.Equip(CampaignCosmeticState.TargetKey(product.Category, product.TargetId), product.Id);
        }
    }

    public static CosmeticCommandResult ImportEntitlements(CampaignState state, CosmeticCatalog catalog, IEnumerable<string> productIds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var id in productIds.Distinct(StringComparer.Ordinal))
        {
            if (!catalog.TryProduct(id, out var product) || !product.IsAvailable) continue;
            state.Cosmetics.Grant(id);
        }
        return CosmeticCommandResult.Ok();
    }

    public static CosmeticCommandResult ImportStoreEntitlements(CampaignState state, CosmeticCatalog catalog, IEnumerable<string> platformSkus)
    {
        var logicalIds = platformSkus
            .Distinct(StringComparer.Ordinal)
            .Select(catalog.ProductByPlatformSku)
            .Where(x => x is { IsAvailable: true })
            .Select(x => x!.Id);
        return ImportEntitlements(state, catalog, logicalIds);
    }

    public static CosmeticCommandResult Purchase(CampaignState state, CosmeticCatalog catalog, IPlatformStore store, string productId)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!catalog.TryProduct(productId, out var product)) return CosmeticCommandResult.Reject("Unknown cosmetic product.");
        if (!product.IsAvailable) return CosmeticCommandResult.Reject("Cosmetic product is unavailable.");
        if (product.IsDefault) return CosmeticCommandResult.Reject("Default cosmetics are not purchasable.");
        if (string.IsNullOrWhiteSpace(product.PlatformSku)) return CosmeticCommandResult.Reject("Platform purchase is not configured.");
        if (!store.Purchase(product.PlatformSku)) return CosmeticCommandResult.Reject("Platform purchase did not complete.");
        state.Cosmetics.Grant(product.Id);
        return CosmeticCommandResult.Ok();
    }

    public static CosmeticCommandResult Equip(CampaignState state, CosmeticCatalog catalog, string productId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryProduct(productId, out var product)) return CosmeticCommandResult.Reject("Unknown cosmetic product.");
        if (!product.IsAvailable) return CosmeticCommandResult.Reject("Cosmetic product is unavailable.");
        if (!state.Cosmetics.Owns(productId)) return CosmeticCommandResult.Reject("Cosmetic product is not owned.");
        state.Cosmetics.Equip(CampaignCosmeticState.TargetKey(product.Category, product.TargetId), productId);
        return CosmeticCommandResult.Ok();
    }

    public static CosmeticProductDefinition? Equipped(CampaignState state, CosmeticCatalog catalog, CosmeticCategory category, string targetId)
    {
        var key = CampaignCosmeticState.TargetKey(category, targetId);
        var productId = state.Cosmetics.EquippedProduct(key);
        return productId is not null && catalog.TryProduct(productId, out var product) ? product : null;
    }
}

public sealed class UnavailablePlatformStore : IPlatformStore
{
    public IReadOnlyList<PlatformStoreProduct> LoadProducts(IReadOnlyList<string> platformSkus)
        => platformSkus.Select(x => new PlatformStoreProduct(x, string.Empty, false)).ToArray();
    public bool Purchase(string platformSku) => false;
    public IReadOnlyList<string> RestorePurchases() => [];
    public IReadOnlyList<string> CurrentEntitlements() => [];
}

public sealed class FakePlatformStore : IPlatformStore
{
    private readonly HashSet<string> _entitlements = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> _prices;

    public FakePlatformStore(IReadOnlyDictionary<string, string>? prices = null)
        => _prices = prices ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<PlatformStoreProduct> LoadProducts(IReadOnlyList<string> platformSkus)
        => platformSkus.Select(sku => new PlatformStoreProduct(sku, _prices.GetValueOrDefault(sku, string.Empty), _prices.ContainsKey(sku))).ToArray();
    public bool Purchase(string platformSku)
    {
        if (!_prices.ContainsKey(platformSku)) return false;
        _entitlements.Add(platformSku);
        return true;
    }
    public IReadOnlyList<string> RestorePurchases() => _entitlements.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> CurrentEntitlements() => RestorePurchases();
}
