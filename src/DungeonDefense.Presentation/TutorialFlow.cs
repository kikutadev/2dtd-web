namespace DungeonDefense.Presentation;

public enum TutorialStep
{
    Entrance,
    Route,
    Passage,
    Room,
    DefensePhase,
    Trap,
    Guard,
    GuardZone,
    Briefing,
    Combat,
    Result,
    Complete,
}

public enum TutorialSignal
{
    InfoNext,
    PassageCommitted,
    RoomPlaced,
    DefensePhaseSelected,
    TrapPlaced,
    GuardPlaced,
    GuardSelected,
    EditorDoneToBriefing,
    DefenseStarted,
    ResultShown,
}

/// <summary>
/// Semantic focus requested by the tutorial. Hosts resolve these intents through existing
/// gameplay/application authorities rather than embedding board coordinates in presentation state.
/// </summary>
public enum TutorialFocusIntent
{
    None,
    Entrance,
    CurrentRoute,
    PassagePlacementAnchor,
    RoomPlacementFootprint,
    DefensePhaseControl,
    TrapRouteCell,
    GuardInterceptCell,
    LatestGuard,
    DefenseStartControl,
}

public sealed record TutorialVisualState(
    bool Active,
    TutorialStep Step,
    TutorialFocusIntent FocusIntent,
    bool RequiresExplicitNext,
    ProductAssetRef SpeakerPortrait);

/// <summary>
/// Engine-neutral authority for the first-run defense tutorial. Persistence is deliberately
/// external: profile settings decide whether Start is called, while this type owns only flow.
/// </summary>
public sealed class TutorialFlow
{
    public TutorialStep Step { get; private set; } = TutorialStep.Entrance;
    public bool Active { get; private set; }

    public void Start()
    {
        Step = TutorialStep.Entrance;
        Active = true;
    }

    public bool Accept(TutorialSignal signal)
    {
        if (!Active) return false;

        // Starting defense is an acknowledged action, but Combat remains visible while the
        // simulation runs. ResultShown is the state transition to the result explanation.
        if (Step == TutorialStep.Combat && signal == TutorialSignal.DefenseStarted) return true;

        var matches = (Step, signal) switch
        {
            (TutorialStep.Entrance, TutorialSignal.InfoNext) => true,
            (TutorialStep.Route, TutorialSignal.InfoNext) => true,
            (TutorialStep.Passage, TutorialSignal.PassageCommitted) => true,
            (TutorialStep.Room, TutorialSignal.RoomPlaced) => true,
            (TutorialStep.DefensePhase, TutorialSignal.DefensePhaseSelected) => true,
            (TutorialStep.Trap, TutorialSignal.TrapPlaced) => true,
            (TutorialStep.Guard, TutorialSignal.GuardPlaced) => true,
            (TutorialStep.GuardZone, TutorialSignal.GuardSelected) => true,
            (TutorialStep.Briefing, TutorialSignal.EditorDoneToBriefing) => true,
            (TutorialStep.Combat, TutorialSignal.ResultShown) => true,
            (TutorialStep.Result, TutorialSignal.InfoNext) => true,
            _ => false,
        };
        if (!matches) return false;

        Step++;
        if (Step >= TutorialStep.Complete)
        {
            Step = TutorialStep.Complete;
            Active = false;
        }
        return true;
    }

    public void Skip()
    {
        Step = TutorialStep.Complete;
        Active = false;
    }

    public TutorialVisualState VisualState => new(
        Active,
        Step,
        FocusIntent(Step),
        Step is TutorialStep.Entrance or TutorialStep.Route or TutorialStep.Result,
        ProductAssetIdentity.DarkSpiritPortrait(DarkSpiritExpression.Neutral));

    public static TutorialFocusIntent FocusIntent(TutorialStep step) => step switch
    {
        TutorialStep.Entrance => TutorialFocusIntent.Entrance,
        TutorialStep.Route => TutorialFocusIntent.CurrentRoute,
        TutorialStep.Passage => TutorialFocusIntent.PassagePlacementAnchor,
        TutorialStep.Room => TutorialFocusIntent.RoomPlacementFootprint,
        TutorialStep.DefensePhase => TutorialFocusIntent.DefensePhaseControl,
        TutorialStep.Trap => TutorialFocusIntent.TrapRouteCell,
        TutorialStep.Guard => TutorialFocusIntent.GuardInterceptCell,
        TutorialStep.GuardZone => TutorialFocusIntent.LatestGuard,
        TutorialStep.Combat => TutorialFocusIntent.DefenseStartControl,
        _ => TutorialFocusIntent.None,
    };
}
