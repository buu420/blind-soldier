using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The seam between the native window read and anything that holds an activity open
/// across frames.
///
/// <para><see cref="FieldActivityStateReader.ReadNumericWindow"/> answers one question -
/// is there a number on screen - and returns the same "no" for a window that closed, a
/// field the party left, and a read that simply failed. That is the right answer for the
/// cliff, which asks again every frame and holds nothing. It is the wrong answer for the
/// mansion safe: a torn read is not the safe closing, and treating it as one ends the
/// attempt, hands the clock back mid-dial, and announces the safe again from the top a
/// frame later.</para>
///
/// <para><see cref="FieldActivityStateReader.TryReadNumericWindow"/> keeps the two apart.
/// False means nothing is known. True means the game was read coherently, and the window
/// it hands back then says whether a number is actually being drawn - a closed window,
/// another display type, another loaded field and another module are all coherent
/// answers of "no".</para>
///
/// <para>Addresses are the ones root's fresh Ghidra run confirmed: WSPCL 0061FD5C writes
/// the display type to CFF5D3 + window * 0x30, WNUMB 0061FE26 writes the value to
/// CFF5D8 + stride and the digit count to CFF5D5 + stride, and MESSAGE 00631586 claims
/// the owner at CC0960 + window.</para>
/// </summary>
internal static class FieldActivityNumericWindowReadTests
{
    private const int SafeRoom = ShinraMansionSafeDialReadout.FieldId;
    private const int DialWindow = ShinraMansionSafeDialReadout.DialWindowId;

    private static readonly DateTime Start = new(2026, 9, 21, 20, 59, 40, DateTimeKind.Utc);

    public static void Run()
    {
        ReadsTheNumberTheDialIsDrawing();
        ACoherentlyClosedWindowIsKnownToBeClosed();
        ACoherentlyDifferentDisplayTypeIsNotANumber();
        AnotherLoadedFieldIsADeparture();
        AnotherModuleIsADeparture();
        AFailedReadIsNotAClosedWindow();
        ATornReadIsNotAClosedWindow();
        TheOldReaderKeepsItsOldAnswers();
        ATornReadLeavesTheDialAttemptStanding();
        AClosedWindowEndsTheDialAttempt();
        ADepartureEndsTheDialAttempt();
        TheGpExchangeSaysTheAmountItDraws();
        TheGpExchangeDeliversOnlyTheNewestAmount();
    }

    /// <summary>
    /// gldst (496): after "Hey, thanks / 1 GP is 100 gil." ayasii draws the amount in numeric
    /// window 0 and its cost in numeric window 1, and kawari moves them by 10 GP on Up and Down.
    /// The October 3 log has the dialogue readout saying only "gil" there, three times: the
    /// numbers are not text. The readout says both, from the windows themselves, as they change.
    /// </summary>
    private static void TheGpExchangeSaysTheAmountItDraws()
    {
        const int Station = FieldActivityReadout.GoldSaucerStationFieldId;
        Equal(true, FieldActivityReadout.HasActivity(Station), "the station is an activity field");
        Equal("0,1", string.Join(",", FieldActivityReadout.ObservedNumericWindows(Station)), "both of its numeric windows");
        Equal(0, FieldActivityReadout.ObservedNumericWindows(FieldActivityReadout.CliffLowerFieldId).Count,
            "the cliff keeps its own single window");

        var memory = new GpMemory();
        var reader = new FieldActivityStateReader(memory);
        var readout = new FieldActivityReadout();
        FieldActivityCue Tick() => readout.Observe(GpObservation(reader), Start);

        var opened = Tick();
        Equal("0 GP, 0 gil.", opened.Speech, "the exchange opens on nothing");
        Equal(true, opened.ReplacesEarlierLine, "a reading the next one makes worthless");
        Equal(false, opened.IsPending, "nothing is pressed or held for the player");
        Equal("0 GP, 0 gil.", readout.CurrentReplaceableLine, "the newest reading is the one owed");
        Equal(null, Tick().Speech, "unchanged, nothing more");

        memory.Set(10);
        Equal("10 GP, 1000 gil.", Tick().Speech, "Up: the amount and its cost");
        Equal("10 GP, 1000 gil.", readout.Describe(GpObservation(reader)), "the repeat key says the amount now on screen");

        memory.TearWindow = FieldActivityReadout.GpExchangeAmountWindowId;
        Equal(null, Tick().Speech, "a torn read says nothing");
        Equal("10 GP, 1000 gil.", readout.CurrentReplaceableLine, "and forgets nothing");
        memory.TearWindow = -1;
        Equal(null, Tick().Speech, "recovered, the same amount is not said again");

        memory.Owner = (byte)FieldActivityStateReader.FreeWindowState;
        Equal(null, Tick().Speech, "OK or Cancel closes both windows; nothing is said");
        Equal(null, readout.CurrentReplaceableLine, "nothing is owed for a closed exchange");
        memory.Owner = 2;
        memory.Set(0);
        Equal("0 GP, 0 gil.", Tick().Speech, "a later exchange is said again");

        memory.DisplayType = 0;
        Equal(null, Tick().Speech, "an ordinary window in the same slot is not an amount");

        memory.DisplayType = 2;
        Tick();
        readout.Observe(GpObservation(reader) with
        {
            FieldId = FieldActivityReadout.ClockRoomFieldId,
            Models = [new(FieldActivityReadout.ClockLongHandEntityId, FieldActivityReadStatus.Hidden, default),
                      new(FieldActivityReadout.ClockShortHandEntityId, FieldActivityReadStatus.Hidden, default)]
        }, Start);
        Equal(null, readout.CurrentReplaceableLine, "a hidden clock cannot retain a former GP exchange amount");
    }

