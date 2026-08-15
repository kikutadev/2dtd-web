using System.Security.Cryptography;
using System.Text;

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

public sealed record InvasionUnitRoleProfile(
    string UnitId,
    InvasionUnitArchetype Archetype = InvasionUnitArchetype.Generalist,
    int SectionDamagePercent = 100,
    int IncomingDamagePercent = 100,
    int AttackCooldownPercent = 100)
{
    public InvasionUnitRoleProfile Validate()
    {
        if (string.IsNullOrWhiteSpace(UnitId)) throw new ArgumentException("Invasion role profile requires a unit ID.", nameof(UnitId));
        if (SectionDamagePercent is < 50 or > 250 || IncomingDamagePercent is < 50 or > 250 || AttackCooldownPercent is < 50 or > 200)
            throw new ArgumentOutOfRangeException(nameof(SectionDamagePercent), "Invasion role profile exceeds supported tuning bounds.");
        return this;
    }
}

public enum InvasionEventType
{
    UnitDeployed,
    UnitAttack,
    UnitDamaged,
    UnitDefeated,
    SpellCast,
    SectionCleared,
    LootSecured,
    RetreatRequested,
    RetreatCompleted,
    ObjectiveCompleted,
    Wiped,
}

public sealed record InvasionSectionDefinition(
    string Id,
    int DefenseHp,
    int DefenseDamage,
    int DefenseAttackCooldownTicks,
    ResourceBundle Loot);

public readonly record struct InvasionRepeatVariationDefinition(
    int DefenseHpPercent = 0,
    int DefenseDamagePercent = 0,
    int AttackCooldownJitterTicks = 0,
    int LootPercent = 0)
{
    public InvasionRepeatVariationDefinition Validate()
    {
        if (DefenseHpPercent is < 0 or > 50 || DefenseDamagePercent is < 0 or > 50 || AttackCooldownJitterTicks is < 0 or > 10 || LootPercent is < 0 or > 50)
            throw new ArgumentOutOfRangeException(nameof(DefenseHpPercent), "Repeat invasion variation exceeds supported bounds.");
        return this;
    }
}

public sealed record InvasionFloorDefinition(
    string Id,
    int Depth,
    InvasionObjectiveKind Objective,
    IReadOnlyList<string> ThreatTags,
    IReadOnlyList<InvasionSectionDefinition> Sections,
    ResourceBundle FirstClearReward,
    ResourceBundle RepeatReward,
    int RegenerationMinutes = 60,
    InvasionRepeatVariationDefinition RepeatVariation = default);

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
        IReadOnlyDictionary<string, int> unitDeploymentCosts,
        IReadOnlyDictionary<string, InvasionSupportSpellDefinition> supportSpells,
        IReadOnlyList<InvasionLocationDefinition> locations,
        IReadOnlyDictionary<string, InvasionUnitRoleProfile>? unitRoleProfiles = null)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Content version is required.", nameof(contentVersion));
        if (deploymentCapacity <= 0 || maxMp <= 0 || mpChargePerTick < 0 || retreatDisengageTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(deploymentCapacity));
        if (wipeLootPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(wipeLootPercent));
        ContentVersion = contentVersion;
        DeploymentCapacity = deploymentCapacity;
        MaxMp = maxMp;
        MpChargePerTick = mpChargePerTick;
        RetreatDisengageTicks = retreatDisengageTicks;
        WipeLootPercent = wipeLootPercent;
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
        foreach (var profile in UnitRoleProfiles.Values) _ = profile.Validate();
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
            if (location.Floors.Any(x => x.RegenerationMinutes <= 0 || x.Sections.Count == 0 || x.Sections.Any(s => s.DefenseHp <= 0 || s.DefenseDamage < 0 || s.DefenseAttackCooldownTicks <= 0)))
                throw new ArgumentException($"Invasion floor sections are invalid: {location.Id}");
            foreach (var floor in location.Floors) _ = floor.RepeatVariation.Validate();
        }
    }
}

public sealed record InvasionFormationEntry(string UnitId, int Count);

