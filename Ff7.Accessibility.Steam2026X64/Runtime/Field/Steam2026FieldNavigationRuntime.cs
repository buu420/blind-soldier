using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldNavigationKeyRouter
{
    internal const int VirtualKeyU = 0x55;
    internal const int VirtualKeyO = 0x4F;
    internal const int VirtualKeyJ = 0x4A;
    internal const int VirtualKeyL = 0x4C;
    internal const int VirtualKeyK = 0x4B;
    internal const int VirtualKeyI = 0x49;

    internal static IReadOnlyList<FieldNavigationAction> ReadActions(
        Func<int, bool> observeRisingEdge)
    {
        ArgumentNullException.ThrowIfNull(observeRisingEdge);
        var actions = new List<FieldNavigationAction>(6);
        AddIfPressed(VirtualKeyU, FieldNavigationAction.PreviousCategory);
        AddIfPressed(VirtualKeyO, FieldNavigationAction.NextCategory);
        AddIfPressed(VirtualKeyJ, FieldNavigationAction.PreviousTarget);
        AddIfPressed(VirtualKeyL, FieldNavigationAction.NextTarget);
        AddIfPressed(VirtualKeyK, FieldNavigationAction.RepeatTarget);
        AddIfPressed(VirtualKeyI, FieldNavigationAction.ToggleBeacon);
        return actions;

        void AddIfPressed(int virtualKey, FieldNavigationAction action)
        {
            if (observeRisingEdge(virtualKey))
            {
                actions.Add(action);
            }
        }
    }
}

internal sealed class Steam2026FieldNavigationPendingActionBuffer
{
    private readonly Queue<FieldNavigationAction> actions;
    private readonly int capacity;
    private int activeFieldId = -1;

    internal Steam2026FieldNavigationPendingActionBuffer(int capacity = 24)
    {
        this.capacity = Math.Max(1, capacity);
        actions = new Queue<FieldNavigationAction>(this.capacity);
    }

    internal int Count => actions.Count;

    internal void Capture(IEnumerable<FieldNavigationAction> observedActions)
    {
        ArgumentNullException.ThrowIfNull(observedActions);
        foreach (var action in observedActions)
        {
            if (actions.Count == capacity)
            {
                actions.Dequeue();
            }

            actions.Enqueue(action);
        }
    }

    internal bool TryTakeReadyForField(
        int fieldId,
        Func<FieldNavigationAction, bool> isReady,
        out FieldNavigationAction action)
    {
        if (!TryPeekReadyForField(fieldId, isReady, out action))
        {
            return false;
        }

        return TryRemoveHeadForField(fieldId, action);
    }

    internal bool TryPeekReadyForField(
        int fieldId,
        Func<FieldNavigationAction, bool> isReady,
        out FieldNavigationAction action)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        action = default;
        if (!TryEstablishField(fieldId) ||
            !actions.TryPeek(out var candidate) ||
            !isReady(candidate))
        {
            return false;
        }

        action = candidate;
        return true;
    }

    internal bool TryRemoveHeadForField(
        int fieldId,
        FieldNavigationAction expected)
    {
        if (!TryEstablishField(fieldId) ||
            !actions.TryPeek(out var candidate) ||
            candidate != expected)
        {
            return false;
        }

        actions.Dequeue();
        return true;
    }

    internal bool TryTakeEmergencyBeaconOff(
        int fieldId,
        bool beaconEnabled,
        out FieldNavigationAction action)
    {
        action = default;
        if (fieldId < 0)
        {
            Clear();
            return false;
        }

        if (activeFieldId >= 0 && activeFieldId != fieldId)
        {
            actions.Clear();
            activeFieldId = fieldId;
            return false;
        }

        activeFieldId = fieldId;
        if (!beaconEnabled || !actions.Contains(FieldNavigationAction.ToggleBeacon))
        {
            return false;
        }

        // Beacon-off is a user cancellation barrier, not ordinary queue
        // reordering. Discard the entire captured batch so an older relock or
        // a trailing toggle cannot immediately undo the requested stop.
        actions.Clear();
        action = FieldNavigationAction.ToggleBeacon;
        return true;
    }

    internal void Clear()
    {
        actions.Clear();
        activeFieldId = -1;
    }

    private bool TryEstablishField(int fieldId)
    {
        if (fieldId < 0)
        {
            Clear();
            return false;
        }

        if (activeFieldId >= 0 && activeFieldId != fieldId)
        {
            actions.Clear();
            activeFieldId = fieldId;
            return false;
        }

        activeFieldId = fieldId;
        return true;
    }
}

