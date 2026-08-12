namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldNavigationRouteGuidance(
    FieldNavigationRouteWaypoint Waypoint,
    int PortalIndex,
    int PortalCount,
    double RemainingDistance,
    bool Replanned,
    FieldNavigationRouteAction? NextAction,
    string Diagnostic,
    double ProgressRemainingDistance = double.NaN);

public sealed record FieldNavigationRouteProbeSnapshot(
    int FieldId,
    string TargetId,
    int TargetTriangle,
    IReadOnlyList<int> TrianglePath,
    IReadOnlyList<FieldNavigationRoutePortal> Portals,
    IReadOnlyList<FieldNavigationRouteStep> StableWaypoints,
    int PortalIndex,
    int WaypointIndex,
    int ResolvedTriangle,
    FieldNavigationRouteGuidance Guidance);

public sealed class FieldNavigationRouteTracker
{
    private const int TargetMovementReplanDistance = 96;
    private const int TargetMovementSamplesBeforeReplan = 6;
    private const double OffRouteDistanceBeforeReplan = 192d;
    private const int OffRouteSamplesBeforeReplan = 12;
    private const int HeadingHeldOffRouteSamplesBeforeReplan = 4;
    private const int SharedEdgeBacktrackTolerance = 2;
    private const double MinimumWaypointArrivalDistance = 20d;
    private const double MaximumWaypointArrivalDistance = 64d;
    private static readonly TimeSpan BlockedDurationBeforeRecovery = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan BlockedDurationBeforeRecoveryRetry = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan BlockedInputReleaseGrace = TimeSpan.FromMilliseconds(400);
    private const double BlockedProgressDistance = 8d;
    private const int MinimumBlockedMovementSamplesBeforeRecovery = 8;
    private const double RecoveryProgressEpsilon = 0.5d;
    private const double RecoveryDirectionAlignment = 0.5d;
    private const double ObstacleRecoveryArrivalDistance = 36d;

    private readonly IFieldNavigationRoutePlanner planner;
    private FieldNavigationRoutePlan? plan;
    private IReadOnlyList<FieldNavigationRouteStep> stableWaypoints = Array.Empty<FieldNavigationRouteStep>();
    private int portalIndex;
    private int waypointIndex;
    private int guidanceWaypointIndex;
    private double offRouteDistance;
    private int offRouteSamples;
    private double headingHeldOffRouteDistance;
    private int headingHeldOffRouteSamples;
    private int consecutiveTargetMovementSamples;
    private FieldPositionSnapshot? lastPosition;
    private FieldNavigationRouteGuidance? currentGuidance;
    private int currentResolvedTriangle = -1;
    private FieldNavigationRouteWaypoint routeOrigin;
    private int routeOriginPortalIndex;
    private double committedHeadingX;
    private double committedHeadingY;
    private bool hasCommittedHeading;
    private int blockedMovementSamples;
    private DateTime? blockedMovementSince;
    private DateTime? blockedMovementLastDirectionalAt;
    private FieldNavigationInput blockedMovementInput;
    private FieldNavigationRouteWaypoint blockedMovementOrigin;
    private bool obstacleRecoveryReady;
    private int obstacleRecoveryAttempt;
    private FieldNavigationRouteWaypoint? obstacleRecoveryWaypoint;
    private FieldNavigationRouteWaypoint obstacleRecoveryOrigin;
    private double obstacleRecoveryInitialDistance;
    private DateTime? obstacleRecoveryLastProgressAt;

    public FieldNavigationRouteTracker(IFieldNavigationRoutePlanner planner)
    {
        this.planner = planner;
    }

    public FieldNavigationRouteProbeSnapshot? CurrentProbeSnapshot =>
        plan is null || currentGuidance is null
            ? null
            : new FieldNavigationRouteProbeSnapshot(
                plan.FieldId,
                plan.TargetId,
                plan.TargetTriangle,
                plan.TrianglePath,
                plan.Portals,
                stableWaypoints,
                portalIndex,
                waypointIndex,
                currentResolvedTriangle,
                currentGuidance.Value);

    public bool TryMeasureRemainingDistance(
        FieldPositionSnapshot position,
        out double remainingDistance)
    {
        if (plan is null || position.FieldId != plan.FieldId)
        {
            remainingDistance = 0d;
            return false;
        }

        var resolvedTriangle = currentResolvedTriangle;
        if (planner.TryResolvePlayerTriangle(position, out var liveResolvedTriangle))
        {
            resolvedTriangle = liveResolvedTriangle;
        }

        var routeIndex = FindRouteIndex(
            plan.TrianglePath,
            resolvedTriangle,
            portalIndex);
        remainingDistance = CalculateProgressRemainingDistance(
            position,
            routeOrigin,
            routeOriginPortalIndex,
            stableWaypoints,
            plan.FinalApproach,
            plan.FinalApproachToTargetDistance,
            routeIndex >= 0 ? routeIndex : portalIndex);
        return true;
    }

    public bool TryGetUpcomingStep(out FieldNavigationRouteStep step)
    {
        if (stableWaypoints.Count == 0)
        {
            step = default;
            return false;
        }

        var anchorIndex = Math.Clamp(
            Math.Max(waypointIndex, guidanceWaypointIndex),
            0,
            stableWaypoints.Count - 1);
        var nextIndex = anchorIndex + 1;
        if (nextIndex < 0 || nextIndex >= stableWaypoints.Count)
        {
            step = default;
            return false;
        }

        step = stableWaypoints[nextIndex];
        return true;
    }

    public bool TryStart(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRouteGuidance guidance)
    {
        Reset();
        return TryBuild(position, target, replanned: false, "activation", out guidance);
    }

    public bool TryUpdate(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRouteGuidance guidance)
        => TryUpdate(position, target, default, out guidance);

    public bool TryUpdate(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationMovementObservation movement,
        out FieldNavigationRouteGuidance guidance)
        => TryUpdate(position, target, movement, DateTime.UtcNow, out guidance);

