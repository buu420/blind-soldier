namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// Owns validated callback identities and permits bounded observation capture only
/// while the translated callback identity and guest stack remain unchanged.
/// It installs no hooks and exposes no unmanaged-call surface.
/// </summary>
internal sealed class Steam2026MenuCallbackContract
{
    private const long HookLeaseHealthProbeIntervalMilliseconds = 1000;

    private readonly object tokenAuthority = new();
    private readonly object hookLeaseLock = new();
    private readonly Steam2026MenuCallbackCatalog catalog;
    private readonly Steam2026MenuCallCaptureDecoder decoder;
    private readonly TranslatedX86CallFrameReader frame;
    private ActiveHookLease? activeHookLease;
    private long validationEpoch;
    private long nextHookLeaseHealthProbeMilliseconds;
    private int hookLeaseUnhealthy;

    public Steam2026MenuCallbackContract(
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

    public Steam2026MenuCallbackContract(
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

    private Steam2026MenuCallbackContract(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        TranslatedX86AddressSpace addressSpace,
        bool hasExactSupportedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var validator = new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, memory);
        catalog = new Steam2026MenuCallbackCatalog(validator);
        frame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);
        decoder = new Steam2026MenuCallCaptureDecoder(
            moduleBase,
            memory,
            addressSpace,
            tokenAuthority);
        HasExactSupportedFingerprint = hasExactSupportedFingerprint;
    }

    internal bool HasExactSupportedFingerprint { get; }

    internal void ActivateHookLease(Func<Steam2026MenuCallbackKind, bool> isCohortEnabled)
    {
        ArgumentNullException.ThrowIfNull(isCohortEnabled);
        lock (hookLeaseLock)
        {
            if (activeHookLease is not null)
            {
                throw new InvalidOperationException("A translated menu hook lease is already active.");
            }

            if (!HasExactSupportedFingerprint)
            {
                throw new InvalidOperationException(
                    "A translated menu hook lease requires the exact supported fingerprint.");
            }

            var validatedCohort = new Steam2026MenuCallbackIdentity[
                Enum.GetValues<Steam2026MenuCallbackKind>().Length];
            foreach (var kind in CaptureKinds)
            {
                if (!isCohortEnabled(kind)
                    || !catalog.TryValidateMappedIdentity(kind, out var identity)
                    || identity.Metadata.Kind != kind
                    || !identity.Metadata.IsCaptureEligible
                    || identity.Metadata.HostAbi
                    != TranslatedMenuHostAbi.TranslatedX86VoidNoArguments)
                {
                    throw new InvalidOperationException(
                        $"The active translated menu hook cohort is incomplete at {kind}.");
                }

                validatedCohort[(int)kind] = identity;
            }

            var generation = Interlocked.Increment(ref validationEpoch);
            var lease = new ActiveHookLease(
                generation,
                isCohortEnabled,
                validatedCohort);
            Volatile.Write(ref nextHookLeaseHealthProbeMilliseconds, 0);
            Volatile.Write(ref hookLeaseUnhealthy, 0);
            Volatile.Write(ref activeHookLease, lease);
        }
    }

    internal void RevokeHookLease()
    {
        lock (hookLeaseLock)
        {
            Volatile.Write(ref activeHookLease, null);
            Interlocked.Increment(ref validationEpoch);
            Volatile.Write(ref nextHookLeaseHealthProbeMilliseconds, 0);
            Volatile.Write(ref hookLeaseUnhealthy, 0);
        }
    }

    /// <summary>
    /// Revalidates cached hook ownership from the managed worker at most once
    /// per second. Native callbacks never call this full mapped-identity probe.
    /// </summary>
    internal bool IsActiveHookLeaseHealthy(long monotonicMilliseconds)
    {
        if (Volatile.Read(ref hookLeaseUnhealthy) != 0)
        {
            return false;
        }

        var lease = Volatile.Read(ref activeHookLease);
        if (lease is null)
        {
            return false;
        }

        var nextProbe = Volatile.Read(ref nextHookLeaseHealthProbeMilliseconds);
        if (monotonicMilliseconds < nextProbe)
        {
            return true;
        }

        var followingProbe = checked(
            monotonicMilliseconds + HookLeaseHealthProbeIntervalMilliseconds);
        if (Interlocked.CompareExchange(
                ref nextHookLeaseHealthProbeMilliseconds,
                followingProbe,
                nextProbe) != nextProbe)
        {
            return Volatile.Read(ref hookLeaseUnhealthy) == 0;
        }

        var healthy = TryValidateActiveHookLease(lease);
        if (!ReferenceEquals(lease, Volatile.Read(ref activeHookLease)))
        {
            return true;
        }

        if (!healthy)
        {
            Interlocked.Exchange(ref hookLeaseUnhealthy, 1);
        }

        return healthy;
    }

