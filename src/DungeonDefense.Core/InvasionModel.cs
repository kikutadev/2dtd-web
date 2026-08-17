namespace DungeonDefense.Core;

public enum InvasionObjectiveKind
{
    Raid,
    Eliminate,
    CoreBreak,
}

public enum InvasionOutcome
{
    Running,
    Success,
    Retreated,
    Wiped,
}

public enum InvasionSupportSpellKind
{
    Heal,
    Shield,
}

public enum InvasionUnitArchetype
{
    Generalist,
    Vanguard,
    BacklineStriker,
    Support,
    Swarm,
    Siege,
}

/// <summary>
/// Product-facing invasion role classification. Combat stats come from the shared
/// DungeonCombatContent; invasion must not secretly redefine unit damage/defense by section.
/// </summary>
public sealed record InvasionUnitRoleProfile(
    string UnitId,
    InvasionUnitArchetype Archetype = InvasionUnitArchetype.Generalist)
{
    public InvasionUnitRoleProfile Validate()
    {
        if (string.IsNullOrWhiteSpace(UnitId)) throw new ArgumentException("Invasion role profile requires a unit ID.", nameof(UnitId));
        return this;
    }
}

/// <summary>
/// Repeat variation may change reward yield while the authored dungeon topology and
/// combat actor semantics remain authoritative. More spatial variation can be added later
/// only as explicit world-object variation, never as abstract Section HP multipliers.
/// </summary>
public readonly record struct InvasionRepeatVariationDefinition(int LootPercent = 0)
{
    public InvasionRepeatVariationDefinition Validate()
    {
        if (LootPercent is < 0 or > 50)
            throw new ArgumentOutOfRangeException(nameof(LootPercent), "Repeat invasion loot variation exceeds supported bounds.");
        return this;
    }
}

public sealed record InvasionLocationDefinition(
    string Id,
    string Category,
    IReadOnlyList<InvasionFloorDefinition> Floors,
    int RequiredDay = 1,
    IReadOnlyList<string>? RequiredResearchIds = null,
    IReadOnlyList<string>? RequiredRegionIds = null);

public sealed record InvasionSupportSpellDefinition(
    string Id,
    InvasionSupportSpellKind Kind,
    int MpCost,
    int CooldownTicks,
    int Magnitude);

