using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class CosmeticCatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly CosmeticCatalogJsonContext JsonContext = new(Options);

    public static CosmeticCatalog Load(string path)
        => Parse(File.ReadAllText(path));

    public static CosmeticCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Cosmetic catalog is empty.");
        var file = JsonSerializer.Deserialize(json, JsonContext.CosmeticCatalogFile)
            ?? throw new InvalidDataException("Cosmetic catalog is empty.");
        if (file.SchemaVersion != 1 || !string.Equals(file.Kind, "cosmetic_catalog", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported cosmetic catalog schema/kind.");
        if (string.IsNullOrWhiteSpace(file.ContentVersion) || file.Products is null || file.Products.Length == 0)
            throw new InvalidDataException("Cosmetic catalog requires content_version and products.");
        return new CosmeticCatalog(file.Products.Select(ToDomain).ToArray());
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "content", "cosmetics.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate content/cosmetics.json.");
    }

    private static CosmeticProductDefinition ToDomain(CosmeticProductFile file)
    {
        if (!Enum.TryParse<CosmeticCategory>(file.Category, false, out var category))
            throw new InvalidDataException($"Unknown cosmetic category: {file.Category}.");
        return new CosmeticProductDefinition(file.Id, category, file.TargetId, file.AssetVariant, file.IsDefault, file.IsAvailable, file.PlatformSku);
    }

    internal sealed record CosmeticCatalogFile(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("content_version")] string ContentVersion,
        [property: JsonPropertyName("products")] CosmeticProductFile[] Products);

    internal sealed record CosmeticProductFile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("target_id")] string TargetId,
        [property: JsonPropertyName("asset_variant")] string AssetVariant,
        [property: JsonPropertyName("is_default")] bool IsDefault = false,
        [property: JsonPropertyName("is_available")] bool IsAvailable = true,
        [property: JsonPropertyName("platform_sku")] string? PlatformSku = null);
}
