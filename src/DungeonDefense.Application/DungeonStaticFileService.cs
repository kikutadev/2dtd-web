using DungeonDefense.Contracts;
using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed record DungeonStaticPreview(
    bool Success,
    string? Error,
    DungeonState State,
    IReadOnlyList<GridPoint> Route,
    int CapacityDelta,
    int RouteDelta,
    string SourceId,
    string SourceName);

public sealed class DungeonStaticFileService
{
    private readonly DungeonEditorSession _editor;
    private readonly MonsterRosterContent? _monsterRoster;
    private readonly HashSet<string> _knownContentIds;
    private readonly IReadOnlySet<string> _availableContentIds;
    private readonly string? _boardProfileId;

    public DungeonStaticFileService(
        DungeonEditorSession editor,
        MonsterRosterContent? monsterRoster = null,
        IReadOnlySet<string>? availableContentIds = null,
        string? boardProfileId = null)
    {
        _editor = editor;
        _monsterRoster = monsterRoster;
        _knownContentIds = BuildKnownContentIds(monsterRoster);
        _availableContentIds = availableContentIds ?? _knownContentIds;
        _boardProfileId = boardProfileId;
    }

    public DungeonBlueprintFile ExportBlueprint(string id, string name, string? description = null)
    {
        var state = _editor.Current;
        var profile = ResolveCurrentProfile(state);
        var passages = new List<StaticPointFile>();
        for (var y = 0; y < state.Height; y++)
        for (var x = 0; x < state.Width; x++)
        {
            var p = new GridPoint(x, y);
            if (state.GetTile(p) == TileKind.Passage && !state.IsIngress(p)) passages.Add(new StaticPointFile(x, y));
        }

        var rooms = state.Rooms
            .OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .Select(x =>
            {
                var def = DefenseSliceBuildCatalog.Rooms.Single(d => d.Id == x.DefinitionId);
                var rotated = def.Width != def.Height && x.Width == def.Height && x.Height == def.Width;
                return new BlueprintRoomFile(x.InstanceId, x.DefinitionId, x.Origin.X, x.Origin.Y, rotated);
            }).ToArray();
        var traps = state.Traps.OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .Select(x => new BlueprintPlacementFile(x.InstanceId, x.DefinitionId, x.Position.X, x.Position.Y)).ToArray();
        var guards = state.Guards.OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .Select(x => new BlueprintPlacementFile(x.InstanceId, x.DefinitionId, x.Position.X, x.Position.Y)).ToArray();
        var facilities = state.Facilities.OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .Select(x => new BlueprintPlacementFile(x.InstanceId, x.DefinitionId, x.Position.X, x.Position.Y)).ToArray();

        return new DungeonBlueprintFile(
            1,
            "dungeon_blueprint",
            id,
            name,
            description,
            new BoardProfileFile(profile.Id, profile.Width, profile.Height,
                new StaticPointFile(profile.Entrance.X, profile.Entrance.Y),
                new StaticPointFile(profile.Core.X, profile.Core.Y),
                profile.IngressCells.Select(x => new StaticPointFile(x.X, x.Y)).ToArray(),
                profile.EntranceTypeId),
            new BlueprintConstructionFile(passages, rooms, traps, guards, facilities));
    }

