using Ff7.Accessibility.Reloaded;

if (args.Length < 9)
{
    Console.Error.WriteLine(
        "Usage: FieldRouteDump <game-root> <field-id> <start-triangle> " +
        "<start-x> <start-y> <start-z> <target-x> <target-y> <target-z> " +
        "[triangle-id ...]");
    return 2;
}

var gameRoot = args[0];
var values = args.Skip(1).Select(int.Parse).ToArray();
var fieldId = values[0];
var startTriangle = values[1];
var startX = values[2];
var startY = values[3];
var startZ = values[4];
var targetX = values[5];
var targetY = values[6];
var targetZ = values[7];

var source = new FlevelDataSource(gameRoot);
if (!source.TryReadField(fieldId, out var encoded))
{
    Console.Error.WriteLine($"Could not read field {fieldId}: {source.Diagnostic}");
    return 1;
}

var fieldBytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
const int sectionOffsetsOffset = 6;
const int walkmeshSectionIndex = 4;
var sectionOffset = BitConverter.ToInt32(
    fieldBytes,
    sectionOffsetsOffset + walkmeshSectionIndex * sizeof(int));
var nextSectionOffset = BitConverter.ToInt32(
    fieldBytes,
    sectionOffsetsOffset + (walkmeshSectionIndex + 1) * sizeof(int));
var payloadOffset = sectionOffset + sizeof(int);
var triangleCount = BitConverter.ToInt32(fieldBytes, payloadOffset);
var trianglesBase = payloadOffset + sizeof(int);
var accessBase = trianglesBase + triangleCount * FieldWalkmeshReader.TriangleSize;
var triangles = new FieldWalkmeshTriangle[triangleCount];

for (var index = 0; index < triangleCount; index++)
{
    var triangleBase = trianglesBase + index * FieldWalkmeshReader.TriangleSize;
    var accessEntry = accessBase + index * FieldWalkmeshReader.AccessSize;
    triangles[index] = new FieldWalkmeshTriangle(
        index,
        ReadVertex(fieldBytes, triangleBase),
        ReadVertex(fieldBytes, triangleBase + FieldWalkmeshReader.VertexSize),
        ReadVertex(fieldBytes, triangleBase + FieldWalkmeshReader.VertexSize * 2),
        BitConverter.ToInt16(fieldBytes, accessEntry),
        BitConverter.ToInt16(fieldBytes, accessEntry + sizeof(short)),
        BitConverter.ToInt16(fieldBytes, accessEntry + sizeof(short) * 2));
}

Console.WriteLine(
    $"FIELD {fieldId} {source.FieldNames[fieldId]} triangles={triangleCount} " +
    $"walkmeshBytes={nextSectionOffset - sectionOffset}");