public sealed record InvasionUnitRuntimeSnapshot(
    string EntityId,
    string UnitId,
    int FormationIndex,
    int Hp,
    int Shield,
    int AttackCooldownRemaining,
    bool Deployed,
    InvasionUnitArchetype Archetype = InvasionUnitArchetype.Generalist,
    int SectionDamagePercent = 100,
    int IncomingDamagePercent = 100,
    int AttackCooldownPercent = 100);

public sealed record InvasionSpellCooldownSnapshot(string SpellId, int RemainingTicks);

public sealed record InvasionSimulationSnapshot(
    string ContentVersion,
    string FloorId,
    int Seed,
    int Tick,
    int Mp,
    int UsedDeploymentCapacity,
    int SectionIndex,
    int SectionDefenseHp,
    int SectionAttackCooldown,
    int? RetreatRemainingTicks,
    InvasionOutcome Outcome,
    ResourceBundle SecuredLoot,
    IReadOnlyList<InvasionUnitRuntimeSnapshot> Units,
    IReadOnlyList<InvasionSpellCooldownSnapshot> SpellCooldowns,
    IReadOnlyList<InvasionEvent> Events);

public sealed record InvasionEvent(
    int Tick,
    InvasionEventType Type,
    string ActorId,
    string? TargetId = null,
    int Amount = 0,
    string? Detail = null);

public sealed class InvasionUnitRuntime
{
    public required string EntityId { get; init; }
    public required UnitDefinition Definition { get; init; }
    public required int FormationIndex { get; init; }
    public int Hp { get; set; }
    public int Shield { get; set; }
    public int AttackCooldownRemaining { get; set; }
    public InvasionUnitArchetype Archetype { get; init; } = InvasionUnitArchetype.Generalist;
    public int SectionDamagePercent { get; init; } = 100;
    public int IncomingDamagePercent { get; init; } = 100;
    public int AttackCooldownPercent { get; init; } = 100;
    public bool Deployed { get; set; }
    public bool Alive => Hp > 0;
}

/// <summary>
/// Deterministic section-based invasion combat. It intentionally does not reuse DefenseSimulation:
/// invasion has different ownership, deployment and support-only intervention semantics.
/// </summary>
public sealed class InvasionSimulation
{
    public const int TicksPerSecond = 20;
    private readonly Dictionary<string, int> _spellCooldowns;
    private int _sectionDefenseHp;
    private int _sectionAttackCooldown;
    private int? _retreatRemainingTicks;

    public InvasionSimulation(
        InvasionFloorDefinition floor,
        IReadOnlyDictionary<string, UnitDefinition> unitDefinitions,
        IReadOnlyList<InvasionFormationEntry> formation,
        InvasionContent content,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(unitDefinitions);
        ArgumentNullException.ThrowIfNull(formation);
        ArgumentNullException.ThrowIfNull(content);
        Floor = floor;
        Content = content;
        Seed = seed;
        var normalized = formation.Where(x => x.Count > 0).ToArray();
        if (normalized.Length == 0) throw new InvalidOperationException("Invasion formation cannot be empty.");
        var usedCapacity = normalized.Sum(x => checked(content.UnitDeploymentCosts.TryGetValue(x.UnitId, out var cost)
            ? cost * x.Count
            : throw new InvalidOperationException($"No deployment cost for invasion unit: {x.UnitId}")));
        if (usedCapacity > content.DeploymentCapacity)
            throw new InvalidOperationException($"Deployment Capacity exceeded: {usedCapacity}/{content.DeploymentCapacity}.");

        var units = new List<InvasionUnitRuntime>();
        var index = 0;
        foreach (var entry in normalized)
        {
            if (!unitDefinitions.TryGetValue(entry.UnitId, out var definition) || definition.Team != Team.Dungeon)
                throw new InvalidOperationException($"Invalid invasion unit: {entry.UnitId}");
            var role = content.UnitRoleProfile(entry.UnitId);
            for (var i = 0; i < entry.Count; i++)
            {
                units.Add(new InvasionUnitRuntime
                {
                    EntityId = $"I{index + 1:D4}",
                    Definition = definition,
                    FormationIndex = index++,
                    Hp = definition.MaxHp,
                    Archetype = role.Archetype,
                    SectionDamagePercent = role.SectionDamagePercent,
                    IncomingDamagePercent = role.IncomingDamagePercent,
                    AttackCooldownPercent = role.AttackCooldownPercent,
                });
            }
        }
        Units = units;
        UsedDeploymentCapacity = usedCapacity;
        _spellCooldowns = content.SupportSpells.Keys.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        _sectionDefenseHp = floor.Sections[0].DefenseHp;
    }

