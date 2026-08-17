using System.Text;
using System.Text.Json;
using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

public static class DungeonStaticFileCodec
{
    public const int MaxFileBytes = 1_048_576;
    private const int MaxStringLength = 2_048;
    private const int MaxConstructionEntries = 65_536;
    private const int MaxPatternCommands = 4_096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly DungeonStaticJsonContext JsonContext = new(JsonOptions);

    public static DungeonBlueprintFile LoadBlueprint(string path)
        => ParseBlueprint(ReadTextBounded(path));

    public static DungeonBuildPatternFile LoadPattern(string path)
        => ParsePattern(ReadTextBounded(path));

    public static DungeonBlueprintFile ParseBlueprint(string json)
    {
        var file = DeserializeBlueprint(json);
        ValidateBlueprint(file);
        return file;
    }

    public static DungeonBuildPatternFile ParsePattern(string json)
    {
        var file = DeserializePattern(json);
        ValidatePattern(file);
        return file;
    }

    public static string SerializeBlueprint(DungeonBlueprintFile file)
    {
        ValidateBlueprint(file);
        var canonical = file with
        {
            Construction = file.Construction with
            {
                Passages = file.Construction.Passages.OrderBy(x => x.Y).ThenBy(x => x.X).ToArray(),
                Rooms = file.Construction.Rooms.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray(),
                Traps = file.Construction.Traps.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray(),
                Guards = file.Construction.Guards.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray(),
                Facilities = file.Construction.Facilities.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray(),
            },
        };
        return SerializeCanonical(canonical, JsonContext.DungeonBlueprintFile);
    }

    public static string SerializePattern(DungeonBuildPatternFile file)
    {
        ValidatePattern(file);
        var canonical = file with
        {
            Tags = file.Tags.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            RequiredContent = file.RequiredContent.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            Recipes = file.Recipes.OrderBy(x => x.BoardProfile, StringComparer.Ordinal).ToArray(),
        };
        return SerializeCanonical(canonical, JsonContext.DungeonBuildPatternFile);
    }

    public static void SaveBlueprint(string path, DungeonBlueprintFile file)
        => WriteUtf8NoBom(path, SerializeBlueprint(file));

    public static void SavePattern(string path, DungeonBuildPatternFile file)
        => WriteUtf8NoBom(path, SerializePattern(file));

