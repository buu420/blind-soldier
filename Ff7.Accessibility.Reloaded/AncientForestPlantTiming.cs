using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Extends the native wait before an Ancient Forest pitcher starts shaking.</summary>
internal sealed class AncientForestPlantTiming(ILegacyAddressSpace memory, ILegacyMemoryWriter writer)
{
    private const ushort NativeWait = 300;
    // Each plant shakes twice for about 48 ticks. Add one full native crossing
    // window before the shakes so the warning and frog launch keep their timing.
    private const ushort ExtendedWait = NativeWait + NativeWait + 96;
    private const int Ffnx60FpsMultiplier = 2;
    private readonly LoadedFieldScriptIdentityReader identity = new(memory);

    internal readonly record struct ClosingWait(int Field, int Entity, uint Script, ushort Cursor)
    {
        internal uint Counter => (uint)(Floor60GuardTimingStateReader.AddressEntityWaitCounters + Entity * sizeof(ushort));
    }

    // The game thread calls this before WAIT. The native handler only loads the
    // operand when the entity's counter is zero.
    internal ClosingWait? CaptureStart(FieldScriptContext context)
    {
        if (context.Opcode != FieldOpcodeAddressResolver.OpcodeWaitIndex ||
            context.ScriptId != 3 || context.ByteIndex != 12 || !IsPlantDirector(context))
            return null;

        try
        {
            var entity = context.EntityId;
            if (!memory.TryReadUInt16((uint)(Floor60GuardTimingStateReader.AddressEntityWaitCounters + entity * sizeof(ushort)), out var counter) ||
                counter != 0 || !identity.TryRead(out var loaded) || loaded.FieldId != context.FieldId)
                return null;

            var variant = EchoSCompatibilityManifest.ResolveVariant(loaded);
            if (variant is not (SupportedFieldScriptVariant.Vanilla or SupportedFieldScriptVariant.EchoS124) ||
                context.FieldId == 620 && (variant == SupportedFieldScriptVariant.Vanilla ? entity == 10 : entity == 13) ||
                !ReadCursor(entity, out var cursor) ||
                !LegacyFf7TextReader.TryAdd(loaded.ScriptPointer, cursor, out var instruction))
                return null;

            Span<byte> opcode = stackalloc byte[3];
            var wait = new ClosingWait(context.FieldId, entity, loaded.ScriptPointer, cursor);
            return memory.TryRead(instruction, opcode) && opcode[0] == 0x24 && opcode[1] == 0x2C &&
                opcode[2] == 0x01 && IsCurrent(wait) ? wait : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // WAIT has just initialized the countdown and yielded on the same instruction.
    // Preserve the other 16-bit counter in the aligned word used for the write.
    internal bool Extend(ClosingWait wait)
    {
        try
        {
            var address = wait.Counter & ~3u;
            var shift = (int)(wait.Counter & 2) * 8;
            if (!IsCurrent(wait) || !memory.TryReadInt32(address, out var word))
                return false;

            // FFNx scales WAIT when it initializes the counter in 60 FPS mode.
            // Use the observed starting value so the shake waits keep the same scale.
            var multiplier = ((word >> shift) & ushort.MaxValue) switch
            {
                NativeWait => 1,
                NativeWait * Ffnx60FpsMultiplier => Ffnx60FpsMultiplier,
                _ => 0
            };
            if (multiplier == 0)
                return false;

            var extended = (word & ~(ushort.MaxValue << shift)) | (ExtendedWait * multiplier << shift);
            return writer.TryWriteInt32(address, extended);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsCurrent(ClosingWait wait) =>
        memory.TryReadByte((uint)FieldScriptContextReader.AddressCurrentModule, out var module) &&
        module == FieldPositionReader.FieldModule &&
        memory.TryReadUInt16((uint)FieldScriptContextReader.AddressCurrentFieldId, out var field) && field == wait.Field &&
        memory.TryReadUInt32((uint)FieldScriptContextReader.AddressFieldScriptPtr, out var script) && script == wait.Script &&
        memory.TryReadByte((uint)FieldScriptContextReader.AddressCurrentEntityId, out var entity) && entity == wait.Entity &&
        ReadCursor(wait.Entity, out var cursor) && cursor == wait.Cursor;

    private bool ReadCursor(int entity, out ushort cursor) =>
        memory.TryReadUInt16((uint)(FieldScriptContextReader.AddressFieldCurrScriptPosition + entity * sizeof(ushort)), out cursor);

    private static bool IsPlantDirector(FieldScriptContext context) => context.FieldId switch
    {
        // anfrst_1 has directors 11–13 in stock and 10–12 in Echo-S.
        620 => context.EntityId is >= 10 and <= 13,
        622 => context.EntityId is >= 17 and <= 19,
        623 => context.EntityId is >= 11 and <= 14,
        _ => false
    };
}
