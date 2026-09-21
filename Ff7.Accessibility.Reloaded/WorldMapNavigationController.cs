namespace Ff7.Accessibility.Reloaded;

public delegate IReadOnlyList<WorldMapNavigationTarget> WorldMapTargetProvider(
    WorldMapStateSnapshot state,
    WorldMapNavigationCategory category);

public readonly record struct WorldMapNavigationOutput(
    string? Speech,
    bool StopAutoWalk = false);

public readonly record struct WorldMapNavigationProbeSnapshot(
    bool BeaconEnabled,
    WorldMapNavigationCategory Category,
    string TargetId,
    string TargetLabel,
    int WaypointIndex,
    int ProgressPercent,
    WorldMapRoutePlan? Route,
    string Diagnostic);

public static class WorldMapNavigationLifecycle
{
    // Ghidra: the world-map dispatcher enters module 0x17 for battle entry,
    // hands ownership to module 2, and battle completion writes module 0x11
    // before the dispatcher restores world-map module 3.
    public const int BattleTransitionModule = 0x17;
    public const int PostBattleResultsModule = 0x11;

    public static bool IsCombatInterruptionModule(int module) =>
        module is BattleTransitionModule or BattleStateReader.BattleModule or PostBattleResultsModule;
}

/// <summary>
/// Shared world-map navigation state machine.  Both executable architectures
/// feed this class the same checked guest state and therefore receive the same
/// target order, speech, progress, and arrival behavior.
/// </summary>
public sealed class WorldMapNavigationController
{
    private const double WaypointArrivalDistance = 480d;
    private const double AutomaticMovementProbeDistance = 120d;
    private const int DefaultDistanceUnitsPerCount = 512;
    private const int OffRouteReplanDistanceCounts = 12;
    private static readonly TimeSpan OffRouteReplanDelay = TimeSpan.FromSeconds(5);

    private readonly WorldMapData map;
    private readonly WorldMapRoutePlanner planner;
    private readonly WorldMapTargetProvider targetProvider;
    private readonly IFieldNavigationProgressSink? progressSink;
    private readonly int distanceUnitsPerCount;
    private readonly TimeSpan guidanceInterval;
    private readonly Dictionary<WorldMapNavigationCategory, int> selectedIndices = new();
    private readonly WorldMapAutoWalkConvergenceTracker autoWalkConvergence = new();
    private readonly FieldNavigationMovementObserver automaticDirection = new();

    // Live world entities, when the host supplies them, so a parked vehicle can be walked
    // around instead of pressed into.
    private readonly Func<IReadOnlyList<WorldMapEntitySnapshot>>? entityProvider;
    private WorldMapVehicleDetourPlanner? detourPlanner;
    private bool measuringLocalDetour;

    private int categoryIndex;
    private bool beaconEnabled;
    private bool combatPaused;
    private WorldMapNavigationTarget? activeTarget;
    private WorldMapRoutePlan? activeRoute;
    private WorldMapRouteWaypoint routeStart;
    private WorldMapRouteWaypoint progressRouteStart;
    private IReadOnlyList<WorldMapRouteWaypoint> progressRouteWaypoints = Array.Empty<WorldMapRouteWaypoint>();
    private int waypointIndex;

    // The native entry handoff is spoken once per route, not on every sample.
    private bool entryHandoffAnnounced;

    // Holding at a native entrance this model may not use. The destination is retained
    // and automatic walking yields no input until the party leaves the vehicle.
    private bool awaitingNativeEntry;
    private WorldMapRouteWaypoint nativeEntryHoldOrigin;
    private const double NativeEntryHoldRadius = 1024d;
    private int progressPercent;
    private int activeModelId = -1;
    private int activeMapType = -1;
    private int activeWorldProgress = int.MinValue;
    private DateTime lastGuidanceAt = DateTime.MinValue;
    private DateTime offRouteSince = DateTime.MinValue;
    private string lastGuidanceSignature = string.Empty;
    private string lastDiagnostic = "uninitialized";

    public WorldMapNavigationController(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        WorldMapTargetProvider targetProvider,
        IFieldNavigationProgressSink? progressSink = null,
        int distanceUnitsPerCount = DefaultDistanceUnitsPerCount,
        TimeSpan? guidanceInterval = null,
        Func<IReadOnlyList<WorldMapEntitySnapshot>>? entityProvider = null)
    {
        this.entityProvider = entityProvider;
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.targetProvider = targetProvider ?? throw new ArgumentNullException(nameof(targetProvider));
        this.progressSink = progressSink;
        this.distanceUnitsPerCount = Math.Max(1, distanceUnitsPerCount);
        this.guidanceInterval = Normalize(guidanceInterval ?? TimeSpan.FromSeconds(2));
    }

    public bool BeaconEnabled => beaconEnabled;

    public WorldMapNavigationCategory CurrentCategory =>
        WorldMapTargetCatalog.CategoryOrder[categoryIndex];

    public int CurrentProgressPercent => progressPercent;

    public string LastDiagnostic => lastDiagnostic;

    public WorldMapNavigationProbeSnapshot Probe => new(
        beaconEnabled,
        CurrentCategory,
        activeTarget?.StableId ?? string.Empty,
        activeTarget?.Label ?? string.Empty,
        waypointIndex,
        progressPercent,
        activeRoute,
        lastDiagnostic);

