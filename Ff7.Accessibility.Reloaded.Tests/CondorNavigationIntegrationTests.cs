using Ff7.Accessibility.Reloaded;

/// <summary>
/// The navigator as the player meets it: fed by the speech tracker that already
/// knows who died, and answering the same keys the field navigator answers.
/// </summary>
/// <remarks>
/// Uses hand-built snapshots so it needs no licensed game data and can run on the
/// release gate's portable path.
/// </remarks>
internal static class CondorNavigationIntegrationTests
{
    public static void Run()
    {
        ALostAllyIsRecordedWhereItFell();
        AnEnemyGoingDownIsNotARecordedLoss();
        ResetForgetsTheBattlefield();
        TheCursorReadoutSpeaksCoordinates();
        TheCursorReadoutDoesNotRepeatItself();
        SyntheticJumpSuppressesIntermediateDestinationPositions();
        ManualDestinationMovementStillReadsItsSettledPosition();
    }

    private static CondorBattleUnit Ally(int slot, int typeId, int x, int y, int hp = 200) =>
        new(slot, IsEnemy: false, typeId, hp, 200, 30, x, y, IsDying: false, Width: 16, HeightAbove: 16);

    private static CondorBattleUnit Enemy(int slot, int typeId, int x, int y, int hp = 140) =>
        new(slot, IsEnemy: true, typeId, hp, 140, 25, x, y, IsDying: false, Width: 16, HeightAbove: 16);

    private static CondorBattleSnapshot Snapshot(
        IReadOnlyList<CondorBattleUnit> units,
        int cursorX = 400,
        int cursorY = 700,
        int phase = 1,
        int unitUnderCursorSlot = -1,
        int interactionMode = CondorBattleSnapshot.CursorInteractionMode,
        int? destinationX = null,
        int? destinationY = null)
        => new(
            InteractionMode: interactionMode,
            ModalState: 0,
            SettingMenuRow: 0,
            SettingMenuRotation: 0,
            AvailableTypeIds: Array.Empty<int>(),
            Gil: 1000,
            CursorX: cursorX,
            CursorY: cursorY,
            CursorPlacementLegal: false,
            UnitUnderCursorSlot: unitUnderCursorSlot,
            Units: units,
            AlliedCount: units.Count(u => !u.IsEnemy),
            EnemyCount: units.Count(u => u.IsEnemy),
            Outcome: 0,
            MessageId: -1,
            Phase: phase,
            ReportState: 0,
            DeploymentFrontierY: 1008,
            EnemyAdvance: 0,
            CollisionTriangles: Array.Empty<CondorCollisionTriangle>())
        {
            DestinationX = destinationX ?? cursorX,
            DestinationY = destinationY ?? cursorY
        };

    private static void ALostAllyIsRecordedWhereItFell()
    {
        var tracker = new CondorBattleSpeechTracker();
        var attacker = Ally(0, 2, 428, 706);
        tracker.Observe(Snapshot(new[] { attacker }));
        tracker.Observe(Snapshot(new[] { attacker }));
        tracker.Observe(Snapshot(Array.Empty<CondorBattleUnit>()));

        // Allies -> Losses
        tracker.Navigate(CondorNavigationAction.PreviousCategory);
        var spoken = tracker.Navigate(CondorNavigationAction.NextTarget);
        AssertContains(spoken, "Attacker");
        AssertContains(spoken, "428, 706");
    }

    private static void AnEnemyGoingDownIsNotARecordedLoss()
    {
        // "Losses" is the player's own dead. An enemy kill is good news and is
        // already announced; putting it in this list would bury what matters.
        var tracker = new CondorBattleSpeechTracker();
        var beast = Enemy(20, 18, 300, 500);
        tracker.Observe(Snapshot(new[] { beast }));
        tracker.Observe(Snapshot(new[] { beast }));
        tracker.Observe(Snapshot(Array.Empty<CondorBattleUnit>()));

        tracker.Navigate(CondorNavigationAction.PreviousCategory);
        AssertContains(tracker.Navigate(CondorNavigationAction.NextTarget), "none");
    }

    private static void ResetForgetsTheBattlefield()
    {
        var tracker = new CondorBattleSpeechTracker();
        var attacker = Ally(0, 2, 428, 706);
        tracker.Observe(Snapshot(new[] { attacker }));
        tracker.Observe(Snapshot(new[] { attacker }));
        tracker.Observe(Snapshot(Array.Empty<CondorBattleUnit>()));
        tracker.Reset();

        tracker.Navigate(CondorNavigationAction.PreviousCategory);
        AssertContains(tracker.Navigate(CondorNavigationAction.NextTarget), "none");
    }

