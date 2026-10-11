using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Reviewed stock/Echo-S 1.24 identities for the slums, shop, bar and hideout.</summary>
public static class Sector7NavigationLayout
{
    public static bool IsField(int fieldId) => fieldId is 151 or 152 or 154 or 155;

    // Prefixes from the current official IRO: base, Aavock/ESUI, FT and Retouch.
    // Text is excluded at the native header's text offset, as in the loaded reader.
    private static readonly IReadOnlyDictionary<int, string[]> EchoPrefixes = new Dictionary<int, string[]>
    {
        [151] = ["4C4DCB40F30C414CE4A6CC70C6FBE0E7C3C4762F195B4305C8D879447C5B5D9E", "DFD728E12885FE99B1BF8990CBDE4492A9DEA5B5A5D0341DD9C361989D300A09", "9F47E4A4BC9F7C71CA2008C9B0A54DED58437C3B84A0583501B9FAB01C04E919", "6B6446B8C20DE7998A99D44B932C5433F821A67BB92A8E4E475C632797EFC14D"],
        [152] = ["18259AFF851E33563D6CBF38F7C235A10548AA8B99BC034652CC50F2417AC42D", "3029B48044CEC553DEBC99FF1284836CC439131FB757A07534645EAAD41BB605", "81818289906A54D43DEF9FC8921CA1F75A33E20EBFC491A38D63BA270921D634", "FD86FAF7E38575CCA7C7179D5C323AD88EE9ED6FCD830CEAD6101FCD95AEF33B"],
        [154] = ["DAFF353BD4F300D18232BC2865E96BD80DA38C4EE34E490440CC88ABF90703EC", "C6BF75614285C8E0B5794E5A0F81CF3F9821152C96FB1BB05EA506F2CF824E35", "2F423D51D698FD08D93566DB4D5A152407EEE6A38EC72FBF873054CC102316BE", "1516FF8F810CBEAB1C34806F716EC0BA72CB7D59DEBFA62C378DF4FC18E3F089"],
        [155] = ["F9DB8C28C21F1F5EFABE464B14D6CDC61B1A67069E0851F2590A7DFFB79F2019", "113FBF7AD5A4415C0CD46158C0740070BC77F88497E4146FD107CFB5F357D863", "B0DFA13541185E5472E6CDBD88AE70F97A59A41FB5F6E3ACCDBE4FAD323D8589", "A12D84E058EDF0AFAAEE815A4911DD3800C80516AD2F019B6958208C1609A581"]
    };

    public static bool IsEcho(int fieldId, string hash) =>
        EchoPrefixes.TryGetValue(fieldId, out var hashes) && hashes.Contains(hash, StringComparer.OrdinalIgnoreCase);

    internal static bool IsStock(int fieldId, IReadOnlyList<string> names)
    {
        bool Has(int id, string name) => id < names.Count && names[id].Equals(name, StringComparison.OrdinalIgnoreCase);
        return fieldId switch
        {
            151 => Has(17, "border4") && Has(22, "ballet"),
            152 => Has(2, "border3") && Has(4, "oldm1"),
            154 => Has(15, "border2") && Has(27, "pinball"),
            155 => Has(8, "pinball") && Has(12, "ballet"),
            _ => true
        };
    }

    private static readonly IReadOnlyDictionary<(int Field, int Entity), string> StockLabels =
        new Dictionary<(int, int), string>
        {
            [(151,22)]="Barret", [(151,27)]="Johnny", [(151,28)]="Man", [(151,29)]="Woman", [(151,30)]="Boy", [(151,31)]="Woman",
            [(152,4)]="Item and Materia shopkeeper",
            [(154,19)]="Barret", [(154,20)]="Tifa", [(154,22)]="Jessie", [(154,23)]="Biggs", [(154,24)]="Wedge", [(154,25)]="Marlene",
            [(154,26)]="Biggs", [(154,28)]="Wedge", [(154,29)]="Jessie",
            [(155,12)]="Barret", [(155,13)]="Tifa", [(155,15)]="Wedge", [(155,16)]="Jessie", [(155,17)]="Marlene"
        };
    private static readonly IReadOnlyDictionary<(int Field, int Entity), string> EchoLabels =
        new Dictionary<(int, int), string>
        {
            [(151,12)]="Barret", [(151,17)]="Johnny", [(151,18)]="Man", [(151,19)]="Woman", [(151,20)]="Boy", [(151,21)]="Woman",
            [(152,4)]="Item and Materia shopkeeper",
            [(154,8)]="Barret", [(154,9)]="Tifa", [(154,10)]="Jessie", [(154,11)]="Biggs", [(154,12)]="Wedge", [(154,13)]="Marlene",
            [(155,6)]="Barret", [(155,7)]="Tifa", [(155,8)]="Biggs", [(155,9)]="Wedge", [(155,10)]="Jessie", [(155,11)]="Marlene"
        };