var walkmesh = new FieldWalkmesh(triangles);
var blockedTriangles = (Environment.GetEnvironmentVariable("FIELD_ROUTE_DUMP_BLOCKED") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(int.Parse)
    .ToHashSet();
var componentByTriangle = BuildComponents(triangles);
var catalog = new FieldScriptNavigationCatalog(gameRoot);
var fieldTransitions = catalog.ReadField(fieldId).Transitions;
if (string.Equals(
        Environment.GetEnvironmentVariable("FIELD_ROUTE_DUMP_TRANSITIONS"),
        "1",
        StringComparison.Ordinal))
{
    foreach (var component in componentByTriangle
                 .Select((value, triangle) => (value, triangle))
                 .GroupBy(entry => entry.value)
                 .OrderBy(group => group.Key))
    {
        Console.WriteLine(
            $"COMPONENT {component.Key} triangles=" +
            string.Join(',', component.Select(entry => entry.triangle)));
    }

    foreach (var transition in fieldTransitions.OrderBy(transition => transition.StableId, StringComparer.Ordinal))
    {
        var sourceTriangle = FieldWalkmeshPathfinder.ResolveTriangle(
            walkmesh,
            transition.SourceX,
            transition.SourceY,
            transition.SourceZ,
            -1);
        Console.WriteLine(
            $"TRANSITION id={transition.StableId} kind={transition.Kind} entity={transition.SourceEntityId} " +
            $"source={transition.SourceX},{transition.SourceY},{transition.SourceZ} " +
            $"sourceTriangle={sourceTriangle} sourceComponent={ComponentOf(componentByTriangle, sourceTriangle)} " +
            $"target={transition.TargetX},{transition.TargetY},{transition.TargetZ?.ToString() ?? "native"} " +
            $"triangle={transition.TargetTriangle} targetComponent={ComponentOf(componentByTriangle, transition.TargetTriangle)} " +
            $"input={transition.RequiredInput} action={transition.RequiresAction}");
    }
}
var planner = new FieldWalkmeshRoutePlanner(
    CreateWalkmeshReader(walkmesh),
    transitionProvider: requestedField =>
        requestedField == fieldId
            ? fieldTransitions
            : Array.Empty<FieldScriptNavigationTransition>());
var position = new FieldPositionSnapshot(
    FieldPositionReader.FieldModule,
    fieldId,
    0,
    startX,
    startY,
    startZ,
    (ushort)startTriangle,
    0);
var target = new FieldNavigationTarget(
    fieldId,
    FieldNavigationCategory.Story,
    "dump target",
    targetX,
    targetY,
    targetZ);
if (blockedTriangles.Count != 0)
{
    var found = FieldWalkmeshPathfinder.TryBuildRoute(
        walkmesh,
        startTriangle,
        startX,
        startY,
        startZ,
        targetX,
        targetY,
        targetZ,
        blockedTriangles.Contains,
        out var blockedTrianglePath,
        out _,
        out var blockedTargetTriangle);
    Console.WriteLine(
        $"BLOCKED {string.Join(',', blockedTriangles.OrderBy(value => value))} " +
        $"reachable={found} target={blockedTargetTriangle}");
    if (found)
    {
        Console.WriteLine($"PATH {string.Join(',', blockedTrianglePath)}");
    }

    return found ? 0 : 1;
}
if (!planner.TryBuildRoute(position, target, out var plan))
{
    Console.Error.WriteLine(
        $"Route unavailable: {planner.LastDiagnostic}; " +
        $"resolvedStart={FieldWalkmeshPathfinder.ResolveTriangle(walkmesh, startX, startY, startZ, startTriangle)}, " +
        $"resolvedTarget={FieldWalkmeshPathfinder.ResolveTriangle(walkmesh, targetX, targetY, targetZ, -1)}");
    foreach (var triangleId in args.Skip(9).Select(int.Parse).Distinct())
    {
        var triangle = triangles[triangleId];
        var centroid = triangle.GetCentroid();
        Console.Error.WriteLine(
            $"INSPECT {triangleId} centroid={centroid.X:0},{centroid.Y:0},{centroid.Z:0} " +
            $"component={ComponentOf(componentByTriangle, triangleId)} " +
            $"vertices=" +
            $"{triangle.Vertex0.X},{triangle.Vertex0.Y},{triangle.Vertex0.Z};" +
            $"{triangle.Vertex1.X},{triangle.Vertex1.Y},{triangle.Vertex1.Z};" +
            $"{triangle.Vertex2.X},{triangle.Vertex2.Y},{triangle.Vertex2.Z} " +
            $"adj={triangle.Adjacent0},{triangle.Adjacent1},{triangle.Adjacent2}");
    }
    return 1;
}

var trianglePath = plan.TrianglePath;
var portals = plan.Portals;
var targetTriangle = plan.TargetTriangle;
var stableWaypoints = FieldWalkmeshPathfinder.BuildStableWaypoints(
    startX,
    startY,
    startZ,
    portals,
    new FieldNavigationRouteWaypoint(targetX, targetY, targetZ));
Console.WriteLine(
    $"ROUTE start={FieldWalkmeshPathfinder.ResolveTriangle(walkmesh, startX, startY, startZ, startTriangle)} " +
    $"target={targetTriangle} portals={portals.Count} steps={stableWaypoints.Count}");
Console.WriteLine($"PATH {string.Join(',', trianglePath)}");
foreach (var step in stableWaypoints.Select((value, index) => (value, index)))
{
    Console.WriteLine(
        $"STEP {step.index + 1}/{stableWaypoints.Count} " +
        $"portal={step.value.RequiredPortalIndex} " +
        $"point={step.value.Waypoint.X},{step.value.Waypoint.Y},{step.value.Waypoint.Z}");
}

foreach (var portal in portals.Select((value, index) => (value, index)))
{
    Console.WriteLine(
        $"PORTAL {portal.index + 1}/{portals.Count} " +
        $"from={portal.value.FromTriangle} to={portal.value.ToTriangle} " +
        $"left={portal.value.Left.X},{portal.value.Left.Y},{portal.value.Left.Z} " +
        $"right={portal.value.Right.X},{portal.value.Right.Y},{portal.value.Right.Z} " +
        $"point={portal.value.Midpoint.X},{portal.value.Midpoint.Y},{portal.value.Midpoint.Z} " +
        $"transition={portal.value.TransitionKind?.ToString() ?? "walk"} " +
        $"input={portal.value.RequiredInput} exit={portal.value.TransitionExit?.ToString() ?? "none"} " +
        $"id={(string.IsNullOrEmpty(portal.value.TransitionId) ? "none" : portal.value.TransitionId)}");
}

foreach (var triangleId in trianglePath.Distinct())
{
    var triangle = triangles[triangleId];
    var centroid = triangle.GetCentroid();
    Console.WriteLine(
        $"TRI {triangleId} centroid={centroid.X:0},{centroid.Y:0},{centroid.Z:0} " +
        $"adj={triangle.Adjacent0},{triangle.Adjacent1},{triangle.Adjacent2}");
}

var previousWaypoint = new FieldNavigationRouteWaypoint(startX, startY, startZ);
var previousPortalIndex = 0;
foreach (var step in stableWaypoints)
{
    var startRouteIndex = Math.Clamp(previousPortalIndex, 0, trianglePath.Count - 1);
    var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
        walkmesh,
        trianglePath[startRouteIndex],
        previousWaypoint,
        step.Waypoint);
    Console.WriteLine(
        $"TRACE portals={previousPortalIndex}-{step.RequiredPortalIndex} " +
        $"from={previousWaypoint.X},{previousWaypoint.Y},{previousWaypoint.Z} " +
        $"to={step.Waypoint.X},{step.Waypoint.Y},{step.Waypoint.Z} " +
        $"clear={trace.IsClear} triangles={string.Join(',', trace.TraversedTriangles)} " +
        $"diagnostic={trace.Diagnostic}");
    previousWaypoint = step.Waypoint;
    previousPortalIndex = step.RequiredPortalIndex;
}

