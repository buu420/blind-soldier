namespace Ff7.Accessibility.Reloaded;

public sealed class FieldRunStateReader
{
    public const int AddressFieldLoopSub = 0x0063C17F;
    public const int RunStatusResolverCallOffset = 0x10C;
    public const int RunButtonStatusPointerOffset = 0x55;

    private readonly Func<int, int> readInt32;
    private readonly Func<int, byte> readByte;
    private int? runButtonStatusAddress;
    private int runStatusResolverAddress;

    public FieldRunStateReader(Func<int, int> readInt32, Func<int, byte> readByte)
    {
        this.readInt32 = readInt32;
        this.readByte = readByte;
    }

    public bool TryRead(out FieldRunStateReadResult result)
    {
        if (!TryResolveRunButtonStatusAddress(out var statusAddress, out var resolverAddress, out var resolveDiagnostic))
        {
            result = FieldRunStateReadResult.Invalid(resolveDiagnostic);
            return false;
        }

        var status = readInt32(statusAddress);
        var isRunning = status != 0;
        result = FieldRunStateReadResult.Valid(
            statusAddress,
            resolverAddress,
            status,
            $"resolver=0x{resolverAddress:X8}, statusPtr=0x{statusAddress:X8}, status={status}, running={isRunning}");
        return true;
    }

    private bool TryResolveRunButtonStatusAddress(out int statusAddress, out int resolverAddress, out string diagnostic)
    {
        if (runButtonStatusAddress is > 0)
        {
            statusAddress = runButtonStatusAddress.Value;
            resolverAddress = runStatusResolverAddress;
            diagnostic = string.Empty;
            return true;
        }

        var callAddress = AddressFieldLoopSub + RunStatusResolverCallOffset;
        var instruction = (ushort)(readByte(callAddress) | (readByte(callAddress + 1) << 8));
        var operandSize = GetCallOperandOffset(instruction);
        if (operandSize == 0)
        {
            statusAddress = 0;
            resolverAddress = 0;
            diagnostic = $"unexpected run resolver instruction 0x{instruction:X4} at 0x{callAddress:X8}";
            return false;
        }

        var displacement = readInt32(callAddress + operandSize);
        var resolved = unchecked(callAddress + displacement + 4 + operandSize);
        var pointer = readInt32(resolved + RunButtonStatusPointerOffset);
        if (!IsPlausibleFf7Address(pointer))
        {
            statusAddress = 0;
            resolverAddress = resolved;
            diagnostic = $"resolved run status pointer 0x{pointer:X8} is outside expected FFVII address range";
            return false;
        }

        runButtonStatusAddress = pointer;
        runStatusResolverAddress = resolved;
        statusAddress = pointer;
        resolverAddress = resolved;
        diagnostic = string.Empty;
        return true;
    }

    private static int GetCallOperandOffset(ushort instruction)
    {
        if (instruction == 0x15FF)
        {
            return 2;
        }

        var opcode = instruction & 0xFF;
        return opcode is 0xE8 or 0xE9 ? 1 : 0;
    }

    private static bool IsPlausibleFf7Address(int address) => address is >= 0x00400000 and <= 0x02000000;
}

public readonly record struct FieldRunStateReadResult(
    bool IsUsable,
    bool IsRunning,
    int Status,
    int StatusAddress,
    int ResolverAddress,
    string Diagnostic)
{
    public static FieldRunStateReadResult Valid(int statusAddress, int resolverAddress, int status, string diagnostic) =>
        new(true, status != 0, status, statusAddress, resolverAddress, diagnostic);

    public static FieldRunStateReadResult Invalid(string diagnostic) =>
        new(false, false, 0, 0, 0, diagnostic);
}
