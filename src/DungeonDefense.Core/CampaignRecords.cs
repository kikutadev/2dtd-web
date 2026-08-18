namespace DungeonDefense.Core;

public enum CampaignDiscoveryCategory
{
    Enemy,
    Monster,
    TrapFacility,
    Region,
}

public readonly record struct CampaignDiscoveryEntry(CampaignDiscoveryCategory Category, string Id);

public sealed record DefenseRecordSnapshot(
    string RecordId, int Day, string RegionId, DefenseOutcome Outcome, int CoreHp, int CoreMaxHp, int DeepestFloorDepth);

public sealed record InvasionRecordSnapshot(
    string RecordId, int Day, string RegionId, string LocationId, string FloorId, InvasionOutcome Outcome, ResourceBundle GrantedLoot, bool FirstClear);

public sealed record ChallengeRecordSnapshot(
    string RecordId, int Day, string RegionId, string ArchiveId, ChallengeMode Mode, DefenseOutcome Outcome, int Score);

/// <summary>Persistent, bounded player-facing history and discovery state.</summary>
public sealed class CampaignRecordBook
{
    public const int MaxRecordsPerEncounterType = 50;
    private readonly HashSet<CampaignDiscoveryEntry> _discovery;
    private readonly List<DefenseRecordSnapshot> _defense;
    private readonly List<InvasionRecordSnapshot> _invasion;
    private readonly List<ChallengeRecordSnapshot> _challenge;

    public CampaignRecordBook(
        IEnumerable<CampaignDiscoveryEntry>? discovery = null,
        IEnumerable<DefenseRecordSnapshot>? defense = null,
        IEnumerable<InvasionRecordSnapshot>? invasion = null,
        IEnumerable<ChallengeRecordSnapshot>? challenge = null)
    {
        _discovery = new HashSet<CampaignDiscoveryEntry>(discovery ?? []);
        _defense = (defense ?? []).TakeLast(MaxRecordsPerEncounterType).ToList();
        _invasion = (invasion ?? []).TakeLast(MaxRecordsPerEncounterType).ToList();
        _challenge = (challenge ?? []).TakeLast(MaxRecordsPerEncounterType).ToList();
        Validate();
    }

    public IReadOnlySet<CampaignDiscoveryEntry> Discovery => _discovery;
    public IReadOnlyList<DefenseRecordSnapshot> DefenseRecords => _defense.ToArray();
    public IReadOnlyList<InvasionRecordSnapshot> InvasionRecords => _invasion.ToArray();
    public IReadOnlyList<ChallengeRecordSnapshot> ChallengeRecords => _challenge.ToArray();

    public bool Discover(CampaignDiscoveryCategory category, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Discovery ID is required.", nameof(id));
        return _discovery.Add(new CampaignDiscoveryEntry(category, id));
    }

    public void Add(DefenseRecordSnapshot record) => AddBounded(_defense, record);
    public void Add(InvasionRecordSnapshot record) => AddBounded(_invasion, record);
    public void Add(ChallengeRecordSnapshot record) => AddBounded(_challenge, record);

    public CampaignRecordBook Clone() => new(_discovery, _defense, _invasion, _challenge);

    private static void AddBounded<T>(List<T> target, T value)
    {
        target.Add(value);
        if (target.Count > MaxRecordsPerEncounterType) target.RemoveRange(0, target.Count - MaxRecordsPerEncounterType);
    }

    private void Validate()
    {
        if (_discovery.Any(x => string.IsNullOrWhiteSpace(x.Id))) throw new ArgumentException("Discovery ID is required.");
        if (_defense.Any(x => string.IsNullOrWhiteSpace(x.RecordId) || x.Day <= 0 || string.IsNullOrWhiteSpace(x.RegionId))) throw new ArgumentException("Defense record is invalid.");
        if (_invasion.Any(x => string.IsNullOrWhiteSpace(x.RecordId) || x.Day <= 0 || string.IsNullOrWhiteSpace(x.RegionId) || string.IsNullOrWhiteSpace(x.LocationId) || string.IsNullOrWhiteSpace(x.FloorId))) throw new ArgumentException("Invasion record is invalid.");
        if (_challenge.Any(x => string.IsNullOrWhiteSpace(x.RecordId) || x.Day <= 0 || string.IsNullOrWhiteSpace(x.RegionId) || string.IsNullOrWhiteSpace(x.ArchiveId))) throw new ArgumentException("Challenge record is invalid.");
    }
}
