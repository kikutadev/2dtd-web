using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed class DefenseAutoBattleController
{
    private readonly PlayerDungeonState _dungeon;

    public DefenseAutoBattleController(DungeonState dungeon)
        : this(PlayerDungeonState.FromSingleFloor(dungeon, "legacy.single"))
    {
    }

    public DefenseAutoBattleController(PlayerDungeonState dungeon)
    {
        _dungeon = dungeon.Clone();
    }

    public bool TryQueueAction(DefenseSimulation simulation)
    {
        if (simulation.Outcome != DefenseOutcome.Running) return false;
        var floorId = simulation.CurrentCombatFloorId;
        var invaders = simulation.Units
            .Where(x => x.Team == Team.Invader && x.Alive && x.FloorId == floorId)
            .OrderByDescending(x => x.PathIndex)
            .ThenBy(x => x.EntityId, StringComparer.Ordinal)
            .ToArray();
        if (invaders.Length == 0) return false;

        if (TryQueueTrapPush(simulation, invaders, floorId)) return true;
        return TryQueueFreeze(simulation, invaders, floorId);
    }

    private bool TryQueueTrapPush(DefenseSimulation simulation, IReadOnlyList<UnitSnapshot> invaders, string floorId)
    {
        if (!simulation.Spells.TryGetValue("spell.push", out var spell)) return false;
        if (simulation.Mp < spell.MpCost || simulation.SpellCooldownRemaining(spell.Id) > 0) return false;

        var floor = _dungeon.GetFloor(floorId);
        var route = simulation.Routes[floorId];
        foreach (var target in invaders)
        {
            if (target.PathIndex <= 0) continue;
            var landingIndex = Math.Max(0, target.PathIndex - Math.Max(1, spell.Magnitude));
            var landing = route[landingIndex];
            var readyTrap = floor.Board.Traps
                .Where(x => x.Position == landing)
                .OrderBy(x => x.InstanceId, StringComparer.Ordinal)
                .FirstOrDefault(x => simulation.TrapCooldownRemaining(x.InstanceId, floorId) == 0);
            if (readyTrap is null) continue;
            var queued = simulation.QueueSpell(spell.Id, target.Position, target.EntityId, floorId);
            return queued.Success;
        }
        return false;
    }

    private static bool TryQueueFreeze(DefenseSimulation simulation, IReadOnlyList<UnitSnapshot> invaders, string floorId)
    {
        if (!simulation.Spells.TryGetValue("spell.freeze", out var spell)) return false;
        if (simulation.Mp < spell.MpCost || simulation.SpellCooldownRemaining(spell.Id) > 0) return false;

        var candidates = invaders
            .Select(x => x.Position)
            .Distinct()
            .Select(position => new
            {
                Position = position,
                Count = invaders.Count(x => x.Position.ManhattanDistance(position) <= spell.Radius),
                Deepest = invaders.Where(x => x.Position.ManhattanDistance(position) <= spell.Radius).Max(x => x.PathIndex),
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Deepest)
            .ThenBy(x => x.Position.Y)
            .ThenBy(x => x.Position.X)
            .First();

        var route = simulation.Routes[floorId];
        var imminentBreach = candidates.Deepest >= Math.Max(0, route.Count - 2);
        if (candidates.Count < 2 && !imminentBreach) return false;
        return simulation.QueueSpell(spell.Id, candidates.Position, floorId: floorId).Success;
    }
}
