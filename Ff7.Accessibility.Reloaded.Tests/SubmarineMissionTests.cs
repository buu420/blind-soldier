using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The submarine mission's reader and readout. Every case here is about the difference
/// between what the game draws and what it merely knows: a record inside sonar range is
/// not a square on the screen, a mission that has stopped is not still tracking enemies,
/// and a reload countdown the game keeps to itself is not something to count down aloud.
/// </summary>
internal static class SubmarineMissionTests
{
    internal static void Run()
    {
        AnIdleModuleIsNotAMission();
        AnUnreadableIdentityIsNotAnEndedMission();
        ATornIdentityIsRefusedRatherThanReported();
        ASingleTearIsRetriedIntoOneCoherentReading();
        AnUninitialisedRunIsNotAMission();
        TheInstrumentsAreTheOnesTheMissionDraws();
        NegativeAndImpossibleCountersDoNotProduceNonsense();
        TorpedoLampsCountOnlyLoadedSlots();
        OnlyDrawnMarkersBecomeTargets();
        TargetsCarryTheirHullAndPosition();
        EveryOneOfTheTwelveSlotsIsRead();
        ASinkingHullIsATargetOnlyWhereItIsDrawn();
        AHiddenLeaderIsNotRevealed();
        ThePlayersPoseThrottleAndOverviewComeFromTheView();
        TheViewportOriginIsHonoured();
        ARecordBehindTheCameraIsNotOnScreen();
        AnAlternateCameraMovesWhereTargetsAppear();
        AnUnreadableViewContributesNoTargetsAndSaysSo();
        ATornViewKeepsTheInstrumentsAndDropsThePlacement();
        AnUninitialisedCameraOrViewportIsNotAnEmptySea();
        TargetsAreTrackedWhileTheMissionIsBeingPlayed();
        AnUnreadableViewDoesNotAnnounceALostLock();
        PausedAndFinishedMissionsDropTheirTargets();
        TheReadoutAnnouncesResultsPausesAndQuitChoices();
        TheLockCueIsProtectedAndOrdinaryUpdatesAreNot();
        LeavingTheMissionClearsEverything();
        DamageAndWarningLampsAreSpokenWhenTheyChange();
        TheReadoutNamesTheHullsItCanSee();
        APursuitCanOwnTheTargetLines();
        TheControlsAreNamedByActionNotByKey();
        NothingUndrawnIsEverSpoken();

        // The pursuit tracker reads these snapshots, so its cases run wherever these do.
        SubmarinePursuitTrackerTests.Run();
    }

    // -- reader -------------------------------------------------------------------

    private static void AnIdleModuleIsNotAMission()
    {
        var memory = new Native();
        memory.Module = 1;
        var reader = new SubmarineMissionStateReader(memory);
        Equal(true, reader.TryRead(out var snapshot), "a different module reads cleanly");
        Equal(false, snapshot.IsActive, "the field module is not the submarine mission");
    }

    private static void AnUnreadableIdentityIsNotAnEndedMission()
    {
        var memory = new Native();
        memory.Unreadable.Add(SubmarineMissionStateReader.AddressCurrentModule);
        var reader = new SubmarineMissionStateReader(memory);
        Equal(false, reader.TryRead(out _), "a failed read is a failure, not an idle module");
    }

    /// <summary>
    /// A word that disagrees with itself across every capture is never reported, however
    /// many captures are tried, and the diagnostic names which word it was. E73F18 is in
    /// the list because 77DF72 clears it as each frame's update starts and 78C9E1 sets it
    /// when the frame is done: a change in it means a frame boundary fell inside the
    /// capture, which is exactly what the double sample exists to catch.
    /// </summary>
    private static void ATornIdentityIsRefusedRatherThanReported()
    {
        foreach (var (address, label) in new[]
                 {
                     (SubmarineMissionStateReader.AddressCurrentModule, "module"),
                     (SubmarineMissionStateReader.AddressResult, "result"),
                     (SubmarineMissionStateReader.AddressSessionFlags, "session flags"),
                     (SubmarineMissionStateReader.AddressInnerCompletion, "frame flag")
                 })
        {
            var memory = Running();
            var reads = 0;
            memory.BeforeRead = read =>
            {
                if (read != address)
                {
                    return;
                }

                // Every second sample of this word lands after the mission has moved on.
                var first = ++reads % 2 == 1;
                if (address == SubmarineMissionStateReader.AddressCurrentModule)
                {
                    memory.Module = first ? SubmarineMissionStateReader.MinigameModule : (byte)3;
                }
                else
                {
                    memory.WriteInt32(address, first ? 0 : 5);
                }
            };
            var reader = new SubmarineMissionStateReader(memory);
            Equal(false, reader.TryRead(out _), $"a torn sample of the {label} is refused");
            Equal(true, reader.LastDiagnostic.Contains("torn identity", StringComparison.Ordinal),
                $"the refusal says why: '{reader.LastDiagnostic}'");
            Equal(true, reader.LastDiagnostic.Contains(label, StringComparison.Ordinal),
                $"and names the {label}: '{reader.LastDiagnostic}'");
            Equal(SubmarineMissionStateReader.MaximumCaptureAttempts, reads / 2,
                $"every bounded attempt sampled the {label} twice");
            Equal(SubmarineMissionStateReader.MaximumCaptureAttempts, reader.TornIdentityCaptures,
                "each torn attempt is counted");
        }
    }