    /// <summary>
    /// Holding Up adds 10 GP every few frames. Through the shared newest-line delivery both
    /// hosts use, the dialogue just spoken is not cut off and only the amount on screen when it
    /// has finished is said - not a queue of every step.
    /// </summary>
    private static void TheGpExchangeDeliversOnlyTheNewestAmount()
    {
        var memory = new GpMemory();
        var reader = new FieldActivityStateReader(memory);
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        var spoken = new List<string>();
        delivery.NoteOtherSpeech("“Hey, thanks 1 GP is 100 gil.”", Start);
        foreach (var (amount, at) in new[] { (0, 0), (10, 80), (20, 160), (30, 240) })
        {
            memory.Set(amount);
            var now = Start.AddMilliseconds(at);
            FieldActivityClockHost.Deliver(readout.Observe(GpObservation(reader), now), readout, delivery, now,
                () => true, () => true, text => { spoken.Add(text); return true; }, isSpeaking: null);
        }

        Equal(0, spoken.Count, "the exchange's own line waits for the dialogue it follows");
        var later = Start.AddSeconds(4);
        FieldActivityClockHost.Deliver(readout.Observe(GpObservation(reader), later), readout, delivery, later,
            () => true, () => true, text => { spoken.Add(text); return true; }, isSpeaking: null);
        Equal("30 GP, 3000 gil.", string.Join(" | ", spoken), "then only the amount now on screen");
    }

    private static FieldActivityObservation GpObservation(FieldActivityStateReader reader) =>
        FieldActivityObservationBuilder.Build(
            new FieldPositionSnapshot(FieldPositionReader.FieldModule, FieldActivityReadout.GoldSaucerStationFieldId,
                0, -1051, 776, 0, 30, 0),
            reader,
            lineStateReader: null,
            boundaryStateReader: null,
            control: default,
            isPlayerControlled: false,
            gameMoment: 1250,
            pillarGate: -1,
            readTemporaryByte: null);

    private static void ReadsTheNumberTheDialIsDrawing()
    {
        var memory = Dial(36);
        var reader = new FieldActivityStateReader(memory);

        Equal(true, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "the dial is read coherently");
        Equal(true, window.IsUsable, "the dial is a numeric window on screen");
        Equal(36, window.Value, "the value is the one WNUMB wrote");
        Equal(2, window.DigitLimit, "the digit count is the one WNUMB wrote");
    }

