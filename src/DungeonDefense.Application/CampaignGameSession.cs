using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record CampaignActionResult(bool Success, string? Error = null)
{
    public static CampaignActionResult Ok() => new(true);
    public static CampaignActionResult Reject(string error) => new(false, error);
}

public sealed record DefenseResolution(
    DefenseOutcome Outcome,
    int CompletedDay,
    int CurrentDay,
    ResourceBundle Reward,
    IReadOnlyList<string> NewlyUnlockedIds,
    bool RegionCleared = false,
    string? ClearedRegionId = null,
    string? CurrentRegionId = null,
    string? ClearedDungeonArchiveId = null);

/// <summary>
/// Owns persistent campaign progression around battle-local Defense/Invasion simulations.
/// Day/resources/research/unlocks and encounter settlement live here rather than in either simulation.
/// </summary>
public sealed class CampaignGameSession
{
    private CampaignState _state;
    private CampaignState? _attemptSnapshot;
    private bool _activeDefenseResolved;
    private InvasionContent? _activeInvasionContent;
    private string? _activeInvasionLocationId;
    private bool _activeInvasionResolved;
    private bool _activeInvasionFirstClearScenario = true;
    private bool _regionAdvancedOnDefenseResolution;

    public CampaignGameSession(
        CampaignState initialState,
        CampaignProgressionContent progression,
        RegionCampaignContent? regions = null)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(progression);
        _state = initialState.Clone();
        Progression = progression;
        Regions = regions;
        Defense = new DefenseGameSession(_state.Dungeon);
        RefreshAutomaticUnlocks();
    }

    public static CampaignGameSession FromSave(
        CampaignSaveFile file,
        CampaignProgressionContent progression,
        InvasionContent? invasionContent = null,
        RegionCampaignContent? regions = null)
    {
        var imported = CampaignSaveService.Import(file, progression.ContentVersion);
        ValidateSaveContentReferences(imported.State, progression, invasionContent, regions);
        var session = new CampaignGameSession(imported.State, progression, regions);
        session.Defense.SelectFloor(imported.SelectedFloorId.Value);
        if (imported.ActiveInvasion is { } suspended)
        {
            if (invasionContent is null)
                throw new InvalidDataException("Campaign save contains an active invasion but invasion content was not supplied.");
            var effectiveInvasionContent = session.EffectiveInvasionContent(invasionContent);
            var scenario = InvasionCampaignService.ResolveScenario(session._state, effectiveInvasionContent, suspended.LocationId, suspended.Snapshot.FloorId, suspended.Snapshot.Seed, suspended.IsFirstClearScenario);
            session.ActiveInvasion = InvasionSimulation.Restore(suspended.Snapshot, scenario.Floor, effectiveInvasionContent);
            session._activeInvasionContent = effectiveInvasionContent;
            session._activeInvasionLocationId = suspended.LocationId;
            session._activeInvasionFirstClearScenario = suspended.IsFirstClearScenario;
            session._activeInvasionResolved = suspended.IsResolved;
        }
        return session;
    }

    private static void ValidateSaveContentReferences(
        CampaignState state,
        CampaignProgressionContent progression,
        InvasionContent? invasionContent,
        RegionCampaignContent? regions)
    {
        var researchById = progression.Research.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var researchId in state.CompletedResearch)
        {
            if (!researchById.TryGetValue(researchId, out var research))
                throw new InvalidDataException($"Campaign save references unknown research: {researchId}.");
            foreach (var requiredId in research.RequiredResearchIds ?? [])
                if (!state.HasCompletedResearch(requiredId))
                    throw new InvalidDataException($"Campaign save research prerequisite is missing: {researchId} requires {requiredId}.");
        }

        var speciesMaxLevels = progression.SpeciesUpgrades
            .GroupBy(x => x.SpeciesId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Max(y => y.TargetLevel), StringComparer.Ordinal);
        foreach (var (speciesId, level) in state.SpeciesLevels)
        {
            if (!speciesMaxLevels.TryGetValue(speciesId, out var maxLevel))
                throw new InvalidDataException($"Campaign save references unknown species progression: {speciesId}.");
            if (level > maxLevel)
                throw new InvalidDataException($"Campaign save species level exceeds content: {speciesId} level {level} > {maxLevel}.");
        }

        if (regions is not null)
        {
            if (!regions.TryRegion(state.RegionId, out _))
                throw new InvalidDataException($"Campaign save references unknown current region: {state.RegionId}.");

            foreach (var archive in state.ClearedDungeons)
            {
                if (!regions.TryRegion(archive.RegionId, out var region))
                    throw new InvalidDataException($"Campaign save archive references unknown region: {archive.RegionId}.");
                var expectedArchiveId = $"{region.Id}.clear";
                if (!string.Equals(archive.ArchiveId, expectedArchiveId, StringComparison.Ordinal))
                    throw new InvalidDataException($"Campaign save archive ID does not match region: {archive.ArchiveId} != {expectedArchiveId}.");
                if (!string.Equals(archive.FinalAssaultProfileId, region.FinalAssaultProfileId, StringComparison.Ordinal))
                    throw new InvalidDataException($"Campaign save archive final assault does not match region: {archive.ArchiveId}.");
                if (archive.ClearedDay != region.FinalDefenseDay)
                    throw new InvalidDataException($"Campaign save archive cleared day does not match region: {archive.ArchiveId} day {archive.ClearedDay} != {region.FinalDefenseDay}.");
            }

            var archiveIds = state.ClearedDungeons.Select(x => x.ArchiveId).ToHashSet(StringComparer.Ordinal);
            foreach (var key in state.ChallengeBestScores.Keys)
            {
                var separator = key.LastIndexOf('|');
                if (separator <= 0 || separator == key.Length - 1)
                    throw new InvalidDataException($"Campaign save challenge score key is invalid: {key}.");
                var archiveId = key[..separator];
                var modeText = key[(separator + 1)..];
                if (!archiveIds.Contains(archiveId))
                    throw new InvalidDataException($"Campaign save challenge score references unknown archive: {archiveId}.");
                if (!Enum.TryParse<ChallengeMode>(modeText, ignoreCase: false, out _))
                    throw new InvalidDataException($"Campaign save challenge score references unknown mode: {modeText}.");
            }
        }

        if (invasionContent is null) return;
        var locations = invasionContent.Locations.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var progress in state.InvasionProgress.Values)
        {
            if (!locations.TryGetValue(progress.LocationId, out var location))
                throw new InvalidDataException($"Campaign save invasion progress references unknown location: {progress.LocationId}.");
            if (progress.UnlockedDepth > location.Floors.Count)
                throw new InvalidDataException($"Campaign save invasion depth exceeds content: {progress.LocationId} depth {progress.UnlockedDepth} > {location.Floors.Count}.");
            var floorsById = location.Floors.ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var floorId in progress.ClearedFloorIds)
            {
                if (!floorsById.TryGetValue(floorId, out var floor))
                    throw new InvalidDataException($"Campaign save invasion progress references unknown floor: {progress.LocationId}/{floorId}.");
                if (floor.Depth > progress.UnlockedDepth)
                    throw new InvalidDataException($"Campaign save cleared invasion floor is beyond unlocked depth: {progress.LocationId}/{floorId}.");
            }
            for (var depth = 1; depth < progress.UnlockedDepth; depth++)
            {
                var floor = location.Floors.Single(x => x.Depth == depth);
                if (!progress.ClearedFloorIds.Contains(floor.Id))
                    throw new InvalidDataException($"Campaign save invasion progression has a gap before unlocked depth: {progress.LocationId}/{floor.Id}.");
            }
        }

        foreach (var regen in state.Realtime.InvasionRegeneration)
        {
            if (!locations.TryGetValue(regen.LocationId, out var location)
                || !location.Floors.Any(x => string.Equals(x.Id, regen.FloorId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Campaign save invasion regeneration references unknown floor: {regen.LocationId}/{regen.FloorId}.");
        }
    }

    public CampaignProgressionContent Progression { get; }
    public RegionCampaignContent? Regions { get; }
    public CampaignState State => SnapshotCurrentState();
    public bool IsCampaignContentComplete
    {
        get
        {
            if (Regions is null || !Regions.TryRegion(_state.RegionId, out var region)) return false;
            if (region.NextRegionId is not null || _state.Day <= region.FinalDefenseDay) return false;
            return _state.ClearedDungeons.Any(x => string.Equals(x.RegionId, region.Id, StringComparison.Ordinal));
        }
    }
    public DefenseGameSession Defense { get; private set; }
    public InvasionSimulation? ActiveInvasion { get; private set; }
    public bool IsActiveInvasionResolved => ActiveInvasion is not null && _activeInvasionResolved;
    public CampaignState? AttemptSnapshot => _attemptSnapshot?.Clone();

    public CampaignSaveFile ExportSave()
    {
        if (Defense.ActiveDefense is not null)
            throw new InvalidOperationException("Campaign save does not yet support an active defense encounter.");
        SyncDungeonFromEditor();
        var suspended = ActiveInvasion is null
            ? null
            : new SuspendedInvasionState(
                _activeInvasionLocationId ?? throw new InvalidOperationException("Active invasion location context is missing."),
                ActiveInvasion.CreateSnapshot(),
                _activeInvasionFirstClearScenario,
                _activeInvasionResolved);
        return CampaignSaveService.Export(_state, Defense.DungeonEditor.SelectedFloorId, Progression.ContentVersion, suspended);
    }

    public DefenseAssaultProfile? EffectiveAssaultProfile(IReadOnlyList<DefenseAssaultProfile> assaultProfiles)
    {
        ArgumentNullException.ThrowIfNull(assaultProfiles);
        if (Regions is null || !Regions.TryRegion(_state.RegionId, out var region) || _state.Day != region.FinalDefenseDay)
            return null;
        return assaultProfiles.SingleOrDefault(x => string.Equals(x.Id, region.FinalAssaultProfileId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Region final assault profile is missing: {region.FinalAssaultProfileId}.");
    }

    public DefenseContent DefenseContentForCurrentDay(
        DefenseContent baseContent,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles)
    {
        ArgumentNullException.ThrowIfNull(baseContent);
        var profile = EffectiveAssaultProfile(assaultProfiles);
        var encounter = profile is null ? baseContent : baseContent.WithWaves(profile.Waves);
        return CampaignDefenseContentService.ApplyProgression(encounter, _state, Progression);
    }

    public GeneratedDefenseScenario ResolveScheduledDefenseScenario(
        RegionDefenseScheduleContent schedule,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(assaultProfiles);
        if (!string.Equals(schedule.RegionId, _state.RegionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Defense schedule {schedule.RegionId} does not apply to current region {_state.RegionId}.");
        var scenario = RegionDefenseScenarioGenerator.Generate(schedule, _state.Day, seed, assaultProfiles);
        var final = EffectiveAssaultProfile(assaultProfiles);
        if (final is not null && !string.Equals(scenario.AssaultProfileId, final.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Final Day schedule must use region final assault {final.Id}, not {scenario.AssaultProfileId}.");
        return scenario;
    }

    public DefenseContent DefenseContentForScheduledDay(
        DefenseContent baseContent,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles,
        RegionDefenseScheduleContent schedule,
        int seed,
        out GeneratedDefenseScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(baseContent);
        scenario = ResolveScheduledDefenseScenario(schedule, assaultProfiles, seed);
        var encounter = baseContent.WithWaves(scenario.Waves);
        return CampaignDefenseContentService.ApplyProgression(encounter, _state, Progression);
    }

    public DefenseSimulation StartDefense(DefenseContent content, int seed)
    {
        if (IsCampaignContentComplete) throw new InvalidOperationException("No scheduled campaign defense remains in the configured regions.");
        if (Defense.ActiveDefense is { Outcome: DefenseOutcome.Running })
            throw new InvalidOperationException("Defense already running.");
        if (ActiveInvasion is not null) throw new InvalidOperationException("Cannot start defense while invasion is active.");

        SyncDungeonFromEditor();
        _attemptSnapshot = _state.Clone();
        _activeDefenseResolved = false;
        return Defense.StartDefense(content, seed);
    }

    public DefenseResolution ResolveCompletedDefense()
    {
        var simulation = Defense.ActiveDefense ?? throw new InvalidOperationException("No active defense.");
        if (simulation.Outcome == DefenseOutcome.Running) throw new InvalidOperationException("Defense is still running.");
        if (_attemptSnapshot is null) throw new InvalidOperationException("Defense attempt snapshot is missing.");
        if (_activeDefenseResolved) throw new InvalidOperationException("Defense outcome was already resolved.");

        _activeDefenseResolved = true;
        var completedDay = _attemptSnapshot.Day;
        if (simulation.Outcome == DefenseOutcome.Failure)
        {
            _state = _attemptSnapshot.Clone();
            return new DefenseResolution(DefenseOutcome.Failure, completedDay, _state.Day, ResourceBundle.Zero, [], CurrentRegionId: _state.RegionId);
        }

        SyncDungeonFromEditor();
        var reward = Progression.DefenseRewardForDay(_state.Day);
        _state.Grant(reward);
        var clearedRegionId = _state.RegionId;
        var regionCleared = false;
        string? archiveId = null;
        _regionAdvancedOnDefenseResolution = false;
        if (Regions is not null && Regions.TryRegion(_state.RegionId, out var region) && completedDay == region.FinalDefenseDay)
        {
            regionCleared = true;
            var archive = _state.ArchiveCurrentDungeon(region.FinalAssaultProfileId);
            archiveId = archive.ArchiveId;
            if (region.NextRegionId is { } nextRegionId)
            {
                var nextRegion = Regions.Region(nextRegionId);
                var board = DungeonBoardProfiles.Resolve(nextRegion.StartingBoardProfileId);
                _state.BeginRegion(nextRegion.Id, PlayerDungeonState.FromSingleFloor(board.CreateBase(), board.Id));
                _regionAdvancedOnDefenseResolution = true;
            }
            else
            {
                _state.AdvanceDay();
            }
        }
        else
        {
            _state.AdvanceDay();
        }
        var unlocked = RefreshAutomaticUnlocks();
        return new DefenseResolution(
            DefenseOutcome.Success, completedDay, _state.Day, reward, unlocked, regionCleared,
            regionCleared ? clearedRegionId : null, _state.RegionId, archiveId);
    }

    public void ReturnToPreparation()
    {
        if (Defense.ActiveDefense is null || Defense.ActiveDefense.Outcome == DefenseOutcome.Running)
            throw new InvalidOperationException("Defense must be completed first.");

        var outcome = Defense.ActiveDefense.Outcome;
        if (!_activeDefenseResolved) ResolveCompletedDefense();
        Defense.ReturnToPreparation();
        if (outcome == DefenseOutcome.Failure || _regionAdvancedOnDefenseResolution)
            Defense = new DefenseGameSession(_state.Dungeon);

        _attemptSnapshot = null;
        _activeDefenseResolved = false;
        _regionAdvancedOnDefenseResolution = false;
    }

    public InvasionContent EffectiveInvasionContent(InvasionContent content)
        => CampaignInvasionContentService.ApplyProgression(content, _state, Progression);

    public InvasionScoutReport ScoutInvasion(InvasionContent content, string locationId, string floorId, int scenarioSeed = 0)
        => InvasionCampaignService.Scout(SnapshotCurrentState(), EffectiveInvasionContent(content), locationId, floorId, scenarioSeed);

    public InvasionSimulation StartInvasion(
        InvasionContent content,
        string locationId,
        string floorId,
        IReadOnlyList<InvasionFormationEntry> formation,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Defense.ActiveDefense is not null) throw new InvalidOperationException("Cannot start invasion while a defense result is active.");
        if (ActiveInvasion is not null) throw new InvalidOperationException("Invasion already active.");

        var effectiveContent = EffectiveInvasionContent(content);
        var firstClearScenario = !_state.IsInvasionFloorCleared(locationId, floorId);
        var scout = InvasionCampaignService.Scout(SnapshotCurrentState(), effectiveContent, locationId, floorId, seed);
        if (!scout.IsUnlocked) throw new InvalidOperationException($"Invasion floor is locked: {locationId}/{floorId}.");
        if (!scout.IsAvailable) throw new InvalidOperationException($"Invasion floor is regenerating: {locationId}/{floorId} ({scout.RegenerationRemaining}).");
        var scenario = InvasionCampaignService.ResolveScenario(_state, effectiveContent, locationId, floorId, seed, firstClearScenario);

        ActiveInvasion = new InvasionSimulation(scenario.Floor, formation, effectiveContent, seed);
        _activeInvasionContent = effectiveContent;
        _activeInvasionLocationId = locationId;
        _activeInvasionFirstClearScenario = firstClearScenario;
        _activeInvasionResolved = false;
        return ActiveInvasion;
    }

    /// <summary>
    /// Returns the player-visible result for an already-settled active invasion without applying rewards again.
    /// Intended for restoring the Result screen from a resolved autosave.
    /// </summary>
    public InvasionResolution DescribeCompletedInvasion()
    {
        var simulation = ActiveInvasion ?? throw new InvalidOperationException("No active invasion.");
        if (simulation.Outcome == InvasionOutcome.Running) throw new InvalidOperationException("Invasion is still running.");
        if (!_activeInvasionResolved) throw new InvalidOperationException("Invasion outcome has not been resolved yet.");
        var content = _activeInvasionContent ?? throw new InvalidOperationException("Invasion content context is missing.");
        var locationId = _activeInvasionLocationId ?? throw new InvalidOperationException("Invasion location context is missing.");
        return InvasionCampaignService.DescribeOutcome(_state, content, locationId, simulation, _activeInvasionFirstClearScenario);
    }

    public InvasionResolution ResolveCompletedInvasion()
    {
        var simulation = ActiveInvasion ?? throw new InvalidOperationException("No active invasion.");
        if (simulation.Outcome == InvasionOutcome.Running) throw new InvalidOperationException("Invasion is still running.");
        if (_activeInvasionResolved) throw new InvalidOperationException("Invasion outcome was already resolved.");
        var content = _activeInvasionContent ?? throw new InvalidOperationException("Invasion content context is missing.");
        var locationId = _activeInvasionLocationId ?? throw new InvalidOperationException("Invasion location context is missing.");

        _activeInvasionResolved = true;
        return InvasionCampaignService.ApplyOutcome(_state, content, locationId, simulation);
    }

    public void ReturnFromInvasion()
    {
        if (ActiveInvasion is null || ActiveInvasion.Outcome == InvasionOutcome.Running)
            throw new InvalidOperationException("Invasion must be completed first.");
        if (!_activeInvasionResolved) ResolveCompletedInvasion();
        ActiveInvasion = null;
        _activeInvasionContent = null;
        _activeInvasionLocationId = null;
        _activeInvasionFirstClearScenario = true;
        _activeInvasionResolved = false;
    }

    public long ObserveRealtime(DateTimeOffset nowUtc)
        => _state.Realtime.Observe(nowUtc, Progression.RealtimeProduction);

    public ResourceBundle PendingProduction() => _state.Realtime.PendingProduction();

    public ResourceBundle CollectProduction()
    {
        if (Defense.ActiveDefense is { Outcome: DefenseOutcome.Running } || ActiveInvasion is not null)
            throw new InvalidOperationException("Cannot collect production during an active encounter.");
        var collected = _state.Realtime.CollectProduction();
        _state.Grant(collected);
        return collected;
    }

    public CampaignChallengeSession CreateChallenge(
        string archiveId,
        ChallengeMode mode,
        DefenseContent baseContent,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles)
    {
        if (Regions is null) throw new InvalidOperationException("Region campaign content is not configured.");
        if (Defense.ActiveDefense is not null || ActiveInvasion is not null)
            throw new InvalidOperationException("Cannot start a challenge while another encounter is active.");
        return CampaignChallengeService.Create(_state, Regions, archiveId, mode, baseContent, assaultProfiles);
    }

    public bool RecordChallengeResult(ChallengeResult result)
        => result.Definition.Mode == ChallengeMode.Score
            && _state.RecordChallengeScore(result.Definition.ArchiveId, result.Definition.Mode, result.Score);

    public CampaignActionResult CompleteResearch(string researchId)
    {
        if (Defense.ActiveDefense is { Outcome: DefenseOutcome.Running } || ActiveInvasion is not null)
            return CampaignActionResult.Reject("Cannot research during an active encounter.");
        var definition = Progression.Research.SingleOrDefault(x => string.Equals(x.Id, researchId, StringComparison.Ordinal));
        if (definition is null) return CampaignActionResult.Reject($"Unknown research: {researchId}");
        if (_state.HasCompletedResearch(researchId)) return CampaignActionResult.Reject("Research already completed.");
        var missingPrerequisite = (definition.RequiredResearchIds ?? []).FirstOrDefault(id => !_state.HasCompletedResearch(id));
        if (missingPrerequisite is not null) return CampaignActionResult.Reject($"Research prerequisite not completed: {missingPrerequisite}.");
        var requiredRegions = definition.RequiredRegionIds ?? [];
        if (requiredRegions.Count > 0 && !requiredRegions.Contains(_state.RegionId, StringComparer.Ordinal))
            return CampaignActionResult.Reject($"Research is unavailable in current region: {_state.RegionId}.");
        if (!_state.Resources.Covers(definition.Cost)) return CampaignActionResult.Reject("Insufficient resources.");

        _state.Spend(definition.Cost);
        _state.CompleteResearch(researchId);
        foreach (var unlockId in definition.UnlockIds) _state.AddUnlock(unlockId);
        RefreshAutomaticUnlocks();
        return CampaignActionResult.Ok();
    }

    public CampaignActionResult UpgradeSpecies(string speciesId)
    {
        if (Defense.ActiveDefense is { Outcome: DefenseOutcome.Running } || ActiveInvasion is not null)
            return CampaignActionResult.Reject("Cannot upgrade species during an active encounter.");
        var nextLevel = _state.SpeciesLevel(speciesId) + 1;
        var definition = Progression.SpeciesUpgrades.SingleOrDefault(x =>
            string.Equals(x.SpeciesId, speciesId, StringComparison.Ordinal) && x.TargetLevel == nextLevel);
        if (definition is null) return CampaignActionResult.Reject($"No species upgrade definition for {speciesId} level {nextLevel}.");
        if (!_state.Resources.Covers(definition.Cost)) return CampaignActionResult.Reject("Insufficient resources.");

        _state.Spend(definition.Cost);
        _state.SetSpeciesLevel(speciesId, nextLevel);
        foreach (var unlockId in definition.UnlockIds) _state.AddUnlock(unlockId);
        RefreshAutomaticUnlocks();
        return CampaignActionResult.Ok();
    }

    public CampaignActionResult UnlockNextFloor()
    {
        if (Defense.ActiveDefense is { Outcome: DefenseOutcome.Running } || ActiveInvasion is not null)
            return CampaignActionResult.Reject("Cannot expand dungeon during an active encounter.");
        if (!_state.HasUnlock(CampaignFeatureIds.MultiFloor))
            return CampaignActionResult.Reject("Floor Expansion is not unlocked.");

        var depth = Defense.Dungeon.FloorCount + 1;
        var definition = Progression.FloorExpansions.SingleOrDefault(x => x.Depth == depth);
        if (definition is null) return CampaignActionResult.Reject($"No floor expansion content for depth {depth}.");
        if (!_state.Resources.Covers(definition.Cost)) return CampaignActionResult.Reject("Insufficient resources.");

        var nextId = $"floor.{depth:D3}";
        Defense.UnlockFloor(nextId, definition.BoardProfileId);
        _state.Spend(definition.Cost);
        SyncDungeonFromEditor();
        return CampaignActionResult.Ok();
    }

    public IReadOnlyList<string> RefreshAutomaticUnlocks()
    {
        var unlocked = new List<string>();
        foreach (var rule in Progression.UnlockRules.Where(x => x.Enabled))
        {
            if (_state.HasUnlock(rule.UnlockId)) continue;
            if (_state.Day < rule.RequiredDay) continue;
            if (rule.RequiredResearchIds.Any(id => !_state.HasCompletedResearch(id))) continue;
            if (_state.AddUnlock(rule.UnlockId)) unlocked.Add(rule.UnlockId);
        }
        return unlocked;
    }

    private CampaignState SnapshotCurrentState()
    {
        var snapshot = _state.Clone();
        if (Defense.ActiveDefense is null)
            snapshot.ReplaceDungeon(Defense.Dungeon);
        return snapshot;
    }

    private void SyncDungeonFromEditor() => _state.ReplaceDungeon(Defense.Dungeon);
}
