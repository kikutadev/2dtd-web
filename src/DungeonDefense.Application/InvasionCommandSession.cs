using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

/// <summary>
/// Application command boundary for the spatial invasion runtime.
/// Hosts submit semantic intents here instead of invoking Core simulation methods directly.
/// Campaign progression/reward settlement remains owned by CampaignGameSession.
/// </summary>
public sealed class InvasionCommandSession
{
    private InvasionCommandSession(InvasionSimulation simulation)
    {
        Simulation = simulation;
    }

    public InvasionSimulation Simulation { get; }

    public static InvasionCommandSession Start(
        InvasionFloorDefinition floor,
        InvasionContent content,
        IReadOnlyList<InvasionFormationEntry> formation,
        int seed)
        => new(new InvasionSimulation(floor, formation, content, seed));

    public static InvasionCommandSession Restore(
        InvasionSimulationSnapshot snapshot,
        InvasionFloorDefinition floor,
        InvasionContent content)
        => new(InvasionSimulation.Restore(snapshot, floor, content));

    public CampaignSemanticCommandResult Execute(SemanticCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            return command switch
            {
                DeployGroupCommand deploy => ExecuteDeploy(deploy),
                CastInvasionSpellCommand spell => ExecuteSpell(spell),
                RetreatInvasionCommand => ExecuteRetreat(),
                AdvanceTicksCommand advance => ExecuteAdvance(advance),
                _ => CampaignSemanticCommandResult.Reject($"Unsupported spatial invasion command: {command.Type}"),
            };
        }
        catch (InvalidOperationException error)
        {
            return CampaignSemanticCommandResult.Reject(error.Message);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return CampaignSemanticCommandResult.Reject(error.Message);
        }
    }

    public InvasionSimulationSnapshot Suspend() => Simulation.CreateSnapshot();

    private CampaignSemanticCommandResult ExecuteDeploy(DeployGroupCommand command)
    {
        Simulation.Deploy(command.UnitId, command.Count);
        return CampaignSemanticCommandResult.Ok();
    }

    private CampaignSemanticCommandResult ExecuteSpell(CastInvasionSpellCommand command)
        => Simulation.CastSupportSpell(command.SpellId)
            ? CampaignSemanticCommandResult.Ok()
            : CampaignSemanticCommandResult.Reject($"Invasion spell could not be cast: {command.SpellId}.");

    private CampaignSemanticCommandResult ExecuteRetreat()
    {
        Simulation.RequestRetreat();
        return CampaignSemanticCommandResult.Ok();
    }

    private CampaignSemanticCommandResult ExecuteAdvance(AdvanceTicksCommand command)
    {
        if (command.Ticks < 0) return CampaignSemanticCommandResult.Reject("advance_ticks cannot be negative.");
        var advanced = 0;
        while (advanced < command.Ticks && Simulation.Outcome == InvasionOutcome.Running)
        {
            Simulation.Step();
            advanced++;
        }
        return CampaignSemanticCommandResult.Ok(advanced);
    }
}
