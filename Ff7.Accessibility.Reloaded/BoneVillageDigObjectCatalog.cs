namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The Bone Village dig, 772 bonevil2: what a sighted player can walk to. It has no gateway
/// and no exit line. Two levels are joined by one ladder: ladu (entity 19) at the top and ladd
/// (entity 20) at the bottom, both LINEs whose Move climbs on a fresh OK. The diggers are
/// entities 14..18: each waits at his Init spot (-427, -5, triangle 49) until the party orders
/// a search, and then walks - up the ladder when the party is on the upper level - and jumps
/// to where the party stands (keyc script 3, and the digger's own script 3). Each one that has
/// gone is an object at his live position; one still waiting is not.
///
/// <para>What is buried is luna (entity 6) and box0..box6 (7..13), each placed at Init where its
/// item is. None of them is a definition here. The one the diggers are sent to look for is
/// offered by <see cref="BoneVillageDigSpot"/> instead, at the user's explicit request, and only
/// once the game has chosen it.</para>
/// </summary>
public static class BoneVillageDigObjectCatalog
{
    public const int FieldId = 772;

    public static IReadOnlyList<FieldNavigationObjectDefinition> Create() =>
    [
        Ladder(19, "Ladder down to the lower level; press OK", "ladu", 170, 514, 331),
        Ladder(20, "Ladder up to the upper level; press OK", "ladd", 218, 379, -88),
        .. FieldActivityReadout.ExcavationWorkerEntityIds.Select((entity, index) =>
            new FieldNavigationObjectDefinition(
                FieldId,
                entity,
                FieldNavigationObjectKind.Named,
                Label: $"Digger {index + 1}",
                SourceFieldName: "bonevil2",
                TargetKind: FieldNavigationObjectTargetKind.Model,
                // Physical targets: manual guidance would make them hints the controller never
                // routes to. The field activity readout and its repeat give what to do here.
                WaitingSpot: [FieldActivityReadout.ExcavationWaitingX, FieldActivityReadout.ExcavationWaitingY]))
    ];

    private static FieldNavigationObjectDefinition Ladder(int entity, string label, string name, int x, int y, int z) =>
        new(
            FieldId,
            entity,
            FieldNavigationObjectKind.Named,
            Label: label,
            SourceFieldName: "bonevil2",
            SourceEntityName: name,
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: x,
            StaticY: y,
            StaticZ: z,
            UsesPlayerCollisionRadius: true);
}

/// <summary>
/// The Bone Village dig spot (772 bonevil2). It shows where the treasure the diggers are sent
/// to look for is buried, and is listed in Objects so the player can walk there and choose it.
/// This tells the player more than the screen does: a sighted player sees only the diggers'
/// lines of sight, which meet at that spot. It is offered because the user asked for exactly
/// this shortcut.
///
/// <para>What the game does, from the installed scripts (the tests check it byte for byte):
/// <list type="bullet">
/// <item><description>luna (6) and box0..box6 (7..13) are placed by their Init at fixed points.
/// Each one stores the walkmesh triangle under it (GETAI) in Bank[6][16], [18] .. [30]. That is
/// the temporary bank, which is zeroed when the field loads.</description></item>
/// <item><description>The final choice is keyc script 3, while Bank[5][12] is 1. On Switch it
/// takes the party's triangle and compares it exactly with slots 16..28 in turn, setting
/// Bank[1][234] to 1..7. Slot 30 (box6's) is never compared. Anywhere that matches no slot gets
/// the junk roll: a Potion half the time, otherwise nothing. Distance plays no part, so the spot
/// counts only when the party stands on its triangle.</description></item>
/// <item><description>The foreman's request is Bank[1][235]: 1 is the Lunar Harp, 2 good
/// treasure and 3 normal treasure. For 1, the diggers' script 4 turns them to luna. For 2 and 3
/// it turns them to a box picked by Bank[5][15], a byte rolled at the blast (below 80 or 160 for
/// good; below 60, 120 or 180 for normal). So the harp's spot is known as soon as the field has
/// loaded, but any other spot only at the final choice (phase 1). Before that, the reader does
/// not guess a roll that has not happened.</description></item>
/// <item><description>Once Bank[1][231] bit 3 is set, the foreman refuses to dig for the harp
/// and bonevil's box1 Talk pays nothing for it. A request of 1 with that bit set is therefore
/// stale.</description></item>
/// </list></para>
///
/// <para>The spot is offered only when all of these agree with the script: the stored triangle,
/// the buried model's own triangle (event +0x78), and its placement. The request, roll and phase
/// must also read the same before and after. Its name is what bonevil's box1 Talk would give
/// right now. A reward that depends on a roll is named as a chance. A spot whose prize has
/// already been taken says there is nothing left. The player still presses Switch, pays for the
/// diggers and sets off the blast; nothing here writes to the game.</para>
/// </summary>
public static class BoneVillageDigSpot
{
    public const int FieldId = 772;

