using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Adapts spatial Invasion Core facts into the shared combat visual timeline.
/// It contains no combat resolution: positions, targets, damage, death, and projectile sources
/// are copied from Core snapshots/events and only visual interpolation/lifetime is added.
/// </summary>
public sealed class InvasionCombatMotionPresentation
{
    public const string ObjectiveRuntimeActorId = "OBJECTIVE";
    private readonly CombatMotionPresentation _timeline = new();
    private string? _boundIdentity;
    private int _consumedEventCount;

    public CombatVisualState VisualState => _timeline.VisualState;

    public void Sync(InvasionSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var identity = $"{simulation.Floor.Id}|{InvasionMapDigest.Compute(simulation.Floor)}|{simulation.Seed}";
        var firstBind = !string.Equals(_boundIdentity, identity, StringComparison.Ordinal);
        if (firstBind)
        {
            _timeline.Reset();
            _boundIdentity = identity;
            // A restored encounter may already contain a long event history. Bind to the current
            // gameplay state without replaying historical presentation cues.
            _consumedEventCount = simulation.Tick > 0 ? simulation.Events.Count : 0;
        }

        _timeline.SyncSnapshot(
            ToUnitSnapshots(simulation),
            simulation.Floor.Id,
            simulation.Route,
            ToStaticActors(simulation));

        for (var index = _consumedEventCount; index < simulation.Events.Count; index++)
        {
            var mapped = MapEvent(simulation, simulation.Events[index]);
            if (mapped is not null) _timeline.ConsumeEvent(mapped);
        }
        _consumedEventCount = simulation.Events.Count;
    }

    public void Advance(double deltaSeconds, double battleSpeed)
        => _timeline.Advance(deltaSeconds, battleSpeed);

    public void Reset()
    {
        _timeline.Reset();
        _boundIdentity = null;
        _consumedEventCount = 0;
    }

    private static UnitSnapshot[] ToUnitSnapshots(InvasionSimulation simulation)
    {
        var units = simulation.Units
            .Where(x => x.Admitted)
            .Select(x => new UnitSnapshot(
                x.EntityId,
                x.DefinitionId,
                x.Team,
                x.Position,
                x.Hp,
                x.MaxHp,
                new RouteProgress(x.RouteProgressUnits).ToLogicalCellIndex(simulation.Route.Count),
                x.Alive,
                x.TargetEntityId,
                simulation.Floor.Id,
                false,
                x.RouteProgressUnits));
        var guards = simulation.EnemyGuards.Select(x => new UnitSnapshot(
            x.EntityId,
            x.DefinitionId,
            x.Team,
            x.Position,
            x.Hp,
            x.MaxHp,
            0,
            x.Alive,
            x.TargetEntityId,
            simulation.Floor.Id));
        return units.Concat(guards).ToArray();
    }

    private static List<DefenseStaticActorSnapshot> ToStaticActors(InvasionSimulation simulation)
    {
        var actors = simulation.StaticActors.Select(x => new DefenseStaticActorSnapshot(
            x.InstanceId,
            x.DefinitionId,
            simulation.Floor.Id,
            x.Position)).ToList();
        actors.Add(new DefenseStaticActorSnapshot(
            ObjectiveRuntimeActorId,
            $"objective.{simulation.Floor.Objective.Kind.ToString().ToLowerInvariant()}",
            simulation.Floor.Id,
            simulation.Floor.Objective.Position));
        return actors;
    }

    private static DefenseEvent? MapEvent(InvasionSimulation simulation, InvasionEvent value)
    {
        var floorId = simulation.Floor.Id;
        return value.Type switch
        {
            InvasionEventType.UnitAdmitted => new DefenseEvent(value.Tick, DefenseEventType.Spawn, value.ActorId,
                Position: value.Position, Detail: value.Detail, FloorId: floorId),
            InvasionEventType.UnitMoved or InvasionEventType.GuardMoved => new DefenseEvent(value.Tick, DefenseEventType.Move,
                value.ActorId, value.TargetId, value.Position, value.Amount, value.Detail, floorId, value.SourcePosition, value.SourceDefinitionId),
            InvasionEventType.UnitAttack or InvasionEventType.GuardAttack or InvasionEventType.FacilityAttack => new DefenseEvent(
                value.Tick, DefenseEventType.Attack, value.ActorId, value.TargetId, value.Position, value.Amount, value.Detail,
                floorId, value.SourcePosition, value.SourceDefinitionId),
            InvasionEventType.TrapTriggered => new DefenseEvent(value.Tick, DefenseEventType.TrapTriggered, value.ActorId,
                value.TargetId, value.Position, value.Amount, value.Detail, floorId, value.SourcePosition, value.SourceDefinitionId),
            InvasionEventType.UnitDefeated or InvasionEventType.GuardDefeated => new DefenseEvent(value.Tick, DefenseEventType.Death,
                value.ActorId, value.TargetId, value.Position, value.Amount, value.Detail, floorId, value.SourcePosition, value.SourceDefinitionId),
            InvasionEventType.SpellCast when string.Equals(value.Detail, "heal", StringComparison.Ordinal)
                || string.Equals(value.Detail, "unit-heal", StringComparison.Ordinal) => new DefenseEvent(
                    value.Tick, DefenseEventType.Heal, value.ActorId, value.TargetId, value.Position, value.Amount, value.Detail,
                    floorId, value.SourcePosition, value.SourceDefinitionId),
            InvasionEventType.ObjectiveDamaged => new DefenseEvent(value.Tick, DefenseEventType.Attack, value.ActorId,
                ObjectiveRuntimeActorId, simulation.Floor.Objective.Position, value.Amount, value.Detail, floorId,
                value.SourcePosition, value.SourceDefinitionId),
            _ => null,
        };
    }
}
