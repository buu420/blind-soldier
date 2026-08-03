using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Owns the exact callback identities used by the shared cutscene-description
/// catalog and the checked translated-guest script reader. It installs no hook
/// and exposes no unmanaged call surface.
/// </summary>
internal sealed class Steam2026FieldCutsceneCallbackContract
{
    private readonly object hookLeaseLock = new();
    private readonly Steam2026FieldCutsceneCallbackCatalog catalog;
    private readonly FieldScriptContextReader contextReader;
    private ActiveHookLease? activeHookLease;
    private long validationEpoch;

    internal Steam2026FieldCutsceneCallbackContract(
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

    internal Steam2026FieldCutsceneCallbackContract(
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

    private Steam2026FieldCutsceneCallbackContract(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        TranslatedX86AddressSpace addressSpace,
        bool hasExactSupportedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var validator = new TranslatedFunctionMapValidator(
            moduleBase,
            moduleImageSize,
            memory);
        catalog = new Steam2026FieldCutsceneCallbackCatalog(validator);
        contextReader = new FieldScriptContextReader(addressSpace);
        HasExactSupportedFingerprint = hasExactSupportedFingerprint;
    }

    internal bool HasExactSupportedFingerprint { get; }

    internal void ActivateHookLease(
        Func<Steam2026FieldCutsceneCallbackKind, bool> isHookEnabled)
    {
        ArgumentNullException.ThrowIfNull(isHookEnabled);
        lock (hookLeaseLock)
        {
            if (activeHookLease is not null)
            {
                throw new InvalidOperationException(
                    "A translated field-cutscene hook lease is already active.");
            }

            if (!HasExactSupportedFingerprint)
            {
                throw new InvalidOperationException(
                    "A translated field-cutscene hook lease requires the exact supported fingerprint.");
            }

            foreach (var kind in Enum.GetValues<Steam2026FieldCutsceneCallbackKind>())
            {
                if (!IsEnabled(isHookEnabled, kind)
                    || !catalog.TryValidateMappedIdentity(kind, out var identity)
                    || identity.Metadata.Kind != kind
                    || identity.Metadata.HostAbi
                        != TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments)
                {
                    throw new InvalidOperationException(
                        $"The active translated {kind.ToString().ToUpperInvariant()} hook identity is unavailable.");
                }
            }

            var generation = Interlocked.Increment(ref validationEpoch);
            Volatile.Write(
                ref activeHookLease,
                new ActiveHookLease(generation, isHookEnabled));
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

    internal bool TryValidateCaptureIdentity(
        Steam2026FieldCutsceneCallbackKind kind,
        out Steam2026FieldCutsceneCallbackIdentity identity)
    {
        identity = default;
        if (!IsSupportedKind(kind)
            || !catalog.TryValidateIdentity(kind, out var candidate)
            || candidate.Metadata.Kind != kind
            || candidate.Metadata.HostAbi
                != TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments)
        {
            return false;
        }

        identity = candidate;
        return true;
    }

    internal bool IsCurrentCaptureIdentity(
        Steam2026FieldCutsceneCallbackIdentity identity) =>
        IsSupportedKind(identity.Metadata.Kind)
        && identity.Metadata.HostAbi
            == TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments
        && TryResolveCurrentIdentity(identity.Metadata.Kind, out var current, out _)
        && current == identity;

    internal bool TryCaptureContext(
        Steam2026FieldCutsceneCallbackIdentity expectedIdentity,
        out FieldScriptContext context)
    {
        context = default;
        var kind = expectedIdentity.Metadata.Kind;
        if (!IsSupportedKind(kind)
            || expectedIdentity.Metadata.HostAbi
                != TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(kind, out var beforeIdentity, out var beforeGeneration)
            || beforeIdentity != expectedIdentity
            || !contextReader.TryRead(out var candidate)
            || !IsExpectedOpcode(kind, candidate.Opcode)
            || !TryResolveCurrentIdentity(kind, out var afterIdentity, out var afterGeneration)
            || afterGeneration != beforeGeneration
            || afterIdentity != expectedIdentity)
        {
            return false;
        }

        context = candidate;
        return true;
    }

    private bool TryResolveCurrentIdentity(
        Steam2026FieldCutsceneCallbackKind kind,
        out Steam2026FieldCutsceneCallbackIdentity identity,
        out long validationGeneration)
    {
        identity = default;
        var lease = Volatile.Read(ref activeHookLease);
        if (lease is not null)
        {
            validationGeneration = lease.Generation;
            try
            {
                return IsEnabled(lease.IsHookEnabled, kind)
                       && catalog.TryValidateMappedIdentity(kind, out identity);
            }
            catch
            {
                identity = default;
                return false;
            }
        }

        validationGeneration = Volatile.Read(ref validationEpoch);
        try
        {
            return catalog.TryValidateIdentity(kind, out identity);
        }
        catch
        {
            identity = default;
            return false;
        }
    }

    private static bool IsEnabled(
        Func<Steam2026FieldCutsceneCallbackKind, bool> isHookEnabled,
        Steam2026FieldCutsceneCallbackKind kind)
    {
        try
        {
            return isHookEnabled(kind);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedKind(
        Steam2026FieldCutsceneCallbackKind kind) =>
        Enum.IsDefined(kind);

    private static bool IsExpectedOpcode(
        Steam2026FieldCutsceneCallbackKind kind,
        int opcode) =>
        kind switch
        {
            Steam2026FieldCutsceneCallbackKind.Request =>
                opcode == FieldOpcodeAddressResolver.OpcodeRequestIndex,
            Steam2026FieldCutsceneCallbackKind.RequestSw =>
                opcode == FieldOpcodeAddressResolver.OpcodeRequestSwIndex,
            Steam2026FieldCutsceneCallbackKind.RequestEw =>
                opcode == FieldOpcodeAddressResolver.OpcodeRequestEwIndex,
            Steam2026FieldCutsceneCallbackKind.Split =>
                opcode == FieldOpcodeAddressResolver.OpcodeSplitIndex,
            Steam2026FieldCutsceneCallbackKind.Wait =>
                opcode == FieldOpcodeAddressResolver.OpcodeWaitIndex,
            Steam2026FieldCutsceneCallbackKind.Scroll2D =>
                opcode == FieldOpcodeAddressResolver.OpcodeScroll2DIndex,
            Steam2026FieldCutsceneCallbackKind.Fade =>
                opcode == FieldOpcodeAddressResolver.OpcodeFadeIndex,
            Steam2026FieldCutsceneCallbackKind.Anime1 =>
                opcode == FieldOpcodeAddressResolver.OpcodeAnime1Index,
            Steam2026FieldCutsceneCallbackKind.Visibility =>
                opcode == FieldOpcodeAddressResolver.OpcodeVisibilityIndex,
            Steam2026FieldCutsceneCallbackKind.AnimOnceOrHold =>
                opcode is FieldOpcodeAddressResolver.OpcodeAnimOnceIndex
                    or FieldOpcodeAddressResolver.OpcodeAnimHoldIndex,
            Steam2026FieldCutsceneCallbackKind.Canm1Or2 =>
                opcode is FieldOpcodeAddressResolver.OpcodeCanm1Index
                    or FieldOpcodeAddressResolver.OpcodeCanm2Index,
            Steam2026FieldCutsceneCallbackKind.BackgroundOn =>
                opcode == FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex,
            Steam2026FieldCutsceneCallbackKind.Sound =>
                opcode == FieldOpcodeAddressResolver.OpcodeSoundIndex,
            Steam2026FieldCutsceneCallbackKind.Akao =>
                opcode == FieldOpcodeAddressResolver.OpcodeAkaoIndex,
            Steam2026FieldCutsceneCallbackKind.Movie =>
                opcode == FieldOpcodeAddressResolver.OpcodeMovieIndex,
            _ => false
        };

    private static TranslatedX86AddressSpace CreateResearchAddressSpace(
        ulong moduleBase,
        INativeMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
        if (!addressSpace.HasExpectedResolverSignature())
        {
            throw new InvalidDataException(
                "The translated x86 resolver identity is unavailable or unstable.");
        }

        return addressSpace;
    }

    private sealed record ActiveHookLease(
        long Generation,
        Func<Steam2026FieldCutsceneCallbackKind, bool> IsHookEnabled);
}
