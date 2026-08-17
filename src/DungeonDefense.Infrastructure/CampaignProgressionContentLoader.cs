using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class CampaignProgressionContentLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly CampaignProgressionJsonContext JsonContext = new(Options);

    public static CampaignProgressionContent Load(string path)
    {
        var dto = JsonSerializer.Deserialize(File.ReadAllText(path), JsonContext.CampaignProgressionFile)
            ?? throw new InvalidDataException("Campaign progression content is empty.");
        if (dto.SchemaVersion != 1 || !string.Equals(dto.Kind, "campaign_progression", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported campaign progression schema/kind.");

        var rewards = dto.DefenseRewards.ToDictionary(x => x.Day, x => x.Resources.ToDomain());
        return new CampaignProgressionContent(
            dto.ContentVersion,
            dto.StartingResources.ToDomain(),
            rewards,
            dto.Research.Select(x => new ResearchDefinition(x.Id, x.Cost.ToDomain(), x.UnlockIds, x.DefenseModifier?.ToDomain() ?? default, x.InvasionModifier?.ToDomain() ?? default, x.RequiredResearchIds ?? [], x.RequiredRegionIds ?? [])).ToArray(),
            dto.SpeciesUpgrades.Select(x => new SpeciesUpgradeDefinition(x.SpeciesId, x.TargetLevel, x.Cost.ToDomain(), x.UnlockIds, x.DefenseModifier?.ToDomain() ?? default, x.InvasionModifier?.ToDomain() ?? default)).ToArray(),
            dto.UnlockRules.Select(x => new ProgressionUnlockRule(x.UnlockId, x.RequiredDay, x.RequiredResearchIds, x.Enabled)).ToArray(),
            dto.FloorExpansions.Select(x => new FloorExpansionDefinition(x.Depth, x.BoardProfileId, x.Cost.ToDomain())).ToArray(),
            new RealtimeProductionDefinition(dto.RealtimeProduction.ResourcesPerHour.ToDomain(), dto.RealtimeProduction.AccumulationCapHours, dto.RealtimeProduction.MaxElapsedEffectHours));
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "content", "progression-vertical-slice.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate content/progression-vertical-slice.json.");
    }

    internal sealed record CampaignProgressionFile(
        int SchemaVersion,
        string Kind,
        string ContentVersion,
        ResourceFile StartingResources,
        DefenseRewardFile[] DefenseRewards,
        ResearchFile[] Research,
        SpeciesUpgradeFile[] SpeciesUpgrades,
        UnlockRuleFile[] UnlockRules,
        FloorExpansionFile[] FloorExpansions,
        RealtimeProductionFile RealtimeProduction);

    internal sealed record ResourceFile(int Stone, int Iron, int Soul, int Relic)
    {
        public ResourceBundle ToDomain() => new(Stone, Iron, Soul, Relic);
    }

    internal sealed record DefenseRewardFile(int Day, ResourceFile Resources);
    internal sealed record ResearchFile(string Id, ResourceFile Cost, string[] UnlockIds, DefenseModifierFile? DefenseModifier = null, InvasionModifierFile? InvasionModifier = null, string[]? RequiredResearchIds = null, string[]? RequiredRegionIds = null);
    internal sealed record SpeciesUpgradeFile(string SpeciesId, int TargetLevel, ResourceFile Cost, string[] UnlockIds, DefenseModifierFile? DefenseModifier = null, InvasionModifierFile? InvasionModifier = null);
    internal sealed record DefenseModifierFile(
        int CoreHpPercent = 0,
        int TrapDamagePercent = 0,
        int TrapCooldownReductionPercent = 0,
        int GuardHpPercent = 0,
        int GuardDamagePercent = 0,
        int FacilityDamagePercent = 0,
        int MaxMpBonus = 0,
        int SpellCooldownReductionPercent = 0,
        int SpellDurationPercent = 0,
        int PushMagnitudeBonus = 0)
    {
        public CampaignDefenseModifier ToDomain() => new(
            CoreHpPercent, TrapDamagePercent, TrapCooldownReductionPercent, GuardHpPercent, GuardDamagePercent, FacilityDamagePercent, MaxMpBonus,
            SpellCooldownReductionPercent, SpellDurationPercent, PushMagnitudeBonus);
    }
    internal sealed record InvasionModifierFile(int DeploymentCapacityBonus = 0)
    {
        public CampaignInvasionModifier ToDomain() => new(DeploymentCapacityBonus);
    }
    internal sealed record UnlockRuleFile(string UnlockId, int RequiredDay, string[] RequiredResearchIds, bool Enabled);
    internal sealed record FloorExpansionFile(int Depth, string BoardProfileId, ResourceFile Cost);
    internal sealed record RealtimeProductionFile(ResourceFile ResourcesPerHour, int AccumulationCapHours, int MaxElapsedEffectHours);
}
