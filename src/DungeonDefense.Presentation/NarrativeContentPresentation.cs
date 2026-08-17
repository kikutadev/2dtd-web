using DungeonDefense.Application;
using DungeonDefense.Contracts;

namespace DungeonDefense.Presentation;

public sealed record TutorialStepDefinition(
    TutorialStep Step,
    string MessageKey,
    TutorialFocusIntent FocusIntent,
    DarkSpiritExpression Expression);

public sealed record NarrativePresentationContent(
    string ContentVersion,
    IReadOnlyDictionary<TutorialStep, TutorialStepDefinition> Tutorial,
    IReadOnlyList<NarrativeBeatDefinition> Beats);

public static class NarrativeContentPresentation
{
    public static NarrativePresentationContent Build(NarrativeContentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var tutorial = file.Tutorial.Select(ToTutorial).ToDictionary(x => x.Step);
        var required = Enum.GetValues<TutorialStep>().Where(x => x != TutorialStep.Complete).ToArray();
        var missing = required.Where(x => !tutorial.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"Narrative tutorial content is incomplete: {string.Join(", ", missing)}.");
        var beats = file.Beats.Select(ToBeat).ToArray();
        return new NarrativePresentationContent(file.ContentVersion, tutorial, beats);
    }

    private static TutorialStepDefinition ToTutorial(NarrativeTutorialStepFile file)
        => new(Parse<TutorialStep>(file.Step, "tutorial step"), file.MessageKey,
            Parse<TutorialFocusIntent>(file.FocusIntent, "tutorial focus"), Parse<DarkSpiritExpression>(file.Expression, "tutorial expression"));

    private static NarrativeBeatDefinition ToBeat(NarrativeBeatFile file)
        => new(file.Id,
            Parse<CampaignTransitionKind>(file.Trigger, "narrative trigger"),
            file.MessageKey,
            Parse<NarrativePresentationMode>(file.Mode, "narrative mode"),
            file.Priority, file.OneShot, file.SubjectId, file.RelatedId, file.RegionId, file.Day,
            Parse<NarrativeFocusIntent>(file.FocusIntent, "narrative focus"),
            Parse<DarkSpiritExpression>(file.Expression, "narrative expression"));

    private static T Parse<T>(string value, string label) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: false, out var result)
            ? result
            : throw new InvalidDataException($"Unknown {label}: {value}.");
}
