namespace DungeonDefense.Core;

public enum TileKind
{
    Bedrock,
    Passage,
    Room,
    Entrance,
    Core,
}

public sealed record RoomConnection(GridPoint LocalCell, CardinalDirection Direction);

public sealed record DungeonSector(string Id, IReadOnlySet<GridPoint> Cells, bool IsUnlocked);

public enum TerrainFeatureKind
{
    AncientPillar,
    NaturalCavern,
    NarrowRock,
    ManaVein,
    CollapsedArea,
}

public sealed record DungeonTerrainFeature(
    string Id,
    TerrainFeatureKind Kind,
    IReadOnlySet<GridPoint> Cells,
    bool BlocksConstruction);

public sealed record PlacedRoom(
    string InstanceId,
    string DefinitionId,
    GridPoint Origin,
    int Width,
    int Height,
    int CapacityCost,
    IReadOnlyList<RoomConnection>? Connections = null,
    int GuardHpBonusPercent = 0,
    int GuardDamageBonusPercent = 0,
    int PoisonDurationBonusPercent = 0,
    int ExecuteThresholdPercent = 0,
    int ExecuteDamageBonusPercent = 0,
    int SpellDurationBonusPercent = 0,
    int PushMagnitudeBonus = 0)
{
    public IEnumerable<GridPoint> Cells()
    {
        for (var y = Origin.Y; y < Origin.Y + Height; y++)
        for (var x = Origin.X; x < Origin.X + Width; x++)
            yield return new GridPoint(x, y);
    }

    public IReadOnlySet<GridPoint> ConnectionCells()
        => (Connections ?? [])
            .Select(x => new GridPoint(Origin.X + x.LocalCell.X, Origin.Y + x.LocalCell.Y))
            .ToHashSet();

    public bool Contains(GridPoint point)
        => point.X >= Origin.X && point.X < Origin.X + Width && point.Y >= Origin.Y && point.Y < Origin.Y + Height;

    public bool AllowsBoundaryCrossing(GridPoint roomCell, GridPoint outsideCell)
    {
        if (!Contains(roomCell) || Contains(outsideCell) || !GridGeometry.AreOrthogonallyAdjacent(roomCell, outsideCell)) return false;
        var local = new GridPoint(roomCell.X - Origin.X, roomCell.Y - Origin.Y);
        var direction = GridGeometry.DirectionFromTo(roomCell, outsideCell);
        return (Connections ?? []).Any(x => x.LocalCell == local && x.Direction == direction);
    }
}

public sealed record PlacedTrap(string InstanceId, string DefinitionId, GridPoint Position, int CapacityCost);
public sealed record PlacedFacility(string InstanceId, string DefinitionId, GridPoint Position, int CapacityCost);
public sealed record PlacedGuard(
    string InstanceId,
    string DefinitionId,
    GridPoint Position,
    int CapacityCost,
    int GuardZoneRadius,
    string? GuardZoneRoomInstanceId = null);

public sealed class DungeonState
{
    private readonly TileKind[,] _tiles;
    private readonly Dictionary<string, DungeonSector> _sectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DungeonTerrainFeature> _terrainFeatures = new(StringComparer.Ordinal);

