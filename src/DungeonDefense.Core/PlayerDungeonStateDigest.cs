using System.Security.Cryptography;
using System.Text;

namespace DungeonDefense.Core;

public static class PlayerDungeonStateDigest
{
    public static string Compute(PlayerDungeonState dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var payload = new StringBuilder();
        payload.Append(dungeon.DungeonId).AppendLine();
        foreach (var floor in dungeon.Floors.OrderBy(x => x.Depth))
        {
            payload.Append(floor.Id.Value).Append('|')
                .Append(floor.Depth).Append('|')
                .Append(floor.BoardProfileId).Append('|')
                .Append(floor.EndpointKind).Append('|')
                .Append(floor.CapacityMax).Append('|')
                .Append(DungeonStateDigest.Compute(floor.Board)).AppendLine();
            foreach (var sector in floor.Board.Sectors.OrderBy(x => x.Id, StringComparer.Ordinal))
                payload.Append("sector|").Append(sector.Id).Append('|').Append(sector.IsUnlocked).AppendLine();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()))).ToLowerInvariant();
    }
}
