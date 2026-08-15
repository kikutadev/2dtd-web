using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public enum JourneyProgressionChoiceKind
{
    Research,
    SpeciesUpgrade,
}

public sealed record JourneyProgressionChoice(
    JourneyProgressionChoiceKind Kind,
    string Id,
    int TargetLevel = 0)
{
    public static JourneyProgressionChoice Research(string id) => new(JourneyProgressionChoiceKind.Research, id);
    public static JourneyProgressionChoice Species(string speciesId, int targetLevel) => new(JourneyProgressionChoiceKind.SpeciesUpgrade, speciesId, targetLevel);
}

public sealed record FirstRegionJourneyPolicy(
    string Id,
    DungeonBuildPatternFile BuildPattern,
    JourneyBuildArchetype BuildArchetype,
    IReadOnlyList<JourneyProgressionChoice> ProgressionPriority,
    int MaxInvasionsPerStuckDay = 4,
    bool AutoBattle = true);

public sealed record FirstRegionJourneyDayResult(
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
    int TrapDamage,
    int GuardDamage,
    int FacilityDamage,
    int SpellCasts,
    ResourceBundle ResourcesAfter,
    IReadOnlyList<string> Purchases,
    IReadOnlyList<string> BuildActions);

public sealed record FirstRegionJourneyResult(
    string PolicyId,
    int Seed,
    bool RegionCleared,
    int ReachedDay,
    int TotalDefenseAttempts,
    int TotalRetries,
    int TotalInvasions,
    int? StuckDay,
    ResourceBundle EndingResources,
    IReadOnlyList<FirstRegionJourneyDayResult> Days,
    CampaignSaveFile EndingSave)
{
    public bool FinalDefenseSuccess => RegionCleared;
    public double AverageRouteLength => Days.Count == 0 ? 0 : Days.Average(x => x.RouteLength);
    public int TrapDamage => Days.Sum(x => x.TrapDamage);
    public int GuardDamage => Days.Sum(x => x.GuardDamage);
    public int FacilityDamage => Days.Sum(x => x.FacilityDamage);
    public int SpellCasts => Days.Sum(x => x.SpellCasts);
}

public static class FirstRegionJourneyAnalyzer
{
    private const string FirstRegionId = "region.first_frontier";

