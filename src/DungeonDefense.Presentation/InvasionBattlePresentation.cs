using System.Collections.Immutable;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral Product Presentation for spatial invasion combat.
/// Every world object in this state comes from Core authority; hosts must not synthesize
/// dungeon geometry, enemies, traps, facilities, objective progress, or command availability.
/// </summary>
public static class InvasionBattlePresentation
{
    public const string MendCommandId = "invasion.spell.mend";
    public const string WardCommandId = "invasion.spell.ward";
    public const string RetreatCommandId = "invasion.retreat";

    public static InvasionBattleVisualState Build(InvasionSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var routeSet = simulation.Route.ToHashSet();
        var cleared = simulation.ClearedSectionIds;
        var tiles = BuildTiles(simulation, routeSet);
        var sections = simulation.Floor.Sections.Select((section, index) => new InvasionSectionVisualState(
            index,
            section.Id,
            cleared.Contains(section.Id),
            index == simulation.CurrentSectionIndex && simulation.Outcome == InvasionOutcome.Running,
            section.Checkpoint,
            section.Cells.OrderBy(x => x.Y).ThenBy(x => x.X).ToImmutableArray(),
            section.Loot)).ToImmutableArray();

        var unitStates = simulation.Units.OrderBy(x => x.FormationIndex).ToArray();
        var reserve = unitStates.Where(x => x.Alive && !x.DeploymentRequested).Select(x => ToUnitVisualState(x, simulation)).ToImmutableArray();
        var staged = unitStates.Where(x => x.Alive && x.DeploymentRequested && !x.Admitted).Select(x => ToUnitVisualState(x, simulation)).ToImmutableArray();
        var active = unitStates.Where(x => x.Alive && x.Admitted).Select(x => ToUnitVisualState(x, simulation)).ToImmutableArray();
        var enemies = simulation.EnemyGuards.OrderBy(x => x.EntityId, StringComparer.Ordinal).Select(x => ToUnitVisualState(x, simulation)).ToImmutableArray();
        var staticActors = simulation.StaticActors.Select(ToStaticActorVisualState).ToImmutableArray();

        var deploy = unitStates
            .Where(x => x.Alive)
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var count = group.Count(x => !x.DeploymentRequested);
                return new InvasionDeployCommandState(group.Key, count, simulation.Outcome == InvasionOutcome.Running && count > 0);
            })
            .ToImmutableArray();

        var objective = new InvasionObjectiveVisualState(
            simulation.Floor.Objective.Kind,
            simulation.Floor.Objective.Position,
            simulation.Floor.Objective.TargetInstanceId,
            simulation.ObjectiveStructureHp,
            simulation.ObjectiveStructureMaxHp,
            simulation.Outcome == InvasionOutcome.Success,
            simulation.Floor.Objective.Kind == InvasionObjectiveKind.CoreBreak ? ProductAssetIdentity.InvasionFortification() : null);

        return new InvasionBattleVisualState(
            simulation.Outcome,
            simulation.Tick,
            simulation.Floor.Depth,
            simulation.Floor.Board.Width,
            simulation.Floor.Board.Height,
            tiles,
            simulation.Route.ToImmutableArray(),
            BuildRooms(simulation.Floor.Board.Rooms),
            sections,
            simulation.CurrentSectionIndex,
            objective,
            simulation.Floor.ThreatTags.ToImmutableArray(),
            reserve,
            staged,
            active,
            enemies,
            staticActors,
            simulation.DefeatedCount,
            simulation.Mp,
            simulation.Content.MaxMp,
            simulation.SecuredLoot,
            deploy,
            SpellAvailability(simulation, MendCommandId),
            SpellAvailability(simulation, WardCommandId),
            new InvasionCommandState(
                RetreatCommandId,
                simulation.Outcome == InvasionOutcome.Running && simulation.RetreatRemainingTicks is null,
                0,
                0,
                simulation.RetreatRemainingTicks is { } ticks ? ticks / (double)InvasionSimulation.TicksPerSecond : null),
            LatestReadableEvent(simulation));
    }

    internal static ImmutableArray<InvasionRoomVisualState> BuildRooms(IEnumerable<PlacedRoom> rooms)
        => rooms
            .OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .Select(x => new InvasionRoomVisualState(
                x.InstanceId,
                x.DefinitionId,
                x.Origin,
                x.Width,
                x.Height,
                (x.Connections ?? [])
                    .Select(connection => new InvasionRoomConnectionVisualState(connection.LocalCell, connection.Direction))
                    .ToImmutableArray()))
            .ToImmutableArray();

    private static ImmutableArray<InvasionTileVisualState> BuildTiles(InvasionSimulation simulation, HashSet<GridPoint> route)
    {
        var builder = ImmutableArray.CreateBuilder<InvasionTileVisualState>(simulation.Floor.Board.Width * simulation.Floor.Board.Height);
        for (var y = 0; y < simulation.Floor.Board.Height; y++)
        for (var x = 0; x < simulation.Floor.Board.Width; x++)
        {
            var position = new GridPoint(x, y);
            var kind = simulation.Floor.Board.GetTile(position);
            builder.Add(new InvasionTileVisualState(position, kind, ProductAssetIdentity.Tile(kind), route.Contains(position)));
        }
        return builder.MoveToImmutable();
    }

    private static InvasionUnitVisualState ToUnitVisualState(InvasionUnitStateSnapshot unit, InvasionSimulation simulation)
    {
        GridPoint? position = unit.Admitted ? unit.Position : null;
        var facing = FacingFor(unit, simulation);
        return new InvasionUnitVisualState(
            unit.EntityId,
            unit.DefinitionId,
            unit.Team,
            ProductAssetIdentity.Unit(unit.DefinitionId, facing),
            position,
            unit.Hp,
            unit.MaxHp,
            unit.Shield,
            unit.RouteProgressUnits,
            unit.FormationIndex,
            unit.Archetype,
            unit.TargetEntityId,
            unit.Alive);
    }

    private static UnitFacing FacingFor(InvasionUnitStateSnapshot unit, InvasionSimulation simulation)
    {
        GridPoint? target = null;
        if (unit.TargetEntityId is { } targetId)
        {
            target = simulation.Units.FirstOrDefault(x => x.EntityId == targetId)?.Position
                     ?? simulation.EnemyGuards.FirstOrDefault(x => x.EntityId == targetId)?.Position;
        }
        if (target is null && unit.Team == Team.Dungeon && unit.Admitted)
        {
            var index = new RouteProgress(unit.RouteProgressUnits).ToLogicalCellIndex(simulation.Route.Count);
            if (index + 1 < simulation.Route.Count) target = simulation.Route[index + 1];
            else target = simulation.Floor.Objective.Position;
        }
        if (target is null) return unit.Team == Team.Dungeon ? UnitFacing.East : UnitFacing.West;
        var dx = target.Value.X - unit.Position.X;
        var dy = target.Value.Y - unit.Position.Y;
        if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0) return dx > 0 ? UnitFacing.East : UnitFacing.West;
        if (dy != 0) return dy > 0 ? UnitFacing.South : UnitFacing.North;
        return unit.Team == Team.Dungeon ? UnitFacing.East : UnitFacing.West;
    }

    private static InvasionStaticActorVisualState ToStaticActorVisualState(InvasionStaticActorStateSnapshot actor)
    {
        var asset = actor.Kind switch
        {
            InvasionActorKind.Trap => ProductAssetIdentity.Trap(actor.DefinitionId),
            InvasionActorKind.Facility => ProductAssetIdentity.Facility(actor.DefinitionId),
            _ => null,
        };
        return new InvasionStaticActorVisualState(actor.InstanceId, actor.DefinitionId, actor.Kind, actor.Position, actor.CooldownRemaining, asset);
    }

    private static InvasionCommandState SpellAvailability(InvasionSimulation simulation, string spellId)
    {
        if (!simulation.Content.SupportSpells.TryGetValue(spellId, out var spell))
            return new InvasionCommandState(spellId, false, 0, 0, null);
        var cooldown = simulation.SpellCooldownRemaining(spellId);
        var enabled = simulation.Outcome == InvasionOutcome.Running
                      && simulation.ActiveCount > 0
                      && simulation.Mp >= spell.MpCost
                      && cooldown == 0;
        return new InvasionCommandState(spellId, enabled, cooldown, spell.MpCost, null);
    }

    private static ProductMessage? LatestReadableEvent(InvasionSimulation simulation)
    {
        foreach (var value in simulation.Events.AsEnumerable().Reverse())
        {
            var message = ToReadableMessage(simulation, value);
            if (message is not null) return message;
        }
        return null;
    }

    private static ProductMessage? ToReadableMessage(InvasionSimulation simulation, InvasionEvent value)
    {
        string? Definition(string? entityId)
            => simulation.Units.FirstOrDefault(x => string.Equals(x.EntityId, entityId, StringComparison.Ordinal))?.DefinitionId
               ?? simulation.EnemyGuards.FirstOrDefault(x => string.Equals(x.EntityId, entityId, StringComparison.Ordinal))?.DefinitionId;

        return value.Type switch
        {
            InvasionEventType.DeploymentRequested => new("invasion.event.deployed", value.Detail ?? Definition(value.ActorId), value.Amount),
            InvasionEventType.UnitAdmitted => new("invasion.event.deployed", value.Detail ?? Definition(value.ActorId), value.Amount),
            InvasionEventType.UnitAttack => new("invasion.event.attack", Definition(value.ActorId), value.Amount),
            InvasionEventType.GuardAttack or InvasionEventType.FacilityAttack or InvasionEventType.TrapTriggered
                => new("invasion.event.damaged", Definition(value.TargetId), value.Amount),
            InvasionEventType.UnitDefeated => new("invasion.event.defeated", value.Detail ?? Definition(value.ActorId), value.Amount),
            InvasionEventType.GuardDefeated => new("invasion.event.enemy_defeated", value.Detail ?? Definition(value.ActorId), value.Amount),
            InvasionEventType.SpellCast when string.Equals(value.Detail, "heal", StringComparison.Ordinal) || string.Equals(value.Detail, "unit-heal", StringComparison.Ordinal)
                => new("invasion.event.heal", Definition(value.TargetId), value.Amount),
            InvasionEventType.SpellCast when string.Equals(value.Detail, "shield", StringComparison.Ordinal)
                => new("invasion.event.ward", null, value.Amount),
            InvasionEventType.SectionCleared => new("invasion.event.section_cleared", value.ActorId, value.Amount),
            InvasionEventType.ObjectiveDamaged => new("invasion.event.objective_damaged", null, value.Amount),
            InvasionEventType.ObjectiveCompleted => new("invasion.event.objective_completed", null, value.Amount),
            InvasionEventType.RetreatRequested => new("invasion.event.retreating", null, value.Amount),
            _ => null,
        };
    }
}

