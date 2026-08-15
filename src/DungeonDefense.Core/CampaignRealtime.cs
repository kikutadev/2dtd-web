namespace DungeonDefense.Core;

public readonly record struct ProductionAccumulator(long StoneUnits, long IronUnits, long SoulUnits)
{
    public static ProductionAccumulator Zero => new();

    public ProductionAccumulator AddSeconds(ResourceBundle perHour, long seconds, int capHours)
    {
        if (seconds <= 0) return this;
        var capSeconds = checked((long)capHours * 3600L);
        return new(
            Math.Min(checked((long)perHour.Stone * capSeconds), checked(StoneUnits + (long)perHour.Stone * seconds)),
            Math.Min(checked((long)perHour.Iron * capSeconds), checked(IronUnits + (long)perHour.Iron * seconds)),
            Math.Min(checked((long)perHour.Soul * capSeconds), checked(SoulUnits + (long)perHour.Soul * seconds)));
    }

    public ResourceBundle AvailableResources()
        => new(checked((int)(StoneUnits / 3600L)), checked((int)(IronUnits / 3600L)), checked((int)(SoulUnits / 3600L)), 0);

    public ProductionAccumulator RemoveWholeResources(ResourceBundle collected)
        => new(
            StoneUnits - checked((long)collected.Stone * 3600L),
            IronUnits - checked((long)collected.Iron * 3600L),
            SoulUnits - checked((long)collected.Soul * 3600L));
}

public sealed record InvasionRegenerationState(string LocationId, string FloorId, DateTimeOffset ReadyAtUtc);

/// <summary>
/// Persistent real-world-time state. No method reads the system clock directly; hosts pass observed UTC explicitly.
/// EffectiveUtc advances by a clamped delta so clock jumps cannot be repeatedly harvested.
/// </summary>
public sealed class CampaignRealtimeState
{
    private readonly Dictionary<(string LocationId, string FloorId), DateTimeOffset> _invasionReadyAt;

    public CampaignRealtimeState(
        DateTimeOffset lastObservedUtc,
        DateTimeOffset effectiveUtc,
        ProductionAccumulator production,
        IEnumerable<InvasionRegenerationState>? invasionRegeneration = null)
    {
        if (lastObservedUtc.Offset != TimeSpan.Zero || effectiveUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Campaign realtime timestamps must be UTC.");
        if (effectiveUtc > lastObservedUtc)
            throw new ArgumentException("Campaign effective UTC cannot be later than the last observed UTC.");
        if (production.StoneUnits < 0 || production.IronUnits < 0 || production.SoulUnits < 0)
            throw new ArgumentException("Campaign production accumulator cannot be negative.");
        LastObservedUtc = lastObservedUtc;
        EffectiveUtc = effectiveUtc;
        Production = production;
        _invasionReadyAt = (invasionRegeneration ?? []).ToDictionary(
            x => (x.LocationId, x.FloorId), x => x.ReadyAtUtc);
    }

    public DateTimeOffset LastObservedUtc { get; private set; }
    public DateTimeOffset EffectiveUtc { get; private set; }
    public ProductionAccumulator Production { get; private set; }
    public IReadOnlyList<InvasionRegenerationState> InvasionRegeneration => _invasionReadyAt
        .OrderBy(x => x.Key.LocationId, StringComparer.Ordinal)
        .ThenBy(x => x.Key.FloorId, StringComparer.Ordinal)
        .Select(x => new InvasionRegenerationState(x.Key.LocationId, x.Key.FloorId, x.Value))
        .ToArray();

    public CampaignRealtimeState Clone() => new(LastObservedUtc, EffectiveUtc, Production, InvasionRegeneration);

    public long Observe(DateTimeOffset nowUtc, RealtimeProductionDefinition definition)
    {
        if (nowUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Observed time must be UTC.", nameof(nowUtc));
        if (LastObservedUtc == DateTimeOffset.UnixEpoch && EffectiveUtc == DateTimeOffset.UnixEpoch)
        {
            LastObservedUtc = nowUtc;
            EffectiveUtc = nowUtc;
            return 0;
        }
        if (nowUtc <= LastObservedUtc) return 0;
        var rawSeconds = checked((long)(nowUtc - LastObservedUtc).TotalSeconds);
        var maxEffectSeconds = checked((long)definition.MaxElapsedEffectHours * 3600L);
        var effectiveSeconds = Math.Min(rawSeconds, maxEffectSeconds);
        LastObservedUtc = nowUtc;
        EffectiveUtc = EffectiveUtc.AddSeconds(effectiveSeconds);
        Production = Production.AddSeconds(definition.ResourcesPerHour, effectiveSeconds, definition.AccumulationCapHours);
        return effectiveSeconds;
    }

    public ResourceBundle PendingProduction() => Production.AvailableResources();

    public ResourceBundle CollectProduction()
    {
        var available = PendingProduction();
        Production = Production.RemoveWholeResources(available);
        return available;
    }

    public void StartInvasionRegeneration(string locationId, string floorId, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        _invasionReadyAt[(locationId, floorId)] = EffectiveUtc + duration;
    }

    public bool IsInvasionReady(string locationId, string floorId)
        => !_invasionReadyAt.TryGetValue((locationId, floorId), out var readyAt) || EffectiveUtc >= readyAt;

    public TimeSpan InvasionRegenerationRemaining(string locationId, string floorId)
    {
        if (!_invasionReadyAt.TryGetValue((locationId, floorId), out var readyAt) || EffectiveUtc >= readyAt) return TimeSpan.Zero;
        return readyAt - EffectiveUtc;
    }
}

public sealed record RealtimeProductionDefinition(
    ResourceBundle ResourcesPerHour,
    int AccumulationCapHours,
    int MaxElapsedEffectHours)
{
    public RealtimeProductionDefinition Validate()
    {
        if (ResourcesPerHour.Stone < 0 || ResourcesPerHour.Iron < 0 || ResourcesPerHour.Soul < 0 || ResourcesPerHour.Relic != 0)
            throw new ArgumentException("Realtime production may only produce non-negative Stone/Iron/Soul and never Relic.");
        if (AccumulationCapHours <= 0 || MaxElapsedEffectHours <= 0) throw new ArgumentOutOfRangeException(nameof(AccumulationCapHours));
        return this;
    }
}
