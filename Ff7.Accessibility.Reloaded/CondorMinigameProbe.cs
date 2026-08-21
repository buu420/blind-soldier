using System.Text;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Finds the Fort Condor battle's live state in memory.
/// </summary>
/// <remarks>
/// The fort battle runs in module 9 and draws none of its interface through any
/// routine the mod can hook. Three separate lines of evidence agree: the text
/// render hooks logged 294,020 draws across the 2026-08-19 session and nothing
/// at all during the battle; a Ghidra call-path scan found no route from any
/// module 9 reader to a draw target; and condor.lgp itself contains no strings,
/// only models, textures and four data blobs. Every word on that screen is baked
/// into a texture, so the only way to speak it is to read the state it is drawn
/// from and supply the wording ourselves.
///
/// <para>The first version of this probe ranked bytes by how often they changed
/// and reported the ones that moved rarely. That was the wrong instrument. It
/// cannot tell a byte that changed because the player pressed Up from one that
/// changed because a sprite moved, and the 2026-08-20 capture came back full of
/// animation counters. It also logged an input word that belongs to the field
/// module, so every sample read zero and appeared to say the player had pressed
/// nothing.</para>
///
/// <para>This version is driven by the player instead. Pressing the marker key
/// takes a labelled snapshot; the probe reports exactly what changed between one
/// snapshot and the next. Each snapshot is taken twice a short distance apart and
/// keeps only the bytes that agree, so anything still animating is excluded
/// before the comparison rather than filtered afterwards. A capture is then a
/// sequence of deliberate actions, each with its own diff: press the marker, do
/// one thing, press the marker again.</para>
/// </remarks>
public sealed class CondorMinigameProbe
{
    public const byte CondorModule = 9;

    /// <summary>The FF7 data segment, where module globals live.</summary>
    public const uint RegionStart = 0x00C00000;
    public const uint RegionEnd = 0x00E00000;
    public const int PageSize = 0x1000;

    /// <summary>
    /// Widened for the one-shot unit table hunt. The old window is where module
    /// globals live, but the loaded minigame data need not be there at all, and
    /// the 2026-08-20 capture never found the table inside it.
    /// </summary>
    public const uint SearchStart = 0x00400000;
    public const uint SearchEnd = 0x02000000;

    /// <summary>
    /// The first twenty bytes of record 1 of condor.lgp's unit table.
    ///
    /// <para>An earlier version of this file described this as spanning the
    /// boundary of records 0 and 1, and subtracted 0x16 to reach the table. That
    /// was wrong. data.bin holds the table offset in a 16-bit word at its own
    /// start, which reads 0x26, and this signature sits at 0x46 — exactly one
    /// 0x20 record later. The table is therefore at the offset the header gives,
    /// and the signature begins one whole record into it.</para>
    /// </summary>
    public static ReadOnlySpan<byte> UnitTableSignature =>
    [
        0x90, 0x01, 0xC8, 0x01, 0x01, 0x1E, 0x01, 0x01, 0x01, 0x01,
        0x03, 0x00, 0x1F, 0x16, 0x1E, 0x00, 0x00, 0x01, 0x02, 0x03
    ];

    /// <summary>
    /// The globals Ghidra reached from FFNx's Fort Condor texture-loader anchor.
    /// These are candidates under test, not a decoded interface: the probe reads
    /// them back so a player moving the cursor can hear whether they follow, which
    /// is the only way to confirm them without a debugger attached to a live
    /// battle. Cursor X and Y are already corroborated — they were the two values
    /// in the 2026-08-21 capture that moved in both directions in step with the
    /// player — and the rest are unverified.
    /// </summary>
    private const uint AddressCursorX = 0x00CBCCC0;
    private const uint AddressCursorY = 0x00CBCCC2;
    private const uint AddressInteractionMode = 0x00C74C50;
    private const uint AddressModalState = 0x00C625E0;
    private const uint AddressSettingMenuRow = 0x00CBCCA0;
    private const uint AddressAllyUnitRow = 0x00CBC930;
    private const uint AddressSelectedUnit = 0x00C6097C;

    /// <summary>
    /// Ticks between the two reads that make up one snapshot. Anything that
    /// differs across them is still moving and is not part of a settled state.
    /// </summary>
    private const int SettleTicks = 2;

    /// <summary>Probe ticks the cursor must hold still before it is spoken.</summary>
    private const int CursorSettleTicks = 3;

