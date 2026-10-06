namespace Ff7.Accessibility.Reloaded;

public sealed partial class WorldMapNavigationController
{
    private bool submarineManualDescent;
    private bool submarineDepthRecoveryAnnounced;
    private DateTime submarineEmeraldMissingSince = DateTime.MinValue;
    private string? submarineUnsafeApproach;

    private static bool UnderwaterRouteEndMoved(WorldMapNavigationTarget target, WorldMapRoutePlan route) =>
        route.Waypoints.Count == 0 ||
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(route.Waypoints[^1].X, target.X, 0x48000)) +
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(route.Waypoints[^1].Z, target.Z, 0x38000)) > 128;

    private bool HasFreshEmeraldSighting(WorldMapNavigationTarget target) =>
        entityProvider?.Invoke().Any(e => e.ModelId == 30 && e.IsVisibleUnderwater && !e.IsPlayer &&
            string.Equals($"world-entity:{e.GuestPointer:X8}:30", target.StableId, StringComparison.Ordinal)) == true;

    private static int UnderwaterHorizontalDistance(WorldMapStateSnapshot state, WorldMapNavigationTarget target) =>
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.X, target.X, 0x48000)) +
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.Z, target.Z, 0x38000));

    private WorldMapNavigationOutput? ObserveSubmarineApproach(WorldMapStateSnapshot state,
        WorldMapNavigationTarget target, int playerTriangle, bool automaticWalkActive, DateTime now)
    {
        if (state.PlayerModelId != 13 || state.WorldMapType != 2) return null;
        if (submarineUnsafeApproach is { } unsafeApproach)
        {
            ResetRoute();
            lastDiagnostic = unsafeApproach;
            return new($"{unsafeApproach} Auto walk stopped. Navigation off.", StopAutoWalk: true);
        }
        if (target.NativeUnderwaterArrival?.ModelId == 30)
        {
            if (HasFreshEmeraldSighting(target))
            { submarineEmeraldMissingSince = DateTime.MinValue; return null; }
            autoWalkConvergence.Reset();
            if (submarineEmeraldMissingSince == DateTime.MinValue) submarineEmeraldMissingSince = now;
            // Render flags are cleared and repopulated within a native frame. No
            // positive observation means no input immediately, but a short gap must
            // not discard the selection or claim the enemy left the screen.
            if (now - submarineEmeraldMissingSince < TimeSpan.FromMilliseconds(500)) return new(null);
            ResetRoute();
            lastDiagnostic = "explicit Emerald approach lost its positive native visibility observation";
            return new("Cannot confirm Emerald Weapon in the current view. Auto walk stopped. Navigation off.", StopAutoWalk: true);
        }
        if (target.NativeUnderwaterArrival is not null && !target.HasArrived(state, playerTriangle) &&
            UnderwaterHorizontalDistance(state, target) <= 128)
        {
            autoWalkConvergence.Reset();
            var first = !submarineManualDescent;
            submarineManualDescent = true;
            AutomaticInputHoldsSubmarineThrust = false;
            lastDiagnostic = $"above {target.Label}; native arrival requires manual descent";
            return new(first ? DescribeSubmarineDescent(target) : null, StopAutoWalk: true);
        }
        submarineManualDescent = false;
        if (automaticWalkActive && state.Y < WorldMapSubmarineSteering.TravelDepth)
        {
            autoWalkConvergence.Reset();
            var first = !submarineDepthRecoveryAnnounced;
            submarineDepthRecoveryAnnounced = true;
            return new(first ? "Rising to travel depth before continuing. This keeps the submarine clear of Emerald Weapon." : null);
        }
        submarineDepthRecoveryAnnounced = false;
        return null;
    }

    private static string DescribeSubmarineDescent(WorldMapNavigationTarget target) =>
        $"Above {target.Label}. Auto walk stopped. Hold Up to descend toward it. " +
        "Navigation remains on. Emerald Weapon may be nearby at this depth.";

    private string DescribeSubmarineGuidance(WorldMapStateSnapshot state, WorldMapNavigationTarget target)
    {
        if (submarineManualDescent || (target.NativeUnderwaterArrival?.ModelId is 17 or 26 or 28 &&
            UnderwaterHorizontalDistance(state, target) <= 128)) return DescribeSubmarineDescent(target);
        var emerald = target.NativeUnderwaterArrival?.ModelId == 30;
        var depth = emerald ? WorldMapSubmarineSteering.EmeraldDepth : WorldMapSubmarineSteering.TravelDepth;
        if (state.Y < depth - (emerald ? 120 : 0)) return "Rise to travel depth.";
        if (emerald && state.Y > depth + 120) return "Descending toward the selected Emerald Weapon.";
        var aim = activeRoute is { Waypoints.Count: > 0 } route
            ? route.Waypoints[Math.Clamp(waypointIndex, 0, route.Waypoints.Count - 1)]
            : new WorldMapRouteWaypoint(target.X, target.Y, target.Z);
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, aim.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, aim.Z, map.WrapHeight);
        var angle = Math.Atan2(dx, -dz) * 4096 / (Math.PI * 2);
        var turn = ((angle - state.CameraFront + 6144) % 4096) - 2048;
        var heading = Math.Abs(turn) <= 64 ? "Ahead" : turn > 0 ? "Turn right" : "Turn left";
        var counts = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * (double)dx + dz * (double)dz) / distanceUnitsPerCount));
        return $"{heading}. {target.Label}, {counts} counts to the next waypoint.";
    }

    private bool TryResolveSubmarineInput(WorldMapStateSnapshot state, out FieldNavigationInput input)
    {
        input = FieldNavigationInput.None;
        if (!beaconEnabled || combatPaused || !IsUsable(state) ||
            activeRoute is not { Waypoints.Count: > 0 } route || activeTarget is null || submarineManualDescent ||
            submarineUnsafeApproach is not null)
            return false;
        var emerald = activeTarget.NativeUnderwaterArrival?.ModelId == 30;
        if (emerald && !HasFreshEmeraldSighting(activeTarget)) return false;
        var measurement = MeasurePolylineProgress(routeStart, route.Waypoints, state, map.WrapWidth, map.WrapHeight);
        waypointIndex = ResolveAutomaticWaypoint(state, route,
            Math.Max(waypointIndex, measurement.NextWaypointIndex));
        var aim = route.Waypoints[waypointIndex];
        if (!WorldMapSubmarineSteering.TryResolve(state, aim, out input, out var thrust,
            emerald ? WorldMapSubmarineSteering.EmeraldDepth : WorldMapSubmarineSteering.TravelDepth))
        {
            lastDiagnostic = "world submarine has no checked native steering command";
            return false;
        }
        var forward = PredictNativeMovement(FieldNavigationInput.Up, state.CameraFront);
        var remaining = Math.Sqrt(Math.Pow(WorldMapTargetCatalog.WrappedDelta(state.X, aim.X, map.WrapWidth), 2) +
            Math.Pow(WorldMapTargetCatalog.WrappedDelta(state.Z, aim.Z, map.WrapHeight), 2));
        var leaseDistance = 30 * state.NativeFrameMultiplier *
            WorldMapSubmarineSurfaceSteering.LeaseUpdates(state.NativeFrameMultiplier) + 96;
        var horizon = thrust ? emerald ? leaseDistance : Math.Min(remaining, leaseDistance) : 0;
        var probe = new WorldMapRouteWaypoint(state.X + (int)Math.Round(forward.X * horizon), state.Y,
            state.Z + (int)Math.Round(forward.Z * horizon));
        string? blockedSite = null;
        if (emerald)
        {
            // Descending on the Key's script triangle fires its handler even without
            // horizontal movement; the ordinary route's escape exemption is insufficient.
            if (planner.TryResolvePlayerTriangle(state, out var currentTriangle) &&
                planner.EntranceTriangleIds.Contains(currentTriangle))
                blockedSite = "Key of the Ancients";
            else if (WorldMapUnderwaterEntryGuard.TryFindBlockedSite(state, probe,
                entityProvider?.Invoke().Any(e => e.ModelId == 28 && !e.IsPlayer) == true, out var site))
                blockedSite = site;
        }
        if (blockedSite is not null)
        {
            submarineUnsafeApproach = $"Approaching Emerald Weapon here could enter {blockedSite}.";
            lastDiagnostic = submarineUnsafeApproach;
            input = FieldNavigationInput.None;
            return false;
        }
        if (thrust)
        {
            if (!planner.CanTraverseSegment(state, probe, null, activeTarget.NativeEntranceExemptions))
            {
                lastDiagnostic = "world submarine forward step reaches blocked terrain or an unselected entrance";
                // A broad heading deadband works in open water. Near a bend it can put
                // the native forward vector across the bank even though the route itself
                // is clear. Turn closer in place, then recheck the actual forward step.
                if (WorldMapSubmarineSteering.TryResolve(state, aim, out input, out _,
                    emerald ? WorldMapSubmarineSteering.EmeraldDepth : WorldMapSubmarineSteering.TravelDepth, 24) &&
                    input is FieldNavigationInput.Left or FieldNavigationInput.Right)
                    return true;
                return false;
            }
        }
        AutomaticInputHoldsSubmarineThrust = thrust;
        return true;
    }
}
