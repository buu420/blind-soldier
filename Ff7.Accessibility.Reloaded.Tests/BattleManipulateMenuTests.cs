using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class BattleManipulateMenuTests
{
    // Independent addresses from the native input and renderer, not reader constants.
    private const short Menu = 0x13;
    private const int Owner = 0x00DC3C64;
    private const int Row = 0x00DC276C;
    private const int Scroll = 0x00DC277C;
    private const int Records = 0x009AC114;
    private const int RecordStride = 6;
    private const int EnemyStride = 0x60;

    internal static void Run()
    {
        ReadsEnemyActionsWithoutAReadyPartyMember();
        SpeaksChangedRowsAndReopenedTurns();
        RejectsInvalidAndIncoherentNativeState();
    }

    private static void ReadsEnemyActionsWithoutAReadyPartyMember()
    {
        var memory = CreateMemory();
        var reader = CreateReader(memory);
        var menu = reader.ReadMenuState(Menu);
        Equal(true, menu.IsValid, "Manipulate menu is readable with stale party owner");
        Equal(5, menu.PartySlot, "controlled enemy owns the menu");
        Equal(true, menu.Actor.IsEnemy, "menu owner is an enemy");
        Equal("Razor Weed", menu.Actor.Name, "controlled enemy name");
        Equal(false, menu.Actor.InformationVisible, "Manipulate does not grant Sense");
        Equal(0, menu.Actor.CurrentHp, "unsensed enemy HP remains hidden");
        Equal("Glasscutter", menu.Selection?.Name, "native scene action name");
        Equal(0x120, menu.Selection?.EntryId, "scene-local index resolves to action id");
        Equal(0, menu.Selection?.SlotIndex, "first action row");
        Equal(null, menu.Selection?.MpCost, "Manipulate list does not display MP costs");

        memory.Byte(BattleStateReader.AddressCurrentActorSlot, 2);
        Equal("Glasscutter", reader.ReadMenuState(Menu).Selection?.Name,
            "stale but valid party owner does not replace the controlled enemy");

        memory.Int32(Row, 1);
        Equal("Spaz Voice", reader.ReadMenuState(Menu).Selection?.Name, "second action row");
        memory.Byte(Records + EnemyStride + RecordStride + 3, 2);
        Equal(false, reader.ReadMenuState(Menu).Selection?.IsAvailable, "native unavailable flag");

        memory.Int32(Row, 2);
        var empty = reader.ReadMenuState(Menu).Selection;
        Equal("Empty slot", empty?.Name, "visible blank action row is identified");
        Equal(false, empty?.IsAvailable, "empty row cannot be confirmed");

        // The owner is a byte. Adjacent scratch bytes need not be zero.
        memory.Byte(Owner + 1, 0xA5);
        Equal(true, reader.ReadMenuState(Menu).IsValid, "owner width matches native byte load");
    }

    private static void SpeaksChangedRowsAndReopenedTurns()
    {
        var memory = CreateMemory();
        var reader = CreateReader(memory);
        var speech = new BattleMenuFrameSpeechCoordinator();
        string? Observe()
        {
            Equal(true, reader.TryIsRootCommandMenuActive(out var active), "menu lifecycle readable");
            speech.ObserveRootCommandMenuActive(active);
            speech.BeginFrame(Menu);
            speech.CompleteFrame(reader.ReadMenuState(Menu));
            return speech.Poll();
        }

        Equal("Razor Weed. Glasscutter", Observe(), "initial controlled-enemy action speech");
        Equal(null, Observe(), "unchanged row does not repeat");
        memory.Int32(Row, 1);
        Equal("Spaz Voice", Observe(), "moving the native cursor speaks the action");
        memory.Int32(Row, 0);
        Equal("Glasscutter", Observe(), "returning to an earlier row speaks again");
        memory.Byte(BattleStateReader.AddressMenuWindowStates + Menu, 0);
        Equal(null, Observe(), "closed menu is silent");
        memory.Byte(BattleStateReader.AddressMenuWindowStates + Menu, 2);
        Equal("Razor Weed. Glasscutter", Observe(), "same action speaks on the next enemy turn");
    }

    private static void RejectsInvalidAndIncoherentNativeState()
    {
        Action<Memory>[] corruptions =
        [
            m => m.Byte(Owner, 6),
            m => m.Byte(Owner, 255),
            m => m.Int32(Row, -1),
            m => m.Int32(Row, 3),
            m => m.Int32(Scroll, -1),
            m => m.Int32(Scroll, int.MaxValue),
            m => m.Byte(Records + EnemyStride, 32),
            m => m.UInt16(BattleStateReader.AddressSceneAttackIds + 7 * 2, 0xFFFF),
            m => m.Text(BattleStateReader.AddressSceneAttackNames + 7 * 32, "", 32),
            m => m.Fill(BattleStateReader.AddressSceneAttackNames + 7 * 32, 32, 0),
            m => m.Remove(BattleStateReader.AddressSceneAttackNames + 7 * 32),
            m => m.Remove(Records + EnemyStride + 3),
            m => m.BeforeRead = (a, n) => { if (a == Owner && n == 2) m.Byte(Owner, 0); },
            m => m.BeforeRead = (a, n) => { if (a == Row && n == 2) m.Int32(Row, 1); },
            m => m.BeforeRead = (a, n) => { if (a == Records + EnemyStride && n == 2) m.Byte(a, 8); },
            m => m.BeforeRead = (a, n) => { if (a == BattleStateReader.AddressSceneAttackNames + 7 * 32 && n == 2) m.Text(a, "Changed", 32); },
        ];
        for (var index = 0; index < corruptions.Length; index++)
        {
            var memory = CreateMemory();
            corruptions[index](memory);
            var snapshot = CreateReader(memory).ReadMenuState(Menu);
            Equal(false, snapshot.IsValid && snapshot.Selection is not null,
                $"invalid or torn Manipulate observation {index} must not speak");
        }
    }

    private static BattleStateReader CreateReader(Memory memory) => new(
        memory, new SavemapPartyReader(memory),
        resolveAbilityName: _ => throw new InvalidOperationException("Scene actions are not KERNEL magic entries."));

    private static Memory CreateMemory()
    {
        var memory = new Memory();
        memory.Byte(BattleStateReader.AddressCurrentModule, 2);
        memory.Byte(BattleStateReader.AddressCurrentActorSlot, 255);
        memory.Byte(BattleStateReader.AddressMenuWindowStates + 1, 0);
        memory.Byte(BattleStateReader.AddressMenuWindowStates + Menu, 2);
        memory.Byte(Owner, 1);
        memory.Int32(Row, 0);
        memory.Int32(Scroll, 0);
        for (var actor = 4; actor <= 5; actor++)
        {
            var address = BattleStateReader.AddressBattleActors + actor * BattleStateReader.BattleActorSize;
            memory.Fill(address, BattleStateReader.BattleActorSize, 0);
            memory.Byte(address + BattleStateReader.ActorInstanceIdOffset, 16);
            memory.Int32(address + BattleStateReader.ActorCurrentHpOffset, 200);
            memory.Int32(address + BattleStateReader.ActorMaxHpOffset, 500);
            memory.UInt16(address + BattleStateReader.ActorCurrentMpOffset, 30);
            memory.UInt16(address + BattleStateReader.ActorMaxMpOffset, 40);
            memory.Byte(BattleStateReader.AddressPersistentActorRecords + actor * BattleStateReader.PersistentActorRecordSize, 0);
            memory.UInt16(BattleStateReader.AddressEnemySceneIndexRecords + (actor - 4) * BattleStateReader.EnemySceneIndexRecordSize, 0);
        }
        memory.Text(BattleStateReader.AddressEnemyData, "Razor Weed", 24);
        memory.Fill(Records, EnemyStride * 2, 0);
        for (var enemy = 0; enemy < 2; enemy++)
        {
            memory.Byte(Records + enemy * EnemyStride, 7);
            memory.Byte(Records + enemy * EnemyStride + RecordStride, 8);
            memory.Byte(Records + enemy * EnemyStride + 2 * RecordStride, 255);
            memory.Byte(Records + enemy * EnemyStride + 2 * RecordStride + 3, 3);
        }
        memory.UInt16(BattleStateReader.AddressSceneAttackIds + 7 * 2, 0x120);
        memory.UInt16(BattleStateReader.AddressSceneAttackIds + 8 * 2, 0x145);
        memory.Text(BattleStateReader.AddressSceneAttackNames + 7 * 32, "Glasscutter", 32);
        memory.Text(BattleStateReader.AddressSceneAttackNames + 8 * 32, "Spaz Voice", 32);
        return memory;
    }

    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{context}: expected {expected}, got {actual}");
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<int, byte> bytes = new();
        private readonly Dictionary<int, int> reads = new();
        internal Action<int, int>? BeforeRead { get; set; }
        internal void Byte(int address, byte value) => bytes[address] = value;
        internal void UInt16(int address, ushort value) => Write(address, BitConverter.GetBytes(value));
        internal void Int32(int address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void Remove(int address) => bytes.Remove(address);
        internal void Fill(int address, int length, byte value) => Write(address, Enumerable.Repeat(value, length).ToArray());
        internal void Text(int address, string value, int length)
        {
            Fill(address, length, 255);
            Write(address, value.Select(c => checked((byte)(c - 0x20))).ToArray());
        }
        private void Write(int address, byte[] value)
        {
            for (var index = 0; index < value.Length; index++) bytes[address + index] = value[index];
        }
        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            var address = checked((int)virtualAddress);
            reads.TryGetValue(address, out var count);
            reads[address] = count + 1;
            BeforeRead?.Invoke(address, count + 1);
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(address + index, out var value)) return false;
                destination[index] = value;
            }
            return true;
        }
    }
}
