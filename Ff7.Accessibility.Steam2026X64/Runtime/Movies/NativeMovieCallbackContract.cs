namespace Ff7.Accessibility.Steam2026X64.Runtime.Movies;

public enum NativeMovieCallbackKind
{
    FrameGetter,
    Prepare,
    Release,
    Start,
    Stop,
    Update
}

public enum NativeMovieCallbackAbi
{
    MicrosoftX64
}

public enum NativeMovieCallbackParameterShape
{
    None,
    Two32BitIntegers
}

public enum NativeMovieCallbackReturnShape
{
    Void,
    BooleanCompatibleInteger,
    Integer32
}

public readonly record struct NativeMovieCallbackShape(
    NativeMovieCallbackAbi Abi,
    NativeMovieCallbackParameterShape Parameters,
    NativeMovieCallbackReturnShape Return);

public readonly record struct NativeMovieCallbackMetadata(
    NativeMovieCallbackKind Kind,
    NativeMovieCallbackShape Shape,
    bool IsInlineDetourEligible);

/// <summary>
/// Immutable proof that one exact callback was validated by one contract.
/// The proof remains usable only while the contract can revalidate its exact
/// address, bytes, and executable main-image page ownership.
/// </summary>
public readonly struct NativeMovieCallbackIdentity
{
    private readonly object? validationOwner;

    internal NativeMovieCallbackIdentity(
        object validationOwner,
        NativeMovieCallbackMetadata metadata,
        ulong address,
        string runtimeSha256)
    {
        this.validationOwner = validationOwner;
        Metadata = metadata;
        Address = address;
        RuntimeSha256 = runtimeSha256;
    }

    public NativeMovieCallbackMetadata Metadata { get; }

    internal ulong Address { get; }

    public string RuntimeSha256 { get; }

    internal bool IsOwnedBy(object owner) => ReferenceEquals(validationOwner, owner);
}

/// <summary>
/// Immutable data copied from one already-intercepted native invocation.
/// It contains no delegate, original-call, hook, publication, or capability
/// surface. A lifecycle consumer must revalidate it through its owning
/// contract before using any value.
/// </summary>
public readonly struct NativeMovieCallbackCapture
{
    private readonly object? validationOwner;

    internal NativeMovieCallbackCapture(
        object validationOwner,
        NativeMovieCallbackIdentity identity,
        long sequence,
        DateTime timestampUtc,
        string? canonicalMoviePath,
        bool succeeded,
        int stateBefore,
        int stateAfter)
    {
        this.validationOwner = validationOwner;
        Identity = identity;
        Sequence = sequence;
        TimestampUtc = timestampUtc;
        CanonicalMoviePath = canonicalMoviePath;
        Succeeded = succeeded;
        StateBefore = stateBefore;
        StateAfter = stateAfter;
    }

    public NativeMovieCallbackIdentity Identity { get; }

    public long Sequence { get; }

    public DateTime TimestampUtc { get; }

    public string? CanonicalMoviePath { get; }

    public bool Succeeded { get; }

    public int StateBefore { get; }

    public int StateAfter { get; }

    internal bool IsOwnedBy(object owner) => ReferenceEquals(validationOwner, owner);
}

/// <summary>
/// Validates exact unpacked Steam 2026 native movie callback identities and
/// copies immutable per-invocation data. This class does not construct or
/// invoke hooks. The seven-byte frame getter is identity evidence only.
/// </summary>
public sealed class NativeMovieCallbackContract
{
    internal const ulong FrameGetterRva = 0x015729F0;
    internal const ulong PrepareRva = 0x01572A00;
    internal const ulong ReleaseRva = 0x01572E40;
    internal const ulong StartRva = 0x01572EC0;
    internal const ulong StopRva = 0x01572EF0;
    internal const ulong UpdateRva = 0x01572F30;

    private const ulong PrepareCallbackRecordRva = 0x016D37F8;
    private const ulong ReleaseCallbackRecordRva = 0x016D3818;
    private const ulong StartCallbackRecordRva = 0x016D3838;
    private const ulong StopCallbackRecordRva = 0x016D3858;
    private const ulong CallbackImplementationPointerOffset = 8;

