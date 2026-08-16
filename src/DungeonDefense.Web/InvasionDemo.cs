using DungeonDefense.Application;
using DungeonDefense.Core;
using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>
/// Thin browser adapter around the production section-based invasion simulation.
/// It owns browser timing and formation UI state, but no combat/deployment rules.
/// </summary>
internal sealed class InvasionDemo
{
    private const double SimulationStepSeconds = 1.0 / InvasionSimulation.TicksPerSecond;
    private readonly DefenseContent _defenseContent;
    private readonly InvasionContent _content;
    private readonly Dictionary<string, int> _formation = new(StringComparer.Ordinal);
    private double _simulationAccumulatorSeconds;

    public InvasionDemo(DefenseContent defenseContent, InvasionContent content)
    {
        _defenseContent = defenseContent ?? throw new ArgumentNullException(nameof(defenseContent));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        SelectedLocationId = content.Locations[0].Id;
        SelectedFloorId = content.Locations[0].Floors[0].Id;
        ResetCanonicalFormation();
    }

    public InvasionContent Content => _content;
    public IReadOnlyList<InvasionScoutReport> ScoutReports => BuildDemoScoutReports();
    public InvasionLocationListVisualState LocationsState => InvasionPreparationPresentation.BuildLocations(_content, ScoutReports);
    public InvasionScoutVisualState ScoutState => InvasionPreparationPresentation.BuildScout(SelectedLocationId, ScoutReports);
    public InvasionScoutReport SelectedScoutReport => ScoutReports.Single(x => string.Equals(x.LocationId, SelectedLocationId, StringComparison.Ordinal) && string.Equals(x.FloorId, SelectedFloorId, StringComparison.Ordinal));
    public InvasionFormationVisualState FormationState => InvasionPreparationPresentation.BuildFormation(_content, SelectedScoutReport, _formation);
    public InvasionSimulation? Simulation { get; private set; }
    public InvasionBattleVisualState? VisualState => Simulation is { } simulation ? InvasionBattlePresentation.Build(simulation) : null;
    public InvasionResultVisualState? ResultState => Simulation is { Outcome: not InvasionOutcome.Running } simulation ? InvasionResultPresentation.Build(SelectedLocationId, simulation) : null;
    public string SelectedLocationId { get; private set; }
    public string SelectedFloorId { get; private set; }
    public InvasionLocationDefinition SelectedLocation => _content.Location(SelectedLocationId);
    public InvasionFloorDefinition SelectedFloor => _content.Floor(SelectedLocationId, SelectedFloorId);
    public IReadOnlyDictionary<string, int> Formation => _formation;
    public IReadOnlyList<string> FormationUnitIds => _content.UnitDeploymentCosts.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    public int UsedDeploymentCapacity => _formation.Sum(x => checked(x.Value * _content.UnitDeploymentCosts[x.Key]));
    public int DeploymentCapacity => _content.DeploymentCapacity;
    public bool CanStart => Simulation is null && FormationState.CanStart;
    public ResourceBundle VisibleSectionLoot => SelectedFloor.Sections.Aggregate(ResourceBundle.Zero, (sum, x) => sum.Add(x.Loot));

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
        if (!_content.UnitDeploymentCosts.ContainsKey(unitId)) throw new InvalidOperationException($"Unknown invasion formation unit: {unitId}");
        var current = _formation.GetValueOrDefault(unitId);
        var candidate = Math.Max(0, current + delta);
        var prior = current;
        _formation[unitId] = candidate;
        if (UsedDeploymentCapacity > DeploymentCapacity)
        {
            _formation[unitId] = prior;
            return false;
        }
        return true;
    }

    public void Start(int seed = 4242)
    {
        EnsureNotRunning();
        if (!CanStart) throw new InvalidOperationException("Invasion formation is empty or exceeds Deployment Capacity.");
        var formation = _formation.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new InvasionFormationEntry(x.Key, x.Value)).ToArray();
        Simulation = new InvasionSimulation(SelectedFloor, _defenseContent.Units, formation, _content, seed);
        _simulationAccumulatorSeconds = 0;
    }

    public void Deploy(string unitId, int count = 1) => RequireSimulation().Deploy(unitId, count);
    public void DeployAllRemaining() => RequireSimulation().DeployAllRemaining();
    public bool CastSupportSpell(string spellId) => RequireSimulation().CastSupportSpell(spellId);
    public void RequestRetreat() => RequireSimulation().RequestRetreat();

    public bool AdvanceFrame(double deltaSeconds, int speed, bool advanceSimulation)
    {
        var simulation = Simulation;
        if (!advanceSimulation || simulation is not { Outcome: InvasionOutcome.Running }) return false;
        var normalizedSpeed = Math.Clamp(speed, 1, 3);
        var renderDelta = Math.Clamp(deltaSeconds, 0.0, 0.10);
        _simulationAccumulatorSeconds += renderDelta * normalizedSpeed;
        var stepped = false;
        while (_simulationAccumulatorSeconds >= SimulationStepSeconds && simulation.Outcome == InvasionOutcome.Running)
        {
            _simulationAccumulatorSeconds -= SimulationStepSeconds;
            simulation.Step();
            stepped = true;
        }
        return stepped;
    }

    public void ReturnToBriefing()
    {
        if (Simulation is { Outcome: InvasionOutcome.Running }) throw new InvalidOperationException("Invasion is still running.");
        Simulation = null;
        _simulationAccumulatorSeconds = 0;
    }

    public IReadOnlyList<InvasionEvent> RecentEvents(int count = 6)
        => Simulation?.Events.TakeLast(count).Reverse().ToArray() ?? [];

    private InvasionScoutReport[] BuildDemoScoutReports()
    {
        // The public vertical slice intentionally exposes all bundled demo floors. Availability is a host/demo policy only;
        // objective, threat, loot and formation semantics still come from production content and shared Presentation.
        return _content.Locations
            .SelectMany(location => location.Floors.Select(floor => new InvasionScoutReport(
                location.Id,
                location.Category,
                floor.Id,
                floor.Depth,
                floor.Objective,
                floor.Sections.Count,
                floor.ThreatTags.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                floor.Sections.Aggregate(ResourceBundle.Zero, (sum, section) => sum.Add(section.Loot)),
                floor.FirstClearReward,
                IsFirstClear: true,
                IsUnlocked: true,
                IsAvailable: true,
                RegenerationRemaining: TimeSpan.Zero)))
            .ToArray();
    }

    private void ResetCanonicalFormation()
    {
        _formation.Clear();
        foreach (var unitId in _content.UnitDeploymentCosts.Keys) _formation[unitId] = 0;
        // Mirrors the production successful black-iron campaign fixture where possible: 3 warriors + 3 archers = 12 capacity.
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

    private InvasionSimulation RequireSimulation()
        => Simulation ?? throw new InvalidOperationException("Invasion has not started.");

    private void EnsureNotRunning()
    {
        if (Simulation is { Outcome: InvasionOutcome.Running }) throw new InvalidOperationException("Invasion is running.");
        if (Simulation is not null) ReturnToBriefing();
    }
}
