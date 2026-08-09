using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;
using System.Runtime.InteropServices;

internal static class Steam2026ResearchAccessibilityOutputTests
{
    internal static void Run()
    {
        ForwardsMovieNarrationLifecycleAndOwnsPlayback();
        LeavesUnrelatedCuesDisabled();
        RequiresAnAbsoluteNarrationPath();
        ReportsPrismSpeechCompletionWhenTheBackendSupportsIt();
        RepeatsLastDeliveredSpeechWithoutReplacingIt();
        LocalizesBeforePrismAndRepeatStorage();
        NarrationCompletionReleasesDialogueWithoutReprotectingIt();
    }

    private static void ForwardsMovieNarrationLifecycleAndOwnsPlayback()
    {
        using var speaker = CreateUnavailableSpeaker();
        var playback = new RecordingNarrationPlayback();
        var output = new Steam2026ResearchAccessibilityOutput(
            speaker,
            playback,
            _ => { });

        output.PlayCue(new AccessibilityCue(
            AccessibilityCueKind.MovieNarration,
            @"C:\mod\Assets\movies\opening_audio_description.ogg",
            3.0f,
            0.0f,
            false));
        output.StopCue(AccessibilityCueKind.MovieNarration);
        output.Dispose();
        output.Dispose();

        Equal(1, playback.StartReasons.Count, "movie narration start count");
        Equal(1, playback.StopReasons.Count, "movie narration stop count");
        Equal(1, playback.DisposeCount, "movie narration playback disposal count");
    }

    private static void LeavesUnrelatedCuesDisabled()
    {
        using var speaker = CreateUnavailableSpeaker();
        var playback = new RecordingNarrationPlayback();
        using var output = new Steam2026ResearchAccessibilityOutput(
            speaker,
            playback,
            _ => { });

        output.PlayCue(new AccessibilityCue(
            AccessibilityCueKind.Footstep,
            @"C:\mod\Assets\footsteps\step.ogg",
            1.0f,
            0.0f,
            false));
        output.StopCue(AccessibilityCueKind.Footstep);

        Equal(0, playback.StartReasons.Count, "unrelated cue start count");
        Equal(0, playback.StopReasons.Count, "unrelated cue stop count");
    }

