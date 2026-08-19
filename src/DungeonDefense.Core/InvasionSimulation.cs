namespace DungeonDefense.Core;

public enum InvasionActorKind
{
    InvaderUnit,
    EnemyGuard,
    Trap,
    Facility,
    Objective,
}

public sealed record InvasionUnitStateSnapshot(
    string EntityId,
    string DefinitionId,
    Team Team,
    int FormationIndex,
    GridPoint Position,
    int Hp,
    int MaxHp,
    int Shield,
    long RouteProgressUnits,
    bool DeploymentRequested,
    bool Admitted,
    bool Alive,
    string? TargetEntityId,
    InvasionUnitArchetype Archetype = InvasionUnitArchetype.Generalist);

public sealed record InvasionStaticActorStateSnapshot(
    string InstanceId,
    string DefinitionId,
    InvasionActorKind Kind,
    GridPoint Position,
    int CooldownRemaining = 0);

public enum InvasionEventType
{
    DeploymentRequested,
    UnitAdmitted,
    UnitMoved,
    UnitAttack,
    UnitDamaged,
    UnitDefeated,
    GuardMoved,
    GuardAttack,
    GuardDefeated,
    TrapTriggered,
    FacilityAttack,
    SpellCast,
    SectionEntered,
    SectionCleared,
    LootSecured,
    ObjectiveDamaged,
    ObjectiveCompleted,
    RetreatRequested,
    RetreatCompleted,
    Wiped,
}

public sealed record InvasionEvent(
    int Tick,
    InvasionEventType Type,
    string ActorId,
    string? TargetId = null,
    GridPoint? Position = null,
    int Amount = 0,
    string? Detail = null,
    GridPoint? SourcePosition = null,
    string? SourceDefinitionId = null);

public sealed record InvasionStatusSnapshot(StatusKind Kind, int Strength, int RemainingTicks);

public sealed record InvasionUnitRuntimeSnapshot(
    string EntityId,
    string DefinitionId,
    int FormationIndex,
    GridPoint Position,
    int Hp,
    int Shield,
    long RouteProgressUnits,
    int PathIndex,
    int MoveRemainder,
    int NextMoveTick,
    int NextAttackTick,
    bool DeploymentRequested,
    bool Admitted,
    string? TargetEntityId,
    InvasionUnitArchetype Archetype,
    IReadOnlyList<InvasionStatusSnapshot> Statuses);

public sealed record InvasionGuardRuntimeSnapshot(
    string EntityId,
    string DefinitionId,
    GridPoint Position,
    int Hp,
    int NextMoveTick,
    int NextAttackTick,
    string? TargetEntityId,
    IReadOnlyList<InvasionStatusSnapshot> Statuses);

public sealed record InvasionCooldownSnapshot(string Id, int ReadyTick);
public sealed record InvasionSpellCooldownSnapshot(string SpellId, int RemainingTicks);

public sealed record InvasionSimulationSnapshot(
    string ContentVersion,
    string FloorId,
    string MapDigest,
    int Seed,
    int Tick,
    int Mp,
    int UsedDeploymentCapacity,
    int? RetreatRemainingTicks,
    InvasionOutcome Outcome,
    ResourceBundle SecuredLoot,
    int ObjectiveStructureHp,
    IReadOnlyList<string> ClearedSectionIds,
    IReadOnlyList<InvasionUnitRuntimeSnapshot> Units,
    IReadOnlyList<InvasionGuardRuntimeSnapshot> Guards,
    IReadOnlyList<InvasionCooldownSnapshot> TrapCooldowns,
    IReadOnlyList<InvasionCooldownSnapshot> FacilityCooldowns,
    IReadOnlyList<InvasionSpellCooldownSnapshot> SpellCooldowns,
    IReadOnlyList<InvasionEvent> Events);

/// <summary>
/// Pure-Core spatial invasion resolver. It deliberately owns no UI/view concepts:
/// every exposed value is gameplay state that can be tested without Godot or Web.
/// </summary>
public sealed class InvasionSimulation
{
    public const int TicksPerSecond = 20;

    private readonly DungeonCombatContent _combat;
    private readonly Dictionary<string, int> _spellCooldowns;
    private readonly Dictionary<string, int> _trapReadyTick;
    private readonly Dictionary<string, int> _facilityReadyTick;
    private readonly Dictionary<string, RuntimeInvasionUnit> _units;
    private readonly Dictionary<string, RuntimeEnemyGuard> _guards;
    private readonly Dictionary<GridPoint, int> _routeIndex;
    private readonly Dictionary<string, int> _sectionCheckpointIndex;
    private readonly HashSet<string> _clearedSections = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<GridPoint> _route;
    private int? _retreatRemainingTicks;
    private int _objectiveStructureHp;

