using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The climb up the rocket gantry, replayed from the native movement states the capture
/// shows.
///
/// <para>rcktbas1's <c>ladd</c> (entity 3) is a LINE whose Go runs <c>PREQ</c> on the
/// party leader, so the climb is several native LADER calls back to back rather than
/// one. The event's movement mode at +0x63 leaves 4/5 for a frame or two between them,
/// and <see cref="FieldLadderStateReader"/> reports each gap as a dismount. On
/// 2026-09-22 that produced five "Ladder mounted" / "Ladder complete" pairs between
/// 15:35:50Z and 15:35:57Z, and in the last gap the Story row was gone as well:
/// "Press Confirm at the ladder up the gantry no longer available. Navigation off." -
/// three seconds before the field changed, with the player still on the ladder.</para>
///
/// <para>A dismount now has to hold before it ends the climb. Arriving at the native
/// landing still ends it at once, and leaving the field clears the hold rather than
/// carrying a finished climb into the next room.</para>
/// </summary>
internal static class RocketGantryLadderTests
{
    private const int Field = 561;

    /// <summary>The installed field 561 walkmesh, so the route is a real one.</summary>
    private static Func<int, FieldWalkmeshReader>? planners;

    /// <summary>The foot of the gantry ladder, on triangle 98.</summary>
    private static readonly FieldPositionSnapshot LadderFoot =
        new(FieldPositionReader.FieldModule, Field, 0, -1156, 4978, 689, 98, 0);

    /// <summary>Where the native ladder state says the climb ends.</summary>
    private static readonly FieldNavigationRouteWaypoint Landing =
        new(-1094, 4949, 1556);

    private const int LandingTriangle = 131;

    private static readonly FieldPositionSnapshot AtTheLanding =
        new(FieldPositionReader.FieldModule, Field, 0,
            Landing.X, Landing.Y, Landing.Z, (ushort)LandingTriangle, 0);

