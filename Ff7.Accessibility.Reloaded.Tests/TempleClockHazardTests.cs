using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>Regression for the clock hand that could knock a player off without any automatic speech.</summary>
internal static class TempleClockHazardTests
{
    private static readonly DateTime Epoch = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        EnteringAHandNamesItsKnockOffHazard();
        AnApproachingHandIsSaidWithoutRequestingRepeat();
        APassingHandIsNotCalledApproaching();
        SafePlatformsAndDisabledHazardsProduceNoWarnings();
        LosingTheSecondHandIsNotCalledSafe();
        ClockTimeReadFailureDoesNotHideAReadableHazard();
        RepeatRetainsTheHazardWhenAnotherHandCannotBeRead();
        RepeatDoesNotConsumeTheNextWarning();
        ACurrentWarningReachesTheSpeakerDuringOtherSpeech();
        ARefusedWarningCannotSurviveLeavingTheBridge();
        FocusAndMuteStillGateHazardDelivery();
        FastPollingDoesNotRepeatOnePose();
    }

    // Doorway six's hand spans native triangles 85, 48 and 49. X=0,Y=-350 is on its
    // outer part. Bearings 64 -> 48 -> 32 -> 4 -> 240 move clockwise past this hand,
    // as second's Main does by decreasing the rendered 0..255 facing value.
    private static FieldActivityObservation Look(int second, bool controlled = true, bool? active = true,
        int triangle = 48, int x = 0, int y = -350, FieldActivityReadStatus status = FieldActivityReadStatus.Visible) =>
        new(607, x, y, 0, triangle, controlled, 618, new FieldNavigationControlTransform(0), true,
            [Hand(21, 0), Hand(22, 170), Hand(23, second, status)],
            new Dictionary<int, FieldActivityWaitState>(), active is null ? null : _ => active.Value, _ => false, 0);

    private static FieldActivityModelReading Hand(int id, int bearing,
        FieldActivityReadStatus status = FieldActivityReadStatus.Visible) =>
        new(id, status, new FieldActivityModelState(true, status == FieldActivityReadStatus.Visible, 0, 0, 0, 131, bearing));

    private static void EnteringAHandNamesItsKnockOffHazard()
    {
        var cue = new FieldActivityReadout().Observe(Look(64), Epoch);
        Contains(cue.Speech, "second hand", "stepping onto a hand automatically names the second hand");
        Contains(cue.Speech, "knock", "stepping onto a hand explains the consequence");
    }

    private static void AnApproachingHandIsSaidWithoutRequestingRepeat()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(64), Epoch);
        readout.Observe(Look(48), Epoch.AddSeconds(4));
        var cue = readout.Observe(Look(32), Epoch.AddSeconds(8));
        Contains(cue.Speech, "approaching", "rendered movement toward the player's hand gives a warning");
        Contains(readout.Observe(Look(4), Epoch.AddSeconds(12)).Speech, "crossing", "the hand beside the player is a stronger warning");
    }

    private static void APassingHandIsNotCalledApproaching()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(32), Epoch);
        readout.Observe(Look(4), Epoch.AddSeconds(4));
        var cue = readout.Observe(Look(240), Epoch.AddSeconds(8));
        Contains(cue.Speech, "passed", "the observed clockwise sweep past the player is named");
        NotContains(cue.Speech, "approaching", "a receding hand does not invite a false warning");
    }

    private static void SafePlatformsAndDisabledHazardsProduceNoWarnings()
    {
        foreach (var look in new[]
        {
            Look(4, triangle: 46, y: -639), Look(4, triangle: 131, y: 0),
            Look(4, controlled: false), Look(4, active: false),
            Look(4, triangle: 110, y: -375), Look(4, status: FieldActivityReadStatus.Hidden)
        })
        {
            var readout = new FieldActivityReadout();
            readout.Observe(look, Epoch);
            NotContains(readout.Observe(look, Epoch.AddSeconds(1)).Speech, "second hand", "no active crossing hazard in this situation");
        }
    }

    private static void LosingTheSecondHandIsNotCalledSafe()
    {
        foreach (var look in new[] { Look(4, status: FieldActivityReadStatus.Unreadable), Look(4, active: null) })
        {
            var cue = new FieldActivityReadout().Observe(look, Epoch);
            Contains(cue.Speech, "cannot read", "a failed hazard read is reported while on a hand");
            NotContains(cue.Speech, "safe", "failed memory is not a clear path");
        }
    }

    private static void ClockTimeReadFailureDoesNotHideAReadableHazard()
    {
        var look = Look(4) with { Models = [FieldActivityModelReading.Unreadable(21), Hand(22, 170), Hand(23, 4)] };
        Contains(new FieldActivityReadout().Observe(look, Epoch).Speech, "second hand", "the readable hazard survives a different hand's failed read");
    }

    private static void RepeatRetainsTheHazardWhenAnotherHandCannotBeRead()
    {
        foreach (var failedHand in new[] { 21, 22 })
        {
            var look = Look(4) with
            {
                Models = [failedHand == 21 ? FieldActivityModelReading.Unreadable(21) : Hand(21, 0),
                    failedHand == 22 ? FieldActivityModelReading.Unreadable(22) : Hand(22, 170), Hand(23, 4)]
            };
            var readout = new FieldActivityReadout();
            Contains(readout.Describe(look), "cannot read the clock hands", "repeat reports the failed time read");
            Contains(readout.Describe(look), "second hand crossing", "repeat retains the readable crossing hazard");
            NotContains(readout.Describe(look with { IsLineEnabled = _ => false }), "second hand", "a disabled line is not a repeat hazard");
            Contains(readout.Describe(look with { IsLineEnabled = null }), "cannot read the second-hand hazard", "repeat distinguishes a failed hazard gate");
            Contains(readout.Observe(look, Epoch).Speech, "second hand crossing", "repeat did not consume the warning");
        }
    }

    private static void RepeatDoesNotConsumeTheNextWarning()
    {
        var plain = new FieldActivityReadout();
        var asked = new FieldActivityReadout();
        foreach (var (bearing, time) in new[] { (64, 0), (48, 4), (32, 8), (4, 12) })
        {
            var look = Look(bearing);
            asked.Describe(look);
            asked.Describe(look);
            Equal(plain.Observe(look, Epoch.AddSeconds(time)).Speech, asked.Observe(look, Epoch.AddSeconds(time)).Speech,
                "requesting details cannot silence an automatic warning");
        }
    }

    private static void ACurrentWarningReachesTheSpeakerDuringOtherSpeech()
    {
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        readout.Observe(Look(48), Epoch);
        delivery.NoteOtherSpeech("Long hand at six, short hand at ten. The bridge to doorway six is open.", Epoch.AddSeconds(1));
        var cue = readout.Observe(Look(32), Epoch.AddSeconds(1.1));
        var said = FieldActivityClockHost.Deliver(cue, readout, delivery, Epoch.AddSeconds(1.1), () => true, () => true, _ => true, () => true);
        Contains(said, "approaching", "a current approaching hazard interrupts a lengthy repeat");
    }

    private static void ARefusedWarningCannotSurviveLeavingTheBridge()
    {
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        var cue = readout.Observe(Look(4), Epoch);
        FieldActivityClockHost.Deliver(cue, readout, delivery, Epoch, () => true, () => true, _ => false, null);
        Equal(true, delivery.HasPending, "the refused current hazard is owed");
        var off = readout.Observe(Look(0, triangle: 46, y: -639), Epoch.AddSeconds(1));
        var said = FieldActivityClockHost.Deliver(off, readout, delivery, Epoch.AddSeconds(1), () => true, () => true, _ => true, null);
        NotContains(said, "second hand", "the speaker cannot deliver a stale warning on the platform");
    }

    private static void FocusAndMuteStillGateHazardDelivery()
    {
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        var cue = readout.Observe(Look(4), Epoch);
        var calls = 0;
        bool Speak(string _) { calls++; return true; }
        FieldActivityClockHost.Deliver(cue, readout, delivery, Epoch, () => true, () => false, Speak, null);
        FieldActivityClockHost.Deliver(cue, readout, delivery, Epoch.AddSeconds(1), () => false, () => true, Speak, null);
        Equal(0, calls, "urgency cannot bypass mute or foreground checks");
        Equal(false, FieldActivityClockHost.MayObserve(607, false, readout, delivery), "loss of focus resets both watches");
        Equal(false, delivery.HasPending, "no hazard remains queued after focus loss");
    }

    private static void FastPollingDoesNotRepeatOnePose()
    {
        var readout = new FieldActivityReadout();
        var lines = 0;
        for (var ms = 0; ms <= 5000; ms += 16)
        {
            if (readout.Observe(Look(32), Epoch.AddMilliseconds(ms)).Speech is { } text && text.Contains("second hand", StringComparison.OrdinalIgnoreCase)) lines++;
        }
        Equal(1, lines, "one unchanged visible hand position produces one announcement");
    }

    public static void RunWithInstalledGameData(Func<int, FieldWalkmeshReader> createReader)
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var scripts = new FieldScriptNavigationCatalog(root);
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 607, 0, 0, -350, 0, 48, 0);
        var meshRead = createReader(607).Read(position);
        Equal(true, meshRead.IsUsable, "installed clock walkmesh is usable");
        var mesh = meshRead.Walkmesh!;
        var bridgeTriangles = new HashSet<int>();
        // Native IDdr unlocks two ends; the triangle adjacent to both is the span.
        // Derive this from installed opcodes/edges rather than the readout's table.
        for (var slot = 3; slot <= 14; slot++)
        {
            var pair = scripts.ReadScriptOpcodes(607, 8, slot)
                .Where(op => op.Opcode == 0x6D && op.Bytes[3] == 0)
                .Select(op => (int)BitConverter.ToUInt16(op.Bytes.ToArray(), 1)).ToArray();
            Equal(2, pair.Length, "a native clock bridge has two unlocked ends");
            var inner = mesh.Triangles[pair[0]];
            var outer = mesh.Triangles[pair[1]];
            var shared = new[] { inner.Adjacent0, inner.Adjacent1, inner.Adjacent2 }
                .Intersect(new[] { outer.Adjacent0, outer.Adjacent1, outer.Adjacent2 }).Single();
            bridgeTriangles.UnionWith([pair[0], pair[1], shared]);
        }
        Equal(36, bridgeTriangles.Count, "twelve native three-triangle hand bridges");
        foreach (var triangle in mesh.Triangles)
        {
            var center = triangle.GetCentroid();
            var look = Look(128, triangle: triangle.Index, x: (int)center.X, y: (int)center.Y);
            var cue = new FieldActivityReadout().Observe(look, Epoch);
            Equal(bridgeTriangles.Contains(triangle.Index),
                cue.Speech?.Contains("second hand", StringComparison.OrdinalIgnoreCase) == true,
                $"native triangle {triangle.Index} gives a hazard announcement exactly on a hand");
        }
        var go = scripts.ReadScriptOpcodes(607, 18, 5);
        Equal(true, go.Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "0214C5"), "contact still runs native Cloud knockdown script");
        Equal(true, scripts.ReadScriptOpcodes(607, 1, 0).Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "0212C8"),
            "the later native return can disable this hazard");
    }

    private static void Contains(string? text, string fragment, string label) =>
        Equal(true, text?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true, $"{label}: {text ?? "silence"}");
    private static void NotContains(string? text, string fragment, string label) =>
        Equal(false, text?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true, $"{label}: {text ?? "silence"}");
    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
