namespace Ff7.Accessibility.Reloaded;

/// <summary>One exit walked on a cross-field approach: taken in <see cref="FieldId"/>, leading to <see cref="DestinationFieldId"/>.</summary>
public sealed record FieldNavigationCrossFieldLeg(int FieldId, string ExitStableId, int DestinationFieldId);

/// <summary>
/// The way to a target on a part of this field that its walkmesh does not join to where the
/// party stands: a chain of native exits out of this field and, in the end, back into it on the
/// target's part. The target stays the goal throughout.
/// </summary>
public sealed record FieldNavigationCrossFieldApproach(int OriginFieldId, IReadOnlyList<FieldNavigationCrossFieldLeg> Legs)
{
    public FieldNavigationCrossFieldApproach(int originFieldId, string outboundExitStableId, int viaFieldId, string returnExitStableId)
        : this(originFieldId, [new(originFieldId, outboundExitStableId, viaFieldId), new(viaFieldId, returnExitStableId, originFieldId)])
    {
    }

    /// <summary>The exit the approach starts through.</summary>
    public string OutboundExitStableId => Legs[0].ExitStableId;

    /// <summary>The field the first exit leads to.</summary>
    public int ViaFieldId => Legs[0].DestinationFieldId;

    /// <summary>The exit that finally leads back into the target's field.</summary>
    public string ReturnExitStableId => Legs[^1].ExitStableId;

    public override string ToString() =>
        $"{OriginFieldId}: " + string.Join(" -> ", Legs.Select(leg => $"{leg.ExitStableId}>{leg.DestinationFieldId}"));
}

/// <summary>
/// Finds <see cref="FieldNavigationCrossFieldApproach"/> legs from the installed field data.
///
/// <para>gidun_1 (546), the first cave of the Gi, is the case that needs it: the Added Effect
/// Materia stands on an upper ledge of 23 triangles that no walkmesh edge, ladder or jump joins
/// to the cave floor. The field's own scripts are the only way up: the floor's LINEJB (entity
/// 24) map jumps into gidun_2 (547), and 547's LINEJA (entity 21) map jumps back to 546 on the
/// ledge, at (-335,675) triangle 6. A planner that only knows the current walkmesh rightly finds
/// no route, and saying "Route unavailable" leaves out a way a sighted player can see.</para>
///
/// <para>Everything is read, not assumed. The way out has to be one of the exits the player is
/// offered now and routable from where they stand on the live walkmesh and locks. Its native
/// arrival in the next field (the MAPJUMP its own scripts run, or its gateway's destination)
/// has to reach the way back on that field's installed walkmesh. And the way back's own native
/// arrival has to reach the target on this field's live walkmesh and locks. The next field's
/// live line and lock state cannot be read from here, so its leg is started from its own live
/// exit list when the party arrives, and fails there with the ordinary message if it is not
/// offered.</para>
/// </summary>
public sealed class FieldCrossFieldApproachResolver
{
    private const int TriggerSectionIndex = 7;
    private const int SectionOffsetsHeaderOffset = 6;
    private const int GatewayTableOffset = 0x38;
    private const int GatewayStride = 24;
    private const int GatewayCount = 12;

    /// <summary>Where a MAPJUMP or gateway puts the party, and what its script is proven to have written first.</summary>
    private sealed record NativeArrival(int X, int Y, int Triangle, FieldEntryWrites Writes);

    /// <summary>Where the party can move from after an arrival; Z is null when it is the walkmesh's own.</summary>
    private readonly record struct Placement(int X, int Y, int? Z, int Triangle);

    private readonly FieldScriptNavigationCatalog catalog;
    private readonly Func<int, byte[]?> readDecodedField;
    private readonly IFieldNavigationRoutePlanner livePlanner;
    private readonly Func<FieldPositionSnapshot, FieldWalkmesh?> readLiveWalkmesh;
    private readonly Dictionary<int, FieldData?> fields = new();

    public FieldCrossFieldApproachResolver(
        FieldScriptNavigationCatalog catalog,
        FlevelDataSource flevel,
        IFieldNavigationRoutePlanner livePlanner,
        Func<FieldPositionSnapshot, FieldWalkmesh?> readLiveWalkmesh)
        : this(
            catalog,
            fieldId => flevel.TryReadField(fieldId, out var encoded) ? Ff7LzsDecoder.DecodeFieldFile(encoded) : null,
            livePlanner,
            readLiveWalkmesh)
    {
    }

