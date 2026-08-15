namespace DungeonDefense.Core;

/// <summary>
/// Immutable board geometry reserved for invader admission. The ordered cells are a mandatory route prefix.
/// </summary>
public sealed record DungeonIngressGeometry(string EntranceTypeId, IReadOnlyList<GridPoint> OrderedCells)
{
    public static DungeonIngressGeometry SingleCell(GridPoint entrance)
        => new("entrance.standard", [entrance]);

    public DungeonIngressGeometry Validate(int width, int height, GridPoint entrance, GridPoint core)
    {
        if (string.IsNullOrWhiteSpace(EntranceTypeId))
            throw new InvalidOperationException("Entrance type ID is required.");
        if (OrderedCells.Count == 0 || OrderedCells[0] != entrance)
            throw new InvalidOperationException("Ingress must start at the dungeon Entrance.");
        if (OrderedCells.Distinct().Count() != OrderedCells.Count)
            throw new InvalidOperationException("Ingress cells must be unique.");

        for (var i = 0; i < OrderedCells.Count; i++)
        {
            var cell = OrderedCells[i];
            if (cell.X < 0 || cell.X >= width || cell.Y < 0 || cell.Y >= height)
                throw new InvalidOperationException($"Ingress cell is outside the board: {cell}");
            if (cell == core) throw new InvalidOperationException("Ingress cannot include the dungeon Core.");
            if (i > 0 && !GridGeometry.AreOrthogonallyAdjacent(OrderedCells[i - 1], cell))
                throw new InvalidOperationException("Ingress cells must be orthogonally contiguous.");
        }
        return this;
    }

    public bool Contains(GridPoint point) => OrderedCells.Contains(point);
}