    public static FirstRegionJourneyResult Run(
        FirstRegionJourneyPolicy policy,
        int seed,
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
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(assaultProfiles);
        ArgumentNullException.ThrowIfNull(invasionContent);
        if (!string.Equals(schedule.RegionId, FirstRegionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"First-region analyzer requires {FirstRegionId} schedule.");
        if (policy.MaxInvasionsPerStuckDay <= 0) throw new ArgumentOutOfRangeException(nameof(policy));

        var region = regions.Region(FirstRegionId);
        var boardProfile = DungeonBoardProfiles.Resolve(region.StartingBoardProfileId);
        var campaign = new CampaignGameSession(
            new CampaignState(1, region.Id, PlayerDungeonState.FromSingleFloor(boardProfile.CreateBase(), boardProfile.Id), progression.StartingResources),
            progression,
            regions);
        var applied = campaign.Defense.StaticFiles.ApplyPattern(policy.BuildPattern);
        if (!applied.Success) throw new InvalidOperationException($"Journey policy {policy.Id} pattern failed: {applied.Error}");

        var dayResults = new List<FirstRegionJourneyDayResult>();
        var totalAttempts = 0;
        var totalRetries = 0;
        var totalInvasions = 0;
        int? stuckDay = null;

        while (string.Equals(campaign.State.RegionId, FirstRegionId, StringComparison.Ordinal)
            && campaign.State.Day <= region.FinalDefenseDay)
        {
            var day = campaign.State.Day;
            var purchases = new List<string>();
            var buildActions = JourneyBuildPlanner.EnsureCapacityTarget(campaign.Defense, baseContent, policy.BuildArchetype, day);
            beforeDayProgression?.Invoke(campaign, day);
            TryPurchaseNext(campaign, policy, purchases);
            var attemptsThisDay = 0;
            var invasionsThisDay = 0;
            FirstRegionJourneyDayResult? completedDay = null;

            while (true)
            {
                var attemptIndex = attemptsThisDay;
                var scenarioSeed = ScenarioSeed(seed, day, attemptIndex);
                var content = campaign.DefenseContentForScheduledDay(baseContent, assaultProfiles, schedule, scenarioSeed, out var scenario);
                var routeLength = DungeonPathfinder.FindRoute(campaign.Defense.Dungeon.DeepestFloor.Board).Count;
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
                    completedDay = new FirstRegionJourneyDayResult(
                        day,
                        attemptsThisDay,
                        invasionsThisDay,
                        simulation.Outcome,
                        scenario.AssaultProfileId,
                        scenario.IntensityPercent,
                        simulation.CoreHp,
                        simulation.CoreMaxHp,
                        routeLength,
                        campaign.Defense.Editor.Current.UsedCapacity,
                        JourneyBuildPlanner.CapacityTargetForDay(day),
                        report.TrapDamage,
                        report.GuardDamage,
                        report.FacilityDamage,
                        report.SpellCasts,
                        campaign.State.Resources,
                        purchases.ToArray(),
                        buildActions);
                    campaign.ReturnToPreparation();
                    dayResults.Add(completedDay);
                    if (resolution.RegionCleared) break;
                    break;
                }

                _ = campaign.ResolveCompletedDefense();
                campaign.ReturnToPreparation();
                totalRetries++;
                if (invasionsThisDay >= policy.MaxInvasionsPerStuckDay)
                {
                    completedDay = new FirstRegionJourneyDayResult(
                        day,
                        attemptsThisDay,
                        invasionsThisDay,
                        simulation.Outcome,
                        scenario.AssaultProfileId,
                        scenario.IntensityPercent,
                        simulation.CoreHp,
                        simulation.CoreMaxHp,
                        routeLength,
                        campaign.Defense.Editor.Current.UsedCapacity,
                        JourneyBuildPlanner.CapacityTargetForDay(day),
                        report.TrapDamage,
                        report.GuardDamage,
                        report.FacilityDamage,
                        report.SpellCasts,
                        campaign.State.Resources,
                        purchases.ToArray(),
                        buildActions);
                    dayResults.Add(completedDay);
                    stuckDay = day;
                    break;
                }

                var invasion = SelectInvasion(campaign, invasionContent);
                if (invasion is null && tryPrepareRescueInvasion?.Invoke(campaign, invasionContent) == true)
                    invasion = SelectInvasion(campaign, invasionContent);
                if (invasion is null)
                {
                    completedDay = new FirstRegionJourneyDayResult(
                        day,
                        attemptsThisDay,
                        invasionsThisDay,
                        simulation.Outcome,
                        scenario.AssaultProfileId,
                        scenario.IntensityPercent,
                        simulation.CoreHp,
                        simulation.CoreMaxHp,
                        routeLength,
                        campaign.Defense.Editor.Current.UsedCapacity,
                        JourneyBuildPlanner.CapacityTargetForDay(day),
                        report.TrapDamage,
                        report.GuardDamage,
                        report.FacilityDamage,
                        report.SpellCasts,
                        campaign.State.Resources,
                        purchases.ToArray(),
                        buildActions);
                    dayResults.Add(completedDay);
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
            if (!string.Equals(campaign.State.RegionId, FirstRegionId, StringComparison.Ordinal)) break;
        }

        return new FirstRegionJourneyResult(
            policy.Id,
            seed,
            !string.Equals(campaign.State.RegionId, FirstRegionId, StringComparison.Ordinal),
            dayResults.Count == 0 ? 1 : dayResults.Max(x => x.Day),
            totalAttempts,
            totalRetries,
            totalInvasions,
            stuckDay,
            campaign.State.Resources,
            dayResults,
            campaign.ExportSave());
    }

    private static bool TryPurchaseNext(CampaignGameSession campaign, FirstRegionJourneyPolicy policy, List<string> purchases)
    {
        // Policies are priorities, not shopping suggestions. Saving for the highest-priority unfinished
        // upgrade keeps archetypes distinct and avoids buying a cheap off-policy upgrade merely because
        // the intended upgrade is temporarily unaffordable.
        foreach (var choice in policy.ProgressionPriority)
        {
            switch (choice.Kind)
            {
                case JourneyProgressionChoiceKind.Research:
                {
                    if (campaign.State.HasCompletedResearch(choice.Id)) continue;
                    var research = campaign.Progression.Research.SingleOrDefault(x => string.Equals(x.Id, choice.Id, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException($"Journey policy references unknown research: {choice.Id}.");
                    var requiredRegions = research.RequiredRegionIds ?? [];
                    if (requiredRegions.Count > 0 && !requiredRegions.Contains(campaign.State.RegionId, StringComparer.Ordinal)) continue;
                    if (!campaign.State.Resources.Covers(research.Cost)) return false;
                    var result = campaign.CompleteResearch(choice.Id);
                    if (!result.Success) throw new InvalidOperationException(result.Error);
                    purchases.Add(choice.Id);
                    return true;
                }
                case JourneyProgressionChoiceKind.SpeciesUpgrade:
                {
                    if (choice.TargetLevel <= 0) throw new InvalidOperationException("Species progression choice requires positive target level.");
                    if (campaign.State.SpeciesLevel(choice.Id) >= choice.TargetLevel) continue;
                    var nextLevel = campaign.State.SpeciesLevel(choice.Id) + 1;
                    var upgrade = campaign.Progression.SpeciesUpgrades.SingleOrDefault(x => string.Equals(x.SpeciesId, choice.Id, StringComparison.Ordinal) && x.TargetLevel == nextLevel)
                        ?? throw new InvalidOperationException($"Journey policy references unavailable species upgrade: {choice.Id} Lv.{nextLevel}.");
                    if (!campaign.State.Resources.Covers(upgrade.Cost)) return false;
                    var result = campaign.UpgradeSpecies(choice.Id);
                    if (!result.Success) throw new InvalidOperationException(result.Error);
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
    {
        return content.Locations
            .SelectMany(location => location.Floors.Select(floor => (Location: location, Floor: floor, Scout: campaign.ScoutInvasion(content, location.Id, floor.Id))))
            .Where(x => x.Scout.IsUnlocked && x.Scout.IsAvailable)
            .OrderByDescending(x => x.Scout.IsFirstClear)
            .ThenByDescending(x => x.Floor.Depth)
            .ThenBy(x => x.Location.Id, StringComparer.Ordinal)
            .Select(x => ((string LocationId, string FloorId)?)(x.Location.Id, x.Floor.Id))
            .FirstOrDefault();
    }

    private static void RunInvasion(
        CampaignGameSession campaign,
        InvasionContent content,
        IReadOnlyDictionary<string, UnitDefinition> unitDefinitions,
        string locationId,
        string floorId,
        int seed)
    {
        var formation = new[]
        {
            new InvasionFormationEntry("monster.skeleton_warrior", 3),
            new InvasionFormationEntry("monster.skeleton_archer", 3),
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
        if (simulation.Outcome == InvasionOutcome.Running) throw new InvalidOperationException("Journey invasion exceeded 20,000 ticks.");
        _ = campaign.ResolveCompletedInvasion();
        campaign.ReturnFromInvasion();
    }

    private static int ScenarioSeed(int seed, int day, int attempt)
        => unchecked(seed * 1009 + day * 7919 + attempt * 104729);

    private static int InvasionSeed(int seed, int day, int invasionIndex)
        => unchecked(seed * 3571 + day * 12289 + invasionIndex * 65537 + 17);
}