    public FieldCrossFieldApproachResolver(
        FieldScriptNavigationCatalog catalog,
        Func<int, byte[]?> readDecodedField,
        IFieldNavigationRoutePlanner livePlanner,
        Func<FieldPositionSnapshot, FieldWalkmesh?> readLiveWalkmesh)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.readDecodedField = readDecodedField ?? throw new ArgumentNullException(nameof(readDecodedField));
        this.livePlanner = livePlanner ?? throw new ArgumentNullException(nameof(livePlanner));
        this.readLiveWalkmesh = readLiveWalkmesh ?? throw new ArgumentNullException(nameof(readLiveWalkmesh));
    }

    public string LastDiagnostic { get; private set; } = "not resolved";

    /// <summary>At most this many exits are walked on one approach.</summary>
    public const int MaximumLegs = 6;

    /// <param name="exits">The exits the player is offered in this field right now.</param>
    public FieldNavigationCrossFieldApproach? Resolve(
        FieldPositionSnapshot position,
        FieldNavigationTarget goal,
        IReadOnlyList<FieldNavigationTarget> exits)
    {
        if (goal.FieldId != position.FieldId || goal.Category == FieldNavigationCategory.Exits)
        {
            LastDiagnostic = "not a target on this field's own walkmesh";
            return null;
        }

        var originId = position.FieldId;
        if (Field(originId) is not { } origin || readLiveWalkmesh(position) is not { } liveMesh)
        {
            LastDiagnostic = $"field {originId} data unavailable";
            return null;
        }

        // Only a target this field's walkmesh, locks and transitions do not already reach.
        if (livePlanner.TryBuildRoute(position, goal, out _))
        {
            LastDiagnostic = "reachable directly";
            return null;
        }

        // Fields the approach may pass through: this one and those its own exits lead to.
        var allowed = new HashSet<int> { originId };
        foreach (var exit in exits.Concat(origin.WaysOut()))
        {
            if (exit.DestinationFieldIds is { Count: 1 } destinations && destinations[0] != originId)
            {
                allowed.Add(destinations[0]);
            }
        }

        // Breadth first over the places the party can stand after each native exit: the fewest
        // exits win. Each exit's own arrivals are placed as the destination's entry script puts
        // the party down; an exit whose arrivals cannot be placed, or are placed in more than one
        // spot, is not taken.
        var startNode = new Node(originId, new Placement(position.X, position.Y, position.Z, position.TriangleId), []);
        var frontier = new Queue<Node>([startNode]);
        var visited = new HashSet<(int Field, int Triangle)> { (originId, position.TriangleId) };
        var tried = new List<string>();
        while (frontier.TryDequeue(out var node))
        {
            if (node.Legs.Count >= MaximumLegs || Field(node.FieldId) is not { } here)
            {
                continue;
            }

            var isStart = node.Legs.Count == 0;
            var candidates = isStart ? exits : here.WaysOut();
            foreach (var exit in candidates)
            {
                if (exit.FieldId != node.FieldId ||
                    exit.DestinationFieldIds is not { Count: 1 } destinations ||
                    destinations[0] == node.FieldId ||
                    !allowed.Contains(destinations[0]) ||
                    string.IsNullOrEmpty(exit.StableId) ||
                    Field(destinations[0]) is not { } there)
                {
                    continue;
                }

                var destination = destinations[0];
                var arrivals = Arrivals(here, exit.StableId, exit.TriggerEntityId, destination);
                var placements = arrivals.Select(there.Place).Distinct().ToArray();
                if (placements.Length != 1 || placements[0] is not { } placed)
                {
                    if (arrivals.Count > 0)
                    {
                        tried.Add($"{exit.StableId}: {(placements.Length > 1 ? "lands in more than one place" : "entry not followed")}");
                    }

                    continue;
                }

                // The live planner decides everything in this field; the others are not loaded,
                // so their installed walkmesh is all that can be read.
                var from = At(node.FieldId, node.Position, position, liveMesh, here);
                var reachable = node.FieldId == originId
                    ? livePlanner.TryBuildRoute(from, exit, out _)
                    : here.Planner.TryBuildRoute(from, exit, out _);
                if (!reachable || !visited.Add((destination, placed.Triangle)))
                {
                    continue;
                }

                var legs = node.Legs.Append(new FieldNavigationCrossFieldLeg(node.FieldId, exit.StableId, destination)).ToArray();
                if (destination == originId)
                {
                    var landing = At(originId, placed, position, liveMesh, origin);
                    if (placed.Triangle < liveMesh.Triangles.Count && livePlanner.TryBuildRoute(landing, goal, out _))
                    {
                        // The live planner's last question is the first leg, the one walked now.
                        _ = livePlanner.TryBuildRoute(position, exits.First(candidate => candidate.StableId == legs[0].ExitStableId), out _);
                        var approach = new FieldNavigationCrossFieldApproach(originId, legs);
                        LastDiagnostic = $"{approach}, landing ({landing.X},{landing.Y}) t{landing.TriangleId}";
                        return approach;
                    }
                }

                frontier.Enqueue(new Node(destination, placed, legs));
            }
        }

        LastDiagnostic = tried.Count == 0
            ? $"no native chain of exits out of {originId} leads back to {GetTargetLabel(goal)}"
            : $"no chain found; {string.Join("; ", tried.Distinct().Take(12))}";
        return null;
    }

    private static FieldPositionSnapshot At(
        int fieldId,
        Placement placed,
        FieldPositionSnapshot position,
        FieldWalkmesh liveMesh,
        FieldData data)
    {
        if (fieldId != position.FieldId)
        {
            return data.At(placed, position.ModelIndex);
        }

        var z = placed.Z ?? (placed.Triangle < liveMesh.Triangles.Count
            ? SurfaceZ(liveMesh.Triangles[placed.Triangle], placed.X, placed.Y)
            : position.Z);
        return position with { X = placed.X, Y = placed.Y, Z = z, TriangleId = (ushort)placed.Triangle, NativeFixedPosition = null };
    }

    private sealed record Node(int FieldId, Placement Position, IReadOnlyList<FieldNavigationCrossFieldLeg> Legs);

    /// <summary>
    /// The height of a triangle's own plane under (x, y): an arrival is placed on the walkmesh
    /// where it lands, not at the triangle's centre, which on a slope or stair is a different
    /// height. A triangle seen edge-on falls back to its centre.
    /// </summary>
    public static int SurfaceZ(FieldWalkmeshTriangle triangle, int x, int y)
    {
        var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        double vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        var nx = uy * vz - uz * vy;
        var ny = uz * vx - ux * vz;
        var nz = ux * vy - uy * vx;
        if (Math.Abs(nz) < 1e-6)
        {
            return (int)Math.Round(triangle.GetCentroid().Z);
        }

        return (int)Math.Round(a.Z - (nx * (x - a.X) + ny * (y - a.Y)) / nz);
    }

    private static string GetTargetLabel(FieldNavigationTarget target) =>
        string.IsNullOrEmpty(target.StableId) ? target.Label : target.StableId;

    /// <summary>
    /// Where a way out of <paramref name="field"/> puts the party in <paramref name="destination"/>:
    /// a gateway's own destination, or the MAPJUMPs a line's scripts run, following the scripts
    /// they request. Each MAPJUMP carries only the writes proven on the paths that reach it
    /// (<see cref="FieldSourceTransfers"/>): a write the script passes on one branch and not
    /// another, one a request may make, one a skipped path makes, is not known when it jumps.
    /// </summary>
    private static IReadOnlyList<NativeArrival> Arrivals(
        FieldData field,
        string stableId,
        int triggerEntityId,
        int destination)
    {
        var parts = stableId.Split(':');
        if (parts.Length == 4 && parts[0] == "gateway" && int.TryParse(parts[2], out var gatewayIndex))
        {
            return field.Gateways
                .Where(gateway => gateway.Index == gatewayIndex && gateway.Destination == destination)
                .Select(gateway => new NativeArrival(gateway.ArrivalX, gateway.ArrivalY, gateway.ArrivalTriangle, FieldEntryWrites.None))
                .ToArray();
        }

        if (triggerEntityId < 0)
        {
            return [];
        }

        return FieldSourceTransfers.Find(field.Scripts, triggerEntityId, destination)
            .Select(transfer => new NativeArrival(transfer.X, transfer.Y, transfer.Triangle, transfer.Writes))
            .ToArray();
    }

    private FieldData? Field(int fieldId)
    {
        if (fields.TryGetValue(fieldId, out var cached))
        {
            return cached;
        }

        FieldData? data = null;
        try
        {
            if (readDecodedField(fieldId) is { } bytes)
            {
                var read = catalog.ReadField(fieldId);
                data = read.IsUsable ? new FieldData(fieldId, bytes, read, catalog.ReadAllScriptOpcodes(fieldId)) : null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            data = null;
        }

        fields[fieldId] = data;
        return data;
    }

    private sealed class FieldData
    {
        private const int FieldDataBase = 0x02000000;
        private readonly byte[] bytes;
        private readonly FieldScriptNavigationReadResult read;

        public FieldData(int fieldId, byte[] bytes, FieldScriptNavigationReadResult read, IReadOnlyList<FieldScriptDefinition> scripts)
        {
            FieldId = fieldId;
            this.bytes = bytes;
            this.read = read;
            Scripts = scripts;
            Gateways = ReadGateways(fieldId, bytes);
            // The installed walkmesh, with nothing locked: this field is not loaded, so its
            // live locks cannot be read. Its leg is started from its own live exit list.
            Planner = new FieldWalkmeshRoutePlanner(
                new FieldWalkmeshReader(ReadInt32, ReadInt16),
                transitionProvider: _ => read.Transitions);
        }

        public int FieldId { get; }

        public IReadOnlyList<FieldScriptDefinition> Scripts { get; }

        public IReadOnlyList<Gateway> Gateways { get; }

        public FieldWalkmeshRoutePlanner Planner { get; }

        public FieldPositionSnapshot At(Placement placed, int modelIndex)
        {
            var position = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, FieldId, modelIndex, placed.X, placed.Y, 0, (ushort)placed.Triangle, 0);
            mesh ??= new FieldWalkmeshReader(ReadInt32, ReadInt16).Read(position).Walkmesh;
            return placed.Z is { } z
                ? position with { Z = z }
                : mesh is { } walkmesh && placed.Triangle < walkmesh.Triangles.Count
                    ? position with { Z = SurfaceZ(walkmesh.Triangles[placed.Triangle], placed.X, placed.Y) }
                    : position;
        }

        /// <summary>Where the party can first move from after <paramref name="arrival"/> (see <see cref="FieldEntryPlacement"/>).</summary>
        public Placement? Place(NativeArrival arrival) =>
            FieldEntryPlacement.Place(Scripts, arrival.X, arrival.Y, arrival.Triangle, arrival.Writes) is { } placed
                ? new Placement(placed.X, placed.Y, placed.Z, placed.Triangle)
                : null;

        private FieldWalkmesh? mesh;

        /// <summary>This field's ways out: script exits with one destination, and gateways.</summary>
        public IEnumerable<FieldNavigationTarget> WaysOut()
        {
            foreach (var exit in read.Exits.Where(exit => exit.DestinationFieldIds is { Count: 1 }))
            {
                yield return exit;
            }

            foreach (var gateway in Gateways)
            {
                var destination = gateway.Destination;
                var line = gateway.Line;
                yield return new FieldNavigationTarget(
                    FieldId,
                    FieldNavigationCategory.Exits,
                    "Exit",
                    (line.StartX + line.EndX) / 2,
                    (line.StartY + line.EndY) / 2,
                    (line.StartZ + line.EndZ) / 2,
                    $"gateway:{FieldId}:{gateway.Index}:{destination}",
                    DestinationFieldIds: [destination],
                    TriggerLine: line);
            }
        }

        private int ReadInt32(int address)
        {
            if (address == FieldWalkmeshReader.AddressFieldDataPtr)
            {
                return FieldDataBase;
            }

            var offset = address - FieldDataBase;
            return offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToInt32(bytes, offset) : 0;
        }

        private short ReadInt16(int address)
        {
            var offset = address - FieldDataBase;
            return offset >= 0 && offset + 2 <= bytes.Length ? BitConverter.ToInt16(bytes, offset) : (short)0;
        }

        private static IReadOnlyList<Gateway> ReadGateways(int fieldId, byte[] bytes)
        {
            var headerAt = SectionOffsetsHeaderOffset + TriggerSectionIndex * 4;
            if (headerAt + 4 > bytes.Length)
            {
                return [];
            }

            var section = BitConverter.ToInt32(bytes, headerAt) + 4;
            var gateways = new List<Gateway>();
            for (var index = 0; index < GatewayCount; index++)
            {
                var at = section + GatewayTableOffset + index * GatewayStride;
                if (at < 0 || at + GatewayStride > bytes.Length)
                {
                    break;
                }

                var destination = BitConverter.ToInt16(bytes, at + 18);
                if (destination < 0 || destination == short.MaxValue || destination == fieldId)
                {
                    continue;
                }

                gateways.Add(new Gateway(
                    index,
                    destination,
                    new FieldNavigationTriggerLine(
                        BitConverter.ToInt16(bytes, at), BitConverter.ToInt16(bytes, at + 2), BitConverter.ToInt16(bytes, at + 4),
                        BitConverter.ToInt16(bytes, at + 6), BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10)),
                    BitConverter.ToInt16(bytes, at + 12),
                    BitConverter.ToInt16(bytes, at + 14),
                    BitConverter.ToUInt16(bytes, at + 16)));
            }

            return gateways;
        }
    }

    private readonly record struct Gateway(
        int Index,
        int Destination,
        FieldNavigationTriggerLine Line,
        int ArrivalX,
        int ArrivalY,
        int ArrivalTriangle);
}