public sealed class InvasionContent
{
    public InvasionContent(
        string contentVersion,
        int deploymentCapacity,
        int maxMp,
        int mpChargePerTick,
        int retreatDisengageTicks,
        int wipeLootPercent,
        DungeonCombatContent combat,
        IReadOnlyDictionary<string, int> unitDeploymentCosts,
        IReadOnlyDictionary<string, InvasionSupportSpellDefinition> supportSpells,
        IReadOnlyList<InvasionLocationDefinition> locations,
        IReadOnlyDictionary<string, InvasionUnitRoleProfile>? unitRoleProfiles = null)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Content version is required.", nameof(contentVersion));
        if (deploymentCapacity <= 0 || maxMp <= 0 || mpChargePerTick < 0 || retreatDisengageTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(deploymentCapacity));
        if (wipeLootPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(wipeLootPercent));
        ArgumentNullException.ThrowIfNull(combat);
        ContentVersion = contentVersion;
        DeploymentCapacity = deploymentCapacity;
        MaxMp = maxMp;
        MpChargePerTick = mpChargePerTick;
        RetreatDisengageTicks = retreatDisengageTicks;
        WipeLootPercent = wipeLootPercent;
        Combat = combat;
        UnitDeploymentCosts = unitDeploymentCosts;
        SupportSpells = supportSpells;
        Locations = locations;
        UnitRoleProfiles = unitRoleProfiles is null
            ? unitDeploymentCosts.Keys.ToDictionary(id => id, id => new InvasionUnitRoleProfile(id), StringComparer.Ordinal)
            : new Dictionary<string, InvasionUnitRoleProfile>(unitRoleProfiles, StringComparer.Ordinal);
        Validate();
    }

    public string ContentVersion { get; }
    public int DeploymentCapacity { get; }
    public int MaxMp { get; }
    public int MpChargePerTick { get; }
    public int RetreatDisengageTicks { get; }
    public int WipeLootPercent { get; }
    public DungeonCombatContent Combat { get; }
    public IReadOnlyDictionary<string, int> UnitDeploymentCosts { get; }
    public IReadOnlyDictionary<string, InvasionUnitRoleProfile> UnitRoleProfiles { get; }
    public IReadOnlyDictionary<string, InvasionSupportSpellDefinition> SupportSpells { get; }
    public IReadOnlyList<InvasionLocationDefinition> Locations { get; }

    public InvasionLocationDefinition Location(string id)
        => Locations.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"Unknown invasion location: {id}");

    public InvasionFloorDefinition Floor(string locationId, string floorId)
        => Location(locationId).Floors.SingleOrDefault(x => string.Equals(x.Id, floorId, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"Unknown invasion floor: {locationId}/{floorId}");

    public InvasionUnitRoleProfile UnitRoleProfile(string unitId)
        => UnitRoleProfiles.TryGetValue(unitId, out var profile) ? profile : new InvasionUnitRoleProfile(unitId);

    private void Validate()
    {
        if (UnitDeploymentCosts.Count == 0 || UnitDeploymentCosts.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value <= 0))
            throw new ArgumentException("Invasion unit deployment costs are invalid.");
        if (!UnitDeploymentCosts.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(UnitRoleProfiles.Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("Invasion unit role profiles must match deployment-cost unit IDs.");
        foreach (var profile in UnitRoleProfiles.Values)
        {
            _ = profile.Validate();
            if (!Combat.Units.TryGetValue(profile.UnitId, out var unit) || unit.Team != Team.Dungeon)
                throw new ArgumentException($"Invasion formation unit must reference a Dungeon-team combat definition: {profile.UnitId}.");
        }
        if (SupportSpells.Values.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.MpCost < 0 || x.CooldownTicks < 0 || x.Magnitude <= 0))
            throw new ArgumentException("Invasion support spells are invalid.");
        if (Locations.Count == 0 || Locations.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new ArgumentException("Invasion locations are invalid.");
        foreach (var location in Locations)
        {
            if (string.IsNullOrWhiteSpace(location.Id) || string.IsNullOrWhiteSpace(location.Category) || location.Floors.Count == 0 || location.RequiredDay <= 0)
                throw new ArgumentException("Invasion location identity is invalid.");
            var requiredResearch = location.RequiredResearchIds ?? [];
            if (requiredResearch.Any(string.IsNullOrWhiteSpace) || requiredResearch.Distinct(StringComparer.Ordinal).Count() != requiredResearch.Count)
                throw new ArgumentException($"Invasion location research requirements are invalid: {location.Id}");
            var requiredRegions = location.RequiredRegionIds ?? [];
            if (requiredRegions.Any(string.IsNullOrWhiteSpace) || requiredRegions.Distinct(StringComparer.Ordinal).Count() != requiredRegions.Count)
                throw new ArgumentException($"Invasion location region requirements are invalid: {location.Id}");
            if (location.Floors.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1)
                || location.Floors.GroupBy(x => x.Depth).Any(x => x.Count() > 1))
                throw new ArgumentException($"Invasion floors must have unique IDs/depths: {location.Id}");
            var orderedDepths = location.Floors.OrderBy(x => x.Depth).Select(x => x.Depth).ToArray();
            if (!orderedDepths.SequenceEqual(Enumerable.Range(1, orderedDepths.Length)))
                throw new ArgumentException($"Invasion floor depths must be contiguous from 1: {location.Id}");
            foreach (var floor in location.Floors) _ = floor.RepeatVariation.Validate();
        }
    }
}

public sealed record InvasionFormationEntry(string UnitId, int Count);
