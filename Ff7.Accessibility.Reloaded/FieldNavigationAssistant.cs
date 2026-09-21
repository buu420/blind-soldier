namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationAction
{
    PreviousCategory,
    NextCategory,
    PreviousTarget,
    NextTarget,
    RepeatTarget,
    ToggleBeacon
}

public readonly record struct FieldNavigationActionResult(string Speech);

public static class FieldNavigationSuppressionPolicy
{
    public static bool IsNavigationSuppressed(
        FieldAudibleCueState cue,
        FieldLadderStateSnapshot ladder,
        bool isLadderStateCoherent) =>
        cue.IsSuppressed &&
        !(isLadderStateCoherent &&
          ladder.IsMounted &&
          cue.Module == FieldPositionReader.FieldModule &&
          cue.UserControl != 0 &&
          cue.ActiveMessageCount == 0 &&
          cue.MovieActive == 0);
}

public readonly record struct FieldNavigationActionRoutePreview(
    bool UsesRoute,
    bool RequiresCoherentRoute,
    FieldNavigationTarget? Target);

public readonly record struct FieldNavigationControllerProbeSnapshot(
    bool BeaconEnabled,
    int FieldId,
    FieldNavigationCategory Category,
    string TargetId,
    string TargetLabel,
    int TargetX,
    int TargetY,
    int TargetZ,
    FieldNavigationRouteProbeSnapshot? Route,
    string Diagnostic);

public sealed class FieldNavigationTargetSource
{
    private readonly Dictionary<int, IReadOnlyList<FieldNavigationTarget>> targetsByField;
    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? objectTargetProvider;
    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? storyTargetProvider;
    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? exitTargetProvider;
    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? npcTargetProvider;
    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets = Array.Empty<FieldNavigationTarget>();

    public FieldNavigationTargetSource(
        IEnumerable<FieldNavigationTarget> targets,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? objectTargetProvider = null,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? storyTargetProvider = null,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? exitTargetProvider = null,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? npcTargetProvider = null)
    {
        this.objectTargetProvider = objectTargetProvider;
        this.storyTargetProvider = storyTargetProvider;
        this.exitTargetProvider = exitTargetProvider;
        this.npcTargetProvider = npcTargetProvider;
        targetsByField = targets
            .GroupBy(target => target.FieldId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FieldNavigationTarget>)group.ToArray());
    }

    public static FieldNavigationTargetSource CreateOpeningReactorRoute(
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? objectTargetProvider = null,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? storyTargetProvider = null,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? exitTargetProvider = null,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? npcTargetProvider = null) =>
        new(new[]
        {
            new FieldNavigationTarget(116, FieldNavigationCategory.Exits, "Station stairs", 3659, 29332, 348),
            new FieldNavigationTarget(116, FieldNavigationCategory.Story, "Regroup with Avalanche near the station stairs", 3659, 29332, 348, CompletesOnArrival: true),
            new FieldNavigationTarget(116, FieldNavigationCategory.Npcs, "Barret near the station stairs", 3600, 28452, 300),

            new FieldNavigationTarget(117, FieldNavigationCategory.Exits, "Gate to the next platform", 1148, 1358, 1288),
            new FieldNavigationTarget(117, FieldNavigationCategory.Story, "Follow Avalanche through the platform gate", 1148, 1358, 1288, CompletesOnArrival: true),
            new FieldNavigationTarget(117, FieldNavigationCategory.Npcs, "Avalanche member near the gate", 1320, 1535, 1288),

            new FieldNavigationTarget(118, FieldNavigationCategory.Exits, "Lower platform exit", 3030, 32390, 642),
            new FieldNavigationTarget(118, FieldNavigationCategory.Story, "Follow the crew across the platform", 3030, 32390, 642, CompletesOnArrival: true),
            new FieldNavigationTarget(118, FieldNavigationCategory.Npcs, "Avalanche at the platform entrance", 3549, 30574, 639),

            new FieldNavigationTarget(119, FieldNavigationCategory.Exits, "Reactor interior door", 31, -1026, 481),
            new FieldNavigationTarget(119, FieldNavigationCategory.Story, "Continue into the reactor interior", 31, -1026, 481, CompletesOnArrival: true),

            new FieldNavigationTarget(120, FieldNavigationCategory.Exits, "Reactor walkway exit", -1490, 4517, -282),
            new FieldNavigationTarget(120, FieldNavigationCategory.Story, "Follow the reactor walkway", -1490, 4517, -282, CompletesOnArrival: true),

            new FieldNavigationTarget(121, FieldNavigationCategory.Exits, "Elevator passage", -197, -44, -5),
            new FieldNavigationTarget(121, FieldNavigationCategory.Story, "Follow Barret toward the elevator", -197, -44, -5, CompletesOnArrival: true),
            new FieldNavigationTarget(121, FieldNavigationCategory.Npcs, "Barret by the elevator passage", -95, 75, -5),

            new FieldNavigationTarget(122, FieldNavigationCategory.Exits, "Reactor path forward", -735, 1030, 1561),
            new FieldNavigationTarget(122, FieldNavigationCategory.Story, "Continue toward the first save point route", -735, 1030, 1561, CompletesOnArrival: true)
        }, objectTargetProvider, storyTargetProvider, exitTargetProvider, npcTargetProvider);

    public IReadOnlyList<FieldNavigationTarget> GetTargets(FieldPositionSnapshot position, FieldNavigationCategory category)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return EmptyTargets;
        }

        if (category == FieldNavigationCategory.Exits && exitTargetProvider is not null)
        {
            return exitTargetProvider(position);
        }

        if (category == FieldNavigationCategory.Story && storyTargetProvider is not null)
        {
            return storyTargetProvider(position);
        }

        if (category == FieldNavigationCategory.Npcs && npcTargetProvider is not null)
        {
            return npcTargetProvider(position);
        }

        var staticTargets = GetTargets(position.FieldId, category);
        if (category != FieldNavigationCategory.Objects || objectTargetProvider is null)
        {
            return staticTargets;
        }

        var objectTargets = objectTargetProvider(position);
        if (staticTargets.Count == 0)
        {
            return objectTargets;
        }

        if (objectTargets.Count == 0)
        {
            return staticTargets;
        }

        return staticTargets.Concat(objectTargets).ToArray();
    }

    public IReadOnlyList<FieldNavigationTarget> GetTargets(int fieldId, FieldNavigationCategory category)
    {
        if (!targetsByField.TryGetValue(fieldId, out var fieldTargets))
        {
            return EmptyTargets;
        }

        var matches = fieldTargets.Where(target => target.Category == category).ToArray();
        return matches.Length == 0 ? EmptyTargets : matches;
    }
}

public sealed class FieldNavigationController
{
    // The native movement probe's own reach, and the reach auto walk used for every speed
    // before the running pace was measured.
    private const double NativeProbeDistance = 8d;

    // One navigation sample of running, with a little room. Beyond this a jump between
    // samples is a map change or a script placement rather than a walk, and probing that
    // far would reject directions that are perfectly walkable.
    private const double MaximumLookaheadDistance = 64d;

    // Under two units between samples is the party standing still; the field reports
    // whole units and a walking step is sixteen.
    private const double StalledMovementDistance = 2d;

    private const int LadderActionArrivalDistance = 56;
    private const int LadderLandingArrivalDistance = 96;
    private const int LadderEndpointMatchDistance = 224;
    private const int CompletedLadderEndpointMatchDistance = 96;
    private const int DefaultSelectionArrivalDistance = 80;

    // An exit fires only when the party crosses its gateway or trigger line, so the route
    // ends on the crossing itself and completion must wait until the player is there;
    // anything wider stops auto-walk short and the transition never fires.
    private const int ExitCrossingArrivalDistance = 0;

    // The Honey Bee Inn lobby rooms carry an inflated interaction radius purely so a
    // fallback route stays plannable; it must never widen proximity to a room-sized blob.
    private const int TriggerLineExitArrivalDistance = 16;
    private static readonly TimeSpan LadderMountPromptInterval =
        TimeSpan.FromMilliseconds(700);

    private static readonly FieldNavigationCategory[] CategoryOrder =
    {
        FieldNavigationCategory.Exits,
        FieldNavigationCategory.Story,
        FieldNavigationCategory.Npcs,
        FieldNavigationCategory.Objects
    };

    private static readonly FieldNavigationCategory[] RemovedTargetRecoveryCategoryOrder =
    {
        FieldNavigationCategory.Story,
        FieldNavigationCategory.Exits,
        FieldNavigationCategory.Npcs
    };

    private readonly FieldNavigationTargetSource source;
    private readonly IFieldNavigationRoutePlanner? routePlanner;
    private readonly FieldNavigationRouteTracker? routeTracker;
    private readonly Func<int, int> spokenDistanceUnitsPerCountResolver;
    private readonly FieldNavigationMovementObserver movementObserver = new();
    private readonly FieldNavigationVelocityEstimator velocityEstimator = new();
    private readonly FieldNavigationPositionContinuityTracker positionContinuityTracker = new();
    private readonly FieldNavigationRouteProgressTracker routeProgressTracker = new();
    private readonly IFieldNavigationProgressSink? routeProgressSink;
    private readonly Dictionary<string, string> selectedTargetIds = new(StringComparer.Ordinal);
    private FieldNavigationTarget? beaconLockedTarget;
    private FieldNavigationRouteGuidance? currentGuidance;
    private FieldNavigationRouteAction? pendingLadderAction;
    private FieldNavigationRouteAction? lastCompletedLadderAction;
    private FieldLadderStateSnapshot activeLadderState;
    private FieldNavigationInput activeLadderGuidanceInput;
    private FieldNavigationRouteWaypoint activeLadderExpectedLanding;
    private int activeLadderExpectedTriangle = -1;
    private bool activeLadderHasExpectedLanding;
    private bool routeStartsAfterMountedLadder;
    private bool routeRefreshPending;
    private string ladderPromptActionId = string.Empty;
    private DateTime nextLadderPromptAt = DateTime.MinValue;
    private FieldPositionSnapshot? positionRecoveryCandidate;
    private FieldPositionSnapshot? positionRecoveryAnchor;
    private FieldPositionSnapshot? lastAcceptedPosition;
    private DateTime positionRecoveryCandidateObservedAt;
    private bool positionRecoveryPending;
    private static readonly TimeSpan PositionRecoveryStableWindow =
        TimeSpan.FromMilliseconds(120);
    private string beaconTargetId = string.Empty;
    private string beaconTargetLabel = string.Empty;

    /// <summary>
    /// A destination the player asked for while the game was holding its way shut. Cosmo
    /// Canyon's observatory is the reported case: Bugenhagen locks the door down for the
    /// length of his lecture and the research centre's upper door while he walks the party
    /// in. Cancelling there left a blind player with no destination and no way to know the
    /// door had opened, so the selection is kept and started the moment it does.
    /// </summary>
    private FieldNavigationTarget? pendingBoundaryTarget;
    private bool heldAutoWalkRequested;
    private FieldNavigationCategory beaconCategory;
    private int beaconFieldId = -1;
    private int[] beaconDestinationFieldIds = [];
    private FieldNavigationTarget[] beaconDepartureExits = [];
    private bool beaconCompletesOnFieldTransition;
    // The distance the party covered between the last two automatic samples, which is how
    // far the clearance probe should look. See TryResolveAutomaticInput.
    private FieldPositionSnapshot? lastAutomaticPosition;
    private double lookaheadDistance;
    private bool automaticMovementStalled;