    /// <summary>
    /// Changed bytes are reported as contiguous runs. Two runs closer together
    /// than this are joined, so a 32-bit field whose middle byte happens to be
    /// unchanged still reads as one field.
    /// </summary>
    private const int RunJoinGap = 3;

    private const int MaximumReportedRuns = 160;

    private readonly ILegacyAddressSpace memory;
    private readonly Action<string> log;

    /// <summary>
    /// Says what each mark found. A capture is run by someone who cannot see the
    /// screen and, until this state is decoded, cannot hear it either, so a probe
    /// that only wrote to a log would leave them pressing a key with no way to
    /// tell whether it registered or whether the action before it did anything.
    /// </summary>
    private readonly Action<string> speak;
    private byte[]? pendingFirstRead;
    private SettledSnapshot? settledPrevious;
    private int settleCountdown;
    private int markerNumber;
    private bool markerRequested;
    private bool active;
    private int lastCursorX = -1;
    private int lastCursorY = -1;
    private int lastMode = -1;
    private int lastModal = -1;
    private int lastSettingRow = -1;
    private int lastAllyRow = -1;
    private int lastSelectedUnit = -1;
    private int pendingCursorX = -1;
    private int pendingCursorY = -1;
    private int cursorSettleTicks;
    private bool searchedForUnitTable;

    public CondorMinigameProbe(
        ILegacyAddressSpace memory, Action<string> log, Action<string>? speak = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.speak = speak ?? (_ => { });
    }

    /// <summary>
    /// Asks for a snapshot at the next opportunity. Called from the caller's fast
    /// path rather than passed into <see cref="Tick"/>, because this probe reads
    /// two megabytes per sample and runs only a few times a second: a key tap
    /// lasts a fraction of that and would fall between two ticks unobserved. The
    /// request is held until a capture actually starts, so a press that lands
    /// while the previous snapshot is still settling is honoured rather than lost.
    /// </summary>
    public void MarkRequested() => markerRequested = true;

    public void Tick(byte currentModule)
    {
        if (currentModule != CondorModule)
        {
            if (active)
            {
                log($"Fort Condor probe: left module {CondorModule} after {markerNumber} marker(s).");
                Reset();
            }

            markerRequested = false;
            return;
        }

        if (!active)
        {
            active = true;
            log(
                $"Fort Condor probe: module {CondorModule} entered. " +
                "Press the marker key once before and once after each deliberate action; " +
                "each press reports what settled memory changed since the previous press.");
            speak("Fort Condor probe armed. Press F9 to mark.");
        }

        if (!searchedForUnitTable)
        {
            searchedForUnitTable = true;
            SearchForUnitTable();
        }

        WatchCandidateFields();

        if (markerRequested && pendingFirstRead is null)
        {
            markerRequested = false;
            pendingFirstRead = ReadRegion();
            settleCountdown = SettleTicks;
            return;
        }

        if (pendingFirstRead is null)
        {
            return;
        }

        if (settleCountdown-- > 0)
        {
            return;
        }

        var second = ReadRegion();
        var settled = KeepAgreeingBytes(pendingFirstRead, second);
        pendingFirstRead = null;
        markerNumber++;
        ReportAgainstPrevious(settled);
        settledPrevious = settled;
    }