    private static readonly byte[] FrameGetterSignature = Convert.FromHexString(
        "8B051EA6B000C3");
    private static readonly byte[] PrepareSignature = Convert.FromHexString(
        "48895C2418555657415641574883EC60");
    private static readonly byte[] ReleaseSignature = Convert.FromHexString(
        "48895C2408574883EC20488B3DF766AC");
    private static readonly byte[] StartSignature = Convert.FromHexString(
        "488B0541A0B00083B8FC010000007406");
    private static readonly byte[] StopSignature = Convert.FromHexString(
        "488B0511A0B00033C98988F801000048");
    private static readonly byte[] UpdateSignature = Convert.FromHexString(
        "48895C24104889742418574883EC2083");

    private readonly object validationOwner = new();
    private readonly object hookLeaseLock = new();
    private readonly ulong moduleBase;
    private readonly ulong moduleImageEndExclusive;
    private readonly INativeMemoryReader memory;
    private ActiveHookLease? activeHookLease;
    private long nextCaptureSequence;

    public NativeMovieCallbackContract(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported
            || !fingerprint.Identity.Is64Bit
            || !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal)
            || !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The native movie callback contract requires the exact supported Steam 2026 x64 fingerprint.",
                nameof(fingerprint));
        }

        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        if (moduleImageSize == 0 || moduleBase > ulong.MaxValue - moduleImageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleImageSize));
        }

        this.moduleBase = moduleBase;
        moduleImageEndExclusive = moduleBase + moduleImageSize;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public static NativeMovieCallbackMetadata GetMetadata(NativeMovieCallbackKind kind) =>
        GetDefinition(kind).Metadata;

    internal static ulong GetRva(NativeMovieCallbackKind kind) =>
        GetDefinition(kind).Rva;

    public static NativeMovieCallbackAbi GetAbi(NativeMovieCallbackKind kind) =>
        GetMetadata(kind).Shape.Abi;

    public static bool IsHookable(NativeMovieCallbackKind kind) =>
        GetMetadata(kind).IsInlineDetourEligible;

    public bool HasExpectedSignature(NativeMovieCallbackKind kind) =>
        TryGetDefinition(kind, out var definition)
        && TryValidateTarget(definition, out _);

    public bool TryValidateIdentity(
        NativeMovieCallbackKind kind,
        out NativeMovieCallbackIdentity identity)
    {
        if (IsHookLeaseKind(kind)
            && Volatile.Read(ref activeHookLease) is not null)
        {
            return TryResolveCurrentIdentity(kind, out identity);
        }

        return TryValidateOriginalIdentity(kind, out identity);
    }

    /// <summary>
    /// Leases the four pre-detour exact identities while the detour owner has
    /// their patched entry bytes. The unchanged native callback table and the
    /// complete enabled hook cohort remain the live identity proof.
    /// </summary>
    internal void ActivateHookLease(
        IReadOnlyDictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity> identities,
        Func<NativeMovieCallbackKind, bool> isCohortEnabled)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(isCohortEnabled);

        lock (hookLeaseLock)
        {
            if (activeHookLease is not null)
            {
                throw new InvalidOperationException("A native movie hook lease is already active.");
            }

            if (identities.Count != HookLeaseKinds.Length)
            {
                throw new InvalidOperationException(
                    "A native movie hook lease requires exactly four callback identities.");
            }

            var leasedIdentities = new Dictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity>();
            foreach (var kind in HookLeaseKinds)
            {
                if (!identities.TryGetValue(kind, out var identity)
                    || !IsOwnedIdentityProof(identity, kind)
                    || !TryValidateCallbackTableRecord(kind, identity.Address)
                    || !TryReadCohortState(isCohortEnabled, kind))
                {
                    throw new InvalidOperationException(
                        $"The active native movie hook cohort is incomplete at {kind}.");
                }

                leasedIdentities.Add(kind, identity);
            }

            Volatile.Write(
                ref activeHookLease,
                new ActiveHookLease(leasedIdentities, isCohortEnabled));
        }
    }

    internal void RevokeHookLease()
    {
        lock (hookLeaseLock)
        {
            Volatile.Write(ref activeHookLease, null);
        }
    }

    public bool TryCapturePrepare(
        NativeMovieCallbackIdentity identity,
        DateTime timestampUtc,
        string? canonicalMoviePath,
        bool succeeded,
        out NativeMovieCallbackCapture capture)
    {
        return TryCreateCapture(
            identity,
            NativeMovieCallbackKind.Prepare,
            timestampUtc,
            canonicalMoviePath,
            succeeded,
            stateBefore: 0,
            stateAfter: 0,
            out capture);
    }

    public bool TryCaptureStart(
        NativeMovieCallbackIdentity identity,
        DateTime timestampUtc,
        int stateBefore,
        int stateAfter,
        out NativeMovieCallbackCapture capture)
    {
        return TryCreateCapture(
            identity,
            NativeMovieCallbackKind.Start,
            timestampUtc,
            canonicalMoviePath: null,
            succeeded: false,
            stateBefore,
            stateAfter,
            out capture);
    }

    public bool TryCaptureTerminal(
        NativeMovieCallbackIdentity identity,
        DateTime timestampUtc,
        out NativeMovieCallbackCapture capture)
    {
        capture = default;
        var kind = identity.Metadata.Kind;
        if (kind is not NativeMovieCallbackKind.Release
            and not NativeMovieCallbackKind.Stop)
        {
            return false;
        }

        return TryCreateCapture(
            identity,
            kind,
            timestampUtc,
            canonicalMoviePath: null,
            succeeded: false,
            stateBefore: 0,
            stateAfter: 0,
            out capture);
    }

    internal bool IsCurrentCapture(NativeMovieCallbackCapture capture)
    {
        if (capture.Sequence <= 0
            || !capture.IsOwnedBy(validationOwner)
            || !IsCurrentIdentity(capture.Identity))
        {
            return false;
        }

        return capture.Identity.Metadata.Kind is NativeMovieCallbackKind.Prepare
            or NativeMovieCallbackKind.Release
            or NativeMovieCallbackKind.Start
            or NativeMovieCallbackKind.Stop;
    }

    private bool TryCreateCapture(
        NativeMovieCallbackIdentity identity,
        NativeMovieCallbackKind expectedKind,
        DateTime timestampUtc,
        string? canonicalMoviePath,
        bool succeeded,
        int stateBefore,
        int stateAfter,
        out NativeMovieCallbackCapture capture)
    {
        capture = default;
        if (identity.Metadata.Kind != expectedKind || !IsCurrentIdentity(identity))
        {
            return false;
        }

        var sequence = Interlocked.Increment(ref nextCaptureSequence);
        if (sequence <= 0)
        {
            return false;
        }

        capture = new NativeMovieCallbackCapture(
            validationOwner,
            identity,
            sequence,
            timestampUtc,
            canonicalMoviePath,
            succeeded,
            stateBefore,
            stateAfter);
        return true;
    }

    private bool IsCurrentIdentity(NativeMovieCallbackIdentity identity)
    {
        if (!IsOwnedIdentityProof(identity, identity.Metadata.Kind)
            || !TryResolveCurrentIdentity(identity.Metadata.Kind, out var current))
        {
            return false;
        }

        return identity.Metadata == current.Metadata
               && identity.Address == current.Address
               && string.Equals(
                   identity.RuntimeSha256,
                   current.RuntimeSha256,
                   StringComparison.Ordinal);
    }

    private bool TryResolveCurrentIdentity(
        NativeMovieCallbackKind kind,
        out NativeMovieCallbackIdentity identity)
    {
        identity = default;
        var lease = Volatile.Read(ref activeHookLease);
        if (lease is null || !IsHookLeaseKind(kind))
        {
            return TryValidateOriginalIdentity(kind, out identity);
        }

        if (!TryValidateFullHookCohort(lease)
            || !lease.Identities.TryGetValue(kind, out identity))
        {
            identity = default;
            return false;
        }

        return true;
    }

    private bool TryValidateOriginalIdentity(
        NativeMovieCallbackKind kind,
        out NativeMovieCallbackIdentity identity)
    {
        identity = default;
        if (!TryGetDefinition(kind, out var definition)
            || !TryValidateTarget(definition, out var address))
        {
            return false;
        }

        identity = new NativeMovieCallbackIdentity(
            validationOwner,
            definition.Metadata,
            address,
            Steam2026Fingerprint.SupportedSha256);
        return true;
    }

    private bool IsOwnedIdentityProof(
        NativeMovieCallbackIdentity identity,
        NativeMovieCallbackKind expectedKind)
    {
        if (!identity.IsOwnedBy(validationOwner)
            || identity.Metadata.Kind != expectedKind
            || !string.Equals(
                identity.RuntimeSha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal)
            || !TryGetDefinition(expectedKind, out var definition)
            || identity.Metadata != definition.Metadata
            || !TryAdd(moduleBase, definition.Rva, out var expectedAddress))
        {
            return false;
        }

        return identity.Address == expectedAddress;
    }

    private bool TryValidateFullHookCohort(ActiveHookLease lease)
    {
        foreach (var kind in HookLeaseKinds)
        {
            if (!lease.Identities.TryGetValue(kind, out var identity)
                || !IsOwnedIdentityProof(identity, kind)
                || !TryReadCohortState(lease.IsCohortEnabled, kind)
                || !TryValidateCallbackTableRecord(kind, identity.Address))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryValidateCallbackTableRecord(
        NativeMovieCallbackKind kind,
        ulong expectedAddress)
    {
        if (!TryGetCallbackRecordRva(kind, out var recordRva)
            || !TryAdd(moduleBase, recordRva, out var recordAddress)
            || !TryAdd(recordAddress, CallbackImplementationPointerOffset, out var pointerAddress)
            || !IsInsideMainImage(pointerAddress, sizeof(ulong))
            || !memory.TryQueryRegion(pointerAddress, out var firstRegion)
            || !IsCommittedReadableImageRange(firstRegion, pointerAddress, sizeof(ulong))
            || !memory.TryReadUInt64(pointerAddress, out var firstPointer)
            || !memory.TryReadUInt64(pointerAddress, out var secondPointer)
            || !memory.TryQueryRegion(pointerAddress, out var secondRegion)
            || firstRegion != secondRegion
            || !IsCommittedReadableImageRange(secondRegion, pointerAddress, sizeof(ulong)))
        {
            return false;
        }

        return firstPointer == secondPointer && firstPointer == expectedAddress;
    }

    private bool IsCommittedReadableImageRange(
        NativeMemoryRegion region,
        ulong address,
        ulong length)
    {
        if (!region.IsCommitted
            || !region.IsReadable
            || !region.IsImage
            || region.AllocationBase != moduleBase
            || region.Size == 0
            || address < region.BaseAddress
            || region.BaseAddress > ulong.MaxValue - (region.Size - 1)
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 <= region.BaseAddress + region.Size - 1;
    }

    private bool TryValidateTarget(
        CallbackDefinition definition,
        out ulong address)
    {
        address = 0;
        if (!TryAdd(moduleBase, definition.Rva, out var candidate)
            || !IsInsideMainImage(candidate, (ulong)definition.Signature.Length)
            || !memory.TryQueryRegion(candidate, out var firstRegion)
            || !IsCommittedExecutableImageRange(
                firstRegion,
                candidate,
                (ulong)definition.Signature.Length))
        {
            return false;
        }

        Span<byte> firstActual = stackalloc byte[definition.Signature.Length];
        Span<byte> secondActual = stackalloc byte[definition.Signature.Length];
        if (!memory.TryRead(candidate, firstActual)
            || !memory.TryRead(candidate, secondActual)
            || !memory.TryQueryRegion(candidate, out var secondRegion)
            || firstRegion != secondRegion
            || !IsCommittedExecutableImageRange(
                secondRegion,
                candidate,
                (ulong)definition.Signature.Length)
            || !firstActual.SequenceEqual(secondActual)
            || !firstActual.SequenceEqual(definition.Signature))
        {
            return false;
        }

        address = candidate;
        return true;
    }

    private bool IsInsideMainImage(ulong address, ulong length)
    {
        if (length == 0
            || address < moduleBase
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 < moduleImageEndExclusive;
    }

    private bool IsCommittedExecutableImageRange(
        NativeMemoryRegion region,
        ulong address,
        ulong length)
    {
        if (!region.IsCommitted
            || !region.IsExecutable
            || !region.IsImage
            || region.AllocationBase != moduleBase
            || region.Size == 0
            || address < region.BaseAddress
            || region.BaseAddress > ulong.MaxValue - (region.Size - 1)
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 <= region.BaseAddress + region.Size - 1;
    }

    private static CallbackDefinition GetDefinition(NativeMovieCallbackKind kind)
    {
        if (TryGetDefinition(kind, out var definition))
        {
            return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    private static bool TryGetDefinition(
        NativeMovieCallbackKind kind,
        out CallbackDefinition definition)
    {
        definition = kind switch
        {
            NativeMovieCallbackKind.FrameGetter => Create(
                kind,
                FrameGetterRva,
                NativeMovieCallbackParameterShape.None,
                NativeMovieCallbackReturnShape.Integer32,
                isInlineDetourEligible: false,
                FrameGetterSignature),
            NativeMovieCallbackKind.Prepare => Create(
                kind,
                PrepareRva,
                NativeMovieCallbackParameterShape.Two32BitIntegers,
                NativeMovieCallbackReturnShape.BooleanCompatibleInteger,
                isInlineDetourEligible: true,
                PrepareSignature),
            NativeMovieCallbackKind.Release => Create(
                kind,
                ReleaseRva,
                NativeMovieCallbackParameterShape.None,
                NativeMovieCallbackReturnShape.Void,
                isInlineDetourEligible: true,
                ReleaseSignature),
            NativeMovieCallbackKind.Start => Create(
                kind,
                StartRva,
                NativeMovieCallbackParameterShape.None,
                NativeMovieCallbackReturnShape.BooleanCompatibleInteger,
                isInlineDetourEligible: true,
                StartSignature),
            NativeMovieCallbackKind.Stop => Create(
                kind,
                StopRva,
                NativeMovieCallbackParameterShape.None,
                NativeMovieCallbackReturnShape.Void,
                isInlineDetourEligible: true,
                StopSignature),
            NativeMovieCallbackKind.Update => Create(
                kind,
                UpdateRva,
                NativeMovieCallbackParameterShape.None,
                NativeMovieCallbackReturnShape.BooleanCompatibleInteger,
                isInlineDetourEligible: true,
                UpdateSignature),
            _ => default
        };
        return definition.Signature is not null;
    }

    private static CallbackDefinition Create(
        NativeMovieCallbackKind kind,
        ulong rva,
        NativeMovieCallbackParameterShape parameters,
        NativeMovieCallbackReturnShape returns,
        bool isInlineDetourEligible,
        byte[] signature) =>
        new(
            new NativeMovieCallbackMetadata(
                kind,
                new NativeMovieCallbackShape(
                    NativeMovieCallbackAbi.MicrosoftX64,
                    parameters,
                    returns),
                isInlineDetourEligible),
            rva,
            signature);

    private static bool TryAdd(ulong left, ulong right, out ulong sum)
    {
        if (left > ulong.MaxValue - right)
        {
            sum = 0;
            return false;
        }

        sum = left + right;
        return true;
    }

    private static bool IsHookLeaseKind(NativeMovieCallbackKind kind) =>
        kind is NativeMovieCallbackKind.Prepare
            or NativeMovieCallbackKind.Release
            or NativeMovieCallbackKind.Start
            or NativeMovieCallbackKind.Stop;

    private static bool TryGetCallbackRecordRva(
        NativeMovieCallbackKind kind,
        out ulong recordRva)
    {
        recordRva = kind switch
        {
            NativeMovieCallbackKind.Prepare => PrepareCallbackRecordRva,
            NativeMovieCallbackKind.Release => ReleaseCallbackRecordRva,
            NativeMovieCallbackKind.Start => StartCallbackRecordRva,
            NativeMovieCallbackKind.Stop => StopCallbackRecordRva,
            _ => 0
        };
        return recordRva != 0;
    }

    private static bool TryReadCohortState(
        Func<NativeMovieCallbackKind, bool> isCohortEnabled,
        NativeMovieCallbackKind kind)
    {
        try
        {
            return isCohortEnabled(kind);
        }
        catch
        {
            return false;
        }
    }

    private static NativeMovieCallbackKind[] HookLeaseKinds { get; } =
    [
        NativeMovieCallbackKind.Prepare,
        NativeMovieCallbackKind.Release,
        NativeMovieCallbackKind.Start,
        NativeMovieCallbackKind.Stop
    ];

    private sealed record ActiveHookLease(
        IReadOnlyDictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity> Identities,
        Func<NativeMovieCallbackKind, bool> IsCohortEnabled);

    private readonly record struct CallbackDefinition(
        NativeMovieCallbackMetadata Metadata,
        ulong Rva,
        byte[] Signature);
}
