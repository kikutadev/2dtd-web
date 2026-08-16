using System.Collections.Immutable;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral product presentation for section-based invasion combat.
/// It owns player-visible grouping, command availability and product asset identity;
/// Godot/Web only decide how to render the resulting state.
/// </summary>
public static class InvasionBattlePresentation
{
    public const string MendCommandId = "invasion.spell.mend";
    public const string WardCommandId = "invasion.spell.ward";
    public const string RetreatCommandId = "invasion.retreat";

    public static InvasionBattleVisualState Build(InvasionSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var sections = simulation.Floor.Sections
            .Select((section, index) => new InvasionSectionVisualState(
                index,
                section.Id,
                index < simulation.SectionIndex || simulation.Outcome == InvasionOutcome.Success,
                index == simulation.SectionIndex && simulation.Outcome == InvasionOutcome.Running,
                index == simulation.SectionIndex ? simulation.SectionDefenseHp : index < simulation.SectionIndex || simulation.Outcome == InvasionOutcome.Success ? 0 : section.DefenseHp,
                section.DefenseHp))
            .ToImmutableArray();

        var reserve = simulation.Units
            .Where(x => x.Alive && !x.Deployed)
            .OrderBy(x => x.FormationIndex)
            .Select(ToUnitVisualState)
            .ToImmutableArray();
        var active = simulation.Units
            .Where(x => x.Alive && x.Deployed)
            .OrderBy(x => x.FormationIndex)
            .Select(ToUnitVisualState)
            .ToImmutableArray();

        var deploy = simulation.Units
            .Where(x => x.Alive)
            .GroupBy(x => x.Definition.Id, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new InvasionDeployCommandState(
                group.Key,
                group.Count(x => !x.Deployed),
                simulation.Outcome == InvasionOutcome.Running && group.Any(x => !x.Deployed)))
            .ToImmutableArray();

        var currentSectionIndex = Math.Clamp(simulation.SectionIndex, 0, simulation.Floor.Sections.Count - 1);
        var currentSection = simulation.Floor.Sections[currentSectionIndex];
        var mend = SpellAvailability(simulation, MendCommandId);
        var ward = SpellAvailability(simulation, WardCommandId);
        var retreat = new InvasionCommandState(
            RetreatCommandId,
            simulation.Outcome == InvasionOutcome.Running && simulation.RetreatRemainingTicks is null,
            0,
            0,
            simulation.RetreatRemainingTicks is { } ticks ? ticks / (double)InvasionSimulation.TicksPerSecond : null);

        return new InvasionBattleVisualState(
            simulation.Outcome,
            simulation.Tick,
            simulation.Floor.Depth,
            simulation.Floor.Objective,
            simulation.SectionIndex,
            sections,
            currentSection.Id,
            Math.Max(0, simulation.SectionDefenseHp),
            currentSection.DefenseHp,
            simulation.Floor.ThreatTags.ToImmutableArray(),
            reserve,
            active,
            simulation.DefeatedCount,
            simulation.Mp,
            simulation.Content.MaxMp,
            simulation.SecuredLoot,
            ProductAssetIdentity.InvasionFortification(),
            deploy,
            mend,
            ward,
            retreat,
            LatestReadableEvent(simulation));
    }

    private static InvasionUnitVisualState ToUnitVisualState(InvasionUnitRuntime unit)
        => new(
            unit.EntityId,
            unit.Definition.Id,
            ProductAssetIdentity.Unit(unit.Definition.Id, UnitFacing.East),
            unit.Hp,
            unit.Definition.MaxHp,
            unit.Shield,
            unit.FormationIndex,
            unit.Archetype);

    private static InvasionCommandState SpellAvailability(InvasionSimulation simulation, string spellId)
    {
        var spell = simulation.Content.SupportSpells[spellId];
        var cooldown = simulation.SpellCooldownRemaining(spellId);
        var enabled = simulation.Outcome == InvasionOutcome.Running
                      && simulation.ActiveCount > 0
                      && simulation.Mp >= spell.MpCost
                      && cooldown == 0;
        return new InvasionCommandState(spellId, enabled, cooldown, spell.MpCost, null);
    }

    private static ProductMessage? LatestReadableEvent(InvasionSimulation simulation)
    {
        foreach (var e in simulation.Events.AsEnumerable().Reverse())
        {
            var message = ToReadableMessage(simulation, e);
            if (message is not null) return message;
        }
        return null;
    }

    private static ProductMessage? ToReadableMessage(InvasionSimulation simulation, InvasionEvent e)
    {
        string? UnitDefinition(string? entityId)
            => simulation.Units.FirstOrDefault(x => string.Equals(x.EntityId, entityId, StringComparison.Ordinal))?.Definition.Id;

        return e.Type switch
        {
            InvasionEventType.UnitDeployed => new("invasion.event.deployed", e.Detail ?? UnitDefinition(e.ActorId), e.Amount),
            InvasionEventType.UnitAttack => new("invasion.event.attack", UnitDefinition(e.ActorId), e.Amount),
            InvasionEventType.UnitDamaged => new("invasion.event.damaged", UnitDefinition(e.TargetId), e.Amount),
            InvasionEventType.UnitDefeated => new("invasion.event.defeated", e.Detail ?? UnitDefinition(e.ActorId), e.Amount),
            InvasionEventType.SpellCast when string.Equals(e.Detail, "heal", StringComparison.Ordinal) => new("invasion.event.heal", UnitDefinition(e.TargetId), e.Amount),
            InvasionEventType.SpellCast when string.Equals(e.Detail, "shield", StringComparison.Ordinal) => new("invasion.event.ward", null, e.Amount),
            InvasionEventType.SectionCleared => new("invasion.event.section_cleared", null, e.Amount),
            InvasionEventType.RetreatRequested => new("invasion.event.retreating", null, e.Amount),
            _ => null,
        };
    }
}

public sealed record InvasionBattleVisualState(
    InvasionOutcome Outcome,
    int Tick,
    int FloorDepth,
    InvasionObjectiveKind Objective,
    int CurrentSectionIndex,
    ImmutableArray<InvasionSectionVisualState> Sections,
    string CurrentSectionId,
    int CurrentSectionHp,
    int CurrentSectionMaxHp,
    ImmutableArray<string> ThreatTags,
    ImmutableArray<InvasionUnitVisualState> ReserveUnits,
    ImmutableArray<InvasionUnitVisualState> ActiveUnits,
    int DefeatedCount,
    int Mp,
    int MaxMp,
    ResourceBundle SecuredLoot,
    ProductAssetRef FortificationAsset,
    ImmutableArray<InvasionDeployCommandState> DeployCommands,
    InvasionCommandState Mend,
    InvasionCommandState Ward,
    InvasionCommandState Retreat,
    ProductMessage? LatestEvent);

public sealed record InvasionSectionVisualState(
    int Index,
    string SectionId,
    bool Cleared,
    bool Current,
    int Hp,
    int MaxHp);

public sealed record InvasionUnitVisualState(
    string EntityId,
    string DefinitionId,
    ProductAssetRef? Asset,
    int Hp,
    int MaxHp,
    int Shield,
    int FormationIndex,
    InvasionUnitArchetype Archetype);

public sealed record InvasionDeployCommandState(string UnitDefinitionId, int ReserveCount, bool Enabled);

public sealed record InvasionCommandState(
    string CommandId,
    bool Enabled,
    int CooldownTicks,
    int MpCost,
    double? CountdownSeconds);

public sealed record ProductMessage(string Key, string? SubjectDefinitionId, int Amount);
