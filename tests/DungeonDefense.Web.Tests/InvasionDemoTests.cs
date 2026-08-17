using DungeonDefense.Core;
using DungeonDefense.Infrastructure;
using DungeonDefense.Web;
using Xunit;

namespace DungeonDefense.Web.Tests;

public sealed class InvasionDemoTests
{
    [Fact]
    public void BrowserJsonTransportBuildsProductionSpatialMapsAndCanonicalFormation()
    {
        var defense = LoadDefenseContent();
        var content = LoadInvasionContent(defense);
        var demo = new InvasionDemo(defense, content);

        Assert.Equal(12, content.Locations.Sum(x => x.Floors.Count));
        Assert.All(content.Locations.SelectMany(x => x.Floors), floor => Assert.Equal(3, floor.Board.Rooms.Count));
        Assert.Equal("location.black_iron_mine", demo.SelectedLocationId);
        Assert.Equal("black_iron.b1", demo.SelectedFloorId);
        Assert.Equal(InvasionMapDigest.Compute(demo.SelectedFloor), demo.SelectedScoutReport.Map.MapDigest);
        Assert.Equal(3, demo.SelectedScoutReport.Map.Rooms.Count);
        Assert.Equal(3, demo.Formation["monster.skeleton_warrior"]);
        Assert.Equal(3, demo.Formation["monster.skeleton_archer"]);
        Assert.Equal(12, demo.UsedDeploymentCapacity);
        Assert.True(demo.CanStart);
    }

    [Fact]
    public void SpatialBattleExposesRealActorsAndSharedMotionThenCompletes()
    {
        var defense = LoadDefenseContent();
        var content = LoadInvasionContent(defense);
        var demo = new InvasionDemo(defense, content);
        demo.Start(seed: 4242);

        var initial = Assert.IsType<DungeonDefense.Presentation.InvasionBattleVisualState>(demo.VisualState);
        Assert.Equal(3, initial.Rooms.Length);
        Assert.NotEmpty(initial.EnemyGuards);
        Assert.Contains(initial.StaticActors, x => x.Kind == InvasionActorKind.Trap);
        Assert.Contains(initial.StaticActors, x => x.Kind == InvasionActorKind.Facility);
        Assert.Equal(demo.SelectedScoutReport.Map.MapDigest, InvasionMapDigest.Compute(demo.SelectedFloor));

        Assert.True(demo.Deploy("monster.skeleton_warrior", 2));
        for (var frame = 0; frame < 40; frame++) demo.AdvanceFrame(0.05, speed: 1, advanceSimulation: true);
        Assert.NotEmpty(demo.CombatVisualState.Units);
        Assert.Contains(demo.CombatVisualState.Units, x => x.Team == Team.Dungeon);

        demo.DeployAllRemaining();
        for (var frame = 0; frame < 8_000 && demo.Simulation?.Outcome == InvasionOutcome.Running; frame++)
        {
            var state = demo.VisualState!;
            if (state.Ward.Enabled) _ = demo.CastSupportSpell("invasion.spell.ward");
            if (state.Mend.Enabled) _ = demo.CastSupportSpell("invasion.spell.mend");
            demo.AdvanceFrame(0.05, speed: 3, advanceSimulation: true);
        }

        Assert.NotNull(demo.Simulation);
        Assert.Equal(InvasionOutcome.Success, demo.Simulation!.Outcome);
        Assert.Equal(demo.SelectedFloor.Sections.Count, demo.Simulation.ClearedSectionIds.Count);
        Assert.Contains(demo.Simulation.Events, x => x.Type == InvasionEventType.ObjectiveCompleted);
    }

    [Fact]
    public void FormationCannotExceedProductionDeploymentCapacity()
    {
        var defense = LoadDefenseContent();
        var content = LoadInvasionContent(defense);
        var demo = new InvasionDemo(defense, content);

        var accepted = demo.AdjustFormation("monster.skeleton_warrior", 1);

        Assert.False(accepted);
        Assert.Equal(12, demo.UsedDeploymentCapacity);
    }

    private static InvasionContent LoadInvasionContent(DefenseContent defense)
        => WebInvasionContentLoader.Parse(
            File.ReadAllText(FindContent("invasion-vertical-slice.json")),
            File.ReadAllText(FindContent("invasion-maps.json")),
            defense);

    private static DefenseContent LoadDefenseContent()
        => VerticalSliceContentLoader.Load(FindContent("vertical-slice.json"));

    private static string FindContent(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DungeonDefense.Web", "wwwroot", "content", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(fileName);
    }
}
