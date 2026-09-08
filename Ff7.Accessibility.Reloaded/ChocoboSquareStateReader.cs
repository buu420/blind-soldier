using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Which of the chocobo racing screens is up.</summary>
public enum ChocoboSquarePhase
{
    None,

    /// <summary>Substate 1: the betting menu, with its grid, card and gifts.</summary>
    Betting,

    /// <summary>Substate 3: the race itself, with the ranking strip and minimap.</summary>
    Race,

    /// <summary>Substate 5: the results screen.</summary>
    Results
}

/// <summary>One racer as the game draws it.</summary>
/// <param name="Number">
/// The digit the betting card puts up. FUN_00776452 indexes its number sprite by the
/// inspected index 0x00E71624, so the card's digit is that index plus one and does not
/// depend on the model's own jockey id.
/// </param>
/// <param name="JockeyNumber">
/// The digit the results screen puts up. FUN_00778C5D indexes its digit sprite by the
/// racer's own wJockeyId at +0x92, which is zero based, so the printed digit is that
/// plus one. The two can differ, and using one where the other is drawn would name a
/// different chocobo than the screen does.
/// </param>
public readonly record struct ChocoboRacerCard(
    int Index,
    int Number,
    int JockeyNumber,
    string Name,
    int TopSpeed,
    int Stamina);

/// <summary>The betting menu exactly as it is drawn.</summary>
/// <param name="SelectedCells">
/// The three ticket slots, each holding column * 10 + row, or -1 for an empty slot.
/// </param>
public readonly record struct ChocoboBettingState(
    bool IsOpen,
    int CursorColumn,
    int CursorRow,
    IReadOnlyList<int> SelectedCells,
    int SelectedCount,
    int RaceClass,
    int DisplayedGil,
    ChocoboRacerCard InspectedRacer,
    IReadOnlyList<string> Gifts);

/// <summary>One numbered face sprite as the race HUD has drawn it.</summary>
public readonly record struct ChocoboRankingSprite(int Place, int Number);

/// <summary>One minimap square as the race HUD has drawn it.</summary>
public readonly record struct ChocoboMinimapMarker(int Index, int X, int Y, bool IsInspected);

/// <param name="TrackedRacer">
/// The racer the camera is following, 0x00E71624. FUN_00777DF4 highlights the cell it
/// owns, and 0x0076E2B0 marks its minimap square, so the other squares are described
/// relative to that one.
/// </param>
public readonly record struct ChocoboRaceState(
    bool IsRacing,
    bool IsPlayerRacing,
    int TrackedRacer,
    IReadOnlyList<ChocoboRankingSprite> Ranking,
    IReadOnlyList<ChocoboMinimapMarker> Minimap,
    ChocoboRaceControlState Control = default,
    bool IsObscured = false);

/// <summary>One finishing place as the results screen lists it.</summary>
public readonly record struct ChocoboResultPlace(int Place, int Number, string Name);

/// <summary>One grid card whose prize the screen has turned face up.</summary>
/// <param name="PrizeIndex">
/// Which of the three offered gifts this card holds, 0 to 2. FUN_007772AE fills the
/// fifteen cells from a shuffled bag of seven, five and three, writing the chosen
/// gift's slot to +0x07 and its own name-table index to +0x06, and FUN_00777DF4 draws
/// the prize picture at a row and scale taken from +0x07. The card therefore shows one
/// of the three gifts the menu already offered, which is why it can be named at all.
/// </param>
public readonly record struct ChocoboPrizeCell(
    int Column,
    int Row,
    int First,
    int Second,
    int PrizeIndex,
    string Prize);

/// <param name="IsRevealed">
/// Whether the finishing order and winning pair are legible. FUN_00778C5D draws both
/// from its very first frame, so the only thing between them and the player is the
/// fade overlay.
/// </param>
/// <param name="Prizes">
/// The grid cards turned face up, which is a separate and much later event than the
/// result itself.
/// </param>
public readonly record struct ChocoboResultsState(
    bool IsShowing,
    bool IsRevealed,
    int FirstWinningNumber,
    int SecondWinningNumber,
    IReadOnlyList<ChocoboResultPlace> Places,
    IReadOnlyList<ChocoboPrizeCell> Prizes);

