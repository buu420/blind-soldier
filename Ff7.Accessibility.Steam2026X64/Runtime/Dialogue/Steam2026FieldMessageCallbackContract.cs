using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Validates and captures the translated Steam 2026 MESSAGE opcode callback.
/// The callback only establishes native lifecycle ownership; visible field
/// window buffers remain the source of the text spoken to the player.
/// </summary>
internal sealed class Steam2026FieldMessageCallbackContract
{
    internal static TranslatedFunctionMapDefinition FunctionMap { get; } = new(
        0x00618DBD,
        0x016EA870,
        0x00BCD3F0,
        "48895C2408574883EC208B0DC8C14601");

    private readonly object hookLeaseLock = new();
    private readonly TranslatedFunctionMapValidator functionValidator;
    private readonly TranslatedX86AddressSpace addressSpace;
    private readonly TranslatedX86CallFrameReader frame;
    private readonly FieldOpcodeParameterReader opcodeReader;
    private ActiveHookLease? activeHookLease;
    private long validationEpoch;

    internal Steam2026FieldMessageCallbackContract(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
        : this(
            moduleBase,
            moduleImageSize,
            memory,
            CreateResearchAddressSpace(moduleBase, memory),
            hasExactSupportedFingerprint: false)
    {
    }

    internal Steam2026FieldMessageCallbackContract(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
        : this(
            moduleBase,
            moduleImageSize,
            memory,
            ValidatedTranslatedX86AddressSpaceFactory.Create(
                fingerprint,
                moduleBase,
                memory),
            hasExactSupportedFingerprint: true)
    {
    }

    private Steam2026FieldMessageCallbackContract(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        TranslatedX86AddressSpace addressSpace,
        bool hasExactSupportedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(memory);
        functionValidator = new TranslatedFunctionMapValidator(
            moduleBase,
            moduleImageSize,
            memory);
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        frame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);
        opcodeReader = new FieldOpcodeParameterReader(addressSpace);
        HasExactSupportedFingerprint = hasExactSupportedFingerprint;
    }

    internal bool HasExactSupportedFingerprint { get; }

    internal bool TryValidateCaptureIdentity(out ulong hostAddress)
    {
        hostAddress = 0;
        return HasExactSupportedFingerprint
               && addressSpace.HasExpectedResolverSignature()
               && functionValidator.TryValidate(FunctionMap, out hostAddress);
    }

    internal void ActivateHookLease(Func<bool> isHookEnabled)
    {
        ArgumentNullException.ThrowIfNull(isHookEnabled);
        lock (hookLeaseLock)
        {
            if (activeHookLease is not null)
            {
                throw new InvalidOperationException(
                    "A translated MESSAGE hook lease is already active.");
            }

            if (!HasExactSupportedFingerprint)
            {
                throw new InvalidOperationException(
                    "A translated MESSAGE hook lease requires the exact supported fingerprint.");
            }

            if (!IsEnabled(isHookEnabled)
                || !functionValidator.TryValidateMappedTarget(FunctionMap, out var hostAddress)
                || hostAddress == 0)
            {
                throw new InvalidOperationException(
                    "The active translated MESSAGE hook identity is unavailable.");
            }

            var generation = Interlocked.Increment(ref validationEpoch);
            Volatile.Write(
                ref activeHookLease,
                new ActiveHookLease(generation, hostAddress, isHookEnabled));
        }
    }

    internal void RevokeHookLease()
    {
        lock (hookLeaseLock)
        {
            Volatile.Write(ref activeHookLease, null);
            Interlocked.Increment(ref validationEpoch);
        }
    }

    internal bool TryCaptureMessage(out FieldOpcodeMessageObservation observation)
    {
        observation = default;
        try
        {
            if (!addressSpace.HasExpectedResolverSignature()
                || !TryResolveCurrentIdentity(
                    out var hostAddressBefore,
                    out var generationBefore)
                || !opcodeReader.TryReadMessage(out var first)
                || first.Kind != FieldOpcodeKind.Message
                || !opcodeReader.TryReadMessage(out var second)
                || first != second
                || !addressSpace.HasExpectedResolverSignature()
                || !TryResolveCurrentIdentity(
                    out var hostAddressAfter,
                    out var generationAfter)
                || hostAddressBefore != hostAddressAfter
                || generationBefore != generationAfter)
            {
                return false;
            }

            observation = first;
            return true;
        }
        catch
        {
            // No managed exception may cross the native callback boundary.
            observation = default;
            return false;
        }
    }

    internal bool TryReadPostCallResult(out int result)
    {
        result = 0;
        try
        {
            if (!addressSpace.HasExpectedResolverSignature()
                || !TryResolveCurrentIdentity(
                    out var hostAddressBefore,
                    out var generationBefore)
                || !frame.TryReadPostCallEax(out var rawResult)
                || !addressSpace.HasExpectedResolverSignature()
                || !TryResolveCurrentIdentity(
                    out var hostAddressAfter,
                    out var generationAfter)
                || hostAddressBefore != hostAddressAfter
                || generationBefore != generationAfter)
            {
                return false;
            }

            result = unchecked((int)rawResult);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private bool TryResolveCurrentIdentity(
        out ulong hostAddress,
        out long validationGeneration)
    {
        hostAddress = 0;
        var lease = Volatile.Read(ref activeHookLease);
        if (lease is not null)
        {
            validationGeneration = lease.Generation;
            try
            {
                return IsEnabled(lease.IsHookEnabled)
                       && functionValidator.TryValidateMappedTarget(
                           FunctionMap,
                           out hostAddress)
                       && hostAddress == lease.HostAddress;
            }
            catch
            {
                hostAddress = 0;
                return false;
            }
        }

        validationGeneration = Volatile.Read(ref validationEpoch);
        try
        {
            return functionValidator.TryValidate(FunctionMap, out hostAddress);
        }
        catch
        {
            hostAddress = 0;
            return false;
        }
    }

    private static bool IsEnabled(Func<bool> isHookEnabled)
    {
        try
        {
            return isHookEnabled();
        }
        catch
        {
            return false;
        }
    }

    private static TranslatedX86AddressSpace CreateResearchAddressSpace(
        ulong moduleBase,
        INativeMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var researchAddressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
        if (!researchAddressSpace.HasExpectedResolverSignature())
        {
            throw new InvalidDataException(
                "The translated x86 resolver identity is unavailable or unstable.");
        }

        return researchAddressSpace;
    }

    private sealed record ActiveHookLease(
        long Generation,
        ulong HostAddress,
        Func<bool> IsHookEnabled);
}
