namespace DungeonDefense.Presentation;

public enum ProductHapticEvent
{
    Selection,
    PlacementSucceeded,
    CommandRejected,
    ImportantActivation,
    FloorBreached,
    CoreDamaged,
    EncounterSucceeded,
    EncounterFailed,
    PurchaseCompleted,
}

public readonly record struct ProductHapticPattern(int DurationMs, float Amplitude, int MinimumIntervalMs);

/// <summary>Maps semantic product feedback to host-neutral haptic intensity and rate limits.</summary>
public static class HapticPresentation
{
    public static ProductHapticPattern Pattern(ProductHapticEvent semanticEvent) => semanticEvent switch
    {
        ProductHapticEvent.Selection => new(18, 0.20f, 80),
        ProductHapticEvent.PlacementSucceeded => new(24, 0.28f, 90),
        ProductHapticEvent.CommandRejected => new(55, 0.48f, 180),
        ProductHapticEvent.ImportantActivation => new(30, 0.35f, 120),
        ProductHapticEvent.FloorBreached => new(85, 0.72f, 350),
        ProductHapticEvent.CoreDamaged => new(70, 0.62f, 280),
        ProductHapticEvent.EncounterSucceeded => new(80, 0.50f, 500),
        ProductHapticEvent.EncounterFailed => new(120, 0.78f, 500),
        ProductHapticEvent.PurchaseCompleted => new(55, 0.42f, 300),
        _ => throw new ArgumentOutOfRangeException(nameof(semanticEvent), semanticEvent, null),
    };
}