/// <summary>
/// Reads the Chocobo Square's own betting, race and results screens.
///
/// Everything here is on the screen that is currently open. The phase comes from the
/// module substate at 0x00E3B740, which FUN_0077C462 sets to 1 for the betting menu
/// (0x0077C4C2) and 3 for the race (0x0077C601), with 5 for the results.
///
/// Betting menu, FUN_00776452 and its reset FUN_00776375:
///  - the grid cursor is column 0x00E737B8 in 0..4 and row 0x00E737B4 in 0..2.
///  - the three ticket slots are the int array at 0x00E73798. FUN_00776375 seeds them
///    to -1 and FUN_00776452 clears one back to -1 at 0x0077664D, so -1 is empty.
///  - 0x00E737AC is a *cyclic* insertion index: 0x00776600 and 0x00776691 wrap it at
///    2. It is not the count. The count is 0x00E73C08, cleared at 0x007763C3. A
///    fourth choice therefore replaces the oldest slot rather than being refused.
///  - the inspected racer is 0x00E71624, which the menu cycles 0..5 (0x0077654B wraps
///    it back to 5). Its record is at 0x00E71158 + index * 0xA4, and the card draws the
///    name at +0x88, TOP SPEED as the signed short at +0x58 divided by 34, and STAMINA
///    as the int at +0x68 divided by 10. The card's own number sprite is picked by the
///    inspected index rather than by the record, so the digit on the card is that index
///    plus one. Nothing else in the record is drawn, so nothing else is read.
///  - the displayed balance is the party's gil less the tickets already reserved:
///    the price table at 0x0097A440 indexed by class 0x00E71130 and count.
///  - the three offered gifts are name indices at 0x00E73340, +4 and +8 into the
///    sixteen-byte string table at 0x0097C9D0.
///
/// Race, FUN_0076E2B0: the numbered face sprites are six records of stride 0x48 at
/// 0x00E71838, drawn only while 0x00E710F8 is non-zero, and a record counts as drawn
/// when its render id at +0 is 0x10. Its pixel UV shorts at +0x08 and +0x0A identify
/// which number is shown, decoded against the graphics mode at 0x00E3BA6C. Reading the
/// drawn sprites rather than the engine's newer internal ranks is what keeps the
/// spoken order identical to the strip on screen, which only refreshes every
/// sixteen frames. The minimap squares are six records of stride 0x48 at 0x00E3B758
/// with the drawn position at +0x14 and +0x16, refreshed only for racers whose
/// finished flag at +0x7E is zero.
///
/// Results, FUN_00778C5D: the six names and their digits, and the two big winning
/// digits, are drawn on every frame the screen owns, starting with its first. The
/// frame counter at 0x00E737B0 gates two later events only - turning the prize cards
/// face up at 115, and allowing the player to continue after 160 - so it is not what
/// says whether the result is legible. The fade overlay at 0x00E710EC is: FUN_0077946A
/// paints it straight over everything, starting at full black and stepping down.
///
/// The player's own stamina gauge is drawn only when 0x00E71128 is non-zero, which is
/// a race the player is riding. During a betting race it is not on screen and is not
/// read, so no rival's stamina is ever exposed.
/// </summary>
public sealed class ChocoboSquareStateReader
{
    /// <summary>
    /// The chocobo race's own module. FUN_0063C17F's minigame switch writes the
    /// module selector 0x00CBF9DC per game type, and the native completion strings
    /// beside each case name them: 0 to 6 are Highway, Chocobo, Snow Board, Condor
    /// War, Sub Marine, Jet and one more, giving modules 6, 7, 8, 9, 10, 11 and 14.
    /// Module 6 already matches <c>HighwayStateReader</c>, 9 matches the Fort Condor
    /// probe and 11 matches the shooting coaster, so 7 is this one.
    /// </summary>
    public const byte ChocoboModule = 7;

    public const int AddressSubstate = 0x00E3B740;
    public const int AddressFadeState = 0x00E710EC;

    public const int AddressCursorColumn = 0x00E737B8;
    public const int AddressCursorRow = 0x00E737B4;
    public const int AddressSelectedCells = 0x00E73798;
    public const int AddressInsertionIndex = 0x00E737AC;
    public const int AddressSelectedCount = 0x00E73C08;
    public const int AddressRaceClass = 0x00E71130;
    public const int AddressGil = 0x00E72EF8;
    public const int AddressPriceTable = 0x0097A440;

    public const int AddressInspectedRacer = 0x00E71624;
    public const int AddressRacerArray = 0x00E71158;
    public const int RacerStride = 0xA4;
    public const int RacerCount = 6;
    public const int RacerTopSpeedOffset = 0x58;
    public const int RacerStaminaOffset = 0x68;
    public const int RacerPositionOffset = 0x70;
    public const int RacerFinishedOffset = 0x7E;
    public const int RacerNameOffset = 0x88;
    public const int RacerNumberOffset = 0x92;

    /// <summary>
    /// The player is always record zero. Root's read-only decompilation of the installed
    /// binary verified each of these against the HUD and movement functions:
    /// FUN_0076E2B0 chooses the automatic or manual sprite on +0x60, the stamina gauge's
    /// height is 208 - current * 68 / maximum from +0x68 and +0x6C, and FUN_00773DD8
    /// translates the chocobo by +0x04 and sets the turbo animation at +0x80 when a dash
    /// is accepted.
    /// </summary>
    public const int PlayerRacerIndex = 0;

    /// <summary>Speed, +0x04 - 0x00E7115C. What moves the chocobo, not what a key asked for.</summary>
    public const int RacerSpeedOffset = 0x04;

    /// <summary>
    /// Automatic when non-zero, manual when zero: +0x60, 0x00E711B8. This is <b>not</b>
    /// <see cref="AddressRaceMode"/>, which only says whether the player is riding at all.
    /// </summary>
    public const int RacerAutomaticOffset = 0x60;