public sealed record InvasionBattleVisualState(
    InvasionOutcome Outcome,
    int Tick,
    int FloorDepth,
    int BoardWidth,
    int BoardHeight,
    ImmutableArray<InvasionTileVisualState> Tiles,
    ImmutableArray<GridPoint> Route,
    ImmutableArray<InvasionRoomVisualState> Rooms,
    ImmutableArray<InvasionSectionVisualState> Sections,
    int CurrentSectionIndex,
    InvasionObjectiveVisualState Objective,
    ImmutableArray<string> ThreatTags,
    ImmutableArray<InvasionUnitVisualState> ReserveUnits,
    ImmutableArray<InvasionUnitVisualState> StagedUnits,
    ImmutableArray<InvasionUnitVisualState> ActiveUnits,
    ImmutableArray<InvasionUnitVisualState> EnemyGuards,
    ImmutableArray<InvasionStaticActorVisualState> StaticActors,
    int DefeatedCount,
    int Mp,
    int MaxMp,
    ResourceBundle SecuredLoot,
    ImmutableArray<InvasionDeployCommandState> DeployCommands,
    InvasionCommandState Mend,
    InvasionCommandState Ward,
    InvasionCommandState Retreat,
    ProductMessage? LatestEvent);

public sealed record InvasionTileVisualState(GridPoint Position, TileKind Kind, ProductAssetRef Asset, bool OnObjectiveRoute);
public sealed record InvasionRoomConnectionVisualState(GridPoint LocalCell, CardinalDirection Direction);
public sealed record InvasionRoomVisualState(
    string InstanceId,
    string DefinitionId,
    GridPoint Origin,
    int Width,
    int Height,
    ImmutableArray<InvasionRoomConnectionVisualState> Connections);

