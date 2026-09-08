namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Speaks the chocobo racing screens as they are drawn.
///
/// The betting menu reports the cell the cursor is on and the pair it stands for,
/// the tickets already bought and any older one a fourth choice replaced, the card
/// the player has opened, the three gifts on offer and the balance the screen shows -
/// which is the party's gil less the tickets already reserved, not the unspent total.
///
/// The race reports the numbered strip in the order it is actually drawn, refreshed
/// on the native sixteen-frame cadence, and the minimap positions of racers still
/// running. The results report the winning pair the big digits show and the six
/// finishing places, which are not the same thing: the pair is sorted ascending and
/// the places come from each racer's own finishing position.
///
/// Nothing here selects a cell, buys a ticket, or reads an attribute the open card
/// does not draw. A grid cell's prize stays hidden until the native display flag
/// exposes it.
/// </summary>
public sealed class ChocoboSquareReadout
{
    private ChocoboSquarePhase lastPhase = ChocoboSquarePhase.None;
    private int lastCursorCell = int.MinValue;
    private int lastInspectedRacer = int.MinValue;
    private int lastSelectedCount = int.MinValue;
    private int[] lastCells = [];
    private string lastRanking = string.Empty;

    // The player's own race: mode, pace, stamina, dash and place. Held here so it shares
    // this readout's lifetime and is reset with it.
    private readonly ChocoboRaceControlReadout raceControl = new();
    private bool announcedResults;
    private bool announcedPrizes;

    public IReadOnlyList<string> ObserveBetting(ChocoboBettingState state)
    {
        var lines = new List<string>(4);
        if (!state.IsOpen)
        {
            if (lastPhase == ChocoboSquarePhase.Betting)
            {
                ResetBetting();
            }

            return lines;
        }

        var entering = lastPhase != ChocoboSquarePhase.Betting;
        lastPhase = ChocoboSquarePhase.Betting;
        if (entering)
        {
            lines.Add($"Betting. Class {DescribeClass(state.RaceClass)}. {state.DisplayedGil} gil.");
            lastCells = state.SelectedCells.ToArray();
            lastSelectedCount = state.SelectedCount;
        }

        var cursorCell = (state.CursorColumn * 10) + state.CursorRow;
        if (cursorCell != lastCursorCell)
        {
            lastCursorCell = cursorCell;
            var (first, second) = ChocoboSquareStateReader.DescribeCell(cursorCell);
            var alreadyChosen = state.SelectedCells.Contains(cursorCell) ? ", already chosen" : string.Empty;
            lines.Add($"{first} and {second}{alreadyChosen}.");
        }

        // A fourth choice replaces an older one rather than being refused, so the
        // slot that changed is reported as well as the one that was added.
        if (lastCells.Length == state.SelectedCells.Count)
        {
            for (var slot = 0; slot < lastCells.Length; slot++)
            {
                if (lastCells[slot] == state.SelectedCells[slot])
                {
                    continue;
                }

                var removed = lastCells[slot];
                var added = state.SelectedCells[slot];
                if (added >= 0)
                {
                    var (addedFirst, addedSecond) = ChocoboSquareStateReader.DescribeCell(added);
                    if (removed >= 0)
                    {
                        var (removedFirst, removedSecond) = ChocoboSquareStateReader.DescribeCell(removed);
                        lines.Add(
                            $"Ticket {slot + 1} is now {addedFirst} and {addedSecond}, " +
                            $"replacing {removedFirst} and {removedSecond}.");
                    }
                    else
                    {
                        lines.Add($"Ticket {slot + 1}: {addedFirst} and {addedSecond}.");
                    }
                }
                else if (removed >= 0)
                {
                    var (removedFirst, removedSecond) = ChocoboSquareStateReader.DescribeCell(removed);
                    lines.Add($"Ticket {slot + 1} cleared, was {removedFirst} and {removedSecond}.");
                }
            }
        }

        if (state.SelectedCount != lastSelectedCount)
        {
            lines.Add($"{state.SelectedCount} of 3 tickets. {state.DisplayedGil} gil.");
        }

        lastCells = state.SelectedCells.ToArray();
        lastSelectedCount = state.SelectedCount;

        if (state.InspectedRacer.Index != lastInspectedRacer)
        {
            lastInspectedRacer = state.InspectedRacer.Index;
            var racer = state.InspectedRacer;
            var giftList = string.Join(", ", state.Gifts.Where(gift => gift.Length > 0));
            var gifts = giftList.Length > 0 ? $" Gifts: {giftList}." : string.Empty;
            lines.Add(
                $"Number {racer.Number}, {racer.Name}. Top speed {racer.TopSpeed}, " +
                $"stamina {racer.Stamina}.{gifts}");
        }

        return lines;
    }