    /// <summary>Maximum stamina, +0x6C - 0x00E711C4, the denominator of the drawn gauge.</summary>
    public const int RacerStaminaMaximumOffset = 0x6C;

    /// <summary>The animation the record is playing, +0x80 - 0x00E711D8.</summary>
    public const int RacerAnimationOffset = 0x80;

    /// <summary>
    /// The turbo animation FUN_00773DD8 selects once a dash has been accepted and stamina
    /// is going into it. A pressed button is only a request; stamina, docility and the
    /// track segment can all refuse it, so the animation is what proves a dash happened.
    /// </summary>
    public const int TurboAnimation = 1;

    /// <summary>
    /// The name field's own storage, eight bytes at +0x88. t_chocobo_Racer puts
    /// wJockeyId at +0x92, so reading further would decode that identity as text.
    /// </summary>
    public const int RacerNameStorageBytes = 8;

    /// <summary>The native card divides the raw values by these before drawing.</summary>
    public const int TopSpeedDivisor = 34;
    public const int StaminaDivisor = 10;

    public const int AddressGiftNameIndices = 0x00E73340;
    public const int AddressGiftNameTable = 0x0097C9D0;
    public const int GiftNameStride = 16;
    public const int GiftCount = 3;

    public const int AddressRankingSprites = 0x00E71838;
    public const int AddressRankingVisible = 0x00E710F8;
    public const int AddressGraphicsMode = 0x00E3BA6C;
    public const int AddressMinimapSquares = 0x00E3B758;
    public const int AddressRaceMode = 0x00E71128;
    public const int SpriteStride = 0x48;
    public const int SpriteRenderIdOffset = 0x00;
    /// <summary>
    /// The sprite record's pixel UV, two unsigned shorts. 0x0076E590 writes the V word
    /// at 0x00E71842, which is the first record's base plus 0x0A. The floats that
    /// follow are the same coordinates normalised - 0x0076E59F writes 0.375 at
    /// 0x00E71848, the base plus 0x10 - so reading +0x10 as a short yields the low half
    /// of a mantissa, which is zero for every atlas row the strip actually uses.
    /// </summary>
    public const int SpriteUOffset = 0x08;

    public const int SpriteVOffset = 0x0A;
    public const int SpriteNormalisedUOffset = 0x0C;
    public const int SpriteNormalisedVOffset = 0x10;
    public const int DrawnRenderId = 0x10;
    public const int MinimapXOffset = 0x14;
    public const int MinimapYOffset = 0x16;
    public const int MinimapWidthOffset = 0x28;

    /// <summary>
    /// The two big digits along the bottom of the results screen. FUN_00778C5D draws
    /// them as sprite records 0x00E3BAB0 + value * 0x48 out of an array whose faces
    /// read one through six, so both globals are zero-based racer indices and the
    /// printed digit is the value plus one.
    /// </summary>
    public const int AddressWinningPairFirst = 0x00E72EF4;

    public const int AddressWinningPairSecond = 0x00E737BC;

    /// <summary>
    /// The results screen's own frame counter, 0x00E737B0. FUN_00778C5D steps it and
    /// hangs its later events on it: at 115 it turns every prize card face up, and
    /// after 160 the player may continue. The finishing order and the winning pair are
    /// not among them - both are drawn unconditionally from the screen's first frame -
    /// so this counter gates the prize grid only.
    /// </summary>
    public const int AddressResultsFrame = 0x00E737B0;

    public const int ResultsPrizeRevealFrame = 0x73;
    public const int ResultsContinueFrame = 0xA0;

    /// <summary>
    /// The fifteen prize cards, t_chocobo_Ranking of eight bytes each in a [5][3]
    /// array. FUN_00777DF4 walks it as base + row * 8 + column * 0x18, so a cell sits
    /// at base + (column * 3 + row) * 8.
    /// </summary>
    public const int AddressPrizeGrid = 0x00E71068;

    public const int PrizeCellStride = 8;
    public const int PrizeColumnStride = 0x18;
    public const int PrizeCounterOffset = 0x02;

    /// <summary>
    /// bDisplayPrize. FUN_00777DF4 fills and draws the prize sprite only where this is
    /// non-zero, and FUN_00778C5D sets it across the whole grid at results frame 115.
    /// </summary>
    public const int PrizeDisplayOffset = 0x03;

    public const int PrizeSideOffset = 0x04;
    public const int PrizeFlipOffset = 0x05;
    public const int PrizeIdOffset = 0x06;
    public const int PrizeIndexOffset = 0x07;

    /// <summary>
    /// The grid's own cell table, fifteen records of four shorts: the two zero-based
    /// racers a cell stands for, then that cell's column and row. FUN_00777DF4 reads it
    /// at 0x0097B470 indexed (row * 5 + column) * 8.
    /// </summary>
    public const int AddressCellPairTable = 0x0097B470;

    public const int CellPairStride = 8;

