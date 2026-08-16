using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral identity of promoted production art. Hosts resolve this identity to their own path/texture mechanism;
/// gameplay and product presentation code never embed Godot resource paths or browser URLs.
/// </summary>
public enum ProductAssetCategory
{
    Tile,
    Core,
    Gate,
    Trap,
    Facility,
    Unit,
    Icon,
    Effect,
    Prop,
    Invasion,
    UiChrome,
}

public readonly record struct ProductAssetRef(ProductAssetCategory Category, string Id, string? Variant = null);

/// <summary>
/// Single definition-to-art mapping shared by Godot and Web. The physical path remains host-specific.
/// </summary>
public static class ProductAssetIdentity
{
    public static ProductAssetRef Tile(TileKind kind) => kind switch
    {
        TileKind.Bedrock => new(ProductAssetCategory.Tile, "TS-02"),
        TileKind.Passage or TileKind.Room => new(ProductAssetCategory.Tile, "TS-01"),
        TileKind.Entrance => new(ProductAssetCategory.Tile, "TS-04"),
        TileKind.Core => new(ProductAssetCategory.Tile, "TS-05"),
        _ => new(ProductAssetCategory.Tile, "TS-01"),
    };

    public static ProductAssetRef Core(bool damaged)
        => new(ProductAssetCategory.Core, damaged ? "CO-02" : "CO-01");

    public static ProductAssetRef DescentGate() => new(ProductAssetCategory.Gate, "DG-01");

    public static ProductAssetRef? Trap(string definitionId) => definitionId switch
    {
        "trap.spike" => new(ProductAssetCategory.Trap, "TR-01"),
        "trap.blade" => new(ProductAssetCategory.Trap, "TR-02"),
        "trap.poison" => new(ProductAssetCategory.Trap, "TR-03"),
        "trap.rune" => new(ProductAssetCategory.Trap, "TR-04"),
        "trap.web" => new(ProductAssetCategory.Trap, "TR-05"),
        _ => null,
    };

    public static ProductAssetRef? Facility(string definitionId) => definitionId switch
    {
        "facility.arrow_slit" => new(ProductAssetCategory.Facility, "DF-01"),
        "facility.magic_eye" => new(ProductAssetCategory.Facility, "DF-02"),
        "facility.flame_vent" => new(ProductAssetCategory.Facility, "DF-03"),
        "facility.ritual_altar" => new(ProductAssetCategory.Facility, "DF-04"),
        "facility.gargoyle" => new(ProductAssetCategory.Facility, "DF-05"),
        _ => null,
    };

    public static ProductAssetRef? UnitCanonical(string definitionId) => definitionId switch
    {
        "monster.skeleton_warrior" => new(ProductAssetCategory.Unit, "MN-01"),
        "monster.skeleton_archer" => new(ProductAssetCategory.Unit, "MN-02"),
        "monster.goblin" => new(ProductAssetCategory.Unit, "MN-03"),
        "monster.slime" => new(ProductAssetCategory.Unit, "MN-04"),
        "monster.spider" => new(ProductAssetCategory.Unit, "MN-05"),
        "monster.necromancer" => new(ProductAssetCategory.Unit, "MN-06"),
        "human.warrior" => new(ProductAssetCategory.Unit, "HU-01"),
        "human.archer" => new(ProductAssetCategory.Unit, "HU-02"),
        "human.priest" => new(ProductAssetCategory.Unit, "HU-03"),
        "human.knight" => new(ProductAssetCategory.Unit, "HU-04"),
        "human.mage" => new(ProductAssetCategory.Unit, "HU-05"),
        "human.scout" => new(ProductAssetCategory.Unit, "HU-06"),
        "human.crossbowman" => new(ProductAssetCategory.Unit, "HU-07"),
        "human.berserker" => new(ProductAssetCategory.Unit, "HU-08"),
        "human.high_priest" => new(ProductAssetCategory.Unit, "HU-09"),
        "human.hero" => new(ProductAssetCategory.Unit, "BS-01"),
        _ => null,
    };

    public static ProductAssetRef? Unit(string definitionId, UnitFacing facing)
        => UnitCanonical(definitionId) is { } canonical
            ? canonical with { Variant = CombatMotionPresentation.FacingSuffix(facing) }
            : null;

    /// <summary>Returns the promoted art identity for a build catalog item when one exists.</summary>
    public static ProductAssetRef? BuildItem(string definitionId)
        => Trap(definitionId) ?? Facility(definitionId) ?? UnitCanonical(definitionId);

    public static ProductAssetRef InvasionFortification()
        => new(ProductAssetCategory.Invasion, "IV-01");

    /// <summary>Primary nine-slice frame for world/gameplay surfaces.</summary>
    public static ProductAssetRef UiWorldFrame()
        => new(ProductAssetCategory.UiChrome, "UIC-01");

    /// <summary>Lower-emphasis nine-slice frame for HUD and analysis surfaces.</summary>
    public static ProductAssetRef UiInfoFrame()
        => new(ProductAssetCategory.UiChrome, "UIC-02");

    /// <summary>Stretchable backing for primary/selected actions; text remains host-native.</summary>
    public static ProductAssetRef UiActionBacking()
        => new(ProductAssetCategory.UiChrome, "UIC-03");

    /// <summary>Decorative section divider shared by Godot and Web.</summary>
    public static ProductAssetRef UiDivider()
        => new(ProductAssetCategory.UiChrome, "UIC-04");
}
