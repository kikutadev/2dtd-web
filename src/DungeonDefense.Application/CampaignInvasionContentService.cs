using DungeonDefense.Core;

namespace DungeonDefense.Application;

public static class CampaignInvasionContentService
{
    public static InvasionContent ApplyProgression(InvasionContent baseContent, CampaignState state, CampaignProgressionContent progression)
    {
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(progression);
        var modifier = progression.InvasionModifierFor(state);
        if (modifier == default) return baseContent;
        return new InvasionContent(
            baseContent.ContentVersion,
            checked(baseContent.DeploymentCapacity + modifier.DeploymentCapacityBonus),
            baseContent.MaxMp,
            baseContent.MpChargePerTick,
            baseContent.RetreatDisengageTicks,
            baseContent.WipeLootPercent,
            baseContent.UnitDeploymentCosts,
            baseContent.SupportSpells,
            baseContent.Locations,
            baseContent.UnitRoleProfiles);
    }
}
