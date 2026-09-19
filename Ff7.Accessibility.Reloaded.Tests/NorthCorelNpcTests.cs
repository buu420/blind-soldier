using Ff7.Accessibility.Reloaded;

internal static class NorthCorelNpcTests
{
    private const int EventTable = 0x02404000;
    private static readonly (int Field, int Entity, string Label)[] Residents =
    [
        (450, 13, "Barret"), (450, 20, "Man"), (450, 21, "General store shopkeeper"),
        (450, 22, "Man"), (450, 23, "Man"), (450, 24, "Weapon shopkeeper"),
        (450, 25, "Item shopkeeper"),
        (453, 7, "Woman"), (453, 8, "Old man"),
        (454, 6, "Old woman"), (454, 7, "Man"), (454, 8, "Boy"),
        (455, 11, "Woman"), (455, 12, "Old man"), (455, 13, "Girl"),
        (455, 14, "Old woman"), (455, 15, "Dog"),
        (456, 11, "Innkeeper"), (456, 12, "Man")
    ];
    private static readonly (int Field, int Entity, int Line, FieldNavigationTriggerLine Geometry)[] Counters =
    [
        (450, 21, 7, new(45, -276, 0, -74, -350, 0)),
        (450, 24, 5, new(-150, -300, 0, -150, -404, 0)),
        (450, 25, 6, new(31, -203, 0, 127, -199, 0)),
        (455, 12, 3, new(31, 15, 0, 81, 34, 0)),
        (455, 14, 5, new(-3, 243, 0, -62, 200, 0))
    ];

    public static void Run(string? installedDataRoot = null)
    {
        foreach (var row in Residents)
        {
            var m = new Memory(row.Entity);
            var reader = m.Reader();
            var pos = new FieldPositionSnapshot(1, row.Field, 0, 0, 0, 0, 0, 0);
            var target = reader.ReadTargets(pos).SingleOrDefault();
            Equal(row.Label, target.Label, $"visible resident {row.Field}:{row.Entity}");
            Equal($"npc:{row.Field}:{row.Entity}", target.StableId, "stable native identity");
            m.Visible = false;
            Equal(0, reader.ReadTargets(pos).Count, "hidden resident is omitted");
            m.Visible = true;
            m.ModelId = 0xff;
            Equal(0, reader.ReadTargets(pos).Count, "unloaded model is omitted");
            m.ModelId = 0;
            Equal(0, reader.ReadTargets(pos).Count, "player model is omitted");
        }
        foreach (var row in Counters)
        {
            var m = new Memory(row.Entity) { TalkDisabled = true };
            var reader = m.Reader();
            var pos = new FieldPositionSnapshot(1, row.Field, 0, 0, 0, 0, 0, 0);
            var target = reader.ReadTargets(pos).Single();
            Equal(row.Line, target.TriggerEntityId, "counter native line entity");
            Equal(row.Geometry, target.TriggerLine, "counter native geometry");
            Equal((int)Math.Round((row.Geometry.StartX + row.Geometry.EndX) / 2d, MidpointRounding.AwayFromZero), target.X, "counter approach x");
            Equal((int)Math.Round((row.Geometry.StartY + row.Geometry.EndY) / 2d, MidpointRounding.AwayFromZero), target.Y, "counter approach y");
            m.LineEnabled = false;
            Equal(0, reader.ReadTargets(pos).Count, "disabled counter line is omitted");
        }

        var resident = new Memory(20) { TalkDisabled = true };
        Equal(0, resident.Reader().ReadTargets(new(1, 450, 0, 0, 0, 0, 0, 0)).Count,
            "ordinary resident still requires native Talk enabled");
        resident.TalkDisabled = false;
        Equal(0, resident.Reader([(450, 20)]).ReadTargets(new(1, 450, 0, 0, 0, 0, 0, 0)).Count,
            "existing object ownership is preserved");

        foreach (var field in new[] {450, 453, 454, 455, 456})
        {
            var m = new Memory(1);
            var reader = m.Reader(definitions: [new(field, 1, "ad", [1])]);
            Equal(0, reader.ReadTargets(new(1, field, 0, 0, 0, 0, 0, 0)).Count,
                "event dialogue must not manufacture an NPC");
        }
        if (installedDataRoot is not null) CheckInstalledScripts(installedDataRoot);
    }

