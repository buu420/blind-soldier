using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// One distinct limit break or summon animation, as the battle queue row that plays it.
/// </summary>
/// <param name="Key">The approved description's key.</param>
/// <param name="HistoryKey">What the save's history records. Persisted: never renumber.</param>
/// <param name="Command">3 for a summon, 0x14 for a limit.</param>
/// <param name="Action">The row action that identifies a limit, or null.</param>
/// <param name="Effect">The row effect that identifies a summon or a Toy Box object, or null.</param>
/// <param name="NativeName">The kernel's own name for the attack, for the log.</param>
public sealed record BattleAnimationIdentity(
    string Key,
    int HistoryKey,
    byte Command,
    ushort? Action,
    byte? Effect,
    string NativeName);

/// <summary>
/// Which queue row is which approved animation. Established from the legacy executable
/// and the kernel, and checked against the x64 translation's use of the same guest data:
///
/// <list type="bullet">
/// <item><b>Summons</b> by effect. <c>FUN_00427C22</c> sends command 3 to
/// <c>FUN_005C0E4B</c>, which switches on the actor's copy of the row effect, 0x00-0x11:
/// the sixteen kernel summons 0x38-0x47 carry effects 0x00-0x0F, Fat-Chocobo (kernel
/// 0x60, swapped in by special 2) 0x10 and Gunge Lance (kernel 0x61, queued by special 1
/// after <c>FUN_00436C4B</c> rolls the sword rows back) 0x11. The model table at
/// 0x008FEBA0 names them (MOGURIDA ... ODIN_GDA). The action is not used: a menu cast
/// carries the relative index, a substitution or a Slots reel the absolute attack.
/// W-Summon reaches the queue as 3 through the command remap at 0x007B7618.</item>
/// <item><b>Limits</b> by action, command 0x14. A menu limit carries its relative index
/// k (<c>FUN_005C930F</c> adds 0x80 only to the attack), so Cloud, Barret, Aerith, Cid,
/// Red XIII, Yuffie, Dice and Vincent's four transformations are k = 0x00-0x37 from the
/// limit table at 0x0091F6D4. Tifa's menu limit never plays: special 0x13
/// (<c>FUN_005DC880</c>) queues one sub-action 0x62 + move for each reel that did not
/// miss, and those rows play. Cait Sith's Slots is replaced by <c>FUN_005DF460</c>: a
/// summon on three bars (command 3), otherwise 0x69-0x6E, or 0x6F Toy Box when nothing
/// matched. Transformed Vincent attacks through the kernel character AI with command
/// 0x14 and 0x70 + form*2 (+1 thirty per cent of the time).</item>
/// <item><b>Toy Box</b> by effect. Special 8 adds 0-6 to effect 0x46, and the limit
/// effect table at 0x008FE860 gives each its own object: 0x46 SPECIAL/RAKU falling rock,
/// 0x47 special/turara icicles, 0x48-0x4B the LIMIT2/TOYBOX models hatena_2 (weight),
/// hanmer (hammer), debuc (fat chocobo) and hellh (house), 0x4C the Comet2 effect.</item>
/// </list>
///
/// <para>Only party members (actors 0-2) perform these; no enemy in scene.bin uses a
/// summon or limit attack, and a row from anyone else is not narrated. Rows that belong
/// to another animation - Finishing Touch, Blade Beam and Satan Slam's second rows
/// (0x78-0x7A), the finish row Tifa's reels end with, the menu limits that never play -
/// have no identity, so they can never be described separately.</para>
/// </summary>
public static class BattleAnimationIdentities
{
    public const byte SummonCommand = 0x03;
    public const byte LimitCommand = 0x14;

    /// <summary>The row action of a Toy Box reel outcome (kernel attack 0x6F).</summary>
    public const ushort ToyBoxAction = 0x6F;

    private const int PartyActorCount = 3;
    private const int SummonOpeningHistoryKey = 0x0200;
    private const int SummonHistoryBase = 0x0300;
    private const int LimitHistoryBase = 0x1400;
    private const int ToyBoxHistoryBase = 0x1500;