    public WorldMapNavigationOutput? HandleAction(
        FieldNavigationAction action,
        WorldMapStateSnapshot state,
        DateTime observedAt = default)
    {
        if (!IsUsable(state))
        {
            Reset();
            return null;
        }

        var now = observedAt == default ? DateTime.UtcNow : observedAt;
        switch (action)
        {
            case FieldNavigationAction.PreviousCategory:
                MoveCategory(-1);
                return RelockAndDescribe(state, now);
            case FieldNavigationAction.NextCategory:
                MoveCategory(1);
                return RelockAndDescribe(state, now);
            case FieldNavigationAction.PreviousTarget:
                MoveTarget(state, -1);
                return RelockAndDescribe(state, now);
            case FieldNavigationAction.NextTarget:
                MoveTarget(state, 1);
                return RelockAndDescribe(state, now);
            case FieldNavigationAction.RepeatTarget:
                if (!beaconEnabled)
                {
                    return DescribeSelection(state);
                }

                lastGuidanceSignature = CreateGuidanceSignature(state);
                lastGuidanceAt = now;
                return new WorldMapNavigationOutput(
                    CreateGuidanceSpeech(state, includeTarget: true, includeProgress: true));
            case FieldNavigationAction.ToggleBeacon:
                if (beaconEnabled)
                {
                    ResetRoute();
                    return new WorldMapNavigationOutput("Navigation off.");
                }

                var selected = GetSelectedTarget(state);
                if (selected is null)
                {
                    return DescribeSelection(state);
                }

                return StartNavigation(selected, state, now, announceOn: true);
            default:
                return null;
        }
    }

