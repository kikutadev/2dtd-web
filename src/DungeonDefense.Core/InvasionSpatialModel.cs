using System.Security.Cryptography;
using System.Text;

namespace DungeonDefense.Core;

/// <summary>
/// One spatial checkpoint/loot segment inside an invasion floor.
/// A section is not a combat actor and therefore owns no HP, damage, or cooldown.
/// </summary>
public sealed record InvasionSectionDefinition(
    string Id,
    IReadOnlySet<GridPoint> Cells,
    GridPoint Checkpoint,
    ResourceBundle Loot);

/// <summary>
/// World-space objective authority used by Scout, Simulation, Presentation, and Result.
/// </summary>
public sealed record InvasionObjectiveDefinition(
    InvasionObjectiveKind Kind,
    GridPoint Position,
    string? TargetInstanceId = null,
    int StructureMaxHp = 0);

/// <summary>
/// Host-neutral spatial authority for one enemy dungeon floor.
/// This type is introduced before the old aggregate InvasionFloorDefinition is hard-cut
/// so the map contract and validation can be completed/tested without UI involvement.
/// </summary>
public sealed class InvasionFloorDefinition
{
    private readonly InvasionSectionDefinition[] _sections;
    private readonly string[] _threatTags;

    public InvasionFloorDefinition(
        string id,
        int depth,
        IEnumerable<string> threatTags,
        DungeonState board,
        IEnumerable<InvasionSectionDefinition> sections,
        InvasionObjectiveDefinition objective,
        ResourceBundle firstClearReward,
        ResourceBundle repeatReward,
        int regenerationMinutes = 60,
        InvasionRepeatVariationDefinition repeatVariation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        ArgumentNullException.ThrowIfNull(threatTags);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regenerationMinutes);

        Id = id;
        Depth = depth;
        _threatTags = threatTags.ToArray();
        Board = board.Clone();
        _sections = sections
            .Select(section => section with { Cells = section.Cells.ToHashSet() })
            .ToArray();
        Objective = objective;
        FirstClearReward = firstClearReward;
        RepeatReward = repeatReward;
        RegenerationMinutes = regenerationMinutes;
        RepeatVariation = repeatVariation.Validate();

        Validate();
    }

    public string Id { get; }
    public int Depth { get; }
    public IReadOnlyList<string> ThreatTags => _threatTags;
    public DungeonState Board { get; }
    public IReadOnlyList<InvasionSectionDefinition> Sections => _sections;
    public InvasionObjectiveDefinition Objective { get; }
    public ResourceBundle FirstClearReward { get; }
    public ResourceBundle RepeatReward { get; }
    public int RegenerationMinutes { get; }
    public InvasionRepeatVariationDefinition RepeatVariation { get; }

    public IReadOnlyList<GridPoint> ObjectiveRoute()
        => DungeonPathfinder.FindRoute(Board, Objective.Position);

    private void Validate()
    {
        if (_threatTags.Any(string.IsNullOrWhiteSpace) || _threatTags.Distinct(StringComparer.Ordinal).Count() != _threatTags.Length)
            throw new ArgumentException($"Invasion floor threat tags are invalid: {Id}.");
        if (_sections.Length == 0)
            throw new ArgumentException($"Invasion floor requires at least one spatial section: {Id}.");
        if (_sections.Any(x => string.IsNullOrWhiteSpace(x.Id))
            || _sections.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != _sections.Length)
            throw new ArgumentException($"Invasion floor section IDs are invalid: {Id}.");

        var claimedCells = new HashSet<GridPoint>();
        foreach (var section in _sections)
        {
            if (section.Cells.Count == 0)
                throw new ArgumentException($"Invasion section has no cells: {Id}/{section.Id}.");
            foreach (var cell in section.Cells)
            {
                if (!Board.InBounds(cell))
                    throw new ArgumentException($"Invasion section cell is out of bounds: {Id}/{section.Id}/{cell}.");
                if (!claimedCells.Add(cell))
                    throw new ArgumentException($"Invasion section cells overlap: {Id}/{section.Id}/{cell}.");
            }
            if (!section.Cells.Contains(section.Checkpoint) || !Board.IsWalkable(section.Checkpoint))
                throw new ArgumentException($"Invasion section checkpoint must be a walkable cell inside the section: {Id}/{section.Id}.");
        }

        if (!Board.InBounds(Objective.Position) || !Board.IsWalkable(Objective.Position))
            throw new ArgumentException($"Invasion objective must be on a walkable in-bounds cell: {Id}/{Objective.Position}.");

        var route = ObjectiveRoute();
        if (route.Count == 0)
            throw new ArgumentException($"Invasion objective is unreachable from the entrance: {Id}/{Objective.Position}.");
        var routeProgress = route.Select((point, index) => (point, index)).ToDictionary(x => x.point, x => x.index);
        var previousCheckpoint = -1;
        foreach (var section in _sections)
        {
            if (!routeProgress.TryGetValue(section.Checkpoint, out var checkpointProgress))
                throw new ArgumentException($"Invasion section checkpoint is not on the objective route: {Id}/{section.Id}.");
            if (checkpointProgress <= previousCheckpoint)
                throw new ArgumentException($"Invasion section checkpoints are not ordered along the objective route: {Id}/{section.Id}.");
            previousCheckpoint = checkpointProgress;
        }

        switch (Objective.Kind)
        {
            case InvasionObjectiveKind.Raid:
                break;
            case InvasionObjectiveKind.Eliminate:
                if (string.IsNullOrWhiteSpace(Objective.TargetInstanceId))
                    throw new ArgumentException($"ELIMINATE objective requires a target instance: {Id}.");
                var targetGuard = Board.Guards.SingleOrDefault(x => string.Equals(x.InstanceId, Objective.TargetInstanceId, StringComparison.Ordinal));
                if (targetGuard is null || targetGuard.Position != Objective.Position)
                    throw new ArgumentException($"ELIMINATE objective must reference the guard placed at its objective position: {Id}/{Objective.TargetInstanceId}.");
                break;
            case InvasionObjectiveKind.CoreBreak:
                if (Objective.StructureMaxHp <= 0)
                    throw new ArgumentException($"CORE_BREAK objective requires positive structure HP: {Id}.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Objective.Kind));
        }
    }
}

/// <summary>
/// Stable identity for authored/variant spatial geometry. Loot/reward amounts are excluded;
/// ScenarioDigest may vary independently while Save/Scout/Battle still agree on the same map.
/// </summary>
public static class InvasionMapDigest
{
    public static string Compute(InvasionFloorDefinition floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        var builder = new StringBuilder();
        builder.Append(floor.Id).Append('|').Append(floor.Depth).Append('|')
            .Append(DungeonStateDigest.Compute(floor.Board)).Append('|')
            .Append(floor.Objective.Kind).Append('|').Append(floor.Objective.Position).Append('|')
            .Append(floor.Objective.TargetInstanceId).Append('|').Append(floor.Objective.StructureMaxHp).Append('\n');
        foreach (var section in floor.Sections)
        {
            builder.Append(section.Id).Append('|').Append(section.Checkpoint).Append('|');
            foreach (var cell in section.Cells.OrderBy(x => x.Y).ThenBy(x => x.X)) builder.Append(cell).Append(',');
            builder.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