    private static readonly BattleAnimationIdentity[] identities =
    [
        SummonOf("summon.choco_mog", 0x00, "Choco/Mog"),
        SummonOf("summon.fat_chocobo", 0x10, "Fat-Chocobo"),
        SummonOf("summon.shiva", 0x01, "Shiva"),
        SummonOf("summon.ifrit", 0x02, "Ifrit"),
        SummonOf("summon.ramuh", 0x03, "Ramuh"),
        SummonOf("summon.titan", 0x04, "Titan"),
        SummonOf("summon.odin_sword", 0x05, "Odin"),
        SummonOf("summon.odin_lance", 0x11, "Gunge Lance"),
        SummonOf("summon.leviathan", 0x06, "Leviathan"),
        SummonOf("summon.bahamut", 0x07, "Bahamut"),
        SummonOf("summon.kujata", 0x08, "Kjata"),
        SummonOf("summon.alexander", 0x09, "Alexander"),
        SummonOf("summon.phoenix", 0x0A, "Phoenix"),
        SummonOf("summon.neo_bahamut", 0x0B, "Neo Bahamut"),
        SummonOf("summon.hades", 0x0C, "Hades"),
        SummonOf("summon.typhon", 0x0D, "Typoon"),
        SummonOf("summon.bahamut_zero", 0x0E, "Bahamut ZERO"),
        SummonOf("summon.knights", 0x0F, "Knights of Round"),

        LimitOf("limit.cloud.braver", 0x00, "Braver"),
        LimitOf("limit.cloud.cross_slash", 0x01, "Cross-slash"),
        LimitOf("limit.cloud.blade_beam", 0x02, "Blade Beam"),
        LimitOf("limit.cloud.climhazzard", 0x03, "Climhazzard"),
        LimitOf("limit.cloud.meteorain", 0x04, "Meteorain"),
        LimitOf("limit.cloud.finishing_touch", 0x05, "Finishing Touch"),
        LimitOf("limit.cloud.omnislash", 0x06, "Omnislash"),
        LimitOf("limit.barret.big_shot", 0x07, "Big Shot"),
        LimitOf("limit.barret.grenade_bomb", 0x08, "Grenade Bomb"),
        LimitOf("limit.barret.mindblow", 0x09, "Mindblow"),
        LimitOf("limit.barret.hammerblow", 0x0A, "Hammerblow"),
        LimitOf("limit.barret.satellite_beam", 0x0B, "Satellite Beam"),
        LimitOf("limit.barret.ungarmax", 0x0C, "Ungarmax"),
        LimitOf("limit.barret.catastrophe", 0x0D, "Catastrophe"),
        LimitOf("limit.aerith.healing_wind", 0x0E, "Healing Wind"),
        LimitOf("limit.aerith.seal_evil", 0x0F, "Seal Evil"),
        LimitOf("limit.aerith.breath_earth", 0x10, "Breath of the Earth"),
        LimitOf("limit.aerith.fury_brand", 0x11, "Fury Brand"),
        LimitOf("limit.aerith.planet_protector", 0x12, "Planet Protector"),
        LimitOf("limit.aerith.pulse_life", 0x13, "Pulse of Life"),
        LimitOf("limit.aerith.great_gospel", 0x14, "Great Gospel"),

        // Tifa's chain moves, one row per reel that did not miss (kernel 0x62-0x68).
        LimitOf("limit.tifa.beat_rush", 0x62, "Beat Rush"),
        LimitOf("limit.tifa.somersault", 0x63, "Somersault"),
        LimitOf("limit.tifa.waterkick", 0x64, "Waterkick"),
        LimitOf("limit.tifa.meteodrive", 0x65, "Meteodrive"),
        LimitOf("limit.tifa.dolphin_blow", 0x66, "Dolphin Blow"),
        LimitOf("limit.tifa.meteor_strike", 0x67, "Meteor Strike"),
        LimitOf("limit.tifa.final_heaven", 0x68, "Final Heaven"),

        LimitOf("limit.cid.boost_jump", 0x1C, "Boost Jump"),
        LimitOf("limit.cid.dragon", 0x1D, "Dragon"),
        LimitOf("limit.cid.hyper_jump", 0x1E, "Hyper Jump"),
        LimitOf("limit.cid.dynamite", 0x1F, "Dynamite"),
        LimitOf("limit.cid.dragon_dive", 0x20, "Dragon Dive"),
        LimitOf("limit.cid.big_brawl", 0x21, "Big Brawl"),
        LimitOf("limit.cid.highwind", 0x22, "Highwind"),
        LimitOf("limit.red.sled_fang", 0x23, "Sled Fang"),
        LimitOf("limit.red.howling_moon", 0x24, "Howling Moon"),
        LimitOf("limit.red.blood_fang", 0x25, "Blood Fang"),
        LimitOf("limit.red.stardust_ray", 0x26, "Stardust Ray"),
        LimitOf("limit.red.lunatic_high", 0x27, "Lunatic High"),
        LimitOf("limit.red.earth_rave", 0x28, "Earth Rave"),
        LimitOf("limit.red.cosmo_memory", 0x29, "Cosmo Memory"),

        // Cait Sith: Dice is a menu limit; the rest are Slots outcomes (kernel 0x69-0x6E).
        LimitOf("limit.cait.dice", 0x2A, "Dice"),
        LimitOf("limit.cait.game_over", 0x69, "Game Over"),
        LimitOf("limit.cait.death_joker", 0x6A, "Death Joker"),
        LimitOf("limit.cait.toy_soldier", 0x6B, "Toy Soldier"),
        LimitOf("limit.cait.lucky_girl", 0x6C, "Lucky Girl"),
        LimitOf("limit.cait.mog_dance", 0x6D, "Mog Dance"),
        LimitOf("limit.cait.transform", 0x6E, "Transform"),
        ToyBoxOf("limit.cait.toy_boulder", 0x46),
        ToyBoxOf("limit.cait.toy_ice", 0x47),
        ToyBoxOf("limit.cait.toy_weight", 0x48),
        ToyBoxOf("limit.cait.toy_hammer", 0x49),
        ToyBoxOf("limit.cait.toy_chocobo", 0x4A),
        ToyBoxOf("limit.cait.toy_house", 0x4B),
        ToyBoxOf("limit.cait.toy_meteors", 0x4C),

        // Vincent: the four transformations are menu limits; each form's two attacks
        // come from the kernel character AI (kernel 0x70-0x77).
        LimitOf("limit.vincent.galian_beast", 0x2D, "Galian Beast"),
        LimitOf("limit.vincent.death_gigas", 0x2E, "Death Gigas"),
        LimitOf("limit.vincent.hellmasker", 0x2F, "Hellmasker"),
        LimitOf("limit.vincent.chaos", 0x30, "Chaos"),
        LimitOf("limit.vincent.berserk_dance", 0x70, "Berserk Dance"),
        LimitOf("limit.vincent.beast_flare", 0x71, "Beast Flare"),
        LimitOf("limit.vincent.gigadunk", 0x72, "Gigadunk"),
        LimitOf("limit.vincent.livewire", 0x73, "Livewire"),
        LimitOf("limit.vincent.splattercombo", 0x74, "Splattercombo"),
        LimitOf("limit.vincent.nightmare", 0x75, "Nightmare"),
        LimitOf("limit.vincent.chaos_saber", 0x76, "Chaos Saber"),
        LimitOf("limit.vincent.satan_slam", 0x77, "Satan Slam"),

        LimitOf("limit.yuffie.greased_lightning", 0x31, "Greased Lightning"),
        LimitOf("limit.yuffie.clear_tranquil", 0x32, "Clear Tranquil"),
        LimitOf("limit.yuffie.landscaper", 0x33, "Landscaper"),
        LimitOf("limit.yuffie.bloodfest", 0x34, "Bloodfest"),
        LimitOf("limit.yuffie.gauntlet", 0x35, "Gauntlet"),
        LimitOf("limit.yuffie.doom_living", 0x36, "Doom of the Living"),
        LimitOf("limit.yuffie.all_creation", 0x37, "All Creation"),
    ];

