using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record SuspendedInvasionState(string LocationId, InvasionSimulationSnapshot Snapshot, bool IsFirstClearScenario = true, bool IsResolved = false);

public sealed record CampaignSaveImportResult(
    CampaignState State,
    DungeonFloorId SelectedFloorId,
    SuspendedInvasionState? ActiveInvasion);

public static class CampaignSaveService
{
    public static CampaignSaveFile Export(
        CampaignState state,
        DungeonFloorId selectedFloorId,
        string contentVersion,
        SuspendedInvasionState? activeInvasion = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Content version is required.", nameof(contentVersion));

        var dungeon = PlayerDungeonSaveService.Export(state.Dungeon, selectedFloorId);
        var resources = state.Resources;
        var realtime = state.Realtime;
        return new CampaignSaveFile(
            1,
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
                .ToArray());
    }

    public static CampaignSaveImportResult Import(CampaignSaveFile file, string expectedContentVersion)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.SchemaVersion != 1 || !string.Equals(file.Kind, "campaign_save", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported campaign save schema/kind.");
        if (!string.Equals(file.ContentVersion, expectedContentVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Campaign save content version mismatch: {file.ContentVersion} != {expectedContentVersion}.");
        if (file.Day <= 0) throw new InvalidDataException("Campaign Day must be positive.");
        if (string.IsNullOrWhiteSpace(file.RegionId)) throw new InvalidDataException("Campaign region_id is required.");

        var dungeon = PlayerDungeonSaveService.Import(file.Dungeon);
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
            PlayerDungeonSaveService.Import(x.Dungeon).Dungeon));
        var challengeBestScores = (file.ChallengeBestScores ?? []).ToDictionary(x => x.Key, x => x.BestScore, StringComparer.Ordinal);
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
            challengeBestScores);
        return new CampaignSaveImportResult(
            state,
            dungeon.SelectedFloorId,
            file.ActiveInvasion is null ? null : ToDomain(file.ActiveInvasion));
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
            snapshot.Seed,
            snapshot.Tick,
            snapshot.Mp,
            snapshot.UsedDeploymentCapacity,
            snapshot.SectionIndex,
            snapshot.SectionDefenseHp,
            snapshot.SectionAttackCooldown,
            snapshot.RetreatRemainingTicks,
            snapshot.Outcome.ToString(),
            ToFile(snapshot.SecuredLoot),
            snapshot.Units.Select(x => new CampaignActiveInvasionUnitFile(
                x.EntityId, x.UnitId, x.FormationIndex, x.Hp, x.Shield, x.AttackCooldownRemaining, x.Deployed,
                x.Archetype.ToString(), x.SectionDamagePercent, x.IncomingDamagePercent, x.AttackCooldownPercent)).ToArray(),
            snapshot.SpellCooldowns.Select(x => new CampaignActiveInvasionSpellCooldownFile(x.SpellId, x.RemainingTicks)).ToArray(),
            snapshot.Events.Select(x => new CampaignActiveInvasionEventFile(
                x.Tick, x.Type.ToString(), x.ActorId, x.TargetId, x.Amount, x.Detail)).ToArray(),
            active.IsFirstClearScenario,
            active.IsResolved);
    }

    private static SuspendedInvasionState ToDomain(CampaignActiveInvasionFile file)
    {
        if (!Enum.TryParse<InvasionOutcome>(file.Outcome, ignoreCase: false, out var outcome))
            throw new InvalidDataException($"Unknown active invasion outcome: {file.Outcome}.");
        var events = file.Events.Select(x =>
        {
            if (!Enum.TryParse<InvasionEventType>(x.Type, ignoreCase: false, out var type))
                throw new InvalidDataException($"Unknown active invasion event type: {x.Type}.");
            return new InvasionEvent(x.Tick, type, x.ActorId, x.TargetId, x.Amount, x.Detail);
        }).ToArray();
        var snapshot = new InvasionSimulationSnapshot(
            file.ContentVersion,
            file.FloorId,
            file.Seed,
            file.Tick,
            file.Mp,
            file.UsedDeploymentCapacity,
            file.SectionIndex,
            file.SectionDefenseHp,
            file.SectionAttackCooldown,
            file.RetreatRemainingTicks,
            outcome,
            ToDomain(file.SecuredLoot),
            file.Units.Select(x => new InvasionUnitRuntimeSnapshot(
                x.EntityId, x.UnitId, x.FormationIndex, x.Hp, x.Shield, x.AttackCooldownRemaining, x.Deployed,
                ParseArchetype(x.Archetype), x.SectionDamagePercent ?? 100, x.IncomingDamagePercent ?? 100, x.AttackCooldownPercent ?? 100)).ToArray(),
            file.SpellCooldowns.Select(x => new InvasionSpellCooldownSnapshot(x.SpellId, x.RemainingTicks)).ToArray(),
            events);
        return new SuspendedInvasionState(file.LocationId, snapshot, file.IsFirstClearScenario ?? true, file.IsResolved ?? false);
    }

    private static InvasionUnitArchetype ParseArchetype(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return InvasionUnitArchetype.Generalist;
        return Enum.TryParse<InvasionUnitArchetype>(value, ignoreCase: false, out var archetype)
            ? archetype
            : throw new InvalidDataException($"Unknown active invasion unit archetype: {value}.");
    }

    private static CampaignResourceFile ToFile(ResourceBundle value)
        => new(value.Stone, value.Iron, value.Soul, value.Relic);

    private static ResourceBundle ToDomain(CampaignResourceFile value)
        => new(value.Stone, value.Iron, value.Soul, value.Relic);
}
