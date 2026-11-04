using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AncientForestWarningDeliveryTests
{
    internal static void Run()
    {
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var bank = new byte[128]; bank[16] = 1;
        var onPlant = new FieldActivityObservation(622, 193, -114, 269, 14, true, 1620, new(0), true,
            [], new Dictionary<int, FieldActivityWaitState>(), null, null, -1)
        { ReadTemporaryByte = i => bank[i], AncientForestShakingPlants = new HashSet<int>() };
        var spoken = new List<string>();
        delivery.NoteOtherSpeech("Release insects and frogs by pressing Confirm. Insects and frogs react differently depending on where you release them, so try many different places.", now);
        void Deliver(FieldActivityCue cue, DateTime at) => FieldActivityClockHost.Deliver(cue, readout, delivery,
            at, () => true, () => true, s => { spoken.Add(s); return true; }, () => true);
        Deliver(readout.Observe(onPlant, now), now);
        var shaking = onPlant with { AncientForestShakingPlants = new HashSet<int> { 1 } };
        Deliver(readout.Observe(shaking, now.AddMilliseconds(100)), now.AddMilliseconds(100));
        if (spoken.Count != 1 || !spoken[0].Contains("shaking", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The player's shaking plant warning must reach speech during a longer protected instruction.");
        if (!readout.CurrentReplaceableLineIsUrgent)
            throw new InvalidOperationException("The current warning should keep priority for a refused speaker.");
        readout.Observe(shaking with { PlayerTriangle = 32 }, now.AddMilliseconds(200));
        if (readout.CurrentReplaceableLineIsUrgent)
            throw new InvalidOperationException("Leaving the shaking plant must remove the interrupt priority.");
        readout.Reset();
        if (readout.CurrentReplaceableLineIsUrgent)
            throw new InvalidOperationException("Reset retained forest warning priority.");
        readout.Observe(shaking with { PlayerTriangle = 32 }, now.AddSeconds(1));
        var steppedOn = readout.Observe(shaking, now.AddSeconds(1.1));
        if (steppedOn.Speech?.Contains("shaking", StringComparison.OrdinalIgnoreCase) != true || !readout.CurrentReplaceableLineIsUrgent)
            throw new InvalidOperationException("Stepping onto an already shaking plant needs a current warning.");
        var echoPlant = onPlant with { FieldId = 620, PlayerX = -489, PlayerY = -155, PlayerZ = 410,
            PlayerTriangle = 8, ReadTemporaryByte = _ => 0, AncientForestShakingPlants = new HashSet<int> { 1 } };
        readout.Reset();
        readout.Observe(echoPlant, now.AddSeconds(2));
        if (!readout.CurrentReplaceableLineIsUrgent)
            throw new InvalidOperationException("The visible Echo-S shake needs priority even after its closed flag clears.");
        var unknownMotion = echoPlant with { AncientForestShakingPlants = null };
        if (readout.Describe(unknownMotion)?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("A cleared Echo-S flag with unreadable motion cannot prove the plant open.");
        readout.Observe(unknownMotion, now.AddSeconds(2.1));
        if (readout.CurrentReplaceableLineIsUrgent)
            throw new InvalidOperationException("An unreadable plant pose cannot retain an urgent current warning.");
        Console.WriteLine("Ancient Forest standing-plant warning delivery tests passed.");
    }
}