    public WorldMapNavigationOutput? Observe(
        WorldMapStateSnapshot state,
        DateTime observedAt = default,
        bool automaticWalkActive = false)
    {
        var now = observedAt == default ? DateTime.UtcNow : observedAt;
        if (!beaconEnabled)
        {
            autoWalkConvergence.Reset();
            return null;
        }

        if (!IsUsable(state))
        {
            if (combatPaused)
            {
                return null;
            }

            var label = activeTarget?.Label ?? "World route";
            ResetRoute();
            lastDiagnostic = "world navigation owner changed";
            return new WorldMapNavigationOutput($"{label} no longer available. Navigation off.");
        }

        var resumedAfterCombat = combatPaused;
        if (resumedAfterCombat)
        {
            combatPaused = false;
            autoWalkConvergence.Reset();
            progressSink?.Activate(progressPercent);
            lastGuidanceAt = DateTime.MinValue;
        }

        // Holding at an entrance the game will not open for this model. The destination is
        // kept and nothing is said again until something actually changes: either the
        // party leaves the vehicle, or they drive away and the route is wanted once more.
        if (awaitingNativeEntry && activeTarget is { } awaited)
        {
            // The native terrain handler restores the prior position after a refused
            // entry. Leaving the trigger triangle is not evidence of a dismount.
            if (ShouldHoldNativeEntry(awaited, state))
            {
                return null;
            }

            awaitingNativeEntry = false;
            entryHandoffAnnounced = false;
            var resumed = StartNavigation(awaited, state, now, announceOn: false);
            return resumed is null
                ? null
                : DescribeRouteTransition(resumed.Value, resumedAfterCombat);
        }

        if (state.PlayerModelId != activeModelId ||
            state.WorldMapType != activeMapType ||
            state.WorldProgress != activeWorldProgress)
        {
            if (activeTarget is not { } changedTarget)
            {
                ResetRoute();
                return null;
            }

            var changed = StartNavigation(changedTarget, state, now, announceOn: false);
            return changed is null
                ? null
                : DescribeRouteTransition(changed.Value, resumedAfterCombat);
        }

        if (activeTarget is not { } target || activeRoute is null)
        {
            ResetRoute();
            return null;
        }

        // A moving entity retains its stable identity but can change native
        // triangle.  Refresh only that identity; never substitute another
        // target merely because it occupies the same category slot.
        var refreshed = GetTargets(state)
            .FirstOrDefault(candidate => string.Equals(candidate.StableId, target.StableId, StringComparison.Ordinal));
        if (refreshed is not null && refreshed != target)
        {
            target = refreshed;
            activeTarget = refreshed;
            // Replan when the route no longer ends somewhere the target can be reached
            // from, not merely when the target's own centre triangle differs from the
            // triangle the route ends on. Those are different things: the catalog gives an
            // entity every walkable neighbour as an arrival, and the planner routes to
            // whichever of them it reaches first, so the centre and the route end normally
            // disagree for a vehicle that has not moved at all. Comparing them announced a
            // fresh route on every observation, which is how the parked Buggy came to be
            // repeated several times a second.
            if (!refreshed.ArrivalTriangleIds.Contains(activeRoute.TargetTriangleId))
            {
                var replanned = StartNavigation(refreshed, state, now, announceOn: false);
                return replanned is null
                    ? null
                    : DescribeRouteTransition(replanned.Value, resumedAfterCombat);
            }
        }

        if (!planner.TryResolvePlayerTriangle(state, out var playerTriangle))
        {
            lastDiagnostic = planner.LastDiagnostic;
            return null;
        }

        if (target.HasArrived(state, playerTriangle))
        {
            if (!IsNativeEntryModelSatisfied(target, state.PlayerModelId))
            {
                awaitingNativeEntry = true;
                nativeEntryHoldOrigin = new(state.X, state.Y, state.Z);
                lastDiagnostic =
                    $"native entry refuses model {state.PlayerModelId} on triangle {playerTriangle}";
                if (entryHandoffAnnounced)
                {
                    return null;
                }

                entryHandoffAnnounced = true;
                return new WorldMapNavigationOutput(
                    DescribeNativeEntryHandoff(target),
                    StopAutoWalk: true);
            }

            progressSink?.Complete();
            var label = target.Label;
            ResetRoute(deactivateProgress: false);
            lastDiagnostic = $"arrived on native triangle {playerTriangle}";
            return new WorldMapNavigationOutput($"Arrived at {label}. Navigation off.");
        }

        var routeMeasurement = MeasurePolylineProgress(
            routeStart,
            activeRoute.Waypoints,
            state,
            map.WrapWidth,
            map.WrapHeight);
        var offRouteReplanDistance = Math.Max(
            WaypointArrivalDistance * 3d,
            distanceUnitsPerCount * (double)OffRouteReplanDistanceCounts);
        var meaningfullyOffRoute =
            !activeRoute.TrianglePath.Contains(playerTriangle) &&
            routeMeasurement.DistanceFromRoute > offRouteReplanDistance;
        if (!meaningfullyOffRoute)
        {
            offRouteSince = DateTime.MinValue;
        }
        else if (offRouteSince == DateTime.MinValue)
        {
            // A wide world-map surface permits lateral movement. Retain the
            // committed route until a large deviation is sustained rather
            // than rebuilding it for every sample.
            offRouteSince = now;
        }
        else if (now - offRouteSince >= OffRouteReplanDelay)
        {
            var replanned = StartNavigation(
                target,
                state,
                now,
                announceOn: false,
                preserveProgressRoute: true);
            return replanned is null
                ? null
                : DescribeRouteTransition(replanned.Value, resumedAfterCombat);
        }

        var guidanceMeasurement = UpdateProgressAndWaypoint(state, automaticWalkActive);
        if (!automaticWalkActive)
        {
            autoWalkConvergence.Reset();
            measuringLocalDetour = false;
        }
        else
        {
            var remainingAlongRoute = Math.Max(
                0d,
                activeRoute.TotalDistance * (1d - guidanceMeasurement.Fraction));
            var remainingDistance = Math.Sqrt(
                remainingAlongRoute * remainingAlongRoute +
                guidanceMeasurement.DistanceFromRoute * guidanceMeasurement.DistanceFromRoute);
            var convergenceWaypoint = waypointIndex;
            var hasLocalDetour = ResolveVehicleDetour(state, activeRoute.Waypoints[waypointIndex], out _) ==
                WorldMapVehicleDetourPlanner.DetourOutcome.Detour;
            if (hasLocalDetour != measuringLocalDetour)
            {
                // Local and full-route distances have different origins. Rejoining the
                // global route must not compare its long remainder to a short detour.
                autoWalkConvergence.Reset();
                measuringLocalDetour = hasLocalDetour;
            }
            if (hasLocalDetour && detourPlanner is { } localPath)
            {
                remainingDistance = localPath.RemainingDistance(state);
                convergenceWaypoint = activeRoute.Waypoints.Count + localPath.Corner;
            }
            if (autoWalkConvergence.Observe(convergenceWaypoint, remainingDistance, now))
            {
                autoWalkConvergence.Reset();
                lastDiagnostic =
                    $"auto walk made no meaningful progress for " +
                    $"{WorldMapAutoWalkConvergenceTracker.NoProgressTimeout.TotalSeconds:0} seconds; " +
                    $"remaining={remainingDistance:0}, waypoint={waypointIndex}";
                return new WorldMapNavigationOutput(
                    $"Auto walk stopped. Could not get closer to {target.Label}. " +
                    "Regular navigation is still on.",
                    StopAutoWalk: true);
            }
        }

        string? speech = null;
        if (now - lastGuidanceAt >= guidanceInterval)
        {
            var signature = CreateGuidanceSignature(state);
            if (resumedAfterCombat ||
                !string.Equals(signature, lastGuidanceSignature, StringComparison.Ordinal))
            {
                speech = CreateGuidanceSpeech(
                    state,
                    includeTarget: resumedAfterCombat,
                    includeProgress: false);
                lastGuidanceSignature = signature;
                lastGuidanceAt = now;
            }
        }

        if (resumedAfterCombat && !string.IsNullOrWhiteSpace(speech))
        {
            speech = $"Navigation resumed. {speech}";
        }

        return speech is null
            ? null
            : new WorldMapNavigationOutput(speech);
    }

