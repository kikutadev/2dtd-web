using DungeonDefense.Core;

namespace DungeonDefense.Application;

public static class CampaignDefenseContentService
{
    public static DefenseContent ApplyProgression(
        DefenseContent content,
        CampaignState state,
        CampaignProgressionContent progression)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(progression);
        var modifier = progression.DefenseModifierFor(state);
        if (modifier == default) return content;

        var units = content.Units.ToDictionary(
            x => x.Key,
            x => x.Value.Team != Team.Dungeon
                ? x.Value
                : x.Value with
                {
                    MaxHp = ApplyPercent(x.Value.MaxHp, modifier.GuardHpPercent),
                    Damage = ApplyPercent(x.Value.Damage, modifier.GuardDamagePercent),
                },
            StringComparer.Ordinal);
        var traps = content.Traps.ToDictionary(
            x => x.Key,
            x => x.Value with
            {
                Damage = ApplyPercent(x.Value.Damage, modifier.TrapDamagePercent),
                CooldownTicks = Math.Max(1, x.Value.CooldownTicks * (100 - modifier.TrapCooldownReductionPercent) / 100),
            },
            StringComparer.Ordinal);
        var facilities = content.Facilities.ToDictionary(
            x => x.Key,
            x => x.Value with { Damage = ApplyPercent(x.Value.Damage, modifier.FacilityDamagePercent) },
            StringComparer.Ordinal);
        var spells = content.Spells.ToDictionary(
            x => x.Key,
            x => x.Value with
            {
                CooldownTicks = Math.Max(1, x.Value.CooldownTicks * (100 - modifier.SpellCooldownReductionPercent) / 100),
                DurationTicks = x.Value.DurationTicks == 0 ? 0 : ApplyPercent(x.Value.DurationTicks, modifier.SpellDurationPercent),
                Magnitude = Math.Max(0, x.Value.Magnitude + modifier.PushMagnitudeBonus),
            },
            StringComparer.Ordinal);

        return new DefenseContent
        {
            MonsterRoster = content.MonsterRoster,
            Units = units,
            Traps = traps,
            Facilities = facilities,
            Spells = spells,
            Waves = content.Waves,
            BossRouteBreaks = content.BossRouteBreaks,
            CoreMaxHp = ApplyPercent(content.CoreMaxHp, modifier.CoreHpPercent),
            MaxMp = checked(content.MaxMp + modifier.MaxMpBonus),
            MpChargePerTick = content.MpChargePerTick,
        };
    }

    private static int ApplyPercent(int value, int bonusPercent)
        => Math.Max(1, (int)Math.Round(value * (100 + bonusPercent) / 100.0, MidpointRounding.AwayFromZero));
}
