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
        int unitUnderCursorSlot = -1)
        => new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
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
            CollisionTriangles: Array.Empty<CondorCollisionTriangle>());

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

    private static void TheCursorReadoutSpeaksCoordinates()
    {
        var tracker = new CondorBattleSpeechTracker();
        var units = new[] { Ally(0, 1, 428, 706) };
        tracker.Observe(Snapshot(units, cursorX: 100, cursorY: 100));
        tracker.Observe(Snapshot(units, cursorX: 100, cursorY: 100));

        // Moving onto the unit should say where it is, not which way it lies.
        var lines = tracker.Observe(
            Snapshot(units, cursorX: 428, cursorY: 706, unitUnderCursorSlot: 0));
        var spoken = string.Join(" ", lines);
        AssertContains(spoken, "428, 706");
    }

    private static void TheCursorReadoutDoesNotRepeatItself()
    {
        // Sweeping produced the identical sentence three times in one second.
        var tracker = new CondorBattleSpeechTracker();
        var units = new[] { Ally(0, 1, 900, 900) };
        tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 700));
        tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 700));

        var first = string.Join(" ", tracker.Observe(Snapshot(units, cursorX: 400, cursorY: 704)));
        var repeats = new List<string>();
        for (var step = 0; step < 4; step++)
        {
            repeats.Add(string.Join(" ", tracker.Observe(
                Snapshot(units, cursorX: 400, cursorY: 704))));
        }

        foreach (var repeat in repeats)
        {
            if (!string.IsNullOrEmpty(repeat) && repeat == first)
            {
                throw new InvalidOperationException(
                    $"The cursor readout repeated '{repeat}' with nothing having changed.");
            }
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
