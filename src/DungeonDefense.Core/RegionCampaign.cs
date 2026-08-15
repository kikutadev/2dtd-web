namespace DungeonDefense.Core;

public enum ChallengeMode
{
    Replay,
    Score,
    SpecialWave,
}

public sealed record RegionCampaignDefinition(
    string Id,
    int FinalDefenseDay,
    string FinalAssaultProfileId,
    string StartingBoardProfileId,
    string? NextRegionId,
    string ScoreChallengeAssaultProfileId,
    string SpecialWaveAssaultProfileId);

public sealed class RegionCampaignContent
{
    private readonly Dictionary<string, RegionCampaignDefinition> _regions;

    public RegionCampaignContent(string contentVersion, IReadOnlyList<RegionCampaignDefinition> regions)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Region content version is required.", nameof(contentVersion));
        if (regions.Count == 0) throw new ArgumentException("At least one region is required.", nameof(regions));
        if (regions.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new ArgumentException("Region IDs must be unique.", nameof(regions));
        foreach (var region in regions)
        {
            if (string.IsNullOrWhiteSpace(region.Id) || region.FinalDefenseDay <= 0
                || string.IsNullOrWhiteSpace(region.FinalAssaultProfileId)
                || string.IsNullOrWhiteSpace(region.StartingBoardProfileId)
                || string.IsNullOrWhiteSpace(region.ScoreChallengeAssaultProfileId)
                || string.IsNullOrWhiteSpace(region.SpecialWaveAssaultProfileId))
                throw new ArgumentException($"Invalid region definition: {region.Id}.", nameof(regions));
        }
        var ids = regions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var region in regions.Where(x => x.NextRegionId is not null))
            if (!ids.Contains(region.NextRegionId!)) throw new ArgumentException($"Unknown next region: {region.NextRegionId}.", nameof(regions));

        ContentVersion = contentVersion;
        Regions = regions.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        _regions = Regions.ToDictionary(x => x.Id, StringComparer.Ordinal);
    }

    public string ContentVersion { get; }
    public IReadOnlyList<RegionCampaignDefinition> Regions { get; }
    public RegionCampaignDefinition Region(string id)
        => _regions.TryGetValue(id, out var region) ? region : throw new InvalidOperationException($"Unknown region: {id}.");
    public bool TryRegion(string id, out RegionCampaignDefinition region) => _regions.TryGetValue(id, out region!);
}

public sealed class ClearedDungeonArchive
{
    public ClearedDungeonArchive(
        string archiveId,
        string regionId,
        int clearedDay,
        string finalAssaultProfileId,
        PlayerDungeonState dungeon)
    {
        if (string.IsNullOrWhiteSpace(archiveId) || string.IsNullOrWhiteSpace(regionId) || string.IsNullOrWhiteSpace(finalAssaultProfileId))
            throw new ArgumentException("Cleared dungeon archive identity is required.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clearedDay);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArchiveId = archiveId;
        RegionId = regionId;
        ClearedDay = clearedDay;
        FinalAssaultProfileId = finalAssaultProfileId;
        Dungeon = dungeon.Clone();
    }

    public string ArchiveId { get; }
    public string RegionId { get; }
    public int ClearedDay { get; }
    public string FinalAssaultProfileId { get; }
    public PlayerDungeonState Dungeon { get; }
    public ClearedDungeonArchive Clone() => new(ArchiveId, RegionId, ClearedDay, FinalAssaultProfileId, Dungeon);
}

public sealed record ChallengeDefinition(
    string ArchiveId,
    string RegionId,
    ChallengeMode Mode,
    string AssaultProfileId);

public sealed record ChallengeResult(
    ChallengeDefinition Definition,
    DefenseOutcome Outcome,
    int Score,
    int CoreHp,
    int CoreMaxHp,
    int Tick,
    string Digest);