internal readonly record struct Steam2026FieldNavigationDomainCoherence(
    bool Exits,
    bool Story,
    bool Npcs,
    bool Objects,
    bool Route)
{
    internal bool IsCategoryCoherent(FieldNavigationCategory category) => category switch
    {
        FieldNavigationCategory.Exits => Exits,
        FieldNavigationCategory.Story => Story,
        FieldNavigationCategory.Npcs => Npcs,
        FieldNavigationCategory.Objects => Objects,
        _ => false
    };
}

internal static class Steam2026FieldNavigationActionGate
{
    internal static bool IsReady(
        FieldNavigationAction action,
        FieldNavigationCategory currentCategory,
        bool beaconEnabled,
        Steam2026FieldNavigationDomainCoherence coherence)
    {
        // Turning an active beacon off is always safe and must never be trapped
        // behind a transient native read failure.
        if (action == FieldNavigationAction.ToggleBeacon && beaconEnabled)
        {
            return true;
        }

        var requiredCategory = ResolveActionCategory(action, currentCategory);
        if (!coherence.IsCategoryCoherent(requiredCategory))
        {
            return false;
        }

        // Starting or relocking a beacon invokes the route planner. Keep the
        // edge pending if its checked native route state tore, avoiding a false
        // "route unavailable" / "Navigation off" announcement. Plain target
        // browsing remains independent from exit/route reads.
        return !RequiresCoherentRoute(action, beaconEnabled) || coherence.Route;
    }

    internal static bool CanUpdateLiveTracking(
        FieldNavigationCategory currentCategory,
        bool beaconEnabled,
        Steam2026FieldNavigationDomainCoherence coherence) =>
        !beaconEnabled ||
        coherence.Route && coherence.IsCategoryCoherent(currentCategory);

    internal static FieldNavigationCategory ResolveActionCategory(
        FieldNavigationAction action,
        FieldNavigationCategory currentCategory) => action switch
        {
            FieldNavigationAction.PreviousCategory => currentCategory switch
            {
                FieldNavigationCategory.Exits => FieldNavigationCategory.Objects,
                FieldNavigationCategory.Story => FieldNavigationCategory.Exits,
                FieldNavigationCategory.Npcs => FieldNavigationCategory.Story,
                FieldNavigationCategory.Objects => FieldNavigationCategory.Npcs,
                _ => FieldNavigationCategory.Exits
            },
            FieldNavigationAction.NextCategory => currentCategory switch
            {
                FieldNavigationCategory.Exits => FieldNavigationCategory.Story,
                FieldNavigationCategory.Story => FieldNavigationCategory.Npcs,
                FieldNavigationCategory.Npcs => FieldNavigationCategory.Objects,
                FieldNavigationCategory.Objects => FieldNavigationCategory.Exits,
                _ => FieldNavigationCategory.Exits
            },
            _ => currentCategory
        };

    private static bool RequiresCoherentRoute(
        FieldNavigationAction action,
        bool beaconEnabled) => action switch
        {
            FieldNavigationAction.ToggleBeacon => !beaconEnabled,
            FieldNavigationAction.PreviousCategory or
            FieldNavigationAction.NextCategory or
            FieldNavigationAction.PreviousTarget or
            FieldNavigationAction.NextTarget => beaconEnabled,
            _ => false
        };
}

