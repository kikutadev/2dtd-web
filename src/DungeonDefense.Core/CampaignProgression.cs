namespace DungeonDefense.Core;

public readonly record struct ResourceBundle(int Stone = 0, int Iron = 0, int Soul = 0, int Relic = 0)
{
    public static ResourceBundle Zero => new();

    public bool Covers(ResourceBundle cost)
        => Stone >= cost.Stone && Iron >= cost.Iron && Soul >= cost.Soul && Relic >= cost.Relic;

    public ResourceBundle Add(ResourceBundle value)
        => new(checked(Stone + value.Stone), checked(Iron + value.Iron), checked(Soul + value.Soul), checked(Relic + value.Relic));

    public ResourceBundle Spend(ResourceBundle cost)
    {
        if (!Covers(cost)) throw new InvalidOperationException("Insufficient resources.");
        return new(Stone - cost.Stone, Iron - cost.Iron, Soul - cost.Soul, Relic - cost.Relic);
    }

    public ResourceBundle ScalePercent(int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return new(Stone * percent / 100, Iron * percent / 100, Soul * percent / 100, Relic * percent / 100);
    }

    public override string ToString() => $"stone={Stone} iron={Iron} soul={Soul} relic={Relic}";
}

public sealed record ResearchDefinition(
    string Id,
    ResourceBundle Cost,
    IReadOnlyList<string> UnlockIds,
    CampaignDefenseModifier DefenseModifier = default,
    CampaignInvasionModifier InvasionModifier = default,
    IReadOnlyList<string>? RequiredResearchIds = null,
    IReadOnlyList<string>? RequiredRegionIds = null);
public sealed record SpeciesUpgradeDefinition(
    string SpeciesId,
    int TargetLevel,
    ResourceBundle Cost,
    IReadOnlyList<string> UnlockIds,
    CampaignDefenseModifier DefenseModifier = default,
    CampaignInvasionModifier InvasionModifier = default);
public sealed record ProgressionUnlockRule(string UnlockId, int RequiredDay, IReadOnlyList<string> RequiredResearchIds, bool Enabled = true);
public sealed record FloorExpansionDefinition(int Depth, string BoardProfileId, ResourceBundle Cost);

