using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record InvasionResolvedScenario(
    InvasionFloorDefinition Floor,
    bool IsRepeatVariant,
    string ScenarioDigest);

public static class InvasionRepeatScenarioService
{
    public static InvasionResolvedScenario Resolve(InvasionFloorDefinition baseFloor, bool isFirstClear, int seed)
    {
        ArgumentNullException.ThrowIfNull(baseFloor);
        if (isFirstClear || baseFloor.RepeatVariation == default)
            return new(baseFloor, false, Digest(baseFloor));

        var variation = baseFloor.RepeatVariation.Validate();
        var sections = baseFloor.Sections.Select(section => new InvasionSectionDefinition(
            section.Id,
            Scale(section.DefenseHp, Delta(seed, baseFloor.Id, section.Id, "hp", variation.DefenseHpPercent), minimum: 1),
            Scale(section.DefenseDamage, Delta(seed, baseFloor.Id, section.Id, "damage", variation.DefenseDamagePercent), minimum: 0),
            Math.Max(1, checked(section.DefenseAttackCooldownTicks + Delta(seed, baseFloor.Id, section.Id, "cooldown", variation.AttackCooldownJitterTicks))),
            ScaleLoot(section.Loot, Delta(seed, baseFloor.Id, section.Id, "loot", variation.LootPercent)))).ToArray();

        var floor = baseFloor with { Sections = sections };
        return new(floor, true, Digest(floor));
    }

    public static string Digest(InvasionFloorDefinition floor)
    {
        var builder = new StringBuilder();
        builder.Append(floor.Id).Append('|').Append(floor.Depth).Append('|').Append(floor.Objective).Append('\n');
        foreach (var section in floor.Sections)
            builder.Append(section.Id).Append('|').Append(section.DefenseHp).Append('|').Append(section.DefenseDamage).Append('|')
                .Append(section.DefenseAttackCooldownTicks).Append('|').Append(section.Loot).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static int Scale(int value, int percentDelta, int minimum)
        => Math.Max(minimum, checked(value + value * percentDelta / 100));

    private static ResourceBundle ScaleLoot(ResourceBundle value, int percentDelta)
        => new(
            Scale(value.Stone, percentDelta, 0),
            Scale(value.Iron, percentDelta, 0),
            Scale(value.Soul, percentDelta, 0),
            value.Relic);

    private static int Delta(int seed, string floorId, string sectionId, string channel, int magnitude)
    {
        if (magnitude <= 0) return 0;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}|{floorId}|{sectionId}|{channel}"));
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        var span = checked(magnitude * 2 + 1);
        return (int)(value % (uint)span) - magnitude;
    }
}