    public DungeonState(int width, int height, GridPoint entrance, GridPoint core, int capacityMax)
    {
        if (width < 3 || height < 3) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Entrance = entrance;
        Core = core;
        CapacityMax = capacityMax;
        _tiles = new TileKind[width, height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            _tiles[x, y] = TileKind.Bedrock;
        SetTileInternal(entrance, TileKind.Entrance);
        SetTileInternal(core, TileKind.Core);
    }

    private DungeonState(DungeonState source)
    {
        Width = source.Width;
        Height = source.Height;
        Entrance = source.Entrance;
        Core = source.Core;
        CapacityMax = source.CapacityMax;
        _tiles = (TileKind[,])source._tiles.Clone();
        foreach (var (id, sector) in source._sectors)
            _sectors[id] = sector with { Cells = sector.Cells.ToHashSet() };
        foreach (var (id, feature) in source._terrainFeatures)
            _terrainFeatures[id] = feature with { Cells = feature.Cells.ToHashSet() };
        Rooms.AddRange(source.Rooms);
        Traps.AddRange(source.Traps);
        Facilities.AddRange(source.Facilities);
        Guards.AddRange(source.Guards);
    }

    public int Width { get; }
    public int Height { get; }
    public GridPoint Entrance { get; }
    public GridPoint Core { get; }
    public int CapacityMax { get; }
    public IReadOnlyCollection<DungeonSector> Sectors => _sectors.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    public IReadOnlyCollection<DungeonTerrainFeature> TerrainFeatures => _terrainFeatures.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    public List<PlacedRoom> Rooms { get; } = [];
    public List<PlacedTrap> Traps { get; } = [];
    public List<PlacedFacility> Facilities { get; } = [];
    public List<PlacedGuard> Guards { get; } = [];

    public int UsedCapacity
    {
        get
        {
            var passage = 0;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (_tiles[x, y] == TileKind.Passage && !HasTerrain(new GridPoint(x, y), TerrainFeatureKind.NaturalCavern)) passage++;
            return passage + Rooms.Sum(x => x.CapacityCost) + Traps.Sum(x => x.CapacityCost)
                + Facilities.Sum(x => x.CapacityCost) + Guards.Sum(x => x.CapacityCost);
        }
    }

    public DungeonState Clone() => new(this);

    /// <summary>Creates an exact construction clone while replacing only the per-floor Capacity ceiling.</summary>
    public DungeonState WithCapacityMax(int capacityMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityMax);
        var clone = new DungeonState(Width, Height, Entrance, Core, capacityMax);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            clone._tiles[x, y] = _tiles[x, y];
        foreach (var (id, sector) in _sectors) clone._sectors[id] = sector with { Cells = sector.Cells.ToHashSet() };
        foreach (var (id, feature) in _terrainFeatures) clone._terrainFeatures[id] = feature with { Cells = feature.Cells.ToHashSet() };
        clone.Rooms.AddRange(Rooms);
        clone.Traps.AddRange(Traps);
        clone.Facilities.AddRange(Facilities);
        clone.Guards.AddRange(Guards);
        return clone;
    }
    public bool InBounds(GridPoint point) => point.X >= 0 && point.X < Width && point.Y >= 0 && point.Y < Height;
    public TileKind GetTile(GridPoint point) => InBounds(point) ? _tiles[point.X, point.Y] : TileKind.Bedrock;
    public bool IsWalkable(GridPoint point) => InBounds(point) && GetTile(point) is TileKind.Passage or TileKind.Room or TileKind.Entrance or TileKind.Core;
    public bool IsBuildable(GridPoint point)
    {
        if (!InBounds(point)) return false;
        var sector = _sectors.Values.FirstOrDefault(x => x.Cells.Contains(point));
        if (sector is not null && !sector.IsUnlocked) return false;
        return !_terrainFeatures.Values.Any(x => x.BlocksConstruction && x.Cells.Contains(point));
    }

    public PlacedRoom? RoomAt(GridPoint point) => Rooms.SingleOrDefault(x => x.Contains(point));
    public bool HasTerrain(GridPoint point, TerrainFeatureKind kind) => _terrainFeatures.Values.Any(x => x.Kind == kind && x.Cells.Contains(point));
    public IReadOnlyList<DungeonTerrainFeature> TerrainAt(GridPoint point) => _terrainFeatures.Values.Where(x => x.Cells.Contains(point)).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

