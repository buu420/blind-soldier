using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>The seven Great Glacier treasures, in the order the Objects list names them.</summary>
public enum GreatGlacierTreasure
{
    MindSource,
    Potion,
    SafetyBit,
    Elixir,
    AddedCut,
    All,
    Alexander
}

/// <summary>
/// What a regional treasure route is walking to right now. Alexander is the only treasure
/// with native prerequisites: hyou13_2's Snow fights (battle 679) only once the hot spring in
/// hyou10 has been touched (bank 1 byte 199 bit 0), and the materia model is shown only once
/// she has been beaten (bit 2); picking it up sets bit 4.
/// </summary>
public enum GreatGlacierGoalStage
{
    Treasure,
    HotSpring,
    Snow
}

/// <param name="Treasure">The treasure the player asked for.</param>
/// <param name="Stage">The native step that has to happen next on the way to it.</param>
/// <param name="FieldId">The field the step is in.</param>
/// <param name="EntityId">The entity the step is: the treasure model, the spring LINE or Snow.</param>
public readonly record struct GreatGlacierGoalPoint(
    GreatGlacierTreasure Treasure,
    GreatGlacierGoalStage Stage,
    int FieldId,
    int EntityId)
{
    /// <summary>Added Cut stands in move_d (675) only while bank 1 byte 184 is 75 (its Init hides it otherwise).</summary>
    public int? RequiredCorridorState =>
        Stage == GreatGlacierGoalStage.Treasure && Treasure == GreatGlacierTreasure.AddedCut ? 75 : null;

    public string Key => $"{FieldId}:{EntityId}:{Stage}";
}

/// <param name="FlagAddress">Bank 1 byte the treasure's Init tests and its pickup sets.</param>
/// <param name="FlagMask">The bit that byte holds for this treasure.</param>
/// <param name="FallbackName">Used only when the kernel text cannot be read.</param>
public readonly record struct GreatGlacierTreasureDefinition(
    GreatGlacierTreasure Treasure,
    int FieldId,
    int EntityId,
    int FlagAddress,
    byte FlagMask,
    string FallbackName);

/// <summary>
/// The Great Glacier region's native facts, each read from the installed scripts and checked
/// by GreatGlacierRegionalItemsTests: the seven treasures and their bank 1 gates, Alexander's
/// prerequisites, the screens that belong to the region and the snowfield's world map.
/// </summary>
public static class GreatGlacierRegion
{
    public const int SnowfieldWorldMapType = 3;
    public const int MapScreenField = GreatGlacierMapScreen.FieldId;
    public const int HotSpringField = 680;
    public const int HotSpringEntity = 20;
    public const int SnowField = 684;
    public const int SnowEntity = 13;
    public const int QuestFlagAddress = 199;
    public const byte HotSpringTouchedMask = 0x01;
    public const byte SnowBeatenMask = 0x04;
    public const int CabinFrontRoom = 687;
    public const int CabinBackRoom = 688;
    public const int CliffBase = 686;
    public const int LakeField = GreatGlacierIceFloePuzzle.FieldId;
    public const int SouthShoreCrossingEntity = 21;
    public const int NorthShoreCrossingEntity = 22;

    /// <summary>
    /// Where a successful crossing puts Cloud on each shore: the destinations of the lake's
    /// own jump transitions for floe 3 facing north and floe 23 facing south (and where a
    /// fall returns him).
    /// </summary>
    public static readonly (int X, int Y, int Triangle) NorthShoreLanding = (177, 320, 13);

    public static readonly (int X, int Y, int Triangle) SouthShoreLanding = (163, -814, 111);

    /// <summary>
    /// Field, entity and native gate for each treasure: the Init tests the bank 1 bit and hides
    /// the model once it is set, and the pickup script sets it (root-native-evidence.json).
    /// </summary>
    public static IReadOnlyList<GreatGlacierTreasureDefinition> Treasures { get; } =
    [
        new(GreatGlacierTreasure.MindSource, 659, 17, 37, 0x08, "Mind Source"),
        new(GreatGlacierTreasure.Potion, 664, 23, 37, 0x02, "Potion"),
        new(GreatGlacierTreasure.SafetyBit, 666, 13, 37, 0x04, "Safety Bit"),
        new(GreatGlacierTreasure.Elixir, 678, 12, 37, 0x01, "Elixir"),
        new(GreatGlacierTreasure.AddedCut, 675, 20, 199, 0x20, "Added Cut Materia"),
        new(GreatGlacierTreasure.All, 682, 11, 199, 0x40, "All Materia"),
        new(GreatGlacierTreasure.Alexander, 684, 14, 199, 0x10, "Alexander Materia")
    ];

    public static GreatGlacierTreasureDefinition Definition(GreatGlacierTreasure treasure) =>
        Treasures.First(definition => definition.Treasure == treasure);

    /// <summary>
    /// Every screen the regional list and route belong to: the glacier screens 658..684
    /// (not the Glacier Map, 669), and the cabin and the ground outside it (686..688).
    /// 685 is not connected to the glacier.
    /// </summary>
    public static bool IsRegionField(int fieldId) =>
        fieldId is >= 658 and <= 688 && fieldId is not (MapScreenField or 685);

    public static bool IsGlacierScreen(int fieldId) =>
        fieldId is >= 658 and <= 684 && fieldId != MapScreenField;

    public static bool IsPassage(int fieldId) => GreatGlacierExitLabels.IsPassage(fieldId);

    /// <summary>
    /// The native step the treasure is waiting on, or null once it has been picked up. Null
    /// with <paramref name="readable"/> false when a bank byte could not be read.
    /// </summary>
    public static GreatGlacierGoalPoint? CurrentGoal(
        GreatGlacierTreasure treasure,
        Func<int, byte?> readBank1,
        out bool readable)
    {
        var definition = Definition(treasure);
        readable = false;
        if (readBank1(definition.FlagAddress) is not { } flags)
        {
            return null;
        }

        readable = true;
        if ((flags & definition.FlagMask) != 0)
        {
            return null;
        }

        if (treasure == GreatGlacierTreasure.Alexander)
        {
            if (readBank1(QuestFlagAddress) is not { } quest)
            {
                readable = false;
                return null;
            }

            if ((quest & HotSpringTouchedMask) == 0)
            {
                return new GreatGlacierGoalPoint(treasure, GreatGlacierGoalStage.HotSpring, HotSpringField, HotSpringEntity);
            }

            if ((quest & SnowBeatenMask) == 0)
            {
                return new GreatGlacierGoalPoint(treasure, GreatGlacierGoalStage.Snow, SnowField, SnowEntity);
            }
        }

        return new GreatGlacierGoalPoint(treasure, GreatGlacierGoalStage.Treasure, definition.FieldId, definition.EntityId);
    }

