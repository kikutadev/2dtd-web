using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record PlayerDungeonSaveImportResult(PlayerDungeonState Dungeon, DungeonFloorId SelectedFloorId);

/// <summary>
/// Maps the domain model to/from the versioned save contract without introducing file I/O into Application/Core.
/// </summary>
public static class PlayerDungeonSaveService
{
    public static PlayerDungeonSaveFile Export(PlayerDungeonState dungeon, DungeonFloorId? selectedFloorId = null)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var selected = selectedFloorId ?? dungeon.CurrentDeepestFloorId;
        _ = dungeon.GetFloor(selected);
        var floors = dungeon.Floors.OrderBy(x => x.Depth).Select(ExportFloor).ToArray();
        return new PlayerDungeonSaveFile(2, "player_dungeon_save", dungeon.DungeonId, selected.Value, floors);
    }

    public static PlayerDungeonSaveImportResult Import(PlayerDungeonSaveFile file, MonsterRosterContent? monsterRoster = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.SchemaVersion != 2 || !string.Equals(file.Kind, "player_dungeon_save", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported player dungeon save schema/kind.");

        var floors = file.Floors.OrderBy(x => x.Depth).Select(x => ImportFloor(x, monsterRoster)).ToArray();
        var dungeon = new PlayerDungeonState(file.DungeonId, floors);
        var selected = DungeonFloorId.Parse(file.SelectedFloorId ?? dungeon.CurrentDeepestFloorId.Value);
        _ = dungeon.GetFloor(selected);
        return new PlayerDungeonSaveImportResult(dungeon, selected);
    }

    /// <summary>
    /// Migration entry for the pre-multi-floor construction snapshot. Legacy content becomes B1F with the Core endpoint.
    /// </summary>
    public static PlayerDungeonSaveImportResult MigrateLegacySingleFloor(
        DungeonBlueprintFile legacy,
        string dungeonId = "player.dungeon.active",
        MonsterRosterContent? monsterRoster = null)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var profile = DungeonBoardProfiles.Resolve(legacy.BoardProfile.Id);
        var editor = new DungeonEditorSession(profile.CreateBase());
        var service = new DungeonStaticFileService(editor, monsterRoster);
        var imported = service.ApplyBlueprint(legacy);
        if (!imported.Success) throw new InvalidDataException(imported.Error);
        var dungeon = PlayerDungeonState.FromSingleFloor(imported.State, profile.Id, dungeonId);
        return new PlayerDungeonSaveImportResult(dungeon, DungeonFloorId.First);
    }

    private static PlayerDungeonSaveFloorFile ExportFloor(DungeonFloorState floor)
    {
        var service = new DungeonStaticFileService(new DungeonEditorSession(floor.Board), boardProfileId: floor.BoardProfileId);
        var blueprint = service.ExportBlueprint($"save.{floor.Id.Value}", $"Save {floor.Id.Value}");
        return new PlayerDungeonSaveFloorFile(
            floor.Id.Value,
            floor.Depth,
            floor.BoardProfileId,
            floor.EndpointKind == FloorEndpointKind.DungeonCore ? "dungeon_core" : "descent_gate",
            floor.CapacityMax,
            floor.Board.Sectors.Where(x => x.IsUnlocked).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            blueprint);
    }

    private static DungeonFloorState ImportFloor(PlayerDungeonSaveFloorFile source, MonsterRosterContent? monsterRoster)
    {
        var profile = DungeonBoardProfiles.Resolve(source.BoardProfileId);
        if (!string.Equals(source.Construction.BoardProfile.Id, profile.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"Floor/profile mismatch: {source.FloorId}.");

        var baseState = profile.CreateBase().WithCapacityMax(source.CapacityMax);
        var unlocked = source.UnlockedSectorIds.ToHashSet(StringComparer.Ordinal);
        var knownSectorIds = baseState.Sectors.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var unknownSector = unlocked.FirstOrDefault(x => !knownSectorIds.Contains(x));
        if (unknownSector is not null) throw new InvalidDataException($"{source.FloorId}: Unknown unlocked sector: {unknownSector}.");
        foreach (var sector in baseState.Sectors)
            baseState.SetSectorUnlocked(sector.Id, unlocked.Contains(sector.Id));

        // Restore progression-owned Capacity/Sector state before materializing construction so a
        // valid saved placement inside an unlocked sector is not rejected against the profile default.
        var editor = new DungeonEditorSession(baseState);
        var service = new DungeonStaticFileService(editor, monsterRoster, boardProfileId: profile.Id);
        var imported = service.ApplyBlueprint(source.Construction);
        if (!imported.Success) throw new InvalidDataException($"{source.FloorId}: {imported.Error}");
        var board = imported.State;
        var endpoint = source.EndpointKind switch
        {
            "descent_gate" => FloorEndpointKind.DescentGate,
            "dungeon_core" => FloorEndpointKind.DungeonCore,
            _ => throw new InvalidDataException($"Invalid endpoint_kind: {source.EndpointKind}."),
        };
        return new DungeonFloorState(DungeonFloorId.Parse(source.FloorId), source.Depth, profile.Id, endpoint, board);
    }
}