    private static void CheckInstalledScripts(string root)
    {
        var catalog = new FieldScriptNavigationCatalog(root);
        foreach (var row in Residents)
            Equal(true, catalog.ReadScriptOpcodes(row.Field, row.Entity, 0).Any(op => op.Opcode == 0xA1),
                $"installed model loader {row.Field}:{row.Entity}");
        foreach (var row in Counters)
        {
            var line = catalog.ReadScriptOpcodes(row.Field, row.Line, 0).Single(op => op.Opcode == 0xD0);
            var bytes = line.Bytes.ToArray();
            var actual = new FieldNavigationTriggerLine(
                BitConverter.ToInt16(bytes, 1), BitConverter.ToInt16(bytes, 3), BitConverter.ToInt16(bytes, 5),
                BitConverter.ToInt16(bytes, 7), BitConverter.ToInt16(bytes, 9), BitConverter.ToInt16(bytes, 11));
            Equal(row.Geometry, actual, "installed counter geometry");
            Equal(true, catalog.ReadScriptOpcodes(row.Field, row.Line, 4).Any(op => op.Opcode == 0x31),
                "installed counter responds to Action");
        }
        // The three shop borders are the counters whose Action delegates straight to
        // the shopkeeper this target is named after, so the label and the approach
        // point describe one model: wsline/tsline reach script 3 on wepsp/tolsp, and
        // bzline1 reaches man2's script 9. Without this the geometry could be right
        // while pointing at somebody else's counter. The ncoin3 chairs are left out
        // on purpose - they delegate through the shared ad entity, not the seated
        // model, so the same assertion would not be true of them.
        foreach (var (entity, line) in new[] { (24, 5), (25, 6), (21, 7) })
            Equal(true, catalog.ReadScriptOpcodes(450, line, 4)
                    .Any(op => op.Opcode == 0x03 && op.Bytes.Count > 1 && op.Bytes[1] == entity),
                $"installed counter {line} delegates to shopkeeper {entity}");
        // The anonymous residents in the report have native MESSAGE dialogue,
        // while the dog's Talk only animates/barks and shops delegate via LINE.
        Equal(true, catalog.ReadScriptOpcodes(450, 20, 1).Any(op => op.Opcode == 0x40), "resident dialogue");
        Equal(true, catalog.ReadScriptOpcodes(455, 15, 1).Any(op => op.Opcode == 0xF1), "dog bark");
        Equal(false, catalog.ReadScriptOpcodes(450, 24, 1).Any(op => op.Opcode == 0x40), "counter-only shopkeeper");
    }

    private sealed class Memory(int entity)
    {
        public bool Visible = true, TalkDisabled, LineEnabled = true;
        public byte ModelId = 1;
        private const int Npc = EventTable + FieldNavigationObjectReader.FieldEventDataStride;
        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressFieldNumModels) return 2;
            if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256)
                return address == FieldNavigationObjectReader.AddressFieldModelIdArray + entity ? ModelId : (byte)0xff;
            if (address == Npc + FieldNavigationObjectReader.VisibilityOffset) return Visible ? (byte)1 : (byte)0;
            if (address == Npc + FieldNavigationNpcReader.TalkDisabledOffset) return TalkDisabled ? (byte)1 : (byte)0;
            return 0;
        }
        public FieldNavigationNpcReader Reader(
            IEnumerable<(int, int)>? excluded = null, IReadOnlyList<FieldScriptNpcDefinition>? definitions = null) =>
            new(a => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? EventTable :
                    a == Npc + FieldNavigationObjectReader.PositionXOffset ? 500 * FieldNavigationObjectReader.ModelPositionFixedPointScale : 0,
                _ => 48, ReadByte, (_, _) => ["Cloud", "Delegated dialogue is not the visible speaker."],
                _ => definitions ?? [], excluded, _ => LineEnabled);
    }
    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
