using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

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
        Check(cue.Speech!.Contains("Steam default controls: R1 or RB, or E, toggles overview") &&
            cue.Speech.Contains("Square or X, or Z, fires") && cue.Speech.Contains("O pursues"),
            "Steam help names evidenced defaults and the conflict-free pursuit hotkey");
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
        Check(cue.Speech is null && repeatPolls == 2,
            "background mission tracks held keys without speaking or acting on them");
        cue = host.Observe(10, true, true, now.AddSeconds(5), () => false, out _);
        Check(cue.Speech?.StartsWith("Submarine mission.") == true, "new run has fresh state");
        memory.AllowRead = false;
        cue = host.Observe(10, true, true, now.AddSeconds(6), Repeat, out _);
        Check(cue.Speech?.Contains("temporarily unavailable") == true && repeatPolls == 3,
            "K reports unavailable instruments without inventing a reading");
        ReturnTraceDistinguishesOwnerPhaseAndConfirm();
        TheActualNativeOverlayReturnsToFiringViewWithoutCyclingOrFiring();
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

    private static void TheActualNativeOverlayReturnsToFiringViewWithoutCyclingOrFiring()
    {
        var memory = new MissionMemory();
        var now = new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);
        memory.Put(SubmarineMissionStateReader.AddressViewMode, 1);
        memory.Put(SubmarineMissionStateReader.AddressTorpedoSlots, 0x20000);
        memory.Put(0x009873D8, 9);
        const uint context = 0x04000000;
        memory.Put(SubmarineMissionStateReader.AddressProjectionContextPointer, (int)context);
        memory.Put(context + 0x850, 320); memory.Put(context + 0x854, 240);
        for (uint i = 0; i < 9; i++) memory.Put(SubmarineMissionStateReader.AddressCamera + i * 2,
            i % 4 == 0 ? 4096 : 0);
        var projection = new float[16];
        projection[0] = 1; projection[3] = 160; projection[5] = 1; projection[7] = 120;
        projection[10] = 1; projection[15] = 1;
        for (uint i = 0; i < 16; i++) memory.Put(context + 0x8D0 + i * 4,
            BitConverter.SingleToInt32Bits(projection[i]));
        var record = SubmarineMissionStateReader.AddressEnemyRecords;
        memory.Put(record, 40 << 12); memory.Put(record + 4, 20 << 12); memory.Put(record + 8, 200 << 12);
        memory.Put(record + 0x34, 0x15); memory.Put(record + 0x38, 0x400); memory.Put(record + 0x54, 0x00989D18);
        foreach (var (slot, token) in new[] { (1, 0x4F), (4, 0x1E), (6, 0x1F), (7, 0x39), (11, 0x1C),
                     (12, 0x48), (13, 0x4D), (14, 0x50), (15, 0x4B) })
            memory.Put(HighwayDirectionInputMappingResolver.MappingTableAddress + (uint)slot * 4, token);

        SubmarineAccessibilityCoordinator? owner = null;
        var sink = new Steam2026NativeDirectionalInputSink(() => true, () => now,
            () => owner?.MayDeliverInputNow == true);
        using var coordinator = new SubmarineAccessibilityCoordinator(memory,
            HighwayAutoSteeringController.CreateCurrentProcess(memory, sink), sink.Renew);
        owner = coordinator;
        var manualFire = false;
        using var hook = Steam2026NativeDirectInputKeyboardHook.CreateForOverlayTest(
            sink, memory, memory, (_, length, destination) =>
            {
                for (uint i = 0; i < length; i += 4) memory.Put(destination + i, 0);
                if (manualFire) memory.Put(destination + 0x39, 0x80);
                return 0;
            }, () => now);
        var keyboard = Steam2026NativeDirectInputKeyboardHook.ExpectedGuestDestination;
        byte State(uint token)
        {
            Span<byte> value = stackalloc byte[1];
            Check(memory.TryRead(keyboard + token, value), "the game's keyboard buffer is readable");
            return value[0];
        }
        void Poll() => hook.InvokeForTest(0x4321,
            Steam2026NativeDirectInputKeyboardHook.KeyboardStateLength, keyboard);

        coordinator.Observe(10, true, true, now, _ => false, null, out _);
        now = now.AddMilliseconds(10);
        coordinator.Observe(10, true, true, now, key => key == 0x4F, null, out _);
        Poll();
        Check(State(0x4F) == 0x80 && State(0x39) == 0,
            "the actual native overlay delivers Target and does not synthesize Fire");
        memory.Put(SubmarineMissionStateReader.AddressViewMode, 0);
        now = now.AddMilliseconds(16);
        Poll();
        Check(State(0x4F) == 0 && State(0x39) == 0,
            "the next native poll retires Target before the worker sees normal view");
        memory.Put(record + 0x38, 0x800);
        var cue = coordinator.Observe(10, true, true, now, _ => false, null, out _);
        Check(cue.Speech?.Contains("Press Switch to fire") == true,
            "the real lock and loaded lamps now provide manual firing guidance");
        Check(cue.Speech!.Split("Normal view.", StringSplitOptions.None).Length == 2,
            "returning to normal view is announced once alongside firing guidance");
        manualFire = true;
        now = now.AddMilliseconds(16);
        Poll();
        Check(State(0x39) == 0x80 && State(0x4F) == 0,
            "manual Fire survives the overlay and normal view is never cycled again");
    }

    private sealed class MissionMemory : ILegacyAddressSpace, ILegacyMemoryWriter
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
        public bool TryWriteInt32(uint address, int value)
        {
            if (address == 0 || (address & 3) != 0) return false;
            Put(address, value);
            return true;
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
