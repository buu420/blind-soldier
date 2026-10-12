using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NorthernCraterNavigationTests
{
    private const uint BackgroundMovie = 0x00CC0DC2;

    public static void Run(Func<int, FieldWalkmeshReader> createMesh)
    {
        var failures = new List<string>();
        void Case(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS Northern Crater " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
        Case("interactive background movie", BackgroundMovieOwnership);
        Case("movie read failures", BackgroundMovieReadFailures);
        Case("background message ownership", BackgroundMessageOwnership);
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            Case("native ledges and cave route", () => NativeDescent(root, createMesh));
            Case("final rock routes", () => FinalPlatforms(root, createMesh));
        }
        else Console.WriteLine("Northern Crater installed-data checks require FF7_ACCESSIBILITY_DATA_ROOT.");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join('\n', failures));
    }

    private static void BackgroundMovieOwnership()
    {
        var memory = new CueMemory();
        foreach (var field in new ushort[] { 765, 766 })
        {
            memory.Word((uint)FieldPositionReader.AddressFieldId, field);
            Equal(false, new FieldAudibleCueStateReader(memory).Read().IsSuppressed,
                "native background playback with released control is gameplay");
            var legacy = new FieldAudibleCueStateReader(memory.Byte, memory.UInt16);
            Equal(false, legacy.Read().IsSuppressed, "legacy callback has the same ownership policy");
        }
        foreach (var bg in new byte[] { 0, 2, 255 })
        {
            memory.Bytes[BackgroundMovie] = bg;
            Equal(true, new FieldAudibleCueStateReader(memory).Read().IsSuppressed,
                "ordinary or unknown movie mode stays suppressed");
        }
        memory.Bytes[BackgroundMovie] = 1;
        memory.Bytes[(uint)FieldAudibleCueStateReader.AddressUserControl] = 1;
        Equal(true, new FieldAudibleCueStateReader(memory).Read().IsSuppressed, "background movie cannot release a script lock");
        memory.Bytes[(uint)FieldAudibleCueStateReader.AddressUserControl] = 0;
        memory.Bytes[(uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount] = 1;
        Equal(true, new FieldAudibleCueStateReader(memory).Read().IsSuppressed, "background movie cannot release dialogue");
        memory.Bytes[(uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount] = 0;
        memory.Word((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 2);
        Equal(true, new FieldAudibleCueStateReader(memory).Read().IsSuppressed, "unknown active movie value stays suppressed");
    }

    private static void BackgroundMovieReadFailures()
    {
        var memory = new CueMemory { DenyBackground = true };
        Equal(true, new FieldAudibleCueStateReader(memory).Read().IsSuppressed, "unreadable background mode fails closed");
        memory.Word((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 0);
        Equal(false, new FieldAudibleCueStateReader(memory).Read().IsSuppressed, "stopped movie needs no background mode read");
        memory.Word((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 1);
        memory.Bytes[(uint)FieldPositionReader.AddressCurrentModule] = 2;
        Equal("not field gameplay", new FieldAudibleCueStateReader(memory).Read().Reason, "non-field module needs no background mode read");
        memory.Bytes[(uint)FieldPositionReader.AddressCurrentModule] = 1;
        memory.DenyBackground = false;
        memory.ChangeBackground = true;
        Equal(true, new FieldAudibleCueStateReader(memory).Read().IsSuppressed, "changing background mode fails closed");
    }

    private static void BackgroundMessageOwnership()
    {
        var memory = new CueMemory();
        memory.Bytes[(uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount] = 1;
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
            memory.Bytes[(uint)(FieldMessageReader.AddressFieldWindowStates + index)] = FieldMessageReader.FreeWindowState;
        Equal(false, new FieldAudibleCueOwnershipStateReader(memory).Read().IsSuppressed, "stale message count during background gameplay is released");
        memory.Bytes[(uint)FieldMessageReader.AddressFieldWindowStates] = 0;
        memory.Word(FieldAudibleCueOwnershipStateReader.AddressFieldWindowLifecyclePhases, 6);
        Equal(true, new FieldAudibleCueOwnershipStateReader(memory).Read().IsSuppressed, "live modal window during background gameplay owns input");
        memory.Word(FieldAudibleCueOwnershipStateReader.AddressFieldWindowLifecyclePhases + 2, 1);
        Equal(false, new FieldAudibleCueOwnershipStateReader(memory).Read().IsSuppressed, "completed permanent window permits background gameplay");
        memory.Bytes[(uint)FieldAudibleCueStateReader.AddressUserControl] = 1;
        Equal(true, new FieldAudibleCueOwnershipStateReader(memory).Read().IsSuppressed, "permanent window never releases scripted control");
    }

    private static void NativeDescent(string root, Func<int, FieldWalkmeshReader> createMesh)
    {
        var catalog = new FieldScriptNavigationCatalog(root);
        var data = new FlevelDataSource(root);
        var reader = Story(1700);
        var meshes = new Dictionary<int, FieldWalkmesh>();
        var groups = new Dictionary<int, int[]>();
        foreach (var field in new[] { 749, 750 })
        {
            meshes[field] = createMesh(field).Read(Position(field, 0, 0, 0, 0)).Walkmesh
                ?? throw new InvalidOperationException("native mesh missing");
            groups[field] = Components(meshes[field]);
        }
        // Expected LINEs are the licensed movement scripts; -N means gateway N-1.
        var expected = new Dictionary<int, int>
        {
            [0]=33, [2]=17, [3]=19, [5]=40, [6]=9, [7]=-6, [8]=29,
            [9]=25, [10]=27, [11]=-4, [12]=23, [13]=38, [14]=16,
            [15]=12, [16]=21, [17]=13
        };
        var graph = new Dictionary<(int Field, int Group), (int Field, int Group)>();
        foreach (var field in new[] { 749, 750 })
        {
            var mesh = meshes[field];
            foreach (var triangle in mesh.Triangles)
            {
                var group = groups[field][triangle.Index];
                var center = triangle.GetCentroid();
                var position = Position(field, triangle.Index, (int)center.X, (int)center.Y, (int)center.Z);
                var targets = reader.ReadTargets(position).Where(t => t.Category == FieldNavigationCategory.Story).ToArray();
                if (field == 749 && !expected.ContainsKey(group))
                {
                    Equal(0, targets.Length, "decorative or terminal mesh has no unrelated ledge objective");
                    continue;
                }
                Equal(1, targets.Length, $"{field} triangle {triangle.Index} has one local onward step");
                var target = targets.Single();
                var definition = FieldStoryEventCatalog.CreateAllFields().Single(d => d.FieldId == field &&
                    d.Label == target.Label && d.TriggerLine == target.TriggerLine &&
                    d.RequiredPlayerTriangles?.Contains(triangle.Index) == true);
                var sourceTriangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, target.X, target.Y, target.Z, -1);
                Equal(group, groups[field][sourceTriangle], "Story trigger is on the player's connected ledge");
                Equal(true, new FieldWalkmeshRoutePlanner(createMesh(field)).TryBuildRoute(position, target, out _),
                    "the local native mesh reaches the selected trigger without another climb");
                if (definition.SourceScriptType == "Gateway")
                {
                    var gateway = int.Parse(definition.SourceEntityName![7..]);
                    if (field == 749) Equal(expected[group], -gateway-1, "onward cave entrance");
                    if (field == 750) Equal(group == 0 ? 0 : group == 1 ? 1 : 3, gateway, "cave exit avoids the west-ledge loop");
                    var (destination, destinationTriangle, line) = Gateway(data, field, gateway);
                    Equal(line, target.TriggerLine!.Value, "native gateway geometry");
                    graph[(field, group)] = (destination, groups[destination][destinationTriangle]);
                }
                else
                {
                    Equal(expected[group], definition.EntityId, "onward native LINE");
                    var jump = catalog.ReadField(field).Transitions.Single(t => t.SourceEntityId == definition.EntityId);
                    graph[(field, group)] = (field, groups[field][jump.TargetTriangle]);
                }
            }
        }
        foreach (var start in graph.Keys)
        {
            var visited = new HashSet<(int, int)>();
            var current = start;
            while (current != (749, 4))
            {
                Equal(true, visited.Add(current), $"native onward route from {start} cannot loop");
                Equal(true, graph.TryGetValue(current, out var next), "every onward landing has a next step");
                current = next;
            }
        }
        Equal(0, Story(1700, disabled: 40).ReadTargets(Position(749, 32, -75, 55, -1041)).Count,
            "disabled floor jump does not fall back to another ledge");
        Equal(0, Story(1500).ReadTargets(Position(749, 181, 3, 54, 920)).Count,
            "descent guidance stays in its native story range");
    }

    private static void FinalPlatforms(string root, Func<int, FieldWalkmeshReader> createMesh)
    {
        var catalog = new FieldScriptNavigationCatalog(root);
        foreach (var field in new[] { 765, 766 })
        {
            var meshReader = createMesh(field);
            var mesh = meshReader.Read(Position(field, 0, 0, 0, 0)).Walkmesh!;
            var planner = new FieldWalkmeshRoutePlanner(meshReader, transitionProvider: f => catalog.ReadField(f).Transitions);
            var components = Components(mesh);
            // las4_3's isolated component 6 has no arrival, LINE or JUMP. The
            // movie includes scenery rocks that the player's native route never uses.
            var reachableComponents = catalog.ReadField(field).Transitions.SelectMany(t => new[]
            {
                components[t.TargetTriangle],
                components[FieldWalkmeshPathfinder.ResolveTriangle(mesh, t.SourceX, t.SourceY, t.SourceZ, -1)]
            }).ToHashSet();
            foreach (var triangle in mesh.Triangles)
            {
                if (!reachableComponents.Contains(components[triangle.Index])) continue;
                var center = triangle.GetCentroid();
                var position = Position(field, triangle.Index, (int)center.X, (int)center.Y, (int)center.Z);
                var target = Story(1999).ReadTargets(position).Single();
                Equal(true, planner.TryBuildRoute(position, target, out _),
                    $"native final platform {field}/{triangle.Index} reaches onward Story trigger: {planner.LastDiagnostic}");
            }
        }
    }

    private static (int Destination, int Triangle, FieldNavigationTriggerLine Line) Gateway(FlevelDataSource data, int field, int gateway)
    {
        if (!data.TryReadField(field, out var encoded)) throw new InvalidOperationException("licensed field missing");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var offset = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4 + 0x38 + gateway * 0x18;
        int Word(int part) => BitConverter.ToInt16(bytes, offset + part);
        return (Word(0x12), Word(0x10), new(Word(0), Word(2), Word(4), Word(6), Word(8), Word(10)));
    }

    private static int[] Components(FieldWalkmesh mesh)
    {
        var groups = Enumerable.Repeat(-1, mesh.Triangles.Count).ToArray();
        var group = 0;
        for (var start = 0; start < groups.Length; start++)
        {
            if (groups[start] >= 0) continue;
            var pending = new Queue<int>(); pending.Enqueue(start); groups[start] = group;
            while (pending.TryDequeue(out var current))
                for (var edge = 0; edge < 3; edge++)
                {
                    var next = mesh.Triangles[current].GetAdjacentTriangle(edge);
                    if (next < 0 || next >= groups.Length || groups[next] >= 0) continue;
                    groups[next] = group; pending.Enqueue(next);
                }
            group++;
        }
        return groups;
    }

    private static FieldPositionSnapshot Position(int field, int triangle, int x, int y, int z) =>
        new(1, field, 0, x, y, z, (ushort)triangle, 0);

    private static FieldStoryTargetReader Story(int moment, int disabled = -1) => new(
        address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? 0x02000000 : 0,
        address => address == 0x02000072 ? (short)40 : (short)0,
        address => address == FieldNavigationObjectReader.AddressFieldBankBase ? (byte)moment :
            address == FieldNavigationObjectReader.AddressFieldBankBase + 1 ? (byte)(moment >> 8) :
            address == FieldPositionReader.AddressFieldNumModels ? (byte)1 : (byte)0,
        FieldStoryEventCatalog.CreateAllFields(), entity => entity != disabled);

    private sealed class CueMemory : ILegacyAddressSpace
    {
        public Dictionary<uint, byte> Bytes { get; } = [];
        public bool DenyBackground;
        public bool ChangeBackground;
        private int backgroundReads;
        public CueMemory()
        {
            Bytes[(uint)FieldPositionReader.AddressCurrentModule] = 1;
            Word((uint)FieldPositionReader.AddressFieldId, 765);
            Word((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 1);
            Bytes[BackgroundMovie] = 1;
        }
        public byte Byte(int address) => Bytes.GetValueOrDefault((uint)address);
        public ushort UInt16(int address) => (ushort)(Byte(address) | Byte(address+1) << 8);
        public void Word(uint address, ushort value) { Bytes[address]=(byte)value; Bytes[address+1]=(byte)(value>>8); }
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address == BackgroundMovie)
            {
                if (DenyBackground) return false;
                if (ChangeBackground) { destination[0]=(byte)(++backgroundReads % 2); return true; }
            }
            for (var i=0; i<destination.Length; i++) destination[i]=Bytes.GetValueOrDefault(address+(uint)i);
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
