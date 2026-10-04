using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>Runs the shared submarine reader through the checked x64 guest address space.</summary>
internal sealed class Steam2026SubmarineMissionHost(ILegacyAddressSpace memory)
{
    private readonly SubmarineMissionStateReader reader = new(memory);
    private readonly SubmarineMissionReadout readout = new();
    private int previousModule = -1;
    private DateTime inspectReturnUntil;
    private string? lastReturnDiagnostic;
    private DateTime nextReturnHeartbeat;

    internal string Diagnostic => reader.LastDiagnostic;

    internal SubmarineMissionCue Observe(int module, bool foreground, bool enabled,
        DateTime now, Func<bool> repeatRequested, out bool interrupt)
    {
        interrupt = false;
        // Module ownership, not a stale result word, determines when K belongs
        // to the mission. Never wait for unmapped minigame memory after returning
        // to Cloud's dialogue in the field.
        if (module != SubmarineMissionStateReader.MinigameModule || !foreground || !enabled)
        {
            Reset();
            return default;
        }
        if (!reader.TryRead(out var snapshot)) return default;
        var cue = readout.Observe(snapshot, now);
        if (snapshot.IsActive && repeatRequested())
        {
            interrupt = true;
            return cue with { Speech = readout.Describe(snapshot) };
        }
        return cue;
    }

    internal void Reset() => readout.Reset();

    // Read-only evidence for the tester's stuck post-mission dialogue. The old
    // "state13" log was entity 19's window ownership, not the window's phase.
    // 769050 advances phase 6 on script-context+0x80 Confirm (0x20), unless
    // CFF5E6 bit 0 marks a permanent window. Never change either to force progress.
    internal string? ObserveReturn(int module, DateTime now, bool menuOpen, bool suppressing,
        long messageSequence = 0)
    {
        if (previousModule == SubmarineMissionStateReader.MinigameModule &&
            module != SubmarineMissionStateReader.MinigameModule)
        {
            inspectReturnUntil = now.AddSeconds(60);
            lastReturnDiagnostic = null;
            nextReturnHeartbeat = default;
        }
        previousModule = module;
        if (module != 1 || now >= inspectReturnUntil) return null;
        var contextReadable = memory.TryReadUInt32(0x00CBF9D8, out var context) &&
            context != 0 && context <= uint.MaxValue - 0x80;
        uint input = 0;
        var inputReadable = contextReadable && memory.TryReadUInt32(context + 0x80, out input);
        var windows = new List<string>(4);
        for (uint i = 0; i < 4; i++)
        {
            if (memory.TryReadByte(0x00CC0960 + i, out var owner) && owner != 0xFF &&
                memory.TryReadUInt16(0x00CFF5E4 + i * 0x30, out var phase) &&
                memory.TryReadUInt16(0x00CFF5E6 + i * 0x30, out var flags))
                windows.Add($"w{i}/owner={owner:X2}/phase={phase}/flags={flags:X4}");
        }
        var diagnostic = $"confirmInput={(inputReadable ? $"{input:X8}" : "unreadable")}, " +
            $"held={Word(0x00CC0E00)}, previous={Word(0x00CC0E04)}, rawConfirm={Word(0x00D011C0)}, " +
            $"entity={(memory.TryReadByte(0x00CC0964, out var entity) ? $"{entity:X2}" : "unreadable")}, " +
            $"cloudPc={(memory.TryReadUInt16(0x00CC0CF8 + 19 * 2, out var pc) ? $"{pc:X4}" : "unreadable")}, " +
            $"navMenu={menuOpen}, navSuppressing={suppressing}, windows=[{string.Join(';', windows)}]";
        if (diagnostic == lastReturnDiagnostic && now < nextReturnHeartbeat) return null;
        lastReturnDiagnostic = diagnostic;
        nextReturnHeartbeat = now.AddSeconds(1);
        return $"{diagnostic}, messageSequence={messageSequence}";
    }

    private string Word(uint address) =>
        memory.TryReadUInt32(address, out var value) ? $"{value:X8}" : "unreadable";
}
