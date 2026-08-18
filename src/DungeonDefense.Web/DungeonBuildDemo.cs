using DungeonDefense.Application;
using DungeonDefense.Contracts;
using DungeonDefense.Core;
using DungeonDefense.Presentation;

namespace DungeonDefense.Web;

/// <summary>
/// Thin Web host adapter for the production dungeon editor/session API.
/// It intentionally owns no duplicate placement rules: every preview, placement,
/// removal, validation, and defense start goes through DungeonDefense.Application.
/// </summary>
internal sealed class DungeonBuildDemo
{
    private readonly DefenseContent _content;
    private int _nextInstanceNumber = 1;

    public DungeonBuildDemo(DefenseContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        Reset();
    }

    public DungeonBuildDemo(DefenseContent content, PlayerDungeonSaveFile save)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentNullException.ThrowIfNull(save);
        var imported = PlayerDungeonSaveService.Import(save);
        Session = new DefenseGameSession(imported.Dungeon);
        Session.SelectFloor(imported.SelectedFloorId.Value);
        _nextInstanceNumber = NextInstanceNumber(Session.Editor.Current);
    }

    public DefenseGameSession Session { get; private set; } = null!;

    public DungeonState Board => Session.Editor.Current;

    public IReadOnlyList<BuildOption> BuildOptions { get; } =
    [
        .. DefenseSliceBuildCatalog.Rooms,
        .. DefenseSliceBuildCatalog.Traps,
        .. DefenseSliceBuildCatalog.Guards,
        .. DefenseSliceBuildCatalog.Facilities,
    ];

    public DefenseStartValidationResult StartValidation
        => DefenseStartValidator.Validate(Session.Dungeon, _content);

    public DungeonBuildVisualState ProductState
        => DungeonBuildProductPresentation.Build(Board, _content, BuildOptions, StartValidation);

    /// <summary>Starts again from the production defense-slice board, preserving its valid entrance-to-core route.</summary>
    public void Reset()
    {
        Session = DefenseSliceScenario.CreateSession();
        _nextInstanceNumber = 1;
    }

    /// <summary>Evaluates a one-cell passage extension through the production editor rules.</summary>
    public SemanticEditResult PreviewDig(int x, int y)
        => Session.EditorCommands.Preview(new DigPathCommand([(x, y)]));

    /// <summary>Digs one passage cell through the production semantic command path.</summary>
    public SemanticEditResult Dig(int x, int y)
        => Session.EditorCommands.Execute(new DigPathCommand([(x, y)]));

    public SemanticEditResult Undo()
        => Session.EditorCommands.Execute(new UndoEditCommand());

    public SemanticEditResult Redo()
        => Session.EditorCommands.Execute(new RedoEditCommand());

    /// <summary>Evaluates a candidate placement through the production semantic command service without mutating the board.</summary>
    public SemanticEditResult PreviewPlacement(string definitionId, int x, int y, bool rotated = false)
    {
        var option = ResolveBuildOption(definitionId);
        var command = CreatePlaceCommand(option, "WEB-PREVIEW", x, y, rotated);
        return Session.EditorCommands.Preview(command);
    }

    /// <summary>Places one build item using the same semantic command and validation path used by the native host.</summary>
    public SemanticEditResult Place(string definitionId, int x, int y, bool rotated = false)
    {
        var option = ResolveBuildOption(definitionId);
        var instanceId = $"WEB-{option.Kind.ToString().ToUpperInvariant()}-{_nextInstanceNumber:D3}";
        var result = Session.EditorCommands.Execute(CreatePlaceCommand(option, instanceId, x, y, rotated));
        if (result.Success) _nextInstanceNumber++;
        return result;
    }

    /// <summary>Removes a previously placed item by its production build kind and instance id.</summary>
    public SemanticEditResult Remove(BuildKind kind, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance id is required.", nameof(instanceId));

        SemanticCommand command = kind switch
        {
            BuildKind.Room => new RemoveRoomCommand(instanceId),
            BuildKind.Trap => new RemoveTrapCommand(instanceId),
            BuildKind.Guard => new RemoveGuardCommand(instanceId),
            BuildKind.Facility => new RemoveFacilityCommand(instanceId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown build kind."),
        };
        return Session.EditorCommands.Execute(command);
    }

    /// <summary>Starts the real deterministic defense simulation from the current edited board.</summary>
    public DefenseSimulation StartDefense(int seed = 20260815)
        => Session.StartDefense(_content, seed);

    /// <summary>Returns a completed attempt to preparation so the same session can be edited and replayed.</summary>
    public void ReturnToBuild()
        => Session.ReturnToPreparation();

    public PlayerDungeonSaveFile ExportSave()
        => PlayerDungeonSaveService.Export(Session.Dungeon, Session.DungeonEditor.SelectedFloorId);

    private static int NextInstanceNumber(DungeonState board)
    {
        var ids = board.Rooms.Select(x => x.InstanceId)
            .Concat(board.Traps.Select(x => x.InstanceId))
            .Concat(board.Guards.Select(x => x.InstanceId))
            .Concat(board.Facilities.Select(x => x.InstanceId));
        var max = 0;
        foreach (var id in ids)
        {
            var suffix = id.Split('-').LastOrDefault();
            if (int.TryParse(suffix, out var value)) max = Math.Max(max, value);
        }
        return max + 1;
    }

    private BuildOption ResolveBuildOption(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("Definition id is required.", nameof(definitionId));

        return BuildOptions.SingleOrDefault(x => string.Equals(x.Id, definitionId, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(definitionId), definitionId, "Unknown build definition.");
    }

    private static SemanticCommand CreatePlaceCommand(BuildOption option, string instanceId, int x, int y, bool rotated)
        => option.Kind switch
        {
            BuildKind.Room => new PlaceRoomCommand(instanceId, option.Id, x, y, rotated),
            BuildKind.Trap => new PlaceTrapCommand(instanceId, option.Id, x, y),
            BuildKind.Guard => new PlaceGuardCommand(instanceId, option.Id, x, y),
            BuildKind.Facility => new PlaceFacilityCommand(instanceId, option.Id, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(option), option.Kind, "Unknown build kind."),
        };
}
