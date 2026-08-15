using System.Text.Json.Serialization;

namespace DungeonDefense.Contracts;

public sealed record StaticPointFile(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record BoardProfileFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("entrance")] StaticPointFile Entrance,
    [property: JsonPropertyName("core")] StaticPointFile Core,
    [property: JsonPropertyName("ingress")] IReadOnlyList<StaticPointFile>? Ingress = null,
    [property: JsonPropertyName("entrance_type_id")] string? EntranceTypeId = null);

public sealed record BlueprintRoomFile(
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("rotated")] bool Rotated = false);

public sealed record BlueprintPlacementFile(
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record BlueprintConstructionFile(
    [property: JsonPropertyName("passages")] IReadOnlyList<StaticPointFile> Passages,
    [property: JsonPropertyName("rooms")] IReadOnlyList<BlueprintRoomFile> Rooms,
    [property: JsonPropertyName("traps")] IReadOnlyList<BlueprintPlacementFile> Traps,
    [property: JsonPropertyName("guards")] IReadOnlyList<BlueprintPlacementFile> Guards,
    [property: JsonPropertyName("facilities")] IReadOnlyList<BlueprintPlacementFile> Facilities);

public sealed record DungeonBlueprintFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("board_profile")] BoardProfileFile BoardProfile,
    [property: JsonPropertyName("construction")] BlueprintConstructionFile Construction);

public sealed record PatternCharacteristicsFile(
    [property: JsonPropertyName("route_length")] string? RouteLength,
    [property: JsonPropertyName("trap_reliance")] string? TrapReliance,
    [property: JsonPropertyName("guard_reliance")] string? GuardReliance,
    [property: JsonPropertyName("facility_reliance")] string? FacilityReliance,
    [property: JsonPropertyName("room_reliance")] string? RoomReliance,
    [property: JsonPropertyName("magic_synergy")] string? MagicSynergy,
    [property: JsonPropertyName("learning_tag")] string? LearningTag,
    [property: JsonPropertyName("failure_profile")] string? FailureProfile);

public sealed record PatternCommandFile(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("cells")] IReadOnlyList<StaticPointFile>? Cells = null,
    [property: JsonPropertyName("instance_id")] string? InstanceId = null,
    [property: JsonPropertyName("definition_id")] string? DefinitionId = null,
    [property: JsonPropertyName("x")] int? X = null,
    [property: JsonPropertyName("y")] int? Y = null,
    [property: JsonPropertyName("rotated")] bool Rotated = false);

public sealed record PatternRecipeFile(
    [property: JsonPropertyName("board_profile")] string BoardProfile,
    [property: JsonPropertyName("commands")] IReadOnlyList<PatternCommandFile> Commands);

public sealed record DungeonBuildPatternFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("required_content")] IReadOnlyList<string> RequiredContent,
    [property: JsonPropertyName("expected_characteristics")] PatternCharacteristicsFile? ExpectedCharacteristics,
    [property: JsonPropertyName("recipes")] IReadOnlyList<PatternRecipeFile> Recipes);

public sealed record PlayerDungeonSaveFloorFile(
    [property: JsonPropertyName("floor_id")] string FloorId,
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("board_profile_id")] string BoardProfileId,
    [property: JsonPropertyName("endpoint_kind")] string EndpointKind,
    [property: JsonPropertyName("capacity_max")] int CapacityMax,
    [property: JsonPropertyName("unlocked_sector_ids")] IReadOnlyList<string> UnlockedSectorIds,
    [property: JsonPropertyName("construction")] DungeonBlueprintFile Construction);

public sealed record PlayerDungeonSaveFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("dungeon_id")] string DungeonId,
    [property: JsonPropertyName("selected_floor_id")] string? SelectedFloorId,
    [property: JsonPropertyName("floors")] IReadOnlyList<PlayerDungeonSaveFloorFile> Floors);
