namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// A described native film. The anchor is the exact field script opcode that starts
/// the film, and the movie number is the native film identity, because one field can
/// start several films from different bytes and only one of them carries narration.
/// </summary>
public readonly record struct FieldMovieNarrationTrack(
    int FieldId,
    int EntityId,
    int ScriptId,
    int ByteIndex,
    int MovieNumber,
    string FileName,
    string Label,
    double DurationSeconds);

/// <summary>
/// One observation of the native film state. The movie number is what actually
/// distinguishes two films started from the same field in the same module.
/// </summary>
/// <param name="MovieHandlerState">
/// The MOVIE opcode handler's own state byte, or -1 when it could not be read.
/// FUN_0061A321 treats 0 as a fresh entry and 4 as re-entering a film that is
/// already running, so this is what separates a new film from the same opcode
/// arriving again on every frame of the film it already started.
/// </param>
/// <param name="MovieHandlerPhase">
/// The handler's phase word when the state is 4: 1 while it yields and 2 on the
/// completion that advances the script pointer. -1 when it could not be read.
/// </param>
public readonly record struct FieldMovieNarrationSample(
    bool MovieActive,
    int MovieNumber,
    int CurrentModule,
    int CurrentFieldId,
    int MovieHandlerState = -1,
    int MovieHandlerPhase = -1);

public enum FieldMovieNarrationStopReason
{
    None,
    MovieEnded,
    OtherMovieStarted,
    FieldChanged,
    ModuleChanged,
    Unloaded,
    Suspended
}

/// <summary>
/// Chooses which native film start carries an independent narration track, and what
/// counts as the same film episode.
/// </summary>
public static class FieldMovieNarrationPolicy
{
    // Ghidra, installed ff7_en.exe: FUN_0040B27B is the native film step. At
    // 0x0040B2FC it pushes the current movie number from 0x00CC0D8A into the
    // movie-open call FUN_0040ADB0, sets the film state 0x00CC0DAE to 1, and drives
    // the active flag 0x00CC1638. The movie table at 0x007BAE80 maps movie 40 to
    // gold1.avi. Installed field 496 prepares that film with F8 28 at byte 185 and
    // starts it with F9 at byte 190; the F9 at byte 201 starts the docking film
    // prepared at byte 196 with F8 04, which is movie number 4, so the film identity
    // alone separates them even though both share the field and the module.
    public static readonly FieldMovieNarrationTrack GoldSaucerArrival = new(
        FieldId: 496,
        EntityId: 0,
        ScriptId: 0,
        ByteIndex: 190,
        MovieNumber: 40,
        FileName: "gold1_audio_description.ogg",
        Label: "Gold Saucer arrival",
        DurationSeconds: 45.0d);

    // The Round Square gondola films. Installed bwhlin (489) and bwhlin2 (490) run
    // them from entity 0 `dic`, script 0, and each film has two anchors because the
    // ride's script forks on which companion came along; only one branch runs per
    // ride. Every anchor is an F9 immediately after the F8 that names the film, and
    // the numbers are the installed table at 0x007BAE80: 6 gold2, 7 gold3, 8 gold4,
    // 9 gold6, 10 gold5. Note that 9 and 10 are not in file-name order, which is
    // exactly why the anchor carries the number rather than the file name.
    //
    // gold7 (48) and gold7_2 (49) are deliberately absent: they are the date scene
    // at the end of the evening, not a first-visit gondola view, and no reviewed
    // recording exists for them.
    //
    // Each track runs for the film's own length, measured from the installed AVI and
    // matched by the recording: gold2 13.133 s, gold3 11.200 s, gold4 32.133 s,
    // gold5 10.133 s, gold6 9.933 s.
    private const int Bwhlin = 489;
    private const int Bwhlin2 = 490;

    public static readonly FieldMovieNarrationTrack GondolaSpeedSquare = new(
        Bwhlin, 0, 0, 155, 6, "gold2_audio_description.ogg",
        "Round Square gondola, Speed Square", 13.133d);

    public static readonly FieldMovieNarrationTrack GondolaSpeedSquareAlternate =
        GondolaSpeedSquare with { ByteIndex = 345 };

    public static readonly FieldMovieNarrationTrack GondolaChocoboSquare = new(
        Bwhlin, 0, 0, 210, 7, "gold3_audio_description.ogg",
        "Round Square gondola, Chocobo Square", 11.200d);

    public static readonly FieldMovieNarrationTrack GondolaChocoboSquareAlternate =
        GondolaChocoboSquare with { ByteIndex = 429 };

    public static readonly FieldMovieNarrationTrack GondolaParkAndStatue = new(
        Bwhlin, 0, 0, 288, 8, "gold4_audio_description.ogg",
        "Round Square gondola, the park and the statue", 32.133d);

    public static readonly FieldMovieNarrationTrack GondolaParkAndStatueAlternate =
        GondolaParkAndStatue with { ByteIndex = 612 };

    public static readonly FieldMovieNarrationTrack GondolaGhostSquare = new(
        Bwhlin2, 0, 0, 91, 10, "gold5_audio_description.ogg",
        "Round Square gondola, Ghost Square", 10.133d);

