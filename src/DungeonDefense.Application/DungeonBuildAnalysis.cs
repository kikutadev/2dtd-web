using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record DungeonBuildAnalysis(
    int RouteLength,
    int FirstDefenseContactPathIndex,
    int TrapContactCount,
    int GuardCoveredRouteCells,
    int FacilityCoveredRouteCells,
    int LongestFacilityFireLane,
    int StructuralUnusedFacilityCount,
    int RoomRouteCells,
    int NaturalCavernRouteCells,
    int ManaVeinRouteCells)
{
    public string CompactSummary()
    {
        var contact = FirstDefenseContactPathIndex < 0 ? "none" : FirstDefenseContactPathIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"Contact {contact} • Trap {TrapContactCount} • Guard {GuardCoveredRouteCells}/{RouteLength} • Facility {FacilityCoveredRouteCells}/{RouteLength} • Lane {LongestFacilityFireLane}";
    }
}

public static class DungeonBuildAnalyzer
{
    public static DungeonBuildAnalysis Analyze(DungeonState state, DefenseContent content)
    {
        var route = DungeonPathfinder.FindRoute(state);
        if (route.Count == 0) return new DungeonBuildAnalysis(0, -1, 0, 0, 0, 0, state.Facilities.Count, 0, 0, 0);
        var index = route.Select((point, i) => (point, i)).ToDictionary(x => x.point, x => x.i);
        var trapContacts = state.Traps.Count(x => index.ContainsKey(x.Position));

        var guardCovered = new HashSet<GridPoint>();
        foreach (var guard in state.Guards)
        {
            var zone = GuardZone.Resolve(state, guard);
            foreach (var point in route.Where(zone.Contains)) guardCovered.Add(point);
        }

        var facilityCovered = new HashSet<GridPoint>();
        var longestLane = 0;
        var unusedFacilities = 0;
        foreach (var facility in state.Facilities)
        {
            if (!content.Facilities.TryGetValue(facility.DefinitionId, out var definition)) continue;
            var coveredIndices = route.Select((point, i) => (point, i))
                .Where(x => x.point.ManhattanDistance(facility.Position) <= definition.Range)
                .Where(x => DungeonLineOfSight.HasLineOfSight(state, facility.Position, x.point))
                .Select(x => x.i)
                .ToArray();
            if (coveredIndices.Length == 0)
            {
                unusedFacilities++;
                continue;
            }
            foreach (var i in coveredIndices) facilityCovered.Add(route[i]);
            longestLane = Math.Max(longestLane, LongestContiguousRun(coveredIndices));
        }

        var contactCandidates = new List<int>();
        contactCandidates.AddRange(state.Traps.Where(x => index.TryGetValue(x.Position, out _)).Select(x => index[x.Position]));
        contactCandidates.AddRange(guardCovered.Select(x => index[x]));
        contactCandidates.AddRange(facilityCovered.Select(x => index[x]));
        var firstContact = contactCandidates.Count == 0 ? -1 : contactCandidates.Min();

        return new DungeonBuildAnalysis(
            route.Count,
            firstContact,
            trapContacts,
            guardCovered.Count,
            facilityCovered.Count,
            longestLane,
            unusedFacilities,
            route.Count(x => state.RoomAt(x) is not null),
            route.Count(x => state.HasTerrain(x, TerrainFeatureKind.NaturalCavern)),
            route.Count(x => state.HasTerrain(x, TerrainFeatureKind.ManaVein)));
    }

    private static int LongestContiguousRun(IEnumerable<int> indices)
    {
        var ordered = indices.Distinct().OrderBy(x => x).ToArray();
        if (ordered.Length == 0) return 0;
        var longest = 1;
        var current = 1;
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i] == ordered[i - 1] + 1) current++;
            else current = 1;
            longest = Math.Max(longest, current);
        }
        return longest;
    }
}
