using System.Collections.Immutable;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

public enum UnitFacing
{
    North,
    East,
    South,
    West,
}

public enum CombatVisualLifecycle
{
    Active,
    Dying,
}

public enum CombatMoveKind
{
    None,
    Walk,
    Push,
}

public readonly record struct PresentationPoint(float X, float Y)
{
    public static PresentationPoint From(GridPoint point) => new(point.X, point.Y);

    public static PresentationPoint Lerp(PresentationPoint from, PresentationPoint to, float amount)
        => new(from.X + ((to.X - from.X) * amount), from.Y + ((to.Y - from.Y) * amount));

    public PresentationPoint Add(PresentationPoint other) => new(X + other.X, Y + other.Y);

    public PresentationPoint Scale(float scalar) => new(X * scalar, Y * scalar);

    public float DistanceSquaredTo(PresentationPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return (dx * dx) + (dy * dy);
    }

    public static PresentationPoint Direction(PresentationPoint from, PresentationPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        return length <= 0.0001f ? new PresentationPoint(0f, 0f) : new PresentationPoint(dx / length, dy / length);
    }
}

/// <summary>
/// Immutable render-neutral frame produced by <see cref="CombatMotionPresentation"/>.
/// Hosts render this state and do not own combat motion timing or visual lifecycle.
/// </summary>
public sealed record CombatVisualState(
    ImmutableArray<CombatUnitVisualState> Units,
    ImmutableArray<CombatProjectileVisualState> Projectiles,
    bool HasActiveMotion)
{
    public static CombatVisualState Empty { get; } = new(ImmutableArray<CombatUnitVisualState>.Empty, ImmutableArray<CombatProjectileVisualState>.Empty, false);

    public CombatUnitVisualState? FindUnit(string entityId)
        => Units.FirstOrDefault(x => string.Equals(x.EntityId, entityId, StringComparison.Ordinal));
}

public sealed record CombatUnitVisualState(
    string EntityId,
    string DefinitionId,
    Team Team,
    string FloorId,
    GridPoint LogicalPosition,
    PresentationPoint RenderPosition,
    int Hp,
    int MaxHp,
    UnitFacing Facing,
    CombatVisualLifecycle Lifecycle,
    CombatMoveKind MoveKind,
    bool IsMoving,
    bool IsAttacking,
    bool IsHit,
    bool IsSpawning,
    bool ShowHp,
    float Opacity,
    float ScaleX,
    float ScaleY,
    float HitFlashStrength);

public sealed record CombatProjectileVisualState(
    string ActorId,
    string TargetId,
    PresentationPoint From,
    PresentationPoint To,
    PresentationPoint Position,
    float Progress,
    ProjectileTrajectoryKind Trajectory,
    float ProjectileHeight);
