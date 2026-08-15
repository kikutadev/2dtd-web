namespace DungeonDefense.Core;

public sealed record EditResult(bool Success, string? Error, DungeonState State, IReadOnlyList<GridPoint> Route)
{
    public static EditResult Failed(DungeonState original, string error) => new(false, error, original, DungeonPathfinder.FindRoute(original));
}

public static class DungeonPathfinder
{
    public static IReadOnlyList<GridPoint> FindRoute(DungeonState state)
    {
        var queue = new Queue<GridPoint>();
        var cameFrom = new Dictionary<GridPoint, GridPoint?>();
        queue.Enqueue(state.Entrance);
        cameFrom[state.Entrance] = null;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == state.Core) break;
            foreach (var next in GridGeometry.NeighborsNorthEastSouthWest(current))
            {
                if (!state.InBounds(next) || !state.CanTraverse(current, next) || cameFrom.ContainsKey(next)) continue;
                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(state.Core)) return [];
        var route = new List<GridPoint>();
        GridPoint? cursor = state.Core;
        while (cursor is { } p)
        {
            route.Add(p);
            cursor = cameFrom[p];
        }
        route.Reverse();
        return route;
    }
}

public static class DungeonLineOfSight
{
    public static bool HasLineOfSight(DungeonState state, GridPoint from, GridPoint to)
    {
        var line = GridGeometry.SupercoverLine(from, to);
        for (var i = 0; i < line.Count; i++)
        {
            var cell = line[i];
            if (cell != from && cell != to && state.GetTile(cell) == TileKind.Bedrock) return false;
            if (i == 0) continue;
            var previous = line[i - 1];
            if (!GridGeometry.AreOrthogonallyAdjacent(previous, cell)) continue;
            var fromRoom = state.RoomAt(previous);
            var toRoom = state.RoomAt(cell);
            if (fromRoom is null && toRoom is null) continue;
            if (fromRoom is not null && ReferenceEquals(fromRoom, toRoom)) continue;
            if (fromRoom is not null && toRoom is null && !fromRoom.AllowsBoundaryCrossing(previous, cell)) return false;
            if (fromRoom is null && toRoom is not null && !toRoom.AllowsBoundaryCrossing(cell, previous)) return false;
            if (fromRoom is not null && toRoom is not null && !ReferenceEquals(fromRoom, toRoom)) return false;
        }
        return true;
    }
}

public static class GuardZone
{
    public static IReadOnlySet<GridPoint> Resolve(DungeonState state, PlacedGuard guard)
    {
        if (guard.GuardZoneRoomInstanceId is { } roomId)
        {
            var room = state.Rooms.SingleOrDefault(x => x.InstanceId == roomId);
            if (room is not null) return room.Cells().Where(state.IsWalkable).ToHashSet();
        }
        return Manhattan(state, guard.Position, guard.GuardZoneRadius);
    }

    public static IReadOnlySet<GridPoint> Manhattan(DungeonState state, GridPoint origin, int radius)
    {
        var cells = new HashSet<GridPoint>();
        for (var y = 0; y < state.Height; y++)
        for (var x = 0; x < state.Width; x++)
        {
            var p = new GridPoint(x, y);
            if (state.IsWalkable(p) && origin.ManhattanDistance(p) <= radius) cells.Add(p);
        }
        return cells;
    }
}

