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


public sealed record CombatAllyCandidate(
    string EntityId,
    GridPoint Position,
    int Hp,
    int MaxHp,
    UnitRole Role,
    int StableOrder = 0);

public sealed record CombatHealDecision(string TargetEntityId, int Amount);

/// <summary>
/// Team-neutral unit behavior rules shared by Defense and Invasion. Mode orchestrators supply
/// their own actor collections/events, while target selection and ability semantics remain common.
/// </summary>
public static class CombatUnitBehaviorRules
{
    public static bool CanAttack(DungeonState board, GridPoint from, int range, GridPoint to)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (range < 1) return false;
        return from.ManhattanDistance(to) <= range
               && (range <= 1 || DungeonLineOfSight.HasLineOfSight(board, from, to));
    }

    public static CombatHealDecision? SelectHealTarget(
        UnitDefinition healer,
        GridPoint healerPosition,
        IEnumerable<CombatAllyCandidate> allies,
        DungeonState board)
    {
        ArgumentNullException.ThrowIfNull(healer);
        ArgumentNullException.ThrowIfNull(allies);
        ArgumentNullException.ThrowIfNull(board);
        if (healer.HealPower <= 0) return null;

        // Support healers deliberately do not heal other healer-class units. Allowing healer-to-healer
        // chains creates deterministic sustain loops where neither side can make combat progress.
        // This rule is team-neutral and therefore applies identically in Defense and Invasion.
        var target = allies
            .Where(x => x.Role != UnitRole.Priest)
            .Where(x => x.Hp > 0 && x.Hp < x.MaxHp)
            .Where(x => CanAttack(board, healerPosition, healer.AttackRange, x.Position))
            .OrderBy(x => x.Hp / (double)x.MaxHp)
            .ThenBy(x => x.StableOrder)
            .ThenBy(x => x.EntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (target is null) return null;
        return new CombatHealDecision(target.EntityId, Math.Min(healer.HealPower, target.MaxHp - target.Hp));
    }

    public static StatusEffect? AttackStatus(UnitDefinition attacker)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        if (attacker.AttackStatusKind is null) return null;
        if (attacker.AttackStatusStrength <= 0 || attacker.AttackStatusDurationTicks <= 0)
            throw new InvalidOperationException($"Attack status parameters are invalid for {attacker.Id}.");
        return new StatusEffect(attacker.AttackStatusKind.Value, attacker.AttackStatusStrength, attacker.AttackStatusDurationTicks);
    }
}
