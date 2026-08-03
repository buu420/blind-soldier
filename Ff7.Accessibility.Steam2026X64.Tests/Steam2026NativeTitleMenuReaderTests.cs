using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal static class Steam2026NativeTitleMenuReaderTests
{
    internal static void Run()
    {
        ReadsEveryBinaryVerifiedTitleRow();
        RejectsForeignModulesAndWrongGeometry();
        RejectsTitleBeforeInputBecomesActive();
        ReportsExactRejectedRuntimeState();
        RejectsTornState();
    }

    private static void ReportsExactRejectedRuntimeState()
    {
        var foreignModule = CreateTitleMemory(cursor: 0, module: 5);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(foreignModule).TryRead(out _, out var moduleDiagnostic),
            "foreign module diagnostic capture");
        Equal(true, moduleDiagnostic.Contains("module=5", StringComparison.Ordinal),
            "foreign module diagnostic value");

        var wrongTitleState = CreateTitleMemory(cursor: 0, titleState: 6);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(wrongTitleState).TryRead(out _, out var stateDiagnostic),
            "title state diagnostic capture");
        Equal(true, stateDiagnostic.Contains("titleState=6", StringComparison.Ordinal),
            "title state diagnostic value");

        var unreadable = new Memory();
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(unreadable).TryRead(out _, out var unreadableDiagnostic),
            "unreadable title diagnostic capture");
        Equal(true, unreadableDiagnostic.Contains("module unreadable", StringComparison.Ordinal),
            "unreadable title diagnostic stage");
    }

    private static void RejectsTitleBeforeInputBecomesActive()
    {
        var wrongTitleState = CreateTitleMemory(cursor: 0, titleState: 6);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(wrongTitleState).TryRead(out _),
            "noninteractive title state");

        var fadeIncomplete = CreateTitleMemory(cursor: 0, inputActive: 0);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(fadeIncomplete).TryRead(out _),
            "title input before fade completion");

        var exitingTitle = CreateTitleMemory(cursor: 0, exitState: 1);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(exitingTitle).TryRead(out _),
            "title exit transition");
    }

    private static void ReadsEveryBinaryVerifiedTitleRow()
    {
        var expected = new[]
        {
            "New Game",
            "Continue?",
            "Additional Credits",
            "Quit"
        };

        for (var cursor = 0; cursor < expected.Length; cursor++)
        {
            var memory = CreateTitleMemory(cursor);
            var reader = new Steam2026NativeTitleMenuReader(memory);

            Equal(true, reader.TryRead(out var selection), $"title row {cursor} capture");
            Equal(cursor, selection.Index, $"title row {cursor} index");
            Equal(expected[cursor], selection.Text, $"title row {cursor} text");
            Equal($"steam2026-title-menu\u001f{cursor}", selection.Key, $"title row {cursor} key");
        }
    }

    private static void RejectsForeignModulesAndWrongGeometry()
    {
        var foreignModule = CreateTitleMemory(cursor: 0, module: 5);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(foreignModule).TryRead(out _),
            "foreign module title-shaped state");

        var legacyTwoRowGeometry = CreateTitleMemory(cursor: 0, rows: 2);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(legacyTwoRowGeometry).TryRead(
                out _,
                out var geometryDiagnostic),
            "old two-row title geometry");
        Equal(true, geometryDiagnostic.Contains("rows=2", StringComparison.Ordinal),
            "old two-row geometry diagnostic value");

        var invalidCursor = CreateTitleMemory(cursor: 4);
        Equal(
            false,
            new Steam2026NativeTitleMenuReader(invalidCursor).TryRead(out _),
            "cursor outside four verified rows");
    }

    private static void RejectsTornState()
    {
        var stable = CreateTitleMemory(cursor: 0);
        var replacement = CreateTitleMemory(cursor: 1);
        var tearing = new TearingMemory(
            stable,
            replacement,
            Steam2026NativeTitleMenuReader.WidgetAddress + 4);

        Equal(
            false,
            new Steam2026NativeTitleMenuReader(tearing).TryRead(out _),
            "title cursor transition tearing");
    }

    private static Memory CreateTitleMemory(
        int cursor,
        byte module = Steam2026NativeTitleMenuReader.TitleModule,
        int rows = Steam2026NativeTitleMenuReader.RowCount,
        int titleState = 7,
        int inputActive = 1,
        int exitState = 0)
    {
        var memory = new Memory();
        memory.WriteByte(Steam2026NativeTitleMenuReader.CurrentModuleAddress, module);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.TitleStateAddress, titleState);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.InputActiveAddress, inputActive);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.ExitStateAddress, exitState);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x00, 0);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x04, cursor);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x08, 1);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x0C, rows);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x14, 0);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x24, 0);
        memory.WriteInt32(Steam2026NativeTitleMenuReader.WidgetAddress + 0x30, 0);
        return memory;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        internal void WriteByte(uint address, byte value) => bytes[address] = value;

        internal void WriteInt32(uint address, int value)
        {
            Span<byte> encoded = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(encoded, value);
            for (var index = 0; index < encoded.Length; index++)
            {
                bytes[address + (uint)index] = encoded[index];
            }
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class TearingMemory(
        ILegacyAddressSpace first,
        ILegacyAddressSpace second,
        uint watchedAddress) : ILegacyAddressSpace
    {
        private int watchedReads;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == watchedAddress)
            {
                watchedReads++;
            }

            var source = watchedReads < 2 ? first : second;
            return source.TryRead(virtualAddress, destination);
        }
    }
}
