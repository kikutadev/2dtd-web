namespace DungeonDefense.Application;

/// <summary>
/// Run-local acknowledgement state for one-shot narrative beats.
/// It is intentionally separate from gameplay progression and from profile-level tutorial preferences.
/// </summary>
public sealed class CampaignNarrativeProgress
{
    private readonly HashSet<string> _seenBeatIds;

    public CampaignNarrativeProgress(IEnumerable<string>? seenBeatIds = null)
    {
        _seenBeatIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in seenBeatIds ?? [])
        {
            ValidateBeatId(id);
            if (!_seenBeatIds.Add(id)) throw new ArgumentException($"Duplicate narrative beat ID: {id}.", nameof(seenBeatIds));
        }
    }

    public IReadOnlySet<string> SeenBeatIds => _seenBeatIds;
    public bool HasSeen(string beatId) => _seenBeatIds.Contains(ValidateBeatId(beatId));
    public bool MarkSeen(string beatId) => _seenBeatIds.Add(ValidateBeatId(beatId));
    public CampaignNarrativeProgress Clone() => new(_seenBeatIds);

    private static string ValidateBeatId(string beatId)
    {
        if (string.IsNullOrWhiteSpace(beatId)) throw new ArgumentException("Narrative beat ID is required.", nameof(beatId));
        return beatId;
    }
}
