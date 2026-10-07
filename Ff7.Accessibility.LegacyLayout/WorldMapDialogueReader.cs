using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed record WorldMapDialogueWindow(int WindowId, ushort State, uint TextPointer, string Text)
{
    public bool IsWaitingForInput => State is 4 or 6 or 11 or 13 or 14;

    /// <summary>The rendered lines, split where FUN_00769C02 copied 0xE7, empty lines kept.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>The ASK on this window, when the world script is asking one; null for a message.</summary>
    public WorldMapDialogueChoice? Choice { get; init; }
}

/// <summary>
/// A world-script ASK as FUN_007693A1 runs it: lines <see cref="FirstLine"/>..<see cref="LastLine"/>
/// of the window are the options, and the cursor DAT_00E36104 is the line highlighted. A cursor
/// outside the options (a sample before state 6's first clamp) names no option.
/// </summary>
public sealed record WorldMapDialogueChoice(int FirstLine, int LastLine, int SelectedLine, IReadOnlyList<string> Options)
{
    /// <summary>
    /// The cursor arrow is drawn: FUN_007693A1's state 6 sets CFF5D2[window] to 1 on the frames it
    /// takes Up and Down, clamps the cursor and places the arrow (CFF5DE = line * 16 + 6), which is
    /// whenever CFF5E6 bit 0 is clear; FUN_00769836 clears it when the window opens.
    /// </summary>
    public bool CursorShown { get; init; }

    public int SelectedIndex => SelectedLine >= FirstLine && SelectedLine <= LastLine ? SelectedLine - FirstLine : -1;

    public string? SelectedOption => SelectedIndex >= 0 && SelectedIndex < Options.Count ? Options[SelectedIndex] : null;
}

public sealed record WorldMapDialogueSnapshot(bool PlayerHasControl, IReadOnlyList<WorldMapDialogueWindow> Windows)
{
    public bool IsBlockingMovement => !PlayerHasControl || Windows.Count != 0;
}

/// <summary>
/// Who owns world movement, read on its own so that a torn or unreadable window never hides
/// it. The native control flag DE6B5C is cleared by FUN_0074D438 whenever a world script,
/// a window or the party menu takes control (0074EA48's menu branch calls 0074D438(0,1)).
/// The main-menu session flag DC12F0 is set by FUN_006CB56A when a menu session starts and
/// cleared only once the closing slide has finished (phase DC1298 back to -1), so the party
/// menu and every submenu, closing included, hold the party still.
/// </summary>
public readonly record struct WorldMapMovementOwnership(bool PlayerHasControl, bool MainMenuSession)
{
    public bool IsBlockingMovement => !PlayerHasControl || MainMenuSession;
}

/// <summary>FUN_0074EA48's full dive gate and the transition set by FUN_0074D6F6.</summary>
public readonly record struct WorldMapSubmarineDiveState(int MainState, int Control, int ControlLock, int MovementEnabled)
{
    public bool CanRequestDive => MainState == 1 && Control != 0 && ControlLock <= 0 && MovementEnabled != 0;
    public bool DiveAccepted => MainState is 4 or 6;
}

/// <summary>
/// Whether world movement belongs to something else this sample. Window text is only for
/// speech; a window that is up still blocks when it can be read.
/// </summary>
public static class WorldMapMovementGate
{
    /// <summary>
    /// The whole sample pauses: the game says a script, a window or the party menu owns
    /// movement, or a readable window is up. The route is kept; nothing is reset.
    /// </summary>
    public static bool ShouldPauseSample(
        bool ownershipRead,
        WorldMapMovementOwnership ownership,
        bool dialogueRead,
        WorldMapDialogueSnapshot? dialogue) =>
        (ownershipRead && ownership.IsBlockingMovement) ||
        (dialogueRead && dialogue is { IsBlockingMovement: true });