    private static readonly Dictionary<string, BattleAnimationIdentity> byKey =
        identities.ToDictionary(identity => identity.Key, StringComparer.Ordinal);

    private static readonly Dictionary<byte, BattleAnimationIdentity> summonsByEffect =
        identities.Where(identity => identity.Command == SummonCommand)
            .ToDictionary(identity => identity.Effect!.Value);

    private static readonly Dictionary<ushort, BattleAnimationIdentity> limitsByAction =
        identities.Where(identity => identity.Command == LimitCommand && identity.Action is not null)
            .ToDictionary(identity => identity.Action!.Value);

    private static readonly Dictionary<byte, BattleAnimationIdentity> toyBoxByEffect =
        identities.Where(identity => identity.Command == LimitCommand && identity.Effect is not null)
            .ToDictionary(identity => identity.Effect!.Value);

    public static IReadOnlyList<BattleAnimationIdentity> All => identities;

    /// <summary>
    /// The casting every summon opens with - the same whoever summons and whatever is
    /// summoned - told once per save under its own key, apart from each summon's own
    /// description. It is not one of the animations in <see cref="All"/>.
    /// </summary>
    public static BattleAnimationIdentity SummonOpening { get; } =
        new("opening.summon", SummonOpeningHistoryKey, SummonCommand, null, null, "Summoning");

