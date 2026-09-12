namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Turns a field script's film number into the file the game will actually play.
///
/// <para>The number is not an index. <c>sm_movie.cpp</c> (FUN_0040ADB0 in the
/// installed <c>ff7_en.exe</c>) picks one of four blocks of the name table at
/// 0x007BAE80: films 0 to 19 are the same on every disc, and from 20 up the block
/// depends on the disc byte at 0x00DC0BDC - table index 20 for disc 1, 54 for
/// disc 2, 96 for disc 3. So film 20 is <c>mkup.avi</c> on disc 1 and
/// <c>last4_2.avi</c> on disc 3, and a description bound to the number alone is
/// only right for one of them.</para>
///
/// <para>The disc moves in ordinary play. The byte is field-script bank 13 index 0 -
/// <c>FieldNavigationObjectReader.AddressFieldBankBase</c> + 0x300 - so a script
/// sets it with the generic bank-write opcode, and the installed field 103
/// <c>blackbgb</c> does exactly that: its init script asks the player, writes 3 at
/// byte 384 or 2 at byte 503, shows the save screen and jumps on to
/// <c>las0_1</c> or <c>lost2</c>. Those are the Forgotten Capital and the final
/// dungeon, the two disc boundaries of the original release.</para>
///
/// <para>So a script address does not name a film. The film has to be resolved from
/// the number and the disc together, every time.</para>
/// </summary>
public static class MovieFilmNameResolver
{
    /// <summary>The disc byte the name blocks are chosen by.</summary>
    public const int AddressMovieDisc = 0x00DC0BDC;

    /// <summary>Films numbered below this are the same file on every disc.</summary>
    public const int SharedFilmCount = 20;

    /// <summary>Reported when the disc byte could not be read.</summary>
    public const int DiscUnknown = -1;

    // The table read out of the installed ff7_en.exe at 0x007BAE80. Entry 95 is a
    // null pointer in the binary and is a hole here too. EveryNameMatchesTheInstalledExecutable
    // reads the file and checks this copy entry by entry.
    private static readonly string?[] Names =
    [
        "fship2.avi", "fship2n.avi", "d_ropego.avi", "d_ropein.avi",
        "u_ropein.avi", "u_ropego.avi", "gold2.avi", "gold3.avi",
        "gold4.avi", "gold6.avi", "gold5.avi", "boogup.avi",
        "boogdown.avi", "junair_u.avi", "junair_d.avi", "junelein.avi",
        "junelego.avi", "junin_in.avi", "junin_go.avi", "moriya.avi",
        "mkup.avi", "northmk.avi", "mk8.avi", "ontrain.avi",
        "mainplr.avi", "smk.avi", "southmk.avi", "plrexp.avi",
        "fallpl.avi", "monitor.avi", "bike.avi", "mtnvl.avi",
        "mtnvl2.avi", "brgnvl.avi", "nvlmk.avi", "nivlsfs.avi",
        "jenova_e.avi", "junon.avi", "hiwind0.avi", "mtcrl.avi",
        "gold1.avi", "biskdead.avi", "boogdemo.avi", "boogstar.avi",
        "setogake.avi", "rcktfail.avi", "jairofly.avi", "jairofal.avi",
        "gold7.avi", "gold7_2.avi", "earithdd.avi", "funeral.avi",
        "car_1209.avi", "opening.avi", "greatpit.avi", "c_scene1.avi",
        "c_scene2.avi", "c_scene3.avi", "biglight.avi", "meteosky.avi",
        "weapon0.avi", "weapon1.avi", "weapon2.avi", "weapon3.avi",
        "weapon4.avi", "weapon5.avi", "hwindfly.avi", "phoenix.avi",
        "nrcrl.avi", "nrcrl_b.avi", "dumcrush.avi", "zmind01.avi",
        "zmind02.avi", "zmind03.avi", "gelnica.avi", "rcketoff.avi",
        "white2.avi", "junsea.avi", "rckethit0.avi", "rckethit1.avi",
        "meteofix.avi", "canonon.avi", "feelwin0.avi", "feelwin1.avi",
        "canonht1.avi", "canonht2.avi", "canonh3f.avi", "parashot.avi",
        "hwindjet.avi", "canonht0.avi", "wh2e2.avi", "loslake1.avi",
        "lslmv.avi", "canonh1p.avi", "canon.avi", null,
        "last4_2.avi", "last4_3.avi", "last4_4.avi", "lastmap.avi",
        "lastflor.avi", "ending1.avi", "ending3.avi", "fcar.avi",
        "white2.avi", "ending2.avi"
    ];

    /// <summary>The table index each disc's block of numbered films starts at.</summary>
    private static int BlockStart(int disc) => disc switch
    {
        1 => 20,
        2 => 54,
        3 => 96,
        _ => -1
    };

    /// <summary>The whole table, for the test that checks it against the executable.</summary>
    public static IReadOnlyList<string?> InstalledNames => Names;

    /// <summary>
    /// The file <paramref name="movieNumber"/> names on <paramref name="disc"/>, or
    /// null when the pair does not name a film. Nothing is guessed: an unknown disc,
    /// a negative number and a number past the end of the disc's block all return
    /// null, and the caller is expected to stay quiet rather than pick something.
    /// </summary>
    public static string? Resolve(int movieNumber, int disc)
    {
        if (movieNumber < 0)
        {
            return null;
        }

        if (movieNumber < SharedFilmCount)
        {
            // The game reads these from the shared block without consulting the disc
            // at all, so an unreadable disc costs nothing here.
            return Names[movieNumber];
        }

        var start = BlockStart(disc);
        if (start < 0)
        {
            return null;
        }

        var index = start + movieNumber - SharedFilmCount;
        return index >= 0 && index < Names.Length ? Names[index] : null;
    }

    /// <summary>
    /// Whether the disc has to be known before this number names anything. Films
    /// below <see cref="SharedFilmCount"/> are the same file on every disc and can be
    /// identified without it; from 20 up the number is meaningless on its own.
    /// </summary>
    public static bool RequiresDisc(int movieNumber) => movieNumber >= SharedFilmCount;

    /// <summary>
    /// Whether a film number resolves, on this disc, to a named film a description
    /// was written for.
    ///
    /// <para>An unreadable disc fails: a film of 20 or more means a different film on
    /// each disc, so not knowing the disc is not knowing the film, and describing one
    /// anyway would be a guess with a two-in-three chance of describing footage the
    /// player is not being shown.</para>
    /// </summary>
    public static bool NamesTheSameFilm(int movieNumber, int disc, string expectedFileName)
    {
        var resolved = Resolve(movieNumber, disc);
        return resolved is not null &&
            string.Equals(resolved, expectedFileName, StringComparison.OrdinalIgnoreCase);
    }
}
