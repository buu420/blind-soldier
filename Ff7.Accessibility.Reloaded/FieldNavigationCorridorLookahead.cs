namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationLookaheadMode
{
    RequiredCorner,
    VisibleStep,
    HeadingHeld,
    ObstacleRecovery,
    ReplanRequired
}

public readonly record struct FieldNavigationCorridorObservation(
    int ResolvedTriangle,
    FieldNavigationRouteWaypoint Waypoint,
    int StableWaypointIndex,
    FieldNavigationLookaheadMode Mode,
    bool ConfirmsRoute,
    string Diagnostic,
    FieldNavigationRouteWaypoint? RecoveryContinuation = null);

public readonly record struct FieldNavigationRouteHeading(
    bool IsUsable,
    double DeltaX,
    double DeltaY,
    string Diagnostic,
    bool IsBlocked = false,
    int BlockedSamples = 0,
    int RecoveryAttempt = 0,
    FieldNavigationRouteWaypoint? RecoveryWaypoint = null);

public interface IFieldNavigationCorridorLookaheadPlanner
{
    bool TryObserveCorridor(
        FieldPositionSnapshot position,
        FieldNavigationRoutePlan plan,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int waypointIndex,
        FieldNavigationRouteAction? nextAction,
        FieldNavigationRouteHeading heading,
        out FieldNavigationCorridorObservation observation);
}

public static class FieldNavigationCorridorLookahead
{
    private const double MinimumHeadingDistance = 96d;
    private const double MinimumRecoveryDistance = 24d;
    private const double RecoveryForwardDistance = 32d;
    private const double StackedRouteMaximumPlanarSeparation = 160d;
    private const double StackedRouteMinimumVerticalSeparation = 192d;
    private static readonly double[] FallbackHeadingProbeDistances = [240d, 192d, 144d, 96d];
    private static readonly double[] RecoverySideDistanceScales = [1d, 0.75d, 0.5d, 0.375d];
    private static readonly double[] RecoveryFanAngles = [15d, 30d, 45d, 60d, 75d, 90d];
    private static readonly double[] RecoveryFanDistances = [64d, 48d, 40d, 32d, 24d];
    private static readonly double[] DynamicObstacleBypassMargins = [8d, 16d, 32d, 48d, 64d];

    public static bool TryResolveDynamicObstacleDetour(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint routeWaypoint,
        int stableStepIndex,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation) =>
        TryResolveDynamicObstacleRecovery(
            walkmesh,
            resolvedTriangle,
            current,
            routeWaypoint,
            stableStepIndex,
            heading,
            isTriangleBlocked,
            dynamicObstacles,
            out observation);

    public static bool TryResolve(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldPositionSnapshot position,
        FieldNavigationRoutePlan plan,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int waypointIndex,
        FieldNavigationRouteAction? nextAction,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        out FieldNavigationCorridorObservation observation) =>
        TryResolve(
            walkmesh,
            resolvedTriangle,
            position,
            plan,
            stableWaypoints,
            waypointIndex,
            nextAction,
            heading,
            isTriangleBlocked,
            dynamicObstacles: null,
            out observation);

