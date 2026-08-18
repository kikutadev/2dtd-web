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

public sealed record RecordsEntryVisualState(
    string Id,
    bool Discovered,
    string PrimaryText,
    string SecondaryText = "",
    string? RelatedId = null);

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
                .Select(x => new RecordsEntryVisualState(x.Id, IsDiscovered(CampaignDiscoveryCategory.Region, x.Id), x.Id, $"final_day={x.FinalDefenseDay}"))
                .ToArray(),
            RecordsCategory.DefenseRecords => state.Records.DefenseRecords.Reverse()
                .Select(x => new RecordsEntryVisualState(x.RecordId, true, x.RegionId, $"day={x.Day}|outcome={x.Outcome}|core={x.CoreHp}/{x.CoreMaxHp}|deepest={x.DeepestFloorDepth}"))
                .ToArray(),
            RecordsCategory.InvasionRecords => state.Records.InvasionRecords.Reverse()
                .Select(x => new RecordsEntryVisualState(x.RecordId, true, x.LocationId, $"day={x.Day}|floor={x.FloorId}|outcome={x.Outcome}|first={x.FirstClear}", x.FloorId))
                .ToArray(),
            RecordsCategory.ChallengeRecords => state.Records.ChallengeRecords.Reverse()
                .Select(x => new RecordsEntryVisualState(x.RecordId, true, x.ArchiveId, $"day={x.Day}|mode={x.Mode}|outcome={x.Outcome}|score={x.Score}", x.ArchiveId))
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        return new RecordsCategoryVisualState(category, entries);
    }
}
