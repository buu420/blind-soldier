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
    /// The command byte that owns the argument word above. Verified as a byte:
    /// every access in the executable is <c>byte ptr</c>, and FUN_0040B27B branches
    /// on it at 0x0040B2B4, 0x0040B2DC, 0x0040B337 and 0x0040B34C - 3 opens the
    /// prepared film, 4 starts it, 0x14 stops it. Other commands own the same
    /// argument word for their own purposes, which is why the film number is only a
    /// film number while this says so.
    /// </summary>
    public const int AddressFieldMovieCommand = 0x00CC0D89;

    /// <summary>
    /// The movie skip gate, verified as a byte. All three movie opcode handlers read
    /// it first - PMVIE at 0x0061A22A, MOVIE at 0x0061A32E, MVIEF at 0x0061A43B -
    /// and when it is non-zero they advance the script without playing anything.
    /// Field-script opcode 0x0F SPECIAL (FUN_0061E78C) writes it.
    /// </summary>
    public const int AddressFieldMoviesSkipped = 0x00CC0B68;

    /// <summary>
    /// How far into the film the engine is, in frames. A UInt16 with exactly one
    /// writer, <c>MOV [0x00cc0e10],AX</c> at 0x0063C29B inside the field module's
    /// own frame loop, which assigns it from the media layer's current-frame
    /// provider FUN_00418613 before running field scripts. FUN_00418613 returns 0
    /// when nothing is playing, and FFNx replaces the same provider with one that
    /// returns 0 unless its movie object is playing.
    ///
    /// <para>It is reachable a second way, which is what confirms it: the field
    /// script context pointer at 0x00CBF9D8 has two writers and both store
    /// 0x00CC0D88, and the MVIEF opcode handler FUN_0061A438 reads the frame as the
    /// ushort at context+0x88 - which is this address.</para>
    ///
    /// <para>This is the film's own clock. It stops when the film stops and does not
    /// run on while the engine is paused, which the wall clock does. Do not confuse
    /// it with 0x00CC0B70, the synthetic counter MVIEF returns while films are being
    /// skipped.</para>
    /// </summary>
    public const int AddressFieldMovieFrame = 0x00CC0E10;

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
