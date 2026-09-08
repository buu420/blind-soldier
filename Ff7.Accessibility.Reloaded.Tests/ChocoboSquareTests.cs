using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The chocobo racing betting menu, race strip and results screen. Every unit and
/// encoding here is the native one root traced, and the tests exist mainly to hold
/// the ones that are easy to get subtly wrong: the cyclic insertion index that is not
/// a count, the balance that already has reservations taken off, the sorted winning
/// pair that is not a finishing order, and the drawn sprites that must not be
/// replaced by newer internal ranks.
/// </summary>
internal static class ChocoboSquareTests
{
    internal static void Run()
    {
        ThePhaseGateIsTheNativeSubstate();
        TheBettingMenuReadsWhatIsDrawn();
        AFourthTicketReplacesAnOlderOne();
        TheRaceStripIsReadAsDrawn();
        TheResultsSeparateTheWinningPairFromTheFinishOrder();
        TheGridCellEncodingMatchesTheRenderedPairs();
        TheCellPairsMatchTheRenderersOwnTable();
        TheHighResolutionStripIsDecodedFromItsOwnUvWords();
        ThePrizeGridIsReadOnlyOnceTheCardsAreFaceUp();
        TheStatusPathDescribesWhatIsOnEachScreen();
        AScreenThatChangesMidReadIsNotDescribed();
        ThePlayerRecordIsSignedAndCheckedAtBothEnds();
    }

    /// <summary>
    /// The player's own record, which is nine reads of a structure the game is writing.
    ///
    /// <para>Two things have to hold. The fields are signed - the speed especially, because
    /// read unsigned the native -1 that a stopped record can carry becomes 65535, which
    /// would be announced as the fastest chocobo in the game's history. And everything that
    /// decides what the nine reads <i>mean</i> - the module, the phase, whether the player
    /// is riding or betting, the fade over the HUD - has to be checked after them as well
    /// as before, because any of it can change in between and leave a state that is half of
    /// one race and half of something else.</para>
    /// </summary>
    private static void ThePlayerRecordIsSignedAndCheckedAtBothEnds()
    {
        var memory = new FakeChocoboMemory { Substate = 3, RankingVisible = 1, RaceMode = 1 };
        memory.SetPlayerControl(speed: 2000, automatic: 1, stamina: 5000, staminaMaximum: 10000);
        Equal(true, new ChocoboSquareStateReader(memory).TryReadRace(out var riding),
            "a settled player race reads");
        Equal(true, riding.Control.IsPlayerRacing, "and it is the player riding");
        Equal(2000, riding.Control.Speed, "at the speed the record carries");

        // -1 in the native short. Unsigned it is 65535.
        memory.SetPlayerControl(speed: -1, automatic: 1, stamina: 5000, staminaMaximum: 10000);
        var wrapped = new ChocoboSquareStateReader(memory);
        Equal(false, wrapped.TryReadRace(out _),
            "a negative speed is not a state to speak about confidently");
        Equal(true, wrapped.LastDiagnostic.Contains("speed=-1", StringComparison.Ordinal),
            $"and it is refused as the signed value it is: {wrapped.LastDiagnostic}");

        // A maximum of zero is the one payload that is odd and still true: the gauge simply
        // cannot be worked out, which the readout says rather than calling it empty.
        memory.SetPlayerControl(speed: 2000, automatic: 0, stamina: 5000, staminaMaximum: 0);
        Equal(true, new ChocoboSquareStateReader(memory).TryReadRace(out var unknownGauge),
            "an unreadable gauge does not stop the mode being read");
        Equal(0, unknownGauge.Control.StaminaMaximum, "and it is passed on as unknown");
        Equal(false, unknownGauge.Control.IsAutomatic, "with the mode still true");

        // The screen leaves the race part way through the record.
        memory.SetPlayerControl(speed: 2000, automatic: 1, stamina: 5000, staminaMaximum: 10000);
        memory.ChangeSubstateAfterReading(
            (uint)(ChocoboSquareStateReader.AddressRacerArray +
                   ChocoboSquareStateReader.RacerStaminaOffset),
            newSubstate: 5);
        Equal(false, new ChocoboSquareStateReader(memory).TryReadRace(out _),
            "a phase change during the player record leaves a mixture, not a state");

        // The player stops being a jockey part way through the record - the same words,
        // about somebody else's race.
        memory.Substate = 3;
        memory.ChangeRaceModeAfterReading(
            (uint)(ChocoboSquareStateReader.AddressRacerArray +
                   ChocoboSquareStateReader.RacerStaminaOffset),
            newRaceMode: 0);
        var switched = new ChocoboSquareStateReader(memory);
        Equal(false, switched.TryReadRace(out _),
            "so does a switch from riding to betting");

        // And a fade that arrives during the read hides the HUD the reader is describing.
        memory.RaceMode = 1;
        memory.ChangeFadeAfterReading(
            (uint)(ChocoboSquareStateReader.AddressRacerArray +
                   ChocoboSquareStateReader.RacerStaminaOffset),
            newFade: 255);
        Equal(true, new ChocoboSquareStateReader(memory).TryReadRace(out var faded),
            "a fade is not a read failure");
        Equal(false, faded.Control.IsPlayerRacing,
            "but nothing of the player's own HUD survives it");
    }

    private static void ThePhaseGateIsTheNativeSubstate()
    {
        var memory = new FakeChocoboMemory();
        var reader = new ChocoboSquareStateReader(memory);

        // Outside the race's own module these globals belong to whatever ran last.
        memory.Module = HighwayStateReader.HighwayModule;
        memory.Substate = 1;
        Equal(true, reader.TryReadPhase(out var wrongModule), "another module reads");
        Equal(ChocoboSquarePhase.None, wrongModule, "but is never a chocobo phase");
        Equal(true, reader.TryReadBetting(out var notHere), "and the menu reads");
        Equal(false, notHere.IsOpen, "reporting itself closed");

        memory.Module = ChocoboSquareStateReader.ChocoboModule;
        foreach (var (substate, expected) in new[]
                 {
                     (0, ChocoboSquarePhase.None),
                     (1, ChocoboSquarePhase.Betting),
                     (2, ChocoboSquarePhase.None),
                     (3, ChocoboSquarePhase.Race),
                     (4, ChocoboSquarePhase.None),
                     (5, ChocoboSquarePhase.Results),
                     (6, ChocoboSquarePhase.None)
                 })
        {
            memory.Substate = substate;
            Equal(true, reader.TryReadPhase(out var phase), $"substate {substate} reads");
            Equal(expected, phase, $"substate {substate} is {expected}");
        }

        // A screen that is not up must not be described from stale globals.
        memory.Substate = 0;
        Equal(true, reader.TryReadBetting(out var closed), "the betting menu reads while closed");
        Equal(false, closed.IsOpen, "and reports itself closed");
        Equal(true, reader.TryReadRace(out var noRace), "the race reads while not running");
        Equal(false, noRace.IsRacing, "and reports itself not running");
        Equal(true, reader.TryReadResults(out var noResults), "the results read while not shown");
        Equal(false, noResults.IsShowing, "and report themselves not shown");

        memory.SubstateReadable = false;
        Equal(false, reader.TryReadPhase(out _), "an unreadable substate is not a phase");
    }

