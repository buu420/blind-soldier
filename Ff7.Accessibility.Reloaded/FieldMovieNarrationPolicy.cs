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
    double DurationSeconds)
{
    /// <summary>
    /// The game's own file for the film this recording describes, which is what the
    /// live film number has to resolve to before the recording may play. The
    /// recording is named after it: <c>mkup_audio_description.ogg</c> describes
    /// <c>mkup.avi</c>.
    /// </summary>
    public string FilmFileName =>
        FileName.EndsWith("_audio_description.ogg", StringComparison.Ordinal)
            ? FileName[..^"_audio_description.ogg".Length] + ".avi"
            : FileName;
}

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
/// <param name="MovieCommand">
/// The field module's command byte at 0x00CC0D89, or -1 when it could not be read.
/// Verified as a byte: every access in the executable is <c>byte ptr</c>. The movie
/// step FUN_0040B27B opens the film when this reads 3 and starts it when it reads 4,
/// and 0x14 stops it. This matters because <paramref name="MovieNumber"/> is not a
/// movie field - it is the shared argument word at 0x00CC0D8A that whichever command
/// is current owns. Reading it while the command is something else yields whatever
/// that command's argument happens to be.
/// </param>
/// <param name="MoviesSkipped">
/// The skip gate at 0x00CC0B68, verified as a byte and read by all three movie
/// opcode handlers. When it is non-zero PMVIE and MOVIE advance the script without
/// playing anything and MVIEF returns a synthetic frame counter, so the player is
/// shown no film and must not be told about one.
/// </param>
public readonly record struct FieldMovieNarrationSample(
    bool MovieActive,
    int MovieNumber,
    int CurrentModule,
    int CurrentFieldId,
    int MovieHandlerState = -1,
    int MovieHandlerPhase = -1,
    int Disc = MovieFilmNameResolver.DiscUnknown,
    int MovieCommand = FieldMovieNarrationSample.CommandUnknown,
    int MoviesSkipped = 0,
    int MovieFrame = FieldMovieNarrationSample.FrameUnknown)
{
    /// <summary>The command byte could not be read.</summary>
    public const int CommandUnknown = -1;

    /// <summary>The film's own frame counter could not be read.</summary>
    public const int FrameUnknown = -1;

    /// <summary>
    /// Every source film these recordings describe runs at 15 frames a second -
    /// all one hundred of them, measured with ffprobe, every one reporting 15/1.
    /// FFNx divides its own counter by ceil(fps/15), which is 1 at that rate, so the
    /// counter is in film frames either way.
    /// </summary>
    public const double FramesPerSecond = 15d;

    /// <summary>
    /// How far into the film the engine is, in seconds, or null when the frame could
    /// not be read. This is the film's own clock: it does not advance while the game
    /// is paused and it does not run on after the film stops, which is why nothing
    /// here is timed off the wall clock.
    /// </summary>
    public double? PositionSeconds =>
        MovieFrame < 0 ? null : MovieFrame / FramesPerSecond;

    /// <summary>FUN_0040B27B opens the prepared film on this command.</summary>
    public const int CommandOpenMovie = 3;

    /// <summary>And starts playing it on this one.</summary>
    public const int CommandStartMovie = 4;

    /// <summary>And stops it on this one.</summary>
    public const int CommandStopMovie = 0x14;

    /// <summary>
    /// Whether the argument word may be read as a film number right now. Only the
    /// two movie commands own it; under anything else, including an unreadable
    /// command byte, the number belongs to something else and is stale.
    /// </summary>
    public bool MovieNumberIsCurrent =>
        MovieCommand is CommandOpenMovie or CommandStartMovie;

    /// <summary>
    /// Whether a film is genuinely being shown. A skipped film raises no picture, so
    /// there is nothing to describe even if the flags otherwise look like playback.
    /// </summary>
    public bool FilmIsOnScreen => MovieActive && MoviesSkipped == 0;

    /// <summary>
    /// Whether a film is really playing, as opposed to being prepared.
    /// FUN_0040B27B opens the file on command 3 and drives the active flag on
    /// command 4, so command 3 with the previous film's flag still up is the engine
    /// between films - the right moment to name the next one, and the wrong moment to
    /// start describing it.
    /// </summary>
    public bool FilmIsPlaying => FilmIsOnScreen && MovieCommand == CommandStartMovie;

    /// <summary>
    /// Everything a description needs before it may run: a film really on screen, an
    /// argument word that is really a film number, and a position in the film. Each
    /// of these is read separately and each can fail on its own, so this is the one
    /// place that says what "we can still see what is happening" means.
    /// </summary>
    public bool PlaybackIsVerified => FilmIsPlaying && MovieFrame >= 0;
}

