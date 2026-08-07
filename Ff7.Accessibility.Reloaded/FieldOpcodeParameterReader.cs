namespace Ff7.Accessibility.Reloaded;

public sealed class FieldOpcodeAddressResolver
{
    public const int AddressFieldInitEvent = 0x0060BACF;
    public const int ExecuteOpcodeCallOffset = 0x80;
    public const int ExecuteOpcodeTableOffset = 0x10D;
    public const int AskUpdateLoopCallOffset = 0x8E;
    public const int OpcodeWaitIndex = 0x24;
    public const int OpcodeTimerIndex = 0x38;
    public const int OpcodeMessageIndex = 0x40;
    public const int OpcodeAskIndex = 0x48;
    public const int OpcodeRequestIndex = 0x01;
    public const int OpcodeRequestSwIndex = 0x02;
    public const int OpcodeRequestEwIndex = 0x03;
    public const int OpcodeSplitIndex = 0x09;
    public const int OpcodeScroll2DIndex = 0x66;
    public const int OpcodeFadeIndex = 0x6B;
    public const int OpcodeAnime1Index = 0xA3;
    public const int OpcodeVisibilityIndex = 0xA4;
    public const int OpcodeAnimOnceIndex = 0xAF;
    public const int OpcodeCanm1Index = 0xB1;
    public const int OpcodeAnimHoldIndex = 0xBA;
    public const int OpcodeCanm2Index = 0xBC;
    public const int OpcodeJumpIndex = 0xC0;
    public const int OpcodeBackgroundOnIndex = 0xE0;
    public const int OpcodeSoundIndex = 0xF1;
    public const int OpcodeAkaoIndex = 0xF2;
    public const int OpcodeMovieIndex = 0xF9;

    private readonly Func<int, int> readInt32;
    private readonly Func<int, byte> readByte;

    public FieldOpcodeAddressResolver(Func<int, int> readInt32, Func<int, byte> readByte)
    {
        this.readInt32 = readInt32;
        this.readByte = readByte;
    }

    public bool TryResolve(out FieldOpcodeAddressResolution result)
    {
        var callAddress = AddressFieldInitEvent + ExecuteOpcodeCallOffset;
        if (!TryResolveRelativeCall(callAddress, out var executeOpcodeAddress))
        {
            result = FieldOpcodeAddressResolution.Invalid($"unexpected execute opcode call at 0x{callAddress:X8}");
            return false;
        }

        var opcodeTableAddress = readInt32(executeOpcodeAddress + ExecuteOpcodeTableOffset);
        var waitOpcodeAddress = readInt32(opcodeTableAddress + OpcodeWaitIndex * sizeof(int));
        var soundOpcodeAddress = readInt32(opcodeTableAddress + OpcodeSoundIndex * sizeof(int));
        var messageOpcodeAddress = readInt32(opcodeTableAddress + OpcodeMessageIndex * sizeof(int));
        var askOpcodeAddress = readInt32(opcodeTableAddress + OpcodeAskIndex * sizeof(int));
        if (!IsPlausibleCodeAddress(opcodeTableAddress) ||
            !IsPlausibleCodeAddress(waitOpcodeAddress) ||
            !IsPlausibleCodeAddress(soundOpcodeAddress) ||
            !IsPlausibleCodeAddress(messageOpcodeAddress) ||
            !IsPlausibleCodeAddress(askOpcodeAddress))
        {
            result = FieldOpcodeAddressResolution.Invalid(
                $"resolved opcode table=0x{opcodeTableAddress:X8}, wait=0x{waitOpcodeAddress:X8}, sound=0x{soundOpcodeAddress:X8}, message=0x{messageOpcodeAddress:X8}, ask=0x{askOpcodeAddress:X8} is not plausible");
            return false;
        }

        if (!TryResolveRelativeCall(askOpcodeAddress + AskUpdateLoopCallOffset, out var askUpdateLoopAddress))
        {
            result = FieldOpcodeAddressResolution.Invalid(
                $"unexpected ASK update-loop call at 0x{askOpcodeAddress + AskUpdateLoopCallOffset:X8}");
            return false;
        }

        result = FieldOpcodeAddressResolution.Valid(
            executeOpcodeAddress,
            opcodeTableAddress,
            waitOpcodeAddress,
            soundOpcodeAddress,
            messageOpcodeAddress,
            askOpcodeAddress,
            askUpdateLoopAddress,
            $"execute=0x{executeOpcodeAddress:X8}, table=0x{opcodeTableAddress:X8}, wait=0x{waitOpcodeAddress:X8}, sound=0x{soundOpcodeAddress:X8}, " +
            $"message=0x{messageOpcodeAddress:X8}, ask=0x{askOpcodeAddress:X8}, askUpdate=0x{askUpdateLoopAddress:X8}");
        return true;
    }