/// <summary>Where <see cref="FieldEntryPlacement"/> puts the party; Z is null when it is the walkmesh's own.</summary>
public readonly record struct FieldEntryPlacementResult(int X, int Y, int? Z, int Triangle);

/// <summary>
/// What the script that left the previous field is proven to have written when it jumped, by
/// savemap block (<c>FieldBankByte.BlockOf</c>) and byte: <see cref="Known"/> bytes and their
/// values, <see cref="Unknown"/> bytes it may have changed to a value that cannot be named, and
/// <see cref="AnyUnknown"/> when it may have changed bytes it does not name at all.
/// </summary>
public sealed record FieldEntryWrites(
    IReadOnlyDictionary<(int Block, int Address), int> Known,
    IReadOnlySet<(int Block, int Address)> Unknown,
    bool AnyUnknown = false)
{
    public static FieldEntryWrites None { get; } =
        new(new Dictionary<(int, int), int>(), new HashSet<(int, int)>());

    /// <summary>Known writes keyed by bank nibble (either nibble of a pair), nothing unknown.</summary>
    public static FieldEntryWrites FromKnown(IReadOnlyDictionary<(int Bank, int Address), int> writes) =>
        new(writes.ToDictionary(pair => (FieldBankByte.BlockOf(pair.Key.Bank), pair.Key.Address), pair => pair.Value),
            new HashSet<(int, int)>());
}

/// <summary>
/// The MAPJUMPs to one field a trigger's scripts can run, each with the writes proven on the
/// paths that reach it. The scripts are walked as a control-flow graph from their own
/// opcodes: both sides of a test the walk cannot decide, jumps forward and back to a fixed
/// point. A byte is known at a MAPJUMP only if every path there sets it to the same literal;
/// any other writer, a request that can write it, or a path that leaves the decoded script
/// leaves it unknown. A requested script is walked from its caller's state (for REQ and REQSW
/// less whatever the caller still writes, because it runs on beside it).
/// </summary>
internal static class FieldSourceTransfers
{
    internal readonly record struct Transfer(int X, int Y, int Triangle, FieldEntryWrites Writes);

    private const int MaximumRequestDepth = 8;