    public bool TryUpdate(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationMovementObservation movement,
        DateTime observedAt,
        out FieldNavigationRouteGuidance guidance)
    {
        guidance = default;
        if (plan is null ||
            plan.FieldId != position.FieldId ||
            !string.Equals(plan.TargetId, GetTargetId(target), StringComparison.Ordinal))
        {
            return TryBuild(position, target, replanned: true, "route identity changed", out guidance);
        }

        var previousPosition = lastPosition;
        lastPosition = position;
        UpdateObstacleRecoveryState(
            previousPosition,
            position,
            movement,
            observedAt);

        if (HasTargetMoved(plan, target))
        {
            consecutiveTargetMovementSamples++;
            if (consecutiveTargetMovementSamples >= TargetMovementSamplesBeforeReplan)
            {
                return TryBuild(position, target, replanned: true, "target moved", out guidance);
            }
        }
        else
        {
            consecutiveTargetMovementSamples = 0;
        }

        FieldNavigationCorridorObservation? corridorObservation = null;
        int resolvedTriangle;
        if (planner is IFieldNavigationCorridorLookaheadPlanner lookaheadPlanner)
        {
            var pendingAction = FindNextAction(plan.Portals, portalIndex);
            if (!lookaheadPlanner.TryObserveCorridor(
                    position,
                    plan,
                    stableWaypoints,
                    waypointIndex,
                    pendingAction,
                    CreateLookaheadHeading(movement),
                    out var observedCorridor))
            {
                Reset();
                return false;
            }

            corridorObservation = observedCorridor;
            resolvedTriangle = observedCorridor.ResolvedTriangle;
        }
        else if (!planner.TryResolvePlayerTriangle(position, out resolvedTriangle))
        {
            Reset();
            return false;
        }

        var corridorConfirmed = corridorObservation?.ConfirmsRoute == true;
        var routeIndex = FindRouteIndex(plan.TrianglePath, resolvedTriangle, portalIndex);
        if (routeIndex >= portalIndex)
        {
            portalIndex = Math.Min(routeIndex, plan.Portals.Count);
            ResetOffRouteEvidence();
            ResetHeadingHeldOffRouteEvidence();
        }
        else if (routeIndex >= 0)
        {
            // A one-triangle regression is normal shared-edge flicker. A deeper regression
            // means the player genuinely backtracked and the committed route may be stale.
            if (portalIndex - routeIndex <= SharedEdgeBacktrackTolerance)
            {
                ResetOffRouteEvidence();
                ResetHeadingHeldOffRouteEvidence();
            }
            else if (corridorObservation?.Mode == FieldNavigationLookaheadMode.HeadingHeld)
            {
                ResetOffRouteEvidence();
                if (ShouldReplanForHeadingHeldDeviation(previousPosition, position))
                {
                    return TryBuild(
                        position,
                        target,
                        replanned: true,
                        "sustained heading-held backtrack",
                        out guidance);
                }
            }
            else if (corridorConfirmed)
            {
                ResetOffRouteEvidence();
                ResetHeadingHeldOffRouteEvidence();
            }
            else
            {
                ResetHeadingHeldOffRouteEvidence();
                if (ShouldReplanForDeviation(previousPosition, position))
                {
                    return TryBuild(position, target, replanned: true, "sustained backtrack", out guidance);
                }
            }
        }
        else if (corridorObservation?.Mode == FieldNavigationLookaheadMode.HeadingHeld)
        {
            // A clear heading on a triangle outside the committed route can be
            // a wide neighboring lane, but it can also be a walkable side ramp.
            // Keep one continuous physical-deviation counter while the route
            // identity is absent. Alternating between heading-held and
            // required-corner observations must not erase that evidence.
            ResetHeadingHeldOffRouteEvidence();
            if (ShouldReplanForDeviation(
                    previousPosition,
                    position,
                    HeadingHeldOffRouteSamplesBeforeReplan,
                    acceptWaypointProgress: false))
            {
                return TryBuild(
                    position,
                    target,
                    replanned: true,
                    "sustained alternate-corridor deviation",
                    out guidance);
            }
        }
        else if (corridorConfirmed)
        {
            ResetOffRouteEvidence();
            ResetHeadingHeldOffRouteEvidence();
        }
        else
        {
            ResetHeadingHeldOffRouteEvidence();
            if (ShouldReplanForDeviation(
                    previousPosition,
                    position,
                    OffRouteSamplesBeforeReplan,
                    acceptWaypointProgress: false))
            {
                return TryBuild(position, target, replanned: true, "sustained off-route deviation", out guidance);
            }
        }

        // A held route heading is also valid progress when Cloud is on a clear
        // neighboring triangle that was not part of the original polygon path.
        // Requiring exact triangle membership here left the waypoint index
        // behind the player, so a later observation could legitimately trace
        // back to that already-crossed funnel corner. A merely visible future
        // step is not enough: it may be seen from a dead-end side branch.
        var corridorProgressConfirmed =
            corridorConfirmed &&
            (routeIndex >= 0 ||
             corridorObservation?.Mode == FieldNavigationLookaheadMode.HeadingHeld);
        AdvanceWaypoint(previousPosition, position, corridorProgressConfirmed);
        guidanceWaypointIndex = waypointIndex;
        FieldNavigationRouteWaypoint? waypointOverride = corridorObservation?.Waypoint;
        var actionClampDiagnostic = string.Empty;
        if (corridorObservation is { } observation)
        {
            var requestedWaypointIndex = Math.Clamp(
                observation.StableWaypointIndex,
                0,
                Math.Max(0, stableWaypoints.Count - 1));
            var observedWaypointIndex = requestedWaypointIndex;
            var nextAction = FindNextAction(plan.Portals, portalIndex);
            while (observedWaypointIndex > waypointIndex &&
                   nextAction is { } action &&
                   stableWaypoints[observedWaypointIndex].RequiredPortalIndex > action.PortalIndex)
            {
                observedWaypointIndex--;
            }

            if (observedWaypointIndex < waypointIndex &&
                stableWaypoints.Count > 0)
            {
                waypointOverride = stableWaypoints[waypointIndex].Waypoint;
                observedWaypointIndex = waypointIndex;
                actionClampDiagnostic = ", lookahead-clamped-to-route-progress";
            }
            else if (nextAction is { } pendingAction &&
                (stableWaypoints.Count == 0 ||
                 stableWaypoints[observedWaypointIndex].RequiredPortalIndex > pendingAction.PortalIndex ||
                 waypointOverride is { } observedWaypoint &&
                 Distance(ToWaypoint(position), observedWaypoint) >
                 Distance(ToWaypoint(position), pendingAction.Waypoint) + 0.5d))
            {
                waypointOverride = pendingAction.Waypoint;
                observedWaypointIndex = waypointIndex;
                actionClampDiagnostic =
                    $", lookahead-clamped-to-action={pendingAction.Kind}:{pendingAction.StableId}";
            }
            else if (requestedWaypointIndex != observedWaypointIndex &&
                     stableWaypoints.Count > 0)
            {
                waypointOverride = stableWaypoints[observedWaypointIndex].Waypoint;
            }

            guidanceWaypointIndex = Math.Max(waypointIndex, observedWaypointIndex);
            if (observation.Mode == FieldNavigationLookaheadMode.ObstacleRecovery &&
                string.IsNullOrEmpty(actionClampDiagnostic) &&
                waypointOverride is { } recoveryWaypoint)
            {
                if (obstacleRecoveryWaypoint != recoveryWaypoint)
                {
                    obstacleRecoveryWaypoint = recoveryWaypoint;
                    obstacleRecoveryOrigin = ToWaypoint(position);
                    obstacleRecoveryInitialDistance = Distance(
                        obstacleRecoveryOrigin,
                        recoveryWaypoint);
                    obstacleRecoveryLastProgressAt = observedAt;
                    ResetBlockedMovementEvidence();
                }
            }

            if (observation.Mode is not (
                    FieldNavigationLookaheadMode.HeadingHeld or
                    FieldNavigationLookaheadMode.ObstacleRecovery) ||
                !string.IsNullOrEmpty(actionClampDiagnostic))
            {
                UpdateCommittedHeading(position, waypointOverride);
            }
        }

        var lookaheadDiagnostic = corridorObservation is { } liveObservation
            ? $", {liveObservation.Diagnostic}{actionClampDiagnostic}"
            : string.Empty;
        guidance = CreateGuidance(
            position,
            plan,
            stableWaypoints,
            portalIndex,
            waypointIndex,
            resolvedTriangle,
            replanned: false,
            $"route retained{lookaheadDiagnostic}",
            routeOrigin,
            routeOriginPortalIndex,
            routeIndex,
            waypointOverride);
        currentResolvedTriangle = resolvedTriangle;
        currentGuidance = guidance;
        return true;
    }

