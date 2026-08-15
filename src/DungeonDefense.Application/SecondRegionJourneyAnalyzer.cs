using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record SecondRegionJourneyDayResult(
    int Day,
    int Attempts,
    int Invasions,
    DefenseOutcome Outcome,
    string AssaultProfileId,
    int IntensityPercent,
    int CoreHp,
    int CoreMaxHp,
    int RouteLength,
    int UsedCapacity,
    int CapacityTarget,
    int RoomRouteCells,
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int GuardCollapses,
    int SpellCasts,
    ResourceBundle ResourcesAfter,
    IReadOnlyList<string> Purchases,
    IReadOnlyList<string> BuildActions);

public sealed record SecondRegionJourneyResult(
    string PolicyId,
    int Seed,
    bool RegionCleared,
    int ReachedDay,
    int TotalDefenseAttempts,
    int TotalRetries,
    int TotalInvasions,
    int? StuckDay,
    ResourceBundle EndingResources,
    IReadOnlyList<SecondRegionJourneyDayResult> Days,
    CampaignSaveFile EndingSave);

public static class SecondRegionJourneyAnalyzer
{
    public const string RegionId = "region.deep_crypt";

    public static SecondRegionJourneyResult Run(
        FirstRegionJourneyPolicy policy,
        int seed,
        CampaignSaveFile startingSave,
        CampaignProgressionContent progression,
        RegionCampaignContent regions,
        RegionDefenseScheduleContent schedule,
        DefenseContent baseContent,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles,
        InvasionContent invasionContent,
        Action<CampaignGameSession, int>? beforeDayProgression = null,
        Func<CampaignGameSession, InvasionContent, bool>? tryPrepareRescueInvasion = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(startingSave);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(assaultProfiles);
        ArgumentNullException.ThrowIfNull(invasionContent);
        if (!string.Equals(schedule.RegionId, RegionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Second-region analyzer requires {RegionId} schedule.");

        var campaign = CampaignGameSession.FromSave(startingSave, progression, invasionContent, baseContent.Units, regions);
        if (!string.Equals(campaign.State.RegionId, RegionId, StringComparison.Ordinal) || campaign.State.Day != 1)
            throw new InvalidOperationException("Second-region journey must start at Deep Crypt Day 1.");
        if (!string.Equals(campaign.State.Dungeon.DeepestFloor.BoardProfileId, DungeonBoardProfiles.DeepCryptId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Second-region journey requires {DungeonBoardProfiles.DeepCryptId} starting board.");

        var region = regions.Region(RegionId);
        var dayResults = new List<SecondRegionJourneyDayResult>();
        var totalAttempts = 0;
        var totalRetries = 0;
        var totalInvasions = 0;
        int? stuckDay = null;
        var regionCleared = false;

        while (campaign.State.Day <= region.FinalDefenseDay && !regionCleared)
        {
            var day = campaign.State.Day;
            var purchases = new List<string>();
            var buildActions = new List<string>();
            buildActions.AddRange(DeepCryptBuildAdaptationPlanner.EnsureSignatureRooms(campaign.Defense, baseContent, policy.BuildArchetype, day));
            buildActions.AddRange(DeepCryptBuildAdaptationPlanner.EnsureCapacityTarget(campaign.Defense, baseContent, policy.BuildArchetype, day));
            beforeDayProgression?.Invoke(campaign, day);
            TryPurchaseNext(campaign, policy, purchases);
            var attemptsThisDay = 0;
            var invasionsThisDay = 0;

            while (true)
            {
                var scenarioSeed = ScenarioSeed(seed, day, attemptsThisDay);
                var content = campaign.DefenseContentForScheduledDay(baseContent, assaultProfiles, schedule, scenarioSeed, out var scenario);
                var analysis = DungeonBuildAnalyzer.Analyze(campaign.Defense.Editor.Current, content);
                var simulation = campaign.StartDefense(content, scenarioSeed);
                var auto = policy.AutoBattle ? campaign.Defense.CreateAutoBattleController() : null;
                while (simulation.Outcome == DefenseOutcome.Running)
                {
                    auto?.TryQueueAction(simulation);
                    simulation.Step();
                }
                totalAttempts++;
                attemptsThisDay++;
                var report = DefenseResultReport.From(simulation);

                if (simulation.Outcome == DefenseOutcome.Success)
                {
                    var resolution = campaign.ResolveCompletedDefense();
                    dayResults.Add(ToDayResult(day, attemptsThisDay, invasionsThisDay, simulation, scenario, analysis, report,
                        campaign.Defense.Editor.Current.UsedCapacity, campaign.State.Resources, purchases, buildActions));
                    regionCleared = resolution.RegionCleared;
                    campaign.ReturnToPreparation();
                    break;
                }

                _ = campaign.ResolveCompletedDefense();
                campaign.ReturnToPreparation();
                totalRetries++;
                if (invasionsThisDay >= policy.MaxInvasionsPerStuckDay)
                {
                    dayResults.Add(ToDayResult(day, attemptsThisDay, invasionsThisDay, simulation, scenario, analysis, report,
                        campaign.Defense.Editor.Current.UsedCapacity, campaign.State.Resources, purchases, buildActions));
                    stuckDay = day;
                    break;
                }

                var invasion = SelectInvasion(campaign, invasionContent);
                if (invasion is null && tryPrepareRescueInvasion?.Invoke(campaign, invasionContent) == true)
                    invasion = SelectInvasion(campaign, invasionContent);
                if (invasion is null)
                {
                    dayResults.Add(ToDayResult(day, attemptsThisDay, invasionsThisDay, simulation, scenario, analysis, report,
                        campaign.Defense.Editor.Current.UsedCapacity, campaign.State.Resources, purchases, buildActions));
                    stuckDay = day;
                    break;
                }

                RunInvasion(campaign, invasionContent, baseContent.Units, invasion.Value.LocationId, invasion.Value.FloorId,
                    InvasionSeed(seed, day, totalInvasions));
                totalInvasions++;
                invasionsThisDay++;
                TryPurchaseNext(campaign, policy, purchases);
            }

            if (stuckDay is not null) break;
        }

        return new SecondRegionJourneyResult(
            policy.Id,
            seed,
            regionCleared,
            dayResults.Count == 0 ? 1 : dayResults.Max(x => x.Day),
            totalAttempts,
            totalRetries,
            totalInvasions,
            stuckDay,
            campaign.State.Resources,
            dayResults,
            campaign.ExportSave());
    }

    private static SecondRegionJourneyDayResult ToDayResult(
        int day,
        int attempts,
        int invasions,
        DefenseSimulation simulation,
        GeneratedDefenseScenario scenario,
        DungeonBuildAnalysis analysis,
        DefenseResultReport report,
        int usedCapacity,
        ResourceBundle resources,
        IReadOnlyList<string> purchases,
        IReadOnlyList<string> buildActions)
        => new(
            day,
            attempts,
            invasions,
            simulation.Outcome,
            scenario.AssaultProfileId,
            scenario.IntensityPercent,
            simulation.CoreHp,
            simulation.CoreMaxHp,
            analysis.RouteLength,
            usedCapacity,
            DeepCryptBuildAdaptationPlanner.CapacityTargetForDay(day),
            analysis.RoomRouteCells,
            report.TrapDamage,
            report.GuardDamage,
            report.FacilityDamage,
            report.GuardCollapseCount,
            report.SpellCasts,
            resources,
            purchases.ToArray(),
            buildActions.ToArray());

    private static bool TryPurchaseNext(CampaignGameSession campaign, FirstRegionJourneyPolicy policy, List<string> purchases)
    {
        foreach (var choice in policy.ProgressionPriority)
        {
            switch (choice.Kind)
            {
                case JourneyProgressionChoiceKind.Research:
                {
                    if (campaign.State.HasCompletedResearch(choice.Id)) continue;
                    var research = campaign.Progression.Research.Single(x => string.Equals(x.Id, choice.Id, StringComparison.Ordinal));
                    if (!campaign.State.Resources.Covers(research.Cost)) return false;
                    var result = campaign.CompleteResearch(choice.Id);
                    if (!result.Success) return false;
                    purchases.Add(choice.Id);
                    return true;
                }
                case JourneyProgressionChoiceKind.SpeciesUpgrade:
                {
                    if (campaign.State.SpeciesLevel(choice.Id) >= choice.TargetLevel) continue;
                    var nextLevel = campaign.State.SpeciesLevel(choice.Id) + 1;
                    var upgrade = campaign.Progression.SpeciesUpgrades.Single(x => string.Equals(x.SpeciesId, choice.Id, StringComparison.Ordinal) && x.TargetLevel == nextLevel);
                    if (!campaign.State.Resources.Covers(upgrade.Cost)) return false;
                    var result = campaign.UpgradeSpecies(choice.Id);
                    if (!result.Success) return false;
                    purchases.Add($"{choice.Id}.lv{nextLevel}");
                    return true;
                }
                default:
                    throw new InvalidOperationException($"Unsupported journey progression choice kind: {choice.Kind}.");
            }
        }
        return false;
    }

    private static (string LocationId, string FloorId)? SelectInvasion(CampaignGameSession campaign, InvasionContent content)
        => content.Locations
            .SelectMany(location => location.Floors.Select(floor => (Location: location, Floor: floor, Scout: campaign.ScoutInvasion(content, location.Id, floor.Id))))
            .Where(x => x.Scout.IsUnlocked && x.Scout.IsAvailable)
            .OrderByDescending(x => x.Scout.IsFirstClear)
            .ThenByDescending(x => x.Floor.Depth)
            .ThenBy(x => x.Location.Id, StringComparer.Ordinal)
            .Select(x => ((string LocationId, string FloorId)?)(x.Location.Id, x.Floor.Id))
            .FirstOrDefault();

    private static void RunInvasion(
        CampaignGameSession campaign,
        InvasionContent content,
        IReadOnlyDictionary<string, UnitDefinition> unitDefinitions,
        string locationId,
        string floorId,
        int seed)
    {
        var effective = campaign.EffectiveInvasionContent(content);
        var warriorCost = effective.UnitDeploymentCosts["monster.skeleton_warrior"];
        var archerCost = effective.UnitDeploymentCosts["monster.skeleton_archer"];
        var capacity = effective.DeploymentCapacity;
        var warriorCount = Math.Max(1, (capacity * 40 / 100) / warriorCost);
        var archerCount = Math.Max(1, (capacity - warriorCount * warriorCost) / archerCost);
        while (warriorCount * warriorCost + archerCount * archerCost > capacity && archerCount > 1) archerCount--;
        var formation = new[]
        {
            new InvasionFormationEntry("monster.skeleton_warrior", warriorCount),
            new InvasionFormationEntry("monster.skeleton_archer", archerCount),
        };
        var simulation = campaign.StartInvasion(content, unitDefinitions, locationId, floorId, formation, seed);
        simulation.DeployAllRemaining();
        var guard = 0;
        while (simulation.Outcome == InvasionOutcome.Running && guard++ < 20_000)
        {
            if (simulation.Mp >= 35 && simulation.SpellCooldownRemaining("invasion.spell.ward") == 0)
                simulation.CastSupportSpell("invasion.spell.ward");
            if (simulation.Mp >= 25 && simulation.SpellCooldownRemaining("invasion.spell.mend") == 0
                && simulation.Units.Any(x => x.Alive && x.Deployed && x.Hp < x.Definition.MaxHp))
                simulation.CastSupportSpell("invasion.spell.mend");
            simulation.Step();
        }
        if (simulation.Outcome == InvasionOutcome.Running) throw new InvalidOperationException("Second-region journey invasion exceeded 20,000 ticks.");
        _ = campaign.ResolveCompletedInvasion();
        campaign.ReturnFromInvasion();
    }

    private static int ScenarioSeed(int seed, int day, int attempt)
        => unchecked(seed * 2017 + day * 12347 + attempt * 65537 + 29);

    private static int InvasionSeed(int seed, int day, int invasionIndex)
        => unchecked(seed * 4099 + day * 16381 + invasionIndex * 104729 + 43);
}
