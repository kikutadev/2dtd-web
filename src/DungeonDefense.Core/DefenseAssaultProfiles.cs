namespace DungeonDefense.Core;

public sealed record DefenseAssaultProfile(
    string Id,
    string Label,
    string Description,
    IReadOnlyList<string> ThreatTags,
    IReadOnlyList<WaveDefinition> Waves);

public static class DefenseContentExtensions
{
    public static DefenseContent WithWaves(this DefenseContent content, IReadOnlyList<WaveDefinition> waves) => new()
    {
        Units = content.Units,
        Traps = content.Traps,
        Facilities = content.Facilities,
        Spells = content.Spells,
        Waves = waves,
        BossRouteBreaks = content.BossRouteBreaks,
        CoreMaxHp = content.CoreMaxHp,
        MaxMp = content.MaxMp,
        MpChargePerTick = content.MpChargePerTick,
    };
}
