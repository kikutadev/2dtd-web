namespace DungeonDefense.Core;

/// <summary>
/// Stable identity for one player-dungeon floor. Display labels such as B1F are intentionally kept separate.
/// </summary>
public readonly record struct DungeonFloorId(string Value)
{
    public static readonly DungeonFloorId First = new("floor.001");

    public override string ToString() => Value;

    public static DungeonFloorId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Floor id is required.", nameof(value));
        return new DungeonFloorId(value);
    }
}

public enum FloorEndpointKind
{
    DescentGate,
    DungeonCore,
}

/// <summary>
/// Persistent state for one independently editable floor.
/// DungeonState.Core is treated as the floor endpoint coordinate during the compatibility migration.
/// </summary>
public sealed class DungeonFloorState
{
    public DungeonFloorState(
        DungeonFloorId id,
        int depth,
        string boardProfileId,
        FloorEndpointKind endpointKind,
        DungeonState board)
    {
        if (string.IsNullOrWhiteSpace(id.Value)) throw new ArgumentException("Floor id is required.", nameof(id));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        if (string.IsNullOrWhiteSpace(boardProfileId)) throw new ArgumentException("Board profile id is required.", nameof(boardProfileId));
        ArgumentNullException.ThrowIfNull(board);

        Id = id;
        Depth = depth;
        BoardProfileId = boardProfileId;
        EndpointKind = endpointKind;
        Board = board.Clone();
    }

    public DungeonFloorId Id { get; }
    public int Depth { get; }
    public string BoardProfileId { get; }
    public FloorEndpointKind EndpointKind { get; }
    public DungeonState Board { get; }
    public GridPoint Entrance => Board.Entrance;
    public GridPoint Endpoint => Board.Core;
    public int CapacityMax => Board.CapacityMax;
    public int UsedCapacity => Board.UsedCapacity;

    public DungeonFloorState Clone() => new(Id, Depth, BoardProfileId, EndpointKind, Board);

    public DungeonFloorState WithBoard(DungeonState board) => new(Id, Depth, BoardProfileId, EndpointKind, board);
    public DungeonFloorState WithEndpointKind(FloorEndpointKind endpointKind) => new(Id, Depth, BoardProfileId, endpointKind, Board);
}

public sealed record PlayerDungeonValidationIssue(string Code, string? FloorId, string Message);
public sealed record PlayerDungeonValidationResult(bool Success, IReadOnlyList<PlayerDungeonValidationIssue> Issues);

/// <summary>
/// Top-level persistent model for the active player dungeon. Floor ordering is determined solely by Depth.
/// </summary>
public sealed class PlayerDungeonState
{
    private readonly DungeonFloorState[] _floors;

    public PlayerDungeonState(string dungeonId, IEnumerable<DungeonFloorState> floors)
    {
        if (string.IsNullOrWhiteSpace(dungeonId)) throw new ArgumentException("Dungeon id is required.", nameof(dungeonId));
        ArgumentNullException.ThrowIfNull(floors);

        DungeonId = dungeonId;
        _floors = floors.Select(x => x.Clone()).OrderBy(x => x.Depth).ToArray();
        var validation = PlayerDungeonValidator.Validate(_floors);
        if (!validation.Success)
            throw new InvalidOperationException($"Invalid player dungeon: {string.Join(" | ", validation.Issues.Select(x => x.Message))}");
    }

    public string DungeonId { get; }
    public IReadOnlyList<DungeonFloorState> Floors => _floors.Select(x => x.Clone()).ToArray();
    public int FloorCount => _floors.Length;
    public DungeonFloorId CurrentDeepestFloorId => _floors[^1].Id;
    public DungeonFloorState DeepestFloor => _floors[^1].Clone();

    public static PlayerDungeonState FromSingleFloor(DungeonState board, string boardProfileId, string dungeonId = "player.dungeon.active")
        => new(dungeonId, [new DungeonFloorState(DungeonFloorId.First, 1, boardProfileId, FloorEndpointKind.DungeonCore, board)]);

    public DungeonFloorState GetFloor(DungeonFloorId id)
        => _floors.SingleOrDefault(x => x.Id == id)?.Clone()
           ?? throw new InvalidOperationException($"Unknown floor: {id}");

    public DungeonFloorState GetFloor(string id) => GetFloor(DungeonFloorId.Parse(id));

    public PlayerDungeonState ReplaceFloorBoard(DungeonFloorId id, DungeonState board)
        => new(DungeonId, _floors.Select(x => x.Id == id ? x.WithBoard(board) : x));

    /// <summary>
    /// Atomically adds a new deepest floor and turns the previous core endpoint into a descent gate.
    /// </summary>
    public PlayerDungeonState UnlockFloor(DungeonFloorId newFloorId, string boardProfileId, DungeonState board)
    {
        if (_floors.Any(x => x.Id == newFloorId)) throw new InvalidOperationException($"Floor id already exists: {newFloorId}");
        var nextDepth = _floors.Length + 1;
        var next = _floors
            .Select(x => x.Depth == _floors.Length ? x.WithEndpointKind(FloorEndpointKind.DescentGate) : x.Clone())
            .Append(new DungeonFloorState(newFloorId, nextDepth, boardProfileId, FloorEndpointKind.DungeonCore, board))
            .ToArray();
        return new PlayerDungeonState(DungeonId, next);
    }

    public PlayerDungeonState Clone() => new(DungeonId, _floors);
}

public static class PlayerDungeonValidator
{
    public static PlayerDungeonValidationResult Validate(IEnumerable<DungeonFloorState> floors)
    {
        ArgumentNullException.ThrowIfNull(floors);
        var ordered = floors.OrderBy(x => x.Depth).ToArray();
        var issues = new List<PlayerDungeonValidationIssue>();

        if (ordered.Length == 0)
        {
            issues.Add(new("NO_FLOORS", null, "Player dungeon must contain at least one floor."));
            return new(false, issues);
        }

        foreach (var duplicate in ordered.GroupBy(x => x.Id).Where(x => x.Count() > 1))
            issues.Add(new("DUPLICATE_FLOOR_ID", duplicate.Key.Value, $"Duplicate floor id: {duplicate.Key}"));

        foreach (var duplicate in ordered.GroupBy(x => x.Depth).Where(x => x.Count() > 1))
            issues.Add(new("DUPLICATE_DEPTH", null, $"Duplicate floor depth: {duplicate.Key}"));

        for (var i = 0; i < ordered.Length; i++)
        {
            var floor = ordered[i];
            var expectedDepth = i + 1;
            if (floor.Depth != expectedDepth)
                issues.Add(new("DEPTH_GAP", floor.Id.Value, $"Floor {floor.Id} has depth {floor.Depth}; expected {expectedDepth}."));

            var expectedEndpoint = i == ordered.Length - 1 ? FloorEndpointKind.DungeonCore : FloorEndpointKind.DescentGate;
            if (floor.EndpointKind != expectedEndpoint)
                issues.Add(new("INVALID_ENDPOINT_KIND", floor.Id.Value, $"Floor {floor.Id} must use endpoint {expectedEndpoint}."));

            if (DungeonPathfinder.FindRoute(floor.Board).Count == 0)
                issues.Add(new("ROUTE_NOT_FOUND", floor.Id.Value, $"Floor {floor.Id} has no entrance-to-endpoint route."));
        }

        return new(issues.Count == 0, issues);
    }
}
