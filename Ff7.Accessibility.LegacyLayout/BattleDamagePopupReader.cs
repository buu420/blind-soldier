using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class BattleDamagePopupReader
{
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const int AddressCurrentEffectIndex = 0x00BF2DF4;
    public const int AddressEffectData = 0x00BFC3A0;
    public const int EffectRecordSize = 0x20;
    public const int EffectCount = 60;
    public const int StateOffset = 0x02;
    public const int ValueOffset = 0x0E;
    public const int TargetActorOffset = 0x10;
    public const int FlagsOffset = 0x14;

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, short>? readInt16;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, int>? readInt32;
    private readonly ILegacyAddressSpace? addressSpace;

    public BattleDamagePopupReader(
        Func<int, byte> readByte,
        Func<int, short> readInt16,
        Func<int, ushort> readUInt16,
        Func<int, int> readInt32)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readInt16 = readInt16 ?? throw new ArgumentNullException(nameof(readInt16));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
    }

    public BattleDamagePopupReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public BattleDamagePopupSnapshot Read()
    {
        if (addressSpace is null)
        {
            return ReadLegacy();
        }

        if (!TryReadRaw(out var candidate) ||
            !TryReadRaw(out var bookend) ||
            candidate != bookend)
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        return CreateSnapshot(candidate);
    }

    private BattleDamagePopupSnapshot ReadLegacy()
    {
        if (readByte!(AddressCurrentModule) != BattleStateReader.BattleModule)
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        var effectIndex = readUInt16!(AddressCurrentEffectIndex);
        if (effectIndex >= EffectCount)
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        var record = AddressEffectData + effectIndex * EffectRecordSize;
        var state = readByte(record + StateOffset);
        if (state != 0)
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        var value = readInt16!(record + ValueOffset);
        var targetActor = readInt32!(record + TargetActorOffset);
        if (targetActor is not (>= 0 and < 3 or >= 4 and <= 9) ||
            (value <= 0 && value != -1))
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        return new BattleDamagePopupSnapshot(
            true,
            effectIndex,
            targetActor,
            value,
            readInt32(record + FlagsOffset));
    }

    private bool TryReadRaw(out RawBattleDamagePopup raw)
    {
        raw = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)AddressCurrentModule, out var module))
        {
            return false;
        }

        if (module != BattleStateReader.BattleModule)
        {
            raw = new RawBattleDamagePopup(module, ushort.MaxValue, byte.MaxValue, 0, -1, 0);
            return true;
        }

        if (!memory.TryReadUInt16((uint)AddressCurrentEffectIndex, out var effectIndex))
        {
            return false;
        }

        if (effectIndex >= EffectCount)
        {
            raw = new RawBattleDamagePopup(module, effectIndex, byte.MaxValue, 0, -1, 0);
            return true;
        }

        if (!TryComputeRecordAddress(effectIndex, out var record) ||
            !TryAdd(record, StateOffset, out var stateAddress) ||
            !TryAdd(record, ValueOffset, out var valueAddress) ||
            !TryAdd(record, TargetActorOffset, out var targetAddress) ||
            !TryAdd(record, FlagsOffset, out var flagsAddress) ||
            !memory.TryReadByte(stateAddress, out var state) ||
            !memory.TryReadInt16(valueAddress, out var value) ||
            !memory.TryReadInt32(targetAddress, out var targetActor) ||
            !memory.TryReadInt32(flagsAddress, out var flags))
        {
            return false;
        }

        raw = new RawBattleDamagePopup(module, effectIndex, state, value, targetActor, flags);
        return true;
    }

    private static BattleDamagePopupSnapshot CreateSnapshot(RawBattleDamagePopup raw)
    {
        if (raw.Module != BattleStateReader.BattleModule ||
            raw.EffectIndex >= EffectCount ||
            raw.State != 0)
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        if (raw.TargetActor is not (>= 0 and < 3 or >= 4 and <= 9) ||
            (raw.Value <= 0 && raw.Value != -1))
        {
            return BattleDamagePopupSnapshot.Invalid;
        }

        return new BattleDamagePopupSnapshot(
            true,
            raw.EffectIndex,
            raw.TargetActor,
            raw.Value,
            raw.Flags);
    }

    private static bool TryComputeRecordAddress(ushort effectIndex, out uint address)
    {
        var candidate = (ulong)(uint)AddressEffectData +
            (ulong)effectIndex * EffectRecordSize;
        address = candidate <= uint.MaxValue ? (uint)candidate : 0;
        return effectIndex < EffectCount && candidate <= uint.MaxValue;
    }

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        var candidate = (ulong)address + checked((uint)offset);
        result = candidate <= uint.MaxValue ? (uint)candidate : 0;
        return offset >= 0 && candidate <= uint.MaxValue;
    }

    private readonly record struct RawBattleDamagePopup(
        byte Module,
        ushort EffectIndex,
        byte State,
        short Value,
        int TargetActor,
        int Flags);
}

public readonly record struct BattleDamagePopupSnapshot(
    bool IsValid,
    int EffectIndex,
    int TargetActor,
    int Value,
    int Flags)
{
    public bool IsMiss => IsValid && Value == -1;

    public static BattleDamagePopupSnapshot Invalid { get; } = new(false, -1, -1, 0, 0);
}
