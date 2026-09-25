using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Lucrecia's cave (zz4, field 81): entity 12 <c>buki</c> is Vincent's weapon model
/// (<c>zz4weapon_vinsen_w.char</c>). Its Init shows it and enables Talk only once game moment
/// 1197, bank11[143] &gt; 8, bank12[145] &lt; bank2[24], Vincent in the party and bank1[51] bit 4
/// clear all hold; its Talk shows "Received Death Penalty!" and "Received Chaos!", sets bank1[51]
/// bit 4, adds items 254 and 93, and hides the model and turns Talk off. One visible pickup that
/// awards two rewards is one object target, gated on the live model and its collected bit.
/// </summary>
internal static class LucreciaCaveObjectTests
{
    private const int Field = 81;
    private const int Entity = 12;
    private const int ModelIndex = 9;
    private const int PlayerModelIndex = 0;
    private const int EventTable = 0x02000000;

    public static void Run()
    {
        TheCaveWeaponIsOneObjectForBothRewards();
        TheCaveWeaponIsOfferedOnlyWhileTheGameShowsIt();
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        True(Has(catalog, 0, "A109"), "zz4 buki's Init loads model 9");
        True(Has(catalog, 0, "CB070D"), "zz4 buki's Init shows it only with Vincent in the party");
        True(Has(catalog, 1, "82103304"), "zz4 buki's Talk sets bank1[51] bit 4");
        True(Has(catalog, 1, "5800FE0001"), "zz4 buki's Talk adds item 254");
        True(Has(catalog, 1, "58005D0001"), "zz4 buki's Talk adds item 93");
        True(Has(catalog, 1, "A400"), "zz4 buki's Talk hides the model afterwards");
        var npc = catalog.ReadField(Field).Npcs.SingleOrDefault(definition => definition.EntityId == Entity);
        True(npc.ModelResourceName == "weapon_vinsen_w.char",
            $"zz4 buki draws Vincent's weapon model, not a person: {npc.ModelResourceName}");
    }

    private static bool Has(FieldScriptNavigationCatalog catalog, int script, string hex) =>
        catalog.ReadScriptOpcodes(Field, Entity, script)
            .Any(opcode => Convert.ToHexString(opcode.Bytes.ToArray()) == hex);

    private static void TheCaveWeaponIsOneObjectForBothRewards()
    {
        var definitions = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(definition => definition.FieldId == Field && definition.EntityId == Entity)
            .ToArray();
        True(definitions.Length == 1, $"one object for the cave weapon, not one per reward: {definitions.Length}");
        var definition = definitions[0];
        True(definition.Kind == FieldNavigationObjectKind.Named, "the cave weapon is named for both rewards");
        True(definition.Label == "Death Penalty and Chaos", $"the cave weapon's rewards: {definition.Label}");
        True(definition.TargetKind == FieldNavigationObjectTargetKind.Model, "the cave weapon is its own live model");
        True(definition.SourceModelResource == "zz4weapon_vinsen_w.char", "the cave weapon's native model");
        True(definition is { CollectedBank: 1, CollectedAddress: 51, CollectedMask: 0x10 },
            "the cave weapon is collected once bank1[51] bit 4 is set");
        True(definition.UsesTalkInteraction, "the cave weapon is picked up by talking to it");
    }

    private static void TheCaveWeaponIsOfferedOnlyWhileTheGameShowsIt()
    {
        var definition = FieldNavigationObjectCatalog.CreateAllFields()
            .Single(candidate => candidate.FieldId == Field && candidate.EntityId == Entity);
        var memory = new CaveMemory();
        var reader = new FieldNavigationObjectReader(memory.ReadInt32, memory.ReadByte, _ => null, _ => null, [definition]);
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, PlayerModelIndex, 0, 0, 0, 20, 0);

        True(reader.ReadTargets(position).Count == 0, "hidden by Init until its conditions hold, it is not offered");

        memory.Visible = true;
        memory.TalkDisabled = false;
        var target = reader.ReadTargets(position).Single();
        True(target.Label == "Death Penalty and Chaos", $"shown and talkable, it is offered: {target.Label}");
        True(target is { X: -6, Y: 737, Z: 70 }, $"at its native XYZI placement: {target.X},{target.Y},{target.Z}");

        memory.TalkDisabled = true;
        True(reader.ReadTargets(position).Count == 0, "with Talk off it cannot be picked up, so it is not offered");

        memory.TalkDisabled = false;
        memory.Collected = true;
        True(reader.ReadTargets(position).Count == 0, "once bank1[51] bit 4 is set it has been picked up");
    }

    /// <summary>The field's event table with the weapon's model and the player's.</summary>
    private sealed class CaveMemory
    {
        public bool Visible { get; set; }

        public bool TalkDisabled { get; set; } = true;

        public bool Collected { get; set; }

        public int ReadInt32(int address)
        {
            if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr)
            {
                return EventTable;
            }

            var model = EventTable + ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            return address switch
            {
                _ when address == model + FieldNavigationObjectReader.PositionXOffset => -6 * FieldNavigationObjectReader.ModelPositionFixedPointScale,
                _ when address == model + FieldNavigationObjectReader.PositionYOffset => 737 * FieldNavigationObjectReader.ModelPositionFixedPointScale,
                _ when address == model + FieldNavigationObjectReader.PositionZOffset => 70 * FieldNavigationObjectReader.ModelPositionFixedPointScale,
                _ => 0
            };
        }

        public byte ReadByte(int address)
        {
            var model = EventTable + ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            var player = EventTable + PlayerModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            return address switch
            {
                FieldPositionReader.AddressFieldNumModels => 10,
                _ when address == FieldNavigationObjectReader.AddressFieldModelIdArray + Entity => ModelIndex,
                _ when address == model + FieldNavigationObjectReader.VisibilityOffset => Visible ? (byte)1 : (byte)0,
                _ when address == model + FieldNavigationNpcReader.TalkDisabledOffset => TalkDisabled ? (byte)1 : (byte)0,
                _ when address == model + FieldNavigationNpcReader.TalkRadiusOffset => 24,
                _ when address == player + FieldNavigationNpcReader.CollisionRadiusOffset => 30,
                _ when address == FieldNavigationObjectReader.AddressFieldBankBase + 51 => Collected ? (byte)0x10 : (byte)0,
                _ => 0
            };
        }
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(what);
        }
    }
}