    /// <summary>Whether a playing row is a party member's summon.</summary>
    public static bool IsSummonCast(BattleAnimationObservation observation) =>
        observation.IsPlaying && observation.Attacker < PartyActorCount && observation.Command == SummonCommand;

    /// <summary>The approved animation a playing row is, if it is one.</summary>
    public static bool TryResolve(
        BattleAnimationObservation observation,
        [NotNullWhen(true)] out BattleAnimationIdentity? identity)
    {
        identity = null;
        if (!observation.IsPlaying || observation.Attacker >= PartyActorCount)
        {
            return false;
        }

        return observation.Command switch
        {
            SummonCommand => summonsByEffect.TryGetValue(observation.Effect, out identity),
            LimitCommand => toyBoxByEffect.TryGetValue(observation.Effect, out identity) ||
                (observation.Action != ToyBoxAction &&
                 limitsByAction.TryGetValue(observation.Action, out identity)),
            _ => false,
        };
    }

    public static bool TryGetByKey(string key, [NotNullWhen(true)] out BattleAnimationIdentity? identity) =>
        byKey.TryGetValue(key, out identity);

    private static BattleAnimationIdentity SummonOf(string key, byte effect, string nativeName) =>
        new(key, SummonHistoryBase + effect, SummonCommand, null, effect, nativeName);

    private static BattleAnimationIdentity LimitOf(string key, ushort action, string nativeName) =>
        new(key, LimitHistoryBase + action, LimitCommand, action, null, nativeName);

    private static BattleAnimationIdentity ToyBoxOf(string key, byte effect) =>
        new(key, ToyBoxHistoryBase + effect, LimitCommand, null, effect, "Toy Box");
}

/// <summary>One stage of a description: what to say once the animation reaches a tick.</summary>
/// <param name="AtFrame">Native battle ticks (0x00BFD0E4, 15 per second in vanilla)
/// after the engine started the animation's queue row.</param>
public sealed record BattleAnimationCue(int AtFrame, string Text);

/// <summary>An approved description ready to be told.</summary>
/// <param name="Revision">1 for the original single text, 2 for staged cues.</param>
/// <param name="Cues">The stages in native tick order; a revision 1 text is one cue at 0.</param>
/// <summary>The native moment a description's cue ticks count from.</summary>
public enum BattleAnimationAnchor
{
    /// <summary>The engine started the queue row: the performer begins to move.</summary>
    RowStart,

    /// <summary>
    /// A summon's own sequence began: the summon dispatcher <c>FUN_005C0E4B</c> left the
    /// effect table after its stage 1 called the summon's main. Only for summons.
    /// </summary>
    SummonSequence,

    /// <summary>
    /// The attack-name banner <c>FUN_0042782A</c> came on screen while the summoner casts.
    /// Only for <see cref="BattleAnimationIdentities.SummonOpening"/>.
    /// </summary>
    Banner,
}

public sealed record BattleAnimationDescription(
    BattleAnimationIdentity Identity,
    string Name,
    int Revision,
    IReadOnlyList<BattleAnimationCue> Cues)
{
    /// <summary>
    /// Every revision after the first moves the key by <see cref="RevisionHistoryOffset"/>,
    /// so a revised description is told once per save even where the original was heard,
    /// and the original's history is kept rather than deleted.
    /// </summary>
    public const int RevisionHistoryOffset = 0x8000;

    public int HistoryKey => Identity.HistoryKey + ((Revision - 1) * RevisionHistoryOffset);

    /// <summary>What tick 0 of the cues is.</summary>
    public BattleAnimationAnchor Anchor { get; init; }

    /// <summary>All the words, for logs and the screen reader.</summary>
    public string Text => string.Join(" ", Cues.Select(cue => cue.Text));
}

