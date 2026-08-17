using DungeonDefense.Application;
using DungeonDefense.Infrastructure;
using DungeonDefense.Presentation;
using Xunit;

namespace DungeonDefense.Web.Tests;

public sealed class NarrativeParityTests
{
    [Fact]
    public void WebSnapshotSelectsTheSameAuthoredHeroRumorBeat()
    {
        var path = FindRepoFile("src/DungeonDefense.Web/wwwroot/content/narrative-campaign.json");
        var content = NarrativeContentPresentation.Build(NarrativeContentLoader.Load(path));
        var transitions = new[]
        {
            new CampaignTransitionEvent(CampaignTransitionKind.DayAdvanced, Day: 25, RegionId: "region.first_frontier"),
        };

        var queue = NarrativeDirector.BuildQueue(transitions, content.Beats, new HashSet<string>(StringComparer.Ordinal));

        var beat = Assert.Single(queue);
        Assert.Equal("first.day25.hero_rumor", beat.BeatId);
        Assert.Equal("spirit.first.day25", beat.MessageKey);
    }
    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}.");
    }
}