    /// <summary>
    /// Automatic walking is held - released, and its stall clock stopped - whenever the
    /// sample pauses, and also when ownership could not be read at all: movement fails
    /// closed, while the rest of the sample (a failed state read's own quiet period, for
    /// one) carries on as it always did.
    /// </summary>
    public static bool ShouldHold(
        bool ownershipRead,
        WorldMapMovementOwnership ownership,
        bool dialogueRead,
        WorldMapDialogueSnapshot? dialogue) =>
        !ownershipRead || ShouldPauseSample(ownershipRead, ownership, dialogueRead, dialogue);
}

/// <summary>
/// FUN_00769836 gives each world window a rendered buffer at E3B220, and
/// FUN_00769C02 appends the visible characters. CFF5E4 is the window state;
/// DE6B5C is the control flag maintained by FUN_0074D438. These guest addresses
/// are shared by both runtimes. No text is taken from an unshown script.
///
/// <para>A window is the world's when its CC0960 owner equals DAT_00CC0964, the test
/// FUN_00769C02 and the other window routines make. FUN_0075EE50, the world's window init,
/// sets CC0964 to 0xFF and FUN_00769836 copies it into the opened window's owner, so on the
/// world map an open window's owner is 0xFF - not "free". Treating 0xFF as free refused every
/// world window, and none was ever spoken.</para>
///
/// <para>The world script's ASK (FUN_0075EEBB) runs on window 0: it stores its first and last
/// option lines at E36108 and E36100 and FUN_007693A1 keeps the cursor at E36104, moving it
/// on Up and Down in state 6. A message (FUN_0075EE86) clears both to 0, which is how
/// FUN_0075EF46 tells the two apart each frame.</para>
/// </summary>
public sealed class WorldMapDialogueReader(ILegacyAddressSpace memory)
{
    public const uint ControlAddress = 0x00DE6B5C;
    public const uint WindowStateAddress = 0x00CFF5E4;
    public const uint TextPointerAddress = 0x00E3B210;
    public const uint TextBufferAddress = 0x00E3B220;
    public const uint CurrentWindowOwnerAddress = 0x00CC0964;
    public const uint AskLastLineAddress = 0x00E36100;
    public const uint AskCursorAddress = 0x00E36104;
    public const uint AskFirstLineAddress = 0x00E36108;
    public const uint CursorShownAddress = 0x00CFF5D2;

