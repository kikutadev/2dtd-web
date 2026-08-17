using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

public static class PlayerDungeonSaveCodec
{
    public const int SchemaVersion = 2;
    public const int MaxFileBytes = 4 * 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly PlayerDungeonSaveJsonContext JsonContext = new(JsonOptions);

    public static PlayerDungeonSaveFile Load(string path) => Parse(ReadTextBounded(path));

    public static PlayerDungeonSaveFile Parse(string json)
    {
        PlayerDungeonSaveFile file;
        try
        {
            file = JsonSerializer.Deserialize(json, JsonContext.PlayerDungeonSaveFile)
                ?? throw new InvalidDataException("Empty player dungeon save JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid player dungeon save JSON: {ex.Message}", ex);
        }
        Validate(file);
        return file;
    }

    public static string Serialize(PlayerDungeonSaveFile file)
    {
        Validate(file);
        var canonical = file with
        {
            Floors = file.Floors.OrderBy(x => x.Depth).Select(x => x with
            {
                UnlockedSectorIds = x.UnlockedSectorIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                Construction = CanonicalizeBlueprint(x.Construction),
            }).ToArray(),
        };
        return JsonSerializer.Serialize(canonical, JsonContext.PlayerDungeonSaveFile).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
    }

    public static void Save(string path, PlayerDungeonSaveFile file)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(file), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static DungeonBlueprintFile CanonicalizeBlueprint(DungeonBlueprintFile file)
        => file with
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

    private static void Validate(PlayerDungeonSaveFile file)
    {
        if (file.SchemaVersion != SchemaVersion) throw new InvalidDataException($"Unsupported player dungeon save schema_version: {file.SchemaVersion}.");
        if (!string.Equals(file.Kind, "player_dungeon_save", StringComparison.Ordinal)) throw new InvalidDataException($"Unexpected player dungeon save kind: {file.Kind}.");
        if (string.IsNullOrWhiteSpace(file.DungeonId)) throw new InvalidDataException("dungeon_id is required.");
        ArgumentNullException.ThrowIfNull(file.Floors);
        if (file.Floors.Count == 0) throw new InvalidDataException("Player dungeon save must contain at least one floor.");
        if (file.Floors.Count > 128) throw new InvalidDataException("Player dungeon save floor limit exceeded.");
        EnsureUnique(file.Floors.Select(x => x.FloorId), "floor_id");
        EnsureUnique(file.Floors.Select(x => x.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture)), "floor depth");
        foreach (var floor in file.Floors)
        {
            if (string.IsNullOrWhiteSpace(floor.FloorId) || string.IsNullOrWhiteSpace(floor.BoardProfileId)) throw new InvalidDataException("Floor identity is required.");
            if (floor.Depth <= 0 || floor.CapacityMax <= 0) throw new InvalidDataException($"Invalid floor numeric metadata: {floor.FloorId}.");
            if (floor.EndpointKind is not ("descent_gate" or "dungeon_core")) throw new InvalidDataException($"Invalid endpoint_kind: {floor.EndpointKind}.");
            ArgumentNullException.ThrowIfNull(floor.UnlockedSectorIds);
            ArgumentNullException.ThrowIfNull(floor.Construction);
            if (!string.Equals(floor.Construction.BoardProfile.Id, floor.BoardProfileId, StringComparison.Ordinal))
                throw new InvalidDataException($"Floor/profile mismatch: {floor.FloorId}.");
            _ = DungeonStaticFileCodec.SerializeBlueprint(floor.Construction);
        }
        if (file.SelectedFloorId is not null && !file.Floors.Any(x => x.FloorId == file.SelectedFloorId))
            throw new InvalidDataException($"Unknown selected_floor_id: {file.SelectedFloorId}.");
    }

    private static string ReadTextBounded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Player dungeon save not found.", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException($"Player dungeon save exceeds {MaxFileBytes} bytes.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate {label}: {duplicate.Key}");
    }
}
