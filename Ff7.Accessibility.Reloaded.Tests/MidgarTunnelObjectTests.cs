using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Sector 8 underground on the way to the Mako Cannon: md8_b1 (733), md8_b2 (734), sbwy4_22
/// (735), tunnel_4 (736), tunnel_5 (737) and tunnel_6 (778), byte-identical in the legacy and
/// Steam 2026 archives.
///
/// <para>The tunnels past sbwy4_22 are one labyrinth of numbered sections in bank15[129]. sbwy4_22
/// sets it to 4; every jump line raises or lowers it by one and reloads a field: section 4 is
/// tunnel_6 (the Turks), other even sections are tunnel_4, odd ones tunnel_5, and tunnel_5's
/// upper-left jump from section 3 leaves for md8brdg2. Each Init shows a pickup only in its own
/// section: tunnel_4's W-Item materia and save point in section 18, tunnel_5's chests in sections
/// 15 (box), 13 (box_2) and 17 (box_3, box_4). md8_b1's and md8_b2's chests are always shown and
/// only change pose once opened. sbwy4_22 and tunnel_6 grant nothing.</para>
///
/// <para>So a reused tunnel room correctly lists no pickup in a section that has none: the
/// catalog offers each pickup only while the game itself shows its model and allows Talk.</para>
/// </summary>
internal static class MidgarTunnelObjectTests
{
    private const int EventTable = 0x02000000;
    private const int PickupModelIndex = 7;
    private const int PlayerModelIndex = 0;

    /// <summary>Every native pickup in the six fields: its Talk's grant and the flag it sets.</summary>
    private static readonly NativePickup[] NativePickups =
    [
        // md8_b1: BITON B15[141] bits 6 and 5, B15[142] bits 2 and 6.
        new(733, 15, FieldNavigationObjectKind.Item, 272, 141, 0x40),
        new(733, 16, FieldNavigationObjectKind.Item, 174, 141, 0x20),
        new(733, 17, FieldNavigationObjectKind.Item, 6, 142, 0x04),
        new(733, 18, FieldNavigationObjectKind.Item, 5, 142, 0x40),
        // md8_b2: B15[142] bits 5 and 4.
        new(734, 16, FieldNavigationObjectKind.Item, 5, 142, 0x20),
        new(734, 17, FieldNavigationObjectKind.Item, 240, 142, 0x10),
        // tunnel_4 materia (SMTRA 21): shown only in section 18 while B15[141] bit 4 is clear,
        // and hidden by its own Talk, so the live model alone is the collected state.
        new(736, 19, FieldNavigationObjectKind.Materia, 21, -1, 0),
        // tunnel_5: B15[146] bits 2, 1 and 0, B15[142] bit 7.
        new(737, 16, FieldNavigationObjectKind.Item, 72, 146, 0x04),
        new(737, 17, FieldNavigationObjectKind.Item, 71, 146, 0x02),
        new(737, 18, FieldNavigationObjectKind.Item, 73, 142, 0x80),
        new(737, 19, FieldNavigationObjectKind.Item, 74, 146, 0x01),
    ];

    private static readonly int[] TunnelFields = [733, 734, 735, 736, 737, 778];

    public static void Run()
    {
        CheckCatalogCoverage(FieldNavigationObjectCatalog.CreateAllFields());
        ASectionPickupIsOfferedOnlyWhileTheGameShowsIt();
        TheMateriaGoesWithItsModel();
        AnOpenedChestIsNoLongerOffered();
        Console.WriteLine("PASS Midgar tunnel objects: every native pickup, in its own section only, until collected.");
    }

    /// <summary>
    /// Every native pickup is catalogued once with its own grant and flag, each save point is
    /// there, and nothing else is: no pickup invented for sbwy4_22 or tunnel_6, and no copy of a
    /// pickup from another reused section.
    /// </summary>
    internal static void CheckCatalogCoverage(IEnumerable<FieldNavigationObjectDefinition> catalog)
    {
        var tunnel = catalog.Where(definition => TunnelFields.Contains(definition.FieldId)).ToArray();
        var pickups = tunnel
            .Where(definition => definition.Kind is FieldNavigationObjectKind.Item or FieldNavigationObjectKind.Materia)
            .ToArray();
        True(pickups.Length == NativePickups.Length,
            $"one object per native tunnel pickup: {pickups.Length} catalogued, {NativePickups.Length} native");
        foreach (var native in NativePickups)
        {
            var matches = pickups.Where(definition =>
                definition.FieldId == native.Field && definition.EntityId == native.Entity).ToArray();
            True(matches.Length == 1, $"field {native.Field} entity {native.Entity} is catalogued once: {matches.Length}");
            var definition = matches[0];
            True(definition.Kind == native.Kind && definition.NativeId == native.NativeId,
                $"field {native.Field} entity {native.Entity} grants {native.Kind} {native.NativeId}: " +
                $"{definition.Kind} {definition.NativeId}");
            True(definition.TargetKind == FieldNavigationObjectTargetKind.Model && definition.UsesTalkInteraction,
                $"field {native.Field} entity {native.Entity} is its live model, picked up by Talk");
            True(definition is { RequiredBank: < 0, MinimumGameMoment: < 0, MaximumGameMoment: < 0 },
                $"field {native.Field} entity {native.Entity} is gated by the game's own model, not a guess");
            if (native.CollectedAddress >= 0)
            {
                True(definition.CollectedBank == 15 && definition.CollectedAddress == native.CollectedAddress &&
                    definition.CollectedMask == native.CollectedMask,
                    $"field {native.Field} entity {native.Entity} is collected by B15[{native.CollectedAddress}] " +
                    $"0x{native.CollectedMask:X2}: B{definition.CollectedBank}[{definition.CollectedAddress}] " +
                    $"0x{definition.CollectedMask:X2}");
            }
        }

        var saves = tunnel.Where(definition => definition.Kind == FieldNavigationObjectKind.SavePoint)
            .Select(definition => (definition.FieldId, definition.EntityId))
            .OrderBy(save => save)
            .ToArray();
        True(saves.SequenceEqual([(733, 13), (736, 20)]),
            $"md8_b1's and tunnel_4's save points: {string.Join(", ", saves)}");
        True(tunnel.All(definition => definition.FieldId is not (735 or 778)),
            "sbwy4_22 and tunnel_6 have no pickups to offer");
    }

