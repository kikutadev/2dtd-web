using DungeonDefense.Core;

namespace DungeonDefense.Application;

public static class CampaignContentIntegrityValidator
{
    public static IReadOnlyList<string> Validate(CampaignProgressionContent progression, RegionCampaignContent regions)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(regions);
        var errors = new List<string>();
        var boardIds = DungeonBoardProfiles.All.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var region in regions.Regions)
            if (!boardIds.Contains(region.StartingBoardProfileId))
                errors.Add($"Region {region.Id} references unknown starting board profile: {region.StartingBoardProfileId}.");

        foreach (var expansion in progression.FloorExpansions)
            if (!boardIds.Contains(expansion.BoardProfileId))
                errors.Add($"Floor expansion depth {expansion.Depth} references unknown board profile: {expansion.BoardProfileId}.");

        var sectorExpansionEnabled = progression.UnlockRules.Any(x =>
            x.Enabled && string.Equals(x.UnlockId, CampaignFeatureIds.SectorExpansion, StringComparison.Ordinal));
        if (sectorExpansionEnabled)
        {
            var hasLockedSector = DungeonBoardProfiles.All.Any(profile => profile.CreateBase().Sectors.Any(x => !x.IsUnlocked));
            if (!hasLockedSector)
                errors.Add("Sector expansion is enabled but no board profile defines a locked sector.");
        }

        var byId = regions.Regions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id))
            {
                errors.Add($"Region progression cycle detected at {id}.");
                return;
            }
            if (byId[id].NextRegionId is { } next) Visit(next);
            visiting.Remove(id);
            visited.Add(id);
        }
        foreach (var id in byId.Keys) Visit(id);

        return errors.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public static void ValidateOrThrow(CampaignProgressionContent progression, RegionCampaignContent regions)
    {
        var errors = Validate(progression, regions);
        if (errors.Count > 0) throw new InvalidDataException($"Campaign content integrity failed: {string.Join(" | ", errors)}");
    }
}
