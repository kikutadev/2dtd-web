using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public static class DeepCryptBuildAdaptationPlanner
{
    public static int CapacityTargetForDay(int day)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(day);
        if (day <= 5) return 64;
        if (day <= 10) return 68;
        if (day <= 15) return 72;
        if (day <= 20) return 76;
        if (day <= 25) return 80;
        return 84;
    }

    public static IReadOnlyList<string> EnsureSignatureRooms(
        DefenseGameSession session,
        DefenseContent content,
        JourneyBuildArchetype archetype,
        int day)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(day);

        var desired = archetype switch
        {
            JourneyBuildArchetype.TrapHeavy => new[] { DefenseSliceBuildCatalog.PoisonChamber, DefenseSliceBuildCatalog.ManaChamber },
            JourneyBuildArchetype.GuardHeavy => new[] { DefenseSliceBuildCatalog.GuardRoom, DefenseSliceBuildCatalog.ExecutionChamber },
            JourneyBuildArchetype.FacilityHeavy => new[] { DefenseSliceBuildCatalog.ExecutionChamber, DefenseSliceBuildCatalog.ManaChamber },
            _ => new[] { DefenseSliceBuildCatalog.ManaChamber, DefenseSliceBuildCatalog.ExecutionChamber },
        };

        var actions = new List<string>();
        foreach (var room in desired)
        {
            if (session.Editor.Current.Rooms.Any(x => string.Equals(x.DefinitionId, room.Id, StringComparison.Ordinal))) continue;
            if (TryPlaceRouteRoom(session, content, room, day, out var action)) actions.Add(action!);
        }
        return actions;
    }

    public static IReadOnlyList<string> EnsureCapacityTarget(
        DefenseGameSession session,
        DefenseContent content,
        JourneyBuildArchetype archetype,
        int day)
    {
        var target = CapacityTargetForDay(day);
        return JourneyBuildPlanner.EnsureCapacityTarget(session, content, archetype, day, target);
    }

    private static bool TryPlaceRouteRoom(
        DefenseGameSession session,
        DefenseContent content,
        BuildOption room,
        int day,
        out string? action)
    {
        var currentRouteLength = DungeonPathfinder.FindRoute(session.Editor.Current).Count;
        var instanceId = $"DC-R-{day:D2}-{room.Id.Replace('.', '-')}";
        var candidates = new List<(PlaceRoomCommand Command, SemanticEditResult Preview, DungeonBuildAnalysis Analysis, int RouteDelta)>();
        for (var y = 0; y < session.Editor.Current.Height; y++)
        for (var x = 0; x < session.Editor.Current.Width; x++)
        for (var rotation = 0; rotation < 2; rotation++)
        {
            var command = new PlaceRoomCommand(instanceId, room.Id, x, y, rotation == 1);
            var preview = session.EditorCommands.Preview(command);
            if (!preview.Success) continue;
            var analysis = DungeonBuildAnalyzer.Analyze(preview.State, content);
            if (analysis.RoomRouteCells <= 0) continue;
            candidates.Add((command, preview, analysis, preview.Route.Count - currentRouteLength));
        }

        var chosen = candidates
            .OrderByDescending(x => x.Analysis.RoomRouteCells)
            .ThenBy(x => Math.Abs(x.RouteDelta))
            .ThenByDescending(x => x.Analysis.GuardCoveredRouteCells + x.Analysis.FacilityCoveredRouteCells)
            .ThenBy(x => x.Command.Y)
            .ThenBy(x => x.Command.X)
            .ThenBy(x => x.Command.Rotated)
            .FirstOrDefault();
        if (chosen.Command is null)
        {
            action = null;
            return false;
        }

        var applied = session.EditorCommands.Execute(chosen.Command);
        if (!applied.Success)
        {
            action = null;
            return false;
        }
        action = $"{room.Id}@{chosen.Command.X},{chosen.Command.Y}{(chosen.Command.Rotated ? ":R" : string.Empty)}";
        return true;
    }
}