    public bool TryResolveAutomaticInput(
        WorldMapStateSnapshot state,
        out FieldNavigationInput input)
    {
        input = FieldNavigationInput.None;
        if (!beaconEnabled || combatPaused || !IsUsable(state) ||
            activeRoute is not { Waypoints.Count: > 0 } route || activeTarget is null)
        {
            return false;
        }

        // Waiting for the party to leave the vehicle. Steering them anywhere would either
        // push them off the entrance or drive them at a door that will not open, so the
        // hold is stated here rather than left to a caller noticing StopAutoWalk.
        if (ShouldHoldNativeEntry(activeTarget, state) || IsAwaitingNativeEntry(activeTarget, state))
        {
            return false;
        }

        waypointIndex = ResolveAutomaticWaypoint(state, route, waypointIndex);
        var waypoint = route.Waypoints[waypointIndex];

        // A vehicle the party parked themselves can physically refuse the approach. Walk
        // round it rather than pressing into it, and stop rather than pretend when there
        // is no clear way in.
        var detourOutcome = ResolveVehicleDetour(state, waypoint, out var detour);
        switch (detourOutcome)
        {
            case WorldMapVehicleDetourPlanner.DetourOutcome.Blocked:
                return false;
            case WorldMapVehicleDetourPlanner.DetourOutcome.Detour:
                waypoint = detour;
                break;
        }
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, waypoint.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, waypoint.Z, map.WrapHeight);
        // Speech may smooth short legs and suppress sub-count diagonals. Native
        // movement must aim from the actual accepted position on every sample.
        input = automaticDirection.ResolveStickDirection(-dx, dz, state.ControlTransform).Input;
        var probeDistance = detourOutcome == WorldMapVehicleDetourPlanner.DetourOutcome.Detour
            ? AutomaticMovementProbeDistance
            : Math.Min(AutomaticMovementProbeDistance, Math.Sqrt(dx * (double)dx + dz * (double)dz));
        bool IsClear(FieldNavigationInput candidate)
        {
            var (x, z) = PredictNativeMovement(candidate, state.CameraFront);
            var next = new WorldMapRouteWaypoint(
                state.X + (int)Math.Round(x * probeDistance), state.Y,
                state.Z + (int)Math.Round(z * probeDistance));
            // The same entrance rule the route was built with. A step the planner would
            // never route through must not be pressed either, or automatic walking zones
            // into a town the player did not select.
            return planner.CanTraverseSegment(state, next, null, activeTarget?.NativeEntranceExemptions) &&
                (state.PlayerModelId is not (0 or 1 or 2) || entityProvider is null ||
                 !WorldMapVehicleObstacles.BlocksSegment(entityProvider().Where(WorldMapVehicleObstacles.IsParkedBuggy).ToArray(), state.PlayerModelId,
                     state.X, state.Z, next.X, next.Z, map.WrapWidth, map.WrapHeight));
        }

        // A clear oblique route can quantize to a cardinal key that hits a
        // cliff. Keep the closest forward key with a clear native short step.
        if (input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft && !IsClear(input))
        {
            input = Enum.GetValues<FieldNavigationInput>()
                .Where(candidate => candidate is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
                .Select(candidate => (Input: candidate, World: PredictNativeMovement(candidate, state.CameraFront)))
                .Select(candidate => (candidate.Input, Progress:
                    (candidate.World.X * dx + candidate.World.Z * dz) /
                    Math.Sqrt(candidate.World.X * candidate.World.X + candidate.World.Z * candidate.World.Z)))
                .Where(candidate => candidate.Progress > 0d)
                .OrderByDescending(candidate => candidate.Progress)
                .Select(candidate => candidate.Input)
                .FirstOrDefault(IsClear);
        }
        return input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
    }

    private static (double X, double Z) PredictNativeMovement(FieldNavigationInput input, int cameraFront)
    {
        var stick = FieldNavigationMovementObserver.ToStickDirection(input);
        // FUN0074EA48 uses 3/4 speed on both diagonal axes and the full native
        // camera angle, before the eight-bit control transform loses precision.
        var scale = stick.X != 0f && stick.Y != 0f ? 0.75d : 1d;
        var x = Math.Sign(stick.X) * scale;
        var z = Math.Sign(stick.Y) * scale;
        var angle = cameraFront * Math.PI * 2d / 4096d;
        return (x * Math.Cos(angle) - z * Math.Sin(angle),
            x * Math.Sin(angle) + z * Math.Cos(angle));
    }

    public void Suspend(string diagnostic)
    {
        if (!beaconEnabled)
        {
            return;
        }

        ResetRoute();
        lastDiagnostic = string.IsNullOrWhiteSpace(diagnostic)
            ? "world navigation suspended"
            : diagnostic;
    }

    public void PauseForCombat(string diagnostic)
    {
        if (!beaconEnabled || combatPaused)
        {
            return;
        }

        combatPaused = true;
        autoWalkConvergence.Reset();
        progressSink?.Deactivate();
        lastDiagnostic = string.IsNullOrWhiteSpace(diagnostic)
            ? "world navigation paused for combat"
            : diagnostic;
    }

    public void Reset()
    {
        ResetRoute();
        categoryIndex = 0;
        selectedIndices.Clear();
        lastDiagnostic = "reset";
    }

    private WorldMapNavigationOutput RelockAndDescribe(WorldMapStateSnapshot state, DateTime now)
    {
        if (!beaconEnabled)
        {
            return DescribeSelection(state);
        }

        var selected = GetSelectedTarget(state);
        if (selected is null)
        {
            ResetRoute();
            return DescribeSelection(state);
        }

        var relocked = StartNavigation(selected, state, now, announceOn: false);
        return relocked ?? DescribeSelection(state);
    }

