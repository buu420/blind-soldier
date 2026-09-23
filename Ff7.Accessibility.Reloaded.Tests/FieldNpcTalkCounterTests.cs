using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// A live Talk beats a LINE that asks the NPC for something else - zz2's reward boxes -
/// but not a LINE that runs the NPC's own Talk, which is the game's counter for talking
/// to them.
///
/// <para>Across the installed fields, 19 speaking NPCs have a counter LINE and a Talk that
/// can be switched on. The LINEs of fields 153 (shinra), 376 (both receptionists), 443
/// (yufi) and 503 (dio) request the NPC's script 1, and three of those NPCs are placed 96
/// to 189 units from the floor their counter serves; the seller's LINE in 79 requests his
/// scripts 5 and 7, and he stands on the floor.</para>
/// </summary>
internal static class FieldNpcTalkCounterTests
{
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("field NPC talk counters: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        var seller = catalog.ReadField(79).Npcs.Single(npc => npc.EntityId == 4);
        Check(seller.InteractionLineEntityId == 5 && !seller.InteractionLineRunsTalk,
            "zz2's box LINE asks the seller for his reward scripts, not his Talk");
        foreach (var (field, entity, line) in new[] { (153, 18, 10), (376, 12, 7), (376, 13, 7), (443, 13, 6), (503, 5, 6) })
        {
            var npc = catalog.ReadField(field).Npcs.Single(candidate => candidate.EntityId == entity);
            Check(npc.InteractionLineEntityId == line && npc.InteractionLineRunsTalk,
                $"field {field} entity {entity}: LINE {line} runs the NPC's own Talk");
        }

        // With both Talks switched on and both LINEs enabled, the seller is walked to and
        // the receptionist is served across her counter.
        var receptionist = catalog.ReadField(376).Npcs.Single(npc => npc.EntityId == 12);
        var atCounter = ReadWithLiveTalk(receptionist, modelIndex: 5, x: -27, y: 17, z: 0);
        Check(atCounter is { } counter && counter.TriggerLine.HasValue && counter.TriggerEntityId == 7,
            $"the receptionist is reached at her counter; got {Describe(atCounter)}");

        var atSeller = ReadWithLiveTalk(seller, modelIndex: 3, x: -176, y: -12, z: 0);
        Check(atSeller is { } walked && walked.TriggerLine is null && walked.TriggerEntityId == 4 &&
              walked.X == -176 && walked.Y == -12,
            $"the seller is reached where he stands; got {Describe(atSeller)}");
        Console.WriteLine("field NPC talk counter tests passed with installed game data.");
    }

    private static FieldNavigationTarget? ReadWithLiveTalk(
        FieldScriptNpcDefinition definition,
        int modelIndex,
        int x,
        int y,
        int z)
    {
        const int events = 0x03000000;
        var npc = events + modelIndex * FieldNavigationObjectReader.FieldEventDataStride;
        int Int32(int address) => address switch
        {
            FieldNavigationObjectReader.AddressFieldEventDataPtr => events,
            _ when address == npc + FieldNavigationObjectReader.PositionXOffset => x * 4096,
            _ when address == npc + FieldNavigationObjectReader.PositionYOffset => y * 4096,
            _ when address == npc + FieldNavigationObjectReader.PositionZOffset => z * 4096,
            _ => 0
        };
        short Int16(int address) =>
            address == events + FieldNavigationNpcReader.CollisionRadiusOffset ||
            address == npc + FieldNavigationNpcReader.TalkRadiusOffset
                ? (short)40
                : (short)0;
        byte Byte(int address) => address switch
        {
            FieldPositionReader.AddressFieldNumModels => 16,
            _ when address == FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId => (byte)modelIndex,
            _ when address == npc + FieldNavigationObjectReader.VisibilityOffset => 1,
            // Talk switched on: TLKON writes 0 here.
            _ when address == npc + FieldNavigationNpcReader.TalkDisabledOffset => 0,
            _ => 0xFF
        };

        var reader = new FieldNavigationNpcReader(
            Int32, Int16, Byte,
            (_, _) => [],
            _ => [definition],
            isLineEnabled: _ => true);
        return reader.ReadTargets(new FieldPositionSnapshot(1, definition.FieldId, 0, 0, 0, 0, 0, 0))
            .Cast<FieldNavigationTarget?>()
            .SingleOrDefault();
    }

    private static string Describe(FieldNavigationTarget? target) =>
        target is { } found
            ? $"{found.Label} at {found.X},{found.Y},{found.Z} trigger {found.TriggerEntityId} line {found.TriggerLine.HasValue}"
            : "no target";

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Field NPC talk counters: " + message);
        }
    }
}
