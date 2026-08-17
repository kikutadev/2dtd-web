namespace DungeonDefense.Application;

/// <summary>
/// Player-visible semantic facts emitted by campaign mutations. These are intentionally
/// independent from narrative copy and renderer concerns so every host observes the same facts.
/// </summary>
public enum CampaignTransitionKind
{
    CampaignStarted,
    DayAdvanced,
    RegionCleared,
    RegionEntered,
    UnlockGranted,
    ResearchCompleted,
    SpeciesUpgraded,
    InvasionFirstCleared,
    InvasionFloorUnlocked,
    RelicAcquired,
}

/// <summary>
/// A host-neutral fact that may be consumed by Tutorial/Narrative presentation.
/// SubjectId identifies the primary entity; RelatedId is optional secondary context
/// such as an invasion location for a newly-unlocked floor.
/// </summary>
public sealed record CampaignTransitionEvent(
    CampaignTransitionKind Kind,
    string? SubjectId = null,
    string? RelatedId = null,
    int? Day = null,
    string? RegionId = null,
    int Amount = 0);