    private static void RequiresAnAbsoluteNarrationPath()
    {
        using var speaker = CreateUnavailableSpeaker();
        var threw = false;
        try
        {
            using var output = new Steam2026ResearchAccessibilityOutput(
                speaker,
                @"Assets\movies\opening_audio_description.ogg",
                300,
                _ => { });
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Equal(true, threw, "relative narration path rejection");
    }

    private static void ReportsPrismSpeechCompletionWhenTheBackendSupportsIt()
    {
        var isSpeaking = true;
        PrismError Query(nint _, out bool speaking)
        {
            speaking = isSpeaking;
            return PrismError.Ok;
        }

        using var speaker = new PrismNativeSpeaker(
            _ => { },
            context: (nint)1,
            backend: (nint)1,
            output: (_, _, _) => PrismError.Ok,
            isSpeaking: Query,
            shutdown: _ => { });
        using var output = new Steam2026ResearchAccessibilityOutput(
            speaker,
            _ => { });

        Equal(true, output.TryIsSpeaking(out var active), "Prism speaking query availability");
        Equal(true, active, "active Prism narration state");

        isSpeaking = false;
        Equal(true, output.TryIsSpeaking(out active), "Prism completion query availability");
        Equal(false, active, "completed Prism narration state");
    }

    private static void RepeatsLastDeliveredSpeechWithoutReplacingIt()
    {
        var spoken = new List<(string Text, bool Interrupt)>();
        using var speaker = new PrismNativeSpeaker(
            _ => { },
            context: (nint)1,
            backend: (nint)1,
            output: (_, text, interrupt) =>
            {
                spoken.Add((Marshal.PtrToStringUTF8(text) ?? string.Empty, interrupt));
                return PrismError.Ok;
            },
            shutdown: _ => { });
        using var output = new Steam2026ResearchAccessibilityOutput(speaker, _ => { });

        Equal(false, output.RepeatLast(), "R is silent before x64 speech has been delivered");
        output.Speak("Barret, back row", interrupt: false);
        Equal(true, output.RepeatLast(), "R repeats the last x64 utterance");
        Equal(2, spoken.Count, "normal and repeated x64 speech count");
        Equal("Barret, back row", spoken[1].Text, "x64 repeat text");
        Equal(true, spoken[1].Interrupt, "repeated speech interrupts stale output immediately");

        Equal(true, output.RepeatLast(), "repeating must leave the same utterance available");
        Equal("Barret, back row", spoken[2].Text, "repeat does not replace remembered speech");
    }

    private static void LocalizesBeforePrismAndRepeatStorage()
    {
        var spoken = new List<string>();
        using var speaker = new PrismNativeSpeaker(
            _ => { },
            context: (nint)1,
            backend: (nint)1,
            output: (_, text, _) =>
            {
                spoken.Add(Marshal.PtrToStringUTF8(text) ?? string.Empty);
                return PrismError.Ok;
            },
            shutdown: _ => { });
        var localizer = BlindSoldierLocalizer.Create(
            Ff7GameLanguages.Get(Ff7GameLanguage.French),
            modDirectory: null);
        using var output = new Steam2026ResearchAccessibilityOutput(
            speaker,
            localizer,
            _ => { });

        output.Speak("Route complete.", interrupt: false);
        Equal("Itinéraire terminé.", spoken[0], "localized x64 Prism speech");
        Equal(true, output.RepeatLast(), "localized speech is repeatable");
        Equal("Itinéraire terminé.", spoken[1], "repeat stores localized x64 speech");
    }

    private static void NarrationCompletionReleasesDialogueWithoutReprotectingIt()
    {
        var tracker = new Steam2026CutsceneNarrationSpeechTracker();
        tracker.Begin(fieldId: 133);

        Equal(
            true,
            tracker.ShouldProtectDialogue(
                fieldId: 133,
                estimatedProtection: true,
                speechStateAvailable: true,
                speechIsActive: false),
            "asynchronous Prism start cannot release dialogue prematurely");
        Equal(
            true,
            tracker.ShouldProtectDialogue(
                fieldId: 133,
                estimatedProtection: true,
                speechStateAvailable: true,
                speechIsActive: true),
            "observed active narration protects itself");
        Equal(
            false,
            tracker.ShouldProtectDialogue(
                fieldId: 133,
                estimatedProtection: true,
                speechStateAvailable: true,
                speechIsActive: false),
            "actual narration completion releases Cloud dialogue before the estimate expires");
        Equal(
            false,
            tracker.ShouldProtectDialogue(
                fieldId: 133,
                estimatedProtection: true,
                speechStateAvailable: true,
                speechIsActive: true),
            "Cloud speech cannot be mistaken for a restarted narration");

        tracker.Begin(fieldId: 133);
        Equal(
            true,
            tracker.ShouldProtectDialogue(
                fieldId: 133,
                estimatedProtection: true,
                speechStateAvailable: false,
                speechIsActive: false),
            "unsupported backend speech state retains the bounded timer fallback");
    }

    private static PrismNativeSpeaker CreateUnavailableSpeaker() =>
        new(
            _ => { },
            context: 0,
            backend: 0,
            (_, _, _) => PrismError.Ok,
            _ => { });

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class RecordingNarrationPlayback : ISteam2026MovieNarrationPlayback
    {
        internal List<string> StartReasons { get; } = [];

        internal List<string> StopReasons { get; } = [];

        internal int DisposeCount { get; private set; }

        public bool Start(string reason)
        {
            StartReasons.Add(reason);
            return true;
        }

        public bool Stop(string reason)
        {
            StopReasons.Add(reason);
            return true;
        }

        public void Dispose() => DisposeCount++;
    }
}
