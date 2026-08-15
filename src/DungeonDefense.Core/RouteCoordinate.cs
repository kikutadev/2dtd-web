namespace DungeonDefense.Core;

/// <summary>
/// Deterministic fixed-point coordinate along the selected dungeon route.
/// Zero is the Entrance cell center; one cell is <see cref="UnitsPerCell"/> route units.
/// </summary>
public readonly record struct RouteProgress(long Units)
{
    public const long UnitsPerCell = 1024;

    public static RouteProgress AtCellCenter(int routeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(routeIndex);
        return new RouteProgress(routeIndex * UnitsPerCell);
    }

    public static RouteProgress Clamp(RouteProgress value, int routeCellCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(routeCellCount);
        var max = AtCellCenter(routeCellCount - 1).Units;
        return new RouteProgress(Math.Clamp(value.Units, 0L, max));
    }

    /// <summary>Returns the logical Grid cell whose center is nearest to the fine route position.</summary>
    public int ToLogicalCellIndex(int routeCellCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(routeCellCount);
        var clamped = Clamp(this, routeCellCount).Units;
        return Math.Min(routeCellCount - 1, (int)((clamped + UnitsPerCell / 2) / UnitsPerCell));
    }
}

public enum BodySizeClass
{
    Standard,
    Large,
    Boss,
}

/// <summary>Physical spacing rules for invaders sharing one route stream.</summary>
public static class TrafficRules
{
    // 5/8 cell keeps 32px-class units visually separated without degenerating into one-unit-per-cell occupancy.
    public const long StandardSpacingUnits = RouteProgress.UnitsPerCell * 5 / 8;
    public const long LargeSpacingUnits = RouteProgress.UnitsPerCell * 3 / 4;
    public const long BossSpacingUnits = RouteProgress.UnitsPerCell;

    public static long BodySpacingUnits(BodySizeClass bodySizeClass) => bodySizeClass switch
    {
        BodySizeClass.Standard => StandardSpacingUnits,
        BodySizeClass.Large => LargeSpacingUnits,
        BodySizeClass.Boss => BossSpacingUnits,
        _ => throw new ArgumentOutOfRangeException(nameof(bodySizeClass), bodySizeClass, null),
    };

    public static long MinimumSpacingUnits(BodySizeClass first, BodySizeClass second)
        => Math.Max(BodySpacingUnits(first), BodySpacingUnits(second));
}
