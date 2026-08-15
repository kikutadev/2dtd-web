using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Web;

/// <summary>Loads production invasion content over HTTP and maps only transport DTOs into the production Core model.</summary>
internal static class WebInvasionContentLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly WebInvasionJsonContext JsonContext = new(Options);

    public static async Task<InvasionContent> LoadAsync(HttpClient http, DefenseContent defenseContent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        var json = await http.GetStringAsync("content/invasion-vertical-slice.json", cancellationToken);
        return Parse(json, defenseContent);
    }

    internal static InvasionContent Parse(string json, DefenseContent defenseContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(defenseContent);
        var dto = JsonSerializer.Deserialize(json, JsonContext.InvasionContentFile)
            ?? throw new InvalidDataException("Invasion content is empty.");
        if (dto.SchemaVersion != 1 || !string.Equals(dto.Kind, "invasion_content", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported invasion content schema/kind.");

        var deploymentCosts = dto.FormationUnits.ToDictionary(
            x => x.UnitId,
            x =>
            {
                if (!defenseContent.Units.TryGetValue(x.UnitId, out var unit) || unit.Team != Team.Dungeon)
                    throw new InvalidDataException($"Unknown dungeon invasion unit: {x.UnitId}");
                return x.DeploymentCost;
            },
            StringComparer.Ordinal);
        var roles = dto.FormationUnits.ToDictionary(
            x => x.UnitId,
            x => new InvasionUnitRoleProfile(
                x.UnitId,
                ParseArchetype(x.Archetype),
                x.SectionDamagePercent,
                x.IncomingDamagePercent,
                x.AttackCooldownPercent),
            StringComparer.Ordinal);
        var spells = dto.Spells.ToDictionary(
            x => x.Id,
            x => new InvasionSupportSpellDefinition(x.Id, ParseSpellKind(x.Kind), x.MpCost, x.CooldownTicks, x.Magnitude),
            StringComparer.Ordinal);
        var locations = dto.Locations.Select(location => new InvasionLocationDefinition(
            location.Id,
            location.Category,
            location.Floors.Select(floor => new InvasionFloorDefinition(
                floor.Id,
                floor.Depth,
                ParseObjective(floor.Objective),
                floor.ThreatTags,
                floor.Sections.Select(section => new InvasionSectionDefinition(
                    section.Id,
                    section.DefenseHp,
                    section.DefenseDamage,
                    section.DefenseAttackCooldownTicks,
                    section.Loot.ToDomain())).ToArray(),
                floor.FirstClearReward.ToDomain(),
                floor.RepeatReward.ToDomain(),
                floor.RegenerationMinutes,
                floor.RepeatVariation?.ToDomain() ?? default)).ToArray(),
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
            deploymentCosts,
            spells,
            locations,
            roles);
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

    internal sealed record InvasionContentFile(
        int SchemaVersion,
        string Kind,
        string ContentVersion,
        int DeploymentCapacity,
        int RetreatDisengageTicks,
        int WipeLootPercent,
        int MaxMp,
        int MpChargePerTick,
        FormationUnitFile[] FormationUnits,
        SpellFile[] Spells,
        LocationFile[] Locations);
    internal sealed record FormationUnitFile(
        string UnitId,
        int DeploymentCost,
        string Archetype = "GENERALIST",
        int SectionDamagePercent = 100,
        int IncomingDamagePercent = 100,
        int AttackCooldownPercent = 100);
    internal sealed record SpellFile(string Id, string Kind, int MpCost, int CooldownTicks, int Magnitude);
    internal sealed record LocationFile(string Id, string Category, FloorFile[] Floors, int RequiredDay = 1, string[]? RequiredResearchIds = null, string[]? RequiredRegionIds = null);
    internal sealed record FloorFile(
        string Id,
        int Depth,
        string Objective,
        string[] ThreatTags,
        SectionFile[] Sections,
        ResourceFile FirstClearReward,
        ResourceFile RepeatReward,
        int RegenerationMinutes,
        RepeatVariationFile? RepeatVariation = null);
    internal sealed record RepeatVariationFile(int DefenseHpPercent = 0, int DefenseDamagePercent = 0, int AttackCooldownJitterTicks = 0, int LootPercent = 0)
    {
        public InvasionRepeatVariationDefinition ToDomain() => new(DefenseHpPercent, DefenseDamagePercent, AttackCooldownJitterTicks, LootPercent);
    }
    internal sealed record SectionFile(string Id, int DefenseHp, int DefenseDamage, int DefenseAttackCooldownTicks, ResourceFile Loot);
    internal sealed record ResourceFile(int Stone, int Iron, int Soul, int Relic)
    {
        public ResourceBundle ToDomain() => new(Stone, Iron, Soul, Relic);
    }
}

[JsonSerializable(typeof(WebInvasionContentLoader.InvasionContentFile))]
internal sealed partial class WebInvasionJsonContext : JsonSerializerContext;