    private static void TheBettingMenuReadsWhatIsDrawn()
    {
        var memory = new FakeChocoboMemory { Substate = 1 };
        var reader = new ChocoboSquareStateReader(memory);

        memory.CursorColumn = 2;
        memory.CursorRow = 1;
        memory.SelectedCount = 1;
        memory.RaceClass = 1;
        memory.PartyGil = 5000;
        // Class B, one ticket already reserved: the screen shows the balance less it.
        memory.SetPrice(raceClass: 1, reservedCount: 1, price: 200);
        memory.SetSelectedCell(0, 21);
        memory.InspectedRacer = 3;
        // The card's digit and the model's own jockey id are deliberately different
        // here: the card draws the inspected index, and using the model's id instead
        // would name a different chocobo than the screen does.
        memory.SetRacer(3, jockeyId: 1, name: "Teioh", rawTopSpeed: 34 * 7, rawStamina: 10 * 42);
        memory.SetGift(0, "Potion");
        memory.SetGift(1, "Ether");
        memory.SetGift(2, "Elixir");

        Equal(true, reader.TryReadBetting(out var state), $"the menu reads; {reader.LastDiagnostic}");
        Equal(true, state.IsOpen, "and is open");
        Equal(2, state.CursorColumn, "with the drawn cursor column");
        Equal(1, state.CursorRow, "and row");
        Equal(21, state.SelectedCells[0], "the chosen cell is column * 10 + row");
        Equal(-1, state.SelectedCells[1], "and an empty slot reads as empty");
        Equal(4800, state.DisplayedGil, "the balance already has the reserved ticket taken off");

        // The card's own units: the raw values are divided before being drawn, and
        // the raw ones are never spoken.
        Equal(7, state.InspectedRacer.TopSpeed, "top speed is the raw short over 34");
        Equal(42, state.InspectedRacer.Stamina, "stamina is the raw int over 10");
        Equal(4, state.InspectedRacer.Number, "the card's digit is the inspected index plus one");
        Equal(2, state.InspectedRacer.JockeyNumber, "which is not the model's own printed identity");
        Equal("Teioh", state.InspectedRacer.Name, "and the name is the one on the card");
        Equal(3, state.Gifts.Count, "the three offered gifts are read");
        Equal("Ether", state.Gifts[1], "each from its own displayed index");

        var readout = new ChocoboSquareReadout();
        var lines = readout.ObserveBetting(state);
        var joined = string.Join(" ", lines);
        Equal(true, joined.Contains("4800 gil", StringComparison.Ordinal),
            $"the spoken balance is the displayed one; got {joined}");
        Equal(true, joined.Contains("Top speed 7", StringComparison.Ordinal),
            $"and the card's own units; got {joined}");
        Equal(false, joined.Contains("238", StringComparison.Ordinal),
            "the raw top speed is never spoken");
        Equal(false, joined.Contains("420", StringComparison.Ordinal),
            "nor the raw stamina");
        Equal(false, joined.Contains("5000", StringComparison.Ordinal),
            "nor the unspent balance before reservations");

        // Out-of-range values are a torn read rather than a state.
        memory.CursorColumn = 9;
        Equal(false, reader.TryReadBetting(out _), "an impossible cursor column is refused");
        memory.CursorColumn = 2;
        memory.InspectedRacer = 12;
        Equal(false, reader.TryReadBetting(out _), "and an impossible inspected racer");
    }

    /// <summary>
    /// 0x00E737AC wraps at 2, so it is a cyclic slot index and a fourth choice
    /// overwrites an older ticket. Reporting it as a count would be wrong, and
    /// silently losing the replaced pair would leave the player unable to tell what
    /// they now hold.
    /// </summary>
    private static void AFourthTicketReplacesAnOlderOne()
    {
        var readout = new ChocoboSquareReadout();
        var first = Betting(cells: [-1, -1, -1], count: 0);
        readout.ObserveBetting(first);

        var one = Betting(cells: [1, -1, -1], count: 1);
        var spokenOne = string.Join(" ", readout.ObserveBetting(one));
        Equal(true, spokenOne.Contains("Ticket 1", StringComparison.Ordinal),
            $"a new ticket is announced; got {spokenOne}");
        Equal(true, spokenOne.Contains("1 of 3 tickets", StringComparison.Ordinal),
            $"with the count the screen shows; got {spokenOne}");

        readout.ObserveBetting(Betting(cells: [1, 11, 21], count: 3));

        // The fourth choice lands back in slot one, replacing what was there.
        var replaced = string.Join(" ", readout.ObserveBetting(Betting(cells: [31, 11, 21], count: 3)));
        Equal(true, replaced.Contains("replacing", StringComparison.Ordinal),
            $"the replaced pair is named; got {replaced}");
    }

