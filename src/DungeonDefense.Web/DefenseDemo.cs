using DungeonDefense.Application;
using DungeonDefense.Core;

namespace DungeonDefense.Web;

/// <summary>Small browser adapter around the production defense simulation.</summary>
internal sealed class DefenseDemo
{
    private readonly DefenseContent _content;
    private DefenseAutoBattleController? _autoBattle;

    public DefenseDemo(DefenseContent content)
    {
        _content = content;
        Reset();
    }

    public DefenseGameSession Session { get; private set; } = null!;
    public DefenseSimulation? Simulation { get; private set; }
    public DungeonState Board => Session.Dungeon.Floors[0].Board;
    public DefenseOutcome Outcome => Simulation?.Outcome ?? DefenseOutcome.Running;

    public void Reset()
    {
        Session = DefenseSliceScenario.CreateSession();
        DefenseSliceScenario.ConfigureSuccessfulDefense(Session);
        Simulation = null;
        _autoBattle = null;
    }

    public void Start()
    {
        if (Simulation is { Outcome: DefenseOutcome.Running }) return;
        if (Simulation is not null) Reset();
        Simulation = Session.StartDefense(_content, seed: 20260815);
        _autoBattle = Session.CreateAutoBattleController();
    }

    public void AdvanceFrame(int speed)
    {
        if (Simulation is not { Outcome: DefenseOutcome.Running } simulation || _autoBattle is null) return;
        var steps = Math.Clamp(speed, 1, 3) * 2; // 100 ms render cadence; 2 ticks equals native 1x time.
        for (var i = 0; i < steps && simulation.Outcome == DefenseOutcome.Running; i++)
        {
            _autoBattle.TryQueueAction(simulation);
            simulation.Step();
        }
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

    private UnitSnapshot? FirstAliveInvader()
        => Simulation?.Units.FirstOrDefault(x => x.Team == Team.Invader && x.Alive);
}
