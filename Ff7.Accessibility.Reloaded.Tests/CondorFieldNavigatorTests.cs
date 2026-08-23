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
        JumpSaysWhereItIsTakingTheCursor();
        JumpSaysSoWhenItCannotMoveTheCursor();
        ForgetsLossesWhenANewBattleStarts();
        SelectionSurvivesUnitsChangingPlaces();
        SelectionSurvivesANewLossBeingRecorded();
        SaysSoWhenTheSelectedUnitLeavesTheField();
    }

    /// <summary>
    /// The list is rebuilt and re-sorted on every reading, so a selection held as
    /// a bare index quietly becomes a different unit the moment two of them cross.
    /// A player who selected their Fighter and pressed jump would be moved to
    /// something else entirely and told nothing.
    /// </summary>
    private static void SelectionSurvivesUnitsChangingPlaces()
    {
        var navigator = new CondorFieldNavigator();
        navigator.Update(new[] { Ally(0, 1, 100, 600), Ally(1, 2, 200, 800) }, 400, 700);

        var selected = navigator.Handle(CondorNavigationAction.NextTarget);
        AssertContains(selected, "Fighter");   // slot 0, the lower Y

        // The two units swap places on the field. Sorted by Y, they swap in the
        // list too - but the player's selection must not follow the ordering.
        navigator.Update(new[] { Ally(0, 1, 100, 900), Ally(1, 2, 200, 500) }, 400, 700);

        var jumped = new List<(int X, int Y)>();
        navigator.Handle(CondorNavigationAction.JumpToTarget,
            (x, y) => { jumped.Add((x, y)); return true; });

        AssertEqual(1, jumped.Count, "jump happened");
        AssertEqual((100, 900), jumped[0], "jump went to the still-selected Fighter, at its new position");
    }

    private static void SelectionSurvivesANewLossBeingRecorded()
    {
        var navigator = new CondorFieldNavigator();
        var first = Ally(0, 1, 100, 600);
        var second = Ally(1, 2, 200, 700);
        navigator.Update(new[] { first, second }, 400, 700);
        navigator.RecordLoss(first);
        navigator.Update(new[] { second }, 400, 700);

        navigator.Handle(CondorNavigationAction.PreviousCategory); // -> Losses, selects the Fighter
        AssertContains(navigator.Current?.Description, "Fighter");

        // Newest-first insertion shifts every existing index by one.
        navigator.RecordLoss(second);
        navigator.Update(Array.Empty<CondorBattleUnit>(), 400, 700);

        AssertContains(navigator.Current?.Description, "Fighter");
        AssertEqual(100, navigator.Current!.Value.X, "the selected loss is still the Fighter's");
    }

    /// <summary>
    /// Silence here would be the worst outcome: the player keeps pressing jump and
    /// lands on a unit they did not choose, with nothing to tell them why.
    /// </summary>
    private static void SaysSoWhenTheSelectedUnitLeavesTheField()
    {
        var navigator = new CondorFieldNavigator();
        navigator.Update(new[] { Ally(0, 1, 100, 600), Ally(1, 2, 200, 800) }, 400, 700);
        navigator.Handle(CondorNavigationAction.NextTarget); // Fighter, slot 0

        navigator.Update(new[] { Ally(1, 2, 200, 800) }, 400, 700); // the Fighter is gone

        var spoken = navigator.Handle(CondorNavigationAction.NextTarget);
        AssertContains(spoken, "Attacker");
        AssertContains(spoken, "gone");
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

    /// <summary>
    /// An accepted jump promises a journey rather than reporting an arrival.
    /// </summary>
    /// <remarks>
    /// The cursor is steered by holding the game's own direction keys, which
    /// takes time and can fail part way. Saying the cursor is already there
    /// would hand the player a position they could act on - buying a unit puts
    /// it where the cursor is - before it was true. Where the cursor actually
    /// comes to rest is announced by the cursor readout when the keys are
    /// released, which is the truth either way.
    /// </remarks>
    private static void JumpSaysWhereItIsTakingTheCursor()
    {
        var navigator = NavigatorWith(new[] { Ally(0, 1, 428, 706) });
        navigator.Handle(CondorNavigationAction.NextTarget);

        var moved = new List<(int X, int Y)>();
        var spoken = navigator.Handle(
            CondorNavigationAction.JumpToTarget,
            (x, y) => { moved.Add((x, y)); return true; });

        AssertEqual(1, moved.Count, "the steering was asked once");
        AssertEqual((428, 706), moved[0], "the steering was given the target's coordinates");
        AssertContains(spoken, "428, 706");
        AssertContains(spoken, "Going to");

        if (spoken is not null && spoken.Contains("Cursor at", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Navigator said '{spoken}', reporting the cursor's position as though the jump " +
                "had already finished. It has only just started.");
        }
    }

    /// <summary>
    /// The host cannot move the cursor by storing a coordinate - the battle's
    /// cursor is camera-relative - so the mover refuses and this must still leave
    /// the player able to find the thing. Both positions, and no claim of a move.
    /// </summary>
    private static void JumpSaysSoWhenItCannotMoveTheCursor()
    {
        var navigator = NavigatorWith(
            new[] { Ally(0, 1, 428, 706) }, cursorX: 100, cursorY: 200);
        navigator.Handle(CondorNavigationAction.NextTarget);
        var spoken = navigator.Handle(
            CondorNavigationAction.JumpToTarget, (_, _) => false);

        AssertContains(spoken, "428, 706");   // where the thing is
        AssertContains(spoken, "100, 200");   // where the cursor still is

        if (spoken is not null && spoken.Contains("Cursor at 428", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Navigator said '{spoken}', claiming the cursor reached the target when the " +
                "move was refused. A false position is worse than no position.");
        }
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