    private static void TheRaceStripIsReadAsDrawn()
    {
        var memory = new FakeChocoboMemory { Substate = 3, RankingVisible = 1, GraphicsMode = 0 };
        var reader = new ChocoboSquareStateReader(memory);

        // Six drawn sprites, each a different number, in the order the strip lists.
        var order = new[] { 3, 0, 5, 1, 4, 2 };
        for (var place = 0; place < order.Length; place++)
        {
            memory.SetRankingSprite(place, ChocoboSquareStateReader.DrawnRenderId, order[place] * 24, 0);
        }

        Equal(true, reader.TryReadRace(out var race), $"the race reads; {reader.LastDiagnostic}");
        Equal(6, race.Ranking.Count, "all six drawn sprites are decoded");
        Equal(4, race.Ranking[0].Number, "the leader is the first drawn sprite's own number");

        // A frame caught mid-refresh must not produce a partial order.
        memory.SetRankingSprite(2, renderId: 0, u: 0, v: 0);
        Equal(true, reader.TryReadRace(out var partial), "the race still reads");
        Equal(0, partial.Ranking.Count, "but a half-updated strip is not an order");

        // Neither must a duplicated number.
        memory.SetRankingSprite(2, ChocoboSquareStateReader.DrawnRenderId, 3 * 24, 0);
        Equal(true, reader.TryReadRace(out var duplicated), "the race still reads");
        Equal(0, duplicated.Ranking.Count, "a strip with a repeated number is not settled");

        // The graphics mode changes how the numbers are laid out in the sheet.
        Equal(true, ChocoboSquareStateReader.TryDecodeJockeyId(2, 48, 48, out var mode2), "mode 2 decodes");
        Equal(4, mode2, "as a three-across grid of forty-eight pixel cells");
        Equal(true, ChocoboSquareStateReader.TryDecodeJockeyId(0, 72, 0, out var mode0), "other modes decode");
        Equal(3, mode0, "as a single row of twenty-four pixel cells");
        Equal(false, ChocoboSquareStateReader.TryDecodeJockeyId(0, 30, 0, out _),
            "a coordinate that is not on the sheet grid is not a number");

        // The minimap shows only racers still running.
        memory.SetRacerFinished(0, finished: 1);
        memory.SetMinimap(1, x: 300, y: 45);
        Equal(true, reader.TryReadRace(out var running), "the race reads");
        Equal(5, running.Minimap.Count, "a finished racer is no longer drawn on the map");
        Equal(300, running.Minimap.Single(marker => marker.Index == 1).X,
            "and the drawn coordinates are the ones on screen");

        // A betting race draws no player stamina gauge, so none is claimed.
        Equal(false, running.IsPlayerRacing, "a betting race is not the player riding");
    }

    private static void TheResultsSeparateTheWinningPairFromTheFinishOrder()
    {
        var memory = new FakeChocoboMemory
        {
            Substate = 5,
            // Both globals are zero-based racer indices into a digit array whose
            // faces read one through six.
            WinningFirst = 1,
            WinningSecond = 4,
            ResultsFrame = 0,
            FadeState = 0
        };
        var reader = new ChocoboSquareStateReader(memory);

        // The sorted pair is 2 and 5, but the actual finish is 5 then 2. The jockey
        // ids run backwards against the array order, so a reader using the index
        // where the screen draws the identity would name the wrong chocobos.
        var places = new[] { 3, 6, 4, 1, 2, 5 };
        for (var index = 0; index < places.Length; index++)
        {
            memory.SetRacer(
                index, jockeyId: 5 - index, name: $"Racer{index + 1}", rawTopSpeed: 34, rawStamina: 10);
            memory.SetRacerPosition(index, places[index]);
        }

        Equal(true, reader.TryReadResults(out var results), $"the results read; {reader.LastDiagnostic}");
        Equal(2, results.FirstWinningNumber, "the first big digit is the raw index plus one");
        Equal(5, results.SecondWinningNumber, "and so is the second");
        Equal(true, results.IsRevealed,
            "the names and pair go up with the screen itself, so a clear frame is revealed");
        Equal(6, results.Places.Count, "with every place");
        Equal(3, results.Places[0].Number,
            "first place prints the racer's own jockey id plus one, not its array index");

        var readout = new ChocoboSquareReadout();
        var spoken = string.Join(" ", readout.ObserveResults(results));
        Equal(true, spoken.Contains("Winning pair 2 and 5.", StringComparison.Ordinal),
            $"the big digits are reported as a pair; got {spoken}");
        Equal(true, spoken.Contains("first number 3, Racer4", StringComparison.Ordinal),
            $"and the finish order separately; got {spoken}");
        Equal(0, readout.ObserveResults(results).Count, "and only once");

        // A completely black screen has nothing on it to read, however settled the
        // state behind it is.
        memory.FadeState = ChocoboSquareStateReader.FullyBlackFade;
        Equal(true, reader.TryReadResults(out var black), "the black screen reads");
        Equal(false, black.IsRevealed, "but there is nothing on it yet");
        Equal(0, new ChocoboSquareReadout().ObserveResults(black).Count,
            "so nothing is announced over a black screen");

        // One step of the fade is enough: the screen is drawing the result now.
        memory.FadeState = ChocoboSquareStateReader.FullyBlackFade - 16;
        Equal(true, reader.TryReadResults(out var fading), "the fading screen reads");
        Equal(true, fading.IsRevealed, "and is showing the result as it comes up");

        // Nor before the order itself has settled.
        memory.FadeState = 0;
        memory.SetRacerPosition(2, 0);
        Equal(true, reader.TryReadResults(out var settling), "the settling screen reads");
        Equal(false, settling.IsRevealed, "but is not revealed yet");
        Equal(0, new ChocoboSquareReadout().ObserveResults(settling).Count,
            "so a half-updated order is never read out");

        // Two racers cannot share a place, and two cannot print the same digit.
        memory.SetRacerPosition(2, 1);
        Equal(true, reader.TryReadResults(out var duplicatePlace), "the duplicated frame reads");
        Equal(false, duplicatePlace.IsRevealed, "but two racers in first place is a torn frame");

        memory.SetRacerPosition(2, 4);
        memory.SetRacer(2, jockeyId: 5, name: "Racer3", rawTopSpeed: 34, rawStamina: 10);
        Equal(true, reader.TryReadResults(out var duplicateNumber), "the duplicated identity reads");
        Equal(false, duplicateNumber.IsRevealed, "and a repeated printed digit is a torn frame too");

        // A pair the digit array cannot draw is not a pair.
        memory.SetRacer(2, jockeyId: 3, name: "Racer3", rawTopSpeed: 34, rawStamina: 10);
        memory.WinningSecond = 6;
        Equal(false, reader.TryReadResults(out _), "an index outside the six-face array is refused");
        memory.WinningSecond = 1;
        Equal(false, reader.TryReadResults(out _), "and so is a pair of the same racer twice");
        memory.WinningSecond = 4;

        // A name with no terminator inside its own field is not a name, and the
        // jockey id that follows it must not be decoded as text.
        memory.FillRacerName(1, 0x41);
        Equal(false, reader.TryReadResults(out _), "an unterminated name is a failed read, not a blank one");
    }