    public static bool TryResolve(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldPositionSnapshot position,
        FieldNavigationRoutePlan plan,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int waypointIndex,
        FieldNavigationRouteAction? nextAction,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        if (resolvedTriangle < 0 ||
            resolvedTriangle >= walkmesh.Triangles.Count ||
            position.FieldId != plan.FieldId)
        {
            return false;
        }

        var current = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
        if (stableWaypoints.Count == 0)
        {
            observation = new FieldNavigationCorridorObservation(
                resolvedTriangle,
                plan.FinalApproach,
                0,
                FieldNavigationLookaheadMode.RequiredCorner,
                false,
                "lookahead=required-corner, no stable route steps");
            return true;
        }

        var currentStepIndex = Math.Clamp(waypointIndex, 0, stableWaypoints.Count - 1);
        var actionPortalLimit = nextAction?.PortalIndex ?? int.MaxValue;
        var requiredStepLimit = stableWaypoints.Count - 1;
        for (var index = currentStepIndex; index < stableWaypoints.Count; index++)
        {
            if (!stableWaypoints[index].MustReach)
            {
                continue;
            }

            requiredStepLimit = index;
            if (index + 1 < stableWaypoints.Count &&
                stableWaypoints[index + 1].RequiredPortalIndex <= actionPortalLimit &&
                CanContinuePastRequiredPortalCorner(
                    walkmesh, resolvedTriangle, current, plan, stableWaypoints[index],
                    stableWaypoints[index + 1].Waypoint, isTriangleBlocked, dynamicObstacles))
            {
                requiredStepLimit = index + 1;
            }

            break;
        }

        var visibleStepIndex = -1;
        FieldWalkmeshSegmentTrace visibleTrace = default;
        for (var index = requiredStepLimit; index >= currentStepIndex; index--)
        {
            var step = stableWaypoints[index];
            if (step.RequiredPortalIndex > actionPortalLimit)
            {
                continue;
            }

            var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                walkmesh,
                resolvedTriangle,
                current,
                step.Waypoint,
                isTriangleBlocked);
            if (!trace.IsClear)
            {
                continue;
            }

            if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                    current,
                    step.Waypoint,
                    dynamicObstacles))
            {
                continue;
            }

            visibleStepIndex = index;
            visibleTrace = trace;
            break;
        }

        var headingStepIndex = visibleStepIndex >= 0
            ? visibleStepIndex
            : currentStepIndex;
        var headingWaypoint = stableWaypoints[headingStepIndex].Waypoint;
        var preserveOrderedStackedRoute = HasVerticallyStackedRoute(
            current,
            stableWaypoints,
            currentStepIndex);
        if (!preserveOrderedStackedRoute &&
            TryResolveActiveRecovery(
                walkmesh,
                resolvedTriangle,
                current,
                headingStepIndex,
                heading,
                isTriangleBlocked,
                dynamicObstacles,
                out observation))
        {
            return true;
        }

        if (!preserveOrderedStackedRoute && dynamicObstacles is { Count: > 0 })
        {
            // A funnel corner can sit inside a model's collision cylinder even
            // though the route safely continues beyond it. Probe the farthest
            // ordered step before the next mandatory action/corner and keep the
            // first native-walkable two-leg detour that clears the cylinder.
            for (var index = requiredStepLimit; index >= currentStepIndex; index--)
            {
                var recoveryStep = stableWaypoints[index];
                if (recoveryStep.RequiredPortalIndex > actionPortalLimit ||
                    !FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                        current,
                        recoveryStep.Waypoint,
                        dynamicObstacles))
                {
                    continue;
                }

                if (TryResolveDynamicObstacleRecovery(
                        walkmesh,
                        resolvedTriangle,
                        current,
                        recoveryStep.Waypoint,
                        index,
                        heading,
                        isTriangleBlocked,
                        dynamicObstacles,
                        out observation))
                {
                    return true;
                }
            }
        }

        if (!preserveOrderedStackedRoute &&
            heading.IsBlocked &&
            TryResolveObstacleRecovery(
                walkmesh,
                resolvedTriangle,
                current,
                headingWaypoint,
                headingStepIndex,
                heading,
                isTriangleBlocked,
                dynamicObstacles,
                out observation))
        {
            return true;
        }

        if (!preserveOrderedStackedRoute &&
            !heading.IsBlocked &&
            TryResolveHeadingHold(
                walkmesh,
                resolvedTriangle,
                current,
                headingWaypoint,
                headingStepIndex,
                nextAction,
                heading,
                isTriangleBlocked,
                dynamicObstacles,
                out observation))
        {
            return true;
        }

        if (!preserveOrderedStackedRoute &&
            visibleStepIndex <= currentStepIndex &&
            TryResolveCornerPortalHandoff(
                walkmesh, resolvedTriangle, current, plan, stableWaypoints,
                currentStepIndex, actionPortalLimit, isTriangleBlocked,
                dynamicObstacles, out observation))
        {
            return true;
        }

        if (visibleStepIndex < 0)
        {
            var requiredWaypoint =
                nextAction is { } action &&
                stableWaypoints[currentStepIndex].RequiredPortalIndex > action.PortalIndex
                    ? action.Waypoint
                    : stableWaypoints[currentStepIndex].Waypoint;
            observation = new FieldNavigationCorridorObservation(
                resolvedTriangle,
                requiredWaypoint,
                currentStepIndex,
                FieldNavigationLookaheadMode.RequiredCorner,
                false,
                "lookahead=required-corner, no forward stable step is directly walkable" +
                (preserveOrderedStackedRoute ? ", stacked-route=ordered" : string.Empty));
            return true;
        }

        var visibleWaypoint = stableWaypoints[visibleStepIndex].Waypoint;
        observation = new FieldNavigationCorridorObservation(
            resolvedTriangle,
            visibleWaypoint,
            visibleStepIndex,
            FieldNavigationLookaheadMode.VisibleStep,
            true,
            $"lookahead=visible-step, step={visibleStepIndex + 1}/{stableWaypoints.Count}, " +
            $"triangles={visibleTrace.TraversedTriangles.Count}" +
            (preserveOrderedStackedRoute ? ", stacked-route=ordered" : string.Empty));
        return true;
    }

    private static bool CanContinuePastRequiredPortalCorner(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRoutePlan plan,
        FieldNavigationRouteStep corner,
        FieldNavigationRouteWaypoint continuation,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles)
    {
        if (corner.RequiredPortalIndex <= 0 || corner.RequiredPortalIndex > plan.Portals.Count)
        {
            return false;
        }

        var portal = plan.Portals[corner.RequiredPortalIndex - 1];
        if (portal.FromTriangle != resolvedTriangle || portal.TransitionKind is not null ||
            portal.RequiresAction || (corner.Waypoint != portal.Left && corner.Waypoint != portal.Right))
        {
            return false;
        }

        // A wide ramp can be entered through the other endpoint, leaving the
        // funnel corner behind while still on its approach triangle. Continue
        // only when the next leg actually crosses this portal and stays clear;
        // proximity alone cannot prove this at the Mt. Corel upper-track turn.
        var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
            walkmesh, resolvedTriangle, current, continuation, isTriangleBlocked);
        return trace.IsClear && trace.TraversedTriangles.Count > 1 &&
            trace.TraversedTriangles[1] == portal.ToTriangle &&
            !FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, continuation, dynamicObstacles);
    }

    private static bool TryResolveCornerPortalHandoff(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRoutePlan plan,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int currentStepIndex,
        int actionPortalLimit,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        for (var portalIndex = 0; portalIndex + 1 < plan.Portals.Count; portalIndex++)
        {
            var entry = plan.Portals[portalIndex];
            var exit = plan.Portals[portalIndex + 1];
            var currentStep = stableWaypoints[currentStepIndex];
            if (currentStep.MustReach &&
                (currentStep.RequiredPortalIndex != portalIndex + 1 ||
                 (currentStep.Waypoint != entry.Left && currentStep.Waypoint != entry.Right)))
            {
                continue;
            }
            if (entry.FromTriangle != resolvedTriangle ||
                entry.ToTriangle != exit.FromTriangle ||
                entry.TransitionKind is not null || entry.RequiresAction ||
                exit.TransitionKind is not null || exit.RequiresAction ||
                portalIndex + 1 >= actionPortalLimit ||
                Math.Min(Distance2D(current, entry.Left), Distance2D(current, entry.Right)) >
                MinimumRecoveryDistance / 2d)
            {
                continue;
            }

            var continuationIndex = currentStepIndex;
            while (continuationIndex < stableWaypoints.Count &&
                   stableWaypoints[continuationIndex].RequiredPortalIndex <= portalIndex + 1 &&
                   !stableWaypoints[continuationIndex].MustReach)
            {
                continuationIndex++;
            }

            if (continuationIndex >= stableWaypoints.Count ||
                stableWaypoints[continuationIndex].RequiredPortalIndex > actionPortalLimit)
            {
                continue;
            }

            // A rounded inset endpoint can remain on the entry triangle and
            // yield zero steering. Aim briefly inside the triangle between its
            // ordinary entry and exit portals, with both legs verified natively.
            // First move toward the entry's center to clear its rounded endpoint,
            // then a quarter of the way toward the exit enters the destination.
            var entryMidpoint = entry.Midpoint;
            var exitMidpoint = exit.Midpoint;
            var endpoint = Distance2D(current, entry.Left) < Distance2D(current, entry.Right)
                ? entry.Left
                : entry.Right;
            var candidate = new FieldNavigationRouteWaypoint(
                (int)Math.Round((endpoint.X + entryMidpoint.X) * 0.375d + exitMidpoint.X * 0.25d),
                (int)Math.Round((endpoint.Y + entryMidpoint.Y) * 0.375d + exitMidpoint.Y * 0.25d),
                (int)Math.Round((endpoint.Z + entryMidpoint.Z) * 0.375d + exitMidpoint.Z * 0.25d));
            var continuation = stableWaypoints[continuationIndex].Waypoint;
            var entryTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                walkmesh, resolvedTriangle, current, candidate, isTriangleBlocked);
            if (!entryTrace.IsClear || entryTrace.EndTriangle != entry.ToTriangle ||
                FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, candidate, dynamicObstacles))
            {
                continue;
            }

            var exitTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                walkmesh, entryTrace.EndTriangle, candidate, continuation, isTriangleBlocked);
            if (!exitTrace.IsClear ||
                FieldNavigationDynamicObstacleGeometry.IntersectsAny(candidate, continuation, dynamicObstacles))
            {
                continue;
            }

            observation = new FieldNavigationCorridorObservation(
                resolvedTriangle, candidate, currentStepIndex,
                FieldNavigationLookaheadMode.VisibleStep, true,
                $"lookahead=corner-portal-handoff, portal={portalIndex + 1}, " +
                $"triangles={entryTrace.TraversedTriangles.Count}+{exitTrace.TraversedTriangles.Count}");
            return true;
        }

        return false;
    }

    private static bool HasVerticallyStackedRoute(
        FieldNavigationRouteWaypoint current,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int currentStepIndex)
    {
        var firstIndex = Math.Clamp(currentStepIndex, 0, stableWaypoints.Count - 1);
        for (var index = firstIndex; index < stableWaypoints.Count; index++)
        {
            var waypoint = stableWaypoints[index].Waypoint;
            if (VerticallyOverlaps(current, waypoint))
            {
                return true;
            }

            for (var laterIndex = index + 1;
                 laterIndex < stableWaypoints.Count;
                 laterIndex++)
            {
                if (VerticallyOverlaps(
                        waypoint,
                        stableWaypoints[laterIndex].Waypoint))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool VerticallyOverlaps(
        FieldNavigationRouteWaypoint first,
        FieldNavigationRouteWaypoint second)
    {
        if (Math.Abs(second.Z - first.Z) < StackedRouteMinimumVerticalSeparation)
        {
            return false;
        }

        return Distance2D(first, second) <= StackedRouteMaximumPlanarSeparation;
    }

    private static bool TryResolveActiveRecovery(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        int stableStepIndex,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        if (heading.RecoveryWaypoint is not { } recovery ||
            Distance2D(current, recovery) < MinimumRecoveryDistance / 2d)
        {
            return false;
        }

        var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
            walkmesh,
            resolvedTriangle,
            current,
            recovery,
            isTriangleBlocked);
        if (!trace.IsClear)
        {
            return false;
        }

        if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                current,
                recovery,
                dynamicObstacles))
        {
            return false;
        }

        observation = new FieldNavigationCorridorObservation(
            resolvedTriangle,
            recovery,
            stableStepIndex,
            FieldNavigationLookaheadMode.ObstacleRecovery,
            false,
            $"lookahead=obstacle-recovery, active, distance={Distance2D(current, recovery):0}, " +
            $"triangles={trace.TraversedTriangles.Count}");
        return true;
    }

    private static bool TryResolveDynamicObstacleRecovery(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint visibleWaypoint,
        int stableStepIndex,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        var routeX = visibleWaypoint.X - current.X;
        var routeY = visibleWaypoint.Y - current.Y;
        var routeLength = Math.Sqrt(routeX * (double)routeX + routeY * (double)routeY);
        if (routeLength <= 0.001d)
        {
            observation = default;
            return false;
        }

        var maximumBlockingClearance = dynamicObstacles?
            .Where(obstacle =>
                FieldNavigationDynamicObstacleGeometry.Intersects(
                    current,
                    visibleWaypoint,
                    obstacle))
            .Select(obstacle => obstacle.ClearanceRadius)
            .DefaultIfEmpty(0d)
            .Max() ?? 0d;
        var desiredSideDistance = Math.Max(64d, maximumBlockingClearance * 1.5d);
        var recoveryAttempt = Math.Max(
            heading.RecoveryAttempt,
            Math.Max(
                0,
                (int)Math.Ceiling((desiredSideDistance - 64d) / 24d) * 2));
        var recoveryHeading = heading.IsUsable
            ? heading with
            {
                IsBlocked = true,
                RecoveryAttempt = recoveryAttempt
            }
            : new FieldNavigationRouteHeading(
                true,
                routeX,
                routeY,
                "native dynamic model collision",
                IsBlocked: true,
                RecoveryAttempt: recoveryAttempt);
        if (!TryResolveObstacleRecovery(
                walkmesh,
                resolvedTriangle,
                current,
                visibleWaypoint,
                stableStepIndex,
                recoveryHeading,
                isTriangleBlocked,
                dynamicObstacles,
                out observation) &&
            !TryResolveDynamicObstacleBypass(
                walkmesh,
                resolvedTriangle,
                current,
                visibleWaypoint,
                stableStepIndex,
                recoveryHeading,
                routeX,
                routeY,
                routeLength,
                isTriangleBlocked,
                dynamicObstacles,
                out observation) &&
            !TryResolveDynamicObstacleContinuation(
                walkmesh, resolvedTriangle, current, visibleWaypoint,
                stableStepIndex, isTriangleBlocked, dynamicObstacles,
                out observation))
        {
            return false;
        }

        observation = observation with
        {
            Diagnostic = $"{observation.Diagnostic}, dynamic-model-collision"
        };
        return true;
    }

    private static bool TryResolveDynamicObstacleContinuation(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint routeWaypoint,
        int stableStepIndex,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        if (dynamicObstacles is null)
        {
            return false;
        }

        var blockingModels = dynamicObstacles
            .Where(model => FieldNavigationDynamicObstacleGeometry.Intersects(current, routeWaypoint, model))
            .OrderBy(model => Math.Pow(model.X - current.X, 2) + Math.Pow(model.Y - current.Y, 2))
            .Take(1);
        var routeDistance = Distance2D(current, routeWaypoint);
        var bestDistance = double.PositiveInfinity;
        foreach (var model in blockingModels)
        {
            // One bend cannot always pass a model beside a native wall. A fixed
            // ring around the first blocking model bounds this local search.
            // All three legs must be clear before exposing the first bend; from
            // there the ordinary two-leg recovery can resolve the continuation.
            var candidates = new List<FieldNavigationRouteWaypoint>();
            foreach (var margin in DynamicObstacleBypassMargins)
            {
                var radius = model.ClearanceRadius + model.PlayerCollisionRadius + margin;
                for (var degrees = 0; degrees < 360; degrees += 15)
                {
                    var angle = degrees * Math.PI / 180d;
                    var x = (int)Math.Round(model.X + radius * Math.Cos(angle));
                    var y = (int)Math.Round(model.Y + radius * Math.Sin(angle));
                    var distance = Math.Sqrt(Math.Pow(x - current.X, 2) + Math.Pow(y - current.Y, 2));
                    candidates.Add(new(x, y,
                        InterpolateHeadingZ(current, routeWaypoint, routeDistance, distance)));
                }
            }

            foreach (var first in candidates)
            {
                var firstDistance = Distance2D(current, first);
                if (firstDistance < MinimumRecoveryDistance ||
                    FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, first, dynamicObstacles))
                {
                    continue;
                }

                var firstTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    walkmesh, resolvedTriangle, current, first, isTriangleBlocked);
                if (!firstTrace.IsClear)
                {
                    continue;
                }

                foreach (var second in candidates)
                {
                    var middleDistance = Distance2D(first, second);
                    var totalDistance = firstDistance + middleDistance + Distance2D(second, routeWaypoint);
                    if (middleDistance < MinimumRecoveryDistance || totalDistance >= bestDistance ||
                        FieldNavigationDynamicObstacleGeometry.IntersectsAny(first, second, dynamicObstacles) ||
                        FieldNavigationDynamicObstacleGeometry.IntersectsAny(second, routeWaypoint, dynamicObstacles))
                    {
                        continue;
                    }

                    var middleTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        walkmesh, firstTrace.EndTriangle, first, second, isTriangleBlocked);
                    if (!middleTrace.IsClear || !FieldWalkmeshPathfinder.TraceWalkableSegment(
                            walkmesh, middleTrace.EndTriangle, second, routeWaypoint, isTriangleBlocked).IsClear)
                    {
                        continue;
                    }

                    bestDistance = totalDistance;
                    observation = new(resolvedTriangle, first, stableStepIndex,
                        FieldNavigationLookaheadMode.ObstacleRecovery, false,
                        $"lookahead=obstacle-recovery, model={model.ModelIndex}, three-leg-bypass, " +
                        $"continuation={second.X},{second.Y},{second.Z}, distance={totalDistance:0}",
                        RecoveryContinuation: second);
                }
            }
        }

        return double.IsFinite(bestDistance);
    }

    private static bool TryResolveDynamicObstacleBypass(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint visibleWaypoint,
        int stableStepIndex,
        FieldNavigationRouteHeading heading,
        double routeX,
        double routeY,
        double routeLength,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        if (dynamicObstacles is null || dynamicObstacles.Count == 0)
        {
            return false;
        }

        var forwardX = routeX / routeLength;
        var forwardY = routeY / routeLength;
        var sideX = -forwardY;
        var sideY = forwardX;
        var preferredSide = heading.RecoveryAttempt % 2 == 0 ? 1d : -1d;
        var blockingObstacles = dynamicObstacles
            .Where(obstacle =>
                FieldNavigationDynamicObstacleGeometry.Intersects(
                    current,
                    visibleWaypoint,
                    obstacle))
            .OrderBy(obstacle =>
                (obstacle.X - current.X) * forwardX +
                (obstacle.Y - current.Y) * forwardY);

        foreach (var obstacle in blockingObstacles)
        {
            var obstacleProgress = Math.Clamp(
                (obstacle.X - current.X) * forwardX +
                (obstacle.Y - current.Y) * forwardY,
                0d,
                routeLength);
            foreach (var margin in DynamicObstacleBypassMargins)
            {
                var clearanceDistance = obstacle.ClearanceRadius + margin;
                foreach (var sideDirection in new[] { preferredSide, -preferredSide })
                {
                    // The ordinary recovery fan starts beside Cloud. That is
                    // enough for a nearby model, but it barely changes a long
                    // segment when the model is at the far end. Put this
                    // fallback just beyond and beside the blocking cylinder so
                    // both verified legs pass the same place a sighted player
                    // can walk around it.
                    var candidate = new FieldNavigationRouteWaypoint(
                        (int)Math.Round(
                            obstacle.X +
                            forwardX * clearanceDistance +
                            sideX * clearanceDistance * sideDirection),
                        (int)Math.Round(
                            obstacle.Y +
                            forwardY * clearanceDistance +
                            sideY * clearanceDistance * sideDirection),
                        InterpolateHeadingZ(
                            current,
                            visibleWaypoint,
                            routeLength,
                            obstacleProgress + clearanceDistance));
                    var recoveryTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        walkmesh,
                        resolvedTriangle,
                        current,
                        candidate,
                        isTriangleBlocked);
                    if (!recoveryTrace.IsClear ||
                        FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                            current,
                            candidate,
                            dynamicObstacles))
                    {
                        continue;
                    }

                    var returnTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        walkmesh,
                        recoveryTrace.EndTriangle,
                        candidate,
                        visibleWaypoint,
                        isTriangleBlocked);
                    if (!returnTrace.IsClear ||
                        FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                            candidate,
                            visibleWaypoint,
                            dynamicObstacles))
                    {
                        continue;
                    }

                    observation = new FieldNavigationCorridorObservation(
                        resolvedTriangle,
                        candidate,
                        stableStepIndex,
                        FieldNavigationLookaheadMode.ObstacleRecovery,
                        false,
                        $"lookahead=obstacle-recovery, model={obstacle.ModelIndex}, " +
                        $"near-model-bypass, margin={margin:0}, " +
                        $"distance={Distance2D(current, candidate):0}, " +
                        $"triangles={recoveryTrace.TraversedTriangles.Count}");
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveObstacleRecovery(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint visibleWaypoint,
        int stableStepIndex,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        var headingLength = Math.Sqrt(
            heading.DeltaX * heading.DeltaX +
            heading.DeltaY * heading.DeltaY);
        if (!heading.IsUsable || headingLength <= 0.001d)
        {
            return false;
        }

        var forwardX = heading.DeltaX / headingLength;
        var forwardY = heading.DeltaY / headingLength;
        var sideX = -forwardY;
        var sideY = forwardX;
        var preferredSide = heading.RecoveryAttempt % 2 == 0 ? 1d : -1d;
        // A first recovery step only needs to clear Cloud's native collision
        // radius. Ninety-six units was large enough to cross most of the Honey
        // Bee Inn lobby and made a local correction feel like a new route.
        var baseSideDistance = 64d + Math.Min(64d, heading.RecoveryAttempt / 2 * 24d);
        var routeDistance = Distance2D(current, visibleWaypoint);
        var forwardDistance = Math.Min(
            RecoveryForwardDistance,
            Math.Max(0d, routeDistance / 3d));
        foreach (var sideDirection in new[] { preferredSide, -preferredSide })
        {
            foreach (var scale in RecoverySideDistanceScales)
            {
                var sideDistance = Math.Max(
                    MinimumRecoveryDistance,
                    baseSideDistance * scale);
                var candidate = new FieldNavigationRouteWaypoint(
                    (int)Math.Round(
                        current.X +
                        forwardX * forwardDistance +
                        sideX * sideDistance * sideDirection),
                    (int)Math.Round(
                        current.Y +
                        forwardY * forwardDistance +
                        sideY * sideDistance * sideDirection),
                    InterpolateHeadingZ(
                        current,
                        visibleWaypoint,
                        Math.Max(routeDistance, 0.001d),
                        forwardDistance));
                var recoveryTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    walkmesh,
                    resolvedTriangle,
                    current,
                    candidate,
                    isTriangleBlocked);
                if (!recoveryTrace.IsClear)
                {
                    continue;
                }

                if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                        current,
                        candidate,
                        dynamicObstacles))
                {
                    continue;
                }

                var returnTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    walkmesh,
                    recoveryTrace.EndTriangle,
                    candidate,
                    visibleWaypoint,
                    isTriangleBlocked);
                if (!returnTrace.IsClear)
                {
                    continue;
                }

                if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                        candidate,
                        visibleWaypoint,
                        dynamicObstacles))
                {
                    continue;
                }

                observation = new FieldNavigationCorridorObservation(
                    resolvedTriangle,
                    candidate,
                    stableStepIndex,
                    FieldNavigationLookaheadMode.ObstacleRecovery,
                    false,
                    $"lookahead=obstacle-recovery, blockedSamples={heading.BlockedSamples}, " +
                    $"attempt={heading.RecoveryAttempt + 1}, sideDistance={sideDistance:0}, " +
                    $"forwardDistance={forwardDistance:0}, triangles={recoveryTrace.TraversedTriangles.Count}");
                return true;
            }
        }

        // A perpendicular side-step can leave a narrow or diagonal walkmesh even
        // though a slight course correction would pass the obstacle. Probe a local
        // fan around the committed route heading and keep the smallest verified
        // correction that can reconnect to the visible route step.
        foreach (var angleMagnitude in RecoveryFanAngles)
        {
            foreach (var sideDirection in new[] { preferredSide, -preferredSide })
            {
                var angle = angleMagnitude * sideDirection * Math.PI / 180d;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                var candidateDirectionX = forwardX * cos - forwardY * sin;
                var candidateDirectionY = forwardX * sin + forwardY * cos;
                foreach (var distance in RecoveryFanDistances)
                {
                    var routeProgress = Math.Max(
                        0d,
                        (candidateDirectionX * forwardX + candidateDirectionY * forwardY) * distance);
                    var candidate = new FieldNavigationRouteWaypoint(
                        (int)Math.Round(current.X + candidateDirectionX * distance),
                        (int)Math.Round(current.Y + candidateDirectionY * distance),
                        InterpolateHeadingZ(
                            current,
                            visibleWaypoint,
                            Math.Max(routeDistance, 0.001d),
                            routeProgress));
                    var recoveryTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        walkmesh,
                        resolvedTriangle,
                        current,
                        candidate,
                        isTriangleBlocked);
                    if (!recoveryTrace.IsClear)
                    {
                        continue;
                    }

                    if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                            current,
                            candidate,
                            dynamicObstacles))
                    {
                        continue;
                    }

                    var returnTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        walkmesh,
                        recoveryTrace.EndTriangle,
                        candidate,
                        visibleWaypoint,
                        isTriangleBlocked);
                    if (!returnTrace.IsClear)
                    {
                        continue;
                    }

                    if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                            candidate,
                            visibleWaypoint,
                            dynamicObstacles))
                    {
                        continue;
                    }

                    observation = new FieldNavigationCorridorObservation(
                        resolvedTriangle,
                        candidate,
                        stableStepIndex,
                        FieldNavigationLookaheadMode.ObstacleRecovery,
                        false,
                        $"lookahead=obstacle-recovery, blockedSamples={heading.BlockedSamples}, " +
                        $"attempt={heading.RecoveryAttempt + 1}, fanAngle={angleMagnitude * sideDirection:0}, " +
                        $"distance={distance:0}, triangles={recoveryTrace.TraversedTriangles.Count}");
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveHeadingHold(
        FieldWalkmesh walkmesh,
        int resolvedTriangle,
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint visibleWaypoint,
        int visibleStepIndex,
        FieldNavigationRouteAction? nextAction,
        FieldNavigationRouteHeading heading,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldNavigationDynamicObstacle>? dynamicObstacles,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        var movementLength = Math.Sqrt(
            heading.DeltaX * heading.DeltaX +
            heading.DeltaY * heading.DeltaY);
        var routeX = visibleWaypoint.X - current.X;
        var routeY = visibleWaypoint.Y - current.Y;
        var routeLength = Math.Sqrt(routeX * (double)routeX + routeY * (double)routeY);
        if (!heading.IsUsable ||
            movementLength <= 0.001d ||
            routeLength <= 0.001d)
        {
            return false;
        }

        var movementX = heading.DeltaX / movementLength;
        var movementY = heading.DeltaY / movementLength;
        var alignment = movementX * routeX / routeLength + movementY * routeY / routeLength;
        if (alignment <= 0d)
        {
            return false;
        }

        // Project to the perpendicular plane of the farthest directly walkable
        // route step. A fixed 240-unit cap split one clear corridor into
        // repeated "up 4" / "down 4" instructions as the player crossed it.
        // The projection keeps the committed heading (and therefore tolerates a
        // harmless lateral offset) while connecting the full safe straight run.
        var maximumDistance = Math.Max(
            0d,
            movementX * routeX + movementY * routeY);
        if (nextAction is { } action)
        {
            maximumDistance = Math.Min(maximumDistance, Distance2D(current, action.Waypoint));
        }

        var probeDistances = new[] { maximumDistance }
            .Concat(FallbackHeadingProbeDistances)
            .Where(distance =>
                distance >= MinimumHeadingDistance &&
                distance <= maximumDistance + 0.001d)
            .DistinctBy(distance => Math.Round(distance, 3))
            .OrderByDescending(distance => distance);
        foreach (var probeDistance in probeDistances)
        {
            var candidate = new FieldNavigationRouteWaypoint(
                (int)Math.Round(current.X + movementX * probeDistance),
                (int)Math.Round(current.Y + movementY * probeDistance),
                InterpolateHeadingZ(current, visibleWaypoint, routeLength, probeDistance));

            var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                walkmesh,
                resolvedTriangle,
                current,
                candidate,
                isTriangleBlocked);
            if (!trace.IsClear)
            {
                continue;
            }

            if (FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                    current,
                    candidate,
                    dynamicObstacles))
            {
                continue;
            }

            observation = new FieldNavigationCorridorObservation(
                resolvedTriangle,
                candidate,
                visibleStepIndex,
                FieldNavigationLookaheadMode.HeadingHeld,
                true,
                $"lookahead=heading-held, alignment={alignment:0.000}, distance={probeDistance:0}, " +
                $"triangles={trace.TraversedTriangles.Count}");
            return true;
        }

        return false;
    }

    private static int InterpolateHeadingZ(
        FieldNavigationRouteWaypoint current,
        FieldNavigationRouteWaypoint visibleWaypoint,
        double routeLength,
        double probeDistance)
    {
        var amount = routeLength <= 0.001d
            ? 0d
            : Math.Clamp(probeDistance / routeLength, 0d, 1d);
        return (int)Math.Round(current.Z + (visibleWaypoint.Z - current.Z) * amount);
    }

    private static double Distance2D(
        FieldNavigationRouteWaypoint first,
        FieldNavigationRouteWaypoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt(dx * (double)dx + dy * (double)dy);
    }
}
