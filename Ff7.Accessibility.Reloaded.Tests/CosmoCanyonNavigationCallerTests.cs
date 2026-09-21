using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The held-destination behaviour at the boundaries that actually drive it, not at the
/// controller in isolation.
///
/// <para>A destination held for a shut native door is pending intent, not a route.
/// <c>BeaconEnabled</c> is false the whole time it waits, and every caller that asks "is
/// navigation engaged" by testing that flag alone gets the wrong answer: B stops cancelling
/// it, A and X cancel it by accident instead of restating it, and an auto-walk request
/// degrades into spoken guidance. These cases pin the three places that decide.</para>
/// </summary>
internal static class CosmoCanyonNavigationCallerTests
{
    private const int Observatory = 541;

    private static readonly FieldPositionSnapshot Stall =
        new(FieldPositionReader.FieldModule, Observatory, 0, -72, 103, -38, 38, 40);

    private static readonly FieldNavigationTarget WayBackDown = new(
        Observatory, FieldNavigationCategory.Story, "Go back down from the observatory",
        -378, 30, -36, StableId: "cosmo:541:back-down");

    private static readonly FieldNavigationTarget Elsewhere = new(
        Observatory, FieldNavigationCategory.Story, "Talk to Bugenhagen in the observatory",
        -107, 54, -28, StableId: "cosmo:541:bugenhagen");

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        SpeechOnlySelectionNeverStartsAWalk(createWalkmeshReader);
        AWalkRequestSurvivesTheWaitAndStartsWhenTheDoorOpens(createWalkmeshReader);
        BCancelsAHeldDestination(createWalkmeshReader);
        ARestatesAHeldDestinationInsteadOfCancellingIt(createWalkmeshReader);
        ChangingTheSelectionDropsTheHold(createWalkmeshReader);
        LeavingTheFieldDropsTheHold(createWalkmeshReader);
        TheScanNeverQueuesAToggleThatWouldCancelItsOwnHold();
        NoInputIsProducedWhileWaiting(createWalkmeshReader);
    }

    /// <summary>The keyboard's own P binding, and the controller's X: intent must survive.</summary>
    private static void AWalkRequestSurvivesTheWaitAndStartsWhenTheDoorOpens(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader);

        // X on the pad is StartAutoWalk. The door is shut, so nothing may move.
        harness.Services.StartAutoWalk();
        Equal(true, harness.Navigation.IsHoldingForNativeBoundary, "X at a shut door must hold the destination");
        Equal(false, harness.Navigation.BeaconEnabled, "a hold is not a running route");
        Equal(0, harness.AutoWalkStarts, "nothing may be walked while the way is shut");
        Equal(false, harness.Navigation.TryConsumeHeldAutoWalkRequest(),
            "the request is not available until the route actually starts");

        harness.OpenTheDoor();
        harness.Tick();
        Equal(true, harness.Navigation.BeaconEnabled, "the held route starts when the door opens");
        Equal(1, harness.AutoWalkStarts, "and the walk the player asked for starts with it");
        Equal(false, harness.Navigation.TryConsumeHeldAutoWalkRequest(), "the request is taken once");
    }

    /// <summary>A on the pad is guidance only. It must never become a walk.</summary>
    private static void SpeechOnlySelectionNeverStartsAWalk(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader);
        harness.Services.StartNavigation();
        Equal(true, harness.Navigation.IsHoldingForNativeBoundary, "A at a shut door must hold the destination");

        harness.OpenTheDoor();
        harness.Tick();
        Equal(true, harness.Navigation.BeaconEnabled, "the held route starts when the door opens");
        Equal(0, harness.AutoWalkStarts, "spoken guidance must not turn into a walk");
    }

    private static void BCancelsAHeldDestination(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader);
        harness.Services.StartAutoWalk();
        Equal(true, harness.Services.RouteIsActive, "a held destination is an engaged selection");

        var stop = harness.Services.Stop();
        Contains("Navigation off", stop, "B must answer");
        Equal(false, harness.Navigation.IsHoldingForNativeBoundary, "B must cancel the hold");
        Equal(false, harness.Services.RouteIsActive, "nothing is engaged after B");

        harness.OpenTheDoor();
        harness.Tick();
        Equal(false, harness.Navigation.BeaconEnabled, "a cancelled destination must not start later");
        Equal(0, harness.AutoWalkStarts, "and must not walk");

        // The keyboard's toggle is the same cancel.
        var second = new Harness(createWalkmeshReader);
        second.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, Stall, second.Transform);
        Equal(true, second.Navigation.IsHoldingForNativeBoundary, "the keyboard toggle holds too");
        var off = second.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, Stall, second.Transform);
        Contains("Navigation off", off?.Speech, "a repeated toggle cancels the hold");
        Equal(false, second.Navigation.IsHoldingForNativeBoundary, "and leaves nothing held");
        second.OpenTheDoor();
        second.Tick();
        Equal(false, second.Navigation.BeaconEnabled, "a cancelled hold never activates");
    }

    /// <summary>
    /// Pressing A or X again on a held destination means "go here", the same as it does on a
    /// running route. It must restate the hold, not silently cancel it.
    /// </summary>
    private static void ARestatesAHeldDestinationInsteadOfCancellingIt(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader);
        harness.Services.StartNavigation();
        var again = harness.Services.StartNavigation();
        Contains("shut just now", again, "restating a held destination must explain the wait again");
        Equal(true, harness.Navigation.IsHoldingForNativeBoundary, "and must leave it held");

        harness.OpenTheDoor();
        harness.Tick();
        Equal(true, harness.Navigation.BeaconEnabled, "the restated hold still starts");
    }

    private static void ChangingTheSelectionDropsTheHold(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader, [WayBackDown, Elsewhere]);
        harness.Select(WayBackDown.Label);
        harness.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, Stall, harness.Transform);
        Equal(WayBackDown.Label, harness.Navigation.HeldForNativeBoundaryLabel, "the shut door is held");

        harness.Navigation.HandleAction(FieldNavigationAction.NextTarget, Stall, harness.Transform);
        Equal(false, harness.Navigation.IsHoldingForNativeBoundary,
            "a destination held for a shut door belongs to the selection the player left");

        harness.OpenTheDoor();
        harness.Tick();
        Equal(false, harness.Navigation.BeaconEnabled,
            "the old held target must not activate behind the new selection");
    }

    private static void LeavingTheFieldDropsTheHold(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader);
        harness.Services.StartAutoWalk();
        Equal(true, harness.Navigation.IsHoldingForNativeBoundary, "held before leaving");

        var elsewhere = Stall with { FieldId = 544 };
        harness.Navigation.UpdateLiveTracking(elsewhere, default, harness.Transform, isSuppressed: false);
        Equal(false, harness.Navigation.IsHoldingForNativeBoundary, "leaving the screen drops the hold");
        Equal(0, harness.AutoWalkStarts, "and walks nothing");
    }

    /// <summary>
    /// The x64 scan queued a ToggleBeacon every pass while an auto-walk request was pending
    /// and the beacon was off. While a destination is held both are true, and a queued
    /// ToggleBeacon is exactly how the player cancels a hold - so the wait cancelled itself.
    /// </summary>
    private static void TheScanNeverQueuesAToggleThatWouldCancelItsOwnHold()
    {
        Equal(true, FieldNavigationAutoWalkIntent.ShouldQueueAutoWalkToggle(
                pendingAutoWalkStart: true, beaconEnabled: false,
                holdingForNativeBoundary: false, alreadyQueued: false),
            "an ordinary pending walk still queues its toggle");
        Equal(false, FieldNavigationAutoWalkIntent.ShouldQueueAutoWalkToggle(
                pendingAutoWalkStart: true, beaconEnabled: false,
                holdingForNativeBoundary: true, alreadyQueued: false),
            "a held destination must never have a toggle queued against it");
        Equal(false, FieldNavigationAutoWalkIntent.ShouldQueueAutoWalkToggle(
                pendingAutoWalkStart: true, beaconEnabled: true,
                holdingForNativeBoundary: false, alreadyQueued: false),
            "a running route needs no toggle");
        Equal(false, FieldNavigationAutoWalkIntent.ShouldQueueAutoWalkToggle(
                pendingAutoWalkStart: true, beaconEnabled: false,
                holdingForNativeBoundary: false, alreadyQueued: true),
            "and one queued toggle is enough");
    }

    private static void NoInputIsProducedWhileWaiting(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var harness = new Harness(createWalkmeshReader);
        harness.Services.StartAutoWalk();
        for (var sample = 0; sample < 4; sample++)
        {
            Equal(false,
                harness.Navigation.TryResolveAutomaticInput(Stall, harness.Transform, 0, out var input),
                "a held destination must produce no automatic input");
            Equal(FieldNavigationInput.None, input, "and no direction");
            harness.Tick();
        }
    }

    /// <summary>
    /// The production wiring: the same <see cref="ControllerNavigationServices"/> both
    /// Mod.cs and the x64 coordinator build, over a real 541 walkmesh with Bugenhagen's
    /// lock applied, plus the per-frame consumption both runtimes perform.
    /// </summary>
    private sealed class Harness
    {
        private readonly MutableBoundaryMemory boundary = new(Observatory, [1]);

        public Harness(
            Func<int, FieldWalkmeshReader> createWalkmeshReader,
            FieldNavigationTarget[]? targets = null)
        {
            var planner = new FieldWalkmeshRoutePlanner(
                createWalkmeshReader(Observatory),
                new FieldBoundaryStateReader(boundary.ReadInt32, boundary.ReadByte, (_, _) => true));
            Navigation = new FieldNavigationController(
                new FieldNavigationTargetSource(targets ?? [WayBackDown]),
                planner);
            while (Navigation.CurrentCategory != FieldNavigationCategory.Story)
                Navigation.HandleAction(FieldNavigationAction.NextCategory, Stall, Transform);

            Services = new ControllerNavigationServices(
                () => Navigation.BeaconEnabled,
                action => Navigation.HandleAction(action, Stall, Transform)?.Speech,
                () => autoWalkRunning,
                () =>
                {
                    autoWalkRunning = true;
                    AutoWalkStarts++;
                    return true;
                },
                () => autoWalkRunning = false,
                () => { },
                () => Navigation.IsHoldingForNativeBoundary,
                Navigation.RequestAutoWalkForHeldRoute);
        }

        private bool autoWalkRunning;

        public FieldNavigationController Navigation { get; }

        public ControllerNavigationServices Services { get; }

        public FieldNavigationControlTransform Transform { get; } = new(0);

        public int AutoWalkStarts { get; private set; }

        public void OpenTheDoor() => boundary.Clear();

        /// <summary>Moves the cursor onto a named target the way the player would.</summary>
        public void Select(string label)
        {
            for (var press = 0; press < 8; press++)
            {
                var speech = Navigation
                    .HandleAction(FieldNavigationAction.RepeatTarget, Stall, Transform)?.Speech;
                if (speech is not null && speech.Contains(label, StringComparison.Ordinal))
                {
                    return;
                }

                Navigation.HandleAction(FieldNavigationAction.NextTarget, Stall, Transform);
            }

            throw new InvalidOperationException($"selection never reached '{label}'");
        }

        /// <summary>One frame of the host loop, as both runtimes run it.</summary>
        public void Tick()
        {
            Navigation.UpdateLiveTracking(Stall, default, Transform, isSuppressed: false);
            if (Navigation.TryConsumeHeldAutoWalkRequest())
            {
                autoWalkRunning = true;
                AutoWalkStarts++;
            }
        }
    }

    internal sealed class MutableBoundaryMemory
    {
        private const int FieldState = 0x02800000;
        private readonly Dictionary<int, byte> bytes = [];

        public MutableBoundaryMemory(int field, IEnumerable<int> triangles)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            for (var index = 0; index < 4; index++)
                bytes[FieldBoundaryStateReader.AddressFieldGlobalObjectPtr + index] = (byte)(FieldState >> (index * 8));
            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] = (byte)(ReadByte(address) | (1 << (triangle & 7)));
            }
        }

        /// <summary>The script releasing the door, as it does at 01:28:57.</summary>
        public void Clear()
        {
            for (var offset = 0; offset < FieldBoundaryStateReader.BoundaryByteCount; offset++)
                bytes[FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + offset] = 0;
        }

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label}: expected '{expected}' in '{actual}'");
    }
}
