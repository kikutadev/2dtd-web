using System.Collections.Immutable;
using DungeonDefense.Core;

namespace DungeonDefense.Presentation;

/// <summary>
/// Engine-independent visual timeline for defense combat. Core snapshots/events remain the gameplay authority;
/// this class owns only render lifetime, interpolation, and semantic motion cues, and exposes an immutable Visual State.
/// </summary>
public sealed class CombatMotionPresentation
{
    private readonly Dictionary<string, CombatUnitTimelineState> _units = new(StringComparer.Ordinal);
    private readonly List<CombatProjectileTimelineState> _projectiles = [];
    private string? _currentFloorId;
    private double _battleSpeed = 1.0;
    private CombatVisualState _visualState = CombatVisualState.Empty;
    private bool _visualStateDirty = true;

    public CombatVisualState VisualState
    {
        get
        {
            if (_visualStateDirty)
            {
                _visualState = BuildVisualState();
                _visualStateDirty = false;
            }
            return _visualState;
        }
    }

    public void SetBattleSpeed(double battleSpeed) => _battleSpeed = Math.Clamp(battleSpeed, 1.0, 10.0);

    public void SyncSnapshot(IEnumerable<UnitSnapshot> snapshots, string currentFloorId)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFloorId);

        if (!string.Equals(_currentFloorId, currentFloorId, StringComparison.Ordinal))
        {
            _currentFloorId = currentFloorId;
            foreach (var id in _units.Where(x => !string.Equals(x.Value.FloorId, currentFloorId, StringComparison.Ordinal)).Select(x => x.Key).ToArray())
                _units.Remove(id);
            _projectiles.RemoveAll(x => !_units.ContainsKey(x.ActorId) && !_units.ContainsKey(x.TargetId));
        }

        var visible = snapshots.Where(x => x.Alive && string.Equals(x.FloorId, currentFloorId, StringComparison.Ordinal)).ToArray();
        var byId = visible.ToDictionary(x => x.EntityId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var snapshot in visible)
        {
            seen.Add(snapshot.EntityId);
            if (!_units.TryGetValue(snapshot.EntityId, out var state))
            {
                state = new CombatUnitTimelineState(snapshot);
                _units.Add(snapshot.EntityId, state);
            }

            var previousLogical = state.LogicalPosition;
            state.DefinitionId = snapshot.DefinitionId;
            state.Team = snapshot.Team;
            state.FloorId = snapshot.FloorId;
            state.LogicalPosition = snapshot.Position;
            state.Hp = snapshot.Hp;
            state.MaxHp = snapshot.MaxHp;
            state.MissingSnapshotSyncs = 0;

            if (previousLogical != snapshot.Position)
                state.Facing = ResolveFacing(previousLogical, snapshot.Position, state.Facing);
        }

        foreach (var snapshot in visible)
        {
            if (snapshot.TargetEntityId is not { } targetId || !byId.TryGetValue(targetId, out var target)) continue;
            var state = _units[snapshot.EntityId];
            state.Facing = ResolveFacing(snapshot.Position, target.Position, state.Facing);
        }

        // The host can sync the post-tick snapshot before it forwards the Death event. Keep a missing visual
        // entity for one sync so the subsequent event can move it into Dying instead of removing it instantly.
        foreach (var state in _units.Values.Where(x => !seen.Contains(x.EntityId)).ToArray())
        {
            if (state.Lifecycle == CombatVisualLifecycle.Dying) continue;
            state.MissingSnapshotSyncs++;
            if (state.MissingSnapshotSyncs > 1) _units.Remove(state.EntityId);
        }
        _visualStateDirty = true;
    }

    public void ConsumeEvent(DefenseEvent combatEvent)
    {
        ArgumentNullException.ThrowIfNull(combatEvent);
        if (_currentFloorId is not null && !string.Equals(combatEvent.FloorId, _currentFloorId, StringComparison.Ordinal)) return;

        _visualStateDirty = true;
        switch (combatEvent.Type)
        {
            case DefenseEventType.Spawn:
                if (_units.TryGetValue(combatEvent.ActorId, out var spawned))
                {
                    spawned.SpawnDurationSeconds = ScaleDuration(0.18, 0.10);
                    spawned.SpawnElapsedSeconds = 0;
                }
                break;

            case DefenseEventType.Move:
                ConsumeMove(combatEvent);
                break;

            case DefenseEventType.Attack:
                ConsumeAttack(combatEvent);
                break;

            case DefenseEventType.TrapTriggered:
                BeginHit(combatEvent.TargetId, source: null, delaySeconds: 0);
                break;

            case DefenseEventType.Death:
                BeginDeath(combatEvent);
                break;
        }
    }

    public void Advance(double deltaSeconds, double battleSpeed)
    {
        SetBattleSpeed(battleSpeed);
        var delta = Math.Max(0.0, deltaSeconds);
        if (delta > 0) _visualStateDirty = true;
        var remove = new List<string>();

        foreach (var state in _units.Values)
        {
            if (state.IsMoving)
            {
                state.MoveElapsedSeconds += delta;
                var eased = EaseInOut(state.MoveProgress);
                state.RenderPosition = PresentationPoint.Lerp(state.MoveFrom, state.MoveTo, eased);
                if (!state.IsMoving)
                {
                    state.RenderPosition = state.MoveTo;
                    state.MoveKind = CombatMoveKind.None;
                }
            }

            if (state.AttackElapsedSeconds < state.AttackDurationSeconds) state.AttackElapsedSeconds += delta;
            if (state.HitElapsedSeconds < state.HitDurationSeconds) state.HitElapsedSeconds += delta;
            if (state.SpawnElapsedSeconds < state.SpawnDurationSeconds) state.SpawnElapsedSeconds += delta;

            if (state.Lifecycle == CombatVisualLifecycle.Dying)
            {
                state.DeathElapsedSeconds += delta;
                if (state.DeathElapsedSeconds >= state.DeathDurationSeconds) remove.Add(state.EntityId);
            }
        }

        foreach (var id in remove) _units.Remove(id);

        foreach (var projectile in _projectiles) projectile.ElapsedSeconds += delta;
        _projectiles.RemoveAll(x => x.ElapsedSeconds >= x.DurationSeconds);
    }

    public void Reset()
    {
        _units.Clear();
        _projectiles.Clear();
        _currentFloorId = null;
        _battleSpeed = 1.0;
        _visualState = CombatVisualState.Empty;
        _visualStateDirty = false;
    }

    public static UnitFacing ResolveFacing(GridPoint from, GridPoint to, UnitFacing fallback)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0) return fallback;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? UnitFacing.East : UnitFacing.West;
        return dy >= 0 ? UnitFacing.South : UnitFacing.North;
    }

    public static string FacingSuffix(UnitFacing facing) => facing switch
    {
        UnitFacing.North => "N",
        UnitFacing.East => "E",
        UnitFacing.South => "S",
        UnitFacing.West => "W",
        _ => "S",
    };

    private CombatVisualState BuildVisualState()
    {
        if (_units.Count == 0 && _projectiles.Count == 0) return CombatVisualState.Empty;

        var units = _units.Values
            .Select(x => x.ToVisualState())
            .OrderBy(x => x.EntityId, StringComparer.Ordinal)
            .ToImmutableArray();
        var projectiles = _projectiles.Select(x => x.ToVisualState()).ToImmutableArray();
        var active = _projectiles.Count > 0 || _units.Values.Any(x => x.HasActiveMotion);
        return new CombatVisualState(units, projectiles, active);
    }

    private void ConsumeMove(DefenseEvent combatEvent)
    {
        var entityId = string.Equals(combatEvent.Detail, "push", StringComparison.OrdinalIgnoreCase)
            ? combatEvent.TargetId
            : combatEvent.ActorId;
        if (entityId is null || combatEvent.Position is not { } position || !_units.TryGetValue(entityId, out var state)) return;
        if (state.Lifecycle != CombatVisualLifecycle.Active) return;

        var target = PresentationPoint.From(position);
        var pushed = string.Equals(combatEvent.Detail, "push", StringComparison.OrdinalIgnoreCase);
        var tickDelta = state.LastMoveEventTick is { } previousTick ? Math.Max(1, combatEvent.Tick - previousTick) : 2;
        state.LastMoveEventTick = combatEvent.Tick;
        state.MoveFrom = state.RenderPosition;
        state.MoveTo = target;
        state.MoveElapsedSeconds = 0;
        state.MoveDurationSeconds = pushed
            ? ScaleDuration(0.11, 0.065)
            : Math.Clamp((tickDelta * 0.05) / Math.Sqrt(_battleSpeed), ScaleDuration(0.075, 0.055), ScaleDuration(0.22, 0.09));
        state.MoveKind = pushed ? CombatMoveKind.Push : CombatMoveKind.Walk;
        state.Facing = ResolveFacing(state.LogicalPosition, position, state.Facing);
        state.LogicalPosition = position;
    }

    private void ConsumeAttack(DefenseEvent combatEvent)
    {
        _units.TryGetValue(combatEvent.ActorId, out var actor);
        CombatUnitTimelineState? target = null;
        if (combatEvent.TargetId is { } targetId) _units.TryGetValue(targetId, out target);

        double hitDelay = 0;
        if (actor is not null && target is not null && actor.Lifecycle == CombatVisualLifecycle.Active)
        {
            var direction = PresentationPoint.Direction(actor.RenderPosition, target.RenderPosition);
            var distance = Math.Abs(actor.LogicalPosition.X - target.LogicalPosition.X) + Math.Abs(actor.LogicalPosition.Y - target.LogicalPosition.Y);
            actor.AttackDirection = direction;
            actor.AttackRanged = distance > 1;
            actor.AttackDurationSeconds = ScaleDuration(actor.AttackRanged ? 0.20 : 0.22, actor.AttackRanged ? 0.085 : 0.095);
            actor.AttackElapsedSeconds = 0;
            actor.Facing = ResolveFacing(actor.LogicalPosition, target.LogicalPosition, actor.Facing);

            hitDelay = actor.AttackRanged
                ? ScaleDuration(0.15, 0.055)
                : ScaleDuration(0.065, 0.035);

            if (actor.AttackRanged)
            {
                _projectiles.RemoveAll(x => string.Equals(x.ActorId, actor.EntityId, StringComparison.Ordinal));
                _projectiles.Add(new CombatProjectileTimelineState(
                    actor.EntityId,
                    target.EntityId,
                    actor.RenderPosition,
                    target.RenderPosition,
                    ScaleDuration(0.18, 0.07)));
            }
        }

        BeginHit(combatEvent.TargetId, actor, hitDelay);
    }

    private void BeginHit(string? targetId, CombatUnitTimelineState? source, double delaySeconds)
    {
        if (targetId is null || !_units.TryGetValue(targetId, out var target) || target.Lifecycle != CombatVisualLifecycle.Active) return;
        target.HitRecoilDirection = source is null
            ? new PresentationPoint(0f, -1f)
            : PresentationPoint.Direction(source.RenderPosition, target.RenderPosition);
        target.HitDurationSeconds = ScaleDuration(0.11, 0.06);
        target.HitElapsedSeconds = -Math.Max(0, delaySeconds);
    }

    private void BeginDeath(DefenseEvent combatEvent)
    {
        if (!_units.TryGetValue(combatEvent.ActorId, out var dying)) return;
        dying.Lifecycle = CombatVisualLifecycle.Dying;
        dying.MoveKind = CombatMoveKind.None;
        dying.DeathDurationSeconds = ScaleDuration(0.34, 0.16);
        dying.DeathElapsedSeconds = -ScaleDuration(0.06, 0.035);
        if (combatEvent.Position is not { } deathPosition) return;

        dying.LogicalPosition = deathPosition;
        if (dying.RenderPosition.DistanceSquaredTo(PresentationPoint.From(deathPosition)) > 1f)
            dying.RenderPosition = PresentationPoint.From(deathPosition);
    }

    private double ScaleDuration(double oneXSeconds, double minimumSeconds)
        => Math.Max(minimumSeconds, oneXSeconds / Math.Sqrt(_battleSpeed));

    private static float EaseInOut(float value) => value * value * (3f - (2f * value));

    private sealed class CombatUnitTimelineState
    {
        public CombatUnitTimelineState(UnitSnapshot snapshot)
        {
            EntityId = snapshot.EntityId;
            DefinitionId = snapshot.DefinitionId;
            Team = snapshot.Team;
            FloorId = snapshot.FloorId;
            LogicalPosition = snapshot.Position;
            RenderPosition = PresentationPoint.From(snapshot.Position);
            Hp = snapshot.Hp;
            MaxHp = snapshot.MaxHp;
            SpawnElapsedSeconds = SpawnDurationSeconds;
        }

        public string EntityId { get; }
        public string DefinitionId { get; set; }
        public Team Team { get; set; }
        public string FloorId { get; set; }
        public GridPoint LogicalPosition { get; set; }
        public PresentationPoint RenderPosition { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public UnitFacing Facing { get; set; } = UnitFacing.South;
        public CombatVisualLifecycle Lifecycle { get; set; } = CombatVisualLifecycle.Active;
        public CombatMoveKind MoveKind { get; set; }
        public bool AttackRanged { get; set; }
        public int MissingSnapshotSyncs { get; set; }

        public PresentationPoint MoveFrom { get; set; }
        public PresentationPoint MoveTo { get; set; }
        public double MoveElapsedSeconds { get; set; }
        public double MoveDurationSeconds { get; set; }
        public int? LastMoveEventTick { get; set; }
        public PresentationPoint AttackDirection { get; set; }
        public double AttackElapsedSeconds { get; set; } = double.PositiveInfinity;
        public double AttackDurationSeconds { get; set; } = 0.22;
        public PresentationPoint HitRecoilDirection { get; set; }
        public double HitElapsedSeconds { get; set; } = double.PositiveInfinity;
        public double HitDurationSeconds { get; set; } = 0.10;
        public double SpawnElapsedSeconds { get; set; }
        public double SpawnDurationSeconds { get; set; } = 0.18;
        public double DeathElapsedSeconds { get; set; }
        public double DeathDurationSeconds { get; set; } = 0.34;

        public bool IsMoving => MoveKind != CombatMoveKind.None && MoveElapsedSeconds < MoveDurationSeconds;
        public bool IsAttacking => AttackElapsedSeconds >= 0 && AttackElapsedSeconds < AttackDurationSeconds;
        public bool IsHit => HitElapsedSeconds >= 0 && HitElapsedSeconds < HitDurationSeconds;
        public bool IsSpawning => SpawnElapsedSeconds >= 0 && SpawnElapsedSeconds < SpawnDurationSeconds;
        public bool HasActiveMotion => IsMoving || IsAttacking || IsHit || IsSpawning || Lifecycle == CombatVisualLifecycle.Dying;

        public float MoveProgress => Progress(MoveElapsedSeconds, MoveDurationSeconds);
        private float AttackProgress => Progress(AttackElapsedSeconds, AttackDurationSeconds);
        private float HitProgress => Progress(HitElapsedSeconds, HitDurationSeconds);
        private float SpawnProgress => Progress(SpawnElapsedSeconds, SpawnDurationSeconds);
        private float DeathProgress => Lifecycle == CombatVisualLifecycle.Dying ? Progress(DeathElapsedSeconds, DeathDurationSeconds) : 0f;

        public CombatUnitVisualState ToVisualState()
        {
            var offset = VisualOffset();
            var opacity = IsSpawning ? SmoothStep(SpawnProgress) : 1f;
            if (Lifecycle == CombatVisualLifecycle.Dying)
            {
                var fade = Math.Clamp((DeathProgress - 0.62f) / 0.38f, 0f, 1f);
                opacity *= 1f - fade;
            }

            var scaleX = Lifecycle == CombatVisualLifecycle.Dying ? 1f + (0.08f * MathF.Min(DeathProgress / 0.55f, 1f)) : 1f;
            var scaleY = Lifecycle == CombatVisualLifecycle.Dying ? 1f - (0.38f * MathF.Min(DeathProgress / 0.55f, 1f)) : 1f;
            var hitFlash = IsHit ? 1f - HitProgress : 0f;

            return new CombatUnitVisualState(
                EntityId,
                DefinitionId,
                Team,
                FloorId,
                LogicalPosition,
                RenderPosition.Add(offset),
                Hp,
                MaxHp,
                Facing,
                Lifecycle,
                MoveKind,
                IsMoving,
                IsAttacking,
                IsHit,
                IsSpawning,
                Lifecycle == CombatVisualLifecycle.Active,
                Math.Clamp(opacity, 0f, 1f),
                scaleX,
                scaleY,
                hitFlash);
        }

        private PresentationPoint VisualOffset()
        {
            var offset = new PresentationPoint(0f, 0f);
            if (IsMoving && MoveKind == CombatMoveKind.Walk)
                offset = offset.Add(new PresentationPoint(0f, -MathF.Sin(MoveProgress * MathF.PI) * 0.045f));

            if (IsAttacking)
            {
                var p = AttackProgress;
                var magnitude = p < 0.22f
                    ? -0.035f * (p / 0.22f)
                    : MathF.Sin(((p - 0.22f) / 0.78f) * MathF.PI) * (AttackRanged ? 0.035f : 0.14f);
                offset = offset.Add(AttackDirection.Scale(magnitude));
            }

            if (IsHit)
                offset = offset.Add(HitRecoilDirection.Scale(MathF.Sin(HitProgress * MathF.PI) * 0.085f));

            if (IsSpawning)
                offset = offset.Add(new PresentationPoint(0f, (1f - SpawnProgress) * 0.10f));

            if (Lifecycle == CombatVisualLifecycle.Dying && DeathElapsedSeconds >= 0)
                offset = offset.Add(new PresentationPoint(0f, DeathProgress * 0.16f));

            return offset;
        }

        private static float Progress(double elapsed, double duration)
            => duration <= 0 ? 1f : Math.Clamp((float)(elapsed / duration), 0f, 1f);

        private static float SmoothStep(float value) => value * value * (3f - (2f * value));
    }

    private sealed class CombatProjectileTimelineState
    {
        public CombatProjectileTimelineState(
            string actorId,
            string targetId,
            PresentationPoint from,
            PresentationPoint to,
            double durationSeconds)
        {
            ActorId = actorId;
            TargetId = targetId;
            From = from;
            To = to;
            DurationSeconds = durationSeconds;
        }

        public string ActorId { get; }
        public string TargetId { get; }
        public PresentationPoint From { get; }
        public PresentationPoint To { get; }
        public double DurationSeconds { get; }
        public double ElapsedSeconds { get; set; }

        public CombatProjectileVisualState ToVisualState()
        {
            var progress = DurationSeconds <= 0 ? 1f : Math.Clamp((float)(ElapsedSeconds / DurationSeconds), 0f, 1f);
            return new CombatProjectileVisualState(ActorId, TargetId, From, To, PresentationPoint.Lerp(From, To, progress), progress);
        }
    }
}