public enum FieldMovieNarrationStopReason
{
    None,
    MovieEnded,
    OtherMovieStarted,
    FieldChanged,
    ModuleChanged,
    Unloaded,
    Suspended,

    /// <summary>
    /// A native dialogue window opened while the recording was playing. The game's
    /// own words are the scene at that moment - Bugenhagen's Study of Planet Life
    /// runs its explanation as ordinary field text over the film - and two voices at
    /// once is worse than one description cut short.
    /// </summary>
    DialogueOpened
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

    public static IReadOnlyList<FieldMovieNarrationTrack> All { get; } = CreateAll();

    // The reviewed films: 47 of them across 81 installed anchors, each with its own
    // recording. Every field, entity, script and byte is an F9 MOVIE opcode boundary
    // in the installed flevel whose preceding F8 PMVIE names the film number on the
    // same row, and every length is the installed film's own. The developer film-test
    // rooms blackbg3/4/6/7/9/b are excluded: they anchor nearly every film in the game
    // and none of them is reachable in play. A film replayed later shares one
    // recording, which is why a file name repeats across rows.
    private static IReadOnlyList<FieldMovieNarrationTrack> CreateAll()
    {
        static FieldMovieNarrationTrack Film(
            int fieldId, int entityId, int scriptId, int byteIndex,
            int movieNumber, string stem, string label, double durationSeconds) =>
            new(fieldId, entityId, scriptId, byteIndex, movieNumber,
                stem + "_audio_description.ogg", label, durationSeconds);

        return
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
            GondolaEventSquareAlternate,

            // 2 d_ropego.avi, 9.133s: 457 ropest.
            Film(457, 2, 3, 109, 2, "d_ropego", "D ropego", 9.133d),
            Film(457, 2, 4, 16, 2, "d_ropego", "D ropego", 9.133d),
            // 3 d_ropein.avi, 10.8s: 457 ropest.
            Film(457, 1, 0, 85, 3, "d_ropein", "D ropein", 10.800d),
            // 4 u_ropein.avi, 9.867s: 496 gldst.
            Film(496, 0, 0, 201, 4, "u_ropein", "U ropein", 9.867d),
            // 5 u_ropego.avi, 9.133s: 496 gldst.
            Film(496, 13, 5, 21, 5, "u_ropego", "U ropego", 9.133d),
            // 13 junair_u.avi, 4.267s: 384 junair.
            Film(384, 0, 3, 73, 13, "junair_u", "Junair u", 4.267d),
            // 14 junair_d.avi, 4.267s: 384 junair.
            Film(384, 0, 3, 201, 14, "junair_d", "Junair d", 4.267d),
            // 15 junelein.avi, 6.4s: 391 junele2.
            Film(391, 2, 1, 5, 15, "junelein", "Junelein", 6.400d),
            // 16 junelego.avi, 6.4s: 391 junele2.
            Film(391, 2, 2, 19, 16, "junelego", "Junelego", 6.400d),
            // 17 junin_in.avi, 7.4s: 395 junin7.
            Film(395, 3, 2, 8, 17, "junin_in", "Junin in", 7.400d),
            // 18 junin_go.avi, 7.4s: 395 junin7.
            Film(395, 3, 1, 19, 18, "junin_go", "Junin go", 7.400d),
            // 20 mkup.avi, 9s: 117 md1_1, 695 gaia_32.
            Film(117, 0, 0, 143, 20, "mkup", "Mkup", 9.000d),
            Film(695, 1, 3, 19, 20, "mkup", "Mkup", 9.000d),
            // 21 northmk.avi, 13.4s: 119 nrthmk, 706 trnad_51.
            Film(119, 0, 3, 17, 21, "northmk", "Northmk", 13.400d),
            Film(706, 3, 3, 266, 21, "northmk", "Northmk", 13.400d),
            // 22 mk8.avi, 3.467s: 133 md8_1, 707 trnad_52, 777 las4_42.
            Film(133, 0, 0, 78, 22, "mk8", "Mk8", 3.467d),
            Film(707, 1, 1, 427, 22, "mk8", "Mk8", 3.467d),
            Film(777, 0, 0, 51, 22, "mk8", "Mk8", 3.467d),
            // 23 ontrain.avi, 12.8s: 137 md8brdg, 706 trnad_51, 708 trnad_53.
            Film(137, 0, 3, 452, 23, "ontrain", "Ontrain", 12.800d),
            Film(706, 3, 3, 243, 23, "ontrain", "Ontrain", 12.800d),
            Film(708, 2, 3, 21, 23, "ontrain", "Ontrain", 12.800d),
            // 24 mainplr.avi, 13.133s: 139 tin_1, 143 rootmap, 706 trnad_51, 708 trnad_53.
            Film(139, 17, 1, 5, 24, "mainplr", "Mainplr", 13.133d),
            Film(143, 5, 1, 5, 24, "mainplr", "Mainplr", 13.133d),
            Film(706, 3, 3, 285, 24, "mainplr", "Mainplr", 13.133d),
            Film(708, 2, 3, 40, 24, "mainplr", "Mainplr", 13.133d),
            // 25 smk.avi, 2.4s: 127 southmk2, 400 junbin3, 763 las4_0.
            Film(127, 2, 6, 69, 25, "smk", "Smk", 2.400d),
            Film(400, 0, 3, 5, 25, "smk", "Smk", 2.400d),
            Film(763, 0, 0, 714, 25, "smk", "Smk", 2.400d),
            // 26 southmk.avi, 16.133s: 127 southmk2, 399 junbin22.
            Film(127, 2, 7, 84, 26, "southmk", "Southmk", 16.133d),
            Film(399, 0, 0, 359, 26, "southmk", "Southmk", 16.133d),
            // 27 plrexp.avi, 15.333s: 160 pillar_3, 399 junbin22.
            Film(160, 1, 3, 187, 27, "plrexp", "Plrexp", 15.333d),
            Film(399, 0, 0, 389, 27, "plrexp", "Plrexp", 15.333d),
            // 28 fallpl.avi, 53.333s: 160 pillar_3, 399 junbin22.
            Film(160, 11, 3, 2, 28, "fallpl", "Fallpl", 53.333d),
            Film(399, 0, 0, 171, 28, "fallpl", "Fallpl", 53.333d),
            // 29 monitor.avi, 7.533s: 240 blin60_2, 402 junbin5.
            Film(240, 8, 0, 15, 29, "monitor", "Monitor", 7.533d),
            Film(402, 0, 0, 81, 29, "monitor", "Monitor", 7.533d),
            // 30 bike.avi, 50.067s: 411 junone2.
            Film(411, 13, 3, 190, 30, "bike", "Bike", 50.067d),
            // The first play, in the Midgar highway field: the director script
            // calls EIGA's prepare script and then its play script.
            Film(234, 36, 4, 0, 30, "bike", "Bike", 50.067d),
            // 31 mtnvl.avi, 20.133s: 311 mtnvl2, 402 junbin5.
            Film(311, 0, 0, 207, 31, "mtnvl", "Mtnvl", 20.133d),
            Film(402, 3, 13, 133, 31, "mtnvl", "Mtnvl", 20.133d),
            // 32 mtnvl2.avi, 11.533s: 416 junone7.
            Film(416, 0, 3, 5, 32, "mtnvl2", "Mtnvl2", 11.533d),
            // 33 brgnvl.avi, 13.4s: 312 mtnvl3, 356 convil_2.
            Film(312, 10, 3, 106, 33, "brgnvl", "Brgnvl", 13.400d),
            Film(356, 11, 11, 260, 33, "brgnvl", "Brgnvl", 13.400d),
            // 34 nvlmk.avi, 21.133s: 323 nvmkin21, 729 zcoal_2, 730 zcoal_3.
            Film(323, 9, 7, 236, 34, "nvlmk", "Nvlmk", 21.133d),
            Film(729, 4, 0, 104, 34, "nvlmk", "Nvlmk", 21.133d),
            Film(730, 11, 3, 90, 34, "nvlmk", "Nvlmk", 21.133d),
            // 35 nivlsfs.avi, 20.133s: 292 nivl_b2, 730 zcoal_3.
            Film(292, 1, 1, 22, 35, "nivlsfs", "Nivlsfs", 20.133d),
            Film(292, 2, 1, 10, 35, "nivlsfs", "Nivlsfs", 20.133d),
            Film(730, 11, 0, 1218, 35, "nivlsfs", "Nivlsfs", 20.133d),
            // 37 junon.avi, 32.133s: 359 junon, 725 zmind1.
            Film(359, 0, 0, 79, 37, "junon", "Junon", 32.133d),
            Film(725, 8, 4, 34, 37, "junon", "Junon", 32.133d),
            // 38 hiwind0.avi, 19.133s: 385 junair2, 726 zmind2.
            Film(385, 0, 0, 136, 38, "hiwind0", "Hiwind0", 19.133d),
            Film(726, 4, 5, 2, 38, "hiwind0", "Hiwind0", 19.133d),
            // 39 mtcrl.avi, 4.133s: 462 mtcrl_4, 727 zmind3.
            Film(462, 3, 1, 14, 39, "mtcrl", "Mtcrl", 4.133d),
            Film(727, 5, 5, 2, 39, "mtcrl", "Mtcrl", 4.133d),
            // 41 biskdead.avi, 9.333s: 461 mtcrl_3, 567 rcktin5.
            Film(461, 1, 1, 9, 41, "biskdead", "Biskdead", 9.333d),
            Film(567, 12, 13, 640, 41, "biskdead", "Biskdead", 9.333d),
            // 42 boogdemo.avi, 32.8s: 643 white2.
            Film(643, 1, 3, 29, 42, "boogdemo", "Boogdemo", 32.800d),
            Film(543, 13, 6, 0, 42, "boogdemo", "Boogdemo", 32.800d),
            // 44 setogake.avi, 9.6s: 569 rcktin7.
            Film(569, 14, 3, 105, 44, "setogake", "Setogake", 9.600d),
            // The first play, in the Cave of the Gi: AD script 3 requests
            // EISHA script 3, which is PMVIE 44 and nothing else, then EISHA
            // script 4, which is MOVIE and nothing else.
            Film(550, 14, 4, 0, 44, "setogake", "Setogake", 9.600d),
            // 45 rcktfail.avi, 34.133s: 569 rcktin7.
            Film(569, 3, 3, 130, 45, "rcktfail", "Rcktfail", 34.133d),
            // 46 jairofly.avi, 41.2s: 774 rckt32.
            Film(774, 14, 3, 102, 46, "jairofly", "Jairofly", 41.200d),
            Film(742, 13, 4, 0, 46, "jairofly", "Jairofly", 41.200d),
            // 47 jairofal.avi, 15.133s: 87 sky, 637 loslake1.
            Film(87, 0, 0, 34, 47, "jairofal", "Jairofal", 15.133d),
            Film(637, 2, 9, 239, 47, "jairofal", "Jairofal", 15.133d),
            // 48 gold7.avi, 19.333s: 347 fr_e, 489 bwhlin.
            Film(347, 0, 0, 74, 48, "gold7", "Gold7", 19.333d),
            Film(489, 0, 0, 538, 48, "gold7", "Gold7", 19.333d),
            // 49 gold7_2.avi, 23.133s: 347 fr_e, 489 bwhlin.
            Film(347, 0, 0, 125, 49, "gold7_2", "Gold7 2", 23.133d),
            Film(489, 0, 0, 686, 49, "gold7_2", "Gold7 2", 23.133d),
            Film(489, 0, 0, 759, 49, "gold7_2", "Gold7 2", 23.133d),
            Film(489, 0, 0, 832, 49, "gold7_2", "Gold7 2", 23.133d),
            Film(489, 0, 0, 905, 49, "gold7_2", "Gold7 2", 23.133d),
            // 50 earithdd.avi, 69.467s: 67 fship_12, 647 ancnt2.
            Film(67, 2, 3, 42, 50, "earithdd", "Earithdd", 69.467d),
            Film(647, 0, 0, 229, 50, "earithdd", "Earithdd", 69.467d),
            // 51 funeral.avi, 36.133s: 67 fship_12, 641 blue_2.
            Film(67, 2, 3, 83, 51, "funeral", "Funeral", 36.133d),
            Film(641, 0, 0, 80, 51, "funeral", "Funeral", 36.133d),
            // 52 car_1209.avi, 71.533s: 779 md8_52.
            Film(779, 3, 0, 64, 52, "car_1209", "Car 1209", 71.533d),
            Film(236, 11, 5, 0, 52, "car_1209", "Car 1209", 71.533d),
            // 54 greatpit.avi, 33.467s: 68 fship_2.
            Film(68, 4, 2, 542, 54, "greatpit", "Greatpit", 33.467d),
            // 55 c_scene1.avi, 14s: 67 fship_12.
            Film(67, 1, 0, 40, 55, "c_scene1", "C scene1", 14.000d),
            // 56 c_scene2.avi, 11.867s: 643 white2.
            Film(643, 3, 3, 79, 56, "c_scene2", "C scene2", 11.867d),
            // 57 c_scene3.avi, 13.4s: 639 loslake3.
            Film(639, 0, 0, 23, 57, "c_scene3", "C scene3", 13.400d),
            // 58 biglight.avi, 71.4s: 639 loslake3.
            Film(639, 0, 0, 35, 58, "biglight", "Biglight", 71.400d),
            // 59 meteosky.avi, 17.4s: 67 fship_12.
            Film(67, 2, 3, 30, 59, "meteosky", "Meteosky", 17.400d),
            // 60 weapon0.avi, 29.133s: 269 blin70_4.
            Film(269, 1, 2, 87, 60, "weapon0", "Weapon0", 29.133d)
        ];
    }

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

    /// <summary>
    /// How late a deferred cue may still be spoken after the moment it describes.
    /// Used only on the path where a film's recording gave way to the game's own
    /// words and the rest of the film is being described from its cue windows: a cue
    /// held up by a line of dialogue is still worth hearing a moment later, but one
    /// whose shot left the screen long ago describes something the player can no
    /// longer be looking at.
    /// </summary>
    public static readonly TimeSpan CueLatenessAllowance = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How far into a film a continuous recording may still be started from its
    /// beginning. A recording is fixed from its first second and cannot be seeked, so
    /// starting one against a film that is already under way would describe the
    /// opening shot over the middle of the scene. Past this the film is still
    /// described, from the cues its position has actually reached.
    /// </summary>
    public static readonly TimeSpan ContinuousStartAllowance = TimeSpan.FromSeconds(1.5);

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

        // A film can outlive the field that started it: the Highwind sequence runs one
        // film across four fields, whose scripts have a MOVIE at byte 0 and no PMVIE
        // of their own because the film is already going. The field changing under a
        // film that is still the same film is the story continuing, not the film
        // ending, so the description continues with it rather than being cut off and
        // restarted from the beginning.
        if (sample.CurrentFieldId != track.FieldId &&
            !(sample.MovieActive && sample.MovieNumber == track.MovieNumber &&
              sample.MovieNumberIsCurrent))
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
