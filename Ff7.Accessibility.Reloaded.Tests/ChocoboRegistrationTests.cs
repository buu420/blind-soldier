using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class ChocoboRegistrationTests
{
    // Native chcounter message 86. FE E2 copies a bounded string from scenario
    // memory; the log's selected row 4 is Locke, not a visibly blank row.
    private const string Registration = "E0FEE220030600E7E0FEE226030600E7E0FEE22C030600E7E0FEE232030600E7E0FEE238030600E7E0FEE23E030600E7E02E45564552004D494E44FF";
    private static readonly string[] Names = ["Rosa", "Rydia", "Edge", "Celes", "Locke", "Boco"];

    public static void Run()
    {
        ReadsSelectedNames();
        RejectsUnresolvedArchiveChoice();
        RejectsUnreadableName();
        RejectsChangingName();
        RejectsFieldChangeDuringNameRead();
        PreservesAnActuallyEmptyRow();
        ChecksInstalledLayouts();
        Console.WriteLine("PASS chocobo registration: selected names, unavailable/changing memory, field transitions and genuine blank rows.");
    }

    private static void ReadsSelectedNames()
    {
        var memory = new Memory();
        Require(new FieldMessageReader(memory).TryReadMessagePagesById(86, out var pages), "registration page is readable");
        Require(FieldAskTextFormatter.TryResolveChoicePage(pages, 0, 6, out var lines), "all seven native registration rows");
        Equal("Rosa|Rydia|Edge|Celes|Locke|Boco|Never mind", string.Join('|', lines), "names keep their native row order");
        var tracker = new FieldAskChoiceSpeechTracker();
        tracker.Observe(new(true, 511, 0, 86, 0, 6, 4, lines));
        Equal("Locke, choice 5 of 7", tracker.Poll(), "the tester's highlighted chocobo is spoken");
        for (var row = 0; row < 7; row++)
        {
            tracker.Observe(new(true, 511, 0, 86, 0, 6, row, lines));
            Equal(row < 6 ? Names[row] : "Never mind", tracker.Poll(), "cursor follows each native row");
        }
    }

    private static void RejectsUnresolvedArchiveChoice()
    {
        var pages = Ff7EncodedTextDecoder.DecodePages(Convert.FromHexString(Registration));
        Require(!FieldAskTextFormatter.TryResolveChoicePage(pages, 0, 6, out _),
            "an archive without live STR values cannot publish six blank choices");
    }

    private static void RejectsUnreadableName()
    {
        var memory = new Memory();
        memory.Bytes.Remove(0x00DC0BFC);
        Require(!new FieldMessageReader(memory).TryReadMessagePagesById(86, out var pages), "a missing name cannot become a blank choice");
        Equal(0, pages.Count, "failed pages are cleared");
        Require(!new FieldMessageReader(memory).TryReadMessageLinesById(86, out _), "the line reader also rejects unavailable names");
    }

    private static void RejectsChangingName()
    {
        var memory = new Memory();
        var reads = 0;
        memory.BeforeRead = address =>
        {
            if (address == 0x00DC0BFC && ++reads == 2) memory.Bytes[address] = 0x21;
        };
        Require(!new FieldMessageReader(memory).TryReadMessagePagesById(86, out _), "a changing name cannot be combined with older rows");
    }

    private static void RejectsFieldChangeDuringNameRead()
    {
        var memory = new Memory();
        memory.BeforeRead = address =>
        {
            if (address == 0x00DC0BFC) memory.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)512));
        };
        Require(!new FieldMessageReader(memory).TryReadMessagePagesById(86, out _), "the field must still own the decoded names");
    }

    private static void PreservesAnActuallyEmptyRow()
    {
        var memory = new Memory();
        memory.Write(0x00DC0BFC, [0xff, 0xff, 0xff, 0xff, 0xff, 0xff]);
        Require(new FieldMessageReader(memory).TryReadMessagePagesById(86, out var pages), "an actually empty native name is readable");
        Require(FieldAskTextFormatter.TryResolveChoicePage(pages, 0, 6, out var lines), "empty rows retain their positions");
        Equal("Blank", FieldAskTextFormatter.GetChoice(lines, 0, 6, 0), "empty memory is not assigned another bird's name");
        Equal("Rydia", FieldAskTextFormatter.GetChoice(lines, 0, 6, 1), "the following row keeps its name");
    }

    private static void ChecksInstalledLayouts()
    {
        var roots = new[] { Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") }
            .Concat((Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Where(root => !string.IsNullOrWhiteSpace(root)).Distinct().ToArray();
        foreach (var root in roots)
        {
            var source = new FlevelDataSource(root!);
            Require(source.TryReadField(511, out var compressed), $"registration field is available in {root}");
            var field = Ff7LzsDecoder.DecodeFieldFile(compressed);
            var script = BitConverter.ToInt32(field, 6) + 4;
            var table = script + BitConverter.ToUInt16(field, script + 4);
            var count = BitConverter.ToUInt16(field, table);
            var found = false;
            for (var dialog = 0; dialog < count; dialog++)
            {
                var start = table + BitConverter.ToUInt16(field, table + 2 + dialog * 2);
                var end = Array.IndexOf(field, (byte)0xff, start);
                Require(end >= start, "installed registration message is terminated");
                var message = field[start..(end + 1)];
                if (message.AsSpan().IndexOf(new byte[] { 0xfe, 0xe2, 0x20, 0x03, 0x06, 0x00 }) < 0) continue;
                var memory = new Memory();
                memory.Write(0x00700200, message);
                Require(new FieldMessageReader(memory).TryReadMessagePagesById(86, out var pages), $"live STR rows for installed dialog {dialog}");
                Require(FieldAskTextFormatter.TryResolveChoicePage(pages, 0, 6, out var lines), "installed seven-row registration picker");
                Equal("Rosa|Rydia|Edge|Celes|Locke|Boco", string.Join('|', lines.Take(6)), "installed layout uses the native names");
                found = true;
            }
            Require(found, $"native registration STR message is present in {root}");
        }
        if (roots.Length != 0) Console.WriteLine($"PASS registration with installed data: {roots.Length} stock/Echo-S layouts.");
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        public Dictionary<uint, byte> Bytes { get; } = [];
        public Action<uint>? BeforeRead { get; set; }

        public Memory()
        {
            Write((uint)FieldPositionReader.AddressCurrentModule, [1]);
            Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)511));
            Write((uint)FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(0x00700000u));
            Write((uint)FieldMessageReader.AddressFieldWindowStates, [0, 0xff, 0xff, 0xff]);
            for (var window = 0; window < 4; window++)
                Write((uint)(FieldMessageReader.AddressFieldWindowMessagePointers + window * 4), BitConverter.GetBytes(window == 0 ? 0x00700200u : 0u));
            Write(0x00700000, new byte[0x1000]);
            Write(0x00700000, BitConverter.GetBytes((ushort)87));
            Write(0x00700000 + 2 + 86 * 2, BitConverter.GetBytes((ushort)0x200));
            Write(0x00700200, Convert.FromHexString(Registration));
            for (var slot = 0; slot < Names.Length; slot++)
            {
                var bytes = Enumerable.Repeat((byte)0xff, 6).ToArray();
                for (var i = 0; i < Names[slot].Length; i++) bytes[i] = (byte)(Names[slot][i] - 0x20);
                Write(0x00DC0BFCu + (uint)slot * 6, bytes);
            }
        }

        public void Write(uint address, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++) Bytes[address + (uint)i] = bytes[i];
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            BeforeRead?.Invoke(virtualAddress);
            for (var i = 0; i < destination.Length; i++)
            {
                if (!Bytes.TryGetValue(virtualAddress + (uint)i, out destination[i])) return false;
            }
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string message) =>
        Require(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
