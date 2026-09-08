using System.Text.Json;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

/// <summary>
/// The Fort Condor battlefield facts that are visible without opening a menu:
/// where the build line has moved, and where a new enemy entered.
/// </summary>
internal static class CondorBattleAnnouncementTests
{
    internal static void Run()
    {
        AnnouncesCombatBeginningWithTheSameLineThePKeyReports();
        CalibratesLineMovementAgainstTheBattleBricePlayed();
        AnnouncesEnemiesFromObservedSpawnCoordinates();
        DoesNotInventASideAfterTheSpawnCanNoLongerBeObserved();
        DoesNotBurstArrivalsOnEntryResetOrPhaseChanges();
        OptingOutLeavesThePKeyAndBookkeepingWorking();
        BothAnnouncementsAreEnabledInTheShippedDefaults();
    }

    private static void AnnouncesCombatBeginningWithTheSameLineThePKeyReports()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Snapshot(phase: CondorPlacementRegion.SetupPhase, frontierY: 480));

        // The phase edge is the event, even if the numerical movement were less
        // than the ordinary threshold. A player sees the setup wall disappear.
        var combat = Snapshot(phase: 0, frontierY: 609);
        var automatic = Single(BattleLineEvents(tracker.Observe(combat)));
        Equal("Battle line at 608.", automatic, "combat-start line event");

        tracker.RequestPlacementLine();
        var requested = tracker.ConsumeRequestedPlacementLine(combat);
        if (requested is null)
        {
            throw new InvalidOperationException("P did not answer after the automatic event.");
        }

        var firstSentenceEnd = requested.IndexOf('.', StringComparison.Ordinal);
        Equal(
            automatic,
            requested[..(firstSentenceEnd + 1)],
            "automatic event and P use the same numerical line");
    }

    private static void CalibratesLineMovementAgainstTheBattleBricePlayed()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Snapshot(phase: CondorPlacementRegion.SetupPhase, frontierY: 480));

        // Combat frontiers reconstructed from the 2026-08-29 battle. FFVII's
        // combat comparison is strict, so each spoken limit is frontier - 1.
        var frontiers = new[] { 480, 760, 907, 886, 867, 733, 529, 675, 739, 745, 598, 494 };
        var spoken = new List<string>();
        foreach (var frontier in frontiers)
        {
            spoken.AddRange(BattleLineEvents(tracker.Observe(Snapshot(phase: 0, frontierY: frontier))));
        }

        SequenceEqual(
            new[]
            {
                "Battle line at 479.",
                "Battle line at 759.",
                "Battle line at 906.",
                "Battle line at 732.",
                "Battle line at 528.",
                "Battle line at 674.",
                "Battle line at 738.",
                "Battle line at 597.",
                "Battle line at 493."
            },
            spoken,
            "64-unit line calibration");
    }

    private static void AnnouncesEnemiesFromObservedSpawnCoordinates()
    {
        var tracker = new CondorBattleSpeechTracker();
        var existing = Enemy(slot: 20, typeId: 18, x: 66, y: 989);
        tracker.Observe(Snapshot(phase: 2, frontierY: 800, units: [existing]));
        Equal(
            0,
            ArrivalEvents(tracker.Observe(Snapshot(phase: 2, frontierY: 800, units: [existing]))).Count,
            "existing enemy is only a baseline");

        // Both have already taken several one-unit simulation steps by the time
        // the 10 Hz reader sees them; neither test uses the exact spawn point.
        var wyvern = Enemy(slot: 21, typeId: 17, x: 70, y: 986);
        Equal(
            "Enemy Wyvern entered from the left.",
            Single(ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [existing, wyvern])))),
            "left arrival after movement from spawn");

        var commander = Enemy(slot: 22, typeId: 16, x: 284, y: 987);
        Equal(
            "Enemy Commander entered from the right.",
            Single(ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [existing, wyvern, commander])))),
            "Commander is named but is not a final-wave event");

        // Waves 2 and 6 really do contain ordinary entries after Commander.
        var laterWyvern = Enemy(slot: 23, typeId: 17, x: 280, y: 984);
        Equal(
            "Enemy Wyvern entered from the right.",
            Single(ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [existing, wyvern, commander, laterWyvern])))),
            "ordinary arrivals continue after Commander");

        Equal(
            0,
            ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [existing, wyvern, commander, laterWyvern]))).Count,
            "an unchanged live array does not repeat arrivals");
    }

    private static void DoesNotInventASideAfterTheSpawnCanNoLongerBeObserved()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Snapshot(phase: 2, frontierY: 800));

        // Route 14 begins on the right and later reaches X=80. Calling this
        // position "left" from X alone would expose a confident falsehood.
        var crossed = Enemy(slot: 20, typeId: 18, x: 80, y: 704);
        Equal(
            "Enemy Beast appeared. Entry side unavailable.",
            Single(ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [crossed])))),
            "late observation does not guess a crossed route's spawn side");
    }

    private static void DoesNotBurstArrivalsOnEntryResetOrPhaseChanges()
    {
        var tracker = new CondorBattleSpeechTracker();
        var left = Enemy(slot: 20, typeId: 17, x: 64, y: 992);
        var right = Enemy(slot: 21, typeId: 19, x: 288, y: 992);

        Equal(
            0,
            ArrivalEvents(tracker.Observe(
                Snapshot(phase: CondorPlacementRegion.SetupPhase, frontierY: 480, units: [left]))).Count,
            "battle entry does not announce units already present");

        Equal(
            0,
            ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [left, right]))).Count,
            "phase rebuild does not become an arrival burst");

        var later = Enemy(slot: 22, typeId: 17, x: 286, y: 990);
        Equal(
            "Enemy Wyvern entered from the right.",
            Single(ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [left, right, later])))),
            "steady-phase arrival after rebuild");

        tracker.Reset();
        Equal(
            0,
            ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [left, right, later]))).Count,
            "re-entry after Reset establishes a fresh baseline");
    }

    private static void OptingOutLeavesThePKeyAndBookkeepingWorking()
    {
        var tracker = CreateConfiguredTracker(
            enableBattleLineAnnouncements: false,
            enableEnemyArrivalAnnouncements: false);
        tracker.Observe(Snapshot(phase: CondorPlacementRegion.SetupPhase, frontierY: 480));

        var first = Enemy(slot: 20, typeId: 17, x: 64, y: 992);
        var disabled = tracker.Observe(Snapshot(phase: 2, frontierY: 800, units: [first]));
        Equal(0, BattleLineEvents(disabled).Count, "automatic line opt-out");
        Equal(0, ArrivalEvents(disabled).Count, "enemy-arrival opt-out");

        tracker.RequestPlacementLine();
        var requested = tracker.ConsumeRequestedPlacementLine(
            Snapshot(phase: 2, frontierY: 800, units: [first]));
        if (requested is null || !requested.StartsWith("Battle line at 799.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"P must remain available when automatic speech is off, got {requested ?? "null"}.");
        }

        // The disabled arrival path must still advance the standing baseline;
        // otherwise every later snapshot would keep rediscovering the same unit.
        Equal(
            0,
            ArrivalEvents(tracker.Observe(
                Snapshot(phase: 2, frontierY: 800, units: [first]))).Count,
            "disabled arrival bookkeeping stays current");
    }

    private static void BothAnnouncementsAreEnabledInTheShippedDefaults()
    {
        var config = new AccessibilityConfig();
        foreach (var propertyName in new[]
                 {
                     "EnableCondorBattleLineAnnouncements",
                     "EnableCondorEnemyArrivalAnnouncements"
                 })
        {
            var property = typeof(AccessibilityConfig).GetProperty(propertyName);
            if (property?.GetValue(config) is not true)
            {
                throw new InvalidOperationException($"{propertyName} must default to true.");
            }
        }

        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new InvalidOperationException("FF7_ACCESSIBILITY_SOURCE_ROOT is required.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            sourceRoot,
            "Ff7.Accessibility.Reloaded",
            "Configuration",
            "config.json")));
        foreach (var propertyName in new[]
                 {
                     "EnableCondorBattleLineAnnouncements",
                     "EnableCondorEnemyArrivalAnnouncements"
                 })
        {
            if (!document.RootElement.TryGetProperty(propertyName, out var value) ||
                value.ValueKind != JsonValueKind.True)
            {
                throw new InvalidOperationException(
                    $"The shipped configuration must enable {propertyName} by default.");
            }
        }
    }

    private static CondorBattleSpeechTracker CreateConfiguredTracker(
        bool enableBattleLineAnnouncements,
        bool enableEnemyArrivalAnnouncements) =>
        new(
            enableBattleLineAnnouncements: enableBattleLineAnnouncements,
            enableEnemyArrivalAnnouncements: enableEnemyArrivalAnnouncements);

    private static List<string> BattleLineEvents(IReadOnlyList<string> lines) =>
        lines.Where(line => line.StartsWith("Battle line at ", StringComparison.Ordinal)).ToList();

    private static List<string> ArrivalEvents(IReadOnlyList<string> lines) =>
        lines.Where(line =>
                line.Contains(" entered from the ", StringComparison.Ordinal) ||
                line.EndsWith("Entry side unavailable.", StringComparison.Ordinal))
            .ToList();

    private static CondorBattleSnapshot Snapshot(
        int phase,
        int frontierY,
        IReadOnlyList<CondorBattleUnit>? units = null) =>
        new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
            ModalState: 0,
            SettingMenuRow: 0,
            SettingMenuRotation: 0,
            AvailableTypeIds: [],
            Gil: 9436,
            CursorX: 248,
            CursorY: 432,
            CursorPlacementLegal: false,
            UnitUnderCursorSlot: -1,
            Units: units ?? [],
            AlliedCount: 0,
            EnemyCount: units?.Count(unit => unit.IsEnemy && !unit.IsDying) ?? 0,
            Outcome: 0,
            MessageId: -1,
            Phase: phase,
            ReportState: 0,
            DeploymentFrontierY: frontierY,
            EnemyAdvance: 0,
            CollisionTriangles: []);

    private static CondorBattleUnit Enemy(int slot, int typeId, int x, int y) =>
        new(
            Slot: slot,
            IsEnemy: true,
            TypeId: typeId,
            CurrentHp: 200,
            MaximumHp: 200,
            Attack: 30,
            X: x,
            Y: y,
            IsDying: false,
            Width: 24,
            HeightAbove: 30);

    private static string Single(IReadOnlyList<string> lines)
    {
        if (lines.Count != 1)
        {
            throw new InvalidOperationException(
                $"expected one line, got {lines.Count}: {string.Join(" | ", lines)}");
        }

        return lines[0];
    }

    private static void SequenceEqual(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual,
        string label)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(" | ", expected)}], " +
                $"got [{string.Join(" | ", actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
