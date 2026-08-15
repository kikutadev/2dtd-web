using DungeonDefense.Application;
using DungeonDefense.Core;
using DungeonDefense.Web;
using Xunit;

namespace DungeonDefense.Web.Tests;

public sealed class DungeonBuildDemoTests
{
    [Fact]
    public void PreviewPlacementWhenTrapTargetsEntranceDoesNotMutateBoard()
    {
        var demo = new DungeonBuildDemo(CreateDefenseContent());

        var preview = demo.PreviewPlacement(DefenseSliceBuildCatalog.SpikeTrap.Id, 0, 3);

        Assert.False(preview.Success);
        Assert.Empty(demo.Board.Traps);
    }

    [Fact]
    public void PlaceAndStartDefenseUsesProductionEditorAndSessionState()
    {
        var demo = new DungeonBuildDemo(CreateDefenseContent());

        var trap = demo.Place(DefenseSliceBuildCatalog.SpikeTrap.Id, 4, 3);
        var guard = demo.Place(DefenseSliceBuildCatalog.SkeletonWarrior.Id, 5, 3);
        var facility = demo.Place(DefenseSliceBuildCatalog.ArrowSlit.Id, 5, 2);

        Assert.True(trap.Success, trap.Error);
        Assert.True(guard.Success, guard.Error);
        Assert.True(facility.Success, facility.Error);
        Assert.True(demo.StartValidation.Success, string.Join(" | ", demo.StartValidation.Errors));

        var simulation = demo.StartDefense(seed: 4242);
        var attempt = demo.Session.AttemptSnapshot;

        Assert.NotNull(attempt);
        Assert.Single(attempt!.Traps);
        Assert.Single(attempt.Guards);
        Assert.Single(attempt.Facilities);
        Assert.Equal(DefenseOutcome.Running, simulation.Outcome);
    }

    [Fact]
    public void DefenseHostUsesEditedSessionAndReturnsToSameBuildState()
    {
        var content = CreateDefenseContent();
        var build = new DungeonBuildDemo(content);
        var placed = build.Place(DefenseSliceBuildCatalog.SpikeTrap.Id, 4, 3);
        Assert.True(placed.Success, placed.Error);
        var placedId = Assert.Single(build.Board.Traps).InstanceId;

        var defense = new DefenseDemo(content, build.Session);
        defense.Start();
        Assert.Equal(placedId, Assert.Single(defense.Board.Traps).InstanceId);

        for (var frame = 0; frame < 2_000 && defense.Simulation?.Outcome == DefenseOutcome.Running; frame++)
            defense.AdvanceFrame(0.05, speed: 3, advanceSimulation: true);

        Assert.NotNull(defense.Simulation);
        Assert.NotEqual(DefenseOutcome.Running, defense.Simulation!.Outcome);

        build.ReturnToBuild();
        Assert.Null(build.Session.ActiveDefense);
        Assert.Equal(placedId, Assert.Single(build.Board.Traps).InstanceId);
    }

    [Fact]
    public void RemoveUsesProductionSemanticCommandAndRestoresSlot()
    {
        var demo = new DungeonBuildDemo(CreateDefenseContent());
        var placed = demo.Place(DefenseSliceBuildCatalog.SpikeTrap.Id, 4, 3);
        Assert.True(placed.Success, placed.Error);
        var instanceId = Assert.Single(demo.Board.Traps).InstanceId;

        var removed = demo.Remove(BuildKind.Trap, instanceId);

        Assert.True(removed.Success, removed.Error);
        Assert.Empty(demo.Board.Traps);
    }

    private static DefenseContent CreateDefenseContent()
    {
        var units = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal)
        {
            [DefenseSliceBuildCatalog.SkeletonWarrior.Id] = new(
                DefenseSliceBuildCatalog.SkeletonWarrior.Id, Team.Dungeon, UnitRole.Fighter,
                MaxHp: 60, Damage: 8, AttackRange: 1, AttackCooldownTicks: 10,
                MoveIntervalTicks: 1, Blocks: true, GuardZoneRadius: 2),
            [DefenseSliceBuildCatalog.SkeletonArcher.Id] = new(
                DefenseSliceBuildCatalog.SkeletonArcher.Id, Team.Dungeon, UnitRole.Ranged,
                MaxHp: 40, Damage: 6, AttackRange: 3, AttackCooldownTicks: 12,
                MoveIntervalTicks: 1, Blocks: false, GuardZoneRadius: 3),
            ["invader.test"] = new(
                "invader.test", Team.Invader, UnitRole.Fighter,
                MaxHp: 25, Damage: 4, AttackRange: 1, AttackCooldownTicks: 12,
                MoveIntervalTicks: 2, Blocks: true, GuardZoneRadius: 0),
        };

        var traps = new Dictionary<string, TrapDefinition>(StringComparer.Ordinal)
        {
            [DefenseSliceBuildCatalog.SpikeTrap.Id] = new(DefenseSliceBuildCatalog.SpikeTrap.Id, Damage: 8, CooldownTicks: 10),
            [DefenseSliceBuildCatalog.PoisonTrap.Id] = new(DefenseSliceBuildCatalog.PoisonTrap.Id, Damage: 3, CooldownTicks: 12, StatusKind.Poison, StatusStrength: 1, StatusDurationTicks: 20),
        };

        var facilities = new Dictionary<string, FacilityDefinition>(StringComparer.Ordinal)
        {
            [DefenseSliceBuildCatalog.ArrowSlit.Id] = new(DefenseSliceBuildCatalog.ArrowSlit.Id, Damage: 5, Range: 3, CooldownTicks: 10),
            [DefenseSliceBuildCatalog.MagicEye.Id] = new(DefenseSliceBuildCatalog.MagicEye.Id, Damage: 4, Range: 4, CooldownTicks: 12),
        };

        return new DefenseContent
        {
            Units = units,
            Traps = traps,
            Facilities = facilities,
            Spells = new Dictionary<string, SpellDefinition>(StringComparer.Ordinal),
            Waves =
            [
                new WaveDefinition("wave.test", 0,
                [
                    new SpawnGroupDefinition("invader.test", Count: 1, InitialDelayTicks: 0, SpawnIntervalTicks: 1),
                ]),
            ],
            CoreMaxHp = 100,
            MaxMp = 100,
            MpChargePerTick = 1,
        };
    }
}
