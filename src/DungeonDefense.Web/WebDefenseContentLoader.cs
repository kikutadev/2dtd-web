using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Web;

/// <summary>Loads the same vertical-slice JSON used by the native game without relying on filesystem APIs.</summary>
internal static class WebDefenseContentLoader
{
    public static async Task<DefenseContent> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        var json = await http.GetStringAsync("content/vertical-slice.json", cancellationToken);
        var dto = JsonSerializer.Deserialize(json, WebDefenseJsonContext.Default.DefenseContentDto)
            ?? throw new InvalidDataException("Defense content is empty.");

        var units = dto.Units.ToDictionary(x => x.Id, x => new UnitDefinition(
            x.Id, Enum.Parse<Team>(x.Team, true), Enum.Parse<UnitRole>(x.Role, true), x.MaxHp, x.Damage,
            x.AttackRange, x.AttackCooldownTicks, x.MoveIntervalTicks, x.Blocks, x.GuardZoneRadius, x.HealPower), StringComparer.Ordinal);
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
            x => new BossRouteBreakDefinition(x.UnitId, Enum.Parse<BossRouteBreakKind>(x.Kind, true), x.TriggerPathPercent,
                x.TelegraphTicks, x.SkipRouteCells, x.MaxUsesPerFloor).Validate(),
            StringComparer.Ordinal);

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

    private static TEnum? ParseNullable<TEnum>(string? value) where TEnum : struct, Enum
        => string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<TEnum>(value, true);
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
internal sealed class UnitDto { public string Id { get; set; } = ""; public string Team { get; set; } = ""; public string Role { get; set; } = ""; public int MaxHp { get; set; } public int Damage { get; set; } public int AttackRange { get; set; } public int AttackCooldownTicks { get; set; } public int MoveIntervalTicks { get; set; } public bool Blocks { get; set; } public int GuardZoneRadius { get; set; } public int HealPower { get; set; } }
internal sealed class TrapDto { public string Id { get; set; } = ""; public int Damage { get; set; } public int CooldownTicks { get; set; } public string? StatusKind { get; set; } public int StatusStrength { get; set; } public int StatusDurationTicks { get; set; } }
internal sealed class FacilityDto { public string Id { get; set; } = ""; public int Damage { get; set; } public int Range { get; set; } public int CooldownTicks { get; set; } public string? StatusKind { get; set; } public int StatusStrength { get; set; } public int StatusDurationTicks { get; set; } }
internal sealed class SpellDto { public string Id { get; set; } = ""; public string Kind { get; set; } = ""; public int MpCost { get; set; } public int CooldownTicks { get; set; } public int Radius { get; set; } public int DurationTicks { get; set; } public int Magnitude { get; set; } }
internal sealed class BossRouteBreakDto { public string UnitId { get; set; } = ""; public string Kind { get; set; } = "ShortWarp"; public int TriggerPathPercent { get; set; } public int TelegraphTicks { get; set; } public int SkipRouteCells { get; set; } public int MaxUsesPerFloor { get; set; } = 1; }
internal sealed class WaveDto { public string Id { get; set; } = ""; public int InterWaveTicks { get; set; } public List<SpawnGroupDto> SpawnGroups { get; set; } = []; }
internal sealed class SpawnGroupDto { public string UnitId { get; set; } = ""; public int Count { get; set; } public int InitialDelayTicks { get; set; } public int SpawnIntervalTicks { get; set; } }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DefenseContentDto))]
internal sealed partial class WebDefenseJsonContext : JsonSerializerContext;