    public static FieldNavigationTriggerLine BarDoor => new(290,379,76,326,364,76);
    public static FieldNavigationTriggerLine DownLift => new(-52,187,0,-8,173,0);
    public static FieldNavigationTriggerLine UpLift => new(155,-14,0,129,-65,0);

    internal static FieldScriptNavigationReadResult Apply(int field, bool echo, FieldScriptNavigationReadResult result)
    {
        if (!IsField(field)) return result;
        var labels = echo ? EchoLabels : StockLabels;
        var npcs = result.Npcs.Select(npc =>
        {
            var reviewed = npc with { ReviewedLabel = labels.GetValueOrDefault((field, npc.EntityId), "") };
            // mds7_im/border3 Go4 tests the action button and REQEWs oldm1's
            // Talk (03 04 C1). The collision-disabled model stands behind the counter.
            return field == 152 && npc.EntityId == 4 ? reviewed with
            {
                InteractionLineEntityId = 2,
                InteractionLine = new(70,19,0,-65,-11,0),
                InteractionLineRunsTalk = true
            } : reviewed;
        }).ToArray();
        if (field is not (154 or 155)) return result with { Npcs = npcs, UsesEchoSector7Layout = echo };
        var segment = field == 154 ? DownLift : UpLift;
        var destination = field == 154 ? 155 : 154;
        // Other stock bar LINEs animate story scenes that eventually MAPJUMP elsewhere.
        // The pinball LINE is the player's actual room connection.
        var exits = result.Exits.Where(e => e.TriggerLine == segment && e.DestinationFieldIds!.Contains(destination))
            .Select(e => e with { DestinationFieldIds = [destination] }).ToArray();
        var guards = new Dictionary<string, IReadOnlyList<FieldScriptExitGuard>>(StringComparer.Ordinal);
        foreach (var exit in exits)
        {
            var tests = field == 154
                ? new List<FieldScriptGuardTest> { new(1,0,true,false,4,72,true), new(5,26,false,false,1,1,true) }
                : new List<FieldScriptGuardTest> { new(5,21,false,false,1,1,true) };
            // Stock AD2 enters the Nibelheim flashback at moment 96 instead of the hideout.
            if (field == 154 && !echo) tests.Add(new(1,0,true,false,1,96,true));
            guards[exit.StableId] = [new(destination, [tests])];
        }
        return result with { Npcs = npcs, Exits = exits, ExitGuards = guards, UsesEchoSector7Layout = echo };
    }

    private static readonly IReadOnlyDictionary<(int Field, int StockEntity), int> EchoEntities =
        new Dictionary<(int, int), int>
        {
            [(151,17)]=8, [(151,18)]=9, [(151,19)]=10, [(151,21)]=11, [(151,22)]=12,
            [(151,23)]=13, [(151,24)]=14, [(151,25)]=15, [(151,26)]=16, [(151,27)]=17,
            [(151,28)]=18, [(151,29)]=19, [(151,30)]=20, [(151,31)]=21,
            [(152,2)]=2, [(152,3)]=3, [(152,4)]=4,
            [(154,14)]=3, [(154,15)]=4, [(154,16)]=5, [(154,17)]=6, [(154,18)]=7,
            [(154,19)]=8, [(154,20)]=9, [(154,22)]=10, [(154,23)]=11, [(154,24)]=12,
            [(154,25)]=13, [(154,27)]=14,
            [(155,8)]=2, [(155,9)]=3, [(155,10)]=4, [(155,11)]=5, [(155,12)]=6, [(155,13)]=7,
            [(155,14)]=8, [(155,15)]=9, [(155,16)]=10, [(155,17)]=11, [(155,20)]=12
        };