    /// <summary>Every goal point the region's route table is built for.</summary>
    public static IReadOnlyList<GreatGlacierGoalPoint> AllGoalPoints { get; } =
        Treasures
            .Select(definition => new GreatGlacierGoalPoint(definition.Treasure, GreatGlacierGoalStage.Treasure, definition.FieldId, definition.EntityId))
            .Append(new GreatGlacierGoalPoint(GreatGlacierTreasure.Alexander, GreatGlacierGoalStage.HotSpring, HotSpringField, HotSpringEntity))
            .Append(new GreatGlacierGoalPoint(GreatGlacierTreasure.Alexander, GreatGlacierGoalStage.Snow, SnowField, SnowEntity))
            .ToArray();
}

/// <summary>
/// Estimated movement cost, in native movement ticks.
///
/// <para>Field: 006342c6 moves the leader by (script-header scale * 2) / 256 of a 4096-unit
/// fixed vector per movement tick without Run (00636c41), so scale / 128 units; every
/// connected Glacier, cabin and passage screen has scale 512 in both installs, so 4 units a
/// tick walking. World: FUN_0074EA48 moves the party on foot 0x1E = 30 units a tick. Slopes,
/// Run and special walk modes change the real pace and encounters and fades add time, so
/// these are estimates of ordinary walking, never promised timings.</para>
///
/// <para>A screen change is not movement. It only breaks ties between otherwise equal routes,
/// so a route never takes an extra screen for nothing.</para>
/// </summary>
public static class GreatGlacierMovementCost
{
    public const double FieldUnitsPerTick = 4d;
    public const double WorldUnitsPerTick = 30d;
    public const double ScreenChangeTieBreak = 0.001d;

    /// <summary>
    /// The ice-floe crossing, in ticks: the shortest native solution is 14 jumps (see
    /// GreatGlacierIceFloePuzzle) and a jump is costed as 32 ticks of walking. Only used to
    /// rank routes: the lake is the only way to hyou5_4 and the Safety Bit, so no choice
    /// depends on the exact figure.
    /// </summary>
    public const double IceFloeCrossingTicks = 14 * 32;

    public static double Field(double units) => units / FieldUnitsPerTick;

    public static double World(double units) => units / WorldUnitsPerTick;
}

/// <summary>
/// Runs the small part of the field script machine the region graph needs, the same way
/// GreatGlacierExitLabelTests rebuilds the passage tables: a LINE's scripts with bank 1 byte
/// 184 given, to the MAPJUMP it runs and the byte it leaves; and each screen's Init and one
/// pass of every main loop, to the IDLCK locks that byte keeps on (move_f's idlock entity).
/// </summary>
public static class GreatGlacierScriptMachine
{
    private const int MapJumpOpcode = 0x60;
    private const int SetByteOpcode = 0x80;
    private const int IdLockOpcode = 0x6D;
    private const int CorridorByte = 184;

    /// <summary>A LINE's MAPJUMP with byte 184 = <paramref name="state"/>: (field, x, y, triangle, byte 184 after).</summary>
    public static (int Field, int X, int Y, int Triangle, int State)? LineJump(
        IReadOnlyList<FieldScriptDefinition> scripts,
        int entity,
        int state)
    {
        for (var slot = 1; slot <= 6; slot++)
        {
            if (!scripts.Any(script => script.EntityId == entity && script.ScriptId == slot))
            {
                continue;
            }

            var bank = new Dictionary<int, int> { [CorridorByte] = state };
            if (Run(scripts, entity, slot, -1, bank, 0, wordTests: false, locks: null, stopAtMapJump: true) is { } jump)
            {
                return (BitConverter.ToUInt16(jump, 1), BitConverter.ToInt16(jump, 3), BitConverter.ToInt16(jump, 5),
                    BitConverter.ToUInt16(jump, 7), bank[CorridorByte]);
            }
        }

        return null;
    }

    /// <summary>
    /// The triangles locked once the screen has loaded with byte 184 = <paramref name="state"/>:
    /// every entity's Init, then one pass of every main loop, following REQ/REQSW/REQEW into
    /// the scripts they request. IDLCK 1 locks, 0 unlocks (0061e29f). Tests on banks this
    /// cannot know read as 0.
    /// </summary>
    public static IReadOnlySet<int> LockedTriangles(IReadOnlyList<FieldScriptDefinition> scripts, int state)
    {
        var locks = new Dictionary<int, bool>();
        var bank = new Dictionary<int, int> { [CorridorByte] = state };
        var entities = scripts.Where(script => script.ScriptId == 0).Select(script => script.EntityId).Distinct().Order().ToArray();
        foreach (var entity in entities)
        {
            _ = Run(scripts, entity, 0, -1, bank, 0, wordTests: true, locks, stopAtMapJump: true);
        }

        foreach (var entity in entities)
        {
            var init = scripts.First(script => script.EntityId == entity && script.ScriptId == 0);
            var firstReturn = init.Opcodes.FirstOrDefault(op => op.Opcode == 0x00);
            if (firstReturn.Bytes is null)
            {
                continue;
            }

            _ = Run(scripts, entity, 0, firstReturn.ByteIndex + 1, bank, 0, wordTests: true, locks, stopAtMapJump: true);
        }

        return locks.Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet();
    }