    private static void ASectionPickupIsOfferedOnlyWhileTheGameShowsIt()
    {
        // tunnel_5 box_3: Init shows it (VISI 1, TLKON 0) only when B15[129] == 17.
        var memory = new TunnelMemory(737, 18);
        var reader = Reader(memory, 737, 18);
        var position = Position(737);

        True(reader.ReadTargets(position).Count == 0, "in any other section its Init hides it, so it is not offered");

        memory.Visible = true;
        memory.TalkDisabled = false;
        var target = reader.ReadTargets(position).Single();
        True(target.Label == "Megalixir", $"in section 17 the chest is offered: {target.Label}");
        True(target is { X: -135, Y: 1453, Z: 21 }, $"at its native XYZI placement: {target.X},{target.Y},{target.Z}");
        True(target.ObjectCueKind == FieldObjectCueKind.Chest, "with the chest cue");

        memory.TalkDisabled = true;
        True(reader.ReadTargets(position).Count == 0, "shown with Talk off it cannot be opened, so it is not offered");
    }

    private static void TheMateriaGoesWithItsModel()
    {
        // tunnel_4 materia: shown only in section 18 while uncollected; Talk hides it.
        var memory = new TunnelMemory(736, 19) { Visible = true, TalkDisabled = false, Placement = (453, -276, 0) };
        var reader = Reader(memory, 736, 19);
        var position = Position(736);
        var target = reader.ReadTargets(position).Single();
        True(target.Label == "W-Item Materia", $"section 18's materia: {target.Label}");
        True(target.ObjectCueKind == FieldObjectCueKind.Materia, "with the materia cue");

        memory.Visible = false;
        memory.TalkDisabled = true;
        True(reader.ReadTargets(position).Count == 0, "once its Talk hides it, it is gone");
    }

    private static void AnOpenedChestIsNoLongerOffered()
    {
        // md8_b1 box_1: always shown; opening it sets B15[141] bit 6.
        var memory = new TunnelMemory(733, 15) { Visible = true, TalkDisabled = false };
        var reader = Reader(memory, 733, 15);
        var position = Position(733);
        True(reader.ReadTargets(position).Single().Label == "Aegis Armlet", "the unopened chest is offered");
        memory.SetBank15(141, 0x40);
        True(reader.ReadTargets(position).Count == 0, "with B15[141] bit 6 set it has been opened");
    }

    private static FieldNavigationObjectReader Reader(TunnelMemory memory, int field, int entity)
    {
        var definition = FieldNavigationObjectCatalog.CreateAllFields()
            .Single(candidate => candidate.FieldId == field && candidate.EntityId == entity);
        return new FieldNavigationObjectReader(
            memory.ReadInt32,
            memory.ReadByte,
            id => id switch { 73 => "Megalixir", 272 => "Aegis Armlet", _ => null },
            id => id == 21 ? "W-Item" : null,
            [definition]);
    }

    private static FieldPositionSnapshot Position(int field) =>
        new(FieldPositionReader.FieldModule, field, PlayerModelIndex, 0, 0, 0, 20, 0);

    private readonly record struct NativePickup(
        int Field, int Entity, FieldNavigationObjectKind Kind, int NativeId, int CollectedAddress, int CollectedMask);

    /// <summary>One pickup's model in the field event table, the player's, and bank 15.</summary>
    private sealed class TunnelMemory(int field, int entity)
    {
        private readonly Dictionary<int, byte> bank15 = [];

        public int Field { get; } = field;

        public bool Visible { get; set; }

        public bool TalkDisabled { get; set; } = true;

        public (int X, int Y, int Z) Placement { get; set; } = (-135, 1453, 21);

        public void SetBank15(int index, byte value) => bank15[index] = value;