    internal bool TryValidateCaptureIdentity(
        Steam2026MenuCallbackKind kind,
        out Steam2026MenuCallbackIdentity identity)
    {
        identity = default;
        if (!catalog.TryValidateIdentity(kind, out var candidate)
            || !candidate.Metadata.IsCaptureEligible
            || candidate.Metadata.HostAbi != TranslatedMenuHostAbi.TranslatedX86VoidNoArguments)
        {
            return false;
        }

        identity = candidate;
        return true;
    }

    internal bool IsCurrentCaptureIdentity(Steam2026MenuCallbackIdentity identity) =>
        identity.Metadata.IsCaptureEligible
        && identity.Metadata.HostAbi == TranslatedMenuHostAbi.TranslatedX86VoidNoArguments
        && TryResolveCurrentCaptureIdentity(
            identity.Metadata.Kind,
            out var current,
            out _)
        && current == identity;

    public bool TryCaptureCursor(
        Steam2026MenuCallbackKind source,
        out TranslatedMenuCursorObservation observation)
    {
        observation = default;
        if (!TryBeginCapture(source, out var token))
        {
            return false;
        }

        try
        {
            if (!decoder.TryCaptureCursor(token, source, out var candidate)
                || !TryFinishCapture(token))
            {
                return false;
            }

            observation = candidate;
            return true;
        }
        finally
        {
            token.Invalidate();
        }
    }

    public bool TryCaptureActiveWidget(out TranslatedMenuWidgetObservation observation)
    {
        observation = default;
        if (!TryBeginCapture(Steam2026MenuCallbackKind.ActiveWidgetUpdate, out var token))
        {
            return false;
        }

        try
        {
            if (!decoder.TryCaptureActiveWidget(token, out var candidate)
                || !TryFinishCapture(token))
            {
                return false;
            }

            observation = candidate;
            return true;
        }
        finally
        {
            token.Invalidate();
        }
    }

    public bool TryCaptureEncodedText(
        Steam2026MenuCallbackKind source,
        out TranslatedMenuTextObservation observation)
    {
        observation = default;
        if (!TryBeginCapture(source, out var token))
        {
            return false;
        }

        try
        {
            if (!decoder.TryCaptureEncodedText(token, source, out var candidate)
                || !TryFinishCapture(token))
            {
                return false;
            }

            observation = candidate;
            return true;
        }
        finally
        {
            token.Invalidate();
        }
    }

    public bool TryCaptureAsciiRenderer(out TranslatedMenuTextObservation observation)
    {
        observation = default;
        if (!TryBeginCapture(Steam2026MenuCallbackKind.AsciiRenderer, out var token))
        {
            return false;
        }

        try
        {
            if (!decoder.TryCaptureAsciiRenderer(token, out var candidate)
                || !TryFinishCapture(token))
            {
                return false;
            }

            observation = candidate;
            return true;
        }
        finally
        {
            token.Invalidate();
        }
    }

    private bool TryBeginCapture(
        Steam2026MenuCallbackKind kind,
        out Steam2026MenuCaptureToken token)
    {
        token = null!;
        if (!TryResolveCurrentCaptureIdentity(kind, out var identity, out var validationGeneration)
            || !identity.Metadata.IsCaptureEligible
            || !frame.TryReadEsp(out var guestEsp)
            || guestEsp == 0)
        {
            return false;
        }

        token = new Steam2026MenuCaptureToken(
            tokenAuthority,
            identity,
            guestEsp,
            validationGeneration);
        return true;
    }

    private bool TryFinishCapture(Steam2026MenuCaptureToken token) =>
        token.IsValidFor(tokenAuthority, token.Identity.Metadata.Kind)
        && frame.TryReadEsp(out var guestEsp)
        && guestEsp == token.GuestEsp
        && TryResolveCurrentCaptureIdentity(
            token.Identity.Metadata.Kind,
            out var identity,
            out var validationGeneration)
        && validationGeneration == token.ValidationGeneration
        && identity == token.Identity;