    private InvasionSimulation(
        InvasionFloorDefinition floor,
        InvasionContent content,
        int seed,
        int tick,
        int mp,
        int usedDeploymentCapacity,
        int sectionIndex,
        int sectionDefenseHp,
        int sectionAttackCooldown,
        int? retreatRemainingTicks,
        InvasionOutcome outcome,
        ResourceBundle securedLoot,
        IReadOnlyList<InvasionUnitRuntime> units,
        IReadOnlyDictionary<string, int> spellCooldowns,
        IReadOnlyList<InvasionEvent> events)
    {
        Floor = floor;
        Content = content;
        Seed = seed;
        Tick = tick;
        Mp = mp;
        UsedDeploymentCapacity = usedDeploymentCapacity;
        SectionIndex = sectionIndex;
        _sectionDefenseHp = sectionDefenseHp;
        _sectionAttackCooldown = sectionAttackCooldown;
        _retreatRemainingTicks = retreatRemainingTicks;
        Outcome = outcome;
        SecuredLoot = securedLoot;
        Units = units;
        _spellCooldowns = new Dictionary<string, int>(spellCooldowns, StringComparer.Ordinal);
        Events.AddRange(events);
    }

    public InvasionFloorDefinition Floor { get; }
    public InvasionContent Content { get; }
    public int Seed { get; }
    public int Tick { get; private set; }
    public int Mp { get; private set; }
    public int UsedDeploymentCapacity { get; }
    public int SectionIndex { get; private set; }
    public int SectionDefenseHp => _sectionDefenseHp;
    public int? RetreatRemainingTicks => _retreatRemainingTicks;
    public InvasionOutcome Outcome { get; private set; } = InvasionOutcome.Running;
    public ResourceBundle SecuredLoot { get; private set; } = ResourceBundle.Zero;
    public IReadOnlyList<InvasionUnitRuntime> Units { get; }
    public List<InvasionEvent> Events { get; } = [];

    public int ReserveCount => Units.Count(x => x.Alive && !x.Deployed);
    public int ActiveCount => Units.Count(x => x.Alive && x.Deployed);
    public int DefeatedCount => Units.Count(x => !x.Alive);

    public void Deploy(string unitId, int count)
    {
        EnsureRunning();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var candidates = Units.Where(x => x.Alive && !x.Deployed && string.Equals(x.Definition.Id, unitId, StringComparison.Ordinal))
            .OrderBy(x => x.FormationIndex).Take(count).ToArray();
        if (candidates.Length != count) throw new InvalidOperationException($"Not enough reserve units: {unitId} x{count}.");
        foreach (var unit in candidates)
        {
            unit.Deployed = true;
            Events.Add(new(Tick, InvasionEventType.UnitDeployed, unit.EntityId, Detail: unit.Definition.Id));
        }
    }

    public void DeployAllRemaining()
    {
        EnsureRunning();
        foreach (var unit in Units.Where(x => x.Alive && !x.Deployed).OrderBy(x => x.FormationIndex))
        {
            unit.Deployed = true;
            Events.Add(new(Tick, InvasionEventType.UnitDeployed, unit.EntityId, Detail: unit.Definition.Id));
        }
    }

    public bool CastSupportSpell(string spellId)
    {
        EnsureRunning();
        if (!Content.SupportSpells.TryGetValue(spellId, out var spell)) throw new InvalidOperationException($"Unknown invasion support spell: {spellId}");
        if (_spellCooldowns[spellId] > 0 || Mp < spell.MpCost) return false;
        var alive = Units.Where(x => x.Alive && x.Deployed).OrderBy(x => x.FormationIndex).ToArray();
        if (alive.Length == 0) return false;

        switch (spell.Kind)
        {
            case InvasionSupportSpellKind.Heal:
            {
                var target = alive.OrderBy(x => x.Hp / (double)x.Definition.MaxHp).ThenBy(x => x.FormationIndex).First();
                var before = target.Hp;
                target.Hp = Math.Min(target.Definition.MaxHp, target.Hp + spell.Magnitude);
                Events.Add(new(Tick, InvasionEventType.SpellCast, spell.Id, target.EntityId, target.Hp - before, "heal"));
                break;
            }
            case InvasionSupportSpellKind.Shield:
                foreach (var target in alive) target.Shield = checked(target.Shield + spell.Magnitude);
                Events.Add(new(Tick, InvasionEventType.SpellCast, spell.Id, Amount: spell.Magnitude, Detail: "shield"));
                break;
            default:
                throw new InvalidOperationException($"Unsupported invasion support spell kind: {spell.Kind}");
        }

        Mp -= spell.MpCost;
        _spellCooldowns[spellId] = spell.CooldownTicks;
        return true;
    }