    public static IReadOnlyList<Transfer> Find(IReadOnlyList<FieldScriptDefinition> scripts, int entity, int destination)
    {
        var transfers = new List<Transfer>();
        var pending = new Queue<(int Entity, int Script, State Start, int Depth)>();
        foreach (var script in scripts.Where(script => script.EntityId == entity && script.ScriptId > 0))
        {
            pending.Enqueue((script.EntityId, script.ScriptId, new State(), 0));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var writesByScript = new Dictionary<(int, int), (HashSet<(int, int)> Bytes, bool Any)>();
        while (pending.TryDequeue(out var job))
        {
            if (job.Depth > MaximumRequestDepth || !seen.Add($"{job.Entity}:{job.Script}:{job.Start.Key}"))
            {
                continue;
            }

            var script = scripts.FirstOrDefault(candidate => candidate.EntityId == job.Entity && candidate.ScriptId == job.Script);
            if (script.Opcodes is null)
            {
                continue;
            }

            Walk(scripts, script, job.Start, destination, transfers, writesByScript,
                (callee, calleeScript, state) => pending.Enqueue((callee, calleeScript, state, job.Depth + 1)));
        }

        return transfers;
    }

    private static void Walk(
        IReadOnlyList<FieldScriptDefinition> scripts,
        FieldScriptDefinition script,
        State start,
        int destination,
        List<Transfer> transfers,
        Dictionary<(int, int), (HashSet<(int, int)> Bytes, bool Any)> writesByScript,
        Action<int, int, State> request)
    {
        var ops = script.Opcodes;
        var index = new Dictionary<int, int>();
        for (var at = 0; at < ops.Count; at++)
        {
            index[ops[at].ByteIndex] = at;
        }

        var found = new List<Transfer>();
        var unsound = false;
        var ownWrites = WritesOf(scripts, script.EntityId, script.ScriptId, writesByScript, new HashSet<(int, int)>());
        var states = new State?[ops.Count];
        var work = new Stack<int>();
        Flow(0, start);
        var steps = 0;
        while (work.TryPop(out var current))
        {
            if (++steps > 20000)
            {
                unsound = true;
                break;
            }

            var state = states[current]!.Clone();
            var op = ops[current];
            var b = op.Bytes;
            var next = op.ByteIndex + b.Count;
            switch (op.Opcode)
            {
                case 0x00 or 0x07:
                    continue;
                case MapJumpOpcode when b.Count == 10:
                    // Read once the walk has settled, from the state every path leaves here.
                    continue;
                case 0x10 when b.Count == 2:
                    FlowTo(op.ByteIndex + 1 + b[1], state);
                    continue;
                case 0x11 when b.Count == 3:
                    FlowTo(op.ByteIndex + 1 + (b[1] | (b[2] << 8)), state);
                    continue;
                case 0x12 when b.Count == 2:
                    FlowTo(op.ByteIndex - b[1], state);
                    continue;
                case 0x13 when b.Count == 3:
                    FlowTo(op.ByteIndex - (b[1] | (b[2] << 8)), state);
                    continue;
                case >= 0x14 and <= 0x19 or >= 0x30 and <= 0x32 or 0xCB or 0xCC:
                {
                    if (FalseTarget(op) is not { } otherwise)
                    {
                        unsound = true;
                        continue;
                    }

                    switch (Decide(op, state))
                    {
                        case true:
                            FlowTo(next, state);
                            break;
                        case false:
                            FlowTo(otherwise, state);
                            break;
                        default:
                            FlowTo(next, state);
                            FlowTo(otherwise, state.Clone());
                            break;
                    }

                    continue;
                }
                case 0x01 or 0x02 or 0x03 when b.Count == 3:
                {
                    var callee = b[1];
                    var calleeScript = b[2] & 0x1F;
                    var calleeStart = state.Clone();
                    if (op.Opcode != 0x03)
                    {
                        // REQ and REQSW do not wait: the caller runs on and can still write.
                        calleeStart.Forget(ownWrites.Bytes, ownWrites.Any);
                    }

                    request(callee, calleeScript, calleeStart);
                    var (bytes, any) = WritesOf(scripts, callee, calleeScript, writesByScript, new HashSet<(int, int)>());
                    state.Forget(bytes, any);
                    FlowTo(next, state);
                    continue;
                }
                case 0x04 or 0x05 or 0x06 when b.Count == 3:
                {
                    // A party member runs its own script: whichever character leads.
                    foreach (var character in PartyCharacters(scripts))
                    {
                        var (bytes, any) = WritesOf(scripts, character, b[2] & 0x1F, writesByScript, new HashSet<(int, int)>());
                        state.Forget(bytes, any);
                    }

                    FlowTo(next, state);
                    continue;
                }
                case SetByteOpcode when b.Count == 4 && (b[1] & 0x0F) == 0 && FieldBankByte.BlockOf(b[1] >> 4) != 0:
                    state.Set((FieldBankByte.BlockOf(b[1] >> 4), b[2]), b[3]);
                    FlowTo(next, state);
                    continue;
                default:
                {
                    var (bytes, any) = WritesOfOpcode(op);
                    state.Forget(bytes, any);
                    FlowTo(next, state);
                    continue;
                }
            }
        }

        for (var at = 0; at < ops.Count; at++)
        {
            var b = ops[at].Bytes;
            if (ops[at].Opcode == MapJumpOpcode && b.Count == 10 && (b[1] | (b[2] << 8)) == destination && states[at] is { } settled)
            {
                found.Add(new Transfer((short)(b[3] | (b[4] << 8)), (short)(b[5] | (b[6] << 8)), b[7] | (b[8] << 8), settled.ToWrites()));
            }
        }

        foreach (var transfer in found)
        {
            // A path the walk could not follow might have reached this jump with other writes.
            transfers.Add(unsound ? transfer with { Writes = transfer.Writes with { AnyUnknown = true } } : transfer);
        }

        void FlowTo(int byteIndex, State state)
        {
            if (!index.TryGetValue(byteIndex, out var target))
            {
                unsound = true;
                return;
            }

            Flow(target, state);
        }

        void Flow(int target, State state)
        {
            if (target >= ops.Count)
            {
                unsound = true;
                return;
            }

            if (states[target] is not { } existing)
            {
                states[target] = state;
                work.Push(target);
                return;
            }

            var merged = State.Merge(existing, state);
            if (!merged.SameAs(existing))
            {
                states[target] = merged;
                work.Push(target);
            }
        }
    }

    // A test on a byte this path has proven, decided; anything else is null (both sides).
    private static bool? Decide(FieldScriptOpcodeDefinition op, State state)
    {
        var b = op.Bytes;
        if (op.Opcode is not (0x14 or 0x15) || b.Count < 5 || (b[1] & 0x0F) != 0)
        {
            return null;
        }

        var key = (FieldBankByte.BlockOf(b[1] >> 4), (int)b[2]);
        if (key.Item1 == 0 || !state.TryGet(key, out var value))
        {
            return null;
        }

        return FieldEntryPlacement.Compare(value, b[3], b[4]);
    }

    internal static int? FalseTarget(FieldScriptOpcodeDefinition op)
    {
        var b = op.Bytes;
        var at = op.ByteIndex;
        return op.Opcode switch
        {
            0x14 when b.Count >= 6 => at + 5 + b[5],
            0x15 when b.Count >= 7 => at + 5 + (b[5] | (b[6] << 8)),
            0x16 or 0x18 when b.Count >= 8 => at + 7 + b[7],
            0x17 or 0x19 when b.Count >= 9 => at + 7 + (b[7] | (b[8] << 8)),
            >= 0x30 and <= 0x32 when b.Count >= 4 => at + 3 + b[3],
            0xCB or 0xCC when b.Count >= 3 => at + 2 + b[2],
            _ => null
        };
    }

    internal static IEnumerable<int> PartyCharacters(IReadOnlyList<FieldScriptDefinition> scripts) =>
        scripts.Where(script => script.ScriptId == 0 && script.Opcodes.Any(op => op.Opcode == 0xA0))
            .Select(script => script.EntityId)
            .Distinct();

    /// <summary>The bytes one opcode can write, by the native writer table.</summary>
    internal static (HashSet<(int, int)> Bytes, bool Any) WritesOfOpcode(FieldScriptOpcodeDefinition op)
    {
        var instruction = new FieldScriptInstruction(op.Opcode, op.ByteIndex, op.Bytes.ToArray());
        var named = new List<FieldBankByte>();
        FieldScriptProgram.Writes(instruction, named, out var unknown);
        return (named.Select(key => (key.Block, key.Address)).ToHashSet(), unknown);
    }

    /// <summary>Every byte a script and everything it requests can write.</summary>
    internal static (HashSet<(int, int)> Bytes, bool Any) WritesOf(
        IReadOnlyList<FieldScriptDefinition> scripts,
        int entity,
        int scriptId,
        Dictionary<(int, int), (HashSet<(int, int)> Bytes, bool Any)> cache,
        HashSet<(int, int)> visiting)
    {
        if (cache.TryGetValue((entity, scriptId), out var cached))
        {
            return cached;
        }

        var bytes = new HashSet<(int, int)>();
        var any = false;
        if (!visiting.Add((entity, scriptId)))
        {
            return (bytes, false);
        }

        var script = scripts.FirstOrDefault(candidate => candidate.EntityId == entity && candidate.ScriptId == scriptId);
        foreach (var op in script.Opcodes ?? [])
        {
            if (op.Opcode is 0x01 or 0x02 or 0x03 && op.Bytes.Count == 3)
            {
                var (calleeBytes, calleeAny) = WritesOf(scripts, op.Bytes[1], op.Bytes[2] & 0x1F, cache, visiting);
                bytes.UnionWith(calleeBytes);
                any |= calleeAny;
            }
            else if (op.Opcode is 0x04 or 0x05 or 0x06 && op.Bytes.Count == 3)
            {
                foreach (var character in PartyCharacters(scripts))
                {
                    var (calleeBytes, calleeAny) = WritesOf(scripts, character, op.Bytes[2] & 0x1F, cache, visiting);
                    bytes.UnionWith(calleeBytes);
                    any |= calleeAny;
                }
            }

            var (own, ownAny) = WritesOfOpcode(op);
            bytes.UnionWith(own);
            any |= ownAny;
        }

        visiting.Remove((entity, scriptId));
        cache[(entity, scriptId)] = (bytes, any);
        return (bytes, any);
    }

    private sealed class State
    {
        private readonly Dictionary<(int, int), int> known = [];
        private readonly HashSet<(int, int)> unknown = [];
        private bool any;

        public string Key =>
            string.Join(",", known.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key.Item1}.{pair.Key.Item2}={pair.Value}")) + "|" +
            string.Join(",", unknown.Order().Select(key => $"{key.Item1}.{key.Item2}")) + (any ? "|*" : string.Empty);

        public State Clone()
        {
            var copy = new State { any = any };
            foreach (var (key, value) in known)
            {
                copy.known[key] = value;
            }

            copy.unknown.UnionWith(unknown);
            return copy;
        }

        public bool TryGet((int, int) key, out int value) => known.TryGetValue(key, out value);

        public void Set((int, int) key, int value)
        {
            known[key] = value;
            unknown.Remove(key);
        }

        public void Forget(IEnumerable<(int, int)> keys, bool everything)
        {
            foreach (var key in keys)
            {
                known.Remove(key);
                unknown.Add(key);
            }

            if (everything)
            {
                any = true;
                unknown.UnionWith(known.Keys);
                known.Clear();
            }
        }

        public static State Merge(State left, State right)
        {
            var merged = new State { any = left.any || right.any };
            merged.unknown.UnionWith(left.unknown);
            merged.unknown.UnionWith(right.unknown);
            foreach (var key in left.known.Keys.Union(right.known.Keys))
            {
                if (left.known.TryGetValue(key, out var a) && right.known.TryGetValue(key, out var b) && a == b)
                {
                    merged.known[key] = a;
                }
                else
                {
                    // Written on one path and not the other, or to different values.
                    merged.unknown.Add(key);
                }
            }

            merged.unknown.ExceptWith(merged.known.Keys);
            if (merged.any)
            {
                merged.unknown.UnionWith(merged.known.Keys);
                merged.known.Clear();
            }

            return merged;
        }

        public bool SameAs(State other) => Key == other.Key;

        public FieldEntryWrites ToWrites() =>
            new(new Dictionary<(int, int), int>(known), new HashSet<(int, int)>(unknown), any);
    }

    private const int MapJumpOpcode = 0x60;
    private const int SetByteOpcode = 0x80;
}

/// <summary>
/// Follows a destination field's controlled actor through its entry, from the installed opcodes.
/// Only a narrow, named set of opcodes is followed; anything else, and anything that could move
/// the party or change what the entry tests without the walk seeing it, is null: the arrival is
/// not offered rather than given a landing that was guessed.
/// </summary>
public static class FieldEntryPlacement
{
    /// <summary>
    /// Where the party can first move from after arriving at (<paramref name="x"/>, <paramref name="y"/>): the MAPJUMP's own
    /// point, unless the arriving script wrote a byte that this field's controlled actor tests
    /// on entry. Then that actor's Main is followed, from the native opcodes, as the game runs
    /// it on the frame the field loads: its IFs on that byte and on the position it reads back
    /// with AXYZI, its XYZI, LADER and JUMP placements, its jumps. md8_b1 and md8_b2 land the
    /// party on a two-triangle ladder start and climb it from there to the scaffold. Anything
    /// this cannot decide - a test on another value, a request that could move the party, a
    /// second actor that also places it, no end - is null: the arrival is not offered. After
    /// that, an entity that polls the leader's triangle can move the party on at once (see
    /// <see cref="FollowTrianglePolls"/>).
    /// </summary>
    public static FieldEntryPlacementResult? Place(
        IReadOnlyList<FieldScriptDefinition> scripts,
        int x,
        int y,
        int triangle,
        IReadOnlyDictionary<(int Bank, int Address), int> writes) =>
        Place(scripts, x, y, triangle, FieldEntryWrites.FromKnown(writes));