    public static string ReadKind(string path)
    {
        using var document = JsonDocument.Parse(ReadTextBounded(path));
        if (!document.RootElement.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Static dungeon file is missing string field 'kind'.");
        return kind.GetString()!;
    }

    private static string ReadTextBounded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Static dungeon file not found.", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException($"Static dungeon file exceeds {MaxFileBytes} bytes.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static DungeonBlueprintFile DeserializeBlueprint(string json)
        => Deserialize(json, "dungeon blueprint", JsonContext.DungeonBlueprintFile);

    private static DungeonBuildPatternFile DeserializePattern(string json)
        => Deserialize(json, "build pattern", JsonContext.DungeonBuildPatternFile);

    private static T Deserialize<T>(string json, string label, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo)
                   ?? throw new InvalidDataException($"Empty {label} JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid {label} JSON: {ex.Message}", ex);
        }
    }

    private static string SerializeCanonical<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private static void WriteUtf8NoBom(string path, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void ValidateBlueprint(DungeonBlueprintFile file)
    {
        RequireCommon(file.SchemaVersion, file.Kind, "dungeon_blueprint", file.Id, file.Name);
        ArgumentNullException.ThrowIfNull(file.BoardProfile);
        ArgumentNullException.ThrowIfNull(file.Construction);
        RequireId(file.BoardProfile.Id, "board_profile.id");
        if (file.BoardProfile.Width <= 0 || file.BoardProfile.Height <= 0) throw new InvalidDataException("Board dimensions must be positive.");
        ArgumentNullException.ThrowIfNull(file.BoardProfile.Entrance);
        ArgumentNullException.ThrowIfNull(file.BoardProfile.Core);
        ArgumentNullException.ThrowIfNull(file.Construction.Passages);
        ArgumentNullException.ThrowIfNull(file.Construction.Rooms);
        ArgumentNullException.ThrowIfNull(file.Construction.Traps);
        ArgumentNullException.ThrowIfNull(file.Construction.Guards);
        ArgumentNullException.ThrowIfNull(file.Construction.Facilities);
        var constructionCount = file.Construction.Passages.Count + file.Construction.Rooms.Count + file.Construction.Traps.Count + file.Construction.Guards.Count + file.Construction.Facilities.Count;
        if (constructionCount > MaxConstructionEntries) throw new InvalidDataException("Blueprint construction entry limit exceeded.");
        RequireOptionalText(file.Description, "description");
        EnsureUnique(file.Construction.Passages.Select(x => $"{x.X},{x.Y}"), "passage coordinate");
        EnsureUnique(file.Construction.Rooms.Select(x => x.InstanceId)
            .Concat(file.Construction.Traps.Select(x => x.InstanceId))
            .Concat(file.Construction.Guards.Select(x => x.InstanceId))
            .Concat(file.Construction.Facilities.Select(x => x.InstanceId)), "instance_id");
        foreach (var room in file.Construction.Rooms) { RequireId(room.InstanceId, "room.instance_id"); RequireId(room.DefinitionId, "room.definition_id"); }
        foreach (var item in file.Construction.Traps.Concat(file.Construction.Guards).Concat(file.Construction.Facilities))
        { RequireId(item.InstanceId, "placement.instance_id"); RequireId(item.DefinitionId, "placement.definition_id"); }
    }

    private static void ValidatePattern(DungeonBuildPatternFile file)
    {
        RequireCommon(file.SchemaVersion, file.Kind, "build_pattern", file.Id, file.Name);
        ArgumentNullException.ThrowIfNull(file.Tags);
        ArgumentNullException.ThrowIfNull(file.RequiredContent);
        ArgumentNullException.ThrowIfNull(file.Recipes);
        if (file.Recipes.Count == 0) throw new InvalidDataException("Build pattern must contain at least one recipe.");
        RequireOptionalText(file.Description, "description");
        if (file.Tags.Count > 64 || file.RequiredContent.Count > 512) throw new InvalidDataException("Pattern metadata entry limit exceeded.");
        foreach (var tag in file.Tags) RequireText(tag, "tag", 64);
        EnsureUnique(file.Recipes.Select(x => x.BoardProfile), "recipe board_profile");
        EnsureUnique(file.RequiredContent, "required_content");
        foreach (var recipe in file.Recipes)
        {
            RequireId(recipe.BoardProfile, "recipe.board_profile");
            ArgumentNullException.ThrowIfNull(recipe.Commands);
            if (recipe.Commands.Count > MaxPatternCommands) throw new InvalidDataException("Pattern command limit exceeded.");
            foreach (var command in recipe.Commands)
            {
                if (string.IsNullOrWhiteSpace(command.Type)) throw new InvalidDataException("Pattern command type is required.");
                if (command.Type is "dig_path" or "close_path" && command.Cells is not { Count: > 0 })
                    throw new InvalidDataException($"{command.Type} requires non-empty cells.");
                if (command.Type.StartsWith("place_", StringComparison.Ordinal))
                {
                    RequireId(command.InstanceId, $"{command.Type}.instance_id");
                    RequireId(command.DefinitionId, $"{command.Type}.definition_id");
                    if (command.X is null || command.Y is null) throw new InvalidDataException($"{command.Type} requires x/y.");
                }
            }
        }
    }

    private static void RequireCommon(int schemaVersion, string kind, string expectedKind, string id, string name)
    {
        if (schemaVersion != 1) throw new InvalidDataException($"Unsupported schema_version: {schemaVersion}.");
        if (!string.Equals(kind, expectedKind, StringComparison.Ordinal)) throw new InvalidDataException($"Expected kind '{expectedKind}', got '{kind}'.");
        RequireId(id, "id");
        RequireText(name, "name", 256);
    }

    private static void RequireId(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{field} is required.");
        if (!char.IsAsciiLetterOrDigit(value[0])) throw new InvalidDataException($"{field} has invalid id: {value}");
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new InvalidDataException($"{field} has invalid id: {value}");
    }

    private static void RequireOptionalText(string? value, string field)
    {
        if (value is not null) RequireText(value, field, MaxStringLength);
    }

    private static void RequireText(string value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{field} is required.");
        if (value.Length > maxLength) throw new InvalidDataException($"{field} exceeds {maxLength} characters.");
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate {label}: {duplicate.Key}");
    }
}
