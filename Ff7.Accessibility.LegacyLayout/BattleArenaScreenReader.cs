using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// What the Battle Square's between-battle screen shows. <see cref="Lines"/> are the screen's
/// own texts in drawing order; <see cref="Options"/> and <see cref="Cursor"/> are set only while
/// it asks "Keep goin'?".
/// </summary>
public sealed record BattleArenaScreenSnapshot(
    int Phase,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Options,
    int Cursor)
{
    public string? SelectedOption => Cursor >= 0 && Cursor < Options.Count ? Options[Cursor] : null;
}

/// <summary>
/// The Battle Square screen between two battles, in the battle module. It is battle menu
/// window 0x1C: FUN_006D7BBB draws it with FUN_006E3C9C and FUN_006DAA08 runs it while the
/// battle menu state at 0x0091EF9C is 0x1C. Its phase is the byte at 0x00DC209C:
///
/// <list type="bullet">
/// <item><description>5: "GREAT!!" (0x0091EBDC), set by FUN_006DA98D; OK goes to 0.</description></item>
/// <item><description>0: "Keep goin'?" (0x0091EB88) over "Of course!" and "No way!", one string
/// at 0x0091EB98, and "Current Battle Points" (0x0091EBB0) with the points at 0x00DC094C. The
/// cursor at 0x00DC2228 (FUN_006F4DB2's record, set up by FUN_006DA98D as two columns) is the
/// column: OK on 0 starts the slot, OK on 1 calls FUN_00431C80(0) and the run ends.</description></item>
/// <item><description>2: "Slot start!" (0x0091EBC6); OK goes to 1.</description></item>
/// <item><description>1: the handicap reels turn; OK stops the current one, and once
/// FUN_006E3839 says it has stopped the phase is 3.</description></item>
/// <item><description>3: "Then, go for it!" (0x0091EC08) and the handicap the reel stopped on:
/// symbol DC3B00[reel * 3 + row] >> 24, row from the reel's position at DC3C30, its text
/// 0x20 bytes per symbol after 0x0091EC08. OK calls FUN_00431C80(1), closes the window and
/// counts the battle (DC3860).</description></item>
/// </list>
///
/// The cursor hand is drawn (FUN_006D0022, case 0x1C) only in phase 0 and only while the
/// window's state at DC2068 + 0x1C is 2. Every text is the game's own string, read from guest
/// memory at the address the drawing call passes, so both runtimes say what each draws.
/// </summary>
public sealed class BattleArenaScreenReader(ILegacyAddressSpace memory)
{
    public const int ArenaMenuState = 0x1C;
    public const uint AddressPhase = 0x00DC209C;
    public const uint AddressCursor = 0x00DC2228;
    public const uint AddressBattlePoints = 0x00DC094C;
    public const uint AddressCurrentReel = 0x00DC3860;
    public const uint AddressReelSymbols = 0x00DC3B00;
    public const uint AddressReelPositions = 0x00DC3C30;
    public const uint AddressKeepGoingText = 0x0091EB88;
    public const uint AddressOptionsText = 0x0091EB98;
    public const uint AddressPointsText = 0x0091EBB0;
    public const uint AddressSlotStartText = 0x0091EBC6;
    public const uint AddressGreatText = 0x0091EBDC;
    public const uint AddressGoForItText = 0x0091EC08;
    public const int HandicapTextStride = 0x20;

    /// <summary>The handicap texts run from "Magic Materia is broken." to "HP restored.".</summary>
    public const int HandicapCount = 24;

    public const byte KeepGoingPhase = 0;
    public const byte ReelsPhase = 1;
    public const byte SlotStartPhase = 2;
    public const byte HandicapPhase = 3;
    public const byte GreatPhase = 5;

    private const int TextLimit = 0x40;

    /// <summary>
    /// The screen as it is now, or null when it is not up. False only when it could not be read
    /// coherently: two samples have to agree, as every battle read here does.
    /// </summary>
    public bool TryRead(out BattleArenaScreenSnapshot? screen)
    {
        screen = null;
        if (!TryReadOnce(out var first) || !TryReadOnce(out var second) || !Same(first, second))
        {
            return false;
        }

        screen = second;
        return true;
    }

    private bool TryReadOnce(out BattleArenaScreenSnapshot? screen)
    {
        screen = null;
        if (!memory.TryReadByte((uint)BattleStateReader.AddressCurrentModule, out var module) ||
            !memory.TryReadInt16((uint)BattleStateReader.AddressBattleMenuTextState, out var menuState) ||
            !memory.TryReadByte((uint)BattleStateReader.AddressMenuWindowStates + ArenaMenuState, out var windowState) ||
            !memory.TryReadByte(AddressPhase, out var phase))
        {
            return false;
        }

        if (module != BattleStateReader.BattleModule || menuState != ArenaMenuState ||
            windowState != BattleStateReader.ActiveWindowState)
        {
            return true;
        }

        switch (phase)
        {
            case KeepGoingPhase:
                if (!TryText(AddressKeepGoingText, out var prompt) ||
                    !TryOptions(out var options) ||
                    !TryText(AddressPointsText, out var pointsLabel) ||
                    !memory.TryReadUInt32(AddressBattlePoints, out var points) ||
                    !memory.TryReadInt32(AddressCursor, out var cursor))
                {
                    return false;
                }

                screen = new BattleArenaScreenSnapshot(phase, [prompt, $"{pointsLabel} {points}"], options, cursor);
                return true;
            case SlotStartPhase:
                return TryLines(phase, AddressSlotStartText, out screen);
            case GreatPhase:
                return TryLines(phase, AddressGreatText, out screen);
            case HandicapPhase:
                if (!TryText(AddressGoForItText, out var goForIt) || !TryHandicap(out var handicap))
                {
                    return false;
                }

                screen = new BattleArenaScreenSnapshot(phase, [goForIt, handicap], [], -1);
                return true;
            case ReelsPhase:
                // The reels themselves are pictures; what they stop on is said in phase 3.
                screen = new BattleArenaScreenSnapshot(phase, [], [], -1);
                return true;
            default:
                return true;
        }
    }