    /// <summary>Bank[5][12]: 7..3 placing diggers, 2 the blast, 1 the final choice.</summary>
    public const int PhaseIndex = 12;

    /// <summary>Bank[5][15], rolled at the blast.</summary>
    public const int RollIndex = 15;

    /// <summary>Bank[1][235], the foreman's request.</summary>
    public const int RequestIndex = 235;

    /// <summary>Bank[1][231] bit 3: the Lunar Harp has been received.</summary>
    public const int HarpReceivedIndex = 231;

    public const byte HarpReceivedMask = 0x08;

    // placeObject puts the model exactly where its Init says, and the fixed-point position reads
    // back to the same whole units. Allow a little slack, never enough to reach another spot.
    private const int PlacementReach = 2;

    private const int LunarHarpRequest = 1;
    private const int GoodTreasureRequest = 2;
    private const int NormalTreasureRequest = 3;

    /// <summary>
    /// One buried model: its entity, the Bank[6] slot its Init stores its triangle in, where its
    /// Init places it, and the Bank[1][234] result keyc gives for standing on its triangle. The
    /// result is 0 for box6, which is never compared.
    /// </summary>
    public readonly record struct Site(int EntityId, int BankSlot, int X, int Y, int Z, int Triangle, int DigResult);

    public static readonly IReadOnlyList<Site> Sites =
    [
        new(6, 16, -319, 608, 326, 7, 1),
        new(7, 18, 368, 634, 327, 24, 2),
        new(8, 20, -108, 81, -48, 44, 3),
        new(9, 22, 26, 132, -40, 40, 4),
        new(10, 24, -61, -113, -110, 60, 5),
        new(11, 26, 21, 513, 328, 12, 6),
        new(12, 28, -227, 500, 328, 8, 7),
        new(13, 30, -223, -252, -105, 77, 0)
    ];

    // One list per spot, the same instance on every read. The x64 host compares two reads'
    // targets, and a record compares a list by reference.
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> CompletionTriangles =
        Sites.ToDictionary(site => site.EntityId, site => (IReadOnlyList<int>)Array.AsReadOnly(new[] { site.Triangle }));

    /// <summary>The dig spot now, or null when the game has not chosen one or it cannot be read coherently.</summary>
    public static FieldNavigationTarget? Read(Func<int, int> readInt32, Func<int, byte> readByte)
    {
        var before = ReadChoice(readByte);
        if (Choose(before) is not { } site ||
            !TryReadPlacement(site, readInt32, readByte, out var x, out var y, out var z))
        {
            return null;
        }

        var label = $"Dig spot: {DescribeReward(site, readByte)}";
        if (ReadChoice(readByte) != before)
        {
            return null;
        }

        return new FieldNavigationTarget(
            FieldId,
            FieldNavigationCategory.Objects,
            label,
            x,
            y,
            z,
            $"object:{FieldId}:dig-spot:{site.EntityId}",
            CompletesOnArrival: true,
            CompletionTriangles: CompletionTriangles[site.EntityId]);
    }

    private readonly record struct Choice(byte Phase, byte Request, byte Roll, byte HarpFlags);

    private static Choice ReadChoice(Func<int, byte> readByte) =>
        new(
            readByte(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + PhaseIndex),
            readByte(FieldNavigationObjectReader.AddressFieldBankBase + RequestIndex),
            readByte(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + RollIndex),
            readByte(FieldNavigationObjectReader.AddressFieldBankBase + HarpReceivedIndex));

