using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Gongaga village, field 518 gongaga.
///
/// <para>The only way back out of the village is entity 11 "line4", a native LINE whose
/// [OK] handler MAPJUMPs to the world map or to the jungle depending on bank 3 address 132
/// bit 6. The six native gateways are all building doors, so without that line the village
/// lists no way out at all.</para>
///
/// <para>Aeris and Tifa stand in the village after the Zack's-parents scene and their
/// conversations are reached the way the game reaches most counters: a LINE in front of
/// them, not their own Talk script, which is a bare RET. Aeris is reached from line1
/// (entity 8) and Tifa from line2/line3 (entities 9 and 10) by way of the event group's
/// script 4. Each conversation is pending only while its native flag is set - bank 3
/// address 129 bit 1 for Aeris, bit 2 for Tifa - and the scripts clear that bit as the
/// conversation begins.</para>
/// </summary>
internal static class GongagaNavigationTests
{
    private const int Gongaga = 518;
    private const int ExitLineEntity = 11;
    private const int AerithEntity = 16;
    private const int TifaEntity = 15;
    private const int AerithLineEntity = 8;
    private const int TifaLineEntity = 9;

    // bank 3 address 129: bit 1 is Aeris's pending conversation, bit 2 is Tifa's.
    private const byte AerithPending = 0x02;
    private const byte TifaPending = 0x04;

