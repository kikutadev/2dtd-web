using System.Text.Json;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class DefenseAssaultProfileLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly AssaultProfileJsonContext JsonContext = new(Options);

    public static IReadOnlyList<DefenseAssaultProfile> Load(string path, DefenseContent baseContent)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize(json, JsonContext.ProfileFileDto)
            ?? throw new InvalidDataException("Assault profile file is empty.");
        if (dto.SchemaVersion != 1) throw new InvalidDataException($"Unsupported assault profile schema version: {dto.SchemaVersion}");
        if (dto.Profiles.Count == 0) throw new InvalidDataException("At least one assault profile is required.");

        EnsureUnique(dto.Profiles.Select(x => x.Id), "assault profile");
        var result = new List<DefenseAssaultProfile>(dto.Profiles.Count);
        foreach (var profile in dto.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Label) || profile.Waves.Count == 0)
                throw new InvalidDataException("Invalid assault profile metadata.");
            EnsureUnique(profile.Waves.Select(x => x.Id), $"wave in {profile.Id}");
            var waves = profile.Waves.Select(w => new WaveDefinition(
                w.Id,
                w.InterWaveTicks,
                w.SpawnGroups.Select(g => new SpawnGroupDefinition(g.UnitId, g.Count, g.InitialDelayTicks, g.SpawnIntervalTicks)).ToArray())).ToArray();
            foreach (var wave in waves)
            foreach (var group in wave.SpawnGroups)
            {
                if (!baseContent.Units.ContainsKey(group.UnitId))
                    throw new InvalidDataException($"Profile {profile.Id} references unknown unit: {group.UnitId}");
                if (group.Count <= 0 || group.InitialDelayTicks < 0 || group.SpawnIntervalTicks < 0)
                    throw new InvalidDataException($"Invalid spawn group in profile {profile.Id}, wave {wave.Id}.");
            }
            result.Add(new DefenseAssaultProfile(profile.Id, profile.Label, profile.Description, profile.ThreatTags, waves));
        }
        return result;
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "content", "assault-profiles.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Could not find content/assault-profiles.json from the current directory hierarchy.");
    }

    private static void EnsureUnique(IEnumerable<string> ids, string category)
    {
        var duplicates = ids.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidDataException($"Duplicate {category} IDs: {string.Join(", ", duplicates)}");
    }

    internal sealed class ProfileFileDto
    {
        public int SchemaVersion { get; set; }
        public List<ProfileDto> Profiles { get; set; } = [];
    }

    internal sealed class ProfileDto
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> ThreatTags { get; set; } = [];
        public List<WaveDto> Waves { get; set; } = [];
    }

    internal sealed class WaveDto
    {
        public string Id { get; set; } = "";
        public int InterWaveTicks { get; set; }
        public List<SpawnGroupDto> SpawnGroups { get; set; } = [];
    }

    internal sealed class SpawnGroupDto
    {
        public string UnitId { get; set; } = "";
        public int Count { get; set; }
        public int InitialDelayTicks { get; set; }
        public int SpawnIntervalTicks { get; set; }
    }
}
