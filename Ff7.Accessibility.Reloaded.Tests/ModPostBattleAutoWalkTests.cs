using System.Reflection;
using System.Runtime.CompilerServices;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The reported "auto walk breaks after a battle", driven through the real host method
/// rather than through the controller API.
///
/// <para><c>Mod.PublishControllerNavigationContextFromModule</c> is the first thing
/// <c>TickNavigationAutoWalkToggleInput</c> calls. For any module with no navigation
/// domain - battle 2, the swirl 23, the results 17 - and for a lost foreground, it used to
/// call <c>StopEveryControllerAutoWalk</c>, which clears the controller's domain. Nothing
/// on that path logs, which is why the 2026-09-22 capture shows auto walk on at 09:13:59,
/// driving at 09:14:13, and silently off by 09:15:23 with no stop line anywhere.</para>
///
/// <para>The <c>Mod</c> is built uninitialised and its fields are set directly: this runs
/// with no game, no hooks and no Reloaded-II loader, and still exercises the same method
/// the host runs.</para>
/// </summary>
internal static class ModPostBattleAutoWalkTests
{
    private const byte BattleModule = 2;
    private const byte BattleSwirlModule = 23;
    private const byte BattleResultsModule = 17;

    public static void Run()
    {
        ABattleSuspendsTheWalkAndKeepsIt();
        TheWholeBattleRoundTripKeepsTheWalk();
        LosingTheForegroundSuspendsTheWalkAndKeepsIt();
        TheControllerMenuStopIsStillAStop();
        ReturningToTitleEndsTheWalk();
    }

    private static void ABattleSuspendsTheWalkAndKeepsIt()
    {
        var host = Host();
        host.StartWalking();

        host.PublishModule(BattleModule, isForeground: true);

        Equal(true, host.AutoWalk.Enabled, "a battle does not end the walk");
        Equal(
            true,
            host.AutoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field),
            "and it is still the field's walk");
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, false),
            host.Sink.Batches[^1].Single(),
            "the held direction comes off at once");
    }

    /// <summary>
    /// The capture's own excursion sequence - swirl 23, battle 2, results 17 - and then
    /// the walk driving again. The field module itself is not published here: that branch
    /// reads the native field id through a raw pointer, which has no meaning without the
    /// game, and the excursion modules are the ones that used to end the walk.
    /// </summary>
    private static void TheWholeBattleRoundTripKeepsTheWalk()
    {
        var host = Host();
        host.StartWalking();

        foreach (var module in new[] { BattleSwirlModule, BattleModule, BattleResultsModule })
        {
            host.PublishModule(module, isForeground: true);
            Equal(true, host.AutoWalk.Enabled, $"module {module} does not end the walk");
        }

        Equal(
            true,
            host.AutoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field),
            "the walk is still the field's when the battle is over");
        Equal(
            true,
            host.AutoWalk.Drive(FieldNavigationInput.Down, canMove: true, routeActive: true).Success,
            "and it drives again without the player pressing anything");
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, true),
            host.Sink.Batches[^1].Single(),
            "the resumed walk presses its direction again");
    }

    private static void LosingTheForegroundSuspendsTheWalkAndKeepsIt()
    {
        var host = Host();
        host.StartWalking();

        host.PublishModule(BattleModule, isForeground: false);

        Equal(true, host.AutoWalk.Enabled, "alt-tabbing does not end the walk");
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, false),
            host.Sink.Batches[^1].Single(),
            "but the keys are released while the game is not listening");
    }

    /// <summary>
    /// The other caller of the old behaviour is the controller menu's own "auto walk off",
    /// and that has to stay a real stop.
    /// </summary>
    private static void TheControllerMenuStopIsStillAStop()
    {
        var host = Host();
        host.StartWalking();

        host.Invoke("StopEveryControllerAutoWalk");

        Equal(false, host.AutoWalk.Enabled, "the controller menu still ends the walk");
    }

    private static void ReturningToTitleEndsTheWalk()
    {
        var host = Host();
        host.StartWalking();
        host.PublishModule(TitleMenuCursorReader.TitleModule, isForeground: true);
        Equal(false, host.AutoWalk.Enabled, "returning to title must not resume a route in another playthrough");
    }

    private static ModHost Host() => new();

    /// <summary>
    /// A <see cref="Mod"/> with only the fields this method touches. Nothing is
    /// constructed that would need the game.
    /// </summary>
    private sealed class ModHost
    {
        private readonly Mod mod;

        internal readonly RecordingSink Sink = new();
        internal readonly NavigationAutoWalkController AutoWalk;

        internal ModHost()
        {
            mod = (Mod)RuntimeHelpers.GetUninitializedObject(typeof(Mod));
            AutoWalk = new NavigationAutoWalkController(Sink);

            Set("config", new AccessibilityConfig
            {
                EnableFieldNavigationAssistant = true,
                EnableWorldMapNavigationAssistant = true
            });
            Set("navigationAutoWalkController", AutoWalk);
            Set("fieldAutoWalkConvergence", new FieldAutoWalkConvergenceTracker());
            Set("pendingNavigationAutoWalkToggle", NavigationAutoWalkDomain.None);
            Set("controllerCaptureHook", CreateCaptureHook());
        }

        internal void StartWalking()
        {
            if (!AutoWalk.TryStart(NavigationAutoWalkDomain.Field, routeActive: true) ||
                !AutoWalk.Drive(FieldNavigationInput.Down, canMove: true, routeActive: true).Success)
            {
                throw new InvalidOperationException("expected the walk to start and hold a direction.");
            }
        }

        internal void PublishModule(byte module, bool isForeground) =>
            Invoke("PublishControllerNavigationContextFromModule", module, isForeground);

        internal void Invoke(string name, params object[] arguments)
        {
            var method = typeof(Mod).GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Mod.{name} is missing.");
            try
            {
                method.Invoke(mod, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private void Set(string name, object? value)
        {
            var field = typeof(Mod).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Mod.{name} is missing.");
            field.SetValue(mod, value);
        }

        /// <summary>
        /// The hook without its hooks. Only its <c>Capture</c> is read here, and the
        /// capture only has to accept the publish calls.
        /// </summary>
        private static object CreateCaptureHook()
        {
            var hook = RuntimeHelpers.GetUninitializedObject(typeof(XInputCaptureHook));
            var capture = new ControllerNavigationCapture(
                suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                () => true);
            typeof(XInputCaptureHook)
                .GetField("capture", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(hook, capture);
            return hook;
        }
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        internal readonly List<IReadOnlyList<HighwayKeyboardTransition>> Batches = [];

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            Batches.Add(transitions);
            return new HighwayKeyboardSendResult(transitions.Count, 0);
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