    /// <summary>
    /// The prize cards, which are a much later and separate event from the result.
    /// FUN_00778C5D turns them over at frame 115; until then the grid is fifteen
    /// backs and there is nothing about them to say.
    /// </summary>
    private static void ThePrizeGridIsReadOnlyOnceTheCardsAreFaceUp()
    {
        var memory = new FakeChocoboMemory
        {
            Substate = 5,
            WinningFirst = 0,
            WinningSecond = 1,
            FadeState = 0
        };
        var reader = new ChocoboSquareStateReader(memory);
        for (var index = 0; index < ChocoboSquareStateReader.RacerCount; index++)
        {
            memory.SetRacer(index, jockeyId: index, name: $"Racer{index + 1}", rawTopSpeed: 34, rawStamina: 10);
            memory.SetRacerPosition(index, index + 1);
        }

        memory.SetGift(0, "Potion");
        memory.SetGift(1, "Ether");
        memory.SetGift(2, "Elixir");

        Equal(true, reader.TryReadResults(out var faceDown), $"the results read; {reader.LastDiagnostic}");
        Equal(0, faceDown.Prizes.Count, "a grid of face-down cards reveals no prize");

        var readout = new ChocoboSquareReadout();
        var opening = string.Join(" ", readout.ObserveResults(faceDown));
        Equal(false, opening.Contains("Potion", StringComparison.Ordinal),
            $"and no prize is named before its card is turned over; got {opening}");

        // The screen turns the whole grid over at once.
        memory.SetEveryPrizeCell(displayed: 0xFF);
        Equal(true, reader.TryReadResults(out var faceUp), $"the turned grid reads; {reader.LastDiagnostic}");
        Equal(15, faceUp.Prizes.Count, "every card is now face up");
        Equal(7, faceUp.Prizes.Count(prize => prize.Prize == "Potion"),
            "with the seven, five and three the native shuffle deals out");
        Equal(5, faceUp.Prizes.Count(prize => prize.Prize == "Ether"), "five of the second gift");
        Equal(3, faceUp.Prizes.Count(prize => prize.Prize == "Elixir"), "and three of the third");

        // Each card belongs to the pair its own cell stands for.
        var corner = faceUp.Prizes.Single(prize => prize is { Column: 4, Row: 2 });
        Equal(5, corner.First, "the last cell is the pair the native table gives it");
        Equal(6, corner.Second, "in both halves");

        var revealed = string.Join(" ", readout.ObserveResults(faceUp));
        Equal(true, revealed.Contains("Prize cards turned over.", StringComparison.Ordinal),
            $"the grid is announced when it turns; got {revealed}");
        Equal(true, revealed.Contains("1 and 2 shows", StringComparison.Ordinal),
            $"with the winning cell's own prize; got {revealed}");
        Equal(0, readout.ObserveResults(faceUp).Count, "and only once");

        // The screen fades back out to black at the end with every display flag still
        // set. Nothing under that overlay is on screen, so nothing is read from it -
        // which is also what stops the grid being announced again on the way out.
        memory.FadeState = ChocoboSquareStateReader.FullyBlackFade;
        Equal(true, reader.TryReadResults(out var leaving), $"the leaving screen reads; {reader.LastDiagnostic}");
        Equal(0, leaving.Prizes.Count, "a card under a black overlay is not a card anyone can see");
        Equal(0, new ChocoboSquareReadout().ObserveResults(leaving).Count,
            "so joining a black screen with old flags set says nothing");

        // A cell naming a gift that was never offered is a torn read.
        memory.FadeState = 0;
        memory.SetPrizeCell(0, 0, displayed: 0xFF, prizeIndex: 5, prizeId: 0);
        Equal(false, reader.TryReadResults(out _), "a prize slot outside the three offered is refused");
    }

    /// <summary>
    /// None of these reads is atomic, and every global behind them belongs to another
    /// screen the moment the substate moves on. A read that straddles the change has
    /// half its values from each, and is not a screen anyone was ever shown.
    /// </summary>
    private static void AScreenThatChangesMidReadIsNotDescribed()
    {
        var memory = new FakeChocoboMemory { Substate = 3, RankingVisible = 1, GraphicsMode = 0 };
        var reader = new ChocoboSquareStateReader(memory);
        for (var place = 0; place < ChocoboSquareStateReader.RacerCount; place++)
        {
            memory.SetRankingSprite(place, ChocoboSquareStateReader.DrawnRenderId, place * 24, 0);
        }

        Equal(true, reader.TryReadRace(out var settled), $"a settled race reads; {reader.LastDiagnostic}");
        Equal(true, settled.IsRacing, "and is running");

        // The race ends while its own sprites are being walked.
        memory.ChangeSubstateAfterReading(
            (uint)(ChocoboSquareStateReader.AddressRankingSprites +
                   (2 * ChocoboSquareStateReader.SpriteStride)),
            newSubstate: 5);
        Equal(false, reader.TryReadRace(out _), "a race that ends mid-read is not a race");

        memory.Substate = 1;
        memory.CursorColumn = 0;
        memory.CursorRow = 0;
        memory.PartyGil = 1000;
        memory.SetGift(0, "Potion");
        memory.SetGift(1, "Ether");
        memory.SetGift(2, "Elixir");
        memory.SetRacer(0, jockeyId: 0, name: "Racer1", rawTopSpeed: 34, rawStamina: 10);
        Equal(true, reader.TryReadBetting(out var menu), $"a settled menu reads; {reader.LastDiagnostic}");
        Equal(true, menu.IsOpen, "and is open");

        memory.ChangeSubstateAfterReading(
            (uint)ChocoboSquareStateReader.AddressGiftNameIndices, newSubstate: 2);
        Equal(false, reader.TryReadBetting(out _), "a menu that closes mid-read is not a menu");
    }

    /// <summary>
    /// The pairs the readout speaks come from the same table the renderer indexes.
    /// </summary>
    private static void TheCellPairsMatchTheRenderersOwnTable()
    {
        var memory = new FakeChocoboMemory { Substate = 1 };
        var reader = new ChocoboSquareStateReader(memory);

        for (var column = 0; column < ChocoboSquareStateReader.GridColumns; column++)
        {
            for (var row = 0; row < ChocoboSquareStateReader.GridRows; row++)
            {
                Equal(true, reader.TryReadCellPair(column, row, out var first, out var second),
                    $"the native table has an entry for ({column},{row})");
                Equal(
                    ChocoboSquareStateReader.DescribeCell((column * 10) + row),
                    (first, second),
                    $"and it agrees with the spoken pair for ({column},{row})");
            }
        }

        Equal(false, reader.TryReadCellPair(5, 0, out _, out _), "a cell off the grid has no pair");
        Equal(false, reader.TryReadCellPair(0, 3, out _, out _), "in either direction");
    }

