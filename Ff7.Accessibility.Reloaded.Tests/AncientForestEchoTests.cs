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
        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var echo in echoes)
        {
            var memory = new ScriptMemory();
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
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ScriptMemory : ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
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
        public bool TryRead(uint address, Span<byte> destination) =>
            Copy(address, (uint)FieldScriptContextReader.AddressCurrentModule, module, destination) ||
            Copy(address, (uint)FieldPositionReader.AddressFieldId, fieldWord, destination) ||
            Copy(address, (uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer, destination) ||
            Copy(address, Script, section, destination);
        private static bool Copy(uint address, uint start, byte[] data, Span<byte> destination)
        {
            if (address < start || (ulong)address + (ulong)destination.Length > start + (ulong)data.Length) return false;
            data.AsSpan((int)(address - start), destination.Length).CopyTo(destination);
            return true;
        }
    }
}
