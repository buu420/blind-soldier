using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Cosmo Canyon's observatory doors are opened by walking onto a line, not by waiting.
///
/// <para>Both doors in the report are locked by their own field's Init and released by a
/// native LINE script the party has to reach. Read from the installed archives:</para>
///
/// <list type="bullet">
/// <item>541 bugin1a, entity 15 <c>LINEW</c>: Init declares
/// <c>LINE (-321,-3,-35)-(-312,64,-35)</c> and runs <c>IDLCK triangle 1 lock</c>; Go 1x runs
/// <c>LINON 0</c>, <c>IDLCK triangle 1 unlock</c>, the door sound and the door animation.
/// Init leaves the line enabled; scripts 7 and 8 are its <c>LINON 0</c> and <c>LINON 1</c>.</item>
/// <item>544 bugin2, entity 18 <c>DOOR</c> Init runs <c>IDLCK triangle 38 lock</c>; entity 19
/// <c>LINEW</c> declares <c>LINE (186,-117,-624)-(118,-174,-624)</c> and reaches its own
/// <c>LINON 0</c> only while <c>Bank[3][161]</c> bit 3 is set and <c>Bank[3][170]</c> bit 0
/// is not - the whole of the first visit - so AD 10 Script 3 is what turns it back on. Go 1x
/// runs <c>IDLCK 38 unlock</c> on both of its branches.</item>
/// </list>
///
/// <para>So a destination held for one of those locks can never open while the party stands
/// still, and the shipped behaviour waited. These cases replay the real thing: the
/// production controller over the installed walkmeshes, from the position the report's own
/// log has the party in, stepped by the automatic input the controller emits, one native
/// movement frame at a time, with the lock enforced against every frame. The lock is
/// released only when that generated movement is inside the line's own radius, measured the
/// way the native line step measures it, and never by the test reaching in and opening the
/// door.</para>
/// </summary>
internal static class ObservatoryApproachNavigationTests
{
    private const int Observatory = 541;
    private const int ResearchCentre = 544;
    private const int CosmoTop = 540;

    /// <summary>
    /// The native line test, coded here rather than called from the code under test.
    ///
    /// <para>From <c>ghidra-line-trigger.log</c>: the step squares the entity record's radius
    /// at <c>+0x72</c> and switches the line off when
    /// <c>radius * radius &lt;= distanceSquared</c>, so being on the line is strictly
    /// <c>distanceSquared &lt; radius²</c>. The radius itself is live state the mod reads from
    /// the player event at runtime, so the replay runs the range a field model can have
    /// rather than pretending to know the one value.</para>
    /// </summary>
    private static readonly int[] NativeLineRadii = [16, 24, 32, 40];

    /// <summary>One native movement frame of held input.</summary>
    private const int NativeStepLength = 4;

    /// <summary>
    /// What the host passes as the arrival distance: the footstep distance it measures from
    /// real movement. The report log settled on 155 for a run in 544, and 80 is the walking
    /// figure the other native replay in this suite uses. Using a real one rather than none
    /// is the point - a generous arrival distance is exactly what would stop the party short
    /// of a native line, and this is the case that has to prove it does not.
    /// </summary>
    private const int ArrivalDistanceUnits = 80;

    /// <summary>541, where the report's log has the party standing when it gave up.</summary>
    private static readonly FieldPositionSnapshot ObservatoryStall =
        new(FieldPositionReader.FieldModule, Observatory, 0, -72, 103, -38, 38, 40);

    private static readonly FieldNavigationTarget WayBackDown = new(
        Observatory, FieldNavigationCategory.Story, "Go back down from the observatory",
        -378, 30, -36, StableId: "cosmo:541:back-down", CompletesOnArrival: true,
        TriggerLine: new FieldNavigationTriggerLine(-365, -1, -36, -392, 62, -36));

    /// <summary>
    /// 544, the first position the log reports once the field has loaded:
    /// <c>01:18:53Z field=544 x=-349, y=-319, z=-635, triangle=1</c>.
    /// </summary>
    private static readonly FieldPositionSnapshot ResearchEntry =
        new(FieldPositionReader.FieldModule, ResearchCentre, 0, -349, -319, -635, 1, 96);

