using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The controller menu through the production pieces the runtimes actually use: the
/// capture that runs inside the game's own input read, and the dispatcher over a
/// real <see cref="FieldNavigationController"/> and a real
/// <see cref="NavigationAutoWalkController"/>.
///
/// <para>Nothing here asserts on a flag the policy set. What is checked is the
/// button word the game would have received, the route the navigation controller
/// really has, and the direction keys the auto walk controller really holds - which
/// is the only way to know that a press chose a destination <em>instead of</em>
/// reaching the game rather than <em>as well as</em>.</para>
/// </summary>
internal static class ControllerNavigationAdapterTests
{
    private const int Field = 500;
    private static readonly DateTime Start = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        TheGameNeverReceivesAButtonTheMenuIsUsing();
        OpeningStartsNothingAndDescribesWhereTheSelectionIs();
        ExplicitStartsReplaceTheRouteAndNeverCancelIt();
        StopEndsBothGuidanceAndAutoWalk();
        AStopStillStopsAfterTheContextChanged();
        BrowsingHoldsThePartyStillWithoutLosingTheRoute();
        LosingTheContextReleasesTheGamesControlsAndRearms();
        ABacklogIsBoundedAndStopsSurviveAClear();
        SpokenGuidanceReleasesTheAutomaticWalk();
        AStaleContextClosesTheMenuOnItsOwn();
        OnlyTheLiveDomainConsumesItsCommands();
        TheWorldMapBehavesTheSameWayAsTheField();
        AMenuWhoseControllerStoppedRespondingRetiresOnce();
        ASelectionFromAnotherRoomIsNotApplied();
        AnIntentionalCloseTailSurvivesAnImmediateContextChange();
    }

    /// <summary>
    /// The whole point of hooking the getter rather than polling. What the capture
    /// returns is applied to the very word the game is about to read, so this checks
    /// the word.
    /// </summary>
    private static void TheGameNeverReceivesAButtonTheMenuIsUsing()
    {
        var host = new FakeHost();

        // Menu shut: the game gets its pad exactly as it was. Not "mostly" - the
        // player's ordinary controls must be untouched when we are not using them.
        Equal(GamepadButton.A | GamepadButton.DPadDown,
            host.Poll(GamepadButton.A | GamepadButton.DPadDown),
            "with the menu closed the game receives every button");

        // The opening click is the one case people get wrong: it is decided in the
        // same read, so even the R3 that opens the menu can be kept from the game.
        _ = host.Poll(GamepadButton.None);
        Equal(GamepadButton.None, host.Poll(GamepadButton.RightThumb),
            "the click that opens the menu does not reach the game either");
        Equal(true, host.Capture.IsOpen, "and the menu is open");

        // Everything the menu owns, while it owns it.
        Equal(GamepadButton.None, host.Poll(GamepadButton.DPadDown | GamepadButton.LeftShoulder),
            "nor does anything the menu is using");

        // What it does not own still gets through: the sticks and the triggers are
        // the game's, and the player can still turn the camera while browsing.
        Equal(GamepadButton.Start | GamepadButton.Back,
            host.Poll(GamepadButton.Start | GamepadButton.Back | GamepadButton.A),
            "buttons the menu does not own are untouched, and the ones it owns are gone");
    }

    private static void OpeningStartsNothingAndDescribesWhereTheSelectionIs()
    {
        var host = new FakeHost();
        Open(host);
        host.Drain();

        Equal(false, host.Navigation.BeaconEnabled,
            "opening the menu starts no route - the player asked for a list, not a journey");
        Equal(false, host.AutoWalkIsRunning, "and no walking");
        Equal(true, host.Spoken.Count > 0, "but it says something");
        Equal(true, host.Spoken[0].StartsWith("Navigation menu", StringComparison.Ordinal),
            $"and it names itself: {host.Spoken[0]}");
    }

    private static void ExplicitStartsReplaceTheRouteAndNeverCancelIt()
    {
        var host = new FakeHost();
        Open(host);
        host.Drain();

        Press(host, GamepadButton.A);
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "A starts a real route");
        var first = host.Navigation.CurrentTargetLabel;

        // A second A on the same selection restates it. A toggle would have turned
        // the route off here, which is how somebody cancels the journey they just
        // asked for without meaning to.
        Open(host);
        Press(host, GamepadButton.A);
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "a second A leaves the route running");
        Equal(first, host.Navigation.CurrentTargetLabel, "on the same target");

        // A different selection replaces it.
        Open(host);
        Press(host, GamepadButton.DPadDown);
        host.Drain();
        Press(host, GamepadButton.A);
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "choosing elsewhere leaves a route running");
        Equal(false, string.Equals(first, host.Navigation.CurrentTargetLabel, StringComparison.Ordinal),
            $"and it is the new target, not the old one ({host.Navigation.CurrentTargetLabel})");
    }

    private static void StopEndsBothGuidanceAndAutoWalk()
    {
        foreach (var stopButton in new[] { GamepadButton.B, GamepadButton.RightThumb })
        {
            var host = new FakeHost();
            Open(host);
            Press(host, GamepadButton.X);
            host.Drain();
            Equal(true, host.Navigation.BeaconEnabled, $"{stopButton}: X starts a route");
            Equal(true, host.AutoWalkIsRunning, $"{stopButton}: and walks it");

            Open(host);
            Press(host, stopButton);
            host.Drain();
            Equal(false, host.Navigation.BeaconEnabled, $"{stopButton}: stops the guidance");
            Equal(false, host.AutoWalkIsRunning, $"{stopButton}: and the walking");
            Equal(false, host.Capture.IsOpen, $"{stopButton}: and closes the menu");
            Equal(0, host.HeldDirectionKeys,
                $"{stopButton}: with no direction key left held down");
        }
    }

    /// <summary>
    /// The failure that would be worst to live with: the player presses stop, the
    /// module changes in the same moment, and the mod keeps walking them because the
    /// command arrived somewhere that had stopped listening.
    /// </summary>
    private static void AStopStillStopsAfterTheContextChanged()
    {
        var host = new FakeHost();
        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "walking");

        // Stop pressed, and before the worker gets to it the player has left the
        // field: the capture closes and clears, but the stop is not clearable.
        Open(host);
        Press(host, GamepadButton.B);
        host.Capture.RequestClose();
        host.ModuleSupportsNavigation = false;
        host.Drain();

        Equal(false, host.Navigation.BeaconEnabled, "the stop still ended the guidance");
        Equal(false, host.AutoWalkIsRunning, "and the walking");
        Equal(0, host.HeldDirectionKeys, "with nothing left held");
    }

    private static void BrowsingHoldsThePartyStillWithoutLosingTheRoute()
    {
        var host = new FakeHost();
        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "walking to the first target");

        // One more frame, so the auto walk controller has actually pushed a
        // direction at the game rather than merely being enabled.
        host.Drain();
        Equal(true, host.HeldDirectionKeys > 0, "with a direction key held");

        // Mid-walk, the player opens the menu to look. The route survives; the feet
        // stop. Being carried away from the spot while reading a list is exactly
        // what the user asked to avoid.
        Open(host);
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "the route survives browsing");
        Equal(0, host.HeldDirectionKeys, "but no direction key is held while the menu is open");

        // Closing without choosing leaves the route alone and lets the walk resume.
        Press(host, GamepadButton.A);
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "and it is still running afterwards");
    }

    private static void LosingTheContextReleasesTheGamesControlsAndRearms()
    {
        var cases = new (string What, Action<FakeHost> Break)[]
        {
            ("the window lost the foreground", host => host.IsForeground = false),
            ("the player left the field", host => host.ModuleSupportsNavigation = false),
            ("the game opened its own window", host => host.GameIsBusy = true),
            ("the controller disconnected", host => host.PadIsConnected = false),
        };

        foreach (var (what, @break) in cases)
        {
            var host = new FakeHost();
            Open(host);
            Equal(GamepadButton.None, host.Poll(GamepadButton.DPadDown),
                $"{what}: the menu is holding the pad");

            // The player is holding the stick click and a direction through the gap -
            // on a loading screen, in a battle, while the window is behind something.
            @break(host);
            _ = host.Poll(GamepadButton.RightThumb | GamepadButton.DPadDown);
            host.PadIsConnected = true;
            host.IsForeground = true;
            host.ModuleSupportsNavigation = true;
            host.GameIsBusy = false;

            // Control goes straight back, still held. A menu that kept those buttons
            // after the player alt-tabbed would look exactly like a broken pad.
            Equal(GamepadButton.RightThumb | GamepadButton.DPadDown,
                host.Poll(GamepadButton.RightThumb | GamepadButton.DPadDown),
                $"{what}: the game gets every button back");
            Equal(false, host.Capture.IsOpen,
                $"{what}: and a click held across the gap did not reopen the menu");

            // Released, and only a fresh press counts.
            Equal(GamepadButton.None, host.Poll(GamepadButton.None),
                $"{what}: releasing it does nothing either");
            Equal(false, host.Capture.IsOpen, $"{what}: still shut");
            Equal(GamepadButton.None, host.Poll(GamepadButton.RightThumb),
                $"{what}: and a genuine press afterwards works again");
            Equal(true, host.Capture.IsOpen, $"{what}: reopening the menu");
        }
    }

    private static void ABacklogIsBoundedAndStopsSurviveAClear()
    {
        // The hook runs on the game's thread and the worker may be busy speaking.
        // The queue must cost the game a dropped selection rather than growth, and
        // a stop must never be the thing dropped.
        var queue = new ControllerNavigationCommandQueue(capacity: 4);
        for (var index = 0; index < 50; index++)
        {
            queue.Enqueue(ControllerNavigationCommand.NextTarget);
        }

        Equal(4, queue.Count, "the queue is bounded");
        Equal(true, queue.Dropped > 0, "and says how much it lost");

        queue.Enqueue(ControllerNavigationCommand.StopNavigation);
        queue.ClearExceptStops();
        Equal(1, queue.Count, "clearing keeps stops and nothing else");
        Equal(true, queue.TryDequeue(out var kept), "and it is still there");
        Equal(ControllerNavigationCommand.StopNavigation, kept, "and it is the stop");
    }

    /// <summary>
    /// Root found this one and it is the sharpest of the lot: the player is being
    /// walked somewhere, opens the menu, picks a place and presses A for spoken
    /// directions - and the mod carries on driving them to the old route. A is
    /// guidance, and guidance means the player is walking themselves.
    /// </summary>
    private static void SpokenGuidanceReleasesTheAutomaticWalk()
    {
        var host = new FakeHost();
        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "the mod is walking the party");
        Equal(true, host.HeldDirectionKeys > 0, "with a direction key held");

        Open(host);
        Press(host, GamepadButton.DPadDown);
        host.Drain();
        Press(host, GamepadButton.A);
        host.Drain();

        Equal(true, host.Navigation.BeaconEnabled, "A leaves spoken guidance running");
        Equal(false, host.AutoWalkIsRunning, "and stops the mod driving");
        host.Drain();
        Equal(0, host.HeldDirectionKeys, "with no direction key still held");

        // X after that starts the walking again, explicitly.
        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "X starts the walking again");
        Equal(true, host.HeldDirectionKeys > 0, "and drives");
    }

    /// <summary>
    /// A worker that stalls or dies must not leave the menu believing the player is
    /// still stood in a field with the window in front of them.
    /// </summary>
    private static void AStaleContextClosesTheMenuOnItsOwn()
    {
        var host = new FakeHost();
        Open(host);
        Equal(GamepadButton.None, host.Poll(GamepadButton.DPadDown), "the menu holds the pad");

        // The worker stops publishing. Time passes only in the reads the game keeps
        // making, which is exactly the situation this guards.
        host.PublishContext = false;
        host.Advance(ControllerNavigationCapture.ContextFreshness + TimeSpan.FromMilliseconds(50));
        Equal(GamepadButton.DPadDown, host.Poll(GamepadButton.DPadDown),
            "a context nobody has refreshed stops being believed");
        Equal(false, host.Capture.IsOpen, "and the menu closes");
    }

    /// <summary>
    /// One capture serves the field and the world map. The inactive one must not eat
    /// the live one's commands and answer them against a selection nobody can see.
    /// </summary>
    private static void OnlyTheLiveDomainConsumesItsCommands()
    {
        var host = new FakeHost();
        Open(host);
        Press(host, GamepadButton.A);

        var queued = host.Capture.Commands.Count;
        Equal(true, queued > 0, "there is something queued to be stolen");

        // The world map drains while the field owns the capture.
        _ = host.DrainAs(ControllerNavigationDomain.WorldMap);
        Equal(false, host.Navigation.BeaconEnabled,
            "the other domain does not act on a command that is not its own");
        Equal(queued, host.Capture.Commands.Count, "and leaves every one of them queued");

        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "the owning domain still gets it");
    }

    /// <summary>
    /// The world map is the same shared policy over the same shared services, so the
    /// rules that matter are asserted there too rather than assumed to carry over.
    /// </summary>
    private static void TheWorldMapBehavesTheSameWayAsTheField()
    {
        var host = new FakeHost { Domain = ControllerNavigationDomain.WorldMap };

        Open(host);
        host.Drain();
        Equal(false, host.Navigation.BeaconEnabled, "opening starts nothing on the world map either");

        Press(host, GamepadButton.X);
        host.Drain();
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "X starts a route");
        Equal(true, host.AutoWalkIsRunning, "and walks it");

        // The finding root caught, on this domain as well.
        Open(host);
        Press(host, GamepadButton.A);
        host.Drain();
        Equal(true, host.Navigation.BeaconEnabled, "A keeps spoken guidance");
        Equal(false, host.AutoWalkIsRunning, "and releases the automatic movement");
        host.Drain();
        Equal(0, host.HeldDirectionKeys, "with nothing held");

        Open(host);
        Press(host, GamepadButton.B);
        host.Drain();
        Equal(false, host.Navigation.BeaconEnabled, "B stops the guidance");
        Equal(false, host.AutoWalkIsRunning, "and the walking");
    }

    /// <summary>
    /// Root's watchdog probe, as a regression: the menu is open and the game simply
    /// stops asking - an unplugged pad is read no further, it does not report itself
    /// as disconnected. There is deliberately <b>no</b> poll after this point.
    ///
    /// <para>The old code asked the menu to close on its next poll, and there was no
    /// next poll, so the menu stayed open for ever and repeated its complaint on
    /// every worker frame.</para>
    /// </summary>
    private static void AMenuWhoseControllerStoppedRespondingRetiresOnce()
    {
        var host = new FakeHost();
        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "the mod is walking the party");

        Open(host);
        Equal(true, host.Capture.IsOpen, "and the menu is open");
        var saidBefore = host.Spoken.Count;

        // From here the pad is never polled again. Only the worker runs.
        host.Advance(ControllerNavigationDispatcher.PollFreshness + TimeSpan.FromMilliseconds(100));
        host.Drain();
        Equal(false, host.Capture.IsOpen, "the menu retires without needing another poll");
        Equal(1, host.Spoken.Count - saidBefore, "and says so exactly once");

        host.Advance(TimeSpan.FromSeconds(1));
        host.Drain();
        host.Drain();
        Equal(false, host.Capture.IsOpen, "it stays closed");
        Equal(1, host.Spoken.Count - saidBefore, "without repeating itself on every frame");

        // The route itself was never cancelled - the player asked for it before they
        // opened the menu - so the walk resumes now the menu has stopped holding it.
        // What must not survive is the menu holding it suspended for ever.
        Equal(true, host.Navigation.BeaconEnabled, "the route the player asked for survives");
        Equal(true, host.AutoWalkIsRunning, "and the walk resumes rather than staying frozen");

        // Neutral rearm: the stick click held through the silence is not a press.
        Equal(GamepadButton.RightThumb, host.Poll(GamepadButton.RightThumb),
            "a button held through the silence reaches the game and does not reopen");
        Equal(false, host.Capture.IsOpen, "still closed");
        _ = host.Poll(GamepadButton.None);
        Equal(GamepadButton.None, host.Poll(GamepadButton.RightThumb), "a fresh press works again");
        Equal(true, host.Capture.IsOpen, "reopening the menu");
    }

    /// <summary>
    /// A start queued in one room must not be applied in the next: the target it named
    /// is not there. A stop is never rejected - the walking is still happening.
    /// </summary>
    private static void ASelectionFromAnotherRoomIsNotApplied()
    {
        var host = new FakeHost();
        Open(host);
        Press(host, GamepadButton.A);

        // The player leaves the room before the worker gets to it.
        host.Identity = 501;
        host.Drain();
        Equal(false, host.Navigation.BeaconEnabled,
            "a start decided in the previous field is discarded");

        // The same crossing must not lose a stop.
        host.Identity = 500;
        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "walking");

        Open(host);
        Press(host, GamepadButton.B);
        host.Identity = 502;
        host.Drain();
        Equal(false, host.AutoWalkIsRunning, "but a stop still crosses the boundary");
        Equal(false, host.Navigation.BeaconEnabled, "and ends the guidance");

        // Clearing on an identity change is the first line; the generation tag on each
        // command is the second, and it is what catches anything that reaches the
        // queue without going past that clear. Asserted directly, because the two
        // mechanisms hide each other in an end-to-end run.
        host.Capture.Commands.Enqueue(
            ControllerNavigationCommand.StartNavigation, generation: -999);
        host.Drain();
        Equal(false, host.Navigation.BeaconEnabled,
            "a start tagged with a generation that is not live is not applied");

        Open(host);
        Press(host, GamepadButton.X);
        host.Drain();
        host.Drain();
        Equal(true, host.AutoWalkIsRunning, "walking again");
        host.Capture.Commands.Enqueue(
            ControllerNavigationCommand.StopNavigation, generation: -999);
        host.Drain();
        Equal(false, host.AutoWalkIsRunning,
            "but a stop is never rejected for its generation - the walking is real");
    }

    /// <summary>
    /// The A that chose a destination is still physically down when the module changes
    /// a frame later. The tail an intentional close established must not be erased by
    /// the involuntary close that follows, or that press reaches the game.
    /// </summary>
    private static void AnIntentionalCloseTailSurvivesAnImmediateContextChange()
    {
        var host = new FakeHost();
        Open(host);

        // A chooses, the menu closes, and A is still held.
        Equal(GamepadButton.None, host.Poll(GamepadButton.A), "A is taken from the game");
        Equal(false, host.Capture.IsOpen, "and the menu closes");

        // The game takes over in the very next frame, still holding A.
        host.GameIsBusy = true;
        Equal(GamepadButton.None, host.Poll(GamepadButton.A),
            "the held A is still kept from the game after the context changed");
        host.ModuleSupportsNavigation = false;
        Equal(GamepadButton.None, host.Poll(GamepadButton.A), "and after the module changed");

        // Released, and only then does the game get it back.
        Equal(GamepadButton.None, host.Poll(GamepadButton.None), "releasing ends the tail");
        Equal(GamepadButton.A, host.Poll(GamepadButton.A), "and the next press is the game's");
    }

    private static void Open(FakeHost host)
    {
        _ = host.Poll(GamepadButton.None);
        _ = host.Poll(GamepadButton.RightThumb);
        _ = host.Poll(GamepadButton.None);
        Equal(true, host.Capture.IsOpen, "the fixture opened the menu");
    }

    private static void Press(FakeHost host, GamepadButton button)
    {
        _ = host.Poll(button);
        _ = host.Poll(GamepadButton.None);
    }

    /// <summary>
    /// Stands in for a runtime: the production capture wired exactly as the hooks
    /// wire it, a real navigation controller over real targets, and a real auto walk
    /// controller over a recording key sink.
    /// </summary>
    private sealed class FakeHost
    {
        private readonly RecordingKeySink keys = new();
        private readonly NavigationAutoWalkController autoWalk;
        private readonly ControllerNavigationDispatcher dispatcher;
        private DateTime now = Start;

        public FakeHost()
        {
            Navigation = new FieldNavigationController(
                new FieldNavigationTargetSource(
                [
                    new FieldNavigationTarget(
                        Field, FieldNavigationCategory.Story, "First destination",
                        0, -1000, 0, "first", CompletesOnArrival: true),
                    new FieldNavigationTarget(
                        Field, FieldNavigationCategory.Story, "Second destination",
                        0, -2000, 0, "second", CompletesOnArrival: true),
                ]),
                new StraightRoutePlanner());
            autoWalk = new NavigationAutoWalkController(keys);
            Capture = new ControllerNavigationCapture(
                suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                () => true,
                isForegroundNow: () => IsForeground);

            // The production policy, not a copy of it: this is the same class both
            // Mod.cs and the x64 coordinators build.
            dispatcher = new ControllerNavigationDispatcher(
                new ControllerNavigationServices(
                    () => Navigation.BeaconEnabled,
                    action => Navigation.HandleAction(action, Position, Transform)?.Speech,
                    () => autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field),
                    () => autoWalk.TryStart(NavigationAutoWalkDomain.Field, routeActive: true),
                    () => { if (autoWalk.Enabled) { _ = autoWalk.Stop(); } },
                    () => autoWalk.Suspend()),
                speech => { Spoken.Add(speech); return true; });

            // Story is not the first category, and the menu's own cycling would take
            // several presses to reach it. The tests are about the controller, not
            // about where the cursor starts.
            _ = Navigation.HandleAction(FieldNavigationAction.NextCategory, Position, Transform);
        }

        public FieldNavigationController Navigation { get; }

        public ControllerNavigationCapture Capture { get; }

        public List<string> Spoken { get; } = [];

        public bool IsForeground { get; set; } = true;

        public bool ModuleSupportsNavigation { get; set; } = true;

        public bool GameIsBusy { get; set; }

        public bool PadIsConnected { get; set; } = true;

        public ControllerNavigationDomain Domain { get; set; } = ControllerNavigationDomain.Field;

        public int Identity { get; set; } = 500;

        public bool AutoWalkIsRunning => autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field);

        public int HeldDirectionKeys => keys.HeldCount;

        public FieldPositionSnapshot Position { get; } =
            new(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0);

        public FieldNavigationControlTransform Transform { get; } = new(0);

        /// <summary>
        /// One poll of the pad, exactly as a capture hook does it: the raw state goes
        /// in, and what comes back is what the game would be handed.
        /// </summary>
        public bool PublishContext { get; set; } = true;

        /// <summary>Moves the clock without a poll, for staleness.</summary>
        public void Advance(TimeSpan span) => now += span;

        public GamepadButton Poll(GamepadButton buttons)
        {
            now = now.AddMilliseconds(16);
            var raw = PadIsConnected
                ? new GamepadSnapshot(true, 0, 0, buttons)
                : GamepadSnapshot.Disconnected;
            if (PublishContext)
            {
                Capture.PublishContext(
                    Domain, IsForeground, ModuleSupportsNavigation, GameIsBusy, now, Identity);
            }

            var strip = Capture.ObserveRawPoll(raw, now);
            return buttons & ~strip;
        }

        /// <summary>
        /// One turn of the worker, in the order that is hardest on the mod: the auto
        /// walk controller re-asserts its direction first, exactly as it does every
        /// frame, and only then does the controller menu get its turn.
        ///
        /// <para>Both runtimes also refuse to drive while the menu is open, which is
        /// the first line of defence. This harness deliberately does not, so that the
        /// dispatcher's own hold is the thing being tested rather than a second copy
        /// of the adapters' gate: a drain that stopped suspending would leave the
        /// party walking here even though the adapters would have caught it.</para>
        /// </summary>
        public void Drain()
        {
            // Production order: the worker publishes what it can see, then drains.
            // Publishing here is what makes a room change visible to the queue.
            if (PublishContext)
            {
                Capture.PublishContext(
                    Domain, IsForeground, ModuleSupportsNavigation, GameIsBusy, now, Identity);
            }

            var input = FieldNavigationInput.None;
            var canMove = Navigation.TryResolveAutomaticInput(Position, Transform, 80, out input);
            _ = autoWalk.Drive(
                canMove ? input : FieldNavigationInput.None,
                canMove,
                Navigation.BeaconEnabled);

            _ = dispatcher.Drain(Capture, Domain, now);
        }

        /// <summary>A drain from a domain that may not be the one that owns the pad.</summary>
        public bool DrainAs(ControllerNavigationDomain domain) =>
            dispatcher.Drain(Capture, domain, now);


    }

    /// <summary>
    /// Records which movement keys the auto walk controller is really holding down.
    ///
    /// <para>This is what makes "the party stopped" a fact rather than a flag: the
    /// controller pushes scan codes at the game, and a key left down is a character
    /// still walking no matter what any state says.</para>
    /// </summary>
    private sealed class RecordingKeySink : IHighwayKeyboardInputSink
    {
        private readonly HashSet<ushort> held = [];

        public int HeldCount => held.Count;

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            foreach (var transition in transitions)
            {
                if (transition.IsKeyDown)
                {
                    held.Add(transition.ScanCode);
                }
                else
                {
                    held.Remove(transition.ScanCode);
                }
            }

            return new HighwayKeyboardSendResult(transitions.Count, 0);
        }
    }

    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [position.TriangleId],
                [],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                position.TriangleId);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Controller navigation adapter: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