internal static class Steam2026FieldNavigationPendingActionExecutor
{
    internal static bool TryExecuteNext(
        Steam2026FieldNavigationPendingActionBuffer pendingActions,
        int fieldId,
        FieldNavigationController controller,
        Steam2026FailClosedFieldRoutePlanner routePlanner,
        FieldPositionSnapshot position,
        FieldNavigationControlTransform control,
        ref Steam2026FieldNavigationDomainCoherence coherence,
        out FieldNavigationAction action,
        out FieldNavigationActionResult? result) =>
        TryExecuteNext(
            pendingActions,
            fieldId,
            controller,
            routePlanner,
            position,
            control,
            FieldLadderStateSnapshot.NotMounted,
            ref coherence,
            out action,
            out result);

    internal static bool TryExecuteNext(
        Steam2026FieldNavigationPendingActionBuffer pendingActions,
        int fieldId,
        FieldNavigationController controller,
        Steam2026FailClosedFieldRoutePlanner routePlanner,
        FieldPositionSnapshot position,
        FieldNavigationControlTransform control,
        FieldLadderStateSnapshot ladderState,
        ref Steam2026FieldNavigationDomainCoherence coherence,
        out FieldNavigationAction action,
        out FieldNavigationActionResult? result)
    {
        ArgumentNullException.ThrowIfNull(pendingActions);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(routePlanner);
        action = default;
        result = null;
        var checkedCoherence = coherence with
        {
            Route = coherence.Route && !routePlanner.HadReadFailure
        };
        if (!pendingActions.TryPeekReadyForField(
                fieldId,
                candidate => Steam2026FieldNavigationActionGate.IsReady(
                    candidate,
                    controller.CurrentCategory,
                    controller.BeaconEnabled,
                    checkedCoherence),
                out var candidate))
        {
            return false;
        }

        var planningPosition =
            FieldNavigationController.ResolveRoutePlanningPosition(position, ladderState);
        var preview = controller.PreviewActionRoute(
            candidate,
            planningPosition,
            includeSelectionRoute: true);
        var preflight = new Steam2026FieldRoutePreflight(true, false);
        if (preview.UsesRoute && preview.Target is { } routeTarget)
        {
            preflight = routePlanner.PrepareActionRoute(planningPosition, routeTarget);
            if (preview.RequiresCoherentRoute && !preflight.IsCoherent)
            {
                coherence = coherence with { Route = false };
                routePlanner.CompletePreparedActionRoute();
                return false;
            }
        }

        if (!pendingActions.TryRemoveHeadForField(fieldId, candidate))
        {
            routePlanner.CompletePreparedActionRoute();
            return false;
        }

        action = candidate;
        try
        {
            result = controller.HandleAction(candidate, position, control, ladderState);
        }
        finally
        {
            coherence = coherence with
            {
                Route = coherence.Route && preflight.IsCoherent && !routePlanner.HadReadFailure
            };
            routePlanner.CompletePreparedActionRoute();
        }

        return true;
    }
}

internal readonly record struct Steam2026FieldRoutePreflight(
    bool IsCoherent,
    bool RouteAvailable);

