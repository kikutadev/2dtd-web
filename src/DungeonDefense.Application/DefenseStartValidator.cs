using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record DefenseStartValidationIssue(string FloorId, string Code, string Message);

public sealed record DefenseStartValidationResult(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DefenseStartValidationIssue>? Issues = null);

public static class DefenseStartValidator
{
    public static DefenseStartValidationResult Validate(DungeonState state, DefenseContent content)
        => ValidateFloor(DungeonFloorId.First.Value, state, content);

    public static DefenseStartValidationResult Validate(PlayerDungeonState dungeon, DefenseContent content)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var issues = new List<DefenseStartValidationIssue>();

        var structural = PlayerDungeonValidator.Validate(dungeon.Floors);
        foreach (var issue in structural.Issues)
            issues.Add(new(issue.FloorId ?? string.Empty, issue.Code, issue.Message));

        foreach (var floor in dungeon.Floors)
        {
            var floorResult = ValidateFloor(floor.Id.Value, floor.Board, content);
            if (floorResult.Issues is { } floorIssues) issues.AddRange(floorIssues);
        }

        var unique = issues
            .DistinctBy(x => (x.FloorId, x.Code, x.Message))
            .OrderBy(x => x.FloorId, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToArray();
        return new(unique.Length == 0, unique.Select(x => x.Message).ToArray(), unique);
    }

    private static DefenseStartValidationResult ValidateFloor(string floorId, DungeonState state, DefenseContent content)
    {
        var issues = new List<DefenseStartValidationIssue>();
        void Add(string code, string message) => issues.Add(new(floorId, code, message));

        if (DungeonPathfinder.FindRoute(state).Count == 0) Add("ROUTE_NOT_FOUND", $"[{floorId}] Entrance-to-endpoint route is missing.");
        if (state.UsedCapacity > state.CapacityMax) Add("CAPACITY_EXCEEDED", $"[{floorId}] Dungeon Capacity is exceeded.");
        if (state.Rooms.Any(x => x.Connections is null || x.Connections.Count == 0)) Add("ROOM_CONNECTION_MISSING", $"[{floorId}] A room has no connection ports.");
        foreach (var trap in state.Traps.Where(x => !content.Traps.ContainsKey(x.DefinitionId))) Add("UNKNOWN_TRAP", $"[{floorId}] Unknown trap content: {trap.DefinitionId}");
        foreach (var facility in state.Facilities.Where(x => !content.Facilities.ContainsKey(x.DefinitionId))) Add("UNKNOWN_FACILITY", $"[{floorId}] Unknown facility content: {facility.DefinitionId}");
        foreach (var guard in state.Guards.Where(x => !content.Units.ContainsKey(x.DefinitionId))) Add("UNKNOWN_GUARD", $"[{floorId}] Unknown guard content: {guard.DefinitionId}");
        foreach (var guard in state.Guards.Where(x => content.Units.TryGetValue(x.DefinitionId, out var definition) && definition.Team != Team.Dungeon))
            Add("INVALID_GUARD_TEAM", $"[{floorId}] Guard content is not on the Dungeon team: {guard.DefinitionId}");

        return new(issues.Count == 0, issues.Select(x => x.Message).ToArray(), issues);
    }
}
