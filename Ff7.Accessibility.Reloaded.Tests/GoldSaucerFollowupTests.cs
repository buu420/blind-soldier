using Ff7.Accessibility.Reloaded;

internal static class GoldSaucerFollowupTests
{
    private const int EventTable = 0x02404000;
    private static readonly (int Entity, string Label)[] Jockeys =
        [(4, "Joe"), (5, "Jockey"), (6, "Jockey"), (7, "Jockey"), (8, "Jockey"), (9, "Ester")];

    public static void Run(string? dataRoot = null)
    {
        var definitions = FieldStoryEventCatalog.CreateAllFields();
        var moment = 442;
        var enabled = true;
        var story = new FieldStoryTargetReader(_ => 0,
            _ => 0, address => address == FieldNavigationObjectReader.AddressFieldBankBase ? (byte)(moment & 255) :
                address == FieldNavigationObjectReader.AddressFieldBankBase + 1 ? (byte)(moment >> 8) : (byte)0, definitions, entity => enabled && entity == 18);
        var position = new FieldPositionSnapshot(1, 505, 0, 0, 0, 0, 0, 0);
        var targets = story.ReadTargets(position);
        Equal(1, targets.Count, "Story must offer the exit after Cait Sith joins");
        var target = targets.Single();
        Equal("Take the Battle Square exit", target.Label, "Cait Sith joins in Wonder Square, not the terminal");
        Equal(new FieldNavigationTriggerLine(30, -852, 0, -8, -815, 0), target.TriggerLine!.Value,
            "walk to the native Battle Square tube");
        Equal(18, definitions.Single(d => d.FieldId == 505 && d.Label == target.Label).RequiredEnabledLineEntityId, "native exit owner");
        foreach (var availableMoment in new[] {443, 444})
        {
            moment = availableMoment;
            Equal(target.Label, story.ReadTargets(position).Single().Label, "remaining first-visit stages");
        }
        enabled = false;
        Equal(0, story.ReadTargets(position).Count, "disabled exit is not offered");
        enabled = true;
        foreach (var otherMoment in new[] {439, 440, 441, 445, 580, 598})
        {
            moment = otherMoment;
            Equal(false, story.ReadTargets(position).Any(t => t.Label == target.Label), "first-visit stage only");
        }

        foreach (var (entity, label) in Jockeys)
        {
            var visible = true;
            var talkDisabled = false;
            byte model = 1;
            var npc = EventTable + FieldNavigationObjectReader.FieldEventDataStride;
            byte ReadByte(int address)
            {
                if (address == FieldPositionReader.AddressFieldNumModels) return 2;
                if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                    address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256)
                    return address == FieldNavigationObjectReader.AddressFieldModelIdArray + entity ? model : (byte)0xff;
                if (address == npc + FieldNavigationObjectReader.VisibilityOffset) return visible ? (byte)1 : (byte)0;
                if (address == npc + FieldNavigationNpcReader.TalkDisabledOffset) return talkDisabled ? (byte)1 : (byte)0;
                return 0;
            }
            var reader = new FieldNavigationNpcReader(
                a => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? EventTable : 0,
                _ => 48, ReadByte, (_, _) => ["……”"], _ => []);
            var room = new FieldPositionSnapshot(1, 512, 0, 0, 0, 0, 0, 0);
            var occupants = reader.ReadTargets(room);
            Equal(1, occupants.Count, $"waiting-room entity {entity} must appear");
            Equal(label, occupants.Single().Label, "visible waiting-room occupant");
            visible = false;
            Equal(0, reader.ReadTargets(room).Count, "Ester leaving the room and hidden racers are omitted");
            visible = true;
            talkDisabled = true;
            Equal(0, reader.ReadTargets(room).Count, "script-owned actors are not talk targets");
            talkDisabled = false;
            model = 0xff;
            Equal(0, reader.ReadTargets(room).Count, "unloaded actors are omitted");
        }

        if (dataRoot is null) return;
        var native = new FieldScriptNavigationCatalog(dataRoot);
        var exit = native.ReadField(505).Exits.Single(e => e.TriggerEntityId == 18);
        Equal(499, exit.DestinationFieldIds!.Single(), "native exit leads directly to Battle Square");
        Equal(exit.TriggerLine, target.TriggerLine, "catalog matches installed LINE geometry");
        foreach (var (entity, _) in Jockeys)
        {
            Equal(true, native.ReadScriptOpcodes(512, entity, 0).Any(o => o.Opcode == 0xA1), "occupant loads a model");
            Equal(true, native.ReadScriptOpcodes(512, entity, 1).Any(o => o.Opcode == 0x40), "occupant has native Talk text");
        }
    }

    private static void Equal<T>(T expected, T actual, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Gold Saucer follow-up: {why}: expected {expected}, got {actual}");
    }
}
