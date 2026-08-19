using DungeonDefense.Core;
using DungeonDefense.Contracts;

namespace DungeonDefense.Application;

public sealed class DefenseGameSession
{
    private PlayerDungeonState? _attemptDungeonSnapshot;

    public DefenseGameSession(
        DungeonState initialDungeon,
        MonsterRosterContent? monsterRoster = null,
        Func<string, bool>? isGuardAvailable = null)
        : this(PlayerDungeonState.FromSingleFloor(initialDungeon, ResolveProfileId(initialDungeon)), monsterRoster, isGuardAvailable)
    {
    }

    public DefenseGameSession(
        PlayerDungeonState initialDungeon,
        MonsterRosterContent? monsterRoster = null,
        Func<string, bool>? isGuardAvailable = null)
    {
        DungeonEditor = new PlayerDungeonEditorSession(initialDungeon);
        MonsterRoster = monsterRoster;
        EditorCommands = new DefenseEditCommandService(DungeonEditor, monsterRoster, isGuardAvailable);
        StaticFiles = CreateStaticFileService();
    }

    public MonsterRosterContent? MonsterRoster { get; private set; }
    public PlayerDungeonEditorSession DungeonEditor { get; }
    public PlayerDungeonState Dungeon => DungeonEditor.Current;
    public DungeonEditorSession Editor => DungeonEditor.SelectedEditor;
    public DefenseEditCommandService EditorCommands { get; }
    public DungeonStaticFileService StaticFiles { get; private set; }
    public DefenseSimulation? ActiveDefense { get; private set; }
    public PlayerDungeonState? AttemptDungeonSnapshot => _attemptDungeonSnapshot?.Clone();
    public DungeonState? AttemptSnapshot => _attemptDungeonSnapshot is null ? null : _attemptDungeonSnapshot.Floors[0].Board.Clone();

    public void ConfigureMonsterRoster(MonsterRosterContent monsterRoster, Func<string, bool>? isGuardAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(monsterRoster);
        MonsterRoster = monsterRoster;
        EditorCommands.ConfigureMonsterRoster(monsterRoster, isGuardAvailable);
        StaticFiles = CreateStaticFileService();
    }

    public void RefreshMonsterAvailability()
    {
        if (MonsterRoster is not null) StaticFiles = CreateStaticFileService();
    }

    public void SelectFloor(string floorId)
    {
        DungeonEditor.SelectFloor(DungeonFloorId.Parse(floorId));
        StaticFiles = CreateStaticFileService();
    }

    public void UnlockFloor(string floorId, string boardProfileId)
    {
        var profile = DungeonBoardProfiles.Resolve(boardProfileId);
        DungeonEditor.UnlockFloor(DungeonFloorId.Parse(floorId), profile.Id, profile.CreateBase());
        StaticFiles = CreateStaticFileService();
    }

    public DefenseSimulation StartDefense(DefenseContent content, int seed)
    {
        if (ActiveDefense is { Outcome: DefenseOutcome.Running }) throw new InvalidOperationException("Defense already running.");
        var validation = DefenseStartValidator.Validate(Dungeon, content);
        if (!validation.Success) throw new InvalidOperationException($"Defense cannot start: {string.Join(" | ", validation.Errors)}");
        _attemptDungeonSnapshot = Dungeon.Clone();
        ActiveDefense = new DefenseSimulation(_attemptDungeonSnapshot, content, seed);
        return ActiveDefense;
    }

    public DefenseAutoBattleController CreateAutoBattleController()
    {
        if (ActiveDefense is null || _attemptDungeonSnapshot is null) throw new InvalidOperationException("Defense must be started before creating Auto Battle.");
        return new DefenseAutoBattleController(_attemptDungeonSnapshot);
    }

    public DefenseOutcome RunActiveDefenseToEnd(int maxTicks = 50_000)
    {
        if (ActiveDefense is null) throw new InvalidOperationException("No active defense.");
        return ActiveDefense.RunToEnd(maxTicks);
    }

    public void ReturnToPreparation()
    {
        if (ActiveDefense is null || ActiveDefense.Outcome == DefenseOutcome.Running)
            throw new InvalidOperationException("Defense must be completed first.");
        ActiveDefense = null;
        _attemptDungeonSnapshot = null;
    }

    private DungeonStaticFileService CreateStaticFileService()
    {
        var floor = Dungeon.GetFloor(DungeonEditor.SelectedFloorId);
        var availableIds = DefenseSliceBuildCatalog.Rooms
            .Concat(DefenseSliceBuildCatalog.Traps)
            .Concat(EditorCommands.AvailableGuards)
            .Concat(DefenseSliceBuildCatalog.Facilities)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        return new DungeonStaticFileService(Editor, MonsterRoster, availableIds, floor.BoardProfileId);
    }

    private static string ResolveProfileId(DungeonState state)
    {
        try { return DungeonBoardProfiles.Resolve(state).Id; }
        catch (InvalidOperationException) { return "legacy.single"; }
    }
}

public static class DefenseSliceScenario
{
    public static DefenseGameSession CreateSession() => new(DungeonFactory.CreateDefenseSliceDungeon());
    public static DefenseGameSession CreateSession(MonsterRosterContent monsterRoster) => new(DungeonFactory.CreateDefenseSliceDungeon(), monsterRoster);
    public static DefenseGameSession CreateSession(string boardProfileId) => new(DungeonBoardProfiles.Resolve(boardProfileId).CreateBase());
    public static DefenseGameSession CreateSession(string boardProfileId, MonsterRosterContent monsterRoster) => new(DungeonBoardProfiles.Resolve(boardProfileId).CreateBase(), monsterRoster);

