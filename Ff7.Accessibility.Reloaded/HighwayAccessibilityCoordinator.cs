using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Composes checked highway state, shared cue policy, Steam Audio, and Prism.
/// The class contains no architecture-specific pointer or input logic.
/// </summary>
internal sealed class HighwayAccessibilityCoordinator : IDisposable
{
    private readonly AccessibilityConfig config;
    private readonly HighwayStateReader combatReader;
    private readonly HighwayRoadStateReader roadReader;
    private readonly HighwayAccessibilityComposer composer;
    private readonly HighwayAutoSteeringModeTracker autoSteeringMode;
    private readonly HighwayAutoSteeringController autoSteeringController;
    private readonly NavigationBeaconPlayer? lowerPriorityPlayer;
    private readonly NavigationBeaconPlayer? importantPlayer;
    private readonly NavigationBeaconPlayer? truckPlayer;
    private readonly NavigationBeaconPlayer? steeringPlayer;
    private readonly Action<string, bool> speak;
    private readonly Action<string> log;
    private bool active;
    private string lastCombatFailureDiagnostic = string.Empty;
    private string lastRoadFailureDiagnostic = string.Empty;
    private string lastAutoSteeringFailureDiagnostic = string.Empty;
    private HighwaySteeringDirection lastAutomaticDirection;
    private int disposed;

    internal HighwayAccessibilityCoordinator(
        AccessibilityConfig config,
        ILegacyAddressSpace addressSpace,
        string modDirectory,
        Action<string, bool> speak,
        Action<string> log)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(addressSpace);
        combatReader = new HighwayStateReader(addressSpace);
        roadReader = new HighwayRoadStateReader(addressSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        autoSteeringMode = new HighwayAutoSteeringModeTracker(config.EnableHighwayAutoSteering);
        autoSteeringController = HighwayAutoSteeringController.CreateCurrentProcess(addressSpace);
        composer = new HighwayAccessibilityComposer(
            new HighwayAccessibilityTracker(
                TimeSpan.FromMilliseconds(Math.Max(0, config.HighwayEnemyCueIntervalMs)),
                TimeSpan.FromMilliseconds(Math.Max(0, config.HighwayTruckBeaconIntervalMs)),
                Math.Max(0, config.HighwayComfortableTruckDistanceUnits),
                Math.Max(0, config.HighwayTruckThreatDistanceUnits),
                Math.Max(0, config.HighwayDistanceWarningUnits),
                Math.Max(0, config.HighwayDistanceWarningRecoveryUnits)),
            new HighwaySteeringTracker(
                TimeSpan.FromMilliseconds(Math.Max(0, config.HighwaySteeringCueIntervalMs)),
                TimeSpan.FromMilliseconds(Math.Max(0, config.HighwayCriticalSteeringCueIntervalMs))),
            new HighwayEngagementSteeringTracker(
                Math.Max(0, config.HighwayComfortableTruckDistanceUnits),
                Math.Max(0, config.HighwayTruckThreatDistanceUnits)));

        if (config.EnableHighwayAccessibility)
        {
            lowerPriorityPlayer = new NavigationBeaconPlayer(
                ResolveConfiguredPath(
                    modDirectory,
                    config.HighwayLowerPriorityCueSoundPath,
                    @"Assets\highway\enemy_lower_priority_058.wav"),
                config.HighwayCueVolumePercent,
                log);
            importantPlayer = new NavigationBeaconPlayer(
                ResolveConfiguredPath(
                    modDirectory,
                    config.HighwayImportantCueSoundPath,
                    @"Assets\highway\enemy_important_059_short.wav"),
                config.HighwayCueVolumePercent,
                log);
            truckPlayer = new NavigationBeaconPlayer(
                ResolveConfiguredPath(
                    modDirectory,
                    config.HighwayTruckBeaconSoundPath,
                    @"Assets\highway\truck_beacon_478.wav"),
                config.HighwayCueVolumePercent,
                log);
            if (config.EnableHighwaySteeringGuidance)
            {
                steeringPlayer = new NavigationBeaconPlayer(
                    ResolveConfiguredPath(
                        modDirectory,
                        config.HighwaySteeringCueSoundPath,
                        @"Assets\navigation\navigation_beacon_214_remix.wav"),
                    config.HighwayCueVolumePercent,
                    log);
            }
        }

        log(
            "Highway accessibility initialized from checked native module-6 state: " +
            $"enabled={config.EnableHighwayAccessibility}, " +
            $"autoSteering={config.EnableHighwayAutoSteering}, toggle=F8, " +
            $"enemyIntervalMs={Math.Max(0, config.HighwayEnemyCueIntervalMs)}, " +
            $"truckIntervalMs={Math.Max(0, config.HighwayTruckBeaconIntervalMs)}, " +
            $"steering={config.EnableHighwaySteeringGuidance}, " +
            $"steeringIntervalMs={Math.Max(0, config.HighwaySteeringCueIntervalMs)}/" +
            $"{Math.Max(0, config.HighwayCriticalSteeringCueIntervalMs)}, " +
            $"comfortable={Math.Max(0, config.HighwayComfortableTruckDistanceUnits)}, " +
            $"warning={Math.Max(0, config.HighwayDistanceWarningUnits)}/" +
            $"{Math.Max(0, config.HighwayDistanceWarningRecoveryUnits)}; " +
            "058=lower priority, shortened 059=important, 478=truck, " +
            "214=manual steering, F8=auto/manual, K=status.");
    }

