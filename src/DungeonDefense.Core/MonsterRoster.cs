namespace DungeonDefense.Core;

public static class MonsterIds
{
    public const string SkeletonWarrior = "monster.skeleton_warrior";
    public const string SkeletonArcher = "monster.skeleton_archer";
    public const string Goblin = "monster.goblin";
    public const string Slime = "monster.slime";
    public const string Spider = "monster.spider";
    public const string Necromancer = "monster.necromancer";
}

/// <summary>
/// Defense-only placement metadata for a monster. Combat capability remains in UnitDefinition.
/// </summary>
public sealed record MonsterDefenseProfile(
    int CapacityCost,
    int GuardZoneRadius,
    bool Blocks)
{
    public MonsterDefenseProfile Validate()
    {
        if (CapacityCost <= 0) throw new ArgumentOutOfRangeException(nameof(CapacityCost));
        if (GuardZoneRadius <= 0) throw new ArgumentOutOfRangeException(nameof(GuardZoneRadius));
        return this;
    }
}

/// <summary>
/// Invasion-only formation metadata. It must not redefine HP/damage/range or other combat stats.
/// </summary>
public sealed record MonsterInvasionProfile(
    int DeploymentCost,
    InvasionUnitArchetype Archetype)
{
    public MonsterInvasionProfile Validate()
    {
        if (DeploymentCost <= 0) throw new ArgumentOutOfRangeException(nameof(DeploymentCost));
        return this;
    }
}

/// <summary>
/// Campaign availability metadata. Null means available from the start of a region/campaign.
/// </summary>
public sealed record MonsterProgressionProfile(string? RequiredUnlockId = null)
{
    public MonsterProgressionProfile Validate()
    {
        if (RequiredUnlockId is not null && string.IsNullOrWhiteSpace(RequiredUnlockId))
            throw new ArgumentException("Monster unlock ID must be null or non-empty.", nameof(RequiredUnlockId));
        return this;
    }
}

/// <summary>
/// Single product authority for one player-controlled dungeon monster.
/// </summary>
public sealed record MonsterDefinition(
    string Id,
    string SpeciesId,
    UnitDefinition Combat,
    MonsterDefenseProfile Defense,
    MonsterInvasionProfile Invasion,
    MonsterProgressionProfile Progression,
    string AssetId)
{
    public MonsterDefinition Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Id.StartsWith("monster.", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid monster ID: {Id}", nameof(Id));
        if (string.IsNullOrWhiteSpace(SpeciesId)) throw new ArgumentException("Monster species ID is required.", nameof(SpeciesId));
        if (Combat.Id != Id) throw new ArgumentException($"Monster combat ID mismatch: {Id} != {Combat.Id}.");
        if (Combat.Team != Team.Dungeon) throw new ArgumentException($"Monster combat team must be Dungeon: {Id}.");
        _ = Defense.Validate();
        _ = Invasion.Validate();
        _ = Progression.Validate();
        if (string.IsNullOrWhiteSpace(AssetId)) throw new ArgumentException($"Monster asset identity is required: {Id}.");
        return this;
    }
}

/// <summary>
/// Runtime aggregate of all player-controlled monsters. This is the only source from which
/// Defense placement and Invasion formation registries may be derived.
/// </summary>
public sealed class MonsterRosterContent
{
    private readonly Dictionary<string, MonsterDefinition> _byId;

    public MonsterRosterContent(string contentVersion, IReadOnlyList<MonsterDefinition> monsters)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Monster roster content version is required.", nameof(contentVersion));
        ArgumentNullException.ThrowIfNull(monsters);
        if (monsters.Count == 0) throw new ArgumentException("Monster roster must contain at least one monster.", nameof(monsters));
        if (monsters.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new ArgumentException("Monster roster IDs must be unique.", nameof(monsters));
        foreach (var monster in monsters) _ = monster.Validate();
        ContentVersion = contentVersion;
        Monsters = monsters.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        _byId = Monsters.ToDictionary(x => x.Id, StringComparer.Ordinal);
    }

    public string ContentVersion { get; }
    public IReadOnlyList<MonsterDefinition> Monsters { get; }

    public MonsterDefinition Monster(string id)
        => _byId.TryGetValue(id, out var monster)
            ? monster
            : throw new InvalidOperationException($"Unknown monster: {id}.");

    public bool TryMonster(string id, out MonsterDefinition monster)
        => _byId.TryGetValue(id, out monster!);

    public bool IsAvailable(CampaignState state, string monsterId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var required = Monster(monsterId).Progression.RequiredUnlockId;
        return required is null || state.HasUnlock(required);
    }

    public IReadOnlyList<MonsterDefinition> Available(CampaignState state)
        => Monsters.Where(x => IsAvailable(state, x.Id)).ToArray();

    public IReadOnlyDictionary<string, UnitDefinition> CombatUnits()
        => Monsters.ToDictionary(x => x.Id, x => x.Combat, StringComparer.Ordinal);
}