    /// <summary>
    /// As above, with what the arriving script may have written without it being known. An
    /// actor that tests such a byte is not followed and not skipped: the arrival is null.
    /// </summary>
    public static FieldEntryPlacementResult? Place(
        IReadOnlyList<FieldScriptDefinition> scripts,
        int x,
        int y,
        int triangle,
        FieldEntryWrites writes)
    {
        var raw = new FieldEntryPlacementResult(x, y, null, triangle);
        // Field initialisation (0060BCFA) zeroes all 256 bytes of the temporary block, so
        // nothing the previous field left there survives into this one.
        writes = new FieldEntryWrites(
            writes.Known.Where(pair => pair.Key.Block != TemporaryBlock).ToDictionary(pair => pair.Key, pair => pair.Value),
            writes.Unknown.Where(key => key.Block != TemporaryBlock).ToHashSet(),
            writes.AnyUnknown);
        return FollowActors(scripts, writes, raw) is { } placed
            ? FollowTrianglePolls(scripts, placed, writes)
            : null;
    }

    private static FieldEntryPlacementResult? FollowActors(
        IReadOnlyList<FieldScriptDefinition> scripts,
        FieldEntryWrites writes,
        FieldEntryPlacementResult raw)
    {
        FieldEntryPlacementResult? result = null;
        var actors = scripts.Where(script => script.ScriptId == 0 && script.Opcodes.Any(op => op.Opcode == PartyCharacterOpcode));
        foreach (var actor in actors)
        {
            // An entry that tests nothing the arriving script may have written is where it was
            // before; one that does is followed, and a read of a byte the script may have
            // changed to an unknown value ends it (null), unless the entry wrote it first.
            var tested = TestedBytes(actor.Opcodes);
            tested.RemoveWhere(key => key.Item1 == TemporaryBlock);
            if (!tested.Overlaps(writes.Known.Keys) && !tested.Overlaps(writes.Unknown) &&
                !(writes.AnyUnknown && tested.Count > 0))
            {
                continue;
            }

            var placed = FollowEntry(scripts, actor, writes.Known, raw);
            if (placed is null || result is { } other && other != placed)
            {
                return null;
            }

            result = placed;
        }

        return result ?? raw;
    }

