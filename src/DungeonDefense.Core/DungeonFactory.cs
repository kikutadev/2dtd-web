namespace DungeonDefense.Core;

public static class DungeonFactory
{
    public static DungeonState CreateDefenseSliceDungeon()
    {
        var state = new DungeonState(12, 7, new GridPoint(0, 3), new GridPoint(11, 3), 160);
        for (var x = 1; x <= 10; x++) state.SetTileInternal(new GridPoint(x, 3), TileKind.Passage);
        return state;
    }

    public static DungeonState CreatePillaredCryptDungeon()
    {
        var state = new DungeonState(12, 7, new GridPoint(0, 2), new GridPoint(11, 4), 160);
        foreach (var point in new[]
        {
            new GridPoint(1,2), new GridPoint(2,2), new GridPoint(3,2), new GridPoint(4,2),
            new GridPoint(4,3), new GridPoint(4,4), new GridPoint(5,4), new GridPoint(6,4),
            new GridPoint(7,4), new GridPoint(8,4), new GridPoint(9,4), new GridPoint(10,4),
        }) state.SetTileInternal(point, TileKind.Passage);

        state.DefineTerrainFeature("pillar.center", TerrainFeatureKind.AncientPillar,
            [new GridPoint(6,2), new GridPoint(7,2), new GridPoint(6,3), new GridPoint(7,3)], true);
        state.DefineTerrainFeature("collapse.southwest", TerrainFeatureKind.CollapsedArea,
            [new GridPoint(2,5), new GridPoint(3,5)], true);
        state.DefineTerrainFeature("narrow.neck", TerrainFeatureKind.NarrowRock,
            [new GridPoint(3,3), new GridPoint(5,3)], false);
        var cavern = new[] { new GridPoint(8,4), new GridPoint(9,4), new GridPoint(8,5), new GridPoint(9,5) };
        state.DefineTerrainFeature("cavern.east", TerrainFeatureKind.NaturalCavern, cavern, false);
        foreach (var point in cavern) state.SetTileInternal(point, TileKind.Passage);
        return state;
    }

    public static DungeonState CreateDeepCryptDungeon()
    {
        var state = new DungeonState(13, 8, new GridPoint(0, 3), new GridPoint(12, 3), 160);
        foreach (var point in new[]
        {
            new GridPoint(1,3), new GridPoint(2,3),
            new GridPoint(2,4), new GridPoint(3,4), new GridPoint(4,4), new GridPoint(5,4), new GridPoint(5,3),
            new GridPoint(6,3),
            new GridPoint(6,4), new GridPoint(7,4), new GridPoint(8,4), new GridPoint(9,4), new GridPoint(9,3),
            new GridPoint(10,3), new GridPoint(11,3),
        }) state.SetTileInternal(point, TileKind.Passage);

        state.DefineTerrainFeature("pillar.north", TerrainFeatureKind.AncientPillar,
            [new GridPoint(5,1), new GridPoint(5,2)], true);
        state.DefineTerrainFeature("pillar.south", TerrainFeatureKind.AncientPillar,
            [new GridPoint(10,5), new GridPoint(10,6)], true);
        state.DefineTerrainFeature("collapse.west", TerrainFeatureKind.CollapsedArea,
            [new GridPoint(1,5), new GridPoint(2,5)], true);
        state.DefineTerrainFeature("narrow.route", TerrainFeatureKind.NarrowRock,
            [new GridPoint(2,3), new GridPoint(5,3), new GridPoint(6,3), new GridPoint(9,3), new GridPoint(10,3)], false);
        var cavern = new[] { new GridPoint(10,3), new GridPoint(11,3), new GridPoint(10,4), new GridPoint(11,4) };
        state.DefineTerrainFeature("cavern.east", TerrainFeatureKind.NaturalCavern, cavern, false);
        foreach (var point in cavern) state.SetTileInternal(point, TileKind.Passage);
        return state;
    }

    public static DungeonState CreateManaFaultDungeon()
    {
        var state = new DungeonState(13, 8, new GridPoint(0, 5), new GridPoint(12, 2), 160);
        foreach (var point in new[]
        {
            new GridPoint(1,5), new GridPoint(2,5), new GridPoint(3,5), new GridPoint(4,5), new GridPoint(5,5),
            new GridPoint(5,4), new GridPoint(5,3), new GridPoint(5,2), new GridPoint(6,2), new GridPoint(7,2),
            new GridPoint(8,2), new GridPoint(9,2), new GridPoint(10,2), new GridPoint(11,2),
        }) state.SetTileInternal(point, TileKind.Passage);

        state.DefineTerrainFeature("mana.fault", TerrainFeatureKind.ManaVein,
            [new GridPoint(5,4), new GridPoint(5,3), new GridPoint(5,2), new GridPoint(6,2)], false);
        state.DefineTerrainFeature("pillar.northwest", TerrainFeatureKind.AncientPillar,
            [new GridPoint(2,2), new GridPoint(2,3), new GridPoint(3,2)], true);
        state.DefineTerrainFeature("collapse.center", TerrainFeatureKind.CollapsedArea,
            [new GridPoint(7,4), new GridPoint(8,4), new GridPoint(7,5)], true);
        state.DefineTerrainFeature("narrow.south", TerrainFeatureKind.NarrowRock,
            [new GridPoint(3,4), new GridPoint(4,4)], false);
        var cavern = new[] { new GridPoint(9,2), new GridPoint(10,2), new GridPoint(9,3), new GridPoint(10,3) };
        state.DefineTerrainFeature("cavern.northeast", TerrainFeatureKind.NaturalCavern, cavern, false);
        foreach (var point in cavern) state.SetTileInternal(point, TileKind.Passage);
        return state;
    }
}