    private bool TryLines(byte phase, uint address, out BattleArenaScreenSnapshot? screen)
    {
        screen = null;
        if (!TryText(address, out var text))
        {
            return false;
        }

        screen = new BattleArenaScreenSnapshot(phase, [text], [], -1);
        return true;
    }

    /// <summary>
    /// FUN_006E3C9C case 3: the reel being counted is DC3860, its row is
    /// |(1 - (position / 8)) % 3| with the division rounding toward zero, and the symbol is the
    /// top byte of its entry in DC3B00.
    /// </summary>
    private bool TryHandicap(out string text)
    {
        text = string.Empty;
        if (!memory.TryReadByte(AddressCurrentReel, out var reel) ||
            !memory.TryReadInt16(AddressReelPositions + (uint)reel * 2, out var position))
        {
            return false;
        }

        var row = Math.Abs((1 - position / 8) % 3);
        if (!memory.TryReadInt32(AddressReelSymbols + (uint)(reel * 3 + row) * 4, out var entry))
        {
            return false;
        }

        var symbol = entry >> 24;
        return symbol is >= 0 and < HandicapCount &&
               TryText(AddressGoForItText + (uint)((symbol + 1) * HandicapTextStride), out text);
    }

    /// <summary>"Of course!" and "No way!" are one string, the second set apart by a run of spaces.</summary>
    private bool TryOptions(out IReadOnlyList<string> options)
    {
        options = [];
        if (!LegacyFf7TextReader.TryReadTerminated(memory, AddressOptionsText, TextLimit, out var bytes, out _))
        {
            return false;
        }

        var parts = new List<string>();
        var text = bytes.AsSpan(0, bytes.Length - 1);
        var start = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && !(text[index] == 0x00 && index + 1 < text.Length && text[index + 1] == 0x00))
            {
                continue;
            }

            var part = Ff7EncodedTextDecoder.NormalizeWhitespace(
                Ff7EncodedTextDecoder.Decode(text[start..index])).Trim();
            if (part.Length != 0)
            {
                parts.Add(part);
            }

            while (index < text.Length && text[index] == 0x00)
            {
                index++;
            }

            start = index;
            index--;
            if (start >= text.Length)
            {
                break;
            }
        }

        options = parts;
        return parts.Count != 0;
    }

    private bool TryText(uint address, out string text)
    {
        text = string.Empty;
        if (!LegacyFf7TextReader.TryReadTerminated(memory, address, TextLimit, out _, out var decoded))
        {
            return false;
        }

        text = Ff7EncodedTextDecoder.NormalizeWhitespace(decoded).Trim();
        return text.Length != 0;
    }

    private static bool Same(BattleArenaScreenSnapshot? left, BattleArenaScreenSnapshot? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.Phase == right.Phase && left.Cursor == right.Cursor &&
              left.Lines.SequenceEqual(right.Lines) && left.Options.SequenceEqual(right.Options);
}

/// <summary>
/// Says the between-battle screen as it changes: each phase's own text once when it appears,
/// and while "Keep goin'?" is asked, the question, the points, both answers and the one the
/// cursor is on, then only the answer the cursor moves to. The choice is the player's; nothing
/// is pressed.
/// </summary>
public sealed class BattleArenaScreenTracker
{
    private int spokenPhase = -1;
    private int spokenCursor = -1;
    private string? spokenText;

    public string? Observe(BattleArenaScreenSnapshot? screen)
    {
        if (screen is null)
        {
            Reset();
            return null;
        }

        var text = string.Join(" ", screen.Lines);
        if (screen.Phase == BattleArenaScreenReader.KeepGoingPhase)
        {
            if (screen.SelectedOption is not { } option)
            {
                return null;
            }

            var position = $"{option}, {screen.Cursor + 1} of {screen.Options.Count}.";
            if (spokenPhase != screen.Phase || spokenText != text)
            {
                Remember(screen, text);
                var choices = screen.Options.Count == 2
                    ? $"{screen.Options[0]}, or {screen.Options[1]}"
                    : string.Join(", ", screen.Options);
                return $"{EndSentence(screen.Lines[0])} {EndSentence(screen.Lines[1])} Choose: {EndSentence(choices)} {position}";
            }

            if (spokenCursor != screen.Cursor)
            {
                spokenCursor = screen.Cursor;
                return position;
            }

            return null;
        }

        if (spokenPhase == screen.Phase && spokenText == text)
        {
            return null;
        }

        Remember(screen, text);
        return text.Length == 0 ? null : string.Join(" ", screen.Lines.Select(EndSentence));
    }

    public void Reset()
    {
        spokenPhase = -1;
        spokenCursor = -1;
        spokenText = null;
    }

    private void Remember(BattleArenaScreenSnapshot screen, string text)
    {
        spokenPhase = screen.Phase;
        spokenCursor = screen.Cursor;
        spokenText = text;
    }

    private static string EndSentence(string line) =>
        line.Length == 0 || ".!?".Contains(line[^1]) ? line : line + ".";
}
