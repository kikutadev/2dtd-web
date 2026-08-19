using DungeonDefense.Core;

namespace DungeonDefense.Application;

public static class CampaignInvasionContentService
{
    public static InvasionContent ApplyProgression(
        InvasionContent baseContent,
        CampaignState state,
        CampaignProgressionContent progression,
        MonsterRosterContent? monsterRoster = null)
    {
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(progression);
        var modifier = progression.InvasionModifierFor(state);
        var progressed = modifier == default
            ? baseContent
            : new InvasionContent(
                baseContent.ContentVersion,
                checked(baseContent.DeploymentCapacity + modifier.DeploymentCapacityBonus),
                baseContent.MaxMp,
                baseContent.MpChargePerTick,
                baseContent.RetreatDisengageTicks,
                baseContent.WipeLootPercent,
                baseContent.Combat,
                baseContent.UnitDeploymentCosts,
                baseContent.SupportSpells,
                baseContent.Locations,
                baseContent.UnitRoleProfiles,
                baseContent.MonsterRoster);
        return monsterRoster is null
            ? progressed
            : CampaignMonsterAvailabilityService.ApplyAvailability(progressed, state, monsterRoster);
    }
}
