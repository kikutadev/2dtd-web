using System.Text.Json.Serialization;

namespace DungeonDefense.Contracts;

public sealed record NarrativeContentFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("content_version")] string ContentVersion,
    [property: JsonPropertyName("tutorial")] IReadOnlyList<NarrativeTutorialStepFile> Tutorial,
    [property: JsonPropertyName("beats")] IReadOnlyList<NarrativeBeatFile> Beats);

public sealed record NarrativeTutorialStepFile(
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("message_key")] string MessageKey,
    [property: JsonPropertyName("focus_intent")] string FocusIntent,
    [property: JsonPropertyName("expression")] string Expression = "Neutral");

public sealed record NarrativeBeatFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("trigger")] string Trigger,
    [property: JsonPropertyName("message_key")] string MessageKey,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("priority")] int Priority = 0,
    [property: JsonPropertyName("one_shot")] bool OneShot = true,
    [property: JsonPropertyName("subject_id")] string? SubjectId = null,
    [property: JsonPropertyName("related_id")] string? RelatedId = null,
    [property: JsonPropertyName("region_id")] string? RegionId = null,
    [property: JsonPropertyName("day")] int? Day = null,
    [property: JsonPropertyName("focus_intent")] string FocusIntent = "None",
    [property: JsonPropertyName("expression")] string Expression = "Neutral");
