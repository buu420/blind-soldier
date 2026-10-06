namespace Ff7.Accessibility.Reloaded;

public delegate IReadOnlyList<WorldMapNavigationTarget> WorldMapTargetProvider(
    WorldMapStateSnapshot state,
    WorldMapNavigationCategory category);

public readonly record struct WorldMapNavigationOutput(
    string? Speech,
    bool StopAutoWalk = false,
    // A Great Glacier treasure route's next snowfield leg, started for a player who asked to
    // be walked: the host starts world-map auto walk for it.
    bool StartAutoWalk = false);

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
public sealed partial class WorldMapNavigationController
{
    private const double WaypointArrivalDistance = 480d;
    private const double AutomaticMovementProbeDistance = 120d;

    /// <summary>FUN_0074EA48 moves model 5 by 0x3C units a frame.</summary>
    private const double BroncoFrameStep = 60d;

    /// <summary>FUN_0074EA48 moves the party on foot by 0x1E units a frame.</summary>
    private const double WalkingFrameStep = 30d;

    /// <summary>FUN_0074EA48: the Buggy moves 0x2D a frame, and is held by the same footprint.</summary>
    private const double BuggyFrameStep = 45d;

    /// <summary>
    /// Models FUN_00751EFC holds to the 200-unit ground footprint that this controller
    /// steers by: the party on foot and the Buggy. The boat has its own 350-unit rule above;
    /// flying, the submarine and the chocobos are not steered by it.
    /// </summary>
    private static bool UsesGroundFootprint(int modelId) => WorldMapRoutePlanner.UsesGroundFootprint(modelId);

    private const int WorldMapBuggyModelId = WorldMapRoutePlanner.BuggyModelId;

    /// <summary>FUN_0074EA48 flies model 3 at 0x78 a frame (eased), whatever the height.</summary>
    private const double FlightFrameStep = 120d;

    private static double GroundFrameStep(int modelId) =>
        modelId == WorldMapBuggyModelId ? BuggyFrameStep
        : modelId == WorldMapHighwindLanding.HighwindModelId ? FlightFrameStep
        : WalkingFrameStep;

    /// <summary>The native frame of the model automatic movement is steering right now.</summary>
    private double groundFrameStep = WalkingFrameStep;
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
    private WorldMapRoutePlan? vehicleMasksPredictedOnRoute;

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

    // In the Highwind, flying to a grass spot for the active destination. Null for every
    // other route. The player lands; nothing here presses Cancel.
    private WorldMapHighwindLandingPlan? highwindLanding;
    private HighwindPhase highwindPhase;
    private bool highwindTightLookout;
    private bool highwindBraking;
    private DateTime highwindBrakeUntil;
    private DateTime highwindStillSince;
    private WorldMapRouteWaypoint highwindLastPosition;
    private bool highwindHasLastPosition;
    private double highwindSampleTravel;
    private bool highwindQuietUntilMoved;

    /// <summary>
    /// How near the spot a player flying the ship themselves may come to rest and still be
    /// told whether to land there. Let go, the ship coasts on along its heading, not toward
    /// the spot, so their stop is judged wherever it falls this near.
    /// </summary>
    private const double HighwindPilotLookout = 1024d;

    /// <summary>
    /// How long the flight action is held alone to stop the ship: FUN_0074EA48 eases the
    /// thrust by (3 * previous) &gt;&gt; 2 a frame, 117 to nothing in 15 frames, which is half a
    /// second at the world map's 30 frames a second.
    /// </summary>
    private static readonly TimeSpan HighwindBrakeTime = TimeSpan.FromMilliseconds(600);

    /// <summary>How long the ship must stay where it is before the landing is judged.</summary>
    private static readonly TimeSpan HighwindStillTime = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Where the ship stops being driven on its way in. FUN_0074EA48 eases the flying speed
    /// (0x78 a frame, on each axis) by a quarter a frame, so released at this distance the ship
    /// runs on to about the spot; the landing is judged once it is at rest.
    /// </summary>
    private const double HighwindLandingLookout = 240d;

    /// <summary>After a stop that did not fit, the way in is taken up again in short hops.</summary>
    private const double HighwindTightLookout = 128d;

    private enum HighwindPhase
    {
        Approaching,
        Settling,
        Ready
    }

    private bool HighwindHolding => highwindLanding is not null && highwindPhase != HighwindPhase.Approaching;

    private const string HighwindRouteSummary = "By Highwind to a landing spot on grass, then on foot";

    /// <summary>
    /// Whether the direction the last <see cref="TryResolveAutomaticInput"/> returned is a
    /// flight direction, to be held together with the 0x80 action (control slot 7): in flight
    /// camera 3 FUN_0074EA48 translates the Highwind on the directions only while it is held,
    /// by the same camera rotation as walking. Hosts pass it to the auto-walk input.
    /// </summary>
    public bool AutomaticInputHoldsFlightAction { get; private set; }
    public bool AutomaticInputIsWorldSubmarine { get; private set; }
    public bool AutomaticInputHoldsSubmarineThrust { get; private set; }

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
        categoryIndex < WorldMapTargetCatalog.CategoryOrder.Count
            ? WorldMapTargetCatalog.CategoryOrder[categoryIndex]
            : WorldMapNavigationCategory.Objects;

    /// <summary>
    /// The Great Glacier's regional treasure routes, shared with the field controller. On the
    /// snowfield it adds an Objects category after Regions, and keeps a chosen treasure as the
    /// destination across every snowfield leg.
    /// </summary>
    public GreatGlacierRegionalNavigator? GlacierRegion { get; set; }

    // The active route is a leg of the glacier objective, not the player's own destination.
    private bool glacierLegActive;
    private int glacierLegFieldEntries;
    private bool glacierPreparingAnnounced;
    private DateTime glacierPreparingSince;
    private bool glacierHoldingAtEntrance;
    private IReadOnlyList<WorldMapNavigationTarget>? glacierRows;
    private int glacierRowsTriangle = -1;
    private int glacierRowsSignature = -1;
    private DateTime glacierRowsAt;

    /// <summary>
    /// A snowfield leg belongs to this visit of the world map. Once the game has been in a
    /// field since it was started - whether or not the treasure is still wanted - it is retired,
    /// so nothing planned before is walked or revived on the next visit.
    /// </summary>
    private void RetireStaleGlacierLeg()
    {
        if (!glacierLegActive || GlacierRegion is not { } region || region.FieldEntries == glacierLegFieldEntries)
        {
            return;
        }

        lastDiagnostic = "retired the snowfield leg of a treasure route: the party went into a field";
        ResetRoute();
    }

    /// <summary>
    /// A treasure route is waiting for its next leg: the beacon is off but the destination is
    /// held. Hosts give this to <see cref="ControllerNavigationServices"/> so B cancels it and
    /// A or X replace it, exactly as a field destination held for a shut door.
    /// </summary>
    public bool IsHoldingDestination => !beaconEnabled && GlacierRegion?.Objective is not null;

    /// <summary>The player started world-map auto walk: later legs of a treasure route walk too.</summary>
    public void NoteAutoWalkStarted() => GlacierRegion?.NoteAutoWalk(true);

    /// <summary>The player (or the stall guard) stopped auto walk: later legs are spoken only.</summary>
    public void NoteAutoWalkStopped() => GlacierRegion?.NoteAutoWalk(false);

    private bool ObjectsAvailable(WorldMapStateSnapshot state) =>
        GlacierRegion is not null && state.WorldMapType == GreatGlacierRegion.SnowfieldWorldMapType;

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
        RetireStaleGlacierLeg();
        if (categoryIndex >= WorldMapTargetCatalog.CategoryOrder.Count && !ObjectsAvailable(state))
        {
            categoryIndex = 0;
        }

        switch (action)
        {
            case FieldNavigationAction.PreviousCategory:
                MoveCategory(-1, state);
                return RelockAndDescribe(state, now);
            case FieldNavigationAction.NextCategory:
                MoveCategory(1, state);
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
                    if (glacierLegActive)
                    {
                        GlacierRegion?.Cancel("navigation switched off on the snowfield");
                    }

                    ResetRoute();
                    return new WorldMapNavigationOutput("Navigation off.");
                }

                if (GlacierRegion?.Objective is not null)
                {
                    // Waiting for its next leg: cancelled, as a held field destination is.
                    GlacierRegion.Cancel("navigation switched off while the next leg was pending");
                    return new WorldMapNavigationOutput("Navigation off.");
                }

                var selected = GetSelectedTarget(state);
                if (selected is null)
                {
                    return DescribeSelection(state);
                }

                if (GlacierRegion is { } region &&
                    GreatGlacierRegionalNavigator.TryParseRow(selected.StableId, out var treasure))
                {
                    region.Start(treasure, autoWalk: false);
                    glacierPreparingAnnounced = false;
                    glacierPreparingSince = default;
                    glacierHoldingAtEntrance = false;
                    return TryStartGlacierLeg(state, now, fromSelection: true);
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
        RetireStaleGlacierLeg();
        if (GlacierRegion?.Objective is { } objective)
        {
            GlacierRegion.ObserveWorldMap();
            if (state.CurrentModule == WorldMapStateReader.WorldModule &&
                state.WorldMapType != GreatGlacierRegion.SnowfieldWorldMapType)
            {
                GlacierRegion.Cancel($"left the snowfield for world map {state.WorldMapType}");
                if (glacierLegActive)
                {
                    ResetRoute();
                }

                return new WorldMapNavigationOutput(
                    $"Left the Great Glacier. Navigation to {objective.Label} cancelled.",
                    StopAutoWalk: true);
            }

            if (!beaconEnabled && !objective.Paused && IsUsable(state))
            {
                return TryStartGlacierLeg(state, now, fromSelection: false);
            }

            if (beaconEnabled && glacierLegActive && automaticWalkActive)
            {
                objective.AutoWalk = true;
            }
        }

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

        // A moving destination must not replan away a refused approach before its
        // explanation is delivered. The prior command already released every input.
        if (submarineUnsafeApproach is not null && state.PlayerModelId == 13 && state.WorldMapType == 2 &&
            activeTarget is { NativeUnderwaterArrival.ModelId: 30 } unsafeTarget)
        {
            return ObserveSubmarineApproach(state, unsafeTarget, -1, automaticWalkActive, now);
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
        // target merely because it occupies the same category slot. A glacier leg is a
        // snowfield exit, which never moves.
        var refreshed = glacierLegActive
            ? null
            : GetTargets(state)
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
                 HasMovedItsContactPoint(refreshed, activeRoute) ||
                 (refreshed.NativeUnderwaterArrival?.ModelId == 30 &&
                  UnderwaterRouteEndMoved(refreshed, activeRoute))))
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