    /// <summary>
    /// The black overlay FUN_0077946A draws over everything, 0x00E710EC. It is written
    /// straight into the overlay's own RGB, so zero is a clear screen and 255 is a
    /// black one; the results screen starts at 255 and steps down, then climbs back
    /// past 255 to leave. Nothing drawn underneath is legible at full black.
    /// </summary>
    public const int FullyBlackFade = 0xFF;

    public const int SelectedCellSlots = 3;
    public const int GridColumns = 5;
    public const int GridRows = 3;

    private readonly ILegacyAddressSpace memory;

    public ChocoboSquareStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    /// <summary>
    /// Which screen is up. The substate is the phase the native refresh dispatches
    /// on, and these globals belong to no other minigame, so it is the gate.
    /// </summary>
    public bool TryReadPhase(out ChocoboSquarePhase phase)
    {
        phase = ChocoboSquarePhase.None;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module))
        {
            LastDiagnostic = "module selector unreadable";
            return false;
        }

        // Outside the race's own module every one of these globals is whatever the
        // last owner of that memory left behind.
        if (module != ChocoboModule)
        {
            LastDiagnostic = $"module {module} is not chocobo racing";
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressSubstate, out var substate))
        {
            LastDiagnostic = "chocobo substate unreadable";
            return false;
        }

        phase = substate switch
        {
            1 => ChocoboSquarePhase.Betting,
            3 => ChocoboSquarePhase.Race,
            5 => ChocoboSquarePhase.Results,
            _ => ChocoboSquarePhase.None
        };
        LastDiagnostic = $"chocobo substate={substate}, phase={phase}";
        return true;
    }

    /// <summary>
    /// Whether the screen that was being read is still the one that is up. None of
    /// these reads is atomic, and every global behind them belongs to a different
    /// screen the moment the substate moves on, so a read that straddles the change
    /// has half its values from each. Checked at the end rather than the start,
    /// because it is the change during the read that matters.
    /// </summary>
    private bool PhaseStillHolds(ChocoboSquarePhase expected)
    {
        if (TryReadPhase(out var phase) && phase == expected)
        {
            return true;
        }

        LastDiagnostic = $"chocobo screen changed while it was being read; wanted {expected}";
        return false;
    }

    public bool TryReadBetting(out ChocoboBettingState state)
    {
        state = default;
        if (!TryReadPhase(out var phase))
        {
            return false;
        }

        if (phase != ChocoboSquarePhase.Betting)
        {
            state = new ChocoboBettingState(
                false, 0, 0, Array.Empty<int>(), 0, 0, 0, default, Array.Empty<string>());
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressCursorColumn, out var column) ||
            !memory.TryReadInt32((uint)AddressCursorRow, out var row) ||
            !memory.TryReadInt32((uint)AddressSelectedCount, out var count) ||
            !memory.TryReadInt32((uint)AddressRaceClass, out var raceClass) ||
            !memory.TryReadInt32((uint)AddressInspectedRacer, out var inspected))
        {
            LastDiagnostic = "chocobo betting menu unreadable";
            return false;
        }

        if (column is < 0 or >= GridColumns ||
            row is < 0 or >= GridRows ||
            count is < 0 or > SelectedCellSlots ||
            raceClass < 0 ||
            inspected is < 0 or >= RacerCount)
        {
            LastDiagnostic =
                $"chocobo betting values out of range: cursor=({column},{row}), count={count}, " +
                $"class={raceClass}, inspected={inspected}";
            return false;
        }

        var cells = new int[SelectedCellSlots];
        for (var slot = 0; slot < SelectedCellSlots; slot++)
        {
            if (!memory.TryReadInt32((uint)(AddressSelectedCells + (slot * sizeof(int))), out var cell))
            {
                LastDiagnostic = $"chocobo ticket slot {slot} unreadable";
                return false;
            }

            cells[slot] = IsSelectableCell(cell) ? cell : -1;
        }

        if (!TryReadRacerCard(inspected, out var card))
        {
            return false;
        }

        if (!TryReadDisplayedGil(raceClass, count, out var gil) ||
            !TryReadGifts(out var gifts))
        {
            return false;
        }

        if (!PhaseStillHolds(ChocoboSquarePhase.Betting))
        {
            return false;
        }

        state = new ChocoboBettingState(
            IsOpen: true,
            CursorColumn: column,
            CursorRow: row,
            SelectedCells: cells,
            SelectedCount: count,
            RaceClass: raceClass,
            DisplayedGil: gil,
            InspectedRacer: card,
            Gifts: gifts);
        LastDiagnostic =
            $"chocobo betting cursor=({column},{row}), count={count}, class={raceClass}, " +
            $"inspected={inspected}, gil={gil}";
        return true;
    }

    /// <summary>
    /// The player's own record, when they are riding rather than betting.
    ///
    /// <para>Every offset here was verified by read-only decompilation of the installed
    /// binary: FUN_0076E2B0 picks the automatic or manual HUD sprite on +0x60, the stamina
    /// gauge is drawn from +0x68 over +0x6C, and FUN_00773DD8 translates the chocobo by
    /// +0x04 and selects the turbo animation at +0x80 once a dash has been accepted.</para>
    ///
    /// <para>The place is matched from the same drawn ranking strip the order comes from,
    /// on the jockey number the record carries, so it is the place actually on screen. When
    /// the strip is mid-refresh there is no place rather than a guessed one.</para>
    /// </summary>
    private bool TryReadPlayerControl(
        int raceMode,
        int started,
        IReadOnlyList<ChocoboRankingSprite> ranking,
        out ChocoboRaceControlState control)
    {
        control = default;
        if (raceMode == 0)
        {
            // A betting race: six other jockeys, and no control state of the player's own.
            return true;
        }

        // A fade covering the screen means a sighted player can see none of this either.
        if (!memory.TryReadInt32((uint)AddressFadeState, out var fade))
        {
            LastDiagnostic = "chocobo fade state unreadable";
            return false;
        }

        if (fade != 0)
        {
            return true;
        }

        // Every one of these is signed in the native record. The speed especially: the
        // upstream field is a short, and read unsigned a -1 becomes 65535 - a chocobo
        // travelling twenty times faster than any of them can, announced as confidently as
        // anything else here.
        var record = (uint)(AddressRacerArray + (PlayerRacerIndex * RacerStride));
        if (!memory.TryReadInt16(record + RacerSpeedOffset, out var speed) ||
            !memory.TryReadInt16(record + RacerAutomaticOffset, out var automatic) ||
            !memory.TryReadInt32(record + RacerStaminaOffset, out var stamina) ||
            !memory.TryReadInt32(record + RacerStaminaMaximumOffset, out var staminaMaximum) ||
            !memory.TryReadInt16(record + RacerAnimationOffset, out var animation) ||
            !memory.TryReadInt16(record + RacerFinishedOffset, out var finished) ||
            !memory.TryReadInt16(record + RacerNumberOffset, out var jockey))
        {
            LastDiagnostic = "chocobo player record unreadable";
            return false;
        }

        // A payload that cannot be true is a torn read however cleanly it arrived. A zero
        // maximum is the one exception: the gauge simply cannot be worked out then, which
        // the readout says in as many words rather than calling it empty.
        if (speed < 0 || stamina < 0 || staminaMaximum < 0 || jockey is < 0 or >= RacerCount)
        {
            LastDiagnostic =
                $"chocobo player record is not a state: speed={speed}, stamina={stamina}/" +
                $"{staminaMaximum}, jockey={jockey}";
            return false;
        }

        var place = 0;
        foreach (var sprite in ranking)
        {
            if (sprite.Number == jockey + 1)
            {
                place = sprite.Place;
                break;
            }
        }

        // Nine separate reads, and everything that decides what they mean is checked again
        // now that they are done. A module change leaves half of one race and half of
        // whatever replaced it; a phase change means the race screen has gone; a switch of
        // 0x00E71128 means these are the controls of a race the player is no longer riding
        // in; and a fade means the whole HUD went behind black while it was being read.
        // Each of those makes the answer a mixture rather than a state, and a mixture is
        // refused rather than spoken.
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            module != ChocoboModule)
        {
            LastDiagnostic = "chocobo module changed while the player record was being read";
            return false;
        }

        if (!PhaseStillHolds(ChocoboSquarePhase.Race))
        {
            return false;
        }

        if (!memory.TryReadInt32((uint)AddressRaceMode, out var raceModeAfter))
        {
            LastDiagnostic = "chocobo player race flag unreadable";
            return false;
        }

        if (raceModeAfter != raceMode)
        {
            LastDiagnostic =
                "chocobo race changed between betting and riding while the player record " +
                "was being read";
            return false;
        }

        if (!memory.TryReadInt32((uint)AddressFadeState, out var fadeAfter))
        {
            LastDiagnostic = "chocobo fade state unreadable";
            return false;
        }

        if (fadeAfter != 0)
        {
            control = default;
            return true;
        }

        control = new ChocoboRaceControlState(
            true,
            started != 0,
            automatic != 0,
            speed,
            stamina,
            staminaMaximum,
            animation == TurboAnimation,
            finished != 0,
            place);
        return true;
    }

    public bool TryReadRace(out ChocoboRaceState state)
    {
        state = default;
        if (!TryReadPhase(out var phase))
        {
            return false;
        }

        if (phase != ChocoboSquarePhase.Race)
        {
            state = new ChocoboRaceState(
                false, false, -1,
                Array.Empty<ChocoboRankingSprite>(), Array.Empty<ChocoboMinimapMarker>());
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressRaceMode, out var raceMode) ||
            !memory.TryReadInt32((uint)AddressRankingVisible, out var rankingVisible) ||
            !memory.TryReadInt32((uint)AddressGraphicsMode, out var graphicsMode) ||
            !memory.TryReadInt32((uint)AddressInspectedRacer, out var tracked))
        {
            LastDiagnostic = "chocobo race HUD unreadable";
            return false;
        }

        if (tracked is < 0 or >= RacerCount)
        {
            LastDiagnostic = $"chocobo tracked racer {tracked} is out of range";
            return false;
        }

        var ranking = new List<ChocoboRankingSprite>(RacerCount);
        if (rankingVisible != 0)
        {
            for (var place = 0; place < RacerCount; place++)
            {
                var record = (uint)(AddressRankingSprites + (place * SpriteStride));
                if (!memory.TryReadInt32(record + SpriteRenderIdOffset, out var renderId) ||
                    !memory.TryReadUInt16(record + SpriteUOffset, out var u) ||
                    !memory.TryReadUInt16(record + SpriteVOffset, out var v))
                {
                    LastDiagnostic = $"chocobo ranking sprite {place} unreadable";
                    return false;
                }

                if (renderId != DrawnRenderId)
                {
                    continue;
                }

                if (!TryDecodeJockeyId(graphicsMode, u, v, out var jockeyId))
                {
                    continue;
                }

                ranking.Add(new ChocoboRankingSprite(place + 1, jockeyId + 1));
            }

            // The strip always shows all six, each exactly once. Anything else is a
            // frame caught mid-refresh, and a partial order is worse than none.
            if (ranking.Count != RacerCount ||
                ranking.Select(sprite => sprite.Number).Distinct().Count() != RacerCount)
            {
                ranking.Clear();
            }
        }

        var minimap = new List<ChocoboMinimapMarker>(RacerCount);
        for (var index = 0; index < RacerCount; index++)
        {
            var racer = (uint)(AddressRacerArray + (index * RacerStride));
            if (!memory.TryReadInt16(racer + RacerFinishedOffset, out var finished))
            {
                LastDiagnostic = $"chocobo racer {index} finish flag unreadable";
                return false;
            }

            if (finished != 0)
            {
                continue;
            }

            var square = (uint)(AddressMinimapSquares + (index * SpriteStride));
            if (!memory.TryReadUInt16(square + MinimapXOffset, out var x) ||
                !memory.TryReadUInt16(square + MinimapYOffset, out var y))
            {
                LastDiagnostic = $"chocobo minimap square {index} unreadable";
                return false;
            }

            minimap.Add(new ChocoboMinimapMarker(index, x, y, index == tracked));
        }

        if (!PhaseStillHolds(ChocoboSquarePhase.Race))
        {
            return false;
        }

        if (!TryReadPlayerControl(raceMode, rankingVisible, ranking, out var control))
        {
            return false;
        }

        // A fade covering the screen means a sighted player can see none of this either,
        // so the readout says nothing rather than narrating over black. A fade that cannot
        // be read is not a fade that is absent: failing open here would narrate over black
        // precisely when the reader is least sure of itself.
        if (!memory.TryReadInt32((uint)AddressFadeState, out var fade))
        {
            LastDiagnostic = "chocobo fade state unreadable";
            return false;
        }

        var obscured = fade != 0;

        state = new ChocoboRaceState(
            IsRacing: true,
            IsPlayerRacing: raceMode != 0,
            TrackedRacer: tracked,
            Ranking: ranking,
            Minimap: minimap,
            Control: control,
            IsObscured: obscured);
        LastDiagnostic =
            $"chocobo race ranking={ranking.Count}, minimap={minimap.Count}, playerRacing={raceMode != 0}";
        return true;
    }

    public bool TryReadResults(out ChocoboResultsState state)
    {
        state = default;
        if (!TryReadPhase(out var phase))
        {
            return false;
        }

        if (phase != ChocoboSquarePhase.Results)
        {
            state = new ChocoboResultsState(
                false, false, 0, 0,
                Array.Empty<ChocoboResultPlace>(), Array.Empty<ChocoboPrizeCell>());
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressWinningPairFirst, out var first) ||
            !memory.TryReadInt32((uint)AddressWinningPairSecond, out var second) ||
            !memory.TryReadInt32((uint)AddressResultsFrame, out var resultsFrame) ||
            !memory.TryReadInt32((uint)AddressFadeState, out var fade))
        {
            LastDiagnostic = "chocobo winning pair unreadable";
            return false;
        }

        // Both globals index a six-face digit array. Anything outside that would be a
        // pair the screen cannot be showing.
        if (first is < 0 or >= RacerCount || second is < 0 or >= RacerCount || first == second)
        {
            LastDiagnostic = $"chocobo winning pair ({first},{second}) is not a drawable pair";
            return false;
        }

        var places = new List<ChocoboResultPlace>(RacerCount);
        var claimed = new bool[RacerCount + 1];
        for (var index = 0; index < RacerCount; index++)
        {
            var racer = (uint)(AddressRacerArray + (index * RacerStride));
            if (!memory.TryReadInt16(racer + RacerPositionOffset, out var position))
            {
                LastDiagnostic = $"chocobo racer {index} finishing place unreadable";
                return false;
            }

            // The field is already one based. Two racers sharing a place, or one
            // outside the six, is a frame caught mid-update rather than a result.
            if (position is < 1 or > RacerCount || claimed[position])
            {
                places.Clear();
                break;
            }

            claimed[position] = true;
            if (!TryReadRacerCard(index, out var card))
            {
                return false;
            }

            places.Add(new ChocoboResultPlace(position, card.JockeyNumber, card.Name));
        }

        // Each printed digit belongs to one chocobo. A repeated identity is the same
        // kind of half-updated frame as a repeated place.
        if (places.Select(place => place.Number).Distinct().Count() != places.Count)
        {
            places.Clear();
        }

        places.Sort((left, right) => left.Place.CompareTo(right.Place));
        if (!TryReadRevealedPrizes(fade, out var prizes))
        {
            return false;
        }

        if (!PhaseStillHolds(ChocoboSquarePhase.Results))
        {
            return false;
        }

        state = new ChocoboResultsState(
            IsShowing: true,
            // The names and the pair go up with the screen itself, so there is no
            // frame to wait for - only the fade. While the overlay is at full black
            // there is nothing on screen to describe, and a settled six-place order is
            // what tells a whole frame from a half-updated one.
            IsRevealed: fade < FullyBlackFade && places.Count == RacerCount,
            FirstWinningNumber: first + 1,
            SecondWinningNumber: second + 1,
            Places: places,
            Prizes: prizes);
        LastDiagnostic =
            $"chocobo results pair=({first + 1},{second + 1}), places={places.Count}, " +
            $"frame={resultsFrame}, fade={fade}, prizes={prizes.Count}";
        return true;
    }

    /// <summary>
    /// The prize cards that are face up right now. FUN_00777DF4 fills a cell's prize
    /// sprite only where bDisplayPrize is set, which FUN_00778C5D does for the whole
    /// grid at results frame 115 - long after the finishing order has been on screen.
    /// The card's picture is one of the three gifts the betting menu already offered,
    /// chosen by +0x07, so it is named from the same table the menu names them from.
    /// </summary>
    private bool TryReadRevealedPrizes(int fade, out IReadOnlyList<ChocoboPrizeCell> prizes)
    {
        var revealed = new List<ChocoboPrizeCell>(GridColumns * GridRows);
        prizes = revealed;

        // A card turned face up under a black overlay is not a card anyone can see.
        // The flags survive the screen fading back out at the end, so this is also
        // what stops the whole grid being read out again on the way off screen.
        if (fade >= FullyBlackFade)
        {
            return true;
        }

        if (!TryReadGifts(out var gifts))
        {
            return false;
        }

        for (var column = 0; column < GridColumns; column++)
        {
            for (var row = 0; row < GridRows; row++)
            {
                var cell = (uint)(AddressPrizeGrid + (column * PrizeColumnStride) + (row * PrizeCellStride));
                if (!memory.TryReadByte(cell + PrizeDisplayOffset, out var displayed) ||
                    !memory.TryReadByte(cell + PrizeIndexOffset, out var prizeIndex))
                {
                    LastDiagnostic = $"chocobo prize cell ({column},{row}) unreadable";
                    return false;
                }

                if (displayed == 0)
                {
                    continue;
                }

                if (prizeIndex >= gifts.Count)
                {
                    LastDiagnostic =
                        $"chocobo prize cell ({column},{row}) names gift {prizeIndex}, which is not offered";
                    return false;
                }

                if (!TryReadCellPair(column, row, out var first, out var second))
                {
                    return false;
                }

                revealed.Add(new ChocoboPrizeCell(
                    column, row, first, second, prizeIndex, gifts[prizeIndex]));
            }
        }

        return true;
    }

    /// <summary>
    /// The pair a grid cell stands for, taken from the renderer's own table rather
    /// than reconstructed. The table's third and fourth shorts repeat the cell's own
    /// column and row, which is what makes it safe to index.
    /// </summary>
    public bool TryReadCellPair(int column, int row, out int first, out int second)
    {
        first = 0;
        second = 0;
        if (column is < 0 or >= GridColumns || row is < 0 or >= GridRows)
        {
            return false;
        }

        var entry = (uint)(AddressCellPairTable + (((row * GridColumns) + column) * CellPairStride));
        if (!memory.TryReadInt16(entry, out var rawFirst) ||
            !memory.TryReadInt16(entry + 2, out var rawSecond) ||
            !memory.TryReadInt16(entry + 4, out var tableColumn) ||
            !memory.TryReadInt16(entry + 6, out var tableRow))
        {
            LastDiagnostic = $"chocobo cell pair table entry ({column},{row}) unreadable";
            return false;
        }

        if (tableColumn != column || tableRow != row ||
            rawFirst is < 0 or >= RacerCount || rawSecond is < 0 or >= RacerCount ||
            rawFirst == rawSecond)
        {
            LastDiagnostic =
                $"chocobo cell pair table entry ({column},{row}) reads " +
                $"({rawFirst},{rawSecond}) at ({tableColumn},{tableRow})";
            return false;
        }

        first = rawFirst + 1;
        second = rawSecond + 1;
        return true;
    }

    /// <summary>
    /// The pair a grid cell stands for, without a memory read. The renderer's own
    /// table at 0x0097B470 lists the fifteen unordered pairs of six racers in exactly
    /// this order, row by row - (1,2), (1,3) ... (4,6), (5,6) - which
    /// <see cref="TryReadCellPair"/> reads directly and the tests check this against.
    /// </summary>
    public static (int First, int Second) DescribeCell(int cell)
    {
        var column = cell / 10;
        var row = cell % 10;
        var index = (row * GridColumns) + column;
        var seen = 0;
        for (var first = 1; first <= RacerCount; first++)
        {
            for (var second = first + 1; second <= RacerCount; second++)
            {
                if (seen++ == index)
                {
                    return (first, second);
                }
            }
        }

        return (0, 0);
    }

    public static bool IsSelectableCell(int cell) =>
        cell >= 0 && cell / 10 is >= 0 and < GridColumns && cell % 10 is >= 0 and < GridRows;

    private bool TryReadRacerCard(int index, out ChocoboRacerCard card)
    {
        card = default;
        if (index is < 0 or >= RacerCount)
        {
            return false;
        }

        var racer = (uint)(AddressRacerArray + (index * RacerStride));
        if (!memory.TryReadInt16(racer + RacerTopSpeedOffset, out var rawTopSpeed) ||
            !memory.TryReadInt32(racer + RacerStaminaOffset, out var rawStamina) ||
            !memory.TryReadInt16(racer + RacerNumberOffset, out var jockeyId))
        {
            LastDiagnostic = $"chocobo racer {index} card unreadable";
            return false;
        }

        // The results digit array holds faces one through six, so an id outside that
        // range would index a sprite the screen has never drawn.
        if (jockeyId is < 0 or >= RacerCount)
        {
            LastDiagnostic = $"chocobo racer {index} jockey id {jockeyId} is out of range";
            return false;
        }

        // A name that runs past its own field, or has no terminator inside it, is not
        // a name. Saying nothing about the racer is better than reading the jockey id
        // that follows it as text.
        if (!LegacyFf7TextReader.TryReadTerminated(
                memory, racer + RacerNameOffset, RacerNameStorageBytes, out _, out var name))
        {
            LastDiagnostic = $"chocobo racer {index} name unreadable within its own field";
            return false;
        }

        card = new ChocoboRacerCard(
            index,
            // The card's digit is the inspected index, not the model's identity.
            index + 1,
            jockeyId + 1,
            name,
            // The card divides before drawing; the raw values are never spoken.
            rawTopSpeed / TopSpeedDivisor,
            rawStamina / StaminaDivisor);
        return true;
    }

    private bool TryReadDisplayedGil(int raceClass, int selectedCount, out int gil)
    {
        gil = 0;
        if (!memory.TryReadUInt32((uint)AddressGil, out var partyGilPointer) ||
            !FieldMovieNarrationSampleReader.IsPlausibleGuestPointer(partyGilPointer) ||
            !memory.TryReadInt32(partyGilPointer, out var partyGil))
        {
            LastDiagnostic = "chocobo party gil unreadable";
            return false;
        }

        // The price table is class-major with one entry per ticket already reserved.
        var priceIndex = (raceClass * (SelectedCellSlots + 1)) + selectedCount;
        if (!memory.TryReadInt32((uint)(AddressPriceTable + (priceIndex * sizeof(int))), out var reserved))
        {
            LastDiagnostic = "chocobo ticket price unreadable";
            return false;
        }

        gil = partyGil - reserved;
        return gil >= 0;
    }

    private bool TryReadGifts(out IReadOnlyList<string> gifts)
    {
        var names = new string[GiftCount];
        gifts = names;
        for (var slot = 0; slot < GiftCount; slot++)
        {
            if (!memory.TryReadInt32((uint)(AddressGiftNameIndices + (slot * sizeof(int))), out var index) ||
                index < 0)
            {
                LastDiagnostic = $"chocobo gift {slot} unreadable";
                return false;
            }

            LegacyFf7TextReader.TryReadTerminated(
                memory,
                (uint)(AddressGiftNameTable + (index * GiftNameStride)),
                GiftNameStride,
                out _,
                out names[slot]);
        }

        return true;
    }

    /// <summary>
    /// Which number a drawn face sprite is showing. The renderer lays the numbers out
    /// differently depending on the graphics mode at 0x00E3BA6C: mode 2 uses a
    /// forty-eight pixel grid three across, and anything else a single row of
    /// twenty-four pixel cells.
    /// </summary>
    public static bool TryDecodeJockeyId(int graphicsMode, int u, int v, out int jockeyId)
    {
        jockeyId = -1;
        if (graphicsMode == 2)
        {
            if (u % 48 != 0 || v % 48 != 0)
            {
                return false;
            }

            var column = u / 48;
            var rowIndex = v / 48;
            if (column is < 0 or > 2 || rowIndex is < 0 or > 1)
            {
                return false;
            }

            jockeyId = (rowIndex * 3) + column;
            return jockeyId is >= 0 and < RacerCount;
        }

        if (v != 0 || u % 24 != 0)
        {
            return false;
        }

        jockeyId = u / 24;
        return jockeyId is >= 0 and < RacerCount;
    }
}
