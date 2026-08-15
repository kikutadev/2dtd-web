using System.Security.Cryptography;
using System.Text;

namespace DungeonDefense.Core;

public static class DungeonStateDigest
{
    public static string Compute(DungeonState state)
    {
        var builder = new StringBuilder();
        builder.Append(state.Width).Append('x').Append(state.Height).Append('|')
            .Append(state.Entrance).Append('|').Append(state.Core).Append('|').Append(state.CapacityMax).Append('\n');
        for (var y = 0; y < state.Height; y++)
        {
            for (var x = 0; x < state.Width; x++) builder.Append((int)state.GetTile(new GridPoint(x, y)));
            builder.Append('\n');
        }
        foreach (var terrain in state.TerrainFeatures.OrderBy(x => x.Id, StringComparer.Ordinal))
            builder.Append("X|").Append(terrain.Id).Append('|').Append(terrain.Kind).Append('|').Append(terrain.BlocksConstruction).Append('|').AppendJoin(',', terrain.Cells.OrderBy(x => x.Y).ThenBy(x => x.X)).Append('\n');
        foreach (var room in state.Rooms.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            builder.Append("R|").Append(room.InstanceId).Append('|').Append(room.DefinitionId).Append('|').Append(room.Origin).Append('|').Append(room.Width).Append('x').Append(room.Height).Append('|').Append(room.CapacityCost).Append('|').Append(room.GuardHpBonusPercent).Append('|').Append(room.GuardDamageBonusPercent).Append('|').Append(room.PoisonDurationBonusPercent).Append('|').Append(room.ExecuteThresholdPercent).Append('|').Append(room.ExecuteDamageBonusPercent).Append('|').Append(room.SpellDurationBonusPercent).Append('|').Append(room.PushMagnitudeBonus).Append('\n');
        foreach (var trap in state.Traps.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            builder.Append("T|").Append(trap.InstanceId).Append('|').Append(trap.DefinitionId).Append('|').Append(trap.Position).Append('|').Append(trap.CapacityCost).Append('\n');
        foreach (var guard in state.Guards.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            builder.Append("G|").Append(guard.InstanceId).Append('|').Append(guard.DefinitionId).Append('|').Append(guard.Position).Append('|').Append(guard.CapacityCost).Append('|').Append(guard.GuardZoneRadius).Append('\n');
        foreach (var facility in state.Facilities.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            builder.Append("F|").Append(facility.InstanceId).Append('|').Append(facility.DefinitionId).Append('|').Append(facility.Position).Append('|').Append(facility.CapacityCost).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
