using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

public enum ResearchVisualCategory
{
    Traps,
    Monsters,
    Facilities,
    Rituals,
    Magic,
    Invasion,
}

public enum ResearchAvailability
{
    Available,
    Completed,
    RegionLocked,
    PrerequisiteLocked,
    InsufficientResources,
}

public sealed record ResearchNodeVisualState(
    string Id,
    ResearchVisualCategory Category,
    ResourceBundle Cost,
    CampaignDefenseModifier DefenseModifier,
    CampaignInvasionModifier InvasionModifier,
    ResearchAvailability Availability,
    string? MissingPrerequisiteId = null);

public sealed record SpeciesUpgradeVisualState(
    string SpeciesId,
    int TargetLevel,
    ResourceBundle Cost,
    CampaignDefenseModifier DefenseModifier,
    CampaignInvasionModifier InvasionModifier,
    ResearchAvailability Availability);

public sealed record ResearchScreenVisualState(
    ResourceBundle Resources,
    IReadOnlyList<ResearchNodeVisualState> Research,
    SpeciesUpgradeVisualState? NextSpeciesUpgrade);

/// <summary>Host-neutral research availability/read model. UI hosts render state and issue commands; they do not re-evaluate progression rules.</summary>
public static class ResearchProductPresentation
{
    public static ResearchScreenVisualState Build(CampaignState state, CampaignProgressionContent progression)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(progression);
        var research = progression.Research.Select(definition =>
        {
            var missingPrerequisite = (definition.RequiredResearchIds ?? []).FirstOrDefault(id => !state.HasCompletedResearch(id));
            var regionLocked = (definition.RequiredRegionIds ?? []).Count > 0
                && !(definition.RequiredRegionIds ?? []).Contains(state.RegionId, StringComparer.Ordinal);
            var availability = state.HasCompletedResearch(definition.Id)
                ? ResearchAvailability.Completed
                : regionLocked
                    ? ResearchAvailability.RegionLocked
                    : missingPrerequisite is not null
                        ? ResearchAvailability.PrerequisiteLocked
                        : !state.Resources.Covers(definition.Cost)
                            ? ResearchAvailability.InsufficientResources
                            : ResearchAvailability.Available;
            return new ResearchNodeVisualState(
                definition.Id, CategoryFor(definition.Id), definition.Cost, definition.DefenseModifier, definition.InvasionModifier, availability, missingPrerequisite);
        }).ToArray();

        var speciesId = "species.skeleton";
        var nextLevel = state.SpeciesLevel(speciesId) + 1;
        var speciesDefinition = progression.SpeciesUpgrades.FirstOrDefault(x => x.SpeciesId == speciesId && x.TargetLevel == nextLevel);
        SpeciesUpgradeVisualState? species = speciesDefinition is null ? null : new SpeciesUpgradeVisualState(
            speciesDefinition.SpeciesId, speciesDefinition.TargetLevel, speciesDefinition.Cost, speciesDefinition.DefenseModifier, speciesDefinition.InvasionModifier,
            state.Resources.Covers(speciesDefinition.Cost) ? ResearchAvailability.Available : ResearchAvailability.InsufficientResources);
        return new ResearchScreenVisualState(state.Resources, research, species);
    }

    private static ResearchVisualCategory CategoryFor(string researchId)
    {
        if (researchId.StartsWith("research.traps.", StringComparison.Ordinal)) return ResearchVisualCategory.Traps;
        if (researchId.StartsWith("research.monsters.", StringComparison.Ordinal)) return ResearchVisualCategory.Monsters;
        if (researchId.StartsWith("research.facilities.", StringComparison.Ordinal)) return ResearchVisualCategory.Facilities;
        if (researchId.StartsWith("research.rituals.", StringComparison.Ordinal)) return ResearchVisualCategory.Rituals;
        if (researchId.StartsWith("research.magic.", StringComparison.Ordinal)) return ResearchVisualCategory.Magic;
        if (researchId.StartsWith("research.invasion.", StringComparison.Ordinal)) return ResearchVisualCategory.Invasion;
        throw new InvalidOperationException($"Research category is not defined for {researchId}.");
    }
}