    public IReadOnlyList<string> ObserveRace(ChocoboRaceState state)
    {
        var lines = new List<string>(4);
        if (!state.IsRacing)
        {
            if (lastPhase == ChocoboSquarePhase.Race)
            {
                lastPhase = ChocoboSquarePhase.None;
                lastRanking = string.Empty;
                raceControl.Reset();
            }

            return lines;
        }

        if (state.IsObscured)
        {
            // The screen is black. Whatever the state says underneath it, a sighted player
            // is being shown nothing, and so is this.
            return lines;
        }

        var entering = lastPhase != ChocoboSquarePhase.Race;
        lastPhase = ChocoboSquarePhase.Race;
        if (entering)
        {
            lines.Add("Race under way.");
        }

        // The player's own race comes first. In the recorded race the mod said the
        // finishing order about sixty-five times in seventy seconds and never once said
        // whether the player was in manual or automatic, or whether they were speeding up.
        // Putting the control readout after the order would bury it again.
        lines.AddRange(raceControl.Observe(state.Control));

        if (state.Ranking.Count == 0)
        {
            // Between refreshes the strip is not settled. Holding the last known
            // order is better than reading a half-updated one.
            return lines;
        }

        var ranking = string.Join(
            ", ", state.Ranking.OrderBy(sprite => sprite.Place).Select(sprite => sprite.Number));
        if (ranking != lastRanking)
        {
            lastRanking = ranking;

            // Riding, the field order is only worth interrupting for when the player's own
            // place has moved. In the recorded race it was announced about sixty-five times
            // in seventy seconds while two rivals swapped behind them, and it buried the
            // mode and the pace. The whole order is still on the status key.
            if (!state.Control.IsPlayerRacing || raceControl.PlaceChangedOnLastObservation || entering)
            {
                lines.Add($"Order: {ranking}.");
            }
        }

        return lines;
    }

