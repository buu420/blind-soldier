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
    private const int LadderActionArrivalDistance = 56;
    private const int LadderLandingArrivalDistance = 96;
    private const int LadderEndpointMatchDistance = 224;
    private const int CompletedLadderEndpointMatchDistance = 96;
    private const int DefaultSelectionArrivalDistance = 80;

    // An exit that fires by crossing a native trigger line is only reached by standing on
    // the line. Its route already ends on the line, so completion must wait until the player
    // is effectively there; anything wider stops auto-walk short and the line never fires.
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
    private FieldPositionSnapshot? lastBeaconPosition;
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
    private FieldNavigationCategory beaconCategory;
    private int beaconFieldId = -1;
    private int[] beaconDestinationFieldIds = [];
    private bool beaconCompletesOnFieldTransition;
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
            TriangleId = (ushort)ladderState.TargetTriangle
        };
    }

    public void Reset()
    {
        ResetCore(deactivateProgress: true);
    }

    private void ResetCore(bool deactivateProgress)
    {
        BeaconEnabled = false;
        lastBeaconPosition = null;
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
        beaconCompletesOnFieldTransition = false;
        interactionArrivalPaused = false;
        interactionArrivalDistance = 0;
        ResetLadderMountPrompt();
        routeTracker?.Reset();
        movementObserver.Reset();
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
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.NextCategory:
                MoveCategory(1);
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.PreviousTarget:
                MoveTarget(position, -1);
                RelockBeaconToSelection(position);
                return DescribeCurrentSelection(position, controlTransform, ladderState);
            case FieldNavigationAction.NextTarget:
                MoveTarget(position, 1);
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

                var target = GetSelectedTarget(position);
                if (target is null)
                {
                    return DescribeCurrentSelection(position, controlTransform, ladderState);
                }

                if (!TryLockBeacon(target.Value, position, ladderState))
                {
                    return new FieldNavigationActionResult(
                        $"Route unavailable to {target.Value.Label}. Navigation off.");
                }

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
            return null;
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
            var reached = completesOnFieldTransition && destinationMatches;
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
                    : $"{label} no longer available. Navigation off.");
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
                lastBeaconPosition = null;
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
        var canCompleteOnArrival = CanCompleteOnArrival(target.Value);
        if (pendingLadderAction is null &&
            canCompleteOnArrival &&
            isWithinArrivalDistance)
        {
            return CompleteNavigation(
                $"{beaconTargetLabel} reached. Navigation off.",
                $"{beaconCategory} arrival",
                completed: true);
        }

        if (pendingLadderAction is null && !canCompleteOnArrival)
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
                lastBeaconPosition = null;
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
                lastBeaconPosition = null;
                LastNavigationDiagnostic =
                    $"{LastNavigationDiagnostic}, interaction arrival paused at " +
                    $"{interactionArrivalDistance} units";
                return new FieldNavigationActionResult(
                    $"{beaconTargetLabel} reached. Interact here. Navigation paused.");
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

    public NavigationBeaconCue? CreateBeaconCue(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        int arrivalDistanceUnits)
    {
        if (!BeaconEnabled ||
            interactionArrivalPaused ||
            !FieldPositionReader.IsUsable(position) ||
            activeLadderState.IsMounted)
        {
            return null;
        }

        if (position.FieldId != beaconFieldId)
        {
            return null;
        }

        var target = GetBeaconTarget(position);
        if (target is null)
        {
            return null;
        }

        if (routeTracker is null)
        {
            return null;
        }

        if (currentGuidance is null)
        {
            return null;
        }

        var guidance = currentGuidance.Value;
        if (IsWithinArrivalDistance(position, target.Value, arrivalDistanceUnits, guidance))
        {
            return null;
        }

        var waypoint = ResolveGuidanceWaypoint(guidance);
        var desiredX = waypoint.X - position.X;
        var desiredY = waypoint.Y - position.Y;
        var recommendation = movementObserver.ResolveStickDirection(desiredX, desiredY, controlTransform);
        var recommendedInput = recommendation.Input;

        if (!IsDirectionalInput(recommendedInput))
        {
            return null;
        }

        var stick = FieldNavigationMovementObserver.ToStickDirection(recommendedInput);
        var cueTarget = target.Value with
        {
            X = waypoint.X,
            Y = waypoint.Y,
            Z = waypoint.Z
        };
        var cue = FieldNavigationBeacon.CreateCue(
            position,
            cueTarget,
            stick,
            arrivalDistanceUnits: 0,
            previousPosition: lastBeaconPosition);
        if (cue is null)
        {
            return null;
        }

        lastBeaconPosition = position;
        LastNavigationDiagnostic = $"{guidance.Diagnostic}, {recommendation.Diagnostic}, cue={recommendedInput}";
        return cue.Value with { DistanceUnits = guidance.RemainingDistance };
    }

    public FieldNavigationActionResult? CreateSpokenGuidance(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        int arrivalDistanceUnits,
        int predictionHorizonMs = 0)
    {
        if (BeaconEnabled && activeLadderState.IsMounted)
        {
            var climbDirection = FormatInputDirection(activeLadderGuidanceInput);
            return climbDirection is null
                ? new FieldNavigationActionResult("climb the ladder")
                : new FieldNavigationActionResult($"climb {climbDirection}");
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
            IsWithinArrivalDistance(position, target.Value, arrivalDistanceUnits, currentGuidance))
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
        if (BeaconEnabled && activeLadderState.IsMounted)
        {
            input = activeLadderGuidanceInput;
            return IsDirectionalInput(input);
        }

        if (!BeaconEnabled ||
            interactionArrivalPaused ||
            !FieldPositionReader.IsUsable(position) ||
            position.FieldId != beaconFieldId ||
            currentGuidance is null)
        {
            return false;
        }

        var target = GetBeaconTarget(position);
        if (target is null ||
            IsWithinArrivalDistance(position, target.Value, arrivalDistanceUnits, currentGuidance))
        {
            return false;
        }

        if (pendingLadderAction is { RequiresAction: true } action &&
            IsNear(position, action.Waypoint, LadderActionArrivalDistance))
        {
            return false;
        }

        var waypoint = ResolveGuidanceWaypoint(currentGuidance.Value);
        var recommendation = movementObserver.ResolveStickDirection(
            waypoint.X - position.X,
            waypoint.Y - position.Y,
            controlTransform);
        input = recommendation.Input;
        return IsDirectionalInput(input);
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
        if (!routeStartsAfterMountedLadder &&
            routeTracker?.TryUpdate(
                position,
                target,
                observation,
                observedAt,
                out var updatedGuidance) == true)
        {
            liveRouteGuidance = updatedGuidance;
            currentGuidance = updatedGuidance;
            if (updatedGuidance.Replanned)
            {
                RestartProgressSegment(ResolveProgressRemainingDistance(updatedGuidance));
            }

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
                 liveRouteGuidance is { } routeGuidance)
        {
            // Some field walkmeshes, notably Floor 63's crawlspace, report the
            // same native movement mode as a ladder even though the route bends
            // across several horizontal and vertical segments. Keep advancing
            // the actual route and let its live waypoint own each turn.
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
        lastBeaconPosition = null;
        lastAcceptedPosition = null;
        ResetLadderMountPrompt();
        movementObserver.Reset();
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
                    out _);
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
        beaconCompletesOnFieldTransition =
            target.Category == FieldNavigationCategory.Exits ||
            target.CompletesOnArrival &&
            target.TriggerLine is not null &&
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
        lastBeaconPosition = null;
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
        FieldNavigationRouteGuidance? guidance = null)
    {
        var threshold = ResolveArrivalDistance(target, arrivalDistanceUnits);
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
        return dx * (double)dx +
               dy * (double)dy +
               dz * (double)dz <= threshold * (double)threshold;
    }

    private static int ResolveArrivalDistance(FieldNavigationTarget target, int configuredDistance) =>
        // An exit that activates by crossing a native trigger line is not reached by
        // standing near it. Some of those exits carry an inflated interaction radius so a
        // fallback route can be planned when the line itself is unroutable; reusing that
        // radius as the arrival threshold ended navigation up to a radius short of the
        // line, so the player stopped, never crossed, and the transition never fired.
        // Interaction radii still govern NPCs and objects, which really are reached by
        // proximity.
        target is
        {
            Category: FieldNavigationCategory.Exits,
            TriggerLine: not null,
            InteractionRadius: > 0
        }
            ? TriggerLineExitArrivalDistance
            : target.InteractionRadius > 0
                ? target.InteractionRadius
                : Math.Max(0, configuredDistance);

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