public static class DungeonEditorRules
{
    public static EditResult Dig(DungeonState state, IReadOnlyList<GridPoint> cells)
    {
        if (cells.Count == 0) return EditResult.Failed(state, "No cells supplied.");
        for (var i = 0; i < cells.Count; i++)
        {
            var p = cells[i];
            if (!state.InBounds(p)) return EditResult.Failed(state, $"Out of bounds: {p}");
            if (!state.IsBuildable(p)) return EditResult.Failed(state, $"Sector is locked: {p}");
            if (state.GetTile(p) != TileKind.Bedrock) return EditResult.Failed(state, $"Not bedrock: {p}");
            if (i > 0 && !GridGeometry.AreOrthogonallyAdjacent(cells[i - 1], p)) return EditResult.Failed(state, "Dig path must be orthogonally contiguous.");
        }

        var next = state.Clone();
        foreach (var cell in cells) next.SetTileInternal(cell, TileKind.Passage);
        if (next.UsedCapacity > next.CapacityMax) return EditResult.Failed(state, "Dungeon Capacity exceeded.");
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult Close(DungeonState state, IReadOnlyList<GridPoint> cells)
    {
        if (cells.Count == 0) return EditResult.Failed(state, "No cells supplied.");
        foreach (var p in cells)
        {
            if (state.GetTile(p) != TileKind.Passage) return EditResult.Failed(state, $"Only passage can be closed: {p}");
            if (state.HasTerrain(p, TerrainFeatureKind.NaturalCavern)) return EditResult.Failed(state, $"Natural cavern cannot be closed: {p}");
            if (state.Traps.Any(x => x.Position == p) || state.Guards.Any(x => x.Position == p))
                return EditResult.Failed(state, $"Occupied passage cannot be closed: {p}");
        }
        var next = state.Clone();
        foreach (var cell in cells) next.SetTileInternal(cell, TileKind.Bedrock);
        var route = DungeonPathfinder.FindRoute(next);
        if (route.Count == 0) return EditResult.Failed(state, "Edit would remove the entrance-to-core route.");
        return new(true, null, next, route);
    }

    public static EditResult PlaceRoom(DungeonState state, string instanceId, string definitionId, GridPoint origin, int width, int height, int capacityCost, IReadOnlyList<RoomConnection>? connections = null, int guardHpBonusPercent = 0, int guardDamageBonusPercent = 0, int poisonDurationBonusPercent = 0, int executeThresholdPercent = 0, int executeDamageBonusPercent = 0, int spellDurationBonusPercent = 0, int pushMagnitudeBonus = 0)
    {
        if (width <= 0 || height <= 0) return EditResult.Failed(state, "Invalid room size.");
        var ports = connections ?? [];
        if (ports.Count == 0) return EditResult.Failed(state, "Room template requires connection ports.");
        var cells = new List<GridPoint>();
        for (var y = origin.Y; y < origin.Y + height; y++)
        for (var x = origin.X; x < origin.X + width; x++)
        {
            var p = new GridPoint(x, y);
            if (!state.InBounds(p) || !state.IsBuildable(p) || state.HasTerrain(p, TerrainFeatureKind.NarrowRock) || state.GetTile(p) != TileKind.Bedrock) return EditResult.Failed(state, $"Room footprint invalid or terrain-locked at {p}.");
            cells.Add(p);
        }
        if (ports.Any(x => x.LocalCell.X < 0 || x.LocalCell.X >= width || x.LocalCell.Y < 0 || x.LocalCell.Y >= height))
            return EditResult.Failed(state, "Room connection port is outside the footprint.");
        var room = new PlacedRoom(instanceId, definitionId, origin, width, height, capacityCost, ports, guardHpBonusPercent, guardDamageBonusPercent, poisonDurationBonusPercent, executeThresholdPercent, executeDamageBonusPercent, spellDurationBonusPercent, pushMagnitudeBonus);
        var touchesWalkableThroughPort = ports.Any(port =>
        {
            var roomCell = new GridPoint(origin.X + port.LocalCell.X, origin.Y + port.LocalCell.Y);
            var outside = GridGeometry.Neighbor(roomCell, port.Direction);
            return state.InBounds(outside) && state.IsWalkable(outside);
        });
        if (!touchesWalkableThroughPort) return EditResult.Failed(state, "Room must connect to an existing walkable cell through a defined port.");

        var next = state.Clone();
        foreach (var p in cells) next.SetTileInternal(p, TileKind.Room);
        next.Rooms.Add(room);
        if (next.UsedCapacity > next.CapacityMax) return EditResult.Failed(state, "Dungeon Capacity exceeded.");
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult RemoveRoom(DungeonState state, string instanceId)
    {
        var room = state.Rooms.SingleOrDefault(x => x.InstanceId == instanceId);
        if (room is null) return EditResult.Failed(state, "Room not found.");
        if (state.Traps.Any(t => room.Cells().Contains(t.Position)) || state.Guards.Any(g => room.Cells().Contains(g.Position)))
            return EditResult.Failed(state, "Room contains placed defense elements.");
        var next = state.Clone();
        var target = next.Rooms.Single(x => x.InstanceId == instanceId);
        next.Rooms.Remove(target);
        foreach (var p in room.Cells()) next.SetTileInternal(p, TileKind.Bedrock);
        var route = DungeonPathfinder.FindRoute(next);
        if (route.Count == 0) return EditResult.Failed(state, "Removing room would remove the entrance-to-core route.");
        return new(true, null, next, route);
    }

    public static EditResult PlaceTrap(DungeonState state, string instanceId, string definitionId, GridPoint position, int capacityCost)
    {
        if (!state.IsWalkable(position) || position == state.Entrance || position == state.Core) return EditResult.Failed(state, "Trap requires a normal walkable cell.");
        if (state.Traps.Any(x => x.Position == position)) return EditResult.Failed(state, "Trap slot occupied.");
        var next = state.Clone();
        next.Traps.Add(new PlacedTrap(instanceId, definitionId, position, capacityCost));
        if (next.UsedCapacity > next.CapacityMax) return EditResult.Failed(state, "Dungeon Capacity exceeded.");
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult RemoveTrap(DungeonState state, string instanceId)
    {
        var next = state.Clone();
        var item = next.Traps.SingleOrDefault(x => x.InstanceId == instanceId);
        if (item is null) return EditResult.Failed(state, "Trap not found.");
        next.Traps.Remove(item);
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult PlaceGuard(DungeonState state, string instanceId, string definitionId, GridPoint position, int capacityCost, int guardZoneRadius)
    {
        if (!state.IsWalkable(position) || position == state.Entrance || position == state.Core) return EditResult.Failed(state, "Guard requires a normal walkable cell.");
        if (state.HasTerrain(position, TerrainFeatureKind.NarrowRock)) return EditResult.Failed(state, "Guard cannot hold a narrow-rock cell.");
        if (state.Guards.Any(x => x.Position == position)) return EditResult.Failed(state, "Guard slot occupied.");
        var next = state.Clone();
        next.Guards.Add(new PlacedGuard(instanceId, definitionId, position, capacityCost, guardZoneRadius, state.RoomAt(position)?.InstanceId));
        if (next.UsedCapacity > next.CapacityMax) return EditResult.Failed(state, "Dungeon Capacity exceeded.");
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult RemoveGuard(DungeonState state, string instanceId)
    {
        var next = state.Clone();
        var item = next.Guards.SingleOrDefault(x => x.InstanceId == instanceId);
        if (item is null) return EditResult.Failed(state, "Guard not found.");
        next.Guards.Remove(item);
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult PlaceFacility(DungeonState state, string instanceId, string definitionId, GridPoint position, int capacityCost)
    {
        if (!state.InBounds(position) || !state.IsBuildable(position) || state.GetTile(position) != TileKind.Bedrock) return EditResult.Failed(state, "Facility requires a buildable bedrock/wall cell.");
        if (!GridGeometry.NeighborsNorthEastSouthWest(position).Any(state.IsWalkable)) return EditResult.Failed(state, "Facility wall must touch a walkable cell.");
        if (state.Facilities.Any(x => x.Position == position)) return EditResult.Failed(state, "Facility slot occupied.");
        var next = state.Clone();
        next.Facilities.Add(new PlacedFacility(instanceId, definitionId, position, capacityCost));
        if (next.UsedCapacity > next.CapacityMax) return EditResult.Failed(state, "Dungeon Capacity exceeded.");
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }

    public static EditResult RemoveFacility(DungeonState state, string instanceId)
    {
        var next = state.Clone();
        var item = next.Facilities.SingleOrDefault(x => x.InstanceId == instanceId);
        if (item is null) return EditResult.Failed(state, "Facility not found.");
        next.Facilities.Remove(item);
        return new(true, null, next, DungeonPathfinder.FindRoute(next));
    }
}