public sealed record InvasionSectionVisualState(
    int Index,
    string SectionId,
    bool Cleared,
    bool Current,
    GridPoint Checkpoint,
    ImmutableArray<GridPoint> Cells,
    ResourceBundle Loot);

public sealed record InvasionObjectiveVisualState(
    InvasionObjectiveKind Kind,
    GridPoint Position,
    string? TargetInstanceId,
    int CurrentHp,
    int MaxHp,
    bool Completed,
    ProductAssetRef? Asset);

public sealed record InvasionUnitVisualState(
    string EntityId,
    string DefinitionId,
    Team Team,
    ProductAssetRef? Asset,
    GridPoint? BoardPosition,
    int Hp,
    int MaxHp,
    int Shield,
    long RouteProgressUnits,
    int FormationIndex,
    InvasionUnitArchetype Archetype,
    string? TargetEntityId,
    bool Alive);

public sealed record InvasionStaticActorVisualState(
    string InstanceId,
    string DefinitionId,
    InvasionActorKind Kind,
    GridPoint Position,
    int CooldownRemaining,
    ProductAssetRef? Asset);

public sealed record InvasionDeployCommandState(string UnitDefinitionId, int ReserveCount, bool Enabled);

public sealed record InvasionCommandState(
    string CommandId,
    bool Enabled,
    int CooldownTicks,
    int MpCost,
    double? CountdownSeconds);

public sealed record ProductMessage(string Key, string? SubjectDefinitionId, int Amount);
