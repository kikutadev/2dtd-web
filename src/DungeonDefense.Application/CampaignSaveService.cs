using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record SuspendedInvasionState(string LocationId, InvasionSimulationSnapshot Snapshot, bool IsFirstClearScenario = true, bool IsResolved = false);

public sealed record CampaignSaveImportResult(
    CampaignState State,
    DungeonFloorId SelectedFloorId,
    SuspendedInvasionState? ActiveInvasion,
    CampaignNarrativeProgress NarrativeProgress);

public static class CampaignSaveService
{
    public static CampaignSaveFile Export(
        CampaignState state,
        DungeonFloorId selectedFloorId,
        string contentVersion,
        SuspendedInvasionState? activeInvasion = null,
        CampaignNarrativeProgress? narrativeProgress = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Content version is required.", nameof(contentVersion));

        var dungeon = PlayerDungeonSaveService.Export(state.Dungeon, selectedFloorId);
        var resources = state.Resources;
        var realtime = state.Realtime;
        return new CampaignSaveFile(
            4,
            "campaign_save",
            contentVersion,
            state.Day,
            state.RegionId,
            ToFile(resources),
            state.CompletedResearch.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            state.Unlocks.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            state.SpeciesLevels.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new CampaignSpeciesLevelFile(x.Key, x.Value)).ToArray(),
            state.InvasionProgress.Values.OrderBy(x => x.LocationId, StringComparer.Ordinal)
                .Select(x => new CampaignInvasionProgressFile(
                    x.LocationId,
                    x.UnlockedDepth,
                    x.ClearedFloorIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()))
                .ToArray(),
            dungeon,
            new CampaignRealtimeFile(
                realtime.LastObservedUtc,
                realtime.EffectiveUtc,
                new CampaignProductionAccumulatorFile(
                    realtime.Production.StoneUnits,
                    realtime.Production.IronUnits,
                    realtime.Production.SoulUnits),
                realtime.InvasionRegeneration.Select(x => new CampaignInvasionRegenerationFile(
                    x.LocationId, x.FloorId, x.ReadyAtUtc)).ToArray()),
            activeInvasion is null ? null : ToFile(activeInvasion),
            state.ClearedDungeons.OrderBy(x => x.ArchiveId, StringComparer.Ordinal)
                .Select(x => new CampaignClearedDungeonFile(
                    x.ArchiveId,
                    x.RegionId,
                    x.ClearedDay,
                    x.FinalAssaultProfileId,
                    PlayerDungeonSaveService.Export(x.Dungeon, DungeonFloorId.First)))
                .ToArray(),
            state.ChallengeBestScores.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new CampaignChallengeBestFile(x.Key, x.Value))
                .ToArray(),
            (narrativeProgress?.SeenBeatIds ?? new HashSet<string>(StringComparer.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            state.Records.Discovery
                .OrderBy(x => x.Category).ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new CampaignDiscoveryFile(x.Category.ToString(), x.Id)).ToArray(),
            state.Records.DefenseRecords
                .Select(x => new CampaignDefenseRecordFile(x.RecordId, x.Day, x.RegionId, x.Outcome.ToString(), x.CoreHp, x.CoreMaxHp, x.DeepestFloorDepth)).ToArray(),
            state.Records.InvasionRecords
                .Select(x => new CampaignInvasionRecordFile(x.RecordId, x.Day, x.RegionId, x.LocationId, x.FloorId, x.Outcome.ToString(), ToFile(x.GrantedLoot), x.FirstClear)).ToArray(),
            state.Records.ChallengeRecords
                .Select(x => new CampaignChallengeRecordFile(x.RecordId, x.Day, x.RegionId, x.ArchiveId, x.Mode.ToString(), x.Outcome.ToString(), x.Score)).ToArray(),
            state.Cosmetics.OwnedProductIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            state.Cosmetics.EquippedByTarget.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new CampaignEquippedCosmeticFile(x.Key, x.Value)).ToArray());
    }

    public static CampaignSaveImportResult Import(
        CampaignSaveFile file,
        string expectedContentVersion,
        MonsterRosterContent? monsterRoster = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.SchemaVersion is not (3 or 4) || !string.Equals(file.Kind, "campaign_save", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported campaign save schema/kind.");
        if (!string.Equals(file.ContentVersion, expectedContentVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Campaign save content version mismatch: {file.ContentVersion} != {expectedContentVersion}.");
        if (file.Day <= 0) throw new InvalidDataException("Campaign Day must be positive.");
        if (string.IsNullOrWhiteSpace(file.RegionId)) throw new InvalidDataException("Campaign region_id is required.");

        var dungeon = PlayerDungeonSaveService.Import(file.Dungeon, monsterRoster);
        ArgumentNullException.ThrowIfNull(file.Resources);
        var resources = ToDomain(file.Resources);
        if (resources.Stone < 0 || resources.Iron < 0 || resources.Soul < 0 || resources.Relic < 0)
            throw new InvalidDataException("Campaign resources cannot be negative.");
        ValidateRealtimeImport(file.Realtime, resources);
        var species = file.SpeciesLevels.ToDictionary(x => x.SpeciesId, x => x.Level, StringComparer.Ordinal);
        var invasionProgress = file.InvasionProgress.Select(x => new InvasionLocationProgress(
            x.LocationId,
            x.UnlockedDepth,
            new HashSet<string>(x.ClearedFloorIds, StringComparer.Ordinal)));
        var realtime = file.Realtime is null
            ? new CampaignRealtimeState(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, ProductionAccumulator.Zero)
            : new CampaignRealtimeState(
                file.Realtime.LastObservedUtc,
                file.Realtime.EffectiveUtc,
                new ProductionAccumulator(
                    file.Realtime.Production.StoneUnits,
                    file.Realtime.Production.IronUnits,
                    file.Realtime.Production.SoulUnits),
                file.Realtime.InvasionRegeneration.Select(x => new InvasionRegenerationState(
                    x.LocationId, x.FloorId, x.ReadyAtUtc)));
        var clearedDungeons = (file.ClearedDungeons ?? []).Select(x => new ClearedDungeonArchive(
            x.ArchiveId,
            x.RegionId,
            x.ClearedDay,
            x.FinalAssaultProfileId,
            PlayerDungeonSaveService.Import(x.Dungeon, monsterRoster).Dungeon));
        var challengeBestScores = (file.ChallengeBestScores ?? []).ToDictionary(x => x.Key, x => x.BestScore, StringComparer.Ordinal);
        if (file.SeenNarrativeBeatIds is null)
            throw new InvalidDataException("Campaign save seen_narrative_beat_ids is required.");
        CampaignNarrativeProgress narrativeProgress;
        try
        {
            narrativeProgress = new CampaignNarrativeProgress(file.SeenNarrativeBeatIds);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Campaign narrative progress is invalid: {ex.Message}", ex);
        }
        var records = new CampaignRecordBook(
            (file.Discovery ?? []).Select(x => new CampaignDiscoveryEntry(ParseDiscoveryCategory(x.Category), x.Id)),
            (file.DefenseRecords ?? []).Select(x => new DefenseRecordSnapshot(
                x.RecordId, x.Day, x.RegionId, ParseDefenseOutcome(x.Outcome), x.CoreHp, x.CoreMaxHp, x.DeepestFloorDepth)),
            (file.InvasionRecords ?? []).Select(x => new InvasionRecordSnapshot(
                x.RecordId, x.Day, x.RegionId, x.LocationId, x.FloorId, ParseInvasionOutcome(x.Outcome), ToDomain(x.GrantedLoot), x.FirstClear)),
            (file.ChallengeRecords ?? []).Select(x => new ChallengeRecordSnapshot(
                x.RecordId, x.Day, x.RegionId, x.ArchiveId, ParseChallengeMode(x.Mode), ParseDefenseOutcome(x.Outcome), x.Score)));

        var cosmetics = new CampaignCosmeticState(
            file.OwnedCosmeticIds ?? [],
            (file.EquippedCosmetics ?? []).ToDictionary(x => x.TargetKey, x => x.ProductId, StringComparer.Ordinal));

        var state = new CampaignState(
            file.Day,
            file.RegionId,
            dungeon.Dungeon,
            resources,
            file.CompletedResearchIds,
            file.UnlockIds,
            species,
            invasionProgress,
            realtime,
            clearedDungeons,
            challengeBestScores,
            records,
            cosmetics);
        return new CampaignSaveImportResult(
            state,
            dungeon.SelectedFloorId,
            file.ActiveInvasion is null ? null : ToDomain(file.ActiveInvasion),
            narrativeProgress);
    }

    private static void ValidateRealtimeImport(CampaignRealtimeFile? realtime, ResourceBundle resources)
    {
        if (realtime is null) return;
        if (realtime.LastObservedUtc.Offset != TimeSpan.Zero || realtime.EffectiveUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Campaign realtime timestamps must be UTC.");
        if (realtime.EffectiveUtc > realtime.LastObservedUtc)
            throw new InvalidDataException("Campaign effective_utc cannot be later than last_observed_utc.");
        if (realtime.Production is null) throw new InvalidDataException("Campaign realtime production is required.");
        ValidateCollectable(realtime.Production.StoneUnits, resources.Stone, "stone");
        ValidateCollectable(realtime.Production.IronUnits, resources.Iron, "iron");
        ValidateCollectable(realtime.Production.SoulUnits, resources.Soul, "soul");
    }

    private static void ValidateCollectable(long units, int current, string resourceName)
    {
        if (units < 0) throw new InvalidDataException($"Campaign {resourceName} production accumulator cannot be negative.");
        if (units / 3600L > int.MaxValue - (long)current)
            throw new InvalidDataException($"Campaign {resourceName} production would overflow the resource balance when collected.");
    }

    private static CampaignActiveInvasionFile ToFile(SuspendedInvasionState active)
    {
        var snapshot = active.Snapshot;
        return new CampaignActiveInvasionFile(
            snapshot.ContentVersion,
            active.LocationId,
            snapshot.FloorId,
            snapshot.MapDigest,
            snapshot.Seed,
            snapshot.Tick,
            snapshot.Mp,
            snapshot.UsedDeploymentCapacity,
            snapshot.RetreatRemainingTicks,
            snapshot.Outcome.ToString(),
            ToFile(snapshot.SecuredLoot),
            snapshot.ObjectiveStructureHp,
            snapshot.ClearedSectionIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            snapshot.Units.OrderBy(x => x.FormationIndex).Select(x => new CampaignActiveInvasionUnitFile(
                x.EntityId, x.DefinitionId, x.FormationIndex, ToFile(x.Position), x.Hp, x.Shield, x.RouteProgressUnits,
                x.PathIndex, x.MoveRemainder, x.NextMoveTick, x.NextAttackTick, x.DeploymentRequested, x.Admitted,
                x.TargetEntityId, x.Archetype.ToString(), x.Statuses.Select(ToFile).ToArray())).ToArray(),
            snapshot.Guards.OrderBy(x => x.EntityId, StringComparer.Ordinal).Select(x => new CampaignActiveInvasionGuardFile(
                x.EntityId, x.DefinitionId, ToFile(x.Position), x.Hp, x.NextMoveTick, x.NextAttackTick, x.TargetEntityId,
                x.Statuses.Select(ToFile).ToArray())).ToArray(),
            snapshot.TrapCooldowns.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new CampaignActiveInvasionCooldownFile(x.Id, x.ReadyTick)).ToArray(),
            snapshot.FacilityCooldowns.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new CampaignActiveInvasionCooldownFile(x.Id, x.ReadyTick)).ToArray(),
            snapshot.SpellCooldowns.OrderBy(x => x.SpellId, StringComparer.Ordinal).Select(x => new CampaignActiveInvasionSpellCooldownFile(x.SpellId, x.RemainingTicks)).ToArray(),
            snapshot.Events.Select(x => new CampaignActiveInvasionEventFile(
                x.Tick, x.Type.ToString(), x.ActorId, x.TargetId, ToOptionalFile(x.Position), x.Amount, x.Detail,
                ToOptionalFile(x.SourcePosition), x.SourceDefinitionId)).ToArray(),
            active.IsFirstClearScenario,
            active.IsResolved);
    }

    private static SuspendedInvasionState ToDomain(CampaignActiveInvasionFile file)
    {
        if (!Enum.TryParse<InvasionOutcome>(file.Outcome, ignoreCase: false, out var outcome))
            throw new InvalidDataException($"Unknown active invasion outcome: {file.Outcome}.");
        var snapshot = new InvasionSimulationSnapshot(
            file.ContentVersion,
            file.FloorId,
            file.MapDigest,
            file.Seed,
            file.Tick,
            file.Mp,
            file.UsedDeploymentCapacity,
            file.RetreatRemainingTicks,
            outcome,
            ToDomain(file.SecuredLoot),
            file.ObjectiveStructureHp,
            file.ClearedSectionIds.ToArray(),
            file.Units.Select(x => new InvasionUnitRuntimeSnapshot(
                x.EntityId, x.DefinitionId, x.FormationIndex, ToDomain(x.Position), x.Hp, x.Shield, x.RouteProgressUnits,
                x.PathIndex, x.MoveRemainder, x.NextMoveTick, x.NextAttackTick, x.DeploymentRequested, x.Admitted,
                x.TargetEntityId, ParseArchetype(x.Archetype), x.Statuses.Select(ToDomain).ToArray())).ToArray(),
            file.Guards.Select(x => new InvasionGuardRuntimeSnapshot(
                x.EntityId, x.DefinitionId, ToDomain(x.Position), x.Hp, x.NextMoveTick, x.NextAttackTick, x.TargetEntityId,
                x.Statuses.Select(ToDomain).ToArray())).ToArray(),
            file.TrapCooldowns.Select(x => new InvasionCooldownSnapshot(x.Id, x.ReadyTick)).ToArray(),
            file.FacilityCooldowns.Select(x => new InvasionCooldownSnapshot(x.Id, x.ReadyTick)).ToArray(),
            file.SpellCooldowns.Select(x => new InvasionSpellCooldownSnapshot(x.SpellId, x.RemainingTicks)).ToArray(),
            file.Events.Select(x => new InvasionEvent(
                x.Tick, ParseEventType(x.Type), x.ActorId, x.TargetId, ToOptionalDomain(x.Position), x.Amount, x.Detail,
                ToOptionalDomain(x.SourcePosition), x.SourceDefinitionId)).ToArray());
        return new SuspendedInvasionState(file.LocationId, snapshot, file.IsFirstClearScenario, file.IsResolved);
    }

    private static CampaignDiscoveryCategory ParseDiscoveryCategory(string value)
        => Enum.TryParse<CampaignDiscoveryCategory>(value, ignoreCase: false, out var category)
            ? category
            : throw new InvalidDataException($"Unknown campaign discovery category: {value}.");

    private static DefenseOutcome ParseDefenseOutcome(string value)
        => Enum.TryParse<DefenseOutcome>(value, ignoreCase: false, out var outcome)
            ? outcome
            : throw new InvalidDataException($"Unknown defense record outcome: {value}.");

    private static InvasionOutcome ParseInvasionOutcome(string value)
        => Enum.TryParse<InvasionOutcome>(value, ignoreCase: false, out var outcome)
            ? outcome
            : throw new InvalidDataException($"Unknown invasion record outcome: {value}.");

    private static ChallengeMode ParseChallengeMode(string value)
        => Enum.TryParse<ChallengeMode>(value, ignoreCase: false, out var mode)
            ? mode
            : throw new InvalidDataException($"Unknown challenge record mode: {value}.");

    private static InvasionUnitArchetype ParseArchetype(string value)
        => Enum.TryParse<InvasionUnitArchetype>(value, ignoreCase: false, out var archetype)
            ? archetype
            : throw new InvalidDataException($"Unknown active invasion unit archetype: {value}.");

    private static InvasionEventType ParseEventType(string value)
        => Enum.TryParse<InvasionEventType>(value, ignoreCase: false, out var type)
            ? type
            : throw new InvalidDataException($"Unknown active invasion event type: {value}.");

    private static CampaignActiveInvasionStatusFile ToFile(InvasionStatusSnapshot value)
        => new(value.Kind.ToString(), value.Strength, value.RemainingTicks);

    private static InvasionStatusSnapshot ToDomain(CampaignActiveInvasionStatusFile value)
    {
        if (!Enum.TryParse<StatusKind>(value.Kind, ignoreCase: false, out var kind))
            throw new InvalidDataException($"Unknown active invasion status kind: {value.Kind}.");
        return new InvasionStatusSnapshot(kind, value.Strength, value.RemainingTicks);
    }

    private static CampaignGridPointFile ToFile(GridPoint value) => new(value.X, value.Y);
    private static CampaignGridPointFile? ToOptionalFile(GridPoint? value) => value is { } point ? ToFile(point) : null;
    private static GridPoint ToDomain(CampaignGridPointFile value) => new(value.X, value.Y);
    private static GridPoint? ToOptionalDomain(CampaignGridPointFile? value) => value is null ? null : ToDomain(value);

    private static CampaignResourceFile ToFile(ResourceBundle value)
        => new(value.Stone, value.Iron, value.Soul, value.Relic);

    private static ResourceBundle ToDomain(CampaignResourceFile value)
        => new(value.Stone, value.Iron, value.Soul, value.Relic);
}
