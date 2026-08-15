using System.Security.Cryptography;
using System.Text;

namespace DungeonDefense.Core;

public enum DefenseOutcome
{
    Running,
    Success,
    Failure,
}

public enum DefenseEventType
{
    WaveStart,
    WaveEnd,
    Spawn,
    Move,
    BossRouteBreakTelegraph,
    BossRouteBreakActivated,
    TrapTriggered,
    Attack,
    Heal,
    StatusApplied,
    SpellCast,
    FloorEntered,
    FloorRegroupWait,
    FloorBreached,
    FloorTransitioned,
    CoreReached,
    CoreDamaged,
    Death,
    DefenseSuccess,
    DefenseFailure,
}

public sealed record DefenseEvent(
    int Tick,
    DefenseEventType Type,
    string ActorId,
    string? TargetId = null,
    GridPoint? Position = null,
    int Amount = 0,
    string? Detail = null,
    string FloorId = "floor.001");

public sealed record UnitSnapshot(
    string EntityId,
    string DefinitionId,
    Team Team,
    GridPoint Position,
    int Hp,
    int MaxHp,
    int PathIndex,
    bool Alive,
    string? TargetEntityId,
    string FloorId = "floor.001",
    bool AwaitingFloorTransition = false);

public sealed record SpellCastResult(bool Success, string? Error);

internal sealed class RuntimeUnit
{
    public required string EntityId { get; init; }
    public required string? SourceInstanceId { get; init; }
    public required UnitDefinition Definition { get; init; }
    public required string FloorId { get; set; }
    public required GridPoint Position { get; set; }
    public required int Hp { get; set; }
    public required int MaxHp { get; init; }
    public required int PathIndex { get; set; }
    public required int NextMoveTick { get; set; }
    public required int NextAttackTick { get; set; }
    public required GridPoint HomePosition { get; init; }
    public string? TargetEntityId { get; set; }
    public bool AwaitingFloorTransition { get; set; }
    public bool CoreReachedLogged { get; set; }
    public bool DeathLogged { get; set; }
    public int BossRouteBreakUses { get; set; }
    public int? BossRouteBreakReadyTick { get; set; }
    public int? BossRouteBreakLandingIndex { get; set; }
    public Dictionary<StatusKind, StatusEffect> Statuses { get; } = [];
    public bool Alive => Hp > 0;
}

internal sealed class FloorRuntime
{
    public FloorRuntime(DungeonFloorState floor)
    {
        Id = floor.Id.Value;
        Depth = floor.Depth;
        EndpointKind = floor.EndpointKind;
        Board = floor.Board.Clone();
        Route = DungeonPathfinder.FindRoute(Board).ToArray();
        if (Route.Length == 0) throw new InvalidOperationException($"Defense cannot start because {Id} has no entrance-to-endpoint route.");
        RouteProgress = Route.Select((point, index) => (point, index)).ToDictionary(x => x.point, x => x.index);
    }

    public string Id { get; }
    public int Depth { get; }
    public FloorEndpointKind EndpointKind { get; }
    public DungeonState Board { get; }
    public GridPoint[] Route { get; }
    public Dictionary<GridPoint, int> RouteProgress { get; }
}

internal sealed record QueuedSpell(string SpellId, GridPoint Target, string? TargetEntityId, string FloorId);

public sealed class DefenseSimulation
{
    public const int TicksPerSecond = 20;
    public const int TickMilliseconds = 50;

    private readonly PlayerDungeonState _dungeon;
    private readonly DefenseContent _content;
    private readonly FloorRuntime[] _floors;
    private readonly Dictionary<string, FloorRuntime> _floorById;
    private readonly List<RuntimeUnit> _units = [];
    private readonly Dictionary<string, PlacedGuard> _guardPlacements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _trapReadyTick = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _facilityReadyTick = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _facilityTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _spellReadyTick = new(StringComparer.Ordinal);
    private readonly List<QueuedSpell> _queuedSpells = [];
    private readonly Dictionary<string, List<GridPoint>> _enteredCells = new(StringComparer.Ordinal);
    private int[] _spawnCounts;
    private int _waveStartTick;
    private int _nextEntityId = 1;
    private int _currentFloorIndex;

    public DefenseSimulation(DungeonState dungeon, DefenseContent content, int seed)
        : this(PlayerDungeonState.FromSingleFloor(dungeon, "legacy.single"), content, seed)
    {
    }

