using DungeonDefense.Core;
using DungeonDefense.Infrastructure;

namespace DungeonDefense.Web;

/// <summary>
/// Browser transport adapter for production invasion content. Schema decoding and spatial-domain
/// materialization are owned by DungeonDefense.Infrastructure; Web owns only HTTP retrieval.
/// </summary>
internal static class WebInvasionContentLoader
{
    public static async Task<InvasionContent> LoadAsync(
        HttpClient http,
        DefenseContent defenseContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(defenseContent);
        var metadataTask = http.GetStringAsync("content/invasion-vertical-slice.json", cancellationToken);
        var mapTask = http.GetStringAsync("content/invasion-maps.json", cancellationToken);
        await Task.WhenAll(metadataTask, mapTask);
        return InvasionContentLoader.LoadFromJson(
            await metadataTask,
            await mapTask,
            defenseContent);
    }

    internal static InvasionContent Parse(string metadataJson, string mapJson, DefenseContent defenseContent)
        => InvasionContentLoader.LoadFromJson(metadataJson, mapJson, defenseContent);
}