    /// <summary>The bytes an actor's IFUB and IFSW read, by block.</summary>
    private static HashSet<(int, int)> TestedBytes(IEnumerable<FieldScriptOpcodeDefinition> ops)
    {
        var tested = new HashSet<(int, int)>();
        foreach (var op in ops)
        {
            var b = op.Bytes;
            if (op.Opcode is 0x14 or 0x15 && b.Count >= 3)
            {
                Add(b[1] >> 4, b[2], word: false);
                if ((b[1] & 0x0F) != 0)
                {
                    Add(b[1] & 0x0F, b[3], word: false);
                }
            }
            else if (op.Opcode is >= 0x16 and <= 0x19 && b.Count >= 6)
            {
                Add(b[1] >> 4, b[2] | (b[3] << 8), word: true);
                if ((b[1] & 0x0F) != 0)
                {
                    Add(b[1] & 0x0F, b[4] | (b[5] << 8), word: true);
                }
            }
        }

        return tested;

        void Add(int bank, int address, bool word)
        {
            var block = FieldBankByte.BlockOf(bank);
            if (block == 0 || address > 0xFF)
            {
                return;
            }

            tested.Add((block, address));
            if (word && FieldBankByte.IsWordBank(bank) && address < 0xFF)
            {
                tested.Add((block, address + 1));
            }
        }
    }

    /// <summary>A native byte comparison (006117CB): 0-5 compare, 6 &amp;, 7 ^, 8 |, 9 bit on, 10 bit off.</summary>
    internal static bool? Compare(int left, int right, int condition) => condition switch
    {
        0 => left == right,
        1 => left != right,
        2 => left > right,
        3 => left < right,
        4 => left >= right,
        5 => left <= right,
        6 => (left & right) != 0,
        7 => (left ^ right) != 0,
        8 => (left | right) != 0,
        9 => ((left >> (right & 31)) & 1) != 0,
        10 => ((left >> (right & 31)) & 1) == 0,
        _ => null
    };

    /// <summary>
    /// Opcodes an entry may run without changing where the party is or what the entry decides
    /// on: presentation, timing and binding only. Each is on this list because a native entry
    /// this follows uses it; nothing is assumed harmless for not being recognised.
    /// </summary>
    private static readonly HashSet<byte> Presentation =
    [
        0xA0, // PC
        0xA1, // CHAR_
        0x33, // UC
        0x4A, // MENU2
        0x7E, // TLKON
        0xC6, // SLIDR
        0xC7, // SOLID
        0xA4, // VISI
        0xB2, // MSPED
        0xB3, // DIR
        0x24, // WAIT
        0xF1, // SOUND
        0xF0, // MUSIC
        0x2B, // SLIP
        0xA2, // DFANM
        0xA3, // ANIME1
        0xAE, // ANIME2
        0xAC, // ANIMW
        0xB1, // CANMX1 (anfrst_1's leader script 25)
        0xBC, // CANMX2
        0xBD  // ASPED
    ];

    // Opcodes a requested script may not run: they move a party member, hand control over,
    // change the field or who is in the party.
    private static bool Moves(byte opcode) =>
        opcode is MapJumpOpcode or 0xA5 or 0xC0 or 0xC2 or 0xA8 or 0xA9 or 0xAA or 0xBF or 0x07 or
            0x04 or 0x05 or 0x06 or 0xC8 or 0xC9 or 0xCA or 0xCD or 0x49;

    private static FieldEntryPlacementResult? FollowEntry(
        IReadOnlyList<FieldScriptDefinition> scripts,
        FieldScriptDefinition actor,
        IReadOnlyDictionary<(int Block, int Address), int> writes,
        FieldEntryPlacementResult start)
    {
        var ops = actor.Opcodes;
        var index = ops.Select((op, at) => (op.ByteIndex, at)).ToDictionary(pair => pair.ByteIndex, pair => pair.at);
        var vars = new Dictionary<(int, int), int>(writes);
        var lost = new HashSet<(int, int)>();
        var tested = TestedBytes(ops);
        var position = start;
        var moved = false;
        var returns = 0;
        for (int at = 0, steps = 0; at >= 0 && at < ops.Count; steps++)
        {
            if (steps > 512)
            {
                return null;
            }

            var op = ops[at];
            var b = op.Bytes;
            int Jump(int target) => index.TryGetValue(target, out var next) ? next : -1;
            switch (op.Opcode)
            {
                case 0x00:
                    // Init, then Main: the second return ends the entry.
                    if (++returns == 2)
                    {
                        return position;
                    }

                    at++;
                    continue;
                case 0x10 when b.Count == 2:
                    at = Jump(op.ByteIndex + 1 + b[1]);
                    continue;
                case 0x11 when b.Count == 3:
                    at = Jump(op.ByteIndex + 1 + (b[1] | (b[2] << 8)));
                    continue;
                case 0x12 or 0x13 when b.Count is 2 or 3:
                {
                    // Main's loop: the entry has run, if nothing in the loop can move the party.
                    var back = op.ByteIndex - (b.Count == 2 ? b[1] : b[1] | (b[2] << 8));
                    if (!index.TryGetValue(back, out var top))
                    {
                        return null;
                    }

                    for (var inside = top; inside < at; inside++)
                    {
                        if (!Presentation.Contains(ops[inside].Opcode) &&
                            ops[inside].Opcode is not (>= 0x10 and <= 0x19 or 0x00))
                        {
                            return null;
                        }
                    }

                    return position;
                }
                case 0x14 or 0x15 or 0x16 or 0x17:
                {
                    if (Test(op, vars, lost) is not { } holds || FieldSourceTransfers.FalseTarget(op) is not { } otherwise)
                    {
                        return null;
                    }

                    at = holds ? at + 1 : Jump(otherwise);
                    continue;
                }
                case SetByteOpcode when b.Count == 4 && (b[1] & 0x0F) == 0 && FieldBankByte.BlockOf(b[1] >> 4) != 0:
                    vars[(FieldBankByte.BlockOf(b[1] >> 4), b[2])] = b[3];
                    lost.Remove((FieldBankByte.BlockOf(b[1] >> 4), b[2]));
                    at++;
                    continue;
                case 0xC1 when b.Count == 8:
                {
                    // AXYZI: an entity's position read back into four words. Only the actor's own,
                    // and only while this walk knows it - a MOVE walks it somewhere unread.
                    var banks = new[] { b[1] >> 4, b[1] & 0x0F, b[2] >> 4, b[2] & 0x0F };
                    var values = new int?[] { position.X, position.Y, position.Z, position.Triangle };
                    for (var axis = 0; axis < 4; axis++)
                    {
                        var block = FieldBankByte.BlockOf(banks[axis]);
                        if (block == 0)
                        {
                            return null;
                        }

                        var known = b[3] == actor.EntityId && !moved ? values[axis] : null;
                        SetWord(vars, lost, block, b[4 + axis], known);
                    }

                    at++;
                    continue;
                }
                case 0xA5 when b.Count == 11 && b[1] == 0 && b[2] == 0:
                    position = new FieldEntryPlacementResult((short)(b[3] | (b[4] << 8)), (short)(b[5] | (b[6] << 8)),
                        (short)(b[7] | (b[8] << 8)), b[9] | (b[10] << 8));
                    moved = false;
                    at++;
                    continue;
                case 0xC2 when b.Count == 15 && b[1] == 0 && b[2] == 0:
                    position = new FieldEntryPlacementResult((short)(b[3] | (b[4] << 8)), (short)(b[5] | (b[6] << 8)),
                        (short)(b[7] | (b[8] << 8)), b[9] | (b[10] << 8));
                    moved = false;
                    at++;
                    continue;
                case 0xC0 when b.Count == 11 && b[1] == 0 && b[2] == 0:
                    position = new FieldEntryPlacementResult((short)(b[3] | (b[4] << 8)), (short)(b[5] | (b[6] << 8)), null,
                        b[7] | (b[8] << 8));
                    moved = false;
                    at++;
                    continue;
                case 0xA8 when b.Count == 6 && b[1] == 0:
                    // MOVE walks on this walkmesh from where the party stands, so the part it can
                    // move from is the same; where on it is no longer known to a read-back.
                    moved = true;
                    at++;
                    continue;
                case 0x01 or 0x02 or 0x03 when b.Count == 3:
                    // A request whose script, or anything it requests, could move the party or
                    // write what this entry reads is not followed.
                    if (!RequestIsInert(scripts, b[1], b[2] & 0x1F, tested, vars.Keys))
                    {
                        return null;
                    }

                    at++;
                    continue;
                default:
                    if (Presentation.Contains(op.Opcode))
                    {
                        at++;
                        continue;
                    }

                    // RETTO, party requests, MAPJUMP, variable placements, other writers and
                    // anything unlisted: not followed.
                    return null;
            }
        }

        return null;
    }