    public bool TryRead(out WorldMapDialogueSnapshot snapshot)
    {
        snapshot = new(false, []);
        if (!TryCapture(out var control, out var before, out var ask)) return false;
        var windows = new List<WorldMapDialogueWindow>();
        var encoded = new Dictionary<int, byte[]>();
        var blocking = false;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i].State == 0) continue;

            // Shown, so the party is held whoever owns it; spoken only when it is the world's.
            blocking = true;
            if (before[i].Owner != before[i].CurrentOwner) continue;
            if (!LegacyFf7TextReader.TryReadTerminated(memory, TextBufferAddress + (uint)(i * 0x100),
                    0x100, out var bytes, out var text)) return false;
            encoded[i] = bytes;
            var lines = Ff7EncodedTextDecoder.DecodeLines(bytes);
            windows.Add(new(i, before[i].State, before[i].Pointer,
                Ff7EncodedTextDecoder.NormalizeWhitespace(text))
            {
                Lines = lines,
                Choice = i == 0 && ask.IsAsk ? CreateChoice(ask, lines) : null
            });
        }
        if (!TryCapture(out var middleControl, out var middle, out var middleAsk) || middleControl != control ||
            !before.SequenceEqual(middle) || middleAsk != ask) return false;
        foreach (var (i, bytes) in encoded)
        {
            if (!LegacyFf7TextReader.TryReadTerminated(memory, TextBufferAddress + (uint)(i * 0x100),
                    0x100, out var second, out _) || !bytes.AsSpan().SequenceEqual(second)) return false;
        }
        if (!TryCapture(out var afterControl, out var after, out var afterAsk) || afterControl != control ||
            !before.SequenceEqual(after) || afterAsk != ask) return false;
        snapshot = new(control && !blocking, windows);
        return true;
    }

    /// <summary>
    /// The control flag and the main-menu session, read twice and agreeing, independently of
    /// the windows. False when either read fails, the world module is not current, or the two
    /// reads disagree.
    /// </summary>
    public bool TryReadMovementOwnership(out WorldMapMovementOwnership ownership)
    {
        ownership = default;
        if (!TryReadOwnershipOnce(out var first) ||
            !TryReadOwnershipOnce(out var second) ||
            first != second)
        {
            return false;
        }

        ownership = first;
        return true;
    }

    /// <summary>
    /// The native world input mask FUN_0074EA48 moves the party by: FUN_007186B9 returns the
    /// 32-bit DAT_009A85D4 (the x64 build reads the same translated guest address). Direction
    /// bits: Up 0x1000, Right 0x2000, Down 0x4000, Left 0x8000. Two agreeing reads, or
    /// unknown. Navigation also uses Cancel's observed release to prepare one dive press.
    /// </summary>
    public const uint NativeWorldInputAddress = 0x009A85D4;

    public bool TryReadNativeWorldInput(out uint mask)
    {
        mask = 0;
        if (!memory.TryReadUInt32(NativeWorldInputAddress, out var first) ||
            !memory.TryReadUInt32(NativeWorldInputAddress, out var second) ||
            first != second)
        {
            return false;
        }

        mask = first;
        return true;
    }

    public const uint NativeWorldMainStateAddress = 0x00E045E4;
    public const uint NativeControlLockAddress = 0x00DFC4B8;
    public const uint NativeMovementEnabledAddress = 0x00E28CDC;

    public bool TryReadSubmarineDiveState(out WorldMapSubmarineDiveState state)
    {
        state = default;
        if (!Capture(out var first) || !Capture(out var second) || first != second) return false;
        state = first;
        return true;

        bool Capture(out WorldMapSubmarineDiveState value)
        {
            value = default;
            if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) || module != 3 ||
                !memory.TryReadInt32(NativeWorldMainStateAddress, out var main) ||
                !memory.TryReadInt32(ControlAddress, out var control) ||
                !memory.TryReadInt32(NativeControlLockAddress, out var controlLock) ||
                !memory.TryReadInt32(NativeMovementEnabledAddress, out var movement)) return false;
            value = new(main, control, controlLock, movement);
            return true;
        }
    }

    private bool TryReadOwnershipOnce(out WorldMapMovementOwnership ownership)
    {
        ownership = default;
        if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) ||
            module != WorldMapStateReader.WorldModule ||
            !memory.TryReadInt32(ControlAddress, out var nativeControl) || nativeControl is not (0 or 1) ||
            !memory.TryReadInt32((uint)MenuGilStateReader.AddressMainMenuSession, out var session) ||
            !memory.TryReadInt32((uint)MenuGilStateReader.AddressMainMenuPhase, out _))
        {
            return false;
        }

        ownership = new WorldMapMovementOwnership(nativeControl != 0, session != 0);
        return true;
    }

    private bool TryCapture(out bool control, out WindowHeader[] windows, out AskHeader ask)
    {
        control = false;
        ask = default;
        windows = new WindowHeader[4];
        if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) ||
            module != WorldMapStateReader.WorldModule ||
            !memory.TryReadInt32(ControlAddress, out var nativeControl) || nativeControl is not (0 or 1) ||
            !memory.TryReadByte(CurrentWindowOwnerAddress, out var currentOwner) ||
            !memory.TryReadUInt16(AskFirstLineAddress, out var first) ||
            !memory.TryReadUInt16(AskLastLineAddress, out var last) ||
            !memory.TryReadUInt16(AskCursorAddress, out var cursor))
            return false;
        // Window 0's arrow flag, only while an ASK is set: a message has no cursor to show.
        byte shown = 0;
        if ((first != 0 || last != 0) && !memory.TryReadByte(CursorShownAddress, out shown)) return false;
        control = nativeControl != 0;
        ask = new AskHeader(first, last, (short)cursor, shown);
        for (var i = 0; i < windows.Length; i++)
        {
            if (!memory.TryReadUInt16(WindowStateAddress + (uint)(i * 0x30), out var state) || state > 14 ||
                !memory.TryReadByte((uint)FieldMessageReader.AddressFieldWindowStates + (uint)i, out var owner) ||
                !memory.TryReadUInt32(TextPointerAddress + (uint)(i * 4), out var pointer)) return false;
            windows[i] = new(state, owner, currentOwner, pointer);
        }
        return true;
    }

    private static WorldMapDialogueChoice? CreateChoice(AskHeader ask, IReadOnlyList<string> lines) =>
        ask.First <= ask.Last && ask.Last < lines.Count
            ? new WorldMapDialogueChoice(ask.First, ask.Last, ask.Cursor, lines.Skip(ask.First).Take(ask.Last - ask.First + 1).ToArray())
            {
                CursorShown = ask.Shown == 1
            }
            : null;

    private readonly record struct WindowHeader(ushort State, byte Owner, byte CurrentOwner, uint Pointer);

    /// <summary>E36108, E36100 and E36104: an ASK's first and last option lines and its cursor;
    /// CFF5D2: whether window 0 draws the cursor arrow.</summary>
    private readonly record struct AskHeader(ushort First, ushort Last, short Cursor, byte Shown)
    {
        public bool IsAsk => First != 0 || Last != 0;
    }
}