    /// <summary>The confirm press the Story catalog offers at the foot of the gantry.</summary>
    private static readonly FieldNavigationTarget ClimbTheGantry =
        new(
            Field,
            FieldNavigationCategory.Story,
            "Press Confirm at the ladder up the gantry",
            -1155, 4977, 688,
            "story:561:-1:-1:Press Confirm at the ladder up the gantry",
            CompletesOnArrival: false,
            InteractionRadius: 33,
            TriggerLine: new FieldNavigationTriggerLine(-1154, 5021, 687, -1157, 4934, 690));

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        planners = createWalkmeshReader;
        OneClimbIsAnnouncedOnce();
        TheObjectiveSurvivesTheGapsBetweenNativeLadderCalls();
        ArrivingAtTheLandingStillFinishesTheClimbAtOnce();
        LeavingTheFieldDoesNotCarryAHeldClimbWithIt();
        AWorkingClimbLongerThanTheDeadlineIsNotStopped();
        AClimbThatGetsNowhereIsStillStopped();
        APauseOnTheLadderNeitherStopsNorExcusesTheClimb();
        EachStretchOfTheRouteIsMeasuredOnItsOwn();
        AClimbWithoutRouteGuidanceIsStillMeasured();
        Console.WriteLine("Rocket gantry ladder tests passed.");
    }

    /// <summary>
    /// Five native mount/dismount pairs, one climb. The capture's own cadence: the gaps
    /// are under a second and the real dismount is followed by standing still.
    /// </summary>
    private static void OneClimbIsAnnouncedOnce()
    {
        var harness = new LadderHarness();
        var spoken = new List<string>();

        harness.Start(spoken);
        for (var cycle = 0; cycle < 5; cycle++)
        {
            spoken.AddRange(harness.Observe(Mounted(), LadderFoot, milliseconds: 300));
            spoken.AddRange(harness.Observe(FieldLadderStateSnapshot.NotMounted, LadderFoot, milliseconds: 400));
        }

        Equal(1, spoken.Count(line => line.StartsWith("Ladder mounted", StringComparison.Ordinal)),
            $"one climb is one mount: {string.Join(" | ", spoken)}");
        Equal(0, spoken.Count(line => line.StartsWith("Ladder complete", StringComparison.Ordinal)),
            $"and the gaps between the native calls are not completions: {string.Join(" | ", spoken)}");
    }

    /// <summary>
    /// The reason it matters: in a gap the Story reader stops offering the row, and the
    /// controller used to answer that by switching navigation off.
    /// </summary>
    private static void TheObjectiveSurvivesTheGapsBetweenNativeLadderCalls()
    {
        var harness = new LadderHarness();
        var spoken = new List<string>();
        harness.Start(spoken);
        harness.Observe(Mounted(), LadderFoot, milliseconds: 300);

        // The row's own gate closes while the game is running the climb.
        harness.TargetIsOffered = false;
        var duringTheGap = harness.Observe(FieldLadderStateSnapshot.NotMounted, LadderFoot, milliseconds: 400);
        Equal(true, harness.Controller.BeaconEnabled,
            "navigation stays on while the climb is still running");
        Equal(0, duringTheGap.Count(line => line.Contains("no longer available", StringComparison.Ordinal)),
            $"and the objective is not withdrawn: {string.Join(" | ", duringTheGap)}");

        var stillClimbing = harness.Observe(Mounted(), LadderFoot, milliseconds: 300);
        Equal(0, stillClimbing.Count(line => line.StartsWith("Ladder mounted", StringComparison.Ordinal)),
            "and the next native call is the same climb, not a new one");
    }

    /// <summary>
    /// A real completion is not delayed: the party climbs up to the native landing, and
    /// arriving there ends the climb on the frame it happens.
    /// </summary>
    private static void ArrivingAtTheLandingStillFinishesTheClimbAtOnce()
    {
        var harness = new LadderHarness();
        var spoken = new List<string>();
        harness.Start(spoken);

        // Up the gantry at an ordinary pace, so the climb is a climb rather than a jump
        // the continuity tracker has to reject.
        for (var step = 1; step <= 8; step++)
        {
            var climbing = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, Field, 0,
                LadderFoot.X + (Landing.X - LadderFoot.X) * step / 8,
                LadderFoot.Y + (Landing.Y - LadderFoot.Y) * step / 8,
                LadderFoot.Z + (Landing.Z - LadderFoot.Z) * step / 8,
                (ushort)(step < 8 ? LadderFoot.TriangleId : LandingTriangle),
                0);
            harness.Observe(Mounted(), climbing, milliseconds: 250);
        }

        var finished = harness.Observe(FieldLadderStateSnapshot.NotMounted, AtTheLanding, milliseconds: 250);
        Equal(1, finished.Count(line => line.StartsWith("Ladder complete", StringComparison.Ordinal)),
            $"the climb ends where the native state said it would: {string.Join(" | ", finished)}; " +
            $"diagnostic={harness.Controller.LastNavigationDiagnostic}");
    }

    /// <summary>
    /// And a held climb is not stale state. Walking into the next field ends navigation
    /// for the old target rather than carrying the hold across the load.
    /// </summary>
    private static void LeavingTheFieldDoesNotCarryAHeldClimbWithIt()
    {
        var harness = new LadderHarness();
        var spoken = new List<string>();
        harness.Start(spoken);
        harness.Observe(Mounted(), LadderFoot, milliseconds: 300);

        var nextField = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, 562, 0, -1094, 4960, 1939, 81, 0);
        harness.TargetIsOffered = false;
        var afterTheLoad = harness.Observe(FieldLadderStateSnapshot.NotMounted, nextField, milliseconds: 200);
        Equal(false, harness.Controller.BeaconEnabled,
            "a target in a field the party has left is not held open by a ladder");
        Equal(true, afterTheLoad.Count > 0,
            $"and the change is reported: {string.Join(" | ", afterTheLoad)}");
    }

    // --- the auto walk convergence guard on a ladder ------------------------------------
    //
    // Gaea's Cliff, 2026-09-28 x64 log: 10:58:25-31 in gaia_1 the party was mounted on a
    // ladder with footsteps every sample and "climb up" spoken, yet the guard stopped auto
    // walk after exactly five seconds ("Could not get closer to Take the cave mouth at the
    // top of the climb", remaining 6088, portal 0); the same at 11:09:20-25 in gaia_2. While
    // mounted the controller holds the route (FieldNavigationRouteTracker.TryHold), so the
    // route's remaining distance and portal do not move during a climb, and both runtimes
    // measured exactly those.

    /// <summary>The same sample both runtimes take, from the same controller call.</summary>
    private static bool Guard(LadderHarness harness, FieldAutoWalkConvergenceTracker tracker, FieldPositionSnapshot at, bool held = false)
    {
        var hasDirection = harness.Controller.TryResolveAutomaticInput(at, new FieldNavigationControlTransform(0), 0, out _);
        return tracker.Observe(
            FieldAutoWalkConvergenceSample.ForRoute(harness.Controller, at, isHeldByGame: held, hasDirection: hasDirection),
            harness.Clock);
    }

    /// <summary>A point <paramref name="fraction"/> of the way up the gantry ladder.</summary>
    private static FieldPositionSnapshot OnTheLadder(double fraction) =>
        new(FieldPositionReader.FieldModule, Field, 0,
            (int)Math.Round(LadderFoot.X + (Landing.X - LadderFoot.X) * fraction),
            (int)Math.Round(LadderFoot.Y + (Landing.Y - LadderFoot.Y) * fraction),
            (int)Math.Round(LadderFoot.Z + (Landing.Z - LadderFoot.Z) * fraction),
            LadderFoot.TriangleId, 0);

    /// <summary>
    /// Eight seconds up an 870-unit ladder at a tenth of a second a sample: slow, steady,
    /// and every sample closer to the landing. The guard must leave it alone.
    /// </summary>
    private static void AWorkingClimbLongerThanTheDeadlineIsNotStopped()
    {
        var harness = new LadderHarness();
        harness.Start([]);
        var tracker = new FieldAutoWalkConvergenceTracker();
        var stopped = false;
        for (var sample = 0; sample <= 80; sample++)
        {
            var at = OnTheLadder(sample / 80d * 0.95);
            harness.Observe(Mounted(), at, milliseconds: 100);
            stopped |= Guard(harness, tracker, at);
        }

        Equal(false, stopped, "a climb that is getting closer to its landing is progress, however long it takes");
    }

    /// <summary>
    /// Mounted, the direction held, and nothing moves for six seconds. That is still auto
    /// walk getting nowhere, and it must still be said.
    /// </summary>
    private static void AClimbThatGetsNowhereIsStillStopped()
    {
        var harness = new LadderHarness();
        harness.Start([]);
        var tracker = new FieldAutoWalkConvergenceTracker();
        var stuck = OnTheLadder(0.3);
        var stoppedAt = -1;
        for (var sample = 0; sample <= 60 && stoppedAt < 0; sample++)
        {
            harness.Observe(Mounted(), stuck, milliseconds: 100);
            if (Guard(harness, tracker, stuck))
            {
                stoppedAt = sample;
            }
        }

        Equal(true, stoppedAt is >= 45 and <= 55, $"a stationary climb is stopped after five seconds (sample {stoppedAt})");
    }

    /// <summary>
    /// The game takes the party for four seconds halfway up (a menu, a battle message). The
    /// paused time counts neither way: the climb carries on afterwards without being
    /// stopped, and a climb that was stuck before the pause and is stuck after it is still
    /// stopped once its own stalled time adds up.
    /// </summary>
    private static void APauseOnTheLadderNeitherStopsNorExcusesTheClimb()
    {
        var harness = new LadderHarness();
        harness.Start([]);
        var tracker = new FieldAutoWalkConvergenceTracker();
        var stopped = false;
        for (var sample = 0; sample <= 30; sample++)
        {
            var at = OnTheLadder(sample / 100d);
            harness.Observe(Mounted(), at, milliseconds: 100);
            stopped |= Guard(harness, tracker, at);
        }

        for (var sample = 0; sample < 40; sample++)
        {
            harness.Observe(Mounted(), OnTheLadder(0.30), milliseconds: 100);
            stopped |= Guard(harness, tracker, OnTheLadder(0.30), held: true);
        }

        for (var sample = 31; sample <= 70; sample++)
        {
            var at = OnTheLadder(sample / 100d);
            harness.Observe(Mounted(), at, milliseconds: 100);
            stopped |= Guard(harness, tracker, at);
        }

        Equal(false, stopped, "a climb that resumes after a pause is not stopped for the pause");

        var stuckHarness = new LadderHarness();
        stuckHarness.Start([]);
        var stuckTracker = new FieldAutoWalkConvergenceTracker();
        var stuck = OnTheLadder(0.2);
        var tripped = false;
        for (var round = 0; round < 3 && !tripped; round++)
        {
            for (var sample = 0; sample < 20 && !tripped; sample++)
            {
                stuckHarness.Observe(Mounted(), stuck, milliseconds: 100);
                tripped = Guard(stuckHarness, stuckTracker, stuck);
            }

            for (var sample = 0; sample < 10 && !tripped; sample++)
            {
                stuckHarness.Observe(Mounted(), stuck, milliseconds: 100);
                tripped = Guard(stuckHarness, stuckTracker, stuck, held: true);
            }
        }

        Equal(true, tripped, "pauses that flicker on and off cannot hide a climb going nowhere");
    }

    /// <summary>
    /// The walk to the ladder, the climb and the walk after it are separate stretches, each a
    /// fresh measurement: the walk's last few units are never held against the climb, nor
    /// the climb against the walk after it, nor one ladder against the next.
    /// </summary>
    private static void EachStretchOfTheRouteIsMeasuredOnItsOwn()
    {
        var harness = new LadderHarness();
        harness.Start([]);
        var walking = harness.Controller.ResolveAutoWalkProgress(LadderFoot);
        Equal("walk", walking.Traversal, "before mounting the party is walking");

        harness.Observe(Mounted(), OnTheLadder(0.05), milliseconds: 100);
        var climbing = harness.Controller.ResolveAutoWalkProgress(OnTheLadder(0.05));
        Equal($"ladder:{Landing.X},{Landing.Y},{Landing.Z}", climbing.Traversal, "mounting starts the climb's own stretch");
        Equal(true, climbing.RemainingDistance > 700 && climbing.RemainingDistance < 900,
            $"the climb is measured to its own landing ({climbing.RemainingDistance:0})");
        var higher = harness.Controller.ResolveAutoWalkProgress(OnTheLadder(0.5));
        Equal(true, higher.RemainingDistance < climbing.RemainingDistance - 300, "and it shrinks as the party climbs");

        // Up to the landing and off: the native dismount at the landing ends the climb, and
        // the stretch after it is walking again.
        for (var step = 1; step <= 8; step++)
        {
            harness.Observe(Mounted(), OnTheLadder(0.5 + (0.5 * step / 8)), milliseconds: 250);
        }

        harness.Observe(FieldLadderStateSnapshot.NotMounted, AtTheLanding, milliseconds: 250);
        Equal("walk", harness.Controller.ResolveAutoWalkProgress(AtTheLanding).Traversal,
            "after the dismount at the landing the party is walking again");

        // A second ladder from there, and one that goes back down: each is its own stretch,
        // measured to its own landing, which the downward one reaches by descending.
        var nextLanding = new FieldNavigationRouteWaypoint(Landing.X + 40, Landing.Y, Landing.Z + 600);
        harness.Observe(Mounted() with { Target = nextLanding }, AtTheLanding, milliseconds: 300);
        var next = harness.Controller.ResolveAutoWalkProgress(AtTheLanding);
        Equal($"ladder:{nextLanding.X},{nextLanding.Y},{nextLanding.Z}", next.Traversal, "a second ladder is a new stretch");

        var down = new LadderHarness();
        down.Start([]);
        var foot = new FieldNavigationRouteWaypoint(LadderFoot.X, LadderFoot.Y, LadderFoot.Z);
        var goingDown = Mounted() with { Target = foot, RequiredInput = FieldNavigationInput.Down };
        down.Observe(goingDown, OnTheLadder(0.9), milliseconds: 100);
        var top = down.Controller.ResolveAutoWalkProgress(OnTheLadder(0.9));
        down.Observe(goingDown, OnTheLadder(0.4), milliseconds: 100);
        var lower = down.Controller.ResolveAutoWalkProgress(OnTheLadder(0.4));
        Equal($"ladder:{foot.X},{foot.Y},{foot.Z}", top.Traversal, "a climb down is measured to the foot it is going to");
        Equal(true, lower.RemainingDistance < top.RemainingDistance - 300, "and it shrinks as the party climbs down");

        // The guard itself across the stretches: a walk that ended a few units from its
        // waypoint does not stop the long climb that follows at once.
        var guarded = new LadderHarness();
        guarded.Start([]);
        var tracker = new FieldAutoWalkConvergenceTracker();
        var stopped = false;
        for (var sample = 0; sample < 20; sample++)
        {
            guarded.Observe(FieldLadderStateSnapshot.NotMounted, LadderFoot, milliseconds: 100);
            stopped |= Guard(guarded, tracker, LadderFoot);
        }

        for (var sample = 0; sample <= 60; sample++)
        {
            var at = OnTheLadder(sample / 60d * 0.9);
            guarded.Observe(Mounted(), at, milliseconds: 100);
            stopped |= Guard(guarded, tracker, at);
        }

        Equal(false, stopped, "the climb after a walk is measured from its own start");
    }

    /// <summary>
    /// Navigation switched on while the party is already on the ladder, and a climb whose
    /// position jumped (the controller's position recovery drops the held route, so the route
    /// guidance is empty while still mounted). The climb is still measured: stationary is still
    /// stopped, moving is still left alone.
    /// </summary>
    private static void AClimbWithoutRouteGuidanceIsStillMeasured()
    {
        // Switched on while already on the ladder, toward the gantry's one objective - the
        // mount press at its foot, now behind the party - navigation does not start at all
        // ("route unavailable"), so there is no auto walk for the guard to watch.
        var mounted = new LadderHarness();
        mounted.Observe(Mounted(), OnTheLadder(0.3), milliseconds: 100);
        mounted.Start([], OnTheLadder(0.3), Mounted());
        Equal(false, mounted.Controller.BeaconEnabled,
            $"navigation to a target behind the climb does not start mid-ladder: {mounted.Controller.LastNavigationDiagnostic}");
        Equal(false, mounted.Controller.TryResolveAutomaticInput(OnTheLadder(0.3), new FieldNavigationControlTransform(0), 0, out _),
            "and gives auto walk nothing to drive");

        var jumped = new LadderHarness();
        jumped.Start([]);
        jumped.Observe(Mounted(), OnTheLadder(0.1), milliseconds: 100);
        jumped.Observe(Mounted(), OnTheLadder(0.1), milliseconds: 100);
        // A reading far up the ladder a tenth of a second later: position recovery.
        jumped.Observe(Mounted(), OnTheLadder(0.9), milliseconds: 100);
        jumped.Observe(Mounted(), OnTheLadder(0.9), milliseconds: 100);
        var jumpTracker = new FieldAutoWalkConvergenceTracker();
        var jumpStoppedAt = -1;
        for (var sample = 0; sample <= 70 && jumpStoppedAt < 0; sample++)
        {
            jumped.Observe(Mounted(), OnTheLadder(0.9), milliseconds: 100);
            if (Guard(jumped, jumpTracker, OnTheLadder(0.9)))
            {
                jumpStoppedAt = sample;
            }
        }

        Equal(true, jumpStoppedAt is >= 45 and <= 60,
            $"after a position recovery on the ladder, a stuck climb is still stopped (sample {jumpStoppedAt}, " +
            $"guidance {(jumped.Controller.CurrentRouteGuidance is null ? "empty" : "held")})");
    }

    private static FieldLadderStateSnapshot Mounted() =>
        new(
            IsUsable: true,
            IsMounted: true,
            Phase: FieldLadderPhase.Climbing,
            RequiredInput: FieldNavigationInput.Up,
            Target: Landing,
            TargetTriangle: LandingTriangle,
            MovementMode: 4,
            Progress: 1);

    /// <summary>
    /// A controller with the gantry objective and a planner that answers for the one
    /// field, so the replay exercises the live-tracking path rather than a stub.
    /// </summary>
    private sealed class LadderHarness
    {
        private readonly FieldNavigationControlTransform transform = new(0);
        private DateTime clock = new(2026, 9, 22, 15, 35, 45, DateTimeKind.Utc);

        internal bool TargetIsOffered { get; set; } = true;

        internal FieldNavigationController Controller { get; }

        internal DateTime Clock => clock;

        internal LadderHarness()
        {
            Controller = new FieldNavigationController(
                new FieldNavigationTargetSource(
                    Array.Empty<FieldNavigationTarget>(),
                    storyTargetProvider: position =>
                        TargetIsOffered && position.FieldId == Field
                            ? [ClimbTheGantry]
                            : Array.Empty<FieldNavigationTarget>()),
                new FieldWalkmeshRoutePlanner(planners!(Field)));
        }

        internal void Start(List<string> spoken) => Start(spoken, LadderFoot, default);

        internal void Start(List<string> spoken, FieldPositionSnapshot at, FieldLadderStateSnapshot ladderState)
        {
            var guard = 0;
            while (Controller.CurrentCategory != FieldNavigationCategory.Story && guard++ < 8)
            {
                Controller.HandleAction(FieldNavigationAction.NextCategory, at, transform, ladderState);
            }

            Controller.HandleAction(FieldNavigationAction.NextTarget, at, transform, ladderState);
            var started = Controller.HandleAction(FieldNavigationAction.ToggleBeacon, at, transform, ladderState);
            if (started?.Speech is { Length: > 0 } speech)
            {
                spoken.Add(speech);
            }
        }

        internal IReadOnlyList<string> Observe(
            FieldLadderStateSnapshot ladderState,
            FieldPositionSnapshot position,
            int milliseconds)
        {
            clock = clock.AddMilliseconds(milliseconds);
            var result = Controller.UpdateLiveTracking(
                position,
                new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                transform,
                isSuppressed: false,
                arrivalDistanceUnits: 0,
                ladderState: ladderState,
                observedAt: clock);
            return result?.Speech is { Length: > 0 } speech ? [speech] : Array.Empty<string>();
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Rocket gantry ladder - {label}: expected {expected}, got {actual}.");
        }
    }
}
