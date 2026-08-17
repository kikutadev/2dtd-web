using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record CampaignSemanticCommandResult(
    bool Success,
    string? Error,
    int AdvancedTicks = 0,
    ResourceBundle? CollectedProduction = null,
    IReadOnlyList<CampaignTransitionEvent>? Transitions = null)
{
    public static CampaignSemanticCommandResult Ok(int advancedTicks = 0, ResourceBundle? collectedProduction = null)
        => new(true, null, advancedTicks, collectedProduction, []);

    public static CampaignSemanticCommandResult Reject(string error)
        => new(false, error, Transitions: []);

    public CampaignSemanticCommandResult WithTransitions(IReadOnlyList<CampaignTransitionEvent> transitions)
        => this with { Transitions = transitions };
}

/// <summary>
/// Executes campaign-level semantic commands through the same Application services used by product adapters.
/// Edit and defense spell commands remain owned by DefenseEditCommandService / DefenseSimulation.
/// </summary>
public static class CampaignSemanticCommandService
{
    public static CampaignSemanticCommandResult Execute(
        CampaignGameSession campaign,
        SemanticCommand command,
        InvasionContent invasionContent,
        int defaultSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(invasionContent);

        var transitionCursor = campaign.TransitionCount;
        var result = command switch
        {
            CompleteResearchCommand research => FromAction(campaign.CompleteResearch(research.ResearchId)),
            ObserveRealtimeCommand realtime => ExecuteObserveRealtime(campaign, realtime),
            CollectProductionCommand => CampaignSemanticCommandResult.Ok(collectedProduction: campaign.CollectProduction()),
            StartInvasionCommand invasion => ExecuteStartInvasion(campaign, invasion, invasionContent, defaultSeed),
            DeployGroupCommand deploy => ExecuteDeploy(campaign, deploy),
            CastInvasionSpellCommand spell => ExecuteInvasionSpell(campaign, spell),
            RetreatInvasionCommand => ExecuteRetreat(campaign),
            ReturnFromInvasionCommand => ExecuteReturnFromInvasion(campaign),
            AdvanceTicksCommand advance => ExecuteAdvanceTicks(campaign, advance),
            _ => CampaignSemanticCommandResult.Reject($"Unsupported campaign semantic command: {command.Type}"),
        };
        return result.WithTransitions(campaign.TransitionsSince(transitionCursor));
    }

    private static CampaignSemanticCommandResult ExecuteObserveRealtime(CampaignGameSession campaign, ObserveRealtimeCommand command)
    {
        campaign.ObserveRealtime(command.NowUtc);
        return CampaignSemanticCommandResult.Ok();
    }

    private static CampaignSemanticCommandResult ExecuteStartInvasion(
        CampaignGameSession campaign,
        StartInvasionCommand command,
        InvasionContent invasionContent,
        int defaultSeed)
    {
        var formation = command.Formation
            .Select(x => new InvasionFormationEntry(x.UnitId, x.Count))
            .ToArray();
        campaign.StartInvasion(
            invasionContent,
            command.LocationId,
            command.FloorId,
            formation,
            command.Seed == 0 ? defaultSeed : command.Seed);
        return CampaignSemanticCommandResult.Ok();
    }

    private static CampaignSemanticCommandResult ExecuteDeploy(CampaignGameSession campaign, DeployGroupCommand command)
    {
        var invasion = campaign.ActiveInvasion;
        if (invasion is null) return CampaignSemanticCommandResult.Reject("deploy_group requires an active invasion.");
        invasion.Deploy(command.UnitId, command.Count);
        return CampaignSemanticCommandResult.Ok();
    }

    private static CampaignSemanticCommandResult ExecuteInvasionSpell(CampaignGameSession campaign, CastInvasionSpellCommand command)
    {
        var invasion = campaign.ActiveInvasion;
        if (invasion is null) return CampaignSemanticCommandResult.Reject("cast_invasion_spell requires an active invasion.");
        return invasion.CastSupportSpell(command.SpellId)
            ? CampaignSemanticCommandResult.Ok()
            : CampaignSemanticCommandResult.Reject($"Invasion spell could not be cast: {command.SpellId}.");
    }

    private static CampaignSemanticCommandResult ExecuteRetreat(CampaignGameSession campaign)
    {
        var invasion = campaign.ActiveInvasion;
        if (invasion is null) return CampaignSemanticCommandResult.Reject("retreat requires an active invasion.");
        invasion.RequestRetreat();
        return CampaignSemanticCommandResult.Ok();
    }

    private static CampaignSemanticCommandResult ExecuteReturnFromInvasion(CampaignGameSession campaign)
    {
        var invasion = campaign.ActiveInvasion;
        if (invasion is null) return CampaignSemanticCommandResult.Reject("return_from_invasion requires an active invasion result.");
        if (invasion.Outcome == InvasionOutcome.Running)
            return CampaignSemanticCommandResult.Reject("return_from_invasion requires a completed invasion.");
        campaign.ReturnFromInvasion();
        return CampaignSemanticCommandResult.Ok();
    }

    private static CampaignSemanticCommandResult ExecuteAdvanceTicks(CampaignGameSession campaign, AdvanceTicksCommand command)
    {
        var defense = campaign.Defense.ActiveDefense;
        var invasion = campaign.ActiveInvasion;
        if (defense is not null && invasion is not null)
            return CampaignSemanticCommandResult.Reject("Cannot advance ticks with both defense and invasion active.");
        if (defense is null && invasion is null)
            return CampaignSemanticCommandResult.Reject("advance_ticks requires an active defense or invasion.");

        var advanced = 0;
        while (advanced < command.Ticks)
        {
            if (defense is { Outcome: DefenseOutcome.Running }) defense.Step();
            else if (invasion is { Outcome: InvasionOutcome.Running }) invasion.Step();
            else break;
            advanced++;
        }
        return CampaignSemanticCommandResult.Ok(advanced);
    }

    private static CampaignSemanticCommandResult FromAction(CampaignActionResult result)
        => result.Success ? CampaignSemanticCommandResult.Ok() : CampaignSemanticCommandResult.Reject(result.Error ?? "Campaign action rejected.");
}
