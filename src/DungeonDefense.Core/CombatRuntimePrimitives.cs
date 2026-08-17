namespace DungeonDefense.Core;

/// <summary>
/// Small deterministic combat primitives shared by Defense and Invasion orchestrators.
/// These helpers contain no ownership, objective, wave, deployment, or UI semantics.
/// </summary>
public static class CombatMovementRules
{
    public static int EffectiveMoveInterval(int baseMoveIntervalTicks, Dictionary<StatusKind, StatusEffect> statuses)
    {
        var multiplier = statuses.TryGetValue(StatusKind.Slow, out var slow) && slow.RemainingTicks > 0
            ? Math.Max(1, slow.Strength)
            : 1;
        return Math.Max(1, checked(baseMoveIntervalTicks * multiplier));
    }

    public static (long Advance, int Remainder) ComputeRouteAdvance(int baseMoveIntervalTicks, Dictionary<StatusKind, StatusEffect> statuses, int moveRemainder)
    {
        var interval = EffectiveMoveInterval(baseMoveIntervalTicks, statuses);
        var numerator = RouteProgress.UnitsPerCell + moveRemainder;
        return (numerator / interval, (int)(numerator % interval));
    }
}

public static class CombatStatusRules
{
    public static bool HasStatus(Dictionary<StatusKind, StatusEffect> statuses, StatusKind kind)
        => statuses.TryGetValue(kind, out var status) && status.RemainingTicks > 0;

    public static void Merge(Dictionary<StatusKind, StatusEffect> statuses, StatusEffect incoming)
        => statuses[incoming.Kind] = StatusRules.Merge(statuses.GetValueOrDefault(incoming.Kind), incoming);
}
