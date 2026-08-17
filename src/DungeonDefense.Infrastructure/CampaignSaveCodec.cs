using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

public static class CampaignSaveCodec
{
    public const int SchemaVersion = 2;
    public const int MaxFileBytes = 8 * 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly CampaignSaveJsonContext JsonContext = new(JsonOptions);

    public static CampaignSaveFile Load(string path) => Parse(ReadTextBounded(path));

    public static CampaignSaveFile Parse(string json)
    {
        CampaignSaveFile file;
        try
        {
            file = JsonSerializer.Deserialize(json, JsonContext.CampaignSaveFile)
                ?? throw new InvalidDataException("Empty campaign save JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid campaign save JSON: {ex.Message}", ex);
        }
        Validate(file);
        return file;
    }

    public static string Serialize(CampaignSaveFile file)
    {
        Validate(file);
        var canonical = file with
        {
            CompletedResearchIds = file.CompletedResearchIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            UnlockIds = file.UnlockIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            SpeciesLevels = file.SpeciesLevels.OrderBy(x => x.SpeciesId, StringComparer.Ordinal).ToArray(),
            InvasionProgress = file.InvasionProgress.OrderBy(x => x.LocationId, StringComparer.Ordinal)
                .Select(x => x with { ClearedFloorIds = x.ClearedFloorIds.OrderBy(id => id, StringComparer.Ordinal).ToArray() }).ToArray(),
            Dungeon = PlayerDungeonSaveCodec.Parse(PlayerDungeonSaveCodec.Serialize(file.Dungeon)),
            Realtime = file.Realtime is null ? null : file.Realtime with
            {
                InvasionRegeneration = file.Realtime.InvasionRegeneration
                    .OrderBy(x => x.LocationId, StringComparer.Ordinal)
                    .ThenBy(x => x.FloorId, StringComparer.Ordinal)
                    .ToArray(),
            },
            ActiveInvasion = file.ActiveInvasion is null ? null : file.ActiveInvasion with
            {
                ClearedSectionIds = file.ActiveInvasion.ClearedSectionIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Units = file.ActiveInvasion.Units.OrderBy(x => x.FormationIndex).ToArray(),
                Guards = file.ActiveInvasion.Guards.OrderBy(x => x.EntityId, StringComparer.Ordinal).ToArray(),
                TrapCooldowns = file.ActiveInvasion.TrapCooldowns.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
                FacilityCooldowns = file.ActiveInvasion.FacilityCooldowns.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
                SpellCooldowns = file.ActiveInvasion.SpellCooldowns.OrderBy(x => x.SpellId, StringComparer.Ordinal).ToArray(),
            },
            ClearedDungeons = (file.ClearedDungeons ?? []).OrderBy(x => x.ArchiveId, StringComparer.Ordinal)
                .Select(x => x with { Dungeon = PlayerDungeonSaveCodec.Parse(PlayerDungeonSaveCodec.Serialize(x.Dungeon)) }).ToArray(),
            ChallengeBestScores = (file.ChallengeBestScores ?? []).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
        };
        return JsonSerializer.Serialize(canonical, JsonContext.CampaignSaveFile).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
    }