/// <summary>
/// The approved descriptions, keyed by the animation they describe.
///
/// <para>Reads root's battle-animations.json: an array of objects with a <c>key</c> and a
/// <c>name</c>, and either a single <c>text</c> (revision 1, told at the animation's
/// start) or <c>"revision": 2</c> with <c>cues</c>, each <c>{ "atFrame", "text" }</c>. A
/// key that is not one of the 93 native animations, a blank text, cues that are empty,
/// out of order or outside 0-<see cref="MaximumCueFrame"/>, an unsupported revision or a
/// second entry for the same key is reported and the whole entry skipped, never partly
/// used. Anything unreadable is an empty catalog, which narrates nothing.</para>
/// </summary>
public sealed class BattleAnimationNarrationCatalog
{
    /// <summary>Four minutes at the vanilla 15 ticks a second; longer than any summon.</summary>
    public const int MaximumCueFrame = 3600;

    private readonly Dictionary<string, BattleAnimationDescription> byKey = new(StringComparer.Ordinal);

    /// <summary>Revision 1 entries: one text each, told when the animation starts.</summary>
    public BattleAnimationNarrationCatalog(
        IEnumerable<(string Key, string Name, string Text)> entries,
        Action<string>? log = null)
        : this(entries.Select(entry => new Entry(
            entry.Key, entry.Name, 1, [new BattleAnimationCue(0, entry.Text)], null)), log)
    {
    }

    private BattleAnimationNarrationCatalog(IEnumerable<Entry> entries, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            if (entry.Key == BattleAnimationIdentities.SummonOpening.Key)
            {
                AddSummonOpening(entry, log);
                continue;
            }

            if (!BattleAnimationIdentities.TryGetByKey(entry.Key, out var identity))
            {
                log?.Invoke($"Battle animation description '{entry.Key}' matches no native animation; skipped.");
                continue;
            }

            if (Invalid(entry) is { } problem)
            {
                log?.Invoke($"Battle animation description '{entry.Key}' {problem}; skipped.");
                continue;
            }

            BattleAnimationAnchor anchor;
            switch (entry.Anchor)
            {
                case null or "row":
                    anchor = BattleAnimationAnchor.RowStart;
                    break;
                case "summon" when identity.Command == BattleAnimationIdentities.SummonCommand:
                    anchor = BattleAnimationAnchor.SummonSequence;
                    break;
                case "summon":
                    log?.Invoke($"Battle animation description '{entry.Key}' uses the summon anchor but is not a summon; skipped.");
                    continue;
                case "banner":
                    log?.Invoke($"Battle animation description '{entry.Key}' uses the banner anchor, which only the summon opening may; skipped.");
                    continue;
                default:
                    log?.Invoke($"Battle animation description '{entry.Key}' has an unknown anchor '{entry.Anchor}'; skipped.");
                    continue;
            }

            if (byKey.ContainsKey(entry.Key))
            {
                log?.Invoke($"Battle animation description '{entry.Key}' is a duplicate; the first is kept.");
                continue;
            }

            byKey[entry.Key] = new BattleAnimationDescription(
                identity,
                string.IsNullOrWhiteSpace(entry.Name) ? identity.NativeName : entry.Name,
                entry.Revision,
                entry.Cues)
            {
                Anchor = anchor,
            };
        }

