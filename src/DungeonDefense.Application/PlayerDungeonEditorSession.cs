using DungeonDefense.Core;

namespace DungeonDefense.Application;

/// <summary>
/// Owns one independent DungeonEditorSession per floor so Undo/Redo history never leaks across floors.
/// </summary>
public sealed class PlayerDungeonEditorSession
{
    private sealed class FloorEntry
    {
        public required DungeonFloorId Id { get; init; }
        public required int Depth { get; init; }
        public required string BoardProfileId { get; init; }
        public required FloorEndpointKind EndpointKind { get; set; }
        public required DungeonEditorSession Editor { get; init; }
    }

    private readonly List<FloorEntry> _floors;

    public PlayerDungeonEditorSession(PlayerDungeonState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        DungeonId = initial.DungeonId;
        _floors = initial.Floors.Select(x => new FloorEntry
        {
            Id = x.Id,
            Depth = x.Depth,
            BoardProfileId = x.BoardProfileId,
            EndpointKind = x.EndpointKind,
            Editor = new DungeonEditorSession(x.Board),
        }).OrderBy(x => x.Depth).ToList();
        SelectedFloorId = _floors[0].Id;
    }

    public string DungeonId { get; }
    public DungeonFloorId SelectedFloorId { get; private set; }
    public IReadOnlyList<DungeonFloorId> FloorIds => _floors.OrderBy(x => x.Depth).Select(x => x.Id).ToArray();
    public DungeonEditorSession SelectedEditor => GetEditor(SelectedFloorId);

    public PlayerDungeonState Current => new(DungeonId, _floors.OrderBy(x => x.Depth).Select(x => new DungeonFloorState(
        x.Id, x.Depth, x.BoardProfileId, x.EndpointKind, x.Editor.Current)));

    public DungeonEditorSession GetEditor(DungeonFloorId id)
        => _floors.SingleOrDefault(x => x.Id == id)?.Editor
           ?? throw new InvalidOperationException($"Unknown floor: {id}");

    public DungeonEditorSession GetEditor(string id) => GetEditor(DungeonFloorId.Parse(id));

    public void SelectFloor(DungeonFloorId id)
    {
        _ = GetEditor(id);
        SelectedFloorId = id;
    }

    /// <summary>
    /// Validates the entire resulting dungeon before mutating editor/session metadata.
    /// </summary>
    public void UnlockFloor(DungeonFloorId id, string boardProfileId, DungeonState board)
    {
        var candidate = Current.UnlockFloor(id, boardProfileId, board);
        var priorDeepest = _floors.Single(x => x.Depth == _floors.Count);
        priorDeepest.EndpointKind = FloorEndpointKind.DescentGate;
        var added = candidate.GetFloor(id);
        _floors.Add(new FloorEntry
        {
            Id = added.Id,
            Depth = added.Depth,
            BoardProfileId = added.BoardProfileId,
            EndpointKind = added.EndpointKind,
            Editor = new DungeonEditorSession(added.Board),
        });
        SelectedFloorId = id;
    }
}
