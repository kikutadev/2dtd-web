using DungeonDefense.Core;

namespace DungeonDefense.Application;

public enum BuildKind
{
    Room,
    Trap,
    Guard,
    Facility,
}

public sealed record BuildOption(
    string Id,
    string Label,
    BuildKind Kind,
    int CapacityCost,
    int Width = 1,
    int Height = 1,
    int GuardZoneRadius = 0,
    IReadOnlyList<RoomConnection>? Connections = null,
    int GuardHpBonusPercent = 0,
    int GuardDamageBonusPercent = 0,
    int PoisonDurationBonusPercent = 0,
    int ExecuteThresholdPercent = 0,
    int ExecuteDamageBonusPercent = 0,
    int SpellDurationBonusPercent = 0,
    int PushMagnitudeBonus = 0)
{
    public IReadOnlyList<RoomConnection> ResolveRoomConnections(bool rotated)
    {
        var source = Connections ?? [];
        if (!rotated) return source;
        return source.Select(x => new RoomConnection(
            new GridPoint(Height - 1 - x.LocalCell.Y, x.LocalCell.X),
            x.Direction switch
            {
                CardinalDirection.North => CardinalDirection.East,
                CardinalDirection.East => CardinalDirection.South,
                CardinalDirection.South => CardinalDirection.West,
                CardinalDirection.West => CardinalDirection.North,
                _ => x.Direction,
            })).ToArray();
    }
}

public static class DefenseSliceBuildCatalog
{
    public static readonly BuildOption GuardRoom = new(
        "room.guard_2x2",
        "Guard Room",
        BuildKind.Room,
        6,
        2,
        2,
        Connections:
        [
            new RoomConnection(new GridPoint(0, 1), CardinalDirection.West),
            new RoomConnection(new GridPoint(1, 1), CardinalDirection.East),
        ],
        GuardHpBonusPercent: 25,
        GuardDamageBonusPercent: 10);
    public static readonly BuildOption PoisonChamber = new(
        "room.poison_2x2", "Poison Chamber", BuildKind.Room, 6, 2, 2,
        Connections:
        [
            new RoomConnection(new GridPoint(0, 1), CardinalDirection.West),
            new RoomConnection(new GridPoint(1, 1), CardinalDirection.East),
        ],
        PoisonDurationBonusPercent: 60);
    public static readonly BuildOption ExecutionChamber = new(
        "room.execution_2x2", "Execution Chamber", BuildKind.Room, 7, 2, 2,
        Connections:
        [
            new RoomConnection(new GridPoint(0, 1), CardinalDirection.West),
            new RoomConnection(new GridPoint(1, 1), CardinalDirection.East),
        ],
        ExecuteThresholdPercent: 35,
        ExecuteDamageBonusPercent: 40);
    public static readonly BuildOption ManaChamber = new(
        "room.mana_2x2", "Mana Chamber", BuildKind.Room, 7, 2, 2,
        Connections:
        [
            new RoomConnection(new GridPoint(0, 1), CardinalDirection.West),
            new RoomConnection(new GridPoint(1, 1), CardinalDirection.East),
        ],
        SpellDurationBonusPercent: 50,
        PushMagnitudeBonus: 1);
    public static readonly BuildOption SpikeTrap = new("trap.spike", "Spike", BuildKind.Trap, 3);
    public static readonly BuildOption PoisonTrap = new("trap.poison", "Poison", BuildKind.Trap, 4);
    public static readonly BuildOption SkeletonWarrior = new("monster.skeleton_warrior", "Skeleton Warrior", BuildKind.Guard, 6, GuardZoneRadius: 2);
    public static readonly BuildOption SkeletonArcher = new("monster.skeleton_archer", "Skeleton Archer", BuildKind.Guard, 5, GuardZoneRadius: 3);
    public static readonly BuildOption ArrowSlit = new("facility.arrow_slit", "Arrow Slit", BuildKind.Facility, 8);
    public static readonly BuildOption MagicEye = new("facility.magic_eye", "Magic Eye", BuildKind.Facility, 8);

    public static IReadOnlyList<BuildOption> Rooms { get; } = [GuardRoom, PoisonChamber, ExecutionChamber, ManaChamber];
    public static IReadOnlyList<BuildOption> Traps { get; } = [SpikeTrap, PoisonTrap];
    public static IReadOnlyList<BuildOption> Guards { get; } = [SkeletonWarrior, SkeletonArcher];
    public static IReadOnlyList<BuildOption> Facilities { get; } = [ArrowSlit, MagicEye];
}