internal sealed class Steam2026FailClosedFieldRoutePlanner :
    IFieldNavigationRoutePlanner,
    IFieldNavigationRouteReadStatus
{
    private readonly IFieldNavigationRoutePlanner inner;
    private PreparedActionRoute? preparedActionRoute;

    internal Steam2026FailClosedFieldRoutePlanner(IFieldNavigationRoutePlanner inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public bool LastReadWasCoherent => !HadReadFailure;

    internal bool HadReadFailure { get; private set; }

    internal void BeginObservation()
    {
        preparedActionRoute = null;
        HadReadFailure = false;
        LastDiagnostic = "not read";
    }

    internal Steam2026FieldRoutePreflight PrepareActionRoute(
        FieldPositionSnapshot position,
        FieldNavigationTarget target)
    {
        preparedActionRoute = null;
        if (HadReadFailure)
        {
            preparedActionRoute = PreparedActionRoute.Incoherent(position, target);
            return new Steam2026FieldRoutePreflight(false, false);
        }

        var resolvedTriangle = -1;
        bool resolved;
        try
        {
            resolved = inner.TryResolvePlayerTriangle(position, out resolvedTriangle);
            LastDiagnostic = inner.LastDiagnostic;
            CaptureInnerReadStatus();
        }
        catch (Exception ex)
        {
            HadReadFailure = true;
            LastDiagnostic = $"checked route triangle preflight failed: {ex.Message}";
            preparedActionRoute = PreparedActionRoute.Incoherent(position, target);
            return new Steam2026FieldRoutePreflight(false, false);
        }

        if (HadReadFailure)
        {
            preparedActionRoute = PreparedActionRoute.Incoherent(position, target);
            return new Steam2026FieldRoutePreflight(false, false);
        }

        if (!resolved)
        {
            preparedActionRoute = new PreparedActionRoute(
                position,
                target,
                false,
                -1,
                false,
                false,
                null);
            return new Steam2026FieldRoutePreflight(true, false);
        }

        bool built;
        FieldNavigationRoutePlan plan;
        try
        {
            built = inner.TryBuildRoute(position, target, out plan!);
            LastDiagnostic = inner.LastDiagnostic;
            CaptureInnerReadStatus();
        }
        catch (Exception ex)
        {
            HadReadFailure = true;
            LastDiagnostic = $"checked route build preflight failed: {ex.Message}";
            preparedActionRoute = PreparedActionRoute.Incoherent(position, target);
            return new Steam2026FieldRoutePreflight(false, false);
        }

        if (HadReadFailure)
        {
            preparedActionRoute = PreparedActionRoute.Incoherent(position, target);
            return new Steam2026FieldRoutePreflight(false, false);
        }

        preparedActionRoute = new PreparedActionRoute(
            position,
            target,
            true,
            resolvedTriangle,
            true,
            built,
            built ? plan : null);
        return new Steam2026FieldRoutePreflight(true, built);
    }

    internal void CompletePreparedActionRoute() => preparedActionRoute = null;

    public bool TryResolvePlayerTriangle(
        FieldPositionSnapshot position,
        out int triangle)
    {
        triangle = -1;
        if (preparedActionRoute is { } prepared)
        {
            if (!prepared.MatchesPosition(position))
            {
                return FailPreparedReplay("triangle position changed", out triangle);
            }

            triangle = prepared.ResolvedTriangle;
            return prepared.ResolveResult;
        }

        try
        {
            var result = inner.TryResolvePlayerTriangle(position, out triangle);
            LastDiagnostic = inner.LastDiagnostic;
            CaptureInnerReadStatus();
            return result;
        }
        catch (Exception ex)
        {
            HadReadFailure = true;
            LastDiagnostic = $"checked route triangle read failed: {ex.Message}";
            triangle = -1;
            return false;
        }
    }

    public bool TryBuildRoute(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRoutePlan plan)
    {
        plan = null!;
        if (preparedActionRoute is { } prepared)
        {
            if (!prepared.Matches(position, target))
            {
                HadReadFailure = true;
                LastDiagnostic = "prepared route replay target changed";
                return false;
            }

            if (prepared.BuildResult && prepared.Plan is { } cachedPlan)
            {
                plan = cachedPlan;
                return true;
            }

            return false;
        }

        try
        {
            var result = inner.TryBuildRoute(position, target, out plan);
            LastDiagnostic = inner.LastDiagnostic;
            CaptureInnerReadStatus();
            return result;
        }
        catch (Exception ex)
        {
            HadReadFailure = true;
            LastDiagnostic = $"checked route build failed: {ex.Message}";
            plan = null!;
            return false;
        }
    }

    public bool TryGetNextWaypoint(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRouteWaypoint waypoint)
    {
        waypoint = default;
        try
        {
            var result = inner.TryGetNextWaypoint(position, target, out waypoint);
            LastDiagnostic = inner.LastDiagnostic;
            CaptureInnerReadStatus();
            return result;
        }
        catch (Exception ex)
        {
            HadReadFailure = true;
            LastDiagnostic = $"checked route waypoint read failed: {ex.Message}";
            waypoint = default;
            return false;
        }
    }

    private void CaptureInnerReadStatus()
    {
        if (inner is IFieldNavigationRouteReadStatus { LastReadWasCoherent: false })
        {
            HadReadFailure = true;
        }
    }

    private bool FailPreparedReplay(string reason, out int triangle)
    {
        triangle = -1;
        HadReadFailure = true;
        LastDiagnostic = $"prepared route replay failed: {reason}";
        return false;
    }

    private sealed record PreparedActionRoute(
        FieldPositionSnapshot Position,
        FieldNavigationTarget Target,
        bool ResolveResult,
        int ResolvedTriangle,
        bool BuildAttempted,
        bool BuildResult,
        FieldNavigationRoutePlan? Plan)
    {
        internal static PreparedActionRoute Incoherent(
            FieldPositionSnapshot position,
            FieldNavigationTarget target) =>
            new(position, target, false, -1, false, false, null);

        internal bool MatchesPosition(FieldPositionSnapshot position) => Position == position;

        internal bool Matches(
            FieldPositionSnapshot position,
            FieldNavigationTarget target) =>
            MatchesPosition(position) &&
            Target.FieldId == target.FieldId &&
            Target.Category == target.Category &&
            Target.X == target.X &&
            Target.Y == target.Y &&
            Target.Z == target.Z &&
            string.Equals(Target.Label, target.Label, StringComparison.Ordinal) &&
            string.Equals(Target.StableId, target.StableId, StringComparison.Ordinal);
    }
}

internal interface ISteam2026FieldExitSpatialPlayback : IDisposable
{
    bool Play(NavigationBeaconCue cue, float gain);

    void StopAll();
}

internal sealed class Steam2026FieldExitSpatialCoordinator : IDisposable
{
    private readonly FieldExitProximityCueTracker tracker;
    private readonly ISteam2026FieldExitSpatialPlayback playback;
    private readonly Action<string> log;
    private readonly bool enabled;
    private int? activeFieldId;
    private bool isReset = true;
    private int disposed;

    internal Steam2026FieldExitSpatialCoordinator(
        FieldExitProximityCueTracker tracker,
        ISteam2026FieldExitSpatialPlayback playback,
        Action<string> log,
        bool enabled = true)
    {
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.enabled = enabled;
    }

    internal static Steam2026FieldExitSpatialCoordinator Create(
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        ArgumentNullException.ThrowIfNull(log);
        var path = Path.IsPathRooted(config.FieldExitCueSoundPath)
            ? config.FieldExitCueSoundPath
            : Path.Combine(modDirectory, config.FieldExitCueSoundPath);
        var coordinator = new Steam2026FieldExitSpatialCoordinator(
            new FieldExitProximityCueTracker(
                config.FieldExitCueInnerRangeUnits,
                config.FieldExitCueOuterRangeUnits,
                TimeSpan.FromMilliseconds(Math.Max(100, config.FieldExitCueIntervalMs))),
            new Steam2026FieldExitSpatialPlayback(
                Path.GetFullPath(path),
                config.FieldExitCueVolumePercent,
                config.EnableFieldExitProximityCues,
                log),
            log,
            config.EnableFieldExitProximityCues);
        log(
            $"Native Steam 2026 exit-point spatial cues initialized: " +
            $"enabled={config.EnableFieldExitProximityCues}, " +
            $"inner={config.FieldExitCueInnerRangeUnits}, " +
            $"outer={config.FieldExitCueOuterRangeUnits}, " +
            $"interval={Math.Max(100, config.FieldExitCueIntervalMs)}ms.");
        return coordinator;
    }

    internal void Observe(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        IReadOnlyList<FieldNavigationTarget> reachableExits,
        bool isHostForeground,
        bool isSuppressed,
        bool isReadCoherent,
        DateTime nowUtc)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(reachableExits);
        if (!enabled || !isHostForeground || isSuppressed || !isReadCoherent ||
            !FieldPositionReader.IsUsable(position))
        {
            TransitionToReset();
            return;
        }

        if (activeFieldId is int previousField && previousField != position.FieldId)
        {
            playback.StopAll();
            tracker.Reset();
            log($"Native Steam 2026 exit-point cues reset: field={previousField}->{position.FieldId}.");
        }

        activeFieldId = position.FieldId;
        isReset = false;
        var proximityCues = tracker.Update(position, reachableExits, nowUtc);
        if (!tracker.HasAudibleTargets)
        {
            playback.StopAll();
        }

        foreach (var proximityCue in proximityCues)
        {
            var spatialCue = FieldProximitySpatializer.CreateCue(
                position,
                proximityCue.Target,
                controlTransform);
            if (spatialCue is not { } cue)
            {
                continue;
            }

            try
            {
                if (playback.Play(cue, proximityCue.Gain))
                {
                    log(
                        $"Native Steam 2026 exit-point cue played: " +
                        $"target={proximityCue.Target.Label}, " +
                        $"distance={cue.DistanceUnits:0}, gain={proximityCue.Gain:0.000}.");
                }
            }
            catch (Exception ex)
            {
                log($"Native Steam 2026 exit-point cue failed without fallback: {ex.Message}");
            }
        }
    }

    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        tracker.Reset();
        playback.StopAll();
        activeFieldId = null;
        isReset = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        tracker.Reset();
        playback.StopAll();
        playback.Dispose();
        activeFieldId = null;
        isReset = true;
    }

    private void TransitionToReset()
    {
        tracker.Reset();
        activeFieldId = null;
        if (!isReset)
        {
            playback.StopAll();
        }

        isReset = true;
    }
}

