using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class SubmarineCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 16, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        List<Exception> failures = [];
        foreach (var test in new Action[]
        {
            EveryUnavailableBoundaryReleasesSubmarineInput,
            TheNativePollRejectsAPauseOrFieldTransitionBeforeTheWorkerSeesIt,
            APhysicalPausePressBlocksSteeringOnTheSameNativePoll,
            ManualSteeringStopsPursuit,
            TheControllerMenuStartsTheSameAutomaticPursuit,
            AHeldPursuitKeyThroughAnUnreadableFrameDoesNotBecomeANewPress,
            UnreadableFramesCannotKeepALastSeenContactAliveForever,
            AReaderFaultReleasesInputBeforeItEscapes,
            ManualPlayStillAnnouncesNativeLocks,
            FiringGuidanceDoesNotDiscardHullDamage,
            APreviousOwnedKeyCanDrainBeforeItCountsAsManualInput,
            BrowsingKeepsTheLockBaselineWhileReleasingSteering,
            APauseBetweenCaptureAndDeliverySuspendsPursuit,
            SelectingOnHullContactStillAnnouncesTheStoppedPursuit,
            ReturningToTheGameKeepsTheFiringGuidance
        })
        {
            try { test(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
    }

    private static void ManualPlayStillAnnouncesNativeLocks()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.Observe(10, true, true, Now, _ => false, null, out _);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords + 0x38, 0x800);
        var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30), _ => false, null, out _);
        Check(cue.Speech?.Contains("locked", StringComparison.OrdinalIgnoreCase) == true && cue.PlayLockCue,
            "manual play retains the native lock speech and protected sound");
    }

    private static void BrowsingKeepsTheLockBaselineWhileReleasingSteering()
    {
        using var fixture = new Fixture();
        fixture.Start();
        var capture = new ControllerNavigationCapture(
            suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor), () => true);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords + 0x38, 0x800);
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(20), _ => false, capture, out _);
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now.AddMilliseconds(21));
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.RightThumb), Now.AddMilliseconds(22));
        var firePrompts = 0;
        var lockCues = 0;
        for (var tick = 1; tick <= 10; tick++)
        {
            var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30 + tick * 30),
                _ => false, capture, out _);
            if (cue.Speech?.Contains("Press Switch", StringComparison.OrdinalIgnoreCase) == true) firePrompts++;
            if (cue.PlayLockCue) lockCues++;
            Check(fixture.Sink.Held.Count == 0, "an open list releases steering on every tick");
        }
        Check(capture.IsOpen && fixture.Coordinator.IsPursuing, "browsing preserves the pursuit intent");
        Check(firePrompts <= 1 && lockCues <= 1,
            $"browsing preserves the lock baseline: {firePrompts} prompts and {lockCues} cues in 300 ms");
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now.AddMilliseconds(350));
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.B), Now.AddMilliseconds(360));
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(370), _ => false, capture, out _);
        Check(!fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0, "B still stops from the list");
    }

    private static void APauseBetweenCaptureAndDeliverySuspendsPursuit()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords, -40 << 12);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords + 4, -20 << 12);
        // The snapshot is active. Pause arrives only when a changed plan sends its
        // new key down, exactly where the real x64 sink checks its delivery gate.
        fixture.Sink.BeforeKeyDown = () => fixture.Memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 1);
        fixture.Sink.AcceptKeyDown = () => fixture.Coordinator.MayDeliverInputNow;
        var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30), _ => false, null, out _);
        Check(fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0,
            "a gate closed between capture and delivery suspends instead of cancelling pursuit");
        Check(cue.Speech?.Contains("Pursuit stopped", StringComparison.OrdinalIgnoreCase) != true,
            "a pause race does not announce an input failure");
        fixture.Sink.BeforeKeyDown = null;
        fixture.Memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 0);
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(60), _ => false, null, out _);
        Check(fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count > 0,
            "the changed turn resumes after the pause");
    }

    private static void SelectingOnHullContactStillAnnouncesTheStoppedPursuit()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.Put(SubmarineMissionStateReader.AddressWarnings, 2);
        var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30),
            key => key == 0x4C, null, out _);
        Check(!fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0,
            "hull contact stops pursuit even with a selection command");
        Check(cue.Speech?.Contains("Pursuit stopped", StringComparison.OrdinalIgnoreCase) == true &&
            cue.Speech.Contains("1 of 1"), "the stopped pursuit and new selection are both announced");
    }

    private static void ReturningToTheGameKeepsTheFiringGuidance()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Coordinator.Observe(10, false, true, Now.AddMilliseconds(30), _ => false, null, out _);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords + 0x38, 0x800);
        var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(60), _ => false, null, out _);
        Check(cue.Speech?.Contains("Press Switch", StringComparison.OrdinalIgnoreCase) == true &&
            cue.Speech.Contains("J and L"), "the introduction retains the selected-target fire prompt");
    }

    private static void APhysicalPausePressBlocksSteeringOnTheSameNativePoll()
    {
        using var fixture = new Fixture();
        fixture.Memory.Put(HighwayDirectionInputMappingResolver.MappingTableAddress + 11 * 4, 0x1C);
        fixture.Start();
        fixture.Memory.Put(0x009ADAE4 + 0x1C, 0x80);
        Check(!fixture.Coordinator.MayDeliverInputNow,
            "the original keyboard buffer's Pause press blocks pursuit before the game sets its pause flag");
        fixture.Memory.Put(0x009ADAE4 + 0x1C, 0);
        Check(fixture.Coordinator.MayDeliverInputNow, "an active mission may deliver after Pause is released");
    }

    private static void FiringGuidanceDoesNotDiscardHullDamage()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords + 0x38, 0x800);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressHealth, 13108);
        var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30), _ => false, null, out _);
        Check(cue.Speech?.Contains("Press Switch") == true && cue.Speech.Contains("Hull 80 percent"),
            "a fire prompt and hull damage arriving together are both spoken");
    }

    private static void APreviousOwnedKeyCanDrainBeforeItCountsAsManualInput()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.Put(0x009873D8, 15);
        fixture.Memory.Put(0x009A85D4, 0x10);
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30), _ => false, null, out _);
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(45), _ => false, null, out _);
        Check(fixture.Coordinator.IsPursuing,
            "a just-released throttle in the previous game poll does not cancel pursuit");
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(150), _ => false, null, out _);
        Check(!fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0,
            "a previously owned key held beyond the drain window still stops pursuit");
    }

    private static void AHeldPursuitKeyThroughAnUnreadableFrameDoesNotBecomeANewPress()
    {
        using var fixture = new Fixture();
        var edges = new NavigationKeyPressTracker();
        var pursuitKeyDown = false;
        bool Pressed(int key) => edges.Observe(key, key == 0x50 && pursuitKeyDown, true);
        fixture.Coordinator.Observe(10, true, true, Now, Pressed, null, out _);
        fixture.Memory.AllowRead = false;
        pursuitKeyDown = true;
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(10), Pressed, null, out _);
        fixture.Memory.AllowRead = true;
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(20), Pressed, null, out _);
        Check(!fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0,
            "a key held through failed reads cannot start movement on recovery");
    }

    private static void UnreadableFramesCannotKeepALastSeenContactAliveForever()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.Put(SubmarineMissionStateReader.AddressEnemyRecords + 0x38, 0);
        for (var second = 1; second <= 25; second++)
        {
            fixture.Memory.AllowRead = false;
            fixture.Coordinator.Observe(10, true, true, Now.AddSeconds(second).AddMilliseconds(-30),
                _ => false, null, out _);
            fixture.Memory.AllowRead = true;
            fixture.Memory.Put(SubmarineMissionStateReader.AddressRemainingFrames, (300 - second) * 60);
            fixture.Coordinator.Observe(10, true, true, Now.AddSeconds(second), _ => false, null, out _);
        }
        Check(!fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0,
            "alternating unavailable and coherent readings cannot extend a last-seen sighting forever");
    }

    private static void EveryUnavailableBoundaryReleasesSubmarineInput()
    {
        foreach (var boundary in new[] { "pause", "quit prompt", "result", "module", "read", "view", "focus", "disabled", "dispose" })
        {
            using var fixture = new Fixture();
            fixture.Start();
            var module = 10;
            var foreground = true;
            var enabled = true;
            switch (boundary)
            {
                case "pause": fixture.Memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 1); break;
                case "quit prompt": fixture.Memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 5); break;
                case "result": fixture.Memory.Put(SubmarineMissionStateReader.AddressResult, 2); break;
                case "module": module = 1; fixture.Memory.AllowRead = false; break;
                case "read": fixture.Memory.AllowRead = false; break;
                case "view": fixture.Memory.Put(SubmarineMissionStateReader.AddressProjectionContextPointer, 0); break;
                case "focus": foreground = false; break;
                case "disabled": enabled = false; break;
                case "dispose": fixture.Coordinator.Dispose(); break;
            }
            if (boundary != "dispose") fixture.Coordinator.Observe(module, foreground, enabled,
                Now.AddMilliseconds(30), _ => false, null, out _);
            Check(!fixture.Coordinator.HasOwnedInput && fixture.Sink.Held.Count == 0,
                boundary + " releases every owned direction and propulsion key");
        }
    }

    private static void TheNativePollRejectsAPauseOrFieldTransitionBeforeTheWorkerSeesIt()
    {
        using var fixture = new Fixture();
        fixture.Start();
        Check(fixture.Coordinator.MayDeliverInputNow, "a coherent active pursuit may deliver input");
        fixture.Memory.Put(SubmarineMissionStateReader.AddressInnerCompletion, 1);
        Check(fixture.Coordinator.MayDeliverInputNow,
            "the ordinary end-of-frame flag is not mission completion and does not reject a new command");
        fixture.Memory.Put(SubmarineMissionStateReader.AddressInnerCompletion, 0);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 5);
        Check(!fixture.Coordinator.MayDeliverInputNow, "the native poll blocks Left from the quit prompt before the worker ticks");
        fixture.Memory.Put(SubmarineMissionStateReader.AddressSessionFlags, 0);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressCurrentModule, 1);
        Check(!fixture.Coordinator.MayDeliverInputNow, "pursuit input cannot reach the return dialogue");
        fixture.Memory.Put(SubmarineMissionStateReader.AddressCurrentModule, 10);
        fixture.Memory.Put(SubmarineMissionStateReader.AddressResult, 1);
        Check(!fixture.Coordinator.MayDeliverInputNow, "result banners block pursuit at the poll");
    }

    private static void ManualSteeringStopsPursuit()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.Put(0x009A85D4, 0x8000);
        var cue = fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30),
            _ => false, null, out _);
        Check(!fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count == 0,
            "a manual direction outside the owned right turn stops the assist");
        Check(cue.Speech?.Contains("manual steering") == true, "manual takeover is announced");
    }

    private static void TheControllerMenuStartsTheSameAutomaticPursuit()
    {
        using var fixture = new Fixture();
        var capture = new ControllerNavigationCapture(
            suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor), () => true);
        fixture.Coordinator.Observe(10, true, true, Now, _ => false, capture, out _);
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.RightThumb), Now);
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(10), _ => false, capture, out _);
        Check(fixture.Sink.Held.Count == 0, "browsing does not issue movement");
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now.AddMilliseconds(12));
        capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.X), Now.AddMilliseconds(14));
        fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(16), _ => false, capture, out _);
        Check(fixture.Coordinator.IsPursuing && fixture.Sink.Held.Count > 0,
            "X from the list starts actual steering through the shared controller");
        Check(!fixture.Sink.Held.Any(key => key.ScanCode == 0x39), "the assist never fires");
    }

    private static void AReaderFaultReleasesInputBeforeItEscapes()
    {
        using var fixture = new Fixture();
        fixture.Start();
        fixture.Memory.ThrowRead = true;
        try { fixture.Coordinator.Observe(10, true, true, Now.AddMilliseconds(30), _ => false, null, out _); }
        catch (InvalidOperationException) { }
        Check(fixture.Sink.Held.Count == 0, "a reader or tracker fault cannot leave keys held");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MissionMemory Memory = new();
        internal readonly Sink Sink = new();
        internal readonly SubmarineAccessibilityCoordinator Coordinator;
        internal Fixture() => Coordinator = new(Memory,
            HighwayAutoSteeringController.CreateCurrentProcess(Memory, Sink));
        internal void Start()
        {
            Coordinator.Observe(10, true, true, Now, _ => false, null, out _);
            var cue = Coordinator.Observe(10, true, true, Now.AddMilliseconds(10), key => key == 0x50, null, out _);
            Check(Coordinator.IsPursuing && Sink.Held.Count > 0,
                "the real reader, tracker and input controller start pursuit: " + cue.Speech);
        }
        public void Dispose() => Coordinator.Dispose();
    }

    private sealed class Sink : IHighwayKeyboardInputSink
    {
        internal readonly HashSet<HighwayKeyboardKey> Held = [];
        internal Action? BeforeKeyDown;
        internal Func<bool>? AcceptKeyDown;
        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            var accepted = 0;
            foreach (var transition in transitions)
            {
                if (transition.IsKeyDown)
                {
                    BeforeKeyDown?.Invoke();
                    if (AcceptKeyDown?.Invoke() == false) continue;
                }
                var key = new HighwayKeyboardKey(transition.ScanCode, transition.IsExtended);
                if (transition.IsKeyDown) Held.Add(key); else Held.Remove(key);
                accepted++;
            }
            return new(accepted, 0);
        }
    }

    private sealed class MissionMemory : ILegacyAddressSpace
    {
        private const uint Context = 0x04000000;
        private readonly Dictionary<uint, byte> bytes = [];
        internal bool AllowRead = true;
        internal bool ThrowRead;
        internal MissionMemory()
        {
            Put(SubmarineMissionStateReader.AddressCurrentModule, 10);
            Put(SubmarineMissionStateReader.AddressActiveRun, 1);
            Put(SubmarineMissionStateReader.AddressRemainingFrames, 300 * 60);
            Put(SubmarineMissionStateReader.AddressHealth, 16384);
            Put(SubmarineMissionStateReader.AddressSpeedDenominator, 12288);
            Put(SubmarineMissionStateReader.AddressTorpedoSlots, 0x20000);
            Put(0x009873D8, 9);
            Put(SubmarineMissionStateReader.AddressProjectionContextPointer, (int)Context);
            Put(Context + 0x850, 320); Put(Context + 0x854, 240);
            for (uint i = 0; i < 9; i++) Put16(SubmarineMissionStateReader.AddressCamera + i * 2, i % 4 == 0 ? (short)4096 : (short)0);
            var projection = new float[16];
            projection[0] = 1; projection[3] = 160; projection[5] = 1; projection[7] = 120;
            projection[10] = 1; projection[15] = 1;
            for (uint i = 0; i < 16; i++) Put(Context + 0x8D0 + i * 4, BitConverter.SingleToInt32Bits(projection[i]));
            var record = SubmarineMissionStateReader.AddressEnemyRecords;
            Put(record, 40 << 12); Put(record + 4, 20 << 12); Put(record + 8, 200 << 12);
            Put(record + 0x34, 0x15); Put(record + 0x38, 0x400); Put(record + 0x54, 0x00989D18);
            foreach (var item in new[] { (4, 0x1E), (6, 0x1F), (7, 0x39), (12, 0x48), (13, 0x4D), (14, 0x50), (15, 0x4B) })
                Put(HighwayDirectionInputMappingResolver.MappingTableAddress + (uint)item.Item1 * 4, item.Item2);
        }
        internal void Put(uint address, int value)
        {
            for (uint i = 0; i < 4; i++) bytes[address + i] = (byte)(value >> (int)(i * 8));
        }
        private void Put16(uint address, short value)
        {
            bytes[address] = (byte)value; bytes[address + 1] = (byte)(value >> 8);
        }
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (ThrowRead) throw new InvalidOperationException("simulated native read fault");
            if (!AllowRead) return false;
            for (var i = 0; i < destination.Length; i++) destination[i] = bytes.GetValueOrDefault(address + (uint)i);
            return true;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Submarine coordinator: " + message);
    }
}
