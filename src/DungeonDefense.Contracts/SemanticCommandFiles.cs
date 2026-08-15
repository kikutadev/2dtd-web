using System.Text.Json.Serialization;

namespace DungeonDefense.Contracts;

public sealed record SemanticCommandPointFile(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record SemanticFormationUnitFile(
    [property: JsonPropertyName("unit_id")] string UnitId,
    [property: JsonPropertyName("count")] int Count);

public sealed record SemanticCommandEntryFile(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("floor_id")] string? FloorId = null,
    [property: JsonPropertyName("cells")] IReadOnlyList<SemanticCommandPointFile>? Cells = null,
    [property: JsonPropertyName("instance_id")] string? InstanceId = null,
    [property: JsonPropertyName("definition_id")] string? DefinitionId = null,
    [property: JsonPropertyName("x")] int? X = null,
    [property: JsonPropertyName("y")] int? Y = null,
    [property: JsonPropertyName("rotated")] bool Rotated = false,
    [property: JsonPropertyName("seed")] int? Seed = null,
    [property: JsonPropertyName("spell_id")] string? SpellId = null,
    [property: JsonPropertyName("target_entity_id")] string? TargetEntityId = null,
    [property: JsonPropertyName("location_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocationId = null,
    [property: JsonPropertyName("formation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SemanticFormationUnitFile>? Formation = null,
    [property: JsonPropertyName("unit_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnitId = null,
    [property: JsonPropertyName("count"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count = null,
    [property: JsonPropertyName("research_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResearchId = null,
    [property: JsonPropertyName("now_utc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NowUtc = null,
    [property: JsonPropertyName("ticks"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Ticks = null);

public sealed record SemanticCommandSequenceFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("default_seed")] int DefaultSeed,
    [property: JsonPropertyName("commands")] IReadOnlyList<SemanticCommandEntryFile> Commands);
