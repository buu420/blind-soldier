using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime;

internal sealed class Steam2026ResearchAccessibilityOutput : IAccessibilityOutput, IDisposable
{
    private readonly PrismNativeSpeaker speaker;
    private readonly Action<string> log;
    private readonly BlindSoldierLocalizer localizer;
    private readonly ISteam2026MovieNarrationPlayback? movieNarrationPlayback;
    private readonly RepeatLastSpeechController repeatLastSpeechController = new();
    private int disposed;

    internal Steam2026ResearchAccessibilityOutput(
        PrismNativeSpeaker speaker,
        Action<string> log)
        : this(
            speaker,
            BlindSoldierLocalizer.Create(
                Ff7GameLanguages.Get(Ff7GameLanguage.English),
                modDirectory: null),
            log,
            movieNarrationPlayback: null)
    {
    }

    internal Steam2026ResearchAccessibilityOutput(
        PrismNativeSpeaker speaker,
        BlindSoldierLocalizer localizer,
        Action<string> log)
        : this(speaker, localizer, log, movieNarrationPlayback: null)
    {
    }

    internal Steam2026ResearchAccessibilityOutput(
        PrismNativeSpeaker speaker,
        string absoluteOpeningMovieAudioTrackPath,
        int openingMovieAudioTrackVolumePercent,
        Action<string> log)
        : this(
            speaker,
            BlindSoldierLocalizer.Create(
                Ff7GameLanguages.Get(Ff7GameLanguage.English),
                modDirectory: null),
            log,
            new Steam2026MovieNarrationPlayback(
                absoluteOpeningMovieAudioTrackPath,
                openingMovieAudioTrackVolumePercent,
                log))
    {
    }

    internal Steam2026ResearchAccessibilityOutput(
        PrismNativeSpeaker speaker,
        string absoluteOpeningMovieAudioTrackPath,
        int openingMovieAudioTrackVolumePercent,
        BlindSoldierLocalizer localizer,
        Action<string> log)
        : this(
            speaker,
            localizer,
            log,
            new Steam2026MovieNarrationPlayback(
                absoluteOpeningMovieAudioTrackPath,
                openingMovieAudioTrackVolumePercent,
                log))
    {
    }

    internal Steam2026ResearchAccessibilityOutput(
        PrismNativeSpeaker speaker,
        ISteam2026MovieNarrationPlayback movieNarrationPlayback,
        Action<string> log)
        : this(
            speaker,
            BlindSoldierLocalizer.Create(
                Ff7GameLanguages.Get(Ff7GameLanguage.English),
                modDirectory: null),
            log,
            movieNarrationPlayback
            ?? throw new ArgumentNullException(nameof(movieNarrationPlayback)))
    {
    }

    private Steam2026ResearchAccessibilityOutput(
        PrismNativeSpeaker speaker,
        BlindSoldierLocalizer localizer,
        Action<string> log,
        ISteam2026MovieNarrationPlayback? movieNarrationPlayback)
    {
        this.speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        this.localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.movieNarrationPlayback = movieNarrationPlayback;
    }

    public void Speak(string text, bool interrupt)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var localizedText = localizer.Localize(text);
        if (!speaker.Speak(localizedText, interrupt))
        {
            throw new InvalidOperationException("Prism did not accept the speech request.");
        }

        repeatLastSpeechController.RememberDelivered(localizedText);
        log($"Speak: {localizedText}");
    }

    internal bool RepeatLast() =>
        repeatLastSpeechController.Repeat(text =>
        {
            if (!speaker.Speak(text, interrupt: true))
            {
                return false;
            }

            log($"Repeat last speech: {text}");
            return true;
        });

    internal bool TryIsSpeaking(out bool speaking) =>
        speaker.TryIsSpeaking(out speaking);

    public void PlayCue(AccessibilityCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (cue.Kind == AccessibilityCueKind.MovieNarration)
        {
            if (movieNarrationPlayback is null)
            {
                log("Research x64 movie narration playback is not configured.");
                return;
            }

            movieNarrationPlayback.Start("native opening movie started");
            return;
        }

        log($"Research x64 cue {cue.Kind} is not enabled in this live pass.");
    }

    public void StopCue(AccessibilityCueKind kind)
    {
        if (kind == AccessibilityCueKind.MovieNarration)
        {
            movieNarrationPlayback?.Stop("native opening movie stopped or skipped");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        movieNarrationPlayback?.Dispose();
    }
}