    private static byte[]? Run(
        IReadOnlyList<FieldScriptDefinition> scripts,
        int entity,
        int slot,
        int startOffset,
        Dictionary<int, int> bank,
        int depth,
        bool wordTests,
        Dictionary<int, bool>? locks,
        bool stopAtMapJump)
    {
        if (depth > 8)
        {
            return null;
        }

        var script = scripts.FirstOrDefault(candidate => candidate.EntityId == entity && candidate.ScriptId == slot);
        if (script.Opcodes is null || script.Opcodes.Count == 0)
        {
            return null;
        }

        var byOffset = new Dictionary<int, FieldScriptOpcodeDefinition>();
        foreach (var op in script.Opcodes)
        {
            byOffset.TryAdd(op.ByteIndex, op);
        }

        var offset = startOffset >= 0 ? startOffset : script.Opcodes[0].ByteIndex;
        for (var steps = 0; steps < 3000 && byOffset.TryGetValue(offset, out var op); steps++)
        {
            var b = op.Bytes as byte[] ?? op.Bytes.ToArray();
            var next = offset + b.Length;
            switch (op.Opcode)
            {
                case 0x00:
                    return null;
                case 0x01 or 0x02 or 0x03:
                    if (Run(scripts, b[1], b[2] & 31, -1, bank, depth + 1, wordTests, locks, stopAtMapJump) is { } called)
                    {
                        return called;
                    }

                    break;
                case 0x10:
                    next = offset + 1 + b[1];
                    break;
                case 0x11:
                    next = offset + 1 + BitConverter.ToUInt16(b, 1);
                    break;
                case 0x12 or 0x13:
                    // A backward jump closes one pass of a loop.
                    return null;
                case 0x14 or 0x15:
                {
                    var left = Value(bank, b[1] >> 4, b[2]);
                    var right = Value(bank, b[1] & 15, b[3]);
                    if (!Passes(b[4], left, right))
                    {
                        next = offset + 5 + (op.Opcode == 0x15 ? BitConverter.ToUInt16(b, 5) : b[5]);
                    }

                    break;
                }
                case 0x16 or 0x17 when wordTests:
                {
                    // Word tests: only literals are known; every savemap word reads as 0.
                    var left = (b[1] >> 4) == 0 ? BitConverter.ToInt16(b, 2) : 0;
                    var right = (b[1] & 15) == 0 ? BitConverter.ToInt16(b, 4) : 0;
                    if (!Passes(b[6], left, right))
                    {
                        next = offset + 7 + (op.Opcode == 0x17 ? BitConverter.ToUInt16(b, 7) : b[7]);
                    }

                    break;
                }
                case SetByteOpcode:
                {
                    var value = (b[1] & 15) switch
                    {
                        0 => b[3],
                        1 => bank.GetValueOrDefault(b[3]),
                        5 => bank.GetValueOrDefault(1000 + b[3]),
                        _ => 0
                    };
                    if (b[1] >> 4 == 1) bank[b[2]] = value;
                    if (b[1] >> 4 == 5) bank[1000 + b[2]] = value;
                    break;
                }
                case IdLockOpcode when locks is not null:
                    locks[BitConverter.ToUInt16(b, 1)] = b[3] != 0;
                    break;
                case MapJumpOpcode:
                    if (stopAtMapJump)
                    {
                        return b;
                    }

                    break;
            }

            offset = next;
        }

        return null;
    }

    private static int Value(Dictionary<int, int> bank, int nibble, int raw) => nibble switch
    {
        0 => raw,
        1 => bank.GetValueOrDefault(raw),
        5 => bank.GetValueOrDefault(1000 + raw),
        _ => 0
    };

    private static bool Passes(int comparison, int left, int right) => comparison switch
    {
        0 => left == right,
        1 => left != right,
        2 => left > right,
        3 => left < right,
        4 => left >= right,
        5 => left <= right,
        6 => (left & right) != 0,
        9 => ((left >> right) & 1) == 1,
        10 => ((left >> right) & 1) == 0,
        _ => false
    };

    /// <summary>
    /// wm3.ev system function 0: where the party is put on the snowfield for each entry
    /// location. Each branch compares the entry (literal L, compare 0x70), then calls
    /// set_mesh_coords (0x308) with the mesh cell and set_coords_in_mesh (0x309) with the
    /// offset inside it. Location 60 lands at (28576, 9692) and 61 at (9692, 36768), as the
    /// 2026-09 logs record.
    /// </summary>
    public static IReadOnlyDictionary<int, (int X, int Z)> ReadSnowfieldPlacements(byte[] events)
    {
        var placements = new Dictionary<int, (int X, int Z)>();
        var start = -1;
        var ends = new List<int>();
        for (var index = 0; index < 256; index++)
        {
            var id = BitConverter.ToUInt16(events, index * 4);
            var address = BitConverter.ToUInt16(events, index * 4 + 2);
            if (id == 0xFFFF || address == 0xFFFF)
            {
                continue;
            }

            var offset = 0x400 + address * 2;
            ends.Add(offset);
            if (id == 0)
            {
                start = offset;
            }
        }

        if (start < 0)
        {
            return placements;
        }

        var end = ends.Where(offset => offset > start).DefaultIfEmpty(events.Length).Min();
        var history = new List<(int Opcode, int Argument)>();
        for (var at = start; at <= end - 2;)
        {
            var opcode = BitConverter.ToUInt16(events, at);
            var words = opcode is > 0x100 and < 0x200 or 0x200 or 0x201 ? 2 : 1;
            var argument = words == 2 ? BitConverter.ToUInt16(events, at + 2) : -1;
            if (opcode == 0x309 && TryReadPlacement(history, out var location, out var placement))
            {
                placements[location] = placement;
            }

            history.Add((opcode, argument));
            at += words * 2;
        }

        return placements;
    }

    private static bool TryReadPlacement(
        List<(int Opcode, int Argument)> history,
        out int location,
        out (int X, int Z) placement)
    {
        location = -1;
        placement = default;
        var literals = new List<int>();
        var index = history.Count - 1;
        for (; index >= 0 && literals.Count < 2; index--)
        {
            if (history[index].Opcode == 0x110) literals.Add(history[index].Argument);
            else if (history[index].Opcode == 0x308) return false;
        }

        if (literals.Count < 2) return false;
        var (offsetX, offsetZ) = (literals[1], literals[0]);
        while (index >= 0 && history[index].Opcode != 0x308) index--;
        if (index < 0) return false;
        var mesh = new List<int>();
        index--;
        for (; index >= 0 && mesh.Count < 2; index--)
        {
            if (history[index].Opcode == 0x110) mesh.Add(history[index].Argument);
            else if (history[index].Opcode is 0x70 or 0x308 or 0x309) return false;
        }

        if (mesh.Count < 2) return false;
        var (meshX, meshZ) = (mesh[1], mesh[0]);
        while (index >= 0 && history[index].Opcode != 0x70) index--;
        if (index < 1 || history[index - 1].Opcode != 0x110) return false;
        location = history[index - 1].Argument;
        placement = (meshX * WorldMapDataLoader.MeshSize + offsetX, meshZ * WorldMapDataLoader.MeshSize + offsetZ);
        return true;
    }

