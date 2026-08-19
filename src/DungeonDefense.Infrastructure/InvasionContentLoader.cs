using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class InvasionContentLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly InvasionContentJsonContext JsonContext = new(Options);

    public static InvasionContent Load(string path, DefenseContent sharedCombatSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var rosterPath = Path.Combine(directory, "monster-roster.json");
        var roster = File.Exists(rosterPath) ? MonsterRosterContentLoader.Load(rosterPath) : null;
        return Load(path, sharedCombatSource, roster);
    }

    public static InvasionContent Load(string path, DefenseContent sharedCombatSource, MonsterRosterContent? roster)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var mapPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "invasion-maps.json");
        if (!File.Exists(mapPath)) throw new FileNotFoundException("Invasion spatial map file is required next to invasion metadata.", mapPath);
        return LoadFromJson(File.ReadAllText(path), File.ReadAllText(mapPath), sharedCombatSource, roster);
    }

    /// <summary>
    /// Decodes invasion metadata and authored spatial maps without owning transport.
    /// Native hosts read files; WebAssembly fetches the same JSON over HTTP.
    /// </summary>
    public static InvasionContent LoadFromJson(string metadataJson, string mapJson, DefenseContent sharedCombatSource, MonsterRosterContent? roster = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapJson);
        ArgumentNullException.ThrowIfNull(sharedCombatSource);
        var dto = JsonSerializer.Deserialize(metadataJson, JsonContext.InvasionContentFile)
            ?? throw new InvalidDataException("Invasion content is empty.");
        if (dto.SchemaVersion != 2 || !string.Equals(dto.Kind, "invasion_content", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported invasion content schema/kind.");

        var combat = DungeonCombatContent.FromDefenseContent(sharedCombatSource);
        IReadOnlyDictionary<string, int> deploymentCosts;
        IReadOnlyDictionary<string, InvasionUnitRoleProfile> roleProfiles;
        if (roster is not null)
        {
            deploymentCosts = roster.Monsters.ToDictionary(x => x.Id, x => x.Invasion.DeploymentCost, StringComparer.Ordinal);
            roleProfiles = roster.Monsters.ToDictionary(
                x => x.Id,
                x => new InvasionUnitRoleProfile(x.Id, x.Invasion.Archetype),
                StringComparer.Ordinal);
        }
        else
        {
            var legacyFormation = dto.FormationUnits ?? [];
            if (legacyFormation.Length == 0)
                throw new InvalidDataException("Invasion roster must come from MonsterRosterContent; legacy formation_units are absent.");
            deploymentCosts = legacyFormation.ToDictionary(x => x.UnitId, x =>
            {
                if (!combat.Units.TryGetValue(x.UnitId, out var unit) || unit.Team != Team.Dungeon)
                    throw new InvalidDataException($"Unknown dungeon invasion unit: {x.UnitId}");
                return x.DeploymentCost;
            }, StringComparer.Ordinal);
            roleProfiles = legacyFormation.ToDictionary(
                x => x.UnitId,
                x => new InvasionUnitRoleProfile(x.UnitId, ParseArchetype(x.Archetype)),
                StringComparer.Ordinal);
        }
        var spells = dto.Spells.ToDictionary(
            x => x.Id,
            x => new InvasionSupportSpellDefinition(x.Id, ParseSpellKind(x.Kind), x.MpCost, x.CooldownTicks, x.Magnitude),
            StringComparer.Ordinal);

        var floorMetadata = dto.Locations.SelectMany(location => location.Floors).ToDictionary(
            floor => floor.Id,
            floor => new InvasionFloorMetadata(
                floor.Id,
                floor.Depth,
                ParseObjective(floor.Objective),
                floor.ThreatTags,
                floor.Sections.ToDictionary(x => x.Id, x => x.Loot.ToDomain(), StringComparer.Ordinal),
                floor.FirstClearReward.ToDomain(),
                floor.RepeatReward.ToDomain(),
                floor.RegenerationMinutes,
                floor.RepeatVariation?.ToDomain() ?? default),
            StringComparer.Ordinal);
        var floors = InvasionSpatialMapLoader.LoadFromJson(mapJson, floorMetadata, combat);
        var locations = dto.Locations.Select(location => new InvasionLocationDefinition(
            location.Id,
            location.Category,
            location.Floors.Select(x => floors[x.Id]).ToArray(),
            location.RequiredDay,
            location.RequiredResearchIds ?? [],
            location.RequiredRegionIds ?? [])).ToArray();

        return new InvasionContent(
            dto.ContentVersion,
            dto.DeploymentCapacity,
            dto.MaxMp,
            dto.MpChargePerTick,
            dto.RetreatDisengageTicks,
            dto.WipeLootPercent,
            combat,
            deploymentCosts,
            spells,
            locations,
            roleProfiles,
            roster);
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "content", "invasion-vertical-slice.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate content/invasion-vertical-slice.json.");
    }

    private static InvasionObjectiveKind ParseObjective(string value) => value switch
    {
        "RAID" => InvasionObjectiveKind.Raid,
        "ELIMINATE" => InvasionObjectiveKind.Eliminate,
        "CORE_BREAK" => InvasionObjectiveKind.CoreBreak,
        _ => throw new InvalidDataException($"Unknown invasion objective: {value}"),
    };

    private static InvasionUnitArchetype ParseArchetype(string value) => value switch
    {
        "GENERALIST" => InvasionUnitArchetype.Generalist,
        "VANGUARD" => InvasionUnitArchetype.Vanguard,
        "BACKLINE_STRIKER" => InvasionUnitArchetype.BacklineStriker,
        "SUPPORT" => InvasionUnitArchetype.Support,
        "SWARM" => InvasionUnitArchetype.Swarm,
        "SIEGE" => InvasionUnitArchetype.Siege,
        _ => throw new InvalidDataException($"Unknown invasion unit archetype: {value}"),
    };

    private static InvasionSupportSpellKind ParseSpellKind(string value) => value switch
    {
        "HEAL" => InvasionSupportSpellKind.Heal,
        "SHIELD" => InvasionSupportSpellKind.Shield,
        _ => throw new InvalidDataException($"Unknown invasion support spell kind: {value}"),
    };

    internal sealed record InvasionContentFile(int SchemaVersion, string Kind, string ContentVersion, int DeploymentCapacity,
        int RetreatDisengageTicks, int WipeLootPercent, int MaxMp, int MpChargePerTick, FormationUnitFile[]? FormationUnits,
        SpellFile[] Spells, LocationFile[] Locations);
    internal sealed record FormationUnitFile(string UnitId, int DeploymentCost, string Archetype = "GENERALIST");
    internal sealed record SpellFile(string Id, string Kind, int MpCost, int CooldownTicks, int Magnitude);
    internal sealed record LocationFile(string Id, string Category, FloorFile[] Floors, int RequiredDay = 1,
        string[]? RequiredResearchIds = null, string[]? RequiredRegionIds = null);
    internal sealed record FloorFile(string Id, int Depth, string Objective, string[] ThreatTags, SectionFile[] Sections,
        ResourceFile FirstClearReward, ResourceFile RepeatReward, int RegenerationMinutes, RepeatVariationFile? RepeatVariation = null);
    internal sealed record RepeatVariationFile(int LootPercent = 0)
    {
        public InvasionRepeatVariationDefinition ToDomain() => new(LootPercent);
    }
    internal sealed record SectionFile(string Id, ResourceFile Loot);
    internal sealed record ResourceFile(int Stone, int Iron, int Soul, int Relic)
    {
        public ResourceBundle ToDomain() => new(Stone, Iron, Soul, Relic);
    }
}