    private static readonly FieldNavigationTarget RoomAtTop = new(
        ResearchCentre, FieldNavigationCategory.Story, "Go through to the room at the top",
        225, -189, -624, StableId: "cosmo:544:room-at-top", CompletesOnArrival: true,
        TriggerLine: new FieldNavigationTriggerLine(255, -158, -624, 196, -220, -624));

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        TheNativeOpeningLineIsWhatTheCatalogNames();
        TheApproachIsOnlyOfferedForTheDoorInTheGoalsWay(createWalkmeshReader);
        SpokenGuidanceContinuesNearTheOpeningLine(createWalkmeshReader);
        SharedPortalOriginRespectsNativeLocks(createWalkmeshReader);
        EmittedInputWalksToTheLineAndOnToTheGateway(createWalkmeshReader);
        ADelayedOpeningKeepsTheDestinationHeld(createWalkmeshReader);
        TheCallerCanStartAndCancelTheApproach(createWalkmeshReader);
        ADoorWithNoOpeningLineStaysHeld(createWalkmeshReader);
    }

    private static void SpokenGuidanceContinuesNearTheOpeningLine(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // Logged approach position: close to the door, but short of its native LINE.
        var position = ObservatoryStall with { X = -280, Y = 20, Z = -45, TriangleId = 3 };
        var fixture = new Fixture(createWalkmeshReader, Observatory, [1]);
        fixture.Select(position, WayBackDown);
        fixture.Controller.UpdateLiveTracking(position, new(0, FieldNavigationInput.None),
            fixture.Transform, isSuppressed: false, arrivalDistanceUnits: ArrivalDistanceUnits);
        Equal(true, fixture.Controller.CreateSpokenGuidance(position, fixture.Transform,
            ArrivalDistanceUnits) is not null,
            "spoken navigation must continue inside the normal arrival radius until the opening line; " +
            $"beacon={fixture.Controller.BeaconEnabled}, held={fixture.Controller.HeldForNativeBoundaryLabel}, " +
            $"guidance={fixture.Controller.CurrentRouteGuidance}, diagnostic={fixture.Controller.LastNavigationDiagnostic}");
    }

    private static void SharedPortalOriginRespectsNativeLocks(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var mesh = createWalkmeshReader(ResearchCentre).Read(ResearchEntry).Walkmesh!;
        var start = new FieldNavigationRouteWaypoint(197, -208, -624);
        var end = new FieldNavigationRouteWaypoint(220, -200, -624);
        var open = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 2, start, end,
            allowStartingEdgeCrossing: true);
        Equal(true, open.IsClear, "the shared-edge origin must enter its open adjacent triangle: " + open.Diagnostic);
        Equal(5, open.EndTriangle, "the corner continuation enters adjacent triangle 5");
        Equal(false, FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 2, start, end,
            triangle => triangle == 5, allowStartingEdgeCrossing: true).IsClear, "a boundary on the adjacent triangle still blocks the crossing");
        Equal(false, FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 2, start, end,
            triangle => triangle == 2, allowStartingEdgeCrossing: true).IsClear, "a boundary on the starting triangle still blocks movement");
        Equal(false, FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 2, start, end).IsClear,
            "native wall-probe callers retain their existing conservative default");
    }

    /// <summary>
    /// The catalog has to name the instruction that actually releases each lock, and the
    /// entity whose live state says whether the line is switched on at all. A wrong triangle
    /// here sends the party to a line that opens nothing.
    /// </summary>
    private static void TheNativeOpeningLineIsWhatTheCatalogNames()
    {
        Equal(true,
            FieldNativeDoorOpeningLines.TryFind(Observatory, 1, out var observatory),
            "541 triangle 1 must have a known opening line");
        Equal(new FieldNavigationTriggerLine(-321, -3, -35, -312, 64, -35), observatory.Line,
            "the 541 line is entity 15 LINEW Init's own endpoints");
        Equal(15, observatory.LineEntityId,
            "and entity 15 is whose LINON state says whether walking there does anything");

        Equal(true,
            FieldNativeDoorOpeningLines.TryFind(ResearchCentre, 38, out var research),
            "544 triangle 38 must have a known opening line");
        Equal(new FieldNavigationTriggerLine(186, -117, -624, 118, -174, -624), research.Line,
            "the 544 line is entity 19 LINEW Init's own endpoints");
        Equal(19, research.LineEntityId, "and entity 19 is the one the scripts switch off and on");

        // The negative half. A lock nothing in the field releases by approach must not
        // acquire a line by accident - 544 entity 5 RED locks 23 and 18 in its Init and
        // releases them himself in Script 10 when he leaves.
        Equal(false, FieldNativeDoorOpeningLines.TryFind(ResearchCentre, 23, out _),
            "Red XIII's own lock is not opened by walking anywhere");
        Equal(false, FieldNativeDoorOpeningLines.TryFind(ResearchCentre, 18, out _),
            "nor is the second triangle he locks");
        Equal(false, FieldNativeDoorOpeningLines.TryFind(Observatory, 38, out _),
            "the triangle the player is standing on is not a door");
    }

    /// <summary>
    /// The boundary reports every lock the field has on, not the ones in this route's way. A
    /// destination blocked by something else, or not blocked at all, must never be routed
    /// toward a door that happens to be open-by-line somewhere else in the room.
    /// </summary>
    private static void TheApproachIsOnlyOfferedForTheDoorInTheGoalsWay(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // 544 while Red XIII is standing there: he holds 23 and 18, and the door holds 38.
        // Only 38 is in the way of the room at the top, and only 38 has a line.
        var fixture = new Fixture(createWalkmeshReader, ResearchCentre, [23, 18, 38]);
        fixture.Select(ResearchEntry, RoomAtTop);
        Equal(true, fixture.Controller.BeaconEnabled,
            "the door in the way is the one that is walked to, even with two other locks on");
        Equal(RoomAtTop.Label, fixture.Controller.HeldForNativeBoundaryLabel,
            "and the destination is still the one that was asked for");

        // And the other direction: with only Red XIII's own locks on, the way to the room at
        // the top is open, so there is nothing to approach and nothing to hold.
        var redOnly = new Fixture(createWalkmeshReader, ResearchCentre, [23, 18]);
        var spoken = redOnly.Select(ResearchEntry, RoomAtTop);
        Equal(false, redOnly.Controller.IsHoldingForNativeBoundary,
            "a destination that is not blocked is not held");
        Contains("Navigation on", spoken?.Speech, "it simply navigates");

        // The scoping question from the other side: somewhere the party can already walk to,
        // while a door with a known line is shut. Releasing that door is not what would let
        // this route through, so it must not be claimed as the reason.
        var nearSide = new FieldNavigationTarget(
            ResearchCentre, FieldNavigationCategory.Story, "the near side of the room",
            -205, -170, -634, StableId: "cosmo:544:near-side");
        var reachable = new Fixture(createWalkmeshReader, ResearchCentre, [38]);
        Equal(true, reachable.Planner.TryBuildRoute(ResearchEntry, nearSide, out _),
            "the near side is reachable with the door still shut");
        Equal(false,
            reachable.Planner.WouldRouteIfBoundaryReleased(ResearchEntry, nearSide, 38),
            "so releasing the door must not be reported as what would let it through");

        // 541 the same way round. Triangle 1 is the only lock and it is the one in the way.
        var wayDown = new Fixture(createWalkmeshReader, Observatory, [1]);
        Equal(true,
            wayDown.Planner.WouldRouteIfBoundaryReleased(ObservatoryStall, WayBackDown, 1),
            "541's own lock is what stands between the party and the way down");
    }

    /// <summary>
    /// The replay. The controller's own automatic input walks the party, one native frame at
    /// a time, with the lock enforced against every frame; the lock clears exactly when that
    /// movement is inside the line's radius; and the walk carries on to the gateway the
    /// player asked for.
    /// </summary>
    private static void EmittedInputWalksToTheLineAndOnToTheGateway(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var scenario in Scenarios())
        {
            foreach (var radius in NativeLineRadii)
            {
                var outcome = Replay(createWalkmeshReader, scenario, radius, scriptDelaySamples: 0);
                Console.WriteLine(
                    $"observatory replay: field {scenario.Field} radius {radius} -> " +
                    $"{outcome.Samples} observations, {outcome.Frames} native frames; " +
                    $"line entered at frame {outcome.UnlockFrame} " +
                    $"(d2={outcome.UnlockDistanceSquared}, previous d2={outcome.DistanceSquaredBeforeUnlock}, " +
                    $"r2={radius * radius}); gateway reached at frame {outcome.ReachedFrame}; " +
                    $"input {outcome.InputTrace}");
            }
        }
    }

    /// <summary>
    /// The native script does not have to run on the frame the party crosses. Crossing is a
    /// request; until the lock actually clears the destination stays held, nothing is
    /// announced as reached, and the party waits on the line rather than wandering off.
    /// </summary>
    private static void ADelayedOpeningKeepsTheDestinationHeld(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var scenario in Scenarios())
        {
            const int delay = 30;
            var outcome = Replay(createWalkmeshReader, scenario, radius: 24, scriptDelaySamples: delay);
            Equal(true, outcome.HeldThroughTheDelay,
                $"field {scenario.Field}: the destination must stay held while the script takes its time");

            // The observation the party crossed on is the one that moved it there, so the
            // waiting is counted from the next.
            Equal(delay - 1, outcome.DelayedSamples,
                $"field {scenario.Field}: and must be held for every one of those observations");
            Console.WriteLine(
                $"observatory replay: field {scenario.Field} delayed opening -> held for " +
                $"{outcome.DelayedSamples} observations; door unlocked at frame " +
                $"{outcome.UnlockFrame}; gateway reached at frame {outcome.ReachedFrame}");
        }
    }

    /// <summary>
    /// The buttons. X starts the approach and walks it; B ends it and starts nothing,
    /// whatever the door does afterwards; and moving the selection on is the same answer.
    /// </summary>
    private static void TheCallerCanStartAndCancelTheApproach(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var scenario in Scenarios())
        {
            var started = new Caller(createWalkmeshReader, scenario);
            started.Services.StartAutoWalk();
            Equal(true, started.Controller.BeaconEnabled,
                $"field {scenario.Field}: X must start the walk to the door");
            Equal(scenario.Target.Label, started.Controller.HeldForNativeBoundaryLabel,
                $"field {scenario.Field}: and must keep the destination that was asked for");
            Equal(1, started.AutoWalkStarts,
                $"field {scenario.Field}: and the approach must actually be walked");
            Equal(true, started.Controller.TryResolveAutomaticInput(
                    scenario.Start, started.Transform, 0, out var direction),
                $"field {scenario.Field}: and it must emit a direction: " +
                started.Controller.LastNavigationDiagnostic);
            Equal(false, direction == FieldNavigationInput.None,
                $"field {scenario.Field}: a real direction, not nothing");

            var cancelled = new Caller(createWalkmeshReader, scenario);
            cancelled.Services.StartAutoWalk();
            var walksBefore = cancelled.AutoWalkStarts;
            Contains("Navigation off", cancelled.Services.Stop(),
                $"field {scenario.Field}: B must answer");
            Equal(false, cancelled.Controller.BeaconEnabled,
                $"field {scenario.Field}: and must end the walk to the door");
            Equal(false, cancelled.Controller.IsHoldingForNativeBoundary,
                $"field {scenario.Field}: and the hold with it");
            cancelled.Boundary.Unlock(scenario.LockedTriangle);
            cancelled.Tick();
            Equal(false, cancelled.Controller.BeaconEnabled,
                $"field {scenario.Field}: a cancelled destination must not start when the door opens");
            Equal(walksBefore, cancelled.AutoWalkStarts,
                $"field {scenario.Field}: and must walk nothing after the cancel");

            var elsewhere = scenario.Target with
            {
                Label = "somewhere else entirely",
                StableId = scenario.Target.StableId + ":other",
                X = scenario.Start.X,
                Y = scenario.Start.Y,
                Z = scenario.Start.Z,
                TriggerLine = null,
            };
            var moved = new Caller(createWalkmeshReader, scenario, [scenario.Target, elsewhere]);
            moved.Services.StartAutoWalk();
            Equal(true, moved.Controller.IsHoldingForNativeBoundary,
                $"field {scenario.Field}: held before the selection moves");
            moved.Controller.HandleAction(
                FieldNavigationAction.NextTarget, scenario.Start, moved.Transform);
            Equal(false, moved.Controller.IsHoldingForNativeBoundary,
                $"field {scenario.Field}: a held destination belongs to the selection the player left");
            moved.Boundary.Unlock(scenario.LockedTriangle);
            moved.Tick();
            Equal(scenario.Target.Label == moved.Controller.HeldForNativeBoundaryLabel, false,
                $"field {scenario.Field}: and must not come back when the door opens");
        }
    }

    /// <summary>
    /// The existing behaviour has to survive. A lock with no approach line - one the game
    /// releases on its own schedule, or never - is still held silently, not walked anywhere.
    /// </summary>
    private static void ADoorWithNoOpeningLineStaysHeld(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // 540 cos_top. Entity 5 locks triangle 76 in its Init and nothing in that field ever
        // unlocks it - there is no IDLCK 76 release anywhere in the script, and neither of
        // its two LINE entities touches it. Triangle 17 routes there with the lock off and is
        // blocked by it with the lock on.
        var start = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, CosmoTop, 0, -125, -525, 0, 17, 40);
        var blocked = new FieldNavigationTarget(
            CosmoTop, FieldNavigationCategory.Story, "behind the sealed way",
            -519, -480, 0, StableId: "cosmo:540:behind-76");
        var fixture = new Fixture(createWalkmeshReader, CosmoTop, [76]);
        var spoken = fixture.Select(start, blocked);

        Equal(blocked.Label, fixture.Controller.HeldForNativeBoundaryLabel,
            "a lock with no opening line is still held");
        Contains("shut", spoken?.Speech, "and still explained");
        Contains("will start when it opens", spoken?.Speech,
            "and still described as waiting, not as a walk");
        Equal(false, fixture.Controller.BeaconEnabled,
            "but nothing is walked anywhere, because there is nowhere that opens it");
        Equal(false,
            fixture.Controller.TryResolveAutomaticInput(start, fixture.Transform, 0, out var input),
            "and no automatic direction is produced");
        Equal(FieldNavigationInput.None, input, "and no direction at all");
    }

    private static IEnumerable<Scenario> Scenarios()
    {
        yield return new Scenario(
            Observatory, ObservatoryStall, WayBackDown, 1,
            new FieldNavigationTriggerLine(-321, -3, -35, -312, 64, -35));
        yield return new Scenario(
            ResearchCentre, ResearchEntry, RoomAtTop, 38,
            new FieldNavigationTriggerLine(186, -117, -624, 118, -174, -624));
    }

    private sealed record Scenario(
        int Field,
        FieldPositionSnapshot Start,
        FieldNavigationTarget Target,
        int LockedTriangle,
        FieldNavigationTriggerLine Line);

    private sealed record ReplayOutcome(
        int Samples,
        int Frames,
        int UnlockFrame,
        long UnlockDistanceSquared,
        long DistanceSquaredBeforeUnlock,
        int ReachedFrame,
        int DelayedSamples,
        bool HeldThroughTheDelay,
        string InputTrace);

    /// <summary>
    /// Walks the party with the controller's own emitted input and nothing else.
    ///
    /// <para><paramref name="scriptDelaySamples"/> is how many observations the field script
    /// takes to notice the crossing. Zero is the ordinary case; a large value is the game
    /// being slow about it, and the destination has to stay held for every one of them.</para>
    /// </summary>
    private static ReplayOutcome Replay(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Scenario scenario,
        int radius,
        int scriptDelaySamples)
    {
        var context = $"field {scenario.Field}, radius {radius}, delay {scriptDelaySamples}";
        var reader = createWalkmeshReader(scenario.Field);
        var mesh = reader.Read(scenario.Start).Walkmesh
            ?? throw new InvalidOperationException($"{context}: the installed walkmesh is unreadable");
        var fixture = new Fixture(
            createWalkmeshReader, scenario.Field, [scenario.LockedTriangle], reader);
        var destinationField = scenario.Field == Observatory ? ResearchCentre : Observatory;
        // The real source also supplies the native exit. The controller uses its
        // matching LINE to complete Story navigation on the ensuing field transition.
        var nativeExit = scenario.Target with
        {
            Category = FieldNavigationCategory.Exits,
            StableId = $"native-exit:{scenario.Field}",
            DestinationFieldIds = [destinationField],
        };
        var spoken = fixture.Select(scenario.Start, scenario.Target, nativeExit);

        Equal(true, fixture.Controller.BeaconEnabled,
            $"{context}: selecting a held door must start the approach, not fall silent");
        Contains(scenario.Target.Label, spoken?.Speech,
            $"{context}: the destination the player asked for must be named");
        Contains("Approaching the door", spoken?.Speech,
            $"{context}: and the player must be told what is happening, briefly");
        fixture.Controller.RequestAutoWalkForHeldRoute();

        var radiusSquared = (long)radius * radius;
        var position = scenario.Start;
        var lastInput = FieldNavigationInput.None;
        var inputs = new List<string>();
        var frames = 0;
        var autoWalkStarts = 0;
        var unlockFrame = -1;
        var reachedFrame = -1;
        var crossedAtSample = -1;
        var delayedSamples = 0;
        var heldThroughTheDelay = true;
        var unlockDistanceSquared = -1L;
        var previousDistanceSquared = -1L;
        var sample = 0;
        var lastStall = "(never stalled)";

        for (; sample < 900 && reachedFrame < 0; sample++)
        {
            var update = fixture.Controller.UpdateLiveTracking(
                position,
                new FieldNavigationInputSnapshot(0, lastInput),
                fixture.Transform,
                isSuppressed: false,
                arrivalDistanceUnits: ArrivalDistanceUnits,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(sample * 34));
            // Hosts consume this intent each tick, including before the door opens.
            if (fixture.Controller.TryConsumeHeldAutoWalkRequest()) autoWalkStarts++;
            var where = $"{context}, observation {sample} at ({position.X},{position.Y}) " +
                        $"triangle {position.TriangleId}";

            if (unlockFrame < 0)
            {
                // Nothing about the leg to the line may be reported as arriving at the
                // destination, and the destination may not stop being the destination.
                Equal(scenario.Target.Label, fixture.Controller.HeldForNativeBoundaryLabel,
                    $"{where}: the destination must still be held while the door is shut");
                NotContains("reached", update?.Speech,
                    $"{where}: reaching the line is not reaching the destination");
                NotContains("Navigation off", update?.Speech,
                    $"{where}: nothing may give up while the approach is running");
                Equal(false, position.TriangleId == scenario.LockedTriangle,
                    $"{where}: the party must never stand inside the locked triangle");
            }
            else if (update?.Speech is { } speech &&
                     speech.Contains("reached", StringComparison.OrdinalIgnoreCase))
            {
                Contains(scenario.Target.Label, speech,
                    $"{where}: the only thing that may be announced as reached is the destination");
                reachedFrame = frames;
                break;
            }

            // The field script notices the crossing, after however long it takes.
            if (crossedAtSample >= 0 && unlockFrame < 0)
            {
                if (sample - crossedAtSample >= scriptDelaySamples)
                {
                    fixture.Boundary.Unlock(scenario.LockedTriangle);
                    unlockFrame = frames;
                }
                else
                {
                    delayedSamples++;
                    heldThroughTheDelay &= string.Equals(
                        fixture.Controller.HeldForNativeBoundaryLabel,
                        scenario.Target.Label,
                        StringComparison.Ordinal);
                    // Continue applying emitted input; only the controller may stop the walk.
                }
            }

            if (!fixture.Controller.TryResolveAutomaticInput(
                    position, fixture.Transform, ArrivalDistanceUnits, out var input))
            {
                // Standing on the line waiting for the script is the route working. Anything
                // else is the approach failing to produce the movement it promised.
                lastStall = $"observation {sample}: no direction, " +
                            $"{fixture.Controller.LastAutomaticInputHold}, " +
                            fixture.Controller.LastNavigationDiagnostic;
                Equal(true, fixture.Controller.LastAutomaticInputHold is
                    FieldAutoWalkHoldReason.Arrived or FieldAutoWalkHoldReason.NoClearDirection,
                    $"{where}: movement may pause for arrival or a blocked direction; the bounded replay must still complete: " +
                    fixture.Controller.LastNavigationDiagnostic);
                lastInput = FieldNavigationInput.None;
                continue;
            }

            inputs.Add(input.ToString());
            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(
                input, fixture.Transform);
            var previousPosition = position;
            if (!TryNativeFrame(
                    mesh, ref position, dx, dy, fixture.Boundary.IsLocked, out var refused))
            {
                lastStall = $"observation {sample}: {input} refused - {refused}";
                lastInput = input;
                continue;
            }

            frames++;
            if (unlockFrame >= 0 && scenario.Target.TriggerLine is { } gateway &&
                CrossesGateway(previousPosition, position, gateway))
            {
                position = position with { FieldId = destinationField };
            }
            if (crossedAtSample < 0)
            {
                var distanceSquared = DistanceSquaredToSegment(position, scenario.Line);
                if (distanceSquared < radiusSquared)
                {
                    crossedAtSample = sample;
                    // Native Go 1x disables its line before continuing the opening script.
                    fixture.Controller.NativeLineIsEnabled = (_, _) => false;
                    unlockDistanceSquared = distanceSquared;
                }
                else
                {
                    previousDistanceSquared = distanceSquared;
                }
            }

            lastInput = input;
        }

        Equal(true, unlockFrame >= 0,
            $"{context}: the emitted input must actually reach the line - stopped after " +
            $"{frames} native frames at ({position.X},{position.Y}), nearest squared distance " +
            $"{previousDistanceSquared} against {radiusSquared}; last stall: {lastStall}; " +
            $"target now '{fixture.Controller.CurrentTargetLabel}', beacon " +
            $"{fixture.Controller.BeaconEnabled}, input {Summarise(inputs)}");
        Equal(true, unlockDistanceSquared >= 0 && unlockDistanceSquared < radiusSquared,
            $"{context}: the lock may only clear strictly inside the native radius - " +
            $"{unlockDistanceSquared} against {radiusSquared}");
        Equal(true, previousDistanceSquared < 0 || previousDistanceSquared >= radiusSquared,
            $"{context}: and the frame before it must have been outside - " +
            $"{previousDistanceSquared} against {radiusSquared}");
        if (reachedFrame < 0)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(fixture.Controller.CreateProbeSnapshot(position).Route));
        }
        Equal(true, reachedFrame >= 0,
            $"{context}: the walk must carry on to the gateway the player asked for - stopped " +
            $"after {frames} native frames at ({position.X},{position.Y}); last stall: " +
            $"{lastStall}; target now '{fixture.Controller.CurrentTargetLabel}', beacon " +
            $"{fixture.Controller.BeaconEnabled}");
        Equal(string.Empty, fixture.Controller.HeldForNativeBoundaryLabel,
            $"{context}: and nothing is left held once it is there");
        Equal(2, autoWalkStarts,
            $"{context}: consumed approach intent must restart the final leg exactly once");

        return new ReplayOutcome(
            sample, frames, unlockFrame, unlockDistanceSquared, previousDistanceSquared,
            reachedFrame, delayedSamples, heldThroughTheDelay, Summarise(inputs));
    }

    // Simulate MAPJUMP only after generated movement crosses the actual gateway
    // segment. No generic interaction radius is allowed to end the final leg.
    private static bool CrossesGateway(FieldPositionSnapshot before, FieldPositionSnapshot after,
        FieldNavigationTriggerLine line)
    {
        double rx = after.X - before.X, ry = after.Y - before.Y;
        double sx = line.EndX - line.StartX, sy = line.EndY - line.StartY;
        var denominator = rx * sy - ry * sx;
        if (Math.Abs(denominator) < 1e-9) return false;
        double qx = line.StartX - before.X, qy = line.StartY - before.Y;
        var movementFraction = (qx * sy - qy * sx) / denominator;
        var gatewayFraction = (qx * ry - qy * rx) / denominator;
        if (movementFraction < 0 || movementFraction > 1 || gatewayFraction < 0 || gatewayFraction > 1)
            return false;
        var crossingZ = before.Z + movementFraction * (after.Z - before.Z);
        var gatewayZ = line.StartZ + gatewayFraction * (line.EndZ - line.StartZ);
        return Math.Abs(crossingZ - gatewayZ) <= 8;
    }

    /// <summary>
    /// A kinematic movement step constrained to the installed walkmesh.
    ///
    /// <para>This simulation tries nearby forward headings when an edge blocks movement.
    /// It does not emulate all native collision probes or slope physics, so it checks
    /// controller continuity and route geometry, not a complete live playthrough. That rotation is what sliding along a wall is;
    /// without it a replay wedges in a corner the game itself walks straight out of. A frame
    /// no rotation clears is a frame the party does not take, and the next observation
    /// steers again from where it really is.</para>
    ///
    /// <para>The floor is followed too. Triangle resolution weights the height difference
    /// heavily, and a walk that kept the Z it started with drifts onto whatever triangle
    /// happens to be stacked above or below the one the party is really on - 541 has such a
    /// pair, and the walk left the corridor at the first of them.</para>
    /// </summary>
    private static bool TryNativeFrame(
        FieldWalkmesh mesh,
        ref FieldPositionSnapshot position,
        double dx,
        double dy,
        Func<int, bool> isLocked,
        out string diagnostic)
    {
        diagnostic = "no candidate heading moved the party";
        for (var attempt = 0; attempt <= 8; attempt++)
        {
            foreach (var sign in attempt == 0 ? new[] { 0 } : new[] { 1, -1 })
            {
                var angle = sign * attempt * (2d * Math.PI / 32d);
                var rotatedX = (dx * Math.Cos(angle)) - (dy * Math.Sin(angle));
                var rotatedY = (dx * Math.Sin(angle)) + (dy * Math.Cos(angle));
                if ((rotatedX * dx) + (rotatedY * dy) <= 0d)
                {
                    continue;
                }

                var current = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
                var next = new FieldNavigationRouteWaypoint(
                    position.X + (int)Math.Round(rotatedX * NativeStepLength),
                    position.Y + (int)Math.Round(rotatedY * NativeStepLength),
                    position.Z);
                if (next.X == position.X && next.Y == position.Y)
                {
                    continue;
                }

                // The lock is a wall to the native movement, so it is a wall here.
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    mesh, position.TriangleId, current, next, isLocked, allowStartingEdgeCrossing: true);
                if (!trace.IsClear)
                {
                    diagnostic = trace.Diagnostic;
                    continue;
                }

                position = position with
                {
                    X = next.X,
                    Y = next.Y,
                    Z = FloorHeight(mesh, trace.EndTriangle, next.X, next.Y, position.Z),
                    TriangleId = (ushort)trace.EndTriangle,
                };
                return true;
            }
        }

        return false;
    }

    /// <summary>The height of the walkmesh under a point, so the party stays on the floor.</summary>
    private static int FloorHeight(
        FieldWalkmesh mesh, int triangleIndex, int x, int y, int fallback)
    {
        if ((uint)triangleIndex >= mesh.Triangles.Count)
        {
            return fallback;
        }

        var triangle = mesh.Triangles[triangleIndex];
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (((double)b.Y - c.Y) * ((double)a.X - c.X)) +
                          (((double)c.X - b.X) * ((double)a.Y - c.Y));
        if (Math.Abs(denominator) < 1e-9d)
        {
            return (int)Math.Round(triangle.GetCentroid().Z);
        }

        var weightA = ((((double)b.Y - c.Y) * (x - c.X)) + (((double)c.X - b.X) * (y - c.Y))) /
                      denominator;
        var weightB = ((((double)c.Y - a.Y) * (x - c.X)) + (((double)a.X - c.X) * (y - c.Y))) /
                      denominator;
        return (int)Math.Round(
            (weightA * a.Z) + (weightB * b.Z) + ((1d - weightA - weightB) * c.Z));
    }

    private static string Summarise(List<string> inputs)
    {
        if (inputs.Count == 0)
        {
            return "(none)";
        }

        var runs = new List<string>();
        var current = inputs[0];
        var count = 1;
        for (var index = 1; index < inputs.Count; index++)
        {
            if (string.Equals(inputs[index], current, StringComparison.Ordinal))
            {
                count++;
                continue;
            }

            runs.Add($"{current}x{count}");
            current = inputs[index];
            count = 1;
        }

        runs.Add($"{current}x{count}");
        return string.Join(' ', runs);
    }

    // Independent translation of installed FUN_00637879 (ghidra-line-distance.log).
    // The game quantizes a 3D projection to 1/256 and rejects projections outside
    // the segment's XY bounds; it does not clamp them to a 2D endpoint capsule.
    private static long DistanceSquaredToSegment(
        FieldPositionSnapshot position, FieldNavigationTriggerLine line)
    {
        long abx = line.EndX - line.StartX;
        long aby = line.EndY - line.StartY;
        long abz = line.EndZ - line.StartZ;
        var lengthSquared = abx * abx + aby * aby + abz * abz;
        if (lengthSquared == 0) return long.MaxValue;
        var projection = ((position.X - line.StartX) * abx +
                          (position.Y - line.StartY) * aby +
                          (position.Z - line.StartZ) * abz) * 256 / lengthSquared;
        var px = line.StartX + ((projection * abx) >> 8);
        var py = line.StartY + ((projection * aby) >> 8);
        var pz = line.StartZ + ((projection * abz) >> 8);
        if (px < Math.Min(line.StartX, line.EndX) || px > Math.Max(line.StartX, line.EndX) ||
            py < Math.Min(line.StartY, line.EndY) || py > Math.Max(line.StartY, line.EndY))
            return long.MaxValue;
        var dx = position.X - px;
        var dy = position.Y - py;
        var dz = position.Z - pz;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>The production controller over an installed walkmesh and a releasable lock.</summary>
    private sealed class Fixture
    {
        private readonly int field;

        public Fixture(
            Func<int, FieldWalkmeshReader> createWalkmeshReader,
            int field,
            int[] lockedTriangles,
            FieldWalkmeshReader? reader = null)
        {
            this.field = field;
            Boundary = new MutableBoundary(field, lockedTriangles);
            Planner = new FieldWalkmeshRoutePlanner(
                reader ?? createWalkmeshReader(field),
                new FieldBoundaryStateReader(Boundary.ReadInt32, Boundary.ReadByte, (_, _) => true));
            Controller = NewController(new FieldNavigationTargetSource([]));
        }

        public MutableBoundary Boundary { get; }

        public FieldWalkmeshRoutePlanner Planner { get; }

        public FieldNavigationController Controller { get; private set; }

        public FieldNavigationControlTransform Transform { get; } = new(0);

        /// <summary>Selects the first of these targets, the way the player would.</summary>
        public FieldNavigationActionResult? Select(
            FieldPositionSnapshot position, params FieldNavigationTarget[] targets)
        {
            Offer(position, targets);
            return Controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, Transform);
        }

        /// <summary>Puts these targets in front of the player without choosing one.</summary>
        public void Offer(FieldPositionSnapshot position, params FieldNavigationTarget[] targets)
        {
            Controller = NewController(new FieldNavigationTargetSource(targets));
            for (var guard = 0;
                 guard < 8 && Controller.CurrentCategory != targets[0].Category;
                 guard++)
            {
                Controller.HandleAction(FieldNavigationAction.NextCategory, position, Transform);
            }
        }

        /// <summary>Moves the cursor onto a named target, the way the player would.</summary>
        public void MoveCursorTo(FieldPositionSnapshot position, string label)
        {
            for (var press = 0; press < 8; press++)
            {
                var spoken = Controller
                    .HandleAction(FieldNavigationAction.RepeatTarget, position, Transform)?.Speech;
                if (spoken is not null && spoken.Contains(label, StringComparison.Ordinal))
                {
                    return;
                }

                Controller.HandleAction(FieldNavigationAction.NextTarget, position, Transform);
            }

            throw new InvalidOperationException($"the selection never reached '{label}'");
        }

        private FieldNavigationController NewController(FieldNavigationTargetSource source)
        {
            // What both hosts supply from the shared FieldScriptLineStateReader. Every line in
            // these scenarios is one the game has switched on where the replay starts; the
            // disabled and unreadable answers are covered in the caller cases.
            return new FieldNavigationController(source, Planner)
            {
                NativeLineIsEnabled = (fieldId, _) => fieldId == field ? true : null,
            };
        }
    }

    /// <summary>The production caller both hosts build, over the same fixture.</summary>
    private sealed class Caller
    {
        private readonly Scenario scenario;
        private readonly Fixture fixture;
        private bool autoWalkRunning;

        public Caller(
            Func<int, FieldWalkmeshReader> createWalkmeshReader,
            Scenario scenario,
            FieldNavigationTarget[]? targets = null)
        {
            this.scenario = scenario;
            fixture = new Fixture(createWalkmeshReader, scenario.Field, [scenario.LockedTriangle]);
            fixture.Offer(scenario.Start, targets ?? [scenario.Target]);
            fixture.MoveCursorTo(scenario.Start, scenario.Target.Label);

            Services = new ControllerNavigationServices(
                () => Controller.BeaconEnabled,
                action => Controller.HandleAction(action, scenario.Start, fixture.Transform)?.Speech,
                () => autoWalkRunning,
                () =>
                {
                    autoWalkRunning = true;
                    AutoWalkStarts++;
                    return true;
                },
                () => autoWalkRunning = false,
                () => { },
                () => Controller.IsHoldingForNativeBoundary,
                () => Controller.RequestAutoWalkForHeldRoute());
        }

        public FieldNavigationController Controller => fixture.Controller;

        public ControllerNavigationServices Services { get; }

        public MutableBoundary Boundary => fixture.Boundary;

        public FieldNavigationControlTransform Transform => fixture.Transform;

        public int AutoWalkStarts { get; private set; }

        /// <summary>One frame of the host loop, as both runtimes run it.</summary>
        public void Tick()
        {
            Controller.UpdateLiveTracking(
                scenario.Start, default, fixture.Transform, isSuppressed: false);
            if (Controller.TryConsumeHeldAutoWalkRequest())
            {
                autoWalkRunning = true;
                AutoWalkStarts++;
            }
        }
    }

    /// <summary>A boundary the test can release, and only ever does after the party arrives.</summary>
    private sealed class MutableBoundary
    {
        private const int FieldState = 0x02800000;
        private readonly Dictionary<int, byte> bytes = [];

        public MutableBoundary(int field, IEnumerable<int> triangles)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            for (var index = 0; index < 4; index++)
            {
                bytes[FieldBoundaryStateReader.AddressFieldGlobalObjectPtr + index] =
                    (byte)(FieldState >> (index * 8));
            }

            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] = (byte)(ReadByte(address) | (1 << (triangle & 7)));
            }
        }

        public void Unlock(int triangle)
        {
            var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
            bytes[address] = (byte)(ReadByte(address) & ~(1 << (triangle & 7)));
        }

        public bool IsLocked(int triangle)
        {
            var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
            return (ReadByte(address) & (1 << (triangle & 7))) != 0;
        }

        public int ReadInt32(int address) =>
            ReadByte(address) | (ReadByte(address + 1) << 8) |
            (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24);

        public byte ReadByte(int address) => bytes.TryGetValue(address, out var value) ? value : (byte)0;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"observatory approach - {label}: expected {expected}, got {actual}");
        }
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"observatory approach - {label}: expected '{expected}' in '{actual}'");
        }
    }

    private static void NotContains(string unexpected, string? actual, string label)
    {
        if (actual is not null && actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"observatory approach - {label}: '{unexpected}' must not appear in '{actual}'");
        }
    }
}