    public void RequestRetreat()
    {
        EnsureRunning();
        if (_retreatRemainingTicks is not null) return;
        _retreatRemainingTicks = Content.RetreatDisengageTicks;
        Events.Add(new(Tick, InvasionEventType.RetreatRequested, "player", Amount: Content.RetreatDisengageTicks));
    }

    public void Step()
    {
        EnsureRunning();
        Tick++;
        Mp = Math.Min(Content.MaxMp, checked(Mp + Content.MpChargePerTick));
        foreach (var spellId in _spellCooldowns.Keys.ToArray())
            if (_spellCooldowns[spellId] > 0) _spellCooldowns[spellId]--;

        ResolveUnitAttacks();
        if (Outcome != InvasionOutcome.Running) return;
        ResolveSectionAttack();
        if (Outcome != InvasionOutcome.Running) return;
        ResolveRetreat();
        if (Outcome != InvasionOutcome.Running) return;
        ResolveWipe();
    }

    public InvasionOutcome RunToEnd(int maxTicks = 50_000)
    {
        while (Outcome == InvasionOutcome.Running && Tick < maxTicks) Step();
        if (Outcome == InvasionOutcome.Running) throw new InvalidOperationException($"Invasion did not finish within {maxTicks} ticks.");
        return Outcome;
    }

    public int SpellCooldownRemaining(string spellId)
        => _spellCooldowns.TryGetValue(spellId, out var value) ? value : throw new InvalidOperationException($"Unknown spell: {spellId}");

