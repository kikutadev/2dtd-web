using DungeonDefense.Core;

namespace DungeonDefense.Application;

public enum InvasionScoutActorKind
{
    Guard,
    Trap,
    Facility,
}

public sealed record InvasionScoutTileReport(GridPoint Position, TileKind Kind);
public sealed record InvasionScoutRoomConnectionReport(GridPoint LocalCell, CardinalDirection Direction);
public sealed record InvasionScoutRoomReport(
    string InstanceId,
    string DefinitionId,
    GridPoint Origin,
    int Width,
    int Height,
    IReadOnlyList<InvasionScoutRoomConnectionReport> Connections);
public sealed record InvasionScoutSectionReport(string SectionId, IReadOnlyList<GridPoint> Cells, GridPoint Checkpoint);
public sealed record InvasionScoutObjectiveReport(InvasionObjectiveKind Kind, GridPoint Position);
public sealed record InvasionScoutActorReport(string InstanceId, string DefinitionId, InvasionScoutActorKind Kind, GridPoint Position);
public sealed record InvasionScoutMapReport(
    string MapDigest,
    int Width,
    int Height,
    IReadOnlyList<InvasionScoutTileReport> Tiles,
    IReadOnlyList<GridPoint> ObjectiveRoute,
    IReadOnlyList<InvasionScoutRoomReport> Rooms,
    IReadOnlyList<InvasionScoutSectionReport> Sections,
    InvasionScoutObjectiveReport Objective,
    IReadOnlyList<InvasionScoutActorReport> VisibleActors);

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
    InvasionScoutMapReport Map,
    bool IsRepeatVariant = false,
    string? ScenarioDigest = null);

public sealed record InvasionResolution(
    InvasionOutcome Outcome,
    string LocationId,
    string FloorId,
    ResourceBundle SecuredLoot,
    ResourceBundle BaseGrantedLoot,
    ResourceBundle PerformanceBonus,
    ResourceBundle GrantedLoot,
    InvasionPerformanceGrade PerformanceGrade,
    int PerformanceBonusPercent,
    int EngagedUnitCount,
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
            floor.Objective.Kind,
            floor.Sections.Count,
            floor.ThreatTags.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            floor.Sections.Aggregate(ResourceBundle.Zero, (sum, x) => sum.Add(x.Loot)),
            firstClear ? floor.FirstClearReward : floor.RepeatReward,
            firstClear,
            IsFloorUnlocked(state, location, baseFloor),
            state.Realtime.IsInvasionReady(location.Id, floor.Id),
            state.Realtime.InvasionRegenerationRemaining(location.Id, floor.Id),
            BuildScoutMap(floor),
            scenario.IsRepeatVariant,
            scenario.ScenarioDigest);
    }

    private static InvasionScoutMapReport BuildScoutMap(InvasionFloorDefinition floor)
    {
        var tiles = new List<InvasionScoutTileReport>(floor.Board.Width * floor.Board.Height);
        for (var y = 0; y < floor.Board.Height; y++)
        for (var x = 0; x < floor.Board.Width; x++)
        {
            var position = new GridPoint(x, y);
            tiles.Add(new InvasionScoutTileReport(position, floor.Board.GetTile(position)));
        }
        var actors = floor.Board.Guards
            .Select(x => new InvasionScoutActorReport(x.InstanceId, x.DefinitionId, InvasionScoutActorKind.Guard, x.Position))
            .Concat(floor.Board.Traps.Select(x => new InvasionScoutActorReport(x.InstanceId, x.DefinitionId, InvasionScoutActorKind.Trap, x.Position)))
            .Concat(floor.Board.Facilities.Select(x => new InvasionScoutActorReport(x.InstanceId, x.DefinitionId, InvasionScoutActorKind.Facility, x.Position)))
            .OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .ToArray();
        return new InvasionScoutMapReport(
            InvasionMapDigest.Compute(floor),
            floor.Board.Width,
            floor.Board.Height,
            tiles,
            floor.ObjectiveRoute().ToArray(),
            floor.Board.Rooms
                .OrderBy(x => x.InstanceId, StringComparer.Ordinal)
                .Select(x => new InvasionScoutRoomReport(
                    x.InstanceId,
                    x.DefinitionId,
                    x.Origin,
                    x.Width,
                    x.Height,
                    (x.Connections ?? [])
                        .Select(connection => new InvasionScoutRoomConnectionReport(connection.LocalCell, connection.Direction))
                        .ToArray()))
                .ToArray(),
            floor.Sections.Select(x => new InvasionScoutSectionReport(
                x.Id,
                x.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray(),
                x.Checkpoint)).ToArray(),
            new InvasionScoutObjectiveReport(floor.Objective.Kind, floor.Objective.Position),
            actors);
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
        var baseGranted = simulation.Outcome switch
        {
            InvasionOutcome.Success => simulation.SecuredLoot.Add(firstClear ? floor.FirstClearReward : floor.RepeatReward),
            InvasionOutcome.Retreated => simulation.SecuredLoot,
            InvasionOutcome.Wiped => Scale(simulation.SecuredLoot, content.WipeLootPercent),
            _ => throw new InvalidOperationException($"Unsupported invasion outcome: {simulation.Outcome}"),
        };
        var performance = InvasionPerformanceRewardPolicy.Resolve(simulation, baseGranted);
        var granted = baseGranted.Add(performance.Bonus);
        var newlyUnlocked = simulation.Outcome == InvasionOutcome.Success && firstClear
            ? location.Floors
                .Where(x => x.Depth > floor.Depth)
                .OrderBy(x => x.Depth)
                .FirstOrDefault()?.Id
            : null;

        return new InvasionResolution(
            simulation.Outcome,
            location.Id,
            floor.Id,
            simulation.SecuredLoot,
            baseGranted,
            performance.Bonus,
            granted,
            performance.Grade,
            performance.BonusPercent,
            performance.EngagedUnitCount,
            firstClear,
            newlyUnlocked);
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