public static class FieldNavigationBeacon
{
    public static NavigationBeaconCue? CreateCue(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        int arrivalDistanceUnits,
        int durationMs = 220,
        FieldPositionSnapshot? previousPosition = null)
    {
        if (!FieldPositionReader.IsUsable(position) || position.FieldId != target.FieldId)
        {
            return null;
        }

        var dx = target.X - position.X;
        var dy = target.Y - position.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (distance <= Math.Max(0, arrivalDistanceUnits))
        {
            return null;
        }

        var direction = DescribeDirection(dx, dy);
        var stickX = CalculateDirectionComponent(dx, distance);
        var stickY = CalculateDirectionComponent(dy, distance);
        return new NavigationBeaconCue(
            target.Label,
            direction,
            stickX,
            stickY,
            stickX,
            0f,
            stickY,
            ClassifyMovement(position, previousPosition, dx, dy, distance),
            Math.Max(40, durationMs),
            distance);
    }

    public static NavigationBeaconCue? CreateCue(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationControlTransform controlTransform,
        int arrivalDistanceUnits,
        int durationMs = 220,
        FieldPositionSnapshot? previousPosition = null)
    {
        if (!FieldPositionReader.IsUsable(position) || position.FieldId != target.FieldId)
        {
            return null;
        }

        var dx = target.X - position.X;
        var dy = target.Y - position.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (distance <= Math.Max(0, arrivalDistanceUnits))
        {
            return null;
        }

        var stick = controlTransform.TransformWorldVector(dx, dy);
        return new NavigationBeaconCue(
            target.Label,
            DescribeStickDirection(stick.X, stick.Y),
            stick.X,
            stick.Y,
            stick.X,
            0f,
            stick.Y,
            ClassifyMovement(position, previousPosition, dx, dy, distance),
            Math.Max(40, durationMs),
            distance);
    }

