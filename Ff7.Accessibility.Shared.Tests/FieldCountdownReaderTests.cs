using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class FieldCountdownReaderTests
{
    public static void Run()
    {
        ReadsAStableVisibleNativeCountdown();
        RejectsStaleAndCountUpClockValuesAsCountdowns();
        FailsClosedForUnreadableOrTornNativeState();
        TreatsAStableNonFieldModuleAsInactive();
    }

    private static void ReadsAStableVisibleNativeCountdown()
    {
        var memory = CreateMemory(600, countdownMode: true, visibleWindow: 1);
        var reader = new FieldCountdownReader(memory);

        Equal(true, reader.TryReadSnapshot(out var snapshot), "stable countdown snapshot");
        Equal(true, snapshot.IsActive, "stable countdown active");
        Equal(600, snapshot.RemainingSeconds, "stable native remaining seconds");
        Equal((byte)0b0010, snapshot.ClockWindowMask, "stable native clock window mask");
        Equal(true, snapshot.OwnsWindow(1), "clock owns its visible window");
        Equal(false, snapshot.OwnsWindow(0), "clock does not own ordinary window");
    }

    private static void RejectsStaleAndCountUpClockValuesAsCountdowns()
    {
        var stale = CreateMemory(600, countdownMode: true, visibleWindow: null);
        var staleReader = new FieldCountdownReader(stale);
        Equal(true, staleReader.TryReadSnapshot(out var staleSnapshot), "stale seconds snapshot coherent");
        Equal(false, staleSnapshot.IsActive, "stale seconds without a visible clock are inactive");
        Equal((byte)0, staleSnapshot.ClockWindowMask, "stale seconds own no window");

        var countUp = CreateMemory(75, countdownMode: false, visibleWindow: 2);
        var countUpReader = new FieldCountdownReader(countUp);
        Equal(true, countUpReader.TryReadSnapshot(out var countUpSnapshot), "count-up clock snapshot coherent");
        Equal(false, countUpSnapshot.IsActive, "count-up clock is not a bomb countdown");
        Equal((byte)0b0100, countUpSnapshot.ClockWindowMask, "count-up clock window remains identifiable");
    }

    private static void FailsClosedForUnreadableOrTornNativeState()
    {
        var unreadable = CreateMemory(600, countdownMode: true, visibleWindow: 0);
        unreadable.Remove((uint)FieldCountdownReader.AddressRemainingSeconds + 3u);
        Equal(
            false,
            new FieldCountdownReader(unreadable).TryReadSnapshot(out _),
            "partial countdown seconds read fails explicitly");

        var stable = CreateMemory(600, countdownMode: true, visibleWindow: 0);
        var tornSeconds = new TearingLegacyAddressSpace(
            stable,
            (uint)FieldCountdownReader.AddressRemainingSeconds,
            BitConverter.GetBytes(599));
        Equal(
            false,
            new FieldCountdownReader(tornSeconds).TryReadSnapshot(out _),
            "changing seconds across the bookend is rejected");

        var tornType = new TearingLegacyAddressSpace(
            stable,
            FieldCountdownReader.WindowAddress(0, FieldCountdownReader.WindowSpecialDisplayTypeOffset),
            [0]);
        Equal(
            false,
            new FieldCountdownReader(tornType).TryReadSnapshot(out _),
            "changing clock ownership across the bookend is rejected");
    }

    private static void TreatsAStableNonFieldModuleAsInactive()
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)FieldPositionReader.AddressCurrentModule, [2]);
        memory.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)125));

        Equal(
            true,
            new FieldCountdownReader(memory).TryReadSnapshot(out var snapshot),
            "stable non-field lifecycle snapshot");
        Equal(false, snapshot.IsActive, "non-field lifecycle has no countdown");
        Equal((byte)0, snapshot.ClockWindowMask, "non-field lifecycle owns no clock window");
    }

    private static ContiguousLegacyAddressSpace CreateMemory(
        int seconds,
        bool countdownMode,
        int? visibleWindow)
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write(
            (uint)FieldPositionReader.AddressCurrentModule,
            [FieldPositionReader.FieldModule]);
        memory.Write(
            (uint)FieldPositionReader.AddressFieldId,
            BitConverter.GetBytes((ushort)125));
        memory.Write(
            (uint)FieldCountdownReader.AddressTimerDirectionFlags,
            [countdownMode ? (byte)0 : FieldCountdownReader.CountUpFlag]);
        memory.Write(
            (uint)FieldCountdownReader.AddressRemainingSeconds,
            BitConverter.GetBytes(seconds));

        for (var windowId = 0; windowId < FieldCountdownReader.WindowCount; windowId++)
        {
            memory.Write(
                (uint)(FieldMessageReader.AddressFieldWindowStates + windowId),
                [windowId == visibleWindow ? (byte)0 : FieldMessageReader.FreeWindowState]);
            memory.Write(
                FieldCountdownReader.WindowAddress(
                    windowId,
                    FieldCountdownReader.WindowSpecialDisplayTypeOffset),
                [windowId == visibleWindow ? FieldCountdownReader.ClockDisplayType : (byte)0]);
            memory.Write(
                FieldCountdownReader.WindowAddress(
                    windowId,
                    FieldCountdownReader.WindowDrawableOffset),
                BitConverter.GetBytes((ushort)(windowId == visibleWindow ? 1 : 0)));
        }

        return memory;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
