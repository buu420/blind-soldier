using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NavigationProgressControlTests
{
    internal static void Run()
    {
        DefaultsMatchTheAccessibleFivePercentMode();
        QuantizesForwardAndBackwardProgressAtTheSelectedInterval();
        ToggleHidesAndRestoresAnActiveRouteAtItsCurrentProgress();
        OneToggleControlsFieldAndWorldRouteIndicators();
        IntervalKeysWrapThroughTheFourSupportedValues();
        HotkeyRouterSamplesF5ThroughF7OnceAndInOrder();
        LegacyCompatibilityEnablesTheRuntimeBeforeSeventhHeavenDeserializes();
    }

    private static void DefaultsMatchTheAccessibleFivePercentMode()
    {
        var config = new AccessibilityConfig();
        Equal(true, config.EnableNavigationProgressIndicators, "progress controls default enabled");
        Equal(5, config.NavigationProgressIntervalPercent, "progress controls default to five percent");
    }

    private static void QuantizesForwardAndBackwardProgressAtTheSelectedInterval()
    {
        var settings = new NavigationProgressController(enabled: true, intervalPercent: 10);
        var native = new RecordingProgressSink();
        using var sink = new IntervalFieldNavigationProgressSink(native, settings);

        sink.Activate(3);
        sink.SetValue(9);
        sink.SetValue(10);
        sink.SetValue(19);
        sink.SetValue(20);
        sink.SetValue(14);

        SequenceEqual(
            ["activate:0", "value:10", "value:20", "value:10"],
            native.Events,
            "progress publishes only selected thresholds and reverses on backtracking");
    }

    private static void ToggleHidesAndRestoresAnActiveRouteAtItsCurrentProgress()
    {
        var settings = new NavigationProgressController(enabled: true, intervalPercent: 5);
        var native = new RecordingProgressSink();
        using var sink = new IntervalFieldNavigationProgressSink(native, settings);

        sink.Activate(0);
        sink.SetValue(37);
        Equal(
            "Navigation progress off.",
            settings.HandleAction(NavigationProgressHotkeyAction.Toggle),
            "F5 disable speech");
        sink.SetValue(64);
        Equal(
            "Navigation progress on.",
            settings.HandleAction(NavigationProgressHotkeyAction.Toggle),
            "F5 enable speech");

        SequenceEqual(
            ["activate:0", "value:35", "deactivate", "activate:60"],
            native.Events,
            "disabled progress remains hidden and restores the live route value");
    }

    private static void IntervalKeysWrapThroughTheFourSupportedValues()
    {
        var settings = new NavigationProgressController(enabled: true, intervalPercent: 5);

        Equal(
            "Navigation progress interval 20 percent.",
            settings.HandleAction(NavigationProgressHotkeyAction.PreviousInterval),
            "F6 wraps five to twenty");
        Equal(20, settings.IntervalPercent, "previous interval state");
        Equal(
            "Navigation progress interval 5 percent.",
            settings.HandleAction(NavigationProgressHotkeyAction.NextInterval),
            "F7 wraps twenty to five");
        Equal(5, settings.IntervalPercent, "next interval state");

        settings.HandleAction(NavigationProgressHotkeyAction.NextInterval);
        Equal(10, settings.IntervalPercent, "five advances to ten");
        settings.HandleAction(NavigationProgressHotkeyAction.NextInterval);
        Equal(15, settings.IntervalPercent, "ten advances to fifteen");
        settings.HandleAction(NavigationProgressHotkeyAction.NextInterval);
        Equal(20, settings.IntervalPercent, "fifteen advances to twenty");
    }

    private static void OneToggleControlsFieldAndWorldRouteIndicators()
    {
        var settings = new NavigationProgressController(enabled: true, intervalPercent: 5);
        var fieldNative = new RecordingProgressSink();
        var worldNative = new RecordingProgressSink();
        using var field = new IntervalFieldNavigationProgressSink(fieldNative, settings);
        using var world = new IntervalFieldNavigationProgressSink(worldNative, settings);

        field.Activate(22);
        world.Activate(38);
        settings.HandleAction(NavigationProgressHotkeyAction.Toggle);
        settings.HandleAction(NavigationProgressHotkeyAction.NextInterval);
        settings.HandleAction(NavigationProgressHotkeyAction.Toggle);

        SequenceEqual(
            ["activate:20", "deactivate", "activate:20"],
            fieldNative.Events,
            "field progress follows the shared toggle and interval");
        SequenceEqual(
            ["activate:35", "deactivate", "activate:30"],
            worldNative.Events,
            "world progress follows the same shared toggle and interval");
    }

    private static void HotkeyRouterSamplesF5ThroughF7OnceAndInOrder()
    {
        var observedKeys = new List<int>();
        var actions = NavigationProgressHotkeyRouter.ReadActions(key =>
        {
            observedKeys.Add(key);
            return key is NavigationProgressHotkeyRouter.VirtualKeyF5 or
                NavigationProgressHotkeyRouter.VirtualKeyF7;
        });

        SequenceEqual([0x74, 0x75, 0x76], observedKeys, "F5-F7 sampling order");
        SequenceEqual(
            [NavigationProgressHotkeyAction.Toggle, NavigationProgressHotkeyAction.NextInterval],
            actions,
            "pressed progress actions");
    }

    private static void LegacyCompatibilityEnablesTheRuntimeBeforeSeventhHeavenDeserializes()
    {
        const string switchName =
            "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization";
        AppContext.SetSwitch(switchName, false);

        _ = new Mod();
        _ = new Mod();

        Equal(
            true,
            AppContext.TryGetSwitch(switchName, out var enabled) && enabled,
            "7th Heaven BinaryFormatter compatibility switch");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
        }
    }

    private static void SequenceEqual<T>(
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual,
        string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected [{string.Join(", ", expected)}], " +
                $"got [{string.Join(", ", actual)}]");
        }
    }

    private sealed class RecordingProgressSink : IFieldNavigationProgressSink
    {
        internal List<string> Events { get; } = [];

        public void Activate(int percent) => Events.Add($"activate:{percent}");

        public void SetValue(int percent) => Events.Add($"value:{percent}");

        public void Complete() => Events.Add("complete");

        public void Deactivate() => Events.Add("deactivate");
    }
}
