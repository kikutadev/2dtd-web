using DungeonDefense.Application;
using DungeonDefense.Contracts;
using DungeonDefense.Core;
using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>
/// Browser product adapter around the production spatial invasion runtime.
/// Web owns only demo selection state and render-clock cadence; gameplay intents go through
/// <see cref="InvasionCommandSession"/> and animation comes from shared Presentation.
/// </summary>
internal sealed class InvasionDemo
{
    private const double SimulationStepSeconds = 1.0 / InvasionSimulation.TicksPerSecond;
    private readonly InvasionContent _content;
    private readonly Dictionary<string, int> _formation = new(StringComparer.Ordinal);
    private readonly InvasionCombatMotionPresentation _motion = new();
    private readonly CampaignState _scoutState;
    private InvasionCommandSession? _commandSession;
    private double _simulationAccumulatorSeconds;

    public InvasionDemo(DefenseContent defenseContent, InvasionContent content)
    {
        ArgumentNullException.ThrowIfNull(defenseContent);
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _scoutState = new CampaignState(
            999,
            "region.first_frontier",
            PlayerDungeonState.FromSingleFloor(DungeonFactory.CreateDefenseSliceDungeon(), "web.demo"),
            ResourceBundle.Zero);
        SelectedLocationId = content.Locations[0].Id;
        SelectedFloorId = content.Locations[0].Floors[0].Id;
        ResetCanonicalFormation();
    }

    public InvasionContent Content => _content;
    public IReadOnlyList<InvasionScoutReport> ScoutReports => BuildDemoScoutReports();
    public InvasionLocationListVisualState LocationsState => InvasionPreparationPresentation.BuildLocations(_content, ScoutReports);
    public InvasionScoutVisualState ScoutState => InvasionPreparationPresentation.BuildScout(SelectedLocationId, ScoutReports);
    public InvasionScoutReport SelectedScoutReport => ScoutReports.Single(x =>
        string.Equals(x.LocationId, SelectedLocationId, StringComparison.Ordinal)
        && string.Equals(x.FloorId, SelectedFloorId, StringComparison.Ordinal));
    public InvasionFormationVisualState FormationState => InvasionPreparationPresentation.BuildFormation(_content, SelectedScoutReport, _formation);
    public InvasionSimulation? Simulation => _commandSession?.Simulation;
    public InvasionBattleVisualState? VisualState => Simulation is { } simulation ? InvasionBattlePresentation.Build(simulation) : null;
    public CombatVisualState CombatVisualState => _motion.VisualState;
    public InvasionResultVisualState? ResultState
    {
        get
        {
            if (Simulation is not { Outcome: not InvasionOutcome.Running } simulation) return null;
            var resolution = InvasionCampaignService.DescribeOutcome(
                _scoutState,
                _content,
                SelectedLocationId,
                simulation,
                firstClearOverride: true);
            return InvasionResultPresentation.Build(SelectedLocationId, simulation, resolution);
        }
    }
    public string SelectedLocationId { get; private set; }
    public string SelectedFloorId { get; private set; }
    public InvasionLocationDefinition SelectedLocation => _content.Location(SelectedLocationId);
    public InvasionFloorDefinition SelectedFloor => _content.Floor(SelectedLocationId, SelectedFloorId);
    public IReadOnlyDictionary<string, int> Formation => _formation;
    public IReadOnlyList<string> FormationUnitIds => _content.UnitDeploymentCosts.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    public int UsedDeploymentCapacity => _formation.Sum(x => checked(x.Value * _content.UnitDeploymentCosts[x.Key]));
    public int DeploymentCapacity => _content.DeploymentCapacity;
    public bool CanStart => Simulation is null && FormationState.CanStart;
    public ResourceBundle VisibleSectionLoot => SelectedScoutReport.VisibleSectionLoot;

    public void SelectLocation(string locationId)
    {
        EnsureNotRunning();
        var location = _content.Location(locationId);
        SelectedLocationId = location.Id;
        SelectedFloorId = location.Floors[0].Id;
        ResetCanonicalFormation();
    }

    public void SelectFloor(string floorId)
    {
        EnsureNotRunning();
        _ = _content.Floor(SelectedLocationId, floorId);
        SelectedFloorId = floorId;
    }

    public bool AdjustFormation(string unitId, int delta)
    {
        EnsureNotRunning();
        if (!_content.UnitDeploymentCosts.ContainsKey(unitId))
            throw new InvalidOperationException($"Unknown invasion formation unit: {unitId}");
        var current = _formation.GetValueOrDefault(unitId);
        var candidate = Math.Max(0, current + delta);
        _formation[unitId] = candidate;
        if (UsedDeploymentCapacity <= DeploymentCapacity) return true;
        _formation[unitId] = current;
        return false;
    }

