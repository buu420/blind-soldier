using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FfnxVoiceSpeechTests
{
    public static void Run()
    {
        VoicedDialogueDoesNotNeedARecognizedDescriptionScript();
        UnvoicedDialogueAndChoiceFallbackRemainReadable();
    }

    private static void VoicedDialogueDoesNotNeedARecognizedDescriptionScript()
    {
        // A save loaded directly into field 214 has no prior Echo-S description
        // fingerprint. FFNx's successful playback is sufficient voice evidence.
        var voice = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(60), 1000);
        voice.ObserveMessage(214, 0, 2, 100);
        voice.ObserveVoice(new("onna_1", 0, 2, 0, true, 110));
        var speech = Poll(voice, 214, 0);
        Equal(0, speech.Count, "a voiced message must not reach Prism before a recognized script loads");
    }

    private static void UnvoicedDialogueAndChoiceFallbackRemainReadable()
    {
        var voice = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(60), 1000);
        voice.ObserveMessage(214, 0, 2, 100);
        Equal(1, Poll(voice, 214, 0).Count, "missing playback evidence preserves dialogue");

        voice.ObserveVoice(new("onna_1", 0, 2, 0, false, 110));
        Equal(1, Poll(voice, 214, 0).Count, "a missing voice file preserves dialogue");

        voice.ObserveVoice(new("onna_1", 0, 2, 0, true, 110));
        Equal(1, Poll(voice, 214, 0, verifiedVoiceHookInstalled: false).Count,
            "an unavailable verified hook preserves the fallback");
        Equal(1, Poll(voice, 214, 0, hasActiveAsk: true).Count,
            "voice ownership does not discard choices and their prompts");
        Equal(1, Poll(voice, 214, 1).Count, "a different visible window remains readable");
        Equal(1, Poll(voice, 218, 0).Count, "a different current field remains readable");

        voice.ObserveVoice(new("onna_1", 0, 2, 1, false, 120));
        Equal(1, Poll(voice, 214, 0).Count, "an unvoiced next page restores dialogue speech");
    }

    private static IReadOnlyList<FieldVisibleWindowSpeechDispatch> Poll(
        FfnxVoicePlaybackTracker voice,
        int fieldId,
        int windowId,
        bool verifiedVoiceHookInstalled = true,
        bool hasActiveAsk = false)
    {
        var windows = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot[] page = [new(windowId, 2, "Cloud: Let's go.", 0x700040)];
        bool Suppress(FieldVisibleWindowSnapshot window) => voice.ShouldSuppressPolling(
            fieldId, window.WindowId, 150, verifiedVoiceHookInstalled, hasActiveAsk);
        windows.Observe(page, 1, DateTime.UnixEpoch, Suppress);
        return windows.Observe(page, 1, DateTime.UnixEpoch.AddTicks(1), Suppress);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}.");
        }
    }
}
