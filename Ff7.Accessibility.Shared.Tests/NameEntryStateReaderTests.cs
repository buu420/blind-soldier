using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class NameEntryStateReaderTests
{
    public static void Run()
    {
        ReaderLivesInSharedPointerFreeAssembly();
        ReadsOneCompleteStableNativeState();
        RejectsEveryUnreadableOrTornField();
        InactiveStateDoesNotPublishStaleEditorFields();
    }

    private static void ReaderLivesInSharedPointerFreeAssembly()
    {
        Equal(typeof(ILegacyAddressSpace).Assembly, typeof(NameEntryStateReader).Assembly, "shared name-entry reader assembly");
        Equal(typeof(ILegacyAddressSpace).Assembly, typeof(NameEntryStateSnapshot).Assembly, "shared name-entry snapshot assembly");
        Equal(
            false,
            typeof(NameEntryStateSnapshot).GetProperties().Any(property =>
                property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr)),
            "name-entry snapshot contains no host pointer");
    }

    private static void ReadsOneCompleteStableNativeState()
    {
        var memory = ValidMemory();
        var reader = new NameEntryStateReader(memory);
        Equal(true, reader.TryRead(out var state), "coherent name-entry state");
        Equal(true, state.IsActive, "name-entry active state");
        Equal(5, state.CurrentModule, "name-entry module");
        Equal(1, state.MenuState, "name-entry menu state");
        Equal(0, state.Focus, "name-entry grid focus");
        Equal(2, state.GridColumn, "name-entry grid column");
        Equal(3, state.GridRow, "name-entry grid row");
        Equal(1, state.CommandRow, "name-entry command row");
        Equal(4, state.SelectedSlot, "name-entry selected slot");
        SequenceEqual([0x23, 0x24, 0xFF, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66], state.NameBuffer, "name-entry native buffer copy");

        memory.Write((uint)NameEntryStateReader.AddressNameBuffer, [0x44]);
        Equal((byte)0x23, state.NameBuffer[0], "published name buffer is producer-immutable");
    }

    private static void RejectsEveryUnreadableOrTornField()
    {
        var required = new (uint Address, string Label)[]
        {
            ((uint)NameEntryStateReader.AddressCurrentModule, "module"),
            ((uint)NameEntryStateReader.AddressMenuState, "menu state"),
            ((uint)NameEntryStateReader.AddressFocus, "focus"),
            ((uint)NameEntryStateReader.AddressGridColumn, "grid column"),
            ((uint)NameEntryStateReader.AddressGridRow, "grid row"),
            ((uint)NameEntryStateReader.AddressCommandRow, "command row"),
            ((uint)NameEntryStateReader.AddressSelectedSlot, "selected slot"),
            ((uint)NameEntryStateReader.AddressNameBuffer, "name buffer")
        };
        foreach (var item in required)
        {
            var unreadable = ValidMemory();
            unreadable.Remove(item.Address);
            Equal(false, new NameEntryStateReader(unreadable).TryRead(out _), $"unreadable {item.Label} invalidates name-entry state");
        }

        var tornFocus = new TearingMemory(
            ValidMemory(),
            (uint)NameEntryStateReader.AddressFocus,
            BitConverter.GetBytes(1));
        Equal(false, new NameEntryStateReader(tornFocus).TryRead(out _), "torn name-entry focus is rejected");

        var tornBuffer = new TearingMemory(
            ValidMemory(),
            (uint)NameEntryStateReader.AddressNameBuffer,
            [0x23, 0x25, 0xFF, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66]);
        Equal(false, new NameEntryStateReader(tornBuffer).TryRead(out _), "torn name-entry buffer is rejected");

        var invalidSlot = ValidMemory();
        invalidSlot.Write((uint)NameEntryStateReader.AddressSelectedSlot, [NameEntryStateReader.NameSlotCount]);
        Equal(false, new NameEntryStateReader(invalidSlot).TryRead(out _), "out-of-range native name slot is rejected");
    }

    private static void InactiveStateDoesNotPublishStaleEditorFields()
    {
        var memory = new Memory();
        memory.Write((uint)NameEntryStateReader.AddressCurrentModule, [1]);
        memory.Write((uint)NameEntryStateReader.AddressMenuState, [0]);
        var reader = new NameEntryStateReader(memory);

        Equal(true, reader.TryRead(out var state), "coherent inactive name-entry ownership");
        Equal(false, state.IsActive, "inactive name-entry state");
        Equal(0, state.NameBuffer.Length, "inactive state publishes no stale name bytes");
    }

    private static Memory ValidMemory()
    {
        var memory = new Memory();
        memory.Write((uint)NameEntryStateReader.AddressCurrentModule, [5]);
        memory.Write((uint)NameEntryStateReader.AddressMenuState, [1]);
        memory.Write((uint)NameEntryStateReader.AddressFocus, BitConverter.GetBytes(0));
        memory.Write((uint)NameEntryStateReader.AddressGridColumn, BitConverter.GetBytes(2));
        memory.Write((uint)NameEntryStateReader.AddressGridRow, BitConverter.GetBytes(3));
        memory.Write((uint)NameEntryStateReader.AddressCommandRow, BitConverter.GetBytes(1));
        memory.Write((uint)NameEntryStateReader.AddressSelectedSlot, [4]);
        memory.Write(
            (uint)NameEntryStateReader.AddressNameBuffer,
            [0x23, 0x24, 0xFF, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66]);
        return memory;
    }

    private static void SequenceEqual(
        IReadOnlyList<byte> expected,
        IReadOnlyList<byte> actual,
        string label)
    {
        Equal(expected.Count, actual.Count, $"{label} length");
        for (var index = 0; index < expected.Count; index++)
        {
            Equal(expected[index], actual[index], $"{label} byte {index}");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        public void Write(uint address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[checked(address + (uint)index)] = values[index];
            }
        }

        public void Remove(uint address) => bytes.Remove(address);

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == 0 ||
                (ulong)virtualAddress + (ulong)destination.Length > (ulong)uint.MaxValue + 1)
            {
                destination.Clear();
                return false;
            }

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
        ILegacyAddressSpace inner,
        uint watchedAddress,
        byte[] replacement) : ILegacyAddressSpace
    {
        private int reads;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == watchedAddress && ++reads > 1)
            {
                if (destination.Length != replacement.Length)
                {
                    destination.Clear();
                    return false;
                }

                replacement.CopyTo(destination);
                return true;
            }

            return inner.TryRead(virtualAddress, destination);
        }
    }
}
