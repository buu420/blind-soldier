using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Shooting Coaster's drawn targets, decided the way the native renderer decides
/// them, and the aim feedback built on top.
///
/// The thing these tests exist to hold is that a projected box is not evidence of a
/// visible model. Visibility is the triangle renderer's own answer - at least one
/// transformed vertex in front of the camera, in a model that is not drawn black -
/// and the half-space gate is a separate thing that guards the hit flag afterwards.
/// </summary>
internal static class SpeedSquareCoasterTargetTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        OnlyObjectsInUseAreConsidered();
        VisibilityIsTheRenderersOwnGeometryTest();
        TheCameraAndParentAreComposedTheNativeWay();
        ANonPresentedFrameHoldsTheWholeLastObservation();
        TheHalfSpaceGuardsTheHitFlagRatherThanVisibility();
        AFrameThatChangesMidReadIsRefused();
        TheSightComesFromTheFrameRatherThanTheCaller();
        TheSightTestMatchesTheNativeHitTest();
        TheAimReadoutSteersAndReportsResolvedHits();
        ACachedFlagIsNotANewHit();
        AReusedSlotIsADifferentTarget();
        NothingIsReportedWhileTheRideIsSuspended();
        TheDirectionalCueUsesTheEstablishedAudioConventions();
    }

    private static void OnlyObjectsInUseAreConsidered()
    {
        var memory = new FakeCoasterMemory();
        var reader = new SpeedSquareCoasterTargetReader(memory);

        memory.Module = 1;
        Equal(false, reader.TryReadTargets(160, 120, out _), "only the coaster module is read");

        memory.Module = SpeedSquareCoasterStateReader.CoasterModule;
        Equal(true, reader.TryReadTargets(160, 120, out var empty), "an empty ride reads");
        Equal(0, empty.Targets.Count, "with no targets while every slot is free");
        Equal(true, empty.WasPresented, "and the frame was presented");

        // The word at +0xDA says in use or not, and nothing else: the allocator
        // writes 1 for every object it ever hands out.
        memory.PlaceTarget(index: 3, left: 100, top: 100, right: 140, bottom: 130);
        memory.SetActive(3, 0);
        Equal(true, reader.TryReadTargets(160, 120, out var free), "the ride still reads");
        Equal(0, free.Targets.Count, "a free slot is not a target however good its box looks");

        memory.SetActive(3, 1);
        Equal(true, reader.TryReadTargets(160, 120, out var live), $"the live object reads; {reader.LastDiagnostic}");
        Equal(1, live.Targets.Count, "and is a target");
        Equal(120, live.Targets[0].CentreX, "with its box centre in sight coordinates");
        Equal(115, live.Targets[0].CentreY, "in both axes");

        // Only the six initialised bound points may be folded in.
        memory.SetReservedBoundPoints(index: 3, x: -30000, y: 30000);
        Equal(true, reader.TryReadTargets(160, 120, out var reserved), "the ride reads");
        Equal(120, reserved.Targets.Single(target => target.Index == 3).CentreX,
            "the two uninitialised bound points are not geometry");

        // Nearest to the sight first.
        memory.PlaceTarget(index: 4, left: 150, top: 110, right: 170, bottom: 130);
        Equal(true, reader.TryReadTargets(160, 120, out var sorted), "both targets read");
        Equal(2, sorted.Targets.Count, "both are on screen");
        Equal(4, sorted.Targets[0].Index, "the nearest to the sight comes first");

        // A torn read is silence, not an empty screen.
        memory.UnreadableObjectIndex = 4;
        Equal(false, reader.TryReadTargets(160, 120, out _), "an unreadable object is not an empty ride");
    }

    /// <summary>
    /// FUN_005EFA3A walks the model's triangles and FUN_005F04D7 transforms all three
    /// vertices of each, drawing only when at least one has a view depth above zero.
    /// The node's origin is not consulted, and cannot stand in for the geometry: it
    /// can sit in front of a model that is entirely behind, and behind one that is
    /// entirely in front.
    /// </summary>
    private static void VisibilityIsTheRenderersOwnGeometryTest()
    {
        var memory = new FakeCoasterMemory();
        var reader = new SpeedSquareCoasterTargetReader(memory);
        memory.PlaceTarget(index: 0, left: 150, top: 110, right: 172, bottom: 128);

        Equal(true, reader.TryReadTargets(160, 120, out var ordinary), $"the ride reads; {reader.LastDiagnostic}");
        Equal(1, ordinary.Targets.Count, "an ordinary lit model in front is a target");

        // The origin stays a hundred units in front; every vertex is two hundred
        // behind it, so nothing of the model is in front of the camera.
        memory.SetGeometryDepth(0, -200);
        Equal(true, reader.TryReadTargets(160, 120, out var behind), "the ride reads");
        Equal(0, behind.Targets.Count,
            "a model whose every vertex is behind the camera is not drawn, whatever its origin says");

        // And the other way round: the origin is behind, the geometry is not.
        memory.SetNodeTranslation(0, 0, 0, -100);
        memory.SetGeometryDepth(0, 200);
        Equal(true, reader.TryReadTargets(160, 120, out var crossing), "the ride reads");
        Equal(1, crossing.Targets.Count,
            "and a model reaching in front of the camera is drawn, whatever its origin says");

        // Exactly on the plane is not in front: FUN_005F04D7 wants strictly greater.
        memory.SetNodeTranslation(0, 0, 0, 0);
        memory.SetGeometryDepth(0, 0);
        Equal(true, reader.TryReadTargets(160, 120, out var onPlane), "the ride reads");
        Equal(0, onPlane.Targets.Count, "a model exactly at the camera is not in front of it");

        // Past the far light bound every vertex is drawn in the dark constant.
        memory.SetNodeTranslation(0, 0, 0, memory.LightFar + 500);
        memory.SetGeometryDepth(0, 0);
        Equal(true, reader.TryReadTargets(160, 120, out var dark), "the ride reads");
        Equal(0, dark.Targets.Count, "and a model drawn entirely black is not something to aim at");

        memory.SetNodeTranslation(0, 0, 0, memory.LightFar - 1);
        Equal(true, reader.TryReadTargets(160, 120, out var lit), "the ride reads");
        Equal(1, lit.Targets.Count, "but one just inside the lit range is drawn");

        // A model with no triangles draws nothing, and that is an answer rather than
        // a failed read.
        memory.SetNodeTranslation(0, 0, 0, 100);
        memory.SetTriangleCount(0, 0);
        Equal(true, reader.TryReadTargets(160, 120, out var empty), "an empty model reads");
        Equal(0, empty.Targets.Count, "and draws nothing");

        // A count that could not be a coaster prop is a torn header, not a model.
        memory.SetTriangleCount(0, short.MaxValue);
        Equal(false, reader.TryReadTargets(160, 120, out _), "an impossible triangle count is refused");
    }

    /// <summary>
    /// FUN_00661E85 converts each packed PSX matrix with FUN_006611FB - rotation over
    /// 4096, translation kept - and multiplies them with FUN_0066C984 as C = A * B.
    /// FUN_005F2759 then takes the depth from the third column. Reading the rotation
    /// as row dot products instead would drop targets a quarter-turn camera puts
    /// squarely in front of the player.
    /// </summary>
    private static void TheCameraAndParentAreComposedTheNativeWay()
    {
        var memory = new FakeCoasterMemory();
        var reader = new SpeedSquareCoasterTargetReader(memory);
        memory.PlaceTarget(index: 0, left: 150, top: 110, right: 172, bottom: 128);

        // A hundred units along +X, and a camera turned a quarter turn so that +X is
        // what it is looking at.
        memory.SetNodeTranslation(0, 100, 0, 0);
        memory.SetCameraRotation([0, 0, 4096, 0, 4096, 0, -4096, 0, 0]);
        Equal(true, reader.TryReadTargets(160, 120, out var turned), $"the turned camera reads; {reader.LastDiagnostic}");
        Equal(1, turned.Targets.Count, "a target the turned camera faces is still in front of it");

        // The same object with the camera turned the other way is behind.
        memory.SetCameraRotation([0, 0, -4096, 0, 4096, 0, 4096, 0, 0]);
        Equal(true, reader.TryReadTargets(160, 120, out var away), "the ride reads");
        Equal(0, away.Targets.Count, "and turning away from it puts it behind the camera");

        // A node hanging off another node has its parent applied first.
        memory.SetCameraRotation([4096, 0, 0, 0, 4096, 0, 0, 0, 4096]);
        memory.SetNodeTranslation(0, 0, 0, -400);
        Equal(true, reader.TryReadTargets(160, 120, out var rooted), "the ride reads");
        Equal(0, rooted.Targets.Count, "the child alone is behind the camera");

        memory.SetParent(0, memory.ParentNodeAddress);
        memory.SetParentTranslation(0, 0, 0, 600);
        Equal(true, reader.TryReadTargets(160, 120, out var parented), $"the parented object reads; {reader.LastDiagnostic}");
        Equal(1, parented.Targets.Count, "but the parent's own transform carries it in front");

        memory.SetParent(0, 0);
        Equal(false, reader.TryReadTargets(160, 120, out _), "a parent that is not a node is a failed read");
    }

    /// <summary>
    /// FUN_005E8E7E runs catch-up refreshes with the presentation flag clear and one
    /// presented refresh with it set, and FUN_005E99FB rewrites the projected bounds
    /// either way. The whole observation is held across those, not just its boxes:
    /// pairing last frame's boxes with this frame's cursor, score or trigger would
    /// describe a moment that never happened.
    /// </summary>
    private static void ANonPresentedFrameHoldsTheWholeLastObservation()
    {
        var memory = new FakeCoasterMemory { Score = 300, Firing = 1 };
        var reader = new SpeedSquareCoasterTargetReader(memory);
        memory.PlaceTarget(index: 0, left: 100, top: 100, right: 140, bottom: 130);

        Equal(true, reader.TryReadTargets(160, 120, out var presented), "the presented frame reads");
        Equal(1, presented.Targets.Count, "with its target");
        Equal(120, presented.Targets[0].CentreX, "at its drawn position");
        Equal(160, presented.CursorX, "and carries the cursor it was read with");
        Equal(300, presented.Score, "the score");
        Equal(true, presented.Firing, "and the trigger");

        // A catch-up pass moves the bounds, the sight, the score and the trigger
        // without drawing anything.
        memory.PresentationFlag = 0;
        memory.Score = 400;
        memory.Firing = 0;
        memory.PlaceTarget(index: 0, left: 10, top: 10, right: 30, bottom: 20);
        Equal(true, reader.TryReadTargets(40, 40, out var caughtUp), "the catch-up pass reads");
        Equal(false, caughtUp.WasPresented, "but reports itself as not presented");
        Equal(120, caughtUp.Targets[0].CentreX, "and holds the image that really was drawn");
        Equal(160, caughtUp.CursorX, "with the sight that went with it");
        Equal(300, caughtUp.Score, "the score that went with it");
        Equal(true, caughtUp.Firing, "and the trigger that went with it");

        // Nothing is said about a frame the engine never presented.
        var readout = new SpeedSquareCoasterAimReadout();
        var state = new SpeedSquareCoasterState(true, false, 160, 120, 128);
        Equal(true, readout.Observe(state, caughtUp, Start).IsEmpty,
            "and the readout says nothing about an image nobody saw");

        memory.PresentationFlag = 1;
        Equal(true, reader.TryReadTargets(40, 40, out var drawn), "the next presented frame reads");
        Equal(20, drawn.Targets[0].CentreX, "and takes the new position");
        Equal(400, drawn.Score, "and the new score");

        // Before anything has ever been presented there is nothing to hold.
        var cold = new SpeedSquareCoasterTargetReader(new FakeCoasterMemory { PresentationFlag = 0 });
        Equal(false, cold.TryReadTargets(160, 120, out _),
            "a catch-up pass before any presented frame reports nothing rather than an empty screen");
    }

    /// <summary>
    /// FUN_005E99FB applies FUN_005EECB5 after rendering and uses it to guard the hit
    /// flag at +0x2C. It is not the renderer's visibility test, and using it as one
    /// would silence targets the player can see and shoot at.
    /// </summary>
    private static void TheHalfSpaceGuardsTheHitFlagRatherThanVisibility()
    {
        var memory = new FakeCoasterMemory();
        var reader = new SpeedSquareCoasterTargetReader(memory);
        memory.PlaceTarget(index: 0, left: 150, top: 110, right: 172, bottom: 128);

        Equal(true, reader.TryReadTargets(160, 120, out var accepted), "the accepted object reads");
        Equal(1, accepted.Targets.Count, "and is a target");
        Equal(true, accepted.Targets[0].HalfSpaceAccepted, "with the gate passing");

        memory.HalfSpaceReferenceA = -1;
        Equal(true, reader.TryReadTargets(160, 120, out var rejected), "the ride reads");
        Equal(1, rejected.Targets.Count,
            "an object the hit gate rejects is still drawn, so it is still something to aim at");
        Equal(false, rejected.Targets[0].HalfSpaceAccepted, "but the gate is reported as not passing");

        // And with the gate closed the flag cannot have been refreshed, so no hit is
        // resolved however the flag itself reads.
        memory.SetHit(0, 1);
        Equal(true, reader.TryReadTargets(160, 120, out var flagged), "the ride reads");
        Equal(false, flagged.Targets[0].IsResolvedHit(firing: true),
            "a set flag behind a closed gate is not a resolved hit");

        memory.HalfSpaceReferenceA = 1;
        memory.HalfSpaceReferenceB = -1;
        Equal(true, reader.TryReadTargets(160, 120, out var second), "the ride reads");
        Equal(false, second.Targets[0].HalfSpaceAccepted, "the second plane rejects independently");

        // A plane value of exactly zero is on neither side, and the native test
        // refuses it.
        memory.HalfSpaceReferenceB = 1;
        memory.HalfSpaceConstantA = 0;
        memory.HalfSpaceCoefficientsA = (0, 0, 0);
        Equal(true, reader.TryReadTargets(160, 120, out var onPlane), "the ride reads");
        Equal(false, onPlane.Targets[0].HalfSpaceAccepted,
            "a plane value of exactly zero is rejected, as the native test does");
    }

    /// <summary>
    /// None of this is read atomically. A frame that straddles a change to anything
    /// the whole observation rests on is half one picture and half another - and two
    /// of those changes leave the module and the presentation flag exactly where they
    /// were, so neither of those on its own is enough.
    /// </summary>
    private static void AFrameThatChangesMidReadIsRefused()
    {
        var bounds = (uint)(SpeedSquareCoasterTargetReader.AddressObjectArray +
                            SpeedSquareCoasterTargetReader.ObjectProjectedBoundsOffset);

        var memory = new FakeCoasterMemory();
        var reader = new SpeedSquareCoasterTargetReader(memory);
        memory.PlaceTarget(index: 0, left: 150, top: 110, right: 172, bottom: 128);
        memory.SetSightCursor(160, 120);
        Equal(true, reader.TryReadTargets(160, 120, out var settled), "a settled frame reads");
        Equal(1, settled.Targets.Count, "with its target");

        memory.ChangeAfterReading(bounds, () => memory.Module = 1);
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "a module change while the boxes were being sampled is not a frame");

        memory.Module = SpeedSquareCoasterStateReader.CoasterModule;
        memory.ChangeAfterReading(bounds, () => memory.PresentationFlag = 0);
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "and neither is one that stopped being presented part way through");

        // The sight moves under the player's hand without touching either.
        memory.PresentationFlag = 1;
        memory.ChangeAfterReading(bounds, () => memory.SetSightCursor(190, 120));
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "a sight that moved while the boxes were sampled cannot be paired with them");

        // Nor does the camera moving, and the ride's camera moves constantly.
        memory.SetSightCursor(160, 120);
        memory.ChangeAfterReading(
            bounds, () => memory.SetCameraRotation([4000, 0, 0, 0, 4096, 0, 0, 0, 4096]));
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "and geometry composed against two different cameras is not one frame");

        // An object released mid-read leaves its slot to the next occupant.
        memory.SetCameraRotation([4096, 0, 0, 0, 4096, 0, 0, 0, 4096]);
        memory.ChangeAfterReading(bounds, () => memory.SetActive(0, 0));
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "an object released while its own bounds were read is not in the snapshot");

        // The slot stays in use and is handed to a different object, which the in-use
        // word cannot show because the new occupant inherits the same 1.
        memory.SetActive(0, 1);
        memory.ChangeAfterReading(
            bounds, () => memory.SetNodePointer(0, memory.NodeAddress(7)));
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "an object whose node was replaced mid-read is not the object those bounds belong to");

        // The trigger goes up and down under the player's finger, and it is one of the
        // three conditions the engine uses to resolve a hit.
        memory.SetNodePointer(0, memory.NodeAddress(0));
        memory.Firing = 1;
        memory.ChangeAfterReading(bounds, () => memory.Firing = 0);
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "a shot that ended while the boxes were sampled cannot be paired with them");

        // And the targets themselves move along their own paths every frame.
        memory.Firing = 0;
        memory.ChangeAfterReading(bounds, () => memory.SetNodeTranslation(0, 0, 0, -100));
        Equal(false, reader.TryReadTargets(160, 120, out _),
            "boxes projected from one transform are not described against another");

        memory.SetNodeTranslation(0, 0, 0, 100);
        Equal(true, reader.TryReadTargets(160, 120, out var recovered),
            $"and the next settled frame reads normally; {reader.LastDiagnostic}");
        Equal(1, recovered.Targets.Count, "with its target back");
    }

    /// <summary>
    /// The sight the snapshot reports is the one the game is drawing, read inside the
    /// capture. Whatever the caller sampled before calling was already taken before
    /// this frame began, and describing a target's direction against it would place
    /// the target somewhere the player never aimed.
    /// </summary>
    private static void TheSightComesFromTheFrameRatherThanTheCaller()
    {
        var memory = new FakeCoasterMemory();
        var reader = new SpeedSquareCoasterTargetReader(memory);
        memory.PlaceTarget(index: 0, left: 150, top: 110, right: 172, bottom: 128);
        memory.SetSightCursor(160, 120);
        Equal(true, reader.TryReadTargets(160, 120, out var agreed), "a caller in step with the game reads");
        Equal(160, agreed.CursorX, "and the sight is the one both agree on");

        // The caller's copy has gone stale between its own read and this one.
        memory.SetSightCursor(190, 130);
        Equal(true, reader.TryReadTargets(160, 120, out var moved),
            $"a stale caller reading does not lose the frame; {reader.LastDiagnostic}");
        Equal(190, moved.CursorX, "the snapshot carries the sight the game is drawing");
        Equal(130, moved.CursorY, "in both axes");
        Equal(true, reader.LastDiagnostic.Contains("stale", StringComparison.Ordinal),
            $"and the caller is told its own reading had aged; got {reader.LastDiagnostic}");
    }

    /// <summary>
    /// The native test compares the folded box edges against cursor * scale + offset,
    /// strictly inside on all four sides.
    /// </summary>
    private static void TheSightTestMatchesTheNativeHitTest()
    {
        var memory = new FakeCoasterMemory { ScaleX = 3, ScaleY = 2, OffsetX = 17, OffsetY = -9 };
        var reader = new SpeedSquareCoasterTargetReader(memory);

        var sightScreenX = (160 * memory.ScaleX) + memory.OffsetX;
        var sightScreenY = (120 * memory.ScaleY) + memory.OffsetY;
        memory.PlaceTargetInScreenSpace(
            0, sightScreenX - 30, sightScreenY - 20, sightScreenX + 30, sightScreenY + 20);
        Equal(true, reader.TryReadTargets(160, 120, out var inside), "the target reads");
        Equal(true, inside.Targets[0].IsUnderSight, "a sight inside the box overlaps it");

        memory.SetScreenBox(0, sightScreenX, sightScreenY - 20, sightScreenX + 30, sightScreenY + 20);
        Equal(true, reader.TryReadTargets(160, 120, out var onEdge), "the target reads");
        Equal(false, onEdge.Targets[0].IsUnderSight, "the native test is strict on every side");

        memory.SetScreenBox(0, sightScreenX + 5, sightScreenY - 20, sightScreenX + 40, sightScreenY + 20);
        Equal(true, reader.TryReadTargets(160, 120, out var beside), "the target reads");
        Equal(false, beside.Targets[0].IsUnderSight, "a box beside the sight does not overlap it");
    }

    private static void TheAimReadoutSteersAndReportsResolvedHits()
    {
        var readout = new SpeedSquareCoasterAimReadout();
        var state = new SpeedSquareCoasterState(true, false, 160, 120, 128);

        var opening = readout.Observe(state, Snapshot(160, 120, Target(0, 260, 120)), Start);
        Equal(true, opening.Speech?.StartsWith(
                SpeedSquareCoasterAimReadout.CueExplanation, StringComparison.Ordinal) == true,
            "the ride opens by explaining the mod's own cue, not the native instructions");
        Equal(true, opening.Speech?.EndsWith("Target right 5.", StringComparison.Ordinal) == true,
            $"and the nearest target's direction in sight steps; got {opening.Speech}");
        Equal(true, opening.Beacon is not null, "and plays on the mod's own spatial device");

        Equal(null, readout.Observe(state, Snapshot(160, 120, Target(0, 260, 120)), Start).Speech,
            "an unchanged aim says nothing");

        var closer = readout.Observe(
            state, Snapshot(160, 120, Target(0, 200, 120)), Start.AddMilliseconds(50));
        Equal("Target right 2.", closer.Speech, "closing on the target is described");

        var centred = readout.Observe(
            state,
            Snapshot(160, 120, Target(0, 160, 120, underSight: true)),
            Start.AddMilliseconds(100));
        Equal("Sight on target.", centred.Speech,
            "the reticle overlapping the target is stated as a present fact");
        Equal(NavigationBeaconMovementState.OnCourse, centred.Beacon!.Value.MovementState,
            "and the cue says so too");

        var hit = readout.Observe(
            state,
            Snapshot(160, 120, firing: true, Target(0, 160, 120, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(200));
        Equal(true, hit.Speech?.Contains("Hit.", StringComparison.Ordinal) == true,
            $"a resolved hit is announced; got {hit.Speech}");

        var scored = readout.Observe(
            state,
            Snapshot(160, 120, score: 300, Target(0, 160, 120, underSight: true)),
            Start.AddMilliseconds(250));
        Equal(true, scored.Speech?.Contains("Score 300.", StringComparison.Ordinal) == true,
            $"the displayed score is valid feedback in its own right; got {scored.Speech}");

        // Nothing may be said about what has not been drawn.
        var everything = new List<string>();
        var walkthrough = new SpeedSquareCoasterAimReadout();
        for (var x = 0; x <= 320; x += 20)
        {
            var line = walkthrough.Observe(
                state, Snapshot(160, 120, Target(0, x, 120)), Start.AddMilliseconds(x)).Speech;
            if (line is not null)
            {
                everything.Add(line);
            }
        }

        foreach (var forbidden in new[] { "next", "incoming", "will appear", "bonus", "weak", "will hit" })
        {
            Equal(false, everything.Any(line => line.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"no line may promise what has not happened: \"{forbidden}\"");
        }

        // An empty screen is said once and then left alone.
        var gone = readout.Observe(state, Snapshot(160, 120, score: 300), Start.AddMilliseconds(300));
        Equal(true, gone.Speech?.Contains("No targets in sight.", StringComparison.Ordinal) == true,
            $"an empty screen is stated once; got {gone.Speech}");
        Equal(null, readout.Observe(state, Snapshot(160, 120, score: 300), Start.AddMilliseconds(350)).Speech,
            "and not repeated");
    }

    /// <summary>
    /// The flag lives in the object's own slot and FUN_005E99FB only touches it when
    /// the gate accepts. A flag found set once the sight has moved off proves nothing:
    /// the pass that would have cleared it may not have run.
    /// </summary>
    private static void ACachedFlagIsNotANewHit()
    {
        var readout = new SpeedSquareCoasterAimReadout();
        var state = new SpeedSquareCoasterState(true, false, 160, 120, 128);
        readout.Observe(state, Snapshot(160, 120, Target(0, 160, 120)), Start);

        // No shot in progress: the flag cannot have been resolved this frame.
        var idle = readout.Observe(
            state,
            Snapshot(160, 120, Target(0, 160, 120, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(20));
        Equal(0, Count(idle.Speech, "Hit."), "a hit flag with no shot in progress is not a hit");

        var first = readout.Observe(
            state,
            Snapshot(160, 120, firing: true, Target(0, 160, 120, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(50));
        Equal(1, Count(first.Speech, "Hit."), $"the first resolved hit is announced; got {first.Speech}");

        for (var poll = 0; poll < 10; poll++)
        {
            var repeat = readout.Observe(
                state,
                Snapshot(160, 120, firing: true, Target(0, 160, 120, underSight: true, hitFlag: true)),
                Start.AddMilliseconds(60 + poll));
            Equal(0, Count(repeat.Speech, "Hit."), "and is not announced again while it stays set");
        }

        // The sight moves off but the flag stays set, because the gate that would
        // clear it has not run for this object.
        var movedOff = readout.Observe(
            state,
            Snapshot(160, 120, firing: true, Target(0, 240, 120, hitFlag: true)),
            Start.AddMilliseconds(120));
        Equal(0, Count(movedOff.Speech, "Hit."),
            $"a cached flag with the sight elsewhere is not a new hit; got {movedOff.Speech}");

        // Nor is one whose gate is closed.
        var gated = readout.Observe(
            state,
            Snapshot(
                160, 120, firing: true,
                Target(0, 160, 120, underSight: true, hitFlag: true, accepted: false)),
            Start.AddMilliseconds(160));
        Equal(0, Count(gated.Speech, "Hit."), "and neither is one behind a closed gate");
    }

    /// <summary>
    /// A slot is handed out again with its in-use word still reading 1, so the slot
    /// cannot tell one occupant from the next. The object's own node, model and type
    /// can.
    /// </summary>
    private static void AReusedSlotIsADifferentTarget()
    {
        var readout = new SpeedSquareCoasterAimReadout();
        var state = new SpeedSquareCoasterState(true, false, 160, 120, 128);
        var firstObject = Identity(node: 0x00500000, slot: 4, type: 2);
        var secondObject = Identity(node: 0x00500038, slot: 5, type: 7);
        var thirdObject = Identity(node: 0x00500070, slot: 6, type: 2);

        readout.Observe(state, Snapshot(160, 120, Target(0, 160, 120, identity: firstObject)), Start);
        var first = readout.Observe(
            state,
            Snapshot(160, 120, firing: true,
                Target(0, 160, 120, identity: firstObject, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(50));
        Equal(1, Count(first.Speech, "Hit."), "the first object's hit is announced");

        // A second object is hit while the first is still flagged. The number of set
        // flags never changes from one moment to the next in slot terms, so counting
        // them would miss this entirely.
        var second = readout.Observe(
            state,
            Snapshot(160, 120, firing: true,
                Target(0, 160, 120, identity: firstObject, underSight: true, hitFlag: true),
                Target(7, 161, 121, identity: secondObject, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(120));
        Equal(1, Count(second.Speech, "Hit."),
            $"a hit on another object is announced in its own right; got {second.Speech}");

        // Slot 0 is handed to a new object with the in-use word still reading 1.
        var reused = readout.Observe(
            state,
            Snapshot(160, 120, firing: true,
                Target(0, 160, 120, identity: thirdObject, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(200));
        Equal(1, Count(reused.Speech, "Hit."),
            $"a reused slot holds a different object and is reported; got {reused.Speech}");

        // The objects that have gone are pruned, so the set does not grow for the
        // length of a held shot and an object that comes back is not swallowed.
        var returned = readout.Observe(
            state,
            Snapshot(160, 120, firing: true,
                Target(0, 160, 120, identity: thirdObject, underSight: true, hitFlag: true),
                Target(7, 161, 121, identity: secondObject, underSight: true, hitFlag: true)),
            Start.AddMilliseconds(260));
        Equal(1, Count(returned.Speech, "Hit."),
            $"an object that disappeared and came back is reported again; got {returned.Speech}");

        // Letting go of the shot clears the run.
        readout.Observe(state, Snapshot(160, 120, Target(0, 160, 120, identity: thirdObject)), Start.AddSeconds(1));
        var nextShot = readout.Observe(
            state,
            Snapshot(160, 120, firing: true,
                Target(0, 160, 120, identity: thirdObject, underSight: true, hitFlag: true)),
            Start.AddSeconds(2));
        Equal(1, Count(nextShot.Speech, "Hit."), "the next shot's hit is announced in its own right");
    }

    private static void NothingIsReportedWhileTheRideIsSuspended()
    {
        var readout = new SpeedSquareCoasterAimReadout();
        var suspended = new SpeedSquareCoasterState(true, true, 160, 120, 128);
        Equal(true,
            readout.Observe(suspended, Snapshot(160, 120, score: 900, firing: true, Target(0, 40, 40)), Start).IsEmpty,
            "the native routine ignores input while suspended, so the sight cannot be moving");

        var inactive = new SpeedSquareCoasterState(false, false, 160, 120, 128);
        Equal(true,
            readout.Observe(inactive, Snapshot(160, 120, score: 900, firing: true, Target(0, 40, 40)), Start).IsEmpty,
            "and nothing is said outside the ride at all");
    }

    /// <summary>
    /// Two conventions meet in the cue and are easy to cross. Steam Audio's listener
    /// looks down negative Z, which the existing highway cue tests already assert, and
    /// takes a unit direction. The legacy stick vector is separate: positive StickY is
    /// down and rear, which is what the spatial emphasis helper consumes.
    /// </summary>
    private static void TheDirectionalCueUsesTheEstablishedAudioConventions()
    {
        var readout = new SpeedSquareCoasterAimReadout();
        var state = new SpeedSquareCoasterState(true, false, 160, 120, 128);

        NavigationBeaconCue Cue(int x, int y, int millisecond)
        {
            var observed = readout.Observe(
                state, Snapshot(160, 120, Target(0, x, y)), Start.AddMilliseconds(millisecond));
            return observed.Beacon ?? throw new InvalidOperationException("the cue must be produced");
        }

        var centred = Cue(160, 120, 0);
        Equal(true, centred.SteamAudioZ < 0f,
            $"a centred target is ahead of the listener, which is negative Z; got {centred.SteamAudioZ}");
        Equal(true, Math.Abs(centred.SteamAudioX) < 0.001f, "and neither left nor right");
        Equal(true, IsUnit(centred), "and the direction is a unit vector");

        var right = Cue(300, 120, 100);
        Equal(true, right.SteamAudioX > 0f, "a target to the right is to the listener's right");
        Equal(true, right.SteamAudioZ < 0f, "and still ahead");
        Equal(true, IsUnit(right), "normalised");

        var left = Cue(20, 120, 200);
        Equal(true, left.SteamAudioX < 0f, "a target to the left is to the listener's left");
        Equal(true, IsUnit(left), "normalised");

        // Screen up is above the listener, which is positive Y for Steam Audio and
        // negative for the legacy stick.
        var above = Cue(160, 20, 300);
        Equal(true, above.SteamAudioY > 0f,
            $"a target the sight must rise to is above the listener; got {above.SteamAudioY}");
        Equal(true, above.StickY < 0f,
            $"and the stick vector keeps its own down-is-positive sense; got {above.StickY}");
        Equal(true, above.SteamAudioZ < 0f, "an above target is still ahead, never behind");
        Equal(true, IsUnit(above), "normalised");

        var below = Cue(160, 220, 400);
        Equal(true, below.SteamAudioY < 0f, "a target below the sight is below the listener");
        Equal(true, below.StickY > 0f, "and the stick vector says down");
        Equal(true, IsUnit(below), "normalised");
    }

    private static bool IsUnit(NavigationBeaconCue cue)
    {
        var length = MathF.Sqrt(
            (cue.SteamAudioX * cue.SteamAudioX) +
            (cue.SteamAudioY * cue.SteamAudioY) +
            (cue.SteamAudioZ * cue.SteamAudioZ));
        return Math.Abs(length - 1f) < 0.001f;
    }

    private static int Count(string? speech, string needle)
    {
        if (speech is null)
        {
            return 0;
        }

        var count = 0;
        var index = speech.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = speech.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static SpeedSquareCoasterTargetSnapshot Snapshot(
        int cursorX,
        int cursorY,
        params SpeedSquareCoasterTarget[] targets) =>
        new(targets, WasPresented: true, cursorX, cursorY, Score: 0, Firing: false);

    private static SpeedSquareCoasterTargetSnapshot Snapshot(
        int cursorX,
        int cursorY,
        bool firing,
        params SpeedSquareCoasterTarget[] targets) =>
        new(targets, WasPresented: true, cursorX, cursorY, Score: 0, firing);

    private static SpeedSquareCoasterTargetSnapshot Snapshot(
        int cursorX,
        int cursorY,
        int score,
        params SpeedSquareCoasterTarget[] targets) =>
        new(targets, WasPresented: true, cursorX, cursorY, score, Firing: false);

    private static SpeedSquareCoasterTargetSnapshot Snapshot(
        int cursorX,
        int cursorY,
        int score,
        bool firing,
        params SpeedSquareCoasterTarget[] targets) =>
        new(targets, WasPresented: true, cursorX, cursorY, score, firing);

    private static SpeedSquareCoasterTargetIdentity Identity(uint node, int slot, int type) =>
        new(node, slot, type, Model: 0x00600000 + (uint)slot, type,
            SpeedSquareCoasterTargetReader.AddressRootParentNode);

    private static SpeedSquareCoasterTarget Target(
        int index,
        int centreX,
        int centreY,
        SpeedSquareCoasterTargetIdentity? identity = null,
        bool underSight = false,
        bool hitFlag = false,
        bool accepted = true) =>
        new(index,
            identity ?? Identity(0x00500000 + ((uint)index * 0x38), index, 1),
            centreX, centreY, 40, 30, underSight, hitFlag, accepted);

    /// <summary>
    /// The native shapes this reader walks, laid out at the addresses it reads: the
    /// object array, each object's node with its packed matrix, the model that node
    /// points at, and the triangles that model owns.
    /// </summary>
    private sealed class FakeCoasterMemory : ILegacyAddressSpace
    {
        private const uint NodeBase = 0x00500000;
        private const int NodeStride = 0x38;
        private const uint ParentBase = 0x00560000;
        private const uint ModelBase = 0x00600000;
        private const int ModelStride = 0x40;
        private const uint TriangleBase = 0x00700000;
        private const int TriangleAreaStride = 0x400;

        // Everything lives in one address space, the way the reader sees it. Nothing
        // is intercepted by address and length, because the reader takes several of
        // these as blocks and a fake that only answered single fields would pass
        // tests the real memory would fail.
        private readonly Dictionary<uint, byte> bytes = [];

        private uint changeAfter;
        private Action? change;

        public FakeCoasterMemory()
        {
            Module = SpeedSquareCoasterStateReader.CoasterModule;
            PresentationFlag = 1;
            ScaleX = 1;
            ScaleY = 1;
            LightNear = 10000;
            LightFar = 20000;
            HalfSpaceConstantA = 1;
            HalfSpaceConstantB = 1;
            HalfSpaceCoefficientsA = (0, 0, 1);
            HalfSpaceCoefficientsB = (0, 0, 1);
            HalfSpaceReferenceA = 1;
            HalfSpaceReferenceB = 1;
            SetCameraRotation([4096, 0, 0, 0, 4096, 0, 0, 0, 4096]);
            SetSightCursor(160, 120);

            for (var index = 0; index < SpeedSquareCoasterTargetReader.ObjectCount; index++)
            {
                WriteInt32(
                    Object(index) + SpeedSquareCoasterTargetReader.ObjectNodePointerOffset,
                    (int)Node(index));
                WriteInt32(Node(index) + SpeedSquareCoasterTargetReader.NodeModelPointerOffset,
                    (int)Model(index));
                WriteInt32(Node(index) + SpeedSquareCoasterTargetReader.NodeParentOffset,
                    unchecked((int)SpeedSquareCoasterTargetReader.AddressRootParentNode));
                WriteInt16(Node(index) + SpeedSquareCoasterTargetReader.NodeTypeOffset, (short)(index + 1));
                WriteInt16(Node(index) + SpeedSquareCoasterTargetReader.NodeSlotOffset, (short)index);
                WriteInt32(Object(index) + SpeedSquareCoasterTargetReader.ObjectTypeOffset, index + 1);
                WriteIdentityRotation(Node(index) + SpeedSquareCoasterTargetReader.NodeMatrixOffset);
                WriteIdentityRotation(Parent(index) + SpeedSquareCoasterTargetReader.NodeMatrixOffset);
                WriteInt32(Model(index) + SpeedSquareCoasterTargetReader.ModelTrianglePointerOffset,
                    (int)Triangles(index));
            }
        }

        public byte Module
        {
            get => bytes.GetValueOrDefault((uint)FieldPositionReader.AddressCurrentModule);
            set => bytes[(uint)FieldPositionReader.AddressCurrentModule] = value;
        }

        public int PresentationFlag
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressPresentationFlag);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressPresentationFlag, value);
        }

        public int ScaleX
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressSightScaleX);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressSightScaleX, value);
        }

        public int ScaleY
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressSightScaleY);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressSightScaleY, value);
        }

        public int OffsetX
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressSightOffsetX);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressSightOffsetX, value);
        }

        public int OffsetY
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressSightOffsetY);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressSightOffsetY, value);
        }

        public int Score
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressScore);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressScore, value);
        }

        public int Firing
        {
            get => bytes.GetValueOrDefault((uint)SpeedSquareCoasterTargetReader.AddressFiring);
            set => bytes[(uint)SpeedSquareCoasterTargetReader.AddressFiring] = (byte)value;
        }

        public int LightNear
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressLightNear);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressLightNear, value);
        }

        public int LightFar
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressLightFar);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressLightFar, value);
        }

        public int UnreadableObjectIndex { get; set; } = -1;

        public int HalfSpaceConstantA
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceConstantA);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceConstantA, value);
        }

        public int HalfSpaceConstantB
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceConstantB);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceConstantB, value);
        }

        public (int X, int Y, int Z) HalfSpaceCoefficientsA
        {
            set => WriteTriple((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceCoefficientsA, value);
        }

        public (int X, int Y, int Z) HalfSpaceCoefficientsB
        {
            set => WriteTriple((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceCoefficientsB, value);
        }

        public int HalfSpaceReferenceA
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceReferenceA);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceReferenceA, value);
        }

        public int HalfSpaceReferenceB
        {
            get => ReadInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceReferenceB);
            set => WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressHalfSpaceReferenceB, value);
        }

        public uint ParentNodeAddress => Parent(0);

        /// <summary>An active, lit, front-facing target with one triangle and a box.</summary>
        public void PlaceTarget(int index, int left, int top, int right, int bottom)
        {
            SetActive(index, 1);
            SetNodeTranslation(index, 0, 0, 100);
            SetTriangleCount(index, 1);
            SetGeometryDepth(index, 0);
            SetBox(index, left, top, right, bottom);
        }

        public void PlaceTargetInScreenSpace(int index, int left, int top, int right, int bottom)
        {
            SetActive(index, 1);
            SetNodeTranslation(index, 0, 0, 100);
            SetTriangleCount(index, 1);
            SetGeometryDepth(index, 0);
            SetScreenBox(index, left, top, right, bottom);
        }

        public void SetActive(int index, short value) =>
            WriteInt16(Object(index) + SpeedSquareCoasterTargetReader.ObjectActiveOffset, value);

        public void SetHit(int index, int value) =>
            WriteInt32(Object(index) + SpeedSquareCoasterTargetReader.ObjectHitFlagOffset, value);

        public void SetNodePointer(int index, uint node) =>
            WriteInt32(
                Object(index) + SpeedSquareCoasterTargetReader.ObjectNodePointerOffset,
                unchecked((int)node));

        public uint NodeAddress(int index) => Node(index);

        public void SetParent(int index, uint parent) =>
            WriteInt32(Node(index) + SpeedSquareCoasterTargetReader.NodeParentOffset, unchecked((int)parent));

        public void SetNodeTranslation(int index, int x, int y, int z) =>
            WriteTranslation(Node(index) + SpeedSquareCoasterTargetReader.NodeMatrixOffset, x, y, z);

        public void SetParentTranslation(int index, int x, int y, int z) =>
            WriteTranslation(Parent(index) + SpeedSquareCoasterTargetReader.NodeMatrixOffset, x, y, z);

        public void SetSightCursor(int x, int y)
        {
            WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressSightCursorX, x);
            WriteInt32((uint)SpeedSquareCoasterTargetReader.AddressSightCursorY, y);
        }

        /// <summary>The shared camera rotation, nine shorts at 4096 for one.</summary>
        public void SetCameraRotation(short[] rotation)
        {
            for (var element = 0; element < rotation.Length; element++)
            {
                WriteInt16(
                    (uint)SpeedSquareCoasterTargetReader.AddressCameraRotation + (uint)(element * 2),
                    rotation[element]);
            }
        }

        public void SetTriangleCount(int index, short count) =>
            WriteInt16(Model(index) + SpeedSquareCoasterTargetReader.ModelTriangleCountOffset, count);

        /// <summary>
        /// Puts all three of the first triangle's vertices at one depth in the model's
        /// own space, which the node translation then carries into camera space.
        /// </summary>
        public void SetGeometryDepth(int index, int z)
        {
            for (var vertex = 0; vertex < SpeedSquareCoasterTargetReader.TriangleVertexCount; vertex++)
            {
                var at = Triangles(index) +
                         (uint)(vertex * SpeedSquareCoasterTargetReader.TriangleVertexStride);
                WriteInt16(at, (short)(vertex == 0 ? -10 : 10));
                WriteInt16(at + 2, (short)(vertex == 1 ? -10 : 10));
                WriteInt16(at + 4, (short)z);
            }
        }

        public void SetBox(int index, int left, int top, int right, int bottom) =>
            SetScreenBox(
                index,
                (left * ScaleX) + OffsetX,
                (top * ScaleY) + OffsetY,
                (right * ScaleX) + OffsetX,
                (bottom * ScaleY) + OffsetY);

        public void SetScreenBox(int index, int left, int top, int right, int bottom)
        {
            Span<(int X, int Y)> points =
            [
                (left, top), (right, top), (left, bottom),
                (right, bottom), (left, top), (right, bottom)
            ];
            for (var point = 0; point < SpeedSquareCoasterTargetReader.UsableBoundPoints; point++)
            {
                WritePoint(index, point, points[point].X, points[point].Y);
            }
        }

        public void SetReservedBoundPoints(int index, int x, int y)
        {
            WritePoint(index, 6, x, y);
            WritePoint(index, 7, x, y);
        }

        /// <summary>
        /// Moves something the moment a particular address has been read, which is what
        /// the running game does to a reader part way through its own pass.
        /// </summary>
        public void ChangeAfterReading(uint address, Action mutation)
        {
            changeAfter = address;
            change = mutation;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            destination.Clear();
            if (UnreadableObjectIndex >= 0)
            {
                var start = Object(UnreadableObjectIndex);
                if (virtualAddress >= start &&
                    virtualAddress < start + SpeedSquareCoasterTargetReader.ObjectStride)
                {
                    return false;
                }
            }

            for (var offset = 0; offset < destination.Length; offset++)
            {
                destination[offset] = bytes.GetValueOrDefault(virtualAddress + (uint)offset);
            }

            Trigger(virtualAddress, destination.Length);
            return true;
        }

        private void Trigger(uint address, int length)
        {
            if (changeAfter == 0 || address > changeAfter || address + (uint)length <= changeAfter)
            {
                return;
            }

            changeAfter = 0;
            var mutation = change;
            change = null;
            mutation?.Invoke();
        }

        private void WritePoint(int index, int point, int x, int y)
        {
            var packed = ((uint)(ushort)(short)y << 16) | (ushort)(short)x;
            WriteInt32(
                Object(index) + SpeedSquareCoasterTargetReader.ObjectProjectedBoundsOffset +
                (uint)(point * sizeof(int)),
                unchecked((int)packed));
        }

        /// <summary>Nine shorts with 4096 down the diagonal, as FUN_006611FB expects.</summary>
        private void WriteIdentityRotation(uint matrix)
        {
            for (var element = 0; element < 9; element++)
            {
                WriteInt16(matrix + (uint)(element * 2), (short)(element % 4 == 0 ? 4096 : 0));
            }
        }

        /// <summary>The three ints packed straight after the nine shorts, with no padding.</summary>
        private void WriteTranslation(uint matrix, int x, int y, int z)
        {
            var at = matrix + SpeedSquareCoasterTargetReader.PackedMatrixTranslationOffset;
            WriteInt32(at, x);
            WriteInt32(at + 4, y);
            WriteInt32(at + 8, z);
        }

        private void WriteTriple(uint address, (int X, int Y, int Z) triple)
        {
            WriteInt32(address, triple.X);
            WriteInt32(address + 4, triple.Y);
            WriteInt32(address + 8, triple.Z);
        }

        private int ReadInt32(uint address)
        {
            Span<byte> buffer = stackalloc byte[4];
            for (var offset = 0; offset < buffer.Length; offset++)
            {
                buffer[offset] = bytes.GetValueOrDefault(address + (uint)offset);
            }

            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        private void WriteInt16(uint address, short value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
            for (var offset = 0; offset < buffer.Length; offset++)
            {
                bytes[address + (uint)offset] = buffer[offset];
            }
        }

        private void WriteInt32(uint address, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            for (var offset = 0; offset < buffer.Length; offset++)
            {
                bytes[address + (uint)offset] = buffer[offset];
            }
        }

        private static uint Object(int index) =>
            (uint)(SpeedSquareCoasterTargetReader.AddressObjectArray +
                   (index * SpeedSquareCoasterTargetReader.ObjectStride));

        private static uint Node(int index) => NodeBase + (uint)(index * NodeStride);

        private static uint Parent(int index) => ParentBase + (uint)(index * NodeStride);

        private static uint Model(int index) => ModelBase + (uint)(index * ModelStride);

        private static uint Triangles(int index) => TriangleBase + (uint)(index * TriangleAreaStride);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Speed Square coaster targets - {message}: expected {expected}, actual {actual}.");
    }
}
