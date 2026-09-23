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

        internal void Start(List<string> spoken)
        {
            var guard = 0;
            while (Controller.CurrentCategory != FieldNavigationCategory.Story && guard++ < 8)
            {
                Controller.HandleAction(FieldNavigationAction.NextCategory, LadderFoot, transform);
            }

            Controller.HandleAction(FieldNavigationAction.NextTarget, LadderFoot, transform);
            var started = Controller.HandleAction(FieldNavigationAction.ToggleBeacon, LadderFoot, transform);
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