    /// <summary>
    /// Reads the candidate globals every tick and says the ones that changed.
    /// This is a probe speaking raw numbers, not the finished reader: it says
    /// "cursor 152, 148" rather than naming what is under the cursor, because
    /// nothing here has earned the right to claim it knows that yet. What it does
    /// buy is a player who can hear whether these addresses follow the cursor,
    /// and who can navigate by ear well enough to reach the menus the next pass
    /// needs.
    /// </summary>
    private void WatchCandidateFields()
    {
        var x = ReadUInt16(AddressCursorX);
        var y = ReadUInt16(AddressCursorY);
        var mode = ReadByte(AddressInteractionMode);
        var modal = ReadByte(AddressModalState);
        var settingRow = ReadByte(AddressSettingMenuRow);
        var allyRow = ReadByte(AddressAllyUnitRow);
        var unit = ReadByte(AddressSelectedUnit);

        if (mode != lastMode || modal != lastModal)
        {
            // The mode is what decides which of the four control schemes the
            // player is under, so it is worth interrupting for.
            lastMode = mode;
            lastModal = modal;
            var name = modal == 7
                ? "setting menu"
                : mode switch
                {
                    1 => "cursor",
                    2 => "ally unit",
                    3 => "placement",
                    _ => $"mode {mode}"
                };
            log($"Fort Condor watch: mode={mode}, modal={modal} -> {name}.");
            speak(name);
            return;
        }

        if (settingRow != lastSettingRow)
        {
            lastSettingRow = settingRow;
            log($"Fort Condor watch: setting menu row={settingRow}.");
            speak($"Row {settingRow}.");
            return;
        }

        if (allyRow != lastAllyRow)
        {
            lastAllyRow = allyRow;
            log($"Fort Condor watch: ally unit row={allyRow}.");
            speak($"Ally row {allyRow}.");
            return;
        }

        if (unit != lastSelectedUnit)
        {
            lastSelectedUnit = unit;
            log($"Fort Condor watch: selected unit={unit}.");
            speak($"Unit {unit}.");
            return;
        }

        // The cursor is a pixel coordinate, so a single press may slide it over
        // several frames. Speaking every intermediate value would bury the player
        // in numbers, so wait until it stops moving and say where it came to rest.
        if (x != pendingCursorX || y != pendingCursorY)
        {
            pendingCursorX = x;
            pendingCursorY = y;
            cursorSettleTicks = CursorSettleTicks;
            return;
        }

        if (cursorSettleTicks > 0 && --cursorSettleTicks == 0 &&
            (x != lastCursorX || y != lastCursorY))
        {
            lastCursorX = x;
            lastCursorY = y;
            log($"Fort Condor watch: cursor={x},{y}.");
            speak($"{x}, {y}");
        }
    }

    private ushort ReadUInt16(uint address)
    {
        Span<byte> buffer = stackalloc byte[2];
        return memory.TryRead(address, buffer) ? BitConverter.ToUInt16(buffer) : (ushort)0;
    }

    private byte ReadByte(uint address)
    {
        Span<byte> buffer = stackalloc byte[1];
        return memory.TryRead(address, buffer) ? buffer[0] : (byte)0;
    }

    private byte[] ReadRegion()
    {
        var buffer = new byte[RegionEnd - RegionStart];
        for (var offset = 0u; offset < buffer.Length; offset += PageSize)
        {
            var span = buffer.AsSpan((int)offset, PageSize);
            if (!memory.TryRead(RegionStart + offset, span))
            {
                // An unmapped page inside the window is normal; leave it zero so
                // it never registers as a change.
                span.Clear();
            }
        }

        return buffer;
    }

    /// <summary>
    /// Marks every byte that differs between the two reads as unsettled, by
    /// carrying the second read forward and recording a mask of what to ignore.
    /// </summary>
    private static SettledSnapshot KeepAgreeingBytes(byte[] first, byte[] second)
    {
        var stable = new bool[first.Length];
        for (var i = 0; i < first.Length; i++)
        {
            stable[i] = first[i] == second[i];
        }

        return new SettledSnapshot(second, stable);
    }

    private void ReportAgainstPrevious(SettledSnapshot settled)
    {
        var moving = settled.Stable.Count(value => !value);
        if (settledPrevious is null)
        {
            // Count what is actually mapped. A window that reads back as zeros
            // would also report almost nothing moving, and the two look identical
            // in the log unless the difference is stated.
            var populated = settled.Bytes.Count(value => value != 0);
            log(
                $"Fort Condor probe: marker {markerNumber} is the baseline. " +
                $"{moving:N0} bytes were still moving and are excluded. " +
                $"{populated:N0} of {settled.Bytes.Length:N0} bytes in the window are non-zero.");
            speak("Mark 1. Baseline.");
            return;
        }

        var changed = new List<int>();
        var previous = settledPrevious;
        for (var i = 0; i < settled.Bytes.Length; i++)
        {
            if (!settled.Stable[i] || !previous.Stable[i])
            {
                continue;
            }

            if (settled.Bytes[i] != previous.Bytes[i])
            {
                changed.Add(i);
            }
        }

        if (changed.Count == 0)
        {
            log(
                $"Fort Condor probe: marker {markerNumber} — nothing settled changed since " +
                $"marker {markerNumber - 1}. ({moving:N0} bytes still moving.)");

            // Worth saying out loud rather than only writing down. It usually
            // means the action did not take, and the run can be repeated on the
            // spot instead of being discovered as a hole afterwards.
            speak($"Mark {markerNumber}. No change.");
            return;
        }

        var runs = CoalesceRuns(changed);
        var reported = runs.Take(MaximumReportedRuns).ToArray();
        var builder = new StringBuilder();
        builder.Append(
            $"Fort Condor probe: marker {markerNumber} — {changed.Count:N0} settled byte(s) " +
            $"in {runs.Count:N0} run(s) changed since marker {markerNumber - 1}");
        if (runs.Count > reported.Length)
        {
            builder.Append($" (showing the first {reported.Length}, {runs.Count - reported.Length} not shown)");
        }

        builder.Append(". ");
        builder.Append(string.Join("; ", reported.Select(run => DescribeRun(run, previous, settled))));
        log(builder.ToString());
        speak($"Mark {markerNumber}. {runs.Count} {(runs.Count == 1 ? "field" : "fields")}.");
    }

