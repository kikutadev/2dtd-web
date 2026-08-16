using DungeonDefense.Application;
using DungeonDefense.Core;
using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>Small browser adapter around the production defense simulation and shared presentation timeline.</summary>
internal sealed class DefenseDemo
{
    private const double SimulationStepSeconds = 1.0 / DefenseSimulation.TicksPerSecond;
    private readonly DefenseContent _content;
    private readonly CombatMotionPresentation _presentation = new();
    private DefenseAutoBattleController? _autoBattle;
    private int _eventCursor;
    private double _simulationAccumulatorSeconds;

    public DefenseDemo(DefenseContent content, DefenseGameSession? session = null)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        if (session is null) Reset();
        else
        {
            Session = session;
            ResetRuntimeState();
        }
    }

    public DefenseGameSession Session { get; private set; } = null!;
    public DefenseSimulation? Simulation { get; private set; }
    public CombatVisualState VisualState => _presentation.VisualState;
    public DefenseHudVisualState? HudState => Simulation is { } simulation ? DefenseProductPresentation.BuildHud(simulation, autoBattleEnabled: false) : null;
    public DefenseResultVisualState? ResultState => Simulation is { Outcome: not DefenseOutcome.Running } simulation ? DefenseProductPresentation.BuildResult(simulation) : null;
    public DungeonState Board => Session.Dungeon.Floors[0].Board;
    public DefenseOutcome Outcome => Simulation?.Outcome ?? DefenseOutcome.Running;

    public void Reset()
    {
        Session = DefenseSliceScenario.CreateSession();
        DefenseSliceScenario.ConfigureSuccessfulDefense(Session);
        ResetRuntimeState();
    }

    public void Start()
    {
        if (Simulation is { Outcome: DefenseOutcome.Running }) return;
        if (Simulation is not null)
        {
            Session.ReturnToPreparation();
            ResetRuntimeState();
        }
        Simulation = Session.StartDefense(_content, seed: 20260815);
        _autoBattle = Session.CreateAutoBattleController();
        _simulationAccumulatorSeconds = 0;
        _presentation.SyncSnapshot(Simulation.Units, Simulation.CurrentCombatFloorId, Simulation.Route, Simulation.StaticActors);
        _eventCursor = Simulation.Events.Count;
    }

    /// <summary>
    /// Advances the browser render clock independently from the deterministic 20 Hz simulation clock.
    /// Presentation receives wall-clock delta, while simulation speed changes only how quickly fixed Core steps are consumed.
    /// </summary>
    public bool AdvanceFrame(double deltaSeconds, int speed, bool advanceSimulation)
    {
        var normalizedSpeed = Math.Clamp(speed, 1, 3);
        var renderDelta = Math.Clamp(deltaSeconds, 0.0, 0.10);
        var hadActiveMotion = _presentation.VisualState.HasActiveMotion;

        _presentation.SetBattleSpeed(normalizedSpeed);
        _presentation.Advance(renderDelta, normalizedSpeed);

        if (!advanceSimulation || Simulation is not { Outcome: DefenseOutcome.Running } simulation || _autoBattle is null)
            return hadActiveMotion || _presentation.VisualState.HasActiveMotion;

        _simulationAccumulatorSeconds += renderDelta * normalizedSpeed;
        var stepped = false;
        while (_simulationAccumulatorSeconds >= SimulationStepSeconds && simulation.Outcome == DefenseOutcome.Running)
        {
            _simulationAccumulatorSeconds -= SimulationStepSeconds;
            _autoBattle.TryQueueAction(simulation);
            simulation.Step();
            stepped = true;
        }

        if (stepped)
        {
            _presentation.SyncSnapshot(simulation.Units, simulation.CurrentCombatFloorId, simulation.Route, simulation.StaticActors);
            var newEvents = simulation.Events.Skip(_eventCursor).ToArray();
            _eventCursor = simulation.Events.Count;
            foreach (var combatEvent in newEvents) _presentation.ConsumeEvent(combatEvent);
        }

        return stepped || hadActiveMotion || _presentation.VisualState.HasActiveMotion;
    }

    public string CastFreeze()
    {
        var target = FirstAliveInvader();
        if (Simulation is null || target is null) return "対象がいません";
        var result = Simulation.QueueSpell("spell.freeze", target.Position, floorId: target.FloorId);
        return result.Success ? "凍結を予約" : result.Error ?? "発動できません";
    }

    public string CastPush()
    {
        var target = FirstAliveInvader();
        if (Simulation is null || target is null) return "対象がいません";
        var result = Simulation.QueueSpell("spell.push", target.Position, target.EntityId, target.FloorId);
        return result.Success ? "押し戻しを予約" : result.Error ?? "発動できません";
    }

    public IReadOnlyList<DefenseEvent> RecentEvents(int count = 5)
        => Simulation?.Events.TakeLast(count).Reverse().ToArray() ?? [];

    /// <summary>Clears only host/presentation runtime state while preserving the currently edited production session.</summary>
    private void ResetRuntimeState()
    {
        Simulation = null;
        _autoBattle = null;
        _eventCursor = 0;
        _simulationAccumulatorSeconds = 0;
        _presentation.Reset();
    }

    private UnitSnapshot? FirstAliveInvader()
        => Simulation?.Units.FirstOrDefault(x => x.Team == Team.Invader && x.Alive);
}
