using System.Collections.Immutable;
using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Product-level preparation states for invasion. Hosts may navigate with different widgets,
/// but they present the same sequence of decisions and the same derived player information.
/// </summary>
public enum InvasionPreparationStage
{
    Locations,
    Scout,
    Formation,
}

public static class InvasionPreparationPresentation
{
    public static InvasionLocationListVisualState BuildLocations(
        InvasionContent content,
        IEnumerable<InvasionScoutReport> scoutReports)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scoutReports);
        var reports = scoutReports.ToArray();
        var locations = content.Locations
            .Select(location =>
            {
                var locationReports = reports.Where(x => string.Equals(x.LocationId, location.Id, StringComparison.Ordinal)).ToArray();
                return new InvasionLocationVisualState(
                    location.Id,
                    location.Category,
                    location.Floors.Count,
                    locationReports.Count(x => x.IsUnlocked),
                    locationReports.Count(x => x.IsUnlocked && x.IsAvailable));
            })
            .ToImmutableArray();
        return new InvasionLocationListVisualState(locations);
    }

    public static InvasionScoutVisualState BuildScout(
        string locationId,
        IEnumerable<InvasionScoutReport> scoutReports)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentNullException.ThrowIfNull(scoutReports);
        var floors = scoutReports
            .Where(x => string.Equals(x.LocationId, locationId, StringComparison.Ordinal))
            .OrderBy(x => x.Depth)
            .Select(report => new InvasionScoutFloorVisualState(
                report.FloorId,
                report.Depth,
                report.Objective,
                report.SectionCount,
                report.ThreatTags.ToImmutableArray(),
                report.VisibleSectionLoot,
                report.ClearReward,
                report.IsFirstClear,
                report.IsUnlocked,
                report.IsAvailable,
                report.RegenerationRemaining,
                report.IsRepeatVariant))
            .ToImmutableArray();
        return new InvasionScoutVisualState(locationId, floors);
    }

    public static InvasionFormationVisualState BuildFormation(
        InvasionContent content,
        InvasionScoutReport scout,
        IReadOnlyDictionary<string, int> formation)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scout);
        ArgumentNullException.ThrowIfNull(formation);

        var units = content.UnitDeploymentCosts
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(pair => new InvasionFormationUnitVisualState(
                pair.Key,
                ProductAssetIdentity.Unit(pair.Key, UnitFacing.East),
                content.UnitRoleProfile(pair.Key).Archetype,
                pair.Value,
                formation.GetValueOrDefault(pair.Key)))
            .ToImmutableArray();
        var used = units.Sum(x => checked(x.DeploymentCost * x.Count));
        return new InvasionFormationVisualState(
            scout.LocationId,
            scout.FloorId,
            scout.Depth,
            scout.Objective,
            used,
            content.DeploymentCapacity,
            used > 0 && used <= content.DeploymentCapacity && scout.IsUnlocked && scout.IsAvailable,
            units);
    }
}

public sealed record InvasionLocationListVisualState(ImmutableArray<InvasionLocationVisualState> Locations);

public sealed record InvasionLocationVisualState(
    string LocationId,
    string Category,
    int FloorCount,
    int UnlockedFloorCount,
    int AvailableFloorCount);

public sealed record InvasionScoutVisualState(
    string LocationId,
    ImmutableArray<InvasionScoutFloorVisualState> Floors);

public sealed record InvasionScoutFloorVisualState(
    string FloorId,
    int Depth,
    InvasionObjectiveKind Objective,
    int SectionCount,
    ImmutableArray<string> ThreatTags,
    ResourceBundle VisibleSectionLoot,
    ResourceBundle ClearReward,
    bool IsFirstClear,
    bool IsUnlocked,
    bool IsAvailable,
    TimeSpan RegenerationRemaining,
    bool IsRepeatVariant);

public sealed record InvasionFormationVisualState(
    string LocationId,
    string FloorId,
    int Depth,
    InvasionObjectiveKind Objective,
    int UsedCapacity,
    int Capacity,
    bool CanStart,
    ImmutableArray<InvasionFormationUnitVisualState> Units);

public sealed record InvasionFormationUnitVisualState(
    string DefinitionId,
    ProductAssetRef? Asset,
    InvasionUnitArchetype Archetype,
    int DeploymentCost,
    int Count);
