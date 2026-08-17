using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public sealed record InvasionFloorMetadata(
    string Id,
    int Depth,
    InvasionObjectiveKind Objective,
    IReadOnlyList<string> ThreatTags,
    IReadOnlyDictionary<string, ResourceBundle> SectionLoot,
    ResourceBundle FirstClearReward,
    ResourceBundle RepeatReward,
    int RegenerationMinutes,
    InvasionRepeatVariationDefinition RepeatVariation);

/// <summary>
/// Loads authored hostile-dungeon geometry and combines it with non-spatial progression/reward metadata.
/// The map file is authoritative for geometry and world actor placement; metadata never synthesizes actors.
/// </summary>
public static class InvasionSpatialMapLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly InvasionSpatialMapJsonContext JsonContext = new(Options);

    public static IReadOnlyDictionary<string, InvasionFloorDefinition> Load(
        string path,
        IReadOnlyDictionary<string, InvasionFloorMetadata> metadataFloors,
        DungeonCombatContent combat)
    {
        ArgumentNullException.ThrowIfNull(metadataFloors);
        ArgumentNullException.ThrowIfNull(combat);
        return LoadFromJson(File.ReadAllText(path), metadataFloors, combat);
    }

    /// <summary>
    /// Decodes authored hostile-dungeon maps from JSON without owning transport or filesystem I/O.
    /// Browser and native hosts share this exact schema/domain assembly path.
    /// </summary>
    public static IReadOnlyDictionary<string, InvasionFloorDefinition> LoadFromJson(
        string json,
        IReadOnlyDictionary<string, InvasionFloorMetadata> metadataFloors,
        DungeonCombatContent combat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(metadataFloors);
        ArgumentNullException.ThrowIfNull(combat);
        var dto = JsonSerializer.Deserialize(json, JsonContext.InvasionSpatialMapFile)
            ?? throw new InvalidDataException("Invasion spatial map content is empty.");
        if (dto.SchemaVersion != 2 || !string.Equals(dto.Kind, "invasion_spatial_maps", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported invasion spatial map schema/kind.");
        if (dto.Floors.Length == 0 || dto.Floors.Select(x => x.FloorId).Distinct(StringComparer.Ordinal).Count() != dto.Floors.Length)
            throw new InvalidDataException("Invasion spatial map floor identities are invalid.");

        var mapIds = dto.Floors.Select(x => x.FloorId).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var metadataIds = metadataFloors.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!mapIds.SequenceEqual(metadataIds, StringComparer.Ordinal))
            throw new InvalidDataException("Invasion spatial map floor set must exactly match invasion metadata floors.");

        var result = new Dictionary<string, InvasionFloorDefinition>(StringComparer.Ordinal);
        foreach (var file in dto.Floors)
        {
            var meta = metadataFloors[file.FloorId];
            var objectiveKind = ParseObjective(file.Objective.Kind);
            if (objectiveKind != meta.Objective)
                throw new InvalidDataException($"Invasion objective mismatch between metadata and map: {file.FloorId}.");
            var mapSectionIds = file.Sections.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (!mapSectionIds.SequenceEqual(meta.SectionLoot.Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException($"Invasion section set mismatch between metadata and map: {file.FloorId}.");

            var board = MaterializeBoard(file, combat);
            var sections = file.Sections.Select(section => new InvasionSectionDefinition(
                section.Id,
                section.Cells.Select(ToPoint).ToHashSet(),
                ToPoint(section.Checkpoint),
                meta.SectionLoot[section.Id])).ToArray();
            var objective = new InvasionObjectiveDefinition(
                objectiveKind,
                ToPoint(file.Objective.Position),
                file.Objective.TargetInstanceId,
                file.Objective.StructureMaxHp);

            result.Add(file.FloorId, new InvasionFloorDefinition(
                meta.Id,
                meta.Depth,
                meta.ThreatTags,
                board,
                sections,
                objective,
                meta.FirstClearReward,
                meta.RepeatReward,
                meta.RegenerationMinutes,
                meta.RepeatVariation));
        }
        return result;
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "content", "invasion-maps.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate content/invasion-maps.json.");
    }

    private static DungeonState MaterializeBoard(FloorMapFile file, DungeonCombatContent combat)
    {
        if (file.Width < 3 || file.Height < 3) throw new InvalidDataException($"Invalid invasion board dimensions: {file.FloorId}.");
        var profile = new DungeonState(file.Width, file.Height, ToPoint(file.Entrance), ToPoint(file.Endpoint), int.MaxValue / 4);
        var passages = file.Passages.Select(ToPoint).ToArray();
        var rooms = file.Rooms.Select(x =>
        {
            if (x.Width <= 0 || x.Height <= 0 || x.Connections.Length == 0)
                throw new InvalidDataException($"Invalid invasion room definition: {file.FloorId}/{x.InstanceId}.");
            return new PlacedRoom(
                x.InstanceId,
                x.DefinitionId,
                ToPoint(x.Origin),
                x.Width,
                x.Height,
                0,
                x.Connections.Select(connection => new RoomConnection(ToPoint(connection.LocalCell), ParseDirection(connection.Direction))).ToArray());
        }).ToArray();
        var traps = file.Traps.Select(x =>
        {
            if (!combat.Traps.ContainsKey(x.DefinitionId)) throw new InvalidDataException($"Unknown invasion trap definition: {file.FloorId}/{x.DefinitionId}.");
            return new PlacedTrap(x.InstanceId, x.DefinitionId, ToPoint(x.Position), 0);
        }).ToArray();
        var guards = file.Guards.Select(x =>
        {
            if (!combat.Units.TryGetValue(x.DefinitionId, out var definition) || definition.Team != Team.Invader)
                throw new InvalidDataException($"Invalid invasion guard definition: {file.FloorId}/{x.DefinitionId}.");
            return new PlacedGuard(x.InstanceId, x.DefinitionId, ToPoint(x.Position), 0, x.GuardZoneRadius);
        }).ToArray();
        var facilities = file.Facilities.Select(x =>
        {
            if (!combat.Facilities.ContainsKey(x.DefinitionId)) throw new InvalidDataException($"Unknown invasion facility definition: {file.FloorId}/{x.DefinitionId}.");
            return new PlacedFacility(x.InstanceId, x.DefinitionId, ToPoint(x.Position), 0);
        }).ToArray();
        var materialized = DungeonSnapshotMaterializer.Materialize(profile, passages, rooms, traps, guards, facilities);
        if (!materialized.Success) throw new InvalidDataException($"Invalid authored invasion board {file.FloorId}: {materialized.Error}");
        return materialized.State;
    }

    private static CardinalDirection ParseDirection(string value) => value switch
    {
        "NORTH" => CardinalDirection.North,
        "EAST" => CardinalDirection.East,
        "SOUTH" => CardinalDirection.South,
        "WEST" => CardinalDirection.West,
        _ => throw new InvalidDataException($"Unknown invasion room connection direction: {value}"),
    };

    private static InvasionObjectiveKind ParseObjective(string value) => value switch
    {
        "RAID" => InvasionObjectiveKind.Raid,
        "ELIMINATE" => InvasionObjectiveKind.Eliminate,
        "CORE_BREAK" => InvasionObjectiveKind.CoreBreak,
        _ => throw new InvalidDataException($"Unknown invasion map objective: {value}"),
    };

    private static GridPoint ToPoint(PointFile value) => new(value.X, value.Y);

    internal sealed record InvasionSpatialMapFile(int SchemaVersion, string Kind, FloorMapFile[] Floors);
    internal sealed record FloorMapFile(string FloorId, int Width, int Height, PointFile Entrance, PointFile Endpoint,
        PointFile[] Passages, RoomFile[] Rooms, GuardFile[] Guards, PlacementFile[] Traps, PlacementFile[] Facilities, SectionFile[] Sections, ObjectiveFile Objective);
    internal sealed record PointFile(int X, int Y);
    internal sealed record RoomFile(string InstanceId, string DefinitionId, PointFile Origin, int Width, int Height, RoomConnectionFile[] Connections);
    internal sealed record RoomConnectionFile(PointFile LocalCell, string Direction);
    internal sealed record PlacementFile(string InstanceId, string DefinitionId, PointFile Position);
    internal sealed record GuardFile(string InstanceId, string DefinitionId, PointFile Position, int GuardZoneRadius);
    internal sealed record SectionFile(string Id, PointFile[] Cells, PointFile Checkpoint);
    internal sealed record ObjectiveFile(string Kind, PointFile Position, string? TargetInstanceId = null, int StructureMaxHp = 0);
}
