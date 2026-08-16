using System.Collections.Immutable;
using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral product presentation for Defense HUD and Result summary.
/// Renderer hosts own layout/input only; player-visible derived values and command availability live here.
/// </summary>
public static class DefenseProductPresentation
{
    public static DefenseHudVisualState BuildHud(DefenseSimulation simulation, bool autoBattleEnabled)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var floorId = simulation.CurrentCombatFloorId;
        var impact = new DefenseDesignImpactVisualState(
            simulation.Events.Where(x => x.FloorId == floorId && x.Type == DefenseEventType.TrapTriggered && simulation.TrapIds.Contains(x.ActorId)).Sum(x => x.Amount),
            simulation.Events.Where(x => x.FloorId == floorId && x.Type == DefenseEventType.Attack && simulation.GuardIds.Contains(x.ActorId)).Sum(x => x.Amount),
            simulation.Events.Where(x => x.FloorId == floorId && x.Type == DefenseEventType.Attack && simulation.FacilityIds.Contains(x.ActorId)).Sum(x => x.Amount));

        var spells = simulation.Spells.Values
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(spell =>
            {
                var cooldown = simulation.SpellCooldownRemaining(spell.Id);
                return new DefenseSpellCommandState(
                    spell.Id,
                    spell.Kind,
                    spell.MpCost,
                    cooldown,
                    !autoBattleEnabled && simulation.Outcome == DefenseOutcome.Running && simulation.Mp >= spell.MpCost && cooldown == 0);
            })
            .ToImmutableArray();

        return new DefenseHudVisualState(
            simulation.Outcome,
            Math.Min(simulation.WaveIndex + 1, simulation.WaveCount),
            simulation.WaveCount,
            simulation.CurrentFloorDepth,
            simulation.CoreHp,
            simulation.CoreMaxHp,
            simulation.Mp,
            simulation.MaxMp,
            impact,
            spells,
            LatestReadableEvent(simulation));
    }

    public static DefenseResultVisualState BuildResult(DefenseSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Outcome == DefenseOutcome.Running)
            throw new InvalidOperationException("Defense result presentation requires a completed simulation.");

        var report = DefenseResultReport.From(simulation);
        var hypothesisKey = report.GuardCollapseCount > 0 && report.Outcome == DefenseOutcome.Failure
            ? "result.hypothesis.guard_collapse"
            : report.UnusedFacilityCount > 0
                ? "result.hypothesis.unused_device"
                : report.TrapPerformance.Count > 0 && report.TrapDamage == 0
                    ? "result.hypothesis.no_trap"
                    : report.CoreHitCount > 0
                        ? "result.hypothesis.core_hit"
                        : "result.hypothesis.safe";

        return new DefenseResultVisualState(
            report.Outcome,
            ProductAssetIdentity.Core(damaged: report.Outcome == DefenseOutcome.Failure),
            report.CoreHp,
            report.CoreMaxHp,
            report.TrapDamage,
            report.GuardDamage,
            report.FacilityDamage,
            report.TrapTriggerCount,
            report.FacilityAttackCount,
            report.UnusedFacilityCount,
            report.GuardCollapseCount,
            report.SpellCasts,
            report.CoreHitCount,
            report.FirstBreachPathIndex,
            report.DeepestPathIndex,
            hypothesisKey,
            report.FloorSummaries
                .OrderBy(x => x.Depth)
                .Select(x => new DefenseFloorResultVisualState(
                    x.FloorId,
                    x.Depth,
                    x.DeepestPathIndex,
                    x.TrafficCount,
                    x.TrapDamage,
                    x.GuardDamage,
                    x.FacilityDamage,
                    x.GuardCollapseCount,
                    x.Breached))
                .ToImmutableArray());
    }

    private static ProductMessage? LatestReadableEvent(DefenseSimulation simulation)
    {
        foreach (var e in simulation.Events.AsEnumerable().Reverse())
        {
            var message = e.Type switch
            {
                DefenseEventType.WaveStart => new ProductMessage("combat.status.wave_start", e.ActorId, WaveNumber(e.ActorId)),
                DefenseEventType.WaveEnd => new ProductMessage("combat.status.wave_end", e.ActorId, WaveNumber(e.ActorId)),
                DefenseEventType.FloorBreached => new ProductMessage("combat.status.breached", e.FloorId, simulation.FloorDepths[e.FloorId]),
                DefenseEventType.FloorEntered => new ProductMessage("combat.status.floor_entered", e.FloorId, simulation.FloorDepths[e.FloorId]),
                DefenseEventType.TrapTriggered => new ProductMessage("combat.status.trap", null, e.Amount),
                DefenseEventType.CoreDamaged => new ProductMessage("combat.status.core_hit", null, e.Amount),
                DefenseEventType.SpellCast => new ProductMessage("combat.status.spell", e.ActorId, e.Amount),
                DefenseEventType.Death => new ProductMessage("combat.status.defeated", e.Detail, e.Amount),
                _ => null,
            };
            if (message is not null) return message;
        }
        return null;
    }

    private static int WaveNumber(string waveId)
    {
        var digits = new string(waveId.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}

public sealed record DefenseHudVisualState(
    DefenseOutcome Outcome,
    int WaveNumber,
    int WaveCount,
    int CurrentFloorDepth,
    int CoreHp,
    int CoreMaxHp,
    int Mp,
    int MaxMp,
    DefenseDesignImpactVisualState DesignImpact,
    ImmutableArray<DefenseSpellCommandState> Spells,
    ProductMessage? LatestEvent);

public sealed record DefenseDesignImpactVisualState(int TrapDamage, int GuardDamage, int FacilityDamage);

public sealed record DefenseSpellCommandState(
    string SpellId,
    SpellKind Kind,
    int MpCost,
    int CooldownTicks,
    bool Enabled);

public sealed record DefenseResultVisualState(
    DefenseOutcome Outcome,
    ProductAssetRef CoreAsset,
    int CoreHp,
    int CoreMaxHp,
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int TrapTriggerCount,
    int FacilityAttackCount,
    int UnusedFacilityCount,
    int GuardCollapseCount,
    int SpellCasts,
    int CoreHitCount,
    int FirstBreachPathIndex,
    int DeepestPathIndex,
    string HypothesisMessageKey,
    ImmutableArray<DefenseFloorResultVisualState> Floors);

public sealed record DefenseFloorResultVisualState(
    string FloorId,
    int Depth,
    int DeepestPathIndex,
    int TrafficCount,
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int GuardCollapseCount,
    bool Breached);