    /// <summary>
    /// wm3.ev's terrain handlers that enter a field: (location, byte 184 written first or null).
    /// The west, south and east edges write 52, 53 and 55.
    /// </summary>
    public static IReadOnlyDictionary<int, int?> ReadSnowfieldEntryCorridors(byte[] events)
    {
        var entries = new List<(int Id, int Offset)>();
        for (var index = 0; index < 256; index++)
        {
            var id = BitConverter.ToUInt16(events, index * 4);
            var address = BitConverter.ToUInt16(events, index * 4 + 2);
            if (id != 0xFFFF && address != 0xFFFF) entries.Add((id, 0x400 + address * 2));
        }

        var starts = entries.Select(entry => entry.Offset).Distinct().Order().ToArray();
        var result = new Dictionary<int, int?>();
        foreach (var (id, offset) in entries.Where(entry => (entry.Id & 0xC000) == 0x8000))
        {
            var end = starts.FirstOrDefault(value => value > offset, events.Length);
            var history = new List<(int Opcode, int Argument)>();
            for (var at = offset; at <= end - 2;)
            {
                var opcode = BitConverter.ToUInt16(events, at);
                var words = opcode is > 0x100 and < 0x200 or 0x200 or 0x201 ? 2 : 1;
                var argument = words == 2 ? BitConverter.ToUInt16(events, at + 2) : -1;
                if (opcode == 0x318 && history.Count >= 2 && history[^1].Opcode == 0x110 && history[^2].Opcode == 0x110)
                {
                    int? corridor = null;
                    for (var index = 2; index < history.Count; index++)
                    {
                        if (history[index].Opcode == 0xE0 && history[index - 1].Opcode == 0x110 &&
                            history[index - 2] == (0x118, 0xB8))
                        {
                            corridor = history[index - 1].Argument;
                        }
                    }

                    result[history[^2].Argument] = corridor;
                }

                history.Add((opcode, argument));
                at += words * 2;
            }
        }

        _ = starts;
        return result;
    }
}

/// <summary>What the regional graph is built from; all of it installed game data.</summary>
public sealed record GreatGlacierRegionInputs(
    FieldScriptNavigationCatalog Catalog,
    Func<int, byte[]?> ReadDecodedField,
    WorldMapData Snowfield,
    IReadOnlyList<WorldMapNavigationTarget> SnowfieldLocations,
    IReadOnlySet<int> SnowfieldEntranceTriangles,
    byte[] FieldTable,
    byte[] SnowfieldEvents,
    IReadOnlyList<FieldNavigationObjectDefinition> ObjectDefinitions);

/// <summary>
/// The Great Glacier region as a graph of the places the game puts the party - every native
/// arrival (field, position, triangle, and bank 1 byte 184 on the six passage screens) and the
/// five snowfield entries - joined by the native ways out of each, costed by walking length on
/// the installed walkmeshes and WM3.MAP (see <see cref="GreatGlacierMovementCost"/>).
///
/// <para>One-way paths are one-way here: an edge exists only where the exit's own MAPJUMP
/// leads. Passage exits follow the byte the screen was entered with; a passage exit that
/// jumps back into the same screen is a leg of its own, with its new byte. Walkmesh locks the
/// passage screens keep on (move_f's idlock entity) are modelled per byte, and an arrival may
/// leave a locked starting triangle but nothing enters one (006367b7 tests only the triangle
/// being entered). The ice floes are not walkable: crossing the lake is its own edge, to the
/// shore the crossing lands on, for the player to solve.</para>
///
/// <para>Built once, off the polling thread; afterwards it is read-only.</para>
/// </summary>
public sealed class GreatGlacierRegionGraph
{
    public const int NoWorldLocation = -1;

    public sealed record Node(int Index, int FieldId, int X, int Y, int Triangle, int State, int WorldLocation)
    {
        public bool IsWorld => WorldLocation != NoWorldLocation;

        public string Key => IsWorld ? $"W{WorldLocation}" : NodeKey(FieldId, X, Y, Triangle, State);
    }

    public enum EdgeKind
    {
        ScriptExit,
        Gateway,
        Snowfield,
        IceFloeCrossing
    }

    public sealed record Edge(int From, int To, EdgeKind Kind, string ExitId, int ExitEntity, double FieldUnits, double WorldUnits)
    {
        public double Ticks =>
            GreatGlacierMovementCost.Field(FieldUnits) +
            GreatGlacierMovementCost.World(WorldUnits) +
            (Kind == EdgeKind.IceFloeCrossing ? GreatGlacierMovementCost.IceFloeCrossingTicks : GreatGlacierMovementCost.ScreenChangeTieBreak);
    }

    private readonly Dictionary<string, int> nodeIndex;
    private readonly Dictionary<string, double[]> costToGoal;
    private readonly Dictionary<string, int[]> nextEdgeToGoal;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<FieldScriptDefinition>> scripts;
    private readonly Dictionary<(int Field, int Entity, int State), (int Field, int X, int Y, int Triangle, int State)?> jumpCache = new();
    private readonly object jumpLock = new();

    private GreatGlacierRegionGraph(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<Edge> edges,
        IReadOnlyDictionary<int, IReadOnlyList<FieldScriptDefinition>> scripts,
        IReadOnlyDictionary<int, (int X, int Z)> snowfieldPlacements,
        IReadOnlyDictionary<int, int?> snowfieldCorridors,
        IReadOnlyDictionary<int, IReadOnlyList<(int Index, int Destination, int X, int Y, int Triangle)>> gateways,
        IReadOnlyDictionary<(int Field, int State), IReadOnlySet<int>> modelledLocks,
        TimeSpan buildTime)
    {
        Nodes = nodes;
        Edges = edges;
        this.scripts = scripts;
        SnowfieldPlacements = snowfieldPlacements;
        SnowfieldCorridors = snowfieldCorridors;
        GatewayArrivals = gateways;
        ModelledLocks = modelledLocks;
        BuildTime = buildTime;
        nodeIndex = nodes.ToDictionary(node => node.Key, node => node.Index);
        costToGoal = new Dictionary<string, double[]>();
        nextEdgeToGoal = new Dictionary<string, int[]>();
    }

