using DungeonDefense.Core;

namespace DungeonDefense.Application;

public static class CampaignChallengeService
{
    public static CampaignChallengeSession Create(
        CampaignState state,
        RegionCampaignContent regions,
        string archiveId,
        ChallengeMode mode,
        DefenseContent baseContent,
        IReadOnlyList<DefenseAssaultProfile> assaultProfiles)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(assaultProfiles);
        var archive = state.ClearedDungeon(archiveId);
        var region = regions.Region(archive.RegionId);
        var assaultId = mode switch
        {
            ChallengeMode.Replay => archive.FinalAssaultProfileId,
            ChallengeMode.Score => region.ScoreChallengeAssaultProfileId,
            ChallengeMode.SpecialWave => region.SpecialWaveAssaultProfileId,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var assault = assaultProfiles.SingleOrDefault(x => string.Equals(x.Id, assaultId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Challenge assault profile is missing: {assaultId}.");
        var definition = new ChallengeDefinition(archive.ArchiveId, archive.RegionId, mode, assault.Id);
        return new CampaignChallengeSession(definition, archive.Dungeon, baseContent.WithWaves(assault.Waves));
    }
}

public sealed class CampaignChallengeSession
{
    private ChallengeResult? _result;

    public CampaignChallengeSession(ChallengeDefinition definition, PlayerDungeonState dungeon, DefenseContent content)
    {
        Definition = definition;
        Content = content;
        Defense = new DefenseGameSession(dungeon);
    }

    public ChallengeDefinition Definition { get; }
    public DefenseContent Content { get; }
    public DefenseGameSession Defense { get; }
    public DefenseSimulation? ActiveDefense => Defense.ActiveDefense;

    public DefenseSimulation Start(int seed)
    {
        if (Defense.ActiveDefense is not null) throw new InvalidOperationException("Challenge already started.");
        var validation = DefenseStartValidator.Validate(Defense.Dungeon, Content);
        if (!validation.Success)
            throw new InvalidOperationException($"Archived dungeon is not valid for challenge: {string.Join("; ", (validation.Issues ?? []).Select(x => x.Message))}");
        return Defense.StartDefense(Content, seed);
    }

    public ChallengeResult Resolve()
    {
        if (_result is not null) return _result;
        var simulation = Defense.ActiveDefense ?? throw new InvalidOperationException("Challenge has not started.");
        if (simulation.Outcome == DefenseOutcome.Running) throw new InvalidOperationException("Challenge is still running.");
        var defeatedInvaders = simulation.Events.Count(x => x.Type == DefenseEventType.Death && x.ActorId.StartsWith('E'));
        var score = Definition.Mode == ChallengeMode.Score
            ? Math.Max(0,
                (simulation.Outcome == DefenseOutcome.Success ? 100_000 : 0)
                + simulation.CoreHp * 100
                + defeatedInvaders * 250
                + Math.Max(0, 20_000 - simulation.Tick))
            : 0;
        _result = new ChallengeResult(
            Definition, simulation.Outcome, score, simulation.CoreHp, simulation.CoreMaxHp, simulation.Tick, simulation.ResultDigest());
        return _result;
    }
}