/// <summary>
/// Announces completed visible pages once while leaving Confirm to the player. A choice is
/// announced once with its prompt and the highlighted option; after that only a
/// change of the highlighted option is spoken. Nothing is pressed.
/// </summary>
public sealed class WorldMapDialogueTracker
{
    private readonly Dictionary<int, (uint Pointer, string Text)> spoken = new();
    private readonly Dictionary<int, int> highlighted = new();

    public string? Observe(WorldMapDialogueSnapshot snapshot)
    {
        foreach (var id in spoken.Keys.Except(snapshot.Windows.Select(w => w.WindowId)).ToArray())
        {
            spoken.Remove(id);
            highlighted.Remove(id);
        }

        var lines = new List<string>();
        foreach (var window in snapshot.Windows)
        {
            if (!window.IsWaitingForInput || window.Text.Length == 0) continue;
            var key = (window.TextPointer, window.Text);
            var alreadySpoken = spoken.TryGetValue(window.WindowId, out var previous) && previous == key;
            if (window.Choice is { } choice && window.State == 6)
            {
                // FUN_007693A1's state 6: the cursor is live. Until its arrow is drawn and it is
                // inside the options (its first clamp) nothing is said, so no stale option is named.
                if (!choice.CursorShown || choice.SelectedOption is not { } option) continue;
                var position = $"{option}, {choice.SelectedIndex + 1} of {choice.Options.Count}.";
                if (!alreadySpoken)
                {
                    spoken[window.WindowId] = key;
                    highlighted[window.WindowId] = choice.SelectedLine;
                    var prompt = DescribePrompt(window, choice);
                    lines.Add(prompt.Length == 0 ? position : prompt + " " + position);
                }
                else if (!highlighted.TryGetValue(window.WindowId, out var line) || line != choice.SelectedLine)
                {
                    highlighted[window.WindowId] = choice.SelectedLine;
                    lines.Add(position);
                }

                continue;
            }

            if (alreadySpoken) continue;
            spoken[window.WindowId] = key;
            lines.Add(window.Text);
        }
        return lines.Count == 0 ? null : string.Join(" ", lines);
    }

    public void Reset()
    {
        spoken.Clear();
        highlighted.Clear();
    }

    /// <summary>The window's own prompt; options are read as the player highlights them.</summary>
    private static string DescribePrompt(WorldMapDialogueWindow window, WorldMapDialogueChoice choice)
    {
        return string.Join(" ", window.Lines
            .Where((_, index) => index < choice.FirstLine || index > choice.LastLine)
            .Where(line => line.Length > 0));
    }
}