        if (ObserveSubmarineApproach(state, target, playerTriangle, automaticWalkActive, now) is { } submarineOutput)
            return submarineOutput;

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

        if (highwindLanding is { } highwindPlan)
        {
            var landingPrompt = ObserveHighwindLanding(highwindPlan, target, state, now, automaticWalkActive);
            if (HighwindHolding)
            {
                // Held over the spot for the player's own landing: nothing is driven.
                autoWalkConvergence.Reset();
                return landingPrompt;
            }

            if (landingPrompt is not null)
            {
                return landingPrompt;
            }
        }

        if (highwindLanding is null && !UsesHighwindLanding(target, state) && target.HasArrived(state, playerTriangle))
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
            var legOfObjective = glacierLegActive && GlacierRegion?.Objective is not null;
            var onwards = throughTarget is { } beyond && ReachedThrough.TryGetValue(beyond.Label, out var way)
                ? $" For {beyond.Label}, {way.Onwards}."
                : string.Empty;
            ResetRoute(deactivateProgress: false);
            lastDiagnostic = $"arrived on native triangle {playerTriangle}";
            return new WorldMapNavigationOutput(
                legOfObjective
                    ? $"Arrived at {label}. Continuing to {GlacierRegion!.Objective!.Label}."
                    : $"Arrived at {label}.{onwards} Navigation off.");
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
            var hasLocalDetour = ResolveVehicleDetourOnRoute(state, activeRoute, activeRoute.Waypoints[waypointIndex], out _) ==
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
                // Stopped by the guard: the next legs of a treasure route are spoken, not
                // walked, until the player asks again.
                NoteAutoWalkStopped();
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
        AutomaticInputIsWorldSubmarine = false;
        AutomaticInputHoldsSubmarineThrust = false;
        AutomaticInputHoldsFlightAction = false;
        if (state.PlayerModelId == WorldMapSubmarineSteering.PlayerModelId)
        {
            AutomaticInputIsWorldSubmarine = true;
            if (state.WorldMapType == 2)
                return TryResolveSubmarineInput(state, out input);
            if (state.WorldMapType != 0 || !state.HasNativeControlMode ||
                state.NativeCameraMode is not (0 or 1 or 2) ||
                state.NativeFrameMultiplier is < 1 or > 4 || state.CameraFront is < 0 or >= 4096)
            { input = FieldNavigationInput.None; return false; }

            // Surface modes 0 and 1 move in world axes. Mode 2 rotates the movement and
            // also moves sideways while turning: the full native lease is checked below.
            var surfaceControls = state.NativeCameraMode is 0 or 1
                ? state with { CameraFront = 0, ControlTransform = new FieldNavigationControlTransform(0) }
                : state;
            return TryResolveAutomaticDirection(surfaceControls, out input);
        }
        if (highwindBraking && highwindLanding is not null &&
            state.PlayerModelId == WorldMapHighwindLanding.HighwindModelId && beaconEnabled && !combatPaused)
        {
            // Stopping the ship: the flight action alone, no direction. FUN_0074EA48 then
            // gives the strafe no X or Z at all while the eased thrust decays; letting go of
            // everything instead drops into normal flight, which carries on forward on that
            // thrust (from 117, 333 units).
            input = FieldNavigationInput.None;
            AutomaticInputHoldsFlightAction = true;
            return false;
        }