    /// <summary>
    /// The betting screen on demand: the cell under the cursor and the pair it stands
    /// for, the tickets bought so far, the card that is open and the gifts on offer.
    /// Everything a sighted player can look back at without the screen having changed.
    /// </summary>
    public static string DescribeBettingStatus(ChocoboBettingState state)
    {
        if (!state.IsOpen)
        {
            return "The betting menu is not open.";
        }

        var cursorCell = (state.CursorColumn * 10) + state.CursorRow;
        var (first, second) = ChocoboSquareStateReader.DescribeCell(cursorCell);
        var parts = new List<string>(5)
        {
            $"Class {DescribeClass(state.RaceClass)}. {state.DisplayedGil} gil.",
            $"Cursor on {first} and {second}."
        };

        var tickets = state.SelectedCells
            .Select((cell, slot) => (cell, slot))
            .Where(entry => entry.cell >= 0)
            .Select(entry =>
            {
                var (ticketFirst, ticketSecond) = ChocoboSquareStateReader.DescribeCell(entry.cell);
                return $"{entry.slot + 1}: {ticketFirst} and {ticketSecond}";
            })
            .ToList();
        parts.Add(tickets.Count == 0
            ? "No tickets bought."
            : $"Tickets, {string.Join("; ", tickets)}.");

        var racer = state.InspectedRacer;
        parts.Add(
            $"Card, number {racer.Number}, {racer.Name}. Top speed {racer.TopSpeed}, " +
            $"stamina {racer.Stamina}.");

        var giftList = string.Join(", ", state.Gifts.Where(gift => gift.Length > 0));
        if (giftList.Length > 0)
        {
            parts.Add($"Gifts: {giftList}.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// The current race status on demand: the drawn order, and where each square sits
    /// on the minimap relative to the one the camera is following.
    ///
    /// The offsets are the map's own, in the pixels the squares are actually drawn at,
    /// so they say the same thing a sighted player reads off the little map: which
    /// squares are bunched with the tracked one and which are away from it. They are
    /// not a distance along the track, and are not described as one.
    /// </summary>
    public static string DescribeRaceStatus(ChocoboRaceState state)
    {
        if (!state.IsRacing)
        {
            return "No race is running.";
        }

        if (state.IsObscured)
        {
            // The same rule the running readout follows. A sighted player pressing the
            // status key behind a fade is shown black, and reading them the HUD underneath
            // it would be showing them something the game is not.
            return "The screen is dark.";
        }

        var parts = new List<string>(4);
        if (state.Control.IsPlayerRacing)
        {
            // What is steering, where they are and what is left, before the field.
            parts.Add(ChocoboRaceControlReadout.Describe(state.Control));
        }

        if (state.Ranking.Count > 0)
        {
            parts.Add("Order: " + string.Join(
                ", ", state.Ranking.OrderBy(sprite => sprite.Place).Select(sprite => sprite.Number)) + ".");
        }

        var tracked = state.Minimap.FirstOrDefault(marker => marker.IsInspected);
        if (!state.Minimap.Any(marker => marker.IsInspected))
        {
            // The camera's own racer has finished and its square is no longer
            // refreshed, so there is nothing left to measure the others against.
            var stillRunning = state.Minimap.Count;
            parts.Add(stillRunning == 1
                ? "1 square left on the map."
                : $"{stillRunning} squares left on the map.");
            return string.Join(" ", parts);
        }

        var others = state.Minimap
            .Where(marker => !marker.IsInspected)
            .Select(marker =>
                $"{marker.Index + 1} {DescribeMapOffset(marker.X - tracked.X, marker.Y - tracked.Y)}")
            .ToList();
        parts.Add($"On the map, relative to {tracked.Index + 1}: " +
                  (others.Count == 0 ? "no other squares." : string.Join(", ", others) + "."));
        return string.Join(" ", parts);
    }

    /// <summary>
    /// The results screen on demand, which is the only way to hear it again once it
    /// has been read out. Nothing is repeated that the screen is not still showing.
    /// </summary>
    public static string DescribeResultsStatus(ChocoboResultsState state)
    {
        if (!state.IsShowing)
        {
            return "The results screen is not up.";
        }

        if (!state.IsRevealed)
        {
            return "The results screen has not come up yet.";
        }

        var parts = new List<string>(3)
        {
            $"Winning pair {state.FirstWinningNumber} and {state.SecondWinningNumber}.",
            "Finish: " + string.Join(
                ", ",
                state.Places.Select(place =>
                    $"{Ordinal(place.Place)} number {place.Number}, {place.Name}")) + "."
        };

        if (state.Prizes.Count > 0)
        {
            parts.Add(DescribePrizes(state));
        }

        return string.Join(" ", parts);
    }

    public IReadOnlyList<string> ObserveResults(ChocoboResultsState state)
    {
        var lines = new List<string>(2);
        if (!state.IsShowing)
        {
            if (lastPhase == ChocoboSquarePhase.Results)
            {
                lastPhase = ChocoboSquarePhase.None;
                announcedResults = false;
                announcedPrizes = false;
            }

            return lines;
        }

        lastPhase = ChocoboSquarePhase.Results;

        // The screen fades in from black over its own entry. Announcing while the
        // overlay is still full would say something no one can see yet.
        if (state.IsRevealed && !announcedResults)
        {
            announcedResults = true;
            lines.Add($"Winning pair {state.FirstWinningNumber} and {state.SecondWinningNumber}.");
            lines.Add("Finish: " + string.Join(
                ", ",
                state.Places.Select(place =>
                    $"{Ordinal(place.Place)} number {place.Number}, {place.Name}")) + ".");
        }

        // The prize cards turn over much later, and only once. Until then the grid
        // shows fifteen backs, and there is nothing about them to say.
        if (state.Prizes.Count > 0 && !announcedPrizes)
        {
            announcedPrizes = true;
            lines.Add(DescribePrizes(state));
        }

        return lines;
    }

    /// <summary>
    /// The grid as it now reads. The cards show pictures of the three gifts the menu
    /// already listed, so they are grouped by gift rather than read out one by one -
    /// fifteen separate cells would be unusable, and the grouping is what a sighted
    /// player takes from the layout anyway.
    /// </summary>
    private static string DescribePrizes(ChocoboResultsState state)
    {
        var winningCell = state.Prizes.FirstOrDefault(prize =>
            prize.First == state.FirstWinningNumber && prize.Second == state.SecondWinningNumber);
        var hasWinningCell = state.Prizes.Any(prize =>
            prize.First == state.FirstWinningNumber && prize.Second == state.SecondWinningNumber);

        var groups = state.Prizes
            .GroupBy(prize => prize.Prize)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} on {DescribeCellList(group)}")
            .ToList();

        var opening = hasWinningCell
            ? $"Prize cards turned over. {state.FirstWinningNumber} and " +
              $"{state.SecondWinningNumber} shows {winningCell.Prize}."
            : "Prize cards turned over.";
        return groups.Count == 0 ? opening : $"{opening} {string.Join("; ", groups)}.";
    }

    private static string DescribeCellList(IEnumerable<ChocoboPrizeCell> cells) =>
        string.Join(", ", cells.Select(cell => $"{cell.First} and {cell.Second}"));

    /// <summary>
    /// Where one minimap square sits relative to another, in the drawn map's own
    /// pixels. Squares closer together than a few pixels are called together rather
    /// than given a direction, because at that separation the map does not show one.
    /// </summary>
    private static string DescribeMapOffset(int dx, int dy)
    {
        const int Together = 3;
        if (Math.Abs(dx) <= Together && Math.Abs(dy) <= Together)
        {
            return "alongside";
        }

        var horizontal = Math.Abs(dx) <= Together
            ? string.Empty
            : $"{Math.Abs(dx)} {(dx < 0 ? "left" : "right")}";
        var vertical = Math.Abs(dy) <= Together
            ? string.Empty
            : $"{Math.Abs(dy)} {(dy < 0 ? "up" : "down")}";
        return horizontal.Length == 0
            ? vertical
            : vertical.Length == 0
                ? horizontal
                : $"{horizontal} and {vertical}";
    }

    public void Reset()
    {
        lastPhase = ChocoboSquarePhase.None;
        ResetBetting();
        lastRanking = string.Empty;
        raceControl.Reset();
        announcedResults = false;
        announcedPrizes = false;
    }

    private void ResetBetting()
    {
        lastPhase = lastPhase == ChocoboSquarePhase.Betting ? ChocoboSquarePhase.None : lastPhase;
        lastCursorCell = int.MinValue;
        lastInspectedRacer = int.MinValue;
        lastSelectedCount = int.MinValue;
        lastCells = [];
    }

    private static string DescribeClass(int raceClass) => raceClass switch
    {
        0 => "C",
        1 => "B",
        2 => "A",
        3 => "S",
        _ => raceClass.ToString()
    };

    private static string Ordinal(int place) => place switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        6 => "sixth",
        _ => place.ToString()
    };
}