    private static Site? Choose(Choice choice)
    {
        if (choice.Phase is < 1 or > 7)
        {
            return null;
        }

        var entity = choice.Request switch
        {
            LunarHarpRequest when (choice.HarpFlags & HarpReceivedMask) == 0 => 6,
            GoodTreasureRequest when choice.Phase == 1 => choice.Roll < 80 ? 7 : choice.Roll < 160 ? 8 : 9,
            NormalTreasureRequest when choice.Phase == 1 =>
                choice.Roll < 60 ? 10 : choice.Roll < 120 ? 11 : choice.Roll < 180 ? 12 : 13,
            _ => -1
        };
        return entity < 0 ? null : Sites.Single(site => site.EntityId == entity);
    }

    private static bool TryReadPlacement(
        Site site,
        Func<int, int> readInt32,
        Func<int, byte> readByte,
        out int x,
        out int y,
        out int z)
    {
        x = y = z = 0;
        var storedAddress = FieldNavigationObjectReader.AddressTemporaryFieldBankBase + site.BankSlot;
        if ((readByte(storedAddress) | (readByte(storedAddress + 1) << 8)) != site.Triangle)
        {
            return false;
        }

        var eventTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
        var modelId = readByte(FieldNavigationObjectReader.AddressFieldModelIdArray + site.EntityId);
        if (eventTable == 0 || modelId == 0xFF || modelId >= modelCount)
        {
            return false;
        }

        var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
        var triangleAddress = eventAddress + FieldPositionReader.ObjectTriangleOffset;
        if ((readByte(triangleAddress) | (readByte(triangleAddress + 1) << 8)) != site.Triangle)
        {
            return false;
        }

        x = readInt32(eventAddress + FieldNavigationObjectReader.PositionXOffset) / FieldNavigationObjectReader.ModelPositionFixedPointScale;
        y = readInt32(eventAddress + FieldNavigationObjectReader.PositionYOffset) / FieldNavigationObjectReader.ModelPositionFixedPointScale;
        z = readInt32(eventAddress + FieldNavigationObjectReader.PositionZOffset) / FieldNavigationObjectReader.ModelPositionFixedPointScale;
        return Math.Abs(x - site.X) <= PlacementReach && Math.Abs(y - site.Y) <= PlacementReach;
    }

    /// <summary>
    /// What bonevil's box1 Talk gives for this spot's result now, checking the gates in the same
    /// order the script does. The game moment is Bank[2][0]. The one-time prizes are tracked by
    /// Bank[15][35] bit 5 (Buntline), Bank[15][38] bits 2, 1 and 3 (Megalixir, Mop, Key to
    /// Sector 5) and Bank[13][97] bits 1, 0 and 2 (Phoenix, Bahamut ZERO, W-Item). Rolls made at
    /// the box are named as chances, never as outcomes.
    /// </summary>
    private static string DescribeReward(Site site, Func<int, byte> readByte)
    {
        const string nothingLeft = "no treasure left";
        var bank = FieldNavigationObjectReader.AddressFieldBankBase;
        var moment = readByte(bank) | (readByte(bank + 1) << 8);
        bool Given15(int index, byte mask) => (readByte(bank + 0x400 + index) & mask) != 0;
        bool Given13(int index, byte mask) => (readByte(bank + 0x300 + index) & mask) != 0;
        var keyStillBuried = moment > 1195 && !Given15(38, 0x08);
        return site.DigResult switch
        {
            1 => "Lunar Harp",
            2 => !Given15(35, 0x20) ? "Buntline"
                : moment >= 1620 && !Given13(97, 0x02) ? "Phoenix Materia"
                : nothingLeft,
            3 => !Given15(38, 0x04) ? "Megalixir"
                : moment >= 1620 && !Given13(97, 0x01) ? "small chance of Bahamut ZERO Materia"
                : nothingLeft,
            4 => !Given15(38, 0x02) ? "Mop"
                : moment >= 1620 && !Given13(97, 0x04) ? "W-Item Materia"
                : nothingLeft,
            5 => keyStillBuried ? "Key to Sector 5" : "chance of an Elixir",
            6 => keyStillBuried ? "Key to Sector 5" : "Ether, or a small chance of Turbo Ether",
            7 => keyStillBuried ? "Key to Sector 5" : "chance of an Ether",
            _ => "chance of a Potion"
        };
    }
}
