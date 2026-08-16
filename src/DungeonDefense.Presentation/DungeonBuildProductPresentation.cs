using System.Collections.Immutable;
using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral product state for dungeon preparation. The editor command service remains the rule authority;
/// this presenter only derives what the player should see about the current build and catalog.
/// </summary>
public static class DungeonBuildProductPresentation
{
    public static DungeonBuildVisualState Build(
        DungeonState board,
        DefenseContent content,
        IEnumerable<BuildOption> options,
        DefenseStartValidationResult? validation = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        var analysis = DungeonBuildAnalyzer.Analyze(board, content);
        var optionStates = options
            .Select(option => new DungeonBuildOptionVisualState(
                option.Id,
                option.Kind,
                option.CapacityCost,
                option.Width,
                option.Height,
                option.GuardZoneRadius,
                ProductAssetIdentity.BuildItem(option.Id)))
            .ToImmutableArray();
        var placementCount = board.Rooms.Count + board.Traps.Count + board.Guards.Count + board.Facilities.Count;

        return new DungeonBuildVisualState(
            board.UsedCapacity,
            board.CapacityMax,
            placementCount,
            analysis.RouteLength,
            analysis.FirstDefenseContactPathIndex,
            analysis.TrapContactCount,
            analysis.GuardCoveredRouteCells,
            analysis.FacilityCoveredRouteCells,
            analysis.LongestFacilityFireLane,
            analysis.StructuralUnusedFacilityCount,
            validation?.Success ?? true,
            validation?.Errors.ToImmutableArray() ?? ImmutableArray<string>.Empty,
            optionStates);
    }
}

public sealed record DungeonBuildVisualState(
    int UsedCapacity,
    int CapacityMax,
    int PlacementCount,
    int RouteLength,
    int FirstDefenseContactPathIndex,
    int TrapContactCount,
    int GuardCoveredRouteCells,
    int FacilityCoveredRouteCells,
    int LongestFacilityFireLane,
    int StructuralUnusedFacilityCount,
    bool CanStartDefense,
    ImmutableArray<string> StartErrors,
    ImmutableArray<DungeonBuildOptionVisualState> Options);

public sealed record DungeonBuildOptionVisualState(
    string Id,
    BuildKind Kind,
    int CapacityCost,
    int Width,
    int Height,
    int GuardZoneRadius,
    ProductAssetRef? Asset);