    private static void ACoherentlyClosedWindowIsKnownToBeClosed()
    {
        // The leader's script closes window 0 and clears its display type on the way
        // out. Owner back to the free value is the game saying so.
        var memory = Dial(36);
        memory.Owner = (byte)FieldActivityStateReader.FreeWindowState;
        var reader = new FieldActivityStateReader(memory);

        Equal(true, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "a closed window is a coherent answer");
        Equal(false, window.IsUsable, "a closed window is not a number on screen");
    }

    private static void ACoherentlyDifferentDisplayTypeIsNotANumber()
    {
        // This is the safe's own question window: window 0 again, owned, but ordinary
        // dialogue rather than a numeric display.
        var memory = Dial(36);
        memory.DisplayType = 0;
        var reader = new FieldActivityStateReader(memory);

        Equal(true, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "an ordinary window is a coherent answer");
        Equal(false, window.IsUsable, "an ordinary window is not a number on screen");
    }

    private static void AnotherLoadedFieldIsADeparture()
    {
        var memory = Dial(36);
        memory.LoadedFieldId = 300;
        var reader = new FieldActivityStateReader(memory);

        Equal(true, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "another field read cleanly is a coherent answer");
        Equal(false, window.IsUsable, "another field is not this field's window");
    }

    private static void AnotherModuleIsADeparture()
    {
        var memory = Dial(36);
        memory.Module = FieldPositionReader.FieldModule + 1;
        var reader = new FieldActivityStateReader(memory);

        Equal(true, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "another module read cleanly is a coherent answer");
        Equal(false, window.IsUsable, "another module is not a field window");
    }

    private static void AFailedReadIsNotAClosedWindow()
    {
        var memory = Dial(36);
        memory.IsReadable = false;
        var reader = new FieldActivityStateReader(memory);

        Equal(false, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "a failed read is not an answer");
        Equal(false, window.IsUsable, "a failed read hands back nothing");
    }

