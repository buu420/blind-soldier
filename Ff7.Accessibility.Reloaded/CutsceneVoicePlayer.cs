namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Which lifetime a recorded description belongs to, and therefore what stops it.
///
/// <para>A field description belongs to the field: the player walking out of the room
/// ends it. A film's timed cue belongs to the film: it survives a field change while
/// the same film keeps running, and dies with the film. Getting these the wrong way
/// round leaves a sentence about one scene playing over another.</para>
/// </summary>
public enum CutsceneVoiceOwner
{
    /// <summary>A description anchored to a field script.</summary>
    FieldAction,

    /// <summary>One timed cue of a film whose recording gave way to dialogue.</summary>
    FilmCue,
}

/// <summary>What happened when a description was offered to the recorded voice.</summary>
public enum CutsceneVoiceResult
{
    /// <summary>
    /// Nothing recorded says this, or the file, the device or the settings will not
    /// produce it. The caller should speak the words instead - saying them in the
    /// wrong voice is far better than not saying them.
    /// </summary>
    Unavailable,

    /// <summary>The recording is playing. The caller must not also speak.</summary>
    Played,

    /// <summary>
    /// A recording exists but the device is busy with another description. The caller
    /// must neither speak nor consume the cue: it is offered again on a later tick,
    /// in the voice it was meant to have. Speaking it now would put a second voice
    /// over the first and spend the cue in the wrong one.
    /// </summary>
    Busy,
}

/// <summary>
/// Speaks a scene description in the recorded voice instead of the screen reader,
/// when a recording of those exact words exists.
///
/// <para>This sits in front of the ordinary speech path rather than replacing it. It
/// decides only <em>how</em> a description is said; when one is said, and whether it
/// yields to the game's own dialogue, is unchanged and still belongs to the tracker
/// and the delivery queue above it. Anything that would make the recording the wrong
/// answer - no clip, no file, the feature switched off, a device that will not open
/// or refuses to start - reports <see cref="CutsceneVoiceResult.Unavailable"/>, and
/// the caller falls back to speech exactly as it does today.</para>
///
/// <para>Being busy is a different answer. One description plays at a time, and a
/// second one arriving while the first is still speaking reports
/// <see cref="CutsceneVoiceResult.Busy"/> so the caller keeps it queued. Falling back
/// to speech there would put two voices over each other and spend the cue in the
/// wrong one.</para>
/// </summary>
public sealed class CutsceneVoicePlayer : IDisposable
{
    private readonly CutsceneVoiceManifest manifest;
    private readonly Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> createOutput;
    private readonly Func<bool> anotherDescriptionIsPlaying;
    private readonly Action<string> log;
    private readonly object sync = new();

    private IFieldMovieNarrationOutput? output;
    private CutsceneVoiceOwner playingOwner;

    /// <param name="anotherDescriptionIsPlaying">
    /// Whether a film's own recording currently owns the independent device. A field
    /// description started over a playing film would be two descriptions at once, so
    /// this refuses and lets the caller speak instead.
    /// </param>
    public CutsceneVoicePlayer(
        CutsceneVoiceManifest manifest,
        Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> createOutput,
        Action<string> log,
        Func<bool>? anotherDescriptionIsPlaying = null)
    {
        this.manifest = manifest ?? CutsceneVoiceManifest.Empty;
        this.createOutput = createOutput;
        this.log = log;
        this.anotherDescriptionIsPlaying = anotherDescriptionIsPlaying ?? (static () => false);
    }

    /// <summary>How many descriptions have a recording.</summary>
    public int RecordedDescriptions => manifest.Count;

    public bool IsPlaying
    {
        get
        {
            lock (sync)
            {
                return output?.IsPlaying == true;
            }
        }
    }

    /// <summary>
    /// Plays the recording of <paramref name="text"/> if there is one.
    ///
    /// <para>Returns false for every reason a recording cannot be the answer, and the
    /// caller then speaks the same words. True means the words are being said, and
    /// <paramref name="duration"/> is the clip's own length, so the caller can hold
    /// the dialogue window for exactly as long as the description actually lasts
    /// rather than for a guess made from its word count.</para>
    /// </summary>
    public CutsceneVoiceResult TrySpeak(
        string? text,
        CutsceneVoiceOwner owner,
        out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text) || !manifest.TryGet(text, out var clip))
        {
            return CutsceneVoiceResult.Unavailable;
        }

        lock (sync)
        {
            // Busy is not unavailable. There is a recording for these words, so
            // speaking them now would be the wrong voice *and* a second one over the
            // first; the caller keeps the cue and offers it again.
            if (output?.IsPlaying == true)
            {
                return CutsceneVoiceResult.Busy;
            }

            if (anotherDescriptionIsPlaying())
            {
                log("Cutscene voice waiting: a film recording has the device.");
                return CutsceneVoiceResult.Busy;
            }

            IFieldMovieNarrationOutput? candidate;
            try
            {
                candidate = createOutput(clip);
            }
            catch (Exception ex)
            {
                log($"Cutscene voice output could not be created ({clip.FileName}): {ex.Message}");
                return CutsceneVoiceResult.Unavailable;
            }

            if (candidate is null)
            {
                return CutsceneVoiceResult.Unavailable;
            }

            bool started;
            try
            {
                started = candidate.Start($"cutscene description ({clip.Duration.TotalSeconds:0.##}s)");
            }
            catch (Exception ex)
            {
                log($"Cutscene voice failed to start ({clip.FileName}): {ex.Message}");
                SafeDispose(candidate);
                return CutsceneVoiceResult.Unavailable;
            }

            if (!started)
            {
                SafeDispose(candidate);
                return CutsceneVoiceResult.Unavailable;
            }

            SafeDispose(output);
            output = candidate;
            playingOwner = owner;
            duration = clip.Duration;
            return CutsceneVoiceResult.Played;
        }
    }

    /// <summary>
    /// Stops whatever is playing. Called for the same reasons a film recording stops:
    /// the game started talking, the field or module changed, the window lost the
    /// foreground, or the mod is going away.
    /// </summary>
    public void Stop(string reason)
    {
        lock (sync)
        {
            if (output is null)
            {
                return;
            }

            try
            {
                output.Stop(reason);
            }
            catch (Exception ex)
            {
                log($"Cutscene voice could not be stopped cleanly: {ex.Message}");
            }

            SafeDispose(output);
            output = null;
        }
    }

    /// <summary>
    /// Stops the playing clip only if this lifetime owns it. A field change ends a
    /// field description; it does not end a film's cue, because the film may be
    /// running on into the next field.
    /// </summary>
    public void StopIfOwnedBy(CutsceneVoiceOwner owner, string reason)
    {
        lock (sync)
        {
            if (output is null || playingOwner != owner)
            {
                return;
            }
        }

        Stop(reason);
    }

    public void Dispose() => Stop("unloaded");

    private void SafeDispose(IFieldMovieNarrationOutput? candidate)
    {
        try
        {
            candidate?.Dispose();
        }
        catch (Exception ex)
        {
            log($"Cutscene voice output could not be released: {ex.Message}");
        }
    }
}
