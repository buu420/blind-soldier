using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

internal readonly record struct Steam2026AskCursorCapture(
    int FieldId,
    int WindowId,
    int DialogId,
    int FirstQuestionLine,
    int LastQuestionLine,
    int CurrentQuestionLine);

/// <summary>
/// Research-only contract for the translated ASK selection-update wrapper.
/// It validates the exact function-map record, host prefix, resolver, guest
/// stack, owned ASK instruction, and pointed-to highlighted line before
/// returning pointer-free state. It does not install a hook or prove callback
/// ingress; a future native detour must keep capture authority inside its
/// validated callback body before this can feed a runtime capability.
/// </summary>
internal sealed class Steam2026AskCursorCallbackContract
{
    internal static TranslatedFunctionMapDefinition FunctionMap { get; } = new(
        0x006310A1,
        0x016EB840,
        0x00CB64D0,
        "48895C2408574883EC208B0DE8303801");

    private readonly object tokenAuthority = new();
    private readonly object hookLeaseLock = new();
    private readonly TranslatedFunctionMapValidator functionValidator;
    private readonly TranslatedX86AddressSpace addressSpace;
    private readonly TranslatedX86CallFrameReader frame;
    private readonly FieldOpcodeParameterReader opcodeReader;
    private ActiveHookLease? activeHookLease;
    private long validationEpoch;

