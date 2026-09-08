using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldAudibleCueState(
    bool IsSuppressed,
    string Reason,
    byte Module,
    byte UserControl,
    byte ActiveMessageCount,
    ushort MovieActive)
{
    public bool SuppressFootsteps => false;
}

public sealed class FieldAudibleCueStateReader
{
    public const int AddressUserControl = 0x00CC040C;
    public const int AddressActiveFieldMessageCount = 0x00CC0B64;
    public const int AddressFieldMovieActive = 0x00CC1638;

    /// <summary>
    /// The native film number the engine is currently running. FUN_0040B27B pushes
    /// this word into the movie-open call FUN_0040ADB0 at 0x0040B2FC, so it is the
    /// identity that separates two films started from the same field: the Gold
    /// Saucer arrival panorama is film 40 and the docking film that follows it in
    /// the same field is film 4.
    /// </summary>
    public const int AddressFieldMovieNumber = 0x00CC0D8A;

    /// <summary>
    /// The field script context pointer the MOVIE opcode handler reads. FUN_0061A321
    /// loads it at 0x0061A36C, takes the state byte at +0x01 and, when that state is
    /// 4, the phase word at +0x26. State 0 is a fresh MOVIE entry; state 4 with
    /// phase 1 is the handler yielding on a film already running, and phase 2 is the
    /// completion that clears the state and advances the script pointer. This is what
    /// separates a genuinely new film from the same opcode arriving again on every
    /// frame of the film it already started.
    /// </summary>
    public const int AddressFieldScriptContextPointer = 0x00CBF9D8;

    public const int FieldScriptContextStateOffset = 0x01;
    public const int FieldScriptContextPhaseOffset = 0x26;

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<bool>? hasReadableActiveMessage;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldAudibleCueStateReader(
        Func<int, byte> readByte,
        Func<int, ushort> readUInt16,
        Func<bool>? hasReadableActiveMessage = null)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.hasReadableActiveMessage = hasReadableActiveMessage;
    }

    public FieldAudibleCueStateReader(
        ILegacyAddressSpace addressSpace,
        Func<bool>? hasReadableActiveMessage = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.hasReadableActiveMessage = hasReadableActiveMessage;
    }

    public FieldAudibleCueState Read()
    {
        if (addressSpace is null)
        {
            return TryReadLegacy(out var legacyState)
                ? legacyState
                : CreateUnavailableState("unstable field state");
        }

        return TryReadChecked(out var state, out var failure)
            ? state
            : CreateUnavailableState(
                failure == FieldAudibleCueReadFailure.Changed
                    ? "unstable field state"
                    : "unreadable field state");
    }

    public bool TryRead(out FieldAudibleCueState state)
    {
        if (addressSpace is null)
        {
            return TryReadLegacy(out state);
        }

        return TryReadChecked(out state, out _);
    }

    private bool TryReadLegacy(out FieldAudibleCueState state)
    {
        state = default;
        var candidate = ReadLegacyFrame();
        var confirmation = ReadLegacyFrame();
        if (candidate != confirmation)
        {
            return false;
        }

        state = CreateState(candidate);
        return true;
    }

    private bool TryReadChecked(
        out FieldAudibleCueState state,
        out FieldAudibleCueReadFailure failure)
    {
        state = default;
        failure = FieldAudibleCueReadFailure.Unreadable;
        if (!TryReadCheckedFrame(out var candidate) || !TryReadCheckedFrame(out var confirmation))
        {
            return false;
        }

        if (candidate != confirmation)
        {
            failure = FieldAudibleCueReadFailure.Changed;
            return false;
        }

        state = CreateState(candidate);
        failure = FieldAudibleCueReadFailure.None;
        return true;
    }

    private FieldAudibleCueFrame ReadLegacyFrame()
    {
        var activeMessageCount = readByte!(AddressActiveFieldMessageCount);
        return new FieldAudibleCueFrame(
            readByte!(FieldPositionReader.AddressCurrentModule),
            readUInt16!(FieldPositionReader.AddressFieldId),
            readByte!(AddressUserControl),
            activeMessageCount,
            readUInt16!(AddressFieldMovieActive),
            ReadActiveMessageOwnership(activeMessageCount));
    }

    private bool TryReadCheckedFrame(out FieldAudibleCueFrame frame)
    {
        frame = default;
        var checkedAddressSpace = addressSpace!;
        if (!checkedAddressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !checkedAddressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !checkedAddressSpace.TryReadByte((uint)AddressUserControl, out var userControl) ||
            !checkedAddressSpace.TryReadByte((uint)AddressActiveFieldMessageCount, out var activeMessageCount) ||
            !checkedAddressSpace.TryReadUInt16((uint)AddressFieldMovieActive, out var movieActive))
        {
            return false;
        }

        frame = new FieldAudibleCueFrame(
            module,
            fieldId,
            userControl,
            activeMessageCount,
            movieActive,
            ReadActiveMessageOwnership(activeMessageCount));
        return true;
    }

    private bool ReadActiveMessageOwnership(byte activeMessageCount) =>
        activeMessageCount == 0 || (hasReadableActiveMessage?.Invoke() ?? true);

    private static FieldAudibleCueState CreateState(FieldAudibleCueFrame frame)
    {
        var reason = frame.Module != FieldPositionReader.FieldModule
            ? "not field gameplay"
            : frame.MovieActive != 0
                ? "movie"
                : frame.UserControl != 0
                    ? "scripted control lock"
                    : frame.ActiveMessageCount != 0
                        ? frame.HasReadableActiveMessage
                            ? "dialogue"
                            : "dialogue unavailable"
                        : "gameplay";
        return new FieldAudibleCueState(
            reason != "gameplay",
            reason,
            frame.Module,
            frame.UserControl,
            frame.ActiveMessageCount,
            frame.MovieActive);
    }

    private static FieldAudibleCueState CreateUnavailableState(string reason) =>
        new(true, reason, 0, 0, 0, 0);

    private readonly record struct FieldAudibleCueFrame(
        byte Module,
        ushort FieldId,
        byte UserControl,
        byte ActiveMessageCount,
        ushort MovieActive,
        bool HasReadableActiveMessage);

    private enum FieldAudibleCueReadFailure
    {
        None,
        Unreadable,
        Changed
    }
}
