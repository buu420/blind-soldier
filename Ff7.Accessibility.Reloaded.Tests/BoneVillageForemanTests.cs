using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Bone Village excavation foreman (617 bonevil, entity 4), whom the tester could reach only
/// by hand: Story said "direction unavailable" and his NPC entry was one of seven "Man"s.
///
/// <para>Native facts, from the installed scripts and the engine: his Init places him at
/// (-283, 63, 18) on triangle 28, which with triangle 29 is an island inside the tent the party
/// never stands on; SLIDR 5 and no TALKR, so 0060bcfa leaves his talk radius at 80 x scale / 512
/// and bonevil's scale is 512. 00636284 lets the party talk to him while the XY distance
/// (00636515) is strictly less than that plus the leader's own width (34 at this scale,
/// 00633c2f), the height apart is within 256 and he is ahead of the leader. So he is talked to
/// from the ground in front of the tent, up to 114 units away.</para>
///
/// <para>These run the production NPC and Story readers over memory seeded from every model's
/// Init, the production obstacle reader and planner, from the floors the game puts the party
/// on: arriving from the Sleeping Forest (618 gateway, triangle 3), from the world map (the
/// tester's first step, triangle 78) and back from the dig (772's MAPJUMP, triangle 64).</para>
/// </summary>
internal static class BoneVillageForemanTests
{
    private const int VillageFieldId = 617;
    private const int ForemanEntityId = 4;
    private const int NativeTalkReach = 80 + 34;

    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader, string? dataRoot)
    {
        TheForemanIsNamed();
        if (dataRoot is null || createWalkmeshReader is null)
        {
            return;
        }

        TheForemanIsReachedFromEveryArrival(createWalkmeshReader, dataRoot);
    }

    private static void TheForemanIsNamed()
    {
        var village = VillageMemory.FromNativeInit(null);
        var npcs = village.NpcReader(null).ReadTargets(village.PlayerAt(-366, 1075, 3, z: 332));
        var foreman = npcs.Where(target => target.TriggerEntityId == ForemanEntityId).ToArray();
        Equal(1, foreman.Length, $"the foreman is an NPC ({string.Join("|", npcs.Select(target => target.Label))})");
        Equal("Excavation foreman", foreman[0].Label, "and he is named for what he is");
        Equal(NativeTalkReach, foreman[0].InteractionRadius, "his reach is his talk radius plus the leader's width");
    }

    private static void TheForemanIsReachedFromEveryArrival(Func<int, FieldWalkmeshReader> createWalkmeshReader, string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        var mesh = createWalkmeshReader(VillageFieldId);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, VillageFieldId, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var village = VillageMemory.FromNativeInit(catalog);
        var transitions = catalog.ReadField(VillageFieldId).Transitions;
        var obstacles = new FieldNavigationDynamicObstacleReader(village.ReadInt32, village.ReadInt16, village.ReadByte);
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions, obstacles.Read, obstacles.ReadPlayerCollisionRadius);
        var arrivals = new (string Name, int X, int Y, int Triangle)[]
        {
            ("from the Sleeping Forest", -366, 1075, 3),
            ("from the world map", int.MinValue, 0, 78),
            ("back from the dig", -206, -161, 64)
        };
        foreach (var (name, arrivalX, arrivalY, triangle) in arrivals)
        {
            var (x, y) = arrivalX == int.MinValue
                ? ((int)Math.Round(walkmesh.Triangles[triangle].GetCentroid().X), (int)Math.Round(walkmesh.Triangles[triangle].GetCentroid().Y))
                : (arrivalX, arrivalY);
            var z = FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[triangle], x, y);
            var start = village.PlayerAt(x, y, (ushort)triangle, z);
            var story = village.StoryReader().ReadTargets(start).Where(target => target.Label == "Talk to the excavation foreman").ToArray();
            Equal(1, story.Length, $"{name}: the Story step is offered");
            var npc = village.NpcReader(catalog).ReadTargets(start).Single(target => target.TriggerEntityId == ForemanEntityId);
            foreach (var (kind, target) in new[] { ("Story", story[0]), ("NPC", npc) })
            {
                var label = $"{kind} {target.Label} {name}";
                Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{label}: a route ({planner.LastDiagnostic})");
                var approach = plan.FinalApproach;
                var distance = Math.Sqrt(Math.Pow(approach.X - target.X, 2) + Math.Pow(approach.Y - target.Y, 2));
                Equal(true, distance < NativeTalkReach, $"{label}: it ends within the native talk reach ({distance:0.0})");
                Equal(true, distance < target.InteractionRadius, $"{label}: and within the target's own reach ({target.InteractionRadius})");
                Equal(true, Math.Abs(target.Z - approach.Z) < 256, $"{label}: at a height the talk test accepts");
                var towards = Math.Max(1d, distance);
                var from = new FieldPositionSnapshot(FieldPositionReader.FieldModule, VillageFieldId, 0,
                    approach.X - (int)Math.Round((target.X - approach.X) / towards * 4),
                    approach.Y - (int)Math.Round((target.Y - approach.Y) / towards * 4),
                    approach.Z, (ushort)plan.TargetTriangle, 0);
                Equal(true, planner.IsNativeProbeMovementClear(from, target, approach),
                    $"{label}: the last step, towards him, is one the native wall probe allows");
                StartsNavigation(target, start, planner, label);
            }
        }
    }

    private static void StartsNavigation(FieldNavigationTarget target, FieldPositionSnapshot start, IFieldNavigationRoutePlanner planner, string label)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([],
                objectTargetProvider: null,
                storyTargetProvider: target.Category == FieldNavigationCategory.Story ? _ => [target] : null,
                npcTargetProvider: target.Category == FieldNavigationCategory.Npcs ? _ => [target] : null),
            planner);
        var transform = new FieldNavigationControlTransform(0);
        for (var index = 0; index < 5 && controller.CurrentCategory != target.Category; index++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, $"{label}: navigation starts ({controller.LastNavigationDiagnostic})");
        _ = controller.UpdateLiveTracking(start, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc));
        Equal(true, controller.TryResolveAutomaticInput(start, transform, 80, out var input) && input != FieldNavigationInput.None,
            $"{label}: auto walk has somewhere to go ({controller.LastNavigationDiagnostic})");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Bone Village foreman: {label}: expected {expected}, got {actual}.");
        }
    }

    /// <summary>
    /// bonevil's engine memory as its Init scripts leave it at the game moment the dig is
    /// offered: every model placed where its XYZI puts it, with its SLIDR, TALKR, TLKON, SOLID and
    /// VISI, and Cloud leading (model 0, width 34) with Tifa and Cid's models put away.
    /// </summary>
    private sealed class VillageMemory
    {
        private const int EventTable = 0x02600000;
        private const int ModelCount = 11;
        private readonly Dictionary<int, byte> bytes = [];

        public static VillageMemory FromNativeInit(FieldScriptNavigationCatalog? catalog)
        {
            var memory = new VillageMemory();
            memory.Int32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            memory.bytes[FieldPositionReader.AddressFieldNumModels] = ModelCount;
            memory.Int16(FieldNavigationObjectReader.AddressFieldBankBase, 641);
            for (var entity = 0; entity < 32; entity++)
            {
                memory.bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 0xFF;
            }

            // Cloud leads; the other two party models are put away by the field.
            memory.Model(1, 0, 0, 0, 0, collision: 34, talk: 80, visible: true, solid: true, talkable: false);
            memory.Model(2, 1, 0, 0, 0, collision: 30, talk: 80, visible: false, solid: false, talkable: false);
            memory.Model(3, 2, 0, 0, 0, collision: 30, talk: 80, visible: false, solid: false, talkable: false);
            var villagers = catalog is null
                ? new[] { (Entity: 4, Model: 3, X: -283, Y: 63, Z: 18, Collision: 5, Talk: 80, Visible: true, Solid: true, Talkable: true) }
                : NativeVillagers(catalog);
            foreach (var villager in villagers)
            {
                memory.Model(villager.Entity, villager.Model, villager.X, villager.Y, villager.Z,
                    villager.Collision, villager.Talk, villager.Visible, villager.Solid, villager.Talkable);
            }

            return memory;
        }

        private static (int Entity, int Model, int X, int Y, int Z, int Collision, int Talk, bool Visible, bool Solid, bool Talkable)[]
            NativeVillagers(FieldScriptNavigationCatalog catalog)
        {
            var villagers = new List<(int, int, int, int, int, int, int, bool, bool, bool)>();
            for (var entity = 4; entity < 32; entity++)
            {
                var init = catalog.ReadScriptOpcodes(VillageFieldId, entity, 0);
                if (init.FirstOrDefault(op => op.Opcode == 0xA1) is not { Bytes.Count: >= 2 } character ||
                    init.FirstOrDefault(op => op.Opcode == 0xA5) is not { Bytes.Count: >= 11 } place)
                {
                    continue;
                }

                var p = place.Bytes.ToArray();
                var collision = init.FirstOrDefault(op => op.Opcode == 0xC6) is { Bytes.Count: >= 3 } slidr ? slidr.Bytes[2] : 30;
                var talk = init.FirstOrDefault(op => op.Opcode == 0xC5) is { Bytes.Count: >= 3 } talkr ? talkr.Bytes[2] : 80;
                // VISI 0, TLKON 1 and SOLID 1 put a model away, stop its Talk and its collision.
                var visible = init.FirstOrDefault(op => op.Opcode == 0xA4) is not { Bytes.Count: >= 2 } visi || visi.Bytes[1] != 0;
                var talkable = init.FirstOrDefault(op => op.Opcode == 0x7E) is not { Bytes.Count: >= 2 } tlkon || tlkon.Bytes[1] == 0;
                var solid = init.FirstOrDefault(op => op.Opcode == 0xC7) is not { Bytes.Count: >= 2 } solidMode || solidMode.Bytes[1] == 0;
                villagers.Add((entity, character.Bytes[1], BitConverter.ToInt16(p, 3), BitConverter.ToInt16(p, 5),
                    BitConverter.ToInt16(p, 7), collision, talk, visible, solid, talkable));
            }

            return villagers.ToArray();
        }

        public FieldPositionSnapshot PlayerAt(int x, int y, ushort triangle, int z)
        {
            Position(0, x, y, z);
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, VillageFieldId, 0, x, y, z, triangle, 0);
        }

        public FieldNavigationNpcReader NpcReader(FieldScriptNavigationCatalog? catalog) =>
            new(ReadInt32, ReadInt16, ReadByte, (_, _) => Array.Empty<string>(),
                field => catalog?.ReadField(field).Npcs ?? Array.Empty<FieldScriptNpcDefinition>());

        public FieldStoryTargetReader StoryReader() =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields());

        public int ReadInt32(int address) =>
            ReadByte(address) | (ReadByte(address + 1) << 8) | (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24);

        public short ReadInt16(int address) => (short)(ReadByte(address) | (ReadByte(address + 1) << 8));

        public byte ReadByte(int address) => bytes.TryGetValue(address, out var value) ? value : (byte)0;

        private void Model(int entity, int model, int x, int y, int z, int collision, int talk, bool visible, bool solid, bool talkable)
        {
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = (byte)model;
            var address = Event(model);
            bytes[address + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
            bytes[address + FieldNavigationNpcReader.TalkDisabledOffset] = talkable ? (byte)0 : (byte)1;
            bytes[address + FieldNavigationNpcReader.CollisionDisabledOffset] = solid ? (byte)0 : (byte)1;
            Int16(address + FieldNavigationNpcReader.CollisionRadiusOffset, collision);
            Int16(address + FieldNavigationNpcReader.TalkRadiusOffset, talk);
            Position(model, x, y, z);
        }

        private void Position(int model, int x, int y, int z)
        {
            var address = Event(model);
            Int32(address + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            Int32(address + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            Int32(address + FieldNavigationObjectReader.PositionZOffset, z * 4096);
        }

        private static int Event(int model) => EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;

        private void Int16(int address, int value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        private void Int32(int address, int value)
        {
            for (var index = 0; index < 4; index++)
            {
                bytes[address + index] = (byte)(value >> (index * 8));
            }
        }
    }
}
