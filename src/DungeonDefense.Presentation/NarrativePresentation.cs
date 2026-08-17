using DungeonDefense.Application;

namespace DungeonDefense.Presentation;

public enum NarrativePresentationMode
{
    Guided,
    Moment,
    Ambient,
    Hint,
}

public enum NarrativeFocusIntent
{
    None,
    Dungeon,
    DefenseResult,
    Research,
    InvasionLocation,
    Region,
}

public sealed record NarrativeBeatDefinition(
    string Id,
    CampaignTransitionKind Trigger,
    string MessageKey,
    NarrativePresentationMode Mode,
    int Priority = 0,
    bool OneShot = true,
    string? SubjectId = null,
    string? RelatedId = null,
    string? RegionId = null,
    int? Day = null,
    NarrativeFocusIntent FocusIntent = NarrativeFocusIntent.None,
    DarkSpiritExpression Expression = DarkSpiritExpression.Neutral);

public sealed record NarrativeSurfaceState(
    string BeatId,
    string SpeakerId,
    string MessageKey,
    NarrativePresentationMode Mode,
    int Priority,
    bool OneShot,
    NarrativeFocusIntent FocusIntent,
    ProductAssetRef SpeakerPortrait);

/// <summary>
/// Deterministically converts Application semantic facts plus authored beat definitions into
/// a host-neutral presentation queue. It never mutates gameplay or seen-state itself.
/// </summary>
public static class NarrativeDirector
{
    public static IReadOnlyList<NarrativeSurfaceState> BuildQueue(
        IEnumerable<CampaignTransitionEvent> transitions,
        IEnumerable<NarrativeBeatDefinition> beats,
        IReadOnlySet<string> seenBeatIds)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(beats);
        ArgumentNullException.ThrowIfNull(seenBeatIds);

        var facts = transitions.ToArray();
        return beats
            .Select(Validate)
            .Where(beat => !beat.OneShot || !seenBeatIds.Contains(beat.Id))
            .Where(beat => facts.Any(fact => Matches(beat, fact)))
            .GroupBy(beat => beat.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(beat => ModeRank(beat.Mode))
            .ThenByDescending(beat => beat.Priority)
            .ThenBy(beat => beat.Id, StringComparer.Ordinal)
            .Select(beat => new NarrativeSurfaceState(
                beat.Id,
                "dark_spirit",
                beat.MessageKey,
                beat.Mode,
                beat.Priority,
                beat.OneShot,
                beat.FocusIntent,
                ProductAssetIdentity.DarkSpiritPortrait(beat.Expression)))
            .ToArray();
    }

    private static NarrativeBeatDefinition Validate(NarrativeBeatDefinition beat)
    {
        if (string.IsNullOrWhiteSpace(beat.Id)) throw new ArgumentException("Narrative beat ID is required.", nameof(beat));
        if (string.IsNullOrWhiteSpace(beat.MessageKey)) throw new ArgumentException($"Narrative beat message key is required: {beat.Id}.", nameof(beat));
        return beat;
    }

    private static bool Matches(NarrativeBeatDefinition beat, CampaignTransitionEvent fact)
        => beat.Trigger == fact.Kind
            && (beat.SubjectId is null || string.Equals(beat.SubjectId, fact.SubjectId, StringComparison.Ordinal))
            && (beat.RelatedId is null || string.Equals(beat.RelatedId, fact.RelatedId, StringComparison.Ordinal))
            && (beat.RegionId is null || string.Equals(beat.RegionId, fact.RegionId, StringComparison.Ordinal))
            && (beat.Day is null || beat.Day == fact.Day);

    private static int ModeRank(NarrativePresentationMode mode) => mode switch
    {
        NarrativePresentationMode.Guided => 4,
        NarrativePresentationMode.Moment => 3,
        NarrativePresentationMode.Hint => 2,
        NarrativePresentationMode.Ambient => 1,
        _ => 0,
    };
}