    /// <summary>
    /// The high-resolution atlas, which is the mode the installed build actually
    /// runs in. Reading the V from where the normalised floats sit reports row zero
    /// for all six sprites, which then fails the uniqueness check and silences the
    /// whole strip.
    /// </summary>
    private static void TheHighResolutionStripIsDecodedFromItsOwnUvWords()
    {
        var memory = new FakeChocoboMemory { Substate = 3, RankingVisible = 1, GraphicsMode = 2 };
        var reader = new ChocoboSquareStateReader(memory);

        var order = new[] { 3, 0, 5, 1, 4, 2 };
        for (var place = 0; place < order.Length; place++)
        {
            memory.SetRankingSprite(
                place,
                ChocoboSquareStateReader.DrawnRenderId,
                u: order[place] % 3 * 48,
                v: order[place] / 3 * 48);
        }

        Equal(true, reader.TryReadRace(out var race), $"the race reads; {reader.LastDiagnostic}");
        Equal(6, race.Ranking.Count, "all six high-resolution sprites decode");
        Equal(
            "4, 1, 6, 2, 5, 3",
            string.Join(
                ", ",
                race.Ranking.OrderBy(sprite => sprite.Place).Select(sprite => sprite.Number)),
            "in the order the strip draws them");
    }

    /// <summary>
    /// The status key, which is the only way to hear any of these screens again.
    /// </summary>
    private static void TheStatusPathDescribesWhatIsOnEachScreen()
    {
        var betting = Betting(cells: [1, 21, -1], count: 2);
        var bettingStatus = ChocoboSquareReadout.DescribeBettingStatus(betting);
        Equal(true, bettingStatus.Contains("Cursor on 1 and 2", StringComparison.Ordinal),
            $"the cursor's own pair is available on demand; got {bettingStatus}");
        Equal(true, bettingStatus.Contains("1: 2 and 3", StringComparison.Ordinal),
            $"with the tickets already bought; got {bettingStatus}");
        Equal(true, bettingStatus.Contains("Racer1", StringComparison.Ordinal),
            $"the card that is open; got {bettingStatus}");
        Equal(true, bettingStatus.Contains("Gifts: Potion, Ether, Elixir", StringComparison.Ordinal),
            $"and the gifts on offer; got {bettingStatus}");
        Equal("The betting menu is not open.",
            ChocoboSquareReadout.DescribeBettingStatus(betting with { IsOpen = false }),
            "a screen that is not up says so");

        // The map is described relative to the square the camera is following, which
        // is the one drawn highlighted.
        var race = new ChocoboRaceState(
            IsRacing: true,
            IsPlayerRacing: false,
            TrackedRacer: 2,
            Ranking:
            [
                new ChocoboRankingSprite(1, 3), new ChocoboRankingSprite(2, 1),
                new ChocoboRankingSprite(3, 6), new ChocoboRankingSprite(4, 2),
                new ChocoboRankingSprite(5, 5), new ChocoboRankingSprite(6, 4)
            ],
            Minimap:
            [
                new ChocoboMinimapMarker(0, 300, 50, false),
                new ChocoboMinimapMarker(2, 280, 40, true),
                new ChocoboMinimapMarker(4, 281, 41, false)
            ]);
        var raceStatus = ChocoboSquareReadout.DescribeRaceStatus(race);
        Equal(true, raceStatus.Contains("Order: 3, 1, 6, 2, 5, 4.", StringComparison.Ordinal),
            $"the drawn order is available on demand; got {raceStatus}");
        Equal(true, raceStatus.Contains("relative to 3", StringComparison.Ordinal),
            $"measured against the square the camera follows; got {raceStatus}");
        Equal(true, raceStatus.Contains("1 20 right and 10 down", StringComparison.Ordinal),
            $"with each other square's own drawn offset; got {raceStatus}");
        Equal(true, raceStatus.Contains("5 alongside", StringComparison.Ordinal),
            $"and squares too close to separate are called that; got {raceStatus}");

        // Nothing about a track position, which the map does not show.
        foreach (var forbidden in new[] { "ahead", "behind", "lengths", "metres", "gaining" })
        {
            Equal(false, raceStatus.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"the minimap says nothing about track position: \"{forbidden}\"");
        }

        // Once the followed racer's own square stops being refreshed there is
        // nothing left to measure against, and none is invented.
        var finished = race with
        {
            Minimap = new[] { new ChocoboMinimapMarker(0, 300, 50, false) }
        };
        var finishedStatus = ChocoboSquareReadout.DescribeRaceStatus(finished);
        Equal(true, finishedStatus.Contains("1 square left on the map.", StringComparison.Ordinal),
            $"and the count is all that is left to say; got {finishedStatus}");

        Equal("No race is running.",
            ChocoboSquareReadout.DescribeRaceStatus(race with { IsRacing = false }),
            "a race that is not running says so");

        var results = new ChocoboResultsState(
            IsShowing: true,
            IsRevealed: true,
            FirstWinningNumber: 2,
            SecondWinningNumber: 5,
            Places: [new ChocoboResultPlace(1, 4, "Teioh")],
            Prizes: [new ChocoboPrizeCell(0, 0, 1, 2, 0, "Potion")]);
        var resultsStatus = ChocoboSquareReadout.DescribeResultsStatus(results);
        Equal(true, resultsStatus.Contains("Winning pair 2 and 5.", StringComparison.Ordinal),
            $"the result can be heard again; got {resultsStatus}");
        Equal(true, resultsStatus.Contains("Potion", StringComparison.Ordinal),
            $"along with the cards already turned over; got {resultsStatus}");
        Equal("The results screen has not come up yet.",
            ChocoboSquareReadout.DescribeResultsStatus(results with { IsRevealed = false }),
            "and a screen still behind its fade gives nothing away");
    }