    public DungeonStaticPreview PreviewBlueprint(DungeonBlueprintFile file)
    {
        if (file.SchemaVersion != 1 || file.Kind != "dungeon_blueprint") return Failed(file.Id, file.Name, "Unsupported blueprint schema/kind.");
        DungeonBoardProfile profile;
        try { profile = DungeonBoardProfiles.Resolve(file.BoardProfile.Id); }
        catch (InvalidOperationException ex) { return Failed(file.Id, file.Name, ex.Message); }
        if (!DungeonBoardProfiles.Matches(profile, file.BoardProfile)) return Failed(file.Id, file.Name, "BOARD_PROFILE_MISMATCH: Board profile metadata does not match the registered profile.");
        var blueprintContent = file.Construction.Rooms.Select(x => x.DefinitionId)
            .Concat(file.Construction.Traps.Select(x => x.DefinitionId))
            .Concat(file.Construction.Guards.Select(x => x.DefinitionId))
            .Concat(file.Construction.Facilities.Select(x => x.DefinitionId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var contentError = ValidateContentAvailability(blueprintContent);
        if (contentError is not null) return Failed(file.Id, file.Name, contentError);
        var candidate = BuildBlueprintCandidate(profile, file, out var error);
        return candidate is null ? Failed(file.Id, file.Name, error!) : Success(file.Id, file.Name, candidate);
    }

    public DungeonStaticPreview ApplyBlueprint(DungeonBlueprintFile file)
    {
        var preview = PreviewBlueprint(file);
        if (!preview.Success) return preview;
        _editor.ReplaceCurrent(preview.State);
        return Success(file.Id, file.Name, _editor.Current);
    }

    public DungeonStaticPreview PreviewPattern(DungeonBuildPatternFile file)
    {
        if (file.SchemaVersion != 1 || file.Kind != "build_pattern") return Failed(file.Id, file.Name, "Unsupported pattern schema/kind.");
        var currentProfile = ResolveCurrentProfile(_editor.Current);
        var recipe = file.Recipes.SingleOrDefault(x => x.BoardProfile == currentProfile.Id);
        if (recipe is null) return Failed(file.Id, file.Name, $"Pattern has no recipe for board profile {currentProfile.Id}.");
        var contentError = ValidateContentAvailability(file.RequiredContent);
        if (contentError is not null) return Failed(file.Id, file.Name, contentError);

        var candidate = CreateConstructionBase(currentProfile);
        foreach (var commandFile in recipe.Commands)
        {
            SemanticCommand command;
            try { command = ToSemanticCommand(commandFile); }
            catch (InvalidOperationException ex) { return Failed(file.Id, file.Name, ex.Message); }
            var result = DefenseEditCommandService.Evaluate(candidate, command, _monsterRoster);
            if (!result.Success) return Failed(file.Id, file.Name, $"{command.Type}: {result.Error}");
            candidate = result.State;
        }
        var route = DungeonPathfinder.FindRoute(candidate);
        if (route.Count == 0) return Failed(file.Id, file.Name, "Pattern produced no entrance-to-core route.");
        return Success(file.Id, file.Name, candidate);
    }

    public DungeonStaticPreview ApplyPattern(DungeonBuildPatternFile file)
    {
        var preview = PreviewPattern(file);
        if (!preview.Success) return preview;
        _editor.ReplaceCurrent(preview.State);
        return Success(file.Id, file.Name, _editor.Current);
    }

    private DungeonState? BuildBlueprintCandidate(DungeonBoardProfile profile, DungeonBlueprintFile file, out string? error)
    {
        var passages = file.Construction.Passages.Select(x => new GridPoint(x.X, x.Y)).ToArray();
        var rooms = new List<PlacedRoom>();
        foreach (var source in file.Construction.Rooms)
        {
            var definition = DefenseSliceBuildCatalog.Rooms.SingleOrDefault(x => x.Id == source.DefinitionId);
            if (definition is null) { error = $"Unknown room definition: {source.DefinitionId}"; return null; }
            var width = source.Rotated ? definition.Height : definition.Width;
            var height = source.Rotated ? definition.Width : definition.Height;
            rooms.Add(new PlacedRoom(source.InstanceId, source.DefinitionId, new GridPoint(source.X, source.Y), width, height, definition.CapacityCost, definition.ResolveRoomConnections(source.Rotated), definition.GuardHpBonusPercent, definition.GuardDamageBonusPercent, definition.PoisonDurationBonusPercent, definition.ExecuteThresholdPercent, definition.ExecuteDamageBonusPercent, definition.SpellDurationBonusPercent, definition.PushMagnitudeBonus));
        }

        var traps = new List<PlacedTrap>();
        foreach (var source in file.Construction.Traps)
        {
            var definition = DefenseSliceBuildCatalog.Traps.SingleOrDefault(x => x.Id == source.DefinitionId);
            if (definition is null) { error = $"Unknown trap definition: {source.DefinitionId}"; return null; }
            traps.Add(new PlacedTrap(source.InstanceId, source.DefinitionId, new GridPoint(source.X, source.Y), definition.CapacityCost));
        }

        var guards = new List<PlacedGuard>();
        foreach (var source in file.Construction.Guards)
        {
            if (_monsterRoster is null || !_monsterRoster.TryMonster(source.DefinitionId, out var monster))
            {
                error = $"Unknown guard definition: {source.DefinitionId}";
                return null;
            }
            var definition = DefenseSliceBuildCatalog.ToGuardOption(monster);
            var position = new GridPoint(source.X, source.Y);
            // Guard-room affiliation is derived from the final room geometry. Keeping it derived avoids
            // duplicating room identity in static blueprint files while preserving runtime semantics.
            var roomId = rooms.SingleOrDefault(x => x.Contains(position))?.InstanceId;
            guards.Add(new PlacedGuard(source.InstanceId, source.DefinitionId, position, definition.CapacityCost, definition.GuardZoneRadius, roomId));
        }

        var facilities = new List<PlacedFacility>();
        foreach (var source in file.Construction.Facilities)
        {
            var definition = DefenseSliceBuildCatalog.Facilities.SingleOrDefault(x => x.Id == source.DefinitionId);
            if (definition is null) { error = $"Unknown facility definition: {source.DefinitionId}"; return null; }
            facilities.Add(new PlacedFacility(source.InstanceId, source.DefinitionId, new GridPoint(source.X, source.Y), definition.CapacityCost));
        }

        var result = DungeonSnapshotMaterializer.Materialize(CreateConstructionBase(profile), passages, rooms, traps, guards, facilities);
        error = result.Error;
        return result.Success ? result.State : null;
    }

    private DungeonState CreateConstructionBase(DungeonBoardProfile profile)
    {
        var current = _editor.Current;
        var currentProfile = ResolveCurrentProfile(current);
        if (!string.Equals(currentProfile.Id, profile.Id, StringComparison.Ordinal))
            return profile.CreateBase();

        var candidate = profile.CreateBase().WithCapacityMax(current.CapacityMax);
        var currentSectors = current.Sectors.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var sector in candidate.Sectors)
        {
            if (currentSectors.TryGetValue(sector.Id, out var existing))
                candidate.SetSectorUnlocked(sector.Id, existing.IsUnlocked);
        }
        return candidate;
    }

    private DungeonStaticPreview Success(string id, string name, DungeonState state)
    {
        var oldRoute = DungeonPathfinder.FindRoute(_editor.Current).Count;
        var route = DungeonPathfinder.FindRoute(state);
        return new DungeonStaticPreview(true, null, state.Clone(), route, state.UsedCapacity - _editor.Current.UsedCapacity, route.Count - oldRoute, id, name);
    }

    private DungeonStaticPreview Failed(string id, string name, string error)
        => new(false, error, _editor.Current.Clone(), DungeonPathfinder.FindRoute(_editor.Current), 0, 0, id, name);

    private static SemanticCommand ToSemanticCommand(PatternCommandFile command)
    {
        return command.Type switch
        {
            "dig_path" => new DigPathCommand(RequireCells(command)),
            "close_path" => new ClosePathCommand(RequireCells(command)),
            "place_room" => new PlaceRoomCommand(Require(command.InstanceId, "instance_id"), Require(command.DefinitionId, "definition_id"), Require(command.X, "x"), Require(command.Y, "y"), command.Rotated),
            "place_trap" => new PlaceTrapCommand(Require(command.InstanceId, "instance_id"), Require(command.DefinitionId, "definition_id"), Require(command.X, "x"), Require(command.Y, "y")),
            "place_guard" => new PlaceGuardCommand(Require(command.InstanceId, "instance_id"), Require(command.DefinitionId, "definition_id"), Require(command.X, "x"), Require(command.Y, "y")),
            "place_facility" => new PlaceFacilityCommand(Require(command.InstanceId, "instance_id"), Require(command.DefinitionId, "definition_id"), Require(command.X, "x"), Require(command.Y, "y")),
            _ => throw new InvalidOperationException($"Unsupported pattern command type: {command.Type}"),
        };
    }

    private static (int X, int Y)[] RequireCells(PatternCommandFile command)
        => command.Cells is { Count: > 0 } cells
            ? cells.Select(x => (x.X, x.Y)).ToArray()
            : throw new InvalidOperationException($"{command.Type} requires non-empty cells.");

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Pattern command requires {field}.") : value;
    private static int Require(int? value, string field)
        => value ?? throw new InvalidOperationException($"Pattern command requires {field}.");

    private DungeonBoardProfile ResolveCurrentProfile(DungeonState state)
    {
        if (_boardProfileId is null) return DungeonBoardProfiles.Resolve(state);
        var profile = DungeonBoardProfiles.Resolve(_boardProfileId);
        if (profile.Width != state.Width || profile.Height != state.Height || profile.Entrance != state.Entrance || profile.Core != state.Core)
            throw new InvalidOperationException($"Board state does not match explicit profile {_boardProfileId}.");
        return profile;
    }

    private string? ValidateContentAvailability(IEnumerable<string> ids)
    {
        var requested = ids.Distinct(StringComparer.Ordinal).ToArray();
        var unknown = requested.Where(x => !_knownContentIds.Contains(x)).ToArray();
        if (unknown.Length > 0) return $"UNKNOWN_CONTENT: {string.Join(", ", unknown)}";
        var locked = requested.Where(x => !_availableContentIds.Contains(x)).ToArray();
        return locked.Length > 0 ? $"CONTENT_LOCKED: {string.Join(", ", locked)}" : null;
    }

    private static HashSet<string> BuildKnownContentIds(MonsterRosterContent? monsterRoster)
        => DefenseSliceBuildCatalog.Rooms
            .Concat(DefenseSliceBuildCatalog.Traps)
            .Concat(monsterRoster is null ? [] : DefenseSliceBuildCatalog.Guards(monsterRoster))
            .Concat(DefenseSliceBuildCatalog.Facilities)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
}