    public bool TryCompleteAction(
        FieldNavigationRouteAction action,
        FieldPositionSnapshot position,
        out FieldNavigationRouteGuidance guidance)
    {
        guidance = default;
        if (plan is null ||
            position.FieldId != plan.FieldId)
        {
            return false;
        }

        var actionPortalIndex = action.PortalIndex;
        if (!IsMatchingActionPortal(plan.Portals, actionPortalIndex, action))
        {
            actionPortalIndex = FindMatchingActionPortal(plan.Portals, portalIndex, action);
        }

        if (actionPortalIndex < 0)
        {
            return false;
        }

        portalIndex = Math.Max(portalIndex, actionPortalIndex + 1);
        portalIndex = Math.Min(portalIndex, plan.Portals.Count);

        var remainingPortals = plan.Portals
            .Skip(portalIndex)
            .ToArray();
        stableWaypoints = FieldWalkmeshPathfinder
            .BuildStableWaypoints(
                position.X,
                position.Y,
                position.Z,
                remainingPortals,
                plan.FinalApproach)
            .Select(step => step with
            {
                RequiredPortalIndex = Math.Min(
                    plan.Portals.Count,
                    portalIndex + step.RequiredPortalIndex)
            })
            .ToArray();
        waypointIndex = 0;
        guidanceWaypointIndex = 0;
        routeOrigin = ToWaypoint(position);
        routeOriginPortalIndex = portalIndex;
        lastPosition = position;
        consecutiveTargetMovementSamples = 0;
        ResetOffRouteEvidence();
        ResetHeadingHeldOffRouteEvidence();
        ResetBlockedMovementEvidence();
        ClearObstacleRecovery(resetAttempt: true);
        hasCommittedHeading = false;

        var resolvedTriangle = action.DestinationTriangle;
        if (planner.TryResolvePlayerTriangle(position, out var liveResolvedTriangle))
        {
            resolvedTriangle = liveResolvedTriangle;
        }

        guidance = CreateGuidance(
            position,
            plan,
            stableWaypoints,
            portalIndex,
            waypointIndex,
            resolvedTriangle,
            replanned: false,
            $"route action completed ({action.Kind}:{action.StableId})",
            routeOrigin,
            routeOriginPortalIndex,
            FindRouteIndex(plan.TrianglePath, resolvedTriangle, portalIndex));
        currentResolvedTriangle = resolvedTriangle;
        currentGuidance = guidance;
        UpdateCommittedHeading(position, guidance.Waypoint);
        return true;
    }

