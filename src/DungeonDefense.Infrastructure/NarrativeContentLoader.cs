using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

public static class NarrativeContentLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly NarrativeContentJsonContext JsonContext = new(Options);

    public static NarrativeContentFile Load(string path)
    {
        var file = JsonSerializer.Deserialize(File.ReadAllText(path), JsonContext.NarrativeContentFile)
            ?? throw new InvalidDataException("Narrative content is empty.");
        Validate(file);
        return file;
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "content", "narrative-campaign.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate content/narrative-campaign.json.");
    }

    public static void Validate(NarrativeContentFile file)
    {
        if (file.SchemaVersion != 1 || !string.Equals(file.Kind, "narrative_content", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported narrative schema/kind.");
        if (string.IsNullOrWhiteSpace(file.ContentVersion)) throw new InvalidDataException("Narrative content_version is required.");
        if (file.Tutorial is null || file.Beats is null) throw new InvalidDataException("Narrative tutorial/beats are required.");
        EnsureUnique(file.Tutorial.Select(x => x.Step), "tutorial step");
        EnsureUnique(file.Beats.Select(x => x.Id), "narrative beat ID");
        foreach (var step in file.Tutorial)
            if (string.IsNullOrWhiteSpace(step.Step) || string.IsNullOrWhiteSpace(step.MessageKey) || string.IsNullOrWhiteSpace(step.FocusIntent))
                throw new InvalidDataException("Narrative tutorial step contains an empty required value.");
        foreach (var beat in file.Beats)
        {
            if (string.IsNullOrWhiteSpace(beat.Id) || string.IsNullOrWhiteSpace(beat.Trigger) || string.IsNullOrWhiteSpace(beat.MessageKey) || string.IsNullOrWhiteSpace(beat.Mode))
                throw new InvalidDataException("Narrative beat contains an empty required value.");
            if (beat.Day is <= 0) throw new InvalidDataException($"Narrative beat day must be positive: {beat.Id}.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                throw new InvalidDataException($"Duplicate or empty {label}: {value}.");
    }
}