    public static readonly FieldMovieNarrationTrack GondolaGhostSquareAlternate =
        GondolaGhostSquare with { ByteIndex = 223 };

    public static readonly FieldMovieNarrationTrack GondolaEventSquare = new(
        Bwhlin2, 0, 0, 146, 9, "gold6_audio_description.ogg",
        "Round Square gondola, Event Square", 9.933d);

    public static readonly FieldMovieNarrationTrack GondolaEventSquareAlternate =
        GondolaEventSquare with { ByteIndex = 307 };

    public static IReadOnlyList<FieldMovieNarrationTrack> All { get; } =
    [
        GoldSaucerArrival,
        GondolaSpeedSquare,
        GondolaSpeedSquareAlternate,
        GondolaChocoboSquare,
        GondolaChocoboSquareAlternate,
        GondolaParkAndStatue,
        GondolaParkAndStatueAlternate,
        GondolaGhostSquare,
        GondolaGhostSquareAlternate,
        GondolaEventSquare,
        GondolaEventSquareAlternate
    ];

    /// <summary>
    /// How long a start opportunity survives after its opcode *first* ran. The
    /// description path can defer delivery behind dialogue, and a track that begins
    /// from zero well into a 45-second film would describe the wrong seconds of it.
    ///
    /// This is measured from the first ingress and never refreshed, because the
    /// native F9 handler yields without advancing the script pointer while a film is
    /// running (0061A321: state 4 with field context +0x26 == 1 returns without
    /// advancing 00CC0CF8, and only +0x26 == 2 advances it). The same opcode
    /// therefore arrives on every movie frame, and a window refreshed on each of
    /// those repeats would never close.
    /// </summary>
    public static readonly TimeSpan StartWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long delivery may be deferred while waiting for the engine to raise its
    /// own active flag. The opcode hook can run a frame or two ahead of it, so the
    /// description is held rather than spoken; past this bound the ordinary
    /// paragraph is spoken instead so the scene is never left undescribed.
    /// </summary>
    public static readonly TimeSpan PreActivationWindow = TimeSpan.FromSeconds(1);

    public static bool TryResolve(
        int fieldId,
        int entityId,
        int scriptId,
        int byteIndex,
        out FieldMovieNarrationTrack track)
    {
        foreach (var candidate in All)
        {
            if (candidate.FieldId == fieldId &&
                candidate.EntityId == entityId &&
                candidate.ScriptId == scriptId &&
                candidate.ByteIndex == byteIndex)
            {
                track = candidate;
                return true;
            }
        }

        track = default;
        return false;
    }

    // FUN_0061A321, the native MOVIE opcode handler: at 0x0061A36C it loads the field
    // script context from 0x00CBF9D8 and reads the state byte at +0x01. State 0 takes
    // the fresh-entry branch at 0x0061A419. State 4 takes 0x0061A393, which reads the
    // phase word at +0x26: phase 1 returns without advancing the script pointer at
    // 0x00CC0CF8, and phase 2 clears the state and the phase and advances it. So state
    // 0 is the only value that means "this opcode is starting a film now"; state 4 is
    // the handler meeting the same opcode again on a later frame of the film it has
    // already started.
    public const int MovieHandlerStateFreshEntry = 0;
    public const int MovieHandlerStateInProgress = 4;
    public const int MovieHandlerPhaseYielding = 1;
    public const int MovieHandlerPhaseCompleting = 2;
    public const int MovieHandlerStateUnknown = -1;

    /// <summary>
    /// True when the handler is entering the MOVIE opcode for a new film, false when
    /// it is re-entering one already in progress, and null when the state could not be
    /// read - in which case the caller falls back to its own bookkeeping, because
    /// guessing either way would be wrong half of the time.
    /// </summary>
    public static bool? IsFreshNativeStart(FieldMovieNarrationSample sample) =>
        sample.MovieHandlerState switch
        {
            MovieHandlerStateFreshEntry => true,
            MovieHandlerStateInProgress => false,
            < 0 => null,
            // No other state reaches the film-start branch either.
            _ => false
        };

    /// <summary>
    /// True on the completion pass that ends the film and lets the script move on.
    /// This is what releases a hold taken because an earlier film had not started yet.
    /// </summary>
    public static bool IsNativeCompletion(FieldMovieNarrationSample sample) =>
        sample.MovieHandlerState == MovieHandlerStateInProgress &&
        sample.MovieHandlerPhase == MovieHandlerPhaseCompleting;

    public static FieldMovieNarrationStopReason ResolveStopReason(
        FieldMovieNarrationSample sample,
        FieldMovieNarrationTrack track,
        int fieldModule)
    {
        if (sample.CurrentModule != fieldModule)
        {
            return FieldMovieNarrationStopReason.ModuleChanged;
        }

        if (sample.CurrentFieldId != track.FieldId)
        {
            return FieldMovieNarrationStopReason.FieldChanged;
        }

        if (!sample.MovieActive)
        {
            return FieldMovieNarrationStopReason.MovieEnded;
        }

        // Same field, same module, film still running - but a different film. The
        // docking film in field 496 reaches exactly this branch.
        return sample.MovieNumber == track.MovieNumber
            ? FieldMovieNarrationStopReason.None
            : FieldMovieNarrationStopReason.OtherMovieStarted;
    }
}