    public IReadOnlyList<Node> Nodes { get; }

    public IReadOnlyList<Edge> Edges { get; }

    public IReadOnlyDictionary<int, (int X, int Z)> SnowfieldPlacements { get; }

    public IReadOnlyDictionary<int, int?> SnowfieldCorridors { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<(int Index, int Destination, int X, int Y, int Triangle)>> GatewayArrivals { get; }

    /// <summary>The IDLCK set each passage screen is modelled with, per byte 184 value seen.</summary>
    public IReadOnlyDictionary<(int Field, int State), IReadOnlySet<int>> ModelledLocks { get; }

    public TimeSpan BuildTime { get; }

    public static string NodeKey(int field, int x, int y, int triangle, int state) =>
        $"F{field}:{(GreatGlacierRegion.IsPassage(field) ? state : 0)}:{x},{y},t{triangle}";

    public Node? FindNode(int field, int x, int y, int triangle, int state) =>
        nodeIndex.TryGetValue(NodeKey(field, x, y, triangle, state), out var index) ? Nodes[index] : null;

    public Node? FindWorldNode(int location) =>
        nodeIndex.TryGetValue($"W{location}", out var index) ? Nodes[index] : null;

    /// <summary>
    /// Estimated ticks from <paramref name="node"/> to the goal, walking the best native route;
    /// +infinity when the goal cannot be reached from there.
    /// </summary>
    public double CostToGoal(GreatGlacierGoalPoint goal, Node node) =>
        costToGoal.TryGetValue(goal.Key, out var costs) ? costs[node.Index] : double.PositiveInfinity;

    /// <summary>The first edge of the best route from <paramref name="node"/>, or null at the goal's field.</summary>
    public Edge? NextEdge(GreatGlacierGoalPoint goal, Node node) =>
        nextEdgeToGoal.TryGetValue(goal.Key, out var next) && next[node.Index] >= 0 ? Edges[next[node.Index]] : null;

    /// <summary>The whole best route from <paramref name="node"/>: the exits in order.</summary>
    public IReadOnlyList<Edge> Route(GreatGlacierGoalPoint goal, Node node)
    {
        var route = new List<Edge>();
        var current = node;
        for (var guard = 0; guard < Nodes.Count && NextEdge(goal, current) is { } edge; guard++)
        {
            route.Add(edge);
            current = Nodes[edge.To];
        }

        return route;
    }

    /// <summary>
    /// Where a LINE of <paramref name="field"/> leads with byte 184 = <paramref name="state"/>
    /// (ordinary screens are read with 0: their exits write the byte themselves).
    /// </summary>
    public (int Field, int X, int Y, int Triangle, int State)? LineOutcome(int field, int entity, int state)
    {
        if (!scripts.TryGetValue(field, out var fieldScripts))
        {
            return null;
        }

        var key = (field, entity, GreatGlacierRegion.IsPassage(field) ? state : 0);
        lock (jumpLock)
        {
            if (jumpCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var outcome = GreatGlacierScriptMachine.LineJump(fieldScripts, entity, key.Item3);
        lock (jumpLock)
        {
            jumpCache[key] = outcome;
        }

        return outcome;
    }

    /// <summary>
    /// The node an exit of this screen leads to: a region arrival, a snowfield entry, or null
    /// when it leaves the region (the main world map) or cannot be followed.
    /// </summary>
    public Node? ExitDestination(int field, int state, FieldNavigationTarget exit)
    {
        if (TryParseGateway(exit.StableId, out var gatewayField, out var gatewayIndex, out _) && gatewayField == field)
        {
            if (!GatewayArrivals.TryGetValue(field, out var gateways) ||
                gateways.FirstOrDefault(gateway => gateway.Index == gatewayIndex) is not { Destination: > 0 } gateway)
            {
                return null;
            }

            return gateway.Destination is >= 60 and <= 64
                ? FindWorldNode(gateway.Destination)
                : FindNode(gateway.Destination, gateway.X, gateway.Y, gateway.Triangle, 0);
        }

        if (!TryParseScriptExit(exit.StableId, out var exitField, out var entity) || exitField != field ||
            LineOutcome(field, entity, state) is not { } jump)
        {
            return null;
        }

        return jump.Field is >= 60 and <= 64
            ? FindWorldNode(jump.Field)
            : FindNode(jump.Field, jump.X, jump.Y, jump.Triangle, jump.State);
    }

    public static bool TryParseScriptExit(string stableId, out int field, out int entity)
    {
        field = 0;
        entity = 0;
        var parts = stableId.Split(':');
        return parts.Length >= 3 && parts[0] == "script-exit" &&
               int.TryParse(parts[1], out field) && int.TryParse(parts[2], out entity);
    }

    public static bool TryParseGateway(string stableId, out int field, out int index, out int destination)
    {
        field = 0;
        index = 0;
        destination = 0;
        var parts = stableId.Split(':');
        return parts.Length >= 4 && parts[0] == "gateway" &&
               int.TryParse(parts[1], out field) && int.TryParse(parts[2], out index) &&
               int.TryParse(parts[3], out destination);
    }

    /// <summary>The snowfield location a world-map target is, from its "world-location:N:" id.</summary>
    public static int? SnowfieldLocationId(WorldMapNavigationTarget target)
    {
        var parts = target.StableId.Split(':');
        return parts.Length >= 2 && parts[0] == "world-location" && int.TryParse(parts[1], out var id) && id is >= 60 and <= 64
            ? id
            : null;
    }

    /// <summary>
    /// The length of a planned field route: from the start along its funnel waypoints (or the
    /// planner's own override) to the final approach, in the X/Y plane.
    /// </summary>
    public static double RouteLength(int startX, int startY, int startZ, FieldNavigationRoutePlan plan)
    {
        var steps = plan.StableWaypointsOverride ??
            FieldWalkmeshPathfinder.BuildStableWaypoints(startX, startY, startZ, plan.Portals, plan.FinalApproach);
        double total = 0;
        double x = startX;
        double y = startY;
        foreach (var step in steps)
        {
            total += Math.Sqrt(Math.Pow(step.Waypoint.X - x, 2) + Math.Pow(step.Waypoint.Y - y, 2));
            x = step.Waypoint.X;
            y = step.Waypoint.Y;
        }

        return total;
    }

    /// <summary>Which shore of the frozen lake (665) a point is on: the lake's shores are the two ends of +Y and -Y.</summary>
    public static bool IsNorthShore(int y) => y > 0;

    public static GreatGlacierRegionGraph Build(GreatGlacierRegionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var fields = Enumerable.Range(658, 31).Where(GreatGlacierRegion.IsRegionField).ToArray();
        var scripts = fields.ToDictionary(field => field, field => inputs.Catalog.ReadAllScriptOpcodes(field));
        var raw = new Dictionary<int, byte[]>();
        foreach (var field in fields)
        {
            raw[field] = inputs.ReadDecodedField(field) ?? throw new InvalidDataException($"Great Glacier field {field} is not installed.");
        }

        var reads = fields.ToDictionary(field => field, field => inputs.Catalog.ReadField(field));
        var gateways = fields.ToDictionary(
            field => field,
            field => (IReadOnlyList<(int Index, int Destination, int X, int Y, int Triangle)>)ReadGateways(field, raw[field])
                .Select(gateway => (gateway.Index, gateway.Destination, gateway.X, gateway.Y, gateway.Triangle)).ToArray());
        var gatewayLines = fields.ToDictionary(field => field, field => ReadGateways(field, raw[field]));
        var placements = GreatGlacierScriptMachine.ReadSnowfieldPlacements(inputs.SnowfieldEvents);
        var corridors = GreatGlacierScriptMachine.ReadSnowfieldEntryCorridors(inputs.SnowfieldEvents);
        var worldPlanner = new WorldMapRoutePlanner(inputs.Snowfield) { EntranceTriangleIds = inputs.SnowfieldEntranceTriangles };
        var locations = inputs.SnowfieldLocations
            .Select(location => (Id: SnowfieldLocationId(location), Target: location))
            .Where(pair => pair.Id is not null)
            .ToDictionary(pair => pair.Id!.Value, pair => pair.Target);

        var meshes = new Dictionary<int, FieldWalkmesh>();
        var lockCache = new Dictionary<(int Field, int State), IReadOnlySet<int>>();
        var planners = new Dictionary<(int Field, string Locks), FieldWalkmeshRoutePlanner>();
        FieldWalkmeshReader MeshReader(int field)
        {
            var bytes = raw[field];
            const int basePointer = 0x03000000;
            return new FieldWalkmeshReader(
                address => address == FieldWalkmeshReader.AddressFieldDataPtr
                    ? basePointer
                    : address - basePointer is var offset && offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToInt32(bytes, offset) : 0,
                address => address - basePointer is var offset && offset >= 0 && offset + 2 <= bytes.Length ? BitConverter.ToInt16(bytes, offset) : (short)0);
        }

        foreach (var field in fields)
        {
            meshes[field] = MeshReader(field).Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, 0, 0, 0, 0, 0)).Walkmesh
                ?? throw new InvalidDataException($"Great Glacier field {field} has no walkmesh.");
        }

        IReadOnlySet<int> LocksFor(int field, int state)
        {
            var key = (field, GreatGlacierRegion.IsPassage(field) ? state : 0);
            if (!lockCache.TryGetValue(key, out var locked))
            {
                locked = GreatGlacierScriptMachine.LockedTriangles(scripts[field], key.Item2);
                lockCache[key] = locked;
            }

            return locked;
        }

        FieldWalkmeshRoutePlanner PlannerFor(int field, IReadOnlySet<int> locked)
        {
            var key = (field, string.Join(',', locked.Order()));
            if (planners.TryGetValue(key, out var planner))
            {
                return planner;
            }

            // The lake's own transitions are the floe jumps, which are the puzzle; they are
            // never walked, so the lake is planned on its walkmesh alone.
            var transitions = field == GreatGlacierRegion.LakeField
                ? Array.Empty<FieldScriptNavigationTransition>()
                : reads[field].Transitions;
            FieldBoundaryStateReader? boundary = null;
            if (locked.Count > 0)
            {
                var bits = new byte[FieldBoundaryStateReader.BoundaryByteCount];
                foreach (var triangle in locked)
                {
                    bits[triangle >> 3] |= (byte)(1 << (triangle & 7));
                }

                const int globalPointer = 0x04000000;
                var bitsAddress = globalPointer + FieldBoundaryStateReader.BoundaryBitsOffset;
                boundary = new FieldBoundaryStateReader(
                    address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? globalPointer : 0,
                    address => address == FieldPositionReader.AddressCurrentModule ? (byte)FieldPositionReader.FieldModule
                        : address == FieldPositionReader.AddressFieldId ? (byte)(field & 0xFF)
                        : address == FieldPositionReader.AddressFieldId + 1 ? (byte)(field >> 8)
                        : address >= bitsAddress && address < bitsAddress + bits.Length ? bits[address - bitsAddress] : (byte)0,
                    (_, _) => true);
            }

            planner = new FieldWalkmeshRoutePlanner(MeshReader(field), boundary, transitionProvider: _ => transitions);
            planners[key] = planner;
            return planner;
        }

        double? Distance(int field, int state, int x, int y, int triangle, FieldNavigationTarget target)
        {
            var mesh = meshes[field];
            if (triangle < 0 || triangle >= mesh.Triangles.Count)
            {
                return null;
            }

            var z = FieldCrossFieldApproachResolver.SurfaceZ(mesh.Triangles[triangle], x, y);
            var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, x, y, z, (ushort)triangle, 0);
            var planner = PlannerFor(field, LocksFor(field, state));
            return planner.TryBuildRoute(position, target, out var plan) ? RouteLength(x, y, z, plan) : null;
        }

        var nodes = new List<Node>();
        var index = new Dictionary<string, int>();
        var edges = new List<Edge>();
        var pending = new Queue<Node>();
        Node Add(int field, int x, int y, int triangle, int state, int location)
        {
            var key = location != NoWorldLocation ? $"W{location}" : NodeKey(field, x, y, triangle, state);
            if (index.TryGetValue(key, out var existing))
            {
                return nodes[existing];
            }

            var node = new Node(nodes.Count, field, x, y, triangle, GreatGlacierRegion.IsPassage(field) ? state : 0, location);
            nodes.Add(node);
            index[key] = node.Index;
            pending.Enqueue(node);
            return node;
        }

        foreach (var location in locations.Keys.Where(placements.ContainsKey).Order())
        {
            Add(0, 0, 0, 0, 0, location);
        }

        foreach (var field in new[] { GreatGlacierRegion.CliffBase, GreatGlacierRegion.CabinFrontRoom, GreatGlacierRegion.CabinBackRoom })
        {
            foreach (var gateway in gateways[field].Where(gateway => gateway.Destination is >= 686 and <= 688))
            {
                Add(gateway.Destination, gateway.X, gateway.Y, gateway.Triangle, 0, NoWorldLocation);
            }
        }

        // The lake's two shores after a crossing.
        Add(GreatGlacierRegion.LakeField, GreatGlacierRegion.NorthShoreLanding.X, GreatGlacierRegion.NorthShoreLanding.Y,
            GreatGlacierRegion.NorthShoreLanding.Triangle, 0, NoWorldLocation);
        Add(GreatGlacierRegion.LakeField, GreatGlacierRegion.SouthShoreLanding.X, GreatGlacierRegion.SouthShoreLanding.Y,
            GreatGlacierRegion.SouthShoreLanding.Triangle, 0, NoWorldLocation);

        var crossingStarts = inputs.ObjectDefinitions
            .Where(definition => definition.FieldId == GreatGlacierRegion.LakeField &&
                                 definition.EntityId is GreatGlacierRegion.SouthShoreCrossingEntity or GreatGlacierRegion.NorthShoreCrossingEntity)
            .ToDictionary(definition => definition.EntityId);

        while (pending.TryDequeue(out var node))
        {
            if (node.IsWorld)
            {
                var entry = placements[node.WorldLocation];
                var state = WorldState(inputs.Snowfield, worldPlanner, entry.X, entry.Z);
                if (state is null)
                {
                    continue;
                }

                foreach (var (location, target) in locations.OrderBy(pair => pair.Key))
                {
                    if (!worldPlanner.TryBuildRoute(state.Value, target, out var route))
                    {
                        continue;
                    }

                    var record = (location * 2 - 2) * 12;
                    var arrivalField = BitConverter.ToUInt16(inputs.FieldTable, record + 6);
                    if (!GreatGlacierRegion.IsRegionField(arrivalField))
                    {
                        continue;
                    }

                    var arrival = Add(
                        arrivalField,
                        BitConverter.ToInt16(inputs.FieldTable, record),
                        BitConverter.ToInt16(inputs.FieldTable, record + 2),
                        BitConverter.ToUInt16(inputs.FieldTable, record + 4),
                        corridors.TryGetValue(location, out var corridor) && corridor is { } written ? written : 0,
                        NoWorldLocation);
                    edges.Add(new Edge(node.Index, arrival.Index, EdgeKind.Snowfield, target.StableId, -1, 0, route.TotalDistance));
                }

                continue;
            }

            var field = node.FieldId;
            var onLake = field == GreatGlacierRegion.LakeField;
            foreach (var exit in reads[field].Exits.Where(exit => exit.StableId.StartsWith("script-exit:", StringComparison.Ordinal)))
            {
                if (!TryParseScriptExit(exit.StableId, out _, out var entity) ||
                    GreatGlacierScriptMachine.LineJump(scripts[field], entity, node.State) is not { } jump)
                {
                    continue;
                }

                if (onLake && exit.TriggerLine is { } line &&
                    IsNorthShore((line.StartY + line.EndY) / 2) != IsNorthShore(node.Y))
                {
                    // On the other shore: only the crossing gets there.
                    continue;
                }

                Node? next = jump.Field is >= 60 and <= 64
                    ? placements.ContainsKey(jump.Field) ? Add(0, 0, 0, 0, 0, jump.Field) : null
                    : GreatGlacierRegion.IsRegionField(jump.Field)
                        ? Add(jump.Field, jump.X, jump.Y, jump.Triangle, jump.State, NoWorldLocation)
                        : null;
                if (next is null || Distance(field, node.State, node.X, node.Y, node.Triangle, exit) is not { } length)
                {
                    continue;
                }

                edges.Add(new Edge(node.Index, next.Index, EdgeKind.ScriptExit, exit.StableId, entity, length, 0));
            }

            foreach (var gateway in gatewayLines[field])
            {
                Node? next = gateway.Destination is >= 60 and <= 64
                    ? placements.ContainsKey(gateway.Destination) ? Add(0, 0, 0, 0, 0, gateway.Destination) : null
                    : GreatGlacierRegion.IsRegionField(gateway.Destination)
                        ? Add(gateway.Destination, gateway.X, gateway.Y, gateway.Triangle, 0, NoWorldLocation)
                        : null;
                if (next is null)
                {
                    continue;
                }

                var target = gateway.ToTarget(field);
                if (Distance(field, node.State, node.X, node.Y, node.Triangle, target) is not { } length)
                {
                    continue;
                }

                edges.Add(new Edge(node.Index, next.Index, EdgeKind.Gateway, target.StableId, -1, length, 0));
            }

            if (onLake)
            {
                var north = IsNorthShore(node.Y);
                var startEntity = north ? GreatGlacierRegion.NorthShoreCrossingEntity : GreatGlacierRegion.SouthShoreCrossingEntity;
                var landing = north ? GreatGlacierRegion.SouthShoreLanding : GreatGlacierRegion.NorthShoreLanding;
                if (crossingStarts.TryGetValue(startEntity, out var start) &&
                    Distance(field, 0, node.X, node.Y, node.Triangle, new FieldNavigationTarget(
                        field, FieldNavigationCategory.Objects, "crossing", start.StaticX, start.StaticY, start.StaticZ,
                        $"crossing:{startEntity}", InteractionRadius: 48)) is { } walk)
                {
                    var far = Add(field, landing.X, landing.Y, landing.Triangle, 0, NoWorldLocation);
                    edges.Add(new Edge(node.Index, far.Index, EdgeKind.IceFloeCrossing, $"object:{field}:{startEntity}:", startEntity, walk, 0));
                }
            }
        }

        var graph = new GreatGlacierRegionGraph(
            nodes,
            edges,
            scripts,
            placements,
            corridors,
            gateways,
            lockCache.Where(pair => GreatGlacierRegion.IsPassage(pair.Key.Field))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            TimeSpan.Zero);

        // The walk from each arrival in a goal's field to the goal itself, then every node's
        // best cost to it by Dijkstra over the reversed edges.
        foreach (var goal in GreatGlacierRegion.AllGoalPoints)
        {
            var goalTarget = GoalTarget(goal, scripts[goal.FieldId], inputs.ObjectDefinitions);
            if (goalTarget is null)
            {
                continue;
            }

            var cost = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
            var next = Enumerable.Repeat(-1, nodes.Count).ToArray();
            var queue = new PriorityQueue<int, double>();
            foreach (var node in nodes.Where(node => !node.IsWorld && node.FieldId == goal.FieldId))
            {
                if (goal.RequiredCorridorState is { } required && node.State != required)
                {
                    continue;
                }

                if (Distance(node.FieldId, node.State, node.X, node.Y, node.Triangle, goalTarget.Value) is { } length)
                {
                    cost[node.Index] = GreatGlacierMovementCost.Field(length);
                    queue.Enqueue(node.Index, cost[node.Index]);
                }
            }

            var incoming = edges.Select((edge, edgeIndex) => (edge, edgeIndex)).ToLookup(pair => pair.edge.To);
            var done = new bool[nodes.Count];
            while (queue.TryDequeue(out var current, out var currentCost))
            {
                if (done[current] || currentCost > cost[current])
                {
                    continue;
                }

                done[current] = true;
                foreach (var (edge, edgeIndex) in incoming[current])
                {
                    var candidate = currentCost + edge.Ticks;
                    if (candidate < cost[edge.From])
                    {
                        cost[edge.From] = candidate;
                        next[edge.From] = edgeIndex;
                        queue.Enqueue(edge.From, candidate);
                    }
                }
            }

            graph.costToGoal[goal.Key] = cost;
            graph.nextEdgeToGoal[goal.Key] = next;
        }

        clock.Stop();
        return graph.WithBuildTime(clock.Elapsed);
    }

