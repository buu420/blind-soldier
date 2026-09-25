using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The gateway reader follows MPJPO's live switch. MPJPO (0061A4D4) writes its operand to
/// script context +0x36 (guest 00CC0DBE), and the movement handler (00636C41) checks the
/// gateway table (00637EBA) only while that byte is zero. The script context pointer and the
/// switch are read inside the same ownership bookends as the table, so a gateway is offered
/// only while the game is known to be checking it. Shared by both test hosts: the Steam 2026
/// runtime reads the same guest addresses through its translated address space.
/// </summary>
internal static class FieldGatewayLiveSwitchTests
{
    private const uint TriggerHeader = 0x00060000;
    private const uint Context = 0x00CC0D88;
    private const uint OtherContext = 0x00CC2000;
    private const ushort FieldId = 117;

    public static void Run()
    {
        TheGatewaysFollowTheSwitch();
        AnUnreadableContextOrSwitchOffersNoGateway();
        ASwitchChangingDuringTheReadIsTorn();
        AnotherContextDuringTheReadIsTornEvenWithTheSameSwitch();
    }

    private static FieldPositionSnapshot Position => new(FieldPositionReader.FieldModule, FieldId, 0, 0, 0, 0, 0, 0);

    private static void TheGatewaysFollowTheSwitch()
    {
        var memory = GatewayField.Create();
        var reader = new FieldGatewayTargetReader(memory);
        var counts = new List<int>();
        foreach (byte disabled in new byte[] { 0, 1, 0, 2, 0 })
        {
            memory.Write(Context + FieldGatewayTargetReader.GatewaysDisabledOffset, disabled);
            True(reader.TryReadTargets(Position, out var targets), $"switch {disabled}: the read itself is usable ({reader.LastDiagnostic})");
            counts.Add(targets.Count);
            if (disabled != 0)
            {
                True(reader.LastDiagnostic.Contains("MPJPO has switched the gateways off", StringComparison.Ordinal),
                    $"switch {disabled}: the diagnostic says why no gateway is offered: {reader.LastDiagnostic}");
            }
        }

        Equal("1,0,1,0,1", string.Join(",", counts), "MPJPO 0->1->0->2->0 switches the one gateway off and on again");
    }

    private static void AnUnreadableContextOrSwitchOffersNoGateway()
    {
        var noPointer = GatewayField.Create();
        noPointer.Forget(FieldGatewayTargetReader.AddressScriptContextPointer);
        var reader = new FieldGatewayTargetReader(noPointer);
        True(!reader.TryReadTargets(Position, out var targets) && targets.Count == 0,
            "an unreadable script context pointer is no confirmed gateway");
        True(reader.LastDiagnostic.Contains("script context unreadable", StringComparison.Ordinal), reader.LastDiagnostic);

        var nullPointer = GatewayField.Create();
        nullPointer.WriteUInt32(FieldGatewayTargetReader.AddressScriptContextPointer, 0);
        reader = new FieldGatewayTargetReader(nullPointer);
        True(!reader.TryReadTargets(Position, out targets) && targets.Count == 0, "a null script context is no confirmed gateway");

        var noSwitch = GatewayField.Create();
        noSwitch.Forget(Context + FieldGatewayTargetReader.GatewaysDisabledOffset);
        reader = new FieldGatewayTargetReader(noSwitch);
        True(!reader.TryReadTargets(Position, out targets) && targets.Count == 0,
            "an unreadable switch is neither an open nor a closed gateway");
        True(reader.LastDiagnostic.Contains("gateway switch unreadable", StringComparison.Ordinal), reader.LastDiagnostic);
        Equal("0", $"{reader.ReadTargets(Position).Count}", "and the plain read offers nothing either");
    }

