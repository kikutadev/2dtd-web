using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record InvasionScoutReport(
    string LocationId,
    string Category,
    string FloorId,
    int Depth,
    InvasionObjectiveKind Objective,
    int SectionCount,
    IReadOnlyList<string> ThreatTags,
    ResourceBundle VisibleSectionLoot,
    ResourceBundle ClearReward,
    bool IsFirstClear,
    bool IsUnlocked,
    bool IsAvailable,
    TimeSpan RegenerationRemaining,
    bool IsRepeatVariant = false,
    string? ScenarioDigest = null);

public sealed record InvasionResolution(
    InvasionOutcome Outcome,
    string LocationId,
    string FloorId,
    ResourceBundle SecuredLoot,
    ResourceBundle GrantedLoot,
    bool FirstClear,
    string? NewlyUnlockedFloorId);

public static class InvasionCampaignService
{
    public static InvasionScoutReport Scout(CampaignState state, InvasionContent content, string locationId, string floorId, int scenarioSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        var location = content.Location(locationId);
        var baseFloor = content.Floor(locationId, floorId);
        var firstClear = !state.IsInvasionFloorCleared(locationId, floorId);
        var scenario = InvasionRepeatScenarioService.Resolve(baseFloor, firstClear, scenarioSeed);
        var floor = scenario.Floor;
        return new InvasionScoutReport(
            location.Id,
            location.Category,
            floor.Id,
            floor.Depth,
            floor.Objective,
            floor.Sections.Count,
            floor.ThreatTags.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            floor.Sections.Aggregate(ResourceBundle.Zero, (sum, x) => sum.Add(x.Loot)),
            firstClear ? floor.FirstClearReward : floor.RepeatReward,
            firstClear,
            IsFloorUnlocked(state, location, baseFloor),
            state.Realtime.IsInvasionReady(location.Id, floor.Id),
            state.Realtime.InvasionRegenerationRemaining(location.Id, floor.Id),
            scenario.IsRepeatVariant,
            scenario.ScenarioDigest);
    }

    public static InvasionResolvedScenario ResolveScenario(CampaignState state, InvasionContent content, string locationId, string floorId, int seed, bool? firstClearOverride = null)
    {
        var firstClear = firstClearOverride ?? !state.IsInvasionFloorCleared(locationId, floorId);
        return InvasionRepeatScenarioService.Resolve(content.Floor(locationId, floorId), firstClear, seed);
    }

    public static bool IsLocationUnlocked(CampaignState state, InvasionLocationDefinition location)
    {
        if (state.InvasionProgress.ContainsKey(location.Id)) return true;
        var requiredRegions = location.RequiredRegionIds ?? [];
        if (requiredRegions.Count > 0 && !requiredRegions.Contains(state.RegionId, StringComparer.Ordinal)) return false;
        // Global locations use campaign-relative Day gates. Advancing to a later region must not
        // make an earlier global location disappear merely because the local Day counter reset.
        var dayGateSatisfied = state.Day >= location.RequiredDay || (requiredRegions.Count == 0 && state.ClearedDungeons.Count > 0);
        return dayGateSatisfied && (location.RequiredResearchIds ?? []).All(state.HasCompletedResearch);
    }

    public static bool IsFloorUnlocked(CampaignState state, InvasionLocationDefinition location, InvasionFloorDefinition floor)
        => IsLocationUnlocked(state, location) && state.IsInvasionFloorUnlocked(location.Id, floor.Depth);

    /// <summary>
    /// Reconstructs the player-visible result of a completed invasion without mutating campaign state.
    /// This is used when a resolved invasion is restored from autosave and its Result screen must be shown again.
    /// </summary>
    public static InvasionResolution DescribeOutcome(
        CampaignState state,
        InvasionContent content,
        string locationId,
        InvasionSimulation simulation,
        bool? firstClearOverride = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Outcome == InvasionOutcome.Running) throw new InvalidOperationException("Invasion is still running.");

        var location = content.Location(locationId);
        var floor = content.Floor(locationId, simulation.Floor.Id);
        var firstClear = firstClearOverride ?? !state.IsInvasionFloorCleared(location.Id, floor.Id);
        var granted = simulation.Outcome switch
        {
            InvasionOutcome.Success => simulation.SecuredLoot.Add(firstClear ? floor.FirstClearReward : floor.RepeatReward),
            InvasionOutcome.Retreated => simulation.SecuredLoot,
            InvasionOutcome.Wiped => Scale(simulation.SecuredLoot, content.WipeLootPercent),
            _ => throw new InvalidOperationException($"Unsupported invasion outcome: {simulation.Outcome}"),
        };
        var newlyUnlocked = simulation.Outcome == InvasionOutcome.Success && firstClear
            ? location.Floors
                .Where(x => x.Depth > floor.Depth)
                .OrderBy(x => x.Depth)
                .FirstOrDefault()?.Id
            : null;

        return new InvasionResolution(simulation.Outcome, location.Id, floor.Id, simulation.SecuredLoot, granted, firstClear, newlyUnlocked);
    }

    public static InvasionResolution ApplyOutcome(CampaignState state, InvasionContent content, string locationId, InvasionSimulation simulation)
    {
        var resolution = DescribeOutcome(state, content, locationId, simulation);
        var location = content.Location(resolution.LocationId);
        var floor = content.Floor(resolution.LocationId, resolution.FloorId);

        if (resolution.Outcome == InvasionOutcome.Success && resolution.FirstClear)
            state.MarkInvasionFloorCleared(location.Id, floor.Id, floor.Depth, location.Floors.Max(x => x.Depth));

        state.Grant(resolution.GrantedLoot);
        if (resolution.Outcome == InvasionOutcome.Success || resolution.SecuredLoot != ResourceBundle.Zero)
            state.Realtime.StartInvasionRegeneration(location.Id, floor.Id, TimeSpan.FromMinutes(floor.RegenerationMinutes));
        return resolution;
    }

    private static ResourceBundle Scale(ResourceBundle value, int percent)
        => new(
            value.Stone * percent / 100,
            value.Iron * percent / 100,
            value.Soul * percent / 100,
            value.Relic * percent / 100);
}
