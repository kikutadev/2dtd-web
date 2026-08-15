namespace DungeonDefense.Core;

public static class DungeonSnapshotMaterializer
{
    public static EditResult Materialize(
        DungeonState profileBase,
        IReadOnlyCollection<GridPoint> passages,
        IReadOnlyCollection<PlacedRoom> rooms,
        IReadOnlyCollection<PlacedTrap> traps,
        IReadOnlyCollection<PlacedGuard> guards,
        IReadOnlyCollection<PlacedFacility> facilities)
    {
        var candidate = profileBase.CreateBlankForConstruction();
        var passageSet = passages.ToHashSet();
        if (passageSet.Count != passages.Count) return EditResult.Failed(profileBase, "Duplicate passage coordinate.");
        foreach (var p in passageSet)
        {
            if (!candidate.InBounds(p)) return EditResult.Failed(profileBase, $"Passage out of bounds: {p}");
            if (!candidate.IsBuildable(p)) return EditResult.Failed(profileBase, $"Passage overlaps locked terrain: {p}");
            if (candidate.IsIngress(p)) continue; // Legacy blueprints may list profile-owned Ingress passages.
            if (p == candidate.Core) return EditResult.Failed(profileBase, $"Passage overlaps immutable cell: {p}");
            candidate.SetTileInternal(p, TileKind.Passage);
        }

        var occupiedRoomCells = new HashSet<GridPoint>();
        foreach (var room in rooms)
        {
            if (room.Width <= 0 || room.Height <= 0) return EditResult.Failed(profileBase, $"Invalid room size: {room.InstanceId}");
            var cells = room.Cells().ToArray();
            foreach (var p in cells)
            {
                if (!candidate.InBounds(p)) return EditResult.Failed(profileBase, $"Room out of bounds: {room.InstanceId} at {p}");
                if (!candidate.IsBuildable(p) || candidate.HasTerrain(p, TerrainFeatureKind.NarrowRock)) return EditResult.Failed(profileBase, $"Room overlaps locked/narrow terrain: {room.InstanceId} at {p}");
                if (candidate.IsIngress(p) || p == candidate.Core || passageSet.Contains(p) || !occupiedRoomCells.Add(p))
                    return EditResult.Failed(profileBase, $"Invalid room footprint: {room.InstanceId} at {p}");
            }
            foreach (var p in cells) candidate.SetTileInternal(p, TileKind.Room);
            candidate.Rooms.Add(room);
        }

        foreach (var room in rooms)
        {
            var roomCells = room.Cells().ToHashSet();
            var touchesExternalWalkable = roomCells.Any(c => GridGeometry.NeighborsNorthEastSouthWest(c)
                .Any(n => candidate.InBounds(n) && !roomCells.Contains(n) && candidate.IsWalkable(n)));
            if (!touchesExternalWalkable) return EditResult.Failed(profileBase, $"Room does not touch an external walkable cell: {room.InstanceId}");
        }

        foreach (var trap in traps)
        {
            if (!candidate.IsWalkable(trap.Position) || candidate.IsIngress(trap.Position) || trap.Position == candidate.Core)
                return EditResult.Failed(profileBase, $"Invalid trap placement: {trap.InstanceId}");
            if (candidate.Traps.Any(x => x.Position == trap.Position)) return EditResult.Failed(profileBase, $"Trap slot occupied: {trap.Position}");
            candidate.Traps.Add(trap);
        }

        foreach (var guard in guards)
        {
            if (!candidate.IsWalkable(guard.Position) || candidate.IsIngress(guard.Position) || guard.Position == candidate.Core
                || candidate.HasTerrain(guard.Position, TerrainFeatureKind.NarrowRock))
                return EditResult.Failed(profileBase, $"Invalid guard placement: {guard.InstanceId}");
            if (candidate.Guards.Any(x => x.Position == guard.Position)) return EditResult.Failed(profileBase, $"Guard slot occupied: {guard.Position}");
            candidate.Guards.Add(guard);
        }

        foreach (var facility in facilities)
        {
            if (!candidate.InBounds(facility.Position) || !candidate.IsBuildable(facility.Position) || candidate.HasTerrain(facility.Position, TerrainFeatureKind.NarrowRock) || candidate.GetTile(facility.Position) != TileKind.Bedrock)
                return EditResult.Failed(profileBase, $"Invalid facility wall cell: {facility.InstanceId}");
            if (!GridGeometry.NeighborsNorthEastSouthWest(facility.Position).Any(candidate.IsWalkable))
                return EditResult.Failed(profileBase, $"Facility does not touch a walkable cell: {facility.InstanceId}");
            if (candidate.Facilities.Any(x => x.Position == facility.Position)) return EditResult.Failed(profileBase, $"Facility slot occupied: {facility.Position}");
            candidate.Facilities.Add(facility);
        }

        var allIds = rooms.Select(x => x.InstanceId)
            .Concat(traps.Select(x => x.InstanceId))
            .Concat(guards.Select(x => x.InstanceId))
            .Concat(facilities.Select(x => x.InstanceId))
            .ToArray();
        var duplicateId = allIds.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateId is not null) return EditResult.Failed(profileBase, $"Duplicate instance ID: {duplicateId.Key}");
        if (candidate.UsedCapacity > candidate.CapacityMax) return EditResult.Failed(profileBase, "Dungeon Capacity exceeded.");
        var route = DungeonPathfinder.FindRoute(candidate);
        if (route.Count == 0) return EditResult.Failed(profileBase, "Blueprint would remove the entrance-to-core route.");
        return new EditResult(true, null, candidate, route);
    }
}