    public static DefenseGameSession CreateMultiFloorSession(params string[] boardProfileIds)
        => CreateMultiFloorSessionCore(null, boardProfileIds);

    public static DefenseGameSession CreateMultiFloorSession(MonsterRosterContent monsterRoster, params string[] boardProfileIds)
        => CreateMultiFloorSessionCore(monsterRoster, boardProfileIds);

    private static DefenseGameSession CreateMultiFloorSessionCore(MonsterRosterContent? monsterRoster, params string[] boardProfileIds)
    {
        if (boardProfileIds.Length == 0) throw new ArgumentException("At least one board profile is required.", nameof(boardProfileIds));
        var first = DungeonBoardProfiles.Resolve(boardProfileIds[0]);
        var dungeon = PlayerDungeonState.FromSingleFloor(first.CreateBase(), first.Id);
        for (var i = 1; i < boardProfileIds.Length; i++)
        {
            var profile = DungeonBoardProfiles.Resolve(boardProfileIds[i]);
            var floorId = new DungeonFloorId($"floor.{i + 1:D3}");
            dungeon = dungeon.UnlockFloor(floorId, profile.Id, profile.CreateBase());
        }
        return new DefenseGameSession(dungeon, monsterRoster);
    }

    public static void ConfigureBoardDemonstration(DefenseGameSession session, string boardProfileId)
        => ConfigureBoardDemonstration(session, boardProfileId, DungeonFloorId.First.Value);

    public static void ConfigureBoardDemonstration(DefenseGameSession session, string boardProfileId, string floorId)
    {
        if (boardProfileId == DungeonBoardProfiles.DefenseSliceId)
        {
            ConfigureSuccessfulDefense(session, floorId);
            return;
        }
        if (boardProfileId == DungeonBoardProfiles.PillaredCryptId)
        {
            Apply(session.EditorCommands.Execute(new PlaceTrapCommand("T-PIL-SPK", DefenseSliceBuildCatalog.SpikeTrap.Id, 3, 2, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceTrapCommand("T-PIL-PSN", DefenseSliceBuildCatalog.PoisonTrap.Id, 6, 4, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceGuardCommand("G-PIL-SW", MonsterIds.SkeletonWarrior, 4, 4, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceGuardCommand("G-PIL-SA", MonsterIds.SkeletonArcher, 9, 4, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceFacilityCommand("F-PIL-ARR", DefenseSliceBuildCatalog.ArrowSlit.Id, 3, 1, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceFacilityCommand("F-PIL-EYE", DefenseSliceBuildCatalog.MagicEye.Id, 7, 5, floorId)));
            return;
        }
        if (boardProfileId == DungeonBoardProfiles.ManaFaultId)
        {
            Apply(session.EditorCommands.Execute(new PlaceTrapCommand("T-MAN-SPK", DefenseSliceBuildCatalog.SpikeTrap.Id, 3, 5, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceTrapCommand("T-MAN-PSN", DefenseSliceBuildCatalog.PoisonTrap.Id, 5, 4, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceGuardCommand("G-MAN-SW", MonsterIds.SkeletonWarrior, 5, 3, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceGuardCommand("G-MAN-SA", MonsterIds.SkeletonArcher, 9, 2, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceFacilityCommand("F-MAN-ARR", DefenseSliceBuildCatalog.ArrowSlit.Id, 4, 2, floorId)));
            Apply(session.EditorCommands.Execute(new PlaceFacilityCommand("F-MAN-EYE", DefenseSliceBuildCatalog.MagicEye.Id, 8, 1, floorId)));
            return;
        }
        throw new InvalidOperationException($"No demonstration recipe for board profile {boardProfileId}.");
    }

    public static void ConfigureSuccessfulDefense(DefenseGameSession session)
        => ConfigureSuccessfulDefense(session, DungeonFloorId.First.Value);

    public static void ConfigureSuccessfulDefense(DefenseGameSession session, string floorId)
    {
        Apply(session.EditorCommands.Execute(new DigPathCommand([(3,2), (4,2), (5,2), (6,2), (7,2)], floorId)));
        Apply(session.EditorCommands.Execute(new ClosePathCommand([(4,3), (5,3), (6,3)], floorId)));
        Apply(session.EditorCommands.Execute(new PlaceTrapCommand("T-SPK", DefenseSliceBuildCatalog.SpikeTrap.Id, 4, 2, floorId)));
        Apply(session.EditorCommands.Execute(new PlaceTrapCommand("T-PSN", DefenseSliceBuildCatalog.PoisonTrap.Id, 6, 2, floorId)));
        Apply(session.EditorCommands.Execute(new PlaceGuardCommand("G-SW", MonsterIds.SkeletonWarrior, 5, 2, floorId)));
        Apply(session.EditorCommands.Execute(new PlaceGuardCommand("G-SA", MonsterIds.SkeletonArcher, 8, 3, floorId)));
        Apply(session.EditorCommands.Execute(new PlaceFacilityCommand("F-ARR", DefenseSliceBuildCatalog.ArrowSlit.Id, 5, 1, floorId)));
        Apply(session.EditorCommands.Execute(new PlaceFacilityCommand("F-EYE", DefenseSliceBuildCatalog.MagicEye.Id, 8, 2, floorId)));
    }

    private static void Apply(SemanticEditResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Error);
    }
}