public sealed class CampaignProgressionContent
{
    public CampaignProgressionContent(
        string contentVersion,
        ResourceBundle startingResources,
        IReadOnlyDictionary<int, ResourceBundle> defenseRewards,
        IReadOnlyList<ResearchDefinition> research,
        IReadOnlyList<SpeciesUpgradeDefinition> speciesUpgrades,
        IReadOnlyList<ProgressionUnlockRule> unlockRules,
        IReadOnlyList<FloorExpansionDefinition> floorExpansions,
        RealtimeProductionDefinition realtimeProduction)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Content version is required.", nameof(contentVersion));
        ContentVersion = contentVersion;
        StartingResources = startingResources;
        DefenseRewards = defenseRewards;
        Research = research;
        SpeciesUpgrades = speciesUpgrades;
        UnlockRules = unlockRules;
        FloorExpansions = floorExpansions;
        RealtimeProduction = realtimeProduction.Validate();
        Validate();
    }

    public string ContentVersion { get; }
    public ResourceBundle StartingResources { get; }
    public IReadOnlyDictionary<int, ResourceBundle> DefenseRewards { get; }
    public IReadOnlyList<ResearchDefinition> Research { get; }
    public IReadOnlyList<SpeciesUpgradeDefinition> SpeciesUpgrades { get; }
    public IReadOnlyList<ProgressionUnlockRule> UnlockRules { get; }
    public IReadOnlyList<FloorExpansionDefinition> FloorExpansions { get; }
    public RealtimeProductionDefinition RealtimeProduction { get; }

    public ResourceBundle DefenseRewardForDay(int day)
        => DefenseRewards.TryGetValue(day, out var reward) ? reward : ResourceBundle.Zero;

    public CampaignDefenseModifier DefenseModifierFor(CampaignState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var modifier = default(CampaignDefenseModifier);
        foreach (var research in Research.Where(x => state.HasCompletedResearch(x.Id)))
            modifier = modifier.Add(research.DefenseModifier);
        foreach (var upgrade in SpeciesUpgrades.Where(x => state.SpeciesLevel(x.SpeciesId) >= x.TargetLevel))
            modifier = modifier.Add(upgrade.DefenseModifier);
        return modifier.Validate();
    }

    public CampaignInvasionModifier InvasionModifierFor(CampaignState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var modifier = default(CampaignInvasionModifier);
        foreach (var research in Research.Where(x => state.HasCompletedResearch(x.Id)))
            modifier = modifier.Add(research.InvasionModifier);
        foreach (var upgrade in SpeciesUpgrades.Where(x => state.SpeciesLevel(x.SpeciesId) >= x.TargetLevel))
            modifier = modifier.Add(upgrade.InvasionModifier);
        return modifier.Validate();
    }

    private void Validate()
    {
        if (Research.Any(x => string.IsNullOrWhiteSpace(x.Id))) throw new ArgumentException("Research ID is required.");
        foreach (var definition in Research)
        {
            _ = definition.DefenseModifier.Validate();
            _ = definition.InvasionModifier.Validate();
        }
        foreach (var definition in SpeciesUpgrades)
        {
            _ = definition.DefenseModifier.Validate();
            _ = definition.InvasionModifier.Validate();
        }
        if (Research.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new ArgumentException("Research IDs must be unique.");
        var researchIds = Research.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in Research)
        {
            var required = definition.RequiredResearchIds ?? [];
            if (required.Any(string.IsNullOrWhiteSpace) || required.Distinct(StringComparer.Ordinal).Count() != required.Count)
                throw new ArgumentException($"Research prerequisites are invalid: {definition.Id}.");
            if (required.Any(x => !researchIds.Contains(x))) throw new ArgumentException($"Research prerequisite is unknown: {definition.Id}.");
            if (required.Contains(definition.Id, StringComparer.Ordinal)) throw new ArgumentException($"Research cannot require itself: {definition.Id}.");
            var requiredRegions = definition.RequiredRegionIds ?? [];
            if (requiredRegions.Any(string.IsNullOrWhiteSpace) || requiredRegions.Distinct(StringComparer.Ordinal).Count() != requiredRegions.Count)
                throw new ArgumentException($"Research region requirements are invalid: {definition.Id}.");
        }
        ValidateResearchPrerequisiteCycles();
        if (SpeciesUpgrades.Any(x => string.IsNullOrWhiteSpace(x.SpeciesId) || x.TargetLevel <= 0)) throw new ArgumentException("Species upgrades require a species ID and positive target level.");
        if (SpeciesUpgrades.GroupBy(x => (x.SpeciesId, x.TargetLevel)).Any(x => x.Count() > 1)) throw new ArgumentException("Species upgrade targets must be unique.");
        if (UnlockRules.Any(x => string.IsNullOrWhiteSpace(x.UnlockId) || x.RequiredDay <= 0)) throw new ArgumentException("Unlock rules require an ID and positive Day.");
        if (FloorExpansions.Any(x => x.Depth < 2 || string.IsNullOrWhiteSpace(x.BoardProfileId))) throw new ArgumentException("Floor expansion content is invalid.");
        if (FloorExpansions.GroupBy(x => x.Depth).Any(x => x.Count() > 1)) throw new ArgumentException("Floor expansion depth must be unique.");
    }

    private void ValidateResearchPrerequisiteCycles()
    {
        var byId = Research.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new ArgumentException($"Research prerequisite cycle detected at {id}.");
            foreach (var required in byId[id].RequiredResearchIds ?? []) Visit(required);
            visiting.Remove(id);
            visited.Add(id);
        }
        foreach (var id in byId.Keys) Visit(id);
    }
}

