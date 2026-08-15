namespace DungeonDefense.Presentation;

public enum ProjectileTrajectoryKind
{
    BallisticArc,
    StraightProjectile,
    Beam,
    Instant,
}

/// <summary>
/// Engine-independent visual delivery semantics for attacks. These values never alter Core LOS or damage timing.
/// </summary>
public sealed record CombatAttackPresentationProfile(
    ProjectileTrajectoryKind Trajectory,
    float HeightPerCell,
    float MaxPeakHeight,
    double OneXDurationSeconds,
    double MinimumDurationSeconds);

public static class CombatAttackPresentationProfiles
{
    private static readonly CombatAttackPresentationProfile Bow = new(
        ProjectileTrajectoryKind.BallisticArc, 0.16f, 0.72f, 0.18, 0.07);
    private static readonly CombatAttackPresentationProfile Crossbow = new(
        ProjectileTrajectoryKind.BallisticArc, 0.08f, 0.34f, 0.14, 0.055);
    private static readonly CombatAttackPresentationProfile Straight = new(
        ProjectileTrajectoryKind.StraightProjectile, 0f, 0f, 0.13, 0.05);
    private static readonly CombatAttackPresentationProfile Beam = new(
        ProjectileTrajectoryKind.Beam, 0f, 0f, 0.12, 0.05);
    private static readonly CombatAttackPresentationProfile Instant = new(
        ProjectileTrajectoryKind.Instant, 0f, 0f, 0.01, 0.01);

    public static CombatAttackPresentationProfile Resolve(string? sourceDefinitionId, bool inferredRanged)
        => sourceDefinitionId switch
        {
            "monster.skeleton_archer" or "human.archer" or "facility.arrow_slit" => Bow,
            "human.crossbowman" => Crossbow,
            "facility.magic_eye" => Beam,
            "human.priest" or "human.high_priest" => Beam,
            null when inferredRanged => Bow,
            _ when inferredRanged => Straight,
            _ => Instant,
        };
}
