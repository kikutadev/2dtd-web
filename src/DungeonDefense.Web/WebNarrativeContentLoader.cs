using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Contracts;
using DungeonDefense.Infrastructure;
using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>
/// Loads the same authored tutorial/narrative content snapshot used by the product runtime.
/// Web owns only HTTP transport; validation and semantic conversion stay shared.
/// </summary>
internal static class WebNarrativeContentLoader
{
    public static async Task<NarrativePresentationContent> LoadAsync(HttpClient http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var json = await http.GetStringAsync("content/narrative-campaign.json", cancellationToken);
        var file = JsonSerializer.Deserialize(json, WebNarrativeJsonContext.Default.NarrativeContentFile)
            ?? throw new InvalidDataException("Narrative content is empty.");
        NarrativeContentLoader.Validate(file);
        return NarrativeContentPresentation.Build(file);
    }
}

[JsonSerializable(typeof(NarrativeContentFile))]
internal sealed partial class WebNarrativeJsonContext : JsonSerializerContext;
