namespace Ff7.Accessibility.Reloaded;

public delegate IReadOnlyList<WorldMapNavigationTarget> WorldMapTargetProvider(
    WorldMapStateSnapshot state,
    WorldMapNavigationCategory category);

public readonly record struct WorldMapNavigationOutput(string? Speech);

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

    private int categoryIndex;
    private bool beaconEnabled;
    private bool combatPaused;
    private WorldMapNavigationTarget? activeTarget;
    private WorldMapRoutePlan? activeRoute;
    private WorldMapRouteWaypoint routeStart;
    private WorldMapRouteWaypoint progressRouteStart;
    private IReadOnlyList<WorldMapRouteWaypoint> progressRouteWaypoints = Array.Empty<WorldMapRouteWaypoint>();
    private int waypointIndex;
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
        TimeSpan? guidanceInterval = null)
    {
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
        DateTime observedAt = default)
    {
        var now = observedAt == default ? DateTime.UtcNow : observedAt;
        if (!beaconEnabled)
        {
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
            progressSink?.Activate(progressPercent);
            lastGuidanceAt = DateTime.MinValue;
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
            if (refreshed.TriangleId != activeRoute.TargetTriangleId)
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

        if (target.HasArrived(playerTriangle))
        {
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

        UpdateProgressAndWaypoint(state);

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

        var run = WorldMapConnectedRunFormatter.Resolve(
            routeStart,
            route.Waypoints,
            waypointIndex,
            state,
            map.WrapWidth,
            map.WrapHeight,
            distanceUnitsPerCount);
        input = run.Direction switch
        {
            "up" => FieldNavigationInput.Up,
            "up-right" => FieldNavigationInput.UpRight,
            "right" => FieldNavigationInput.Right,
            "down-right" => FieldNavigationInput.DownRight,
            "down" => FieldNavigationInput.Down,
            "down-left" => FieldNavigationInput.DownLeft,
            "left" => FieldNavigationInput.Left,
            "up-left" => FieldNavigationInput.UpLeft,
            _ => FieldNavigationInput.None
        };
        return input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;
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

        if (target.HasArrived(playerTriangle))
        {
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
        activeTarget = target;
        activeRoute = route;
        routeStart = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        waypointIndex = 0;
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
        UpdateProgressAndWaypoint(state);
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
        if (target is null || route is null || route.Waypoints.Count == 0)
        {
            return includeTarget && target is not null ? target.Label : "nearby";
        }

        var direction = WorldMapConnectedRunFormatter.Resolve(
            routeStart,
            route.Waypoints,
            waypointIndex,
            state,
            map.WrapWidth,
            map.WrapHeight,
            distanceUnitsPerCount).Speech;
        var prefix = includeTarget ? $"{target.Label}. " : string.Empty;
        var progress = includeProgress ? $" Route progress {progressPercent} percent." : string.Empty;
        return $"{prefix}{direction}.{progress}".Trim();
    }

    private string CreateGuidanceSignature(WorldMapStateSnapshot state)
    {
        if (activeRoute is not { Waypoints.Count: > 0 } route)
        {
            return string.Empty;
        }

        var run = WorldMapConnectedRunFormatter.Resolve(
            routeStart,
            route.Waypoints,
            waypointIndex,
            state,
            map.WrapWidth,
            map.WrapHeight,
            distanceUnitsPerCount);
        return $"{run.Direction}:{run.EndWaypointIndex}";
    }

    private void UpdateProgressAndWaypoint(WorldMapStateSnapshot state)
    {
        if (activeRoute is null)
        {
            return;
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
        lastDiagnostic =
            $"route progress={progressPercent}, waypoint={waypointIndex}, offset={guidanceMeasurement.DistanceFromRoute:0}";
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
        progressPercent = 0;
        activeModelId = -1;
        activeMapType = -1;
        activeWorldProgress = int.MinValue;
        lastGuidanceAt = DateTime.MinValue;
        offRouteSince = DateTime.MinValue;
        lastGuidanceSignature = string.Empty;
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
