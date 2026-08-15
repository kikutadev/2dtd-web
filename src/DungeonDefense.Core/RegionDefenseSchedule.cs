namespace DungeonDefense.Core;

public sealed record RegionDayDefenseDefinition(
    int Day,
    IReadOnlyList<string> AssaultProfileIds,
    int IntensityPercent,
    int CountVariation,
    int TimingJitterTicks,
    bool SeedVariation = true);

public sealed class RegionDefenseScheduleContent
{
    private readonly IReadOnlyDictionary<int, RegionDayDefenseDefinition> _days;

    public RegionDefenseScheduleContent(
        string contentVersion,
        string regionId,
        IReadOnlyList<RegionDayDefenseDefinition> days)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Content version is required.", nameof(contentVersion));
        if (string.IsNullOrWhiteSpace(regionId)) throw new ArgumentException("Region ID is required.", nameof(regionId));
        if (days.Count == 0) throw new ArgumentException("At least one Day definition is required.", nameof(days));
        if (days.GroupBy(x => x.Day).Any(x => x.Count() > 1)) throw new ArgumentException("Day definitions must be unique.", nameof(days));
        if (days.Any(x => x.Day <= 0 || x.AssaultProfileIds.Count == 0 || x.AssaultProfileIds.Any(string.IsNullOrWhiteSpace)
            || x.IntensityPercent <= 0 || x.CountVariation < 0 || x.TimingJitterTicks < 0))
            throw new ArgumentException("Region defense Day definition is invalid.", nameof(days));

        ContentVersion = contentVersion;
        RegionId = regionId;
        _days = days.OrderBy(x => x.Day).ToDictionary(x => x.Day);
    }

    public string ContentVersion { get; }
    public string RegionId { get; }
    public IReadOnlyList<RegionDayDefenseDefinition> Days => _days.Values.OrderBy(x => x.Day).ToArray();

    public RegionDayDefenseDefinition Day(int day)
        => _days.TryGetValue(day, out var definition)
            ? definition
            : throw new InvalidOperationException($"No defense schedule for {RegionId} Day {day}.");

    public bool TryDay(int day, out RegionDayDefenseDefinition definition)
        => _days.TryGetValue(day, out definition!);
}

public sealed record GeneratedDefenseScenario(
    string Id,
    string RegionId,
    int Day,
    int Seed,
    string AssaultProfileId,
    int IntensityPercent,
    IReadOnlyList<string> ThreatTags,
    IReadOnlyList<WaveDefinition> Waves);

public static class RegionDefenseScenarioGenerator
{
    public static GeneratedDefenseScenario Generate(
        RegionDefenseScheduleContent schedule,
        int day,
        int seed,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(assaultProfiles);
        var definition = schedule.Day(day);
        var state = Mix(unchecked((uint)seed), unchecked((uint)day), StableHash(schedule.RegionId));

        var profileIndex = definition.SeedVariation
            ? Next(ref state, definition.AssaultProfileIds.Count)
            : 0;
        var profileId = definition.AssaultProfileIds[profileIndex];
        var profile = assaultProfiles.SingleOrDefault(x => string.Equals(x.Id, profileId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Defense schedule references unknown assault profile: {profileId}.");

        var waves = profile.Waves.Select((wave, waveIndex) => new WaveDefinition(
            $"{wave.Id}.d{day:D2}",
            Math.Max(0, wave.InterWaveTicks + Jitter(ref state, definition.SeedVariation ? definition.TimingJitterTicks : 0)),
            wave.SpawnGroups.Select((group, groupIndex) =>
            {
                var scaled = Math.Max(1, (int)Math.Round(group.Count * definition.IntensityPercent / 100.0, MidpointRounding.AwayFromZero));
                var countDelta = definition.SeedVariation && definition.CountVariation > 0
                    ? Next(ref state, definition.CountVariation * 2 + 1) - definition.CountVariation
                    : 0;
                var count = Math.Max(1, scaled + countDelta);
                var initial = Math.Max(0, group.InitialDelayTicks + Jitter(ref state, definition.SeedVariation ? definition.TimingJitterTicks : 0));
                var interval = group.SpawnIntervalTicks == 0
                    ? 0
                    : Math.Max(1, group.SpawnIntervalTicks + Jitter(ref state, definition.SeedVariation ? definition.TimingJitterTicks : 0));
                return new SpawnGroupDefinition(group.UnitId, count, initial, interval);
            }).ToArray())).ToArray();

        return new GeneratedDefenseScenario(
            $"{schedule.RegionId}.day{day:D2}.seed{seed}",
            schedule.RegionId,
            day,
            seed,
            profile.Id,
            definition.IntensityPercent,
            profile.ThreatTags,
            waves);
    }

    private static int Jitter(ref uint state, int magnitude)
        => magnitude <= 0 ? 0 : Next(ref state, magnitude * 2 + 1) - magnitude;

    private static int Next(ref uint state, int upperExclusive)
    {
        if (upperExclusive <= 1) return 0;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (int)(state % (uint)upperExclusive);
    }

    private static uint Mix(uint a, uint b, uint c)
    {
        var state = 0x9E3779B9u ^ a;
        state = unchecked(state * 1664525u + 1013904223u + b);
        state = unchecked(state * 1664525u + 1013904223u + c);
        return state == 0 ? 0xA341316Cu : state;
    }

    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash ^= c;
            hash = unchecked(hash * 16777619u);
        }
        return hash;
    }
}
