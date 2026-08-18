namespace DungeonDefense.Core;

public enum CosmeticCategory
{
    DungeonTheme,
    MonsterSkin,
    MagicEffect,
    CoreEmblem,
    SupporterPack,
}

public sealed record CosmeticProductDefinition(
    string Id,
    CosmeticCategory Category,
    string TargetId,
    string AssetVariant,
    bool IsDefault = false,
    bool IsAvailable = true,
    string? PlatformSku = null);

public sealed class CosmeticCatalog
{
    private readonly Dictionary<string, CosmeticProductDefinition> _products;

    public CosmeticCatalog(IEnumerable<CosmeticProductDefinition> products)
    {
        _products = products.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (_products.Count == 0) throw new ArgumentException("Cosmetic catalog requires at least one product.", nameof(products));
        if (_products.Values.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.TargetId) || string.IsNullOrWhiteSpace(x.AssetVariant)))
            throw new ArgumentException("Cosmetic product identity is required.", nameof(products));
        if (_products.Values.Where(x => x.IsDefault).GroupBy(x => (x.Category, x.TargetId)).Any(x => x.Count() > 1))
            throw new ArgumentException("Only one default cosmetic is allowed per target.", nameof(products));
        var paidSkus = _products.Values.Where(x => !x.IsDefault && !string.IsNullOrWhiteSpace(x.PlatformSku)).Select(x => x.PlatformSku!).ToArray();
        if (paidSkus.Distinct(StringComparer.Ordinal).Count() != paidSkus.Length)
            throw new ArgumentException("Platform cosmetic SKUs must be unique.", nameof(products));
    }

    public IReadOnlyList<CosmeticProductDefinition> Products => _products.Values.OrderBy(x => x.Category).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
    public CosmeticProductDefinition Product(string id) => _products.TryGetValue(id, out var value) ? value : throw new InvalidOperationException($"Unknown cosmetic product: {id}.");
    public bool TryProduct(string id, out CosmeticProductDefinition value) => _products.TryGetValue(id, out value!);
    public CosmeticProductDefinition? ProductByPlatformSku(string sku)
        => _products.Values.SingleOrDefault(x => string.Equals(x.PlatformSku, sku, StringComparison.Ordinal));
}

public sealed class CampaignCosmeticState
{
    private readonly HashSet<string> _owned;
    private readonly Dictionary<string, string> _equipped;

    public CampaignCosmeticState(IEnumerable<string>? owned = null, IReadOnlyDictionary<string, string>? equipped = null)
    {
        _owned = new HashSet<string>(owned ?? [], StringComparer.Ordinal);
        _equipped = equipped is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(equipped, StringComparer.Ordinal);
        if (_owned.Any(string.IsNullOrWhiteSpace) || _equipped.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value)))
            throw new ArgumentException("Cosmetic state contains an invalid identity.");
    }

    public IReadOnlySet<string> OwnedProductIds => _owned;
    public IReadOnlyDictionary<string, string> EquippedByTarget => _equipped;
    public bool Owns(string productId) => _owned.Contains(productId);
    public bool Grant(string productId) => _owned.Add(productId);
    public bool Revoke(string productId)
    {
        var removed = _owned.Remove(productId);
        foreach (var target in _equipped.Where(x => x.Value == productId).Select(x => x.Key).ToArray()) _equipped.Remove(target);
        return removed;
    }
    public void Equip(string targetKey, string productId) => _equipped[targetKey] = productId;
    public string? EquippedProduct(string targetKey) => _equipped.GetValueOrDefault(targetKey);
    public CampaignCosmeticState Clone() => new(_owned, _equipped);
    public static string TargetKey(CosmeticCategory category, string targetId) => $"{category}:{targetId}";
}