foreach (var triangleId in args.Skip(9).Select(int.Parse).Distinct())
{
    var triangle = triangles[triangleId];
    var centroid = triangle.GetCentroid();
    Console.WriteLine(
        $"INSPECT {triangleId} centroid={centroid.X:0},{centroid.Y:0},{centroid.Z:0} " +
        $"component={ComponentOf(componentByTriangle, triangleId)} " +
        $"vertices=" +
        $"{triangle.Vertex0.X},{triangle.Vertex0.Y},{triangle.Vertex0.Z};" +
        $"{triangle.Vertex1.X},{triangle.Vertex1.Y},{triangle.Vertex1.Z};" +
        $"{triangle.Vertex2.X},{triangle.Vertex2.Y},{triangle.Vertex2.Z} " +
        $"adj={triangle.Adjacent0},{triangle.Adjacent1},{triangle.Adjacent2}");
}

return 0;

static int ComponentOf(IReadOnlyList<int> components, int triangle) =>
    triangle >= 0 && triangle < components.Count
        ? components[triangle]
        : -1;

static int[] BuildComponents(IReadOnlyList<FieldWalkmeshTriangle> triangles)
{
    var components = Enumerable.Repeat(-1, triangles.Count).ToArray();
    var pending = new Queue<int>();
    var component = 0;
    for (var start = 0; start < triangles.Count; start++)
    {
        if (components[start] >= 0)
        {
            continue;
        }

        components[start] = component;
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var triangle = triangles[pending.Dequeue()];
            foreach (var adjacent in new[]
                     {
                         triangle.Adjacent0,
                         triangle.Adjacent1,
                         triangle.Adjacent2
                     })
            {
                if (adjacent < 0 ||
                    adjacent >= triangles.Count ||
                    components[adjacent] >= 0)
                {
                    continue;
                }

                components[adjacent] = component;
                pending.Enqueue(adjacent);
            }
        }

        component++;
    }

    return components;
}