    private static void ASwitchChangingDuringTheReadIsTorn()
    {
        var memory = GatewayField.Create();
        // The switch reads 0 at the first bookend and 1 from the next read on.
        memory.ChangeAfterReads(Context + FieldGatewayTargetReader.GatewaysDisabledOffset, 1, 1);
        var reader = new FieldGatewayTargetReader(memory);
        True(!reader.TryReadTargets(Position, out var targets) && targets.Count == 0,
            "MPJPO switching during the read is a torn read, not an answer");
        True(reader.LastDiagnostic.Contains("changed during read", StringComparison.Ordinal), reader.LastDiagnostic);
    }

    private static void AnotherContextDuringTheReadIsTornEvenWithTheSameSwitch()
    {
        var memory = GatewayField.Create();
        memory.Write(OtherContext + FieldGatewayTargetReader.GatewaysDisabledOffset, 0);
        // The pointer names another context (whose switch is also 0) from its second read on.
        memory.ChangePointerAfterReads(FieldGatewayTargetReader.AddressScriptContextPointer, OtherContext, 1);
        var reader = new FieldGatewayTargetReader(memory);
        True(!reader.TryReadTargets(Position, out var targets) && targets.Count == 0,
            "a different script context between the bookends is a torn read even when both switches agree");
        True(reader.LastDiagnostic.Contains("changed during read", StringComparison.Ordinal), reader.LastDiagnostic);
    }

    /// <summary>A field with one usable gateway into field 116, gateways switched on.</summary>
    internal sealed class GatewayField : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, (byte Value, int AfterReads)> changes = [];
        private readonly Dictionary<uint, int> reads = [];

        public static GatewayField Create()
        {
            var field = new GatewayField();
            field.Write((uint)FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
            field.WriteUInt16((uint)FieldPositionReader.AddressFieldId, FieldId);
            field.WriteUInt32((uint)FieldNavigationControlReader.AddressFieldTriggersPtr, TriggerHeader);
            field.WriteUInt32(FieldGatewayTargetReader.AddressScriptContextPointer, Context);
            field.Write(Context + FieldGatewayTargetReader.GatewaysDisabledOffset, 0);
            for (var gateway = 0; gateway < FieldGatewayTargetReader.GatewayCount; gateway++)
            {
                var record = TriggerHeader + FieldGatewayTargetReader.GatewaysOffset +
                             (uint)(gateway * FieldGatewayTargetReader.GatewayStride);
                for (var offset = 0; offset < FieldGatewayTargetReader.GatewayStride; offset++)
                {
                    field.Write(record + (uint)offset, 0);
                }

                field.WriteUInt16(record + FieldGatewayTargetReader.DestinationFieldOffset,
                    gateway == 0 ? (ushort)116 : (ushort)short.MaxValue);
            }

            return field;
        }

        public void Write(uint address, byte value) => bytes[address] = value;

        public void WriteUInt16(uint address, ushort value)
        {
            Write(address, (byte)value);
            Write(address + 1, (byte)(value >> 8));
        }

        public void WriteUInt32(uint address, uint value)
        {
            for (var index = 0; index < 4; index++)
            {
                Write(address + (uint)index, (byte)(value >> (index * 8)));
            }
        }

        public void Forget(uint address)
        {
            for (var index = 0u; index < 4; index++)
            {
                bytes.Remove(address + index);
            }
        }

        /// <summary>From the read after <paramref name="afterReads"/> reads of the byte on, it is <paramref name="value"/>.</summary>
        public void ChangeAfterReads(uint address, byte value, int afterReads) => changes[address] = (value, afterReads);

        public void ChangePointerAfterReads(uint address, uint value, int afterReads)
        {
            for (var index = 0; index < 4; index++)
            {
                changes[address + (uint)index] = ((byte)(value >> (index * 8)), afterReads);
            }
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var at = address + (uint)index;
                if (changes.TryGetValue(at, out var change))
                {
                    reads[at] = reads.GetValueOrDefault(at) + 1;
                    if (reads[at] > change.AfterReads)
                    {
                        bytes[at] = change.Value;
                    }
                }

                if (!bytes.TryGetValue(at, out destination[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static void Equal(string expected, string actual, string what)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
        }
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(what);
        }
    }
}