    public void Start(int seed = 4242)
    {
        EnsureNotRunning();
        if (!CanStart) throw new InvalidOperationException("Invasion formation is empty or exceeds Deployment Capacity.");
        var formation = _formation
            .Where(x => x.Value > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new InvasionFormationEntry(x.Key, x.Value))
            .ToArray();
        _commandSession = InvasionCommandSession.Start(SelectedFloor, _content, formation, seed);
        _simulationAccumulatorSeconds = 0;
        _motion.Reset();
        _motion.Sync(_commandSession.Simulation);
    }

    public bool Deploy(string unitId, int count = 1)
        => Execute(new DeployGroupCommand(unitId, count)).Success;

    public void DeployAllRemaining()
    {
        var state = VisualState ?? throw new InvalidOperationException("Invasion has not started.");
        foreach (var command in state.DeployCommands.Where(x => x.Enabled && x.ReserveCount > 0))
        {
            var result = Execute(new DeployGroupCommand(command.UnitDefinitionId, command.ReserveCount));
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }
    }

    public bool CastSupportSpell(string spellId)
        => Execute(new CastInvasionSpellCommand(spellId)).Success;

    public bool RequestRetreat()
        => Execute(new RetreatInvasionCommand()).Success;

    public bool AdvanceFrame(double deltaSeconds, int speed, bool advanceSimulation)
    {
        var simulation = Simulation;
        if (simulation is not { Outcome: InvasionOutcome.Running }) return false;
        var normalizedSpeed = Math.Clamp(speed, 1, 3);
        var renderDelta = Math.Clamp(deltaSeconds, 0.0, 0.10);
        if (!advanceSimulation) return false;

        _simulationAccumulatorSeconds += renderDelta * normalizedSpeed;
        var stepped = false;
        while (_simulationAccumulatorSeconds >= SimulationStepSeconds && simulation.Outcome == InvasionOutcome.Running)
        {
            _simulationAccumulatorSeconds -= SimulationStepSeconds;
            var result = Execute(new AdvanceTicksCommand(1));
            if (!result.Success) throw new InvalidOperationException(result.Error);
            stepped |= result.AdvancedTicks > 0;
        }
        _motion.Advance(renderDelta, normalizedSpeed);
        return stepped || _motion.VisualState.HasActiveMotion;
    }

    public void ReturnToBriefing()
    {
        if (Simulation is { Outcome: InvasionOutcome.Running })
            throw new InvalidOperationException("Invasion is still running.");
        _commandSession = null;
        _simulationAccumulatorSeconds = 0;
        _motion.Reset();
    }

    public IReadOnlyList<InvasionEvent> RecentEvents(int count = 6)
        => Simulation?.Events.TakeLast(count).Reverse().ToArray() ?? [];

    private CampaignSemanticCommandResult Execute(SemanticCommand command)
    {
        var session = _commandSession ?? throw new InvalidOperationException("Invasion has not started.");
        var result = session.Execute(command);
        _motion.Sync(session.Simulation);
        return result;
    }

    private InvasionScoutReport[] BuildDemoScoutReports()
        => _content.Locations
            .SelectMany(location => location.Floors.Select(floor =>
            {
                var report = InvasionCampaignService.Scout(_scoutState, _content, location.Id, floor.Id, scenarioSeed: 4242);
                // Public demo policy: every bundled floor is inspectable/playable. Geometry, actors,
                // objective and loot disclosure still come from the production Application service.
                return report with
                {
                    IsUnlocked = true,
                    IsAvailable = true,
                    RegenerationRemaining = TimeSpan.Zero,
                };
            }))
            .ToArray();

    private void ResetCanonicalFormation()
    {
        _formation.Clear();
        foreach (var unitId in _content.UnitDeploymentCosts.Keys) _formation[unitId] = 0;
        TrySetCanonical("monster.skeleton_warrior", 3);
        TrySetCanonical("monster.skeleton_archer", 3);
        if (_formation.Values.All(x => x == 0))
        {
            var first = _content.UnitDeploymentCosts.OrderBy(x => x.Key, StringComparer.Ordinal).First();
            _formation[first.Key] = Math.Max(1, DeploymentCapacity / first.Value);
        }
    }

    private void TrySetCanonical(string unitId, int count)
    {
        if (!_content.UnitDeploymentCosts.ContainsKey(unitId)) return;
        _formation[unitId] = count;
        while (UsedDeploymentCapacity > DeploymentCapacity && _formation[unitId] > 0) _formation[unitId]--;
    }

    private void EnsureNotRunning()
    {
        if (Simulation is { Outcome: InvasionOutcome.Running }) throw new InvalidOperationException("Invasion is running.");
        if (Simulation is not null) ReturnToBriefing();
    }
}