    /// <summary>
    /// Where the cursor came to rest, and what is standing there.
    /// </summary>
    /// <remarks>
    /// The readout waits for the cursor to settle. Holding a direction moves it
    /// about 24 coordinate units between two readings, so a sentence per
    /// reading buried the player under a backlog of speech that arrived long
    /// after they had let go - which is what "the cursor jumps extra amounts"
    /// turned out to be.
    /// </remarks>
    private static void TheCursorReadoutSpeaksCoordinates()
    {
        var tracker = new CondorBattleSpeechTracker();
        var units = new[] { Ally(0, 1, 428, 706) };
        tracker.Observe(Snapshot(units, cursorX: 100, cursorY: 100));
        tracker.Observe(Snapshot(units, cursorX: 100, cursorY: 100));

        // Caught mid-sweep: the cursor is somewhere it is not staying, so
        // nothing is said about it.
        var travelling = tracker.Observe(
            Snapshot(units, cursorX: 428, cursorY: 706, unitUnderCursorSlot: 0));
        if (travelling.Count != 0)
        {
            throw new InvalidOperationException(
                $"expected silence while the cursor was still moving, got '{string.Join(" ", travelling)}'.");
        }

        // Come to rest on the unit: the coordinates first, then who is there.
        var spoken = string.Join(
            " ",
            tracker.Observe(Snapshot(units, cursorX: 428, cursorY: 706, unitUnderCursorSlot: 0)));
        AssertContains(spoken, "428, 706");
    }

    private static void TheCursorReadoutDoesNotRepeatItself()
    {
        // Sweeping produced the identical sentence three times in one second.
        var tracker = new CondorBattleSpeechTracker();
        var units = new[] { Ally(0, 1, 900, 900) };
        tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 700));
        tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 700));

        // Moved, then held still. The first reading at rest is the one that
        // speaks; every reading after it has nothing new to report.
        tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 704));

        var spoken = new List<string>();
        for (var step = 0; step < 5; step++)
        {
            var line = string.Join(" ", tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 704)));
            if (!string.IsNullOrEmpty(line))
            {
                spoken.Add(line);
            }
        }

        // Exactly one. Asserting only that consecutive lines differ passed even
        // when the readout had gone silent altogether, which is the worse of
        // the two failures.
        if (spoken.Count != 1)
        {
            throw new InvalidOperationException(
                $"expected the resting cursor to be announced once, got {spoken.Count}: " +
                $"[{string.Join(" | ", spoken)}].");
        }

        AssertContains(spoken[0], "400, 704");
    }

    /// <summary>
    /// Steering deliberately lets go for a pass to reset FFVII's repeat ramp.
    /// Two identical samples during that pause are not a destination the player
    /// chose to stop on, so they must not escape as a settled readout.
    /// </summary>
    private static void SyntheticJumpSuppressesIntermediateDestinationPositions()
    {
        var tracker = new CondorBattleSpeechTracker();
        var units = Array.Empty<CondorBattleUnit>();
        tracker.Observe(Snapshot(units));

        Equal(
            "Choose destination. Cursor at 256, 795.",
            Single(tracker.Observe(Snapshot(
                units,
                interactionMode: CondorBattleSnapshot.DestinationInteractionMode,
                destinationX: 256,
                destinationY: 795))),
            "destination mode opens normally");

        var pausedMidJump = Snapshot(
            units,
            interactionMode: CondorBattleSnapshot.DestinationInteractionMode,
            destinationX: 256,
            destinationY: 980);
        Equal(0, tracker.Observe(pausedMidJump, cursorJumpInProgress: true).Count, "first synthetic sample");
        Equal(0, tracker.Observe(pausedMidJump, cursorJumpInProgress: true).Count, "repeat-ramp pause stays silent");

        var finished = Snapshot(
            units,
            interactionMode: CondorBattleSnapshot.DestinationInteractionMode,
            destinationX: 256,
            destinationY: 1008);

        // Both hosts step steering before Observe. On the terminal pass the
        // post-step state is false, so ordinary settling resumes immediately.
        Equal(0, tracker.Observe(finished, cursorJumpInProgress: false).Count, "first final sample");
        Equal(
            "Destination 256, 1008.",
            Single(tracker.Observe(finished, cursorJumpInProgress: false)),
            "settled final position remains available after the stop line");
    }

    private static void ManualDestinationMovementStillReadsItsSettledPosition()
    {
        var tracker = new CondorBattleSpeechTracker();
        var units = Array.Empty<CondorBattleUnit>();
        tracker.Observe(Snapshot(
            units,
            interactionMode: CondorBattleSnapshot.DestinationInteractionMode,
            destinationX: 240,
            destinationY: 500));

        var manuallyMoved = Snapshot(
            units,
            interactionMode: CondorBattleSnapshot.DestinationInteractionMode,
            destinationX: 240,
            destinationY: 640);
        Equal(0, tracker.Observe(manuallyMoved).Count, "manual cursor is still moving");
        Equal(
            "Destination 240, 640.",
            Single(tracker.Observe(manuallyMoved)),
            "manual settled destination readout is unchanged");
    }

    private static string Single(IReadOnlyList<string> lines)
    {
        if (lines.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one spoken line, got {lines.Count}: [{string.Join(" | ", lines)}].");
        }

        return lines[0];
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void AssertContains(string? actual, string expected)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected '{expected}' in the spoken text but it was '{actual ?? "<null>"}'.");
        }
    }
}
