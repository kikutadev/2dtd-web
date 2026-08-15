using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record DefenseElementPerformance(string InstanceId, int Activations, int Damage, string FloorId = "floor.001");
public sealed record GuardCollapseDetail(string GuardId, int Tick, GridPoint Position, int RouteIndex, string FloorId = "floor.001");

public enum DefenseBreachCauseKind
{
    GuardCollapse,
    FloorBreached,
    CoreReached,
}

public sealed record BreachDetail(int Tick, GridPoint Position, int RouteIndex, string Cause, string FloorId = "floor.001")
{
    public DefenseBreachCauseKind CauseKind { get; init; } = DefenseBreachCauseKind.CoreReached;
    public string? RelatedInstanceId { get; init; }
}

public sealed record DefenseFloorResultSummary(
    string FloorId,
    int Depth,
    int DeepestPathIndex,
    int PassageCount,
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int GuardCollapseCount,
    bool Breached)
{
    // Compatibility alias while existing hosts migrate to Route Pressure terminology.
    public int TrafficCount => PassageCount;
}

public sealed record DefenseResultReport(
    DefenseOutcome Outcome,
    int CoreHp,
    int CoreMaxHp,
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int TrapTriggerCount,
    int FacilityAttackCount,
    int UnusedFacilityCount,
    int GuardCollapseCount,
    int FirstBreachPathIndex,
    int SpellCasts,
    int CoreHitCount,
    int DeepestPathIndex,
    IReadOnlyDictionary<GridPoint, int> RoutePressureHeatmap,
    IReadOnlyList<DefenseElementPerformance> TrapPerformance,
    IReadOnlyList<DefenseElementPerformance> FacilityPerformance,
    IReadOnlyList<GuardCollapseDetail> GuardCollapses,
    BreachDetail? FirstBreach,
    GridPoint? RoutePressureHotspot,
    string Digest)
{
    public IReadOnlyList<DefenseFloorResultSummary> FloorSummaries { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyDictionary<GridPoint, int>> RoutePressureHeatmapsByFloor { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<GridPoint, int>>(StringComparer.Ordinal);
    public int TrafficBlockedTicks { get; init; }
    public int TrafficWaitCount { get; init; }
    public GridPoint? TrafficHotspot { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<GridPoint, int>> TrafficBlockedTicksByFloor { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<GridPoint, int>>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, IReadOnlyDictionary<GridPoint, int>> TrafficWaitCountsByFloor { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<GridPoint, int>>(StringComparer.Ordinal);
    public string? DeepestBreachedFloorId { get; init; }

    // Compatibility views retain Route Pressure semantics.
    public IReadOnlyDictionary<GridPoint, int> TrafficHeatmap => RoutePressureHeatmap;
    public IReadOnlyDictionary<string, IReadOnlyDictionary<GridPoint, int>> TrafficHeatmapsByFloor => RoutePressureHeatmapsByFloor;

    public static DefenseResultReport From(DefenseSimulation simulation)
    {
        var trapEvents = simulation.Events.Where(x => x.Type == DefenseEventType.TrapTriggered && simulation.TrapIds.Contains(x.ActorId)).ToArray();
        var guardAttackEvents = simulation.Events.Where(x => x.Type == DefenseEventType.Attack && simulation.GuardIds.Contains(x.ActorId)).ToArray();
        var facilityAttackEvents = simulation.Events.Where(x => x.Type == DefenseEventType.Attack && simulation.FacilityIds.Contains(x.ActorId)).ToArray();
        var trapPerformance = simulation.TrapIds.OrderBy(x => x, StringComparer.Ordinal)
            .Select(id => Performance(id, trapEvents))
            .ToArray();
        var facilityPerformance = simulation.FacilityIds.OrderBy(x => x, StringComparer.Ordinal)
            .Select(id => Performance(id, facilityAttackEvents))
            .ToArray();
        var guardDeaths = simulation.Events
            .Where(x => x.Type == DefenseEventType.Death && simulation.GuardIds.Contains(x.ActorId) && x.Position is not null)
            .OrderBy(x => x.Tick)
            .ThenBy(x => x.ActorId, StringComparer.Ordinal)
            .ToArray();
        var guardCollapses = guardDeaths
            .Select(x => new GuardCollapseDetail(x.ActorId, x.Tick, x.Position!.Value, NearestRouteIndex(simulation.Routes[x.FloorId], x.Position.Value), x.FloorId))
            .ToArray();
        var firstBreach = ResolveFirstBreach(simulation, guardCollapses);
        var spellCasts = simulation.Events.Count(x => x.Type == DefenseEventType.SpellCast);
        var coreHits = simulation.Events.Count(x => x.Type == DefenseEventType.CoreDamaged);
        var moveEvents = simulation.Events
            .Where(x => x.Type == DefenseEventType.Move && x.Position is not null && !simulation.GuardIds.Contains(x.ActorId))
            .ToArray();
        var deepest = moveEvents.Select(x => x.Amount).DefaultIfEmpty(0).Max();
        var routePressureByFloor = simulation.Routes.Keys
            .OrderBy(x => simulation.FloorDepths[x])
            .ToDictionary(
                floorId => floorId,
                floorId => (IReadOnlyDictionary<GridPoint, int>)moveEvents
                    .Where(x => x.FloorId == floorId)
                    .GroupBy(x => x.Position!.Value)
                    .ToDictionary(x => x.Key, x => x.Count()),
                StringComparer.Ordinal);

        // Compatibility view for one-floor consumers. Multi-floor consumers must use TrafficHeatmapsByFloor.
        var routePressure = routePressureByFloor.Count == 1
            ? new Dictionary<GridPoint, int>(routePressureByFloor.Values.First())
            : moveEvents.GroupBy(x => x.Position!.Value).ToDictionary(x => x.Key, x => x.Count());
        var routePressureHotspot = routePressure.Count == 0
            ? (GridPoint?)null
            : routePressure.OrderByDescending(x => x.Value).ThenBy(x => x.Key.Y).ThenBy(x => x.Key.X).First().Key;

        var trafficBlockedByFloor = simulation.Routes.Keys
            .OrderBy(x => simulation.FloorDepths[x])
            .ToDictionary(
                floorId => floorId,
                floorId => simulation.TrafficBlockedTicksForFloor(floorId),
                StringComparer.Ordinal);
        var trafficWaitsByFloor = simulation.Routes.Keys
            .OrderBy(x => simulation.FloorDepths[x])
            .ToDictionary(
                floorId => floorId,
                floorId => simulation.TrafficWaitCountsForFloor(floorId),
                StringComparer.Ordinal);
        var trafficBlockedTicks = trafficBlockedByFloor.Values.Sum(x => x.Values.Sum());
        var trafficWaitCount = trafficWaitsByFloor.Values.Sum(x => x.Values.Sum());
        var trafficHotspot = trafficBlockedByFloor
            .SelectMany(x => x.Value.Select(cell => (FloorId: x.Key, Position: cell.Key, Ticks: cell.Value)))
            .OrderByDescending(x => x.Ticks)
            .ThenBy(x => simulation.FloorDepths[x.FloorId])
            .ThenBy(x => x.Position.Y)
            .ThenBy(x => x.Position.X)
            .Select(x => (GridPoint?)x.Position)
            .FirstOrDefault();

        var breachedFloors = simulation.Events
            .Where(x => x.Type == DefenseEventType.FloorBreached)
            .Select(x => x.FloorId)
            .Append(simulation.Events.Any(x => x.Type is DefenseEventType.CoreReached or DefenseEventType.CoreDamaged) ? simulation.FloorDepths.MaxBy(x => x.Value).Key : null)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var floorSummaries = simulation.Routes.Keys
            .OrderBy(x => simulation.FloorDepths[x])
            .Select(floorId => new DefenseFloorResultSummary(
                floorId,
                simulation.FloorDepths[floorId],
                moveEvents.Where(x => x.FloorId == floorId).Select(x => x.Amount).DefaultIfEmpty(0).Max(),
                routePressureByFloor[floorId].Values.Sum(),
                trapEvents.Where(x => x.FloorId == floorId).Sum(x => x.Amount),
                guardAttackEvents.Where(x => x.FloorId == floorId).Sum(x => x.Amount),
                facilityAttackEvents.Where(x => x.FloorId == floorId).Sum(x => x.Amount),
                guardCollapses.Count(x => x.FloorId == floorId),
                breachedFloors.Contains(floorId)))
            .ToArray();
        var deepestBreachedFloor = floorSummaries.Where(x => x.Breached).OrderByDescending(x => x.Depth).Select(x => x.FloorId).FirstOrDefault();

        return new DefenseResultReport(
            simulation.Outcome,
            simulation.CoreHp,
            simulation.CoreMaxHp,
            trapEvents.Sum(x => x.Amount),
            guardAttackEvents.Sum(x => x.Amount),
            facilityAttackEvents.Sum(x => x.Amount),
            trapEvents.Length,
            facilityAttackEvents.Length,
            facilityPerformance.Count(x => x.Activations == 0),
            guardCollapses.Length,
            firstBreach?.RouteIndex ?? -1,
            spellCasts,
            coreHits,
            deepest,
            routePressure,
            trapPerformance,
            facilityPerformance,
            guardCollapses,
            firstBreach,
            routePressureHotspot,
            simulation.ResultDigest())
        {
            FloorSummaries = floorSummaries,
            RoutePressureHeatmapsByFloor = routePressureByFloor,
            TrafficBlockedTicks = trafficBlockedTicks,
            TrafficWaitCount = trafficWaitCount,
            TrafficHotspot = trafficHotspot,
            TrafficBlockedTicksByFloor = trafficBlockedByFloor,
            TrafficWaitCountsByFloor = trafficWaitsByFloor,
            DeepestBreachedFloorId = deepestBreachedFloor,
        };
    }

    private static DefenseElementPerformance Performance(string id, IReadOnlyList<DefenseEvent> events)
    {
        var matched = events.Where(x => x.ActorId == id).ToArray();
        var floorId = matched.Select(x => x.FloorId).FirstOrDefault() ?? ResolveFloorIdFromCompositeId(id);
        return new DefenseElementPerformance(id, matched.Length, matched.Sum(x => x.Amount), floorId);
    }

    private static BreachDetail? ResolveFirstBreach(DefenseSimulation simulation, IReadOnlyList<GuardCollapseDetail> guardCollapses)
    {
        foreach (var collapse in guardCollapses.OrderBy(x => x.Tick))
        {
            var crossing = simulation.Events
                .Where(x => x.Type == DefenseEventType.Move
                    && !simulation.GuardIds.Contains(x.ActorId)
                    && x.FloorId == collapse.FloorId
                    && x.Tick >= collapse.Tick
                    && x.Amount > collapse.RouteIndex
                    && x.Position is not null)
                .OrderBy(x => x.Tick)
                .ThenBy(x => x.ActorId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (crossing is not null)
                return new BreachDetail(crossing.Tick, crossing.Position!.Value, crossing.Amount, $"Guard {collapse.GuardId} collapsed", crossing.FloorId)
                {
                    CauseKind = DefenseBreachCauseKind.GuardCollapse,
                    RelatedInstanceId = collapse.GuardId,
                };
        }

        var floorBreach = simulation.Events.FirstOrDefault(x => x.Type == DefenseEventType.FloorBreached);
        if (floorBreach?.Position is { } position)
            return new BreachDetail(floorBreach.Tick, position, Math.Max(0, simulation.Routes[floorBreach.FloorId].Count - 1), "Floor breached", floorBreach.FloorId)
            {
                CauseKind = DefenseBreachCauseKind.FloorBreached,
            };

        var coreHit = simulation.Events.FirstOrDefault(x => x.Type == DefenseEventType.CoreDamaged && x.Position is not null);
        return coreHit is null
            ? null
            : new BreachDetail(coreHit.Tick, coreHit.Position!.Value, Math.Max(0, simulation.Routes[coreHit.FloorId].Count - 1), "Core reached", coreHit.FloorId)
            {
                CauseKind = DefenseBreachCauseKind.CoreReached,
            };
    }

    private static int NearestRouteIndex(IReadOnlyList<GridPoint> route, GridPoint point)
        => route.Select((p, i) => (p, i, distance: p.ManhattanDistance(point)))
            .OrderBy(x => x.distance)
            .ThenBy(x => x.i)
            .Select(x => x.i)
            .DefaultIfEmpty(0)
            .First();

    private static string ResolveFloorIdFromCompositeId(string id)
    {
        var separator = id.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 ? id[..separator] : DungeonFloorId.First.Value;
    }
}