    public void Reset()
    {
        plan = null;
        stableWaypoints = Array.Empty<FieldNavigationRouteStep>();
        portalIndex = 0;
        waypointIndex = 0;
        guidanceWaypointIndex = 0;
        offRouteDistance = 0d;
        offRouteSamples = 0;
        headingHeldOffRouteDistance = 0d;
        headingHeldOffRouteSamples = 0;
        consecutiveTargetMovementSamples = 0;
        lastPosition = null;
        currentGuidance = null;
        currentResolvedTriangle = -1;
        routeOrigin = default;
        routeOriginPortalIndex = 0;
        committedHeadingX = 0d;
        committedHeadingY = 0d;
        hasCommittedHeading = false;
        ResetBlockedMovementEvidence();
        obstacleRecoveryAttempt = 0;
        obstacleRecoveryWaypoint = null;
        obstacleRecoveryOrigin = default;
        obstacleRecoveryInitialDistance = 0d;
        obstacleRecoveryLastProgressAt = null;
    }

    private static int FindMatchingActionPortal(
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        int preferredStart,
        FieldNavigationRouteAction action)
    {
        for (var index = Math.Clamp(preferredStart, 0, portals.Count);
             index < portals.Count;
             index++)
        {
            if (IsMatchingActionPortal(portals, index, action))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsMatchingActionPortal(
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        int index,
        FieldNavigationRouteAction action) =>
        index >= 0 &&
        index < portals.Count &&
        portals[index].TransitionKind == action.Kind &&
        string.Equals(
            portals[index].TransitionId,
            action.StableId,
            StringComparison.Ordinal);

    private bool TryBuild(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        bool replanned,
        string reason,
        out FieldNavigationRouteGuidance guidance)
    {
        guidance = default;
        if (!planner.TryResolvePlayerTriangle(position, out var resolvedTriangle) ||
            !planner.TryBuildRoute(position, target, out var builtPlan))
        {
            Reset();
            return false;
        }

        return CommitPlan(position, builtPlan, resolvedTriangle, replanned, reason, out guidance);
    }

    private bool CommitPlan(
        FieldPositionSnapshot position,
        FieldNavigationRoutePlan builtPlan,
        int resolvedTriangle,
        bool replanned,
        string reason,
        out FieldNavigationRouteGuidance guidance)
    {
        plan = builtPlan;
        stableWaypoints = builtPlan.StableWaypointsOverride ??
            FieldWalkmeshPathfinder.BuildStableWaypoints(
                position.X,
                position.Y,
                position.Z,
                builtPlan.Portals,
                builtPlan.FinalApproach);
        portalIndex = Math.Max(0, FindRouteIndex(builtPlan.TrianglePath, resolvedTriangle, 0));
        portalIndex = Math.Min(portalIndex, builtPlan.Portals.Count);
        waypointIndex = 0;
        guidanceWaypointIndex = 0;
        offRouteDistance = 0d;
        offRouteSamples = 0;
        headingHeldOffRouteDistance = 0d;
        headingHeldOffRouteSamples = 0;
        consecutiveTargetMovementSamples = 0;
        lastPosition = position;
        routeOrigin = ToWaypoint(position);
        routeOriginPortalIndex = portalIndex;
        guidance = CreateGuidance(
            position,
            builtPlan,
            stableWaypoints,
            portalIndex,
            waypointIndex,
            resolvedTriangle,
            replanned,
            replanned ? $"route replanned ({reason})" : "route started",
            routeOrigin,
            routeOriginPortalIndex,
            FindRouteIndex(builtPlan.TrianglePath, resolvedTriangle, portalIndex));
        currentResolvedTriangle = resolvedTriangle;
        currentGuidance = guidance;
        UpdateCommittedHeading(position, guidance.Waypoint);
        return true;
    }

    private static FieldNavigationRouteGuidance CreateGuidance(
        FieldPositionSnapshot position,
        FieldNavigationRoutePlan plan,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int portalIndex,
        int waypointIndex,
        int resolvedTriangle,
        bool replanned,
        string state,
        FieldNavigationRouteWaypoint routeOrigin,
        int routeOriginPortalIndex,
        int progressRouteIndex,
        FieldNavigationRouteWaypoint? waypointOverride = null)
    {
        var resolvedWaypointIndex = Math.Clamp(waypointIndex, 0, Math.Max(0, stableWaypoints.Count - 1));
        var waypoint = waypointOverride ?? (stableWaypoints.Count == 0
            ? plan.FinalApproach
            : stableWaypoints[resolvedWaypointIndex].Waypoint);
        var remainingDistance = CalculateRemainingDistance(
            position,
            stableWaypoints,
            resolvedWaypointIndex,
            plan.FinalApproach,
            plan.FinalApproachToTargetDistance);
        var progressRemainingDistance = CalculateProgressRemainingDistance(
            position,
            routeOrigin,
            routeOriginPortalIndex,
            stableWaypoints,
            plan.FinalApproach,
            plan.FinalApproachToTargetDistance,
            progressRouteIndex >= 0 ? progressRouteIndex : portalIndex);
        var nextAction = FindNextAction(plan.Portals, portalIndex);
        return new FieldNavigationRouteGuidance(
            waypoint,
            portalIndex,
            plan.Portals.Count,
            remainingDistance,
            replanned,
            nextAction,
            $"{state}, field={plan.FieldId}, target={plan.TargetId}, " +
            $"nativeTriangle={position.TriangleId}, resolvedTriangle={resolvedTriangle}, " +
            $"portal={portalIndex}/{plan.Portals.Count}, " +
            $"waypointStep={resolvedWaypointIndex + 1}/{Math.Max(1, stableWaypoints.Count)}, " +
            $"waypoint={waypoint.X},{waypoint.Y},{waypoint.Z}, " +
            $"remaining={remainingDistance:0}, progressRemaining={progressRemainingDistance:0}" +
            (nextAction is null ? string.Empty : $", nextAction={nextAction.Value.Kind}:{nextAction.Value.StableId}"),
            progressRemainingDistance);
    }

    private static FieldNavigationRouteAction? FindNextAction(
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        int portalIndex)
    {
        for (var index = Math.Clamp(portalIndex, 0, portals.Count); index < portals.Count; index++)
        {
            var portal = portals[index];
            if (portal.TransitionKind is not { } kind || string.IsNullOrWhiteSpace(portal.TransitionId))
            {
                continue;
            }

            return new FieldNavigationRouteAction(
                kind,
                portal.TransitionId,
                portal.Midpoint,
                portal.RequiredInput,
                portal.TransitionExit ?? portal.Midpoint,
                portal.ToTriangle,
                portal.RequiresAction,
                index);
        }

        return null;
    }

    private static double CalculateRemainingDistance(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int waypointIndex,
        FieldNavigationRouteWaypoint finalApproach,
        double finalApproachToTargetDistance)
    {
        var previous = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
        var distance = 0d;
        for (var index = waypointIndex; index < stableWaypoints.Count; index++)
        {
            var next = stableWaypoints[index].Waypoint;
            distance += Distance(previous, next);
            previous = next;
        }

        if (stableWaypoints.Count == 0 || previous != finalApproach)
        {
            distance += Distance(previous, finalApproach);
        }

        return distance + Math.Max(0d, finalApproachToTargetDistance);
    }

    private static double CalculateProgressRemainingDistance(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint routeOrigin,
        int routeOriginPortalIndex,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        FieldNavigationRouteWaypoint finalApproach,
        double finalApproachToTargetDistance,
        int routeIndex)
    {
        var routeSteps = stableWaypoints.Count == 0
            ? [new FieldNavigationRouteStep(finalApproach, routeOriginPortalIndex)]
            : stableWaypoints;
        var current = ToWaypoint(position);
        var previous = routeOrigin;
        var previousPortalIndex = routeOriginPortalIndex;
        var distanceBeforeSegment = 0d;
        var totalRouteDistance = 0d;
        var bestDistanceFromRouteSquared = double.PositiveInfinity;
        var bestDistanceAlongRoute = 0d;
        var foundMatchingSegment = false;

        foreach (var step in routeSteps)
        {
            var segmentLength = Distance(previous, step.Waypoint);
            var segmentMatchesRouteIndex =
                routeIndex < 0 ||
                routeIndex >= Math.Min(previousPortalIndex, step.RequiredPortalIndex) &&
                routeIndex <= Math.Max(previousPortalIndex, step.RequiredPortalIndex);
            if (segmentMatchesRouteIndex)
            {
                var projection = ProjectOntoSegment(current, previous, step.Waypoint);
                if (!foundMatchingSegment ||
                    projection.DistanceSquared < bestDistanceFromRouteSquared)
                {
                    foundMatchingSegment = true;
                    bestDistanceFromRouteSquared = projection.DistanceSquared;
                    bestDistanceAlongRoute =
                        distanceBeforeSegment + projection.Amount * segmentLength;
                }
            }

            distanceBeforeSegment += segmentLength;
            totalRouteDistance += segmentLength;
            previous = step.Waypoint;
            previousPortalIndex = step.RequiredPortalIndex;
        }

        if (previous != finalApproach)
        {
            var segmentLength = Distance(previous, finalApproach);
            var segmentMatchesRouteIndex =
                routeIndex < 0 ||
                routeIndex >= previousPortalIndex;
            if (segmentMatchesRouteIndex)
            {
                var projection = ProjectOntoSegment(current, previous, finalApproach);
                if (!foundMatchingSegment ||
                    projection.DistanceSquared < bestDistanceFromRouteSquared)
                {
                    foundMatchingSegment = true;
                    bestDistanceFromRouteSquared = projection.DistanceSquared;
                    bestDistanceAlongRoute =
                        distanceBeforeSegment + projection.Amount * segmentLength;
                }
            }

            totalRouteDistance += segmentLength;
        }

        if (!foundMatchingSegment)
        {
            previous = routeOrigin;
            distanceBeforeSegment = 0d;
            foreach (var step in routeSteps)
            {
                var segmentLength = Distance(previous, step.Waypoint);
                var projection = ProjectOntoSegment(current, previous, step.Waypoint);
                if (projection.DistanceSquared < bestDistanceFromRouteSquared)
                {
                    bestDistanceFromRouteSquared = projection.DistanceSquared;
                    bestDistanceAlongRoute =
                        distanceBeforeSegment + projection.Amount * segmentLength;
                }

                distanceBeforeSegment += segmentLength;
                previous = step.Waypoint;
            }
        }

        return Math.Max(
            0d,
            totalRouteDistance - Math.Clamp(bestDistanceAlongRoute, 0d, totalRouteDistance)) +
            Math.Max(0d, finalApproachToTargetDistance);
    }

    private static (double Amount, double DistanceSquared) ProjectOntoSegment(
        FieldNavigationRouteWaypoint point,
        FieldNavigationRouteWaypoint segmentStart,
        FieldNavigationRouteWaypoint segmentEnd)
    {
        var segmentX = segmentEnd.X - segmentStart.X;
        var segmentY = segmentEnd.Y - segmentStart.Y;
        var segmentZ = segmentEnd.Z - segmentStart.Z;
        var lengthSquared =
            segmentX * (double)segmentX +
            segmentY * (double)segmentY +
            segmentZ * (double)segmentZ;
        if (lengthSquared <= 0.001d)
        {
            var pointX = point.X - segmentStart.X;
            var pointY = point.Y - segmentStart.Y;
            var pointZ = point.Z - segmentStart.Z;
            return (
                0d,
                pointX * (double)pointX +
                pointY * (double)pointY +
                pointZ * (double)pointZ);
        }

        var amount = Math.Clamp(
            ((point.X - segmentStart.X) * segmentX +
             (point.Y - segmentStart.Y) * segmentY +
             (point.Z - segmentStart.Z) * segmentZ) /
            lengthSquared,
            0d,
            1d);
        var closestX = segmentStart.X + amount * segmentX;
        var closestY = segmentStart.Y + amount * segmentY;
        var closestZ = segmentStart.Z + amount * segmentZ;
        var distanceX = point.X - closestX;
        var distanceY = point.Y - closestY;
        var distanceZ = point.Z - closestZ;
        return (
            amount,
            distanceX * distanceX +
            distanceY * distanceY +
            distanceZ * distanceZ);
    }

    private bool ShouldReplanForDeviation(
        FieldPositionSnapshot? previousPosition,
        FieldPositionSnapshot position,
        int minimumSamples = 1,
        bool acceptWaypointProgress = true)
    {
        if (previousPosition is null)
        {
            return false;
        }

        var movement = Distance(ToWaypoint(previousPosition.Value), ToWaypoint(position));
        if (movement <= 0d)
        {
            return false;
        }

        var activeWaypoint = stableWaypoints.Count == 0
            ? plan?.FinalApproach
            : stableWaypoints[Math.Clamp(waypointIndex, 0, stableWaypoints.Count - 1)].Waypoint;
        if (acceptWaypointProgress && activeWaypoint is { } waypoint)
        {
            var previous = ToWaypoint(previousPosition.Value);
            var current = ToWaypoint(position);
            var waypointTolerance = GetWaypointTolerance(previousPosition, position);
            var crossedWaypoint =
                PassesWaypointBetweenSamples(
                    previous,
                    current,
                    waypoint,
                    waypointTolerance);
            var previousDistance = Distance(previous, waypoint);
            var currentDistance = Distance(current, waypoint);
            if (crossedWaypoint || currentDistance + 0.5d < previousDistance)
            {
                // Native triangle identities can diverge from the committed
                // corridor while Cloud is running. Progress toward (or across)
                // the active waypoint is route confirmation, not deviation.
                ResetOffRouteEvidence();
                return false;
            }
        }

        offRouteDistance += movement;
        offRouteSamples++;
        return offRouteDistance >= OffRouteDistanceBeforeReplan &&
               offRouteSamples >= Math.Max(1, minimumSamples);
    }

    private void ResetOffRouteEvidence()
    {
        offRouteDistance = 0d;
        offRouteSamples = 0;
    }

    private bool ShouldReplanForHeadingHeldDeviation(
        FieldPositionSnapshot? previousPosition,
        FieldPositionSnapshot position)
    {
        if (previousPosition is null)
        {
            return false;
        }

        var movement = Distance(ToWaypoint(previousPosition.Value), ToWaypoint(position));
        if (movement <= 0d)
        {
            return false;
        }

        var activeWaypoint = stableWaypoints.Count == 0
            ? plan?.FinalApproach
            : stableWaypoints[Math.Clamp(waypointIndex, 0, stableWaypoints.Count - 1)].Waypoint;
        if (activeWaypoint is { } waypoint)
        {
            var previous = ToWaypoint(previousPosition.Value);
            var current = ToWaypoint(position);
            var waypointTolerance = GetWaypointTolerance(previousPosition, position);
            if (PassesWaypointBetweenSamples(previous, current, waypoint, waypointTolerance) ||
                Distance(current, waypoint) + 0.5d < Distance(previous, waypoint))
            {
                ResetHeadingHeldOffRouteEvidence();
                return false;
            }
        }

        headingHeldOffRouteDistance += movement;
        headingHeldOffRouteSamples++;
        return headingHeldOffRouteDistance >= OffRouteDistanceBeforeReplan &&
               headingHeldOffRouteSamples >= HeadingHeldOffRouteSamplesBeforeReplan;
    }

    private void ResetHeadingHeldOffRouteEvidence()
    {
        headingHeldOffRouteDistance = 0d;
        headingHeldOffRouteSamples = 0;
    }

    private void AdvanceWaypoint(
        FieldPositionSnapshot? previousPosition,
        FieldPositionSnapshot position,
        bool corridorConfirmed)
    {
        var waypointTolerance = GetWaypointTolerance(previousPosition, position);
        while (waypointIndex < stableWaypoints.Count - 1)
        {
            var step = stableWaypoints[waypointIndex];
            var stepTolerance = step.MustReach
                ? MinimumWaypointArrivalDistance
                : waypointTolerance;
            var currentWaypoint = ToWaypoint(position);
            var crossedWaypoint = previousPosition is not null &&
                                    PassesWaypointBetweenSamples(
                                        ToWaypoint(previousPosition.Value),
                                        currentWaypoint,
                                        step.Waypoint,
                                        stepTolerance);
            var waypointDistance = Distance(currentWaypoint, step.Waypoint);
            var passedRoutePlane =
                corridorConfirmed &&
                !step.MustReach &&
                PassesWaypointPlane(
                    waypointIndex == 0
                        ? routeOrigin
                        : stableWaypoints[waypointIndex - 1].Waypoint,
                    step.Waypoint,
                    currentWaypoint,
                    stepTolerance);
            // A required approach prevents lookahead from skipping a steep
            // entrance, but native triangle progress wins once Cloud has
            // entered the steep continuation. Running can legitimately cross
            // a wide sloped portal through the opposite endpoint and miss the
            // planar funnel corner's small arrival radius.
            var enteredSteepContinuation =
                corridorConfirmed &&
                portalIndex >= Math.Max(0, step.RequiredPortalIndex - 1);
            if (step.MustReach &&
                !enteredSteepContinuation &&
                waypointDistance > stepTolerance &&
                !crossedWaypoint)
            {
                break;
            }

            if (!step.MustReach &&
                portalIndex < step.RequiredPortalIndex &&
                waypointDistance > MinimumWaypointArrivalDistance &&
                !crossedWaypoint &&
                !passedRoutePlane)
            {
                break;
            }

            waypointIndex++;
        }
    }

    private FieldNavigationRouteHeading CreateLookaheadHeading(
        FieldNavigationMovementObservation movement)
    {
        var isBlocked =
            obstacleRecoveryWaypoint is null &&
            obstacleRecoveryReady;
        if (!hasCommittedHeading)
        {
            return movement.IsUsable && movement.IsMoving
                ? new FieldNavigationRouteHeading(
                    true,
                    movement.DeltaX,
                    movement.DeltaY,
                    movement.Diagnostic,
                    isBlocked,
                    blockedMovementSamples,
                    obstacleRecoveryAttempt,
                    obstacleRecoveryWaypoint)
                : default;
        }

        if (Math.Abs(committedHeadingX) <= 0.000001d &&
            Math.Abs(committedHeadingY) <= 0.000001d)
        {
            return default;
        }

        return new FieldNavigationRouteHeading(
            true,
            committedHeadingX,
            committedHeadingY,
            obstacleRecoveryWaypoint is null
                ? "committed route heading"
                : "committed route heading with active obstacle recovery",
            isBlocked,
            blockedMovementSamples,
            obstacleRecoveryAttempt,
            obstacleRecoveryWaypoint);
    }

    private void UpdateObstacleRecoveryState(
        FieldPositionSnapshot? previousPosition,
        FieldPositionSnapshot position,
        FieldNavigationMovementObservation movement,
        DateTime observedAt)
    {
        var current = ToWaypoint(position);
        if (obstacleRecoveryWaypoint is { } recovery &&
            Distance(current, recovery) <= GetObstacleRecoveryArrivalDistance() &&
            Distance(obstacleRecoveryOrigin, current) >= BlockedProgressDistance)
        {
            ClearObstacleRecovery(resetAttempt: true);
            ResetBlockedMovementEvidence();
            return;
        }

        if (obstacleRecoveryWaypoint is { } activeRecovery)
        {
            if (previousPosition is { } previous)
            {
                var previousWaypoint = ToWaypoint(previous);
                var currentWaypoint = ToWaypoint(position);
                var actualMovement = Distance(previousWaypoint, currentWaypoint);
                if (actualMovement > 0.001d)
                {
                    var previousRecoveryDistance = Distance(previousWaypoint, activeRecovery);
                    var currentRecoveryDistance = Distance(currentWaypoint, activeRecovery);
                    var recoveryX = activeRecovery.X - previousWaypoint.X;
                    var recoveryY = activeRecovery.Y - previousWaypoint.Y;
                    var movementX = currentWaypoint.X - previousWaypoint.X;
                    var movementY = currentWaypoint.Y - previousWaypoint.Y;
                    var recoveryLength = Math.Sqrt(
                        recoveryX * (double)recoveryX +
                        recoveryY * (double)recoveryY);
                    var movementLength = Math.Sqrt(
                        movementX * (double)movementX +
                        movementY * (double)movementY);
                    var alignment =
                        recoveryLength <= 0.001d ||
                        movementLength <= 0.001d
                            ? 1d
                            : (movementX * recoveryX + movementY * recoveryY) /
                              (movementLength * recoveryLength);
                    var progressingTowardRecovery =
                        currentRecoveryDistance <= previousRecoveryDistance - RecoveryProgressEpsilon &&
                        alignment >= RecoveryDirectionAlignment;
                    if (progressingTowardRecovery)
                    {
                        obstacleRecoveryLastProgressAt = observedAt;
                        ResetBlockedMovementEvidence();
                        return;
                    }

                    if (movement.IsMoving || actualMovement >= BlockedProgressDistance)
                    {
                        // The player resumed a real route movement rather than following
                        // the local side-step. Do not let the stale recovery point override
                        // the connected corridor, which was the source of contradictory
                        // guidance in the compact Honey Bee Inn.
                        ClearObstacleRecovery(resetAttempt: true);
                        ResetBlockedMovementEvidence();
                        return;
                    }
                }
            }

            if (!movement.IsUsable || !IsDirectionalInput(movement.Input))
            {
                ResetBlockedMovementEvidence();
                return;
            }

            obstacleRecoveryLastProgressAt ??= observedAt;
            if (observedAt - obstacleRecoveryLastProgressAt.Value >= BlockedDurationBeforeRecoveryRetry)
            {
                ClearObstacleRecovery(resetAttempt: false);
                obstacleRecoveryAttempt++;
                ResetBlockedMovementEvidence();
            }

            return;
        }

        if (!movement.IsUsable)
        {
            ResetBlockedMovementEvidence();
            return;
        }

        if (blockedMovementSince is not null &&
            (movement.IsMoving ||
             Distance(blockedMovementOrigin, current) >= BlockedProgressDistance))
        {
            // Native position is the authoritative collision result. Any material
            // displacement means the player is moving, even when the sampled input
            // changed between frames or the movement observer quantized the delta.
            ResetBlockedMovementEvidence();
            return;
        }

        if (!IsDirectionalInput(movement.Input))
        {
            if (blockedMovementLastDirectionalAt is null ||
                observedAt - blockedMovementLastDirectionalAt.Value > BlockedInputReleaseGrace)
            {
                ResetBlockedMovementEvidence();
            }

            return;
        }

        if (blockedMovementSince is null)
        {
            BeginBlockedMovementEvidence(current, movement.Input, observedAt);
            return;
        }

        // Players naturally release or change the stick after a direction fails.
        // Keep the stationary attempt anchored to native position instead of
        // restarting the timer for every new direction.
        blockedMovementInput = movement.Input;
        blockedMovementLastDirectionalAt = observedAt;
        blockedMovementSamples++;
        obstacleRecoveryReady =
            blockedMovementSamples >= MinimumBlockedMovementSamplesBeforeRecovery &&
            observedAt - blockedMovementSince.Value >= BlockedDurationBeforeRecovery;
    }

    private double GetObstacleRecoveryArrivalDistance() =>
        Math.Clamp(
            obstacleRecoveryInitialDistance * 0.35d,
            BlockedProgressDistance,
            ObstacleRecoveryArrivalDistance);

    private void BeginBlockedMovementEvidence(
        FieldNavigationRouteWaypoint position,
        FieldNavigationInput input,
        DateTime observedAt)
    {
        blockedMovementSince = observedAt;
        blockedMovementLastDirectionalAt = observedAt;
        blockedMovementInput = input;
        blockedMovementOrigin = position;
        blockedMovementSamples = 1;
        obstacleRecoveryReady = false;
    }

    private void ResetBlockedMovementEvidence()
    {
        blockedMovementSince = null;
        blockedMovementLastDirectionalAt = null;
        blockedMovementInput = FieldNavigationInput.None;
        blockedMovementOrigin = default;
        blockedMovementSamples = 0;
        obstacleRecoveryReady = false;
    }

    private void ClearObstacleRecovery(bool resetAttempt)
    {
        obstacleRecoveryWaypoint = null;
        obstacleRecoveryOrigin = default;
        obstacleRecoveryInitialDistance = 0d;
        obstacleRecoveryLastProgressAt = null;
        if (resetAttempt)
        {
            obstacleRecoveryAttempt = 0;
        }
    }

    private static bool IsDirectionalInput(FieldNavigationInput input) =>
        input is
            FieldNavigationInput.Up or
            FieldNavigationInput.UpRight or
            FieldNavigationInput.Right or
            FieldNavigationInput.DownRight or
            FieldNavigationInput.Down or
            FieldNavigationInput.DownLeft or
            FieldNavigationInput.Left or
            FieldNavigationInput.UpLeft;

    private void UpdateCommittedHeading(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint? waypoint)
    {
        if (waypoint is not { } target)
        {
            return;
        }

        var deltaX = target.X - position.X;
        var deltaY = target.Y - position.Y;
        var length = Math.Sqrt(deltaX * (double)deltaX + deltaY * (double)deltaY);
        if (length <= 0.001d)
        {
            return;
        }

        committedHeadingX = deltaX / length;
        committedHeadingY = deltaY / length;
        hasCommittedHeading = true;
    }

    private static bool PassesWaypointPlane(
        FieldNavigationRouteWaypoint legStart,
        FieldNavigationRouteWaypoint legEnd,
        FieldNavigationRouteWaypoint position,
        double tolerance)
    {
        var legX = legEnd.X - legStart.X;
        var legY = legEnd.Y - legStart.Y;
        var lengthSquared = legX * (double)legX + legY * (double)legY;
        if (lengthSquared <= 0.001d)
        {
            return false;
        }

        var remainingX = legEnd.X - position.X;
        var remainingY = legEnd.Y - position.Y;
        var remainingAlongLeg = (remainingX * legX + remainingY * legY) / Math.Sqrt(lengthSquared);
        return remainingAlongLeg <= tolerance;
    }

    private static double GetWaypointTolerance(
        FieldPositionSnapshot? previousPosition,
        FieldPositionSnapshot position)
    {
        if (previousPosition is null)
        {
            return MinimumWaypointArrivalDistance;
        }

        var movement = Distance(
            ToWaypoint(previousPosition.Value),
            ToWaypoint(position));
        return Math.Clamp(
            movement,
            MinimumWaypointArrivalDistance,
            MaximumWaypointArrivalDistance);
    }

    private static int FindRouteIndex(
        IReadOnlyList<int> trianglePath,
        int triangleId,
        int preferredStart)
    {
        for (var index = Math.Max(0, preferredStart); index < trianglePath.Count; index++)
        {
            if (trianglePath[index] == triangleId)
            {
                return index;
            }
        }

        for (var index = Math.Min(preferredStart - 1, trianglePath.Count - 1); index >= 0; index--)
        {
            if (trianglePath[index] == triangleId)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasTargetMoved(FieldNavigationRoutePlan plan, FieldNavigationTarget target)
    {
        if (plan.TargetTriggerLine is not null || target.TriggerLine is not null)
        {
            return plan.TargetTriggerLine != target.TriggerLine;
        }

        var dx = target.X - plan.FinalApproach.X;
        var dy = target.Y - plan.FinalApproach.Y;
        var dz = target.Z - plan.FinalApproach.Z;
        var movementDistance = Math.Max(TargetMovementReplanDistance, Math.Max(0, target.InteractionRadius));
        var threshold = movementDistance * (double)movementDistance;
        return dx * (double)dx + dy * (double)dy + dz * (double)dz > threshold;
    }

    private static bool PassesWaypointBetweenSamples(
        FieldNavigationRouteWaypoint segmentStart,
        FieldNavigationRouteWaypoint segmentEnd,
        FieldNavigationRouteWaypoint point,
        double tolerance)
    {
        var segmentX = segmentEnd.X - segmentStart.X;
        var segmentY = segmentEnd.Y - segmentStart.Y;
        var segmentZ = segmentEnd.Z - segmentStart.Z;
        var lengthSquared = segmentX * (double)segmentX +
                            segmentY * (double)segmentY +
                            segmentZ * (double)segmentZ;
        if (lengthSquared <= 0d)
        {
            return false;
        }

        var pointX = point.X - segmentStart.X;
        var pointY = point.Y - segmentStart.Y;
        var pointZ = point.Z - segmentStart.Z;
        var projection =
            (pointX * segmentX + pointY * segmentY + pointZ * segmentZ) /
            lengthSquared;
        if (projection <= 0d || projection >= 1d)
        {
            return false;
        }

        var closest = new FieldNavigationRouteWaypoint(
            (int)Math.Round(segmentStart.X + projection * segmentX),
            (int)Math.Round(segmentStart.Y + projection * segmentY),
            (int)Math.Round(segmentStart.Z + projection * segmentZ));
        return Distance(closest, point) <= tolerance;
    }

    private static FieldNavigationRouteWaypoint ToWaypoint(FieldPositionSnapshot position) =>
        new(position.X, position.Y, position.Z);

    private static double Distance(FieldNavigationRouteWaypoint first, FieldNavigationRouteWaypoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        return Math.Sqrt(dx * (double)dx + dy * (double)dy + dz * (double)dz);
    }

    private static string GetTargetId(FieldNavigationTarget target) =>
        string.IsNullOrWhiteSpace(target.StableId)
            ? $"{target.FieldId}:{target.Category}:{target.Label}:{target.X}:{target.Y}:{target.Z}"
            : $"{target.FieldId}:{target.StableId}";
}
