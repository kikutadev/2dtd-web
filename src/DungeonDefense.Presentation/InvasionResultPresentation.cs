using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral result semantics for a completed invasion. Campaign hosts can provide a resolved reward;
/// lightweight hosts may omit it while keeping the same outcome/survivor/progress hierarchy.
/// </summary>
public static class InvasionResultPresentation
{
    public static InvasionResultVisualState Build(
        string locationId,
        InvasionSimulation simulation,
        InvasionResolution? resolution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Outcome == InvasionOutcome.Running)
            throw new InvalidOperationException("Invasion result presentation requires a completed simulation.");
        if (resolution is not null && resolution.Outcome != simulation.Outcome)
            throw new InvalidOperationException("Invasion resolution outcome does not match the simulation outcome.");

        var totalUnits = simulation.Units.Count;
        var survivors = simulation.Units.Count(x => x.Alive);
        var reachedSection = simulation.Outcome == InvasionOutcome.Success
            ? simulation.Floor.Sections.Count
            : Math.Min(simulation.CurrentSectionIndex + 1, simulation.Floor.Sections.Count);
        var lessonKey = simulation.Outcome switch
        {
            InvasionOutcome.Success => "invasion.result.lesson.success",
            InvasionOutcome.Retreated => "invasion.result.lesson.retreated",
            _ => "invasion.result.lesson.wiped",
        };

        return new InvasionResultVisualState(
            simulation.Outcome,
            locationId,
            simulation.Floor.Id,
            simulation.Floor.Depth,
            simulation.Floor.Objective.Kind,
            survivors,
            totalUnits,
            simulation.DefeatedCount,
            reachedSection,
            simulation.Floor.Sections.Count,
            simulation.SecuredLoot,
            resolution is not null,
            resolution?.GrantedLoot ?? ResourceBundle.Zero,
            resolution?.FirstClear ?? false,
            resolution?.NewlyUnlockedFloorId,
            lessonKey);
    }
}

public sealed record InvasionResultVisualState(
    InvasionOutcome Outcome,
    string LocationId,
    string FloorId,
    int FloorDepth,
    InvasionObjectiveKind Objective,
    int Survivors,
    int TotalUnits,
    int DefeatedCount,
    int ReachedSection,
    int SectionCount,
    ResourceBundle SecuredLoot,
    bool HasCampaignResolution,
    ResourceBundle GrantedLoot,
    bool FirstClear,
    string? NewlyUnlockedFloorId,
    string LessonMessageKey);