    private GreatGlacierRegionGraph WithBuildTime(TimeSpan elapsed)
    {
        var copy = new GreatGlacierRegionGraph(Nodes, Edges, scripts, SnowfieldPlacements, SnowfieldCorridors, GatewayArrivals, ModelledLocks, elapsed);
        foreach (var pair in costToGoal) copy.costToGoal[pair.Key] = pair.Value;
        foreach (var pair in nextEdgeToGoal) copy.nextEdgeToGoal[pair.Key] = pair.Value;
        return copy;
    }

    /// <summary>
    /// The native point a goal is walked to: the model's Init placement (SETMODELPOS, opcode
    /// 0xA5) for the treasures and Snow, the spring's own LINE for the hot spring.
    /// </summary>
    public static FieldNavigationTarget? GoalTarget(
        GreatGlacierGoalPoint goal,
        IReadOnlyList<FieldScriptDefinition> scripts,
        IReadOnlyList<FieldNavigationObjectDefinition> definitions)
    {
        if (goal.Stage == GreatGlacierGoalStage.HotSpring)
        {
            var spring = definitions.FirstOrDefault(definition =>
                definition.FieldId == goal.FieldId && definition.EntityId == goal.EntityId);
            return spring.FieldId == 0
                ? null
                : new FieldNavigationTarget(goal.FieldId, FieldNavigationCategory.Objects, "goal", spring.StaticX, spring.StaticY,
                    spring.StaticZ, $"glacier-goal:{goal.Key}", InteractionRadius: 48);
        }

        var init = scripts.FirstOrDefault(script => script.EntityId == goal.EntityId && script.ScriptId == 0);
        var placement = init.Opcodes?.FirstOrDefault(op => op.Opcode == 0xA5);
        if (placement is not { Bytes: { Count: >= 9 } bytes })
        {
            return null;
        }

        var array = bytes.ToArray();
        return new FieldNavigationTarget(goal.FieldId, FieldNavigationCategory.Objects, "goal",
            BitConverter.ToInt16(array, 3), BitConverter.ToInt16(array, 5), BitConverter.ToInt16(array, 7),
            $"glacier-goal:{goal.Key}", InteractionRadius: goal.Stage == GreatGlacierGoalStage.Snow ? 40 : 48);
    }

