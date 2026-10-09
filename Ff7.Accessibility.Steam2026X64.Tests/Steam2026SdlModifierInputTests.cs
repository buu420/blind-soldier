using Ff7.Accessibility.Core;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

internal static class Steam2026SdlModifierInputTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        UnmodifiedR3IsNotANavigationShortcut();
        ModifierAxesAreConsumedAndReleaseTailsStayPrivate();
        ASecondPadCanTakeOverWithItsFirstFreshTrigger();
        R3BeforeItsTriggerCannotToggleBattleAssist();
        Console.WriteLine("Steam SDL modifier input tests passed.");
    }

    private static void ModifierAxesAreConsumedAndReleaseTailsStayPrivate()
    {
        var buttons = new byte[15];
        var axes = new short[6];
        using var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
            installed => new(s => new ControllerAccessibilityMenu(s), installed),
            (_, b) => buttons[b], _ => 1, () => Now, originalGetAxis: (_, a) => axes[a]);
        hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        _ = hook.InvokeGetAxisForTest(0x1000, 4);
        axes[4] = 24000;
        axes[0] = 17000;
        Equal((short)0, hook.InvokeGetAxisForTest(0x1000, 4), "real trigger axis is consumed");
        Equal(true, hook.Capture.IsOpen, "the axis getter alone can open navigation");
        Equal((short)0, hook.InvokeGetAxisForTest(0x1000, 0), "the stick cannot move during browsing");
        buttons[8] = 1;
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "modified R3 is consumed");
        axes[4] = 0;
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "R3 remains consumed after its trigger releases");
        buttons[8] = 0;
        _ = hook.InvokeGetButtonForTest(0x1000, 8);
        Equal((short)17000, hook.InvokeGetAxisForTest(0x1000, 0), "movement returns after the entire chord releases");
        buttons[8] = 1;
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "R3 remains reserved against partial-chord shortcut releases");
    }

    private static void ASecondPadCanTakeOverWithItsFirstFreshTrigger()
    {
        var buttons = new Dictionary<nint, byte[]> { [0x1000] = new byte[15], [0x2000] = new byte[15] };
        var axes = new Dictionary<nint, short[]> { [0x1000] = new short[6], [0x2000] = new short[6] };
        using var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
            installed => new(s => new ControllerAccessibilityMenu(s), installed),
            (p, b) => buttons[p][b], _ => 1, () => Now, originalGetAxis: (p, a) => axes[p][a]);
        hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        _ = hook.InvokeGetButtonForTest(0x1000, 0);
        _ = hook.InvokeGetButtonForTest(0x2000, 0);
        axes[0x2000][5] = 22000;
        Equal((short)0, hook.InvokeGetAxisForTest(0x2000, 5), "fresh foreign trigger is consumed after takeover");
        Equal((nint)0x2000, hook.LatchedController, "first deliberate foreign trigger moves ownership");
        Equal(true, hook.Capture.IsOpen, "the second pad can browse");
        hook.Capture.Commands.ClearExceptStops();
        axes[0x1000][4] = 22000;
        buttons[0x1000][8] = buttons[0x1000][0] = 1;
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "foreign modified R3 stays private");
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 0), "foreign modified A stays private");
        axes[0x1000][4] = 0;
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "foreign R3 release tail stays private");
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 0), "foreign A release tail stays private");
        Equal(0, hook.Capture.Commands.Count, "an open menu cannot be stolen by another pad");
    }

    private static void UnmodifiedR3IsNotANavigationShortcut()
    {
        byte physicalR3 = 0;
        using var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
            installed => new(s => new ControllerAccessibilityMenu(s), installed),
            (_, button) => button == 8 ? physicalR3 : (byte)0, _ => 1, () => Now);
        hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        _ = hook.InvokeGetButtonForTest(0x1000, 8);
        physicalR3 = 1;
        Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "unmodified R3 remains reserved against native Battle Assist");
        Equal(false, hook.Capture.IsOpen, "unmodified R3 cannot open the mod");
    }

    private static void R3BeforeItsTriggerCannotToggleBattleAssist()
    {
        var buttons = new byte[15];
        var axes = new short[6];
        using var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
            installed => new(s => new ControllerAccessibilityMenu(s), installed),
            (_, b) => buttons[b], _ => 1, () => Now, originalGetAxis: (_, a) => axes[a]);
        hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        var previouslyDown = false;
        var nativeToggles = 0;
        void NativePoll()
        {
            var down = hook.InvokeGetButtonForTest(0x1000, 8) != 0;
            if (previouslyDown && !down) nativeToggles++;
            previouslyDown = down;
        }
        NativePoll();
        buttons[8] = 1; NativePoll();
        axes[4] = 22000; NativePoll();
        buttons[8] = 0; NativePoll();
        buttons[8] = 1; NativePoll();
        axes[4] = 0; NativePoll();
        buttons[8] = 0; NativePoll();
        Equal(0, nativeToggles, "the native release-triggered action cannot see either R3-first or trigger-first partial chords");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