    private static void SetWord(Dictionary<(int, int), int> vars, HashSet<(int, int)> lost, int block, int address, int? value)
    {
        foreach (var (key, part) in new[] { ((block, address), value & 0xFF), ((block, address + 1), (value >> 8) & 0xFF) })
        {
            if (key.Item2 > 0xFF)
            {
                continue;
            }

            if (part is { } known)
            {
                vars[key] = known;
                lost.Remove(key);
            }
            else
            {
                vars.Remove(key);
                lost.Add(key);
            }
        }
    }

    // An IFUB/IFUBL/IFSW/IFSWL whose operands this walk knows, decided; null otherwise.
    private static bool? Test(FieldScriptOpcodeDefinition op, IReadOnlyDictionary<(int, int), int> vars, IReadOnlySet<(int, int)> lost)
    {
        var b = op.Bytes;
        var word = op.Opcode is 0x16 or 0x17;
        if (b.Count < (word ? 8 : 6))
        {
            return null;
        }

        int? Read(int bank, int raw)
        {
            if (bank == 0)
            {
                return word ? (short)raw : raw & 0xFF;
            }

            var block = FieldBankByte.BlockOf(bank);
            if (block == 0 || raw > 0xFF || ByteAt(block, raw) is not { } low)
            {
                return null;
            }

            if (!word || !FieldBankByte.IsWordBank(bank))
            {
                return low;
            }

            return ByteAt(block, raw + 1) is { } high ? (short)(low | (high << 8)) : null;
        }

        // What this entry knows of a byte: its own or the arrival's write, or, for the
        // temporary block the field just zeroed, 0 until something it cannot follow writes it.
        int? ByteAt(int block, int address)
        {
            if (vars.TryGetValue((block, address), out var value))
            {
                return value;
            }

            return block == TemporaryBlock && address <= 0xFF && !lost.Contains((block, address)) ? 0 : null;
        }

        var left = Read(b[1] >> 4, word ? b[2] | (b[3] << 8) : b[2]);
        var right = Read(b[1] & 0x0F, word ? b[4] | (b[5] << 8) : b[3]);
        return left is { } l && right is { } r ? Compare(l, r, word ? b[6] : b[4]) : null;
    }