        public int ReadInt32(int address)
        {
            if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr)
            {
                return EventTable;
            }

            var model = EventTable + PickupModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            var scale = FieldNavigationObjectReader.ModelPositionFixedPointScale;
            return address switch
            {
                _ when address == model + FieldNavigationObjectReader.PositionXOffset => Placement.X * scale,
                _ when address == model + FieldNavigationObjectReader.PositionYOffset => Placement.Y * scale,
                _ when address == model + FieldNavigationObjectReader.PositionZOffset => Placement.Z * scale,
                _ => 0
            };
        }

        public byte ReadByte(int address)
        {
            var model = EventTable + PickupModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            var player = EventTable + PlayerModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            var bank15Base = FieldNavigationObjectReader.AddressFieldBankBase + 0x400;
            if (address >= bank15Base && address < bank15Base + 0x100)
            {
                return bank15.TryGetValue(address - bank15Base, out var value) ? value : (byte)0;
            }

            return address switch
            {
                FieldPositionReader.AddressFieldNumModels => 10,
                _ when address == FieldNavigationObjectReader.AddressFieldModelIdArray + entity => PickupModelIndex,
                _ when address == model + FieldNavigationObjectReader.VisibilityOffset => Visible ? (byte)1 : (byte)0,
                _ when address == model + FieldNavigationNpcReader.TalkDisabledOffset => TalkDisabled ? (byte)1 : (byte)0,
                _ when address == model + FieldNavigationNpcReader.TalkRadiusOffset => 24,
                _ when address == player + FieldNavigationNpcReader.CollisionRadiusOffset => 30,
                _ => 0
            };
        }
    }

    /// <summary>
    /// The installed scripts themselves (legacy or Steam 2026 data root): each pickup's Init
    /// section test and Talk grant/flag, the section counter's writers, and the two empty rooms.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        // Section tests: IFUB B15[129] == n in each section pickup's Init.
        Has(catalog, 736, 19, 0, "14F081120017", "tunnel_4 materia shows only in section 18");
        Has(catalog, 736, 19, 0, "14F08D040A09", "and only while B15[141] bit 4 is clear");
        Has(catalog, 736, 20, 0, "14F081120009", "tunnel_4's save point shows only in section 18");
        Has(catalog, 737, 16, 0, "14F0810F0009", "tunnel_5 box shows only in section 15");
        Has(catalog, 737, 17, 0, "14F0810D0009", "tunnel_5 box_2 shows only in section 13");
        Has(catalog, 737, 18, 0, "14F081110009", "tunnel_5 box_3 shows only in section 17");
        Has(catalog, 737, 19, 0, "14F081110009", "tunnel_5 box_4 shows only in section 17");
        // Grants and flags.
        Has(catalog, 736, 19, 1, "5B000015000000", "tunnel_4 materia grants W-Item");
        Has(catalog, 736, 19, 1, "82F08D04", "and sets B15[141] bit 4");
        Has(catalog, 736, 19, 1, "A400", "and hides its model");
        foreach (var native in NativePickups.Where(pickup => pickup.Kind == FieldNavigationObjectKind.Item))
        {
            var grant = $"5800{native.NativeId & 0xFF:X2}{native.NativeId >> 8:X2}01";
            Has(catalog, native.Field, native.Entity, 1, grant, $"field {native.Field} entity {native.Entity} grants item {native.NativeId}");
            var bit = System.Numerics.BitOperations.Log2((uint)native.CollectedMask);
            Has(catalog, native.Field, native.Entity, 1, $"82F0{native.CollectedAddress:X2}{bit:X2}",
                $"field {native.Field} entity {native.Entity} sets B15[{native.CollectedAddress}] bit {bit}");
        }

        // The section counter: sbwy4_22 enters at section 4, tunnel_6's jumps move it by one.
        Has(catalog, 735, 1, 0, "80F08104", "sbwy4_22 enters the labyrinth at section 4");
        Has(catalog, 778, 19, 2, "7C0F81", "tunnel_6 jump_u lowers the section");
        Has(catalog, 778, 20, 2, "950F81", "tunnel_6 jump_d raises the section");

        foreach (var empty in new[] { 735, 778 })
        {
            for (var entity = 0; entity < 24; entity++)
            {
                for (var script = 0; script < 32; script++)
                {
                    True(!catalog.ReadScriptOpcodes(empty, entity, script).Any(opcode =>
                            opcode.Opcode is 0x58 or 0x5B or 0x39),
                        $"field {empty} entity {entity} script {script} grants nothing");
                }
            }
        }

        Console.WriteLine("PASS Midgar tunnel objects (installed scripts): sections, grants and flags.");
    }

    private static void Has(FieldScriptNavigationCatalog catalog, int field, int entity, int script, string hex, string what) =>
        True(catalog.ReadScriptOpcodes(field, entity, script)
                .Any(opcode => Convert.ToHexString(opcode.Bytes.ToArray()) == hex),
            $"{what} ({field} e{entity} s{script} {hex})");

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Midgar tunnel objects: {what}");
        }
    }
}
