using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AncientForestEchoTests
{
    internal static void Run()
    {
        var map = WholeGameEchoNavigationManifest.EntityMap(622);
        foreach (var entity in new[] { 9, 10, 11 })
            Check(map.TryGetValue(entity, out var mapped) && mapped == entity, $"Echo-S frog {entity} is missing from the reviewed map");
        var throatChest = FieldNavigationObjectCatalog.CreateAllFields().Single(o => o.FieldId == 622 && o.EntityId == 26);
        Check(throatChest.ManualNavigationGuidance is null && throatChest.UsesTalkInteraction,
            "Supershot ST keeps its native Talk approach available outside the jaws");
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var timingMemory = new ScriptMemory();
        AssertPlantWaits(root, timingMemory, echo: false);
        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var echo in echoes)
        {
            var memory = new ScriptMemory();
            AssertPlantWaits(echo, timingMemory, echo: true);
            var loaded = new FieldScriptNavigationCatalog(root, Ff7GameLanguageDetector.Detect(root), new(memory));
            foreach (var field in new[] { 620, 622, 623 })
            {
                memory.Load(echo, field);
                var current = loaded.ReadField(field);
                Check(current.IsUsable && current.UsesReviewedEchoLayout, $"{echo}: forest {field} not accepted");
                var jaws = field switch { 620 => new[] { 25, 26, 27, 28 }, 622 => new[] { 22, 23 }, _ => new[] { 31, 32, 33 } };
                var actualJaws = jaws.Select(id => WholeGameEchoNavigationManifest.EntityMap(field)[id]).ToHashSet();
                Check(!current.Transitions.Any(t => t.Kind == FieldNavigationTransitionKind.Jump && actualJaws.Contains(t.SourceEntityId)),
                    $"{echo}: field {field} exposes a flytrap bite as a route");
                var vine = field switch { 620 => 34, 622 => 24, _ => 39 };
                Check(current.Transitions.Any(t => t.SourceEntityId == WholeGameEchoNavigationManifest.EntityMap(field)[vine]),
                    $"{echo}: field {field} lost the legitimate walk-on vine");
                if (field == 622)
                {
                    var frogs = TownInteractionObjectCatalog.Create().Where(o => o.FieldId == 622 && o.Label!.StartsWith("Frog ", StringComparison.Ordinal))
                        .Select(loaded.RemapObjectDefinition).ToArray();
                    Check(frogs.Length == 3 && frogs.All(o => o is not null), $"{echo}: not all three frogs survive loaded-layout remapping");
                }
                if (field == 620)
                {
                    var climb = TownInteractionObjectCatalog.Create().Single(o => o.FieldId == 620 && o.Label == "Vine to pitcher plant 1");
                    Check(loaded.RemapObjectDefinition(climb) is { EntityId: 16, ActivatesOnOk: false },
                        $"{echo}: the native vine's walk-on line must survive the entity shift");
                    var down = TownInteractionObjectCatalog.Create().Single(o => o.FieldId == 620 && o.EntityId == 33);
                    Check(loaded.RemapObjectDefinition(down)?.Label?.Contains("hold Down", StringComparison.Ordinal) == true,
                        $"{echo}: the held Down jump is incorrectly called a press");
                }
            }
        }
        Console.WriteLine($"Ancient Forest Echo-S map, bite exclusions and native controls passed on {echoes.Length} layouts.");
        Console.WriteLine($"Ancient Forest plant timing passed on installed data and {echoes.Length} Echo-S layouts.");
    }

    private static void AssertPlantWaits(string root, ScriptMemory memory, bool echo)
    {
        var timing = new AncientForestPlantTiming(memory, memory);
        foreach (var field in new[] { 620, 622, 623 })
        {
            memory.Load(root, field);
            var first = field switch { 620 => echo ? 10 : 11, 622 => 17, _ => 11 };
            var count = field == 623 ? 4 : 3;
            for (var entity = first; entity < first + count; entity++)
            {
                var context = memory.SetPlantWait(field, entity);
                memory.SetWaitCounter(entity, 300);
                Check(timing.CaptureStart(context) is null, $"field {field}, entity {entity}: running countdown extended again");
                memory.SetWaitCounter(entity, 0);
                memory.SetWaitCounter(entity ^ 1, 17);
                Check(timing.CaptureStart(context with { ByteIndex = 13 }) is null, $"field {field}: unrelated wait accepted");
                foreach (var (native, extended) in new (ushort, ushort)[] { (300, 696), (600, 1392) })
                {
                    memory.SetWaitCounter(entity, 0);
                    memory.SetCurrentEntity(entity);
                    var start = timing.CaptureStart(context) ??
                        throw new InvalidOperationException($"field {field}, entity {entity}: initial plant wait missing");
                    memory.SetWaitCounter(entity, native); // The native or FFNx WAIT's first invocation.
                    Check(timing.Extend(start) && memory.WaitCounter(entity) == extended && memory.WaitCounter(entity ^ 1) == 17,
                        $"field {field}, entity {entity}: wrong extended wait at native counter {native}");
                    Check(!timing.Extend(start), $"field {field}, entity {entity}: same start applied twice");

                    memory.SetWaitCounter(entity, native);
                    memory.SetCurrentEntity(entity ^ 1);
                    Check(!timing.Extend(start) && memory.WaitCounter(entity) == native, $"field {field}: cursor lifetime check failed");
                }
            }
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ScriptMemory : ILegacyAddressSpace, ILegacyMemoryWriter
    {
        private const uint Script = 0x02000000;
        private const uint WaitCounters = Floor60GuardTimingStateReader.AddressEntityWaitCounters;
        private readonly byte[] counters = new byte[128], cursors = new byte[128], currentEntity = new byte[1];
        private byte[] section = [];
        private readonly byte[] fieldWord = new byte[2], pointer = new byte[4];
        private readonly byte[] module = [FieldPositionReader.FieldModule];
        internal void Load(string root, int field)
        {
            var source = new FlevelDataSource(root);
            Check(source.TryReadField(field, out var encoded), "missing native field");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            section = decoded.AsSpan(at + 4, length).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(fieldWord, (ushort)field);
            BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
        }

        internal FieldScriptContext SetPlantWait(int field, int entity)
        {
            SetCurrentEntity(entity);
            var offsetTable = (BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(6)) << 2) +
                FieldScriptContextReader.ScriptOffsetTableHeaderSize + section[2] * FieldScriptContextReader.ScriptSlotsPerEntity;
            var slot = offsetTable + entity * FieldScriptContextReader.ScriptOffsetEntityStride + 3 * sizeof(ushort);
            var cursor = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(slot)) + 12;
            Check(section.AsSpan(cursor, 3).SequenceEqual(new byte[] { 0x24, 0x2C, 0x01 }),
                $"field {field}, entity {entity}: expected native WAIT 300");
            BinaryPrimitives.WriteUInt16LittleEndian(cursors.AsSpan(entity * sizeof(ushort)), (ushort)cursor);
            return new(field, entity, 3, 12, 0x24);
        }

        internal ushort WaitCounter(int entity) => BinaryPrimitives.ReadUInt16LittleEndian(counters.AsSpan(entity * sizeof(ushort)));

        internal void SetWaitCounter(int entity, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(counters.AsSpan(entity * sizeof(ushort)), value);

        internal void SetCurrentEntity(int entity) => currentEntity[0] = (byte)entity;

        public bool TryWriteInt32(uint address, int value)
        {
            if ((address & 3) != 0 || address < WaitCounters || address > WaitCounters + counters.Length - sizeof(int))
                return false;
            BinaryPrimitives.WriteInt32LittleEndian(counters.AsSpan((int)(address - WaitCounters)), value);
            return true;
        }

        public bool TryRead(uint address, Span<byte> destination) =>
            Copy(address, (uint)FieldScriptContextReader.AddressCurrentModule, module, destination) ||
            Copy(address, (uint)FieldPositionReader.AddressFieldId, fieldWord, destination) ||
            Copy(address, (uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer, destination) ||
            Copy(address, (uint)FieldScriptContextReader.AddressCurrentEntityId, currentEntity, destination) ||
            Copy(address, (uint)FieldScriptContextReader.AddressFieldCurrScriptPosition, cursors, destination) ||
            Copy(address, WaitCounters, counters, destination) ||
            Copy(address, Script, section, destination);
        private static bool Copy(uint address, uint start, byte[] data, Span<byte> destination)
        {
            if (address < start || (ulong)address + (ulong)destination.Length > start + (ulong)data.Length) return false;
            data.AsSpan((int)(address - start), destination.Length).CopyTo(destination);
            return true;
        }
    }
}
