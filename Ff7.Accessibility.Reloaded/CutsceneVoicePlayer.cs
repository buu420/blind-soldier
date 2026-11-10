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

    /// <summary>An observed field creature, stopped when its native presentation disappears.</summary>
    FieldCreature,

    /// <summary>
    /// A field description that was already being heard when the story moved on through
    /// a scripted transition: the name-entry screen a script opens, or the script taking
    /// the party on to the next field. It no longer belongs to the field, so a field
    /// reset does not end it. It finishes, and only a discontinuity ends it sooner -
    /// another module, a film taking the device for its own recording, or the host.
    /// </summary>
    SceneTransition,
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
    /// <summary>
    /// The most a description may still have to say when the story moves on and it is
    /// let finish. Every recorded description is shorter - the longest runs a little over
    /// fourteen seconds - so this only ever refuses a clip that is not a description.
    /// </summary>
    public static readonly TimeSpan MaximumTransitionTail = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long past its declared length a clip may still report playing and be treated
    /// as finishing, which covers the device's own start-up latency. Later than that it
    /// is stuck, not finishing, and is not carried anywhere.
    /// </summary>
    public static readonly TimeSpan TransitionOverrunAllowance = TimeSpan.FromSeconds(2);

    private readonly CutsceneVoiceManifest manifest;
    private readonly Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> createOutput;
    private readonly Func<bool> anotherDescriptionIsPlaying;
    private readonly Func<DateTime> utcNow;
    private readonly Action<string> log;
    private readonly object sync = new();

    private IFieldMovieNarrationOutput? output;
    private CutsceneVoiceOwner playingOwner;
    private TimeSpan playingDuration;
    private DateTime playingStartedUtc;

    /// <param name="anotherDescriptionIsPlaying">
    /// Whether a film's own recording currently owns the independent device. A field
    /// description started over a playing film would be two descriptions at once, so
    /// this refuses and lets the caller speak instead.
    /// </param>
    /// <param name="utcNow">
    /// The clock a playing clip's progress is measured on. Only the time left in the
    /// clip is read from it, when deciding whether a sentence may finish across a scene
    /// transition.
    /// </param>
    public CutsceneVoicePlayer(
        CutsceneVoiceManifest manifest,
        Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> createOutput,
        Action<string> log,
        Func<bool>? anotherDescriptionIsPlaying = null,
        Func<DateTime>? utcNow = null)
    {
        this.manifest = manifest ?? CutsceneVoiceManifest.Empty;
        this.createOutput = createOutput;
        this.log = log;
        this.anotherDescriptionIsPlaying = anotherDescriptionIsPlaying ?? (static () => false);
        this.utcNow = utcNow ?? (static () => DateTime.UtcNow);
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

    /// <summary>Whether a description is finishing across a scene transition right now.</summary>
    public bool IsContinuingAcrossTransition
    {
        get
        {
            lock (sync)
            {
                return output?.IsPlaying == true &&
                    playingOwner == CutsceneVoiceOwner.SceneTransition;
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
            playingDuration = clip.Duration;
            playingStartedUtc = utcNow();
            duration = clip.Duration;
            return CutsceneVoiceResult.Played;
        }
    }

    /// <summary>
    /// Lets the field description already being heard finish across a scripted scene
    /// transition instead of being cut off by it.
    ///
    /// <para>Only a <see cref="CutsceneVoiceOwner.FieldAction"/> clip that is still
    /// playing, inside its own declared length, with no more than
    /// <see cref="MaximumTransitionTail"/> left to say, is handed over. From then on it
    /// belongs to <see cref="CutsceneVoiceOwner.SceneTransition"/>, which no field reset
    /// stops. Asking again for a clip already handed over changes nothing.</para>
    ///
    /// <para>Nothing is stopped here. A clip that does not qualify keeps its owner, and
    /// the caller's own field reset ends it exactly as it always did.</para>
    /// </summary>
    /// <param name="transition">What the story moved on to, for the log.</param>
    /// <returns>True when a description is now finishing across the transition.</returns>
    public bool ContinueAcrossTransition(string transition)
    {
        lock (sync)
        {
            if (output?.IsPlaying != true)
            {
                return false;
            }

            if (playingOwner == CutsceneVoiceOwner.SceneTransition)
            {
                return true;
            }

            if (playingOwner != CutsceneVoiceOwner.FieldAction || playingDuration <= TimeSpan.Zero)
            {
                return false;
            }

            var elapsed = utcNow() - playingStartedUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            var remaining = playingDuration - elapsed;
            if (remaining > MaximumTransitionTail ||
                elapsed > playingDuration + TransitionOverrunAllowance)
            {
                return false;
            }

            playingOwner = CutsceneVoiceOwner.SceneTransition;
            log($"Cutscene description continues across {transition}: " +
                $"{Math.Max(0d, remaining.TotalSeconds):0.##}s left.");
            return true;
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

/// <summary>
/// What becomes of a field description already being heard when the field module hands
/// the screen to another module. Both runtimes make this one decision, on every tick the
/// field module is not current.
///
/// <para>The script's own name-entry screen is the one module a sentence may finish
/// over. It is static, it waits for its player, and the field resumes under it
/// afterwards; its words wait on the same device-backed hold the game's dialogue already
/// uses. Every other module - the player's menu, a battle, the world map, the title
/// screen a load passes through - is a different scene, or one that will not wait, and
/// it ends the description as it always has, including one that was finishing across an
/// earlier transition.</para>
/// </summary>
public static class CutsceneVoiceTransition
{
    /// <summary>
    /// The field module's request at 0x00CC0D89 for the name-entry screen. The same byte
    /// is read as <see cref="FieldAudibleCueStateReader.AddressFieldMovieCommand"/>.
    ///
    /// <para>Ghidra, installed ff7_en.exe: the MENU opcode handler FUN_0061F04A takes
    /// the opcode's menu type; the field driver FUN_0063C17F switches the module to 5
    /// for requests 6, 7, 8, 9, 0x0E, 0x12 and 0x13, and forces request 9 when the
    /// player opens the menu; the menu driver FUN_006CDA83 runs name entry
    /// (FUN_006CBD32, FUN_00719C08) for request 6 and nothing else. The request is
    /// written before the module changes, so it is already there on the first tick of
    /// the screen - unlike the editor state at 0x00DD45E8, which reads 1 only after
    /// the screen has faded in.</para>
    /// </summary>
    public const int ScriptedNameEntryRequest = 6;

    /// <summary>Whether the field module has handed the screen to the script's name entry.</summary>
    public static bool IsScriptedNameEntry(int module, int fieldModuleRequest) =>
        module == NameEntryStateReader.NameEntryModule &&
        fieldModuleRequest == ScriptedNameEntryRequest;

    /// <summary>
    /// Lets the description finish over the script's name-entry screen, and ends one that
    /// was finishing across an earlier transition whenever any other module takes over.
    /// The caller's field reset runs afterwards, unchanged, and ends a field description
    /// that was not handed over.
    /// </summary>
    /// <param name="fieldModuleRequest">
    /// The byte at 0x00CC0D89, or -1 when it could not be read; an unknown request is
    /// never name entry.
    /// </param>
    public static void LeaveFieldModule(CutsceneVoicePlayer? player, int module, int fieldModuleRequest)
    {
        if (player is null)
        {
            return;
        }

        if (IsScriptedNameEntry(module, fieldModuleRequest))
        {
            player.ContinueAcrossTransition("the name-entry screen");
            return;
        }

        player.StopIfOwnedBy(
            CutsceneVoiceOwner.SceneTransition,
            $"module {module} replaced the scene");
    }
}
