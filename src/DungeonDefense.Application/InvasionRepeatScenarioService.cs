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
        var variation = baseFloor.RepeatVariation.Validate();
        if (isFirstClear || variation.LootPercent == 0)
            return new(baseFloor, false, Digest(baseFloor));

        var sections = baseFloor.Sections.Select(section => new InvasionSectionDefinition(
            section.Id,
            section.Cells,
            section.Checkpoint,
            ScaleLoot(section.Loot, Delta(seed, baseFloor.Id, section.Id, "loot", variation.LootPercent)))).ToArray();
        var floor = new InvasionFloorDefinition(
            baseFloor.Id,
            baseFloor.Depth,
            baseFloor.ThreatTags,
            baseFloor.Board,
            sections,
            baseFloor.Objective,
            baseFloor.FirstClearReward,
            baseFloor.RepeatReward,
            baseFloor.RegenerationMinutes,
            baseFloor.RepeatVariation);
        return new(floor, true, Digest(floor));
    }

    public static string Digest(InvasionFloorDefinition floor)
    {
        var builder = new StringBuilder();
        builder.Append(floor.Id).Append('|').Append(floor.Depth).Append('|').Append(floor.Objective.Kind).Append('|')
            .Append(floor.Objective.Position).Append('|').Append(floor.Objective.TargetInstanceId).Append('|')
            .Append(floor.Objective.StructureMaxHp).Append('|').Append(DungeonStateDigest.Compute(floor.Board)).Append('\n');
        foreach (var section in floor.Sections)
        {
            builder.Append(section.Id).Append('|').Append(section.Checkpoint).Append('|').Append(section.Loot).Append('|');
            foreach (var cell in section.Cells.OrderBy(x => x.Y).ThenBy(x => x.X)) builder.Append(cell).Append(',');
            builder.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static int Scale(int value, int percentDelta)
        => Math.Max(0, checked(value + value * percentDelta / 100));

    private static ResourceBundle ScaleLoot(ResourceBundle value, int percentDelta)
        => new(
            Scale(value.Stone, percentDelta),
            Scale(value.Iron, percentDelta),
            Scale(value.Soul, percentDelta),
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
