using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class WorldMapDialogueTests
{
    /// <summary>
    /// wm "mes" entry 43, the dismount prompt for a stable Chocobo with Cloud leading, as the
    /// world renderer FUN_00769C02 copies it into E3B220: the speaker code 0xEA expanded into
    /// Cloud's name, 0xE7 kept between lines, and each choice line indented by ten 0x00 spaces.
    /// </summary>
    private static readonly byte[] ChocoboDismount = Convert.FromHexString(
        "234C4F5544" + "E7" +
        "B22E4F5700464F52005448450023484F434F424FA9B3" + "E7" +
        "0000000000000000000033454E44004954004241434B00544F0054484500535441424C4553" + "E7" +
        "000000000000000000003455524E004954004C4F4F5345" + "FF");

    public static void Run()
    {
        TheWorldOwnsItsOpenWindowsWithOwner0xFF();
        PlainMessagesAreReadAsBefore();
        TheChocoboDismountChoiceIsReadWithItsCursor();
        AChoiceIsNotSpokenBeforeItsCursorIsInRange();
        AChoiceIsNotSpokenBeforeItsArrowIsDrawn();
        Console.WriteLine("World dialogue reader tests passed.");
    }

    /// <summary>
    /// FUN_0075EE50, the world's window init, sets DAT_00CC0964 to 0xFF, and FUN_00769836 opens a
    /// window by copying it into CC0960[window]; FUN_00769C02 and the rest then test
    /// CC0960[window] == CC0964. So every open world window is owned by 0xFF - and a reader that
    /// took 0xFF for "free" never read one.
    /// </summary>
    private static void TheWorldOwnsItsOpenWindowsWithOwner0xFF()
    {
        var memory = new Memory();
        var reader = new WorldMapDialogueReader(memory);
        memory.SetWindow(6, Encode("Not even a Chocobo could make it across."));
        Require(reader.TryRead(out var page), "a native world window (owner 0xFF, as CC0964) is readable");
        Require(page.Windows.Single().Text.Contains("Chocobo"), "with its text");

        memory.Owner = 0x00;
        Require(reader.TryRead(out page) && page.Windows.Count == 0,
            "a buffer whose owner is not the world's current one (CC0964) is not world dialogue");
        Require(page.IsBlockingMovement, "the window still holds the party: its state is not 0");
    }

    private static void PlainMessagesAreReadAsBefore()
    {
        var memory = new Memory();
        var reader = new WorldMapDialogueReader(memory);
        memory.SetWindow(6, Encode("Not even a Chocobo could make it across."));
        Require(reader.TryRead(out var page), "native world warning is readable");
        Require(page.IsBlockingMovement && page.Windows.Single().Text.Contains("Chocobo"),
            "the visible warning owns input while the script waits for Confirm");
        Require(page.Windows[0].IsWaitingForInput, "completed text is ready to speak");
        Require(page.Windows[0].Choice is null, "a message (ASK lines 0 and 0, FUN_0075EF46) has no choice");
        var tracker = new WorldMapDialogueTracker();
        Require(tracker.Observe(page)?.Contains("Chocobo") == true, "ready warning speaks");
        Require(tracker.Observe(page) is null, "waiting for the player does not repeat the warning");
        memory.SetWindow(2, Encode("Not even"));
        Require(reader.TryRead(out page) && !page.Windows[0].IsWaitingForInput,
            "letters still appearing are not announced as a complete sentence");
        Require(tracker.Observe(page) is null, "typing does not interrupt a spoken sentence");
        memory.SetWindow(0, Encode("stale warning"));
        memory.Control = 1;
        Require(reader.TryRead(out page) && !page.IsBlockingMovement && page.Windows.Count == 0,
            "closed buffers do not become stale dialogue");
        Require(tracker.Observe(page) is null, "closing produces no speech");
        memory.Control = 0;
        Require(reader.TryRead(out page) && page.IsBlockingMovement,
            "scripted pushback before the warning also suspends movement");
        memory.Module = 1;
        Require(!reader.TryRead(out _), "field ownership cannot publish old world text");
        memory.Module = 3;
        memory.SetWindow(6, Encode("Warning"));
        Require(reader.TryRead(out page) && tracker.Observe(page) == "Warning",
            "a later visible window is announced");
        memory.SetWindow(6, Encode("Warning"));
        memory.TearText = true;
        Require(!reader.TryRead(out _), "a changing rendered buffer is rejected");
        memory.TearText = false;
        memory.FailRead = true;
        Require(!reader.TryRead(out _), "unreadable state is not fabricated as a zero value");
    }

    /// <summary>
    /// FUN_0075EEBB (the world script's ASK) stores the first and last choice lines at E36108 and
    /// E36100 and runs FUN_007693A1, whose state 6 moves the cursor E36104 between them on Up and
    /// Down and draws it at line * 16 + 6. The prompt is read with its lines kept apart, the
    /// highlighted option named, and after that only a change of option is spoken.
    /// </summary>
    private static void TheChocoboDismountChoiceIsReadWithItsCursor()
    {
        var memory = new Memory();
        var reader = new WorldMapDialogueReader(memory);
        var tracker = new WorldMapDialogueTracker();
        memory.SetWindow(6, ChocoboDismount);
        memory.SetAsk(first: 2, last: 3, cursor: 2);
        Require(reader.TryRead(out var page), "the dismount prompt is readable");
        var window = page.Windows.Single();
        Require(window.Lines.SequenceEqual(["Cloud", "“Now for the Chocobo…”", "Send it back to the stables", "Turn it loose"]),
            $"its lines are kept apart: {string.Join(" | ", window.Lines)}");
        Require(window.Choice is { FirstLine: 2, LastLine: 3, SelectedLine: 2 } choice &&
                choice.Options.SequenceEqual(["Send it back to the stables", "Turn it loose"]) &&
                choice.SelectedOption == "Send it back to the stables",
            "the choice is the game's own lines 2..3, with the cursor on line 2");
        Require(page.IsBlockingMovement, "the choice holds the party");

        var opening = tracker.Observe(page);
        Require(opening == "Cloud “Now for the Chocobo…” " +
                           "Send it back to the stables, 1 of 2.",
            $"opening speaks the prompt and highlighted option once: {opening}");
        Require(tracker.Observe(page) is null, "waiting on the same option says nothing more");

        memory.SetAsk(first: 2, last: 3, cursor: 3);
        Require(reader.TryRead(out page) && tracker.Observe(page) == "Turn it loose, 2 of 2.",
            "Down: only the newly highlighted option");
        Require(tracker.Observe(page) is null, "and only once");
        memory.SetAsk(first: 2, last: 3, cursor: 2);
        Require(reader.TryRead(out page) && tracker.Observe(page) == "Send it back to the stables, 1 of 2.",
            "Up: back to the first");

        // Confirmed: FUN_007693A1 goes to state 7 and closes. Nothing is spoken for the closing
        // window, and the same prompt opened again is a new prompt.
        memory.SetWindow(7, ChocoboDismount);
        Require(reader.TryRead(out page) && tracker.Observe(page) is null, "the closing window says nothing");
        memory.SetWindow(0, ChocoboDismount);
        memory.Control = 1;
        Require(reader.TryRead(out page) && tracker.Observe(page) is null, "closed: nothing");
        memory.Control = 0;
        memory.SetWindow(6, ChocoboDismount);
        memory.SetAsk(first: 2, last: 3, cursor: 3);
        Require(reader.TryRead(out page) && tracker.Observe(page)?.EndsWith("Turn it loose, 2 of 2.", StringComparison.Ordinal) == true,
            "reopened, the prompt is spoken again from where its cursor is");
        tracker.Reset();
        Require(tracker.Observe(page)?.StartsWith("Cloud", StringComparison.Ordinal) == true, "and after a reset");
    }

    /// <summary>
    /// FUN_0075EEBB does not set E36104; FUN_007693A1's state 6 clamps it into first..last on its
    /// first frame. A sample taken before that may see the last prompt's cursor, so a cursor
    /// outside the choice is not spoken until it is in range.
    /// </summary>
    private static void AChoiceIsNotSpokenBeforeItsCursorIsInRange()
    {
        var memory = new Memory();
        var reader = new WorldMapDialogueReader(memory);
        var tracker = new WorldMapDialogueTracker();
        memory.SetWindow(6, ChocoboDismount);
        memory.SetAsk(first: 2, last: 3, cursor: 7);
        Require(reader.TryRead(out var page) && page.Windows.Single().Choice is { SelectedOption: null },
            "an out-of-range cursor names no option");
        Require(tracker.Observe(page) is null, "and nothing is spoken yet");
        memory.SetAsk(first: 2, last: 3, cursor: 2);
        Require(reader.TryRead(out page) && tracker.Observe(page)?.EndsWith("Send it back to the stables, 1 of 2.", StringComparison.Ordinal) == true,
            "once clamped, the prompt is spoken with its option");
    }

    /// <summary>
    /// FUN_00769836 clears CFF5D2 on open, and FUN_007693A1's state 6 sets it only on the frames
    /// it runs the cursor (CFF5E6 bit 0 clear). Until then no arrow is on screen, so nothing is
    /// said, even with the cursor already inside the choice.
    /// </summary>
    private static void AChoiceIsNotSpokenBeforeItsArrowIsDrawn()
    {
        var memory = new Memory();
        var reader = new WorldMapDialogueReader(memory);
        var tracker = new WorldMapDialogueTracker();
        memory.SetWindow(6, ChocoboDismount);
        memory.SetAsk(first: 2, last: 3, cursor: 2, shown: false);
        Require(reader.TryRead(out var page) && page.Windows.Single().Choice is { CursorShown: false },
            "the arrow is not drawn yet");
        Require(page.IsBlockingMovement, "the window still holds the party");
        Require(tracker.Observe(page) is null, "and nothing is spoken");
        memory.SetAsk(first: 2, last: 3, cursor: 2);
        Require(reader.TryRead(out page) && tracker.Observe(page)?.EndsWith("Send it back to the stables, 1 of 2.", StringComparison.Ordinal) == true,
            "once drawn, the prompt is spoken with its option");
    }

    private static byte[] Encode(string text) =>
        [.. text.Select(character => (byte)(character - 32)), 0xFF];

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = new();
        private int textReads;
        public byte Module = 3;
        public int Control;
        public bool TearText;
        public bool FailRead;

        /// <summary>DAT_00CC0964: 0xFF on the world map (FUN_0075EE50).</summary>
        public byte CurrentOwner = 0xFF;

        /// <summary>CC0960[0] of an open window: what FUN_00769836 copied from CC0964.</summary>
        public byte Owner = 0xFF;

        public void SetWindow(ushort state, byte[] encoded)
        {
            textReads = 0;
            for (var window = 0; window < 4; window++)
            {
                Write(WorldMapDialogueReader.WindowStateAddress + (uint)(window * 0x30),
                    BitConverter.GetBytes(window == 0 ? state : (ushort)0));
                Write(WorldMapDialogueReader.TextPointerAddress + (uint)(window * 4), BitConverter.GetBytes(0x00123400u));
            }

            var buffer = Enumerable.Repeat((byte)0xFF, 256).ToArray();
            encoded.CopyTo(buffer, 0);
            Write(WorldMapDialogueReader.TextBufferAddress, buffer);
            SetAsk(0, 0, 0, shown: false);
        }

        /// <summary>E36108, E36100, E36104, and CFF5D2 (the arrow FUN_007693A1 draws in state 6).</summary>
        public void SetAsk(ushort first, ushort last, ushort cursor, bool shown = true)
        {
            bytes[WorldMapDialogueReader.CursorShownAddress] = shown ? (byte)1 : (byte)0;
            Write(WorldMapDialogueReader.AskFirstLineAddress, BitConverter.GetBytes(first));
            Write(WorldMapDialogueReader.AskLastLineAddress, BitConverter.GetBytes(last));
            Write(WorldMapDialogueReader.AskCursorAddress, BitConverter.GetBytes(cursor));
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            if (FailRead) return false;
            if (address == (uint)WorldMapStateReader.AddressCurrentModule && destination.Length == 1)
            { destination[0] = Module; return true; }
            if (address == WorldMapDialogueReader.ControlAddress && destination.Length == 4)
            { BitConverter.GetBytes(Control).CopyTo(destination); return true; }
            if (address == WorldMapDialogueReader.CurrentWindowOwnerAddress && destination.Length == 1)
            { destination[0] = CurrentOwner; return true; }
            if (address >= (uint)FieldMessageReader.AddressFieldWindowStates &&
                address < (uint)FieldMessageReader.AddressFieldWindowStates + 4 && destination.Length == 1)
            {
                var window = address - (uint)FieldMessageReader.AddressFieldWindowStates;
                var state = BitConverter.ToUInt16([bytes[WorldMapDialogueReader.WindowStateAddress + window * 0x30],
                    bytes[WorldMapDialogueReader.WindowStateAddress + window * 0x30 + 1]]);
                destination[0] = window == 0 && state != 0 ? Owner : (byte)0xFF;
                return true;
            }

            for (var i = 0; i < destination.Length; i++)
            {
                if (!bytes.TryGetValue(address + (uint)i, out var value)) return false;
                destination[i] = value;
            }

            if (address == WorldMapDialogueReader.TextBufferAddress && ++textReads > 1 && TearText)
                destination[0]++;
            return true;
        }

        private void Write(uint address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (uint)i] = value[i];
        }
    }
}