internal sealed class Steam2026FieldExitSpatialPlayback : ISteam2026FieldExitSpatialPlayback
{
    private readonly NavigationBeaconPlayer? player;
    private int disposed;

    internal Steam2026FieldExitSpatialPlayback(
        string path,
        int volumePercent,
        bool enabled,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);
        if (enabled)
        {
            player = new NavigationBeaconPlayer(path, volumePercent, log);
        }
    }

    public bool Play(NavigationBeaconCue cue, float gain)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        return player?.Play(cue, gain) == true;
    }

    public void StopAll()
    {
        if (disposed == 0)
        {
            player?.StopAll();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            player?.Dispose();
        }
    }
}

internal interface ISteam2026FieldLadderSpatialPlayback : IDisposable
{
    bool Play(NavigationBeaconCue cue, float gain);

    void StopAll();
}

internal sealed class Steam2026FieldLadderSpatialCoordinator : IDisposable
{
    private readonly FieldLadderProximityCueTracker tracker;
    private readonly ISteam2026FieldLadderSpatialPlayback playback;
    private readonly Action<string> log;
    private readonly bool enabled;
    private int? activeFieldId;
    private bool isReset = true;
    private int disposed;

    internal Steam2026FieldLadderSpatialCoordinator(
        FieldLadderProximityCueTracker tracker,
        ISteam2026FieldLadderSpatialPlayback playback,
        Action<string> log,
        bool enabled = true)
    {
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.enabled = enabled;
    }