    internal void Update(
        DateTime nowUtc,
        bool isHighway,
        bool isForeground,
        bool statusRequested,
        bool autoSteeringToggleRequested)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (!config.EnableHighwayAccessibility)
        {
            _ = autoSteeringMode.Observe(false, false, false);
            Reset("disabled");
            return;
        }

        if (!isHighway)
        {
            _ = autoSteeringMode.Observe(false, isForeground, false);
            Reset("motorcycle module is not active");
            return;
        }

        if (!isForeground)
        {
            _ = autoSteeringMode.Observe(true, false, false);
            Reset("game is not foreground");
            return;
        }

        HighwayAccessibilityState? combatState = null;
        HighwayRoadState? roadState = null;
        var combatAvailable = combatReader.TryRead(out var combatSnapshot);
        if (combatAvailable)
        {
            combatState = MapState(combatSnapshot);
            lastCombatFailureDiagnostic = string.Empty;
        }
        else
        {
            var diagnostic = combatReader.LastDiagnostic;
            if (!diagnostic.Contains("not highway", StringComparison.Ordinal) &&
                !string.Equals(diagnostic, lastCombatFailureDiagnostic, StringComparison.Ordinal))
            {
                lastCombatFailureDiagnostic = diagnostic;
                log($"Highway combat snapshot unavailable: {diagnostic}.");
            }

            StopCombatPlayers();
        }

        var roadAvailable = roadReader.TryRead(out var roadSnapshot);
        if (roadAvailable)
        {
            roadState = MapRoadState(roadSnapshot);
            lastRoadFailureDiagnostic = string.Empty;
        }
        else
        {
            var diagnostic = roadReader.LastDiagnostic;
            if (!diagnostic.Contains("not highway", StringComparison.Ordinal) &&
                !string.Equals(diagnostic, lastRoadFailureDiagnostic, StringComparison.Ordinal))
            {
                lastRoadFailureDiagnostic = diagnostic;
                log($"Highway road snapshot unavailable: {diagnostic}.");
            }

            steeringPlayer?.StopAll();
        }

        if (!combatAvailable && !roadAvailable)
        {
            Reset("native highway snapshots unavailable");
            return;
        }

        if (!active)
        {
            active = true;
            log("Highway accessibility acquired native module-6 ownership.");
        }

        var mode = autoSteeringMode.Observe(
            isHighway: true,
            isForeground: true,
            autoSteeringToggleRequested);
        var update = composer.Update(
            combatState,
            roadState,
            nowUtc,
            statusRequested,
            steeringAudioEnabled: config.EnableHighwaySteeringGuidance && !mode.Enabled);
        ApplyAutomaticDirection(
            mode.ShouldControl
                ? update.AutomaticDirection
                : HighwaySteeringDirection.None);

        if (mode.Announcement is { } announcement)
        {
            StopAll();
            try
            {
                speak(announcement, true);
                log($"Highway steering mode speech: {announcement}");
            }
            catch (Exception ex)
            {
                log($"Highway steering mode Prism speech failed: {ex.Message}");
            }

            return;
        }

        if (update.Speech is { } speech)
        {
            StopAll();
            try
            {
                speak(speech.Text, speech.Interrupt);
                log($"Highway {speech.Kind.ToString().ToLowerInvariant()} speech: {speech.Text}");
            }
            catch (Exception ex)
            {
                log($"Highway Prism speech failed: {ex.Message}");
            }

            return;
        }

