using System.Text.Json;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class VerticalSliceContentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly VerticalSliceJsonContext JsonContext = new(Options);

    public static DefenseContent Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize(json, JsonContext.DefenseContentDto)
            ?? throw new InvalidDataException("Defense content is empty.");
        Validate(dto);

        var units = dto.Units.ToDictionary(x => x.Id, x => new UnitDefinition(
            x.Id,
            Enum.Parse<Team>(x.Team, true),
            Enum.Parse<UnitRole>(x.Role, true),
            x.MaxHp,
            x.Damage,
            x.AttackRange,
            x.AttackCooldownTicks,
            x.MoveIntervalTicks,
            x.Blocks,
            x.GuardZoneRadius,
            x.HealPower), StringComparer.Ordinal);

        var traps = dto.Traps.ToDictionary(x => x.Id, x => new TrapDefinition(
            x.Id, x.Damage, x.CooldownTicks, ParseNullable<StatusKind>(x.StatusKind), x.StatusStrength, x.StatusDurationTicks), StringComparer.Ordinal);
        var facilities = dto.Facilities.ToDictionary(x => x.Id, x => new FacilityDefinition(
            x.Id, x.Damage, x.Range, x.CooldownTicks, ParseNullable<StatusKind>(x.StatusKind), x.StatusStrength, x.StatusDurationTicks), StringComparer.Ordinal);
        var spells = dto.Spells.ToDictionary(x => x.Id, x => new SpellDefinition(
            x.Id, Enum.Parse<SpellKind>(x.Kind, true), x.MpCost, x.CooldownTicks, x.Radius, x.DurationTicks, x.Magnitude), StringComparer.Ordinal);
        var waves = dto.Waves.Select(x => new WaveDefinition(x.Id, x.InterWaveTicks,
            x.SpawnGroups.Select(g => new SpawnGroupDefinition(g.UnitId, g.Count, g.InitialDelayTicks, g.SpawnIntervalTicks)).ToArray())).ToArray();
        var bossRouteBreaks = dto.BossRouteBreaks.ToDictionary(
            x => x.UnitId,
            x => new BossRouteBreakDefinition(
                x.UnitId,
                Enum.Parse<BossRouteBreakKind>(x.Kind, true),
                x.TriggerPathPercent,
                x.TelegraphTicks,
                x.SkipRouteCells,
                x.MaxUsesPerFloor).Validate(),
            StringComparer.Ordinal);

        foreach (var wave in waves)
        foreach (var group in wave.SpawnGroups)
            if (!units.ContainsKey(group.UnitId)) throw new InvalidDataException($"Wave references unknown unit: {group.UnitId}");

        return new DefenseContent
        {
            Units = units,
            Traps = traps,
            Facilities = facilities,
            Spells = spells,
            Waves = waves,
            BossRouteBreaks = bossRouteBreaks,
            CoreMaxHp = dto.CoreMaxHp,
            MaxMp = dto.MaxMp,
            MpChargePerTick = dto.MpChargePerTick,
        };
    }

    public static string FindDefaultContentPath(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "content", "vertical-slice.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Could not find content/vertical-slice.json from the current directory hierarchy.");
    }

    private static TEnum? ParseNullable<TEnum>(string? value) where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<TEnum>(value, true);

    private static void Validate(DefenseContentDto dto)
    {
        if (dto.CoreMaxHp <= 0 || dto.MaxMp <= 0 || dto.MpChargePerTick < 0) throw new InvalidDataException("Invalid defense global values.");
        EnsureUnique(dto.Units.Select(x => x.Id), "unit");
        EnsureUnique(dto.Traps.Select(x => x.Id), "trap");
        EnsureUnique(dto.Facilities.Select(x => x.Id), "facility");
        EnsureUnique(dto.Spells.Select(x => x.Id), "spell");
        EnsureUnique(dto.Waves.Select(x => x.Id), "wave");
        EnsureUnique(dto.BossRouteBreaks.Select(x => x.UnitId), "boss route-break unit");
        if (dto.Waves.Count == 0) throw new InvalidDataException("At least one wave is required.");
        foreach (var unit in dto.Units)
        {
            if (unit.MaxHp <= 0 || unit.AttackCooldownTicks <= 0 || unit.MoveIntervalTicks <= 0 || unit.AttackRange < 1)
                throw new InvalidDataException($"Invalid unit definition: {unit.Id}");
        }
        foreach (var wave in dto.Waves)
        foreach (var group in wave.SpawnGroups)
            if (group.Count <= 0 || group.InitialDelayTicks < 0 || group.SpawnIntervalTicks < 0)
                throw new InvalidDataException($"Invalid spawn group in wave: {wave.Id}");
        var unitsById = dto.Units.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var boss in dto.BossRouteBreaks)
        {
            if (!unitsById.TryGetValue(boss.UnitId, out var unit) || !string.Equals(unit.Team, "Invader", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Boss route-break must reference an Invader unit: {boss.UnitId}");
            _ = new BossRouteBreakDefinition(boss.UnitId, Enum.Parse<BossRouteBreakKind>(boss.Kind, true), boss.TriggerPathPercent, boss.TelegraphTicks, boss.SkipRouteCells, boss.MaxUsesPerFloor).Validate();
            var totalSpawnCount = dto.Waves.SelectMany(x => x.SpawnGroups).Where(x => string.Equals(x.UnitId, boss.UnitId, StringComparison.Ordinal)).Sum(x => x.Count);
            if (totalSpawnCount > 1)
                throw new InvalidDataException($"Boss route-break unit may spawn at most once across the encounter: {boss.UnitId} count={totalSpawnCount}.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> ids, string category)
    {
        var duplicates = ids.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidDataException($"Duplicate {category} IDs: {string.Join(", ", duplicates)}");
    }

    internal sealed class DefenseContentDto
    {
        public int CoreMaxHp { get; set; }
        public int MaxMp { get; set; }
        public int MpChargePerTick { get; set; }
        public List<UnitDto> Units { get; set; } = [];
        public List<TrapDto> Traps { get; set; } = [];
        public List<FacilityDto> Facilities { get; set; } = [];
        public List<SpellDto> Spells { get; set; } = [];
        public List<WaveDto> Waves { get; set; } = [];
        public List<BossRouteBreakDto> BossRouteBreaks { get; set; } = [];
    }

    internal sealed class UnitDto
    {
        public string Id { get; set; } = "";
        public string Team { get; set; } = "";
        public string Role { get; set; } = "";
        public int MaxHp { get; set; }
        public int Damage { get; set; }
        public int AttackRange { get; set; }
        public int AttackCooldownTicks { get; set; }
        public int MoveIntervalTicks { get; set; }
        public bool Blocks { get; set; }
        public int GuardZoneRadius { get; set; }
        public int HealPower { get; set; }
    }

    internal sealed class TrapDto
    {
        public string Id { get; set; } = "";
        public int Damage { get; set; }
        public int CooldownTicks { get; set; }
        public string? StatusKind { get; set; }
        public int StatusStrength { get; set; }
        public int StatusDurationTicks { get; set; }
    }

    internal sealed class FacilityDto
    {
        public string Id { get; set; } = "";
        public int Damage { get; set; }
        public int Range { get; set; }
        public int CooldownTicks { get; set; }
        public string? StatusKind { get; set; }
        public int StatusStrength { get; set; }
        public int StatusDurationTicks { get; set; }
    }

    internal sealed class SpellDto
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public int MpCost { get; set; }
        public int CooldownTicks { get; set; }
        public int Radius { get; set; }
        public int DurationTicks { get; set; }
        public int Magnitude { get; set; }
    }


    internal sealed class BossRouteBreakDto
    {
        public string UnitId { get; set; } = "";
        public string Kind { get; set; } = "ShortWarp";
        public int TriggerPathPercent { get; set; }
        public int TelegraphTicks { get; set; }
        public int SkipRouteCells { get; set; }
        public int MaxUsesPerFloor { get; set; } = 1;
    }

    internal sealed class WaveDto
    {
        public string Id { get; set; } = "";
        public int InterWaveTicks { get; set; }
        public List<SpawnGroupDto> SpawnGroups { get; set; } = [];
    }

    internal sealed class SpawnGroupDto
    {
        public string UnitId { get; set; } = "";
        public int Count { get; set; }
        public int InitialDelayTicks { get; set; }
        public int SpawnIntervalTicks { get; set; }
    }
}
