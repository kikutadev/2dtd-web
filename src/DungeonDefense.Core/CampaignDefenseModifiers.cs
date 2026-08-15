namespace DungeonDefense.Core;

public readonly record struct CampaignDefenseModifier(
    int CoreHpPercent = 0,
    int TrapDamagePercent = 0,
    int TrapCooldownReductionPercent = 0,
    int GuardHpPercent = 0,
    int GuardDamagePercent = 0,
    int FacilityDamagePercent = 0,
    int MaxMpBonus = 0,
    int SpellCooldownReductionPercent = 0,
    int SpellDurationPercent = 0,
    int PushMagnitudeBonus = 0)
{
    public CampaignDefenseModifier Add(CampaignDefenseModifier other) => new(
        checked(CoreHpPercent + other.CoreHpPercent),
        checked(TrapDamagePercent + other.TrapDamagePercent),
        checked(TrapCooldownReductionPercent + other.TrapCooldownReductionPercent),
        checked(GuardHpPercent + other.GuardHpPercent),
        checked(GuardDamagePercent + other.GuardDamagePercent),
        checked(FacilityDamagePercent + other.FacilityDamagePercent),
        checked(MaxMpBonus + other.MaxMpBonus),
        checked(SpellCooldownReductionPercent + other.SpellCooldownReductionPercent),
        checked(SpellDurationPercent + other.SpellDurationPercent),
        checked(PushMagnitudeBonus + other.PushMagnitudeBonus));

    public CampaignDefenseModifier Validate()
    {
        if (CoreHpPercent < 0 || TrapDamagePercent < 0 || TrapCooldownReductionPercent is < 0 or > 80 || GuardHpPercent < 0 || GuardDamagePercent < 0 || FacilityDamagePercent < 0
            || MaxMpBonus < 0 || SpellCooldownReductionPercent is < 0 or > 80 || SpellDurationPercent < 0 || PushMagnitudeBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(CampaignDefenseModifier));
        return this;
    }
}
