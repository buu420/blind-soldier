namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The Fort Condor unit records, decoded from <c>data.bin</c> inside <c>condor.lgp</c>.
///
/// The battle draws every word of its interface from textures, so there is no text to
/// intercept and the reader has to supply the wording itself. The names and descriptions here
/// are the strings the game actually shows, lifted from <c>emes01.tex</c> and <c>emes08.tex</c>;
/// the numbers are the ones the game actually uses, read out of the unit table.
///
/// <c>data.bin</c> opens with twelve ascending section offsets. The first section runs from
/// 0x0026 to 0x0226 and holds sixteen 0x20-byte records. Ten of them are the units a player can
/// hire, in the order below, which is also the order of the ten descriptions in
/// <c>emes08.tex</c>. The remaining six are not purchasable and are left out.
///
/// Within a record: price is the u16 at +0x00, HP the byte at +0x02, attack the byte at +0x05,
/// and +0x0C a movement delay. Confirmed against a published stat table for all ten units:
/// price and attack match outright, HP is exactly (u16 at +0x02) - 256, and the delay reproduces
/// the published speed as 255 - delay for all ten including both tied pairs.
///
/// Range is deliberately absent. Published tables give Shooter a range of 1 while the game's own
/// description is "Can shoot from afar", so they disagree with the game and nothing in the
/// record has been tied to range yet. Speaking a wrong number is worse than speaking none.
/// </summary>
public static class CondorUnitCatalog
{
    /// <param name="RecordIndex">Index into the 0x20-byte record table at <c>data.bin</c> + 0x26.</param>
    /// <param name="Speed">As the game shows it, HIGH to LOW: 255 minus the record's delay byte.</param>
    /// <param name="Ability">The unit's own line from <c>emes08.tex</c>.</param>
    /// <param name="StrongAgainst">Unit this one beats, or null where the game states none.</param>
    /// <param name="WeakAgainst">Unit this one loses to, or null where the game states none.</param>
    public sealed record CondorUnit(
        int RecordIndex,
        string Name,
        int Price,
        int Hp,
        int Attack,
        int Speed,
        string Ability,
        string? StrongAgainst,
        string? WeakAgainst);

    private static readonly IReadOnlyList<CondorUnit> Units = new[]
    {
        new CondorUnit(1, "Fighter", 400, 200, 30, 224, "Regular unit", null, null),
        new CondorUnit(2, "Attacker", 420, 180, 25, 234, "Moves fast", "Beast", "Barbarian"),
        new CondorUnit(3, "Defender", 440, 220, 35, 208, "Has the highest HP", "Barbarian", "Wyvern"),
        new CondorUnit(4, "Shooter", 520, 160, 20, 212, "Can shoot from afar", "Wyvern", "Beast"),
        new CondorUnit(5, "Stoner", 480, 100, 20, 188, "Can roll a stone. Can't move", null, null),
        new CondorUnit(6, "Tristoner", 1000, 150, 30, 178, "Can roll three stones at a time. Can't move", null, null),
        new CondorUnit(7, "Catapult", 480, 100, 18, 200, "Can throw a stone far off and roll it. Can't move", null, null),
        new CondorUnit(8, "Fire Catapult", 600, 120, 25, 190, "Can fire a bomb far off. Can't move", null, null),
        new CondorUnit(12, "Repairer", 480, 160, 10, 212, "Can repair an ally. Low power", null, null),
        new CondorUnit(13, "Worker", 400, 160, 15, 230, "Can set a bomb. Low power", null, null)
    };

    public static IReadOnlyList<CondorUnit> HireableUnits => Units;

    /// <summary>
    /// The enemy types, named from the labels the game draws for them.
    /// </summary>
    /// <remarks>
    /// These are not hireable, so they are kept out of the catalog proper. The
    /// executable picks a name region as <c>0x5F + typeId</c> and draws it from
    /// <c>emes01</c>; reading the cells that rule selects gives these four.
    /// Published guides list Beast at 212 HP and Wyvern at 140, which match no
    /// record in the shipped archive - the archive is what the game runs.
    /// </remarks>
    private static readonly IReadOnlyDictionary<int, string> EnemyNames =
        new Dictionary<int, string>
        {
            [16] = "Commander",
            [17] = "Wyvern",
            [18] = "Beast",
            [19] = "Barbarian"
        };

    /// <summary>The drawn label for a unit type, or null if it has not been proved.</summary>
    public static string? ResolveName(int recordIndex) =>
        ResolveByRecordIndex(recordIndex)?.Name ??
        (EnemyNames.TryGetValue(recordIndex, out var name) ? name : null);

    public static CondorUnit? ResolveByRecordIndex(int recordIndex) =>
        Units.FirstOrDefault(unit => unit.RecordIndex == recordIndex);

    /// <summary>Offset of the first unit record from the start of <c>data.bin</c>.</summary>
    public const int RecordTableOffset = 0x26;

    public const int RecordLength = 0x20;

    /// <summary>
    /// Reads price, HP, attack and speed out of one raw record. The name and description are not
    /// in <c>data.bin</c> - they live in textures - so this returns only the numbers, for checking
    /// the catalog against the shipped game data or against a live table.
    /// </summary>
    public static (int Price, int Hp, int Attack, int Speed)? DecodeRecordStats(ReadOnlySpan<byte> record)
    {
        if (record.Length < 0x0D)
        {
            return null;
        }

        var price = record[0x00] | (record[0x01] << 8);
        var hp = record[0x02];
        var attack = record[0x05];
        var speed = 255 - record[0x0C];

        return (price, hp, attack, speed);
    }

    /// <summary>
    /// The unit as a hire screen reads it: name, price, and the stats <c>eunit00.tex</c> puts on
    /// the panel, then the ability line and the matchups the game states for it.
    /// </summary>
    public static string DescribeForHire(CondorUnit unit, int? availableGil = null)
    {
        var description = $"{unit.Name}. {unit.Price} gil";

        if (availableGil is { } gil && gil < unit.Price)
        {
            description += ", not affordable";
        }

        description += $". HP {unit.Hp}. Attack {unit.Attack}. Speed {unit.Speed}. {unit.Ability}.";

        if (unit.StrongAgainst is { } strong)
        {
            description += $" Beats {strong}.";
        }

        if (unit.WeakAgainst is { } weak)
        {
            description += $" Loses to {weak}.";
        }

        return description;
    }
}
