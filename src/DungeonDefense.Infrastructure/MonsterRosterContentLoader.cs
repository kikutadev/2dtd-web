using System.Text.Json;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class MonsterRosterContentLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly MonsterRosterJsonContext JsonContext = new(Options);

    public static MonsterRosterContent Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize(json, JsonContext.MonsterRosterFile)
            ?? throw new InvalidDataException("Monster roster content is empty.");
        return Materialize(dto);
    }

    public static MonsterRosterContent LoadFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize(json, JsonContext.MonsterRosterFile)
            ?? throw new InvalidDataException("Monster roster content is empty.");
        return Materialize(dto);
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "content", "monster-roster.json");
            if (File.Exists(candidate)) return candidate;
            var projectCandidate = Path.Combine(current.FullName, "godot", "content", "monster-roster.json");
            if (File.Exists(projectCandidate)) return projectCandidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Could not find content/monster-roster.json from the current directory hierarchy.");
    }

    private static MonsterRosterContent Materialize(MonsterRosterFile dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ContentVersion)) throw new InvalidDataException("Monster roster content_version is required.");
        if (dto.Monsters.Count == 0) throw new InvalidDataException("Monster roster requires at least one monster.");
        if (dto.Monsters.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("Monster roster IDs must be unique.");

        var monsters = dto.Monsters.Select(x =>
        {
            if (string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.SpeciesId) || string.IsNullOrWhiteSpace(x.AssetId))
                throw new InvalidDataException("Monster identity fields are required.");
            var defense = new MonsterDefenseProfile(x.Defense.CapacityCost, x.Defense.GuardZoneRadius, x.Defense.Blocks).Validate();
            var invasion = new MonsterInvasionProfile(x.Invasion.DeploymentCost, ParseArchetype(x.Invasion.Archetype)).Validate();
            var progression = new MonsterProgressionProfile(x.Progression.RequiredUnlockId).Validate();
            var combat = new UnitDefinition(
                x.Id,
                Team.Dungeon,
                Enum.Parse<UnitRole>(x.Combat.Role, ignoreCase: true),
                x.Combat.MaxHp,
                x.Combat.Damage,
                x.Combat.AttackRange,
                x.Combat.AttackCooldownTicks,
                x.Combat.MoveIntervalTicks,
                defense.Blocks,
                defense.GuardZoneRadius,
                x.Combat.HealPower,
                BodySizeClass.Standard,
                ParseNullable<StatusKind>(x.Combat.AttackStatusKind),
                x.Combat.AttackStatusStrength,
                x.Combat.AttackStatusDurationTicks);
            ValidateCombat(combat);
            return new MonsterDefinition(x.Id, x.SpeciesId, combat, defense, invasion, progression, x.AssetId).Validate();
        }).ToArray();
        return new MonsterRosterContent(dto.ContentVersion, monsters);
    }

    private static void ValidateCombat(UnitDefinition unit)
    {
        if (unit.MaxHp <= 0 || unit.Damage < 0 || unit.AttackRange < 1 || unit.AttackCooldownTicks <= 0 || unit.MoveIntervalTicks <= 0 || unit.HealPower < 0)
            throw new InvalidDataException($"Invalid monster combat definition: {unit.Id}.");
        if (unit.AttackStatusKind is null && (unit.AttackStatusStrength != 0 || unit.AttackStatusDurationTicks != 0))
            throw new InvalidDataException($"Monster attack status parameters require attack_status_kind: {unit.Id}.");
        if (unit.AttackStatusKind is not null && (unit.AttackStatusStrength <= 0 || unit.AttackStatusDurationTicks <= 0))
            throw new InvalidDataException($"Monster attack status parameters are invalid: {unit.Id}.");
    }

    private static TEnum? ParseNullable<TEnum>(string? value) where TEnum : struct, Enum
        => string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<TEnum>(value, ignoreCase: true);

    private static InvasionUnitArchetype ParseArchetype(string value) => value switch
    {
        "GENERALIST" => InvasionUnitArchetype.Generalist,
        "VANGUARD" => InvasionUnitArchetype.Vanguard,
        "BACKLINE_STRIKER" => InvasionUnitArchetype.BacklineStriker,
        "SUPPORT" => InvasionUnitArchetype.Support,
        "SWARM" => InvasionUnitArchetype.Swarm,
        "SIEGE" => InvasionUnitArchetype.Siege,
        _ => throw new InvalidDataException($"Unknown monster invasion archetype: {value}"),
    };

    internal sealed class MonsterRosterFile
    {
        public string ContentVersion { get; set; } = "";
        public List<MonsterDto> Monsters { get; set; } = [];
    }

    internal sealed class MonsterDto
    {
        public string Id { get; set; } = "";
        public string SpeciesId { get; set; } = "";
        public string AssetId { get; set; } = "";
        public MonsterCombatDto Combat { get; set; } = new();
        public MonsterDefenseDto Defense { get; set; } = new();
        public MonsterInvasionDto Invasion { get; set; } = new();
        public MonsterProgressionDto Progression { get; set; } = new();
    }

    internal sealed class MonsterCombatDto
    {
        public string Role { get; set; } = "Fighter";
        public int MaxHp { get; set; }
        public int Damage { get; set; }
        public int AttackRange { get; set; }
        public int AttackCooldownTicks { get; set; }
        public int MoveIntervalTicks { get; set; }
        public int HealPower { get; set; }
        public string? AttackStatusKind { get; set; }
        public int AttackStatusStrength { get; set; }
        public int AttackStatusDurationTicks { get; set; }
    }

    internal sealed class MonsterDefenseDto
    {
        public int CapacityCost { get; set; }
        public int GuardZoneRadius { get; set; }
        public bool Blocks { get; set; }
    }

    internal sealed class MonsterInvasionDto
    {
        public int DeploymentCost { get; set; }
        public string Archetype { get; set; } = "GENERALIST";
    }

    internal sealed class MonsterProgressionDto
    {
        public string? RequiredUnlockId { get; set; }
    }
}
