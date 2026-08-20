using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Finds the Fort Condor battle's live state in memory.
/// </summary>
/// <remarks>
/// The fort battle runs in module 9 and draws none of its interface through any
/// routine the mod can hook. The 2026-08-19 session proves it: the menu text
/// render hook and both in-game text draw hooks were installed and logged
/// 294,020 draws across that run, and across the whole 23 seconds of the battle
/// they logged nothing at all. The unit names, costs and commands a sighted
/// player reads on that screen are drawn by the minigame's own renderer, so the
/// only way to speak them is to read the state they are drawn from.
///
/// This probe locates that state. It samples a window of the data segment while
/// module 9 is live and reports the bytes that move, discarding the ones that
/// move constantly - frame counters, timers and animation cursors churn every
/// sample and drown out the handful of bytes that only change when the player
/// presses a direction. What survives that filter is the cursor and menu state.
///
/// It also looks for condor.lgp's unit table, whose first two records are a
/// fixed 20-byte signature. Finding it gives the base address of the loaded
/// minigame data, and every table in it is then at a known offset:
/// stride 0x20 from base+0x30, cost u16 at +0x16, HP u8 at +0x18, attack u8 at
/// +0x1B. Those three fields were confirmed against all ten published unit
/// stat lines before this probe was written.
/// </remarks>
public sealed class CondorMinigameProbe
{
    public const byte CondorModule = 9;

    /// <summary>The FF7 data segment, where module globals live.</summary>
    public const uint RegionStart = 0x00C00000;
    public const uint RegionEnd = 0x00E00000;
    public const int PageSize = 0x1000;

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
    /// An address that changes on more than this share of samples is a counter,
    /// not a selection.
    /// </summary>
    private const double ChurnRejectionRatio = 0.34d;

    /// <summary>Report nothing until there is enough evidence to rank by.</summary>
    private const int MinimumSamplesBeforeReporting = 12;

    private const int MaximumReportedAddresses = 24;

    private readonly ILegacyAddressSpace memory;
    private readonly Action<string> log;
    private readonly Dictionary<uint, int> changeCounts = new();
    private readonly Dictionary<uint, byte> lastValues = new();
    private readonly HashSet<uint> changedThisSample = new();
    private byte[]? previous;
    private int sampleCount;
    private bool active;
    private bool searchedForUnitTable;
    private string lastReport = string.Empty;

    public CondorMinigameProbe(ILegacyAddressSpace memory, Action<string> log)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Tick(byte currentModule, uint rawInput)
    {
        if (currentModule != CondorModule)
        {
            if (active)
            {
                Report(rawInput, final: true);
                Reset();
            }

            return;
        }

        if (!active)
        {
            active = true;
            log($"Fort Condor probe: module {CondorModule} entered, sampling 0x{RegionStart:X8}-0x{RegionEnd:X8}.");
        }

        var current = ReadRegion();
        if (current is null)
        {
            return;
        }

        if (!searchedForUnitTable)
        {
            searchedForUnitTable = true;
            ReportUnitTableLocation(current);
        }

        if (previous is not null)
        {
            sampleCount++;
            changedThisSample.Clear();
            for (var offset = 0; offset < current.Length; offset++)
            {
                if (current[offset] == previous[offset])
                {
                    continue;
                }

                var address = RegionStart + (uint)offset;
                changedThisSample.Add(address);
                changeCounts[address] = changeCounts.GetValueOrDefault(address) + 1;
                lastValues[address] = current[offset];
            }

            Report(rawInput, final: false);
        }

        previous = current;
    }

    private byte[]? ReadRegion()
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

    private void ReportUnitTableLocation(byte[] region)
    {
        var signature = UnitTableSignature;
        for (var offset = 0; offset + signature.Length <= region.Length; offset++)
        {
            if (!region.AsSpan(offset, signature.Length).SequenceEqual(signature))
            {
                continue;
            }

            // The signature spans records 0 and 1, so the table starts 0x16
            // bytes earlier and the data block 0x30 before that.
            var tableStart = RegionStart + (uint)offset - 0x16u;
            log(
                $"Fort Condor probe: unit table found at 0x{tableStart:X8} " +
                $"(condor data base 0x{tableStart - 0x30u:X8}).");
            return;
        }

        log("Fort Condor probe: unit table not present in the sampled window.");
    }

    private void Report(uint rawInput, bool final)
    {
        if (sampleCount < MinimumSamplesBeforeReporting)
        {
            return;
        }

        var candidates = changeCounts
            .Where(entry => entry.Value / (double)sampleCount <= ChurnRejectionRatio)
            .Where(entry => final || changedThisSample.Contains(entry.Key))
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Take(MaximumReportedAddresses)
            .Select(entry =>
                $"0x{entry.Key:X8}={lastValues.GetValueOrDefault(entry.Key)} " +
                $"({entry.Value}/{sampleCount})")
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var report =
            $"Fort Condor probe: input=0x{rawInput:X8}, samples={sampleCount}, " +
            $"steady movers: {string.Join(", ", candidates)}";
        if (!final && string.Equals(report, lastReport, StringComparison.Ordinal))
        {
            return;
        }

        lastReport = report;
        log(final ? report + " (final)" : report);
    }

    private void Reset()
    {
        active = false;
        previous = null;
        sampleCount = 0;
        searchedForUnitTable = false;
        lastReport = string.Empty;
        changeCounts.Clear();
        lastValues.Clear();
        changedThisSample.Clear();
    }
}
