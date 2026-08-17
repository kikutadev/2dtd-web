using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

public static class DungeonPatternCatalog
{
    public static string FindPatternDirectory(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "content", "dungeon-patterns");
            if (Directory.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find content/dungeon-patterns from the current directory hierarchy.");
    }

    public static IReadOnlyList<(string Path, DungeonBuildPatternFile Pattern)> LoadAll(string? startDirectory = null)
        => Directory.GetFiles(FindPatternDirectory(startDirectory), "*.pattern.json")
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => (x, DungeonStaticFileCodec.LoadPattern(x)))
            .ToArray();

    public static (string Path, DungeonBuildPatternFile Pattern) Resolve(string idOrPath, string? startDirectory = null)
    {
        if (File.Exists(idOrPath)) return (Path.GetFullPath(idOrPath), DungeonStaticFileCodec.LoadPattern(idOrPath));
        var match = LoadAll(startDirectory).SingleOrDefault(x => x.Pattern.Id == idOrPath);
        return match.Pattern is null
            ? throw new FileNotFoundException($"Unknown build pattern: {idOrPath}")
            : match;
    }
}
