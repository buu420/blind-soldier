using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;
using System.Buffers.Binary;
using System.Text;

internal static class FfnxPopupSpeechTests
{
    internal static void Run()
    {
        SpeaksEachVisiblePopupGenerationOnce();
        ReannouncesSameTextWhenItsTtlRestarts();
        NormalizesOnlyInvisibleWhitespace();
        RejectsInactiveAndEmptySnapshots();
        ResetForgetsThePreviousGeneration();
        ReadsOneStableNativePopupSnapshot();
        RejectsTornAndUnterminatedNativePopupSnapshots();
        UsesTheVerifiedFfnx1243PopupLayout();
    }

    private static void SpeaksEachVisiblePopupGenerationOnce()
    {
        var tracker = new FfnxPopupSpeechTracker();

        Equal(
            "Current Speedhack: ENABLED",
            tracker.Observe(new FfnxPopupSnapshot(
                "Current Speedhack: ENABLED",
                Ttl: 120,
                Color: 0xFFFFFFFF)),
            "first FFNx popup generation");
        Equal(
            null,
            tracker.Observe(new FfnxPopupSnapshot(
                "Current Speedhack: ENABLED",
                Ttl: 119,
                Color: 0xFFFFFFFF)),
            "unchanged FFNx popup frame");
        Equal(
            "Battle mode: ENABLED",
            tracker.Observe(new FfnxPopupSnapshot(
                "Battle mode: ENABLED",
                Ttl: 118,
                Color: 0xFFFFFFFF)),
            "changed FFNx popup text");
    }

    private static void ReannouncesSameTextWhenItsTtlRestarts()
    {
        var tracker = new FfnxPopupSpeechTracker();
        var popup = new FfnxPopupSnapshot(
            "Current Speedhack: 0.5x",
            Ttl: 120,
            Color: 0xFF00FFFF);

        Equal("Current Speedhack: 0.5x", tracker.Observe(popup), "initial speed popup");
        Equal(
            null,
            tracker.Observe(popup with { Ttl = 35 }),
            "speed popup countdown");
        Equal(
            "Current Speedhack: 0.5x",
            tracker.Observe(popup),
            "same visible text with restarted TTL is a new popup generation");

        Equal(
            null,
            tracker.Observe(popup with { Ttl = 0 }),
            "expired popup is silent");
        Equal(
            "Current Speedhack: 0.5x",
            tracker.Observe(popup),
            "same text after expiry is a new popup generation");
    }

    private static void NormalizesOnlyInvisibleWhitespace()
    {
        var tracker = new FfnxPopupSpeechTracker();
        Equal(
            "Voice auto text mode: DISABLED",
            tracker.Observe(new FfnxPopupSnapshot(
                " Voice auto text\r\nmode:\tDISABLED ",
                Ttl: 60,
                Color: 7)),
            "FFNx popup whitespace normalization");
    }

    private static void RejectsInactiveAndEmptySnapshots()
    {
        var tracker = new FfnxPopupSpeechTracker();
        Equal(null, tracker.Observe(null), "missing FFNx popup");
        Equal(
            null,
            tracker.Observe(new FfnxPopupSnapshot(
                "Current Speedhack: ENABLED",
                Ttl: 0,
                Color: 0)),
            "expired FFNx popup");
        Equal(
            null,
            tracker.Observe(new FfnxPopupSnapshot(
                "   \r\n\t ",
                Ttl: 120,
                Color: 0)),
            "blank FFNx popup");
    }

    private static void ResetForgetsThePreviousGeneration()
    {
        var tracker = new FfnxPopupSpeechTracker();
        var popup = new FfnxPopupSnapshot("FMV Skipped", Ttl: 60, Color: 0);
        Equal("FMV Skipped", tracker.Observe(popup), "initial FMV popup");
        tracker.Reset();
        Equal("FMV Skipped", tracker.Observe(popup), "popup after tracker reset");
    }

    private static void ReadsOneStableNativePopupSnapshot()
    {
        const uint moduleBase = 0x10000000;
        var memory = new PopupMemory();
        memory.WriteUInt32(
            moduleBase + FfnxPopupStateReader.PopupTtlRva,
            120);
        memory.WriteUInt32(
            moduleBase + FfnxPopupStateReader.PopupColorRva,
            0xFF00FFFF);
        memory.WriteCString(
            moduleBase + FfnxPopupStateReader.PopupMessageRva,
            "Current Speedhack: 0.5x");

        var reader = new FfnxPopupStateReader(moduleBase, memory);
        Equal(true, reader.TryRead(out var snapshot), "stable FFNx popup read");
        Equal("Current Speedhack: 0.5x", snapshot.Text, "native FFNx popup text");
        Equal((uint)120, snapshot.Ttl, "native FFNx popup TTL");
        Equal(0xFF00FFFFu, snapshot.Color, "native FFNx popup color");
    }