    internal static Steam2026FieldLadderSpatialCoordinator Create(
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        ArgumentNullException.ThrowIfNull(log);
        var path = Path.IsPathRooted(config.FieldLadderCueSoundPath)
            ? config.FieldLadderCueSoundPath
            : Path.Combine(modDirectory, config.FieldLadderCueSoundPath);
        var coordinator = new Steam2026FieldLadderSpatialCoordinator(
            new FieldLadderProximityCueTracker(
                config.FieldLadderCueInnerRangeUnits,
                config.FieldLadderCueOuterRangeUnits,
                TimeSpan.FromMilliseconds(Math.Max(100, config.FieldLadderCueIntervalMs))),
            new Steam2026FieldLadderSpatialPlayback(
                Path.GetFullPath(path),
                config.FieldLadderCueVolumePercent,
                config.EnableFieldLadderProximityCues,
                log),
            log,
            config.EnableFieldLadderProximityCues);
        log(
            $"Native Steam 2026 ladder spatial cues initialized: " +
            $"enabled={config.EnableFieldLadderProximityCues}, " +
            $"inner={config.FieldLadderCueInnerRangeUnits}, " +
            $"outer={config.FieldLadderCueOuterRangeUnits}, " +
            $"interval={Math.Max(100, config.FieldLadderCueIntervalMs)}ms, " +
            "source=all live-enabled native LADER entrances.");
        return coordinator;
    }

