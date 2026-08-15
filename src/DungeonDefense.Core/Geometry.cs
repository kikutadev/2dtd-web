namespace DungeonDefense.Core;

public readonly record struct GridPoint(int X, int Y)
{
    public int ManhattanDistance(GridPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public override string ToString() => $"{X},{Y}";
}

public enum CardinalDirection
{
    North,
    East,
    South,
    West,
}

public static class GridGeometry
{
    private static readonly (CardinalDirection Direction, GridPoint Offset)[] Ordered =
    [
        (CardinalDirection.North, new GridPoint(0, -1)),
        (CardinalDirection.East, new GridPoint(1, 0)),
        (CardinalDirection.South, new GridPoint(0, 1)),
        (CardinalDirection.West, new GridPoint(-1, 0)),
    ];

    public static IEnumerable<GridPoint> NeighborsNorthEastSouthWest(GridPoint point)
    {
        foreach (var (_, offset) in Ordered)
        {
            yield return new GridPoint(point.X + offset.X, point.Y + offset.Y);
        }
    }

    public static bool AreOrthogonallyAdjacent(GridPoint a, GridPoint b) => a.ManhattanDistance(b) == 1;

    public static GridPoint Neighbor(GridPoint point, CardinalDirection direction) => direction switch
    {
        CardinalDirection.North => new GridPoint(point.X, point.Y - 1),
        CardinalDirection.East => new GridPoint(point.X + 1, point.Y),
        CardinalDirection.South => new GridPoint(point.X, point.Y + 1),
        CardinalDirection.West => new GridPoint(point.X - 1, point.Y),
        _ => point,
    };

    public static CardinalDirection DirectionFromTo(GridPoint from, GridPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return (dx, dy) switch
        {
            (0, -1) => CardinalDirection.North,
            (1, 0) => CardinalDirection.East,
            (0, 1) => CardinalDirection.South,
            (-1, 0) => CardinalDirection.West,
            _ => throw new ArgumentException("Points must be orthogonally adjacent."),
        };
    }

    public static IReadOnlyList<GridPoint> SupercoverLine(GridPoint from, GridPoint to)
    {
        var cells = new List<GridPoint> { from };
        var x = from.X;
        var y = from.Y;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var nx = Math.Abs(dx);
        var ny = Math.Abs(dy);
        var signX = Math.Sign(dx);
        var signY = Math.Sign(dy);
        var ix = 0;
        var iy = 0;

        while (ix < nx || iy < ny)
        {
            var lhs = (1 + (2 * ix)) * ny;
            var rhs = (1 + (2 * iy)) * nx;

            if (lhs == rhs)
            {
                var sideX = new GridPoint(x + signX, y);
                var sideY = new GridPoint(x, y + signY);
                if (!cells.Contains(sideX)) cells.Add(sideX);
                if (!cells.Contains(sideY)) cells.Add(sideY);
                x += signX;
                y += signY;
                ix++;
                iy++;
                var diagonal = new GridPoint(x, y);
                if (!cells.Contains(diagonal)) cells.Add(diagonal);
            }
            else if (lhs < rhs)
            {
                x += signX;
                ix++;
                var next = new GridPoint(x, y);
                if (!cells.Contains(next)) cells.Add(next);
            }
            else
            {
                y += signY;
                iy++;
                var next = new GridPoint(x, y);
                if (!cells.Contains(next)) cells.Add(next);
            }
        }

        return cells;
    }
}