        if (update.SteeringCue is { } steeringRequest)
        {
            if (CreateSteeringCue(steeringRequest) is not { } steeringCue)
            {
                return;
            }

            StopAll();
            if (steeringPlayer?.Play(steeringCue) == true)
            {
                log(
                    $"Highway steering cue: reason={steeringRequest.Reason}, " +
                    $"direction={steeringRequest.Direction}, " +
                    $"lateral={steeringRequest.CloudLateralUnits:0.0}, " +
                    $"halfWidth={steeringRequest.RoadHalfWidthUnits:0.0}, " +
                    $"ratio={steeringRequest.EdgeRatio:0.000}, " +
                    $"truckDelta=({steeringRequest.TruckDeltaLateral:0.0}," +
                    $"{steeringRequest.TruckDeltaLongitudinal:0.0}), " +
                    $"critical={steeringRequest.IsCritical}.");
            }

            return;
        }

        if (update.CombatCue is not { } request || CreateSpatialCue(request) is not { } cue)
        {
            return;
        }

        StopAll();
        var player = request.Kind switch
        {
            HighwayCueKind.LowerPriorityEnemy => lowerPriorityPlayer,
            HighwayCueKind.ImportantEnemy => importantPlayer,
            HighwayCueKind.TruckBeacon => truckPlayer,
            _ => null
        };
        if (player?.Play(cue) == true)
        {
            log(
                $"Highway spatial cue: kind={request.Kind}, slot={request.TargetSlot}, " +
                $"attackSide={request.AttackSide}, " +
                $"delta=({request.DeltaLateral:0.0},{request.DeltaLongitudinal:0.0}), " +
                $"distance={request.DistanceUnits:0.0}.");
        }
    }

    internal void Reset(string reason)
    {
        composer.Reset();
        ReleaseAutomaticDirection();
        StopAll();
        if (!active)
        {
            return;
        }

        active = false;
        log($"Highway accessibility released ownership: {reason}.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        composer.Reset();
        autoSteeringController.Dispose();
        lowerPriorityPlayer?.Dispose();
        importantPlayer?.Dispose();
        truckPlayer?.Dispose();
        steeringPlayer?.Dispose();
        active = false;
    }

    private void ApplyAutomaticDirection(HighwaySteeringDirection direction)
    {
        var result = autoSteeringController.Apply(direction);
        ObserveAutomaticInputResult(result);
        if (!result.Success || direction == lastAutomaticDirection)
        {
            return;
        }

        lastAutomaticDirection = direction;
        log($"Highway auto-steering direction: {direction}.");
    }

    private void ReleaseAutomaticDirection()
    {
        var result = autoSteeringController.ReleaseAll();
        ObserveAutomaticInputResult(result);
        if (result.Success)
        {
            lastAutomaticDirection = HighwaySteeringDirection.None;
        }
    }

    private void ObserveAutomaticInputResult(HighwayAutoSteeringInputResult result)
    {
        if (result.Success)
        {
            lastAutoSteeringFailureDiagnostic = string.Empty;
            return;
        }

        if (string.Equals(
                result.Diagnostic,
                lastAutoSteeringFailureDiagnostic,
                StringComparison.Ordinal))
        {
            return;
        }

        lastAutoSteeringFailureDiagnostic = result.Diagnostic;
        log($"Highway auto-steering input failed closed: {result.Diagnostic}.");
    }

    internal static HighwayAccessibilityState MapState(HighwayStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new HighwayAccessibilityState(
            new HighwayPoint(snapshot.Cloud.LateralUnits, snapshot.Cloud.LongitudinalUnits),
            new HighwayPoint(snapshot.Truck.LateralUnits, snapshot.Truck.LongitudinalUnits),
            Array.AsReadOnly(
                snapshot.Enemies.Select(enemy =>
                    new HighwayEnemyState(
                        enemy.Slot,
                        enemy.Type,
                        enemy.IsActive,
                        enemy.HitPoints,
                        new HighwayPoint(enemy.LateralUnits, enemy.LongitudinalUnits)))
                    .ToArray()),
            Array.AsReadOnly(
                snapshot.PartyHealth.Select(member =>
                    new HighwayPartyHealth(
                        member.Name,
                        member.CurrentHp,
                        member.MaximumHp))
                    .ToArray()),
            snapshot.Score,
            snapshot.IsStoryChase,
            snapshot.Cloud.AttackTimer);
    }

    internal static HighwayRoadState MapRoadState(HighwayRoadStateSnapshot snapshot) =>
        new(snapshot.CloudLateralUnits, snapshot.RoadHalfWidthUnits);

    internal static NavigationBeaconCue? CreateSteeringCue(HighwaySteeringCueRequest request)
    {
        const float diagonal = 0.70710677f;
        var (right, front, direction) = request.Direction switch
        {
            HighwaySteeringDirection.Left => (-1f, 0f, "steer left"),
            HighwaySteeringDirection.Right => (1f, 0f, "steer right"),
            HighwaySteeringDirection.Up => (0f, -1f, "steer up"),
            HighwaySteeringDirection.Down => (0f, 1f, "steer down"),
            HighwaySteeringDirection.UpLeft => (-diagonal, -diagonal, "steer up-left"),
            HighwaySteeringDirection.UpRight => (diagonal, -diagonal, "steer up-right"),
            HighwaySteeringDirection.DownLeft => (-diagonal, diagonal, "steer down-left"),
            HighwaySteeringDirection.DownRight => (diagonal, diagonal, "steer down-right"),
            _ => (0f, 0f, string.Empty)
        };
        if (direction.Length == 0 ||
            !double.IsFinite(request.CloudLateralUnits) ||
            !double.IsFinite(request.RoadHalfWidthUnits) ||
            !double.IsFinite(request.EdgeRatio) ||
            request.RoadHalfWidthUnits <= 0d)
        {
            return null;
        }

        return new NavigationBeaconCue(
            request.Reason == HighwaySteeringCueReason.TruckAvoidance
                ? "Car avoidance"
                : "Road",
            direction,
            right,
            front,
            right,
            0f,
            front,
            NavigationBeaconMovementState.Correcting,
            DurationMs: 160,
            DistanceUnits: Math.Abs(request.CloudLateralUnits));
    }

    internal static NavigationBeaconCue? CreateSpatialCue(HighwayCueRequest request)
    {
        float right;
        float front;
        string direction;
        if (request.Kind is HighwayCueKind.LowerPriorityEnemy or HighwayCueKind.ImportantEnemy)
        {
            (right, direction) = request.AttackSide switch
            {
                HighwayAttackSide.LeftSquare => (-1f, "left, Square"),
                HighwayAttackSide.RightCircle => (1f, "right, Circle"),
                _ => (0f, string.Empty)
            };
            if (direction.Length == 0)
            {
                return null;
            }

            front = 0f;
        }
        else
        {
            var vectorLength = Math.Sqrt(
                request.DeltaLateral * request.DeltaLateral +
                request.DeltaLongitudinal * request.DeltaLongitudinal);
            if (!double.IsFinite(vectorLength) || vectorLength <= double.Epsilon)
            {
                return null;
            }

            right = (float)Math.Clamp(request.DeltaLateral / vectorLength, -1d, 1d);
            front = (float)Math.Clamp(-request.DeltaLongitudinal / vectorLength, -1d, 1d);
            direction = "spatial";
        }
        var label = request.Kind switch
        {
            HighwayCueKind.LowerPriorityEnemy => "Biker",
            HighwayCueKind.ImportantEnemy => "Important biker",
            HighwayCueKind.TruckBeacon => "Truck",
            _ => "Highway target"
        };
        var duration = request.Kind switch
        {
            HighwayCueKind.LowerPriorityEnemy => 150,
            HighwayCueKind.ImportantEnemy => 280,
            HighwayCueKind.TruckBeacon => 62,
            _ => 150
        };
        return new NavigationBeaconCue(
            label,
            direction,
            right,
            front,
            right,
            0f,
            front,
            NavigationBeaconMovementState.OnCourse,
            duration,
            request.DistanceUnits);
    }

    private void StopAll()
    {
        StopCombatPlayers();
        steeringPlayer?.StopAll();
    }

    private void StopCombatPlayers()
    {
        lowerPriorityPlayer?.StopAll();
        importantPlayer?.StopAll();
        truckPlayer?.StopAll();
    }

    private static string ResolveConfiguredPath(
        string modDirectory,
        string? configuredPath,
        string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(configuredPath)
            ? fallback
            : configuredPath;
        return Path.IsPathRooted(selected)
            ? Path.GetFullPath(selected)
            : Path.GetFullPath(Path.Combine(modDirectory, selected));
    }
}