        var resolved = TryResolveAutomaticDirection(state, out input);
        AutomaticInputHoldsFlightAction = resolved && state.PlayerModelId == WorldMapHighwindLanding.HighwindModelId;
        return resolved;
    }

    private bool TryResolveAutomaticDirection(
        WorldMapStateSnapshot state,
        out FieldNavigationInput input)
    {
        input = FieldNavigationInput.None;
        // Nothing planned before the party last went into a field is driven.
        RetireStaleGlacierLeg();
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

        // Over the Highwind's landing spot the ship is held still for the player's own landing.
        if (HighwindHolding)
        {
            return false;
        }

        // Where the route says the party is heading, before a straight line that is not clear
        // sends automatic movement back to an earlier corner.
        // How far the party moves between samples while this is driving it: a host sample
        // can span several native frames, and the slot walk plans in those steps.
        var sampleMoved = Math.Sqrt(
            Math.Pow(WorldMapTargetCatalog.WrappedDelta(lastAutomaticPosition.X, state.X, map.WrapWidth), 2) +
            Math.Pow(WorldMapTargetCatalog.WrappedDelta(lastAutomaticPosition.Z, state.Z, map.WrapHeight), 2));
        groundFrameStep = GroundFrameStep(state.PlayerModelId);
        if (hasLastAutomaticPosition && sampleMoved <= 6 * groundFrameStep)
        {
            observedSampleStep = Math.Max(observedSampleStep * 0.9d, sampleMoved);
        }

        // The game's own answer to the last key: the same key, under the same camera, pressed
        // for two samples running from the same spot without moving the party at all, was
        // refused here. What is remembered is the world direction it pushed in, not the raw
        // key: after the camera turns, the same key points somewhere else and may well be the
        // way out.
        if (hasLastAutomaticPosition && state.X == lastAutomaticPosition.X && state.Z == lastAutomaticPosition.Z &&
            lastAutomaticKey is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
        {
            if (lastAutomaticKey == refusalCountingKey &&
                Math.Abs(CameraTurn(lastAutomaticCamera, refusalCountingCamera)) <= RefusalCameraTolerance)
            {
                unmovedSamples++;
            }
            else
            {
                refusalCountingKey = lastAutomaticKey;
                refusalCountingCamera = lastAutomaticCamera;
                unmovedSamples = 1;
            }

            if (unmovedSamples >= 2)
            {
                var heading = WorldHeading(lastAutomaticKey, lastAutomaticCamera);
                if (!refusedHeadingsHere.Contains(heading))
                {
                    refusedHeadingsHere.Add(heading);
                }
            }
        }
        else
        {
            unmovedSamples = 0;
            refusalCountingKey = FieldNavigationInput.None;
            refusedHeadingsHere.Clear();
        }

        lastAutomaticPosition = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        hasLastAutomaticPosition = true;
        var measuredWaypoint = Math.Clamp(
            Math.Max(waypointIndex, MeasurePolylineProgress(routeStart, route.Waypoints, state, map.WrapWidth, map.WrapHeight)
                .NextWaypointIndex),
            0,
            route.Waypoints.Count - 1);
        waypointIndex = ResolveAutomaticWaypoint(state, route, waypointIndex);
        var steppedBack = waypointIndex < measuredWaypoint;
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
            lastAutomaticKey = input;
            lastAutomaticCamera = state.CameraFront;
            return input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
        }

        var waypoint = route.Waypoints[waypointIndex];

        // A vehicle the party parked themselves can refuse the approach. Walk round it
        // when the native masks leave a way round. When they leave none, that is only the
        // mask model's prediction, not the game's answer: on 2026-09-27 the party walked on
        // foot straight through the modelled footprint of the Buggy parked beside Old Man's
        // House (Mythril) and in at the door, the game accepting every step, while automatic
        // walking pressed nothing at all. So the route is then walked as if the car were
        // not there, and the game decides: a key it refuses is remembered and not pressed
        // again here, and no progress ends in the ordinary spoken fail-stop.
        var detourOutcome = ResolveVehicleDetourOnRoute(state, route, waypoint, out var detour);
        var vehicleMasksAreOnlyPredicted = detourOutcome == WorldMapVehicleDetourPlanner.DetourOutcome.Blocked;
        var followsRoute = detourOutcome != WorldMapVehicleDetourPlanner.DetourOutcome.Detour;
        if (detourOutcome == WorldMapVehicleDetourPlanner.DetourOutcome.Detour)
        {
            waypoint = detour;
        }
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, waypoint.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, waypoint.Z, map.WrapHeight);
        if (state.PlayerModelId == WorldMapBroncoLanding.BroncoModelId ||
            (WorldMapRoutePlanner.UsesGroundFootprint(state.PlayerModelId) && followsRoute))
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
        // Not on the way to a vehicle: the last step there is into the vehicle's own mask,
        // which the game refuses while it records the contact that boards it.
        var footprintHere = UsesGroundFootprint(state.PlayerModelId) &&
            activeTarget.NativeVehicleContact is null &&
            activeTarget.VehicleContactPoints.Count == 0 &&
            !IsOnBridge(state, state.X, state.Z) &&
            planner.HasWalkingFootprint(state, state.X, state.Y, state.Z);
        bool IsCentreClear(FieldNavigationInput candidate)
        {
            if (state.PlayerModelId == WorldMapSubmarineSteering.PlayerModelId && state.WorldMapType == 0)
            {
                return WorldMapSubmarineSurfaceSteering.IsCommandSafe(
                    state, candidate, map, planner, activeTarget.NativeEntranceExemptions,
                    activeTarget.ArrivalTriangleIds);
            }

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
                (state.PlayerModelId is not (0 or 1 or 2) || entityProvider is null || vehicleMasksAreOnlyPredicted ||
                 !WorldMapVehicleObstacles.BlocksSegment(
                     (activeTarget is { } selected ? ObstaclesOtherThan(selected) : entityProvider())
                         .Where(WorldMapVehicleObstacles.IsParkedVehicle).ToArray(),
                     state.PlayerModelId,
                     state.X, state.Z, next.X, next.Z, map.WrapWidth, map.WrapHeight));
        }

        // The party on foot is refused by the same routine as the boat: FUN_00751EFC moves
        // models 0, 1 and 2 only where the centre and the points 200 units along each axis
        // (007530B3) stand on walking ground. A key whose next native frame (0x1E) lands
        // where that does not fit is pressed into a refusal - the 2026-09-26 Old Man's House
        // stall, held for five seconds a door's width from the trigger. A whole frame is
        // judged even when the aim is nearer than that, because the game moves a whole frame.
        // Where the party already stands somewhere the footprint says it cannot (the
        // bridges), it is not asked.
        bool FitsWalkingFrame(FieldNavigationInput candidate)
        {
            if (!footprintHere)
            {
                return true;
            }

            var (x, z) = NativeWalkingFrame(candidate, state.CameraFront, groundFrameStep);
            var landingX = state.X + x;
            var landingZ = state.Z + z;
            return IsOnBridge(state, landingX, landingZ) ||
                   planner.HasWalkingFootprint(state, landingX, state.Y, landingZ);
        }

        // A key the game has just refused here is not pressed again here: whatever this
        // model says, holding it again is the five-second stall.
        bool IsClear(FieldNavigationInput candidate) =>
            !(footprintHere && IsRefusedHere(candidate, state.CameraFront)) &&
            IsCentreClear(candidate) && FitsWalkingFrame(candidate);

        FieldNavigationInput ClosestForward(Func<FieldNavigationInput, bool> clear) =>
            Enum.GetValues<FieldNavigationInput>()
                .Where(candidate => candidate is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
                .Select(candidate => (Input: candidate, World: PredictNativeMovement(candidate, state.CameraFront)))
                .Select(candidate => (candidate.Input, Progress:
                    (candidate.World.X * dx + candidate.World.Z * dz) /
                    Math.Sqrt(candidate.World.X * candidate.World.X + candidate.World.Z * candidate.World.Z)))
                .Where(candidate => candidate.Progress > 0d)
                .OrderByDescending(candidate => candidate.Progress)
                .Select(candidate => candidate.Input)
                .FirstOrDefault(clear);

        // A clear oblique route can quantize to a cardinal key that hits a
        // cliff. Keep the closest forward key with a clear native short step.
        var aimed = input;
        var hasAim = aimed is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
        if (hasAim && !IsClear(input))
        {
            input = ClosestForward(IsClear);
        }

        // On foot, where the footprint leaves only a slot (the Old Man's House door is one
        // row about 30 units wide between two mountain faces) neither the corner ahead nor the
        // one behind has a clear straight line: aiming at them either presses no key or turns
        // the party back and forth between the two every sample. A short walk through steps
        // the game itself would accept is found once and then followed, so that holding a key
        // for several native frames between observations cannot turn it round.
        if (hasAim && footprintHere && landingPlan is null && followsRoute)
        {
            if (slotPath.Count > 0 && !ReferenceEquals(slotRoute, route))
            {
                ClearSlotPath();
                slotModeEntry = double.NaN;
            }

            // Once the plain aim has failed here, keep walking by search until the party is
            // a whole search horizon further along than where it began. Handing back sooner
            // lets the corner aim step off the route again, and the next search bring it
            // straight back: the Costa replay did that at 120 units a sample.
            var needsSearch = steppedBack || input is not (>= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft);
            if (!double.IsNaN(slotModeEntry) &&
                SlotPotential(state, route) <= slotModeEntry - WalkingSlotSearchFrames * groundFrameStep)
            {
                slotModeEntry = double.NaN;
                ClearSlotPath();
            }

            if (needsSearch && double.IsNaN(slotModeEntry))
            {
                slotModeEntry = SlotPotential(state, route);
            }

            // A walk that is finished or left behind is replaced at once while searching,
            // rather than handing this sample back to the corner aim.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (slotPath.Count == 0 && !double.IsNaN(slotModeEntry))
                {
                    SearchWalkingSlot(state, route, honourVehicleMasks: !vehicleMasksAreOnlyPredicted);
                    if (slotPath.Count == 0)
                    {
                        slotModeEntry = double.NaN;
                    }
                }

                if (slotPath.Count == 0)
                {
                    break;
                }

                if (TryFollowSlotPath(state, IsClear, out var slotInput))
                {
                    input = slotInput;
                    lastAutomaticKey = input;
                    lastAutomaticCamera = state.CameraFront;
                    return true;
                }

                ClearSlotPath();
            }
        }
        else
        {
            ClearSlotPath();
            slotModeEntry = double.NaN;
        }

        // The footprint is how routes keep their room, never a reason to refuse one: it is
        // stricter than the game in places (the bridges; the edge of a boarding shore). When
        // no key both fits it and closes, keep the centre-line choice the controller always
        // made rather than stopping.
        if (hasAim && input is not (>= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft))
        {
            bool CentreClearAndNotRefused(FieldNavigationInput candidate) =>
                !(footprintHere && IsRefusedHere(candidate, state.CameraFront)) && IsCentreClear(candidate);
            input = CentreClearAndNotRefused(aimed) ? aimed : ClosestForward(CentreClearAndNotRefused);
        }

        lastAutomaticKey = input;
        lastAutomaticCamera = state.CameraFront;
        return input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
    }

    private WorldMapRouteWaypoint paceLastPosition;
    private int paceLastCamera;
    private bool paceHasLast;
    private string paceLastSignature = string.Empty;

    /// <summary>
    /// One line for the host log about what automatic walking pressed, what the engine's own
    /// input mask held, and how the party moved - or null when nothing worth a line has
    /// changed.
    ///
    /// <para><c>native</c> is the world input mask the engine moved by (DAT_009A85D4, read by
    /// the host), or unknown. <c>motionEstimate</c> is only an estimate from displacement: the
    /// key whose FUN_0074EA48 direction under the previous sample's camera matches the
    /// movement since then, named only when the party moved and one key matches closely
    /// ("none" or "unmatched" otherwise). Sliding and camera turns can make it differ from the
    /// key the engine saw, which is why both are given. A line is written when the command,
    /// the native mask, the estimate or the slot walk changes - never for position alone.</para>
    /// </summary>
    public string? DescribeAutomaticPace(
        WorldMapStateSnapshot state,
        bool hasDirection,
        FieldNavigationInput commanded,
        uint? nativeInputMask = null)
    {
        var motionEstimate = "none";
        var step = 0d;
        if (paceHasLast)
        {
            var dx = (double)WorldMapTargetCatalog.WrappedDelta(paceLastPosition.X, state.X, map.WrapWidth);
            var dz = (double)WorldMapTargetCatalog.WrappedDelta(paceLastPosition.Z, state.Z, map.WrapHeight);
            step = Math.Sqrt(dx * dx + dz * dz);
            if (step >= 4d)
            {
                var best = Enum.GetValues<FieldNavigationInput>()
                    .Where(candidate => candidate is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
                    .Select(candidate => (Input: candidate, World: PredictNativeMovement(candidate, paceLastCamera)))
                    .Select(candidate => (candidate.Input, Match:
                        (candidate.World.X * dx + candidate.World.Z * dz) /
                        (Math.Sqrt(candidate.World.X * candidate.World.X + candidate.World.Z * candidate.World.Z) * step)))
                    .OrderByDescending(candidate => candidate.Match)
                    .First();
                motionEstimate = best.Match >= 0.92d ? best.Input.ToString() : "unmatched";
            }
        }

        paceLastPosition = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        paceLastCamera = state.CameraFront;
        paceHasLast = true;
        var command = hasDirection ? commanded.ToString() : "None";
        var slot = slotPath.Count > 0 ? "slot" : "route";
        var native = nativeInputMask is { } mask ? DescribeNativeInput(mask) : "unknown";
        if (hasDirection && AutomaticInputHoldsFlightAction)
        {
            command += "+flight action";
        }

        // The 0x80 action the Highwind is flown with: whether the engine's own mask holds it.
        if (state.PlayerModelId == WorldMapHighwindLanding.HighwindModelId && nativeInputMask is { } flightMask)
        {
            native += (flightMask & 0x80u) != 0 ? "+0x80" : "-0x80";
        }

        var signature = $"{command}|{native}|{motionEstimate}|{slot}";
        if (string.Equals(signature, paceLastSignature, StringComparison.Ordinal))
        {
            return null;
        }

        paceLastSignature = signature;
        return $"commanded={command}, native={native}, motionEstimate={motionEstimate}, step={step:0}, position={state.X},{state.Y},{state.Z}, " +
               $"camera={state.CameraFront}, model={state.PlayerModelId}, waypoint={waypointIndex}, " +
               (slotPath.Count > 0 ? SlotDiagnostic : "following the route");
    }

    /// <summary>The native mask's direction bits as a key name, with the raw direction bits.</summary>
    private static string DescribeNativeInput(uint mask)
    {
        var up = (mask & 0x1000u) != 0;
        var right = (mask & 0x2000u) != 0;
        var down = (mask & 0x4000u) != 0;
        var left = (mask & 0x8000u) != 0;
        var name = (up, right, down, left) switch
        {
            (false, false, false, false) => "none",
            (true, false, false, false) => "Up",
            (true, true, false, false) => "UpRight",
            (false, true, false, false) => "Right",
            (false, true, true, false) => "DownRight",
            (false, false, true, false) => "Down",
            (false, false, true, true) => "DownLeft",
            (false, false, false, true) => "Left",
            (true, false, false, true) => "UpLeft",
            _ => "conflicting"
        };
        return $"{name}(0x{mask & 0xF000u:X4})";
    }

    /// <summary>How many native frames ahead the slot search looks: 360 units on foot.</summary>
    private const int WalkingSlotSearchFrames = 12;

    /// <summary>A bound on the positions the slot search visits, whatever the ground.</summary>
    private const int WalkingSlotSearchNodes = 400;

    /// <summary>How near a corner of the slot walk counts as reached: half a native frame.</summary>
    private double SlotCornerReach => 0.5 * groundFrameStep;

    /// <summary>Further off the slot walk than this, the party is not following it.</summary>
    private double SlotPathCorridor => 3 * groundFrameStep;

    private readonly List<WorldMapRouteWaypoint> slotPath = new();
    private double slotModeEntry = double.NaN;
    private WorldMapRouteWaypoint lastAutomaticPosition;
    private FieldNavigationInput lastAutomaticKey;
    private int lastAutomaticCamera;
    private FieldNavigationInput refusalCountingKey;
    private int refusalCountingCamera;
    private int unmovedSamples;

    /// <summary>World directions (4096ths of a turn) the game refused from the current spot.</summary>
    private readonly List<int> refusedHeadingsHere = new();

    /// <summary>How far the camera may turn and the same key still count as the same push.</summary>
    private const int RefusalCameraTolerance = 64;

    /// <summary>How near a refused world direction a key's push has to be to count as it.</summary>
    private const int RefusedHeadingTolerance = 160;

    private static int CameraTurn(int from, int to) => ((to - from) % 4096 + 6144) % 4096 - 2048;

    /// <summary>The world direction a key pushes the party in under a camera, in 4096ths.</summary>
    private int WorldHeading(FieldNavigationInput key, int camera)
    {
        var (x, z) = NativeWalkingFrame(key, camera, groundFrameStep);
        return ((int)Math.Round(Math.Atan2(x, -z) * 4096d / (2d * Math.PI)) % 4096 + 4096) % 4096;
    }

    private bool IsRefusedHere(FieldNavigationInput key, int camera)
    {
        if (refusedHeadingsHere.Count == 0)
        {
            return false;
        }

        var heading = WorldHeading(key, camera);
        return refusedHeadingsHere.Any(refused => Math.Abs(CameraTurn(refused, heading)) <= RefusedHeadingTolerance);
    }

    /// <summary>
    /// Forgets what automatic walking learned from the party's recent movement: the previous
    /// command and position, refused directions, the committed slot walk and search mode, the
    /// per-sample cadence and the pace sample. Called whenever something else owns movement
    /// (a world script, a window, the party menu, focus, combat) - keys are released then, so
    /// the first sample afterwards must not read a deliberately released key as a wall. The
    /// selected destination and its route are kept.
    /// </summary>
    private void ResetAutomaticLearning()
    {
        ClearSlotPath();
        slotModeEntry = double.NaN;
        hasLastAutomaticPosition = false;
        observedSampleStep = 0d;
        lastAutomaticKey = FieldNavigationInput.None;
        refusalCountingKey = FieldNavigationInput.None;
        unmovedSamples = 0;
        refusedHeadingsHere.Clear();
        paceHasLast = false;
    }
    private bool hasLastAutomaticPosition;
    private double observedSampleStep;
    private WorldMapRoutePlan? slotRoute;
    private WorldMapRouteWaypoint slotPathStart;
    private WorldMapRouteWaypoint slotLastObserved;
    private double slotObservedStep;
    private int slotPlannedHold = 1;
    private int slotCorner;

    /// <summary>
    /// The Corel and Wutai bridges and their bridgeheads. The footprint model is stricter than
    /// the game there - the 2026-09-23 log has the party pacing the Wutai Bridge nearer its
    /// edge than 200 units - so it is neither a reason to avoid a key nor a way across.
    /// </summary>
    private static bool IsBridgeTerrain(int terrainId) => terrainId is 13 or 14 or 29;

    private bool IsOnBridge(WorldMapStateSnapshot state, int x, int z) =>
        planner.TryResolvePlayerTriangle(state with { X = x, Z = z }, out var triangle) &&
        IsBridgeTerrain(map.Triangles[triangle].TerrainId);

    /// <summary>What the slot walk is doing, for the diagnostics; empty when it is not in use.</summary>
    internal string SlotDiagnostic => slotPath.Count == 0
        ? string.Empty
        : $"slot walk corner {slotCorner + 1}/{slotPath.Count} at {slotPath[slotCorner].X},{slotPath[slotCorner].Z}";

    /// <summary>
    /// What is left of the route from here, in plan: its remaining length plus the distance off
    /// it. Heights are left out on purpose. A leg's height is the straight line between its
    /// corners, not the ground, so on a climb (the Mount Corel foot, 665 to 1109 over one leg)
    /// the ground a step reaches can look far off a route it is standing on.
    /// </summary>
    private double SlotPotential(WorldMapStateSnapshot at, WorldMapRoutePlan route)
    {
        if (!ReferenceEquals(flatSlotRoute, route))
        {
            flatSlotRoute = route;
            flatSlotWaypoints = route.Waypoints.Select(waypoint => waypoint with { Y = 0 }).ToArray();
            flatSlotStart = routeStart with { Y = 0 };
            var total = 0d;
            var previous = flatSlotStart;
            foreach (var waypoint in flatSlotWaypoints)
            {
                total += Math.Sqrt(
                    Math.Pow(WorldMapTargetCatalog.WrappedDelta(previous.X, waypoint.X, map.WrapWidth), 2) +
                    Math.Pow(WorldMapTargetCatalog.WrappedDelta(previous.Z, waypoint.Z, map.WrapHeight), 2));
                previous = waypoint;
            }

            flatSlotLength = total;
        }

        var measurement = MeasurePolylineProgress(flatSlotStart, flatSlotWaypoints, at with { Y = 0 }, map.WrapWidth, map.WrapHeight);
        return flatSlotLength * (1d - measurement.Fraction) + measurement.DistanceFromRoute;
    }

    private WorldMapRoutePlan? flatSlotRoute;
    private IReadOnlyList<WorldMapRouteWaypoint> flatSlotWaypoints = Array.Empty<WorldMapRouteWaypoint>();
    private WorldMapRouteWaypoint flatSlotStart;
    private double flatSlotLength;

    private void ClearSlotPath()
    {
        slotPath.Clear();
        slotRoute = null;
        slotCorner = 0;
    }

    /// <summary>
    /// The key towards the next corner of the committed slot walk, or false when the walk is
    /// finished, left behind, or no longer has a step the game would accept.
    ///
    /// <para>The walk was planned in the steps the party actually takes per host sample, so
    /// its corners are reachable one sample at a time. A corner is reached when the party
    /// stands on it, has gone past it along its leg, or walked over it since the previous
    /// sample - eight directions held for several frames can straddle a point sideways. The
    /// aim never looks past the corner: a planned walk can turn back on itself (out of a
    /// pocket and round), and aiming beyond a turn like that turned the party round.</para>
    /// </summary>
    private bool TryFollowSlotPath(
        WorldMapStateSnapshot state,
        Func<FieldNavigationInput, bool> isClear,
        out FieldNavigationInput input)
    {
        input = FieldNavigationInput.None;
        var previous = slotLastObserved;
        var moved = Math.Sqrt(
            Math.Pow(WorldMapTargetCatalog.WrappedDelta(previous.X, state.X, map.WrapWidth), 2) +
            Math.Pow(WorldMapTargetCatalog.WrappedDelta(previous.Z, state.Z, map.WrapHeight), 2));
        slotObservedStep = Math.Max(slotObservedStep, moved);
        slotLastObserved = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        var corridor = Math.Max(SlotPathCorridor, 1.5 * slotObservedStep);
        var here = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        while (slotCorner < slotPath.Count)
        {
            var from = slotCorner == 0 ? slotPathStart : slotPath[slotCorner - 1];
            var corner = slotPath[slotCorner];
            var (along, offset, length) = MeasureAlongLeg(state, from, corner);
            if (offset > corridor)
            {
                return false;
            }

            var distance = Math.Sqrt(
                Math.Pow(WorldMapTargetCatalog.WrappedDelta(state.X, corner.X, map.WrapWidth), 2) +
                Math.Pow(WorldMapTargetCatalog.WrappedDelta(state.Z, corner.Z, map.WrapHeight), 2));
            var (_, crossed, _) = MeasureAlongLeg(state with { X = corner.X, Z = corner.Z }, previous, here);
            if (distance > SlotCornerReach && along < length && crossed > groundFrameStep)
            {
                break;
            }

            slotCorner++;
        }

        if (slotCorner >= slotPath.Count)
        {
            return false;
        }

        // Planned for shorter samples than the party is now taking: plan again.
        if (SampleHold() > slotPlannedHold)
        {
            return false;
        }

        var aim = slotPath[slotCorner];
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, aim.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, aim.Z, map.WrapHeight);
        var reach = Math.Max(1d, Math.Sqrt(dx * (double)dx + dz * (double)dz));
        var hold = SampleHold();

        // A key held for a whole sample must end nearer the corner than the party is now;
        // one that carries it past and further away turns into a walk back and forth.
        bool EndsNearer(FieldNavigationInput candidate)
        {
            var (stepX, stepZ) = NativeWalkingFrame(candidate, state.CameraFront, groundFrameStep);
            var endX = dx - stepX * hold;
            var endZ = dz - stepZ * hold;
            return endX * (double)endX + endZ * (double)endZ < reach * reach;
        }

        // Only a key that heads for the corner - within 67.5 degrees, one direction either
        // side. When the party stands a few units off the planned walk, the step that fitted
        // there may not fit here; sidestepping instead walked it back and forth, so the walk
        // is given up and planned again from where the party really is.
        input = Enum.GetValues<FieldNavigationInput>()
            .Where(candidate => candidate is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
            .Select(candidate => (Input: candidate, World: PredictNativeMovement(candidate, state.CameraFront)))
            .Select(candidate => (candidate.Input, Progress:
                (candidate.World.X * dx + candidate.World.Z * dz) /
                (Math.Sqrt(candidate.World.X * candidate.World.X + candidate.World.Z * candidate.World.Z) * reach)))
            .Where(candidate => candidate.Progress >= 0.38d)
            .OrderByDescending(candidate => candidate.Progress)
            .Select(candidate => candidate.Input)
            .FirstOrDefault(candidate => EndsNearer(candidate) && isClear(candidate));
        return input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
    }

    /// <summary>How many native frames a host sample has been holding a key for, one to six.</summary>
    private int SampleHold() => Math.Clamp((int)Math.Round(observedSampleStep / groundFrameStep), 1, 6);

    /// <summary>
    /// Finds and commits the best short walk the game would accept, or leaves none when
    /// nothing within reach gets the party a frame further along the route.
    ///
    /// <para>Every step is one native frame (0x1E) in one of the eight directions under the
    /// current camera, and is kept only if the centre line stays on ground the route may use,
    /// it never stands on another place's entrance, the whole FUN_00751EFC footprint fits
    /// where it lands, and no parked Buggy refuses it (unless the vehicle detour has found no
    /// way round, when the masks are only a prediction). A walk is scored by how much of the
    /// route is left from where it ends - the remaining length plus the distance off the line
    /// - so no single corner has to be chosen, which is what flip-flopped. Reaching the
    /// destination's own arrival ground beats everything. The search is bounded in frames
    /// and positions, so it answers in the same observation, and it gives up rather than
    /// pretend.</para>
    /// </summary>
    private void SearchWalkingSlot(WorldMapStateSnapshot state, WorldMapRoutePlan route, bool honourVehicleMasks)
    {
        ClearSlotPath();
        if (!planner.TryResolvePlayerTriangle(state, out var originTriangle))
        {
            return;
        }

        var exemptions = activeTarget?.NativeEntranceExemptions;
        // Not when the vehicle detour has found no way round: see TryResolveAutomaticInput.
        var buggies = honourVehicleMasks && entityProvider is not null
            ? (activeTarget is { } selected ? ObstaclesOtherThan(selected) : entityProvider())
                .Where(WorldMapVehicleObstacles.IsParkedVehicle).ToArray()
            : Array.Empty<WorldMapEntitySnapshot>();
        var keys = Enum.GetValues<FieldNavigationInput>()
            .Where(candidate => candidate is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
            .Select(candidate => (Input: candidate, World: PredictNativeMovement(candidate, state.CameraFront)))
            .ToArray();

        double Remaining(WorldMapStateSnapshot at, int triangle)
        {
            if (activeTarget is { } destination && destination.ArrivalTriangleIds.Contains(triangle))
            {
                return double.NegativeInfinity;
            }

            return SlotPotential(at, route);
        }

        // Faces are resolved once per point: resolving is the expensive part of every step.
        var faces = new Dictionary<(int, int), int>();
        bool Resolve(int x, int z, out int face)
        {
            if (faces.TryGetValue((x, z), out face))
            {
                return face >= 0;
            }

            if (!planner.TryResolvePlayerTriangle(state with { X = x, Z = z }, out face))
            {
                face = -1;
            }

            faces[(x, z)] = face;
            return face >= 0;
        }

        // Still inside the face it was on, in plan: nothing to resolve. Overlapping levels are
        // resolved at the party's height by the planner; a frame this short stays on its level.
        bool InsideFace(int face, int x, int z)
        {
            var triangle = map.Triangles[face];
            var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
            var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
            if (Math.Abs(denominator) < 1e-6)
            {
                return false;
            }

            var wa = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
            var wb = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
            return wa > 1e-9 && wb > 1e-9 && 1d - wa - wb > 1e-9;
        }

        var hold = SampleHold();
        var nodes = new List<(int X, int Z, int Parent, FieldNavigationInput Key)> { (state.X, state.Z, -1, FieldNavigationInput.None) };
        var nodeFaces = new List<int> { originTriangle };
        var visited = new HashSet<(int, int)> { (state.X / 10, state.Z / 10) };
        var best = Remaining(state, originTriangle) - groundFrameStep;
        var bestNode = -1;
        var frontier = new List<int> { 0 };
        for (var depth = 0; depth < Math.Max(3, WalkingSlotSearchFrames / hold) && frontier.Count > 0 && nodes.Count < WalkingSlotSearchNodes; depth++)
        {
            var next = new List<int>();
            foreach (var index in frontier)
            {
                var node = nodes[index];
                foreach (var (key, world) in keys)
                {
                    if (nodes.Count >= WalkingSlotSearchNodes)
                    {
                        break;
                    }

                    // The key held for one host sample: as many native frames as the party
                    // has been moving per sample, each accepted only where the game would
                    // accept it, and stopping at the first it would refuse.
                    var (x, z) = (node.X, node.Z);
                    var face = nodeFaces[index];
                    var triangle = -1;
                    var (stepX, stepZ) = NativeWalkingFrame(key, state.CameraFront, groundFrameStep);
                    for (var frame = 0; frame < hold; frame++)
                    {
                        var nextX = x + stepX;
                        var nextZ = z + stepZ;
                        // Cheapest first. A frame that stays inside one face enters nothing new,
                        // so only a frame that changes face walks the centre line.
                        var nextTriangle = face;
                        if (!planner.HasWalkingFootprint(state, nextX, state.Y, nextZ) ||
                            (!InsideFace(face, nextX, nextZ) && !Resolve(nextX, nextZ, out nextTriangle)) ||
                            planner.IsUnwantedEntrance(nextTriangle, exemptions, originTriangle) ||
                            IsBridgeTerrain(map.Triangles[nextTriangle].TerrainId) ||
                            (nextTriangle != face &&
                             !planner.CanTraverseSegment(state with { X = x, Z = z }, new WorldMapRouteWaypoint(nextX, state.Y, nextZ), null, exemptions)) ||
                            (buggies.Length > 0 &&
                             WorldMapVehicleObstacles.BlocksSegment(
                                 buggies, state.PlayerModelId, x, z, nextX, nextZ, map.WrapWidth, map.WrapHeight)))
                        {
                            break;
                        }

                        (x, z, triangle, face) = (nextX, nextZ, nextTriangle, nextTriangle);
                        if (activeTarget is { } goal && goal.ArrivalTriangleIds.Contains(triangle))
                        {
                            break;
                        }
                    }

                    var at = state with { X = x, Z = z };
                    if (triangle < 0 || !visited.Add((x / 10, z / 10)))
                    {
                        continue;
                    }

                    nodes.Add((x, z, index, key));
                    nodeFaces.Add(triangle);
                    var remaining = Remaining(at, triangle);
                    if (remaining < best)
                    {
                        best = remaining;
                        bestNode = nodes.Count - 1;
                    }

                    next.Add(nodes.Count - 1);
                }

                if (bestNode >= 0 && double.IsNegativeInfinity(best))
                {
                    break;
                }
            }

            if (bestNode >= 0 && double.IsNegativeInfinity(best))
            {
                break;
            }

            frontier = next;
        }

        if (bestNode < 0)
        {
            return;
        }

        // The walk as corners: where its direction changes, and where it ends.
        var chain = new List<(int X, int Z, FieldNavigationInput Key)>();
        for (var index = bestNode; index > 0; index = nodes[index].Parent)
        {
            chain.Add((nodes[index].X, nodes[index].Z, nodes[index].Key));
        }

        chain.Reverse();
        for (var index = 0; index < chain.Count; index++)
        {
            if (index == chain.Count - 1 || chain[index + 1].Key != chain[index].Key)
            {
                slotPath.Add(new WorldMapRouteWaypoint(chain[index].X, state.Y, chain[index].Z));
            }
        }

        slotRoute = route;
        slotPlannedHold = hold;
        slotPathStart = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        slotLastObserved = slotPathStart;
        slotObservedStep = 0d;
        slotCorner = 0;
    }

    /// <summary>
    /// One native walking frame for a key, as FUN_0074EA48 builds it: 0x1E on an axis, and on
    /// a diagonal each axis as <c>(axis * 3) &gt;&gt; 2</c> (an arithmetic shift, so +22 and -23),
    /// turned by a Y rotation of the negative camera angle (006628DE) applied in 4096ths and
    /// shifted down by 12 (00662ECC). The Old Man's House slot is about 30 units wide, so the
    /// unit or two a rounded floating vector differs by decides whether the game accepts it.
    /// </summary>
    private static (int X, int Z) NativeWalkingFrame(FieldNavigationInput input, int cameraFront, double frameStep)
    {
        var stick = FieldNavigationMovementObserver.ToStickDirection(input);
        var step = (int)frameStep;
        var x = Math.Sign(stick.X) * step;
        var z = Math.Sign(stick.Y) * step;
        if (x != 0 && z != 0)
        {
            x = (x * 3) >> 2;
            z = (z * 3) >> 2;
        }

        var angle = cameraFront * Math.PI * 2d / 4096d;
        var cos = (int)Math.Round(Math.Cos(angle) * 4096d);
        var sin = (int)Math.Round(Math.Sin(angle) * 4096d);
        return ((cos * x - sin * z) >> 12, (sin * x + cos * z) >> 12);
    }

    internal static (double X, double Z) PredictNativeMovement(FieldNavigationInput input, int cameraFront)
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

    public void PauseForNativeControl()
    {
        ResetAutomaticLearning();
        autoWalkConvergence.Reset();
        offRouteSince = DateTime.MinValue;
        lastGuidanceAt = DateTime.MinValue;
        lastDiagnostic = "native world script or dialogue owns movement";
    }

    public void PauseForCombat(string diagnostic)
    {
        if (!beaconEnabled || combatPaused)
        {
            return;
        }

        combatPaused = true;
        ResetAutomaticLearning();
        autoWalkConvergence.Reset();
        progressSink?.Deactivate();
        lastDiagnostic = string.IsNullOrWhiteSpace(diagnostic)
            ? "world navigation paused for combat"
            : diagnostic;
    }

    public void Reset()
    {
        // The world map is being left (usually for a field). A glacier leg ends here; the
        // treasure it serves is the region navigator's and carries on in the next field.
        ResetRoute();
        glacierRows = null;
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
        if (glacierLegActive && GlacierRegion?.Objective is { } objective)
        {
            if (selected is not null &&
                string.Equals(selected.StableId, GreatGlacierRegionalNavigator.RowId(objective.Treasure), StringComparison.Ordinal))
            {
                // Browsing back to the treasure being walked to keeps its leg.
                return DescribeSelection(state);
            }

            // Anything else selected while navigating replaces the treasure route.
            GlacierRegion.Cancel("another world-map target was selected");
            glacierLegActive = false;
        }

        if (selected is not null && GlacierRegion is { } region &&
            GreatGlacierRegionalNavigator.TryParseRow(selected.StableId, out var treasure))
        {
            ResetRoute();
            region.Start(treasure, autoWalk: false);
            glacierPreparingAnnounced = false;
            return TryStartGlacierLeg(state, now, fromSelection: true) ?? DescribeSelection(state);
        }
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
        // A different destination is a new route; the same one (a replan) keeps where it leads.
        if (!string.Equals(activeTarget?.StableId, target.StableId, StringComparison.Ordinal))
        {
            throughTarget = null;
        }
        submarineManualDescent = false;
        submarineDepthRecoveryAnnounced = false;
        submarineEmeraldMissingSince = DateTime.MinValue;
        submarineUnsafeApproach = null;

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

        if (!UsesHighwindLanding(target, state) && target.HasArrived(state, playerTriangle))
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
        WorldMapHighwindLandingPlan? highwind = null;
        var highwindDiagnostic = string.Empty;
        WorldMapRoutePlan route;
        if (UsesHighwindLanding(target, state))
        {
            // In the air no town is entered: the way there is a grass spot where the game's
            // own landing sets the party down on ground that leads to it.
            if (!TryBuildHighwindRoute(state, target, out highwind, out route, out highwindDiagnostic))
            {
                if (TryStartThroughGateway(target, state, now, announceOn) is { } throughHighwind)
                {
                    return throughHighwind;
                }

                ResetRoute();
                lastDiagnostic = highwindDiagnostic;
                return new WorldMapNavigationOutput(
                    $"Route unavailable to {target.Label} by Highwind. " +
                    "No grass landing spot that leads there on foot was found. Navigation off.",
                    StopAutoWalk: true);
            }
        }
        else if (!planner.TryBuildRoute(state, target, out route))
        {
            var diagnostic = planner.LastDiagnostic;
            if (!IsDestination(target) || state.PlayerModelId != WorldMapBroncoLanding.BroncoModelId)
            {
                if (TryStartThroughGateway(target, state, now, announceOn) is { } throughOnFoot)
                {
                    return throughOnFoot;
                }

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
        highwindLanding = highwind;
        ResetHighwindApproach();
        landingSettleSamples = 0;
        landingClosestApproach = double.PositiveInfinity;
        landingStalledSamples = 0;
        landingOnRunIn = false;
        lastGuidanceAt = now;
        _ = UpdateProgressAndWaypoint(state);
        lastDiagnostic = highwind is not null
            ? highwindDiagnostic
            : landing is null
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

        // In the Highwind a town is previewed by its landing, not by flying over it.
        var byHighwind = UsesHighwindLanding(target, state);
        var byLanding = false;
        WorldMapRoutePlan preview = default!;
        if (byHighwind && !TryBuildHighwindRoute(state, target, out _, out preview, out _))
        {
            return new WorldMapNavigationOutput(
                ReachedThrough.TryGetValue(target.Label, out var throughWay)
                    ? $"{DisplayName(CurrentCategory)}, {target.Label}. Reached through {throughWay.Gateway}: {throughWay.Why}."
                    : $"{DisplayName(CurrentCategory)}, {target.Label}. No grass landing spot that leads there on foot.");
        }

        if ((byHighwind ||
             planner.TryBuildRoute(state, target, out preview) ||
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
                byHighwind
                    ? $"{DisplayName(CurrentCategory)}, {target.Label}. {HighwindRouteSummary}. {direction}."
                    : byLanding
                    ? $"{DisplayName(CurrentCategory)}, {target.Label}. {LandingRouteSummary}. {direction}."
                    : $"{DisplayName(CurrentCategory)}, {target.Label}. {direction}.");
        }

        return new WorldMapNavigationOutput(
            ReachedThrough.TryGetValue(target.Label, out var gatewayWay)
                ? $"{DisplayName(CurrentCategory)}, {target.Label}. Reached through {gatewayWay.Gateway}: {gatewayWay.Why}."
                : $"{DisplayName(CurrentCategory)}, {target.Label}. Route unavailable.");
    }

    private const string LandingRouteSummary = "By Tiny Bronco to a shore landing, then on foot";

    private string CreateGuidanceSpeech(
        WorldMapStateSnapshot state,
        bool includeTarget,
        bool includeProgress)
    {
        var target = activeTarget;
        var route = activeRoute;
        if (target is not null && state.PlayerModelId == 13 && state.WorldMapType == 2)
            return DescribeSubmarineGuidance(state, target);
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

        if (target is not null && highwindLanding is not null)
        {
            switch (highwindPhase)
            {
                case HighwindPhase.Ready:
                    return DescribeHighwindLanding(target);
                case HighwindPhase.Settling:
                    return $"Stopping at the landing spot for {target.Label}.";
            }
        }

        if (target is null || route is null || route.Waypoints.Count == 0)
        {
            return includeTarget && target is not null ? target.Label : "nearby";
        }

        var direction = ResolveGuidanceRun(state, route).Speech;
        var prefix = includeTarget
            ? highwindLanding is not null ? $"{target.Label}. {HighwindRouteSummary}. "
            : landingPlan is null ? $"{target.Label}. " : $"{target.Label}. {LandingRouteSummary}. "
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
            ["Wutai"] = new HashSet<int> { 0, 1, 2 },
            // wm0.ev handler A584, table slot 116, mesh (24,16) script 7, entry IP 2C12: push
            // special 8 and compare it with 0, 1 and 2 (2C13..2C23), skip to the return at
            // 2C2C unless one matched (2C24), otherwise EnterField 9 entry 0 (2C26..2C2B).
            // The same bytes in both installed archives (wm0.ev payload SHA256 a020dace...).
            // The 2026-09-26 Buggy stood on this trigger, nothing loaded, and navigation
            // still said it had arrived.
            ["Old Man's House (Mythril)"] = new HashSet<int> { 0, 1, 2 }
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
        !UsesHighwindLanding(target, state) &&
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

    /// <summary>
    /// The vehicle detour for this route, except that once it has found no way round on this
    /// route the masks stay a prediction until the route is planned again. A way round found
    /// on one sample and lost on the next otherwise turns the aim back and forth and clears,
    /// every time, the slot walk the one-row door of Old Man's House needs: the 2026-09-27
    /// replays with the saved Buggy stalled at the door that way from starts that entered
    /// with no car at all.
    /// </summary>
    private WorldMapVehicleDetourPlanner.DetourOutcome ResolveVehicleDetourOnRoute(
        WorldMapStateSnapshot state,
        WorldMapRoutePlan route,
        WorldMapRouteWaypoint goal,
        out WorldMapRouteWaypoint aim)
    {
        var outcome = ResolveVehicleDetour(state, goal, out aim);
        if (outcome == WorldMapVehicleDetourPlanner.DetourOutcome.Blocked)
        {
            vehicleMasksPredictedOnRoute = route;
        }

        if (!ReferenceEquals(vehicleMasksPredictedOnRoute, route))
        {
            return outcome;
        }

        aim = goal;
        return WorldMapVehicleDetourPlanner.DetourOutcome.Blocked;
    }

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
        WorldMapRoutePlanner.UsesGroundFootprint(playerModelId);

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
            : 2 * GroundFrameStep(state.PlayerModelId));
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
            : 4 * GroundFrameStep(state.PlayerModelId);
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
        else if (WorldMapRoutePlanner.UsesGroundFootprint(state.PlayerModelId) &&
                 planner.HasWalkingFootprint(state, state.X, state.Y, state.Z))
        {
            fitsAt = (x, y, z) => planner.HasWalkingFootprint(state, x, y, z);
            frameStep = GroundFrameStep(state.PlayerModelId);
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

        // The Highwind flies at its own height: FUN_0074EA48's strafe moves it in X and Z
        // only, and a route over the ground has the ground's height. Measured in three
        // dimensions a waypoint below the ship is never reached (the replay held at 1281
        // over a waypoint at 0, 50 units away, turning back and forth), so flight is
        // measured across the map alone. The submarine likewise travels independently
        // of the seabed waypoint height; its depth has a separate native input policy.
        var flat = state.PlayerModelId is WorldMapHighwindLanding.HighwindModelId or WorldMapSubmarineSteering.PlayerModelId;
        var points = new List<(double X, double Y, double Z)>(waypoints.Count + 1)
        {
            (start.X, flat ? 0d : start.Y, start.Z)
        };
        foreach (var waypoint in waypoints)
        {
            var prior = points[^1];
            points.Add((
                prior.X + WorldMapTargetCatalog.WrappedDelta((int)Math.Round(prior.X), waypoint.X, wrapWidth),
                flat ? 0d : waypoint.Y,
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
            var player = (X: playerX, Y: flat ? 0d : state.Y, Z: playerZ);
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

    private void MoveCategory(int delta, WorldMapStateSnapshot state)
    {
        var count = WorldMapTargetCatalog.CategoryOrder.Count + (ObjectsAvailable(state) ? 1 : 0);
        categoryIndex = PositiveModulo(categoryIndex + delta, count);
    }

    /// <summary>
    /// Starts the next snowfield leg of the glacier objective from where the party is: the
    /// snowfield exit whose walk plus the best route on is least. When the route table is
    /// still being prepared it waits (and says so once); when no exit leads toward the
    /// treasure the objective ends with the reason.
    /// </summary>
    private WorldMapNavigationOutput? TryStartGlacierLeg(WorldMapStateSnapshot state, DateTime now, bool fromSelection)
    {
        if (GlacierRegion is not { Objective: { } objective } region)
        {
            return null;
        }

        var goal = region.CurrentGoal(objective.Treasure, out var readable);
        if (!readable)
        {
            return null;
        }

        if (goal is null)
        {
            region.Cancel("already picked up");
            return new WorldMapNavigationOutput($"{objective.Label} has already been collected. Navigation off.", StopAutoWalk: true);
        }

        objective.Goal = goal;
        var locations = targetProvider(state, WorldMapNavigationCategory.Locations) ?? Array.Empty<WorldMapNavigationTarget>();

        // Standing on a snowfield exit's own trigger: the game is about to enter its field
        // (the terrain script runs its MAPJUMP a few samples later). Nothing is started again
        // and nothing is walked; the treasure is kept for the field it leads into.
        if (planner.TryResolvePlayerTriangle(state, out var standing) &&
            locations.FirstOrDefault(location =>
                GreatGlacierRegionGraph.SnowfieldLocationId(location) is not null &&
                location.HasArrived(state, standing)) is { } entrance)
        {
            lastDiagnostic = $"glacier objective {objective.Treasure}: on the native trigger of {entrance.Label}, holding for the game's own entry";
            if (glacierHoldingAtEntrance && !fromSelection)
            {
                return null;
            }

            glacierHoldingAtEntrance = true;
            return fromSelection
                ? new WorldMapNavigationOutput($"Navigation on. {objective.Label}: at {entrance.Label}; the game takes the party in from here.")
                : null;
        }

        glacierHoldingAtEntrance = false;
        var leg = region.PlanWorldLeg(goal.Value, locations, location => LiveWorldLength(state, location), out var failure);
        if (leg is null)
        {
            if (region.Graph is null && region.BuildFailure is null)
            {
                if (glacierPreparingSince == default)
                {
                    glacierPreparingSince = now;
                }

                if (now - glacierPreparingSince >= GreatGlacierRegionalNavigator.PreparationLimit)
                {
                    region.Cancel("the route table was not ready in time");
                    return new WorldMapNavigationOutput(
                        $"Route unavailable to {objective.Label}: the Great Glacier routes could not be prepared. Navigation off.",
                        StopAutoWalk: true);
                }

                if (glacierPreparingAnnounced)
                {
                    return null;
                }

                glacierPreparingAnnounced = true;
                return new WorldMapNavigationOutput($"{objective.Label}. Preparing the Great Glacier routes; navigation starts when they are ready.");
            }

            region.Cancel($"no snowfield leg: {failure}");
            lastDiagnostic = $"glacier objective {objective.Treasure}: {failure}";
            return new WorldMapNavigationOutput($"Route unavailable to {objective.Label}: {failure}. Navigation off.", StopAutoWalk: true);
        }

        var started = StartNavigation(leg.Target, state, now, announceOn: false);
        if (!beaconEnabled)
        {
            region.Cancel($"snowfield leg {leg.Target.StableId} could not start: {lastDiagnostic}");
            return started is { } refused
                ? refused with { Speech = $"Route unavailable to {objective.Label}. {refused.Speech}", StopAutoWalk = true }
                : new WorldMapNavigationOutput($"Route unavailable to {objective.Label}. Navigation off.", StopAutoWalk: true);
        }

        glacierLegActive = true;
        glacierLegFieldEntries = region.FieldEntries;
        glacierPreparingSince = default;
        lastDiagnostic = $"glacier objective {objective.Treasure} ({goal.Value.Stage}): {leg.Diagnostic}; {lastDiagnostic}";
        var lead = fromSelection ? "Navigation on. " : string.Empty;
        return new WorldMapNavigationOutput(
            $"{lead}{objective.Label}: first to {leg.Target.Label}. {started?.Speech}".TrimEnd(),
            StartAutoWalk: objective.AutoWalk && !fromSelection);
    }

    private double? LiveWorldLength(WorldMapStateSnapshot state, WorldMapNavigationTarget target) =>
        planner.TryBuildRoute(state, target, out var route) ? route.TotalDistance : null;

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
        if (CurrentCategory == WorldMapNavigationCategory.Objects)
        {
            return ReadGlacierRows(state);
        }

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

    /// <summary>
    /// The snowfield's treasure rows. Planning them walks the snowfield once per exit, so the
    /// list is kept while the party stays on the same triangle, for at most two seconds.
    /// </summary>
    private IReadOnlyList<WorldMapNavigationTarget> ReadGlacierRows(WorldMapStateSnapshot state)
    {
        if (GlacierRegion is not { } region || !ObjectsAvailable(state))
        {
            return Array.Empty<WorldMapNavigationTarget>();
        }

        var triangle = planner.TryResolvePlayerTriangle(state, out var resolved) ? resolved : -1;
        var now = DateTime.UtcNow;
        var signature = region.CollectionSignature();
        if (glacierRows is { } cached && glacierRowsTriangle == triangle && glacierRowsSignature == signature &&
            now - glacierRowsAt < TimeSpan.FromSeconds(2))
        {
            return cached;
        }

        var locations = targetProvider(state, WorldMapNavigationCategory.Locations) ?? Array.Empty<WorldMapNavigationTarget>();
        glacierRows = region.ReadWorldRows(state, locations, location => LiveWorldLength(state, location));
        glacierRowsTriangle = triangle;
        glacierRowsSignature = signature;
        glacierRowsAt = now;
        return glacierRows;
    }

    private static bool IsDestination(WorldMapNavigationTarget target) =>
        target.Kind is WorldMapTargetKind.Location or WorldMapTargetKind.Story;

    /// <summary>
    /// Destinations whose world-map ground no route can reach from outside - no landing, no
    /// walk - and the destination whose own field is the way to them.
    ///
    /// <para>The Great Glacier's trigger (world location 48) and Icicle Inn's north side (47)
    /// stand in one pocket of snow north of the town: no grass in it to land on, and closed in
    /// by Icicle Inn's own north-side trigger, so no walk enters it either. field.tbl sends both
    /// 27 (the south side) and 47 into field 654, the town. The town's way to the Great Glacier
    /// is the snowboard run: snow/playgam's Move (e11.s2), under Bank[3][9] == 0 and
    /// Bank[1][130] bits 1 and 6, runs MINIGAME type 2 into field 658, whose line03 MAPJUMPs to
    /// the world map at id 48 - in that pocket. So for either, the way is Icicle Inn's south
    /// side, where the Highwind can land; the run itself is offered in the town's Exits
    /// (story-regions/IcicleInn.ps1). WorldMapHighwindNavigationTests reads all of this back
    /// out of the installed game. Nothing is walked or flown past the gateway.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Gateway, string Why, string Onwards)> ReachedThrough =
        new Dictionary<string, (string Gateway, string Why, string Onwards)>(StringComparer.Ordinal)
        {
            ["Icicle Inn (North Side)"] = ("Icicle Inn (South Side)",
                "both sides of Icicle Inn enter the same town",
                "you are entering the same town"),
            ["Great Glacier"] = ("Icicle Inn (South Side)",
                "the way there is the snowboard run from the top of the slope in the village",
                "snowboard down from the top of the slope in the village, in Exits")
        };

    // The destination the active route leads towards through its gateway (ReachedThrough).
    private WorldMapNavigationTarget? throughTarget;

    /// <summary>
    /// When <paramref name="target"/> cannot be routed to and <see cref="ReachedThrough"/> names
    /// its gateway: navigation to the gateway instead, said as such. Null when there is none, or
    /// the gateway cannot be routed to either.
    /// </summary>
    private WorldMapNavigationOutput? TryStartThroughGateway(
        WorldMapNavigationTarget target,
        WorldMapStateSnapshot state,
        DateTime now,
        bool announceOn)
    {
        if (!ReachedThrough.TryGetValue(target.Label, out var way) ||
            (targetProvider(state, WorldMapNavigationCategory.Locations) ?? Array.Empty<WorldMapNavigationTarget>())
                .FirstOrDefault(location => string.Equals(location.Label, way.Gateway, StringComparison.Ordinal))
                is not { } gateway ||
            string.Equals(gateway.StableId, target.StableId, StringComparison.Ordinal))
        {
            return null;
        }

        var started = StartNavigation(gateway, state, now, announceOn: false);
        if (started is not { } output || !beaconEnabled ||
            !string.Equals(activeTarget?.StableId, gateway.StableId, StringComparison.Ordinal))
        {
            // Already there, or no route to the gateway either: say that, with the way on.
            return started is { } said && said.Speech?.StartsWith("Arrived at", StringComparison.Ordinal) == true
                ? said with { Speech = $"{said.Speech.Replace(" Navigation off.", string.Empty)} For {target.Label}, {way.Onwards}. Navigation off." }
                : null;
        }

        throughTarget = target;
        lastDiagnostic = $"{target.Label} is reached through {gateway.Label}; {lastDiagnostic}";
        return output with
        {
            Speech = $"{(announceOn ? "Navigation on. " : string.Empty)}{target.Label} is reached through {gateway.Label}: {way.Why}. {output.Speech}"
        };
    }

    /// <summary>
    /// A destination the Highwind cannot arrive at in the air, so is flown to a landing for.
    /// Towns are entered on foot or by a vehicle on the ground; flying over one enters
    /// nothing (the 2026-09-29 flight over Mideel). The Highwind's own stops - Midgar at
    /// 1596, and the native crater targets with their own arrival test - keep flying there.
    /// </summary>
    private static bool UsesHighwindLanding(WorldMapNavigationTarget target, WorldMapStateSnapshot state) =>
        state.PlayerModelId == WorldMapHighwindLanding.HighwindModelId &&
        IsDestination(target) &&
        !target.ReachedInFlight &&
        target.NativeStoryArrival is null;

    /// <summary>
    /// The ship's route: flown straight over whatever lies between, to the grass spot
    /// <see cref="WorldMapHighwindLanding"/> chose, ending exactly on it.
    /// </summary>
    private bool TryBuildHighwindRoute(
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target,
        out WorldMapHighwindLandingPlan? highwind,
        out WorldMapRoutePlan route,
        out string diagnostic)
    {
        highwind = null;
        route = default!;
        if (!WorldMapHighwindLanding.TryPlan(planner, map, state, target, out var plan, out diagnostic))
        {
            return false;
        }

        var spotTriangle = map.Triangles[plan.ShipTriangleId];
        var spot = new WorldMapNavigationTarget(
            target.Category,
            WorldMapTargetKind.TerrainArea,
            $"{target.Label} landing",
            plan.ShipX,
            plan.ShipY,
            plan.ShipZ,
            spotTriangle.Id,
            spotTriangle.RegionId,
            $"{target.StableId}:highwind-landing",
            new HashSet<int> { spotTriangle.Id });
        if (!planner.TryBuildRoute(state, spot, out var flight))
        {
            diagnostic = $"{diagnostic}; the flight to the spot could not be planned: {planner.LastDiagnostic}";
            return false;
        }

        // One wrapped leg straight to the spot. Flight crosses every terrain
        // (WorldMapTerrainPassability, model 3), so the ground triangles between add only
        // corners the ship would have to be turned through; the triangle path is kept for
        // the off-route test.
        var end = new WorldMapRouteWaypoint(plan.ShipX, plan.ShipY, plan.ShipZ);
        var legX = (double)WorldMapTargetCatalog.WrappedDelta(state.X, end.X, map.WrapWidth);
        var legZ = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, end.Z, map.WrapHeight);
        route = flight with
        {
            TargetId = target.StableId,
            Waypoints = [end],
            TotalDistance = Math.Sqrt(legX * legX + legZ * legZ)
        };
        highwind = plan;
        return true;
    }

    private void ResetHighwindApproach()
    {
        highwindPhase = HighwindPhase.Approaching;
        highwindTightLookout = false;
        highwindBraking = false;
        highwindBrakeUntil = DateTime.MinValue;
        highwindStillSince = DateTime.MinValue;
        highwindHasLastPosition = false;
        highwindSampleTravel = 0d;
        highwindQuietUntilMoved = false;
    }

    /// <summary>
    /// Brings the ship to rest near the spot, then asks once for the landing - only if the game
    /// would honour it from where the ship really stopped and as it is really turned.
    ///
    /// <para>Near the spot (the farther of <see cref="HighwindLandingLookout"/> and most of the
    /// last sample's travel, so a host sample spanning several frames cannot carry the ship
    /// through it) the approach ends. When auto walk was driving, the flight action is then
    /// held alone for <see cref="HighwindBrakeTime"/>: FUN_0074EA48 gives the strafe no X or Z
    /// without a direction while its eased thrust (DAT_00DE6A18) decays, where letting go of
    /// everything would coast on in normal flight. Then nothing is held, and once the ship has
    /// stayed put for <see cref="HighwindStillTime"/> the ground is judged. A player flying it
    /// themselves is simply waited for.</para>
    ///
    /// <para>A stop that does not fit is said, and the way in is taken up again in short hops.
    /// Leaving the fitting ground takes the prompt back.</para>
    /// </summary>
    private WorldMapNavigationOutput? ObserveHighwindLanding(
        WorldMapHighwindLandingPlan plan,
        WorldMapNavigationTarget target,
        WorldMapStateSnapshot state,
        DateTime now,
        bool driven)
    {
        var dx = (double)WorldMapTargetCatalog.WrappedDelta(state.X, plan.ShipX, map.WrapWidth);
        var dz = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, plan.ShipZ, map.WrapHeight);
        var fromSpot = Math.Sqrt(dx * dx + dz * dz);
        var travel = 0d;
        if (highwindHasLastPosition)
        {
            var mx = (double)WorldMapTargetCatalog.WrappedDelta(highwindLastPosition.X, state.X, map.WrapWidth);
            var mz = (double)WorldMapTargetCatalog.WrappedDelta(highwindLastPosition.Z, state.Z, map.WrapHeight);
            travel = Math.Sqrt(mx * mx + mz * mz);
        }

        highwindLastPosition = new(state.X, state.Y, state.Z);
        highwindHasLastPosition = true;
        switch (highwindPhase)
        {
            case HighwindPhase.Approaching:
            {
                if (travel > 0d)
                {
                    highwindSampleTravel = travel;
                }

                if (travel > 4d)
                {
                    highwindQuietUntilMoved = false;
                }

                var lookout = Math.Max(
                    highwindTightLookout ? HighwindTightLookout : HighwindLandingLookout,
                    0.6d * highwindSampleTravel);
                // A player flying it themselves stops where they stop: near enough, and once
                // it has stopped, that is where the landing is judged - once per stop.
                var pilotStopped = !driven && !highwindQuietUntilMoved && travel <= 4d && fromSpot <= HighwindPilotLookout;
                if (fromSpot > lookout && !pilotStopped)
                {
                    return null;
                }

                highwindPhase = HighwindPhase.Settling;
                highwindBraking = driven;
                highwindBrakeUntil = now + HighwindBrakeTime;
                highwindStillSince = DateTime.MinValue;
                lastDiagnostic = $"stopping {fromSpot:0} from the landing spot for {target.Label}" +
                                 (driven ? ", holding the flight action alone to brake" : string.Empty);
                return null;
            }
            case HighwindPhase.Settling:
                if (highwindBraking)
                {
                    if (now < highwindBrakeUntil)
                    {
                        return null;
                    }

                    // Let go of the flight action; stillness is judged from here on.
                    highwindBraking = false;
                    highwindStillSince = DateTime.MinValue;
                    return null;
                }

                if (travel > 4d)
                {
                    highwindStillSince = DateTime.MinValue;
                    if (!driven && fromSpot > HighwindPilotLookout)
                    {
                        // Flown away again by hand: back to the way in, quietly.
                        highwindPhase = HighwindPhase.Approaching;
                    }

                    return null;
                }

                if (highwindStillSince == DateTime.MinValue)
                {
                    highwindStillSince = now;
                    return null;
                }

                if (now - highwindStillSince < HighwindStillTime)
                {
                    return null;
                }

                if (WorldMapHighwindLanding.IsReadyToLand(map, planner, state, plan.DestinationFootComponents))
                {
                    highwindPhase = HighwindPhase.Ready;
                    lastDiagnostic = $"at rest over grass to land for {target.Label} at {state.X},{state.Z}, " +
                                     $"{fromSpot:0} from the spot, rotation {state.ModelRotation}{state.SlideRotation:+0;-0;+0}";
                    return new WorldMapNavigationOutput(DescribeHighwindLanding(target));
                }

                highwindPhase = HighwindPhase.Approaching;
                highwindTightLookout = true;
                highwindSampleTravel = 0d;
                highwindQuietUntilMoved = true;
                lastDiagnostic = $"at rest at {state.X},{state.Z}, {fromSpot:0} from the landing spot for {target.Label}, " +
                                 $"not over ground that lands there (terrain {state.TerrainId})";
                return new WorldMapNavigationOutput(
                    $"Not over the landing ground for {target.Label} here. Navigation continues to the landing spot.");
            default:
                if (WorldMapHighwindLanding.IsReadyToLand(map, planner, state, plan.DestinationFootComponents))
                {
                    return null;
                }

                highwindPhase = HighwindPhase.Approaching;
                highwindTightLookout = true;
                highwindSampleTravel = 0d;
                lastDiagnostic = $"no longer over the landing ground for {target.Label} at {state.X},{state.Z}";
                return new WorldMapNavigationOutput($"No longer over the landing spot for {target.Label}.");
        }
    }
    private static string DescribeHighwindLanding(WorldMapNavigationTarget target) =>
        $"{target.Label} landing spot. Over grass: press Cancel to land, then continue on foot. Navigation stays on.";

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
        throughTarget = null;
        if (deactivateProgress)
        {
            progressSink?.Deactivate();
        }

        glacierLegActive = false;
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
        highwindLanding = null;
        submarineManualDescent = false;
        submarineDepthRecoveryAnnounced = false;
        submarineEmeraldMissingSince = DateTime.MinValue;
        submarineUnsafeApproach = null;
        ResetHighwindApproach();
        AutomaticInputHoldsFlightAction = false;
        AutomaticInputIsWorldSubmarine = false;
        AutomaticInputHoldsSubmarineThrust = false;
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
        vehicleMasksPredictedOnRoute = null;
        ResetAutomaticLearning();
        paceLastSignature = string.Empty;
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
