using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record DefensePatternMatrixRow(
    string PatternId,
    string PatternName,
    string AssaultId,
    string AssaultLabel,
    bool AutoBattle,
    DefenseOutcome Outcome,
    int CoreHp,
    int CoreMaxHp,
    int Ticks,
    int CompletedWaves,
    int DeepestPathIndex,
    int CoreHits,
    int InvaderDeaths,
    int GuardDeaths,
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int SpellCasts,
    string Digest);

public static class DefensePatternMatrixAnalyzer
{
    public static DefensePatternMatrixRow Analyze(
        DefenseContent baseContent,
        DungeonBuildPatternFile pattern,
        DefenseAssaultProfile assault,
        int seed = 4242,
        bool autoBattle = false)
    {
        var roster = baseContent.MonsterRoster
            ?? throw new InvalidOperationException("Defense content must carry MonsterRosterContent for pattern analysis.");
        var session = DefenseSliceScenario.CreateSession(roster);
        var staticFiles = session.StaticFiles;
        var applied = staticFiles.ApplyPattern(pattern);
        if (!applied.Success) throw new InvalidOperationException($"Pattern {pattern.Id} could not be applied: {applied.Error}");

        var dungeon = session.Editor.Current.Clone();
        var content = baseContent.WithWaves(assault.Waves);
        var simulation = session.StartDefense(content, seed);
        var auto = autoBattle ? session.CreateAutoBattleController() : null;
        while (simulation.Outcome == DefenseOutcome.Running)
        {
            auto?.TryQueueAction(simulation);
            simulation.Step();
        }

        var guardIds = dungeon.Guards.Select(x => x.InstanceId).ToHashSet(StringComparer.Ordinal);
        var facilityIds = dungeon.Facilities.Select(x => x.InstanceId).ToHashSet(StringComparer.Ordinal);
        var trapIds = dungeon.Traps.Select(x => x.InstanceId).ToHashSet(StringComparer.Ordinal);
        var invaderIds = simulation.Events.Where(x => x.Type == DefenseEventType.Spawn).Select(x => x.ActorId).ToHashSet(StringComparer.Ordinal);
        var moves = simulation.Events.Where(x => x.Type == DefenseEventType.Move && invaderIds.Contains(x.ActorId));

        return new DefensePatternMatrixRow(
            pattern.Id,
            pattern.Name,
            assault.Id,
            assault.Label,
            autoBattle,
            simulation.Outcome,
            simulation.CoreHp,
            simulation.CoreMaxHp,
            simulation.Tick,
            simulation.Events.Count(x => x.Type == DefenseEventType.WaveEnd),
            moves.Select(x => x.Amount).DefaultIfEmpty(0).Max(),
            simulation.Events.Count(x => x.Type == DefenseEventType.CoreDamaged),
            simulation.Events.Count(x => x.Type == DefenseEventType.Death && invaderIds.Contains(x.ActorId)),
            simulation.Events.Count(x => x.Type == DefenseEventType.Death && guardIds.Contains(x.ActorId)),
            simulation.Events.Where(x => x.Type == DefenseEventType.TrapTriggered && trapIds.Contains(x.ActorId)).Sum(x => x.Amount),
            simulation.Events.Where(x => x.Type == DefenseEventType.Attack && guardIds.Contains(x.ActorId)).Sum(x => x.Amount),
            simulation.Events.Where(x => x.Type == DefenseEventType.Attack && facilityIds.Contains(x.ActorId)).Sum(x => x.Amount),
            simulation.Events.Count(x => x.Type == DefenseEventType.SpellCast),
            simulation.ResultDigest());
    }
}