    private static void RejectsTornAndUnterminatedNativePopupSnapshots()
    {
        const uint moduleBase = 0x20000000;
        var torn = new PopupMemory
        {
            TtlReadOverride = readIndex => readIndex == 0 ? 120u : 119u
        };
        torn.WriteUInt32(moduleBase + FfnxPopupStateReader.PopupTtlRva, 120);
        torn.WriteUInt32(moduleBase + FfnxPopupStateReader.PopupColorRva, 7);
        torn.WriteCString(
            moduleBase + FfnxPopupStateReader.PopupMessageRva,
            "Battle mode: ENABLED");
        var tornReader = new FfnxPopupStateReader(moduleBase, torn);
        Equal(false, tornReader.TryRead(out _), "torn FFNx popup read");
        Equal(
            false,
            tornReader.LastReadWasDefinitelyHidden,
            "torn frame does not retire the active popup generation");

        var hidden = new PopupMemory();
        hidden.WriteUInt32(moduleBase + FfnxPopupStateReader.PopupTtlRva, 0);
        var hiddenReader = new FfnxPopupStateReader(moduleBase, hidden);
        Equal(false, hiddenReader.TryRead(out _), "hidden FFNx popup read");
        Equal(
            true,
            hiddenReader.LastReadWasDefinitelyHidden,
            "two zero TTL reads retire the active popup generation");

        var unterminated = new PopupMemory();
        unterminated.WriteUInt32(moduleBase + FfnxPopupStateReader.PopupTtlRva, 120);
        unterminated.WriteUInt32(moduleBase + FfnxPopupStateReader.PopupColorRva, 7);
        unterminated.Fill(
            moduleBase + FfnxPopupStateReader.PopupMessageRva,
            FfnxPopupStateReader.PopupMessageCapacity,
            (byte)'A');
        Equal(
            false,
            new FfnxPopupStateReader(moduleBase, unterminated).TryRead(out _),
            "unterminated FFNx popup read");
    }

    private static void UsesTheVerifiedFfnx1243PopupLayout()
    {
        Equal(0x0210BCB8u, FfnxPopupStateReader.PopupMessageRva, "FFNx popup message RVA");
        Equal(0x0210C0B8u, FfnxPopupStateReader.PopupTtlRva, "FFNx popup TTL RVA");
        Equal(0x0210C0BCu, FfnxPopupStateReader.PopupColorRva, "FFNx popup color RVA");
        Equal(
            "7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A",
            FfnxPopupStateReader.SupportedModuleSha256,
            "FFNx 1.24.3 module SHA-256");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class PopupMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private int ttlReadCount;

        internal Func<int, uint>? TtlReadOverride { get; init; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == 0 || destination.IsEmpty)
            {
                return false;
            }

            if (destination.Length == sizeof(uint)
                && TtlReadOverride is not null)
            {
                var expectedTtlAddress =
                    0x20000000u + FfnxPopupStateReader.PopupTtlRva;
                if (virtualAddress == expectedTtlAddress)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination,
                        TtlReadOverride(ttlReadCount++));
                    return true;
                }
            }

            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(
                        checked(virtualAddress + (uint)index),
                        out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }

        internal void WriteUInt32(uint address, uint value)
        {
            Span<byte> data = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            Write(address, data);
        }

        internal void WriteCString(uint address, string value)
        {
            var data = Encoding.UTF8.GetBytes(value + "\0");
            Write(address, data);
            Fill(
                checked(address + (uint)data.Length),
                FfnxPopupStateReader.PopupMessageCapacity - data.Length,
                0);
        }

        internal void Fill(uint address, int count, byte value)
        {
            for (var index = 0; index < count; index++)
            {
                bytes[checked(address + (uint)index)] = value;
            }
        }

        private void Write(uint address, ReadOnlySpan<byte> data)
        {
            for (var index = 0; index < data.Length; index++)
            {
                bytes[checked(address + (uint)index)] = data[index];
            }
        }
    }
}
