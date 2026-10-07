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
            ResetAutoWalkConvergence();
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
            IsAboveSubmarineDescent(state, target))
        {
            ResetAutoWalkConvergence();
            var first = !submarineManualDescent;
            submarineManualDescent = true;
            AutomaticInputHoldsSubmarineThrust = false;
            lastDiagnostic = $"above {target.Label}; native arrival requires manual descent";
            return new(first ? DescribeSubmarineDescent(target) : null, StopAutoWalk: true);
        }
        submarineManualDescent = false;
        if (automaticWalkActive && state.Y < WorldMapSubmarineSteering.TravelDepth)
        {
            ResetAutoWalkConvergence();
            var first = !submarineDepthRecoveryAnnounced;
            submarineDepthRecoveryAnnounced = true;
            return new(first ? "Rising to travel depth before continuing. This keeps the submarine clear of Emerald Weapon." : null);
        }
        submarineDepthRecoveryAnnounced = false;
        return null;
    }

    /// <summary>
    /// Where the player's own straight descent reaches the destination: within 128 of its
    /// fixed point, or, for the Key, over one of its own script-3 floor faces. C_00765F61
    /// fires the Key's mesh handler there once the sub is within 500 of that seabed, so the
    /// native trigger area is itself a descent point even where a wall keeps the full
    /// thrust lease from closing on the model's point.
    /// </summary>
    private bool IsAboveSubmarineDescent(WorldMapStateSnapshot state, WorldMapNavigationTarget target) =>
        UnderwaterHorizontalDistance(state, target) <= 128 ||
        target.NativeUnderwaterArrival?.ModelId == 26 &&
        WorldMapBroncoLanding.TryFindSurface(map, state.X, state.Z, out var floor) &&
        floor.TerrainScriptId == 3 && target.NativeTriggerTriangleIds.Contains(floor.Id);

    private static string DescribeSubmarineDescent(WorldMapNavigationTarget target) =>
        $"Above {target.Label}. Auto walk stopped. Hold Up to descend toward it. " +
        "Navigation remains on. Emerald Weapon may be nearby at this depth.";

    private string DescribeSubmarineGuidance(WorldMapStateSnapshot state, WorldMapNavigationTarget target)
    {
        if (submarineManualDescent || (target.NativeUnderwaterArrival?.ModelId is 17 or 26 or 28 &&
            IsAboveSubmarineDescent(state, target))) return DescribeSubmarineDescent(target);
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
        ObserveSubmarineCadence(state);
        submarinePollWork = 0;
        LastSubmarineCommandWasPlanning = false;
        bool resolved;
        try
        {
            resolved = ResolveSubmarineCommand(state, out input);
        }
        finally
        {
            LastSubmarinePollWork = submarinePollWork;
            WorstSubmarinePollWork = Math.Max(WorstSubmarinePollWork, submarinePollWork);
        }
        if (!resolved) return false;
        submarineLastCamera = state.CameraFront;
        submarineLastX = state.X;
        submarineLastZ = state.Z;
        submarineLastTurned = input is FieldNavigationInput.Left or FieldNavigationInput.Right;
        submarineLastThrust = AutomaticInputHoldsSubmarineThrust;
        return true;
    }

    // The command given at the previous scan, to measure how many native frames it covered.
    private int submarineLastCamera = -1;
    private int submarineLastX;
    private int submarineLastZ;
    private bool submarineLastTurned;
    private bool submarineLastThrust;
    private readonly Queue<int> submarineScanFrames = new();

    /// <summary>
    /// How many native frames the last held command lasted, read from the sub itself: a
    /// held turn moves the camera 8 x multiplier a frame and held thrust moves it
    /// 30 x multiplier (FUN_0074EA48). The host's scan interval (50 ms by default) and its
    /// timer granularity decide this, so it is measured rather than assumed.
    /// </summary>
    private void ObserveSubmarineCadence(WorldMapStateSnapshot state)
    {
        if (submarineLastCamera >= 0)
        {
            var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
            var frames = submarineLastTurned
                ? (int)Math.Round(Math.Abs(SignedTurn(submarineLastCamera, state.CameraFront)) / (8d * multiplier))
                : submarineLastThrust
                    ? (int)Math.Round(SubmarineDistance(state,
                        new WorldMapRouteWaypoint(submarineLastX, state.Y, submarineLastZ)) / (30d * multiplier))
                    : 0;
            if (frames > 0)
            {
                submarineScanFrames.Enqueue(frames);
                while (submarineScanFrames.Count > 16) submarineScanFrames.Dequeue();
            }
        }

        submarineLastCamera = -1;
    }

    /// <summary>
    /// The most native frames one scan's command can cover: the larger of the largest count
    /// seen at least twice among recent scans and the latest scan itself, never under 2; until
    /// anything is known, the 100 ms host sample (ceil(6 / multiplier)). A host that has just
    /// slowed down counts at once, so plans made for shorter scans stop being trusted.
    /// </summary>
    private int SubmarineScanFrames(int multiplier)
    {
        if (submarineScanFrames.Count == 0) return Math.Max(2, SubmarineSampleFrames(multiplier));
        var repeated = submarineScanFrames.GroupBy(frames => frames)
            .Where(group => group.Count() >= 2).Select(group => group.Key).DefaultIfEmpty(0).Max();
        return Math.Max(2, Math.Max(repeated, submarineScanFrames.Last()));
    }

    private bool ResolveSubmarineCommand(WorldMapStateSnapshot state, out FieldNavigationInput input)
    {
        input = FieldNavigationInput.None;
        if (!beaconEnabled || combatPaused || !IsUsable(state) ||
            activeRoute is not { Waypoints.Count: > 0 } route || activeTarget is null || submarineManualDescent ||
            submarineUnsafeApproach is not null)
        {
            // Any suspension discards a plan in progress; it is prepared again, from a
            // fresh neutral pose, when automatic input resumes.
            EndSubmarineTerminal();
            return false;
        }
        var emerald = activeTarget.NativeUnderwaterArrival?.ModelId == 30;
        if (emerald && !HasFreshEmeraldSighting(activeTarget)) return false;
        waypointIndex = ResolveSubmarineWaypoint(state, route, waypointIndex);
        var aim = route.Waypoints[waypointIndex];
        int? finalHeading = null;
        var finalThrust = false;
        var neutral = false;
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var scanFrames = SubmarineScanFrames(multiplier);
        if (!emerald && waypointIndex == route.Waypoints.Count - 1)
            aim = ResolveSubmarineFinalAim(state, route, aim, activeTarget.NativeEntranceExemptions, scanFrames,
                out finalHeading, out finalThrust, out neutral);
        else
            EndSubmarineTerminal();
        var depth = emerald ? WorldMapSubmarineSteering.EmeraldDepth : WorldMapSubmarineSteering.TravelDepth;
        if (neutral)
        {
            // Preparing or searching: no turn and no thrust, so the host releases whatever it
            // held before any search work. Only a depth correction, which moves nothing
            // horizontally, may still be given.
            if (!WorldMapSubmarineSteering.TryResolve(state, aim, out input, out _, depth) ||
                input is not (FieldNavigationInput.Up or FieldNavigationInput.Down))
                input = FieldNavigationInput.None;
            AutomaticInputHoldsSubmarineThrust = false;
            LastSubmarineCommandWasPlanning = true;
            return true;
        }
        if (!WorldMapSubmarineSteering.TryResolve(state, aim, out input, out var thrust, depth))
        {
            // Right over the point there is no steering toward it, but a final or
            // maneuver heading still says what to do; depth is then already right.
            if (finalHeading is null)
            {
                lastDiagnostic = "world submarine has no checked native steering command";
                return false;
            }
            input = FieldNavigationInput.None;
            thrust = false;
        }

        // Depth first, in place: native Up/Down change only Y underwater.
        var depthCommand = input is FieldNavigationInput.Up or FieldNavigationInput.Down;
        var exempt = activeTarget.NativeEntranceExemptions;
        var leaseEnd = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        if (!depthCommand)
        {
            // FUN_0074EA48 moves underwater only along the camera while Confirm is held, and
            // a held key lasts the whole native input lease if the host stalls. Thrust is
            // therefore given only when that entire lease, frame by frame, keeps the native
            // five-point body (FUN_00751EFC, +-200) on submarine floor. Otherwise the sub
            // turns in place, which moves nothing underwater, toward a heading whose lease
            // fits and still closes on the route.
            var desired = HeadingTo(state, aim);
            var leaseClear = TryForecastSubmarineLease(state, state.CameraFront, exempt, out leaseEnd);
            var toDesired = SignedTurn(state.CameraFront, desired);
            if (finalHeading is { } final)
            {
                // The final approach, or one leg of a positioning maneuver: turn in place to
                // the heading, then thrust along it while the leg lasts and the camera's own
                // full lease fits. A finished leg holds still for the next one.
                var toFinal = SignedTurn(state.CameraFront, final);
                thrust = finalThrust && leaseClear && toFinal == 0;
                input = toFinal == 0 ? FieldNavigationInput.None
                    : toFinal > 0 ? FieldNavigationInput.Right : FieldNavigationInput.Left;
            }
            else if (!thrust || !leaseClear)
            {
                if (!TryChooseSubmarineHeading(state, desired, exempt, out var chosen))
                {
                    lastDiagnostic = "no native submarine heading toward the route keeps the full input lease on submarine floor";
                    input = FieldNavigationInput.None;
                    AutomaticInputHoldsSubmarineThrust = false;
                    return false;
                }

                // One scan can turn the camera past the chosen heading by up to a scan of
                // native turning; the camera's own lease, already checked, still decides.
                var toChosen = SignedTurn(state.CameraFront, chosen);
                var overshoot = Math.Max(SubmarineHeadingWindow, 8 * multiplier * scanFrames);
                thrust = leaseClear && (Math.Abs(toChosen) <= overshoot ||
                                        thrust && Math.Abs(toDesired) <= SubmarineThrustTolerance);
                input = thrust ? FieldNavigationInput.None
                    : toChosen > 0 ? FieldNavigationInput.Right : FieldNavigationInput.Left;
            }
        }

        string? blockedSite = null;
        if (emerald)
        {
            // Descending on the Key's script triangle fires its handler even without
            // horizontal movement; the ordinary route's escape exemption is insufficient.
            if (planner.TryResolvePlayerTriangle(state, out var currentTriangle) &&
                planner.EntranceTriangleIds.Contains(currentTriangle))
                blockedSite = "Key of the Ancients";
            else if (WorldMapUnderwaterEntryGuard.TryFindBlockedSite(state,
                thrust ? leaseEnd : new WorldMapRouteWaypoint(state.X, state.Y, state.Z),
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

        AutomaticInputHoldsSubmarineThrust = thrust;
        return true;
    }

    /// <summary>Thrust while the camera is this close to the route's heading and the lease fits.</summary>
    private const int SubmarineThrustTolerance = 64;

    /// <summary>
    /// A chosen heading must stay clear this far either side: one host sample turns the sub
    /// 3 frames x 8 x multiplier, so the window is never stepped over.
    /// </summary>
    private const int SubmarineHeadingWindow = 24;

    /// <summary>How near a route waypoint the sub must come before aiming at the next one.</summary>
    private const double SubmarineWaypointReach = 240d;

    private const int SubmarineHeadingStep = 32;
    private const int SubmarineHeadingSearch = 64;

    private int HeadingTo(WorldMapStateSnapshot state, WorldMapRouteWaypoint aim)
    {
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, aim.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, aim.Z, map.WrapHeight);
        var heading = (int)Math.Round(Math.Atan2(dx, -dz) * 4096d / (Math.PI * 2d));
        return (heading % 4096 + 4096) % 4096;
    }

    private static int SignedTurn(int from, int to) => ((to - from) % 4096 + 6144) % 4096 - 2048;

    /// <summary>
    /// The nearest heading to the desired one, all the way round (backing out of a pocket
    /// whose only clear leases point away from the route), whose full native lease
    /// fits at it and at the window either side.
    /// </summary>
    private bool TryChooseSubmarineHeading(WorldMapStateSnapshot state, int desired,
        IReadOnlySet<int>? exempt, out int chosen)
    {
        for (var k = 0; k <= SubmarineHeadingSearch; k++)
        {
            for (var side = 0; side < (k == 0 ? 1 : 2); side++)
            {
                var offset = side == 0 ? k * SubmarineHeadingStep : -k * SubmarineHeadingStep;
                var heading = ((desired + offset) % 4096 + 4096) % 4096;
                if (TryForecastSubmarineLease(state, heading, exempt, out _) &&
                    TryForecastSubmarineLease(state, (heading + SubmarineHeadingWindow) % 4096, exempt, out _) &&
                    TryForecastSubmarineLease(state, (heading - SubmarineHeadingWindow + 4096) % 4096, exempt, out _))
                {
                    chosen = heading;
                    return true;
                }
            }
        }

        chosen = desired;
        return false;
    }

    /// <summary>
    /// The native underwater forward frame: FUN_0074EA48 rotates (0, 0, -30 * multiplier) by
    /// RotMatrixXYZ(0, -front, 0) and RotTrans shifts the product down by 12.
    /// </summary>
    internal static (int X, int Z) SubmarineForwardFrame(int cameraFront, int multiplier)
    {
        var radians = cameraFront * Math.PI * 2d / 4096d;
        var sine = (int)Math.Round(Math.Sin(radians) * 4096d);
        var cosine = (int)Math.Round(Math.Cos(radians) * 4096d);
        return ((sine * multiplier * 30) >> 12, (-cosine * multiplier * 30) >> 12);
    }

    /// <summary>
    /// Every native frame of the full input lease at this heading, each one a segment the
    /// planner's submarine body test accepts (the +-200 five-point footprint every 30 units
    /// on the native lowest floor, and no unselected entrance), then 96 units of margin.
    /// </summary>
    private bool TryForecastSubmarineLease(WorldMapStateSnapshot state, int cameraFront,
        IReadOnlySet<int>? exempt, out WorldMapRouteWaypoint end, int leadFrames = 0)
    {
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var (fx, fz) = SubmarineForwardFrame(cameraFront, multiplier);
        var current = state;
        for (var frame = 0; frame < leadFrames + WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier); frame++)
        {
            var next = new WorldMapRouteWaypoint(
                ((current.X + fx) % map.WrapWidth + map.WrapWidth) % map.WrapWidth, current.Y,
                ((current.Z + fz) % map.WrapHeight + map.WrapHeight) % map.WrapHeight);
            if (!SubmarineSegmentFits(current, next, exempt) ||
                !WorldMapBroncoLanding.TryFindSurface(map, next.X, next.Z, out var floor))
            {
                end = next;
                return false;
            }

            current = current with
            {
                X = next.X, Z = next.Z, TerrainId = floor.TerrainId,
                TerrainScriptId = floor.TerrainScriptId, RegionId = floor.RegionId & 31
            };
        }

        var radians = cameraFront * Math.PI * 2d / 4096d;
        end = new WorldMapRouteWaypoint(current.X + (int)Math.Round(Math.Sin(radians) * 96), current.Y,
            current.Z - (int)Math.Round(Math.Cos(radians) * 96));
        return SubmarineSegmentFits(current, end, exempt);
    }

    /// <summary>
    /// Set only while a maneuver is searched: the body test by sample position, cached. The
    /// search compares thousands of overlapping legs; what it chooses is still flown only on
    /// the exact per-scan lease, so a cached answer can at worst end a maneuver early.
    /// </summary>
    private Dictionary<(int, int), bool>? submarinePlanFits;

    /// <summary>
    /// Work is counted in body-test segments (one native frame of lease forecast, about two
    /// 30-unit samples) and approach tests; a count rather than a clock, so outcomes do not
    /// depend on the machine. A poll does at most one slice of search work, plus the single
    /// step it was in; a whole search at most the budget, after which it has found nothing.
    /// </summary>
    internal const int SubmarineSearchSlice = 12000;
    internal const int SubmarineSearchBudget = 600000;

    /// <summary>Tests only: a smaller total search budget.</summary>
    internal int? SubmarineSearchBudgetOverride { get; set; }

    private int submarinePollWork;
    private int submarineSearchWork;
    internal int LastSubmarinePollWork { get; private set; }

    /// <summary>Tests and diagnostics: the last command was a neutral planning poll.</summary>
    internal bool LastSubmarineCommandWasPlanning { get; private set; }
    internal int WorstSubmarinePollWork { get; private set; }

    /// <summary>Runs a check with the search's cached samples switched off: exact.</summary>
    private bool SubmarineExactly(Func<bool> check)
    {
        var cached = submarinePlanFits;
        submarinePlanFits = null;
        try
        {
            return check();
        }
        finally
        {
            submarinePlanFits = cached;
        }
    }

    private void CountSubmarineWork(int units)
    {
        submarinePollWork += units;
        if (submarinePlanFits is not null) submarineSearchWork += units;
    }

    private bool SubmarineSegmentFits(WorldMapStateSnapshot current, WorldMapRouteWaypoint next, IReadOnlySet<int>? exempt)
    {
        CountSubmarineWork(1);
        if (submarinePlanFits is not { } fits) return planner.CanTraverseSegment(current, next, null, exempt);
        var dx = WorldMapTargetCatalog.WrappedDelta(current.X, next.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(current.Z, next.Z, map.WrapHeight);
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * (double)dx + dz * (double)dz) / 30));
        for (var sample = 0; sample <= steps; sample++)
        {
            var x = current.X + (int)Math.Round(dx * sample / (double)steps);
            var z = current.Z + (int)Math.Round(dz * sample / (double)steps);
            var key = (x / 10, z / 10);
            if (!fits.TryGetValue(key, out var fit))
                fits[key] = fit = planner.HasSubmarineFootprint(x, z) &&
                                  WorldMapBroncoLanding.TryFindSurface(map, x, z, out var floor) &&
                                  (exempt is null || !planner.IsUnwantedEntrance(floor.Id, exempt));
            if (!fit) return false;
        }
        return true;
    }

    private WorldMapRoutePlan? submarineStagingRoute;
    private WorldMapRouteWaypoint submarineStaging;
    private WorldMapRoutePlan? submarineStagingCountRoute;
    private int submarineStagingCount;
    private const int MaximumSubmarineStagings = 8;

    private double SubmarineDistance(WorldMapStateSnapshot state, WorldMapRouteWaypoint point)
    {
        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, point.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, point.Z, map.WrapHeight);
        return Math.Sqrt(dx * (double)dx + dz * (double)dz);
    }

    /// <summary>
    /// The final approach, in phases, so no key is held while anything expensive is worked
    /// out and nothing is flown from a pose older than the poll that flies it.
    ///
    /// <para>Near the destination (a lease and a waypoint's reach from its point) the first
    /// poll only marks the approach for planning and asks for neutral input (Prepare); both
    /// hosts resolve before they drive, so whatever was held is released at once. The polls
    /// after it search, in bounded slices, from the pose seen after that release, and stay
    /// neutral, including the one that finishes (Search). The next poll must find the same
    /// pose, multiplier and scan length, and re-checks the chosen command exactly from there
    /// (Ready); anything else prepares again. Flying it (Final or Maneuver), every poll checks
    /// the exact full lease from the actual camera and position; anything that no longer
    /// holds goes back through a neutral Prepare.</para>
    ///
    /// <para>What is searched: the camera heading, on the native per-frame turning lattice,
    /// that carries the sub into the handoff square for a scan's worth of frames on a lease
    /// fitting from the frame before the square; or, where a wall rules that out, a
    /// positioning maneuver of straight native legs ending where it exists. A search that
    /// finds nothing or runs out of budget leaves ordinary lease-checked steering, from each
    /// poll's own pose, to move the sub for a while before another search; after
    /// MaximumSubmarineSearches the sub stays still and the host's guard stops it with an
    /// explanation. No clock is reset by any of this.</para>
    /// </summary>
    private WorldMapRouteWaypoint ResolveSubmarineFinalAim(WorldMapStateSnapshot state, WorldMapRoutePlan route,
        WorldMapRouteWaypoint point, IReadOnlySet<int>? exempt, int scanFrames, out int? finalHeading,
        out bool finalThrust, out bool neutral)
    {
        finalHeading = null;
        finalThrust = false;
        neutral = false;
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var lease = 30d * multiplier * WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier) + 96d;
        if (!ReferenceEquals(submarineTerminalRoute, route))
        {
            EndSubmarineTerminal();
            submarineTerminalRoute = route;
            submarineStagingCount = 0;
            submarineSearches = 0;
        }
        if (SubmarineDistance(state, point) > lease + SubmarineWaypointReach)
        {
            EndSubmarineTerminal();
            return point;
        }

        var pose = new SubmarinePose(state.X, state.Z, state.CameraFront, multiplier, scanFrames);
        switch (submarinePhase)
        {
            case SubmarineTerminalPhase.Final:
                if (TryFlySubmarineFinal(state, point, exempt, scanFrames, out var flown, out finalThrust))
                {
                    finalHeading = flown;
                    return point;
                }
                lastDiagnostic = "the final approach no longer holds from here; planning again";
                break;

            case SubmarineTerminalPhase.Maneuver:
                // The camera may already carry the sub through the point; that is exact.
                if (ApproachesThrough(state, state.CameraFront, point, exempt, scanFrames))
                {
                    EndSubmarineManeuver();
                    submarinePhase = SubmarineTerminalPhase.Final;
                    submarineFinalHeading = state.CameraFront;
                    submarineFinalWholeScans = false;
                    finalHeading = state.CameraFront;
                    finalThrust = true;
                    return point;
                }
                if (TryContinueSubmarineManeuver(state, exempt, scanFrames, out var leg, out finalThrust))
                {
                    finalHeading = leg;
                    return point;
                }
                EndSubmarineManeuver();
                lastDiagnostic = $"positioning maneuver ended: {submarineManeuverEnd}; planning again";
                break;

            case SubmarineTerminalPhase.Ordinary:
                if (submarineOrdinaryPolls > 0 &&
                    SubmarineDistance(state, new WorldMapRouteWaypoint(submarineOrdinaryX, state.Y, submarineOrdinaryZ)) < 90)
                {
                    submarineOrdinaryPolls--;
                    return point;
                }
                break;

            case SubmarineTerminalPhase.Prepare:
                // The pose seen after the neutral request is what is planned from.
                submarineSearches++;
                submarinePose = pose;
                submarineSearchCache = new();
                submarineSearchWork = 0;
                submarineFoundFinal = null;
                submarineFoundLegs.Clear();
                submarineSearch = SearchSubmarineApproach(state, point, exempt, scanFrames).GetEnumerator();
                submarinePhase = SubmarineTerminalPhase.Search;
                goto case SubmarineTerminalPhase.Search;

            case SubmarineTerminalPhase.Search:
                neutral = true;
                if (pose != submarinePose)
                {
                    lastDiagnostic = "the submarine moved while its final approach was planned; planning again";
                    break;
                }
                if (!StepSubmarineSearch(out var exhausted))
                {
                    lastDiagnostic = "planning the final approach";
                    return point;
                }
                submarineSearch = null;
                submarineSearchCache = null;
                if (submarineFoundFinal is not null || submarineFoundLegs.Count > 0)
                {
                    submarinePhase = SubmarineTerminalPhase.Ready;
                    lastDiagnostic = $"final approach planned; it is checked against the next native pose (search work {submarineSearchWork})";
                }
                else
                {
                    submarinePhase = SubmarineTerminalPhase.Ordinary;
                    submarineOrdinaryPolls = 20;
                    submarineOrdinaryX = state.X;
                    submarineOrdinaryZ = state.Z;
                    lastDiagnostic = exhausted
                        ? "planning the final approach ran out of its work budget; steering on before trying again"
                        : $"no native submarine maneuver reaches a final approach from here (search work {submarineSearchWork}); steering on before trying again";
                }
                return point;

            case SubmarineTerminalPhase.Ready:
                if (pose != submarinePose)
                {
                    lastDiagnostic = "the submarine's position, camera or scan length changed since planning; planning again";
                    break;
                }
                if (TryStartSubmarineCandidate(state, route, point, exempt, scanFrames, out var heading, out finalThrust))
                {
                    finalHeading = heading;
                    return point;
                }
                lastDiagnostic = $"the planned approach does not hold from the actual pose ({submarineManeuverEnd}); planning again";
                break;
        }

        // A fresh plan is needed: release first, search on the following polls.
        neutral = true;
        submarineSearch = null;
        submarineSearchCache = null;
        EndSubmarineManeuver();
        if (submarineSearches >= MaximumSubmarineSearches)
        {
            submarinePhase = SubmarineTerminalPhase.Exhausted;
            lastDiagnostic = "no safe final approach was found for this destination; the submarine is holding still";
            return point;
        }
        submarinePhase = SubmarineTerminalPhase.Prepare;
        return point;
    }

    private enum SubmarineTerminalPhase { None, Prepare, Search, Ready, Final, Maneuver, Ordinary, Exhausted }

    private readonly record struct SubmarinePose(int X, int Z, int Camera, int Multiplier, int ScanFrames);

    private SubmarineTerminalPhase submarinePhase;
    private SubmarinePose submarinePose;
    private WorldMapRoutePlan? submarineTerminalRoute;
    private IEnumerator<bool>? submarineSearch;
    private Dictionary<(int, int), bool>? submarineSearchCache;
    private int submarineSearches;
    private int? submarineFoundFinal;
    private bool submarineFoundWholeScans;
    private readonly List<SubmarineLeg> submarineFoundLegs = new();
    private WorldMapRouteWaypoint submarineFoundEnd;
    private int submarineFinalHeading;
    private bool submarineFinalWholeScans;
    private int submarineOrdinaryPolls;
    private int submarineOrdinaryX;
    private int submarineOrdinaryZ;

    /// <summary>Searches per destination before the sub holds still for the host's guard.</summary>
    private const int MaximumSubmarineSearches = 12;

    /// <summary>Tests and diagnostics: the final approach's current phase.</summary>
    internal string SubmarineTerminalState => submarinePhase.ToString();

    private void EndSubmarineTerminal()
    {
        submarinePhase = SubmarineTerminalPhase.None;
        submarineSearch = null;
        submarineSearchCache = null;
        EndSubmarineManeuver();
    }

    /// <summary>
    /// Runs the search on, at most one slice of work this poll; true once it has finished,
    /// <paramref name="exhausted"/> when that was its total budget running out. The cache of
    /// sample answers is live only inside the search, so nothing flown is ever checked with it.
    /// </summary>
    private bool StepSubmarineSearch(out bool exhausted)
    {
        exhausted = false;
        var budget = SubmarineSearchBudgetOverride ?? SubmarineSearchBudget;
        submarinePlanFits = submarineSearchCache;
        try
        {
            while (true)
            {
                if (submarineSearchWork >= budget)
                {
                    exhausted = true;
                    submarineFoundFinal = null;
                    submarineFoundLegs.Clear();
                    return true;
                }
                if (submarineSearch is null || !submarineSearch.MoveNext()) return true;
                if (submarinePollWork >= SubmarineSearchSlice) return false;
            }
        }
        finally
        {
            submarinePlanFits = null;
        }
    }

    /// <summary>
    /// The candidate's first command, checked exactly from the actual pose (the search's
    /// cached samples never authorise movement): a final heading must still carry the sub
    /// through the point on a fitting lease; a maneuver's first leg must still fit.
    /// </summary>
    private bool TryStartSubmarineCandidate(WorldMapStateSnapshot state, WorldMapRoutePlan route,
        WorldMapRouteWaypoint point, IReadOnlySet<int>? exempt, int scanFrames, out int heading, out bool thrust)
    {
        heading = state.CameraFront;
        thrust = false;
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        if (submarineFoundFinal is { } final)
        {
            if (!ApproachesThrough(state, final, point, exempt, scanFrames, submarineFoundWholeScans)) return false;
            submarinePhase = SubmarineTerminalPhase.Final;
            submarineFinalHeading = final;
            submarineFinalWholeScans = submarineFoundWholeScans;
            return TryFlySubmarineFinal(state, point, exempt, scanFrames, out heading, out thrust);
        }

        if (submarineFoundLegs.Count == 0 || submarineStagingCount >= MaximumSubmarineStagings) return false;
        var lease = WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier);
        var first = submarineFoundLegs[0];
        if (CountClearSubmarineFrames(state, first.Heading, exempt, first.Frames + lease + 2) < first.Frames + lease + 1)
            return false;
        submarineLegs.Clear();
        submarineLegs.AddRange(submarineFoundLegs);
        submarineStaging = submarineFoundEnd;
        submarineStagingCount++;
        submarineStagingRoute = route;
        submarineLegStartX = state.X;
        submarineLegStartZ = state.Z;
        submarinePhase = SubmarineTerminalPhase.Maneuver;
        if (TryContinueSubmarineManeuver(state, exempt, scanFrames, out heading, out thrust)) return true;
        EndSubmarineManeuver();
        return false;
    }

    /// <summary>
    /// One poll of the final approach: thrust when the camera itself carries the sub through
    /// the point on a fitting lease (exact, from the actual pose); otherwise turn toward the
    /// chosen heading while that heading still does; otherwise the approach no longer holds.
    /// </summary>
    private bool TryFlySubmarineFinal(WorldMapStateSnapshot state, WorldMapRouteWaypoint point,
        IReadOnlySet<int>? exempt, int scanFrames, out int heading, out bool thrust)
    {
        heading = state.CameraFront;
        thrust = true;
        if (ApproachesThrough(state, state.CameraFront, point, exempt, scanFrames, submarineFinalWholeScans)) return true;
        thrust = false;
        if (state.CameraFront == submarineFinalHeading ||
            !ApproachesThrough(state, submarineFinalHeading, point, exempt, scanFrames, submarineFinalWholeScans)) return false;
        heading = submarineFinalHeading;
        return true;
    }

    /// <summary>
    /// The search, in steps: each yield is a point where the driver may stop for this poll.
    /// First the final heading from here, guaranteed and then on whole scans; then a
    /// positioning maneuver, likewise.
    /// </summary>
    private IEnumerable<bool> SearchSubmarineApproach(WorldMapStateSnapshot state, WorldMapRouteWaypoint point,
        IReadOnlySet<int>? exempt, int scanFrames)
    {
        // A scan longer than the square's widest chord (2 x SubmarineFinalReach) cannot be
        // guaranteed to end inside it, so only the whole-scan approach is searched for then.
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var modes = scanFrames * 30 * multiplier > 2 * SubmarineFinalReach ? new[] { true } : new[] { false, true };
        foreach (var wholeScans in modes)
        {
            var found = new SubmarineHeadingResult();
            foreach (var step in FindSubmarineFinalHeading(state, point, exempt, scanFrames, wholeScans, found, exact: true))
                yield return step;
            if (found.Heading is { } heading)
            {
                submarineFoundFinal = heading;
                submarineFoundWholeScans = wholeScans;
                yield break;
            }
        }

        foreach (var wholeScans in modes)
        {
            foreach (var step in PlanSubmarineManeuver(state, point, exempt, scanFrames, wholeScans))
                yield return step;
            if (submarineFoundLegs.Count > 0) yield break;
        }
    }

    private sealed class SubmarineHeadingResult
    {
        internal int? Heading;
    }

    /// <summary>One straight leg of a positioning maneuver: a heading and its native frames.</summary>
    private readonly record struct SubmarineLeg(int Heading, int Frames);

    private readonly List<SubmarineLeg> submarineLegs = new();
    private string submarineManeuverEnd = "its legs are done";
    private int submarineLegStartX;
    private int submarineLegStartZ;

    private void EndSubmarineManeuver()
    {
        submarineStagingRoute = null;
        submarineLegs.Clear();
    }

    /// <summary>Leg lengths in native frames; 16 frames is a little over one lease.</summary>
    private static readonly int[] SubmarineLegFrames = [1, 2, 3, 4, 6, 8, 11, 16];
    private const int MaximumSubmarineLegs = 3;

    /// <summary>First legs searched on from, the most promising first.</summary>
    private const int SubmarineManeuverBreadth = 32;

    /// <summary>
    /// The current leg's heading, and whether it still wants thrust. A leg is done once the
    /// sub has gone its frames along it (a scan can carry it up to a scan further, which the
    /// plan allowed for). Before a leg starts, a camera that one scan of turning carried just
    /// past its heading is taken as the heading when that leg still fits from here.
    /// </summary>
    private bool TryContinueSubmarineManeuver(WorldMapStateSnapshot state, IReadOnlySet<int>? exempt,
        int scanFrames, out int heading, out bool thrust)
    {
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var lease = WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier);
        submarineManeuverEnd = "its legs are done";
        while (submarineLegs.Count > 0)
        {
            var leg = submarineLegs[0];
            var (fx, fz) = SubmarineForwardFrame(leg.Heading, multiplier);
            var dx = WorldMapTargetCatalog.WrappedDelta(submarineLegStartX, state.X, map.WrapWidth);
            var dz = WorldMapTargetCatalog.WrappedDelta(submarineLegStartZ, state.Z, map.WrapHeight);
            var travelled = (dx * (double)fx + dz * (double)fz) / Math.Max(1d, fx * (double)fx + fz * (double)fz);
            if (travelled >= leg.Frames - 0.5)
            {
                submarineLegs.RemoveAt(0);
                submarineLegStartX = state.X;
                submarineLegStartZ = state.Z;
                continue;
            }

            // Scans turn the camera by whole scans of native frames, which need not land on
            // the leg's heading; the plan made the leg fit a scan of turning either side.
            var off = SignedTurn(state.CameraFront, leg.Heading);
            if (off != 0 && Math.Abs(off) <= 8 * multiplier * scanFrames &&
                CountClearSubmarineFrames(state, state.CameraFront, exempt, leg.Frames + lease + 2) >= leg.Frames + lease + 1)
            {
                leg = leg with { Heading = state.CameraFront };
                submarineLegs[0] = leg;
                off = 0;
            }

            // A camera that whole scans cannot bring onto the leg, or a leg that no longer
            // fits from where the sub is, ends the maneuver; the next one is searched from
            // the camera and place the sub actually has.
            if (off != 0 && Math.Abs(off) < 8 * multiplier * scanFrames)
            {
                submarineManeuverEnd = $"camera {state.CameraFront} cannot reach leg heading {leg.Heading} in whole scans";
                break;
            }
            if (off == 0 && !TryForecastSubmarineLease(state, state.CameraFront, exempt, out _))
            {
                submarineManeuverEnd = $"leg heading {leg.Heading} for {leg.Frames} frames no longer has a fitting lease";
                break;
            }
            heading = leg.Heading;
            thrust = true;
            return true;
        }

        heading = state.CameraFront;
        thrust = false;
        return false;
    }

    /// <summary>
    /// How many native frames along this heading, up to <paramref name="limit"/>, each pass
    /// the planner's submarine body test (the +-200 five-point footprint every 30 units on
    /// the native lowest floor, no unselected entrance).
    /// </summary>
    private int CountClearSubmarineFrames(WorldMapStateSnapshot state, int heading, IReadOnlySet<int>? exempt, int limit)
    {
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var (fx, fz) = SubmarineForwardFrame(heading, multiplier);
        var current = state;
        for (var frame = 0; frame < limit; frame++)
        {
            var next = new WorldMapRouteWaypoint(
                ((current.X + fx) % map.WrapWidth + map.WrapWidth) % map.WrapWidth, current.Y,
                ((current.Z + fz) % map.WrapHeight + map.WrapHeight) % map.WrapHeight);
            if (!SubmarineSegmentFits(current, next, exempt) ||
                !WorldMapBroncoLanding.TryFindSurface(map, next.X, next.Z, out var floor)) return frame;
            current = current with
            {
                X = next.X, Z = next.Z, TerrainId = floor.TerrainId,
                TerrainScriptId = floor.TerrainScriptId, RegionId = floor.RegionId & 31
            };
        }
        return limit;
    }

    private WorldMapStateSnapshot SubmarineStateAt(WorldMapStateSnapshot state, int x, int z, int camera)
    {
        x = (x % map.WrapWidth + map.WrapWidth) % map.WrapWidth;
        z = (z % map.WrapHeight + map.WrapHeight) % map.WrapHeight;
        return WorldMapBroncoLanding.TryFindSurface(map, x, z, out var floor)
            ? state with
            {
                X = x, Z = z, CameraFront = camera, TerrainId = floor.TerrainId,
                TerrainScriptId = floor.TerrainScriptId, RegionId = floor.RegionId & 31
            }
            : state with { X = x, Z = z, CameraFront = camera };
    }

    /// <summary>
    /// A breadth-first search, from where the sub is, over up to three straight native legs
    /// (each further leg from the most promising ends of the last only), yielding between
    /// steps. Leg headings lie whole scans of turning from the camera; a leg of n frames is
    /// taken only when n - 1 + a full lease + the 96-unit margin of frames ahead pass the
    /// body test, so every scan along it holds a fitting lease. The plan ends where, for every
    /// stop a scan can make (n to n + scanFrames - 1 frames), the final approach exists. The
    /// cheapest plan in native frames (turning plus moving) is left in submarineFoundLegs.
    /// </summary>
    private IEnumerable<bool> PlanSubmarineManeuver(WorldMapStateSnapshot state, WorldMapRouteWaypoint point,
        IReadOnlySet<int>? exempt, int scanFrames, bool wholeScans)
    {
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var turn = 8 * multiplier;
        var scanTurn = turn * scanFrames;
        var step = scanTurn * Math.Max(1, (int)Math.Ceiling(64d / scanTurn));
        var lease = WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier);
        // On whole scans the sub stops only after whole scans of thrust, so legs are whole
        // scans long; otherwise any length, since the stops a scan can add are all checked.
        var legFrames = wholeScans
            ? Enumerable.Range(1, Math.Max(1, SubmarineLegFrames[^1] / scanFrames)).Select(scans => scans * scanFrames).ToArray()
            : SubmarineLegFrames;
        var longest = legFrames[^1];
        var frame = 30d * multiplier;

        // Headings whose lease from just before the square fits: a final approach must use one.
        var lineFits = new bool[4096 / turn + 1];
        for (var index = 0; index < lineFits.Length - 1; index++)
        {
            var (lx, lz) = SubmarineForwardFrame(index * turn, multiplier);
            var back = (int)Math.Ceiling(SubmarineFinalReach / frame) + 1;
            lineFits[index] = TryForecastSubmarineLease(
                SubmarineStateAt(state, point.X - lx * back, point.Z - lz * back, index * turn), index * turn, exempt, out _);
            yield return false;
        }
        bool MayApproach(WorldMapStateSnapshot from)
        {
            var distance = SubmarineDistance(from, point);
            if (distance > 2 * lease * frame) return false;
            var toward = HeadingTo(from, point);
            var spread = (int)Math.Ceiling(Math.Atan2(SubmarineFinalReach * 1.5, Math.Max(1d, distance)) * 4096 / (Math.PI * 2)) + turn;
            for (var offset = -spread; offset <= spread; offset += turn)
                if (lineFits[(((toward + offset) % 4096 + 4096) % 4096) / turn]) return true;
            return false;
        }

        // Whether the final approach exists from a spot: all plan cameras share one turning
        // lattice, so this depends only on where the sub stops (kept per 15-unit cell).
        var approachable = new Dictionary<(int, int), bool>();

        var frontier = new List<(WorldMapStateSnapshot At, List<SubmarineLeg> Legs, int Cost)> { (state, new(), 0) };
        var seen = new HashSet<(int, int)> { (state.X / 120, state.Z / 120) };
        for (var depth = 1; depth <= MaximumSubmarineLegs && frontier.Count > 0; depth++)
        {
            List<SubmarineLeg>? best = null;
            var bestCost = int.MaxValue;
            var bestEnd = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
            var next = new List<(WorldMapStateSnapshot At, List<SubmarineLeg> Legs, int Cost)>();
            foreach (var node in frontier)
            {
                // Signed offsets, at most half a turn: the turn executed is the shorter one,
                // and 4096 need not be a whole number of scans of turning.
                for (var k = -(2047 / step); k <= 2047 / step; k++)
                {
                    var heading = ((node.At.CameraFront + k * step) % 4096 + 4096) % 4096;
                    var clear = CountClearSubmarineFrames(node.At, heading, exempt, longest + lease + 2);
                    yield return false;
                    var (fx, fz) = SubmarineForwardFrame(heading, multiplier);
                    var turning = (Math.Abs(SignedTurn(node.At.CameraFront, heading)) + turn - 1) / turn;
                    foreach (var frames in legFrames)
                    {
                        if (frames + lease + 1 > clear) break;
                        var cost = node.Cost + turning + frames;
                        var end = SubmarineStateAt(node.At, node.At.X + fx * frames, node.At.Z + fz * frames, heading);
                        var legs = new List<SubmarineLeg>(node.Legs) { new(heading, frames) };
                        var arrives = cost < bestCost && MayApproach(end);
                        // Guaranteed: every stop a scan can make. Whole scans are best effort
                        // already, so only the planned stop is asked about.
                        for (var extra = 0; arrives && extra < (wholeScans ? 1 : scanFrames); extra++)
                        {
                            var stop = SubmarineStateAt(node.At, node.At.X + fx * (frames + extra), node.At.Z + fz * (frames + extra), heading);
                            var key = (stop.X / 15, stop.Z / 15);
                            if (!approachable.TryGetValue(key, out var exists))
                            {
                                var found = new SubmarineHeadingResult();
                                foreach (var inner in FindSubmarineFinalHeading(stop, point, exempt, scanFrames, wholeScans, found))
                                    yield return inner;
                                approachable[key] = exists = found.Heading is not null;
                            }
                            arrives = exists;
                        }
                        if (arrives && depth == 1 && node.Legs.Count == 0)
                        {
                            var confirmHeading = heading;
                            var confirmFrames = frames;
                            arrives = SubmarineExactly(() => CountClearSubmarineFrames(state, confirmHeading, exempt,
                                confirmFrames + lease + 2) >= confirmFrames + lease + 1);
                        }
                        if (arrives)
                        {
                            best = legs;
                            bestCost = cost;
                            bestEnd = new WorldMapRouteWaypoint(end.X, end.Y, end.Z);
                        }
                        else if (depth < MaximumSubmarineLegs && seen.Add((end.X / 120, end.Z / 120)))
                        {
                            next.Add((end, legs, cost));
                        }
                    }
                }
            }

            // The first leg is flown from this pose: confirm it without the cached samples.
            if (best is not null && depth > 1)
            {
                var first = best[0];
                if (!SubmarineExactly(() => CountClearSubmarineFrames(state, first.Heading, exempt, first.Frames + lease + 2) >= first.Frames + lease + 1))
                    best = null;
            }
            if (best is not null)
            {
                submarineFoundLegs.Clear();
                submarineFoundLegs.AddRange(best);
                submarineFoundEnd = bestEnd;
                yield break;
            }
            // Search on from first legs that already see a fitting final line, nearest first.
            frontier = next
                .OrderBy(node => MayApproach(node.At) ? 0 : 1)
                .ThenBy(node => SubmarineDistance(node.At, point))
                .Take(SubmarineManeuverBreadth)
                .ToList();
            yield return false;
        }
    }

    /// <summary>
    /// The square a native frame must end in to count as inside the 128-unit Manhattan
    /// handoff, less a few units for the per-frame integer rounding.
    /// </summary>
    private const int SubmarineFinalReach = 120;

    /// <summary>Native frames in a 100 ms host sample: ceil(6 / multiplier).</summary>
    private static int SubmarineSampleFrames(int multiplier) => (6 + multiplier - 1) / multiplier;

    /// <summary>
    /// The heading nearest the camera, in whole native turning frames, that approaches
    /// through the point; turned to, it must stay such a heading for a scan's worth of frames
    /// further in the same direction, so a scan that turns too far still lands on one. Yields
    /// after every heading it tests; the answer is left in <paramref name="result"/>.
    /// </summary>
    private IEnumerable<bool> FindSubmarineFinalHeading(WorldMapStateSnapshot state, WorldMapRouteWaypoint point,
        IReadOnlySet<int>? exempt, int scanFrames, bool wholeScans, SubmarineHeadingResult result, bool exact = false)
    {
        result.Heading = null;
        var turn = 8 * Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var known = new Dictionary<int, bool>();
        // Only headings within the cone the square subtends from here (with a frame's
        // rounding to spare) can enter it; the rest are ruled out without walking them.
        var toward = HeadingTo(state, point);
        var distance = SubmarineDistance(state, point);
        var cone = distance <= 2 * SubmarineFinalReach ? 2048
            : (int)Math.Ceiling(Math.Asin(Math.Min(1d, (SubmarineFinalReach * 1.5 + 60) / distance)) * 4096 / (Math.PI * 2)) + turn;
        bool Approaches(int candidate)
        {
            candidate = (candidate % 4096 + 4096) % 4096;
            if (Math.Abs(SignedTurn(toward, candidate)) > cone) return false;
            if (!known.TryGetValue(candidate, out var through))
                known[candidate] = through = ApproachesThrough(state, candidate, point, exempt, scanFrames, wholeScans);
            return through;
        }

        for (var steps = 0; steps <= 2048 / turn; steps++)
        {
            for (var side = 0; side < (steps == 0 ? 1 : 2); side++)
            {
                var sign = side == 0 ? 1 : -1;
                var heading = ((state.CameraFront + sign * steps * turn) % 4096 + 4096) % 4096;
                var good = Approaches(heading);
                yield return false;
                if (!good) continue;
                var holds = true;
                for (var beyond = 1; steps > 0 && beyond < scanFrames && holds; beyond++)
                {
                    holds = Approaches(heading + sign * beyond * turn);
                    yield return false;
                }
                // A heading that will be flown from this very pose is confirmed without the
                // cached samples; one that fails that is simply not a candidate.
                if (holds && exact)
                {
                    var confirmed = heading;
                    holds = SubmarineExactly(() => ApproachesThrough(state, confirmed, point, exempt, scanFrames, wholeScans));
                    yield return false;
                }
                if (holds)
                {
                    result.Heading = heading;
                    yield break;
                }
            }
        }
    }

    /// <summary>
    /// Whether thrust held at this heading takes the sub, frame by native frame, into the
    /// handoff square and keeps it there for at least <paramref name="scanFrames"/> frames,
    /// with the full lease from the frame before the square fitting the native body. A scan
    /// that starts anywhere short of the square then cannot step over it.
    /// </summary>
    private bool ApproachesThrough(WorldMapStateSnapshot from, int heading, WorldMapRouteWaypoint point,
        IReadOnlySet<int>? exempt, int scanFrames, bool wholeScans = false)
    {
        CountSubmarineWork(1);
        var multiplier = Math.Clamp(from.NativeFrameMultiplier, 1, 4);
        var (fx, fz) = SubmarineForwardFrame(heading, multiplier);
        var entry = -1;
        var run = 0;
        var landed = false;
        for (var frame = 1; frame <= 2 * WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier) && run < scanFrames; frame++)
        {
            var dx = WorldMapTargetCatalog.WrappedDelta(from.X + fx * frame, point.X, map.WrapWidth);
            var dz = WorldMapTargetCatalog.WrappedDelta(from.Z + fz * frame, point.Z, map.WrapHeight);
            if (Math.Abs(dx) + Math.Abs(dz) <= SubmarineFinalReach)
            {
                if (entry < 0) entry = frame;
                run++;
                landed |= frame % scanFrames == 0;
            }
            else if (entry >= 0) break;
        }

        return (wholeScans ? landed : run >= scanFrames) &&
               TryForecastSubmarineLease(from, heading, exempt, out _, entry - 1);
    }

    private int ResolveSubmarineWaypoint(WorldMapStateSnapshot state, WorldMapRoutePlan route, int index)
    {
        index = Math.Clamp(index, 0, route.Waypoints.Count - 1);
        while (index < route.Waypoints.Count - 1)
        {
            var point = route.Waypoints[index];
            var dx = WorldMapTargetCatalog.WrappedDelta(state.X, point.X, map.WrapWidth);
            var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, point.Z, map.WrapHeight);
            // A waypoint counts as passed within SubmarineWaypointReach. The next leg is then
            // the aim; a straight line to it need not fit, because every thrust is still
            // checked over its whole native lease before it is given.
            var distance = Math.Sqrt(dx * (double)dx + dz * (double)dz);
            if (distance > SubmarineWaypointReach &&
                (distance > 120 ||
                 !planner.CanTraverseSegment(state, route.Waypoints[index + 1], null, activeTarget?.NativeEntranceExemptions))) break;
            index++;
        }

        // In narrow water the triangle path's portals can zig-zag, leaving a waypoint where
        // the native body never fits. A later waypoint the body reaches in a straight line
        // from here (every 30 units, five points, no unselected entrance) replaces them.
        for (var ahead = Math.Min(index + SubmarineWaypointLookahead, route.Waypoints.Count - 1); ahead > index; ahead--)
        {
            if (SubmarineDistance(state, route.Waypoints[ahead]) > SubmarineLookaheadDistance ||
                !planner.CanTraverseSegment(state, route.Waypoints[ahead], null, activeTarget?.NativeEntranceExemptions)) continue;
            index = ahead;
            break;
        }
        return index;
    }

    private const int SubmarineWaypointLookahead = 6;
    private const double SubmarineLookaheadDistance = 2400d;
}
