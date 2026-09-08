using Ff7.Accessibility.Reloaded;

internal static class FieldNavigationNativeProbeMovementTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var mesh = createWalkmeshReader(453).Read(new(1, 453, 0, 137, 167, -10, 7, 192)).Walkmesh;
        if (mesh is null) throw new InvalidOperationException("Installed native room mesh must be available.");
        NativeWallRetriesKeepTheProvenShortStep(mesh);
        WalkingSpeedDoesNotComeFromTheSteeringHorizon(mesh);
        PreciseStartingPositionPreservesNativeCollisionDecisions(mesh);
        PreciseHeadingAndRoundedEightUnitInputsMatchNativeEvidence(mesh);
        ModelCollisionBailsOutBeforeWallRecovery(mesh);
        ForwardWallDoesNotClampOrRotateThePlayer(mesh);
        NativeIntegerModelRadiusRemainsStrict(mesh);
        BoundsAndUnsupportedSurfacesFailClosed(mesh);
        ReplayIndependentNativeWitnesses(mesh);
    }

    private static FieldNavigationDynamicObstacle[] Residents(bool womanNear, int grandfatherY = -3) =>
    [
        new(3, womanNear ? 113 : -69, womanNear ? 211 : 195, 0, 27.5, 30),
        new(4, 119, grandfatherY, 0, 30, 30)
    ];

    private static FieldNavigationNativeMovementState Start(int x = 137, int y = 167,
        int triangle = 7, int z = -10) => new(x * 4096, y * 4096, z * 4096, triangle, 192);

    private static void NativeWallRetriesKeepTheProvenShortStep(FieldWalkmesh mesh)
    {
        // Exact values from the frozen, independently executed Python native
        // emulator, installed trig table and captured room mesh.
        var result = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 96, 1024, 30, Residents(false));
        Check(result.IsSupported && result.Moved && result.AttemptCount == 3 &&
              result.CandidateHeading == 112 && result.State.FixedX == 567420 &&
              result.State.FixedY == 699168 && result.State.FixedZ == 0 && result.State.TriangleId == 7,
            "A native right-wall retry must accept heading112 at exact fixed (567420,699168), not veto the requested diagonal.");
        result = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 96, 2048, 30, Residents(false));
        Check(result.Moved && result.AttemptCount == 4 && result.CandidateHeading == 120 &&
              result.State.FixedX == 567544 && result.State.FixedY == 716168,
            "The eight-unit native step has its own four-attempt numeric witness.");
        result = FieldNavigationNativeProbeMovement.Step(mesh, Start(98, 160, z: 0), 192, 1024, 30, Residents(true));
        Check(result.Moved && result.AttemptCount == 4 && result.CandidateHeading == 168 &&
              result.State.FixedX == 387788 && result.State.FixedY == 664460,
            "The captured controller stall at98,160 must permit the native Left-to-heading168 retry.");
        Check(FieldNavigationNativeProbeMovement.IsClear(mesh, 7, new(98, 160, 0), new(90, 160, 0), 30, Residents(true)),
            "Short-input feasibility must retain the independently proven native wall retry.");
    }

    private static void ModelCollisionBailsOutBeforeWallRecovery(FieldWalkmesh mesh)
    {
        var start = Start(-64, 248, 10);
        var result = FieldNavigationNativeProbeMovement.Step(mesh, start, 0, 1024, 30, Residents(false));
        Check(result.IsSupported && !result.Moved && result.AttemptCount == 1 &&
              result.State.FixedX == start.FixedX && result.State.FixedY == start.FixedY &&
              result.State.FixedZ == start.FixedZ,
            "Native player/model collision must reject immediately and preserve fixed XYZ.");
    }

    private static void PreciseHeadingAndRoundedEightUnitInputsMatchNativeEvidence(FieldWalkmesh mesh)
    {
        var residents = Residents(false);
        // The installed native table at heading52 rounds an eight-unit proposal
        // to(8,-2). Re-deriving an angle from those integers gives54 instead.
        // Frozen Python evidence:52 accepts in six attempts at heading12, while
        //54 exhausts sixteen attempts at native walking speed1024.
        var exact = FieldNavigationNativeProbeMovement.Step(mesh, Start(z: 0), 52, 1024, 30, residents);
        var altered = FieldNavigationNativeProbeMovement.Step(mesh, Start(z: 0), 54, 1024, 30, residents);
        Check(exact.Moved && exact.AttemptCount == 6 && exact.CandidateHeading == 12 &&
              exact.State.FixedX == 565908 && exact.State.FixedY == 668356 &&
              !altered.Moved && altered.AttemptCount == 16,
            "The independently pinned native heading52/54 difference must remain observable.");
        Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 7, new(137, 167, 0), new(145, 165, 0), 30, residents),
            "Legacy callers without a precise heading retain the conservative rounded-vector fallback.");
        var preciseClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
            new(137, 167, 0), new(145, 165, 0), 30, residents, requestedHeading: 52);

        // Native heading103 has table pair(2358,-3348), so the exact eight-unit
        // movement(4.60546875,6.5390625) rounds to(5,7), whose length is sqrt74.
        exact = FieldNavigationNativeProbeMovement.Step(mesh, Start(-150, 100, 11, 0), 103, 2048, 30, residents);
        Check(exact.Moved && exact.AttemptCount == 1 && exact.State.FixedX == -595536 && exact.State.FixedY == 436384,
            "The rounded(5,7) input must retain its independent native eight-unit numeric witness.");
        var roundedClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 11,
            new(-150, 100, 0), new(-145, 107, 0), 30, residents, requestedHeading: 103);
        Check(preciseClear && roundedClear,
            $"Precise native heading and rounded-eight proposals must remain usable: precise={preciseClear}, roundedEight={roundedClear}.");
        Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 11,
                new(-150, 100, 0), new(-141, 100, 0), 30, residents, requestedHeading: 64),
            "The rounding allowance must still reject a genuinely longer nine-unit input.");
    }

    private static void WalkingSpeedDoesNotComeFromTheSteeringHorizon(FieldWalkmesh mesh)
    {
        var residents = Residents(true, 107);
        // Native no-Run speed is scale512*2=1024 in the ordinary walking mode.
        // The controller's eight-unit lookahead is not a single native tick.
        var walking = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 224, 1024, 30, residents);
        var doubled = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 224, 2048, 30, residents);
        Check(walking.Moved && walking.AttemptCount == 1 && walking.State.FixedX == 549568 &&
              walking.State.FixedY == 672448 && !doubled.Moved && doubled.AttemptCount == 1,
            "The grandfather107 phase must retain the independent walking-clear/eight-unit-blocked witness.");
        var normalWalkingClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
            new(137, 167, -10), new(131, 161, 0), 30, residents, requestedHeading: 224);

        // A one-unit waypoint likewise does not reduce native walking speed.
        // The short fictional tick is clear; the real four-unit tick hits model3.
        FieldNavigationDynamicObstacle[] closeModel = [new(3, 137, 107, 0, 27.5, 30)];
        var shortened = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 0, 256, 30, closeModel);
        walking = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 0, 1024, 30, closeModel);
        Check(shortened.Moved && !walking.Moved,
            "The short-waypoint fixture must distinguish an artificial one-unit tick from native walking.");
        var shortWaypointClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
            new(137, 167, -10), new(137, 166, 0), 30, closeModel, requestedHeading: 0);
        Check(normalWalkingClear && !shortWaypointClear,
            $"Immediate feasibility must use normal walking regardless of waypoint horizon: walking={normalWalkingClear}, shortened={shortWaypointClear}.");
        Check(FieldNavigationNativeProbeMovement.IsClear(mesh, 11,
                new(-150, 100, 0), new(-149, 100, 0), 30, residents, requestedHeading: 64),
            "A short waypoint still permits a real clear native walking step.");
    }

    private static void PreciseStartingPositionPreservesNativeCollisionDecisions(FieldWalkmesh mesh)
    {
        // Fresh independent Python evidence pins the ordinary entry's second
        // Down tick. Reconstructing visible(138,163) discards its legal fraction.
        var ordinaryResidents = Residents(true, 107);
        var ordinaryPosition = new FieldNavigationFixedPosition(567420, 668896, 0);
        var ordinaryState = new FieldNavigationNativeMovementState(
            ordinaryPosition.X, ordinaryPosition.Y, ordinaryPosition.Z, 7, 0);
        var exact = FieldNavigationNativeProbeMovement.Step(mesh, ordinaryState, 0, 1024, 30, ordinaryResidents);
        var rounded = FieldNavigationNativeProbeMovement.Step(mesh, Start(138, 163, z: 0), 0, 1024, 30, ordinaryResidents);
        Check(exact.Moved && exact.AttemptCount == 1 && exact.State.FixedX == 567420 &&
              exact.State.FixedY == 652512 && !rounded.Moved && rounded.AttemptCount == 1,
            "The ordinary exact second Down must be legal while its integer reconstruction hits a resident.");
        Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
                new(138, 163, 0), new(138, 159, 0), 30, ordinaryResidents, requestedHeading: 0),
            "Callers without precision retain the existing integer-observation behavior.");
        var ordinaryClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
            new(138, 163, 0), new(138, 159, 0), 30, ordinaryResidents, requestedHeading: 0,
            nativeFixedPosition: ordinaryPosition);

        // The captured stress case has the opposite error: its real upper
        // probe hits the woman, while the reconstructed integer probe misses.
        var residents = Residents(true);
        var fractionalPosition = new FieldNavigationFixedPosition(395328, 649280, 0);
        var fractionalState = new FieldNavigationNativeMovementState(
            fractionalPosition.X, fractionalPosition.Y, fractionalPosition.Z, 7, 160);
        exact = FieldNavigationNativeProbeMovement.Step(mesh, fractionalState, 160, 1024, 30, residents);
        rounded = FieldNavigationNativeProbeMovement.Step(mesh, Start(96, 158, z: 0), 160, 1024, 30, residents);
        Check(!exact.Moved && exact.AttemptCount == 1 && rounded.Moved && rounded.AttemptCount == 1 &&
              rounded.State.FixedX == 381632 && rounded.State.FixedY == 658752,
            "The real fractional UpLeft must reject the resident collision masked by integer reconstruction.");
        Check(FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
                new(96, 158, 0), new(90, 164, 0), 30, residents, requestedHeading: 160),
            "The regression must retain the independently demonstrated integer false acceptance.");
        var fractionalClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
            new(96, 158, 0), new(90, 164, 0), 30, residents, requestedHeading: 160,
            nativeFixedPosition: fractionalPosition);
        exact = FieldNavigationNativeProbeMovement.Step(mesh, fractionalState, 192, 1024, 30, residents);
        Check(exact.Moved && exact.AttemptCount == 4 && exact.State.FixedX == 381708 && exact.State.FixedY == 658380,
            "The same precise state retains its independently proven native Left retry.");
        var leftClear = FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
            new(96, 158, 0), new(88, 158, 0), 30, residents, requestedHeading: 192,
            nativeFixedPosition: fractionalPosition);
        Check(ordinaryClear && !fractionalClear && leftClear,
            $"Precise native starting position must preserve both collision decisions: ordinary={ordinaryClear}, fractional={fractionalClear}, left={leftClear}.");

        FieldNavigationFixedPosition[] stalePositions =
        [
            fractionalPosition with { X = fractionalPosition.X + 4096 },
            fractionalPosition with { Y = fractionalPosition.Y + 4096 },
            fractionalPosition with { Z = fractionalPosition.Z + 4096 }
        ];
        foreach (var stale in stalePositions)
        {
            Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 7,
                    new(96, 158, 0), new(88, 158, 0), 30, residents, requestedHeading: 192,
                    nativeFixedPosition: stale),
                "Precision from another XYZ observation must fail closed instead of silently using integer coordinates.");
        }
        Check(FieldNavigationNativeProbeMovement.IsClear(mesh, 11,
                new(-151, 100, 0), new(-143, 100, 0), 30, residents, requestedHeading: 64,
                nativeFixedPosition: new(-615424, 410624, 0)),
            "Negative native fractions must match the snapshot's arithmetic shift, not truncate toward zero.");
    }

    private static void ForwardWallDoesNotClampOrRotateThePlayer(FieldWalkmesh mesh)
    {
        var start = Start();
        var result = FieldNavigationNativeProbeMovement.Step(mesh, start, 64, 1024, 30, Residents(false));
        Check(result.IsSupported && !result.Moved && result.AttemptCount == 16 &&
              result.CandidateHeading == 64 && result.State.FixedX == start.FixedX &&
              result.State.FixedY == start.FixedY && result.State.FixedZ == start.FixedZ,
            "Forward-only wall failure must exhaust sixteen attempts without invented slide or clamp.");
        result = FieldNavigationNativeProbeMovement.Step(mesh, start, 64, 1024, 30, Residents(false), lineRetryActive: true);
        Check(!result.Moved && result.AttemptCount == 2,
            "The explicit native line-retry state must retain its two-attempt bound.");
        result = FieldNavigationNativeProbeMovement.Step(mesh, Start(98, 160, z: 0), 192, 1024, 30,
            Residents(true), isTriangleBlocked: triangle => triangle == 9);
        Check(!result.Moved, "Live native triangle closure cannot be bypassed by wall-angle retries.");
    }

    private static void NativeIntegerModelRadiusRemainsStrict(FieldWalkmesh mesh)
    {
        FieldNavigationDynamicObstacle[] atBoundary = [new(3, 137, 106, 0, 27.5, 30)];
        FieldNavigationDynamicObstacle[] inside = [new(3, 137, 107, 0, 27.5, 30)];
        var result = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 0, 1024, 30, atBoundary);
        Check(result.Moved && result.AttemptCount == 1 && result.State.FixedY == 667648,
            "Widths30/25 produce strict native integer radius27; exactly27 is clear.");
        result = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 0, 1024, 30, inside);
        Check(!result.Moved && result.AttemptCount == 1,
            "The otherwise identical native model26 units from the forward probe must block.");
        // Installed00637724 tests -0x7f < dz && dz < 0x80. The lower and upper
        // endpoints are intentionally asymmetric; this is not |dz| <=127.
        (int Height, bool CanMove)[] boundaries = [(-127, true), (-126, false), (127, false), (128, true)];
        foreach (var boundary in boundaries)
        {
            FieldNavigationDynamicObstacle[] elevated = [new(3, 137, 107, boundary.Height, 27.5, 30)];
            result = FieldNavigationNativeProbeMovement.Step(mesh, Start(), 0, 1024, 30, elevated);
            Check(result.IsSupported && result.Moved == boundary.CanMove && result.AttemptCount == 1,
                $"Native signed model-height boundary{boundary.Height} must preserve -127 < dz <128 exactly.");
        }
    }

    private static void BoundsAndUnsupportedSurfacesFailClosed(FieldWalkmesh mesh)
    {
        Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 7, new(98, 160, 0), new(66, 180, 0), 30, Residents(true)),
            "Wall retry feasibility must never rotate a long continuation leg.");
        Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 7, new(98, 160, 0), new(98, 160, 0), 30, Residents(true)),
            "A zero movement is not proven forward movement.");
        Check(!FieldNavigationNativeProbeMovement.IsClear(mesh, 7, new(98, 160, 0), new(90, 160, 0), double.NaN, Residents(true)),
            "Invalid native collision radius must fail closed.");
        var triangles = mesh.Triangles.ToArray();
        triangles[0] = triangles[0] with { Vertex0 = triangles[0].Vertex0 with { Z = 1 } };
        var sloped = new FieldWalkmesh(triangles);
        Check(!FieldNavigationNativeProbeMovement.IsSupportedWalkmesh(sloped) &&
              !FieldNavigationNativeProbeMovement.Step(sloped, Start(), 96, 1024, 30, Residents(false)).IsSupported,
            "The verified flat native subset must not claim support for slope physics.");
    }

    private static void ReplayIndependentNativeWitnesses(FieldWalkmesh mesh)
    {
        foreach (var witness in Witnesses)
        {
            var state = Start();
            var inputs = Convert.FromBase64String(witness.Inputs);
            var checkpointIndex = 0;
            for (var index = 0; index < inputs.Length; index++)
            {
                var heading = inputs[index] switch
                {
                    1 => 128, 2 => 96, 3 => 64, 4 => 32,
                    5 => 0, 6 => 224, 7 => 192, 8 => 160,
                    _ => throw new InvalidOperationException("Invalid frozen witness input.")
                };
                var result = FieldNavigationNativeProbeMovement.Step(mesh, state, (byte)heading,
                    1024, 30, Residents(true, witness.GrandfatherY));
                Check(result.IsSupported && result.Moved,
                    $"Independent Python native witness must retain accepted movement{index}, grandfather{witness.GrandfatherY}.");
                state = result.State;
                if (checkpointIndex < witness.Checkpoints.Length &&
                    witness.Checkpoints[checkpointIndex].Step == index + 1)
                {
                    var expected = witness.Checkpoints[checkpointIndex++];
                    Check(state.FixedX == expected.X && state.FixedY == expected.Y &&
                          state.FixedZ == 0 && state.TriangleId == expected.Triangle,
                        $"Independent native fixed-point checkpoint{expected.Step} must match exactly, grandfather{witness.GrandfatherY}.");
                }
            }
            Check(checkpointIndex == witness.Checkpoints.Length && state.TriangleId == 1 &&
                  (state.FixedY >> 12) < -217 && (state.FixedX >> 12) > -75 && (state.FixedX >> 12) < 23,
                "The replay must cross the native exit segment, not merely finish near its first waypoint.");
        }
    }

    private readonly record struct Checkpoint(int Step, int X, int Y, int Triangle);
    private sealed record Witness(int GrandfatherY, string Inputs, Checkpoint[] Checkpoints);

    // Frozen Python accepted-input chains, with exact fixed-position checks
    // every16 movements and at the exit. These predate this C# implementation.
    private static readonly Witness[] Witnesses =
    [
        new(-3, "BQYFBgUGBQYFBgUGBQYFBgUGBQYFBgYGBgYGBQUFBQUCCAYFCAgBAQEBAQEBAQEBAQEBAQEBAQEBBwcFBQcFBwcHBQUHBwcFBQUCBwcFBQcFBwcHBwcHBgQFBQUFBwcHBQcFBwcGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBQUFBQUFBQQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAMDAwMDAwMEBAQEBAQEBAQEBAQEBQQFBQUFBQUEBAQFBAUEBQQFBAUEBQQFBA==",
        [
            new(16, 468480, 460288, 7),
            new(32, 411604, 215988, 6),
            new(48, 410404, 407164, 6),
            new(64, 394548, 568808, 7),
            new(80, 375768, 586904, 7),
            new(96, 326808, 631860, 7),
            new(112, 146616, 707012, 9),
            new(128, -114580, 716600, 9),
            new(144, -361548, 668600, 10),
            new(160, -448000, 426136, 12),
            new(176, -460784, 165256, 12),
            new(192, -470372, -95940, 12),
            new(208, -458200, -353124, 13),
            new(224, -275340, -540504, 2),
            new(240, -178272, -767468, 1),
            new(250, -120352, -907308, 1)
        ]),
        new(107, "BAUHBwcHBwcHBwcHBwcFBwcFBAcHBQcEBwUHBQcGBAUHBQcGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgUFBQUFBQQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAMDAwMDAwMEAwQEBAQEBAQEBAQEBQQFBQUFBQUFBQQEBAQEBQQFBAUEBQQFBAU=",
        [
            new(16, 365916, 663744, 7),
            new(32, 336412, 634660, 7),
            new(48, 124532, 715756, 9),
            new(64, -137612, 715756, 9),
            new(80, -377948, 650144, 10),
            new(96, -449860, 401980, 12),
            new(112, -462644, 141100, 12),
            new(128, -472232, -120096, 12),
            new(144, -444692, -373268, 13),
            new(160, -268896, -560928, 2),
            new(176, -165436, -787260, 1),
            new(185, -119100, -915516, 1)
        ])
    ];

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