    public bool TryResolveCutsceneHandlers(
        out int waitOpcodeAddress,
        out int soundOpcodeAddress,
        out string diagnostic)
    {
        var callAddress = AddressFieldInitEvent + ExecuteOpcodeCallOffset;
        if (!TryResolveRelativeCall(callAddress, out var executeOpcodeAddress))
        {
            waitOpcodeAddress = 0;
            soundOpcodeAddress = 0;
            diagnostic = $"unexpected execute opcode call at 0x{callAddress:X8}";
            return false;
        }

        var opcodeTableAddress = readInt32(executeOpcodeAddress + ExecuteOpcodeTableOffset);
        waitOpcodeAddress = readInt32(opcodeTableAddress + OpcodeWaitIndex * sizeof(int));
        soundOpcodeAddress = readInt32(opcodeTableAddress + OpcodeSoundIndex * sizeof(int));
        if (!IsPlausibleCodeAddress(opcodeTableAddress) ||
            !IsPlausibleCodeAddress(waitOpcodeAddress) ||
            !IsPlausibleCodeAddress(soundOpcodeAddress))
        {
            diagnostic =
                $"resolved opcode table=0x{opcodeTableAddress:X8}, wait=0x{waitOpcodeAddress:X8}, " +
                $"sound=0x{soundOpcodeAddress:X8} is not plausible";
            return false;
        }

        diagnostic =
            $"execute=0x{executeOpcodeAddress:X8}, table=0x{opcodeTableAddress:X8}, " +
            $"wait=0x{waitOpcodeAddress:X8}, sound=0x{soundOpcodeAddress:X8}";
        return true;
    }

    public bool TryResolveMessageHooks(out FieldOpcodeMessageHookResolution result)
    {
        if (!TryResolveOpcodeHandlers(
                [OpcodeMessageIndex, OpcodeAskIndex],
                out var handlers,
                out var diagnostic) ||
            !handlers.TryGetValue(OpcodeMessageIndex, out var messageOpcodeAddress) ||
            !handlers.TryGetValue(OpcodeAskIndex, out var askOpcodeAddress))
        {
            result = FieldOpcodeMessageHookResolution.Invalid(diagnostic);
            return false;
        }

        var hasAskUpdateLoop = TryResolveRelativeCall(
            askOpcodeAddress + AskUpdateLoopCallOffset,
            out var askUpdateLoopAddress);
        if (!hasAskUpdateLoop)
        {
            askUpdateLoopAddress = 0;
        }

        var askUpdateDiagnostic = hasAskUpdateLoop
            ? $"askUpdate=0x{askUpdateLoopAddress:X8}"
            : $"ASK cursor helper unavailable at legacy offset 0x{askOpcodeAddress + AskUpdateLoopCallOffset:X8}";
        result = new FieldOpcodeMessageHookResolution(
            messageOpcodeAddress,
            askOpcodeAddress,
            askUpdateLoopAddress,
            $"{diagnostic}, {askUpdateDiagnostic}");
        return true;
    }

