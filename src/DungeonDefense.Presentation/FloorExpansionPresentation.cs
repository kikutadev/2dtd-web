using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Host-neutral product state for the next player-dungeon floor expansion.
/// Hosts render this state and invoke the existing CampaignGameSession command; they do not
/// interpret progression content or duplicate affordability/unlock rules.
/// </summary>
public sealed record FloorExpansionVisualState(
    bool HasNextExpansion,
    int NextDepth,
    string BoardProfileId,
    ResourceBundle Cost,
    bool FeatureUnlocked,
    bool Affordable,
    string? UnavailableReason);

public static class FloorExpansionPresentation
{
    public static FloorExpansionVisualState Build(CampaignGameSession campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        var nextDepth = campaign.Defense.Dungeon.FloorCount + 1;
        var expansion = campaign.Progression.FloorExpansions.SingleOrDefault(x => x.Depth == nextDepth);
        if (expansion is null)
            return new FloorExpansionVisualState(false, nextDepth, string.Empty, ResourceBundle.Zero, false, false, "content_end");

        var state = campaign.State;
        var unlocked = state.HasUnlock(CampaignFeatureIds.MultiFloor);
        var affordable = state.Resources.Covers(expansion.Cost);
        var reason = !unlocked ? "locked" : !affordable ? "insufficient_resources" : null;

        return new FloorExpansionVisualState(
            true,
            nextDepth,
            expansion.BoardProfileId,
            expansion.Cost,
            unlocked,
            affordable,
            reason);
    }
}