    public static WorldMapStateSnapshot? WorldState(WorldMapData map, WorldMapRoutePlanner planner, int x, int z)
    {
        var state = new WorldMapStateSnapshot(WorldMapStateReader.WorldModule, GreatGlacierRegion.SnowfieldWorldMapType, 0, 0,
            x, 0, z, 0, 0, 0, 0, 0, 30, 0, new FieldNavigationControlTransform(0));
        if (!planner.TryResolvePlayerTriangle(state, out var triangle))
        {
            return null;
        }

        var resolved = map.Triangles[triangle];
        return state with
        {
            Y = (int)resolved.Centroid.Y,
            TerrainId = resolved.TerrainId,
            RegionId = resolved.RegionId & 0x1F,
            TerrainScriptId = resolved.TerrainScriptId
        };
    }

    internal readonly record struct Gateway(int Index, int Destination, FieldNavigationTriggerLine Line, int X, int Y, int Triangle)
    {
        public FieldNavigationTarget ToTarget(int field) => new(
            field,
            FieldNavigationCategory.Exits,
            "Exit",
            (Line.StartX + Line.EndX) / 2,
            (Line.StartY + Line.EndY) / 2,
            (Line.StartZ + Line.EndZ) / 2,
            $"gateway:{field}:{Index}:{Destination}",
            DestinationFieldIds: [Destination],
            TriggerLine: Line);
    }

    /// <summary>The trigger section's gateways, as FieldCrossFieldApproachResolver reads them.</summary>
    internal static IReadOnlyList<Gateway> ReadGateways(int field, byte[] bytes)
    {
        var gateways = new List<Gateway>();
        const int headerAt = 6 + 7 * 4;
        if (headerAt + 4 > bytes.Length)
        {
            return gateways;
        }

        var section = BitConverter.ToInt32(bytes, headerAt) + 4;
        for (var index = 0; index < 12; index++)
        {
            var at = section + 0x38 + index * 24;
            if (at < 0 || at + 24 > bytes.Length)
            {
                break;
            }

            var destination = BitConverter.ToInt16(bytes, at + 18);
            if (destination < 0 || destination == short.MaxValue || destination == field)
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