    private WorldMapNavigationOutput? StartNavigation(
        WorldMapNavigationTarget target,
        WorldMapStateSnapshot state,
        DateTime now,
        bool announceOn,
        bool preserveProgressRoute = false)
    {
        var continuesProgressRoute =
            preserveProgressRoute &&
            beaconEnabled &&
            activeTarget is { } priorTarget &&
            string.Equals(priorTarget.StableId, target.StableId, StringComparison.Ordinal) &&
            progressRouteWaypoints.Count > 0;

        if (!planner.TryResolvePlayerTriangle(state, out var playerTriangle))
        {
            ResetRoute();
            lastDiagnostic = planner.LastDiagnostic;
            return new WorldMapNavigationOutput($"Route unavailable to {target.Label}. Navigation off.");
        }

        if (target.HasArrived(state, playerTriangle))
        {
            if (!IsNativeEntryModelSatisfied(target, state.PlayerModelId))
            {
                // Selecting, or reselecting, the destination while parked on its entrance
                // in a vehicle the game refuses. Keep it: dropping the destination here is
                // what made the player lose Cosmo Canyon by asking for it again.
                beaconEnabled = true;
                combatPaused = false;
                activeTarget = target;
                activeModelId = state.PlayerModelId;
                activeMapType = state.WorldMapType;
                activeWorldProgress = state.WorldProgress;
                awaitingNativeEntry = true;
                nativeEntryHoldOrigin = new(state.X, state.Y, state.Z);
                entryHandoffAnnounced = true;
                lastDiagnostic =
                    $"native entry refuses model {state.PlayerModelId} on triangle {playerTriangle}";
                return new WorldMapNavigationOutput(
                    DescribeNativeEntryHandoff(target),
                    StopAutoWalk: true);
            }

            progressSink?.Complete();
            ResetRoute(deactivateProgress: false);
            lastDiagnostic = $"already at target triangle {playerTriangle}";
            return new WorldMapNavigationOutput($"Arrived at {target.Label}. Navigation off.");
        }

        if (!planner.TryBuildRoute(state, target, out var route))
        {
            ResetRoute();
            lastDiagnostic = planner.LastDiagnostic;
            return new WorldMapNavigationOutput($"Route unavailable to {target.Label}. Navigation off.");
        }

        beaconEnabled = true;
        combatPaused = false;
        autoWalkConvergence.Reset();
        activeTarget = target;
        activeRoute = route;
        routeStart = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        waypointIndex = 0;
        entryHandoffAnnounced = false;
        awaitingNativeEntry = false;
        offRouteSince = DateTime.MinValue;
        if (!continuesProgressRoute)
        {
            progressRouteStart = routeStart;
            progressRouteWaypoints = route.Waypoints;
            progressPercent = 0;
            progressSink?.Activate(0);
        }
        activeModelId = state.PlayerModelId;
        activeMapType = state.WorldMapType;
        activeWorldProgress = state.WorldProgress;
        lastGuidanceAt = now;
        _ = UpdateProgressAndWaypoint(state);
        lastDiagnostic = planner.LastDiagnostic;
        var guidance = CreateGuidanceSpeech(state, includeTarget: true, includeProgress: false);
        lastGuidanceSignature = CreateGuidanceSignature(state);
        return new WorldMapNavigationOutput(
            announceOn ? $"Navigation on. {guidance}" : guidance);
    }

    private WorldMapNavigationOutput DescribeSelection(WorldMapStateSnapshot state)
    {
        var targets = GetTargets(state);
        if (targets.Count == 0)
        {
            return new WorldMapNavigationOutput($"{DisplayName(CurrentCategory)}: none available.");
        }

        var target = GetSelectedTarget(state)!;
        if (IsAwaitingNativeEntry(target, state))
        {
            return new WorldMapNavigationOutput(
                $"{DisplayName(CurrentCategory)}, {target.Label} entrance. The way in is on foot, so leave the vehicle here.");
        }

        if (planner.TryBuildRoute(state, target, out var preview) && preview.Waypoints.Count > 0)
        {
            var routeStart = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
            var measurement = MeasurePolylineProgress(
                routeStart,
                preview.Waypoints,
                state,
                map.WrapWidth,
                map.WrapHeight);
            var direction = WorldMapConnectedRunFormatter.Resolve(
                routeStart,
                preview.Waypoints,
                measurement.NextWaypointIndex,
                state,
                map.WrapWidth,
                map.WrapHeight,
                distanceUnitsPerCount).Speech;
            return new WorldMapNavigationOutput(
                $"{DisplayName(CurrentCategory)}, {target.Label}. {direction}.");
        }

        return new WorldMapNavigationOutput(
            $"{DisplayName(CurrentCategory)}, {target.Label}. Route unavailable.");
    }