public static class CampaignFeatureIds
{
    public const string MultiFloor = "feature.dungeon.multi_floor";
    public const string SectorExpansion = "feature.dungeon.sector_expansion";
}

public sealed record InvasionLocationProgress(string LocationId, int UnlockedDepth, IReadOnlySet<string> ClearedFloorIds);

public sealed class CampaignState
{
    private readonly HashSet<string> _completedResearch;
    private readonly HashSet<string> _unlocks;
    private readonly Dictionary<string, int> _speciesLevels;
    private readonly Dictionary<string, InvasionLocationProgress> _invasionProgress;
    private readonly Dictionary<string, ClearedDungeonArchive> _clearedDungeons;
    private readonly Dictionary<string, int> _challengeBestScores;

    public CampaignState(
        int day,
        string regionId,
        PlayerDungeonState dungeon,
        ResourceBundle resources,
        IEnumerable<string>? completedResearch = null,
        IEnumerable<string>? unlocks = null,
        IReadOnlyDictionary<string, int>? speciesLevels = null,
        IEnumerable<InvasionLocationProgress>? invasionProgress = null,
        CampaignRealtimeState? realtime = null,
        IEnumerable<ClearedDungeonArchive>? clearedDungeons = null,
        IReadOnlyDictionary<string, int>? challengeBestScores = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(day);
        if (string.IsNullOrWhiteSpace(regionId)) throw new ArgumentException("Region ID is required.", nameof(regionId));
        ArgumentNullException.ThrowIfNull(dungeon);

        Day = day;
        RegionId = regionId;
        Dungeon = dungeon.Clone();
        Resources = resources;
        _completedResearch = new HashSet<string>(completedResearch ?? [], StringComparer.Ordinal);
        _unlocks = new HashSet<string>(unlocks ?? [], StringComparer.Ordinal);
        _speciesLevels = speciesLevels is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(speciesLevels, StringComparer.Ordinal);
        _invasionProgress = (invasionProgress ?? []).ToDictionary(
            x => x.LocationId,
            x => new InvasionLocationProgress(
                x.LocationId,
                x.UnlockedDepth,
                new HashSet<string>(x.ClearedFloorIds, StringComparer.Ordinal)),
            StringComparer.Ordinal);

        if (_speciesLevels.Values.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(speciesLevels));
        if (_invasionProgress.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value.UnlockedDepth <= 0))
            throw new ArgumentOutOfRangeException(nameof(invasionProgress));
        Realtime = realtime?.Clone() ?? new CampaignRealtimeState(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, ProductionAccumulator.Zero);
        _clearedDungeons = (clearedDungeons ?? []).ToDictionary(x => x.ArchiveId, x => x.Clone(), StringComparer.Ordinal);
        _challengeBestScores = challengeBestScores is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(challengeBestScores, StringComparer.Ordinal);
        if (_challengeBestScores.Values.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(challengeBestScores));
    }

    public int Day { get; private set; }
    public string RegionId { get; private set; }
    public PlayerDungeonState Dungeon { get; private set; }
    public ResourceBundle Resources { get; private set; }
    public IReadOnlySet<string> CompletedResearch => _completedResearch;
    public IReadOnlySet<string> Unlocks => _unlocks;
    public IReadOnlyDictionary<string, int> SpeciesLevels => _speciesLevels;
    public IReadOnlyDictionary<string, InvasionLocationProgress> InvasionProgress => _invasionProgress;
    public CampaignRealtimeState Realtime { get; }
    public IReadOnlyList<ClearedDungeonArchive> ClearedDungeons => _clearedDungeons.Values.OrderBy(x => x.RegionId, StringComparer.Ordinal).Select(x => x.Clone()).ToArray();
    public IReadOnlyDictionary<string, int> ChallengeBestScores => _challengeBestScores;

    public bool HasUnlock(string id) => _unlocks.Contains(id);
    public bool HasCompletedResearch(string id) => _completedResearch.Contains(id);
    public int SpeciesLevel(string speciesId) => _speciesLevels.GetValueOrDefault(speciesId);

    public bool IsInvasionFloorUnlocked(string locationId, int depth)
        => depth > 0 && (!_invasionProgress.TryGetValue(locationId, out var progress) ? depth == 1 : depth <= progress.UnlockedDepth);

    public bool IsInvasionFloorCleared(string locationId, string floorId)
        => _invasionProgress.TryGetValue(locationId, out var progress) && progress.ClearedFloorIds.Contains(floorId);

    public CampaignState Clone()
        => new(Day, RegionId, Dungeon, Resources, _completedResearch, _unlocks, _speciesLevels, _invasionProgress.Values, Realtime, _clearedDungeons.Values, _challengeBestScores);

    public void ReplaceDungeon(PlayerDungeonState dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        Dungeon = dungeon.Clone();
    }

    public void Grant(ResourceBundle reward) => Resources = Resources.Add(reward);
    public void Spend(ResourceBundle cost) => Resources = Resources.Spend(cost);
    public void AdvanceDay() => Day = checked(Day + 1);
    public bool AddUnlock(string id) => _unlocks.Add(id);
    public bool CompleteResearch(string id) => _completedResearch.Add(id);

    public void SetSpeciesLevel(string speciesId, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        _speciesLevels[speciesId] = level;
    }

    public bool MarkInvasionFloorCleared(string locationId, string floorId, int depth, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(locationId) || string.IsNullOrWhiteSpace(floorId)) throw new ArgumentException("Invasion progress identity is required.");
        if (depth <= 0 || maxDepth < depth) throw new ArgumentOutOfRangeException(nameof(depth));
        if (!_invasionProgress.TryGetValue(locationId, out var progress))
            progress = new InvasionLocationProgress(locationId, 1, new HashSet<string>(StringComparer.Ordinal));

        var cleared = new HashSet<string>(progress.ClearedFloorIds, StringComparer.Ordinal);
        var firstClear = cleared.Add(floorId);
        var unlockedDepth = Math.Max(progress.UnlockedDepth, Math.Min(maxDepth, depth + 1));
        _invasionProgress[locationId] = new InvasionLocationProgress(locationId, unlockedDepth, cleared);
        return firstClear;
    }

    public ClearedDungeonArchive ArchiveCurrentDungeon(string finalAssaultProfileId)
    {
        var archiveId = $"{RegionId}.clear";
        if (_clearedDungeons.TryGetValue(archiveId, out var existing)) return existing.Clone();
        var archive = new ClearedDungeonArchive(archiveId, RegionId, Day, finalAssaultProfileId, Dungeon);
        _clearedDungeons.Add(archiveId, archive);
        return archive.Clone();
    }

    public ClearedDungeonArchive ClearedDungeon(string archiveId)
        => _clearedDungeons.TryGetValue(archiveId, out var archive)
            ? archive.Clone()
            : throw new InvalidOperationException($"Unknown cleared dungeon archive: {archiveId}.");

    public void BeginRegion(string regionId, PlayerDungeonState dungeon)
    {
        if (string.IsNullOrWhiteSpace(regionId)) throw new ArgumentException("Region ID is required.", nameof(regionId));
        ArgumentNullException.ThrowIfNull(dungeon);
        RegionId = regionId;
        Day = 1;
        Dungeon = dungeon.Clone();
    }

    public int ChallengeBestScore(string archiveId, ChallengeMode mode)
        => _challengeBestScores.GetValueOrDefault(ChallengeScoreKey(archiveId, mode));

    public bool RecordChallengeScore(string archiveId, ChallengeMode mode, int score)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(score);
        _ = ClearedDungeon(archiveId);
        var key = ChallengeScoreKey(archiveId, mode);
        if (_challengeBestScores.TryGetValue(key, out var current) && current >= score) return false;
        _challengeBestScores[key] = score;
        return true;
    }

    public static string ChallengeScoreKey(string archiveId, ChallengeMode mode) => $"{archiveId}|{mode}";

}
