using DungeonDefense.Core;

namespace DungeonDefense.Application;

public enum InvasionPerformanceGrade
{
    None,
    ControlledClear,
    CleanClear,
}

public sealed record InvasionPerformanceReward(
    InvasionPerformanceGrade Grade,
    int BonusPercent,
    int EngagedUnitCount,
    int DefeatedEngagedUnitCount,
    ResourceBundle Bonus);

/// <summary>
/// Campaign-level reward policy for successful spatial invasions.
/// Combat truth remains in Core; this policy only converts admitted-unit casualties into a small normal-resource bonus.
/// </summary>
public static class InvasionPerformanceRewardPolicy
{
    public const int CleanClearBonusPercent = 10;
    public const int ControlledClearBonusPercent = 5;

    public static InvasionPerformanceReward Resolve(InvasionSimulation simulation, ResourceBundle baseGrantedLoot)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var engaged = simulation.Units.Count(x => x.Admitted);
        var defeated = simulation.Units.Count(x => x.Admitted && !x.Alive);
        return Resolve(simulation.Outcome, engaged, defeated, baseGrantedLoot);
    }

    public static InvasionPerformanceReward Resolve(
        InvasionOutcome outcome,
        int engagedUnitCount,
        int defeatedEngagedUnitCount,
        ResourceBundle baseGrantedLoot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(engagedUnitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(defeatedEngagedUnitCount);
        if (defeatedEngagedUnitCount > engagedUnitCount)
            throw new ArgumentOutOfRangeException(nameof(defeatedEngagedUnitCount), "Defeated engaged units cannot exceed engaged units.");

        if (outcome != InvasionOutcome.Success || engagedUnitCount == 0)
            return new(InvasionPerformanceGrade.None, 0, engagedUnitCount, defeatedEngagedUnitCount, ResourceBundle.Zero);

        var (grade, percent) = defeatedEngagedUnitCount switch
        {
            0 => (InvasionPerformanceGrade.CleanClear, CleanClearBonusPercent),
            _ when defeatedEngagedUnitCount * 5 <= engagedUnitCount
                => (InvasionPerformanceGrade.ControlledClear, ControlledClearBonusPercent),
            _ => (InvasionPerformanceGrade.None, 0),
        };

        return new(
            grade,
            percent,
            engagedUnitCount,
            defeatedEngagedUnitCount,
            ScaleNormalResources(baseGrantedLoot, percent));
    }

    private static ResourceBundle ScaleNormalResources(ResourceBundle value, int percent)
        => percent == 0
            ? ResourceBundle.Zero
            : new ResourceBundle(
                value.Stone * percent / 100,
                value.Iron * percent / 100,
                value.Soul * percent / 100,
                0);
}