    private string CreateGuidanceSpeech(
        WorldMapStateSnapshot state,
        bool includeTarget,
        bool includeProgress)
    {
        var target = activeTarget;
        var route = activeRoute;
        if (target is not null && (ShouldHoldNativeEntry(target, state) || IsAwaitingNativeEntry(target, state)))
        {
            // Repeating the destination while parked on its entrance must not report an
            // arrival the game has refused.
            return DescribeNativeEntryHandoff(target);
        }

        if (target is null || route is null || route.Waypoints.Count == 0)
        {
            return includeTarget && target is not null ? target.Label : "nearby";
        }

        var direction = ResolveGuidanceRun(state, route).Speech;
        var prefix = includeTarget ? $"{target.Label}. " : string.Empty;
        var progress = includeProgress ? $" Route progress {progressPercent} percent." : string.Empty;
        return $"{prefix}{direction}.{progress}".Trim();
    }

    private WorldMapSpokenRun ResolveGuidanceRun(WorldMapStateSnapshot state, WorldMapRoutePlan route)
    {
        if (ResolveVehicleDetour(state, route.Waypoints[waypointIndex], out _) ==
            WorldMapVehicleDetourPlanner.DetourOutcome.Detour && detourPlanner is { } localPath)
        {
            return WorldMapConnectedRunFormatter.Resolve(new(state.X, state.Y, state.Z), localPath.Path,
                localPath.Corner, state, map.WrapWidth, map.WrapHeight, distanceUnitsPerCount);
        }
        return WorldMapConnectedRunFormatter.Resolve(
            routeStart,
            route.Waypoints,
            waypointIndex,
            state,
            map.WrapWidth,
            map.WrapHeight,
            distanceUnitsPerCount);
    }

    private string CreateGuidanceSignature(WorldMapStateSnapshot state)
    {
        if (activeRoute is not { Waypoints.Count: > 0 } route)
        {
            return string.Empty;
        }

        var run = ResolveGuidanceRun(state, route);
        return $"{run.Direction}:{run.EndWaypointIndex}";
    }

    private WorldMapPolylineProgress UpdateProgressAndWaypoint(WorldMapStateSnapshot state, bool automaticWalkActive = false)
    {
        if (activeRoute is null)
        {
            return default;
        }

        var guidanceMeasurement = MeasurePolylineProgress(
            routeStart,
            activeRoute.Waypoints,
            state,
            map.WrapWidth,
            map.WrapHeight);
        var progressMeasurement = progressRouteWaypoints.Count == 0
            ? guidanceMeasurement
            : MeasurePolylineProgress(
                progressRouteStart,
                progressRouteWaypoints,
                state,
                map.WrapWidth,
                map.WrapHeight);
        progressPercent = Math.Clamp((int)Math.Floor(progressMeasurement.Fraction * 100d), 0, 99);
        progressSink?.SetValue(progressPercent);
        waypointIndex = Math.Clamp(
            guidanceMeasurement.NextWaypointIndex,
            0,
            Math.Max(0, activeRoute.Waypoints.Count - 1));
        if (automaticWalkActive)
        {
            waypointIndex = ResolveAutomaticWaypoint(state, activeRoute, waypointIndex);
        }
        lastDiagnostic =
            $"route progress={progressPercent}, waypoint={waypointIndex}, offset={guidanceMeasurement.DistanceFromRoute:0}";
        return guidanceMeasurement;
    }

