namespace DungeonDefense.Contracts;

public abstract record SemanticCommand(string Type);

public sealed record DigPathCommand(IReadOnlyList<(int X, int Y)> Cells, string FloorId = "floor.001") : SemanticCommand("dig_path");
public sealed record ClosePathCommand(IReadOnlyList<(int X, int Y)> Cells, string FloorId = "floor.001") : SemanticCommand("close_path");

public sealed record PlaceRoomCommand(string InstanceId, string DefinitionId, int X, int Y, bool Rotated = false, string FloorId = "floor.001") : SemanticCommand("place_room");
public sealed record RemoveRoomCommand(string InstanceId, string FloorId = "floor.001") : SemanticCommand("remove_room");
public sealed record RotateRoomCommand(string InstanceId, string FloorId = "floor.001") : SemanticCommand("rotate_room");
public sealed record PlaceTrapCommand(string InstanceId, string DefinitionId, int X, int Y, string FloorId = "floor.001") : SemanticCommand("place_trap");
public sealed record RemoveTrapCommand(string InstanceId, string FloorId = "floor.001") : SemanticCommand("remove_trap");
public sealed record PlaceGuardCommand(string InstanceId, string DefinitionId, int X, int Y, string FloorId = "floor.001") : SemanticCommand("place_guard");
public sealed record RemoveGuardCommand(string InstanceId, string FloorId = "floor.001") : SemanticCommand("remove_guard");
public sealed record PlaceFacilityCommand(string InstanceId, string DefinitionId, int X, int Y, string FloorId = "floor.001") : SemanticCommand("place_facility");
public sealed record RemoveFacilityCommand(string InstanceId, string FloorId = "floor.001") : SemanticCommand("remove_facility");
public sealed record UndoEditCommand(string FloorId = "floor.001") : SemanticCommand("undo_edit");
public sealed record RedoEditCommand(string FloorId = "floor.001") : SemanticCommand("redo_edit");

public sealed record StartDefenseCommand(int Seed) : SemanticCommand("start_defense");
public sealed record CastDefenseSpellCommand(string SpellId, int X, int Y, string? TargetEntityId = null) : SemanticCommand("cast_defense_spell");

public sealed record SemanticFormationUnit(string UnitId, int Count);
public sealed record CompleteResearchCommand(string ResearchId) : SemanticCommand("research");
public sealed record ObserveRealtimeCommand(DateTimeOffset NowUtc) : SemanticCommand("observe_realtime");
public sealed record CollectProductionCommand() : SemanticCommand("collect_production");
public sealed record StartInvasionCommand(
    string LocationId,
    string FloorId,
    IReadOnlyList<SemanticFormationUnit> Formation,
    int Seed = 0) : SemanticCommand("start_invasion");
public sealed record DeployGroupCommand(string UnitId, int Count) : SemanticCommand("deploy_group");
public sealed record CastInvasionSpellCommand(string SpellId) : SemanticCommand("cast_invasion_spell");
public sealed record RetreatInvasionCommand() : SemanticCommand("retreat");
public sealed record ReturnFromInvasionCommand() : SemanticCommand("return_from_invasion");
public sealed record AdvanceTicksCommand(int Ticks) : SemanticCommand("advance_ticks");
