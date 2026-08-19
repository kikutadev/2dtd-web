using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record SemanticEditResult(
    bool Success,
    string? Error,
    DungeonState State,
    IReadOnlyList<GridPoint> Route,
    string FloorId = "floor.001")
{
    public static SemanticEditResult From(EditResult result, string floorId = "floor.001")
        => new(result.Success, result.Error, result.State, result.Route, floorId);
}

public sealed class DefenseEditCommandService
{
    private readonly DungeonEditorSession? _legacyEditor;
    private readonly PlayerDungeonEditorSession? _dungeonEditor;
    private MonsterRosterContent? _monsterRoster;
    private Func<string, bool>? _isGuardAvailable;

    public DefenseEditCommandService(DungeonEditorSession editor, MonsterRosterContent? monsterRoster = null, Func<string, bool>? isGuardAvailable = null)
    {
        _legacyEditor = editor;
        ConfigureMonsterRoster(monsterRoster, isGuardAvailable);
    }

    public DefenseEditCommandService(PlayerDungeonEditorSession editor, MonsterRosterContent? monsterRoster = null, Func<string, bool>? isGuardAvailable = null)
    {
        _dungeonEditor = editor;
        ConfigureMonsterRoster(monsterRoster, isGuardAvailable);
    }

    public IReadOnlyList<BuildOption> AvailableGuards => _monsterRoster is null
        ? []
        : _monsterRoster.Monsters
            .Where(x => _isGuardAvailable?.Invoke(x.Id) ?? true)
            .Select(DefenseSliceBuildCatalog.ToGuardOption)
            .ToArray();

    public void ConfigureMonsterRoster(MonsterRosterContent? monsterRoster, Func<string, bool>? isGuardAvailable = null)
    {
        _monsterRoster = monsterRoster;
        _isGuardAvailable = isGuardAvailable;
    }

    public SemanticEditResult Execute(SemanticCommand command)
    {
        var floorId = ResolveFloorId(command);
        var editor = ResolveEditor(floorId);
        if (command is UndoEditCommand) return Undo(editor, floorId);
        if (command is RedoEditCommand) return Redo(editor, floorId);
        return SemanticEditResult.From(editor.Apply(state => Evaluate(state, command, _monsterRoster, _isGuardAvailable)), floorId);
    }

    public static EditResult Evaluate(DungeonState state, SemanticCommand command)
        => Evaluate(state, command, null, null);

    public static EditResult Evaluate(
        DungeonState state,
        SemanticCommand command,
        MonsterRosterContent? monsterRoster,
        Func<string, bool>? isGuardAvailable = null)
    {
        return command switch
        {
            DigPathCommand dig => DungeonEditorRules.Dig(state, ToPoints(dig.Cells)),
            ClosePathCommand close => DungeonEditorRules.Close(state, ToPoints(close.Cells)),
            PlaceRoomCommand room => EvaluatePlaceRoom(state, room),
            RemoveRoomCommand room => DungeonEditorRules.RemoveRoom(state, room.InstanceId),
            RotateRoomCommand room => EvaluateRotateRoom(state, room),
            PlaceTrapCommand trap => EvaluatePlaceTrap(state, trap),
            RemoveTrapCommand trap => DungeonEditorRules.RemoveTrap(state, trap.InstanceId),
            PlaceGuardCommand guard => EvaluatePlaceGuard(state, guard, monsterRoster, isGuardAvailable),
            RemoveGuardCommand guard => DungeonEditorRules.RemoveGuard(state, guard.InstanceId),
            PlaceFacilityCommand facility => EvaluatePlaceFacility(state, facility),
            RemoveFacilityCommand facility => DungeonEditorRules.RemoveFacility(state, facility.InstanceId),
            _ => EditResult.Failed(state, $"Unsupported edit command: {command.Type}"),
        };
    }

    public SemanticEditResult Preview(SemanticCommand command)
    {
        var floorId = ResolveFloorId(command);
        var editor = ResolveEditor(floorId);
        return SemanticEditResult.From(Evaluate(editor.Current, command, _monsterRoster, _isGuardAvailable), floorId);
    }

    private DungeonEditorSession ResolveEditor(string floorId)
    {
        if (_dungeonEditor is not null) return _dungeonEditor.GetEditor(floorId);
        if (_legacyEditor is not null)
        {
            if (!string.Equals(floorId, DungeonFloorId.First.Value, StringComparison.Ordinal))
                throw new InvalidOperationException($"Legacy single-floor editor cannot edit {floorId}.");
            return _legacyEditor;
        }
        throw new InvalidOperationException("Editor service is not initialized.");
    }

    private static string ResolveFloorId(SemanticCommand command) => command switch
    {
        DigPathCommand x => x.FloorId,
        ClosePathCommand x => x.FloorId,
        PlaceRoomCommand x => x.FloorId,
        RemoveRoomCommand x => x.FloorId,
        RotateRoomCommand x => x.FloorId,
        PlaceTrapCommand x => x.FloorId,
        RemoveTrapCommand x => x.FloorId,
        PlaceGuardCommand x => x.FloorId,
        RemoveGuardCommand x => x.FloorId,
        PlaceFacilityCommand x => x.FloorId,
        RemoveFacilityCommand x => x.FloorId,
        UndoEditCommand x => x.FloorId,
        RedoEditCommand x => x.FloorId,
        _ => DungeonFloorId.First.Value,
    };

