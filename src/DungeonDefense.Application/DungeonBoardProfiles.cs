using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record DungeonBoardProfile(
    string Id,
    string Label,
    string Description,
    int Width,
    int Height,
    GridPoint Entrance,
    GridPoint Core,
    Func<DungeonState> CreateBase);

public static class DungeonBoardProfiles
{
    public const string DefenseSliceId = "defense.slice.12x7.v1";
    public const string PillaredCryptId = "defense.pillared_crypt.12x7.v1";
    public const string DeepCryptId = "defense.deep_crypt.13x8.v1";
    public const string ManaFaultId = "defense.mana_fault.13x8.v1";

    public static readonly DungeonBoardProfile DefenseSlice = new(
        DefenseSliceId,
        "Open Crypt",
        "Open bedrock around a simple central route. Best for learning unrestricted construction.",
        12, 7,
        new GridPoint(0, 3),
        new GridPoint(11, 3),
        DungeonFactory.CreateDefenseSliceDungeon);

    public static readonly DungeonBoardProfile PillaredCrypt = new(
        PillaredCryptId,
        "Pillared Crypt",
        "Central ancient pillars break long firing lanes while an eastern natural cavern offers free route capacity.",
        12, 7,
        new GridPoint(0, 2),
        new GridPoint(11, 4),
        DungeonFactory.CreatePillaredCryptDungeon);

    public static readonly DungeonBoardProfile DeepCrypt = new(
        DeepCryptId,
        "Deep Crypt",
        "A central bend with reserved room pockets, broken sightlines and an eastern natural cavern. Designed for room-assisted counterplay.",
        13, 8,
        new GridPoint(0, 3),
        new GridPoint(12, 3),
        DungeonFactory.CreateDeepCryptDungeon);

    public static readonly DungeonBoardProfile ManaFault = new(
        ManaFaultId,
        "Mana Fault",
        "A bent route crosses a mana vein and natural cavern; magic positioning matters more than on the open board.",
        13, 8,
        new GridPoint(0, 5),
        new GridPoint(12, 2),
        DungeonFactory.CreateManaFaultDungeon);

    public static IReadOnlyList<DungeonBoardProfile> All { get; } = [DefenseSlice, PillaredCrypt, DeepCrypt, ManaFault];

    public static DungeonBoardProfile Resolve(string id)
        => All.SingleOrDefault(x => x.Id == id) ?? throw new InvalidOperationException($"Unknown board profile: {id}");

    public static DungeonBoardProfile Resolve(DungeonState state)
        => All.SingleOrDefault(x => x.Width == state.Width && x.Height == state.Height && x.Entrance == state.Entrance && x.Core == state.Core)
           ?? throw new InvalidOperationException("No board profile matches the dungeon state.");

    public static bool Matches(DungeonBoardProfile profile, BoardProfileFile file)
        => profile.Id == file.Id
           && profile.Width == file.Width
           && profile.Height == file.Height
           && profile.Entrance == new GridPoint(file.Entrance.X, file.Entrance.Y)
           && profile.Core == new GridPoint(file.Core.X, file.Core.Y);
}