    /// <summary>
    /// Writes via a temporary file in the same directory and atomically renames it over the destination.
    /// A failed write never intentionally truncates the previous save.
    /// </summary>
    public static void SaveAtomic(string path, CampaignSaveFile file)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Save path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(file));
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void Validate(CampaignSaveFile file)
    {
        if (file.SchemaVersion != SchemaVersion) throw new InvalidDataException($"Unsupported campaign save schema_version: {file.SchemaVersion}.");
        if (!string.Equals(file.Kind, "campaign_save", StringComparison.Ordinal)) throw new InvalidDataException($"Unexpected campaign save kind: {file.Kind}.");
        if (string.IsNullOrWhiteSpace(file.ContentVersion)) throw new InvalidDataException("content_version is required.");
        if (file.Day <= 0) throw new InvalidDataException("day must be positive.");
        if (string.IsNullOrWhiteSpace(file.RegionId)) throw new InvalidDataException("region_id is required.");
        ArgumentNullException.ThrowIfNull(file.Resources);
        if (file.Resources.Stone < 0 || file.Resources.Iron < 0 || file.Resources.Soul < 0 || file.Resources.Relic < 0)
            throw new InvalidDataException("resources cannot be negative.");
        ArgumentNullException.ThrowIfNull(file.CompletedResearchIds);
        ArgumentNullException.ThrowIfNull(file.UnlockIds);
        ArgumentNullException.ThrowIfNull(file.SpeciesLevels);
        ArgumentNullException.ThrowIfNull(file.InvasionProgress);
        ArgumentNullException.ThrowIfNull(file.Dungeon);
        EnsureUnique(file.CompletedResearchIds, "completed research ID");
        EnsureUnique(file.UnlockIds, "unlock ID");
        EnsureUnique(file.SpeciesLevels.Select(x => x.SpeciesId), "species ID");
        EnsureUnique(file.InvasionProgress.Select(x => x.LocationId), "invasion location progress");
        foreach (var progress in file.InvasionProgress)
        {
            if (string.IsNullOrWhiteSpace(progress.LocationId) || progress.UnlockedDepth <= 0)
                throw new InvalidDataException("Invalid invasion progress entry.");
            ArgumentNullException.ThrowIfNull(progress.ClearedFloorIds);
            EnsureUnique(progress.ClearedFloorIds, $"cleared floor ID for {progress.LocationId}");
        }
        foreach (var species in file.SpeciesLevels)
        {
            if (string.IsNullOrWhiteSpace(species.SpeciesId) || species.Level < 0)
                throw new InvalidDataException("Invalid species progression entry.");
        }
        ValidateRealtime(file.Realtime, file.Resources);
        ValidateActiveInvasion(file.ActiveInvasion);
        ValidateClearedDungeons(file.ClearedDungeons);
        ValidateChallengeBestScores(file.ChallengeBestScores);
        _ = PlayerDungeonSaveCodec.Serialize(file.Dungeon);
    }

    private static void ValidateRealtime(CampaignRealtimeFile? realtime, CampaignResourceFile resources)
    {
        if (realtime is null) return;
        if (realtime.LastObservedUtc.Offset != TimeSpan.Zero || realtime.EffectiveUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Campaign realtime timestamps must be UTC.");
        if (realtime.EffectiveUtc > realtime.LastObservedUtc)
            throw new InvalidDataException("Campaign effective_utc cannot be later than last_observed_utc.");
        ArgumentNullException.ThrowIfNull(realtime.Production);
        if (realtime.Production.StoneUnits < 0 || realtime.Production.IronUnits < 0 || realtime.Production.SoulUnits < 0)
            throw new InvalidDataException("Campaign production accumulator cannot be negative.");
        EnsureCollectableProduction(realtime.Production.StoneUnits, resources.Stone, "stone");
        EnsureCollectableProduction(realtime.Production.IronUnits, resources.Iron, "iron");
        EnsureCollectableProduction(realtime.Production.SoulUnits, resources.Soul, "soul");
        ArgumentNullException.ThrowIfNull(realtime.InvasionRegeneration);
        EnsureUnique(realtime.InvasionRegeneration.Select(x => $"{x.LocationId}\u001f{x.FloorId}"), "invasion regeneration entry");
        foreach (var entry in realtime.InvasionRegeneration)
        {
            if (string.IsNullOrWhiteSpace(entry.LocationId) || string.IsNullOrWhiteSpace(entry.FloorId) || entry.ReadyAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Invalid invasion regeneration entry.");
        }
    }

    private static void EnsureCollectableProduction(long accumulatorUnits, int currentResource, string resourceName)
    {
        var available = accumulatorUnits / 3600L;
        if (available > int.MaxValue - (long)currentResource)
            throw new InvalidDataException($"Campaign {resourceName} production would overflow the resource balance when collected.");
    }

    private static void ValidateActiveInvasion(CampaignActiveInvasionFile? active)
    {
        if (active is null) return;
        if (string.IsNullOrWhiteSpace(active.ContentVersion) || string.IsNullOrWhiteSpace(active.LocationId) || string.IsNullOrWhiteSpace(active.FloorId)
            || string.IsNullOrWhiteSpace(active.MapDigest))
            throw new InvalidDataException("Active invasion identity is required.");
        if (active.Tick < 0 || active.Mp < 0 || active.UsedDeploymentCapacity <= 0 || active.RetreatRemainingTicks is < 0 || active.ObjectiveStructureHp < 0)
            throw new InvalidDataException("Active invasion counters are invalid.");
        if (!Enum.TryParse<DungeonDefense.Core.InvasionOutcome>(active.Outcome, ignoreCase: false, out var outcome))
            throw new InvalidDataException($"Unknown active invasion outcome: {active.Outcome}.");
        if (active.IsResolved && outcome == DungeonDefense.Core.InvasionOutcome.Running)
            throw new InvalidDataException("A running invasion cannot already be resolved.");
        ArgumentNullException.ThrowIfNull(active.SecuredLoot);
        if (active.SecuredLoot.Stone < 0 || active.SecuredLoot.Iron < 0 || active.SecuredLoot.Soul < 0 || active.SecuredLoot.Relic < 0)
            throw new InvalidDataException("Active invasion secured loot cannot be negative.");
        ArgumentNullException.ThrowIfNull(active.ClearedSectionIds);
        ArgumentNullException.ThrowIfNull(active.Units);
        ArgumentNullException.ThrowIfNull(active.Guards);
        ArgumentNullException.ThrowIfNull(active.TrapCooldowns);
        ArgumentNullException.ThrowIfNull(active.FacilityCooldowns);
        ArgumentNullException.ThrowIfNull(active.SpellCooldowns);
        ArgumentNullException.ThrowIfNull(active.Events);
        EnsureUnique(active.ClearedSectionIds, "active invasion cleared section ID");
        if (active.Units.Count == 0) throw new InvalidDataException("Active invasion must contain units.");
        EnsureUnique(active.Units.Select(x => x.EntityId), "active invasion entity ID");
        EnsureUnique(active.Units.Select(x => x.FormationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)), "active invasion formation index");
        foreach (var unit in active.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.EntityId) || string.IsNullOrWhiteSpace(unit.DefinitionId) || unit.FormationIndex < 0
                || unit.Position is null || unit.Hp < 0 || unit.Shield < 0 || unit.RouteProgressUnits < 0 || unit.PathIndex < 0
                || unit.MoveRemainder < 0 || unit.NextMoveTick < 0 || unit.NextAttackTick < 0
                || !Enum.TryParse<DungeonDefense.Core.InvasionUnitArchetype>(unit.Archetype, ignoreCase: false, out _))
                throw new InvalidDataException("Invalid active invasion unit runtime.");
            ValidateStatuses(unit.Statuses);
        }
        EnsureUnique(active.Guards.Select(x => x.EntityId), "active invasion guard ID");
        foreach (var guard in active.Guards)
        {
            if (string.IsNullOrWhiteSpace(guard.EntityId) || string.IsNullOrWhiteSpace(guard.DefinitionId) || guard.Position is null
                || guard.Hp < 0 || guard.NextMoveTick < 0 || guard.NextAttackTick < 0)
                throw new InvalidDataException("Invalid active invasion guard runtime.");
            ValidateStatuses(guard.Statuses);
        }
        ValidateCooldowns(active.TrapCooldowns, "trap");
        ValidateCooldowns(active.FacilityCooldowns, "facility");
        EnsureUnique(active.SpellCooldowns.Select(x => x.SpellId), "active invasion spell cooldown");
        if (active.SpellCooldowns.Any(x => string.IsNullOrWhiteSpace(x.SpellId) || x.RemainingTicks < 0))
            throw new InvalidDataException("Invalid active invasion spell cooldown.");
        foreach (var e in active.Events)
        {
            if (e.Tick < 0 || string.IsNullOrWhiteSpace(e.ActorId)
                || !Enum.TryParse<DungeonDefense.Core.InvasionEventType>(e.Type, ignoreCase: false, out _))
                throw new InvalidDataException("Invalid active invasion event.");
        }
    }

    private static void ValidateStatuses(IReadOnlyList<CampaignActiveInvasionStatusFile> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        EnsureUnique(statuses.Select(x => x.Kind), "active invasion status kind");
        if (statuses.Any(x => x.Strength < 0 || x.RemainingTicks <= 0 || !Enum.TryParse<DungeonDefense.Core.StatusKind>(x.Kind, false, out _)))
            throw new InvalidDataException("Invalid active invasion status.");
    }

    private static void ValidateCooldowns(IReadOnlyList<CampaignActiveInvasionCooldownFile> cooldowns, string kind)
    {
        EnsureUnique(cooldowns.Select(x => x.Id), $"active invasion {kind} cooldown");
        if (cooldowns.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.ReadyTick < 0))
            throw new InvalidDataException($"Invalid active invasion {kind} cooldown.");
    }

    private static void ValidateClearedDungeons(IReadOnlyList<CampaignClearedDungeonFile>? archives)
    {
        if (archives is null) return;
        EnsureUnique(archives.Select(x => x.ArchiveId), "cleared dungeon archive ID");
        foreach (var archive in archives)
        {
            if (string.IsNullOrWhiteSpace(archive.ArchiveId) || string.IsNullOrWhiteSpace(archive.RegionId)
                || archive.ClearedDay <= 0 || string.IsNullOrWhiteSpace(archive.FinalAssaultProfileId))
                throw new InvalidDataException("Invalid cleared dungeon archive entry.");
            ArgumentNullException.ThrowIfNull(archive.Dungeon);
            _ = PlayerDungeonSaveCodec.Serialize(archive.Dungeon);
        }
    }

    private static void ValidateChallengeBestScores(IReadOnlyList<CampaignChallengeBestFile>? scores)
    {
        if (scores is null) return;
        EnsureUnique(scores.Select(x => x.Key), "challenge best score key");
        foreach (var score in scores)
        {
            if (string.IsNullOrWhiteSpace(score.Key) || score.BestScore < 0)
                throw new InvalidDataException("Invalid challenge best score entry.");
        }
    }

    private static string ReadTextBounded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Campaign save not found.", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException($"Campaign save exceeds {MaxFileBytes} bytes.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate {label}: {duplicate.Key}");
    }
}
