using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// One capture serves the field and the world map, and exactly one of them holds the pad
/// at a time. A coordinator that is merely not the live one must not close the live one's
/// menu.
///
/// <para>The reported fault, on the world map near Fort Condor: the menu opened and read
/// out "Locations, Mythril Mine, Junon side", and then the bumpers did nothing to the
/// category while the native camera swung around instead. The field coordinator is
/// suspended on every worker frame it is not the live module, and its suspend asked the
/// shared capture to close unconditionally - so the world map's menu was shut a frame
/// after it opened, in silence, with suppression released, and the R1 the player pressed
/// next went to the game.</para>
/// </summary>
public static class ControllerNavigationDomainOwnershipTests
{
    private static readonly DateTime Now = new(2026, 9, 18, 10, 57, 47, DateTimeKind.Utc);

    public static void Run()
    {
        ASuspendedFieldDoesNotCloseTheWorldMapsMenu();
        ASuspendedFieldDoesNotDiscardTheWorldMapsQueuedSelection();
        TheOwningDomainStillClosesItsOwnMenu();
        TheGlobalCloseStillAppliesToWhoeverHoldsThePad();
    }

    /// <summary>
    /// The player's report, at the seam it happens at. The menu is open on the world map
    /// and the field is suspended; the next thing the player does is press a bumper.
    /// </summary>
    private static void ASuspendedFieldDoesNotCloseTheWorldMapsMenu()
    {
        var capture = OpenOnWorldMap();

        // Every worker frame the field is not the live module, this is what it does.
        capture.RequestClose(ControllerNavigationDomain.Field);

        // R1, to change category.
        var strip = capture.ObserveRawPoll(Pad(GamepadButton.RightShoulder), Now);

        Equal(true, capture.IsOpen,
            "the field being suspended does not close the world map's menu");
        Equal(
            GamepadButton.RightShoulder,
            strip & GamepadButton.RightShoulder,
            "and the bumper the player pressed is still kept from the game");
        Equal(true, capture.Commands.TryDequeue(out var command, out _),
            "and the bumper still produces a command");
        Equal(ControllerNavigationCommand.NextCategory, command,
            "which is the category change the player asked for");
    }

    /// <summary>
    /// The other half of the same call: a refused close must not take the queue with it.
    /// Those selections belong to the domain that is holding the pad.
    /// </summary>
    private static void ASuspendedFieldDoesNotDiscardTheWorldMapsQueuedSelection()
    {
        var capture = OpenOnWorldMap();

        // A selection the player has made and the worker has not drained yet.
        _ = capture.ObserveRawPoll(Pad(GamepadButton.DPadDown), Now);

        capture.RequestClose(ControllerNavigationDomain.Field);

        Equal(true, capture.Commands.TryDequeue(out var kept, out _),
            "a suspended field does not discard the world map's queued selection");
        Equal(ControllerNavigationCommand.NextTarget, kept,
            "which is still the selection the player made");
    }

    /// <summary>
    /// The guard is about somebody else's menu, not about closing being unreliable. The
    /// domain that owns the pad still closes its own, and still clears what it queued.
    /// </summary>
    private static void TheOwningDomainStillClosesItsOwnMenu()
    {
        // The same undrained selection the refused close above had to leave alone.
        var capture = OpenOnWorldMap();
        _ = capture.ObserveRawPoll(Pad(GamepadButton.DPadDown), Now);

        capture.RequestClose(ControllerNavigationDomain.WorldMap);
        var strip = capture.ObserveRawPoll(Pad(GamepadButton.None), Now);

        Equal(false, capture.IsOpen, "the domain holding the pad closes its own menu");
        Equal(GamepadButton.None, strip, "and lets go of the game's controls");
        Equal(false, capture.Commands.TryDequeue(out _, out _),
            "and its queued selections go with it");
    }

    /// <summary>
    /// A shutdown or a lost lifecycle frame is nobody's domain and always applies.
    /// </summary>
    private static void TheGlobalCloseStillAppliesToWhoeverHoldsThePad()
    {
        var capture = OpenOnWorldMap();

        capture.RequestClose();
        _ = capture.ObserveRawPoll(Pad(GamepadButton.None), Now);

        Equal(false, capture.IsOpen, "the global close still shuts the world map's menu");
    }

    /// <summary>The world map holding the pad with its menu open, as the log shows it.</summary>
    private static ControllerNavigationCapture OpenOnWorldMap()
    {
        var capture = new ControllerNavigationCapture(
            suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
            captureIsInstalled: () => true);

        capture.PublishContext(
            ControllerNavigationDomain.WorldMap,
            isHostForeground: true,
            moduleSupportsNavigation: true,
            gameIsBusy: false,
            Now,
            identity: 3);

        _ = capture.ObserveRawPoll(Pad(GamepadButton.None), Now);
        _ = capture.ObserveRawPoll(Pad(GamepadButton.RightThumb), Now);
        Equal(true, capture.IsOpen, "the fixture opened the menu on the world map");

        // The click that opened it is still down and is blocked until released.
        _ = capture.ObserveRawPoll(Pad(GamepadButton.None), Now);
        Drain(capture, out _);
        return capture;
    }

    private static GamepadSnapshot Pad(GamepadButton buttons) => new(true, 0, 0, buttons);

    private static void Drain(ControllerNavigationCapture capture, out ControllerNavigationCommand last)
    {
        last = ControllerNavigationCommand.None;
        while (capture.Commands.TryDequeue(out var command, out _))
        {
            last = command;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Controller navigation domain ownership: {message}. " +
                $"Expected {expected}, actual {actual}.");
        }
    }
}
