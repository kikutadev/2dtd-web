using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

public static class SemanticCommandFileCodec
{
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly SemanticCommandJsonContext Context = new(Options);

    public static SemanticCommandSequenceFile Load(string path)
        => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static SemanticCommandSequenceFile Parse(string json)
    {
        SemanticCommandSequenceFile file;
        try
        {
            file = JsonSerializer.Deserialize(json, Context.SemanticCommandSequenceFile)
                ?? throw new InvalidDataException("Empty semantic command sequence JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid semantic command sequence JSON: {ex.Message}", ex);
        }
        Validate(file);
        return file;
    }

    public static string Serialize(SemanticCommandSequenceFile file)
    {
        Validate(file);
        var canonical = file with
        {
            Commands = file.Commands.Select(Canonicalize).ToArray(),
        };
        return JsonSerializer.Serialize(canonical, Context.SemanticCommandSequenceFile)
            .Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
    }

    public static void Save(string path, SemanticCommandSequenceFile file)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllText(full, Serialize(file), new UTF8Encoding(false));
    }

    public static IReadOnlyList<SemanticCommand> ToCommands(SemanticCommandSequenceFile file)
    {
        Validate(file);
        return file.Commands.Select(ToCommand).ToArray();
    }

    public static SemanticCommandSequenceFile FromCommands(string id, int defaultSeed, IEnumerable<SemanticCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Sequence ID is required.", nameof(id));
        return new SemanticCommandSequenceFile(SchemaVersion, "semantic_command_sequence", id, defaultSeed, commands.Select(ToEntry).ToArray());
    }

    private static SemanticCommand ToCommand(SemanticCommandEntryFile e)
    {
        var floor = string.IsNullOrWhiteSpace(e.FloorId) ? "floor.001" : e.FloorId!;
        return e.Type switch
        {
            "dig_path" => new DigPathCommand(RequireCells(e), floor),
            "close_path" => new ClosePathCommand(RequireCells(e), floor),
            "place_room" => new PlaceRoomCommand(Require(e.InstanceId, "instance_id"), Require(e.DefinitionId, "definition_id"), Require(e.X, "x"), Require(e.Y, "y"), e.Rotated, floor),
            "remove_room" => new RemoveRoomCommand(Require(e.InstanceId, "instance_id"), floor),
            "rotate_room" => new RotateRoomCommand(Require(e.InstanceId, "instance_id"), floor),
            "place_trap" => new PlaceTrapCommand(Require(e.InstanceId, "instance_id"), Require(e.DefinitionId, "definition_id"), Require(e.X, "x"), Require(e.Y, "y"), floor),
            "remove_trap" => new RemoveTrapCommand(Require(e.InstanceId, "instance_id"), floor),
            "place_guard" => new PlaceGuardCommand(Require(e.InstanceId, "instance_id"), Require(e.DefinitionId, "definition_id"), Require(e.X, "x"), Require(e.Y, "y"), floor),
            "remove_guard" => new RemoveGuardCommand(Require(e.InstanceId, "instance_id"), floor),
            "place_facility" => new PlaceFacilityCommand(Require(e.InstanceId, "instance_id"), Require(e.DefinitionId, "definition_id"), Require(e.X, "x"), Require(e.Y, "y"), floor),
            "remove_facility" => new RemoveFacilityCommand(Require(e.InstanceId, "instance_id"), floor),
            "undo_edit" => new UndoEditCommand(floor),
            "redo_edit" => new RedoEditCommand(floor),
            "start_defense" => new StartDefenseCommand(e.Seed ?? 0),
            "cast_defense_spell" => new CastDefenseSpellCommand(Require(e.SpellId, "spell_id"), Require(e.X, "x"), Require(e.Y, "y"), e.TargetEntityId),
            "research" => new CompleteResearchCommand(Require(e.ResearchId, "research_id")),
            "observe_realtime" => new ObserveRealtimeCommand(RequireUtc(e.NowUtc)),
            "collect_production" => new CollectProductionCommand(),
            "start_invasion" => new StartInvasionCommand(
                Require(e.LocationId, "location_id"),
                Require(e.FloorId, "floor_id"),
                RequireFormation(e.Formation),
                e.Seed ?? 0),
            "deploy_group" => new DeployGroupCommand(Require(e.UnitId, "unit_id"), RequirePositive(e.Count, "count")),
            "cast_invasion_spell" => new CastInvasionSpellCommand(Require(e.SpellId, "spell_id")),
            "retreat" => new RetreatInvasionCommand(),
            "return_from_invasion" => new ReturnFromInvasionCommand(),
            "advance_ticks" => new AdvanceTicksCommand(RequirePositive(e.Ticks, "ticks")),
            _ => throw new InvalidDataException($"Unsupported semantic command type: {e.Type}."),
        };
    }

    private static SemanticCommandEntryFile ToEntry(SemanticCommand command)
        => command switch
        {
            DigPathCommand x => new(x.Type, x.FloorId, x.Cells.Select(p => new SemanticCommandPointFile(p.X, p.Y)).ToArray()),
            ClosePathCommand x => new(x.Type, x.FloorId, x.Cells.Select(p => new SemanticCommandPointFile(p.X, p.Y)).ToArray()),
            PlaceRoomCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId, DefinitionId: x.DefinitionId, X: x.X, Y: x.Y, Rotated: x.Rotated),
            RemoveRoomCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId),
            RotateRoomCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId),
            PlaceTrapCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId, DefinitionId: x.DefinitionId, X: x.X, Y: x.Y),
            RemoveTrapCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId),
            PlaceGuardCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId, DefinitionId: x.DefinitionId, X: x.X, Y: x.Y),
            RemoveGuardCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId),
            PlaceFacilityCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId, DefinitionId: x.DefinitionId, X: x.X, Y: x.Y),
            RemoveFacilityCommand x => new(x.Type, x.FloorId, InstanceId: x.InstanceId),
            UndoEditCommand x => new(x.Type, x.FloorId),
            RedoEditCommand x => new(x.Type, x.FloorId),
            StartDefenseCommand x => new(x.Type, Seed: x.Seed),
            CastDefenseSpellCommand x => new(x.Type, SpellId: x.SpellId, X: x.X, Y: x.Y, TargetEntityId: x.TargetEntityId),
            CompleteResearchCommand x => new(x.Type, ResearchId: x.ResearchId),
            ObserveRealtimeCommand x => new(x.Type, NowUtc: x.NowUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            CollectProductionCommand x => new(x.Type),
            StartInvasionCommand x => new(
                x.Type,
                FloorId: x.FloorId,
                Seed: x.Seed,
                LocationId: x.LocationId,
                Formation: x.Formation.Select(f => new SemanticFormationUnitFile(f.UnitId, f.Count)).ToArray()),
            DeployGroupCommand x => new(x.Type, UnitId: x.UnitId, Count: x.Count),
            CastInvasionSpellCommand x => new(x.Type, SpellId: x.SpellId),
            RetreatInvasionCommand x => new(x.Type),
            ReturnFromInvasionCommand x => new(x.Type),
            AdvanceTicksCommand x => new(x.Type, Ticks: x.Ticks),
            _ => throw new InvalidDataException($"Unsupported semantic command type: {command.Type}."),
        };

    private static SemanticCommandEntryFile Canonicalize(SemanticCommandEntryFile e)
        => ToEntry(ToCommand(e));

    private static (int X, int Y)[] RequireCells(SemanticCommandEntryFile e)
        => e.Cells is { Count: > 0 } cells ? cells.Select(x => (x.X, x.Y)).ToArray() : throw new InvalidDataException($"{e.Type} requires non-empty cells.");
    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"Semantic command requires {field}.") : value;
    private static int Require(int? value, string field)
        => value ?? throw new InvalidDataException($"Semantic command requires {field}.");

    private static int RequirePositive(int? value, string field)
        => value is > 0 ? value.Value : throw new InvalidDataException($"Semantic command requires positive {field}.");

    private static SemanticFormationUnit[] RequireFormation(IReadOnlyList<SemanticFormationUnitFile>? formation)
    {
        if (formation is not { Count: > 0 }) throw new InvalidDataException("start_invasion requires non-empty formation.");
        if (formation.Any(x => string.IsNullOrWhiteSpace(x.UnitId) || x.Count <= 0))
            throw new InvalidDataException("start_invasion formation entries require unit_id and positive count.");
        if (formation.GroupBy(x => x.UnitId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("start_invasion formation unit IDs must be unique.");
        return formation.Select(x => new SemanticFormationUnit(x.UnitId, x.Count)).ToArray();
    }

    private static DateTimeOffset RequireUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("observe_realtime requires now_utc.");
        if (!DateTimeOffset.TryParseExact(value, "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidDataException("observe_realtime now_utc must use round-trip ISO-8601 format.");
        if (parsed.Offset != TimeSpan.Zero)
            throw new InvalidDataException("observe_realtime now_utc must be UTC (+00:00).");
        return parsed;
    }

    private static void Validate(SemanticCommandSequenceFile file)
    {
        if (file.SchemaVersion != SchemaVersion || !string.Equals(file.Kind, "semantic_command_sequence", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported semantic command sequence schema/kind.");
        if (string.IsNullOrWhiteSpace(file.Id)) throw new InvalidDataException("Semantic command sequence id is required.");
        ArgumentNullException.ThrowIfNull(file.Commands);
        if (file.Commands.Count == 0) throw new InvalidDataException("Semantic command sequence must contain at least one command.");
        foreach (var entry in file.Commands) _ = ToCommand(entry);
    }
}
