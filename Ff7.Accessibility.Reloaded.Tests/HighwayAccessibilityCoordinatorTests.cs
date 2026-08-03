using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class HighwayAccessibilityCoordinatorTests
{
    internal static void Run()
    {
        MapsTheCheckedGuestSnapshotWithoutArchitectureState();
        MapsEnemyCuesToAttackButtonSidesAndKeepsTruckSpatial();
        MapsSteeringCuesToTheRequestedTwoDimensionalDirection();
        ComposesRoadAndCombatStateIndependently();
        PrioritizesAndRotatesSimultaneousHighwayInformation();
        PrioritizesTruckAvoidanceOverRoadAndCombatCues();
        KeepsCombatAudioWhenAutomaticSteeringSuppressesSteeringTones();
        EnablesSteeringWithTheApprovedDefaults();
        RejectsEnemyCueWithoutAKnownAttackSide();
        RejectsAZeroLengthSpatialVector();
    }

    private static void MapsTheCheckedGuestSnapshotWithoutArchitectureState()
    {
        var snapshot = new HighwayStateSnapshot(
            HighwayStateReader.HighwayModule,
            Actor(
                0,
                lateralFixed: 2560,
                longitudinalFixed: 5120,
                hp: 100,
                type: 0,
                attackTimer: 17),
            Actor(1, lateralFixed: -1280, longitudinalFixed: 8192, hp: 100, type: 0),
            Array.AsReadOnly(
            [
                Actor(2, lateralFixed: 512, longitudinalFixed: 1024, hp: 5, type: 10),
                Actor(3, lateralFixed: 768, longitudinalFixed: 1280, hp: 0, type: 12)
            ]),
            Array.AsReadOnly(
            [
                new HighwayPartyHealthSnapshot(0, "Cloud", 700, 900),
                new HighwayPartyHealthSnapshot(1, "Barret", 610, 650)
            ]),
            Score: 3210,
            IsStoryChase: true);

        var state = HighwayAccessibilityCoordinator.MapState(snapshot);
        Equal(new HighwayPoint(10, 20), state.Cloud, "decoded Cloud point");
        Equal(new HighwayPoint(-5, 32), state.Truck, "decoded truck point");
        Equal(2, state.Enemies.Count, "mapped enemy count");
        Equal(true, state.Enemies[0].IsActive, "mapped live enemy state");
        Equal(false, state.Enemies[1].IsActive, "mapped defeated enemy state");
        Equal(2, state.PartyHealth.Count, "mapped visible party health");
        Equal(3210, state.Score, "mapped score");
        Equal(17, state.CloudAttackTimer, "mapped native Cloud attack timer");
    }

    private static void MapsEnemyCuesToAttackButtonSidesAndKeepsTruckSpatial()
    {
        var square = HighwayAccessibilityCoordinator.CreateSpatialCue(
            new HighwayCueRequest(
                HighwayCueKind.ImportantEnemy,
                TargetSlot: 2,
                DeltaLateral: 3,
                DeltaLongitudinal: 4,
                DistanceUnits: 5,
                HighwayAttackSide.LeftSquare));
        Equal(true, square.HasValue, "Square cue exists");
        Near(-1f, square!.Value.SteamAudioX, "Square cue is hard left");
        Near(0f, square.Value.SteamAudioZ, "Square cue has no front/rear ambiguity");
        Near(0f, square.Value.StickY, "Square cue avoids the rear-marker path");

        var circle = HighwayAccessibilityCoordinator.CreateSpatialCue(
            new HighwayCueRequest(
                HighwayCueKind.LowerPriorityEnemy,
                TargetSlot: 3,
                DeltaLateral: -3,
                DeltaLongitudinal: -4,
                DistanceUnits: 5,
                HighwayAttackSide.RightCircle));
        Equal(true, circle.HasValue, "Circle cue exists");
        Near(1f, circle!.Value.SteamAudioX, "Circle cue is hard right");
        Near(0f, circle.Value.SteamAudioZ, "Circle cue has no front/rear ambiguity");
        Near(0f, circle.Value.StickY, "Circle cue avoids the rear-marker path");

        var rearLeft = HighwayAccessibilityCoordinator.CreateSpatialCue(
            new HighwayCueRequest(
                HighwayCueKind.TruckBeacon,
                TargetSlot: 1,
                DeltaLateral: -3,
                DeltaLongitudinal: -4,
                DistanceUnits: 5));
        Near(-0.6f, rearLeft!.Value.SteamAudioX, "rear-left Steam Audio X");
        Near(0.8f, rearLeft.Value.SteamAudioZ, "behind maps to positive Steam Audio Z");
        Near(0.8f, rearLeft.Value.StickY, "behind enables the native rear marker");
    }

    private static void RejectsEnemyCueWithoutAKnownAttackSide()
    {
        Equal(
            null,
            HighwayAccessibilityCoordinator.CreateSpatialCue(
                new HighwayCueRequest(
                    HighwayCueKind.ImportantEnemy,
                    TargetSlot: 2,
                    DeltaLateral: 3,
                    DeltaLongitudinal: 4,
                    DistanceUnits: 5)),
            "enemy cue without a verified button side fails closed");
    }

    private static void MapsSteeringCuesToTheRequestedTwoDimensionalDirection()
    {
        var left = HighwayAccessibilityCoordinator.CreateSteeringCue(
            new HighwaySteeringCueRequest(
                HighwaySteeringDirection.Left,
                CloudLateralUnits: 40,
                RoadHalfWidthUnits: 100,
                EdgeRatio: 0.4,
                IsCritical: false));
        Equal(true, left.HasValue, "left steering cue exists");
        Near(-1f, left!.Value.SteamAudioX, "left steering cue is hard left");
        Near(0f, left.Value.SteamAudioZ, "left steering cue has no front/rear ambiguity");
        Equal("steer left", left.Value.Direction, "left steering direction label");
        Equal(160, left.Value.DurationMs, "steering tone duration");

        var right = HighwayAccessibilityCoordinator.CreateSteeringCue(
            new HighwaySteeringCueRequest(
                HighwaySteeringDirection.Right,
                CloudLateralUnits: -75,
                RoadHalfWidthUnits: 100,
                EdgeRatio: 0.75,
                IsCritical: true));
        Equal(true, right.HasValue, "right steering cue exists");
        Near(1f, right!.Value.SteamAudioX, "right steering cue is hard right");
        Equal("steer right", right.Value.Direction, "right steering direction label");

        var down = HighwayAccessibilityCoordinator.CreateSteeringCue(
            new HighwaySteeringCueRequest(
                HighwaySteeringDirection.Down,
                CloudLateralUnits: 0,
                RoadHalfWidthUnits: 100,
                EdgeRatio: 0,
                IsCritical: true,
                HighwaySteeringCueReason.TruckAvoidance,
                TruckDeltaLateral: 0,
                TruckDeltaLongitudinal: 200));
        Equal(true, down.HasValue, "down steering cue exists");
        Near(0f, down!.Value.SteamAudioX, "down steering cue is centered");
        Near(1f, down.Value.SteamAudioZ, "down steering cue uses the rear marker");
        Equal("steer down", down.Value.Direction, "down steering direction label");

        var upRight = HighwayAccessibilityCoordinator.CreateSteeringCue(
            new HighwaySteeringCueRequest(
                HighwaySteeringDirection.UpRight,
                CloudLateralUnits: 0,
                RoadHalfWidthUnits: 100,
                EdgeRatio: 0,
                IsCritical: true,
                HighwaySteeringCueReason.TruckAvoidance,
                TruckDeltaLateral: -80,
                TruckDeltaLongitudinal: -200));
        Equal(true, upRight.HasValue, "up-right steering cue exists");
        Near(0.7071f, upRight!.Value.SteamAudioX, "up-right steering cue pans right");
        Near(-0.7071f, upRight.Value.SteamAudioZ, "up-right steering cue is in front");
        Equal("steer up-right", upRight.Value.Direction, "up-right steering direction label");

        Equal(
            null,
            HighwayAccessibilityCoordinator.CreateSteeringCue(
                new HighwaySteeringCueRequest(
                    HighwaySteeringDirection.None,
                    0,
                    100,
                    0,
                    false)),
            "steering cue without a requested direction fails closed");
    }

    private static void ComposesRoadAndCombatStateIndependently()
    {
        var now = UtcNow();
        var roadOnly = CreateComposer().Update(
            combatState: null,
            roadState: new HighwayRoadState(40, 100),
            now,
            statusRequested: false);
        Equal(
            HighwaySteeringDirection.Left,
            roadOnly.SteeringCue?.Direction,
            "road guidance survives an unavailable combat snapshot");
        Equal(
            HighwaySteeringDirection.Left,
            roadOnly.AutomaticDirection,
            "road-only update carries automatic direction");
        Equal(null, roadOnly.CombatCue, "road-only update has no fabricated combat cue");

        var combatOnly = CreateComposer().Update(
            CombatState(EnemyState(2, 160, 200), truckLongitudinal: 450),
            roadState: null,
            now,
            statusRequested: false);
        Equal(
            HighwayCueKind.ImportantEnemy,
            combatOnly.CombatCue?.Kind,
            "combat cues survive an unavailable road snapshot");
        Equal(null, combatOnly.SteeringCue, "combat-only update has no fabricated road cue");
        Equal(
            HighwaySteeringDirection.UpRight,
            combatOnly.AutomaticDirection,
            "combat coordinates keep approaching an attackable biker when the road snapshot is unavailable");
    }

    private static void PrioritizesAndRotatesSimultaneousHighwayInformation()
    {
        var now = UtcNow();
        var important = CombatState(EnemyState(2, 80, 100));

        var speechComposer = CreateComposer();
        var speech = speechComposer.Update(
            important,
            new HighwayRoadState(80, 100),
            now,
            statusRequested: true);
        Equal(HighwaySpeechKind.Status, speech.Speech?.Kind, "speech owns its update");
        Equal(null, speech.SteeringCue, "speech suppresses simultaneous steering");
        Equal(null, speech.CombatCue, "speech suppresses simultaneous combat audio");
        Equal(
            HighwaySteeringDirection.Left,
            speech.AutomaticDirection,
            "status speech does not interrupt continuous steering direction");

        var criticalComposer = CreateComposer();
        var critical = criticalComposer.Update(
            important,
            new HighwayRoadState(80, 100),
            now,
            statusRequested: false);
        Equal(true, critical.SteeringCue?.IsCritical, "critical steering precedes an important biker");
        Equal(null, critical.CombatCue, "critical steering owns one sound slot");

        var importantComposer = CreateComposer();
        var moderate = importantComposer.Update(
            important,
            new HighwayRoadState(30, 100),
            now,
            statusRequested: false);
        Equal(HighwayCueKind.ImportantEnemy, moderate.CombatCue?.Kind, "important biker precedes moderate steering");
        Equal(null, moderate.SteeringCue, "important biker owns one sound slot");

        var rotatingComposer = CreateComposer();
        var lower = CombatState(EnemyState(2, 300, 0), truckLongitudinal: 450);
        var first = rotatingComposer.Update(lower, new HighwayRoadState(30, 100), now, false);
        var second = rotatingComposer.Update(lower, new HighwayRoadState(30, 100), now.AddMilliseconds(1), false);
        var third = rotatingComposer.Update(lower, new HighwayRoadState(30, 100), now.AddMilliseconds(2), false);
        Equal(true, first.SteeringCue is not null, "moderate rotation starts with steering");
        Equal(HighwayCueKind.LowerPriorityEnemy, second.CombatCue?.Kind, "moderate rotation advances to biker");
        Equal(true, third.SteeringCue is not null, "moderate rotation returns to steering");
    }

    private static void PrioritizesTruckAvoidanceOverRoadAndCombatCues()
    {
        var now = UtcNow();
        var update = CreateComposer().Update(
            CombatState(EnemyState(2, 80, 100), truckLongitudinal: 200),
            new HighwayRoadState(80, 100),
            now,
            statusRequested: false);

        Equal(
            HighwaySteeringCueReason.TruckAvoidance,
            update.SteeringCue?.Reason,
            "truck avoidance owns the steering output");
        Equal(HighwaySteeringDirection.Down, update.SteeringCue?.Direction, "edge-safe truck direction");
        Equal(null, update.CombatCue, "truck collision avoidance suppresses a simultaneous biker cue");
    }

    private static void KeepsCombatAudioWhenAutomaticSteeringSuppressesSteeringTones()
    {
        var update = CreateComposer().Update(
            CombatState(EnemyState(2, 80, 100)),
            new HighwayRoadState(80, 100),
            UtcNow(),
            statusRequested: false,
            steeringAudioEnabled: false);

        Equal(null, update.SteeringCue, "automatic mode suppresses the steering tone");
        Equal(
            HighwayCueKind.ImportantEnemy,
            update.CombatCue?.Kind,
            "automatic mode preserves the important enemy tone");
        Equal(
            HighwaySteeringDirection.Left,
            update.AutomaticDirection,
            "automatic mode still carries the critical road correction");
    }

    private static void EnablesSteeringWithTheApprovedDefaults()
    {
        var config = new AccessibilityConfig();
        Equal(true, config.EnableHighwayAutoSteering, "auto steering default enabled");
        var preservedLegacyConfig = System.Text.Json.JsonSerializer.Deserialize<AccessibilityConfig>("{}");
        Equal(
            true,
            preservedLegacyConfig?.EnableHighwayAutoSteering,
            "preserved configuration without the new property inherits auto steering enabled");
        Equal(true, config.EnableHighwaySteeringGuidance, "steering guidance default enabled");
        Equal(700, config.HighwaySteeringCueIntervalMs, "normal steering interval default");
        Equal(260, config.HighwayCriticalSteeringCueIntervalMs, "critical steering interval default");
        Equal(
            @"Assets\navigation\navigation_beacon_214_remix.wav",
            config.HighwaySteeringCueSoundPath,
            "approved steering tone default");
    }

    private static void RejectsAZeroLengthSpatialVector()
    {
        Equal(
            null,
            HighwayAccessibilityCoordinator.CreateSpatialCue(
                new HighwayCueRequest(
                    HighwayCueKind.TruckBeacon,
                    TargetSlot: 1,
                    DeltaLateral: 0,
                    DeltaLongitudinal: 0,
                    DistanceUnits: 0)),
            "zero-length target has no fabricated direction");
    }

    private static HighwayActorSnapshot Actor(
        int slot,
        int lateralFixed,
        int longitudinalFixed,
        int hp,
        int type,
        int attackTimer = 0) =>
        new(
            slot,
            State: hp > 0 ? 0 : 2,
            SecondaryState: 0,
            lateralFixed,
            longitudinalFixed,
            hp,
            type,
            attackTimer);

    private static HighwayAccessibilityComposer CreateComposer() =>
        new(
            new HighwayAccessibilityTracker(
                enemyCueInterval: TimeSpan.Zero,
                truckCueInterval: TimeSpan.Zero,
                comfortableTruckDistance: 500,
                truckThreatDistance: 300,
                warningDistance: 1200,
                warningRecoveryDistance: 900),
            new HighwaySteeringTracker(
                normalCueInterval: TimeSpan.Zero,
                criticalCueInterval: TimeSpan.Zero),
            new HighwayEngagementSteeringTracker(
                comfortableTruckDistance: 500,
                truckThreatDistance: 300));

    private static HighwayAccessibilityState CombatState(
        HighwayEnemyState enemy,
        double truckLongitudinal = 800) =>
        new(
            new HighwayPoint(0, 0),
            new HighwayPoint(0, truckLongitudinal),
            [enemy],
            Array.Empty<HighwayPartyHealth>(),
            Score: 0,
            IsStoryChase: true);

    private static HighwayEnemyState EnemyState(int slot, double lateral, double longitudinal) =>
        new(
            slot,
            NativeType: 10,
            IsActive: true,
            HitPoints: 5,
            new HighwayPoint(lateral, longitudinal));

    private static DateTime UtcNow() =>
        new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    private static void Near(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.001f)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