    /// <summary>
    /// Destinations the game itself refuses to let a vehicle enter.
    ///
    /// <para>Cosmo Canyon's world handler tests the player model before it will open the
    /// field: wm0.ev handler ADA4 at IP 2D1C..2D36 pushes special 8, compares it against
    /// 0, 1 and 2, and returns without entering unless one of those matches. Standing on
    /// the entrance triangle in the Buggy therefore satisfies the mod's arrival test while
    /// the game will not actually admit the party, and announcing "Arrived" there leaves a
    /// blind player waiting at a door that never opens.</para>
    ///
    /// <para>Only the rule proven from the installed script is recorded. Nothing here
    /// dismounts anybody or touches game state; the party is told what the game wants and
    /// the route is kept so that changing model on foot resumes it.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<int>> NativeEntryModels =
        new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal)
        {
            // wm0.ev handler ADA4, IP 2D1C..2D36: push special 8, compare against 0, 1 and
            // 2, return unless one matches, then opcode 0x318 EnterField with native
            // destination 18 decimal and entry 0.
            ["Cosmo Canyon"] = new HashSet<int> { 0, 1, 2 }
        };

    /// <summary>
    /// Whether the game will admit this model at this destination. The same place is
    /// offered both as a Location and, while it is the current stage, as a Story target
    /// carrying the same label, so the rule has to answer for both.
    /// </summary>
    private static bool IsNativeEntryModelSatisfied(WorldMapNavigationTarget target, int playerModelId) =>
        target.Kind is not (WorldMapTargetKind.Location or WorldMapTargetKind.Story) ||
        !NativeEntryModels.TryGetValue(target.Label, out var allowed) ||
        allowed.Contains(playerModelId);

    /// <summary>
    /// The party is standing where the destination is reached, in a model the game will
    /// not let in. Used by selection, observation and the model-change replan alike, so
    /// none of them can announce an arrival the game will not honour.
    /// </summary>
    private bool IsAwaitingNativeEntry(WorldMapNavigationTarget target, WorldMapStateSnapshot state) =>
        !IsNativeEntryModelSatisfied(target, state.PlayerModelId) &&
        planner.TryResolvePlayerTriangle(state, out var triangle) &&
        target.HasArrived(state, triangle);

    private bool ShouldHoldNativeEntry(WorldMapNavigationTarget target, WorldMapStateSnapshot state)
    {
        var dx = (double)WorldMapTargetCatalog.WrappedDelta(nativeEntryHoldOrigin.X, state.X, map.WrapWidth);
        var dz = (double)WorldMapTargetCatalog.WrappedDelta(nativeEntryHoldOrigin.Z, state.Z, map.WrapHeight);
        return awaitingNativeEntry && state.WorldMapType == activeMapType &&
            !IsNativeEntryModelSatisfied(target, state.PlayerModelId) &&
            dx * dx + dz * dz <= NativeEntryHoldRadius * NativeEntryHoldRadius;
    }

    private static string DescribeNativeEntryHandoff(WorldMapNavigationTarget target) =>
        $"{target.Label} entrance. The way in is on foot, so leave the vehicle here. " +
        "Navigation stays on.";

    private WorldMapVehicleDetourPlanner.DetourOutcome ResolveVehicleDetour(
        WorldMapStateSnapshot state,
        WorldMapRouteWaypoint goal,
        out WorldMapRouteWaypoint aim)
    {
        aim = goal;
        if (entityProvider is null || activeTarget is not { } target)
        {
            return WorldMapVehicleDetourPlanner.DetourOutcome.Clear;
        }

        detourPlanner ??= new WorldMapVehicleDetourPlanner(map, planner);
        return detourPlanner.Resolve(state, entityProvider(), target, goal, out aim);
    }

    private int ResolveAutomaticWaypoint(WorldMapStateSnapshot state, WorldMapRoutePlan route, int suggestedIndex)
    {
        var index = Math.Clamp(suggestedIndex, 0, route.Waypoints.Count - 1);
        // Spoken guidance smooths nearby legs, but automatic movement cannot
        // cross a native cliff just because both ends share one component - and it cannot
        // aim across somebody else's doorway either. The same exemption the route was
        // built with belongs here: a null set turns the entrance check off, which would
        // let line-of-sight selection pick a waypoint the final step probe then refuses.
        while (index > 0 &&
               !planner.CanTraverseSegment(
                   state, route.Waypoints[index], null, activeTarget?.NativeEntranceExemptions))
        {
            index--;
        }
        return index;
    }

    internal static WorldMapPolylineProgress MeasurePolylineProgress(
        WorldMapRouteWaypoint start,
        IReadOnlyList<WorldMapRouteWaypoint> waypoints,
        WorldMapStateSnapshot state,
        int wrapWidth,
        int wrapHeight)
    {
        if (waypoints.Count == 0)
        {
            return new WorldMapPolylineProgress(0d, 0, 0d);
        }

        var points = new List<(double X, double Y, double Z)>(waypoints.Count + 1)
        {
            (start.X, start.Y, start.Z)
        };
        foreach (var waypoint in waypoints)
        {
            var prior = points[^1];
            points.Add((
                prior.X + WorldMapTargetCatalog.WrappedDelta((int)Math.Round(prior.X), waypoint.X, wrapWidth),
                waypoint.Y,
                prior.Z + WorldMapTargetCatalog.WrappedDelta((int)Math.Round(prior.Z), waypoint.Z, wrapHeight)));
        }

        var cumulative = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
        {
            cumulative[index] = cumulative[index - 1] + Distance(points[index - 1], points[index]);
        }

        var bestDistance = double.PositiveInfinity;
        var bestAlong = 0d;
        for (var segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            var a = points[segmentIndex];
            var b = points[segmentIndex + 1];
            var playerX = a.X + WorldMapTargetCatalog.WrappedDelta((int)Math.Round(a.X), state.X, wrapWidth);
            var playerZ = a.Z + WorldMapTargetCatalog.WrappedDelta((int)Math.Round(a.Z), state.Z, wrapHeight);
            var player = (X: playerX, Y: (double)state.Y, Z: playerZ);
            var vx = b.X - a.X;
            var vy = b.Y - a.Y;
            var vz = b.Z - a.Z;
            var lengthSquared = vx * vx + vy * vy + vz * vz;
            var t = lengthSquared <= 0d
                ? 0d
                : Math.Clamp(
                    ((player.X - a.X) * vx + (player.Y - a.Y) * vy + (player.Z - a.Z) * vz) /
                    lengthSquared,
                    0d,
                    1d);
            var projected = (X: a.X + vx * t, Y: a.Y + vy * t, Z: a.Z + vz * t);
            var offRoute = Distance(player, projected);
            var along = cumulative[segmentIndex] + Math.Sqrt(lengthSquared) * t;
            if (offRoute < bestDistance - 0.001d ||
                (Math.Abs(offRoute - bestDistance) <= 0.001d && along > bestAlong))
            {
                bestDistance = offRoute;
                bestAlong = along;
            }
        }

        var total = cumulative[^1];
        var nextWaypoint = 0;
        while (nextWaypoint < waypoints.Count - 1 &&
               cumulative[nextWaypoint + 1] <= bestAlong + WaypointArrivalDistance)
        {
            nextWaypoint++;
        }

        return new WorldMapPolylineProgress(
            total <= 0d ? 0d : Math.Clamp(bestAlong / total, 0d, 1d),
            nextWaypoint,
            bestDistance);
    }

    private void MoveCategory(int delta)
    {
        categoryIndex = PositiveModulo(categoryIndex + delta, WorldMapTargetCatalog.CategoryOrder.Count);
    }

    private void MoveTarget(WorldMapStateSnapshot state, int delta)
    {
        var targets = GetTargets(state);
        if (targets.Count == 0)
        {
            selectedIndices[CurrentCategory] = 0;
            return;
        }

        selectedIndices.TryGetValue(CurrentCategory, out var index);
        selectedIndices[CurrentCategory] = PositiveModulo(index + delta, targets.Count);
    }

    private WorldMapNavigationTarget? GetSelectedTarget(WorldMapStateSnapshot state)
    {
        var targets = GetTargets(state);
        if (targets.Count == 0)
        {
            return null;
        }

        selectedIndices.TryGetValue(CurrentCategory, out var index);
        index = PositiveModulo(index, targets.Count);
        selectedIndices[CurrentCategory] = index;
        return targets[index];
    }

    private IReadOnlyList<WorldMapNavigationTarget> GetTargets(WorldMapStateSnapshot state)
    {
        var candidates = targetProvider(state, CurrentCategory) ?? Array.Empty<WorldMapNavigationTarget>();
        return candidates
            .Where(target => planner.CanReach(state, target))
            .ToArray();
    }

    private bool IsUsable(WorldMapStateSnapshot state) =>
        state.CurrentModule == WorldMapStateReader.WorldModule &&
        state.WorldMapType == map.WorldMapType;

    private void ResetRoute(bool deactivateProgress = true)
    {
        if (deactivateProgress)
        {
            progressSink?.Deactivate();
        }

        beaconEnabled = false;
        combatPaused = false;
        activeTarget = null;
        activeRoute = null;
        progressRouteWaypoints = Array.Empty<WorldMapRouteWaypoint>();
        waypointIndex = 0;
        entryHandoffAnnounced = false;
        awaitingNativeEntry = false;
        progressPercent = 0;
        activeModelId = -1;
        activeMapType = -1;
        activeWorldProgress = int.MinValue;
        lastGuidanceAt = DateTime.MinValue;
        offRouteSince = DateTime.MinValue;
        lastGuidanceSignature = string.Empty;
        autoWalkConvergence.Reset();
        detourPlanner?.Invalidate();
        measuringLocalDetour = false;
    }

    private static string DisplayName(WorldMapNavigationCategory category) => category switch
    {
        WorldMapNavigationCategory.ChocoboTracks => "Chocobo Tracks",
        _ => category.ToString()
    };

    private static WorldMapNavigationOutput DescribeRouteTransition(
        WorldMapNavigationOutput output,
        bool resumedAfterCombat) => output with
    {
        Speech = resumedAfterCombat
            ? $"Navigation resumed. {output.Speech}"
            : output.Speech
    };

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static TimeSpan Normalize(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static double Distance(
        (double X, double Y, double Z) first,
        (double X, double Y, double Z) second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public readonly record struct WorldMapPolylineProgress(
    double Fraction,
    int NextWaypointIndex,
    double DistanceFromRoute);

internal sealed class WorldMapAutoWalkConvergenceTracker
{
    internal static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumObservedSampleGap = TimeSpan.FromSeconds(2);
    private const double MeaningfulProgressDistance = 32d;

    private DateTime lastObservedAt = DateTime.MinValue;
    private DateTime lastProgressAt = DateTime.MinValue;
    private double bestRemainingDistance = double.PositiveInfinity;
    private int bestWaypointIndex = -1;

    internal bool Observe(int waypointIndex, double remainingDistance, DateTime observedAt)
    {
        if (!double.IsFinite(remainingDistance) || remainingDistance < 0d)
        {
            Reset();
            return false;
        }

        if (lastObservedAt == DateTime.MinValue ||
            observedAt <= lastObservedAt ||
            observedAt - lastObservedAt > MaximumObservedSampleGap)
        {
            Begin(waypointIndex, remainingDistance, observedAt);
            return false;
        }

        lastObservedAt = observedAt;
        if (waypointIndex > bestWaypointIndex ||
            remainingDistance <= bestRemainingDistance - MeaningfulProgressDistance)
        {
            bestWaypointIndex = Math.Max(bestWaypointIndex, waypointIndex);
            bestRemainingDistance = remainingDistance;
            lastProgressAt = observedAt;
            return false;
        }

        return observedAt - lastProgressAt >= NoProgressTimeout;
    }

    internal void Reset()
    {
        lastObservedAt = DateTime.MinValue;
        lastProgressAt = DateTime.MinValue;
        bestRemainingDistance = double.PositiveInfinity;
        bestWaypointIndex = -1;
    }

    private void Begin(int waypointIndex, double remainingDistance, DateTime observedAt)
    {
        lastObservedAt = observedAt;
        lastProgressAt = observedAt;
        bestRemainingDistance = remainingDistance;
        bestWaypointIndex = waypointIndex;
    }
}