    private bool interactionArrivalPaused;
    private int interactionArrivalDistance;
    private int categoryIndex;

    public FieldNavigationController(
        FieldNavigationTargetSource source,
        IFieldNavigationRoutePlanner? routePlanner = null,
        int spokenDistanceUnitsPerCount = FieldNavigationSpokenCueFormatter.DefaultDistanceUnitsPerCount,
        IFieldNavigationProgressSink? routeProgressSink = null)
        : this(
            source,
            routePlanner,
            _ => Math.Max(1, spokenDistanceUnitsPerCount),
            routeProgressSink)
    {
    }

    public FieldNavigationController(
        FieldNavigationTargetSource source,
        IFieldNavigationRoutePlanner? routePlanner,
        Func<int, int> spokenDistanceUnitsPerCountResolver,
        IFieldNavigationProgressSink? routeProgressSink = null)
    {
        this.source = source;
        this.routePlanner = routePlanner;
        this.spokenDistanceUnitsPerCountResolver = spokenDistanceUnitsPerCountResolver;
        this.routeProgressSink = routeProgressSink;
        routeTracker = routePlanner is null ? null : new FieldNavigationRouteTracker(routePlanner);
    }

    public bool BeaconEnabled { get; private set; }

    public FieldNavigationCategory CurrentCategory => CategoryOrder[categoryIndex];

    public FieldNavigationRouteGuidance? CurrentRouteGuidance => currentGuidance;

    public string? PrioritizedLadderTransitionId =>
        BeaconEnabled && pendingLadderAction is { } action
            ? action.StableId
            : null;

    public int CurrentRouteProgressPercent => routeProgressTracker.Percent;

    public string LastNavigationDiagnostic { get; private set; } = string.Empty;

    /// <summary>
    /// Why the last call to <see cref="TryResolveAutomaticInput"/> produced no direction.
    ///
    /// <para>Auto walk holds still for several completely different reasons and they must
    /// not be treated alike. Waiting at an interaction point or a ladder for the player to
    /// press something is correct behaviour and no one should be told about it. Having a
    /// route, a waypoint and no direction that probes clear is the party wedged against
    /// something, which is precisely the failure the convergence guard exists to catch -
    /// and while both looked the same from outside, the guard reset itself on every sample
    /// of the second and so could never trip.</para>
    /// </summary>
    public FieldAutoWalkHoldReason LastAutomaticInputHold { get; private set; } =
        FieldAutoWalkHoldReason.NoRoute;

    /// <summary>
    /// The label of the target the beacon is on, for messages about the route itself.
    /// Empty when no route is running.
    /// </summary>
    public string CurrentTargetLabel => BeaconEnabled ? beaconTargetLabel : string.Empty;

    /// <summary>
    /// What is being walked to, as something two targets cannot share.
    ///
    /// <para>The label will not do. A room with two men in it has two targets called
    /// "Man", and an identity built from the label cannot tell the player changing between
    /// them from the player staying put - which is the one thing this is for. The stable
    /// target id is the id the catalog and the route planner already key on, and the field
    /// and category are carried with it because ids are only unique inside them.</para>
    /// </summary>
    public string CurrentRouteIdentity =>
        BeaconEnabled ? $"{beaconFieldId}|{beaconCategory}|{beaconTargetId}" : string.Empty;

    public FieldNavigationControllerProbeSnapshot CreateProbeSnapshot(
        FieldPositionSnapshot position)
    {
        var target = GetBeaconTarget(position);
        if (target is not { } selected)
        {
            return new FieldNavigationControllerProbeSnapshot(
                BeaconEnabled,
                BeaconEnabled ? beaconFieldId : position.FieldId,
                BeaconEnabled ? beaconCategory : CurrentCategory,
                BeaconEnabled ? beaconTargetId : string.Empty,
                BeaconEnabled ? beaconTargetLabel : string.Empty,
                0,
                0,
                0,
                routeTracker?.CurrentProbeSnapshot,
                LastNavigationDiagnostic);
        }

        return new FieldNavigationControllerProbeSnapshot(
            true,
            selected.FieldId,
            selected.Category,
            string.IsNullOrWhiteSpace(selected.StableId)
                ? beaconTargetId
                : selected.StableId,
            selected.Label,
            selected.X,
            selected.Y,
            selected.Z,
            routeTracker?.CurrentProbeSnapshot,
            LastNavigationDiagnostic);
    }

    public FieldNavigationActionRoutePreview PreviewActionRoute(
        FieldNavigationAction action,
        FieldPositionSnapshot position,
        bool includeSelectionRoute)
    {
        if (!FieldPositionReader.IsUsable(position) || routePlanner is null)
        {
            return default;
        }

        FieldNavigationTarget? target;
        var requiresCoherentRoute = false;
        switch (action)
        {
            case FieldNavigationAction.PreviousCategory:
                target = PeekSelectedTarget(position, PeekCategory(-1));
                requiresCoherentRoute = BeaconEnabled && target is not null;
                break;
            case FieldNavigationAction.NextCategory:
                target = PeekSelectedTarget(position, PeekCategory(1));
                requiresCoherentRoute = BeaconEnabled && target is not null;
                break;
            case FieldNavigationAction.PreviousTarget:
                target = PeekMovedTarget(position, CurrentCategory, -1);
                requiresCoherentRoute = BeaconEnabled && target is not null;
                break;
            case FieldNavigationAction.NextTarget:
                target = PeekMovedTarget(position, CurrentCategory, 1);
                requiresCoherentRoute = BeaconEnabled && target is not null;
                break;
            case FieldNavigationAction.RepeatTarget:
                if (BeaconEnabled)
                {
                    return default;
                }

                target = PeekSelectedTarget(position, CurrentCategory);
                break;
            case FieldNavigationAction.ToggleBeacon:
                if (BeaconEnabled)
                {
                    return default;
                }

                target = PeekSelectedTarget(position, CurrentCategory);
                requiresCoherentRoute = target is not null;
                break;
            default:
                return default;
        }

        if (target is { } manualTarget && !string.IsNullOrWhiteSpace(manualTarget.ManualNavigationGuidance))
        {
            return new FieldNavigationActionRoutePreview(false, false, target);
        }

        return new FieldNavigationActionRoutePreview(
            target is not null && (requiresCoherentRoute || includeSelectionRoute),
            requiresCoherentRoute,
            target);
    }

    public static FieldPositionSnapshot ResolveRoutePlanningPosition(
        FieldPositionSnapshot position,
        FieldLadderStateSnapshot ladderState)
    {
        if (!ladderState.IsUsable ||
            !ladderState.IsMounted ||
            ladderState.TargetTriangle < 0 ||
            ladderState.TargetTriangle > ushort.MaxValue)
        {
            return position;
        }

        return position with
        {
            X = ladderState.Target.X,
            Y = ladderState.Target.Y,
            Z = ladderState.Target.Z,
            TriangleId = (ushort)ladderState.TargetTriangle,
            NativeFixedPosition = null
        };
    }

    public void Reset()
    {
        CancelHeldBoundaryTarget();
        ResetCore(deactivateProgress: true);
    }

    /// <summary>
    /// Whether the last route attempt failed only because the game is currently holding a
    /// native walkmesh boundary shut, rather than because there is no such route.
    /// </summary>
    public bool LastRouteFailureWasNativeBoundary =>
        routePlanner is IFieldNavigationNativeBoundaryStatus status &&
        status.LastFailureWasNativeBoundary;

    /// <summary>The destination being held until the game opens its way, if any.</summary>
    public string HeldForNativeBoundaryLabel => pendingBoundaryTarget?.Label ?? string.Empty;

    /// <summary>
    /// A destination is selected and waiting for the game to open its way.
    ///
    /// <para>Callers that ask "is navigation engaged" must test this as well as
    /// <see cref="BeaconEnabled"/>. A hold is pending intent, not a movement route: no input
    /// is produced while it waits, but B must still cancel it, changing the selection must
    /// still replace it, and an auto-walk request must survive it.</para>
    /// </summary>
    public bool IsHoldingForNativeBoundary => pendingBoundaryTarget is not null;

    /// <summary>
    /// Records that the player asked to be walked there, not merely told the way. Ignored
    /// unless a hold is actually armed, so a plain speech selection can never turn into a
    /// walk by itself.
    /// </summary>
    public void RequestAutoWalkForHeldRoute() =>
        heldAutoWalkRequested = pendingBoundaryTarget is not null;

    /// <summary>
    /// Whether the held destination that has just started was an auto-walk request. Taken
    /// once: the caller starts the walk, and a later frame must not start it again.
    /// </summary>
    public bool TryConsumeHeldAutoWalkRequest()
    {
        if (!heldAutoWalkRequested || !BeaconEnabled)
        {
            return false;
        }

        heldAutoWalkRequested = false;
        return true;
    }

    /// <summary>
    /// Cancels a held destination. Used by B, by a repeated toggle, and by anything that
    /// replaces or invalidates the selection.
    /// </summary>
    public bool CancelHeldBoundaryTarget()
    {
        if (pendingBoundaryTarget is null)
        {
            heldAutoWalkRequested = false;
            return false;
        }

        pendingBoundaryTarget = null;
        heldAutoWalkRequested = false;
        return true;
    }

    /// <summary>
    /// The player moved the selection. A destination held for a shut door belongs to the
    /// selection they left, so it must not activate behind the new one.
    /// </summary>
    private void DropHeldBoundaryTargetOnSelectionChange(FieldPositionSnapshot position)
    {
        if (pendingBoundaryTarget is not { } held)
        {
            return;
        }

        var selected = GetSelectedTarget(position);
        if (selected is null ||
            !string.Equals(GetTargetId(selected.Value), GetTargetId(held), StringComparison.Ordinal))
        {
            CancelHeldBoundaryTarget();
        }
    }

    private FieldNavigationActionResult? TryStartHeldBoundaryRoute(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        FieldLadderStateSnapshot ladderState)
    {
        if (pendingBoundaryTarget is not { } held)
        {
            return null;
        }

        if (!FieldPositionReader.IsUsable(position))
        {
            return null;
        }

        if (position.FieldId != held.FieldId)
        {
            // Left the screen the door belongs to. Nothing is owed here.
            CancelHeldBoundaryTarget();
            return null;
        }

        // TryLockBeacon resets first, and a reset clears the hold along with everything
        // else. The player asked to be walked there before the door opened; that request
        // has to survive the mechanics of starting the route it belongs to.
        var walkWasRequested = heldAutoWalkRequested;
        if (!TryLockBeacon(held, position, ladderState))
        {
            if (LastRouteFailureWasNativeBoundary)
            {
                pendingBoundaryTarget = held;
                heldAutoWalkRequested = walkWasRequested;
                return null;
            }

            CancelHeldBoundaryTarget();
            return new FieldNavigationActionResult(
                $"Route unavailable to {held.Label}. Navigation off.");
        }

        pendingBoundaryTarget = null;
        heldAutoWalkRequested = walkWasRequested;
        var guidance = CreateSpokenGuidance(position, controlTransform, arrivalDistanceUnits: 0);
        return new FieldNavigationActionResult(
            guidance is null
                ? $"The way to {held.Label} is open. Navigation on."
                : $"The way to {held.Label} is open. Navigation on. {guidance.Value.Speech}.");
    }

