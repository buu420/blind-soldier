using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

public static class TownInteractionCoverageTests
{
    public static void Run()
    {
        WalkableControlsCanStartNavigation();
        // These are independently identified native player actions, not all the models
        // or automatic callbacks appearing in a field script.
        foreach (var (field, entity) in new[] { (578,16), (582,11), (587,14), (588,14),
                     (529,7), (532,14), (582,14), (287,6), (503,7), (553,8), (620,7), (622,9), (623,16) })
            Check(FieldNavigationObjectCatalog.CreateAllFields().Any(d => d.FieldId == field && d.EntityId == entity),
                $"missing optional action {field}/{entity}");

        var memory = new Memory();
        var levers = TownInteractionObjectCatalog.Create().Single(d => d.FieldId == 582 && d.EntityId == 11);
        var reader = memory.Reader(levers);
        Check(reader.ReadTargets(memory.Position(582)).Count == 1, "enabled Wutai levers are reachable through Objects");
        Check(reader.ReadTargets(memory.Position(582))[0].InteractionRadius == 19, "native LINE radius rather than generic 48 units");
        memory.LineEnabled = false;
        Check(reader.ReadTargets(memory.Position(582)).Count == 0, "disabled LINE cannot survive a cutscene");
        memory.LineEnabled = true;
        memory.Bytes[Memory.Events + FieldNavigationNpcReader.CollisionRadiusOffset] = 0;
        Check(reader.ReadTargets(memory.Position(582)).Count == 0, "missing player geometry is not a guessed arrival range");

        memory = new Memory();
        var door = TownInteractionObjectCatalog.Create().First(d => d.FieldId == 589 && d.TargetKind == FieldNavigationObjectTargetKind.Model);
        memory.Bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + door.EntityId] = 1;
        memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 1;
        reader = memory.Reader(door);
        Check(reader.ReadTargets(memory.Position(589)).Count == 1, "visible talkable sliding door");
        memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset] = 1;
        Check(reader.ReadTargets(memory.Position(589)).Count == 0, "opened door removed when its Talk is disabled");
        memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset] = 0;
        memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 0;
        Check(reader.ReadTargets(memory.Position(589)).Count == 0, "hidden scenery cannot be navigated to");

        memory = new Memory();
        var bell = TownInteractionObjectCatalog.Create().Single(d => d.FieldId == 587);
        Check(memory.Reader(bell).ReadTargets(memory.Position(587)).Count == 0,
            "the bell is not offered while uutai2's AD still holds triangle 114 before the trap");
        memory.SetRequiredState(bell);
        var target = memory.Reader(bell).ReadTargets(memory.Position(587)).Single();
        Check(target.InteractionRadius == 12 && target.Category == FieldNavigationCategory.Objects,
            "bell approach stays inside its activation triangle and remains optional");
        foreach (var (field, entity, label) in new[] { (606, 31, "Temple spirit"), (700, 12, "Cloaked figure"), (709, 18, "Cloaked figure"), (709, 19, "Cloaked figure"), (183, 7, "Reno"), (317, 21, "Large creature"), (725, 2, "Cloud"), (726, 4, "Cloud"), (727, 5, "Cloud") })
        {
            memory = new Memory();
            memory.Bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 1;
            memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 1;
            var npcs = new FieldNavigationNpcReader(memory.ReadInt32, a => (short)(memory.ReadByte(a) | memory.ReadByte(a + 1) << 8),
                memory.ReadByte, (_, _) => Array.Empty<string>(), _ => Array.Empty<FieldScriptNpcDefinition>(), null, _ => true);
            Check(npcs.ReadTargets(memory.Position(field)).Any(n => n.Label == label), $"verified native NPC {field}/{entity}");
            memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset] = 1;
            Check(npcs.ReadTargets(memory.Position(field)).Count == 0, "scene actor with disabled Talk is excluded");
        }
        memory = new Memory();
        var kalmDoor = TownInteractionObjectCatalog.Create().Single(d => d.FieldId == 336 && d.EntityId == 4);
        Check(memory.Reader(kalmDoor).ReadTargets(memory.Position(336)).Count == 1, "closed Kalm door is available");
        memory.Bytes[FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 7] = 1;
        Check(memory.Reader(kalmDoor).ReadTargets(memory.Position(336)).Count == 0, "opened Kalm door is not offered again");
        var rocketDoor = TownInteractionObjectCatalog.Create().Single(d => d.FieldId == 558 && d.EntityId == 13);
        memory.SetMoment(566);
        Check(memory.Reader(rocketDoor).ReadTargets(memory.Position(558)).Count == 0, "backyard door is not described as locked during first visit");
        memory.SetMoment(567);
        Check(memory.Reader(rocketDoor).ReadTargets(memory.Position(558)).Count == 1, "native post-event locked-door interaction is available");
        Console.WriteLine("Town interaction coverage and native-state gating passed.");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var scripts = new FieldScriptNavigationCatalog(root);
        foreach (var group in TownInteractionObjectCatalog.Create().GroupBy(d => d.FieldId))
        {
            var native = scripts.ReadAllScriptOpcodes(group.Key);
            foreach (var row in group)
            {
                var entity = native.Where(s => s.EntityId == row.EntityId).ToArray();
                Check(entity.Length > 0 && entity.All(s => s.EntityName == row.SourceEntityName), $"native entity identity {row.FieldId}/{row.EntityId}");
                if (row.TargetKind == FieldNavigationObjectTargetKind.Line)
                {
                    var line = entity.Where(s => s.ScriptId == 0).SelectMany(s => s.Opcodes)
                        .Single(o => o.Opcode == 0xD0).Bytes.ToArray();
                    for (var axis = 0; axis < 3; axis++)
                    {
                        var midpoint = (BitConverter.ToInt16(line, 1 + axis * 2) + BitConverter.ToInt16(line, 7 + axis * 2)) / 2;
                        Check(midpoint == new[] { row.StaticX, row.StaticY, row.StaticZ }[axis], $"native LINE geometry {row.FieldId}/{row.EntityId}");
                    }
                    Check(entity.Any(s => s.ScriptId != 0 && s.Opcodes.Any(o => o.Opcode != 0)), "LINE has an interaction handler");
                }
                else if (row.TargetKind == FieldNavigationObjectTargetKind.Model)
                    Check(entity.Any(s => s.ScriptId == 0 && s.Opcodes.Any(o => o.Opcode == 0xA1)), "model door loads CHAR");
                else
                {
                    Check(entity.Any(s => s.ScriptId == 0 && s.Opcodes.Any(o => o.Opcode == 0x31)), "background control polls Confirm");
                    // The bell, the front of the hanging scroll, and its back: JIKU's
                    // reverse turn is polled on triangle 95 behind a leader-X guard.
                    var triangle = (row.FieldId, row.StaticX) switch
                    {
                        (587, _) => 138,
                        (588, -440) => 95,
                        _ => 53
                    };
                    Check(entity.SelectMany(s => s.Opcodes).Any(o => o.Opcode == 0x16 && o.Bytes.Count == 8 &&
                        o.Bytes[1] == 0x60 && o.Bytes[6] == 0 &&
                        BitConverter.ToUInt16(o.Bytes.ToArray(), 4) == triangle), "native script tests the authored activation triangle");
                    if (triangle == 95)
                    {
                        var guard = entity.SelectMany(s => s.Opcodes).Single(o => o.Opcode == 0x16 && o.Bytes.Count == 8 &&
                            o.Bytes[1] == 0x60 && BitConverter.ToUInt16(o.Bytes.ToArray(), 2) == 2 && o.Bytes[6] == 2);
                        Check(row.StaticX - row.InteractionRadiusOverride > BitConverter.ToInt16(guard.Bytes.ToArray(), 4),
                            "reverse scroll arrival stays inside the native leader-X guard");
                    }
                    var source = new FlevelDataSource(root);
                    Check(source.TryReadField(row.FieldId, out var encoded), "background control field can be decoded");
                    var data = Ff7LzsDecoder.DecodeFieldFile(encoded);
                    var start = BitConverter.ToInt32(data, 6 + 4 * 4) + 8 + triangle * FieldWalkmeshReader.TriangleSize;
                    var vertices = Enumerable.Range(0, 3).Select(v =>
                        (X: (int)BitConverter.ToInt16(data, start + v * FieldWalkmeshReader.VertexSize),
                         Y: (int)BitConverter.ToInt16(data, start + v * FieldWalkmeshReader.VertexSize + 2))).ToArray();
                    for (var edge = 0; edge < 3; edge++)
                    {
                        var a = vertices[edge]; var b = vertices[(edge + 1) % 3];
                        var dx = (double)b.X - a.X; var dy = (double)b.Y - a.Y;
                        var cross = dx * (row.StaticY - a.Y) - dy * (row.StaticX - a.X);
                        var opposite = vertices[(edge + 2) % 3];
                        Check(cross * (dx * (opposite.Y - a.Y) - dy * (opposite.X - a.X)) > 0,
                            "background control point lies inside the activation triangle");
                        Check(cross * cross / (dx * dx + dy * dy) > row.InteractionRadiusOverride * row.InteractionRadiusOverride,
                            "arrival radius stays inside native triangle edges");
                    }
                }
            }
        }
        Console.WriteLine("Town interaction bindings match the installed native archive.");
    }

    private static void WalkableControlsCanStartNavigation()
    {
        foreach (var row in TownInteractionObjectCatalog.Create().Where(d =>
                     d.TargetKind == FieldNavigationObjectTargetKind.Location || d.FieldId is >= 620 and <= 623))
        {
            var memory = new Memory();
            memory.Bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + row.EntityId] = 1;
            memory.Bytes[Memory.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 1;
            memory.SetRequiredState(row);
            var position = memory.Position(row.FieldId) with
            {
                X = -2000,
                TriangleId = (ushort)(row.RequiredPlayerTriangles is { Length: > 0 } triangles ? triangles[0] : 0)
            };
            var target = memory.Reader(row).ReadTargets(position).Single();
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), new ApproachPlanner());
            var transform = new FieldNavigationControlTransform(0);
            while (controller.CurrentCategory != FieldNavigationCategory.Objects)
                controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
            Check(controller.BeaconEnabled,
                $"walkable {row.FieldId}/{row.EntityId} must allow navigation to its interaction approach");
        }
    }

    // Geometry is independently checked by TownInteractionRouteAudit; this exercises
    // the reader-to-controller path so an instruction cannot accidentally disable it.
    private sealed class ApproachPlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "town interaction approach";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot p, out int triangle)
        { triangle = 0; return true; }
        public bool TryBuildRoute(FieldPositionSnapshot p, FieldNavigationTarget t, out FieldNavigationRoutePlan plan)
        { plan = new(p.FieldId, $"{t.FieldId}:{t.StableId}", [0], [], new(t.X, t.Y, t.Z), 0); return true; }
        public bool TryGetNextWaypoint(FieldPositionSnapshot p, FieldNavigationTarget t, out FieldNavigationRouteWaypoint point)
        { point = new(t.X, t.Y, t.Z); return true; }
    }

    private sealed class Memory
    {
        public const int Events = 0x02404000;
        public readonly Dictionary<int, byte> Bytes = new();
        public bool LineEnabled = true;
        public Memory()
        {
            Bytes[FieldPositionReader.AddressFieldNumModels] = 2;
            Bytes[Events + FieldNavigationNpcReader.CollisionRadiusOffset] = 20;
            // Unmapped entities must remain absent, including other verified NPCs.
            for (var i = 0; i < 256; i++) Bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + i] = 255;
        }
        public void SetMoment(int moment)
        {
            Bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            Bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        /// <summary>Writes the one native byte a row is gated on, as its own script leaves it.</summary>
        public void SetRequiredState(FieldNavigationObjectDefinition row)
        {
            if (row.RequiredMask == 0) return;
            var address = row.RequiredBank switch
            {
                1 => FieldNavigationObjectReader.AddressFieldBankBase + row.RequiredAddress,
                3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + row.RequiredAddress,
                5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + row.RequiredAddress,
                11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200 + row.RequiredAddress,
                _ => throw new InvalidOperationException($"unmodelled required bank {row.RequiredBank}")
            };
            Bytes[address] = (byte)((ReadByte(address) & ~row.RequiredMask) | row.RequiredValue);
        }
        public byte ReadByte(int a) => Bytes.GetValueOrDefault(a);
        public int ReadInt32(int a) => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events : 0;
        public FieldPositionSnapshot Position(int f) => new(1, f, 0, 0, 0, 0, 0, 0);
        public FieldNavigationObjectReader Reader(FieldNavigationObjectDefinition d) =>
            new(ReadInt32, ReadByte, _ => null, _ => null, [d], _ => LineEnabled);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Town interaction coverage: " + message);
    }
}
