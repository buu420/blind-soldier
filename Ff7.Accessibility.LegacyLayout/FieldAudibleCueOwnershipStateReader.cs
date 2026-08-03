using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Resolves field-input ownership from both the legacy active-message count and
/// the native message-window assignment/lifecycle records. FFVII can retain a
/// stale nonzero count after every native window slot has already closed.
/// </summary>
public sealed class FieldAudibleCueOwnershipStateReader
{
    // Ghidra: DAT_00cff5e4 is a ushort lifecycle phase indexed at slot * 0x18,
    // producing the native 0x30-byte slot stride. The adjacent ushort at +2
    // contains the phase-six input flags. WMODE's permanent-window bit is bit
    // zero; native Confirm handling is disabled when it is set.
    public const uint AddressFieldWindowLifecyclePhases = 0x00CFF5E4;
    public const uint FieldWindowLifecycleStride = 0x30;
    public const ushort CompletedTextPhase = 6;
    public const ushort NewPageWaitPhase = 14;

    private const uint FieldWindowLifecycleFlagsOffset = sizeof(ushort);
    private const ushort PermanentNonClosableWindowFlag = 0x0001;

    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldAudibleCueStateReader cueReader;

    public FieldAudibleCueOwnershipStateReader(
        ILegacyAddressSpace addressSpace,
        Func<bool>? hasReadableActiveMessage = null)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        this.addressSpace = addressSpace;
        cueReader = new FieldAudibleCueStateReader(addressSpace, hasReadableActiveMessage);
    }

    public string LastDiagnostic { get; private set; } = "cueRaw=unread";

    public FieldAudibleCueState Read() =>
        TryRead(out var state)
            ? state
            : new FieldAudibleCueState(
                IsSuppressed: true,
                Reason: "unreadable or unstable field ownership",
                Module: 0,
                UserControl: 0,
                ActiveMessageCount: 0,
                MovieActive: 0);

    public bool TryRead(out FieldAudibleCueState state)
    {
        state = default;
        if (!cueReader.TryRead(out var before))
        {
            LastDiagnostic = "cueRaw=unread";
            return false;
        }

        if (!NeedsWindowOwnershipConfirmation(before))
        {
            LastDiagnostic = $"{FormatRawCue(before)}; windows=not-sampled";
            state = before;
            return true;
        }

        if (!TryClassifyActiveWindowSlots(
                out var noActiveWindowSlots,
                out var hasOnlyCompletedPermanentWindows,
                out var hasOnlyMovableNewPageWindows,
                out var windowDiagnostic))
        {
            LastDiagnostic = $"{FormatRawCue(before)}; {windowDiagnostic}";
            return false;
        }

        if (!cueReader.TryRead(out var after))
        {
            LastDiagnostic = $"{FormatRawCue(before)}; {windowDiagnostic}; cueBookend=unread";
            return false;
        }

        if (before != after)
        {
            LastDiagnostic = $"{FormatRawCue(before)}; {windowDiagnostic}; cueBookend=changed";
            return false;
        }

        state = noActiveWindowSlots ||
            hasOnlyCompletedPermanentWindows ||
            hasOnlyMovableNewPageWindows
            ? new FieldAudibleCueState(
                IsSuppressed: false,
                Reason: "gameplay",
                after.Module,
                after.UserControl,
                after.ActiveMessageCount,
                after.MovieActive)
            : after;
        LastDiagnostic =
            $"{FormatRawCue(after)}; {windowDiagnostic}; " +
            $"activeAssigned={!noActiveWindowSlots}; " +
            $"nonModalComplete={hasOnlyCompletedPermanentWindows}; " +
            $"movableNewPage={hasOnlyMovableNewPageWindows}";
        return true;
    }

    private bool TryClassifyActiveWindowSlots(
        out bool noActiveWindowSlots,
        out bool hasOnlyCompletedPermanentWindows,
        out bool hasOnlyMovableNewPageWindows,
        out string diagnostic)
    {
        noActiveWindowSlots = false;
        hasOnlyCompletedPermanentWindows = false;
        hasOnlyMovableNewPageWindows = false;
        diagnostic = "windows=unread";
        Span<byte> beforeStates = stackalloc byte[FieldMessageReader.WindowCount];
        Span<ushort> beforePhases = stackalloc ushort[FieldMessageReader.WindowCount];
        Span<ushort> beforeFlags = stackalloc ushort[FieldMessageReader.WindowCount];
        Span<byte> afterStates = stackalloc byte[FieldMessageReader.WindowCount];
        Span<ushort> afterPhases = stackalloc ushort[FieldMessageReader.WindowCount];
        Span<ushort> afterFlags = stackalloc ushort[FieldMessageReader.WindowCount];
        if (!TryReadWindowOwnership(beforeStates, beforePhases, beforeFlags) ||
            !TryReadWindowOwnership(afterStates, afterPhases, afterFlags))
        {
            return false;
        }

        if (!beforeStates.SequenceEqual(afterStates) ||
            !beforePhases.SequenceEqual(afterPhases) ||
            !beforeFlags.SequenceEqual(afterFlags))
        {
            diagnostic = "windows=changing";
            return false;
        }

        diagnostic = $"{FormatWindowOwnership(beforeStates, beforePhases)}; " +
            FormatWindowFlags(beforeStates, beforePhases, beforeFlags);
        noActiveWindowSlots = true;
        var activeWindowCount = 0;
        var completedPermanentWindowCount = 0;
        var movableNewPageWindowCount = 0;
        for (var index = 0; index < beforeStates.Length; index++)
        {
            if (beforeStates[index] != FieldMessageReader.FreeWindowState &&
                beforePhases[index] != 0)
            {
                noActiveWindowSlots = false;
                activeWindowCount++;
                if (beforePhases[index] == CompletedTextPhase &&
                    (beforeFlags[index] & PermanentNonClosableWindowFlag) != 0)
                {
                    completedPermanentWindowCount++;
                }

                // Ghidra: phase 0x000E is the native {NEW PAGE} wait. When the
                // field user-control byte is zero (the only state that reaches
                // this classifier), the window is proximity/ambient text over
                // live movement rather than a modal conversation.
                if (beforePhases[index] == NewPageWaitPhase)
                {
                    movableNewPageWindowCount++;
                }
            }
        }

        hasOnlyCompletedPermanentWindows = activeWindowCount != 0 &&
            activeWindowCount == completedPermanentWindowCount;
        hasOnlyMovableNewPageWindows = movableNewPageWindowCount != 0 &&
            activeWindowCount == completedPermanentWindowCount + movableNewPageWindowCount;
        return true;
    }

    private bool TryReadWindowOwnership(
        Span<byte> states,
        Span<ushort> phases,
        Span<ushort> flags)
    {
        phases.Clear();
        flags.Clear();
        for (var index = 0; index < states.Length; index++)
        {
            if (!addressSpace.TryReadByte(
                    (uint)(FieldMessageReader.AddressFieldWindowStates + index),
                    out states[index]))
            {
                return false;
            }
        }

        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] == FieldMessageReader.FreeWindowState)
            {
                continue;
            }

            var phaseAddress = AddressFieldWindowLifecyclePhases +
                ((uint)index * FieldWindowLifecycleStride);
            if (!addressSpace.TryReadUInt16(phaseAddress, out phases[index]))
            {
                return false;
            }

            if (phases[index] != CompletedTextPhase)
            {
                continue;
            }

            if (!addressSpace.TryReadUInt16(
                    phaseAddress + FieldWindowLifecycleFlagsOffset,
                    out flags[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatRawCue(FieldAudibleCueState state) =>
        $"cueRaw=module:{state.Module:X2},control:{state.UserControl:X2}," +
        $"messages:{state.ActiveMessageCount:X2},movie:{state.MovieActive:X4}";

    private static string FormatWindowOwnership(
        ReadOnlySpan<byte> states,
        ReadOnlySpan<ushort> phases) =>
        $"windows=[{FormatWindowSlot(states[0], phases[0])}," +
        $"{FormatWindowSlot(states[1], phases[1])}," +
        $"{FormatWindowSlot(states[2], phases[2])}," +
        $"{FormatWindowSlot(states[3], phases[3])}]";

    private static string FormatWindowSlot(byte state, ushort phase) =>
        state == FieldMessageReader.FreeWindowState
            ? $"{state:X2}/--"
            : $"{state:X2}/{phase:X4}";

    private static string FormatWindowFlags(
        ReadOnlySpan<byte> states,
        ReadOnlySpan<ushort> phases,
        ReadOnlySpan<ushort> flags) =>
        $"windowFlags=[{FormatWindowFlag(states[0], phases[0], flags[0])}," +
        $"{FormatWindowFlag(states[1], phases[1], flags[1])}," +
        $"{FormatWindowFlag(states[2], phases[2], flags[2])}," +
        $"{FormatWindowFlag(states[3], phases[3], flags[3])}]";

    private static string FormatWindowFlag(byte state, ushort phase, ushort flags) =>
        state == FieldMessageReader.FreeWindowState || phase != CompletedTextPhase
            ? "--"
            : $"{flags:X4}";

    private static bool NeedsWindowOwnershipConfirmation(FieldAudibleCueState state) =>
        state.Module == FieldPositionReader.FieldModule &&
        state.MovieActive == 0 &&
        state.UserControl == 0 &&
        state.ActiveMessageCount != 0;
}