    private static void TheGridCellEncodingMatchesTheRenderedPairs()
    {
        // The rendered grid is five columns by three rows, listing the fifteen
        // unordered pairs of six racers row by row: 1-2, 1-3, 1-4, 1-5, 1-6, then
        // 2-3 and so on. Cells are stored as column * 10 + row.
        Equal((1, 2), ChocoboSquareStateReader.DescribeCell(0), "column 0 row 0 is 1 and 2");
        Equal((1, 6), ChocoboSquareStateReader.DescribeCell(40), "column 4 row 0 is 1 and 6");
        Equal((2, 3), ChocoboSquareStateReader.DescribeCell(1), "column 0 row 1 is 2 and 3");
        Equal((3, 4), ChocoboSquareStateReader.DescribeCell(41), "column 4 row 1 is 3 and 4");
        Equal((3, 5), ChocoboSquareStateReader.DescribeCell(2), "column 0 row 2 is 3 and 5");
        Equal((5, 6), ChocoboSquareStateReader.DescribeCell(42), "column 4 row 2 is 5 and 6");

        // Every one of the fifteen cells is a distinct pair.
        var pairs = new HashSet<(int, int)>();
        for (var column = 0; column < ChocoboSquareStateReader.GridColumns; column++)
        {
            for (var row = 0; row < ChocoboSquareStateReader.GridRows; row++)
            {
                pairs.Add(ChocoboSquareStateReader.DescribeCell((column * 10) + row));
            }
        }

        Equal(15, pairs.Count, "the grid covers all fifteen pairs exactly once");
        Equal(false, ChocoboSquareStateReader.IsSelectableCell(-1), "an empty slot is not a cell");
        Equal(false, ChocoboSquareStateReader.IsSelectableCell(53), "nor is an impossible column");
    }

    private static ChocoboBettingState Betting(int[] cells, int count) =>
        new(IsOpen: true,
            CursorColumn: 0,
            CursorRow: 0,
            SelectedCells: cells,
            SelectedCount: count,
            RaceClass: 0,
            DisplayedGil: 1000,
            InspectedRacer: new ChocoboRacerCard(0, 1, 1, "Racer1", 5, 20),
            Gifts: ["Potion", "Ether", "Elixir"]);

    private sealed class FakeChocoboMemory : ILegacyAddressSpace
    {
        private const uint PartyGilPointer = 0x00800000;
        private const float TextureWidth = 128f;

        /// <summary>
        /// The renderer's own cell table at 0x0097B470, as it reads in the installed
        /// executable: for each of the fifteen cells, the two zero-based racers it
        /// stands for, then that cell's own column and row.
        /// </summary>
        private static readonly (int First, int Second, int Column, int Row)[] NativeCellTable =
        [
            (0, 1, 0, 0), (0, 2, 1, 0), (0, 3, 2, 0), (0, 4, 3, 0), (0, 5, 4, 0),
            (1, 2, 0, 1), (1, 3, 1, 1), (1, 4, 2, 1), (1, 5, 3, 1), (2, 3, 4, 1),
            (2, 4, 0, 2), (2, 5, 1, 2), (3, 4, 2, 2), (3, 5, 3, 2), (4, 5, 4, 2)
        ];

        private readonly byte[] prizeGrid =
            new byte[ChocoboSquareStateReader.GridColumns * ChocoboSquareStateReader.PrizeColumnStride];

        private readonly byte[] racers =
            new byte[ChocoboSquareStateReader.RacerCount * ChocoboSquareStateReader.RacerStride];
        private readonly byte[] rankingSprites =
            new byte[ChocoboSquareStateReader.RacerCount * ChocoboSquareStateReader.SpriteStride];
        private readonly byte[] minimap =
            new byte[ChocoboSquareStateReader.RacerCount * ChocoboSquareStateReader.SpriteStride];
        private readonly int[] selectedCells = [-1, -1, -1];
        private readonly int[] giftIndices = [0, 1, 2];
        private readonly Dictionary<int, int> prices = [];
        private readonly Dictionary<int, string> giftNames = [];

        public byte Module { get; set; } = ChocoboSquareStateReader.ChocoboModule;
        public int Substate { get; set; }
        public bool SubstateReadable { get; set; } = true;
        public int CursorColumn { get; set; }
        public int CursorRow { get; set; }
        public int SelectedCount { get; set; }
        public int RaceClass { get; set; }
        public int InspectedRacer { get; set; }
        public int PartyGil { get; set; } = 1000;
        public int RankingVisible { get; set; }
        public int GraphicsMode { get; set; }
        public int RaceMode { get; set; }
        public int WinningFirst { get; set; }
        public int WinningSecond { get; set; }
        public int ResultsFrame { get; set; }

        /// <summary>
        /// The black overlay. Zero is a clear screen; the results screen starts at
        /// 255, which is opaque black, and steps down from there.
        /// </summary>
        public int FadeState { get; set; }

        public void SetSelectedCell(int slot, int cell) => selectedCells[slot] = cell;

        public void SetPrice(int raceClass, int reservedCount, int price) =>
            prices[(raceClass * (ChocoboSquareStateReader.SelectedCellSlots + 1)) + reservedCount] = price;

        public void SetGift(int slot, string name)
        {
            giftIndices[slot] = slot;
            giftNames[slot] = name;
        }