    /// <summary>
    /// One tear is not a broken mission. The whole capture is taken again, and the next
    /// one - which agrees with itself - is the reading, rather than either half of the
    /// torn one.
    /// </summary>
    private static void ASingleTearIsRetriedIntoOneCoherentReading()
    {
        var memory = Running();
        var reads = 0;
        memory.BeforeRead = read =>
        {
            if (read == SubmarineMissionStateReader.AddressSessionFlags && ++reads == 2)
            {
                memory.WriteInt32(SubmarineMissionStateReader.AddressSessionFlags, 1);
            }
        };
        var reader = new SubmarineMissionStateReader(memory);
        Equal(true, reader.TryRead(out var paused), $"a single torn identity is retried: {reader.LastDiagnostic}");
        Equal(true, paused.IsPaused, "the coherent second capture is the one reported");
        Equal(1, reader.TornIdentityCaptures, "the torn capture is counted");
        Equal(true, reader.LastTear?.Contains("session flags", StringComparison.Ordinal) == true,
            $"and the torn word is named: '{reader.LastTear}'");

        var moving = Running();
        moving.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x400);
        var positionReads = 0;
        moving.BeforeRead = read =>
        {
            if (read == SubmarineMissionStateReader.AddressEnemyRecords && ++positionReads == 2)
            {
                moving.WriteInt32(SubmarineMissionStateReader.AddressEnemyRecords, 60 << 12);
            }
        };
        var viewReader = new SubmarineMissionStateReader(moving);
        Equal(true, viewReader.TryRead(out var placed), viewReader.LastDiagnostic);
        Equal(true, placed.CanPlaceTargets, "a view torn once is captured again rather than withheld");
        Equal(60, placed.VisibleTargets.Single().X, "and shows where the target now is");
        Equal(1, viewReader.TornViewCaptures, "the torn view capture is counted");
        Equal(true, viewReader.LastTear?.Contains("enemy 0 position", StringComparison.Ordinal) == true,
            $"and named: '{viewReader.LastTear}'");
        Equal(false, viewReader.LastDiagnostic.Contains("torn", StringComparison.Ordinal),
            $"a recovered reading's diagnostic is the ordinary one: '{viewReader.LastDiagnostic}'");
    }

    private static void AnUninitialisedRunIsNotAMission()
    {
        var memory = Running();
        memory.WriteInt32(SubmarineMissionStateReader.AddressActiveRun, 0);
        var reader = new SubmarineMissionStateReader(memory);
        Equal(true, reader.TryRead(out var snapshot), "an inactive run reads cleanly");
        Equal(false, snapshot.IsActive, "the module alone is not a running mission");
    }

    private static void TheInstrumentsAreTheOnesTheMissionDraws()
    {
        var memory = Running();
        memory.WriteInt32(SubmarineMissionStateReader.AddressRemainingFrames, 90 * 60 + 30);
        memory.WriteInt32(SubmarineMissionStateReader.AddressHealth, 16384 / 2);
        memory.WriteInt32(SubmarineMissionStateReader.AddressDepth, 640 << 12);
        memory.WriteInt32(SubmarineMissionStateReader.AddressSpeedNumerator, 1200);
        memory.WriteInt32(SubmarineMissionStateReader.AddressSpeedDenominator, 12288);
        memory.WriteInt16(SubmarineMissionStateReader.AddressPitch, 0);
        memory.WriteInt16(SubmarineMissionStateReader.AddressYaw, 1024);
        memory.WriteInt32(SubmarineMissionStateReader.AddressWarnings, 0x802);

        var snapshot = Read(memory);
        Equal(true, snapshot.IsActive, "the mission is running");
        Equal(90, snapshot.RemainingSeconds, "the clock is the frame count over sixty");
        Equal(50, snapshot.HealthPercent, "the damage bar is normalised by 16384");
        Equal(640, snapshot.Depth, "the depth number is the word shifted down twelve");
        Equal(9, snapshot.Speed, "speed is numerator times 200 over twice the denominator");
        Equal(true, snapshot.HullContact, "bit 2 is the collision lamp");
        Equal(true, snapshot.TorpedoInTheWater, "bit 0x800 is the torpedo lamp");
        Equal(false, snapshot.EnemyDetected, "an unlit lamp stays unlit");

        var speech = new SubmarineMissionReadout().Describe(snapshot);
        Equal(true, speech.Contains("1:30 left", StringComparison.Ordinal), $"clock in '{speech}'");
        Equal(true, speech.Contains("Hull 50 percent", StringComparison.Ordinal), $"hull in '{speech}'");
        Equal(true, speech.Contains("Depth 640", StringComparison.Ordinal), $"depth in '{speech}'");
        Equal(true, speech.Contains("heading east", StringComparison.Ordinal), $"compass in '{speech}'");
    }

    private static void NegativeAndImpossibleCountersDoNotProduceNonsense()
    {
        var memory = Running();
        // 792216 clamps both of these to zero before drawing them, so a transient
        // negative is a legitimate native value and reads the way the screen reads.
        memory.WriteInt32(SubmarineMissionStateReader.AddressRemainingFrames, -600);
        memory.WriteInt32(SubmarineMissionStateReader.AddressHealth, -1);
        memory.WriteInt32(SubmarineMissionStateReader.AddressDepth, 9999 << 12);
        var snapshot = Read(memory);
        Equal(0, snapshot.RemainingSeconds, "a negative frame count is nothing left, not a wrap");
        Equal(0, snapshot.HealthPercent, "a negative health word clamps to nothing");
        Equal(1024, snapshot.Depth, "the depth reading is bounded to the drawn range");

        // Reverse is a real reading and stays signed.
        var astern = Running();
        astern.WriteInt32(SubmarineMissionStateReader.AddressSpeedNumerator, -12288);
        Equal(-100, Read(astern).Speed, "a negative numerator is reverse, not an error");

        // The denominator is what the native readout divides by. A zero one has not been
        // initialised, and calling that a stopped submarine invents a reading.
        var uninitialised = Running();
        uninitialised.WriteInt32(SubmarineMissionStateReader.AddressSpeedDenominator, 0);
        Equal(
            false,
            new SubmarineMissionStateReader(uninitialised).TryRead(out _),
            "a zero speed denominator is unavailable, not a stopped submarine");
    }

    private static void TorpedoLampsCountOnlyLoadedSlots()
    {
        var memory = Running();
        memory.WriteInt32(SubmarineMissionStateReader.AddressTorpedoSlots + 0, 0x20000);
        memory.WriteInt32(SubmarineMissionStateReader.AddressTorpedoSlots + 4, 0x20000);
        // Reloading: the low word is the countdown the game does not print.
        memory.WriteInt32(SubmarineMissionStateReader.AddressTorpedoSlots + 8, 0x10000 | 0x2C);
        memory.WriteInt32(SubmarineMissionStateReader.AddressTorpedoSlots + 12, 0);
        var snapshot = Read(memory);
        Equal(2, snapshot.ReadyTorpedoes, "only a slot holding exactly 0x20000 will fire");
        Equal(1, snapshot.ReloadingTorpedoes, "a high word of 0x10000 is reloading");

        var speech = new SubmarineMissionReadout().Describe(snapshot);
        Equal(true, speech.Contains("2 torpedoes loaded", StringComparison.Ordinal), $"count in '{speech}'");
        Equal(false, speech.Contains("44", StringComparison.Ordinal), $"no countdown in '{speech}'");
    }

    private static void OnlyDrawnMarkersBecomeTargets()
    {
        var memory = Running();
        // Drawn: active, marked, in front, inside the viewport.
        memory.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x400);
        // In sonar range but carrying no marker: nothing is drawn for it.
        memory.PlaceEnemy(1, x: 40, y: 20, flags: 3 | 0x10, marker: 0);
        // Inactive slot, marker left over from a previous life.
        memory.PlaceEnemy(2, x: 40, y: 20, flags: 0, marker: 0x800);
        // Drawn and locked.
        memory.PlaceEnemy(3, x: -100, y: -60, flags: 3, marker: 0x800);
        // Marked, in front, but well outside the viewport rectangle.
        memory.PlaceEnemy(4, x: 5000, y: 20, flags: 3, marker: 0x200);

        var snapshot = Read(memory);
        Equal(2, snapshot.VisibleTargets.Count, "only the two the game draws inside the view");
        Equal(true, snapshot.VisibleTargets.Any(target => target.Slot == 0), "slot 0 is drawn");
        Equal(true, snapshot.VisibleTargets.Any(target => target.Slot == 3), "slot 3 is drawn");
        Equal(true, snapshot.HasLockedTarget, "marker 0x800 is the lock the torpedoes need");

        var located = snapshot.VisibleTargets.Single(target => target.Slot == 3);
        Equal(true, located.IsLocked, "slot 3 is the locked one");
        var speech = new SubmarineMissionReadout().Describe(snapshot);
        Equal(true, speech.Contains("2 marked enemies", StringComparison.Ordinal), speech);
        Equal(true, speech.Contains("locked", StringComparison.Ordinal), speech);
    }

    private static void TargetsCarryTheirHullAndPosition()
    {
        var memory = Running();
        memory.WriteInt32(SubmarineMissionStateReader.AddressArcadeFlag, 2);
        // Arcade table 2 keeps its leader (model 2) in slot 7; model 3 is the ordinary
        // hull in the leader's red.
        memory.PlaceEnemy(7, x: 40, y: 20, z: 3, flags: 3, marker: 0x400, hull: Hull(2));
        memory.PlaceEnemy(1, x: -40, y: 20, flags: 3, marker: 0x200, hull: Hull(3));
        memory.PlaceEnemy(0, x: 0, y: -20, flags: 3, marker: 0x100, hull: Hull(0));
        memory.PlaceEnemy(2, x: 10, y: 10, flags: 3, marker: 0x400, hull: Hull(1));
        memory.PlaceEnemy(3, x: -10, y: 10, flags: 3, marker: 0x400, hull: 0x12345678);

        var snapshot = Read(memory);
        Equal(true, snapshot.IsArcade, "the arcade flag is read");
        Equal(SubmarineTargetModel.RedLeader, ModelOf(snapshot, 7), "the leader's own hull in slot 7 is the leader");
        Equal(SubmarineTargetModel.Red, ModelOf(snapshot, 1), "model 3 is red on the ordinary hull");
        Equal(SubmarineTargetModel.Yellow, ModelOf(snapshot, 0), "model 0 is the yellow hull");
        Equal(SubmarineTargetModel.Unknown, ModelOf(snapshot, 2), "the player's blue hull is no enemy kind");
        Equal(SubmarineTargetModel.Unknown, ModelOf(snapshot, 3), "an unrecognised pointer names nothing");

        var leader = snapshot.VisibleTargets.Single(target => target.Slot == 7);
        Equal((40, 20, 3), (leader.X, leader.Y, leader.Z), "the sighting is where the record is, in map units");
        Equal(false, leader.IsSinking, "a marked hull is not sinking");
    }

    private static void EveryOneOfTheTwelveSlotsIsRead()
    {
        var memory = Running();
        for (var slot = 0; slot < SubmarineMissionStateReader.EnemyRecordCount; slot++)
        {
            memory.PlaceEnemy(slot, x: (slot - 6) * 20, y: 0, flags: 3, marker: 0x400, hull: Hull(slot == 0 ? 2 : 0));
        }

        var snapshot = Read(memory);
        Equal(12, snapshot.VisibleTargets.Count, "all twelve records are drawn and read");
        Equal(true,
            Enumerable.Range(0, 12).SequenceEqual(snapshot.VisibleTargets.Select(target => target.Slot).OrderBy(slot => slot)),
            "one target per slot, the last one included");
    }

    private static void ASinkingHullIsATargetOnlyWhereItIsDrawn()
    {
        var memory = Running();
        // 78D092 turns a dead record into exactly 0x20 with no marker, and 78E3FB still
        // draws its hull on the way down.
        memory.PlaceEnemy(0, x: 30, y: 40, flags: 0x20, marker: 0, hull: Hull(0));
        // On the sea bed the flags are cleared and nothing is drawn.
        memory.PlaceEnemy(1, x: 30, y: 40, flags: 0, marker: 0, hull: Hull(0));
        // Going down, but outside the viewport.
        memory.PlaceEnemy(2, x: 5000, y: 40, flags: 0x20, marker: 0, hull: Hull(3));

        var snapshot = Read(memory);
        Equal(1, snapshot.VisibleTargets.Count, "only the sinking hull inside the view is drawn");
        var sinking = snapshot.VisibleTargets.Single();
        Equal(0, sinking.Slot, "slot 0 is the one going down");
        Equal(true, sinking.IsSinking, "and it is marked sinking");
        Equal(false, sinking.IsLocked, "a sinking hull holds no lock");
        Equal(false, snapshot.HasLockedTarget, "nor does the view");

        var speech = new SubmarineMissionReadout().Describe(snapshot);
        Equal(true, speech.Contains("No marked enemy on screen", StringComparison.Ordinal),
            $"a sinking hull is not a marked enemy: '{speech}'");
        Equal(true, speech.Contains("Sinking: yellow submarine", StringComparison.Ordinal),
            $"it is said as sinking: '{speech}'");
    }

    private static void AHiddenLeaderIsNotRevealed()
    {
        var memory = Running();
        // In sonar range but unmarked, then marked but outside the view.
        memory.PlaceEnemy(0, x: 40, y: 20, flags: 3 | 0x10, marker: 0, hull: Hull(2));
        memory.PlaceEnemy(1, x: 4000, y: 20, flags: 3, marker: 0x400, hull: Hull(2));
        // Dead and on the sea bed, its hull pointer left behind.
        memory.PlaceEnemy(2, x: 40, y: 20, flags: 0, marker: 0x800, hull: Hull(2));

        var snapshot = Read(memory);
        Equal(0, snapshot.VisibleTargets.Count, "nothing the game is not drawing is reported");
        var speech = new SubmarineMissionReadout().Describe(snapshot);
        Equal(false, speech.Contains("Leader", StringComparison.OrdinalIgnoreCase),
            $"an undrawn leader is not named: '{speech}'");
    }

    private static void ThePlayersPoseThrottleAndOverviewComeFromTheView()
    {
        var memory = Running();
        memory.SetPlayer(-1728, 512, -1792);
        memory.WriteInt32(SubmarineMissionStateReader.AddressSpeedIndex, 18);
        memory.WriteInt32(SubmarineMissionStateReader.AddressViewMode, 1);

        var snapshot = Read(memory);
        Equal((-1728, 512, -1792), (snapshot.PlayerX, snapshot.PlayerY, snapshot.PlayerZ),
            "the submarine's own position, in map units");
        Equal(512, snapshot.Depth, "the depth instrument reads the same word");
        Equal(18, snapshot.SpeedIndex, "the throttle step, the held boost included");
        Equal(true, snapshot.IsOverview, "9873A8 is PageDown's overview");
    }

    private static void TheViewportOriginIsHonoured()
    {
        var memory = Running();
        memory.SetViewport(originX: 200, originY: 100, width: 320, height: 240);
        // The flat fixture puts a record at its own coordinates plus the centre of a
        // 320 by 240 view, so this one is drawn at 420: inside a rectangle that starts
        // at 200 and outside one that started at 0 with the same width, and in the right
        // third of it.
        memory.PlaceEnemy(0, x: 260, y: 0, flags: 3, marker: 0x400);
        memory.PlaceEnemy(1, x: -400, y: 0, flags: 3, marker: 0x400);
        var snapshot = Read(memory);
        Equal(1, snapshot.VisibleTargets.Count, "the rectangle that is drawn into is the one that clips");
        Equal(200, snapshot.ViewportOriginX, "the origin comes from the context, not from zero");
        Equal(0, snapshot.VisibleTargets[0].Slot, "the record inside the rectangle is the one kept");

        var described = new SubmarineMissionReadout().Describe(snapshot);
        Equal(true, described.Contains("right", StringComparison.Ordinal),
            $"a marker past two thirds of the width is on the right: '{described}'");
    }

    private static void ARecordBehindTheCameraIsNotOnScreen()
    {
        var memory = Running();
        memory.UsePerspectiveProjection();
        memory.PlaceEnemy(0, x: 10, y: 10, z: 40, flags: 3, marker: 0x400);
        memory.PlaceEnemy(1, x: 10, y: 10, z: -40, flags: 3, marker: 0x400);
        var snapshot = Read(memory);
        Equal(1, snapshot.VisibleTargets.Count, "a record with a non-positive W is not drawn");
        Equal(0, snapshot.VisibleTargets[0].Slot, "the one in front is the one kept");
    }

    private static void AnAlternateCameraMovesWhereTargetsAppear()
    {
        var straight = Running();
        straight.PlaceEnemy(0, x: 100, y: 0, flags: 3, marker: 0x400);
        var straightSnapshot = Read(straight);

        var turned = Running();
        // A quarter turn about the vertical: what was to the right is now ahead.
        turned.SetCameraRotation([0, 0, -4096, 0, 4096, 0, 4096, 0, 0]);
        turned.PlaceEnemy(0, x: 100, y: 0, flags: 3, marker: 0x400);
        var turnedSnapshot = Read(turned);

        Equal(1, straightSnapshot.VisibleTargets.Count, "the straight camera draws it");
        Equal(1, turnedSnapshot.VisibleTargets.Count, "the turned camera still draws it");
        Equal(true,
            straightSnapshot.VisibleTargets[0].ScreenX != turnedSnapshot.VisibleTargets[0].ScreenX,
            "turning the camera has to move where the marker is drawn");
    }

    private static void AnUnreadableViewContributesNoTargetsAndSaysSo()
    {
        var memory = Running();
        memory.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x800);
        memory.WriteInt32(SubmarineMissionStateReader.AddressProjectionContextPointer, 0);
        var snapshot = Read(memory);
        Equal(true, snapshot.IsActive, "the instruments are still readable");
        Equal(false, snapshot.CanPlaceTargets, "without a view nothing can be placed on it");
        Equal(0, snapshot.VisibleTargets.Count, "and no marker is invented");

        var speech = new SubmarineMissionReadout().Describe(snapshot);
        Equal(true, speech.Contains("cannot be read", StringComparison.Ordinal),
            $"an unreadable view has to be said, not passed off as an empty sea: '{speech}'");
    }

    /// <summary>
    /// A stable module number is not evidence that the camera, the renderer context, the
    /// submarine and the enemy records came from one frame. Each of those is sampled twice
    /// per capture; when they disagree in every bounded capture, the markers are withheld,
    /// the instruments - which were read coherently - are still reported, and the
    /// diagnostic names what would not hold still.
    /// </summary>
    private static void ATornViewKeepsTheInstrumentsAndDropsThePlacement()
    {
        (uint Address, int First, int Second, string What, string Label)[] tears =
        [
            (SubmarineMissionStateReader.AddressCamera, 4096, 0, "the camera turning", "camera rotation"),
            (SubmarineMissionStateReader.AddressProjectionContextPointer,
                unchecked((int)Native.Context), unchecked((int)Native.Context) + 0x10000,
                "the renderer context moving", "renderer context"),
            (SubmarineMissionStateReader.AddressEnemyRecords, 0, 900 << 12, "a target moving", "enemy 0 position"),
            (SubmarineMissionStateReader.AddressEnemyRecords + 0x38, 0x800, 0, "a marker changing", "enemy 0 marker"),
            (SubmarineMissionStateReader.AddressEnemyRecords + 0x54, unchecked((int)Hull(2)), unchecked((int)Hull(3)),
                "a hull pointer changing", "enemy 0 hull"),
            (SubmarineMissionStateReader.AddressPlayerX, 0, 50 << 12, "the submarine moving", "player position"),
            (SubmarineMissionStateReader.AddressSpeedIndex, 17, 16, "the throttle stepping", "throttle"),
            (SubmarineMissionStateReader.AddressViewMode, 0, 1, "the overview toggling", "view mode")
        ];

        foreach (var tear in tears)
        {
            var memory = Running();
            memory.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x800, hull: Hull(2));
            memory.WriteInt32(tear.Address, tear.First);
            var reads = 0;
            memory.BeforeRead = address =>
            {
                if (address == tear.Address)
                {
                    memory.WriteInt32(tear.Address, ++reads % 2 == 1 ? tear.First : tear.Second);
                }
            };

            var reader = new SubmarineMissionStateReader(memory);
            Equal(true, reader.TryRead(out var snapshot), $"{tear.What} still reads the instruments");
            Equal(false, snapshot.CanPlaceTargets, $"{tear.What} withholds the markers");
            Equal(0, snapshot.VisibleTargets.Count, $"{tear.What} places nothing");
            Equal(false, snapshot.HasLockedTarget, $"{tear.What} claims no lock");
            Equal(100, snapshot.HealthPercent, $"{tear.What} leaves the instruments alone");
            Equal(true, reader.LastDiagnostic.Contains("torn view", StringComparison.Ordinal),
                $"{tear.What} is diagnosed as a torn view: '{reader.LastDiagnostic}'");
            Equal(true, reader.LastDiagnostic.Contains(tear.Label, StringComparison.Ordinal),
                $"{tear.What} names the {tear.Label}: '{reader.LastDiagnostic}'");
            Equal(SubmarineMissionStateReader.MaximumCaptureAttempts, reader.TornViewCaptures,
                $"{tear.What} was captured the bounded number of times");
        }
    }

    private static void AnUninitialisedCameraOrViewportIsNotAnEmptySea()
    {
        var blankCamera = Running();
        blankCamera.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x400);
        blankCamera.SetCameraRotation([0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Equal(false, Read(blankCamera).CanPlaceTargets,
            "an all-zero rotation is not a camera to place anything from");

        // int.MinValue has no positive counterpart, so a bound that asks for its
        // magnitude is an exception rather than a rejection.
        var brokenViewport = Running();
        brokenViewport.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x400);
        brokenViewport.SetViewport(int.MinValue, 0, 320, 240);
        Equal(false, Read(brokenViewport).CanPlaceTargets,
            "an impossible viewport declines rather than throws");
    }

    private static void TargetsAreTrackedWhileTheMissionIsBeingPlayed()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

        var empty = Running();
        _ = readout.Observe(Read(empty), start);

        // A marker appearing is the thing a player most needs told.
        var appeared = Running();
        appeared.PlaceEnemy(0, x: 0, y: 0, flags: 3, marker: 0x400);
        var appearance = readout.Observe(Read(appeared), start.AddSeconds(5)).Speech ?? "";
        Equal(true, appearance.Contains("Enemy", StringComparison.OrdinalIgnoreCase),
            $"a new marker is announced, got '{appearance}'");

        // And so is one crossing into another part of the screen.
        var moved = Running();
        moved.PlaceEnemy(0, x: -140, y: 0, flags: 3, marker: 0x400);
        var movement = readout.Observe(Read(moved), start.AddSeconds(10)).Speech ?? "";
        Equal(true, movement.Contains("left", StringComparison.OrdinalIgnoreCase),
            $"a marker crossing the screen is announced, got '{movement}'");

        // Drifting within the same third is not news.
        var drifted = Running();
        drifted.PlaceEnemy(0, x: -138, y: 0, flags: 3, marker: 0x400);
        Equal(null, readout.Observe(Read(drifted), start.AddSeconds(15)).Speech,
            "a marker that has not changed part of the screen says nothing");

        var gone = readout.Observe(Read(empty), start.AddSeconds(20)).Speech ?? "";
        Equal(true, gone.Contains("No marked enemy", StringComparison.Ordinal),
            $"a marker leaving the screen is announced, got '{gone}'");
    }

    private static void AnUnreadableViewDoesNotAnnounceALostLock()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 9, 8, 3, 30, 0, DateTimeKind.Utc);

        var locked = Running();
        locked.PlaceEnemy(0, x: 0, y: 0, flags: 3, marker: 0x800);
        _ = readout.Observe(Read(locked), start);

        var blind = Running();
        blind.PlaceEnemy(0, x: 0, y: 0, flags: 3, marker: 0x800);
        blind.WriteInt32(SubmarineMissionStateReader.AddressProjectionContextPointer, 0);
        var unreadable = Read(blind);
        Equal(false, unreadable.CanPlaceTargets, "the view really is unreadable");
        Equal(null, readout.Observe(unreadable, start.AddSeconds(5)).Speech,
            "a frame that cannot be read has not lost the lock");

        // And the lock is still the one the mission holds when the view comes back.
        Equal(null, readout.Observe(Read(locked), start.AddSeconds(10)).Speech,
            "the lock survives the gap rather than being announced again");
    }

    private static void PausedAndFinishedMissionsDropTheirTargets()
    {
        foreach (var (flags, result, what) in new[]
                 {
                     (0x1, 0, "a paused mission"),
                     (0x2, 1, "a finished mission")
                 })
        {
            var memory = Running();
            memory.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x800);
            memory.WriteInt32(SubmarineMissionStateReader.AddressSessionFlags, flags);
            memory.WriteInt32(SubmarineMissionStateReader.AddressResult, result);
            var snapshot = Read(memory);
            Equal(0, snapshot.VisibleTargets.Count, $"{what} is not still tracking anything");
            Equal(false, snapshot.HasLockedTarget, $"{what} holds no lock");
        }
    }

    // -- readout ------------------------------------------------------------------

    private static void TheReadoutAnnouncesResultsPausesAndQuitChoices()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        var running = Snapshot();
        Equal(true, (readout.Observe(running, start).Speech ?? "")
                .Contains("Submarine mission.", StringComparison.Ordinal),
            "entering the mission says so once");

        var paused = running with { IsPaused = true };
        Equal("Paused.", readout.Observe(paused, start.AddSeconds(1)).Speech, "the pause is announced");
        Equal(null, readout.Observe(paused, start.AddSeconds(2)).Speech, "and not repeated");

        var prompt = paused with { IsQuitPromptOpen = true, QuitPromptYesSelected = false };
        Equal(true, (readout.Observe(prompt, start.AddSeconds(3)).Speech ?? "")
                .Contains("No selected", StringComparison.Ordinal),
            "the highlighted answer is the moving part of that prompt");
        Equal(true, (readout.Observe(prompt with { QuitPromptYesSelected = true }, start.AddSeconds(4)).Speech ?? "")
                .Contains("Yes selected", StringComparison.Ordinal),
            "moving the highlight is announced");

        var destroyed = running with
        {
            HasResult = true,
            Outcome = SubmarineMissionOutcome.Destroyed
        };
        Equal("The submarine has been destroyed.",
            readout.Observe(destroyed, start.AddSeconds(5)).Speech,
            "the mission's own word decides the result");
        Equal(null, readout.Observe(destroyed, start.AddSeconds(6)).Speech, "and it is said once");
    }

    private static void TheLockCueIsProtectedAndOrdinaryUpdatesAreNot()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        _ = readout.Observe(Snapshot(), start);

        var locked = Snapshot() with
        {
            HasLockedTarget = true,
            VisibleTargets = [new SubmarineVisibleTarget(0, true, 160, 120)]
        };
        var acquired = readout.Observe(locked, start.AddSeconds(1));
        Equal(true, (acquired.Speech ?? "").StartsWith("Target locked,", StringComparison.Ordinal),
            $"the lock is announced with where it is, got '{acquired.Speech}'");
        Equal(true, acquired.PlayLockCue,
            "the lock takes the cue that a held button cannot cut off");

        Equal(default, readout.Observe(locked, start.AddSeconds(2)), "an unchanged lock says nothing");

        var lost = readout.Observe(Snapshot(), start.AddSeconds(3));
        Equal("Lock lost.", lost.Speech, "losing the lock is announced");
        Equal(false, lost.PlayLockCue, "only acquiring it takes the protected cue");

        // The ordinary beat never claims the protected device.
        var later = readout.Observe(Snapshot() with { Depth = 400 }, start.AddSeconds(20));
        Equal(false, later.PlayLockCue, "an instrument refresh is not a protected cue");
    }

    private static void LeavingTheMissionClearsEverything()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        _ = readout.Observe(Snapshot() with { HasLockedTarget = true }, start);
        Equal("Submarine mission over.",
            readout.Observe(SubmarineMissionSnapshot.Inactive, start.AddSeconds(1)).Speech,
            "leaving is announced");
        Equal(default,
            readout.Observe(SubmarineMissionSnapshot.Inactive, start.AddSeconds(2)),
            "and only once");

        // A second run must not inherit the first run's lock: if it did, the player would
        // never be told about the lock they actually acquire.
        var second = readout.Observe(Snapshot(), start.AddSeconds(3));
        Equal(true, (second.Speech ?? "").Contains("Submarine mission.", StringComparison.Ordinal),
            "a second run starts from nothing");
        var relock = readout.Observe(
            Snapshot() with
            {
                HasLockedTarget = true,
                VisibleTargets = [new SubmarineVisibleTarget(0, true, 160, 120)]
            },
            start.AddSeconds(4));
        Equal(true, (relock.Speech ?? "").StartsWith("Target locked,", StringComparison.Ordinal),
            "the new run's lock is announced on its own terms");
        Equal(true, relock.PlayLockCue, "and takes the protected cue again");
    }

    private static void DamageAndWarningLampsAreSpokenWhenTheyChange()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        _ = readout.Observe(Snapshot(), start);

        var hurt = Snapshot() with { HealthPercent = 62 };
        Equal("Hull 62 percent.", readout.Observe(hurt, start.AddSeconds(1)).Speech,
            "taking damage is said at once");
        Equal(null, readout.Observe(hurt, start.AddSeconds(2)).Speech,
            "and a bar that has not moved a step is not said again");

        var warned = hurt with { Warnings = 0x4 | 0x400 };
        var warning = readout.Observe(warned, start.AddSeconds(3)).Speech ?? "";
        Equal(true, warning.Contains("mine nearby", StringComparison.Ordinal), warning);
        Equal(true, warning.Contains("enemy detected", StringComparison.Ordinal), warning);
        Equal(false, warning.Contains("incoming", StringComparison.OrdinalIgnoreCase),
            "the detection lamp is not an attack");
        Equal("Warnings clear.", readout.Observe(hurt, start.AddSeconds(4)).Speech,
            "a lamp going out is a visible change too");
    }

    private static void TheReadoutNamesTheHullsItCanSee()
    {
        var snapshot = Snapshot() with
        {
            VisibleTargets =
            [
                new SubmarineVisibleTarget(0, false, 280, 30) { Model = SubmarineTargetModel.RedLeader },
                new SubmarineVisibleTarget(3, false, 20, 200) { Model = SubmarineTargetModel.Yellow },
                new SubmarineVisibleTarget(5, false, 160, 120) { Model = SubmarineTargetModel.Red, IsSinking = true }
            ]
        };
        var speech = new SubmarineMissionReadout().Describe(snapshot);
        foreach (var expected in new[]
                 {
                     "2 marked enemies",
                     "Red Leader, right and high",
                     "yellow submarine, left and low",
                     "Sinking: red submarine, centre and level"
                 })
        {
            Equal(true, speech.Contains(expected, StringComparison.Ordinal), $"'{expected}' in '{speech}'");
        }

        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        _ = readout.Observe(Snapshot(), start);
        var locked = readout.Observe(
            Snapshot() with
            {
                HasLockedTarget = true,
                VisibleTargets = [new SubmarineVisibleTarget(0, true, 160, 120) { Model = SubmarineTargetModel.RedLeader }]
            },
            start.AddSeconds(1));
        Equal("Target locked, Red Leader, centre and level.", locked.Speech, "the lock names the hull it is on");
    }

    /// <summary>
    /// While a pursuit runs it speaks the lock and where its target is, with the fire
    /// guidance attached. The readout keeps the instruments, and taking the targets back
    /// does not replay a lock that was already spoken.
    /// </summary>
    private static void APursuitCanOwnTheTargetLines()
    {
        var readout = new SubmarineMissionReadout();
        var start = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        _ = readout.Observe(Snapshot(), start);
        readout.PursuitOwnsTargets = true;

        var locked = Snapshot() with
        {
            HasLockedTarget = true,
            VisibleTargets = [new SubmarineVisibleTarget(0, true, 160, 120) { Model = SubmarineTargetModel.RedLeader }]
        };
        var quiet = readout.Observe(locked, start.AddSeconds(1));
        Equal(null, quiet.Speech, "the pursuit speaks the lock while it runs");
        Equal(false, quiet.PlayLockCue, "and plays the cue itself");
        Equal("Hull 62 percent.", readout.Observe(locked with { HealthPercent = 62 }, start.AddSeconds(2)).Speech,
            "the instruments are still the readout's");

        readout.PursuitOwnsTargets = false;
        Equal(default, readout.Observe(locked with { HealthPercent = 62 }, start.AddSeconds(3)),
            "handing the targets back does not replay a lock already spoken");
    }

    private static void TheControlsAreNamedByActionNotByKey()
    {
        var controls = SubmarineMissionReadout.DescribeControls();
        foreach (var action in new[] { "Switch", "Menu", "Cancel", "PageUp", "PageDown", "Target", "Camera", "Start" })
        {
            Equal(true, controls.Contains(action, StringComparison.Ordinal),
                $"the mission's controls name the action {action}");
        }

        // 798580: Up takes 4 from 98734C (nose down), Down adds 4 (nose up).
        Equal(true, controls.Contains("Up dips the nose to dive", StringComparison.Ordinal) &&
                    controls.Contains("Down raises it to climb", StringComparison.Ordinal),
            $"the pitch directions are said the way the mission applies them: '{controls}'");

        foreach (var key in new[] { "Enter", "Space", "Ctrl", "F1", "keyboard", "arrow key" })
        {
            Equal(false, controls.Contains(key, StringComparison.OrdinalIgnoreCase),
                $"a remapped control must not be described as the key {key}");
        }
    }

    private static void NothingUndrawnIsEverSpoken()
    {
        var memory = Running();
        memory.PlaceEnemy(0, x: 40, y: 20, flags: 3, marker: 0x800);
        // Health, AI heading and distance all sit in the record and none of them is on
        // the screen. Writing them proves the reader never picks them up.
        memory.WriteInt32(SubmarineMissionStateReader.AddressEnemyRecords + 0x10, 7777);
        memory.WriteInt32(SubmarineMissionStateReader.AddressEnemyRecords + 0x40, 8888);
        memory.WriteInt32(SubmarineMissionStateReader.AddressEnemyRecords + 0x58, 9999);
        memory.WriteInt32(SubmarineMissionStateReader.AddressTorpedoSlots + 8, 0x10000 | 1234);

        var speech = new SubmarineMissionReadout().Describe(Read(memory));
        foreach (var forbidden in new[] { "7777", "8888", "9999", "1234" })
        {
            Equal(false, speech.Contains(forbidden, StringComparison.Ordinal),
                $"'{speech}' must not carry the undrawn value {forbidden}");
        }
    }

    // -- fixtures -----------------------------------------------------------------

    private static SubmarineMissionSnapshot Read(Native memory)
    {
        var reader = new SubmarineMissionStateReader(memory);
        Equal(true, reader.TryRead(out var snapshot), $"the mission reads: {reader.LastDiagnostic}");
        return snapshot;
    }

    /// <summary>The +0x54 pointer 78E882 stores for a model index.</summary>
    private static uint Hull(int model) =>
        SubmarineMissionStateReader.AddressModelTable + (uint)(model * SubmarineMissionStateReader.ModelTableStride);

    private static SubmarineTargetModel ModelOf(SubmarineMissionSnapshot snapshot, int slot) =>
        snapshot.VisibleTargets.Single(target => target.Slot == slot).Model;

    private static SubmarineMissionSnapshot Snapshot() =>
        new(
            IsActive: true,
            IsArcade: false,
            IsPaused: false,
            HasResult: false,
            Outcome: SubmarineMissionOutcome.Active,
            IsQuitPromptOpen: false,
            QuitPromptYesSelected: false,
            RemainingSeconds: 300,
            HealthPercent: 100,
            Depth: 200,
            Speed: 8,
            Pitch: 0,
            Yaw: 0,
            ReadyTorpedoes: 4,
            ReloadingTorpedoes: 0,
            Warnings: 0,
            HasLockedTarget: false,
            CanPlaceTargets: true,
            VisibleTargets: Array.Empty<SubmarineVisibleTarget>(),
            ViewportOriginX: 0,
            ViewportOriginY: 0,
            ViewportWidth: 320,
            ViewportHeight: 240);

    private static Native Running()
    {
        var memory = new Native { Module = SubmarineMissionStateReader.MinigameModule };
        memory.WriteInt32(SubmarineMissionStateReader.AddressActiveRun, 1);
        memory.WriteInt32(SubmarineMissionStateReader.AddressRemainingFrames, 300 * 60);
        memory.WriteInt32(SubmarineMissionStateReader.AddressHealth, 16384);
        memory.WriteInt32(SubmarineMissionStateReader.AddressSpeedDenominator, 12288);
        memory.WriteInt32(SubmarineMissionStateReader.AddressProjectionContextPointer, unchecked((int)Native.Context));
        memory.SetViewport(0, 0, 320, 240);
        memory.SetCameraRotation([4096, 0, 0, 0, 4096, 0, 0, 0, 4096]);
        memory.UseFlatProjection();
        return memory;
    }

    private sealed class Native : ILegacyAddressSpace
    {
        internal const uint Context = 0x04000000;

        private readonly Dictionary<uint, byte> bytes = [];

        internal HashSet<uint> Unreadable { get; } = [];

        internal Action<uint>? BeforeRead { get; set; }

        internal byte Module
        {
            set => bytes[SubmarineMissionStateReader.AddressCurrentModule] = value;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            BeforeRead?.Invoke(virtualAddress);
            for (var offset = 0u; offset < destination.Length; offset++)
            {
                if (Unreadable.Contains(virtualAddress + offset))
                {
                    return false;
                }

                destination[(int)offset] = bytes.GetValueOrDefault(virtualAddress + offset);
            }

            return true;
        }

        internal void WriteInt32(uint address, int value)
        {
            for (var index = 0u; index < 4; index++)
            {
                bytes[address + index] = (byte)(value >> (int)(index * 8));
            }
        }

        internal void WriteInt16(uint address, short value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        private void WriteSingle(uint address, float value) =>
            WriteInt32(address, BitConverter.SingleToInt32Bits(value));

        internal void SetViewport(int originX, int originY, int width, int height)
        {
            WriteInt32(Context + 0x848, originX);
            WriteInt32(Context + 0x84C, originY);
            WriteInt32(Context + 0x850, width);
            WriteInt32(Context + 0x854, height);
        }

        internal void SetCameraRotation(short[] rotation)
        {
            for (var index = 0u; index < 9; index++)
            {
                WriteInt16(
                    SubmarineMissionStateReader.AddressCamera + index * 2,
                    rotation[index]);
            }
        }

        /// <summary>
        /// A projection whose W is one, so a record lands at its own coordinates plus
        /// the middle of the 320 by 240 view. It keeps the placement cases arithmetic
        /// rather than trigonometric.
        ///
        /// <para>Written as contiguous rows, because that is how 67C2C0 stored it and
        /// how 66C6CD reads it back: clip[j] is row j dotted with the view vector, so
        /// the translation sits at the end of each row rather than in the last one.</para>
        /// </summary>
        internal void UseFlatProjection()
        {
            var matrix = new float[16];
            matrix[0] = 1f;   // clipX = viewX ...
            matrix[3] = 160f; // ... + 160
            matrix[5] = 1f;   // clipY = viewY ...
            matrix[7] = 120f; // ... + 120
            matrix[10] = 1f;
            matrix[15] = 1f;  // W = 1
            WriteProjection(matrix);
        }

        /// <summary>A projection whose W is the depth, so anything behind fails.</summary>
        internal void UsePerspectiveProjection()
        {
            var matrix = new float[16];
            matrix[0] = 1f;
            matrix[5] = 1f;
            matrix[10] = 1f;
            matrix[14] = 1f; // W = viewZ
            WriteProjection(matrix);
        }

        private void WriteProjection(float[] matrix)
        {
            for (var index = 0u; index < matrix.Length; index++)
            {
                WriteSingle(Context + 0x8D0 + index * 4, matrix[index]);
            }
        }

        internal void PlaceEnemy(int slot, int x, int y, int flags, int marker, int z = 0, uint hull = 0)
        {
            var record = SubmarineMissionStateReader.AddressEnemyRecords +
                (uint)(slot * SubmarineMissionStateReader.EnemyRecordStride);
            WriteInt32(record + 0x00, x << 12);
            WriteInt32(record + 0x04, y << 12);
            WriteInt32(record + 0x08, z << 12);
            WriteInt32(record + 0x34, flags);
            WriteInt32(record + 0x38, marker);
            WriteInt32(record + 0x54, unchecked((int)hull));
        }

        internal void SetPlayer(int x, int y, int z)
        {
            WriteInt32(SubmarineMissionStateReader.AddressPlayerX, x << 12);
            WriteInt32(SubmarineMissionStateReader.AddressPlayerY, y << 12);
            WriteInt32(SubmarineMissionStateReader.AddressPlayerZ, z << 12);
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