    private void ResetCore(bool deactivateProgress)
    {
        BeaconEnabled = false;
        beaconLockedTarget = null;
        currentGuidance = null;
        pendingLadderAction = null;
        lastCompletedLadderAction = null;
        activeLadderState = default;
        activeLadderGuidanceInput = FieldNavigationInput.None;
        activeLadderExpectedLanding = default;
        activeLadderExpectedTriangle = -1;
        activeLadderHasExpectedLanding = false;
        routeStartsAfterMountedLadder = false;
        routeRefreshPending = false;
        positionRecoveryCandidate = null;
        positionRecoveryAnchor = null;
        lastAcceptedPosition = null;
        positionRecoveryCandidateObservedAt = default;
        positionRecoveryPending = false;
        beaconTargetId = string.Empty;
        beaconTargetLabel = string.Empty;
        beaconFieldId = -1;
        beaconDestinationFieldIds = [];
        beaconDepartureExits = [];
        beaconCompletesOnFieldTransition = false;
        interactionArrivalPaused = false;
        interactionArrivalDistance = 0;
        ResetLadderMountPrompt();
        routeTracker?.Reset();
        movementObserver.Reset();
        ResetAutomaticPace();
        velocityEstimator.Reset();
        positionContinuityTracker.Reset();
        routeProgressTracker.Reset();
        if (deactivateProgress)
        {
            PublishProgress(sink => sink.Deactivate());
        }
    }

    public void SuspendForPositionRecovery(string diagnostic)
    {
        if (!BeaconEnabled)
        {
            return;
        }

        BeginPositionRecovery(
            string.IsNullOrWhiteSpace(diagnostic)
                ? "native field position unavailable"
                : diagnostic);
    }