        public void SetRacer(int index, int jockeyId, string name, int rawTopSpeed, int rawStamina)
        {
            var offset = index * ChocoboSquareStateReader.RacerStride;
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerTopSpeedOffset), (short)rawTopSpeed);
            BinaryPrimitives.WriteInt32LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerStaminaOffset), rawStamina);
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerNumberOffset), (short)jockeyId);
            WriteFf7Text(racers, offset + ChocoboSquareStateReader.RacerNameOffset, name);
        }

        /// <summary>
        /// Fills the name field to its last byte with no terminator, which is what a
        /// reader running past the field would have to cope with.
        /// </summary>
        public void FillRacerName(int index, byte value)
        {
            var offset = (index * ChocoboSquareStateReader.RacerStride) +
                         ChocoboSquareStateReader.RacerNameOffset;
            racers.AsSpan(offset, ChocoboSquareStateReader.RacerNameStorageBytes).Fill(value);
        }

        public void SetRacerPosition(int index, int position) =>
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan((index * ChocoboSquareStateReader.RacerStride) +
                              ChocoboSquareStateReader.RacerPositionOffset),
                (short)position);

        public void SetRacerFinished(int index, int finished) =>
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan((index * ChocoboSquareStateReader.RacerStride) +
                              ChocoboSquareStateReader.RacerFinishedOffset),
                (short)finished);

        /// <summary>
        /// A drawn sprite record as the native renderer leaves it: the pixel UV as two
        /// unsigned shorts, and then the same coordinates normalised as floats. The
        /// floats are written too, because a reader that took its V from where they
        /// sit would read the low half of a mantissa and get zero for every row.
        /// </summary>
        public void SetRankingSprite(int place, int renderId, int u, int v)
        {
            var offset = place * ChocoboSquareStateReader.SpriteStride;
            BinaryPrimitives.WriteInt32LittleEndian(
                rankingSprites.AsSpan(offset + ChocoboSquareStateReader.SpriteRenderIdOffset), renderId);
            BinaryPrimitives.WriteUInt16LittleEndian(
                rankingSprites.AsSpan(offset + ChocoboSquareStateReader.SpriteUOffset), (ushort)u);
            BinaryPrimitives.WriteUInt16LittleEndian(
                rankingSprites.AsSpan(offset + ChocoboSquareStateReader.SpriteVOffset), (ushort)v);
            BinaryPrimitives.WriteSingleLittleEndian(
                rankingSprites.AsSpan(offset + ChocoboSquareStateReader.SpriteNormalisedUOffset),
                u / TextureWidth);
            BinaryPrimitives.WriteSingleLittleEndian(
                rankingSprites.AsSpan(offset + ChocoboSquareStateReader.SpriteNormalisedVOffset),
                v / TextureWidth);
        }

        public void SetPrizeCell(int column, int row, int displayed, int prizeIndex, int prizeId)
        {
            var offset = (column * ChocoboSquareStateReader.PrizeColumnStride) +
                         (row * ChocoboSquareStateReader.PrizeCellStride);
            prizeGrid[offset + ChocoboSquareStateReader.PrizeDisplayOffset] = (byte)displayed;
            prizeGrid[offset + ChocoboSquareStateReader.PrizeIndexOffset] = (byte)prizeIndex;
            prizeGrid[offset + ChocoboSquareStateReader.PrizeIdOffset] = (byte)prizeId;
        }

        public void SetEveryPrizeCell(int displayed)
        {
            for (var column = 0; column < ChocoboSquareStateReader.GridColumns; column++)
            {
                for (var row = 0; row < ChocoboSquareStateReader.GridRows; row++)
                {
                    // The bag is seven of the first gift, five of the second and
                    // three of the third, which is what FUN_007772AE deals out.
                    var cell = (row * ChocoboSquareStateReader.GridColumns) + column;
                    SetPrizeCell(column, row, displayed, cell < 7 ? 0 : cell < 12 ? 1 : 2, cell);
                }
            }
        }

        public void SetMinimap(int index, int x, int y)
        {
            var offset = index * ChocoboSquareStateReader.SpriteStride;
            BinaryPrimitives.WriteUInt16LittleEndian(
                minimap.AsSpan(offset + ChocoboSquareStateReader.MinimapXOffset), (ushort)x);
            BinaryPrimitives.WriteUInt16LittleEndian(
                minimap.AsSpan(offset + ChocoboSquareStateReader.MinimapYOffset), (ushort)y);
        }

        private uint changeSubstateAfter;
        private int changedSubstate;
        private uint changeRaceModeAfter;
        private int changedRaceMode;
        private uint changeFadeAfter;
        private int changedFade;

        /// <summary>
        /// Moves the screen on the moment a particular global has been read, which is
        /// what the native refresh does to a reader part way through its own pass.
        /// </summary>
        public void ChangeSubstateAfterReading(uint address, int newSubstate)
        {
            changeSubstateAfter = address;
            changedSubstate = newSubstate;
        }

        /// <summary>The player stops being a jockey mid-read.</summary>
        public void ChangeRaceModeAfterReading(uint address, int newRaceMode)
        {
            changeRaceModeAfter = address;
            changedRaceMode = newRaceMode;
        }

        /// <summary>The HUD goes behind black mid-read.</summary>
        public void ChangeFadeAfterReading(uint address, int newFade)
        {
            changeFadeAfter = address;
            changedFade = newFade;
        }

        /// <summary>
        /// The player's own record at index zero, as the native racer structure holds it:
        /// a signed short speed at +0x04, the automatic flag at +0x60, the stamina pair of
        /// ints at +0x68 and +0x6C, and the animation at +0x80.
        /// </summary>
        public void SetPlayerControl(
            int speed,
            int automatic,
            int stamina,
            int staminaMaximum,
            int animation = 0)
        {
            var offset = ChocoboSquareStateReader.PlayerRacerIndex * ChocoboSquareStateReader.RacerStride;
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerSpeedOffset), (short)speed);
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerAutomaticOffset), (short)automatic);
            BinaryPrimitives.WriteInt32LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerStaminaOffset), stamina);
            BinaryPrimitives.WriteInt32LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerStaminaMaximumOffset), staminaMaximum);
            BinaryPrimitives.WriteInt16LittleEndian(
                racers.AsSpan(offset + ChocoboSquareStateReader.RacerAnimationOffset), (short)animation);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            destination.Clear();
            if (changeSubstateAfter != 0 &&
                virtualAddress <= changeSubstateAfter &&
                virtualAddress + (uint)destination.Length > changeSubstateAfter)
            {
                changeSubstateAfter = 0;
                Substate = changedSubstate;
            }

            if (changeRaceModeAfter != 0 &&
                virtualAddress <= changeRaceModeAfter &&
                virtualAddress + (uint)destination.Length > changeRaceModeAfter)
            {
                changeRaceModeAfter = 0;
                RaceMode = changedRaceMode;
            }

            if (changeFadeAfter != 0 &&
                virtualAddress <= changeFadeAfter &&
                virtualAddress + (uint)destination.Length > changeFadeAfter)
            {
                changeFadeAfter = 0;
                FadeState = changedFade;
            }

            switch (virtualAddress)
            {
                case (uint)FieldPositionReader.AddressCurrentModule when destination.Length == 1:
                    destination[0] = Module;
                    return true;
                case (uint)ChocoboSquareStateReader.AddressSubstate when destination.Length == 4:
                    if (!SubstateReadable)
                    {
                        return false;
                    }

                    return Write(destination, Substate);
                case (uint)ChocoboSquareStateReader.AddressCursorColumn when destination.Length == 4:
                    return Write(destination, CursorColumn);
                case (uint)ChocoboSquareStateReader.AddressCursorRow when destination.Length == 4:
                    return Write(destination, CursorRow);
                case (uint)ChocoboSquareStateReader.AddressSelectedCount when destination.Length == 4:
                    return Write(destination, SelectedCount);
                case (uint)ChocoboSquareStateReader.AddressRaceClass when destination.Length == 4:
                    return Write(destination, RaceClass);
                case (uint)ChocoboSquareStateReader.AddressInspectedRacer when destination.Length == 4:
                    return Write(destination, InspectedRacer);
                case (uint)ChocoboSquareStateReader.AddressGil when destination.Length == 4:
                    return Write(destination, unchecked((int)PartyGilPointer));
                case PartyGilPointer when destination.Length == 4:
                    return Write(destination, PartyGil);
                case (uint)ChocoboSquareStateReader.AddressRankingVisible when destination.Length == 4:
                    return Write(destination, RankingVisible);
                case (uint)ChocoboSquareStateReader.AddressGraphicsMode when destination.Length == 4:
                    return Write(destination, GraphicsMode);
                case (uint)ChocoboSquareStateReader.AddressRaceMode when destination.Length == 4:
                    return Write(destination, RaceMode);
                case (uint)ChocoboSquareStateReader.AddressWinningPairFirst when destination.Length == 4:
                    return Write(destination, WinningFirst);
                case (uint)ChocoboSquareStateReader.AddressWinningPairSecond when destination.Length == 4:
                    return Write(destination, WinningSecond);
                case (uint)ChocoboSquareStateReader.AddressResultsFrame when destination.Length == 4:
                    return Write(destination, ResultsFrame);
                case (uint)ChocoboSquareStateReader.AddressFadeState when destination.Length == 4:
                    return Write(destination, FadeState);
            }

            if (TryReadRegion(
                    virtualAddress, destination, (uint)ChocoboSquareStateReader.AddressPrizeGrid, prizeGrid))
            {
                return true;
            }

            var cellTableBase = (uint)ChocoboSquareStateReader.AddressCellPairTable;
            if (destination.Length == 2 &&
                virtualAddress >= cellTableBase &&
                virtualAddress < cellTableBase +
                    (uint)(NativeCellTable.Length * ChocoboSquareStateReader.CellPairStride))
            {
                var offset = virtualAddress - cellTableBase;
                var entry = NativeCellTable[offset / ChocoboSquareStateReader.CellPairStride];
                var field = (offset % ChocoboSquareStateReader.CellPairStride) / 2;
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)(field switch
                {
                    0 => entry.First,
                    1 => entry.Second,
                    2 => entry.Column,
                    _ => entry.Row
                }));
                return true;
            }

            if (TryReadIndexed(
                    virtualAddress, destination,
                    (uint)ChocoboSquareStateReader.AddressSelectedCells, selectedCells) ||
                TryReadIndexed(
                    virtualAddress, destination,
                    (uint)ChocoboSquareStateReader.AddressGiftNameIndices, giftIndices))
            {
                return true;
            }

            if (TryReadRegion(
                    virtualAddress, destination, (uint)ChocoboSquareStateReader.AddressRacerArray, racers) ||
                TryReadRegion(
                    virtualAddress, destination,
                    (uint)ChocoboSquareStateReader.AddressRankingSprites, rankingSprites) ||
                TryReadRegion(
                    virtualAddress, destination,
                    (uint)ChocoboSquareStateReader.AddressMinimapSquares, minimap))
            {
                return true;
            }

            // The price table, indexed by class and reserved count.
            var priceBase = (uint)ChocoboSquareStateReader.AddressPriceTable;
            if (virtualAddress >= priceBase && destination.Length == 4)
            {
                var index = (int)((virtualAddress - priceBase) / 4);
                return Write(destination, prices.GetValueOrDefault(index));
            }

            // The sixteen-byte gift string table.
            var giftBase = (uint)ChocoboSquareStateReader.AddressGiftNameTable;
            if (virtualAddress >= giftBase &&
                virtualAddress < giftBase + (ChocoboSquareStateReader.GiftCount *
                                             ChocoboSquareStateReader.GiftNameStride))
            {
                var slot = (int)((virtualAddress - giftBase) / ChocoboSquareStateReader.GiftNameStride);
                var within = (int)((virtualAddress - giftBase) % ChocoboSquareStateReader.GiftNameStride);
                Span<byte> text = stackalloc byte[ChocoboSquareStateReader.GiftNameStride];
                WriteFf7Text(text, 0, giftNames.GetValueOrDefault(slot, string.Empty));
                var available = ChocoboSquareStateReader.GiftNameStride - within;
                text.Slice(within, Math.Min(destination.Length, available)).CopyTo(destination);
                return true;
            }

            return false;
        }

        private static bool TryReadIndexed(uint address, Span<byte> destination, uint regionBase, int[] values)
        {
            if (destination.Length != 4 ||
                address < regionBase ||
                address >= regionBase + (uint)(values.Length * 4))
            {
                return false;
            }

            BinaryPrimitives.WriteInt32LittleEndian(destination, values[(int)((address - regionBase) / 4)]);
            return true;
        }

        private static bool TryReadRegion(
            uint address, Span<byte> destination, uint regionBase, byte[] region)
        {
            if (address < regionBase || address + (uint)destination.Length > regionBase + (uint)region.Length)
            {
                return false;
            }

            region.AsSpan((int)(address - regionBase), destination.Length).CopyTo(destination);
            return true;
        }

        private static bool Write(Span<byte> destination, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            return true;
        }

        /// <summary>
        /// Writes an FF7-encoded name, terminated by 0xFF. The encoding is derived
        /// from the production decoder rather than duplicated, so the test cannot
        /// pass against a table the runtime does not actually use.
        /// </summary>
        private static void WriteFf7Text(Span<byte> destination, int offset, string text)
        {
            var written = 0;
            foreach (var character in text)
            {
                for (var code = 0; code < 0xFF; code++)
                {
                    if (Ff7TextEncoding.TryReadNormal(
                            (byte)code, Ff7TextEncodingProfile.Western, out var decoded) &&
                        decoded == character)
                    {
                        destination[offset + written] = (byte)code;
                        written++;
                        break;
                    }
                }
            }

            destination[offset + written] = 0xFF;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Chocobo Square - {message}: expected {expected}, actual {actual}.");
    }
}
