using DungeonDefense.Core;

namespace DungeonDefense.Application;

/// <summary>
/// Campaign authority for which player monsters may be placed or formed. Hosts must consume
/// the derived states and must not recreate unlock conditions from day/research themselves.
/// </summary>
public static class CampaignMonsterAvailabilityService
{
    public static bool IsAvailable(CampaignState state, MonsterRosterContent roster, string monsterId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(roster);
        return roster.IsAvailable(state, monsterId);
    }

    public static IReadOnlyList<MonsterDefinition> AvailableMonsters(CampaignState state, MonsterRosterContent roster)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(roster);
        return roster.Available(state);
    }

    public static IReadOnlyList<BuildOption> AvailableDefenseGuards(CampaignState state, MonsterRosterContent roster)
        => AvailableMonsters(state, roster).Select(DefenseSliceBuildCatalog.ToGuardOption).ToArray();

    public static InvasionContent ApplyAvailability(InvasionContent content, CampaignState state, MonsterRosterContent roster)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(roster);
        var availableIds = AvailableMonsters(state, roster).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var costs = content.UnitDeploymentCosts
            .Where(x => availableIds.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var roles = content.UnitRoleProfiles
            .Where(x => availableIds.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (costs.Count == 0) throw new InvalidOperationException("Campaign has no available invasion monsters.");
        return new InvasionContent(
            content.ContentVersion,
            content.DeploymentCapacity,
            content.MaxMp,
            content.MpChargePerTick,
            content.RetreatDisengageTicks,
            content.WipeLootPercent,
            content.Combat,
            costs,
            content.SupportSpells,
            content.Locations,
            roles,
            roster);
    }
}