    internal void Observe(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        IReadOnlyList<FieldScriptNavigationTransition> liveTransitions,
        FieldLadderStateSnapshot ladderState,
        bool isHostForeground,
        bool isSuppressed,
        bool isReadCoherent,
        DateTime nowUtc)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(liveTransitions);
        if (!enabled || !isHostForeground || isSuppressed || !isReadCoherent ||
            !FieldPositionReader.IsUsable(position) ||
            (ladderState.IsUsable && ladderState.IsMounted))
        {
            TransitionToReset();
            return;
        }

        if (activeFieldId is int previousField && previousField != position.FieldId)
        {
            playback.StopAll();
            tracker.Reset();
            log($"Native Steam 2026 ladder cues reset: field={previousField}->{position.FieldId}.");
        }

        activeFieldId = position.FieldId;
        isReset = false;
        foreach (var proximityCue in tracker.Update(position, liveTransitions, nowUtc))
        {
            var transition = proximityCue.Transition;
            var target = new FieldNavigationTarget(
                transition.FieldId,
                FieldNavigationCategory.Objects,
                "Ladder",
                transition.SourceX,
                transition.SourceY,
                transition.SourceZ,
                transition.StableId);
            var spatialCue = FieldProximitySpatializer.CreateCue(
                position,
                target,
                controlTransform);
            if (spatialCue is not { } cue)
            {
                continue;
            }

            try
            {
                if (playback.Play(cue, proximityCue.Gain))
                {
                    log(
                        $"Native Steam 2026 ladder cue played: " +
                        $"entity={transition.SourceEntityId}, " +
                        $"position=({transition.SourceX},{transition.SourceY},{transition.SourceZ}), " +
                        $"distance={cue.DistanceUnits:0}, gain={proximityCue.Gain:0.000}, " +
                        $"id={proximityCue.TargetKey}.");
                }
            }
            catch (Exception ex)
            {
                log($"Native Steam 2026 ladder cue failed without fallback: {ex.Message}");
            }
        }
    }

    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        tracker.Reset();
        playback.StopAll();
        activeFieldId = null;
        isReset = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        tracker.Reset();
        playback.StopAll();
        playback.Dispose();
        activeFieldId = null;
        isReset = true;
    }

    private void TransitionToReset()
    {
        tracker.Reset();
        activeFieldId = null;
        if (!isReset)
        {
            playback.StopAll();
        }

        isReset = true;
    }
}

internal sealed class Steam2026FieldLadderSpatialPlayback : ISteam2026FieldLadderSpatialPlayback
{
    private readonly NavigationBeaconPlayer? player;
    private int disposed;

    internal Steam2026FieldLadderSpatialPlayback(
        string path,
        int volumePercent,
        bool enabled,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(log);
        if (enabled)
        {
            player = new NavigationBeaconPlayer(path, volumePercent, log);
        }
    }

    public bool Play(NavigationBeaconCue cue, float gain)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        return player?.Play(cue, gain) == true;
    }

    public void StopAll()
    {
        if (disposed == 0)
        {
            player?.StopAll();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            player?.Dispose();
        }
    }
}
