using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonDefense.Core;

namespace DungeonDefense.Infrastructure;

public static class RegionCampaignContentLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly RegionCampaignJsonContext JsonContext = new(Options);

    public static RegionCampaignContent Load(string path)
    {
        var dto = JsonSerializer.Deserialize(File.ReadAllText(path), JsonContext.RegionCampaignFile)
            ?? throw new InvalidDataException("Region campaign content is empty.");
        if (dto.SchemaVersion != 1 || !string.Equals(dto.Kind, "region_campaign", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported region campaign schema/kind.");
        return new RegionCampaignContent(
            dto.ContentVersion,
            dto.Regions.Select(x => new RegionCampaignDefinition(
                x.Id, x.FinalDefenseDay, x.FinalAssaultProfileId, x.StartingBoardProfileId, x.NextRegionId,
                x.ScoreChallengeAssaultProfileId, x.SpecialWaveAssaultProfileId)).ToArray());
    }

    public static string FindDefaultPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "content", "regions-vertical-slice.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate content/regions-vertical-slice.json.");
    }

    internal sealed record RegionCampaignFile(int SchemaVersion, string Kind, string ContentVersion, RegionFile[] Regions);
    internal sealed record RegionFile(
        string Id,
        int FinalDefenseDay,
        string FinalAssaultProfileId,
        string StartingBoardProfileId,
        string? NextRegionId,
        string ScoreChallengeAssaultProfileId,
        string SpecialWaveAssaultProfileId);
}
