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
    /// Records 0 and 1 of condor.lgp's unit table, spanning their boundary:
    /// Fighter's cost/HP/attack tail followed by Attacker's header.
    /// </summary>
    public static ReadOnlySpan<byte> UnitTableSignature =>
    [
        0x90, 0x01, 0xC8, 0x01, 0x01, 0x1E, 0x01, 0x01, 0x01, 0x01,
        0x03, 0x00, 0x1F, 0x16, 0x1E, 0x00, 0x00, 0x01, 0x02, 0x03
    ];

    /// <summary>
    /// Ticks between the two reads that make up one snapshot. Anything that
    /// differs across them is still moving and is not part of a settled state.
    /// </summary>
    private const int SettleTicks = 2;

    /// <summary>
    /// Changed bytes are reported as contiguous runs. Two runs closer together
    /// than this are joined, so a 32-bit field whose middle byte happens to be
    /// unchanged still reads as one field.
    /// </summary>
    private const int RunJoinGap = 3;

    private const int MaximumReportedRuns = 160;

    private readonly ILegacyAddressSpace memory;
    private readonly Action<string> log;
    private byte[]? pendingFirstRead;
    private SettledSnapshot? settledPrevious;
    private int settleCountdown;
    private int markerNumber;
    private bool markerWasDown;
    private bool active;
    private bool searchedForUnitTable;

    public CondorMinigameProbe(ILegacyAddressSpace memory, Action<string> log)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <param name="markerDown">
    /// Whether the marker key is held this tick. The probe acts on the press, not
    /// the hold, so leaning on the key does not produce a snapshot per tick.
    /// </param>
    public void Tick(byte currentModule, bool markerDown)
    {
        if (currentModule != CondorModule)
        {
            if (active)
            {
                log($"Fort Condor probe: left module {CondorModule} after {markerNumber} marker(s).");
                Reset();
            }

            markerWasDown = markerDown;
            return;
        }

        if (!active)
        {
            active = true;
            log(
                $"Fort Condor probe: module {CondorModule} entered. " +
                "Press the marker key once before and once after each deliberate action; " +
                "each press reports what settled memory changed since the previous press.");
        }

        if (!searchedForUnitTable)
        {
            searchedForUnitTable = true;
            SearchForUnitTable();
        }

        var pressed = markerDown && !markerWasDown;
        markerWasDown = markerDown;

        if (pressed && pendingFirstRead is null)
        {
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
            log(
                $"Fort Condor probe: marker {markerNumber} is the baseline. " +
                $"{moving:N0} bytes were still moving and are excluded.");
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

            // The signature spans records 0 and 1, so the table starts 0x16
            // bytes earlier and the data block 0x30 before that.
            var tableStart = page + (uint)index - 0x16u;
            log(
                $"Fort Condor probe: unit table found at 0x{tableStart:X8} " +
                $"(condor data base 0x{tableStart - 0x30u:X8}).");
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
        searchedForUnitTable = false;
    }

    private sealed record SettledSnapshot(byte[] Bytes, bool[] Stable);
}