    public InvasionSimulation(
        InvasionFloorDefinition floor,
        IReadOnlyList<InvasionFormationEntry> formation,
        InvasionContent content,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(formation);
        ArgumentNullException.ThrowIfNull(content);
        Floor = floor;
        Content = content;
        _combat = content.Combat;
        Seed = seed;
        _route = floor.ObjectiveRoute();
        if (_route.Count == 0) throw new InvalidOperationException($"Invasion floor has no route to objective: {floor.Id}.");
        _routeIndex = _route.Select((point, index) => (point, index)).ToDictionary(x => x.point, x => x.index);
        _sectionCheckpointIndex = floor.Sections.ToDictionary(x => x.Id, x => _routeIndex[x.Checkpoint], StringComparer.Ordinal);

        ValidatePlacedCombatDefinitions(floor.Board, _combat);

        var normalized = formation.Where(x => x.Count > 0).ToArray();
        if (normalized.Length == 0) throw new InvalidOperationException("Invasion formation cannot be empty.");
        UsedDeploymentCapacity = normalized.Sum(x => checked(content.UnitDeploymentCosts.TryGetValue(x.UnitId, out var cost)
            ? cost * x.Count
            : throw new InvalidOperationException($"No deployment cost for invasion unit: {x.UnitId}")));
        if (UsedDeploymentCapacity > content.DeploymentCapacity)
            throw new InvalidOperationException($"Deployment Capacity exceeded: {UsedDeploymentCapacity}/{content.DeploymentCapacity}.");

        _units = new Dictionary<string, RuntimeInvasionUnit>(StringComparer.Ordinal);
        var formationIndex = 0;
        foreach (var entry in normalized)
        {
            if (!_combat.Units.TryGetValue(entry.UnitId, out var definition) || definition.Team != Team.Dungeon)
                throw new InvalidOperationException($"Invalid invasion unit: {entry.UnitId}");
            var role = content.UnitRoleProfile(entry.UnitId);
            for (var i = 0; i < entry.Count; i++)
            {
                var entityId = $"I{formationIndex + 1:D4}";
                _units.Add(entityId, new RuntimeInvasionUnit
                {
                    EntityId = entityId,
                    Definition = definition,
                    FormationIndex = formationIndex++,
                    Archetype = role.Archetype,
                    Position = floor.Board.Entrance,
                    Hp = definition.MaxHp,
                });
            }
        }

        _guards = floor.Board.Guards.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToDictionary(
            x => x.InstanceId,
            x =>
            {
                var definition = _combat.Units[x.DefinitionId];
                return new RuntimeEnemyGuard
                {
                    EntityId = x.InstanceId,
                    Placement = x,
                    Definition = definition,
                    Position = x.Position,
                    Hp = definition.MaxHp,
                };
            },
            StringComparer.Ordinal);

        _trapReadyTick = floor.Board.Traps.ToDictionary(x => x.InstanceId, _ => 0, StringComparer.Ordinal);
        _facilityReadyTick = floor.Board.Facilities.ToDictionary(x => x.InstanceId, _ => 0, StringComparer.Ordinal);
        _spellCooldowns = content.SupportSpells.Keys.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        _objectiveStructureHp = floor.Objective.Kind == InvasionObjectiveKind.CoreBreak ? floor.Objective.StructureMaxHp : 0;
    }

    private InvasionSimulation(
        InvasionFloorDefinition floor,
        InvasionContent content,
        int seed,
        int tick,
        int mp,
        int usedDeploymentCapacity,
        int? retreatRemainingTicks,
        InvasionOutcome outcome,
        ResourceBundle securedLoot,
        int objectiveStructureHp,
        IReadOnlyCollection<string> clearedSectionIds,
        IReadOnlyDictionary<string, RuntimeInvasionUnit> units,
        IReadOnlyDictionary<string, RuntimeEnemyGuard> guards,
        IReadOnlyDictionary<string, int> trapReadyTick,
        IReadOnlyDictionary<string, int> facilityReadyTick,
        IReadOnlyDictionary<string, int> spellCooldowns,
        IReadOnlyList<InvasionEvent> events)
    {
        Floor = floor;
        Content = content;
        _combat = content.Combat;
        Seed = seed;
        Tick = tick;
        Mp = mp;
        UsedDeploymentCapacity = usedDeploymentCapacity;
        _retreatRemainingTicks = retreatRemainingTicks;
        Outcome = outcome;
        SecuredLoot = securedLoot;
        _objectiveStructureHp = objectiveStructureHp;
        _route = floor.ObjectiveRoute();
        _routeIndex = _route.Select((point, index) => (point, index)).ToDictionary(x => x.point, x => x.index);
        _sectionCheckpointIndex = floor.Sections.ToDictionary(x => x.Id, x => _routeIndex[x.Checkpoint], StringComparer.Ordinal);
        _units = new Dictionary<string, RuntimeInvasionUnit>(units, StringComparer.Ordinal);
        _guards = new Dictionary<string, RuntimeEnemyGuard>(guards, StringComparer.Ordinal);
        _trapReadyTick = new Dictionary<string, int>(trapReadyTick, StringComparer.Ordinal);
        _facilityReadyTick = new Dictionary<string, int>(facilityReadyTick, StringComparer.Ordinal);
        _spellCooldowns = new Dictionary<string, int>(spellCooldowns, StringComparer.Ordinal);
        foreach (var id in clearedSectionIds) _clearedSections.Add(id);
        Events.AddRange(events);
    }

    public InvasionFloorDefinition Floor { get; }
    public InvasionContent Content { get; }
    public int Seed { get; }
    public int Tick { get; private set; }
    public int Mp { get; private set; }
    public int UsedDeploymentCapacity { get; }
    public int? RetreatRemainingTicks => _retreatRemainingTicks;
    public int ObjectiveStructureHp => _objectiveStructureHp;
    public int ObjectiveStructureMaxHp => Floor.Objective.Kind == InvasionObjectiveKind.CoreBreak ? Floor.Objective.StructureMaxHp : 0;
    public InvasionOutcome Outcome { get; private set; } = InvasionOutcome.Running;
    public ResourceBundle SecuredLoot { get; private set; } = ResourceBundle.Zero;
    public List<InvasionEvent> Events { get; } = [];
    public IReadOnlyList<GridPoint> Route => _route;
    public IReadOnlySet<string> ClearedSectionIds => _clearedSections;

    public int CurrentSectionIndex
    {
        get
        {
            for (var index = 0; index < Floor.Sections.Count; index++)
                if (!_clearedSections.Contains(Floor.Sections[index].Id)) return index;
            return Math.Max(0, Floor.Sections.Count - 1);
        }
    }

    public int ReserveCount => _units.Values.Count(x => x.Alive && !x.DeploymentRequested);
    public int StagedCount => _units.Values.Count(x => x.Alive && x.DeploymentRequested && !x.Admitted);
    public int ActiveCount => _units.Values.Count(x => x.Alive && x.Admitted);
    public int DefeatedCount => _units.Values.Count(x => !x.Alive);

    public IReadOnlyList<InvasionUnitStateSnapshot> Units => _units.Values
        .OrderBy(x => x.FormationIndex)
        .Select(x => x.Snapshot())
        .ToArray();

    public IReadOnlyList<InvasionUnitStateSnapshot> EnemyGuards => _guards.Values
        .OrderBy(x => x.EntityId, StringComparer.Ordinal)
        .Select(x => x.Snapshot())
        .ToArray();

    public IReadOnlyList<InvasionStaticActorStateSnapshot> StaticActors
        => Floor.Board.Traps.Select(x => new InvasionStaticActorStateSnapshot(
                x.InstanceId, x.DefinitionId, InvasionActorKind.Trap, x.Position, Math.Max(0, _trapReadyTick[x.InstanceId] - Tick)))
            .Concat(Floor.Board.Facilities.Select(x => new InvasionStaticActorStateSnapshot(
                x.InstanceId, x.DefinitionId, InvasionActorKind.Facility, x.Position, Math.Max(0, _facilityReadyTick[x.InstanceId] - Tick))))
            .OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .ToArray();