    // Whether a requested script and everything it requests leave the party and this entry's
    // bytes alone.
    private static bool RequestIsInert(
        IReadOnlyList<FieldScriptDefinition> scripts,
        int entity,
        int scriptId,
        IReadOnlySet<(int, int)> tested,
        IEnumerable<(int, int)> read)
    {
        var watched = new HashSet<(int, int)>(tested);
        watched.UnionWith(read);
        var seen = new HashSet<(int, int)>();
        var pending = new Stack<(int, int)>();
        pending.Push((entity, scriptId));
        while (pending.TryPop(out var next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            var script = scripts.FirstOrDefault(candidate => candidate.EntityId == next.Item1 && candidate.ScriptId == next.Item2);
            foreach (var op in script.Opcodes ?? [])
            {
                if (Moves(op.Opcode))
                {
                    return false;
                }

                if (op.Opcode is 0x01 or 0x02 or 0x03 && op.Bytes.Count == 3)
                {
                    pending.Push((op.Bytes[1], op.Bytes[2] & 0x1F));
                }

                var (bytes, any) = FieldSourceTransfers.WritesOfOpcode(op);
                if (any || bytes.Overlaps(watched))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Then any entity whose Main watches the leader's triangle and, standing on the one the
    /// party is on, hands the leader a scripted jump with no key to press. anfrst_1 (620) is the
    /// case: 621's gateway lands the party on t25, and dir's Main - PXYZI of party slot 0 into
    /// 6[0], compared with 6[53] and 6[55], which its Init sets to 25 and 26 - runs the leader's
    /// script 25 at once, two JUMPs down to t29, where the Minerva Band's ledge begins. A Main
    /// that tests some other triangle is not about this arrival and is left alone; one that
    /// tests this triangle is followed from the native opcodes, with no key down - a jump asked
    /// for with a key is the player's to take - and anything it cannot decide - a test of an
    /// unknown value, a request whose script is not plain JUMPs the leader family agrees on,
    /// an unlisted opcode - is null: the arrival is not offered.
    /// </summary>
    private static FieldEntryPlacementResult? FollowTrianglePolls(
        IReadOnlyList<FieldScriptDefinition> scripts,
        FieldEntryPlacementResult placed,
        FieldEntryWrites writes)
    {
        var position = placed;
        for (var pass = 0; pass < 4; pass++)
        {
            var moved = false;
            foreach (var poller in scripts.Where(script => script.ScriptId == 0))
            {
                if (!TestsLeaderTriangle(poller, position.Triangle, out var constants))
                {
                    continue;
                }

                if (TestedBytes(poller.Opcodes).Overlaps(writes.Unknown) ||
                    FollowPoll(scripts, poller, constants, position) is not { } next)
                {
                    return null;
                }

                moved |= next != position;
                position = next;
            }

            if (!moved)
            {
                return position;
            }
        }

        return null;
    }

    // Whether the entity's Main compares the triangle PXYZI read for party slot 0 with this
    // triangle, as a literal or a word its Init set; with the words its Init set.
    private static bool TestsLeaderTriangle(
        FieldScriptDefinition script,
        int triangle,
        out Dictionary<(int Bank, int Address), int> constants)
    {
        constants = [];
        var ops = script.Opcodes;
        var mainStart = ops.Select((op, at) => (op.Opcode, at)).Where(pair => pair.Opcode == 0x00).Select(pair => pair.at + 1).FirstOrDefault();
        if (mainStart <= 0 || mainStart >= ops.Count)
        {
            return false;
        }

        for (var at = 0; at < mainStart; at++)
        {
            if (ops[at] is { Opcode: SetWordOpcode, Bytes.Count: 5 } set && (set.Bytes[1] & 0x0F) == 0)
            {
                constants[(set.Bytes[1] >> 4, set.Bytes[2])] = (short)(set.Bytes[3] | (set.Bytes[4] << 8));
            }
        }

        var leader = ops.Skip(mainStart).Where(op => op.Opcode == PartyPositionOpcode && op.Bytes.Count == 8 && op.Bytes[3] == 0)
            .Select(op => (Bank: op.Bytes[2] & 0x0F, Address: (int)op.Bytes[7]))
            .Distinct()
            .ToArray();
        if (leader.Length == 0)
        {
            return false;
        }

        var known = constants;
        return ops.Skip(mainStart).Any(op =>
            op.Opcode == 0x16 && op.Bytes.Count == 8 && op.Bytes[6] == 0 &&
            (Operand(op, first: true), Operand(op, first: false)) is var (left, right) &&
            ((leader.Contains(left.Var) && Value(right) == triangle) || (leader.Contains(right.Var) && Value(left) == triangle)));

        int? Value(((int Bank, int Address) Var, int? Literal) operand) =>
            operand.Literal ?? (known.TryGetValue(operand.Var, out var value) ? value : null);
    }

    // An IFSW operand: a variable (bank nibble, address) or, for bank 0, the literal itself.
    private static ((int Bank, int Address) Var, int? Literal) Operand(FieldScriptOpcodeDefinition op, bool first)
    {
        var b = op.Bytes;
        var bank = first ? b[1] >> 4 : b[1] & 0x0F;
        var raw = first ? b[2] | (b[3] << 8) : b[4] | (b[5] << 8);
        return bank == 0 ? ((0, -1), (short)raw) : ((bank, raw), null);
    }

    private static FieldEntryPlacementResult? FollowPoll(
        IReadOnlyList<FieldScriptDefinition> scripts,
        FieldScriptDefinition poller,
        IReadOnlyDictionary<(int Bank, int Address), int> constants,
        FieldEntryPlacementResult start)
    {
        var ops = poller.Opcodes;
        var index = ops.Select((op, at) => (op.ByteIndex, at)).ToDictionary(pair => pair.ByteIndex, pair => pair.at);
        var vars = constants.ToDictionary(pair => pair.Key, pair => pair.Value);
        var position = start;
        var at = ops.Select((op, i) => (op, i)).First(pair => pair.op.Opcode == 0x00).i + 1;
        for (var steps = 0; at >= 0 && at < ops.Count; steps++)
        {
            if (steps > 512)
            {
                return null;
            }

            var op = ops[at];
            var b = op.Bytes;
            int Jump(int target) => index.TryGetValue(target, out var next) ? next : -1;
            switch (op.Opcode)
            {
                case 0x00 or 0x12 or 0x13:
                    // One pass of Main: back to the top, or its end.
                    return position;
                case 0x10 when b.Count == 2:
                    at = Jump(op.ByteIndex + 1 + b[1]);
                    continue;
                case 0x11 when b.Count == 3:
                    at = Jump(op.ByteIndex + 1 + (b[1] | (b[2] << 8)));
                    continue;
                case PartyPositionOpcode when b.Count == 8:
                {
                    var banks = new[] { b[1] >> 4, b[1] & 0x0F, b[2] >> 4, b[2] & 0x0F };
                    var values = new int?[] { position.X, position.Y, position.Z, position.Triangle };
                    for (var axis = 0; axis < 4; axis++)
                    {
                        if (b[3] == 0 && values[axis] is { } value)
                        {
                            vars[(banks[axis], b[4 + axis])] = value;
                        }
                        else
                        {
                            vars.Remove((banks[axis], b[4 + axis]));
                        }
                    }

                    at++;
                    continue;
                }
                case 0x16 when b.Count == 8:
                {
                    var left = Operand(op, first: true);
                    var right = Operand(op, first: false);
                    int? Read(((int Bank, int Address) Var, int? Literal) operand) =>
                        operand.Literal ?? (vars.TryGetValue(operand.Var, out var value) ? value : null);
                    if (Read(left) is not { } l || Read(right) is not { } r || Compare(l, r, b[6]) is not { } holds || b[6] > 5)
                    {
                        return null;
                    }

                    at = holds ? at + 1 : Jump(op.ByteIndex + 7 + b[7]);
                    continue;
                }
                case 0x30 or 0x31 when b.Count == 4:
                    // IFKEY and IFKEYON: a jump the player asks for with a key is theirs to take,
                    // so the arrival is where they stand until they do (Cosmo Canyon's stairwell,
                    // cosin5, lands on t54, one of its OK-to-climb triangles).
                    at = Jump(op.ByteIndex + 3 + b[3]);
                    continue;
                case 0x04 or 0x05 or 0x06 when b.Count == 3 && b[1] == 0:
                    // The leader runs its own script: whichever character leads, so the party
                    // characters have to agree on where it puts them.
                    if (LeaderJump(scripts, b[2] & 0x1F, position) is not { } jumped)
                    {
                        return null;
                    }

                    position = jumped;
                    at++;
                    continue;
                default:
                    if (Presentation.Contains(op.Opcode))
                    {
                        at++;
                        continue;
                    }

                    // A request, a write, a test of a value this pass does not know, a
                    // placement, or anything unlisted: not followed.
                    return null;
            }
        }

        return null;
    }

    // Where the party characters' script number puts the leader: only literal JUMPs between
    // presentation opcodes, and the same final JUMP in every character that has the script.
    private static FieldEntryPlacementResult? LeaderJump(IReadOnlyList<FieldScriptDefinition> scripts, int scriptId, FieldEntryPlacementResult start)
    {
        var characters = FieldSourceTransfers.PartyCharacters(scripts).ToHashSet();
        FieldEntryPlacementResult? agreed = null;
        var count = 0;
        foreach (var script in scripts.Where(script => characters.Contains(script.EntityId) && script.ScriptId == scriptId))
        {
            var position = start;
            var jumped = false;
            var ended = false;
            foreach (var op in script.Opcodes)
            {
                var b = op.Bytes;
                if (ended)
                {
                    break;
                }

                if (op.Opcode == 0xC0 && b.Count == 11 && b[1] == 0 && b[2] == 0)
                {
                    position = new FieldEntryPlacementResult((short)(b[3] | (b[4] << 8)), (short)(b[5] | (b[6] << 8)), null, b[7] | (b[8] << 8));
                    jumped = true;
                }
                else if (op.Opcode == 0x00)
                {
                    ended = true;
                }
                else if (!Presentation.Contains(op.Opcode) &&
                         !(op.Opcode == SetByteOpcode && op.Bytes.Count == 4 && FieldBankByte.BlockOf(op.Bytes[1] >> 4) == 5))
                {
                    // Anything that could place, branch, hand over or write the savemap is not a
                    // plain jump. The zone byte a jump leaves in the temporary block is not read here.
                    return null;
                }
            }

            if (!jumped || agreed is { } other && other != position)
            {
                return null;
            }

            agreed = position;
            count++;
        }

        return count >= 2 ? agreed : null;
    }

    private const int TemporaryBlock = 5;
    private const int PartyCharacterOpcode = 0xA0;
    private const int PartyPositionOpcode = 0x75;
    private const int SetByteOpcode = 0x80;
    private const int SetWordOpcode = 0x81;
    private const int MapJumpOpcode = 0x60;
}