    public static NavigationBeaconCue? CreateCue(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationStickDirection stick,
        int arrivalDistanceUnits,
        int durationMs = 220,
        FieldPositionSnapshot? previousPosition = null)
    {
        if (!FieldPositionReader.IsUsable(position) || position.FieldId != target.FieldId)
        {
            return null;
        }

        var dx = target.X - position.X;
        var dy = target.Y - position.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (distance <= Math.Max(0, arrivalDistanceUnits))
        {
            return null;
        }

        return new NavigationBeaconCue(
            target.Label,
            DescribeStickDirection(stick.X, stick.Y),
            stick.X,
            stick.Y,
            stick.X,
            0f,
            stick.Y,
            ClassifyMovement(position, previousPosition, dx, dy, distance),
            Math.Max(40, durationMs),
            distance);
    }

    private static string DescribeDirection(int dx, int dy)
    {
        var max = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (max == 0)
        {
            return "here";
        }

        var threshold = max * 0.35;
        var vertical = Math.Abs(dy) <= threshold ? string.Empty : dy > 0 ? "down" : "up";
        var horizontal = Math.Abs(dx) <= threshold ? string.Empty : dx > 0 ? "right" : "left";
        if (vertical.Length != 0 && horizontal.Length != 0)
        {
            return $"{vertical}-{horizontal}";
        }

        return vertical.Length != 0 ? vertical : horizontal;
    }

    private static string DescribeStickDirection(float x, float y)
    {
        var max = Math.Max(Math.Abs(x), Math.Abs(y));
        if (max <= 0.001f)
        {
            return "here";
        }

        var threshold = max * 0.35f;
        var vertical = Math.Abs(y) <= threshold ? string.Empty : y < 0 ? "up" : "down";
        var horizontal = Math.Abs(x) <= threshold ? string.Empty : x < 0 ? "left" : "right";
        if (vertical.Length != 0 && horizontal.Length != 0)
        {
            return $"{vertical}-{horizontal}";
        }

        return vertical.Length != 0 ? vertical : horizontal;
    }

    private static float CalculatePan(int dx, int dy)
    {
        var max = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (max == 0)
        {
            return 0f;
        }

        var pan = dx / (double)max;
        return (float)Math.Round(Math.Clamp(pan, -1d, 1d), 2);
    }

    private static float CalculateDirectionComponent(int value, double length)
    {
        if (length <= 0)
        {
            return 0f;
        }

        return (float)(value / length);
    }

    private static NavigationBeaconMovementState ClassifyMovement(
        FieldPositionSnapshot position,
        FieldPositionSnapshot? previousPosition,
        int targetDx,
        int targetDy,
        double targetDistance)
    {
        if (previousPosition is null || previousPosition.Value.FieldId != position.FieldId || targetDistance <= 0)
        {
            return NavigationBeaconMovementState.Correcting;
        }

        var moveDx = position.X - previousPosition.Value.X;
        var moveDy = position.Y - previousPosition.Value.Y;
        var moveDistance = Math.Sqrt(moveDx * (double)moveDx + moveDy * (double)moveDy);
        if (moveDistance < 1d)
        {
            return NavigationBeaconMovementState.Correcting;
        }

        var alignment =
            (moveDx / moveDistance * (targetDx / targetDistance)) +
            (moveDy / moveDistance * (targetDy / targetDistance));
        return alignment >= 0.55d
            ? NavigationBeaconMovementState.OnCourse
            : NavigationBeaconMovementState.Correcting;
    }
}
