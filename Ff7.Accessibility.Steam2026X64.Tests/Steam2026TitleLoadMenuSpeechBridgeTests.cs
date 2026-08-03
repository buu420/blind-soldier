using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal static class Steam2026TitleLoadMenuSpeechBridgeTests
{
    internal static void Run()
    {
        ReadsNativeContinueStateWithoutRendererGuessing();
        ReadsTranslatedContinueFileAndGame();
        RejectsUnownedRenderedGameTextAndAcceptsIngressRestart();
        ExactTitleRootAndModuleLossReleaseOwnership();
    }

    private static void ReadsNativeContinueStateWithoutRendererGuessing()
    {
        var preview = new Ff7SaveSlotPreview(
            false,
            "Cloud",
            8,
            296,
            334,
            18,
            64,
            539,
            1572,
            "No.1 Reactor");
        var bridge = new Steam2026TitleLoadMenuSpeechBridge(
            TimeSpan.Zero,
            _ => false,
            (_, _) => null);
        var now = new DateTime(2026, 7, 21, 22, 59, 0, DateTimeKind.Utc);

        bridge.SetOwnership(true);
        bridge.ObserveState(
            new TitleLoadMenuStateSnapshot(TitleLoadMenuPage.SaveFiles, 1, true, 0, null),
            now);
        Equal(
            "Select a save data file. Save 1.",
            bridge.Poll(now),
            "native Continue file state");
        bridge.ObserveState(
            new TitleLoadMenuStateSnapshot(TitleLoadMenuPage.Games, 1, true, 1, preview),
            now.AddMilliseconds(1));
        Equal(
            "Select a save game. Game 1. Cloud, level 8. No.1 Reactor. HP 296 of 334. MP 18 of 64. Time 26 minutes, 12 seconds. 539 gil.",
            bridge.Poll(now.AddMilliseconds(1)),
            "native Continue selected game state");
        bridge.ObserveState(
            new TitleLoadMenuStateSnapshot(TitleLoadMenuPage.Checking, 1, true, 0, null),
            now.AddMilliseconds(2));
        Equal("Checking save data.", bridge.Poll(now.AddMilliseconds(2)), "native Continue checking state");
        bridge.ObserveState(
            new TitleLoadMenuStateSnapshot(TitleLoadMenuPage.Loading, 1, true, 1, preview),
            now.AddMilliseconds(3));
        Equal("Loading.", bridge.Poll(now.AddMilliseconds(3)), "native Continue loading state");
    }

    private static void ReadsTranslatedContinueFileAndGame()
    {
        var preview = new Ff7SaveSlotPreview(
            false,
            "Cloud",
            8,
            296,
            334,
            18,
            64,
            539,
            1572,
            "No.1 Reactor");
        var bridge = new Steam2026TitleLoadMenuSpeechBridge(
            TimeSpan.Zero,
            saveFile => saveFile == 1,
            (_, game) => game == 1 ? preview : Ff7SaveSlotPreview.Empty);
        var now = new DateTime(2026, 7, 21, 23, 0, 0, DateTimeKind.Utc);
        var sequence = 0L;

        bridge.SetOwnership(true);
        bridge.Observe(Widget(++sequence, now, TitleLoadMenuSpeechTracker.SaveFileWidgetAddress));
        Equal(
            "Select a save data file. Save 1.",
            bridge.Poll(now),
            "translated Continue file grid");
        Equal(true, bridge.HasOwnership, "Continue file grid owns title-load speech");

        bridge.Observe(Text(++sequence, now, "Select a save game."));
        bridge.Observe(Text(++sequence, now.AddMilliseconds(1), "GAME"));
        bridge.Observe(Text(++sequence, now.AddMilliseconds(2), "01"));
        Equal(
            "Select a save game. Game 1. Cloud, level 8. No.1 Reactor. HP 296 of 334. MP 18 of 64. Time 26 minutes, 12 seconds. 539 gil.",
            bridge.Poll(now.AddMilliseconds(2)),
            "translated Continue game preview");
    }

    private static void ExactTitleRootAndModuleLossReleaseOwnership()
    {
        var bridge = new Steam2026TitleLoadMenuSpeechBridge(
            TimeSpan.Zero,
            _ => true,
            (_, _) => Ff7SaveSlotPreview.Empty);
        var now = new DateTime(2026, 7, 21, 23, 1, 0, DateTimeKind.Utc);

        bridge.SetOwnership(true);
        bridge.Observe(Widget(1, now, TitleLoadMenuSpeechTracker.SaveFileWidgetAddress));
        _ = bridge.Poll(now);
        bridge.Observe(Widget(2, now, TitleLoadMenuSpeechTracker.TitleRootWidgetAddress));
        Equal(false, bridge.HasOwnership, "exact title root releases Continue speech");

        bridge.Observe(Widget(3, now, TitleLoadMenuSpeechTracker.SaveFileWidgetAddress));
        Equal(true, bridge.HasOwnership, "exact Continue widget reacquires within title module");
        bridge.SetOwnership(false);
        Equal(false, bridge.HasOwnership, "module loss releases Continue speech");
        bridge.Observe(Widget(4, now, TitleLoadMenuSpeechTracker.SaveFileWidgetAddress));
        Equal(false, bridge.HasOwnership, "module loss rejects delayed Continue callbacks");
        Equal(null, bridge.Poll(now), "module loss clears pending Continue speech");
    }

    private static void RejectsUnownedRenderedGameTextAndAcceptsIngressRestart()
    {
        var bridge = new Steam2026TitleLoadMenuSpeechBridge(
            TimeSpan.Zero,
            _ => true,
            (_, _) => Ff7SaveSlotPreview.Empty);
        var now = new DateTime(2026, 7, 21, 23, 2, 0, DateTimeKind.Utc);

        bridge.SetOwnership(true);
        bridge.Observe(Text(10, now, "Select a save game."));
        bridge.Observe(Text(11, now, "GAME"));
        bridge.Observe(Text(12, now, "01"));
        Equal(null, bridge.Poll(now), "rendered game text without exact Continue owner is silent");
        Equal(false, bridge.HasOwnership, "unowned rendered text cannot acquire Continue speech");

        bridge.Observe(Widget(13, now, TitleLoadMenuSpeechTracker.SaveFileWidgetAddress));
        Equal(true, bridge.HasOwnership, "exact file widget owns first ingress cohort");
        bridge.ResetIngress();
        bridge.Observe(Widget(1, now, TitleLoadMenuSpeechTracker.TitleRootWidgetAddress));
        Equal(false, bridge.HasOwnership, "new ingress cohort may restart sequence numbering");
    }

    private static TranslatedMenuIngressSnapshot Widget(
        long sequence,
        DateTime now,
        uint identity)
    {
        var kind = identity == TitleLoadMenuSpeechTracker.SaveFileWidgetAddress
            ? MenuWidgetKind.TitleSaveFile
            : MenuWidgetKind.Generic;
        var widget = new TranslatedMenuWidgetIngressObservation(
            identity == TitleLoadMenuSpeechTracker.SaveFileWidgetAddress
                ? "Title load save file"
                : "Title menu",
            kind,
            0,
            0,
            identity == TitleLoadMenuSpeechTracker.SaveFileWidgetAddress ? 5 : 1,
            2,
            0,
            0,
            0)
        {
            WidgetIdentity = identity
        };
        return new TranslatedMenuIngressSnapshot(
            Steam2026MenuCallbackKind.ActiveWidgetUpdate,
            sequence,
            now,
            null,
            widget,
            null);
    }

    private static TranslatedMenuIngressSnapshot Text(long sequence, DateTime now, string value) =>
        new(
            Steam2026MenuCallbackKind.EncodedTextB,
            sequence,
            now,
            null,
            null,
            new TranslatedMenuTextObservation(
                Steam2026MenuCallbackKind.EncodedTextB,
                value,
                10,
                13,
                7,
                0));

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{description}: expected '{expected}', actual '{actual}'.");
        }
    }
}
