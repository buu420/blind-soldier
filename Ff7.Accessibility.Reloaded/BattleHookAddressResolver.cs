namespace Ff7.Accessibility.Reloaded;

public sealed class BattleHookAddressResolver
{
    public const int AddressUpdateDisplayTextCall = 0x0042D833;
    public const int SetBattleTextActiveCallOffset = 0x14A;

    private readonly Func<int, byte> readByte;
    private readonly Func<int, int> readInt32;

    public BattleHookAddressResolver(Func<int, byte> readByte, Func<int, int> readInt32)
    {
        this.readByte = readByte;
        this.readInt32 = readInt32;
    }

    public bool TryResolveBattleTextActive(out int address)
    {
        address = 0;
        if (!TryResolveRelativeCall(AddressUpdateDisplayTextCall, out var updateDisplayText))
        {
            return false;
        }

        return TryResolveRelativeCall(updateDisplayText + SetBattleTextActiveCallOffset, out address);
    }

    private bool TryResolveRelativeCall(int callAddress, out int targetAddress)
    {
        targetAddress = 0;
        if (readByte(callAddress) != 0xE8)
        {
            return false;
        }

        var displacement = readInt32(callAddress + 1);
        var target = (long)callAddress + 5 + displacement;
        if (target is <= 0 or > int.MaxValue)
        {
            return false;
        }

        targetAddress = (int)target;
        return true;
    }
}
