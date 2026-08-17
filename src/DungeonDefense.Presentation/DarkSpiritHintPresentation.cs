namespace DungeonDefense.Presentation;

/// <summary>
/// Rephrases already player-visible defense analysis as a character hint. This deliberately
/// consumes DefenseResultVisualState instead of introducing a second solver or hidden metrics.
/// </summary>
public static class DarkSpiritHintPresentation
{
    public static NarrativeSurfaceState BuildDefenseHint(DefenseResultVisualState result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var (id, key, expression) = result.HypothesisMessageKey switch
        {
            "result.hypothesis.guard_collapse" => ("hint.defense.guard_collapse", "spirit.hint.guard_collapse", DarkSpiritExpression.Concerned),
            "result.hypothesis.unused_device" => ("hint.defense.unused_device", "spirit.hint.unused_device", DarkSpiritExpression.Curious),
            "result.hypothesis.no_trap" => ("hint.defense.no_trap", "spirit.hint.no_trap", DarkSpiritExpression.Curious),
            "result.hypothesis.core_hit" => ("hint.defense.core_hit", "spirit.hint.core_hit", DarkSpiritExpression.Concerned),
            _ => ("hint.defense.safe", "spirit.hint.safe", DarkSpiritExpression.Pleased),
        };
        return new NarrativeSurfaceState(
            id, "dark_spirit", key, NarrativePresentationMode.Hint, 10, false,
            NarrativeFocusIntent.DefenseResult, ProductAssetIdentity.DarkSpiritPortrait(expression));
    }
}