    public FieldNavigationActionResult? HandleAction(
        FieldNavigationAction action,
        FieldPositionSnapshot position,
        FieldNavigationControlTransform? controlTransform = null,
        FieldLadderStateSnapshot ladderState = default)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            Reset();
            return null;
        }

        switch (action)
        {
            case FieldNavigationAction.PreviousCategory:
                MoveCategory(-1);
                DropHeldBoundaryTargetOnSelectionChange(position);
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.NextCategory:
                MoveCategory(1);
                DropHeldBoundaryTargetOnSelectionChange(position);
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.PreviousTarget:
                MoveTarget(position, -1);
                DropHeldBoundaryTargetOnSelectionChange(position);
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.NextTarget:
                MoveTarget(position, 1);
                DropHeldBoundaryTargetOnSelectionChange(position);
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.RepeatTarget:
                if (BeaconEnabled)
                {
                    var guidance = controlTransform is null
                        ? null
                        : CreateSpokenGuidance(position, controlTransform.Value, arrivalDistanceUnits: 0);
                    return new FieldNavigationActionResult(
                        guidance is null
                            ? $"{beaconTargetLabel}. nearby. Route progress {CurrentRouteProgressPercent} percent."
                            : $"{beaconTargetLabel}. {guidance.Value.Speech}. " +
                              $"Route progress {CurrentRouteProgressPercent} percent.");
                }

                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.ToggleBeacon:
                if (BeaconEnabled)
                {
                    Reset();
                    return new FieldNavigationActionResult("Navigation off.");
                }

                if (CancelHeldBoundaryTarget())
                {
                    return new FieldNavigationActionResult("Navigation off.");
                }

                var target = GetSelectedTarget(position);
                if (target is null)
                {
                    return DescribeCurrentSelection(position, controlTransform, ladderState);
                }

                if (!string.IsNullOrWhiteSpace(target.Value.ManualNavigationGuidance))
                {
                    Reset();
                    return DescribeManualTarget(target.Value);
                }

                if (!TryLockBeacon(target.Value, position, ladderState))
                {
                    if (LastRouteFailureWasNativeBoundary)
                    {
                        // Nothing routes through the lock and nothing pretends it is
                        // open. The destination is held and started when the game
                        // releases it, which is the information a sighted player gets
                        // from watching the door.
                        pendingBoundaryTarget = target.Value;
                        return new FieldNavigationActionResult(
                            $"{target.Value.Label}. The way is shut just now. " +
                            "Navigation will start when it opens.");
                    }

                    CancelHeldBoundaryTarget();
                    return new FieldNavigationActionResult(
                        $"Route unavailable to {target.Value.Label}. Navigation off.");
                }

                CancelHeldBoundaryTarget();

                var initialGuidance = controlTransform is null
                    ? null
                    : CreateSpokenGuidance(position, controlTransform.Value, arrivalDistanceUnits: 0);
                return new FieldNavigationActionResult(
                    initialGuidance is null
                        ? $"Navigation on. {target.Value.Label}."
                        : $"Navigation on. {target.Value.Label}. {initialGuidance.Value.Speech}.");
            default:
                return null;
        }
    }

    public FieldNavigationActionResult? UpdateLiveTracking(
        FieldPositionSnapshot position,
        FieldNavigationInputSnapshot input,
        FieldNavigationControlTransform controlTransform,
        bool isSuppressed,
        int arrivalDistanceUnits = 0,
        FieldLadderStateSnapshot ladderState = default,
        DateTime observedAt = default)
    {
        if (!BeaconEnabled)
        {
            // Not while the game owns the speech. Bugenhagen's door opens mid-line, and
            // announcing over voiced dialogue is its own defect; the hold simply waits.
            return isSuppressed
                ? null
                : TryStartHeldBoundaryRoute(position, controlTransform, ladderState);
        }

        if (!FieldPositionReader.IsUsable(position))
        {
            BeginPositionRecovery("native field position unusable");
            return null;
        }

        if (position.FieldId != beaconFieldId)
        {
            var category = beaconCategory;
            var label = beaconTargetLabel;
            var departedFieldId = beaconFieldId;
            var expectedDestinations = beaconDestinationFieldIds;
            var completesOnFieldTransition = beaconCompletesOnFieldTransition;
            var destinationMatches =
                expectedDestinations.Length == 0
                    ? category == FieldNavigationCategory.Exits
                    : expectedDestinations.Contains(position.FieldId);
            var reached = completesOnFieldTransition && destinationMatches &&
                MatchesDepartureGateway(position.FieldId);
            var reason = reached
                ? $"matching field transition to {position.FieldId}"
                : $"native target field changed to {position.FieldId}";
            ClearSelectionsForField(departedFieldId);
            if (reached)
            {
                CompleteRouteProgress();
                ResetCore(deactivateProgress: false);
            }
            else
            {
                Reset();
            }

            LastNavigationDiagnostic = $"navigation completion, target field changed, reason={reason}";
            if (isSuppressed)
            {
                return null;
            }

            return new FieldNavigationActionResult(
                reached
                    ? $"{label} reached. Navigation off."
                    : $"Left the area. Navigation to {label} cancelled.");
        }

        if (isSuppressed)
        {
            positionContinuityTracker.Reset();
            velocityEstimator.Observe(position, observedAt, isSuppressed: true);
            var suppressedObservation = movementObserver.Observe(
                position,
                input,
                controlTransform,
                isSuppressed: true);
            BeginPositionRecovery(suppressedObservation.Diagnostic);
            LastNavigationDiagnostic = suppressedObservation.Diagnostic;
            return null;
        }

        if (!TryCompletePositionRecovery(position, observedAt))
        {
            return null;
        }

        var target = GetBeaconTarget(position);
        if (target is null &&
            beaconLockedTarget is { } lockedTarget &&
            (ladderState.IsMounted || activeLadderState.IsMounted))
        {
            // Native exits can disappear from their live LINON-backed list while
            // Cloud is transitioning through a ladder. Retain only the already
            // locked target, and only until the mounted transition resolves.
            target = lockedTarget;
        }

        if (target is null)
        {
            FieldNavigationActionResult completion;
            if (interactionArrivalPaused)
            {
                completion = CompleteNavigation(
                    $"{beaconTargetLabel} completed. Navigation off.",
                    "native target removed after interaction arrival",
                    completed: true);
            }
            else if (beaconCategory == FieldNavigationCategory.Exits)
            {
                completion = CompleteNavigation(
                    $"{beaconTargetLabel} is no longer reachable. Navigation off.",
                    "native exit route blocked",
                    completed: false);
            }
            else
            {
                completion = CompleteNavigation(
                    $"{beaconTargetLabel} no longer available. Navigation off.",
                    "native target removed",
                    completed: false);
            }

            RecoverCategoryAfterTargetRemoval(position);
            return completion;
        }

        if (positionContinuityTracker.Observe(
                position,
                observedAt,
                out var continuityDiagnostic))
        {
            ResetRouteForPositionDiscontinuity(continuityDiagnostic);
            return null;
        }

        lastAcceptedPosition = position;

        if (routeRefreshPending)
        {
            currentGuidance = null;
            if (!activeLadderState.IsMounted)
            {
                pendingLadderAction = null;
                ResetLadderMountPrompt();
            }

            routeRefreshPending = false;
        }

        var observation = movementObserver.Observe(position, input, controlTransform, isSuppressed: false);
        velocityEstimator.Observe(position, observedAt, isSuppressed: false);

        var completesFromLiveLanding =
            activeLadderState.IsMounted &&
            activeLadderHasExpectedLanding &&
            IsAtLadderLanding(
                position,
                activeLadderExpectedLanding,
                activeLadderExpectedTriangle) &&
            (!ladderState.IsUsable ||
             !ladderState.IsMounted ||
             ladderState.Phase == FieldLadderPhase.Completing);

        if (ladderState.IsUsable && ladderState.IsMounted && !completesFromLiveLanding)
        {
            CapturePendingLadderAction(currentGuidance);
            if (ShouldAcceptMountedLadder(ladderState))
            {
                velocityEstimator.Reset();
                return UpdateMountedLadder(position, target.Value, observation, controlTransform, ladderState, observedAt);
            }

            LastNavigationDiagnostic =
                $"ignored native mounted sample that does not own the active route ladder, " +
                $"input={ladderState.RequiredInput}, " +
                $"target={ladderState.Target.X},{ladderState.Target.Y},{ladderState.Target.Z}, " +
                $"triangle={ladderState.TargetTriangle}";
            ladderState = FieldLadderStateSnapshot.NotMounted;
        }

        if (!ladderState.IsUsable && activeLadderState.IsMounted && !completesFromLiveLanding)
        {
            LastNavigationDiagnostic = "native ladder state temporarily unavailable; route remains frozen";
            return null;
        }

        if (activeLadderState.IsMounted &&
            (completesFromLiveLanding || ladderState.IsUsable && !ladderState.IsMounted))
        {
            if (pendingLadderAction is { } pending &&
                activeLadderHasExpectedLanding &&
                !IsAtLadderLanding(
                    position,
                    activeLadderExpectedLanding,
                    activeLadderExpectedTriangle) &&
                !IsNear(position, pending.Waypoint, LadderLandingArrivalDistance))
            {
                LastNavigationDiagnostic =
                    $"native ladder state flickered between endpoints; route remains frozen, " +
                    $"position={position.X},{position.Y},{position.Z}, triangle={position.TriangleId}";
                return null;
            }

            if (routeStartsAfterMountedLadder)
            {
                routeStartsAfterMountedLadder = false;
                activeLadderState = FieldLadderStateSnapshot.NotMounted;
                activeLadderGuidanceInput = FieldNavigationInput.None;
                activeLadderExpectedLanding = default;
                activeLadderExpectedTriangle = -1;
                activeLadderHasExpectedLanding = false;
                pendingLadderAction = null;
                routeTracker?.Reset();
                currentGuidance = null;
                UpdateRouteProgress(position, target.Value, observation, observedAt);
                return new FieldNavigationActionResult("Ladder complete. Navigation resumed.");
            }

            var ladderCompletion = CompleteMountedLadder(
                position,
                out var completedForwardAction);
            FieldNavigationRouteGuidance postActionGuidance = default;
            var actionAdvanced =
                completedForwardAction is { } completed &&
                routeTracker is not null &&
                routeTracker.TryCompleteAction(
                    completed,
                    position,
                    out postActionGuidance);
            if (actionAdvanced)
            {
                currentGuidance = postActionGuidance;
                RestartProgressSegment(ResolveProgressRemainingDistance(postActionGuidance));
                CapturePendingLadderAction(postActionGuidance);
                LastNavigationDiagnostic = postActionGuidance.Diagnostic;
            }
            else
            {
                routeTracker?.Reset();
                currentGuidance = null;
                UpdateRouteProgress(position, target.Value, observation, observedAt);
            }

            return ladderCompletion;
        }

        if (ladderState.IsUsable)
        {
            activeLadderState = ladderState;
        }

        UpdateRouteProgress(position, target.Value, observation, observedAt);

        var routeAction = CreateRouteActionSpeech(position, observedAt);
        if (routeAction is not null)
        {
            return routeAction;
        }

        var isWithinArrivalDistance = pendingLadderAction is null &&
            IsWithinArrivalDistance(
                position,
                target.Value,
                arrivalDistanceUnits,
                currentGuidance);
        var isAtCompletionPoint = pendingLadderAction is null &&
            IsWithinArrivalDistance(
                position,
                target.Value,
                arrivalDistanceUnits,
                currentGuidance,
                forCompletion: true);
        var canCompleteOnArrival = CanCompleteOnArrival(target.Value);
        if (pendingLadderAction is null &&
            canCompleteOnArrival &&
            isAtCompletionPoint)
        {
            return CompleteNavigation(
                $"{beaconTargetLabel} reached. Navigation off.",
                $"{beaconCategory} arrival",
                completed: true);
        }

        if (pendingLadderAction is null &&
            !canCompleteOnArrival &&
            !CompletesByCrossing(target.Value))
        {
            if (interactionArrivalPaused)
            {
                var resumeDistance = ResolveInteractionArrivalResumeDistance();
                if (IsWithinArrivalDistance(
                        position,
                        target.Value,
                        resumeDistance,
                        guidance: null))
                {
                    return null;
                }

                interactionArrivalPaused = false;
                interactionArrivalDistance = 0;
                var resumedGuidance = CreateSpokenGuidance(
                    position,
                    controlTransform,
                    arrivalDistanceUnits);
                LastNavigationDiagnostic =
                    $"{LastNavigationDiagnostic}, interaction arrival exited, navigation resumed";
                return new FieldNavigationActionResult(
                    resumedGuidance is null
                        ? "Navigation resumed."
                        : $"Navigation resumed. {resumedGuidance.Value.Speech}.");
            }

            if (isWithinArrivalDistance)
            {
                interactionArrivalPaused = true;
                interactionArrivalDistance = ResolveArrivalDistance(
                    target.Value,
                    arrivalDistanceUnits);
                LastNavigationDiagnostic =
                    $"{LastNavigationDiagnostic}, interaction arrival paused at " +
                    $"{interactionArrivalDistance} units";
                return new FieldNavigationActionResult(
                    target.Value.Activation == FieldNavigationActivation.Contact
                        // Nothing is pressed here. The script runs because the party
                        // walked into it, which is what a sighted player does when the
                        // materia is the only thing in the room.
                        ? $"{beaconTargetLabel} reached. Walk into it. Navigation paused."
                        : $"{beaconTargetLabel} reached. Interact here. Navigation paused.");
            }
        }

        return null;
    }

    private int ResolveInteractionArrivalResumeDistance()
    {
        var threshold = Math.Max(0, interactionArrivalDistance);
        var hysteresis = Math.Max(32, threshold / 2);
        return (int)Math.Min(int.MaxValue, threshold + (long)hysteresis);
    }

    private bool CanCompleteOnArrival(FieldNavigationTarget target)
    {
        // A normal one-way exit route ends at the exit itself. Repeating
        // same-field trigger lines deliberately leave CompletesOnArrival false
        // so routes such as the winding tunnel survive their intermediate wrap.
        if (beaconCategory == FieldNavigationCategory.Exits &&
            target.CompletesOnArrival)
        {
            return true;
        }

        if (beaconCompletesOnFieldTransition &&
            beaconDestinationFieldIds.Length > 0)
        {
            return false;
        }

        if (target.Category == FieldNavigationCategory.Story &&
            !target.CompletesOnArrival)
        {
            return false;
        }

        return target.TriggerLine is not null
            ? target.CompletesOnArrival
            : target.Category != FieldNavigationCategory.Objects ||
              target.CompletesOnArrival;
    }

    private FieldNavigationActionResult? CreateRouteActionSpeech(
        FieldPositionSnapshot position,
        DateTime observedAt)
    {
        CapturePendingLadderAction(currentGuidance);
        if (pendingLadderAction is not { } action)
        {
            ResetLadderMountPrompt();
            return null;
        }

        var dx = action.Waypoint.X - position.X;
        var dy = action.Waypoint.Y - position.Y;
        var dz = action.Waypoint.Z - position.Z;
        var thresholdSquared = LadderActionArrivalDistance * (double)LadderActionArrivalDistance;
        if (dx * (double)dx + dy * (double)dy + dz * (double)dz > thresholdSquared)
        {
            ResetLadderMountPrompt(action.StableId);
            return null;
        }

        if (!string.Equals(ladderPromptActionId, action.StableId, StringComparison.Ordinal))
        {
            ladderPromptActionId = action.StableId;
            nextLadderPromptAt = DateTime.MinValue;
        }

        if (observedAt < nextLadderPromptAt)
        {
            return null;
        }

        nextLadderPromptAt = observedAt + LadderMountPromptInterval;
        LastNavigationDiagnostic =
            $"{currentGuidance?.Diagnostic ?? "pending ladder route"}, action announced={action.StableId}";
        var direction = action.RequiredInput switch
        {
            FieldNavigationInput.Up => "up",
            FieldNavigationInput.Right => "right",
            FieldNavigationInput.Down => "down",
            FieldNavigationInput.Left => "left",
            _ => null
        };
        return new FieldNavigationActionResult(
            action.RequiresAction
                ? direction is null
                    ? "Ladder. Press action to climb."
                    : $"Ladder. Press action to mount, then climb {direction}."
                : direction is null
                    ? "Climb the ladder."
                    : $"Climb {direction}.");
    }

    public FieldNavigationActionResult? CreateSpokenGuidance(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        int arrivalDistanceUnits,
        int predictionHorizonMs = 0,
        FieldLadderStateSnapshot ladderState = default)
    {
        // Being on a ladder is worth saying whether or not the player asked for a route
        // to it. Some fields start the climb themselves: coming back down from the
        // rocket cabin the party lands on a two-triangle ledge and the field immediately
        // puts them on a ladder facing down, and with no route running there was nothing
        // at all to say which way it goes or that turning round returns to the gantry.
        // A sighted player can see the ladder under their character; this is the same
        // information.
        // A route that owns the climb knows which way the player wants to go, and says
        // only that: offering the way back would be inviting them off their own route.
        if (BeaconEnabled && activeLadderState.IsMounted)
        {
            var routedDirection = FormatInputDirection(activeLadderGuidanceInput);
            return routedDirection is null
                ? new FieldNavigationActionResult("climb the ladder")
                : new FieldNavigationActionResult($"climb {routedDirection}");
        }

        // With no route, the ladder is all there is to say, and both ways off it matter -
        // the player did not choose to be here and may well want to go back.
        if (ladderState is { IsUsable: true, IsMounted: true })
        {
            var climbDirection = FormatInputDirection(ladderState.RequiredInput);
            if (climbDirection is null)
            {
                return new FieldNavigationActionResult("climb the ladder");
            }

            var back = FormatInputDirection(Opposite(ladderState.RequiredInput));
            return new FieldNavigationActionResult(
                back is null
                    ? $"climb {climbDirection}"
                    : $"climb {climbDirection}, or {back} to go back");
        }

        if (!BeaconEnabled ||
            interactionArrivalPaused ||
            !FieldPositionReader.IsUsable(position) ||
            position.FieldId != beaconFieldId ||
            currentGuidance is null)
        {
            return null;
        }

        var target = GetBeaconTarget(position);
        if (target is null ||
            IsWithinArrivalDistance(position, target.Value, arrivalDistanceUnits, currentGuidance, forCompletion: true))
        {
            return null;
        }

        var waypoint = ResolveGuidanceWaypoint(currentGuidance.Value);
        var speech = FormatSpokenRoute(
            position,
            waypoint,
            currentGuidance.Value,
            routeTracker,
            controlTransform,
            out var connectedDirection);
        if (string.Equals(speech, "at destination", StringComparison.Ordinal))
        {
            return null;
        }

        var predictiveTurn = CreatePredictiveTurnSpeech(
            position,
            controlTransform,
            currentGuidance.Value,
            waypoint,
            connectedDirection,
            predictionHorizonMs);
        return new FieldNavigationActionResult(
            predictiveTurn is null
                ? speech
                : $"{speech}, then {predictiveTurn}");
    }

    /// <summary>
    /// Resolves the same screen-relative direction used by spoken navigation
    /// without mutating route state. Interaction points deliberately return no
    /// input so auto walk cannot press through a ladder or object prompt.
    /// </summary>
    public bool TryResolveAutomaticInput(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        int arrivalDistanceUnits,
        out FieldNavigationInput input)
    {
        input = FieldNavigationInput.None;
        LastAutomaticInputHold = FieldAutoWalkHoldReason.NoRoute;
        if (BeaconEnabled && activeLadderState.IsMounted)
        {
            input = activeLadderGuidanceInput;
            if (IsDirectionalInput(input))
            {
                LastAutomaticInputHold = FieldAutoWalkHoldReason.None;
                return true;
            }

            // On a ladder with nothing to press is the ladder's own business - the player
            // gets off it by hand - and never a route that cannot find its way.
            LastAutomaticInputHold = FieldAutoWalkHoldReason.PlayerAction;
            return false;
        }

        if (!BeaconEnabled ||
            !FieldPositionReader.IsUsable(position) ||
            position.FieldId != beaconFieldId ||
            currentGuidance is null)
        {
            return false;
        }

        if (interactionArrivalPaused)
        {
            // Stopped at an interaction point on purpose. Auto walk must not press it, so
            // standing here is the route working, not failing.
            LastAutomaticInputHold = FieldAutoWalkHoldReason.PlayerAction;
            return false;
        }

        var target = GetBeaconTarget(position);
        if (target is null)
        {
            return false;
        }

        if (IsWithinArrivalDistance(position, target.Value, arrivalDistanceUnits, currentGuidance, forCompletion: true))
        {
            LastAutomaticInputHold = FieldAutoWalkHoldReason.Arrived;
            return false;
        }

        if (pendingLadderAction is { RequiresAction: true } action &&
            IsNear(position, action.Waypoint, LadderActionArrivalDistance))
        {
            LastAutomaticInputHold = FieldAutoWalkHoldReason.PlayerAction;
            return false;
        }

        ObserveAutomaticPace(position);
        var waypoint = ResolveGuidanceWaypoint(currentGuidance.Value);
        var recommendation = movementObserver.ResolveStickDirection(
            waypoint.X - position.X,
            waypoint.Y - position.Y,
            controlTransform);
        input = recommendation.Input;
        if (IsDirectionalInput(input) && routePlanner is IFieldNavigationAutomaticMovementPlanner nativePlanner)
        {
            var deltaX = waypoint.X - position.X;
            var deltaY = waypoint.Y - position.Y;
            var distance = Math.Sqrt(deltaX * (double)deltaX + deltaY * (double)deltaY);
            bool IsClear(FieldNavigationInput candidate, double probeDistance)
            {
                var (x, y) = FieldNavigationMovementObserver.PredictWorldDirection(candidate, controlTransform);
                var candidateDestination = new FieldNavigationRouteWaypoint(
                    position.X + (int)Math.Round(x * probeDistance),
                    position.Y + (int)Math.Round(y * probeDistance), position.Z);
                var nativeBaseAngle = candidate switch
                {
                    FieldNavigationInput.Up => 0, FieldNavigationInput.UpRight => -32,
                    FieldNavigationInput.Right => -64, FieldNavigationInput.DownRight => -96,
                    FieldNavigationInput.Down => 128, FieldNavigationInput.DownLeft => 96,
                    FieldNavigationInput.Left => 64, FieldNavigationInput.UpLeft => 32,
                    _ => 0
                };
                return currentGuidance.Value.UsesNativeProbeClearance
                    ? nativePlanner.IsNativeProbeAutomaticMovementClear(position, target.Value, candidateDestination,
                        unchecked((byte)(controlTransform.SignedControlDirection + nativeBaseAngle)))
                    : nativePlanner.IsAutomaticMovementClear(position, target.Value, candidateDestination);
            }

            // A clear diagonal route can round to a blocked cardinal input.
            // Keep the closest forward input whose native movement probes clear.
            var recommended = input;
            FieldNavigationInput ChooseClear(double probeDistance)
            {
                if (IsClear(recommended, probeDistance))
                {
                    return recommended;
                }

                return Enum.GetValues<FieldNavigationInput>()
                    .Where(IsDirectionalInput)
                    .Select(candidate => (Input: candidate,
                        World: FieldNavigationMovementObserver.PredictWorldDirection(candidate, controlTransform)))
                    .Select(candidate => (candidate.Input,
                        Progress: candidate.World.X * deltaX + candidate.World.Y * deltaY))
                    .Where(candidate => candidate.Progress > 0d)
                    .OrderByDescending(candidate => candidate.Progress)
                    .Select(candidate => candidate.Input)
                    .FirstOrDefault(candidate => IsClear(candidate, probeDistance));
            }

            // Only when the party is actually stuck. A direction that is clear for the
            // eight units this checks and blocked at twenty is how a running party ends
            // up standing against a wall while auto walk keeps asking for it - so once a
            // sample has produced no movement, look as far as the party was moving before
            // it stopped and prefer a direction that stays clear that far.
            //
            // A party that is moving keeps the eight-unit probe exactly as before. That
            // matters: threading a narrow doorway needs a direction that is clear through
            // the gap and no further, and demanding a running step of clearance would
            // reject it.
            var reach = automaticMovementStalled
                ? Math.Min(Math.Max(NativeProbeDistance, lookaheadDistance), distance)
                : Math.Min(NativeProbeDistance, distance);
            var chosen = ChooseClear(reach);

            // Nothing clear that far out is not a reason to give up: the eight-unit answer
            // is what this always did, and auto walk must always come away with a
            // direction. So the longer reach can only ever change which of the directions
            // already on the table is preferred.
            input = IsDirectionalInput(chosen) || reach <= NativeProbeDistance
                ? chosen
                : ChooseClear(Math.Min(NativeProbeDistance, distance));
        }

        if (IsDirectionalInput(input))
        {
            LastAutomaticInputHold = FieldAutoWalkHoldReason.None;
            return true;
        }

        // A route, a waypoint, somewhere to be - and not one direction toward it came back
        // clear. That is the party wedged against something, and it is measurable.
        LastAutomaticInputHold = FieldAutoWalkHoldReason.NoClearDirection;
        return false;
    }

    /// <summary>
    /// How far the party moved between this automatic sample and the last one.
    ///
    /// <para>This is the distance the clearance probe has to cover, and the session that
    /// prompted it measured both paces: about sixteen units a sample walking and
    /// forty-five to forty-eight running. It decays rather than dropping to nothing when
    /// the party stops, because a party stopped by a wall is precisely when the longer
    /// reach is needed to find a way round it, and a jump larger than one running sample
    /// is a map change or a script placement rather than a walk.</para>
    /// </summary>
    private void ObserveAutomaticPace(FieldPositionSnapshot position)
    {
        var previous = lastAutomaticPosition;
        lastAutomaticPosition = position;
        if (previous is not { } from || from.FieldId != position.FieldId)
        {
            lookaheadDistance = 0d;
            automaticMovementStalled = false;
            return;
        }

        var dx = position.X - (double)from.X;
        var dy = position.Y - (double)from.Y;
        var step = Math.Sqrt((dx * dx) + (dy * dy));
        automaticMovementStalled = step < StalledMovementDistance;
        if (!automaticMovementStalled)
        {
            // Only a sample that actually moved says anything about the pace. A stalled
            // one keeps the pace it was travelling at, which is the distance the probe
            // needs to cover to find the way round whatever stopped it.
            lookaheadDistance = Math.Min(step, MaximumLookaheadDistance);
        }
    }

    private void ResetAutomaticPace()
    {
        lastAutomaticPosition = null;
        lookaheadDistance = 0d;
        automaticMovementStalled = false;
    }

    private FieldNavigationActionResult? UpdateMountedLadder(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationMovementObservation observation,
        FieldNavigationControlTransform controlTransform,
        FieldLadderStateSnapshot ladderState,
        DateTime observedAt)
    {
        ResetLadderMountPrompt();
        FieldNavigationRouteGuidance? liveRouteGuidance = null;
        // A mounted climb is not player-steered walking, so the route must be held
        // rather than re-evaluated. See FieldNavigationRouteTracker.TryHold.
        if (!routeStartsAfterMountedLadder &&
            routeTracker?.TryHold(
                position,
                target,
                out var updatedGuidance) == true)
        {
            liveRouteGuidance = updatedGuidance;
            currentGuidance = updatedGuidance;
            ObserveProgress(ResolveProgressRemainingDistance(updatedGuidance));
        }
        else if (routeTracker?.TryMeasureRemainingDistance(position, out var remainingDistance) == true)
        {
            ObserveProgress(remainingDistance);
        }

        if (!routeStartsAfterMountedLadder)
        {
            CapturePendingLadderAction(currentGuidance);
        }
        var wasMounted = activeLadderState.IsMounted;
        activeLadderState = ladderState;
        activeLadderGuidanceInput = ladderState.RequiredInput;
        var guidanceSource = "native";
        activeLadderExpectedLanding = ladderState.Target;
        activeLadderExpectedTriangle = ladderState.TargetTriangle;
        activeLadderHasExpectedLanding = true;

        if (!routeStartsAfterMountedLadder && pendingLadderAction is { } pending)
        {
            activeLadderExpectedLanding = pending.Destination;
            activeLadderExpectedTriangle = pending.DestinationTriangle;
            if (pending.RequiredInput != FieldNavigationInput.None)
            {
                activeLadderGuidanceInput = pending.RequiredInput;
                guidanceSource = "route action";
            }
        }
        else if (lastCompletedLadderAction is { } completed &&
                 IsNear(position, completed.Destination, LadderLandingArrivalDistance) &&
                 IsNear(ladderState.Target, completed.Waypoint, LadderEndpointMatchDistance))
        {
            // The player remounted the ladder from its destination side. The opposite
            // native input returns to the side the active route expects.
            activeLadderExpectedLanding = completed.Destination;
            activeLadderExpectedTriangle = completed.DestinationTriangle;
            activeLadderGuidanceInput = Opposite(ladderState.RequiredInput);
            guidanceSource = "reverse route action";
        }
        else if (!routeStartsAfterMountedLadder &&
                 pendingLadderAction is null &&
                 !IsDirectionalInput(ladderState.RequiredInput) &&
                 liveRouteGuidance is { } routeGuidance)
        {
            // Some field walkmeshes, notably Floor 63's crawlspace, report the
            // same native movement mode as a ladder even though the route bends
            // across several horizontal and vertical segments. Keep advancing
            // the actual route and let its live waypoint own each turn.
            //
            // Only when the native climb has no direction of its own. The native
            // input is the button the player must physically hold and outranks
            // any direction inferred from route geometry. A reroute at the moment
            // of mounting clears pendingLadderAction, and the route's next
            // waypoint is then still the mount approach behind the player;
            // deriving a direction from that told players to "climb left" onto
            // Fort Condor's entrance ladder, which the native state reports as Up.
            var waypoint = ResolveGuidanceWaypoint(routeGuidance);
            var routeInput = movementObserver.ResolveStickDirection(
                waypoint.X - position.X,
                waypoint.Y - position.Y,
                controlTransform).Input;
            if (IsDirectionalInput(routeInput))
            {
                activeLadderGuidanceInput = routeInput;
                guidanceSource = "live route";
            }
        }

        LastNavigationDiagnostic =
            $"ladder mounted, phase={ladderState.Phase}, input={activeLadderGuidanceInput}, " +
            $"source={guidanceSource}, " +
            $"nativeTarget={ladderState.Target.X},{ladderState.Target.Y},{ladderState.Target.Z}, " +
            $"expectedLanding={activeLadderExpectedLanding.X},{activeLadderExpectedLanding.Y},{activeLadderExpectedLanding.Z}";
        if (wasMounted)
        {
            return null;
        }

        var direction = FormatInputDirection(activeLadderGuidanceInput);
        return new FieldNavigationActionResult(
            direction is null
                ? "Ladder mounted. Climb the ladder."
                : $"Ladder mounted. Climb {direction}.");
    }

    private FieldNavigationActionResult CompleteMountedLadder(
        FieldPositionSnapshot position,
        out FieldNavigationRouteAction? completedForwardAction)
    {
        completedForwardAction = null;
        var reachedExpectedLanding = !activeLadderHasExpectedLanding ||
            IsAtLadderLanding(position, activeLadderExpectedLanding, activeLadderExpectedTriangle);
        var speech = "Ladder complete.";
        if (pendingLadderAction is { } pending)
        {
            if (reachedExpectedLanding)
            {
                lastCompletedLadderAction = pending;
                completedForwardAction = pending;
                pendingLadderAction = null;
            }
            else
            {
                speech = "Back at ladder entrance. Press action to climb.";
            }

        }

        ResetLadderMountPrompt();
        activeLadderState = FieldLadderStateSnapshot.NotMounted;
        activeLadderGuidanceInput = FieldNavigationInput.None;
        activeLadderExpectedLanding = default;
        activeLadderExpectedTriangle = -1;
        activeLadderHasExpectedLanding = false;
        routeStartsAfterMountedLadder = false;
        LastNavigationDiagnostic =
            $"ladder dismounted, expectedLandingReached={reachedExpectedLanding}, " +
            $"position={position.X},{position.Y},{position.Z}, triangle={position.TriangleId}";
        return new FieldNavigationActionResult(speech);
    }

    private void CapturePendingLadderAction(FieldNavigationRouteGuidance? guidance)
    {
        if (guidance?.NextAction is not { Kind: FieldNavigationTransitionKind.Ladder } action)
        {
            return;
        }

        if (lastCompletedLadderAction is { } completed &&
            IsSameLadderTraversal(action, completed))
        {
            return;
        }

        if (pendingLadderAction is null ||
            (!activeLadderState.IsMounted &&
             !string.Equals(pendingLadderAction.Value.StableId, action.StableId, StringComparison.Ordinal)))
        {
            pendingLadderAction = action;
            ResetLadderMountPrompt(action.StableId);
        }
    }

    private bool ShouldAcceptMountedLadder(FieldLadderStateSnapshot ladderState)
    {
        if (activeLadderState.IsMounted || routeStartsAfterMountedLadder)
        {
            return true;
        }

        if (pendingLadderAction is not { } pending)
        {
            return lastCompletedLadderAction is null;
        }

        return (pending.DestinationTriangle >= 0 &&
                ladderState.TargetTriangle == pending.DestinationTriangle) ||
               IsNear(ladderState.Target, pending.Destination, LadderEndpointMatchDistance) ||
               IsNear(ladderState.Target, pending.Waypoint, LadderEndpointMatchDistance);
    }

    private static bool IsSameLadderTraversal(
        FieldNavigationRouteAction candidate,
        FieldNavigationRouteAction completed)
    {
        if (string.Equals(candidate.StableId, completed.StableId, StringComparison.Ordinal))
        {
            return true;
        }

        var sameDirection =
            IsNear(candidate.Waypoint, completed.Waypoint, CompletedLadderEndpointMatchDistance) &&
            IsNear(candidate.Destination, completed.Destination, CompletedLadderEndpointMatchDistance);
        var reverseDirection =
            IsNear(candidate.Waypoint, completed.Destination, CompletedLadderEndpointMatchDistance) &&
            IsNear(candidate.Destination, completed.Waypoint, CompletedLadderEndpointMatchDistance);
        return sameDirection || reverseDirection;
    }

    private void ResetLadderMountPrompt(string retainedActionId = "")
    {
        ladderPromptActionId = retainedActionId;
        nextLadderPromptAt = DateTime.MinValue;
    }

    private FieldNavigationRouteWaypoint ResolveGuidanceWaypoint(FieldNavigationRouteGuidance guidance)
    {
        if (pendingLadderAction is { } action &&
            (action.PortalIndex < 0 || guidance.PortalIndex >= action.PortalIndex))
        {
            return action.Waypoint;
        }

        return guidance.Waypoint;
    }

    private string? CreatePredictiveTurnSpeech(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        FieldNavigationRouteGuidance guidance,
        FieldNavigationRouteWaypoint currentWaypoint,
        string currentDirection,
        int predictionHorizonMs)
    {
        if (predictionHorizonMs <= 0 ||
            routeTracker is null ||
            pendingLadderAction is not null ||
            !routeTracker.TryGetUpcomingStep(out var nextStep) ||
            !velocityEstimator.TryGetEstimate(out var velocity))
        {
            return null;
        }

        if (guidance.NextAction is { } nextAction &&
            nextAction.PortalIndex <= nextStep.RequiredPortalIndex)
        {
            return null;
        }

        if (!FieldNavigationPredictiveTurnResolver.TryResolve(
                position,
                currentWaypoint,
                nextStep.Waypoint,
                velocity,
                predictionHorizonMs,
                out var turn))
        {
            return null;
        }

        var nextX = turn.Waypoint.X - currentWaypoint.X;
        var nextY = turn.Waypoint.Y - currentWaypoint.Y;
        var recommendation = movementObserver.ResolveStickDirection(
            nextX,
            nextY,
            controlTransform);
        var direction = FieldNavigationSpokenCueFormatter.TryResolveSegment(
            nextX,
            nextY,
            controlTransform,
            FieldNavigationSpokenCueFormatter.DefaultDistanceUnitsPerCount,
            out var predictiveSegment)
            ? predictiveSegment.Direction
            : null;
        if (direction is null ||
            string.Equals(direction, currentDirection, StringComparison.Ordinal))
        {
            return null;
        }

        LastNavigationDiagnostic =
            $"{LastNavigationDiagnostic}, predictiveTurn={direction}, {turn.Diagnostic}";
        return direction;
    }

    private static bool IsAtLadderLanding(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint landing,
        int destinationTriangle) =>
        (destinationTriangle >= 0 && position.TriangleId == destinationTriangle) ||
        IsNear(position, landing, LadderLandingArrivalDistance);

    private static bool IsNear(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint waypoint,
        int distance) =>
        IsNear(new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z), waypoint, distance);

    private static bool IsNear(
        FieldNavigationRouteWaypoint first,
        FieldNavigationRouteWaypoint second,
        int distance)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        return dx * (double)dx + dy * (double)dy + dz * (double)dz <= distance * (double)distance;
    }

    private static string? FormatInputDirection(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => "up",
        FieldNavigationInput.Right => "right",
        FieldNavigationInput.Down => "down",
        FieldNavigationInput.Left => "left",
        _ => null
    };

    private static FieldNavigationInput Opposite(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => FieldNavigationInput.Down,
        FieldNavigationInput.Right => FieldNavigationInput.Left,
        FieldNavigationInput.Down => FieldNavigationInput.Up,
        FieldNavigationInput.Left => FieldNavigationInput.Right,
        _ => FieldNavigationInput.None
    };

    private void UpdateRouteProgress(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationMovementObservation observation,
        DateTime observedAt)
    {
        if (routeTracker is null)
        {
            return;
        }

        var startsRemainingSegment = currentGuidance is null;
        var routeAvailable = startsRemainingSegment
            ? routeTracker.TryStart(position, target, out var guidance)
            : routeTracker.TryUpdate(position, target, observation, observedAt, out guidance);
        if (!routeAvailable)
        {
            currentGuidance = null;
            LastNavigationDiagnostic = $"route unavailable, field={position.FieldId}, target={beaconTargetId}";
            return;
        }

        currentGuidance = guidance;
        if (!routeProgressTracker.Active)
        {
            StartRouteProgress(ResolveProgressRemainingDistance(guidance));
        }
        else if (startsRemainingSegment || guidance.Replanned)
        {
            RestartProgressSegment(ResolveProgressRemainingDistance(guidance));
        }

        ObserveProgress(ResolveProgressRemainingDistance(guidance));
        CapturePendingLadderAction(guidance);
        LastNavigationDiagnostic = $"{guidance.Diagnostic}, {observation.Diagnostic}";
    }

    private void ResetRouteForPositionDiscontinuity(string diagnostic)
    {
        routeTracker?.Reset();
        currentGuidance = null;
        pendingLadderAction = null;
        lastCompletedLadderAction = null;
        activeLadderState = default;
        activeLadderGuidanceInput = FieldNavigationInput.None;
        activeLadderExpectedLanding = default;
        activeLadderExpectedTriangle = -1;
        activeLadderHasExpectedLanding = false;
        routeStartsAfterMountedLadder = false;
        routeRefreshPending = false;
        interactionArrivalPaused = false;
        interactionArrivalDistance = 0;
        lastAcceptedPosition = null;
        ResetLadderMountPrompt();
        movementObserver.Reset();
        ResetAutomaticPace();
        velocityEstimator.Reset();
        LastNavigationDiagnostic = $"navigation route reset for {diagnostic}";
    }

    private void BeginPositionRecovery(string diagnostic)
    {
        if (!positionRecoveryPending)
        {
            positionRecoveryAnchor =
                lastAcceptedPosition is { } accepted &&
                (accepted.X != 0 || accepted.Y != 0 || accepted.Z != 0)
                    ? accepted
                    : null;
        }

        positionRecoveryPending = true;
        positionRecoveryCandidate = null;
        positionRecoveryCandidateObservedAt = default;
        positionContinuityTracker.Reset();
        velocityEstimator.Reset();
        movementObserver.Reset();
        ResetAutomaticPace();

        if (!routeRefreshPending)
        {
            routeTracker?.Reset();
            routeRefreshPending = true;
        }

        LastNavigationDiagnostic = $"navigation position recovery armed, reason={diagnostic}";
    }

    private bool TryCompletePositionRecovery(
        FieldPositionSnapshot position,
        DateTime observedAt)
    {
        if (!positionRecoveryPending)
        {
            return true;
        }

        currentGuidance = null;
        if (!activeLadderState.IsMounted)
        {
            pendingLadderAction = null;
            ResetLadderMountPrompt();
        }

        if (positionRecoveryAnchor is { } anchor &&
            anchor.FieldId == position.FieldId &&
            anchor.ModelIndex == position.ModelIndex &&
            position.X == 0 &&
            position.Y == 0 &&
            position.Z == 0)
        {
            positionRecoveryCandidate = null;
            positionRecoveryCandidateObservedAt = default;
            LastNavigationDiagnostic =
                $"navigation position recovery quarantined hydration origin, " +
                $"field={position.FieldId}, model={position.ModelIndex}, " +
                $"anchor={anchor.X},{anchor.Y},{anchor.Z}";
            return false;
        }

        if (positionRecoveryCandidate is not { } candidate)
        {
            positionRecoveryCandidate = position;
            positionRecoveryCandidateObservedAt = observedAt;
            LastNavigationDiagnostic =
                $"navigation position recovery candidate, " +
                $"field={position.FieldId}, model={position.ModelIndex}, " +
                $"position={position.X},{position.Y},{position.Z}";
            return false;
        }

        var identityChanged =
            candidate.FieldId != position.FieldId ||
            candidate.ModelIndex != position.ModelIndex;
        var isDiscontinuous = FieldNavigationPositionContinuity.IsDiscontinuous(
            candidate,
            positionRecoveryCandidateObservedAt,
            position,
            observedAt,
            out var discontinuityDiagnostic);
        if (identityChanged || isDiscontinuous)
        {
            positionRecoveryCandidate = position;
            positionRecoveryCandidateObservedAt = observedAt;
            LastNavigationDiagnostic = identityChanged
                ? $"navigation position recovery candidate replaced, " +
                  $"field={position.FieldId}, model={position.ModelIndex}"
                : $"navigation position recovery candidate replaced for {discontinuityDiagnostic}";
            return false;
        }

        var stableDuration = observedAt - positionRecoveryCandidateObservedAt;
        if (stableDuration < PositionRecoveryStableWindow)
        {
            LastNavigationDiagnostic =
                $"navigation position recovery stabilizing, " +
                $"field={position.FieldId}, model={position.ModelIndex}, " +
                $"elapsedMs={stableDuration.TotalMilliseconds:0}";
            return false;
        }

        positionRecoveryPending = false;
        positionRecoveryCandidate = null;
        positionRecoveryCandidateObservedAt = default;
        positionRecoveryAnchor = null;
        positionContinuityTracker.Reset();
        LastNavigationDiagnostic =
            $"navigation position recovery coherent, " +
            $"field={position.FieldId}, model={position.ModelIndex}, " +
            $"position={position.X},{position.Y},{position.Z}";
        return true;
    }

    private FieldNavigationActionResult CompleteNavigation(
        string speech,
        string reason,
        bool completed)
    {
        var target = beaconTargetId;
        if (completed)
        {
            CompleteRouteProgress();
            ResetCore(deactivateProgress: false);
        }
        else
        {
            Reset();
        }

        LastNavigationDiagnostic = $"navigation completion, target={target}, reason={reason}";
        return new FieldNavigationActionResult(speech);
    }

    private void StartRouteProgress(double remainingDistance)
    {
        var percent = routeProgressTracker.Start(remainingDistance);
        PublishProgress(sink => sink.Activate(percent));
    }

    private void RestartProgressSegment(double remainingDistance)
    {
        routeProgressTracker.BeginRemainingSegment(remainingDistance);
    }

    private void ObserveProgress(double remainingDistance)
    {
        var previous = routeProgressTracker.Percent;
        var percent = routeProgressTracker.Observe(remainingDistance);
        if (percent != previous)
        {
            PublishProgress(sink => sink.SetValue(percent));
        }
    }

    private static double ResolveProgressRemainingDistance(
        FieldNavigationRouteGuidance guidance) =>
        double.IsFinite(guidance.ProgressRemainingDistance)
            ? Math.Max(0d, guidance.ProgressRemainingDistance)
            : Math.Max(0d, guidance.RemainingDistance);

    private void CompleteRouteProgress()
    {
        routeProgressTracker.Complete();
        PublishProgress(sink => sink.Complete());
    }

    private void PublishProgress(Action<IFieldNavigationProgressSink> publish)
    {
        if (routeProgressSink is null)
        {
            return;
        }

        try
        {
            publish(routeProgressSink);
        }
        catch
        {
            // Route speech and navigation remain usable if the optional native
            // presentation window cannot accept an update.
        }
    }

    private static bool IsDirectionalInput(FieldNavigationInput input) =>
        input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;

    private void MoveCategory(int delta)
    {
        categoryIndex = (categoryIndex + delta) % CategoryOrder.Length;
        if (categoryIndex < 0)
        {
            categoryIndex += CategoryOrder.Length;
        }
    }

    private void RecoverCategoryAfterTargetRemoval(FieldPositionSnapshot position)
    {
        if (source.GetTargets(position, CurrentCategory).Count > 0)
        {
            return;
        }

        foreach (var category in RemovedTargetRecoveryCategoryOrder)
        {
            if (source.GetTargets(position, category).Count == 0)
            {
                continue;
            }

            categoryIndex = Array.IndexOf(CategoryOrder, category);
            return;
        }
    }

    private FieldNavigationCategory PeekCategory(int delta)
    {
        var index = (categoryIndex + delta) % CategoryOrder.Length;
        if (index < 0)
        {
            index += CategoryOrder.Length;
        }

        return CategoryOrder[index];
    }

    private FieldNavigationTarget? PeekMovedTarget(
        FieldPositionSnapshot position,
        FieldNavigationCategory category,
        int delta)
    {
        var targets = source.GetTargets(position, category);
        if (targets.Count == 0)
        {
            return null;
        }

        var index = (PeekSelectedIndex(position, category, targets) + delta) % targets.Count;
        if (index < 0)
        {
            index += targets.Count;
        }

        return targets[index];
    }

    private FieldNavigationTarget? PeekSelectedTarget(
        FieldPositionSnapshot position,
        FieldNavigationCategory category)
    {
        var targets = source.GetTargets(position, category);
        return targets.Count == 0
            ? null
            : targets[PeekSelectedIndex(position, category, targets)];
    }

    private void MoveTarget(FieldPositionSnapshot position, int delta)
    {
        var targets = source.GetTargets(position, CurrentCategory);
        if (targets.Count == 0)
        {
            return;
        }

        var key = CreateSelectionKey(position.FieldId, CurrentCategory);
        var index = GetSelectedIndex(position, targets);
        index = (index + delta) % targets.Count;
        if (index < 0)
        {
            index += targets.Count;
        }

        selectedTargetIds[key] = GetTargetId(targets[index]);
    }

    private FieldNavigationActionResult? DescribeCurrentSelection(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform? controlTransform,
        FieldLadderStateSnapshot ladderState = default)
    {
        var categoryName = GetCategoryDisplayName(CurrentCategory);
        var target = GetSelectedTarget(position);
        if (target is null)
        {
            return new FieldNavigationActionResult($"{categoryName}: none for this field yet.");
        }

        if (!string.IsNullOrWhiteSpace(target.Value.ManualNavigationGuidance))
        {
            return DescribeManualTarget(target.Value);
        }

        if (ladderState.IsUsable && ladderState.IsMounted)
        {
            var climbDirection = FormatInputDirection(ladderState.RequiredInput);
            return new FieldNavigationActionResult(
                climbDirection is null
                    ? $"{categoryName}, {target.Value.Label}. climb the ladder."
                    : $"{categoryName}, {target.Value.Label}. climb {climbDirection}.");
        }

        string? spokenOffset = null;
        double? routeDistance = null;
        if (controlTransform is not null && routePlanner is not null)
        {
            var selectionRoute = new FieldNavigationRouteTracker(routePlanner);
            if (selectionRoute.TryStart(position, target.Value, out var guidance))
            {
                // A freshly built native route can begin on the exact funnel
                // point Cloud already occupies. The live tracker advances or
                // applies its corridor lookahead on its first update, but a
                // selection summary previously formatted the unadvanced
                // startup point and could therefore say "at destination" for
                // a target that was still several steps away.
                if (selectionRoute.TryUpdate(position, target.Value, out var updatedGuidance))
                {
                    guidance = updatedGuidance;
                }

                routeDistance = guidance.RemainingDistance;
                var waypoint = guidance.Waypoint;
                spokenOffset = FormatSpokenRoute(
                    position,
                    waypoint,
                    guidance,
                    selectionRoute,
                    controlTransform.Value,
                    out var spokenDirection);
                if (guidance.RemainingDistance > ResolveArrivalDistance(target.Value, DefaultSelectionArrivalDistance) &&
                    string.Equals(spokenOffset, "at destination", StringComparison.Ordinal))
                {
                    // A short opening run can round to zero while later turns
                    // remain. Preserve its direction and sub-count distance.
                    spokenOffset = string.IsNullOrEmpty(spokenDirection)
                        ? null
                        : $"{spokenDirection} less than 1";
                }
            }
        }
        var distance = routeDistance ?? Math.Sqrt(
            Math.Pow(target.Value.X - position.X, 2) +
            Math.Pow(target.Value.Y - position.Y, 2));
        var direction = distance <= ResolveArrivalDistance(target.Value, DefaultSelectionArrivalDistance)
            ? "nearby"
            : spokenOffset ?? "direction unavailable";
        return new FieldNavigationActionResult($"{categoryName}, {target.Value.Label}. {direction}.");
    }

    private static FieldNavigationActionResult DescribeManualTarget(FieldNavigationTarget target) =>
        new($"{GetCategoryDisplayName(target.Category)}, {target.Label}. {target.ManualNavigationGuidance}");

    private int ResolveSpokenDistanceUnits(int fieldId) =>
        Math.Max(1, spokenDistanceUnitsPerCountResolver(fieldId));

    private string FormatSpokenRoute(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint immediateWaypoint,
        FieldNavigationRouteGuidance guidance,
        FieldNavigationRouteTracker? tracker,
        FieldNavigationControlTransform controlTransform,
        out string direction)
    {
        var scale = ResolveSpokenDistanceUnits(position.FieldId);
        if (tracker?.CurrentProbeSnapshot is { } probe)
        {
            return FieldNavigationConnectedRunFormatter.Format(
                position,
                immediateWaypoint,
                probe.StableWaypoints,
                probe.WaypointIndex,
                guidance.NextAction,
                controlTransform,
                scale,
                out direction);
        }

        if (FieldNavigationSpokenCueFormatter.TryResolveSegment(
                immediateWaypoint.X - position.X,
                immediateWaypoint.Y - position.Y,
                controlTransform,
                scale,
                out var segment))
        {
            direction = segment.Direction;
            return FieldNavigationSpokenCueFormatter.Format(segment, scale);
        }

        direction = string.Empty;
        return "at destination";
    }

    private FieldNavigationTarget? GetSelectedTarget(FieldPositionSnapshot position)
    {
        var targets = source.GetTargets(position, CurrentCategory);
        if (targets.Count == 0)
        {
            return null;
        }

        return targets[GetSelectedIndex(position, targets)];
    }

    private int GetSelectedIndex(FieldPositionSnapshot position, IReadOnlyList<FieldNavigationTarget> targets)
    {
        var key = CreateSelectionKey(position.FieldId, CurrentCategory);
        var index = PeekSelectedIndex(position, CurrentCategory, targets);
        selectedTargetIds[key] = GetTargetId(targets[index]);
        return index;
    }

    private int PeekSelectedIndex(
        FieldPositionSnapshot position,
        FieldNavigationCategory category,
        IReadOnlyList<FieldNavigationTarget> targets)
    {
        var key = CreateSelectionKey(position.FieldId, category);
        if (selectedTargetIds.TryGetValue(key, out var selectedId))
        {
            for (var index = 0; index < targets.Count; index++)
            {
                if (string.Equals(GetTargetId(targets[index]), selectedId, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return FindClosestTargetIndex(position, targets);
    }

    private void RelockBeaconToSelection(FieldPositionSnapshot position)
    {
        if (!BeaconEnabled)
        {
            return;
        }

        var target = GetSelectedTarget(position);
        if (target is null)
        {
            Reset();
            return;
        }

        if (!TryLockBeacon(target.Value, position, activeLadderState))
        {
            Reset();
        }
    }

    private bool TryLockBeacon(
        FieldNavigationTarget target,
        FieldPositionSnapshot position,
        FieldLadderStateSnapshot ladderState = default)
    {
        Reset();
        if (!string.IsNullOrWhiteSpace(target.ManualNavigationGuidance))
        {
            LastNavigationDiagnostic = $"manual traversal required, field={target.FieldId}, target={GetTargetId(target)}";
            return false;
        }

        if (routeTracker is null)
        {
            LastNavigationDiagnostic = $"route planner unavailable, field={target.FieldId}, target={GetTargetId(target)}";
            return false;
        }

        var planningPosition = ResolveRoutePlanningPosition(position, ladderState);
        if (!routeTracker.TryStart(planningPosition, target, out var guidance))
        {
            LastNavigationDiagnostic = $"route unavailable, field={target.FieldId}, target={GetTargetId(target)}";
            return false;
        }

        BeaconEnabled = true;
        beaconLockedTarget = target;
        beaconCategory = target.Category;
        beaconFieldId = target.FieldId;
        beaconTargetId = GetTargetId(target);
        beaconTargetLabel = target.Label;
        beaconDestinationFieldIds = ResolveDestinationFieldIds(target, position);
        // Cache these while the native source still describes the departure field.
        // Different gateways can lead to different tracks on the same destination screen.
        beaconDepartureExits = target.TriggerLine is null ? [] :
            source.GetTargets(position, FieldNavigationCategory.Exits)
                .Where(exit => exit.TriggerLine is not null &&
                    exit.DestinationFieldIds is { Count: > 0 })
                .ToArray();
        beaconCompletesOnFieldTransition =
            target.Category == FieldNavigationCategory.Exits ||
            target.CompletesOnArrival &&
            beaconDestinationFieldIds.Length > 0;
        currentGuidance = guidance;
        var correctedMountedRoute = false;
        if (ladderState.IsUsable && ladderState.IsMounted)
        {
            activeLadderState = ladderState;
            activeLadderGuidanceInput = ladderState.RequiredInput;
            activeLadderExpectedLanding = ladderState.Target;
            activeLadderExpectedTriangle = ladderState.TargetTriangle;
            activeLadderHasExpectedLanding = true;
            if (TryResolveMountedRouteCorrection(ladderState, guidance, out var correction))
            {
                pendingLadderAction = correction;
                activeLadderGuidanceInput = correction.RequiredInput;
                activeLadderExpectedLanding = correction.Destination;
                activeLadderExpectedTriangle = correction.DestinationTriangle;
                routeStartsAfterMountedLadder = false;
                correctedMountedRoute = true;
            }
            else
            {
                routeStartsAfterMountedLadder = true;
            }
        }
        else
        {
            CapturePendingLadderAction(guidance);
        }
        lastAcceptedPosition = position;
        var initialRemainingDistance = ResolveProgressRemainingDistance(guidance);
        if (routeStartsAfterMountedLadder)
        {
            var dx = planningPosition.X - position.X;
            var dy = planningPosition.Y - position.Y;
            var dz = planningPosition.Z - position.Z;
            initialRemainingDistance += Math.Sqrt(
                dx * (double)dx +
                dy * (double)dy +
                dz * (double)dz);
        }

        StartRouteProgress(initialRemainingDistance);
        LastNavigationDiagnostic = correctedMountedRoute
            ? $"{guidance.Diagnostic}, route corrected against active native ladder"
            : routeStartsAfterMountedLadder
                ? $"{guidance.Diagnostic}, route projected from active native ladder landing"
                : guidance.Diagnostic;
        return true;
    }

    private static bool TryResolveMountedRouteCorrection(
        FieldLadderStateSnapshot ladderState,
        FieldNavigationRouteGuidance guidance,
        out FieldNavigationRouteAction correction)
    {
        if (guidance.NextAction is { Kind: FieldNavigationTransitionKind.Ladder } candidate &&
            candidate.RequiredInput == Opposite(ladderState.RequiredInput) &&
            IsNear(candidate.Waypoint, ladderState.Target, LadderEndpointMatchDistance))
        {
            correction = candidate;
            return true;
        }

        correction = default;
        return false;
    }

    private FieldNavigationTarget? GetBeaconTarget(FieldPositionSnapshot position)
    {
        if (!BeaconEnabled || position.FieldId != beaconFieldId)
        {
            return null;
        }

        var targets = source.GetTargets(position, beaconCategory);
        foreach (var target in targets)
        {
            if (string.Equals(GetTargetId(target), beaconTargetId, StringComparison.Ordinal))
            {
                return target;
            }
        }

        return null;
    }

    private int[] ResolveDestinationFieldIds(
        FieldNavigationTarget target,
        FieldPositionSnapshot position)
    {
        if (target.DestinationFieldIds is { Count: > 0 } directDestinations)
        {
            return directDestinations
                .Where(destination => destination >= 0)
                .Distinct()
                .ToArray();
        }

        if (target.TriggerLine is not { } targetLine)
        {
            return [];
        }

        return source.GetTargets(position, FieldNavigationCategory.Exits)
            .Where(exit => exit.TriggerLine == targetLine)
            .SelectMany(exit => exit.DestinationFieldIds ?? Array.Empty<int>())
            .Where(destination => destination >= 0)
            .Distinct()
            .ToArray();
    }

    private bool MatchesDepartureGateway(int destinationField)
    {
        if (beaconLockedTarget?.TriggerLine is not { } selectedLine ||
            lastAcceptedPosition is not { } departure)
            return true;

        var lines = beaconDepartureExits
            .Where(exit => exit.DestinationFieldIds!.Contains(destinationField))
            .Select(exit => exit.TriggerLine!.Value).Distinct().ToArray();
        // A wide doorway can consist of joined native segments (Costa's beach uses
        // three). Treat that connected gateway as one entrance before comparing it
        // with separate entrances such as Mount Corel's upper and lower tracks.
        var selectedGateway = new HashSet<FieldNavigationTriggerLine> { selectedLine };
        bool expanded;
        do
        {
            expanded = false;
            foreach (var line in lines)
                if (!selectedGateway.Contains(line) &&
                    selectedGateway.Any(connected => LinesShareEndpoint(connected, line)))
                    expanded |= selectedGateway.Add(line);
        } while (expanded);

        var selectedDistance = selectedGateway.Min(line => SquaredDistanceToLine(departure, line));
        foreach (var line in lines)
        {
            if (!selectedGateway.Contains(line) &&
                SquaredDistanceToLine(departure, line) + 0.001d < selectedDistance)
                return false;
        }
        return true;
    }

    private static bool LinesShareEndpoint(FieldNavigationTriggerLine a, FieldNavigationTriggerLine b) =>
        (a.StartX, a.StartY, a.StartZ) == (b.StartX, b.StartY, b.StartZ) ||
        (a.StartX, a.StartY, a.StartZ) == (b.EndX, b.EndY, b.EndZ) ||
        (a.EndX, a.EndY, a.EndZ) == (b.StartX, b.StartY, b.StartZ) ||
        (a.EndX, a.EndY, a.EndZ) == (b.EndX, b.EndY, b.EndZ);

    private static double SquaredDistanceToLine(
        FieldPositionSnapshot position, FieldNavigationTriggerLine line)
    {
        var dx = (double)line.EndX - line.StartX;
        var dy = (double)line.EndY - line.StartY;
        var dz = (double)line.EndZ - line.StartZ;
        var lengthSquared = dx * dx + dy * dy + dz * dz;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(
            ((position.X - line.StartX) * dx + (position.Y - line.StartY) * dy +
             (position.Z - line.StartZ) * dz) / lengthSquared, 0, 1);
        var x = position.X - (line.StartX + t * dx);
        var y = position.Y - (line.StartY + t * dy);
        var z = position.Z - (line.StartZ + t * dz);
        return x * x + y * y + z * z;
    }

    private static string GetTargetId(FieldNavigationTarget target) =>
        string.IsNullOrWhiteSpace(target.StableId)
            ? $"{target.FieldId}:{target.Category}:{target.Label}:{target.X}:{target.Y}:{target.Z}"
            : $"{target.FieldId}:{target.StableId}";

    private static int FindClosestTargetIndex(FieldPositionSnapshot position, IReadOnlyList<FieldNavigationTarget> targets)
    {
        var closestIndex = 0;
        var closestDistance = double.MaxValue;
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distance = dx * (double)dx + dy * (double)dy;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        return closestIndex;
    }

    private static bool IsWithinArrivalDistance(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        int arrivalDistanceUnits,
        FieldNavigationRouteGuidance? guidance = null,
        bool forCompletion = false)
    {
        // A native triangle activation has no radius at all. The script fires when the
        // party leader's own walkmesh triangle enters the set, so anything short of that
        // is not an arrival no matter how close it measures. Falling back to a radius
        // here released auto walk on the plaza, one step outside the ramp the player
        // was being sent to.
        if (forCompletion && target.CompletionTriangles is { Count: > 0 } completionTriangles)
        {
            return completionTriangles.Contains(position.TriangleId);
        }

        var threshold = ResolveArrivalDistance(target, arrivalDistanceUnits, forCompletion);
        // Contact is measured on the party's own position, not on what is left of the
        // route. FUN_00637724 compares the squared horizontal distance on its own
        // against the square of half the sum of the two collision widths and tests the
        // height separately against a band of its own, so a materia standing a hundred
        // units up on its stand is within reach even though the walk to it is long. It
        // is also strictly less than, not within: standing exactly on the boundary is a
        // touch the game refuses.
        if (target.Activation == FieldNavigationActivation.Contact)
        {
            var contactX = target.X - position.X;
            var contactY = target.Y - position.Y;
            return contactX * (double)contactX + contactY * (double)contactY <
                       threshold * (double)threshold &&
                   FieldWalkmeshRoutePlanner.IsWithinActivationVerticalRange(
                       target,
                       target.Z - position.Z);
        }

        if (guidance is not null)
        {
            return guidance.Value.RemainingDistance <= threshold;
        }

        if (threshold == 0)
        {
            return position.X == target.X &&
                   position.Y == target.Y &&
                   position.Z == target.Z;
        }

        var dx = target.X - position.X;
        var dy = target.Y - position.Y;
        var dz = target.Z - position.Z;

        // Contact is not a sphere. FUN_00637724 compares the squared horizontal
        // distance on its own against the square of half the sum of the two collision
        // widths, and tests the height separately against its own band. Measuring it
        // as a sphere would refuse the Huge Materia from a step above it while the
        // game itself would have accepted the touch.
        if (target.Activation == FieldNavigationActivation.Contact)
        {
            return dx * (double)dx + dy * (double)dy <= threshold * (double)threshold &&
                   FieldWalkmeshRoutePlanner.IsWithinActivationVerticalRange(target, dz);
        }

        return dx * (double)dx +
               dy * (double)dy +
               dz * (double)dz <= threshold * (double)threshold;
    }

    // A native gateway or an arrival-completing script line is a traversal even when the
    // player selected it from Story. It only fires when the party reaches the crossing,
    // so its route ends on the crossing and is finished only once the player is
    // actually there. Measuring completion against a radius ended the route short of the
    // crossing and released auto walk, leaving the player beside a doorway that never
    // opened: 122 units short at the Honey Bee Inn lobby rooms, and 72 units short of the
    // gateways inside the Mythril Mine. Falling short of a crossing is never a completion,
    // so there is no radius to fall back on. Proximity still governs everything else the
    // radius is used for - beacon cues, interaction pauses, NPCs and objects - which really
    // are reached by standing near them.
    private static bool CompletesByCrossing(FieldNavigationTarget target) =>
        (target.Category == FieldNavigationCategory.Exits ||
         target.Category == FieldNavigationCategory.Story && target.CompletesOnArrival) &&
        (target.TriggerLine is not null ||
         target.DestinationFieldIds is { Count: > 0 } ||
         target.CompletionTriangles is { Count: > 0 });

    private static int ResolveArrivalDistance(
        FieldNavigationTarget target,
        int configuredDistance,
        bool forCompletion = false)
    {
        if (forCompletion && CompletesByCrossing(target))
        {
            return ExitCrossingArrivalDistance;
        }

        // An exit that activates by crossing a native trigger line can still carry an
        // inflated interaction radius so a fallback route stays plannable when the line
        // itself is unroutable. That radius is a routing aid, not a proximity range.
        return target is
        {
            Category: FieldNavigationCategory.Exits,
            TriggerLine: not null,
            InteractionRadius: > 0
        }
            ? TriggerLineExitArrivalDistance
            : target.InteractionRadius > 0
                ? target.InteractionRadius
                : Math.Max(0, configuredDistance);
    }

    private static string CreateSelectionKey(int fieldId, FieldNavigationCategory category) =>
        $"{fieldId}:{category}";

    private void ClearSelectionsForField(int fieldId)
    {
        foreach (var category in CategoryOrder)
        {
            selectedTargetIds.Remove(CreateSelectionKey(fieldId, category));
        }
    }

    private static string GetCategoryDisplayName(FieldNavigationCategory category) =>
        category switch
        {
            FieldNavigationCategory.Exits => "Exits",
            FieldNavigationCategory.Story => "Story",
            FieldNavigationCategory.Npcs => "NPCs",
            FieldNavigationCategory.Objects => "Objects",
            _ => category.ToString()
        };
}