static FieldWalkmeshVertex ReadVertex(byte[] bytes, int offset) =>
    new(
        BitConverter.ToInt16(bytes, offset),
        BitConverter.ToInt16(bytes, offset + sizeof(short)),
        BitConverter.ToInt16(bytes, offset + sizeof(short) * 2));

static FieldWalkmeshReader CreateWalkmeshReader(FieldWalkmesh walkmesh)
{
    const int baseAddress = 0x01000000;
    const int walkmeshSectionOffset = 64;
    var memory = new Dictionary<int, byte>();
    var sectionEntry =
        baseAddress +
        FieldWalkmeshReader.SectionOffsetsHeaderOffset +
        FieldWalkmeshReader.WalkmeshSectionIndex * sizeof(int);
    var payloadAddress = baseAddress + walkmeshSectionOffset + sizeof(int);
    var nextSectionOffset =
        walkmeshSectionOffset +
        sizeof(int) +
        sizeof(int) +
        walkmesh.Triangles.Count *
        (FieldWalkmeshReader.TriangleSize + FieldWalkmeshReader.AccessSize);
    WriteInt32(memory, FieldWalkmeshReader.AddressFieldDataPtr, baseAddress);
    WriteInt32(memory, sectionEntry, walkmeshSectionOffset);
    WriteInt32(memory, sectionEntry + sizeof(int), nextSectionOffset);
    WriteInt32(memory, payloadAddress, walkmesh.Triangles.Count);
    var triangleBase = payloadAddress + sizeof(int);
    var accessBase = triangleBase + walkmesh.Triangles.Count * FieldWalkmeshReader.TriangleSize;
    foreach (var triangle in walkmesh.Triangles)
    {
        var address = triangleBase + triangle.Index * FieldWalkmeshReader.TriangleSize;
        WriteVertex(memory, address, triangle.Vertex0);
        WriteVertex(memory, address + FieldWalkmeshReader.VertexSize, triangle.Vertex1);
        WriteVertex(memory, address + FieldWalkmeshReader.VertexSize * 2, triangle.Vertex2);
        var accessAddress = accessBase + triangle.Index * FieldWalkmeshReader.AccessSize;
        WriteInt16(memory, accessAddress, triangle.Adjacent0);
        WriteInt16(memory, accessAddress + sizeof(short), triangle.Adjacent1);
        WriteInt16(memory, accessAddress + sizeof(short) * 2, triangle.Adjacent2);
    }

    byte ReadByte(int address) => memory.GetValueOrDefault(address);
    int ReadInt32(int address) =>
        ReadByte(address) |
        (ReadByte(address + 1) << 8) |
        (ReadByte(address + 2) << 16) |
        (ReadByte(address + 3) << 24);
    short ReadInt16(int address) =>
        (short)(ReadByte(address) | (ReadByte(address + 1) << 8));
    return new FieldWalkmeshReader(ReadInt32, ReadInt16);
}

static void WriteVertex(
    IDictionary<int, byte> memory,
    int address,
    FieldWalkmeshVertex vertex)
{
    WriteInt16(memory, address, vertex.X);
    WriteInt16(memory, address + sizeof(short), vertex.Y);
    WriteInt16(memory, address + sizeof(short) * 2, vertex.Z);
}

static void WriteInt16(IDictionary<int, byte> memory, int address, short value)
{
    memory[address] = (byte)value;
    memory[address + 1] = (byte)(value >> 8);
}

static void WriteInt32(IDictionary<int, byte> memory, int address, int value)
{
    memory[address] = (byte)value;
    memory[address + 1] = (byte)(value >> 8);
    memory[address + 2] = (byte)(value >> 16);
    memory[address + 3] = (byte)(value >> 24);
}