    private static List<(int Start, int Length)> CoalesceRuns(List<int> offsets)
    {
        var runs = new List<(int Start, int Length)>();
        var start = offsets[0];
        var end = offsets[0];
        foreach (var offset in offsets.Skip(1))
        {
            if (offset - end <= RunJoinGap)
            {
                end = offset;
                continue;
            }

            runs.Add((start, end - start + 1));
            start = offset;
            end = offset;
        }

        runs.Add((start, end - start + 1));
        return runs;
    }

    private static string DescribeRun(
        (int Start, int Length) run, SettledSnapshot before, SettledSnapshot after)
    {
        var address = RegionStart + (uint)run.Start;
        var oldBytes = Hex(before.Bytes, run.Start, run.Length);
        var newBytes = Hex(after.Bytes, run.Start, run.Length);
        var description = $"0x{address:X8}[{run.Length}] {oldBytes}->{newBytes}";

        // A run that is exactly one, two or four bytes wide is most likely a
        // single field, so give the value a reader would actually compare.
        if (run.Length is 1 or 2 or 4)
        {
            description +=
                $" ({ReadValue(before.Bytes, run.Start, run.Length)}" +
                $"->{ReadValue(after.Bytes, run.Start, run.Length)})";
        }

        return description;
    }

    private static string Hex(byte[] bytes, int start, int length) =>
        string.Concat(bytes.AsSpan(start, length).ToArray().Select(value => value.ToString("X2")));

    private static long ReadValue(byte[] bytes, int start, int length) => length switch
    {
        1 => bytes[start],
        2 => BitConverter.ToUInt16(bytes, start),
        _ => BitConverter.ToUInt32(bytes, start)
    };

    /// <summary>
    /// One pass over a much wider range than the sampling window, because the
    /// loaded minigame data need not live beside the module globals.
    /// </summary>
    private void SearchForUnitTable()
    {
        var signature = UnitTableSignature;
        var window = new byte[PageSize + signature.Length];
        for (var page = SearchStart; page < SearchEnd; page += PageSize)
        {
            var span = window.AsSpan(0, PageSize);
            if (!memory.TryRead(page, span))
            {
                continue;
            }

            // Read a little of the next page so a table straddling the boundary
            // is still found.
            var tail = window.AsSpan(PageSize, signature.Length);
            if (!memory.TryRead(page + PageSize, tail))
            {
                tail.Clear();
            }

            var index = window.AsSpan().IndexOf(signature);
            if (index < 0)
            {
                continue;
            }

            // The signature is record 1, so the table itself starts one 0x20
            // record earlier. data.bin's own header word agrees: it reads 0x26
            // and the signature sits at 0x46.
            var tableStart = page + (uint)index - 0x20u;
            log($"Fort Condor probe: unit table found at 0x{tableStart:X8}.");
            return;
        }

        log(
            $"Fort Condor probe: unit table not found between 0x{SearchStart:X8} and 0x{SearchEnd:X8}. " +
            "The table may be transformed on load rather than copied verbatim.");
    }

    private void Reset()
    {
        active = false;
        pendingFirstRead = null;
        settledPrevious = null;
        settleCountdown = 0;
        markerNumber = 0;
        markerRequested = false;
        searchedForUnitTable = false;
        lastCursorX = -1;
        lastCursorY = -1;
        lastMode = -1;
        lastModal = -1;
        lastSettingRow = -1;
        lastAllyRow = -1;
        lastSelectedUnit = -1;
        pendingCursorX = -1;
        pendingCursorY = -1;
        cursorSettleTicks = 0;
    }

    private sealed record SettledSnapshot(byte[] Bytes, bool[] Stable);
}