    internal static FieldStoryEventDefinition? RemapStory(FieldStoryEventDefinition definition, FieldScriptNavigationReadResult current)
    {
        if (definition.FieldId is 154 or 155 && definition.SourceEntityName?.Equals("pinball", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lift = current.Exits.SingleOrDefault();
            if (lift.TriggerLine is null) return null;
            definition = definition with
            {
                TriggerLine = lift.TriggerLine,
                RequiredEnabledLineEntityId = lift.TriggerEntityId,
                UsesPlayerCollisionRadius = true
            };
            // This line already uses the loaded entity id.
            return definition;
        }
        if (!current.UsesEchoSector7Layout) return definition;
        int? Remap(int entity) => entity < 0 ? entity : EchoEntities.TryGetValue((definition.FieldId, entity), out var mapped) ? mapped : null;
        var id = Remap(definition.EntityId);
        var line = definition.RequiredEnabledLineEntityId is { } originalLine ? Remap(originalLine) : null;
        if (id is null || definition.RequiredEnabledLineEntityId.HasValue && line is null) return null;
        return definition with { EntityId = id.Value, RequiredEnabledLineEntityId = line };
    }

    internal static FieldNavigationObjectDefinition? RemapObject(
        FieldNavigationObjectDefinition definition, FieldScriptNavigationReadResult current) =>
        !current.UsesEchoSector7Layout ? definition :
        EchoEntities.TryGetValue((definition.FieldId, definition.EntityId), out var mapped)
            ? definition with { EntityId = mapped } : null;

    /// <summary>Lift approaches use the current leader's native strict LINE touch radius.</summary>
    public static IReadOnlyList<FieldNavigationTarget> ApplyLiftApproaches(
        FieldPositionSnapshot position, IReadOnlyList<FieldNavigationTarget> exits, ILegacyAddressSpace memory)
    {
        if (position.FieldId is not (154 or 155) || exits.Count == 0) return exits;
        if (!memory.TryReadByte((uint)FieldScriptContextReader.AddressCurrentModule, out var module) || module != 1 ||
            !memory.TryReadUInt16((uint)FieldScriptContextReader.AddressCurrentFieldId, out var field) || field != position.FieldId ||
            !memory.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var count) || position.ModelIndex < 0 || position.ModelIndex >= count ||
            !memory.TryReadUInt32((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, out var events) || events == 0)
            return [];
        var address = (ulong)events + (uint)position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.CollisionRadiusOffset;
        if (address + sizeof(ushort) > (ulong)uint.MaxValue + 1 || !memory.TryReadUInt16((uint)address, out var radius) || radius <= 1 || radius > short.MaxValue)
            return [];
        var flagAddress = (uint)(FieldScriptExitGuards.AddressTemporaryFieldBank + (position.FieldId == 154 ? 26 : 21));
        if (!memory.TryReadByte(flagAddress, out var busy) || busy == 1 ||
            !memory.TryReadUInt16((uint)FieldNavigationObjectReader.AddressFieldBankBase, out var moment) ||
            position.FieldId == 154 && moment < 72 ||
            !memory.TryReadByte(flagAddress, out var busyAfter) || busy != busyAfter ||
            !memory.TryReadUInt16((uint)FieldNavigationObjectReader.AddressFieldBankBase, out var momentAfter) || moment != momentAfter ||
            !memory.TryReadByte((uint)FieldScriptContextReader.AddressCurrentModule, out var moduleAfter) || module != moduleAfter ||
            !memory.TryReadUInt16((uint)FieldScriptContextReader.AddressCurrentFieldId, out var fieldAfter) || field != fieldAfter ||
            !memory.TryReadUInt32((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, out var eventsAfter) || events != eventsAfter ||
            !memory.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var countAfter) || count != countAfter ||
            !memory.TryReadUInt16((uint)address, out var radiusAfter) || radius != radiusAfter)
            return [];
        return exits.Where(exit => position.FieldId != 154 || exit.TriggerEntityId != 15 || moment != 96)
            .Select(exit => exit.TriggerLine == DownLift || exit.TriggerLine == UpLift
                ? exit with { LineActivationRadius = radius } : exit).ToArray();
    }

    public static string? ExitLabel(FieldNavigationTarget target) => target switch
    {
        { FieldId:151, TriggerLine: { } line, DestinationFieldIds: { } fields } when line == BarDoor && fields.Contains(154) => "Enter Seventh Heaven",
        { FieldId:154, TriggerLine: { } line, DestinationFieldIds: { } fields } when line == DownLift && fields.Contains(155) => "Use pinball lift to AVALANCHE hideout",
        { FieldId:155, TriggerLine: { } line, DestinationFieldIds: { } fields } when line == UpLift && fields.Contains(154) => "Use pinball lift to Seventh Heaven",
        { StableId:"gateway:154:0:151" } => "Leave Seventh Heaven for Sector 7 slums",
        { StableId:"gateway:151:2:152" } => "Enter Item and Materia shop",
        _ => null
    };
}
