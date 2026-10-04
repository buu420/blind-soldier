using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026SubmarineMissionHostTests
{
    internal static void Run()
    {
        var memory = new MissionMemory();
        var host = new Steam2026SubmarineMissionHost(memory);
        var now = DateTime.UtcNow;
        var repeatPolls = 0;
        bool Repeat() { repeatPolls++; return true; }
        var cue = host.Observe(10, true, true, now, () => false, out _);
        Check(cue.Speech?.StartsWith("Submarine mission.") == true,
            "x64 must speak the shared instruments when the mission starts");
        cue = host.Observe(10, true, true, now.AddMilliseconds(30), Repeat, out var interrupt);
        Check(cue.Speech?.Contains("Hull 100 percent") == true && interrupt,
            "K repeats the native instruments immediately");
        memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 1);
        cue = host.Observe(10, true, true, now.AddSeconds(1), () => false, out _);
        Check(cue.Speech == "Paused.", "native pause is spoken");
        memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 0);
        memory.Put(SubmarineMissionStateReader.AddressInnerCompletion, 1);
        memory.Put(SubmarineMissionStateReader.AddressResult, 2);
        cue = host.Observe(10, true, true, now.AddSeconds(2), () => false, out _);
        Check(cue.Speech == "The submarine has been destroyed.", "native result is spoken");
        memory.AllowRead = false;
        cue = host.Observe(1, true, true, now.AddSeconds(3), Repeat, out _);
        Check(cue.Speech is null && repeatPolls == 1,
            "return to field releases K immediately even when submarine memory is gone");
        memory.AllowRead = true;
        memory.Put(SubmarineMissionStateReader.AddressInnerCompletion, 0);
        memory.Put(SubmarineMissionStateReader.AddressResult, 0);
        cue = host.Observe(10, false, true, now.AddSeconds(4), Repeat, out _);
        Check(cue.Speech is null && repeatPolls == 1, "background mission remains silent");
        cue = host.Observe(10, true, true, now.AddSeconds(5), () => false, out _);
        Check(cue.Speech?.StartsWith("Submarine mission.") == true, "new run has fresh state");
        memory.AllowRead = false;
        cue = host.Observe(10, true, true, now.AddSeconds(6), Repeat, out _);
        Check(cue.Speech is null && repeatPolls == 1, "unreadable instruments are never invented");
        ReturnTraceDistinguishesOwnerPhaseAndConfirm();
    }

    private static void ReturnTraceDistinguishesOwnerPhaseAndConfirm()
    {
        var memory = new MissionMemory();
        var host = new Steam2026SubmarineMissionHost(memory);
        var now = new DateTime(2026, 10, 4, 6, 0, 0, DateTimeKind.Utc);
        Check(host.ObserveReturn(1, now, false, false) is null,
            "ordinary field play does not generate return diagnostics");
        memory.Put(0x00CBF9D8, 0x01000000);
        memory.Put(0x01000080, 0);
        memory.Put(0x00CC0960, unchecked((int)0xFFFFFF13));
        memory.Put(0x00CFF5E4, 6);
        Check(host.ObserveReturn(10, now, false, false) is null, "mission itself has no return trace");
        Check(host.ObserveReturn(0, now.AddMilliseconds(500), false, false) is null,
            "an intermediate loading module arms the trace without reading field state");
        var trace = host.ObserveReturn(1, now.AddSeconds(1), false, false);
        Check(trace?.Contains("owner=13/phase=6/flags=0000") == true,
            "Cloud's owner index is distinct from the native waiting phase");
        Check(host.ObserveReturn(1, now.AddMilliseconds(1030), false, false) is null,
            "unchanged state is not logged every frame");
        memory.Put(0x00CC0E00, 0x20);
        memory.Put(0x00D011C0, 1);
        trace = host.ObserveReturn(1, now.AddSeconds(2), false, false, 42);
        Check(trace?.Contains("held=00000020") == true && trace.Contains("rawConfirm=00000001") &&
            trace.Contains("messageSequence=42"), "held input and script activity survive missed input-edge samples");
        memory.Put(0x01000080, 0x20);
        Check(host.ObserveReturn(1, now.AddSeconds(3), false, false)?.Contains("confirmInput=00000020") == true,
            "a native Confirm edge is recorded");
        memory.Put(0x01000080, 0);
        Check(host.ObserveReturn(1, now.AddSeconds(62), false, false) is null,
            "the diagnostic window ends after sixty seconds");
    }

    private sealed class MissionMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        internal bool AllowRead = true;
        internal MissionMemory()
        {
            Put(SubmarineMissionStateReader.AddressCurrentModule, 10);
            Put(SubmarineMissionStateReader.AddressActiveRun, 1);
            Put(SubmarineMissionStateReader.AddressRemainingFrames, 300 * 60);
            Put(SubmarineMissionStateReader.AddressHealth, 16384);
            Put(SubmarineMissionStateReader.AddressSpeedDenominator, 12288);
        }
        internal void Put(uint address, int value)
        {
            for (var i = 0; i < 4; i++) bytes[address + (uint)i] = (byte)(value >> (i * 8));
        }
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (!AllowRead) return false;
            for (var i = 0; i < destination.Length; i++)
                destination[i] = bytes.GetValueOrDefault(address + (uint)i);
            return true;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Submarine host: " + message);
    }
}
