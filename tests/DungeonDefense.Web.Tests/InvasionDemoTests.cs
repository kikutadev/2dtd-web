using DungeonDefense.Core;
using DungeonDefense.Web;
using Xunit;

namespace DungeonDefense.Web.Tests;

public sealed class InvasionDemoTests
{
    [Fact]
    public void ProductionInvasionContentParsesAndUsesCanonicalFormation()
    {
        var defense = CreateDefenseContent();
        var content = WebInvasionContentLoader.Parse(File.ReadAllText(FindContent("invasion-vertical-slice.json")), defense);
        var demo = new InvasionDemo(defense, content);

        Assert.Equal("location.black_iron_mine", demo.SelectedLocationId);
        Assert.Equal("black_iron.b1", demo.SelectedFloorId);
        Assert.Equal(3, demo.Formation["monster.skeleton_warrior"]);
        Assert.Equal(3, demo.Formation["monster.skeleton_archer"]);
        Assert.Equal(12, demo.UsedDeploymentCapacity);
        Assert.True(demo.CanStart);
    }

    [Fact]
    public void CanonicalBlackIronFlowCompletesThroughProductionSimulation()
    {
        var defense = CreateDefenseContent();
        var content = WebInvasionContentLoader.Parse(File.ReadAllText(FindContent("invasion-vertical-slice.json")), defense);
        var demo = new InvasionDemo(defense, content);
        demo.Start(seed: 4242);
        demo.DeployAllRemaining();

        for (var frame = 0; frame < 5_000 && demo.Simulation?.Outcome == InvasionOutcome.Running; frame++)
        {
            if (demo.Simulation is { Mp: >= 35 } simulation
                && simulation.SpellCooldownRemaining("invasion.spell.ward") == 0)
                _ = demo.CastSupportSpell("invasion.spell.ward");
            demo.AdvanceFrame(0.05, speed: 3, advanceSimulation: true);
        }

        Assert.NotNull(demo.Simulation);
        Assert.Equal(InvasionOutcome.Success, demo.Simulation!.Outcome);
        Assert.Equal(demo.SelectedFloor.Sections.Count - 1, demo.Simulation.SectionIndex);
        Assert.Contains(demo.Simulation.Events, x => x.Type == InvasionEventType.ObjectiveCompleted);
    }

    [Fact]
    public void FormationCannotExceedProductionDeploymentCapacity()
    {
        var defense = CreateDefenseContent();
        var content = WebInvasionContentLoader.Parse(File.ReadAllText(FindContent("invasion-vertical-slice.json")), defense);
        var demo = new InvasionDemo(defense, content);

        var accepted = demo.AdjustFormation("monster.skeleton_warrior", 1);

        Assert.False(accepted);
        Assert.Equal(12, demo.UsedDeploymentCapacity);
    }

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

    private static DefenseContent CreateDefenseContent()
    {
        var units = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal)
        {
            ["monster.skeleton_warrior"] = new("monster.skeleton_warrior", Team.Dungeon, UnitRole.Fighter, 60, 8, 1, 10, 1, true, 2),
            ["monster.skeleton_archer"] = new("monster.skeleton_archer", Team.Dungeon, UnitRole.Ranged, 40, 6, 3, 12, 1, false, 3),
        };
        return new DefenseContent
        {
            Units = units,
            Traps = new Dictionary<string, TrapDefinition>(StringComparer.Ordinal),
            Facilities = new Dictionary<string, FacilityDefinition>(StringComparer.Ordinal),
            Spells = new Dictionary<string, SpellDefinition>(StringComparer.Ordinal),
            Waves = [],
            CoreMaxHp = 100,
            MaxMp = 100,
            MpChargePerTick = 1,
        };
    }
}
