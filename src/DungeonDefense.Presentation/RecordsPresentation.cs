using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

public enum RecordsCategory
{
    Enemies,
    Monsters,
    TrapsFacilities,
    Regions,
    DefenseRecords,
    InvasionRecords,
    ChallengeRecords,
}

/// <summary>Typed host-neutral record/codex entry. Hosts never parse ad-hoc formatted strings to recover semantics.</summary>
public sealed record RecordsEntryVisualState(
    string Id,
    bool Discovered,
    string PrimaryId,
    string? RegionId = null,
    string? RelatedId = null,
    int? Day = null,
    int? FinalDay = null,
    DefenseOutcome? DefenseOutcome = null,
    InvasionOutcome? InvasionOutcome = null,
    ChallengeMode? ChallengeMode = null,
    int? CoreHp = null,
    int? CoreMaxHp = null,
    int? DeepestFloorDepth = null,
    string? FloorId = null,
    bool? FirstClear = null,
    int? Score = null);

public sealed record RecordsCategoryVisualState(RecordsCategory Category, IReadOnlyList<RecordsEntryVisualState> Entries);

/// <summary>Host-neutral read model for the complete Records/Codex surface.</summary>
public static class RecordsPresentation
{
    public static RecordsCategoryVisualState Build(
        RecordsCategory category,
        CampaignState state,
        DefenseContent defenseContent,
        RegionCampaignContent regions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(defenseContent);
        ArgumentNullException.ThrowIfNull(regions);

        bool IsDiscovered(CampaignDiscoveryCategory discoveryCategory, string id)
            => state.Records.Discovery.Contains(new CampaignDiscoveryEntry(discoveryCategory, id));

        IReadOnlyList<RecordsEntryVisualState> entries = category switch
        {
            RecordsCategory.Enemies => defenseContent.Units.Values
                .Where(x => x.Team == Team.Invader)
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new RecordsEntryVisualState(x.Id, IsDiscovered(CampaignDiscoveryCategory.Enemy, x.Id), x.Id)).ToArray(),
            RecordsCategory.Monsters => defenseContent.Units.Values
                .Where(x => x.Team == Team.Dungeon)
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new RecordsEntryVisualState(x.Id, IsDiscovered(CampaignDiscoveryCategory.Monster, x.Id), x.Id)).ToArray(),
            RecordsCategory.TrapsFacilities => defenseContent.Traps.Keys.Concat(defenseContent.Facilities.Keys)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => new RecordsEntryVisualState(x, IsDiscovered(CampaignDiscoveryCategory.TrapFacility, x), x)).ToArray(),
            RecordsCategory.Regions => regions.Regions
                .Select(x => new RecordsEntryVisualState(x.Id, IsDiscovered(CampaignDiscoveryCategory.Region, x.Id), x.Id, RegionId: x.Id, RelatedId: x.Id, FinalDay: x.FinalDefenseDay))
                .ToArray(),
            RecordsCategory.DefenseRecords => state.Records.DefenseRecords.Reverse()
                .Select(x => new RecordsEntryVisualState(
                    x.RecordId, true, x.RegionId, RegionId: x.RegionId, RelatedId: x.RegionId, Day: x.Day,
                    DefenseOutcome: x.Outcome, CoreHp: x.CoreHp, CoreMaxHp: x.CoreMaxHp, DeepestFloorDepth: x.DeepestFloorDepth))
                .ToArray(),
            RecordsCategory.InvasionRecords => state.Records.InvasionRecords.Reverse()
                .Select(x => new RecordsEntryVisualState(
                    x.RecordId, true, x.LocationId, RegionId: x.RegionId, RelatedId: x.FloorId, Day: x.Day,
                    InvasionOutcome: x.Outcome, FloorId: x.FloorId, FirstClear: x.FirstClear))
                .ToArray(),
            RecordsCategory.ChallengeRecords => state.Records.ChallengeRecords.Reverse()
                .Select(x => new RecordsEntryVisualState(
                    x.RecordId, true, x.ArchiveId, RegionId: x.RegionId, RelatedId: x.ArchiveId, Day: x.Day,
                    DefenseOutcome: x.Outcome, ChallengeMode: x.Mode, Score: x.Score))
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        return new RecordsCategoryVisualState(category, entries);
    }
}