    public bool CanTraverse(GridPoint from, GridPoint to)
    {
        if (!IsWalkable(from) || !IsWalkable(to) || !GridGeometry.AreOrthogonallyAdjacent(from, to)) return false;
        var fromRoom = RoomAt(from);
        var toRoom = RoomAt(to);
        if (ReferenceEquals(fromRoom, toRoom) || (fromRoom is null && toRoom is null)) return true;
        if (fromRoom is not null && toRoom is null) return fromRoom.AllowsBoundaryCrossing(from, to);
        if (fromRoom is null && toRoom is not null) return toRoom.AllowsBoundaryCrossing(to, from);
        return false;
    }

    public void DefineTerrainFeature(string id, TerrainFeatureKind kind, IEnumerable<GridPoint> cells, bool blocksConstruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var cellSet = cells.ToHashSet();
        if (cellSet.Any(x => !InBounds(x) || x == Entrance || x == Core)) throw new ArgumentOutOfRangeException(nameof(cells));
        _terrainFeatures[id] = new DungeonTerrainFeature(id, kind, cellSet, blocksConstruction);
    }

    internal DungeonState CreateBlankForConstruction()
    {
        var blank = new DungeonState(Width, Height, Entrance, Core, CapacityMax);
        foreach (var (id, sector) in _sectors) blank._sectors[id] = sector with { Cells = sector.Cells.ToHashSet() };
        foreach (var (id, feature) in _terrainFeatures) blank._terrainFeatures[id] = feature with { Cells = feature.Cells.ToHashSet() };
        foreach (var feature in blank._terrainFeatures.Values.Where(x => x.Kind == TerrainFeatureKind.NaturalCavern))
            foreach (var point in feature.Cells) blank.SetTileInternal(point, TileKind.Passage);
        return blank;
    }

    public void DefineSector(string id, IEnumerable<GridPoint> cells, bool isUnlocked)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Sector id is required.", nameof(id));
        var set = cells.ToHashSet();
        if (set.Count == 0 || set.Any(x => !InBounds(x))) throw new ArgumentException("Sector cells must be non-empty and in bounds.", nameof(cells));
        if (_sectors.Values.Any(x => x.Cells.Overlaps(set))) throw new InvalidOperationException("Sector cells must not overlap.");
        _sectors[id] = new DungeonSector(id, set, isUnlocked);
    }

    public void SetSectorUnlocked(string id, bool unlocked)
    {
        if (!_sectors.TryGetValue(id, out var sector)) throw new InvalidOperationException($"Unknown sector: {id}");
        _sectors[id] = sector with { IsUnlocked = unlocked };
    }

    internal void SetTileInternal(GridPoint point, TileKind kind)
    {
        if (!InBounds(point)) throw new ArgumentOutOfRangeException(nameof(point));
        _tiles[point.X, point.Y] = kind;
    }

    public string ToAscii(IReadOnlyCollection<GridPoint>? route = null)
    {
        var routeSet = route is null ? null : new HashSet<GridPoint>(route);
        var chars = new List<string>(Height);
        for (var y = 0; y < Height; y++)
        {
            var row = new char[Width];
            for (var x = 0; x < Width; x++)
            {
                var p = new GridPoint(x, y);
                row[x] = GetTile(p) switch
                {
                    TileKind.Bedrock when !IsBuildable(p) => 'X',
                    TileKind.Bedrock => '#',
                    TileKind.Passage => '.',
                    TileKind.Room => 'r',
                    TileKind.Entrance => 'E',
                    TileKind.Core => 'C',
                    _ => '?',
                };
                if (routeSet?.Contains(p) == true && row[x] == '.') row[x] = '*';
            }
            foreach (var trap in Traps.Where(t => t.Position.Y == y)) row[trap.Position.X] = '^';
            foreach (var guard in Guards.Where(g => g.Position.Y == y)) row[guard.Position.X] = 'G';
            foreach (var facility in Facilities.Where(f => f.Position.Y == y)) row[facility.Position.X] = 'F';
            chars.Add(new string(row));
        }
        return string.Join(Environment.NewLine, chars);
    }
}