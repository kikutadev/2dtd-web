namespace DungeonDefense.Core;

public enum Team
{
    Dungeon,
    Invader,
}

public enum UnitRole
{
    Fighter,
    Ranged,
    Priest,
}


public enum BossRouteBreakKind
{
    ShortWarp,
}

public sealed record BossRouteBreakDefinition(
    string UnitId,
    BossRouteBreakKind Kind,
    int TriggerPathPercent,
    int TelegraphTicks,
    int SkipRouteCells,
    int MaxUsesPerFloor = 1)
{
    public BossRouteBreakDefinition Validate()
    {
        if (string.IsNullOrWhiteSpace(UnitId)) throw new ArgumentException("Boss route-break unit ID is required.", nameof(UnitId));
        if (TriggerPathPercent is < 10 or > 90) throw new ArgumentOutOfRangeException(nameof(TriggerPathPercent));
        if (TelegraphTicks < 1) throw new ArgumentOutOfRangeException(nameof(TelegraphTicks));
        if (SkipRouteCells is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(SkipRouteCells));
        if (MaxUsesPerFloor != 1) throw new ArgumentOutOfRangeException(nameof(MaxUsesPerFloor), "Initial boss route-break contract allows exactly one use per floor.");
        return this;
    }
}

public enum StatusKind
{
    Freeze,
    Slow,
    Poison,
}

public enum SpellKind
{
    Freeze,
    Push,
}

public sealed record UnitDefinition(
    string Id,
    Team Team,
    UnitRole Role,
    int MaxHp,
    int Damage,
    int AttackRange,
    int AttackCooldownTicks,
    int MoveIntervalTicks,
    bool Blocks,
    int GuardZoneRadius,
    int HealPower = 0,
    BodySizeClass BodySizeClass = BodySizeClass.Standard,
    StatusKind? AttackStatusKind = null,
    int AttackStatusStrength = 0,
    int AttackStatusDurationTicks = 0);

public sealed record TrapDefinition(
    string Id,
    int Damage,
    int CooldownTicks,
    StatusKind? StatusKind = null,
    int StatusStrength = 0,
    int StatusDurationTicks = 0);

public sealed record FacilityDefinition(
    string Id,
    int Damage,
    int Range,
    int CooldownTicks,
    StatusKind? StatusKind = null,
    int StatusStrength = 0,
    int StatusDurationTicks = 0);

public sealed record SpellDefinition(
    string Id,
    SpellKind Kind,
    int MpCost,
    int CooldownTicks,
    int Radius,
    int DurationTicks,
    int Magnitude);

public sealed record SpawnGroupDefinition(string UnitId, int Count, int InitialDelayTicks, int SpawnIntervalTicks);
public sealed record WaveDefinition(string Id, int InterWaveTicks, IReadOnlyList<SpawnGroupDefinition> SpawnGroups);

/// <summary>
/// Mode-neutral combat definition registry. Defense and Invasion may orchestrate combat
/// differently, but a definition ID must not acquire different unit/trap/facility semantics
/// merely because a different game mode is running.
/// </summary>
public sealed class DungeonCombatContent
{
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }
    public required IReadOnlyDictionary<string, TrapDefinition> Traps { get; init; }
    public required IReadOnlyDictionary<string, FacilityDefinition> Facilities { get; init; }

    public static DungeonCombatContent FromDefenseContent(DefenseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new DungeonCombatContent
        {
            Units = content.Units,
            Traps = content.Traps,
            Facilities = content.Facilities,
        };
    }
}

public sealed class DefenseContent
{
    public MonsterRosterContent? MonsterRoster { get; init; }
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }
    public required IReadOnlyDictionary<string, TrapDefinition> Traps { get; init; }
    public required IReadOnlyDictionary<string, FacilityDefinition> Facilities { get; init; }
    public required IReadOnlyDictionary<string, SpellDefinition> Spells { get; init; }
    public required IReadOnlyList<WaveDefinition> Waves { get; init; }
    public IReadOnlyDictionary<string, BossRouteBreakDefinition> BossRouteBreaks { get; init; } = new Dictionary<string, BossRouteBreakDefinition>(StringComparer.Ordinal);
    public required int CoreMaxHp { get; init; }
    public required int MaxMp { get; init; }
    public required int MpChargePerTick { get; init; }
}

public sealed record StatusEffect(StatusKind Kind, int Strength, int RemainingTicks);

public static class StatusRules
{
    public static StatusEffect Merge(StatusEffect? existing, StatusEffect incoming)
    {
        if (existing is null) return incoming;
        if (incoming.Strength >= existing.Strength) return incoming;
        return existing with { RemainingTicks = Math.Max(existing.RemainingTicks, incoming.RemainingTicks) };
    }
}