        MissingKeys = BattleAnimationIdentities.All
            .Where(identity => !byKey.ContainsKey(identity.Key))
            .Select(identity => identity.Key)
            .ToArray();
    }

    public static BattleAnimationNarrationCatalog Empty { get; } =
        new(Array.Empty<(string Key, string Name, string Text)>());

    /// <summary>The native animations described; the summon opening is not one of them.</summary>
    public int Count => byKey.Count;

    /// <summary>The casting shared by every summon, or null when none is installed.</summary>
    public BattleAnimationDescription? SummonOpening { get; private set; }

    /// <summary>The native animations this catalog has no description for.</summary>
    public IReadOnlyList<string> MissingKeys { get; }

    public static BattleAnimationNarrationCatalog Parse(string? json, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out var nested)
                    ? nested
                    : default;
            if (array.ValueKind != JsonValueKind.Array)
            {
                log?.Invoke("Battle animation descriptions have no entries array.");
                return Empty;
            }

            var entries = new List<Entry>();
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object || String(element, "key") is not { Length: > 0 } key)
                {
                    log?.Invoke("Battle animation description without a key; skipped.");
                    continue;
                }

                entries.Add(ReadEntry(element, key));
            }

            return new BattleAnimationNarrationCatalog(entries, log);
        }
        catch (JsonException ex)
        {
            log?.Invoke($"Battle animation descriptions could not be read: {ex.Message}");
            return Empty;
        }
    }

    public bool TryGet(
        BattleAnimationObservation observation,
        [NotNullWhen(true)] out BattleAnimationDescription? description)
    {
        description = null;
        return BattleAnimationIdentities.TryResolve(observation, out var identity) &&
            byKey.TryGetValue(identity.Key, out description);
    }

    /// <summary>
    /// Reads one entry as written; <see cref="Invalid"/> decides whether it may be used.
    /// A malformed field becomes a value validation rejects, so nothing is guessed.
    /// </summary>
    private static Entry ReadEntry(JsonElement element, string key)
    {
        var name = String(element, "name") ?? string.Empty;
        var anchor = !element.TryGetProperty("anchor", out var anchorValue)
            ? null
            : anchorValue.ValueKind == JsonValueKind.String ? anchorValue.GetString() : "(not text)";
        var hasRevision = element.TryGetProperty("revision", out var revisionValue);
        var revision = !hasRevision ? 0 : revisionValue.TryGetInt32(out var number) ? number : -1;
        if (!element.TryGetProperty("cues", out var cuesValue))
        {
            return new Entry(key, name, hasRevision ? revision : 1,
                [new BattleAnimationCue(0, String(element, "text") ?? string.Empty)], anchor);
        }

        var cues = new List<BattleAnimationCue>();
        if (cuesValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var cue in cuesValue.EnumerateArray())
            {
                var atFrame = cue.ValueKind == JsonValueKind.Object &&
                    cue.TryGetProperty("atFrame", out var frame) && frame.TryGetInt32(out var value)
                        ? value
                        : -1;
                cues.Add(new BattleAnimationCue(
                    atFrame,
                    cue.ValueKind == JsonValueKind.Object ? String(cue, "text") ?? string.Empty : string.Empty));
            }
        }

        // Staged cues must say which revision they are; without it the history key is unknown.
        return new Entry(key, name, hasRevision ? revision : 0, cues, anchor);
    }

    private void AddSummonOpening(Entry entry, Action<string>? log)
    {
        var key = entry.Key;
        if (Invalid(entry) is { } problem)
        {
            log?.Invoke($"Battle animation description '{key}' {problem}; skipped.");
        }
        else if (entry.Anchor != "banner")
        {
            log?.Invoke($"Battle animation description '{key}' must count from the banner (\"anchor\": \"banner\"); skipped.");
        }
        else if (SummonOpening is not null)
        {
            log?.Invoke($"Battle animation description '{key}' is a duplicate; the first is kept.");
        }
        else
        {
            var identity = BattleAnimationIdentities.SummonOpening;
            SummonOpening = new BattleAnimationDescription(
                identity,
                string.IsNullOrWhiteSpace(entry.Name) ? identity.NativeName : entry.Name,
                entry.Revision,
                entry.Cues)
            {
                Anchor = BattleAnimationAnchor.Banner,
            };
        }
    }

    private static string? Invalid(Entry entry)
    {
        if (entry.Revision is not (1 or 2))
        {
            return "has no supported revision (1 for a single text, 2 for staged cues)";
        }

        if (entry.Revision == 1 && entry.Cues.Count != 1)
        {
            return "is revision 1 but is not a single text";
        }

        if (entry.Cues.Count == 0)
        {
            return "has no cues";
        }

        var previous = 0;
        foreach (var cue in entry.Cues)
        {
            if (string.IsNullOrWhiteSpace(cue.Text))
            {
                return "has a cue without text";
            }

            if (cue.AtFrame is < 0 or > MaximumCueFrame)
            {
                return $"has a cue at tick {cue.AtFrame}, outside 0-{MaximumCueFrame}";
            }

            if (cue.AtFrame < previous)
            {
                return "has cues out of tick order";
            }

            previous = cue.AtFrame;
        }

        return null;
    }

    private static string? String(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record Entry(
        string Key, string Name, int Revision, IReadOnlyList<BattleAnimationCue> Cues, string? Anchor);
}
