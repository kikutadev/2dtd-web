using System.Text.Json;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class RegionDefenseScheduleContentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly RegionDefenseScheduleJsonContext JsonContext = new(Options);

    public static RegionDefenseScheduleContent Load(string path)
    {
        var dto = JsonSerializer.Deserialize(File.ReadAllText(path), JsonContext.RegionDefenseScheduleFile)
            ?? throw new InvalidDataException("Region defense schedule content is empty.");
        if (dto.SchemaVersion != 1 || !string.Equals(dto.Kind, "region_defense_schedule", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported region defense schedule schema/kind.");
        return new RegionDefenseScheduleContent(
            dto.ContentVersion,
            dto.RegionId,
            dto.Days.Select(x => new RegionDayDefenseDefinition(
                x.Day,
                x.AssaultProfileIds,
                x.IntensityPercent,
                x.CountVariation,
                x.TimingJitterTicks,
                x.SeedVariation)).ToArray());
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "content", "first-region-defense-schedule.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Could not locate content/first-region-defense-schedule.json.");
    }

    public static IReadOnlyList<string> FindAllDefaultPaths(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var contentDirectory = Path.Combine(current.FullName, "content");
            if (Directory.Exists(contentDirectory))
            {
                var paths = Directory.GetFiles(contentDirectory, "*-region-defense-schedule.json")
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();
                if (paths.Length > 0) return paths;
            }
            current = current.Parent;
        }
        throw new FileNotFoundException("Could not locate region defense schedule content files.");
    }

    public static IReadOnlyDictionary<string, RegionDefenseScheduleContent> LoadAllDefault(string? startDirectory = null)
    {
        var schedules = FindAllDefaultPaths(startDirectory).Select(Load).ToArray();
        if (schedules.GroupBy(x => x.RegionId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("Region defense schedule region IDs must be unique.");
        return schedules.ToDictionary(x => x.RegionId, StringComparer.Ordinal);
    }

    internal sealed record RegionDefenseScheduleFile(
        int SchemaVersion,
        string Kind,
        string ContentVersion,
        string RegionId,
        DayFile[] Days);

    internal sealed record DayFile(
        int Day,
        string[] AssaultProfileIds,
        int IntensityPercent,
        int CountVariation,
        int TimingJitterTicks,
        bool SeedVariation = true);
}