    public DefenseSimulation(PlayerDungeonState dungeon, DefenseContent content, int seed)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Waves.Count == 0) throw new InvalidOperationException("Defense content must contain at least one wave.");
        ValidateBossRouteBreakContent(content);

        _dungeon = dungeon.Clone();
        _content = content;
        Seed = seed;
        _floors = _dungeon.Floors.OrderBy(x => x.Depth).Select(x => new FloorRuntime(x)).ToArray();
        _floorById = _floors.ToDictionary(x => x.Id, StringComparer.Ordinal);
        _spawnCounts = new int[_content.Waves[0].SpawnGroups.Count];
        CoreHp = content.CoreMaxHp;
        Mp = 0;

        foreach (var floor in _floors)
        {
            foreach (var trap in floor.Board.Traps) _trapReadyTick[PlacementKey(floor.Id, trap.InstanceId)] = 0;
            foreach (var facility in floor.Board.Facilities)
            {
                var key = PlacementKey(floor.Id, facility.InstanceId);
                _facilityReadyTick[key] = 0;
                _facilityTargets[key] = null;
            }

            foreach (var guard in floor.Board.Guards.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            {
                var definition = _content.Units[guard.DefinitionId];
                var homeRoom = floor.Board.RoomAt(guard.Position);
                var maxHp = ApplyPercentBonus(definition.MaxHp, homeRoom?.GuardHpBonusPercent ?? 0);
                var runtimeId = RuntimeGuardId(floor.Id, guard.InstanceId);
                _guardPlacements[runtimeId] = guard;
                _units.Add(new RuntimeUnit
                {
                    EntityId = runtimeId,
                    SourceInstanceId = guard.InstanceId,
                    Definition = definition,
                    FloorId = floor.Id,
                    Position = guard.Position,
                    HomePosition = guard.Position,
                    Hp = maxHp,
                    MaxHp = maxHp,
                    PathIndex = floor.RouteProgress.GetValueOrDefault(guard.Position, -1),
                    NextMoveTick = 0,
                    NextAttackTick = 0,
                });
            }
        }

        foreach (var spell in _content.Spells.Values) _spellReadyTick[spell.Id] = 0;
    }

    public int Seed { get; }
    public int Tick { get; private set; }
    public int WaveIndex { get; private set; }
    public int CoreHp { get; private set; }
    public int CoreMaxHp => _content.CoreMaxHp;
    public int Mp { get; private set; }
    public int MaxMp => _content.MaxMp;
    public int WaveCount => _content.Waves.Count;
    public int FloorCount => _floors.Length;
    public string CurrentCombatFloorId => _floors[_currentFloorIndex].Id;
    public int CurrentFloorDepth => _floors[_currentFloorIndex].Depth;
    public int FacilityCount => _floors.Sum(x => x.Board.Facilities.Count);
    public IReadOnlySet<string> TrapIds => _floors.SelectMany(x => x.Board.Traps.Select(t => DisplayPlacementId(x.Id, t.InstanceId))).ToHashSet(StringComparer.Ordinal);
    public IReadOnlySet<string> GuardIds => _floors.SelectMany(x => x.Board.Guards.Select(g => RuntimeGuardId(x.Id, g.InstanceId))).ToHashSet(StringComparer.Ordinal);
    public IReadOnlySet<string> FacilityIds => _floors.SelectMany(x => x.Board.Facilities.Select(f => DisplayPlacementId(x.Id, f.InstanceId))).ToHashSet(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, SpellDefinition> Spells => _content.Spells;
    public DefenseOutcome Outcome { get; private set; } = DefenseOutcome.Running;
    public List<DefenseEvent> Events { get; } = [];
    public IReadOnlyList<GridPoint> Route => CurrentFloor.Route;
    public IReadOnlyDictionary<string, IReadOnlyList<GridPoint>> Routes => _floors.ToDictionary(x => x.Id, x => (IReadOnlyList<GridPoint>)x.Route, StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> FloorDepths => _floors.ToDictionary(x => x.Id, x => x.Depth, StringComparer.Ordinal);

    public IReadOnlyList<UnitSnapshot> Units => _units
        .OrderBy(x => x.EntityId, StringComparer.Ordinal)
        .Select(x => new UnitSnapshot(x.EntityId, x.Definition.Id, x.Definition.Team, x.Position, x.Hp, x.MaxHp, x.PathIndex, x.Alive, x.TargetEntityId, x.FloorId, x.AwaitingFloorTransition))
        .ToArray();

    private FloorRuntime CurrentFloor => _floors[_currentFloorIndex];

    public int SpellCooldownRemaining(string spellId) => _spellReadyTick.TryGetValue(spellId, out var readyTick) ? Math.Max(0, readyTick - Tick) : 0;

    public int StatusRemainingTicks(string entityId, StatusKind kind)
    {
        var unit = _units.SingleOrDefault(x => x.EntityId == entityId);
        return unit is not null && unit.Statuses.TryGetValue(kind, out var status) ? status.RemainingTicks : 0;
    }

    public int TrapCooldownRemaining(string trapInstanceId, string? floorId = null)
    {
        var resolvedFloor = floorId ?? CurrentCombatFloorId;
        return _trapReadyTick.TryGetValue(PlacementKey(resolvedFloor, trapInstanceId), out var readyTick) ? Math.Max(0, readyTick - Tick) : 0;
    }

    public SpellCastResult QueueSpell(string spellId, GridPoint target, string? targetEntityId = null, string? floorId = null)
    {
        if (Outcome != DefenseOutcome.Running) return new(false, "Defense is not running.");
        if (!_content.Spells.TryGetValue(spellId, out var spell)) return new(false, "Unknown spell.");
        if (Mp < spell.MpCost) return new(false, "Not enough MP.");
        if (_spellReadyTick[spellId] > Tick) return new(false, "Spell is on cooldown.");
        if (spell.Kind == SpellKind.Push && string.IsNullOrWhiteSpace(targetEntityId)) return new(false, "Push requires a target entity.");
        var resolvedFloor = floorId ?? CurrentCombatFloorId;
        if (!string.Equals(resolvedFloor, CurrentCombatFloorId, StringComparison.Ordinal)) return new(false, "Spells can target only the current combat floor.");
        _queuedSpells.Add(new QueuedSpell(spellId, target, targetEntityId, resolvedFloor));
        return new(true, null);
    }

    public void Step()
    {
        if (Outcome != DefenseOutcome.Running) return;
        _enteredCells.Clear();
        ProcessCommands();
        SpawnPhase();
        MovementPhase();
        TrapPhase();
        TargetPhase();
        AttackPhase();
        StatusAndTimePhase();
        DeathCleanupPhase();
        EndJudgmentPhase();
        Tick++;
    }

    public DefenseOutcome RunToEnd(int maxTicks = 50_000)
    {
        while (Outcome == DefenseOutcome.Running && Tick < maxTicks) Step();
        if (Outcome == DefenseOutcome.Running) throw new InvalidOperationException($"Defense exceeded {maxTicks} ticks.");
        return Outcome;
    }

    public string ResultDigest()
    {
        var payload = new StringBuilder();
        payload.Append(Seed).Append('|').Append(Outcome).Append('|').Append(CoreHp).Append('|').Append(Mp).AppendLine();
        foreach (var e in Events)
        {
            payload.Append(e.Tick).Append('|').Append(e.Type).Append('|').Append(e.ActorId).Append('|')
                .Append(e.TargetId).Append('|').Append(e.Position).Append('|').Append(e.Amount).Append('|').Append(e.Detail).Append('|').Append(e.FloorId).AppendLine();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()))).ToLowerInvariant();
    }

    private void ProcessCommands()
    {
        foreach (var command in _queuedSpells)
        {
            var spell = _content.Spells[command.SpellId];
            if (Mp < spell.MpCost || _spellReadyTick[spell.Id] > Tick) continue;
            var floor = _floorById[command.FloorId];
            Mp -= spell.MpCost;
            _spellReadyTick[spell.Id] = Tick + spell.CooldownTicks;
            Events.Add(new DefenseEvent(Tick, DefenseEventType.SpellCast, spell.Id, command.TargetEntityId, command.Target, FloorId: floor.Id));

            if (spell.Kind == SpellKind.Freeze)
            {
                var room = floor.Board.RoomAt(command.Target);
                var terrainBonus = floor.Board.HasTerrain(command.Target, TerrainFeatureKind.ManaVein) ? 25 : 0;
                var duration = ApplyPercentBonus(spell.DurationTicks, (room?.SpellDurationBonusPercent ?? 0) + terrainBonus);
                foreach (var target in AliveInvaders(floor.Id).Where(x => x.Position.ManhattanDistance(command.Target) <= spell.Radius))
                    ApplyStatus(target, new StatusEffect(StatusKind.Freeze, 1, duration), spell.Id);
            }
            else if (spell.Kind == SpellKind.Push && command.TargetEntityId is { } id)
            {
                var target = AliveInvaders(floor.Id).SingleOrDefault(x => x.EntityId == id);
                if (target is not null)
                {
                    var room = floor.Board.RoomAt(target.Position);
                    var terrainBonus = floor.Board.HasTerrain(target.Position, TerrainFeatureKind.ManaVein) ? 1 : 0;
                    var magnitude = Math.Max(1, spell.Magnitude + (room?.PushMagnitudeBonus ?? 0) + terrainBonus);
                    var nextIndex = Math.Max(0, target.PathIndex - magnitude);
                    if (nextIndex != target.PathIndex)
                    {
                        target.PathIndex = nextIndex;
                        target.Position = floor.Route[nextIndex];
                        target.AwaitingFloorTransition = false;
                        target.CoreReachedLogged = false;
                        RecordEntry(target.EntityId, target.Position);
                        Events.Add(new DefenseEvent(Tick, DefenseEventType.Move, spell.Id, target.EntityId, target.Position, Detail: "push", FloorId: floor.Id));
                    }
                }
            }
        }
        _queuedSpells.Clear();
    }

    private void SpawnPhase()
    {
        if (WaveIndex >= _content.Waves.Count || Tick < _waveStartTick || _currentFloorIndex != 0) return;
        var wave = _content.Waves[WaveIndex];
        if (Tick == _waveStartTick) Events.Add(new DefenseEvent(Tick, DefenseEventType.WaveStart, wave.Id, FloorId: CurrentFloor.Id));
        var elapsed = Tick - _waveStartTick;

        for (var i = 0; i < wave.SpawnGroups.Count; i++)
        {
            var group = wave.SpawnGroups[i];
            while (_spawnCounts[i] < group.Count)
            {
                var due = group.InitialDelayTicks + (_spawnCounts[i] * group.SpawnIntervalTicks);
                if (elapsed < due) break;
                var definition = _content.Units[group.UnitId];
                var entity = new RuntimeUnit
                {
                    EntityId = $"E{_nextEntityId++:D4}",
                    SourceInstanceId = null,
                    Definition = definition,
                    FloorId = CurrentFloor.Id,
                    Position = CurrentFloor.Board.Entrance,
                    HomePosition = CurrentFloor.Board.Entrance,
                    Hp = definition.MaxHp,
                    MaxHp = definition.MaxHp,
                    PathIndex = 0,
                    NextMoveTick = Tick + Math.Max(1, definition.MoveIntervalTicks),
                    NextAttackTick = Tick,
                };
                _units.Add(entity);
                _spawnCounts[i]++;
                Events.Add(new DefenseEvent(Tick, DefenseEventType.Spawn, entity.EntityId, Position: entity.Position, Detail: entity.Definition.Id, FloorId: entity.FloorId));
                Events.Add(new DefenseEvent(Tick, DefenseEventType.FloorEntered, entity.EntityId, Position: entity.Position, Detail: "spawn", FloorId: entity.FloorId));
                if (group.SpawnIntervalTicks > 0) break;
            }
        }
    }

    private void MovementPhase()
    {
        foreach (var invader in AliveInvaders(CurrentFloor.Id).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (invader.AwaitingFloorTransition || HasStatus(invader, StatusKind.Freeze) || Tick < invader.NextMoveTick || invader.PathIndex >= CurrentFloor.Route.Length - 1) continue;
            if (TryHandleBossRouteBreak(invader, CurrentFloor)) continue;
            if (IsEngaged(invader)) continue;
            invader.PathIndex++;
            invader.Position = CurrentFloor.Route[invader.PathIndex];
            RecordEntry(invader.EntityId, invader.Position);
            invader.NextMoveTick = Tick + EffectiveMoveInterval(invader);
            Events.Add(new DefenseEvent(Tick, DefenseEventType.Move, invader.EntityId, Position: invader.Position, Amount: invader.PathIndex, FloorId: invader.FloorId));

            if (invader.PathIndex >= CurrentFloor.Route.Length - 1)
            {
                if (CurrentFloor.EndpointKind == FloorEndpointKind.DescentGate)
                {
                    invader.AwaitingFloorTransition = true;
                    Events.Add(new DefenseEvent(Tick, DefenseEventType.FloorRegroupWait, invader.EntityId, Position: invader.Position, FloorId: invader.FloorId));
                }
                else if (!invader.CoreReachedLogged)
                {
                    invader.CoreReachedLogged = true;
                    Events.Add(new DefenseEvent(Tick, DefenseEventType.CoreReached, invader.EntityId, "CORE", invader.Position, FloorId: invader.FloorId));
                }
            }
        }

        foreach (var guard in AliveGuards(CurrentFloor.Id).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (HasStatus(guard, StatusKind.Freeze) || Tick < guard.NextMoveTick || guard.TargetEntityId is null) continue;
            var target = AliveInvaders(CurrentFloor.Id).SingleOrDefault(x => x.EntityId == guard.TargetEntityId);
            if (target is null) continue;
            var zone = GuardZone.Resolve(CurrentFloor.Board, _guardPlacements[guard.EntityId]);
            if (!zone.Contains(target.Position) || guard.Position.ManhattanDistance(target.Position) <= guard.Definition.AttackRange) continue;
            var neighbors = GridGeometry.NeighborsNorthEastSouthWest(guard.Position).ToArray();
            var next = neighbors
                .Where(p => zone.Contains(p) && CurrentFloor.Board.IsWalkable(p))
                .OrderBy(p => p.ManhattanDistance(target.Position))
                .ThenBy(p => Array.IndexOf(neighbors, p))
                .FirstOrDefault(guard.Position);
            if (next != guard.Position)
            {
                guard.Position = next;
                guard.NextMoveTick = Tick + EffectiveMoveInterval(guard);
                Events.Add(new DefenseEvent(Tick, DefenseEventType.Move, guard.EntityId, guard.TargetEntityId, guard.Position, Detail: "guard", FloorId: guard.FloorId));
            }
        }
    }

    private void TrapPhase()
    {
        foreach (var (entityId, cells) in _enteredCells.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var unit = AliveInvaders(CurrentFloor.Id).SingleOrDefault(x => x.EntityId == entityId);
            if (unit is null) continue;
            foreach (var cell in cells)
            {
                foreach (var placed in CurrentFloor.Board.Traps.Where(x => x.Position == cell).OrderBy(x => x.InstanceId, StringComparer.Ordinal))
                {
                    var key = PlacementKey(CurrentFloor.Id, placed.InstanceId);
                    if (_trapReadyTick[key] > Tick) continue;
                    var definition = _content.Traps[placed.DefinitionId];
                    unit.Hp -= definition.Damage;
                    _trapReadyTick[key] = Tick + definition.CooldownTicks;
                    Events.Add(new DefenseEvent(Tick, DefenseEventType.TrapTriggered, DisplayPlacementId(CurrentFloor.Id, placed.InstanceId), unit.EntityId, cell, definition.Damage, definition.Id, CurrentFloor.Id));
                    if (definition.StatusKind is { } kind && unit.Alive)
                    {
                        var room = CurrentFloor.Board.RoomAt(cell);
                        var duration = kind == StatusKind.Poison
                            ? ApplyPercentBonus(definition.StatusDurationTicks, room?.PoisonDurationBonusPercent ?? 0)
                            : definition.StatusDurationTicks;
                        ApplyStatus(unit, new StatusEffect(kind, definition.StatusStrength, duration), DisplayPlacementId(CurrentFloor.Id, placed.InstanceId));
                    }
                }
            }
        }
    }

    private void TargetPhase()
    {
        foreach (var guard in AliveGuards(CurrentFloor.Id).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            var zone = GuardZone.Resolve(CurrentFloor.Board, _guardPlacements[guard.EntityId]);
            guard.TargetEntityId = AliveInvaders(CurrentFloor.Id)
                .Where(x => !x.AwaitingFloorTransition && zone.Contains(x.Position))
                .OrderByDescending(x => x.PathIndex)
                .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                .Select(x => x.EntityId)
                .FirstOrDefault();
        }

        foreach (var invader in AliveInvaders(CurrentFloor.Id).Where(x => x.Definition.Role != UnitRole.Priest).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            invader.TargetEntityId = AliveGuards(CurrentFloor.Id)
                .Where(g => g.Position.ManhattanDistance(invader.Position) <= invader.Definition.AttackRange)
                .Where(g => invader.Definition.AttackRange <= 1 || DungeonLineOfSight.HasLineOfSight(CurrentFloor.Board, invader.Position, g.Position))
                .OrderBy(g => g.Position.ManhattanDistance(invader.Position))
                .ThenBy(g => g.EntityId, StringComparer.Ordinal)
                .Select(g => g.EntityId)
                .FirstOrDefault();
        }

        foreach (var placed in CurrentFloor.Board.Facilities.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
        {
            var definition = _content.Facilities[placed.DefinitionId];
            var key = PlacementKey(CurrentFloor.Id, placed.InstanceId);
            _facilityTargets[key] = AliveInvaders(CurrentFloor.Id)
                .Where(x => !x.AwaitingFloorTransition)
                .Where(x => x.Position.ManhattanDistance(placed.Position) <= definition.Range)
                .Where(x => DungeonLineOfSight.HasLineOfSight(CurrentFloor.Board, placed.Position, x.Position))
                .OrderByDescending(x => x.PathIndex)
                .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                .Select(x => x.EntityId)
                .FirstOrDefault();
        }
    }

    private void AttackPhase()
    {
        foreach (var guard in AliveGuards(CurrentFloor.Id).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (HasStatus(guard, StatusKind.Freeze) || Tick < guard.NextAttackTick || guard.TargetEntityId is null) continue;
            var target = AliveInvaders(CurrentFloor.Id).SingleOrDefault(x => x.EntityId == guard.TargetEntityId);
            if (target is null || guard.Position.ManhattanDistance(target.Position) > guard.Definition.AttackRange) continue;
            if (guard.Definition.AttackRange > 1 && !DungeonLineOfSight.HasLineOfSight(CurrentFloor.Board, guard.Position, target.Position)) continue;
            DealDamage(guard, target, ApplyExecutionBonus(EffectiveGuardDamage(guard), target));
            guard.NextAttackTick = Tick + Math.Max(1, guard.Definition.AttackCooldownTicks);
        }

        foreach (var invader in AliveInvaders(CurrentFloor.Id).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (HasStatus(invader, StatusKind.Freeze) || Tick < invader.NextAttackTick) continue;
            if (invader.Definition.Role == UnitRole.Priest && invader.Definition.HealPower > 0)
            {
                var ally = AliveInvaders(CurrentFloor.Id)
                    .Where(x => x.EntityId != invader.EntityId && x.Hp < x.MaxHp && x.Position.ManhattanDistance(invader.Position) <= invader.Definition.AttackRange)
                    .OrderBy(x => (double)x.Hp / x.MaxHp)
                    .ThenBy(x => x.EntityId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (ally is not null)
                {
                    var before = ally.Hp;
                    ally.Hp = Math.Min(ally.MaxHp, ally.Hp + invader.Definition.HealPower);
                    Events.Add(new DefenseEvent(Tick, DefenseEventType.Heal, invader.EntityId, ally.EntityId, ally.Position, ally.Hp - before, FloorId: invader.FloorId));
                    invader.NextAttackTick = Tick + Math.Max(1, invader.Definition.AttackCooldownTicks);
                    continue;
                }
            }

            var guard = invader.TargetEntityId is null ? null : AliveGuards(CurrentFloor.Id).SingleOrDefault(x => x.EntityId == invader.TargetEntityId);
            if (guard is not null && invader.Position.ManhattanDistance(guard.Position) <= invader.Definition.AttackRange)
            {
                DealDamage(invader, guard, invader.Definition.Damage);
                invader.NextAttackTick = Tick + Math.Max(1, invader.Definition.AttackCooldownTicks);
            }
            else if (CurrentFloor.EndpointKind == FloorEndpointKind.DungeonCore && invader.PathIndex >= CurrentFloor.Route.Length - 1)
            {
                CoreHp -= invader.Definition.Damage;
                invader.NextAttackTick = Tick + Math.Max(1, invader.Definition.AttackCooldownTicks);
                Events.Add(new DefenseEvent(Tick, DefenseEventType.CoreDamaged, invader.EntityId, "CORE", CurrentFloor.Board.Core, invader.Definition.Damage, FloorId: invader.FloorId));
            }
        }

        foreach (var placed in CurrentFloor.Board.Facilities.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
        {
            var key = PlacementKey(CurrentFloor.Id, placed.InstanceId);
            if (_facilityReadyTick[key] > Tick || _facilityTargets[key] is not { } targetId) continue;
            var target = AliveInvaders(CurrentFloor.Id).SingleOrDefault(x => x.EntityId == targetId);
            if (target is null) continue;
            var definition = _content.Facilities[placed.DefinitionId];
            var damage = ApplyExecutionBonus(definition.Damage, target);
            target.Hp -= damage;
            _facilityReadyTick[key] = Tick + definition.CooldownTicks;
            Events.Add(new DefenseEvent(Tick, DefenseEventType.Attack, DisplayPlacementId(CurrentFloor.Id, placed.InstanceId), target.EntityId, target.Position, damage, $"progress={target.PathIndex}", CurrentFloor.Id));
            if (definition.StatusKind is { } kind && target.Alive)
                ApplyStatus(target, new StatusEffect(kind, definition.StatusStrength, definition.StatusDurationTicks), DisplayPlacementId(CurrentFloor.Id, placed.InstanceId));
        }
    }

    private void StatusAndTimePhase()
    {
        foreach (var unit in _units.Where(x => x.Alive).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            if (unit.Statuses.TryGetValue(StatusKind.Poison, out var poison) && poison.RemainingTicks > 0 && Tick % TicksPerSecond == 0)
            {
                unit.Hp -= poison.Strength;
                Events.Add(new DefenseEvent(Tick, DefenseEventType.Attack, "status.poison", unit.EntityId, unit.Position, poison.Strength, FloorId: unit.FloorId));
            }
            foreach (var kind in unit.Statuses.Keys.ToArray())
            {
                var status = unit.Statuses[kind];
                var remaining = status.RemainingTicks - 1;
                if (remaining <= 0) unit.Statuses.Remove(kind);
                else unit.Statuses[kind] = status with { RemainingTicks = remaining };
            }
        }
        Mp = Math.Min(_content.MaxMp, Mp + _content.MpChargePerTick);
    }

    private void DeathCleanupPhase()
    {
        foreach (var unit in _units.Where(x => !x.Alive && !x.DeathLogged).OrderBy(x => x.EntityId, StringComparer.Ordinal))
        {
            unit.DeathLogged = true;
            Events.Add(new DefenseEvent(Tick, DefenseEventType.Death, unit.EntityId, Position: unit.Position, Detail: unit.Definition.Id, FloorId: unit.FloorId));
        }
    }

    private void EndJudgmentPhase()
    {
        if (CoreHp <= 0)
        {
            CoreHp = 0;
            Outcome = DefenseOutcome.Failure;
            Events.Add(new DefenseEvent(Tick, DefenseEventType.DefenseFailure, "CORE", Position: _floors[^1].Board.Core, FloorId: _floors[^1].Id));
            return;
        }

        if (TryTransitionFloor()) return;
        if (Tick < _waveStartTick) return;

        var wave = _content.Waves[WaveIndex];
        if (!AllSpawned(wave) || AliveInvaders().Any()) return;

        Events.Add(new DefenseEvent(Tick, DefenseEventType.WaveEnd, wave.Id, FloorId: CurrentFloor.Id));
        if (WaveIndex == _content.Waves.Count - 1)
        {
            Outcome = DefenseOutcome.Success;
            Events.Add(new DefenseEvent(Tick, DefenseEventType.DefenseSuccess, "CORE", Position: _floors[^1].Board.Core, Amount: CoreHp, FloorId: _floors[^1].Id));
            return;
        }

        var wait = Math.Max(0, wave.InterWaveTicks);
        WaveIndex++;
        _waveStartTick = Tick + wait + 1;
        _spawnCounts = new int[_content.Waves[WaveIndex].SpawnGroups.Count];
        _currentFloorIndex = 0;
    }

    private bool TryHandleBossRouteBreak(RuntimeUnit invader, FloorRuntime floor)
    {
        if (!_content.BossRouteBreaks.TryGetValue(invader.Definition.Id, out var routeBreak)) return false;
        if (invader.BossRouteBreakUses >= routeBreak.MaxUsesPerFloor) return false;
        if (routeBreak.Kind != BossRouteBreakKind.ShortWarp) throw new InvalidOperationException($"Unsupported boss route-break kind: {routeBreak.Kind}.");

        if (invader.BossRouteBreakReadyTick is { } readyTick)
        {
            if (Tick < readyTick) return true;
            var landingIndex = invader.BossRouteBreakLandingIndex ?? invader.PathIndex;
            invader.BossRouteBreakReadyTick = null;
            invader.BossRouteBreakLandingIndex = null;
            invader.BossRouteBreakUses++;
            if (landingIndex <= invader.PathIndex) return false;
            invader.PathIndex = landingIndex;
            invader.Position = floor.Route[landingIndex];
            invader.TargetEntityId = null;
            invader.AwaitingFloorTransition = false;
            invader.CoreReachedLogged = false;
            RecordEntry(invader.EntityId, invader.Position);
            invader.NextMoveTick = Tick + EffectiveMoveInterval(invader);
            Events.Add(new DefenseEvent(
                Tick,
                DefenseEventType.BossRouteBreakActivated,
                invader.EntityId,
                Position: invader.Position,
                Amount: landingIndex,
                Detail: $"kind={routeBreak.Kind};use={invader.BossRouteBreakUses}/{routeBreak.MaxUsesPerFloor}",
                FloorId: floor.Id));
            return true;
        }

        var triggerIndex = Math.Max(1, (int)Math.Ceiling((floor.Route.Length - 1) * routeBreak.TriggerPathPercent / 100.0));
        if (invader.PathIndex < triggerIndex) return false;
        var maxLandingIndex = Math.Max(invader.PathIndex, floor.Route.Length - 2);
        var targetIndex = Math.Min(invader.PathIndex + routeBreak.SkipRouteCells, maxLandingIndex);
        if (targetIndex <= invader.PathIndex)
        {
            invader.BossRouteBreakUses = routeBreak.MaxUsesPerFloor;
            return false;
        }
        invader.BossRouteBreakReadyTick = Tick + routeBreak.TelegraphTicks;
        invader.BossRouteBreakLandingIndex = targetIndex;
        Events.Add(new DefenseEvent(
            Tick,
            DefenseEventType.BossRouteBreakTelegraph,
            invader.EntityId,
            Position: floor.Route[targetIndex],
            Amount: targetIndex,
            Detail: $"kind={routeBreak.Kind};ready={invader.BossRouteBreakReadyTick};skip={targetIndex - invader.PathIndex}",
            FloorId: floor.Id));
        return true;
    }

    private bool TryTransitionFloor()
    {
        if (CurrentFloor.EndpointKind != FloorEndpointKind.DescentGate || _currentFloorIndex >= _floors.Length - 1) return false;
        var wave = _content.Waves[WaveIndex];
        if (_currentFloorIndex == 0 && !AllSpawned(wave)) return false;

        var alive = AliveInvaders(CurrentFloor.Id).OrderBy(x => x.EntityId, StringComparer.Ordinal).ToArray();
        if (alive.Length == 0 || alive.Any(x => !x.AwaitingFloorTransition)) return false;

        var from = CurrentFloor;
        var next = _floors[_currentFloorIndex + 1];
        Events.Add(new DefenseEvent(Tick, DefenseEventType.FloorBreached, wave.Id, Detail: next.Id, FloorId: from.Id));

        foreach (var unit in alive)
        {
            unit.FloorId = next.Id;
            unit.Position = next.Board.Entrance;
            unit.PathIndex = 0;
            unit.TargetEntityId = null;
            unit.AwaitingFloorTransition = false;
            unit.CoreReachedLogged = false;
            unit.BossRouteBreakUses = 0;
            unit.BossRouteBreakReadyTick = null;
            unit.BossRouteBreakLandingIndex = null;
            Events.Add(new DefenseEvent(Tick, DefenseEventType.FloorTransitioned, unit.EntityId, Position: unit.Position, Detail: $"{from.Id}->{next.Id}", FloorId: from.Id));
            Events.Add(new DefenseEvent(Tick, DefenseEventType.FloorEntered, unit.EntityId, Position: unit.Position, Detail: from.Id, FloorId: next.Id));
        }

        _currentFloorIndex++;
        return true;
    }

    private bool AllSpawned(WaveDefinition wave)
        => wave.SpawnGroups.Select((group, index) => _spawnCounts[index] >= group.Count).All(x => x);

    private bool IsEngaged(RuntimeUnit invader) => AliveGuards(invader.FloorId)
        .Any(guard => guard.Definition.Blocks
            && guard.Position.ManhattanDistance(invader.Position) <= 1
            && GuardZone.Resolve(_floorById[guard.FloorId].Board, _guardPlacements[guard.EntityId]).Contains(invader.Position));

    private int EffectiveGuardDamage(RuntimeUnit guard)
    {
        var floor = _floorById[guard.FloorId];
        var room = floor.Board.RoomAt(guard.Position);
        return ApplyPercentBonus(guard.Definition.Damage, room?.GuardDamageBonusPercent ?? 0);
    }

    private int ApplyExecutionBonus(int damage, RuntimeUnit target)
    {
        var floor = _floorById[target.FloorId];
        var room = floor.Board.RoomAt(target.Position);
        if (room is null || room.ExecuteThresholdPercent <= 0 || room.ExecuteDamageBonusPercent <= 0) return damage;
        var thresholdHp = Math.Max(1, (target.MaxHp * room.ExecuteThresholdPercent) / 100);
        return target.Hp <= thresholdHp ? ApplyPercentBonus(damage, room.ExecuteDamageBonusPercent) : damage;
    }

    private static void ValidateBossRouteBreakContent(DefenseContent content)
    {
        foreach (var (unitId, routeBreak) in content.BossRouteBreaks)
        {
            _ = routeBreak.Validate();
            if (!string.Equals(unitId, routeBreak.UnitId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Boss route-break dictionary key mismatch: {unitId}/{routeBreak.UnitId}.");
            if (!content.Units.TryGetValue(unitId, out var unit) || unit.Team != Team.Invader)
                throw new InvalidOperationException($"Boss route-break must reference an Invader unit: {unitId}.");
            var count = content.Waves.SelectMany(x => x.SpawnGroups).Where(x => string.Equals(x.UnitId, unitId, StringComparison.Ordinal)).Sum(x => x.Count);
            if (count > 1) throw new InvalidOperationException($"Boss route-break unit may spawn at most once across the encounter: {unitId} count={count}.");
        }
    }

    private static int ApplyPercentBonus(int baseValue, int bonusPercent)
        => Math.Max(1, (baseValue * (100 + Math.Max(0, bonusPercent)) + 99) / 100);

    private static int EffectiveMoveInterval(RuntimeUnit unit)
    {
        var multiplier = unit.Statuses.TryGetValue(StatusKind.Slow, out var slow) ? Math.Max(1, slow.Strength) : 1;
        return Math.Max(1, unit.Definition.MoveIntervalTicks * multiplier);
    }

    private static bool HasStatus(RuntimeUnit unit, StatusKind kind) => unit.Statuses.TryGetValue(kind, out var status) && status.RemainingTicks > 0;

    private void ApplyStatus(RuntimeUnit target, StatusEffect status, string source)
    {
        target.Statuses.TryGetValue(status.Kind, out var existing);
        target.Statuses[status.Kind] = StatusRules.Merge(existing, status);
        Events.Add(new DefenseEvent(Tick, DefenseEventType.StatusApplied, source, target.EntityId, target.Position, status.Strength, $"{status.Kind}:{status.DurationTicksOrRemaining()}", target.FloorId));
    }

    private void DealDamage(RuntimeUnit attacker, RuntimeUnit target, int damage)
    {
        if (damage <= 0) return;
        target.Hp -= damage;
        Events.Add(new DefenseEvent(Tick, DefenseEventType.Attack, attacker.EntityId, target.EntityId, target.Position, damage, $"progress={target.PathIndex}", target.FloorId));
    }

    private void RecordEntry(string entityId, GridPoint point)
    {
        if (!_enteredCells.TryGetValue(entityId, out var cells))
        {
            cells = [];
            _enteredCells[entityId] = cells;
        }
        cells.Add(point);
    }

    private string RuntimeGuardId(string floorId, string instanceId) => _floors.Length == 1 ? instanceId : PlacementKey(floorId, instanceId);
    private string DisplayPlacementId(string floorId, string instanceId) => _floors.Length == 1 ? instanceId : PlacementKey(floorId, instanceId);
    private static string PlacementKey(string floorId, string instanceId) => $"{floorId}:{instanceId}";

    private IEnumerable<RuntimeUnit> AliveInvaders() => _units.Where(x => x.Alive && x.Definition.Team == Team.Invader);
    private IEnumerable<RuntimeUnit> AliveInvaders(string floorId) => AliveInvaders().Where(x => string.Equals(x.FloorId, floorId, StringComparison.Ordinal));
    private IEnumerable<RuntimeUnit> AliveGuards(string floorId) => _units.Where(x => x.Alive && x.Definition.Team == Team.Dungeon && string.Equals(x.FloorId, floorId, StringComparison.Ordinal));
}

internal static class StatusEffectExtensions
{
    public static int DurationTicksOrRemaining(this StatusEffect effect) => effect.RemainingTicks;
}