    internal Steam2026AskCursorCallbackContract(
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

    internal Steam2026AskCursorCallbackContract(
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

    private Steam2026AskCursorCallbackContract(
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
                    "A translated ASK cursor hook lease is already active.");
            }

            if (!HasExactSupportedFingerprint)
            {
                throw new InvalidOperationException(
                    "A translated ASK cursor hook lease requires the exact supported fingerprint.");
            }

            if (!IsEnabled(isHookEnabled)
                || !functionValidator.TryValidateMappedTarget(FunctionMap, out var hostAddress)
                || hostAddress == 0)
            {
                throw new InvalidOperationException(
                    "The active translated ASK cursor hook identity is unavailable.");
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

    internal bool TryCaptureAskCursor(out Steam2026AskCursorCapture capture)
    {
        capture = default;
        Steam2026AskCaptureToken? token = null;
        try
        {
            if (!TryBeginCapture(out token)
                || !TryReadSnapshot(token, out var first)
                || !TryReadSnapshot(token, out var second)
                || first != second
                || !TryFinishCapture(token))
            {
                return false;
            }

            capture = first;
            return true;
        }
        catch
        {
            // This method is intended for a future native callback path. No
            // managed exception may be allowed to escape that boundary.
            capture = default;
            return false;
        }
        finally
        {
            token?.Invalidate();
        }
    }

    private bool TryBeginCapture(out Steam2026AskCaptureToken token)
    {
        token = null!;
        if (!addressSpace.HasExpectedResolverSignature()
            || !TryResolveCurrentIdentity(
                out var relocatedHostAddress,
                out var validationGeneration)
            || !frame.TryReadEsp(out var guestEsp)
            || guestEsp == 0)
        {
            return false;
        }

        token = new Steam2026AskCaptureToken(
            tokenAuthority,
            relocatedHostAddress,
            guestEsp,
            validationGeneration);
        return true;
    }

    private bool TryReadSnapshot(
        Steam2026AskCaptureToken token,
        out Steam2026AskCursorCapture capture)
    {
        capture = default;
        if (!token.IsValidFor(tokenAuthority)
            || !HasExpectedEsp(token)
            || !opcodeReader.TryReadAsk(out var ownedAsk)
            || !TryReadArgument(token, 0, out var rawWindowId))
        {
            return false;
        }

        var windowId = unchecked((byte)rawWindowId);
        if (windowId >= FieldMessageReader.WindowCount
            || !TryReadWindowLifecyclePhase(windowId, out var phaseBefore)
            || phaseBefore != Steam2026FieldAudibleCueStateReader.CompletedTextPhase
            || !TryReadArgument(token, 1, out var rawDialogId)
            || !TryReadArgument(token, 2, out var rawFirstQuestionLine)
            || !TryReadArgument(token, 3, out var rawLastQuestionLine)
            || !TryReadArgument(token, 4, out var currentQuestionLinePointer)
            || currentQuestionLinePointer == 0
            || !addressSpace.TryReadUInt16(
                currentQuestionLinePointer,
                out var currentQuestionLine)
            || !HasExpectedEsp(token)
            || !opcodeReader.TryReadAsk(out var ownedAskAfter)
            || ownedAsk != ownedAskAfter
            || !TryReadWindowLifecyclePhase(windowId, out var phaseAfter)
            || phaseBefore != phaseAfter)
        {
            return false;
        }

        var dialogId = unchecked((byte)rawDialogId);
        var firstQuestionLine = unchecked((byte)rawFirstQuestionLine);
        var lastQuestionLine = unchecked((byte)rawLastQuestionLine);
        if (ownedAsk.Kind != FieldOpcodeKind.Ask
            || ownedAsk.WindowId != windowId
            || ownedAsk.DialogId != dialogId
            || ownedAsk.FirstQuestionLine != firstQuestionLine
            || ownedAsk.LastQuestionLine != lastQuestionLine
            || firstQuestionLine > lastQuestionLine
            || lastQuestionLine >= FieldOpcodeParameterReader.MaximumAskVisibleLineCount
            || currentQuestionLine < firstQuestionLine
            || currentQuestionLine > lastQuestionLine)
        {
            return false;
        }

        capture = new Steam2026AskCursorCapture(
            ownedAsk.FieldId,
            windowId,
            dialogId,
            firstQuestionLine,
            lastQuestionLine,
            currentQuestionLine);
        return true;
    }

    private bool TryReadWindowLifecyclePhase(byte windowId, out ushort phase) =>
        addressSpace.TryReadUInt16(
            Steam2026FieldAudibleCueStateReader.AddressFieldWindowLifecyclePhases
            + ((uint)windowId * Steam2026FieldAudibleCueStateReader.FieldWindowLifecycleStride),
            out phase);

    private bool TryReadArgument(
        Steam2026AskCaptureToken token,
        int argumentIndex,
        out uint value)
    {
        value = 0;
        return token.IsValidFor(tokenAuthority)
               && HasExpectedEsp(token)
               && frame.TryReadArgumentAtEsp(
                   token.GuestEsp,
                   argumentIndex,
                   out value)
               && HasExpectedEsp(token);
    }

    private bool TryFinishCapture(Steam2026AskCaptureToken token) =>
        token.IsValidFor(tokenAuthority)
        && HasExpectedEsp(token)
        && addressSpace.HasExpectedResolverSignature()
        && TryResolveCurrentIdentity(
            out var relocatedHostAddress,
            out var validationGeneration)
        && relocatedHostAddress == token.RelocatedHostAddress
        && validationGeneration == token.ValidationGeneration;

    private bool HasExpectedEsp(Steam2026AskCaptureToken token) =>
        frame.TryReadEsp(out var guestEsp) && guestEsp == token.GuestEsp;

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

internal sealed class Steam2026AskCaptureToken
{
    private readonly object authority;
    private bool isActive = true;

    internal Steam2026AskCaptureToken(
        object authority,
        ulong relocatedHostAddress,
        uint guestEsp,
        long validationGeneration)
    {
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        RelocatedHostAddress = relocatedHostAddress;
        GuestEsp = guestEsp;
        ValidationGeneration = validationGeneration;
    }

    internal ulong RelocatedHostAddress { get; }

    internal uint GuestEsp { get; }

    internal long ValidationGeneration { get; }

    internal bool IsValidFor(object expectedAuthority) =>
        isActive
        && ReferenceEquals(authority, expectedAuthority)
        && RelocatedHostAddress != 0
        && GuestEsp != 0;

    internal void Invalidate() => isActive = false;
}
