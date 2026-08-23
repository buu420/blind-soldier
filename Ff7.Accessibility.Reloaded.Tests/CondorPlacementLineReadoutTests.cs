using Ff7.Accessibility.Reloaded;

/// <summary>
/// The battle line, on a key.
/// </summary>
/// <remarks>
/// Written against the battle Brice lost on 2026-08-22. Every unit he bought sat
/// on the setup wall at 671, including the ones bought after the battle had
/// started and the window below it had already opened, because nothing told him
/// the line had moved.
/// </remarks>
internal static class CondorPlacementLineReadoutTests
{
    internal static void Run()
    {
        SaysTheSetupWallAndHowFarTheCursorIsFromIt();
        SaysHowFarTheLineHasAdvancedOnceTheBattleIsRunning();
        SaysWhenTheCursorIsPastTheLine();
        BanksThePressUntilASnapshotCanAnswerIt();
    }

    private static void BanksThePressUntilASnapshotCanAnswerIt()
    {
        // The battle state is read ten times a second and an ordinary tap lands
        // between two reads, so the press is held until a coherent snapshot
        // exists rather than dropped. This is the same guarantee the status key
        // got after a press was found vanishing into an unreadable snapshot.
        var tracker = new CondorBattleSpeechTracker();
        if (tracker.HasPendingPlacementLineRequest)
        {
            throw new InvalidOperationException("a fresh tracker has no banked press");
        }

        tracker.RequestPlacementLine();
        if (!tracker.HasPendingPlacementLineRequest)
        {
            throw new InvalidOperationException("P is banked before a snapshot exists");
        }

        var answered = tracker.ConsumeRequestedPlacementLine(Setup(cursorY: 671));
        if (answered is null || !answered.Contains("Battle line", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"the banked press must be answered, got \"{answered ?? "null"}\".");
        }

        if (tracker.HasPendingPlacementLineRequest)
        {
            throw new InvalidOperationException("answering the press consumes it");
        }

        // And it is answered once. A press that kept answering would talk over
        // the battle every reading.
        if (tracker.ConsumeRequestedPlacementLine(Setup(cursorY: 671)) is not null)
        {
            throw new InvalidOperationException("a consumed press must not answer again");
        }
    }

    private static void SaysTheSetupWallAndHowFarTheCursorIsFromIt()
    {
        // 671 is the fixed setup boundary in the executable, and 432 is where the
        // cursor sat in his log after he first swept down into the placeable band.
        Equal(
            "Battle line at 671. Cursor at 432, 239 short of it.",
            CondorPlacementLineReadout.Describe(Setup(cursorY: 432)),
            "setup line with the cursor short of it");

        // Standing exactly on the wall is the correct setup position and reads as
        // such rather than as "0 short of it".
        Equal(
            "Battle line at 671. Cursor at 671, on the line.",
            CondorPlacementLineReadout.Describe(Setup(cursorY: 671)),
            "cursor on the setup line");

        // No advance is mentioned during setup: there is nothing to have advanced
        // from, and saying "advanced 0" every press would be noise.
        AssertDoesNotContain(
            CondorPlacementLineReadout.Describe(Setup(cursorY: 671)),
            "advanced");
    }

    private static void SaysHowFarTheLineHasAdvancedOnceTheBattleIsRunning()
    {
        // Combat compares strictly, so the deepest legal row is one above the
        // frontier: a frontier of 800 is a line of 799.
        var running = Combat(cursorY: 671, frontierY: 800);

        Equal(
            "Battle line at 799. Cursor at 671, 128 short of it. Advanced 128 since setup.",
            CondorPlacementLineReadout.Describe(running),
            "the line after the player's units have advanced");
    }

    private static void SaysWhenTheCursorIsPastTheLine()
    {
        // Past the line is ground the game will not take a unit on. Said in those
        // words rather than as a negative number the player has to interpret.
        Equal(
            "Battle line at 671. Cursor at 700, 29 past it.",
            CondorPlacementLineReadout.Describe(Setup(cursorY: 700)),
            "cursor beyond the line");
    }

    private static CondorBattleSnapshot Setup(int cursorY) =>
        Snapshot(cursorY, CondorPlacementRegion.SetupPhase, frontierY: 480);

    private static CondorBattleSnapshot Combat(int cursorY, int frontierY) =>
        Snapshot(cursorY, phase: 0, frontierY);

    private static CondorBattleSnapshot Snapshot(int cursorY, int phase, int frontierY) =>
        new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
            ModalState: 0,
            SettingMenuRow: 0,
            SettingMenuRotation: 0,
            AvailableTypeIds: [],
            Gil: 9436,
            CursorX: 248,
            CursorY: cursorY,
            CursorPlacementLegal: true,
            UnitUnderCursorSlot: -1,
            Units: [],
            AlliedCount: 0,
            EnemyCount: 0,
            Outcome: 0,
            MessageId: -1,
            Phase: phase,
            ReportState: 0,
            DeploymentFrontierY: frontierY,
            EnemyAdvance: 0,
            CollisionTriangles: []);

    private static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected \"{expected}\", got \"{actual}\".");
        }
    }

    private static void AssertDoesNotContain(string actual, string unwanted)
    {
        if (actual.Contains(unwanted, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"did not expect \"{unwanted}\" within \"{actual}\".");
        }
    }
}
