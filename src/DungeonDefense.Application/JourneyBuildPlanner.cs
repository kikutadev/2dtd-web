using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public enum JourneyBuildArchetype
{
    Balanced,
    TrapHeavy,
    GuardHeavy,
    FacilityHeavy,
}

public static class JourneyBuildPlanner
{
    public static int CapacityTargetForDay(int day)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(day);
        if (day <= 5) return 40;
        if (day <= 10) return 44;
        if (day <= 15) return 48;
        if (day <= 20) return 52;
        if (day <= 25) return 56;
        return 60;
    }

    public static IReadOnlyList<string> EnsureCapacityTarget(
        DefenseGameSession session,
        DefenseContent content,
        JourneyBuildArchetype archetype,
        int day)
        => EnsureCapacityTarget(session, content, archetype, day, CapacityTargetForDay(day));

    public static IReadOnlyList<string> EnsureCapacityTarget(
        DefenseGameSession session,
        DefenseContent content,
        JourneyBuildArchetype archetype,
        int day,
        int targetCapacity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCapacity);
        var target = Math.Min(session.Editor.Current.CapacityMax, targetCapacity);
        var actions = new List<string>();
        var attempts = 0;
        while (session.Editor.Current.UsedCapacity < target && attempts++ < 64)
        {
            var kind = SelectNextKind(archetype, session.Editor.Current);
            if (TryAdd(session, content, kind, day, attempts, out var action))
            {
                actions.Add(action!);
                continue;
            }

            // A category can exhaust legal placements on a small board. Preserve the archetype,
            // but allow a deterministic support fallback rather than leaving unequal total investment.
            var fallback = archetype switch
            {
                JourneyBuildArchetype.TrapHeavy => new[] { BuildKind.Guard, BuildKind.Facility },
                JourneyBuildArchetype.GuardHeavy => new[] { BuildKind.Trap, BuildKind.Facility },
                JourneyBuildArchetype.FacilityHeavy => new[] { BuildKind.Guard, BuildKind.Trap },
                _ => new[] { BuildKind.Trap, BuildKind.Guard, BuildKind.Facility },
            };
            var added = false;
            foreach (var support in fallback)
            {
                if (!TryAdd(session, content, support, day, attempts, out action)) continue;
                actions.Add(action!);
                added = true;
                break;
            }
            if (!added) break;
        }
        return actions;
    }

    private static BuildKind SelectNextKind(JourneyBuildArchetype archetype, DungeonState state)
    {
        if (archetype == JourneyBuildArchetype.TrapHeavy) return BuildKind.Trap;
        if (archetype == JourneyBuildArchetype.GuardHeavy) return BuildKind.Guard;
        if (archetype == JourneyBuildArchetype.FacilityHeavy) return BuildKind.Facility;

        var trapCapacity = state.Traps.Sum(x => x.CapacityCost);
        var guardCapacity = state.Guards.Sum(x => x.CapacityCost);
        var facilityCapacity = state.Facilities.Sum(x => x.CapacityCost);
        return new[]
            {
                (Kind: BuildKind.Trap, Capacity: trapCapacity, Tie: 0),
                (Kind: BuildKind.Guard, Capacity: guardCapacity, Tie: 1),
                (Kind: BuildKind.Facility, Capacity: facilityCapacity, Tie: 2),
            }
            .OrderBy(x => x.Capacity)
            .ThenBy(x => x.Tie)
            .First().Kind;
    }

    private static bool TryAdd(
        DefenseGameSession session,
        DefenseContent content,
        BuildKind kind,
        int day,
        int sequence,
        out string? action)
    {
        return kind switch
        {
            BuildKind.Trap => TryAddTrap(session, day, sequence, out action),
            BuildKind.Guard => TryAddGuard(session, content, day, sequence, out action),
            BuildKind.Facility => TryAddFacility(session, content, day, sequence, out action),
            _ => Fail(out action),
        };
    }

    private static bool TryAddTrap(DefenseGameSession session, int day, int sequence, out string? action)
    {
        var state = session.Editor.Current;
        var route = DungeonPathfinder.FindRoute(state);
        var routeIndex = route.Select((point, index) => (point, index)).ToDictionary(x => x.point, x => x.index);
        var existing = state.Traps.Where(x => routeIndex.ContainsKey(x.Position)).Select(x => routeIndex[x.Position]).ToArray();
        var definitions = sequence % 2 == 0
            ? new[] { DefenseSliceBuildCatalog.PoisonTrap, DefenseSliceBuildCatalog.SpikeTrap }
            : new[] { DefenseSliceBuildCatalog.SpikeTrap, DefenseSliceBuildCatalog.PoisonTrap };

        foreach (var definition in definitions)
        {
            var instanceId = $"J-T-{day:D2}-{sequence:D2}";
            var candidates = route
                .Select((point, index) => (point, index))
                .Where(x => x.index > 0 && x.index < route.Count - 1)
                .Where(x => !state.Traps.Any(t => t.Position == x.point))
                .Select(x =>
                {
                    var command = new PlaceTrapCommand(instanceId, definition.Id, x.point.X, x.point.Y);
                    var preview = session.EditorCommands.Preview(command);
                    var separation = existing.Length == 0 ? route.Count : existing.Min(i => Math.Abs(i - x.index));
                    return (command, preview, separation, center: Math.Abs(x.index - route.Count / 2));
                })
                .Where(x => x.preview.Success)
                .OrderByDescending(x => x.separation)
                .ThenBy(x => x.center)
                .ThenBy(x => x.command.Y)
                .ThenBy(x => x.command.X)
                .ToArray();
            if (candidates.Length == 0) continue;
            var chosen = candidates[0].command;
            var applied = session.EditorCommands.Execute(chosen);
            if (!applied.Success) continue;
            action = $"{definition.Id}@{chosen.X},{chosen.Y}";
            return true;
        }
        return Fail(out action);
    }

    private static bool TryAddGuard(DefenseGameSession session, DefenseContent content, int day, int sequence, out string? action)
    {
        var state = session.Editor.Current;
        var route = DungeonPathfinder.FindRoute(state);
        var definitions = sequence % 2 == 0
            ? new[] { DefenseSliceBuildCatalog.SkeletonArcher, DefenseSliceBuildCatalog.SkeletonWarrior }
            : new[] { DefenseSliceBuildCatalog.SkeletonWarrior, DefenseSliceBuildCatalog.SkeletonArcher };
        foreach (var definition in definitions)
        {
            var instanceId = $"J-G-{day:D2}-{sequence:D2}";
            var candidates = route
                .Select((point, index) => (point, index))
                .Where(x => x.index > 0 && x.index < route.Count - 1)
                .Where(x => !state.Guards.Any(g => g.Position == x.point))
                .Select(x =>
                {
                    var command = new PlaceGuardCommand(instanceId, definition.Id, x.point.X, x.point.Y);
                    var preview = session.EditorCommands.Preview(command);
                    var coverage = preview.Success ? DungeonBuildAnalyzer.Analyze(preview.State, content).GuardCoveredRouteCells : -1;
                    return (command, preview, coverage, x.index);
                })
                .Where(x => x.preview.Success)
                .OrderByDescending(x => x.coverage)
                .ThenByDescending(x => x.index)
                .ThenBy(x => x.command.Y)
                .ThenBy(x => x.command.X)
                .ToArray();
            if (candidates.Length == 0) continue;
            var chosen = candidates[0].command;
            var applied = session.EditorCommands.Execute(chosen);
            if (!applied.Success) continue;
            action = $"{definition.Id}@{chosen.X},{chosen.Y}";
            return true;
        }
        return Fail(out action);
    }

    private static bool TryAddFacility(DefenseGameSession session, DefenseContent content, int day, int sequence, out string? action)
    {
        var state = session.Editor.Current;
        var definitions = sequence % 2 == 0
            ? new[] { DefenseSliceBuildCatalog.MagicEye, DefenseSliceBuildCatalog.ArrowSlit }
            : new[] { DefenseSliceBuildCatalog.ArrowSlit, DefenseSliceBuildCatalog.MagicEye };
        foreach (var definition in definitions)
        {
            var instanceId = $"J-F-{day:D2}-{sequence:D2}";
            var candidates = AllCells(state)
                .Where(point => !state.Facilities.Any(f => f.Position == point))
                .Select(point =>
                {
                    var command = new PlaceFacilityCommand(instanceId, definition.Id, point.X, point.Y);
                    var preview = session.EditorCommands.Preview(command);
                    var analysis = preview.Success ? DungeonBuildAnalyzer.Analyze(preview.State, content) : null;
                    return (command, preview, covered: analysis?.FacilityCoveredRouteCells ?? -1, lane: analysis?.LongestFacilityFireLane ?? -1, unused: analysis?.StructuralUnusedFacilityCount ?? int.MaxValue);
                })
                .Where(x => x.preview.Success && x.unused == 0)
                .OrderByDescending(x => x.covered)
                .ThenByDescending(x => x.lane)
                .ThenBy(x => x.command.Y)
                .ThenBy(x => x.command.X)
                .ToArray();
            if (candidates.Length == 0) continue;
            var chosen = candidates[0].command;
            var applied = session.EditorCommands.Execute(chosen);
            if (!applied.Success) continue;
            action = $"{definition.Id}@{chosen.X},{chosen.Y}";
            return true;
        }
        return Fail(out action);
    }

    private static IEnumerable<GridPoint> AllCells(DungeonState state)
    {
        for (var y = 0; y < state.Height; y++)
        for (var x = 0; x < state.Width; x++)
            yield return new GridPoint(x, y);
    }

    private static bool Fail(out string? action)
    {
        action = null;
        return false;
    }
}
