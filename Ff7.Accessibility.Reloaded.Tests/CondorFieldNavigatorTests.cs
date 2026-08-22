using Ff7.Accessibility.Reloaded;

/// <summary>
/// The Fort Condor battlefield navigator: three lists a player can walk with the
/// same keys they already use on the field, and a jump that puts the cursor on
/// the chosen thing.
/// </summary>
/// <remarks>
/// Positions are spoken as coordinates rather than as a direction and distance
/// from the cursor. A direction is only true until the cursor moves; a coordinate
/// is a fact about the battlefield, so a player can keep a map of it in their
/// head across a whole battle. For enemies it is also the more useful number
/// outright - the fort is at low Y and the enemy advances toward it, so a falling
/// Y *is* their progress.
/// </remarks>
internal static class CondorFieldNavigatorTests
{
    public static void Run()
    {
        StartsOnAlliesAndNamesWhatIsThere();
        CyclesCategoriesInBothDirections();
        CyclesTargetsAndWrapsAround();
        SpeaksCoordinatesRatherThanDirections();
        RemembersWhereAUnitDiedAfterItLeavesTheField();
        OrdersEnemiesByHowFarTheyHaveAdvanced();
        SaysSoWhenACategoryIsEmpty();
        JumpReportsTheTargetItMovedTo();
        JumpSaysSoWhenItCannotMoveTheCursor();
        ForgetsLossesWhenANewBattleStarts();
    }

    private static CondorBattleUnit Ally(int slot, int typeId, int x, int y, int hp = 200) =>
        new(slot, IsEnemy: false, typeId, hp, 200, 30, x, y, IsDying: false, Width: 16, HeightAbove: 16);

    private static CondorBattleUnit Enemy(int slot, int typeId, int x, int y, int hp = 140) =>
        new(slot, IsEnemy: true, typeId, hp, 140, 25, x, y, IsDying: false, Width: 16, HeightAbove: 16);

    private static CondorFieldNavigator NavigatorWith(
        IReadOnlyList<CondorBattleUnit> units, int cursorX = 400, int cursorY = 700)
    {
        var navigator = new CondorFieldNavigator();
        navigator.Update(units, cursorX, cursorY);
        return navigator;
    }

    private static void StartsOnAlliesAndNamesWhatIsThere()
    {
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706) });
        var spoken = navigator.Handle(CondorNavigationAction.NextTarget);
        AssertContains(spoken, "Fighter");
        AssertContains(spoken, "428, 706");
    }

    private static void CyclesCategoriesInBothDirections()
    {
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706), Enemy(20, 17, 396, 512) });

        AssertContains(navigator.Handle(CondorNavigationAction.NextCategory), "Enemies");
        AssertContains(navigator.Handle(CondorNavigationAction.NextCategory), "Losses");
        // Three categories, so a third step comes back round to the start.
        AssertContains(navigator.Handle(CondorNavigationAction.NextCategory), "Allies");
        AssertContains(navigator.Handle(CondorNavigationAction.PreviousCategory), "Losses");
    }

    private static void CyclesTargetsAndWrapsAround()
    {
        var navigator = NavigatorWith(new[]
        {
            Ally(0, 1, 100, 600),
            Ally(1, 2, 200, 700),
            Ally(2, 3, 300, 800)
        });

        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "100, 600");
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "200, 700");
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "300, 800");
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "100, 600");
        AssertContains(navigator.Handle(CondorNavigationAction.PreviousTarget), "300, 800");
    }

    private static void SpeaksCoordinatesRatherThanDirections()
    {
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706) }, cursorX: 100, cursorY: 100);
        var spoken = navigator.Handle(CondorNavigationAction.NextTarget);
        AssertContains(spoken, "428, 706");
        foreach (var directional in new[] { " up", " down", " left", " right" })
        {
            if (spoken is not null && spoken.Contains(directional, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Navigator said '{spoken}', which contains the relative term '{directional.Trim()}'. " +
                    "Positions are spoken as coordinates so they stay true after the cursor moves.");
            }
        }
    }

    private static void RemembersWhereAUnitDiedAfterItLeavesTheField()
    {
        var navigator = new CondorFieldNavigator();
        var attacker = Ally(0, 2, 428, 706);
        navigator.Update(new[] { attacker }, 400, 700);

        // The unit is gone from the field; the place it fell is the whole point.
        navigator.RecordLoss(attacker);
        navigator.Update(Array.Empty<CondorBattleUnit>(), 400, 700);

        navigator.Handle(CondorNavigationAction.PreviousCategory); // Allies -> Losses
        var spoken = navigator.Handle(CondorNavigationAction.NextTarget);
        AssertContains(spoken, "Attacker");
        AssertContains(spoken, "428, 706");
    }

    private static void OrdersEnemiesByHowFarTheyHaveAdvanced()
    {
        // The fort is at low Y, so the enemy nearest it is the one to hear about
        // first regardless of where the cursor happens to be.
        var navigator = NavigatorWith(new[]
        {
            Enemy(20, 17, 300, 900),
            Enemy(21, 18, 320, 500),
            Enemy(22, 19, 340, 700)
        });
        // Switching category shows the player into the list rather than only
        // counting it, so the most advanced enemy is named by that press alone.
        var entered = navigator.Handle(CondorNavigationAction.NextCategory);
        AssertContains(entered, "Enemies");
        AssertContains(entered, "3");
        AssertContains(entered, "320, 500");

        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "340, 700");
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "300, 900");
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "320, 500");
    }

    private static void SaysSoWhenACategoryIsEmpty()
    {
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706) });
        var spoken = navigator.Handle(CondorNavigationAction.PreviousCategory); // -> Losses, empty
        AssertContains(spoken, "Losses");
        AssertContains(spoken, "none");

        // Silence would read as a broken key. It has to keep saying the list is empty.
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "none");
    }

    private static void JumpReportsTheTargetItMovedTo()
    {
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706) });
        navigator.Handle(CondorNavigationAction.NextTarget);

        var moved = new List<(int X, int Y)>();
        var spoken = navigator.Handle(
            CondorNavigationAction.JumpToTarget,
            (x, y) => { moved.Add((x, y)); return true; });

        AssertEqual(1, moved.Count, "jump wrote the cursor once");
        AssertEqual((428, 706), moved[0], "jump wrote the target's coordinates");
        AssertContains(spoken, "428, 706");
    }

    private static void JumpSaysSoWhenItCannotMoveTheCursor()
    {
        // A jump that silently does nothing would leave the player believing the
        // cursor had moved and reading the wrong ground.
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706) });
        navigator.Handle(CondorNavigationAction.NextTarget);
        var spoken = navigator.Handle(
            CondorNavigationAction.JumpToTarget, (_, _) => false);
        AssertContains(spoken, "could not");
    }

    private static void ForgetsLossesWhenANewBattleStarts()
    {
        var navigator = new CondorFieldNavigator();
        var attacker = Ally(0, 2, 428, 706);
        navigator.Update(new[] { attacker }, 400, 700);
        navigator.RecordLoss(attacker);

        navigator.Reset();
        navigator.Update(Array.Empty<CondorBattleUnit>(), 400, 700);
        navigator.Handle(CondorNavigationAction.PreviousCategory);
        AssertContains(navigator.Handle(CondorNavigationAction.NextTarget), "none");
    }

    private static void AssertContains(string? actual, string expected)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected the navigator to say something containing '{expected}' but it said '{actual ?? "<null>"}'.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {what} to be '{expected}' but it was '{actual}'.");
        }
    }
}
