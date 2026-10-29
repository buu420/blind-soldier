namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reviewed names for people in the current Echo-S 1.24 field layouts, keyed by the loaded
/// entity index.
///
/// <para>Echo-S replaces the dialogue table, so the installed stock text cannot name anyone in
/// an Echo-S field and the reader suppresses it. This table names those people instead. It is
/// consulted only after the loaded script prefix has been accepted as a reviewed Echo-S 1.24
/// layout. All five reviewed variants (base, Aavock, ESUI, FT, Retouch) share these entity
/// indices, meshes and names.</para>
///
/// <para>A name is the heading Echo-S's own loaded text puts on that entity's own windows, in
/// Echo-S's spelling, so the player hears the name the dialogue box shows. A window another
/// entity opens never counts. A role is the stock reviewed role of the same mesh holding the
/// same conversation, used where Echo-S renumbered the entity. Visibility, Talk, LINE and
/// player-model gates still decide whether anyone is offered.</para>
/// </summary>
internal static class EchoNpcIdentityCatalog
{
    private static readonly IReadOnlyDictionary<(int FieldId, int LoadedEntityId), string> Reviewed =
        new Dictionary<(int FieldId, int LoadedEntityId), string>
        {
            // The Highwind and Cosmo Canyon: Bugenhagen (cosmo_boo) heads his own windows.
            [(66, 11)] = "Bugenhagen",
            [(525, 28)] = "Bugenhagen",
            [(531, 15)] = "Bugenhagen",
            [(534, 5)] = "Bugenhagen",
            [(541, 11)] = "Bugenhagen",
            [(544, 11)] = "Bugenhagen",

            // white1: an unnamed, unmapped cosmo_boo entity whose own windows are his.
            [(642, 10)] = "Bugenhagen",

            // cosin1_1, bugin2: the elder (cosmo_old), spelled as Echo-S spells him.
            [(530, 5)] = "Elder Hago",
            [(544, 20)] = "Elder Hago",

            // zz3: the Chocobo Sage (another_guid).
            [(80, 4)] = "Chocobo Sage",

            // nrthmk, sbwy4_5: Wedge (midgal_avafat).
            [(119, 5)] = "Wedge",
            [(168, 6)] = "Wedge",

            // mds7_w2: Echo-S replaces the Beginner's Hall with its author's cameo. He reuses
            // Cloud's mesh, is unmapped, and every window of his own is headed Tsuna.
            [(149, 9)] = "Tsuna",

            // blinst_1: an Echo-S cameo on Barret's mesh, headed UpRisen on his own windows.
            [(229, 11)] = "UpRisen",

            // min71: Echo-S renumbers Johnny's parents; their own windows carry these headings
            // (no apostrophe in Echo-S's text).
            [(153, 8)] = "Johnnys Dad",
            [(153, 9)] = "Johnnys Mom",

            // mrkt3: Echo-S renumbers the street outside the members' club. Each entity holds
            // the same conversation on the same mesh as the stock reviewed role, and Echo-S's
            // own text calls the club the Honeybee Manor.
            [(214, 12)] = "Honeybee Manor doorman",
            [(214, 13)] = "Honeybee Manor attendant",
            [(214, 15)] = "Man",
            [(214, 17)] = "Shinra soldier",
            [(214, 18)] = "Shinra soldier",

            // onna_52, delpb: Mukki (market_muki1).
            [(220, 10)] = "Mukki",
            [(445, 10)] = "Mukki",

            // The Turks: Reno, Rude, Tseng and Elena on their own meshes.
            [(377, 9)] = "Reno",
            [(377, 10)] = "Tseng",
            [(377, 11)] = "Elena",
            [(379, 14)] = "Rude",
            [(562, 10)] = "Rude",
            [(580, 13)] = "Elena",
            [(580, 14)] = "Rude",
            [(580, 15)] = "Reno",
            [(587, 12)] = "Reno",
            [(587, 13)] = "Rude",
            [(592, 6)] = "Reno",
            [(592, 7)] = "Rude",
            [(594, 4)] = "Rude",
            [(595, 4)] = "Reno",
            [(596, 4)] = "Rude",
            [(598, 4)] = "Reno",
            [(599, 17)] = "Rude",
            [(602, 16)] = "Tseng",

            // junbin4: Scarlet (sub_scarlet).
            [(401, 6)] = "Scarlet",

            // jailin4: Coates (korel_korts), as Echo-S names him.
            [(477, 8)] = "Coates",

            // The Gold Saucer: Mr. Hangman (gold_hang), Dio (sub_dio), Ester (sub_esto).
            [(495, 4)] = "Mr. Hangman",
            [(496, 16)] = "Dio",
            [(503, 5)] = "Dio",
            [(511, 11)] = "Ester",

            // Rocket Town: Shera (sub_siera) and Palmer (sub_palmer).
            [(558, 23)] = "Shera",
            [(558, 24)] = "Palmer",

            // Wutai: Godo (utai_godoh) in the pagoda and at home.
            [(586, 21)] = "Godo",
            [(589, 9)] = "Godo",

            // bonevil: Echo-S renumbers the village. Entity 4 alone offers the Lunar Harp
            // excavation and lends his staff to dig, as the stock reviewed foreman does.
            [(617, 4)] = "Excavation foreman"
        };

    /// <summary>The reviewed name or role for a loaded Echo-S entity; null when there is none.</summary>
    internal static string? Label(int fieldId, int loadedEntityId) =>
        Reviewed.TryGetValue((fieldId, loadedEntityId), out var label) ? label : null;

    /// <summary>Every reviewed row, for coverage tests.</summary>
    internal static IEnumerable<(int FieldId, int LoadedEntityId, string Label)> Rows =>
        Reviewed.Select(row => (row.Key.FieldId, row.Key.LoadedEntityId, row.Value));
}