    private static void ATornReadIsNotAClosedWindow()
    {
        // The dial moves once per native frame, so the two samples this reader takes
        // can legitimately straddle a write. That is a read to discard, not a window
        // to declare closed.
        var memory = Dial(36);
        memory.ValuesPerSample = new Queue<int>(new[] { 36, 37 });
        var reader = new FieldActivityStateReader(memory);

        Equal(false, reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window), "two samples that disagree are not an answer");
        Equal(false, window.IsUsable, "a torn read hands back nothing");
    }

    private static void TheOldReaderKeepsItsOldAnswers()
    {
        // The cliff asks again every frame and holds nothing, so it wants the old
        // shape: a number when there is one, and default for every kind of no.
        var open = new FieldActivityStateReader(Dial(41));
        Equal(41, open.ReadNumericWindow(SafeRoom, DialWindow).Value, "an open window still reads its value");

        var closed = Dial(41);
        closed.Owner = (byte)FieldActivityStateReader.FreeWindowState;
        Equal(default, new FieldActivityStateReader(closed).ReadNumericWindow(SafeRoom, DialWindow), "a closed window still reads as default");

        var torn = Dial(41);
        torn.ValuesPerSample = new Queue<int>(new[] { 41, 42 });
        Equal(default, new FieldActivityStateReader(torn).ReadNumericWindow(SafeRoom, DialWindow), "a torn read still reads as default");

        var gone = Dial(41);
        gone.Module = FieldPositionReader.FieldModule + 1;
        Equal(default, new FieldActivityStateReader(gone).ReadNumericWindow(SafeRoom, DialWindow), "a departure still reads as default");
    }

    private static void ATornReadLeavesTheDialAttemptStanding()
    {
        var memory = Dial(36);
        var reader = new FieldActivityStateReader(memory);
        var dial = new ShinraMansionSafeDialReadout();

        Equal("Safe dial 36.", Tick(dial, reader, Start).Speech, "the dial opens");

        memory.IsReadable = false;
        var torn = Tick(dial, reader, Start.AddMilliseconds(35));
        Equal(null, torn.Speech, "a torn read says nothing");
        Equal(true, torn.IsDialOnScreen, "a torn read does not close the dial");
        Equal(true, dial.SuppressesCountdownSpeech, "the clock cannot take the voice back mid-dial");
        Equal(null, dial.CurrentNumber, "a torn read leaves no number to vouch for");
        Equal(null, dial.Describe(), "the repeat key falls back rather than inventing a number");

        memory.IsReadable = true;
        var recovered = Tick(dial, reader, Start.AddMilliseconds(70));
        Equal(null, recovered.Speech, "recovery does not announce the safe again");
        Equal(true, recovered.IsDialOnScreen, "the attempt is still the same attempt");
        Equal(36, dial.CurrentNumber, "the number is vouched for again");
    }

    private static void AClosedWindowEndsTheDialAttempt()
    {
        var memory = Dial(36);
        var reader = new FieldActivityStateReader(memory);
        var dial = new ShinraMansionSafeDialReadout();
        Tick(dial, reader, Start);

        memory.Owner = (byte)FieldActivityStateReader.FreeWindowState;
        var closed = Tick(dial, reader, Start.AddMilliseconds(35));

        Equal(false, closed.IsDialOnScreen, "a coherently closed window ends the attempt");
        Equal(false, dial.SuppressesCountdownSpeech, "the clock gets its voice back");
        Equal(false, dial.OwnsWindow(DialWindow), "the Fail page is ordinary dialogue again");
    }

    private static void ADepartureEndsTheDialAttempt()
    {
        var memory = Dial(36);
        var reader = new FieldActivityStateReader(memory);
        var dial = new ShinraMansionSafeDialReadout();
        Tick(dial, reader, Start);

        memory.Module = FieldPositionReader.FieldModule + 1;
        var gone = Tick(dial, reader, Start.AddMilliseconds(35));

        Equal(false, gone.IsDialOnScreen, "leaving the field module ends the attempt");
        Equal(false, dial.SuppressesCountdownSpeech, "nothing is owned outside the field");
    }

    /// <summary>One runtime tick: read, then observe or declare the read unusable.</summary>
    private static ShinraMansionSafeDialCue Tick(
        ShinraMansionSafeDialReadout dial,
        FieldActivityStateReader reader,
        DateTime now) =>
        reader.TryReadNumericWindow(SafeRoom, DialWindow, out var window)
            ? dial.Observe(SafeRoom, window, now)
            : dial.ObserveUnavailable();

    private static DialMemory Dial(int value) => new(value);

    /// <summary>
    /// Field 299 loaded with the safe dial on screen, and every knob the cases above
    /// need to turn one at a time.
    /// </summary>
    private sealed class DialMemory(int value) : ILegacyAddressSpace
    {
        private const uint ScriptSection = 0x00A00000;

        private int drawnValue = value;

        public int Module { get; set; } = FieldPositionReader.FieldModule;
        public int LoadedFieldId { get; set; } = SafeRoom;
        public byte Owner { get; set; } = 3;
        public byte DisplayType { get; set; } = FieldActivityStateReader.NumericWindowDisplayType;
        public byte DigitLimit { get; set; } = 2;
        public bool IsReadable { get; set; } = true;

        /// <summary>
        /// One value per sample, for straddling a native write. Empty means the value
        /// never changes, which is the ordinary case.
        /// </summary>
        public Queue<int>? ValuesPerSample { get; set; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (!IsReadable)
            {
                return false;
            }

            var stride = (uint)(DialWindow * FieldActivityStateReader.WindowRecordStride);
            if (Matches(virtualAddress, destination, (uint)FieldPositionReader.AddressCurrentModule, 1))
            {
                destination[0] = (byte)Module;
                return true;
            }

            if (Matches(virtualAddress, destination, (uint)FieldPositionReader.AddressFieldId, 2))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)LoadedFieldId);
                return true;
            }

            if (Matches(virtualAddress, destination, (uint)FieldScriptControllerReader.AddressFieldScriptPointer, 4))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination, ScriptSection);
                return true;
            }

            if (Matches(virtualAddress, destination, ScriptSection + FieldActivityStateReader.ScriptHeaderEntityCountOffset, 1))
            {
                destination[0] = 13;
                return true;
            }

            if (Matches(virtualAddress, destination, (uint)(FieldActivityStateReader.AddressWindowStates + DialWindow), 1))
            {
                destination[0] = Owner;
                return true;
            }

            if (Matches(virtualAddress, destination, (uint)FieldActivityStateReader.AddressWindowDisplayType + stride, 1))
            {
                destination[0] = DisplayType;
                return true;
            }

            if (Matches(virtualAddress, destination, (uint)FieldActivityStateReader.AddressWindowDigitLimit + stride, 1))
            {
                destination[0] = DigitLimit;
                return true;
            }

            if (Matches(virtualAddress, destination, (uint)FieldActivityStateReader.AddressWindowNumericValue + stride, 4))
            {
                if (ValuesPerSample is { Count: > 0 } queued)
                {
                    drawnValue = queued.Dequeue();
                }

                BinaryPrimitives.WriteInt32LittleEndian(destination, drawnValue);
                return true;
            }

            // Anything this fixture was not asked about is genuinely not mapped, which
            // keeps an accidental new read from silently passing as zeroes.
            return false;
        }

        private static bool Matches(uint address, Span<byte> destination, uint expected, int length) =>
            address == expected && destination.Length == length;
    }

    /// <summary>gldst loaded with the GP exchange's two numeric windows on screen.</summary>
    private sealed class GpMemory : ILegacyAddressSpace
    {
        private const uint ScriptSection = 0x00A00000;
        private readonly int[] values = new int[FieldMessageReader.WindowCount];
        private int tearReads;

        public byte Owner { get; set; } = 2;
        public byte DisplayType { get; set; } = FieldActivityStateReader.NumericWindowDisplayType;
        public int TearWindow { get; set; } = -1;

        /// <summary>What kawari's WNUMBs write: the amount in window 0, its cost in gil in window 1.</summary>
        public void Set(int gp)
        {
            values[FieldActivityReadout.GpExchangeAmountWindowId] = gp;
            values[FieldActivityReadout.GpExchangeCostWindowId] = gp * 100;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == (uint)FieldPositionReader.AddressCurrentModule && destination.Length == 1)
            {
                destination[0] = (byte)FieldPositionReader.FieldModule;
                return true;
            }

            if (virtualAddress == (uint)FieldPositionReader.AddressFieldId && destination.Length == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)FieldActivityReadout.GoldSaucerStationFieldId);
                return true;
            }

            if (virtualAddress == (uint)FieldScriptControllerReader.AddressFieldScriptPointer && destination.Length == 4)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination, ScriptSection);
                return true;
            }

            if (virtualAddress == ScriptSection + FieldActivityStateReader.ScriptHeaderEntityCountOffset && destination.Length == 1)
            {
                destination[0] = 35;
                return true;
            }

            for (var window = 0; window < FieldMessageReader.WindowCount; window++)
            {
                var stride = (uint)(window * FieldActivityStateReader.WindowRecordStride);
                if (virtualAddress == (uint)(FieldActivityStateReader.AddressWindowStates + window) && destination.Length == 1)
                {
                    destination[0] = window <= FieldActivityReadout.GpExchangeCostWindowId
                        ? Owner
                        : (byte)FieldActivityStateReader.FreeWindowState;
                    return true;
                }

                if (virtualAddress == (uint)FieldActivityStateReader.AddressWindowDisplayType + stride && destination.Length == 1)
                {
                    destination[0] = DisplayType;
                    return true;
                }

                if (virtualAddress == (uint)FieldActivityStateReader.AddressWindowDigitLimit + stride && destination.Length == 1)
                {
                    destination[0] = (byte)(window == FieldActivityReadout.GpExchangeAmountWindowId ? 3 : 5);
                    return true;
                }

                if (virtualAddress == (uint)FieldActivityStateReader.AddressWindowNumericValue + stride && destination.Length == 4)
                {
                    var value = values[window] + (window == TearWindow && ++tearReads % 2 == 0 ? 10 : 0);
                    BinaryPrimitives.WriteInt32LittleEndian(destination, value);
                    return true;
                }
            }

            return false;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