    private static EditResult EvaluatePlaceRoom(DungeonState state, PlaceRoomCommand command)
    {
        var item = DefenseSliceBuildCatalog.Rooms.SingleOrDefault(x => x.Id == command.DefinitionId);
        if (item is null) return EditResult.Failed(state, $"Unknown room definition: {command.DefinitionId}");
        var width = command.Rotated ? item.Height : item.Width;
        var height = command.Rotated ? item.Width : item.Height;
        return DungeonEditorRules.PlaceRoom(state, command.InstanceId, item.Id, new GridPoint(command.X, command.Y), width, height, item.CapacityCost, item.ResolveRoomConnections(command.Rotated), item.GuardHpBonusPercent, item.GuardDamageBonusPercent, item.PoisonDurationBonusPercent, item.ExecuteThresholdPercent, item.ExecuteDamageBonusPercent, item.SpellDurationBonusPercent, item.PushMagnitudeBonus);
    }

    private static EditResult EvaluateRotateRoom(DungeonState state, RotateRoomCommand command)
    {
        var existing = state.Rooms.SingleOrDefault(x => x.InstanceId == command.InstanceId);
        if (existing is null) return EditResult.Failed(state, $"Unknown room instance: {command.InstanceId}");
        var item = DefenseSliceBuildCatalog.Rooms.SingleOrDefault(x => x.Id == existing.DefinitionId);
        if (item is null) return EditResult.Failed(state, $"Unknown room definition: {existing.DefinitionId}");
        var rotatedConnections = item.ResolveRoomConnections(true);
        static bool SameConnections(IReadOnlyList<RoomConnection>? a, IReadOnlyList<RoomConnection> b)
            => (a ?? []).OrderBy(x => x.LocalCell.Y).ThenBy(x => x.LocalCell.X).ThenBy(x => x.Direction)
                .SequenceEqual(b.OrderBy(x => x.LocalCell.Y).ThenBy(x => x.LocalCell.X).ThenBy(x => x.Direction));
        var currentlyRotated = SameConnections(existing.Connections, rotatedConnections);
        var removed = DungeonEditorRules.RemoveRoom(state, existing.InstanceId);
        if (!removed.Success) return removed;
        var targetRotated = !currentlyRotated;
        var width = targetRotated ? item.Height : item.Width;
        var height = targetRotated ? item.Width : item.Height;
        var placed = DungeonEditorRules.PlaceRoom(removed.State, existing.InstanceId, item.Id, existing.Origin, width, height, item.CapacityCost, item.ResolveRoomConnections(targetRotated), item.GuardHpBonusPercent, item.GuardDamageBonusPercent, item.PoisonDurationBonusPercent, item.ExecuteThresholdPercent, item.ExecuteDamageBonusPercent, item.SpellDurationBonusPercent, item.PushMagnitudeBonus);
        return placed.Success ? placed : EditResult.Failed(state, placed.Error ?? "Room rotation rejected.");
    }

    private static EditResult EvaluatePlaceTrap(DungeonState state, PlaceTrapCommand command)
    {
        var item = DefenseSliceBuildCatalog.Traps.SingleOrDefault(x => x.Id == command.DefinitionId);
        return item is null
            ? EditResult.Failed(state, $"Unknown trap definition: {command.DefinitionId}")
            : DungeonEditorRules.PlaceTrap(state, command.InstanceId, item.Id, new GridPoint(command.X, command.Y), item.CapacityCost);
    }

    private static EditResult EvaluatePlaceGuard(
        DungeonState state,
        PlaceGuardCommand command,
        MonsterRosterContent? monsterRoster,
        Func<string, bool>? isGuardAvailable)
    {
        if (monsterRoster is null)
            return EditResult.Failed(state, "Monster roster is not configured for guard placement.");
        if (!monsterRoster.TryMonster(command.DefinitionId, out var monster))
            return EditResult.Failed(state, $"Unknown guard definition: {command.DefinitionId}");
        if (isGuardAvailable is not null && !isGuardAvailable(command.DefinitionId))
            return EditResult.Failed(state, $"Guard is not unlocked: {command.DefinitionId}");
        var item = DefenseSliceBuildCatalog.ToGuardOption(monster);
        return DungeonEditorRules.PlaceGuard(state, command.InstanceId, item.Id, new GridPoint(command.X, command.Y), item.CapacityCost, item.GuardZoneRadius);
    }

    private static EditResult EvaluatePlaceFacility(DungeonState state, PlaceFacilityCommand command)
    {
        var item = DefenseSliceBuildCatalog.Facilities.SingleOrDefault(x => x.Id == command.DefinitionId);
        return item is null
            ? EditResult.Failed(state, $"Unknown facility definition: {command.DefinitionId}")
            : DungeonEditorRules.PlaceFacility(state, command.InstanceId, item.Id, new GridPoint(command.X, command.Y), item.CapacityCost);
    }

    private static SemanticEditResult Undo(DungeonEditorSession editor, string floorId)
    {
        if (!editor.Undo()) return Failed(editor, floorId, "Nothing to undo.");
        return CurrentSuccess(editor, floorId);
    }

    private static SemanticEditResult Redo(DungeonEditorSession editor, string floorId)
    {
        if (!editor.Redo()) return Failed(editor, floorId, "Nothing to redo.");
        return CurrentSuccess(editor, floorId);
    }

    private static SemanticEditResult CurrentSuccess(DungeonEditorSession editor, string floorId)
        => new(true, null, editor.Current, DungeonPathfinder.FindRoute(editor.Current), floorId);

    private static SemanticEditResult Failed(DungeonEditorSession editor, string floorId, string error)
        => new(false, error, editor.Current, DungeonPathfinder.FindRoute(editor.Current), floorId);

    private static GridPoint[] ToPoints(IReadOnlyList<(int X, int Y)> cells) => cells.Select(x => new GridPoint(x.X, x.Y)).ToArray();
}
