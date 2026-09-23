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

    /// <summary>FUN_0074EA48 moves model 5 by 0x3C units a frame.</summary>
    private const double BroncoFrameStep = 60d;

    /// <summary>FUN_0074EA48 moves the party on foot by 0x1E units a frame.</summary>
    private const double WalkingFrameStep = 30d;
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

    // Aboard the Tiny Bronco, taking it to a shore landing for the active destination.
    // Null for every other route.
    private WorldMapBroncoLandingPlan? landingPlan;
    private LandingPhase landingPhase;
    private WorldMapRouteWaypoint landingSettlePosition;
    private int landingSettleSamples;
    private double landingClosestApproach = double.PositiveInfinity;
    private int landingStalledSamples;
    private bool landingOnRunIn;

    /// <summary>
    /// How far ahead along the run in the boat aims. Eight directions cannot hold an
    /// arbitrary heading, so the boat weaves; aiming a little way down the line keeps the
    /// weave on it rather than letting it drift into the bank.
    /// </summary>
    private const double LandingRunInLookahead = 480d;

    /// <summary>How far along the run in the boat may notice it is already lined up.</summary>
    private const double LandingLookoutDistance = 1200d;

    /// <summary>
    /// Samples without getting closer to the landing before the run in is over. The boat
    /// covers about 60 units a frame (FUN_0074EA48, step 0x3C), so this is well under a
    /// second of pressing against the shore.
    /// </summary>
    private const int LandingStallSamples = 12;

    /// <summary>Driven this far from the landing spot, the approach is taken up again.</summary>
    private const double LandingHoldRadius = 1600d;

    private enum LandingPhase
    {
        Approaching,
        Settling,
        LinedUp,
        NotLinedUp
    }
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

        // A vehicle is a live entity, not a place, and this has to be asked before the
        // model change below. Boarding a boat is a model change, and the party in the boat
        // can trivially "reach" it, so replanning first is how navigation came to announce
        // a route to the vehicle the player was sitting in.
        if (activeTarget is { Kind: WorldMapTargetKind.Transportation } vehicle &&
            !GetTargets(state).Any(candidate =>
                string.Equals(candidate.StableId, vehicle.StableId, StringComparison.Ordinal)))
        {
            ResetRoute();
            lastDiagnostic = $"{vehicle.Label} is no longer a parked entity in the native list";
            return new WorldMapNavigationOutput(
                $"{vehicle.Label} is no longer there. Navigation off.",
                StopAutoWalk: true);
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
            // A landing route ends on water beside the destination by design; its end is
            // never one of the destination's own triangles, and replanning because of that
            // would start the approach over on every sample.
            // Replan when the route no longer ends somewhere the target can be reached
            // from, not merely when the target's own centre triangle differs from the
            // triangle the route ends on. Those are different things: the catalog gives an
            // entity every walkable neighbour as an arrival, and the planner routes to
            // whichever of them it reaches first, so the centre and the route end normally
            // disagree for a vehicle that has not moved at all. Comparing them announced a
            // fresh route on every observation, which is how the parked Buggy came to be
            // repeated several times a second.
            if (landingPlan is null &&
                (!refreshed.ArrivalTriangleIds.Contains(activeRoute.TargetTriangleId) ||
                 HasMovedItsContactPoint(refreshed, activeRoute)))
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

        if (landingPlan is { } landing)
        {
            var landingOutput = ObserveLanding(landing, target, state);
            if (landingPhase != LandingPhase.Approaching)
            {
                // Stopped at the shore: nothing is being driven, so neither the
                // convergence guard nor route guidance has anything to say.
                autoWalkConvergence.Reset();
                return landingOutput;
            }
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

        // At the landing spot the boat is held still: the landing is judged at rest, and
        // getting off is the player's own Cancel. Nothing here presses it.
        if (landingPlan is not null && landingPhase != LandingPhase.Approaching)
        {
            return false;
        }

        waypointIndex = ResolveAutomaticWaypoint(state, route, waypointIndex);
        if (landingPlan is { } runIn && (landingOnRunIn || waypointIndex >= route.Waypoints.Count - 1))
        {
            // The run in is the straight line from where it starts, through the spot, to
            // the ground the landing puts the party on. FUN_0074EA48 turns the boat to the
            // way it is driven and the get-off probes along that turn, so holding the boat
            // to this line is what lines the probe up. Once on it the boat stays on it: the
            // segment test that picks route waypoints would otherwise send a boat weaving a
            // few units off the line back to where the run began. The step probe is not
            // asked either - near the shore the way to the landing is land, which is the
            // point, and FUN_00751EFC slides or holds the boat there.
            landingOnRunIn = true;
            var (aimX, aimZ) = ResolveRunInAim(runIn, state);
            input = automaticDirection.ResolveStickDirection(-aimX, aimZ, state.ControlTransform).Input;
            return input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
        }

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
        if (state.PlayerModelId == WorldMapBroncoLanding.BroncoModelId ||
            (WorldMapRoutePlanner.IsWalkingModel(state.PlayerModelId) &&
             detourOutcome == WorldMapVehicleDetourPlanner.DetourOutcome.Clear))
        {
            // The boat and the party follow their leg rather than cutting at the corner
            // ahead: each leg is the part of the river, or of the pass, their footprint was
            // proved to fit. Steering straight at a far corner on eight directions drifts off
            // it - at 11:23:29's camera by 13 degrees, 300 units into the foot of a mountain
            // on the last 4,200-unit leg to Wutai.
            (dx, dz) = ResolveLegAim(
                state,
                waypointIndex > 0 ? route.Waypoints[waypointIndex - 1] : routeStart,
                waypoint);
        }

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
                // The boat moves only where its whole five-point footprint is on water
                // (FUN_00751EFC, 350 units for model 5); pressing into a bank it cannot
                // reach wedges it against the shore. Judged over one native frame of
                // movement (0x3C), which is what the game accepts or refuses: in a tight
                // bend the step that makes progress fits for a frame and not for two.
                (state.PlayerModelId != WorldMapBroncoLanding.BroncoModelId ||
                 WorldMapBroncoLanding.HasBoatFootprint(
                     map,
                     state.X + (int)Math.Round(x * Math.Min(probeDistance, BroncoFrameStep)),
                     state.Z + (int)Math.Round(z * Math.Min(probeDistance, BroncoFrameStep)))) &&
                (state.PlayerModelId is not (0 or 1 or 2) || entityProvider is null ||
                 !WorldMapVehicleObstacles.BlocksSegment(
                     (activeTarget is { } selected ? ObstaclesOtherThan(selected) : entityProvider())
                         .Where(WorldMapVehicleObstacles.IsParkedBuggy).ToArray(),
                     state.PlayerModelId,
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

        WorldMapBroncoLandingPlan? landing = null;
        if (!planner.TryBuildRoute(state, target, out var route))
        {
            var diagnostic = planner.LastDiagnostic;
            if (!IsDestination(target) || state.PlayerModelId != WorldMapBroncoLanding.BroncoModelId)
            {
                ResetRoute();
                lastDiagnostic = diagnostic;
                return new WorldMapNavigationOutput($"Route unavailable to {target.Label}. Navigation off.");
            }

            // Aboard the Tiny Bronco no town can be sailed into: the boat is held to water
            // and every entrance is on land. The way there is a shore where getting off
            // puts the party on ground that leads to it.
            if (!TryBuildLandingRoute(state, target, out landing, out route))
            {
                ResetRoute();
                lastDiagnostic = $"{diagnostic}; no suitable Tiny Bronco landing route found for {target.Label}";
                return new WorldMapNavigationOutput(
                    $"Route unavailable to {target.Label} by Tiny Bronco. " +
                    "No suitable landing route was found from here. Navigation off.",
                    StopAutoWalk: true);
            }
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
        landingPlan = landing;
        landingPhase = LandingPhase.Approaching;
        landingSettleSamples = 0;
        landingClosestApproach = double.PositiveInfinity;
        landingStalledSamples = 0;
        landingOnRunIn = false;
        lastGuidanceAt = now;
        _ = UpdateProgressAndWaypoint(state);
        lastDiagnostic = landing is null
            ? planner.LastDiagnostic
            : $"Tiny Bronco landing for {target.Label}: spot {landing.Option.X},{landing.Option.Z} " +
              $"rotation {landing.Option.Rotation}, ground {landing.Option.LandingX},{landing.Option.LandingZ} " +
              $"triangle {landing.Option.LandingTriangleId}, boat {landing.BoatDistance:0}, " +
              $"walk {landing.FootDistance:0}";
        var guidance = CreateGuidanceSpeech(state, includeTarget: true, includeProgress: false);
        lastGuidanceSignature = CreateGuidanceSignature(state);
        return new WorldMapNavigationOutput(
            announceOn ? $"Navigation on. {guidance}" : guidance);
    }

    /// <summary>
    /// The boat's route to a landing: to the start of the straight run in, then along it
    /// to the spot, so that it arrives facing the shore it is meant to land on.
    /// </summary>
    private bool TryBuildLandingRoute(
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target,
        out WorldMapBroncoLandingPlan? landing,
        out WorldMapRoutePlan route)
    {
        landing = null;
        route = default!;
        if (!WorldMapBroncoLanding.TryPlan(planner, map, state, target, out var plan) ||
            !WorldMapBroncoLanding.TryFindSurface(map, plan.ApproachStart.X, plan.ApproachStart.Z, out var startTriangle))
        {
            return false;
        }

        var option = plan.Option;
        var runStart = new WorldMapNavigationTarget(
            target.Category,
            WorldMapTargetKind.TerrainArea,
            $"{target.Label} landing",
            plan.ApproachStart.X,
            plan.ApproachStart.Y,
            plan.ApproachStart.Z,
            startTriangle.Id,
            startTriangle.RegionId,
            $"{target.StableId}:tiny-bronco-landing",
            new HashSet<int> { startTriangle.Id });
        if (!planner.TryBuildRoute(state, runStart, out var toRunStart))
        {
            return false;
        }

        var spot = new WorldMapRouteWaypoint(option.X, option.Y, option.Z);
        var last = toRunStart.Waypoints.Count > 0
            ? toRunStart.Waypoints[^1]
            : new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        var runX = (double)WorldMapTargetCatalog.WrappedDelta(last.X, spot.X, map.WrapWidth);
        var runZ = (double)WorldMapTargetCatalog.WrappedDelta(last.Z, spot.Z, map.WrapHeight);
        var trianglePath = toRunStart.TrianglePath.Contains(option.WaterTriangleId)
            ? toRunStart.TrianglePath
            : toRunStart.TrianglePath.Append(option.WaterTriangleId).ToArray();
        route = toRunStart with
        {
            TargetId = target.StableId,
            TargetTriangleId = option.WaterTriangleId,
            TrianglePath = trianglePath,
            Waypoints = toRunStart.Waypoints.Append(spot).ToArray(),
            TotalDistance = toRunStart.TotalDistance + Math.Sqrt(runX * runX + runZ * runZ)
        };
        landing = plan;
        return true;
    }

    /// <summary>
    /// The native get-off, judged each sample once the boat is at the landing spot. See
    /// <see cref="WorldMapBroncoLanding"/>: nothing is promised until the boat is at rest
    /// and every rotation it can still settle through lands on ground that leads to the
    /// destination.
    /// </summary>
    private WorldMapNavigationOutput? ObserveLanding(
        WorldMapBroncoLandingPlan landing,
        WorldMapNavigationTarget target,
        WorldMapStateSnapshot state)
    {
        var option = landing.Option;
        var dx = (double)WorldMapTargetCatalog.WrappedDelta(state.X, option.X, map.WrapWidth);
        var dz = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, option.Z, map.WrapHeight);
        var fromSpot = Math.Sqrt(dx * dx + dz * dz);
        if (!state.HasModelRotation)
        {
            // A torn or unreadable rotation is no evidence either way.
            return null;
        }

        var linedUp = WorldMapBroncoLanding.IsLinedUp(
            map, planner, state, landing.DestinationFootComponents, out var ground);
        switch (landingPhase)
        {
            case LandingPhase.Approaching:
            {
                var groundX = (double)WorldMapTargetCatalog.WrappedDelta(state.X, option.LandingX, map.WrapWidth);
                var groundZ = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, option.LandingZ, map.WrapHeight);
                var fromGround = Math.Sqrt(groundX * groundX + groundZ * groundZ);
                var onRunIn = landingOnRunIn;
                var stopReason = string.Empty;
                if (linedUp && fromSpot <= LandingLookoutDistance)
                {
                    stopReason = "lined up on the way in";
                }
                else if (onRunIn)
                {
                    // The run in has its own measure of progress - towards the landing, which
                    // it does not reach - so the route's convergence guard stays out of it.
                    autoWalkConvergence.Reset();
                    if (fromGround < landingClosestApproach - 8d)
                    {
                        landingClosestApproach = fromGround;
                        landingStalledSamples = 0;
                    }
                    else if (++landingStalledSamples >= LandingStallSamples)
                    {
                        stopReason = "can go no closer";
                    }
                }
                else
                {
                    landingClosestApproach = double.PositiveInfinity;
                    landingStalledSamples = 0;
                }

                if (stopReason.Length > 0)
                {
                    landingPhase = LandingPhase.Settling;
                    landingSettlePosition = new(state.X, state.Y, state.Z);
                    landingSettleSamples = 0;
                    lastDiagnostic =
                        $"stopping {fromSpot:0} from the landing spot for {target.Label}: {stopReason}";
                }

                return null;
            }
            case LandingPhase.Settling:
            {
                var settleX = (double)WorldMapTargetCatalog.WrappedDelta(landingSettlePosition.X, state.X, map.WrapWidth);
                var settleZ = (double)WorldMapTargetCatalog.WrappedDelta(landingSettlePosition.Z, state.Z, map.WrapHeight);
                var atRest = settleX * settleX + settleZ * settleZ <= 16d &&
                             Math.Abs(RotationArc(state.ModelRotation + state.SlideRotation, state.Facing)) <= 24;
                landingSettlePosition = new(state.X, state.Y, state.Z);
                landingSettleSamples = atRest ? landingSettleSamples + 1 : 0;
                if (landingSettleSamples < 2)
                {
                    return null;
                }

                landingPhase = linedUp ? LandingPhase.LinedUp : LandingPhase.NotLinedUp;
                lastDiagnostic = linedUp
                    ? $"lined up to land for {target.Label} on triangle {ground.Id}"
                    : $"at rest {fromSpot:0} from the landing spot for {target.Label}, not lined up; " +
                      $"rotation {state.ModelRotation}{state.SlideRotation:+0;-0;+0}, facing {state.Facing}";
                return new WorldMapNavigationOutput(
                    linedUp ? DescribeLandingHandoff(target) : DescribeNotLinedUp(target),
                    StopAutoWalk: true);
            }
            case LandingPhase.LinedUp:
                if (linedUp)
                {
                    return null;
                }

                landingPhase = LandingPhase.NotLinedUp;
                lastDiagnostic = $"no longer lined up to land for {target.Label}";
                return new WorldMapNavigationOutput($"No longer lined up to land for {target.Label}.");
            default:
                if (fromSpot > LandingHoldRadius)
                {
                    // Driven away: take the approach up again from wherever this is.
                    landingPhase = LandingPhase.Approaching;
                    landingClosestApproach = double.PositiveInfinity;
                    landingStalledSamples = 0;
                    landingOnRunIn = false;
                    lastDiagnostic = $"left the landing spot for {target.Label}";
                    return null;
                }

                if (!linedUp)
                {
                    return null;
                }

                landingPhase = LandingPhase.LinedUp;
                lastDiagnostic = $"lined up to land for {target.Label} on triangle {ground.Id}";
                return new WorldMapNavigationOutput(
                    $"Lined up to land for {target.Label}. Press Cancel once to get off here.");
        }
    }

    /// <summary>
    /// The offset from the boat to the point it should steer for: a little way down the run
    /// in from where the boat is level with it, and never past the landing itself.
    /// </summary>
    private (int X, int Z) ResolveRunInAim(WorldMapBroncoLandingPlan landing, WorldMapStateSnapshot state)
    {
        var start = landing.ApproachStart;
        var lineX = (double)WorldMapTargetCatalog.WrappedDelta(start.X, landing.Option.LandingX, map.WrapWidth);
        var lineZ = (double)WorldMapTargetCatalog.WrappedDelta(start.Z, landing.Option.LandingZ, map.WrapHeight);
        var length = Math.Sqrt(lineX * lineX + lineZ * lineZ);
        var boatX = (double)WorldMapTargetCatalog.WrappedDelta(start.X, state.X, map.WrapWidth);
        var boatZ = (double)WorldMapTargetCatalog.WrappedDelta(start.Z, state.Z, map.WrapHeight);
        if (length < 1d)
        {
            return ((int)Math.Round(lineX - boatX), (int)Math.Round(lineZ - boatZ));
        }

        var along = Math.Clamp((boatX * lineX + boatZ * lineZ) / length + LandingRunInLookahead, 0d, length);
        return ((int)Math.Round(lineX * along / length - boatX), (int)Math.Round(lineZ * along / length - boatZ));
    }

    private static string DescribeLandingHandoff(WorldMapNavigationTarget target) =>
        $"Landing for {target.Label}. Press Cancel once to get off the Tiny Bronco here, " +
        "then continue on foot. Navigation stays on.";

    private static string DescribeNotLinedUp(WorldMapNavigationTarget target) =>
        $"At the landing for {target.Label}, but the Tiny Bronco is not lined up to land there. " +
        "A landing is not confirmed from this angle. " +
        "Navigation stays on, and you will hear when it is lined up.";

    private static int RotationArc(int from, int to)
    {
        var arc = (to - from) % 4096;
        return arc > 2048 ? arc - 4096 : arc < -2048 ? arc + 4096 : arc;
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

        var byLanding = false;
        if ((planner.TryBuildRoute(state, target, out var preview) ||
             (byLanding = IsReachedByLanding(target, state) &&
                          TryBuildLandingRoute(state, target, out _, out preview))) &&
            preview.Waypoints.Count > 0)
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
                byLanding
                    ? $"{DisplayName(CurrentCategory)}, {target.Label}. {LandingRouteSummary}. {direction}."
                    : $"{DisplayName(CurrentCategory)}, {target.Label}. {direction}.");
        }

        return new WorldMapNavigationOutput(
            $"{DisplayName(CurrentCategory)}, {target.Label}. Route unavailable.");
    }

    private const string LandingRouteSummary = "By Tiny Bronco to a shore landing, then on foot";

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

        if (target is not null && landingPlan is not null)
        {
            switch (landingPhase)
            {
                case LandingPhase.LinedUp:
                    return DescribeLandingHandoff(target);
                case LandingPhase.NotLinedUp:
                    return DescribeNotLinedUp(target);
                case LandingPhase.Settling:
                    return $"Stopping at the landing for {target.Label}.";
            }
        }

        if (target is null || route is null || route.Waypoints.Count == 0)
        {
            return includeTarget && target is not null ? target.Label : "nearby";
        }

        var direction = ResolveGuidanceRun(state, route).Speech;
        var prefix = includeTarget
            ? landingPlan is null ? $"{target.Label}. " : $"{target.Label}. {LandingRouteSummary}. "
            : string.Empty;
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
    /// blind player waiting at a door that never opens. Wutai's handler, 96C4, opens with
    /// the same test.</para>
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
            ["Cosmo Canyon"] = new HashSet<int> { 0, 1, 2 },
            // wm0.ev handler 96C4, Wutai's own at mesh (4,10) script 7, IP 2DB3..2DC5: the
            // same test of special 8 against 0, 1 and 2, jumping to the return at 2E06
            // unless one matches; every other branch is opcode 0x318 EnterField with
            // destination 23 and entry 0 or 1.
            ["Wutai"] = new HashSet<int> { 0, 1, 2 }
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
        return detourPlanner.Resolve(state, ObstaclesOtherThan(target), target, goal, out aim);
    }

    /// <summary>
    /// Whether a vehicle has drifted far enough that the route's last step no longer lands
    /// on it.
    ///
    /// <para>Triangle membership is too coarse for this. A vehicle's arrival triangles are
    /// hundreds of units across and its boardable ground is one 256-unit cell of them, so
    /// a boat can move well out of reach of the endpoint the route committed to while
    /// every arrival triangle stays the same. Compare the endpoint itself.</para>
    /// </summary>
    private static bool HasMovedItsContactPoint(
        WorldMapNavigationTarget refreshed,
        WorldMapRoutePlan route) =>
        refreshed.VehicleContactPoints.TryGetValue(route.TargetTriangleId, out var contact) &&
        route.Waypoints.Count > 0 &&
        (route.Waypoints[^1].X != contact.X || route.Waypoints[^1].Z != contact.Z);

    /// <summary>
    /// Everything parked on the map except the vehicle the player asked to walk to. Its
    /// approach is a point inside its own footprint, because that is the only place the
    /// game boards it from, so detouring around the destination would stop the party just
    /// outside the one cell that makes it boardable. Every other vehicle still blocks.
    /// </summary>
    private IReadOnlyList<WorldMapEntitySnapshot> ObstaclesOtherThan(WorldMapNavigationTarget target)
    {
        var entities = entityProvider!();
        return target.NativeVehicleContact is null
            ? entities
            : entities
                .Where(entity => !string.Equals(
                    $"world-entity:{entity.GuestPointer:X8}:{entity.ModelId}",
                    target.StableId,
                    StringComparison.Ordinal))
                .ToArray();
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
               (!planner.CanTraverseSegment(
                    state, route.Waypoints[index], null, activeTarget?.NativeEntranceExemptions) ||
                (!IsFootprintSegmentClear(state, route.Waypoints[index]) &&
                 !IsOnLeg(state, route.Waypoints[index - 1], route.Waypoints[index]))))
        {
            index--;
        }
        return index;
    }

    /// <summary>
    /// How near a leg counts as being on it: two native frames of movement. Each boat and
    /// walking leg was planned where the footprint fits; the boat or the party weaves about
    /// it on eight directions, and a line from wherever it has weaved to straight at the
    /// next corner can clip the bank or the cliff that the leg itself clears. Sending it
    /// back to the last corner for that makes it circle there. Further off than this it is
    /// not weaving: on the way to Mount Corel at camera 3452 the party slid 118 units off a
    /// 123-unit leg with the foot of the mountain between, and held as on the leg it was
    /// steered into the mountain until auto walk gave up.
    /// </summary>
    private const double BoatLegCorridor = 2 * BroncoFrameStep;

    private const double WalkingLegCorridor = 2 * WalkingFrameStep;

    /// <summary>
    /// How far down its leg the boat or the party aims, so that the weave closes on the
    /// leg: four native frames of its movement.
    /// </summary>
    private const double BoatLegLookahead = 4 * BroncoFrameStep;

    private const double WalkingLegLookahead = 4 * WalkingFrameStep;

    /// <summary>The boat and the party on foot follow legs planned for their footprint.</summary>
    private static bool FollowsLegs(int playerModelId) =>
        playerModelId == WorldMapBroncoLanding.BroncoModelId ||
        WorldMapRoutePlanner.IsWalkingModel(playerModelId);

    private bool IsOnLeg(
        WorldMapStateSnapshot state,
        WorldMapRouteWaypoint legStart,
        WorldMapRouteWaypoint legEnd)
    {
        if (!FollowsLegs(state.PlayerModelId))
        {
            return false;
        }

        var (_, offset, _) = MeasureAlongLeg(state, legStart, legEnd);
        return offset <= (state.PlayerModelId == WorldMapBroncoLanding.BroncoModelId
            ? BoatLegCorridor
            : WalkingLegCorridor);
    }

    /// <summary>
    /// Where the boat or the party is against a leg: how far along it, how far off it, and
    /// the leg's own length, all in the leg's wrapped frame.
    /// </summary>
    private (double Along, double Offset, double Length) MeasureAlongLeg(
        WorldMapStateSnapshot state,
        WorldMapRouteWaypoint legStart,
        WorldMapRouteWaypoint legEnd)
    {
        var lineX = (double)WorldMapTargetCatalog.WrappedDelta(legStart.X, legEnd.X, map.WrapWidth);
        var lineZ = (double)WorldMapTargetCatalog.WrappedDelta(legStart.Z, legEnd.Z, map.WrapHeight);
        var boatX = (double)WorldMapTargetCatalog.WrappedDelta(legStart.X, state.X, map.WrapWidth);
        var boatZ = (double)WorldMapTargetCatalog.WrappedDelta(legStart.Z, state.Z, map.WrapHeight);
        var length = Math.Sqrt(lineX * lineX + lineZ * lineZ);
        if (length < 1d)
        {
            return (0d, Math.Sqrt(boatX * boatX + boatZ * boatZ), 0d);
        }

        var along = (boatX * lineX + boatZ * lineZ) / length;
        var clamped = Math.Clamp(along, 0d, length);
        var nearestX = lineX * clamped / length - boatX;
        var nearestZ = lineZ * clamped / length - boatZ;
        return (along, Math.Sqrt(nearestX * nearestX + nearestZ * nearestZ), length);
    }

    /// <summary>
    /// The offset from the boat or the party to the point it should steer for on its current
    /// leg: a little way down the leg from where it is level with it, never past its end.
    /// </summary>
    private (int X, int Z) ResolveLegAim(
        WorldMapStateSnapshot state,
        WorldMapRouteWaypoint legStart,
        WorldMapRouteWaypoint legEnd)
    {
        var (along, _, length) = MeasureAlongLeg(state, legStart, legEnd);
        var lineX = (double)WorldMapTargetCatalog.WrappedDelta(legStart.X, legEnd.X, map.WrapWidth);
        var lineZ = (double)WorldMapTargetCatalog.WrappedDelta(legStart.Z, legEnd.Z, map.WrapHeight);
        var boatX = (double)WorldMapTargetCatalog.WrappedDelta(legStart.X, state.X, map.WrapWidth);
        var boatZ = (double)WorldMapTargetCatalog.WrappedDelta(legStart.Z, state.Z, map.WrapHeight);
        if (length < 1d)
        {
            return ((int)Math.Round(lineX - boatX), (int)Math.Round(lineZ - boatZ));
        }

        var lookahead = state.PlayerModelId == WorldMapBroncoLanding.BroncoModelId
            ? BoatLegLookahead
            : WalkingLegLookahead;
        var aim = Math.Clamp(along + lookahead, 0d, length);
        return ((int)Math.Round(lineX * aim / length - boatX), (int)Math.Round(lineZ * aim / length - boatZ));
    }

    /// <summary>
    /// Whether the straight line to a waypoint keeps the whole footprint where it may go,
    /// one native frame of movement at a time. Triangles alone say a line across a bend is
    /// water, or a line past the foot of a cliff is grass; FUN_00751EFC says whether the
    /// boat, 350 units either way, or the party, 200, can actually follow it. Aiming across
    /// what they cannot round leaves every key that makes progress pressing into the bank or
    /// the cliff. Always clear for anybody else.
    ///
    /// <para>The walking footprint is stricter than the game on the bridges, so where the
    /// party already stands somewhere it says the party cannot, it is not asked at all and
    /// the centre line alone decides, as it always did.</para>
    /// </summary>
    private bool IsFootprintSegmentClear(WorldMapStateSnapshot state, WorldMapRouteWaypoint waypoint)
    {
        Func<int, int, int, bool> fitsAt;
        double frameStep;
        if (state.PlayerModelId == WorldMapBroncoLanding.BroncoModelId)
        {
            fitsAt = (x, _, z) => WorldMapBroncoLanding.HasBoatFootprint(map, x, z);
            frameStep = BroncoFrameStep;
        }
        else if (WorldMapRoutePlanner.IsWalkingModel(state.PlayerModelId) &&
                 planner.HasWalkingFootprint(state, state.X, state.Y, state.Z))
        {
            fitsAt = (x, y, z) => planner.HasWalkingFootprint(state, x, y, z);
            frameStep = WalkingFrameStep;
        }
        else
        {
            return true;
        }

        var dx = (double)WorldMapTargetCatalog.WrappedDelta(state.X, waypoint.X, map.WrapWidth);
        var dy = waypoint.Y - (double)state.Y;
        var dz = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, waypoint.Z, map.WrapHeight);
        var samples = (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / frameStep);
        for (var sample = 1; sample <= samples; sample++)
        {
            var fraction = sample / (double)samples;
            if (!fitsAt(
                    state.X + (int)Math.Round(dx * fraction),
                    state.Y + (int)Math.Round(dy * fraction),
                    state.Z + (int)Math.Round(dz * fraction)))
            {
                return false;
            }
        }

        return true;
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

        // A place the party cannot walk to is not a destination, and offering one is how
        // a route gets promised that can never start. The party's own vehicles are the
        // exception: they are things the player owns and left somewhere, a sighted player
        // can see them sitting there, and where they are is information in its own right.
        //
        // The user got off the Tiny Bronco on the far bank of the river it was moored in
        // and Transportation went silent - the boat, and the Buggy parked on another
        // continent, both vanished from the list rather than being reported as somewhere
        // they could not currently walk. Selecting one that has no walking route says so:
        // DescribeSelection falls through to "Route unavailable." and StartNavigation
        // refuses without leaving a route running. Nothing here invents an arrival or a
        // position, so this is the vehicle being reported, never a way of reaching it.
        if (CurrentCategory == WorldMapNavigationCategory.Transportation)
        {
            return candidates.ToArray();
        }

        // Aboard the Tiny Bronco a town is still a destination when the boat can take the
        // party to a shore that leads to it on foot. Nothing about the place has changed -
        // Gongaga is where it was - so dropping it because it cannot be sailed into would
        // hide a trip the player can actually make.
        return candidates
            .Where(target =>
                planner.CanReach(state, target) || IsReachedByLanding(target, state))
            .ToArray();
    }

    private static bool IsDestination(WorldMapNavigationTarget target) =>
        target.Kind is WorldMapTargetKind.Location or WorldMapTargetKind.Story;

    /// <summary>
    /// A destination the party can only reach from the Tiny Bronco by getting off at a
    /// shore. The boat is held to water - FUN_0074CECA gives model 5 mask 0x70, terrain 4,
    /// 5 and 6 - and field entrances are on land, so none is sailed into; the question is
    /// whether a landing it can reach puts the party on ground joined to it on foot. The
    /// landing rule itself is <see cref="WorldMapBroncoLanding"/>.
    /// </summary>
    private bool IsReachedByLanding(WorldMapNavigationTarget target, WorldMapStateSnapshot state) =>
        IsDestination(target) &&
        state.PlayerModelId == WorldMapBroncoLanding.BroncoModelId &&
        !planner.CanReach(state, target) &&
        WorldMapBroncoLanding.HasLanding(planner, map, state, target);

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
        landingPlan = null;
        landingPhase = LandingPhase.Approaching;
        landingSettleSamples = 0;
        landingClosestApproach = double.PositiveInfinity;
        landingStalledSamples = 0;
        landingOnRunIn = false;
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