    public void Deploy(string unitId, int count)
    {
        EnsureRunning();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var candidates = _units.Values
            .Where(x => x.Alive && !x.DeploymentRequested && string.Equals(x.Definition.Id, unitId, StringComparison.Ordinal))
            .OrderBy(x => x.FormationIndex)
            .Take(count)
            .ToArray();
        if (candidates.Length != count) throw new InvalidOperationException($"Not enough reserve units: {unitId} x{count}.");
        foreach (var unit in candidates)
        {
            unit.DeploymentRequested = true;
            Events.Add(new(Tick, InvasionEventType.DeploymentRequested, unit.EntityId, Detail: unit.Definition.Id));
        }
    }

    public void DeployAllRemaining()
    {
        EnsureRunning();
        foreach (var unit in _units.Values.Where(x => x.Alive && !x.DeploymentRequested).OrderBy(x => x.FormationIndex))
        {
            unit.DeploymentRequested = true;
            Events.Add(new(Tick, InvasionEventType.DeploymentRequested, unit.EntityId, Detail: unit.Definition.Id));
        }
    }

    public bool CastSupportSpell(string spellId)
    {
        EnsureRunning();
        if (!Content.SupportSpells.TryGetValue(spellId, out var spell)) throw new InvalidOperationException($"Unknown invasion support spell: {spellId}");
        if (_spellCooldowns[spellId] > 0 || Mp < spell.MpCost) return false;
        var active = _units.Values.Where(x => x.Alive && x.Admitted).OrderBy(x => x.FormationIndex).ToArray();
        if (active.Length == 0) return false;

        switch (spell.Kind)
        {
            case InvasionSupportSpellKind.Heal:
            {
                var target = active.OrderBy(x => x.Hp / (double)x.Definition.MaxHp).ThenBy(x => x.FormationIndex).First();
                var before = target.Hp;
                target.Hp = Math.Min(target.Definition.MaxHp, target.Hp + spell.Magnitude);
                Events.Add(new(Tick, InvasionEventType.SpellCast, spell.Id, target.EntityId, target.Position, target.Hp - before, "heal"));
                break;
            }
            case InvasionSupportSpellKind.Shield:
                foreach (var target in active) target.Shield = checked(target.Shield + spell.Magnitude);
                Events.Add(new(Tick, InvasionEventType.SpellCast, spell.Id, Amount: spell.Magnitude, Detail: "shield"));
                break;
            default:
                throw new InvalidOperationException($"Unsupported invasion support spell kind: {spell.Kind}");
        }

        Mp -= spell.MpCost;
        _spellCooldowns[spellId] = spell.CooldownTicks;
        return true;
    }

    public int SpellCooldownRemaining(string spellId)
        => _spellCooldowns.TryGetValue(spellId, out var value) ? value : throw new InvalidOperationException($"Unknown spell: {spellId}");

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