    public bool TryResolveOpcodeHandlers(
        IEnumerable<int> opcodeIndexes,
        out IReadOnlyDictionary<int, int> handlers,
        out string diagnostic)
    {
        var requestedOpcodes = opcodeIndexes.Distinct().Order().ToArray();
        if (requestedOpcodes.Length == 0 || requestedOpcodes.Any(opcode => opcode is < 0 or > byte.MaxValue))
        {
            handlers = new Dictionary<int, int>();
            diagnostic = "opcode indexes must contain at least one native byte value";
            return false;
        }

        var callAddress = AddressFieldInitEvent + ExecuteOpcodeCallOffset;
        if (!TryResolveRelativeCall(callAddress, out var executeOpcodeAddress))
        {
            handlers = new Dictionary<int, int>();
            diagnostic = $"unexpected execute opcode call at 0x{callAddress:X8}";
            return false;
        }

        var opcodeTableAddress = readInt32(executeOpcodeAddress + ExecuteOpcodeTableOffset);
        if (!IsPlausibleCodeAddress(opcodeTableAddress))
        {
            handlers = new Dictionary<int, int>();
            diagnostic = $"resolved opcode table 0x{opcodeTableAddress:X8} is not plausible";
            return false;
        }

        var resolved = new Dictionary<int, int>(requestedOpcodes.Length);
        foreach (var opcode in requestedOpcodes)
        {
            var handlerAddress = readInt32(opcodeTableAddress + opcode * sizeof(int));
            if (!IsPlausibleCodeAddress(handlerAddress))
            {
                handlers = new Dictionary<int, int>();
                diagnostic =
                    $"resolved opcode 0x{opcode:X2} handler 0x{handlerAddress:X8} from table 0x{opcodeTableAddress:X8} is not plausible";
                return false;
            }

            resolved[opcode] = handlerAddress;
        }

        handlers = resolved;
        diagnostic =
            $"execute=0x{executeOpcodeAddress:X8}, table=0x{opcodeTableAddress:X8}, " +
            string.Join(", ", resolved.Select(pair => $"0x{pair.Key:X2}=0x{pair.Value:X8}"));
        return true;
    }

    private bool TryResolveRelativeCall(int callAddress, out int targetAddress)
    {
        var instruction = (ushort)(readByte(callAddress) | (readByte(callAddress + 1) << 8));
        var operandOffset = GetCallOperandOffset(instruction);
        if (operandOffset == 0)
        {
            targetAddress = 0;
            return false;
        }

        targetAddress = unchecked(callAddress + readInt32(callAddress + operandOffset) + 4 + operandOffset);
        return IsPlausibleCodeAddress(targetAddress);
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

    private static bool IsPlausibleCodeAddress(int address) => address is >= 0x00400000 and <= 0x7FFF0000;
}

public readonly record struct FieldOpcodeAddressResolution(
    bool IsUsable,
    int ExecuteOpcodeAddress,
    int OpcodeTableAddress,
    int WaitOpcodeAddress,
    int SoundOpcodeAddress,
    int MessageOpcodeAddress,
    int AskOpcodeAddress,
    int AskUpdateLoopAddress,
    string Diagnostic)
{
    public static FieldOpcodeAddressResolution Valid(
        int executeOpcodeAddress,
        int opcodeTableAddress,
        int waitOpcodeAddress,
        int soundOpcodeAddress,
        int messageOpcodeAddress,
        int askOpcodeAddress,
        int askUpdateLoopAddress,
        string diagnostic) =>
        new(
            true,
            executeOpcodeAddress,
            opcodeTableAddress,
            waitOpcodeAddress,
            soundOpcodeAddress,
            messageOpcodeAddress,
            askOpcodeAddress,
            askUpdateLoopAddress,
            diagnostic);

    public static FieldOpcodeAddressResolution Invalid(string diagnostic) =>
        new(false, 0, 0, 0, 0, 0, 0, 0, diagnostic);
}

public readonly record struct FieldOpcodeMessageHookResolution(
    int MessageOpcodeAddress,
    int AskOpcodeAddress,
    int AskUpdateLoopAddress,
    string Diagnostic)
{
    public bool HasAskUpdateLoop => AskUpdateLoopAddress != 0;

    public static FieldOpcodeMessageHookResolution Invalid(string diagnostic) =>
        new(
            MessageOpcodeAddress: 0,
            AskOpcodeAddress: 0,
            AskUpdateLoopAddress: 0,
            Diagnostic: diagnostic);
}