    private bool TryResolveCurrentCaptureIdentity(
        Steam2026MenuCallbackKind kind,
        out Steam2026MenuCallbackIdentity identity,
        out long validationGeneration)
    {
        identity = default;
        var lease = Volatile.Read(ref activeHookLease);
        if (lease is not null)
        {
            validationGeneration = lease.Generation;
            try
            {
                return lease.IsCohortEnabled(kind)
                       && lease.TryGetValidatedIdentity(kind, out identity);
            }
            catch
            {
                identity = default;
                return false;
            }
        }

        validationGeneration = Volatile.Read(ref validationEpoch);
        return catalog.TryValidateIdentity(kind, out identity);
    }

    private bool TryValidateActiveHookLease(ActiveHookLease lease)
    {
        try
        {
            foreach (var kind in CaptureKinds)
            {
                if (!lease.IsCohortEnabled(kind)
                    || !lease.TryGetValidatedIdentity(kind, out var expectedIdentity)
                    || !catalog.TryValidateMappedIdentity(kind, out var currentIdentity)
                    || currentIdentity != expectedIdentity)
                {
                    return false;
                }
            }

            return true;
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
        var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
        if (!addressSpace.HasExpectedResolverSignature())
        {
            throw new InvalidDataException(
                "The translated x86 resolver identity is unavailable or unstable.");
        }

        return addressSpace;
    }

    private static Steam2026MenuCallbackKind[] CaptureKinds { get; } =
    [
        Steam2026MenuCallbackKind.CursorB,
        Steam2026MenuCallbackKind.CursorA,
        Steam2026MenuCallbackKind.ActiveWidgetUpdate,
        Steam2026MenuCallbackKind.EncodedTextB,
        Steam2026MenuCallbackKind.EncodedTextA,
        Steam2026MenuCallbackKind.AsciiRenderer
    ];

    private sealed class ActiveHookLease
    {
        private readonly Steam2026MenuCallbackIdentity[] validatedCohort;

        internal ActiveHookLease(
            long generation,
            Func<Steam2026MenuCallbackKind, bool> isCohortEnabled,
            Steam2026MenuCallbackIdentity[] validatedCohort)
        {
            Generation = generation;
            IsCohortEnabled = isCohortEnabled;
            this.validatedCohort = (Steam2026MenuCallbackIdentity[])
                validatedCohort.Clone();
        }

        internal long Generation { get; }

        internal Func<Steam2026MenuCallbackKind, bool> IsCohortEnabled { get; }

        internal bool TryGetValidatedIdentity(
            Steam2026MenuCallbackKind kind,
            out Steam2026MenuCallbackIdentity identity)
        {
            identity = default;
            var index = (int)kind;
            if ((uint)index >= (uint)validatedCohort.Length)
            {
                return false;
            }

            var candidate = validatedCohort[index];
            if (candidate.Metadata.Kind != kind
                || !candidate.Metadata.IsCaptureEligible
                || candidate.Metadata.HostAbi
                != TranslatedMenuHostAbi.TranslatedX86VoidNoArguments
                || candidate.HostAddress == 0)
            {
                return false;
            }

            identity = candidate;
            return true;
        }
    }
}

internal sealed class Steam2026MenuCaptureToken
{
    private readonly object authority;
    private bool isActive = true;

    internal Steam2026MenuCaptureToken(
        object authority,
        Steam2026MenuCallbackIdentity identity,
        uint guestEsp,
        long validationGeneration = 0)
    {
        this.authority = authority;
        Identity = identity;
        GuestEsp = guestEsp;
        ValidationGeneration = validationGeneration;
    }

    internal Steam2026MenuCallbackIdentity Identity { get; }

    internal uint GuestEsp { get; }

    internal long ValidationGeneration { get; }

    internal bool IsValidFor(object expectedAuthority, Steam2026MenuCallbackKind kind) =>
        isActive
        && ReferenceEquals(authority, expectedAuthority)
        && Identity.Metadata.Kind == kind
        && Identity.Metadata.IsCaptureEligible;

    internal void Invalidate() => isActive = false;
}