        TickStatuses();
        ResolveAdmissions();
        ResolveTargets();
        ResolveInvaderMovement();
        ResolveTrapTriggers();
        ResolveGuardMovement();
        ResolveTargets();
        ResolveAttacks();
        ResolveSections();
        ResolveObjectiveArrival();
        ResolveRetreat();
        ResolveWipe();
    }

    public InvasionOutcome RunToEnd(int maxTicks = 50_000)
    {
        while (Outcome == InvasionOutcome.Running && Tick < maxTicks) Step();
        if (Outcome == InvasionOutcome.Running) throw new InvalidOperationException($"Spatial invasion did not finish within {maxTicks} ticks.");
        return Outcome;
    }

    public InvasionSimulationSnapshot CreateSnapshot()
        => new(
            Content.ContentVersion,
            Floor.Id,
            InvasionMapDigest.Compute(Floor),
            Seed,
            Tick,
            Mp,
            UsedDeploymentCapacity,
            _retreatRemainingTicks,
            Outcome,
            SecuredLoot,
            _objectiveStructureHp,
            _clearedSections.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            _units.Values.OrderBy(x => x.FormationIndex).Select(x => x.RuntimeSnapshot()).ToArray(),
            _guards.Values.OrderBy(x => x.EntityId, StringComparer.Ordinal).Select(x => x.RuntimeSnapshot()).ToArray(),
            _trapReadyTick.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new InvasionCooldownSnapshot(x.Key, x.Value)).ToArray(),
            _facilityReadyTick.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new InvasionCooldownSnapshot(x.Key, x.Value)).ToArray(),
            _spellCooldowns.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new InvasionSpellCooldownSnapshot(x.Key, x.Value)).ToArray(),
            Events.ToArray());

    public static InvasionSimulation Restore(
        InvasionSimulationSnapshot snapshot,
        InvasionFloorDefinition floor,
        InvasionContent content)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(content);
        if (!string.Equals(snapshot.ContentVersion, content.ContentVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Spatial invasion snapshot content version mismatch: {snapshot.ContentVersion} != {content.ContentVersion}.");
        if (!string.Equals(snapshot.FloorId, floor.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"Spatial invasion snapshot floor mismatch: {snapshot.FloorId} != {floor.Id}.");
        var expectedMapDigest = InvasionMapDigest.Compute(floor);
        if (!string.Equals(snapshot.MapDigest, expectedMapDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"Spatial invasion snapshot map mismatch: {snapshot.MapDigest} != {expectedMapDigest}.");
        if (snapshot.Tick < 0 || snapshot.Mp < 0 || snapshot.Mp > content.MaxMp || snapshot.UsedDeploymentCapacity <= 0)
            throw new InvalidDataException("Spatial invasion snapshot counters are invalid.");
        if (snapshot.RetreatRemainingTicks is < 0) throw new InvalidDataException("Spatial invasion snapshot retreat state is invalid.");
        if (snapshot.Units.Count == 0) throw new InvalidDataException("Spatial invasion snapshot has no units.");
        if (snapshot.Units.Select(x => x.EntityId).Distinct(StringComparer.Ordinal).Count() != snapshot.Units.Count
            || snapshot.Units.Select(x => x.FormationIndex).Distinct().Count() != snapshot.Units.Count)
            throw new InvalidDataException("Spatial invasion snapshot unit identity is duplicated.");

        ValidatePlacedCombatDefinitions(floor.Board, content.Combat);
        var route = floor.ObjectiveRoute();
        var units = snapshot.Units.OrderBy(x => x.FormationIndex).ToDictionary(x => x.EntityId, x =>
        {
            if (!content.Combat.Units.TryGetValue(x.DefinitionId, out var definition) || definition.Team != Team.Dungeon)
                throw new InvalidDataException($"Unknown spatial invasion unit: {x.DefinitionId}.");
            if (x.Hp < 0 || x.Hp > definition.MaxHp || x.Shield < 0 || x.RouteProgressUnits < 0
                || x.PathIndex < 0 || x.PathIndex >= route.Count || x.MoveRemainder < 0 || x.NextMoveTick < 0 || x.NextAttackTick < 0)
                throw new InvalidDataException($"Invalid spatial invasion unit runtime: {x.EntityId}.");
            if (x.Position != route[x.PathIndex])
                throw new InvalidDataException($"Spatial invasion unit position/path mismatch: {x.EntityId}.");
            var runtime = new RuntimeInvasionUnit
            {
                EntityId = x.EntityId,
                Definition = definition,
                FormationIndex = x.FormationIndex,
                Archetype = x.Archetype,
                Position = x.Position,
                Hp = x.Hp,
                Shield = x.Shield,
                RouteProgressUnits = x.RouteProgressUnits,
                PathIndex = x.PathIndex,
                MoveRemainder = x.MoveRemainder,
                NextMoveTick = x.NextMoveTick,
                NextAttackTick = x.NextAttackTick,
                DeploymentRequested = x.DeploymentRequested,
                Admitted = x.Admitted,
                TargetEntityId = x.TargetEntityId,
            };
            RestoreStatuses(runtime.Statuses, x.Statuses, x.EntityId);
            return runtime;
        }, StringComparer.Ordinal);

        var expectedCapacity = units.Values.Sum(x => checked(content.UnitDeploymentCosts.TryGetValue(x.Definition.Id, out var cost)
            ? cost
            : throw new InvalidDataException($"No deployment cost for spatial invasion unit: {x.Definition.Id}.")));
        if (expectedCapacity != snapshot.UsedDeploymentCapacity || expectedCapacity > content.DeploymentCapacity)
            throw new InvalidDataException("Spatial invasion snapshot deployment capacity is inconsistent.");

        var placements = floor.Board.Guards.ToDictionary(x => x.InstanceId, StringComparer.Ordinal);
        if (snapshot.Guards.Select(x => x.EntityId).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(placements.Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) is false)
            throw new InvalidDataException("Spatial invasion snapshot guard set does not match floor authority.");
        var guards = snapshot.Guards.ToDictionary(x => x.EntityId, x =>
        {
            var placement = placements[x.EntityId];
            if (!string.Equals(placement.DefinitionId, x.DefinitionId, StringComparison.Ordinal)
                || !content.Combat.Units.TryGetValue(x.DefinitionId, out var definition) || definition.Team != Team.Invader)
                throw new InvalidDataException($"Invalid spatial invasion guard definition: {x.EntityId}/{x.DefinitionId}.");
            if (!floor.Board.InBounds(x.Position) || !floor.Board.IsWalkable(x.Position) || x.Hp < 0 || x.Hp > definition.MaxHp || x.NextMoveTick < 0 || x.NextAttackTick < 0)
                throw new InvalidDataException($"Invalid spatial invasion guard runtime: {x.EntityId}.");
            var runtime = new RuntimeEnemyGuard
            {
                EntityId = x.EntityId,
                Placement = placement,
                Definition = definition,
                Position = x.Position,
                Hp = x.Hp,
                NextMoveTick = x.NextMoveTick,
                NextAttackTick = x.NextAttackTick,
                TargetEntityId = x.TargetEntityId,
            };
            RestoreStatuses(runtime.Statuses, x.Statuses, x.EntityId);
            return runtime;
        }, StringComparer.Ordinal);

        var trapCooldowns = RestoreCooldownMap(snapshot.TrapCooldowns, floor.Board.Traps.Select(x => x.InstanceId), "trap");
        var facilityCooldowns = RestoreCooldownMap(snapshot.FacilityCooldowns, floor.Board.Facilities.Select(x => x.InstanceId), "facility");
        var spellCooldowns = snapshot.SpellCooldowns.ToDictionary(x => x.SpellId, x => x.RemainingTicks, StringComparer.Ordinal);
        if (!spellCooldowns.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(content.SupportSpells.Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal)
            || spellCooldowns.Values.Any(x => x < 0))
            throw new InvalidDataException("Spatial invasion snapshot spell cooldown state is invalid.");
        if (snapshot.ClearedSectionIds.Distinct(StringComparer.Ordinal).Count() != snapshot.ClearedSectionIds.Count
            || snapshot.ClearedSectionIds.Any(x => floor.Sections.All(section => !string.Equals(section.Id, x, StringComparison.Ordinal))))
            throw new InvalidDataException("Spatial invasion snapshot cleared sections are invalid.");
        var maxObjectiveHp = floor.Objective.Kind == InvasionObjectiveKind.CoreBreak ? floor.Objective.StructureMaxHp : 0;
        if (snapshot.ObjectiveStructureHp < 0 || snapshot.ObjectiveStructureHp > maxObjectiveHp)
            throw new InvalidDataException("Spatial invasion snapshot objective HP is invalid.");

        return new InvasionSimulation(
            floor, content, snapshot.Seed, snapshot.Tick, snapshot.Mp, snapshot.UsedDeploymentCapacity,
            snapshot.RetreatRemainingTicks, snapshot.Outcome, snapshot.SecuredLoot, snapshot.ObjectiveStructureHp,
            snapshot.ClearedSectionIds, units, guards, trapCooldowns, facilityCooldowns, spellCooldowns, snapshot.Events);
    }

    public string ResultDigest()
    {
        var snapshot = CreateSnapshot();
        var builder = new System.Text.StringBuilder();
        builder.Append(snapshot.FloorId).Append('|').Append(snapshot.MapDigest).Append('|').Append(snapshot.Seed).Append('|').Append(snapshot.Tick).Append('|')
            .Append(snapshot.Outcome).Append('|').Append(snapshot.Mp).Append('|').Append(snapshot.ObjectiveStructureHp).Append('|')
            .Append(snapshot.SecuredLoot).Append('\n');
        foreach (var section in snapshot.ClearedSectionIds.OrderBy(x => x, StringComparer.Ordinal)) builder.Append("C|").Append(section).Append('\n');
        foreach (var unit in snapshot.Units)
        {
            builder.Append("U|").Append(unit.EntityId).Append('|').Append(unit.DefinitionId).Append('|').Append(unit.Position).Append('|')
                .Append(unit.Hp).Append('|').Append(unit.Shield).Append('|').Append(unit.RouteProgressUnits).Append('|').Append(unit.PathIndex).Append('|')
                .Append(unit.DeploymentRequested).Append('|').Append(unit.Admitted).Append('|').Append(unit.TargetEntityId).Append('\n');
            foreach (var status in unit.Statuses.OrderBy(x => x.Kind)) builder.Append("US|").Append(unit.EntityId).Append('|').Append(status).Append('\n');
        }
        foreach (var guard in snapshot.Guards)
        {
            builder.Append("G|").Append(guard.EntityId).Append('|').Append(guard.DefinitionId).Append('|').Append(guard.Position).Append('|')
                .Append(guard.Hp).Append('|').Append(guard.TargetEntityId).Append('\n');
            foreach (var status in guard.Statuses.OrderBy(x => x.Kind)) builder.Append("GS|").Append(guard.EntityId).Append('|').Append(status).Append('\n');
        }
        foreach (var value in snapshot.TrapCooldowns) builder.Append("T|").Append(value.Id).Append('|').Append(value.ReadyTick).Append('\n');
        foreach (var value in snapshot.FacilityCooldowns) builder.Append("F|").Append(value.Id).Append('|').Append(value.ReadyTick).Append('\n');
        foreach (var value in snapshot.SpellCooldowns) builder.Append("S|").Append(value.SpellId).Append('|').Append(value.RemainingTicks).Append('\n');
        foreach (var value in snapshot.Events)
            builder.Append("E|").Append(value.Tick).Append('|').Append(value.Type).Append('|').Append(value.ActorId).Append('|')
                .Append(value.TargetId).Append('|').Append(value.Position).Append('|').Append(value.Amount).Append('|').Append(value.Detail).Append('|')
                .Append(value.SourcePosition).Append('|').Append(value.SourceDefinitionId).Append('\n');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void RestoreStatuses(Dictionary<StatusKind, StatusEffect> target, IReadOnlyList<InvasionStatusSnapshot> source, string actorId)
    {
        if (source.Select(x => x.Kind).Distinct().Count() != source.Count || source.Any(x => x.Strength < 0 || x.RemainingTicks <= 0))
            throw new InvalidDataException($"Invalid spatial invasion status state: {actorId}.");
        foreach (var status in source) target[status.Kind] = new StatusEffect(status.Kind, status.Strength, status.RemainingTicks);
    }

    private static Dictionary<string, int> RestoreCooldownMap(IReadOnlyList<InvasionCooldownSnapshot> source, IEnumerable<string> expectedIds, string kind)
    {
        var map = source.ToDictionary(x => x.Id, x => x.ReadyTick, StringComparer.Ordinal);
        if (!map.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expectedIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal)
            || map.Values.Any(x => x < 0))
            throw new InvalidDataException($"Spatial invasion snapshot {kind} cooldown state is invalid.");
        return map;
    }

    private void ResolveAdmissions()
    {
        foreach (var candidate in _units.Values.Where(x => x.Alive && x.DeploymentRequested && !x.Admitted).OrderBy(x => x.FormationIndex))
        {
            var nearest = _units.Values.Where(x => x.Alive && x.Admitted)
                .OrderBy(x => x.RouteProgressUnits)
                .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (nearest is not null)
            {
                var spacing = TrafficRules.MinimumSpacingUnits(candidate.Definition.BodySizeClass, nearest.Definition.BodySizeClass);
                if (nearest.RouteProgressUnits < spacing) break;
            }

            candidate.Admitted = true;
            candidate.Position = Floor.Board.Entrance;
            candidate.RouteProgressUnits = 0;
            candidate.PathIndex = 0;
            candidate.EnteredCells.Add(candidate.Position);
            Events.Add(new(Tick, InvasionEventType.UnitAdmitted, candidate.EntityId, Position: candidate.Position, Detail: candidate.Definition.Id));
        }
    }

    private void ResolveTargets()
    {
        foreach (var guard in _guards.Values.Where(x => x.Alive).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            var zone = GuardZone.Resolve(Floor.Board, guard.Placement);
            IEnumerable<RuntimeInvasionUnit> candidates = _units.Values.Where(x => x.Alive && x.Admitted && zone.Contains(x.Position));
            if (guard.Definition.Role == UnitRole.Ranged)
                candidates = candidates.Where(x => CanAttack(guard.Position, guard.Definition.AttackRange, x.Position));
            guard.TargetEntityId = candidates
                .OrderByDescending(x => x.RouteProgressUnits)
                .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                .Select(x => x.EntityId)
                .FirstOrDefault();
        }

        foreach (var unit in _units.Values.Where(x => x.Alive && x.Admitted).OrderBy(x => x.FormationIndex))
        {
            unit.TargetEntityId = _guards.Values.Where(x => x.Alive && CanAttack(unit.Position, unit.Definition.AttackRange, x.Position))
                .OrderBy(x => x.Position.ManhattanDistance(unit.Position))
                .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                .Select(x => x.EntityId)
                .FirstOrDefault();
        }
    }

    private void ResolveInvaderMovement()
    {
        RuntimeInvasionUnit? leader = null;
        var objectiveProgress = RouteProgress.AtCellCenter(_route.Count - 1).Units;
        foreach (var unit in _units.Values.Where(x => x.Alive && x.Admitted)
                     .OrderByDescending(x => x.RouteProgressUnits)
                     .ThenBy(x => x.EntityId, StringComparer.Ordinal))
        {
            var current = unit.RouteProgressUnits;
            var desired = current;
            if (unit.TargetEntityId is null && !CanAttackObjective(unit) && !CombatStatusRules.HasStatus(unit.Statuses, StatusKind.Freeze) && Tick >= unit.NextMoveTick && current < objectiveProgress)
                desired = Math.Min(objectiveProgress, current + ComputeMoveAdvance(unit));

            if (leader is not null)
            {
                var spacing = TrafficRules.MinimumSpacingUnits(unit.Definition.BodySizeClass, leader.Definition.BodySizeClass);
                desired = Math.Min(desired, leader.RouteProgressUnits - spacing);
            }
            desired = Math.Max(current, desired);
            SetRouteProgress(unit, desired);
            leader = unit;
        }
    }

    private void SetRouteProgress(RuntimeInvasionUnit unit, long desiredProgress)
    {
        var clamped = RouteProgress.Clamp(new RouteProgress(desiredProgress), _route.Count).Units;
        if (clamped <= unit.RouteProgressUnits) return;
        var oldIndex = unit.PathIndex;
        unit.RouteProgressUnits = clamped;
        var newIndex = new RouteProgress(clamped).ToLogicalCellIndex(_route.Count);
        if (newIndex == oldIndex) return;
        for (var index = oldIndex + 1; index <= newIndex; index++)
        {
            unit.PathIndex = index;
            unit.Position = _route[index];
            unit.EnteredCells.Add(unit.Position);
            Events.Add(new(Tick, InvasionEventType.UnitMoved, unit.EntityId, Position: unit.Position, Amount: index));
        }
    }

    private void ResolveTrapTriggers()
    {
        foreach (var unit in _units.Values.Where(x => x.Alive && x.Admitted).OrderBy(x => x.FormationIndex))
        {
            foreach (var cell in unit.EnteredCells)
            foreach (var placed in Floor.Board.Traps.Where(x => x.Position == cell).OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            {
                if (_trapReadyTick[placed.InstanceId] > Tick) continue;
                var definition = _combat.Traps[placed.DefinitionId];
                ApplyDamage(unit, definition.Damage, placed.InstanceId, placed.Position, definition.Id, InvasionEventType.TrapTriggered);
                _trapReadyTick[placed.InstanceId] = Tick + definition.CooldownTicks;
                if (definition.StatusKind is { } status && unit.Alive)
                    CombatStatusRules.Merge(unit.Statuses, new StatusEffect(status, definition.StatusStrength, definition.StatusDurationTicks));
            }
            unit.EnteredCells.Clear();
        }
    }

    private void ResolveGuardMovement()
    {
        foreach (var guard in _guards.Values.Where(x => x.Alive).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (CombatStatusRules.HasStatus(guard.Statuses, StatusKind.Freeze) || Tick < guard.NextMoveTick || guard.TargetEntityId is null) continue;
            var target = _units.GetValueOrDefault(guard.TargetEntityId);
            if (target is null || !target.Alive || !target.Admitted || guard.Position.ManhattanDistance(target.Position) <= guard.Definition.AttackRange) continue;
            var zone = GuardZone.Resolve(Floor.Board, guard.Placement);
            var neighbors = GridGeometry.NeighborsNorthEastSouthWest(guard.Position).ToArray();
            var next = neighbors.Where(x => zone.Contains(x) && Floor.Board.IsWalkable(x))
                .OrderBy(x => x.ManhattanDistance(target.Position))
                .ThenBy(x => Array.IndexOf(neighbors, x))
                .FirstOrDefault(guard.Position);
            if (next == guard.Position) continue;
            guard.Position = next;
            guard.NextMoveTick = Tick + CombatMovementRules.EffectiveMoveInterval(guard.Definition.MoveIntervalTicks, guard.Statuses);
            Events.Add(new(Tick, InvasionEventType.GuardMoved, guard.EntityId, target.EntityId, guard.Position, SourceDefinitionId: guard.Definition.Id));
        }
    }

    private void ResolveAttacks()
    {
        foreach (var guard in _guards.Values.Where(x => x.Alive).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (CombatStatusRules.HasStatus(guard.Statuses, StatusKind.Freeze) || Tick < guard.NextAttackTick) continue;
            if (TryHealAlly(guard, _guards.Values.Where(x => x.Alive)))
            {
                guard.NextAttackTick = Tick + Math.Max(1, guard.Definition.AttackCooldownTicks);
                continue;
            }
            if (guard.TargetEntityId is null) continue;
            var target = _units.GetValueOrDefault(guard.TargetEntityId);
            if (target is null || !target.Alive || !CanAttack(guard.Position, guard.Definition.AttackRange, target.Position)) continue;
            ApplyDamage(target, guard.Definition.Damage, guard.EntityId, guard.Position, guard.Definition.Id, InvasionEventType.GuardAttack);
            ApplyUnitAttackStatus(guard, target);
            guard.NextAttackTick = Tick + Math.Max(1, guard.Definition.AttackCooldownTicks);
        }

        foreach (var unit in _units.Values.Where(x => x.Alive && x.Admitted).OrderBy(x => x.FormationIndex))
        {
            if (CombatStatusRules.HasStatus(unit.Statuses, StatusKind.Freeze) || Tick < unit.NextAttackTick) continue;
            if (TryHealAlly(unit, _units.Values.Where(x => x.Alive && x.Admitted)))
            {
                unit.NextAttackTick = Tick + Math.Max(1, unit.Definition.AttackCooldownTicks);
                continue;
            }

            if (unit.TargetEntityId is { } guardId && _guards.TryGetValue(guardId, out var guard) && guard.Alive && CanAttack(unit.Position, unit.Definition.AttackRange, guard.Position))
            {
                ApplyDamage(guard, unit.Definition.Damage, unit.EntityId, unit.Position, unit.Definition.Id);
                ApplyUnitAttackStatus(unit, guard);
                unit.NextAttackTick = Tick + Math.Max(1, unit.Definition.AttackCooldownTicks);
                continue;
            }

            if (Floor.Objective.Kind == InvasionObjectiveKind.CoreBreak && CanAttackObjective(unit))
            {
                var damage = Math.Min(_objectiveStructureHp, unit.Definition.Damage);
                _objectiveStructureHp -= damage;
                Events.Add(new(Tick, InvasionEventType.ObjectiveDamaged, unit.EntityId, "OBJECTIVE", Floor.Objective.Position, damage, Floor.Objective.Kind.ToString(), unit.Position, unit.Definition.Id));
                unit.NextAttackTick = Tick + Math.Max(1, unit.Definition.AttackCooldownTicks);
                if (_objectiveStructureHp <= 0) CompleteObjective(unit.EntityId);
            }
        }

        foreach (var placed in Floor.Board.Facilities.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
        {
            if (_facilityReadyTick[placed.InstanceId] > Tick) continue;
            var definition = _combat.Facilities[placed.DefinitionId];
            var target = _units.Values.Where(x => x.Alive && x.Admitted)
                .Where(x => placed.Position.ManhattanDistance(x.Position) <= definition.Range)
                .Where(x => DungeonLineOfSight.HasLineOfSight(Floor.Board, placed.Position, x.Position))
                .OrderByDescending(x => x.RouteProgressUnits)
                .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (target is null) continue;
            ApplyDamage(target, definition.Damage, placed.InstanceId, placed.Position, definition.Id, InvasionEventType.FacilityAttack);
            _facilityReadyTick[placed.InstanceId] = Tick + definition.CooldownTicks;
            if (definition.StatusKind is { } status && target.Alive)
                CombatStatusRules.Merge(target.Statuses, new StatusEffect(status, definition.StatusStrength, definition.StatusDurationTicks));
        }
    }

    private void ResolveSections()
    {
        foreach (var section in Floor.Sections)
        {
            if (_clearedSections.Contains(section.Id)) continue;
            var checkpointProgress = RouteProgress.AtCellCenter(_sectionCheckpointIndex[section.Id]).Units;
            if (_guards.Values.Any(x => x.Alive && section.Cells.Contains(x.Position))) return;
            var crosser = _units.Values.Where(x => x.Alive && x.Admitted && x.RouteProgressUnits >= checkpointProgress)
                .OrderByDescending(x => x.RouteProgressUnits)
                .ThenBy(x => x.FormationIndex)
                .FirstOrDefault();
            if (crosser is null) return;
            Events.Add(new(Tick, InvasionEventType.SectionEntered, section.Id, crosser.EntityId, section.Checkpoint));
            _clearedSections.Add(section.Id);
            SecuredLoot = SecuredLoot.Add(section.Loot);
            Events.Add(new(Tick, InvasionEventType.SectionCleared, section.Id, crosser.EntityId, section.Checkpoint));
            Events.Add(new(Tick, InvasionEventType.LootSecured, section.Id, crosser.EntityId, section.Checkpoint,
                section.Loot.Stone + section.Loot.Iron + section.Loot.Soul + section.Loot.Relic));
        }
    }

    private void ResolveObjectiveArrival()
    {
        if (Outcome != InvasionOutcome.Running) return;
        switch (Floor.Objective.Kind)
        {
            case InvasionObjectiveKind.Raid:
            {
                var endpointProgress = RouteProgress.AtCellCenter(_route.Count - 1).Units;
                var actor = _units.Values.Where(x => x.Alive && x.Admitted && x.RouteProgressUnits >= endpointProgress)
                    .OrderBy(x => x.FormationIndex).FirstOrDefault();
                if (actor is not null) CompleteObjective(actor.EntityId);
                break;
            }
            case InvasionObjectiveKind.Eliminate:
                if (Floor.Objective.TargetInstanceId is { } targetId && _guards.TryGetValue(targetId, out var target) && !target.Alive)
                    CompleteObjective(targetId);
                break;
            case InvasionObjectiveKind.CoreBreak:
                if (_objectiveStructureHp <= 0) CompleteObjective("OBJECTIVE");
                break;
        }
    }

    private void CompleteObjective(string actorId)
    {
        if (Outcome != InvasionOutcome.Running) return;
        Outcome = InvasionOutcome.Success;
        Events.Add(new(Tick, InvasionEventType.ObjectiveCompleted, actorId, "OBJECTIVE", Floor.Objective.Position, Detail: Floor.Objective.Kind.ToString()));
    }

    private void ResolveRetreat()
    {
        if (Outcome != InvasionOutcome.Running || _retreatRemainingTicks is null) return;
        _retreatRemainingTicks--;
        if (_retreatRemainingTicks > 0) return;
        if (_units.Values.Any(x => x.Alive))
        {
            Outcome = InvasionOutcome.Retreated;
            Events.Add(new(Tick, InvasionEventType.RetreatCompleted, "player"));
        }
    }

    private void ResolveWipe()
    {
        if (Outcome != InvasionOutcome.Running || _units.Values.Any(x => x.Alive)) return;
        Outcome = InvasionOutcome.Wiped;
        Events.Add(new(Tick, InvasionEventType.Wiped, "party"));
    }

    private void TickStatuses()
    {
        foreach (var unit in _units.Values.Where(x => x.Alive)) TickStatuses(unit, unit.Statuses);
        foreach (var guard in _guards.Values.Where(x => x.Alive)) TickStatuses(guard, guard.Statuses);
    }

    private void TickStatuses(RuntimeCombatActor actor, Dictionary<StatusKind, StatusEffect> statuses)
    {
        if (statuses.TryGetValue(StatusKind.Poison, out var poison) && poison.RemainingTicks > 0 && Tick % TicksPerSecond == 0)
        {
            actor.Hp = Math.Max(0, actor.Hp - poison.Strength);
            if (actor.Hp == 0) RecordDefeat(actor, "status.poison");
        }
        foreach (var kind in statuses.Keys.ToArray())
        {
            var status = statuses[kind];
            if (status.RemainingTicks <= 1) statuses.Remove(kind);
            else statuses[kind] = status with { RemainingTicks = status.RemainingTicks - 1 };
        }
    }

    private bool TryHealAlly(RuntimeCombatActor healer, IEnumerable<RuntimeCombatActor> allies)
    {
        if (healer.Definition.HealPower <= 0) return false;
        var candidates = allies
            .Where(x => x.EntityId != healer.EntityId)
            .Select((x, index) => new CombatAllyCandidate(x.EntityId, x.Position, x.Hp, x.Definition.MaxHp, x.Definition.Role, index))
            .ToArray();
        var decision = CombatUnitBehaviorRules.SelectHealTarget(healer.Definition, healer.Position, candidates, Floor.Board);
        if (decision is null || decision.Amount <= 0) return false;
        var target = allies.Single(x => x.EntityId == decision.TargetEntityId);
        target.Hp = Math.Min(target.Definition.MaxHp, target.Hp + decision.Amount);
        Events.Add(new(Tick, InvasionEventType.SpellCast, healer.EntityId, target.EntityId, target.Position, decision.Amount, "unit-heal", healer.Position, healer.Definition.Id));
        return true;
    }

    private static void ApplyUnitAttackStatus(RuntimeCombatActor attacker, RuntimeCombatActor target)
    {
        if (!target.Alive) return;
        if (CombatUnitBehaviorRules.AttackStatus(attacker.Definition) is { } status)
            CombatStatusRules.Merge(target.Statuses, status);
    }

    private bool CanAttack(GridPoint from, int range, GridPoint to)
        => CombatUnitBehaviorRules.CanAttack(Floor.Board, from, range, to);

    private bool CanAttackObjective(RuntimeInvasionUnit unit)
        => Floor.Objective.Kind == InvasionObjectiveKind.CoreBreak && CanAttack(unit.Position, unit.Definition.AttackRange, Floor.Objective.Position);

    private static long ComputeMoveAdvance(RuntimeInvasionUnit unit)
    {
        var result = CombatMovementRules.ComputeRouteAdvance(unit.Definition.MoveIntervalTicks, unit.Statuses, unit.MoveRemainder);
        unit.MoveRemainder = result.Remainder;
        return result.Advance;
    }

    private void ApplyDamage(RuntimeInvasionUnit target, int rawDamage, string sourceId, GridPoint sourcePosition, string sourceDefinitionId, InvasionEventType eventType)
    {
        var incoming = Math.Max(0, rawDamage);
        var absorbed = Math.Min(target.Shield, incoming);
        target.Shield -= absorbed;
        var damage = incoming - absorbed;
        if (damage > 0) target.Hp = Math.Max(0, target.Hp - damage);
        Events.Add(new(Tick, eventType, sourceId, target.EntityId, target.Position, incoming, SourcePosition: sourcePosition, SourceDefinitionId: sourceDefinitionId));
        if (!target.Alive) RecordDefeat(target, sourceId);
    }

    private void ApplyDamage(RuntimeEnemyGuard target, int rawDamage, string sourceId, GridPoint sourcePosition, string sourceDefinitionId)
    {
        var damage = Math.Max(0, rawDamage);
        target.Hp = Math.Max(0, target.Hp - damage);
        Events.Add(new(Tick, InvasionEventType.UnitAttack, sourceId, target.EntityId, target.Position, damage, SourcePosition: sourcePosition, SourceDefinitionId: sourceDefinitionId));
        if (!target.Alive) RecordDefeat(target, sourceId);
    }

    private void RecordDefeat(RuntimeCombatActor actor, string sourceId)
    {
        if (actor.DefeatRecorded) return;
        actor.DefeatRecorded = true;
        actor.TargetEntityId = null;
        var type = actor is RuntimeEnemyGuard ? InvasionEventType.GuardDefeated : InvasionEventType.UnitDefeated;
        Events.Add(new(Tick, type, actor.EntityId, sourceId, actor.Position, Detail: actor.Definition.Id));
    }

    private static void ValidatePlacedCombatDefinitions(DungeonState board, DungeonCombatContent combat)
    {
        foreach (var guard in board.Guards)
        {
            if (!combat.Units.TryGetValue(guard.DefinitionId, out var definition) || definition.Team != Team.Invader)
                throw new InvalidOperationException($"Enemy dungeon guard must reference an Invader-team unit definition: {guard.InstanceId}/{guard.DefinitionId}.");
        }
        foreach (var trap in board.Traps)
            if (!combat.Traps.ContainsKey(trap.DefinitionId)) throw new InvalidOperationException($"Unknown enemy dungeon trap: {trap.InstanceId}/{trap.DefinitionId}.");
        foreach (var facility in board.Facilities)
            if (!combat.Facilities.ContainsKey(facility.DefinitionId)) throw new InvalidOperationException($"Unknown enemy dungeon facility: {facility.InstanceId}/{facility.DefinitionId}.");
    }

    private void EnsureRunning()
    {
        if (Outcome != InvasionOutcome.Running) throw new InvalidOperationException($"Invasion is already complete: {Outcome}.");
    }

    private abstract class RuntimeCombatActor
    {
        public required string EntityId { get; init; }
        public required UnitDefinition Definition { get; init; }
        public required GridPoint Position { get; set; }
        public required int Hp { get; set; }
        public string? TargetEntityId { get; set; }
        public int NextAttackTick { get; set; }
        public int NextMoveTick { get; set; }
        public bool DefeatRecorded { get; set; }
        public Dictionary<StatusKind, StatusEffect> Statuses { get; } = [];
        public bool Alive => Hp > 0;
    }

    private sealed class RuntimeInvasionUnit : RuntimeCombatActor
    {
        public required int FormationIndex { get; init; }
        public required InvasionUnitArchetype Archetype { get; init; }
        public int Shield { get; set; }
        public bool DeploymentRequested { get; set; }
        public bool Admitted { get; set; }
        public long RouteProgressUnits { get; set; }
        public int PathIndex { get; set; }
        public int MoveRemainder { get; set; }
        public List<GridPoint> EnteredCells { get; } = [];

        public InvasionUnitStateSnapshot Snapshot()
            => new(EntityId, Definition.Id, Definition.Team, FormationIndex, Position, Hp, Definition.MaxHp, Shield, RouteProgressUnits,
                DeploymentRequested, Admitted, Alive, TargetEntityId, Archetype);

        public InvasionUnitRuntimeSnapshot RuntimeSnapshot()
            => new(EntityId, Definition.Id, FormationIndex, Position, Hp, Shield, RouteProgressUnits, PathIndex, MoveRemainder,
                NextMoveTick, NextAttackTick, DeploymentRequested, Admitted, TargetEntityId, Archetype,
                Statuses.OrderBy(x => x.Key).Select(x => new InvasionStatusSnapshot(x.Key, x.Value.Strength, x.Value.RemainingTicks)).ToArray());
    }

    private sealed class RuntimeEnemyGuard : RuntimeCombatActor
    {
        public required PlacedGuard Placement { get; init; }

        public InvasionUnitStateSnapshot Snapshot()
            => new(EntityId, Definition.Id, Definition.Team, -1, Position, Hp, Definition.MaxHp, 0, 0,
                true, true, Alive, TargetEntityId);

        public InvasionGuardRuntimeSnapshot RuntimeSnapshot()
            => new(EntityId, Definition.Id, Position, Hp, NextMoveTick, NextAttackTick, TargetEntityId,
                Statuses.OrderBy(x => x.Key).Select(x => new InvasionStatusSnapshot(x.Key, x.Value.Strength, x.Value.RemainingTicks)).ToArray());
    }
}
