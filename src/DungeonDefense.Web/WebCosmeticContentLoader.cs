using DungeonDefense.Core;
using DungeonDefense.Infrastructure;

namespace DungeonDefense.Web;

internal static class WebCosmeticContentLoader
{
    public static async Task<CosmeticCatalog> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        var json = await http.GetStringAsync("content/cosmetics.json", cancellationToken);
        return CosmeticCatalogLoader.Parse(json);
    }
}