    public static void Run()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(gameRoot);
        var field = catalog.ReadField(Gongaga);
        TheVillageOffersItsNativeWayOut(field);
        TheWayOutIsNamedWithoutPromisingADestination(field);
        TheConversationLinesNeverBecomeExits(field);
        CompanionsAreReachedAtTheirNativeConversationLines(field);
        AnEmptyPersonalTalkDoesNotWithholdTheProxy(field);
        CompanionTargetsHonourTheNativeState(field);
    }

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Run();
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return;
        }

        TheExitAndBothConversationsAreReachable(
            new FieldScriptNavigationCatalog(gameRoot).ReadField(Gongaga),
            createWalkmeshReader);
    }

    /// <summary>
    /// The reported defect: once inside the village the player is offered six doors and no
    /// way back out. The story record that would have covered it is scoped to game moments
    /// 641-651 and the village is reachable long before that, so the exit has to come from
    /// the field's own script.
    /// </summary>
    private static void TheVillageOffersItsNativeWayOut(FieldScriptNavigationReadResult field)
    {
        var exits = field.Exits
            .Where(exit => exit.TriggerEntityId == ExitLineEntity)
            .ToArray();
        Equal(1, exits.Length, "Gongaga's path out of the village is a navigable exit");

        // 17 is wm16, the world map; 514 is gonjun1, the jungle. The field picks between
        // them on bank 3 address 132 bit 6, so the label must not promise either one.
        Equal(
            "17,514",
            string.Join(',', exits[0].DestinationFieldIds ?? Array.Empty<int>()),
            "it leads to the world map or to the jungle, as the native branch decides");
        Equal(
            new FieldNavigationTriggerLine(-798, -1333, 17, -529, -1386, 17),
            exits[0].TriggerLine,
            "and it keeps line4's own geometry so navigation stops on the native line");
    }

    /// <summary>
    /// The destination is chosen natively, and reaching the line is not leaving: the player
    /// still has to press Confirm. The label has to say so without naming a destination the
    /// field has not decided on yet.
    /// </summary>
    private static void TheWayOutIsNamedWithoutPromisingADestination(
        FieldScriptNavigationReadResult field)
    {
        var exit = field.Exits.Single(candidate => candidate.TriggerEntityId == ExitLineEntity);
        var resolver = new FieldExitLabelResolver(
            _ => FieldMapNameResolution.Unknown,
            () => "Gongaga Village");
        Equal(
            "Leave Gongaga; press Confirm",
            resolver.Resolve([exit]).Single().Label,
            "the way out is announced as a Confirm press, not as a named doorway");
    }

    /// <summary>
    /// line1 is Aeris's conversation and line2/line3 are Tifa's. They are interactions, not
    /// doorways, and must never be steered to as a way out of the village.
    /// </summary>
    private static void TheConversationLinesNeverBecomeExits(
        FieldScriptNavigationReadResult field)
    {
        foreach (var entity in new[] { AerithLineEntity, TifaLineEntity, 10 })
        {
            Equal(
                0,
                field.Exits.Count(exit => exit.TriggerEntityId == entity),
                $"field 518 entity {entity} is a conversation, not an exit");
        }
    }

    private static void CompanionsAreReachedAtTheirNativeConversationLines(
        FieldScriptNavigationReadResult field)
    {
        var memory = new GongagaMemory();
        var targets = memory.Read(field);

        var aerith = Single(targets, "Aerith");
        Equal(AerithLineEntity, aerith.TriggerEntityId, "Aeris is reached from line1");
        Equal(
            new FieldNavigationTriggerLine(-111, -284, 17, -69, -74, 17),
            aerith.TriggerLine,
            "at line1's own native segment");

        var tifa = Single(targets, "Tifa");
        Equal(TifaLineEntity, tifa.TriggerEntityId, "Tifa is reached from line2");
        Equal(
            new FieldNavigationTriggerLine(321, 559, 17, 173, 800, 17),
            tifa.TriggerLine,
            "at line2's own native segment");
    }

    /// <summary>
    /// Both companions have a Talk script that is a bare RET. That is ordinary for an actor
    /// whose interaction is delegated to a LINE, and it is exactly how the reviewed shop and
    /// inn counters already work, so it must not withhold the target.
    /// </summary>
    private static void AnEmptyPersonalTalkDoesNotWithholdTheProxy(
        FieldScriptNavigationReadResult field)
    {
        foreach (var entity in new[] { AerithEntity, TifaEntity })
        {
            var definition = field.Npcs.FirstOrDefault(npc => npc.EntityId == entity);
            Equal(
                0,
                definition.DialogIds?.Count ?? 0,
                $"field 518 entity {entity} carries no dialogue of its own");
        }

        var memory = new GongagaMemory();
        memory.TalkDisabled = true;
        var targets = memory.Read(field);
        Equal(1, targets.Count(target => target.Label == "Aerith"), "Aeris is still offered");
        Equal(1, targets.Count(target => target.Label == "Tifa"), "Tifa is still offered");
    }

    private static void CompanionTargetsHonourTheNativeState(
        FieldScriptNavigationReadResult field)
    {
        // A conversation already had clears its pending bit, so it stops being offered.
        // Visibility alone does not offer this conversation, even if the field places
        // the character again during a later story visit.
        var completed = new GongagaMemory { PendingConversations = 0 };
        Equal(0, completed.Read(field).Count(target => target.Label is "Aerith" or "Tifa"),
            "a finished conversation is no longer somewhere to walk to");

        var aerithOnly = new GongagaMemory { PendingConversations = AerithPending };
        var aerithTargets = aerithOnly.Read(field);
        Equal(1, aerithTargets.Count(target => target.Label == "Aerith"), "Aeris is still pending");
        Equal(0, aerithTargets.Count(target => target.Label == "Tifa"), "Tifa's is done");

        var tifaOnly = new GongagaMemory { PendingConversations = TifaPending };
        var tifaTargets = tifaOnly.Read(field);
        Equal(0, tifaTargets.Count(target => target.Label == "Aerith"), "Aeris's is done");
        Equal(1, tifaTargets.Count(target => target.Label == "Tifa"), "Tifa is still pending");

        var hidden = new GongagaMemory();
        hidden.Hidden.Add(AerithEntity);
        Equal(0, hidden.Read(field).Count(target => target.Label == "Aerith"),
            "a companion the field has not made visible is not offered");

        var unloaded = new GongagaMemory();
        unloaded.Unloaded.Add(TifaEntity);
        Equal(0, unloaded.Read(field).Count(target => target.Label == "Tifa"),
            "a companion with no loaded model is not offered");

        var deadLine = new GongagaMemory();
        deadLine.EnabledLines.Remove(AerithLineEntity);
        Equal(0, deadLine.Read(field).Count(target => target.Label == "Aerith"),
            "a conversation whose native line is not live is not offered");

        // Whoever the player is leading is not somebody to walk to.
        var leading = new GongagaMemory { PlayerEntity = TifaEntity };
        Equal(0, leading.Read(field).Count(target => target.Label == "Tifa"),
            "the character the player is controlling is not a target");
    }

    private static void TheExitAndBothConversationsAreReachable(
        FieldScriptNavigationReadResult field,
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new GongagaMemory();
        var targets = memory.Read(field);
        var exit = field.Exits.Single(candidate => candidate.TriggerEntityId == ExitLineEntity);

        // Native placements the field itself uses, so every start point is a triangle the
        // game puts somebody on: the old woman's corner, the flower bed, and Barret's spot.
        (FieldPositionSnapshot Position, FieldNavigationTarget Target, string Label)[] cases =
        [
            (new(1, Gongaga, 0, -658, -890, 17, 77, 0), exit, "the way out of the village"),
            (new(1, Gongaga, 0, -712, -819, 17, 6, 0), exit, "the way out from the flower bed"),
            (new(1, Gongaga, 0, -35, 315, 17, 63, 0), Single(targets, "Aerith"), "Aeris"),
            (new(1, Gongaga, 0, -223, 831, 17, 54, 0), Single(targets, "Tifa"), "Tifa")
        ];

        foreach (var (position, target, label) in cases)
        {
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(Gongaga));
            Equal(
                true,
                planner.TryBuildRoute(position, target, out var route),
                $"{label} must be reachable on Gongaga's installed triangles: {planner.LastDiagnostic}");
            Equal(
                target.TriggerLine,
                route.TargetTriggerLine,
                $"{label} must keep its native activation line");
        }
    }

    /// <summary>
    /// Native state for field 518. Every entity the catalog found is given its own model
    /// slot, visible and talkable, with both conversation lines live, so that each test
    /// changes exactly the one condition it is about.
    /// </summary>
    private sealed class GongagaMemory
    {
        private const int EventTable = 0x02404000;
        private const int Bank3 = FieldNavigationObjectReader.AddressFieldBankBase + 0x100;

        public readonly HashSet<int> Hidden = [];
        public readonly HashSet<int> Unloaded = [];
        public readonly HashSet<int> EnabledLines = [AerithLineEntity, TifaLineEntity, 10];
        public byte PendingConversations = AerithPending | TifaPending;
        public bool TalkDisabled;
        public int PlayerEntity = 1;

        public IReadOnlyList<FieldNavigationTarget> Read(FieldScriptNavigationReadResult field)
        {
            var reader = new FieldNavigationNpcReader(
                address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr
                    ? EventTable
                    : 0,
                _ => 48,
                ReadByte,
                (_, _) => Array.Empty<string>(),
                _ => field.Npcs,
                null,
                line => EnabledLines.Contains(line));
            return reader.ReadTargets(
                new FieldPositionSnapshot(1, Gongaga, PlayerEntity, 0, 0, 0, 0, 0));
        }

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressFieldNumModels)
            {
                return 255;
            }

            if (address == Bank3 + 129)
            {
                return PendingConversations;
            }

            if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256)
            {
                var entity = address - FieldNavigationObjectReader.AddressFieldModelIdArray;
                return Unloaded.Contains(entity) ? (byte)0xff : (byte)entity;
            }

            var offset = address - EventTable;
            if (offset < 0)
            {
                return 0;
            }

            var model = offset / FieldNavigationObjectReader.FieldEventDataStride;
            var field = offset % FieldNavigationObjectReader.FieldEventDataStride;
            if (field == FieldNavigationObjectReader.VisibilityOffset)
            {
                return Hidden.Contains(model) ? (byte)0 : (byte)1;
            }

            if (field == FieldNavigationNpcReader.TalkDisabledOffset)
            {
                return TalkDisabled ? (byte)1 : (byte)0;
            }

            return 0;
        }
    }

    private static FieldNavigationTarget Single(
        IReadOnlyList<FieldNavigationTarget> targets,
        string label)
    {
        var matches = targets.Where(target => target.Label == label).ToArray();
        Equal(1, matches.Length, $"exactly one {label} target");
        return matches[0];
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}.");
        }
    }
}