    public InvasionSimulationSnapshot CreateSnapshot()
        => new(
            Content.ContentVersion,
            Floor.Id,
            Seed,
            Tick,
            Mp,
            UsedDeploymentCapacity,
            SectionIndex,
            _sectionDefenseHp,
            _sectionAttackCooldown,
            _retreatRemainingTicks,
            Outcome,
            SecuredLoot,
            Units.OrderBy(x => x.FormationIndex).Select(x => new InvasionUnitRuntimeSnapshot(
                x.EntityId, x.Definition.Id, x.FormationIndex, x.Hp, x.Shield, x.AttackCooldownRemaining, x.Deployed,
                x.Archetype, x.SectionDamagePercent, x.IncomingDamagePercent, x.AttackCooldownPercent)).ToArray(),
            _spellCooldowns.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new InvasionSpellCooldownSnapshot(x.Key, x.Value)).ToArray(),
            Events.ToArray());

    public static InvasionSimulation Restore(
        InvasionSimulationSnapshot snapshot,
        InvasionFloorDefinition floor,
        IReadOnlyDictionary<string, UnitDefinition> unitDefinitions,
        InvasionContent content)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(unitDefinitions);
        ArgumentNullException.ThrowIfNull(content);
        if (!string.Equals(snapshot.ContentVersion, content.ContentVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Invasion snapshot content version mismatch: {snapshot.ContentVersion} != {content.ContentVersion}.");
        if (!string.Equals(snapshot.FloorId, floor.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"Invasion snapshot floor mismatch: {snapshot.FloorId} != {floor.Id}.");
        if (snapshot.Tick < 0 || snapshot.Mp < 0 || snapshot.Mp > content.MaxMp || snapshot.UsedDeploymentCapacity <= 0)
            throw new InvalidDataException("Invasion snapshot counters are invalid.");
        if (snapshot.SectionIndex < 0 || snapshot.SectionIndex >= floor.Sections.Count || snapshot.SectionDefenseHp < 0 || snapshot.SectionAttackCooldown < 0)
            throw new InvalidDataException("Invasion snapshot section state is invalid.");
        if (snapshot.RetreatRemainingTicks is < 0) throw new InvalidDataException("Invasion snapshot retreat state is invalid.");
        if (snapshot.Units.Count == 0) throw new InvalidDataException("Invasion snapshot has no units.");
        if (snapshot.Units.Select(x => x.EntityId).Distinct(StringComparer.Ordinal).Count() != snapshot.Units.Count
            || snapshot.Units.Select(x => x.FormationIndex).Distinct().Count() != snapshot.Units.Count)
            throw new InvalidDataException("Invasion snapshot unit identity is duplicated.");

        var units = snapshot.Units.OrderBy(x => x.FormationIndex).Select(x =>
        {
            if (!unitDefinitions.TryGetValue(x.UnitId, out var definition) || definition.Team != Team.Dungeon)
                throw new InvalidDataException($"Unknown invasion snapshot unit: {x.UnitId}.");
            if (x.Hp < 0 || x.Hp > definition.MaxHp || x.Shield < 0 || x.AttackCooldownRemaining < 0
                || x.SectionDamagePercent is < 50 or > 250 || x.IncomingDamagePercent is < 50 or > 250 || x.AttackCooldownPercent is < 50 or > 200)
                throw new InvalidDataException($"Invalid invasion snapshot unit runtime: {x.EntityId}.");
            return new InvasionUnitRuntime
            {
                EntityId = x.EntityId,
                Definition = definition,
                FormationIndex = x.FormationIndex,
                Hp = x.Hp,
                Shield = x.Shield,
                AttackCooldownRemaining = x.AttackCooldownRemaining,
                Archetype = x.Archetype,
                SectionDamagePercent = x.SectionDamagePercent,
                IncomingDamagePercent = x.IncomingDamagePercent,
                AttackCooldownPercent = x.AttackCooldownPercent,
                Deployed = x.Deployed,
            };
        }).ToArray();

        var expectedCapacity = units.Sum(x => checked(content.UnitDeploymentCosts[x.Definition.Id]));
        if (expectedCapacity != snapshot.UsedDeploymentCapacity || expectedCapacity > content.DeploymentCapacity)
            throw new InvalidDataException("Invasion snapshot deployment capacity is inconsistent.");

        var cooldowns = snapshot.SpellCooldowns.ToDictionary(x => x.SpellId, x => x.RemainingTicks, StringComparer.Ordinal);
        if (!cooldowns.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(content.SupportSpells.Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal)
            || cooldowns.Values.Any(x => x < 0))
            throw new InvalidDataException("Invasion snapshot spell cooldown state is invalid.");

        return new InvasionSimulation(
            floor, content, snapshot.Seed, snapshot.Tick, snapshot.Mp, snapshot.UsedDeploymentCapacity,
            snapshot.SectionIndex, snapshot.SectionDefenseHp, snapshot.SectionAttackCooldown, snapshot.RetreatRemainingTicks,
            snapshot.Outcome, snapshot.SecuredLoot, units, cooldowns, snapshot.Events);
    }

    public string ResultDigest()
    {
        var builder = new StringBuilder();
        builder.Append(Floor.Id).Append('|').Append(Seed).Append('|').Append(Tick).Append('|')
            .Append(Outcome).Append('|').Append(SectionIndex).Append('|').Append(_sectionDefenseHp).Append('|')
            .Append(Mp).Append('|').Append(SecuredLoot).Append('\n');
        foreach (var unit in Units.OrderBy(x => x.FormationIndex))
            builder.Append("U|").Append(unit.EntityId).Append('|').Append(unit.Definition.Id).Append('|')
                .Append(unit.Hp).Append('|').Append(unit.Shield).Append('|').Append(unit.AttackCooldownRemaining).Append('|')
                .Append(unit.Archetype).Append('|').Append(unit.SectionDamagePercent).Append('|').Append(unit.IncomingDamagePercent).Append('|')
                .Append(unit.AttackCooldownPercent).Append('|').Append(unit.Deployed).Append('\n');
        foreach (var cooldown in _spellCooldowns.OrderBy(x => x.Key, StringComparer.Ordinal))
            builder.Append("S|").Append(cooldown.Key).Append('|').Append(cooldown.Value).Append('\n');
        foreach (var e in Events)
            builder.Append("E|").Append(e.Tick).Append('|').Append(e.Type).Append('|').Append(e.ActorId).Append('|')
                .Append(e.TargetId).Append('|').Append(e.Amount).Append('|').Append(e.Detail).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private void ResolveUnitAttacks()
    {
        foreach (var unit in Units.Where(x => x.Alive && x.Deployed).OrderBy(x => x.FormationIndex))
        {
            if (unit.AttackCooldownRemaining > 0)
            {
                unit.AttackCooldownRemaining--;
                continue;
            }
            var damage = ScalePercent(unit.Definition.Damage, unit.SectionDamagePercent);
            _sectionDefenseHp = Math.Max(0, _sectionDefenseHp - damage);
            Events.Add(new(Tick, InvasionEventType.UnitAttack, unit.EntityId, Floor.Sections[SectionIndex].Id, damage));
            unit.AttackCooldownRemaining = Math.Max(1, ScalePercent(unit.Definition.AttackCooldownTicks, unit.AttackCooldownPercent));
            if (_sectionDefenseHp == 0)
            {
                CompleteSection();
                return;
            }
        }
    }

    private void ResolveSectionAttack()
    {
        var target = Units.Where(x => x.Alive && x.Deployed).OrderBy(x => x.FormationIndex).FirstOrDefault();
        if (target is null) return;
        if (_sectionAttackCooldown > 0)
        {
            _sectionAttackCooldown--;
            return;
        }
        var section = Floor.Sections[SectionIndex];
        var damage = ScalePercent(section.DefenseDamage, target.IncomingDamagePercent);
        var absorbed = Math.Min(target.Shield, damage);
        target.Shield -= absorbed;
        damage -= absorbed;
        if (damage > 0) target.Hp = Math.Max(0, target.Hp - damage);
        Events.Add(new(Tick, InvasionEventType.UnitDamaged, section.Id, target.EntityId, ScalePercent(section.DefenseDamage, target.IncomingDamagePercent)));
        if (!target.Alive) Events.Add(new(Tick, InvasionEventType.UnitDefeated, target.EntityId, Detail: target.Definition.Id));
        _sectionAttackCooldown = section.DefenseAttackCooldownTicks;
    }

    private void CompleteSection()
    {
        var section = Floor.Sections[SectionIndex];
        SecuredLoot = SecuredLoot.Add(section.Loot);
        Events.Add(new(Tick, InvasionEventType.SectionCleared, section.Id));
        Events.Add(new(Tick, InvasionEventType.LootSecured, section.Id, Amount: section.Loot.Stone + section.Loot.Iron + section.Loot.Soul + section.Loot.Relic));
        if (SectionIndex == Floor.Sections.Count - 1)
        {
            Outcome = InvasionOutcome.Success;
            Events.Add(new(Tick, InvasionEventType.ObjectiveCompleted, Floor.Id, Detail: Floor.Objective.ToString()));
            return;
        }
        SectionIndex++;
        _sectionDefenseHp = Floor.Sections[SectionIndex].DefenseHp;
        _sectionAttackCooldown = 0;
    }

    private void ResolveRetreat()
    {
        if (_retreatRemainingTicks is null) return;
        _retreatRemainingTicks--;
        if (_retreatRemainingTicks > 0) return;
        if (Units.Any(x => x.Alive))
        {
            Outcome = InvasionOutcome.Retreated;
            Events.Add(new(Tick, InvasionEventType.RetreatCompleted, "player"));
        }
    }

    private void ResolveWipe()
    {
        if (Units.Any(x => x.Alive)) return;
        Outcome = InvasionOutcome.Wiped;
        Events.Add(new(Tick, InvasionEventType.Wiped, "party"));
    }

    private static int ScalePercent(int value, int percent)
        => Math.Max(value == 0 ? 0 : 1, (int)Math.Round(value * percent / 100.0, MidpointRounding.AwayFromZero));

    private void EnsureRunning()
    {
        if (Outcome != InvasionOutcome.Running) throw new InvalidOperationException($"Invasion is already complete: {Outcome}.");
    }
}
