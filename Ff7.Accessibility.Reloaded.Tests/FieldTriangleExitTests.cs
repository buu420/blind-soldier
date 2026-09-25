using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The two rooms whose only way out is a triangle their Director polls (FieldTriangleExitCatalog),
/// and the beach where the dolphin only answers the whistle on one triangle (the Story row in
/// JunonDolphinReturn.ps1), against the installed scripts and walkmesh. Each row has to still be
/// the native code it was read from, stand on its own triangle, and be somewhere the party can
/// get to. Shared by both test hosts.
/// </summary>
internal static class FieldTriangleExitTests
{
    public static void RunWithInstalledGameData(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("field triangle exits: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        TheTopOfFortCondorIsLeftByItsDeadEndTriangle(catalog, createWalkmeshReader);
        TheRespectableInnIsLeftFromItsLadderLanding(catalog, createWalkmeshReader);
        TheDolphinIsCalledFromTheLastDryTriangleOfTheBeach(new FlevelDataSource(root), catalog, createWalkmeshReader);
        Console.WriteLine("field triangle exit tests passed with installed game data.");
    }

    /// <summary>
    /// convil_4 (358): Init <c>8160020E00</c> sets 6[2] to 14; init's Main reads the leader's own
    /// triangle (<c>B9060504</c> for Cloud) and <c>1666040002000004</c> IFSW 6[4] == 6[2] runs
    /// event script 1 (<c>030441</c>), <c>6064014CFFDBFF050040</c> MAPJUMP 356. The party arrives
    /// from convil_2 on triangle 15 (<c>6066013A0383FD0F00A0</c>), beside 14.
    /// </summary>
    private static void TheTopOfFortCondorIsLeftByItsDeadEndTriangle(
        FieldScriptNavigationCatalog catalog,
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        True(HasOpcode(catalog, 358, 0, 0, "8160020E00") && HasOpcode(catalog, 358, 0, 0, "B9060504") &&
             HasOpcode(catalog, 358, 0, 0, "1666040002000004") && HasOpcode(catalog, 358, 0, 0, "030441"),
            "native convil_4 init polls the leader's triangle against 6[2] = 14 and runs event script 1");
        True(HasOpcode(catalog, 358, 4, 1, "6064014CFFDBFF050040"), "native convil_4 event script 1 is MAPJUMP convil_2");
        var exit = PublishedExit(catalog, 358, "triangle-exit:358:14:356", 14, 356);
        var walkmesh = StandsOnItsTriangle(createWalkmeshReader, exit, 14);

        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(358));
        var arrival = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 358, 0, 826, -637,
            (int)Math.Round(walkmesh.Triangles[15].GetCentroid().Z), 15, 0);
        True(planner.TryBuildRoute(arrival, exit, out _),
            $"the exit is walkable from where the party arrives: {planner.LastDiagnostic}");
    }

    /// <summary>
    /// junpb_2 (378): direct's Main sets 6[17] to 45 (<c>8160112D00</c>), reads party slot 0's
    /// triangle (<c>7566660004060802</c>) and <c>166602001100000B</c> IFSW 6[2] == 6[17] is
    /// <c>606F01AE00C8002C0000</c> MAPJUMP 367. Triangle 45 is a two-triangle landing apart from
    /// the room, reached by the room's published ladder.
    /// </summary>
    private static void TheRespectableInnIsLeftFromItsLadderLanding(
        FieldScriptNavigationCatalog catalog,
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        True(HasOpcode(catalog, 378, 2, 0, "8160112D00") && HasOpcode(catalog, 378, 2, 0, "7566660004060802") &&
             HasOpcode(catalog, 378, 2, 0, "166602001100000B") && HasOpcode(catalog, 378, 2, 0, "606F01AE00C8002C0000"),
            "native junpb_2 direct polls slot 0's triangle against 6[17] = 45 and MAPJUMPs to junmin1");
        var exit = PublishedExit(catalog, 378, "triangle-exit:378:45:367", 45, 367);
        StandsOnItsTriangle(createWalkmeshReader, exit, 45);
        True(catalog.ReadField(378).Transitions.Any(transition =>
                transition.Kind == FieldNavigationTransitionKind.Ladder && transition.TargetTriangle == 45),
            "the room's ladder is published and lands on triangle 45");
    }

    /// <summary>
    /// ujunon2 (429) on any visit after the first: ad (entity 3) has no Init, and its Main is the
    /// whole of the returning dolphin. <c>16200000F0030384</c> IFSW 2[0] &lt; 1008 and
    /// <c>16200000D501047C</c> IFSW 2[0] &gt;= 469 wrap a poll that reads cloud's triangle
    /// (<c>B9060506</c> GETAI into 6[6]; cloud is <c>A100</c> <c>A000</c>, Cloud's own model and
    /// PC), waits for a fresh [SWITCH] (<c>31800072</c> IFKEYON 0x80), blows the whistle anywhere,
    /// and only with <c>1660060019000062</c> 6[6] == 25 asks dialog 32 (<c>48050020020308</c>) and
    /// on Yes (<c>145008020020</c>) MAPJUMPs to field 38 (<c>60260000000000000000</c>). drctr's Main
    /// locks triangles 160 and 159 on every entry from 388 (<c>1620000084010409</c>,
    /// <c>6DA00001</c>, <c>6D9F0001</c>): the water the dolphin comes up in, beside 25.
    /// </summary>
    private static void TheDolphinIsCalledFromTheLastDryTriangleOfTheBeach(
        FlevelDataSource source,
        FieldScriptNavigationCatalog catalog,
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        const int field = 429;
        const int whistleTriangle = 25;
        string[] poll =
        [
            "16200000F0030384", "16200000D501047C", "B9060506", "31800072", "1660060019000062",
            "48050020020308", "145008020020", "60260000000000000000"
        ];
        var main = catalog.ReadScriptOpcodes(field, 3, 0).Select(opcode => Convert.ToHexString(opcode.Bytes.ToArray())).ToList();
        var at = -1;
        foreach (var instruction in poll)
        {
            var next = main.FindIndex(at + 1, hex => hex == instruction);
            True(next > at, $"native ujunon2 ad polls {instruction} after what comes before it");
            at = next;
        }

        True(HasOpcode(catalog, field, 5, 0, "A100") && HasOpcode(catalog, field, 5, 0, "A000"),
            "the entity ad reads the triangle of is Cloud's own model and PC");
        True(HasOpcode(catalog, field, 2, 0, "1620000084010409") && HasOpcode(catalog, field, 2, 0, "6DA00001") &&
             HasOpcode(catalog, field, 2, 0, "6D9F0001"),
            "native drctr locks triangles 160 and 159 once 2[0] >= 388");

        // The row's band and triangle, from the bytes rather than from the row.
        var upper = Convert.FromHexString(poll[0]);
        var lower = Convert.FromHexString(poll[1]);
        var triangleTest = Convert.FromHexString(poll[4]);
        True(upper[6] == 3 && lower[6] == 4 && triangleTest[1] == 0x60 && triangleTest[6] == 0,
            "the native tests are 2[0] < n, 2[0] >= n and 6[6] == n");
        var definitions = FieldStoryEventCatalog.CreateAllFields()
            .Where(definition => definition.FieldId == field && definition.CompletionPlayerTriangles is { Length: > 0 })
            .ToArray();
        True(definitions.Length == 1, "ujunon2 has one Story row finished on a triangle");
        var row = definitions[0];
        True(row.Kind == FieldStoryTargetKind.Location && row.CompletesOnArrival && row.TargetGameMoment < 0 &&
             row.CompletionPlayerTriangles is [var only] && only == BitConverter.ToInt16(triangleTest, 4) &&
             row.MinimumGameMoment == BitConverter.ToInt16(lower, 4) &&
             row.MaximumGameMoment == BitConverter.ToInt16(upper, 4) - 1,
            "the dolphin row is offered for exactly the band ad polls and finishes on the triangle it tests");

        var target = StoryTarget(row, row.MinimumGameMoment);
        True(target.CompletionTriangles is [var completion] && completion == whistleTriangle && target.TriggerLine is null &&
             target.TriggerEntityId < 0 && target.Activation == FieldNavigationActivation.Default,
            "the reader finishes the dolphin on triangle 25 alone, with nothing to walk into or talk to");
        var walkmesh = StandsOnItsTriangle(createWalkmeshReader, target, whistleTriangle);

        // Triangle 25 is the water's edge: the dolphin comes up on 160, one of the two drctr
        // keeps locked (iruka script 6, A80069FE5701, moves it to (-407,343)), and 25 is the
        // triangle beside it.
        var beach = walkmesh.Triangles;
        True(HasOpcode(catalog, field, 13, 6, "A80069FE5701") && Contains(beach[160], -407, 343),
            "native iruka brings the dolphin up at (-407,343), inside locked triangle 160");
        True(Neighbours(beach[whistleTriangle]).Contains(160) && Neighbours(beach[160]).Contains(159),
            "triangle 25 borders 160, and 160 borders 159");

        // The places JunonFieldNavigationTests walks the assistant through are this geometry:
        // (-632,577) is on 20, inside the default 80-unit arrival distance of the row's point,
        // and (-640,400) is on 25 far outside it, so a radius cannot stand in for the triangle.
        True(Contains(beach[20], -632, 577) && !Contains(beach[whistleTriangle], -632, 577) &&
             Math.Sqrt(Math.Pow(-632 - target.X, 2) + Math.Pow(577 - target.Y, 2)) < 80 &&
             Contains(beach[whistleTriangle], -640, 400) &&
             Math.Sqrt(Math.Pow(-640 - target.X, 2) + Math.Pow(400 - target.Y, 2)) > 80 &&
             Contains(beach[26], -598, 759),
            "a radius around the row's point both reaches off triangle 25 and falls short of its far corner");

        // Walked to from where ujunon1's beach gateway lands, with the water locked as it is.
        var arrivals = NativeGatewayArrivals(source, 428, field).ToArray();
        True(arrivals.Length == 1, "ujunon1 has one gateway to the beach");
        var (x, y, arrivalTriangle) = arrivals[0];
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(field), LockedTriangles(field, 159, 160),
            transitionProvider: _ => catalog.ReadField(field).Transitions);
        var arrival = new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, x, y,
            (int)Math.Round(beach[arrivalTriangle].GetCentroid().Z), arrivalTriangle, 0);
        True(planner.TryBuildRoute(arrival, target, out var plan),
            $"the whistle triangle is walkable from the beach gateway with 159 and 160 locked: {planner.LastDiagnostic}");
        True(plan.TargetTriangle == whistleTriangle && plan.TrianglePath.Count > 0 && plan.TrianglePath[^1] == whistleTriangle &&
             !plan.TrianglePath.Any(t => t is 159 or 160),
            $"the route ends on 25 and never enters the water (path {string.Join(",", plan.TrianglePath)})");
        True(Contains(beach[whistleTriangle], plan.FinalApproach.X, plan.FinalApproach.Y),
            "and its last step is inside triangle 25, where the whistle brings the dolphin");
    }

    private static FieldNavigationTarget StoryTarget(FieldStoryEventDefinition row, int gameMoment)
    {
        byte ReadByte(int address) =>
            address == FieldNavigationObjectReader.AddressFieldBankBase ? (byte)gameMoment
            : address == FieldNavigationObjectReader.AddressFieldBankBase + 1 ? (byte)(gameMoment >> 8)
            : (byte)0;
        var targets = new FieldStoryTargetReader(
                address => ReadByte(address) | ReadByte(address + 1) << 8 | ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24,
                address => (short)(ReadByte(address) | ReadByte(address + 1) << 8),
                ReadByte,
                [row])
            .ReadTargets(new FieldPositionSnapshot(FieldPositionReader.FieldModule, row.FieldId, 0, 0, 0, 0, 0, 0));
        True(targets.Count == 1, $"'{row.Label}' is offered at game moment {gameMoment}");
        return targets[0];
    }

    /// <summary>The engine's IDLCK bits as <see cref="FieldBoundaryStateReader"/> finds them, with <paramref name="locked"/> set.</summary>
    private static FieldBoundaryStateReader LockedTriangles(int field, params int[] locked)
    {
        const int fieldGlobalObject = 0x02700000;
        byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressCurrentModule) return (byte)FieldPositionReader.FieldModule;
            if (address == FieldPositionReader.AddressFieldId) return (byte)field;
            if (address == FieldPositionReader.AddressFieldId + 1) return (byte)(field >> 8);
            var bits = address - fieldGlobalObject - FieldBoundaryStateReader.BoundaryBitsOffset;
            return bits is >= 0 and < FieldBoundaryStateReader.BoundaryByteCount
                ? (byte)locked.Where(triangle => triangle >> 3 == bits).Aggregate(0, (value, triangle) => value | 1 << (triangle & 7))
                : (byte)0;
        }

        return new FieldBoundaryStateReader(
            address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr
                ? fieldGlobalObject
                : ReadByte(address) | ReadByte(address + 1) << 8 | ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24,
            ReadByte,
            (_, _) => true);
    }

    /// <summary>Where <paramref name="from"/>'s own gateways to <paramref name="to"/> put the party (its trigger table).</summary>
    private static IEnumerable<(int X, int Y, ushort Triangle)> NativeGatewayArrivals(FlevelDataSource source, int from, int to)
    {
        True(source.TryReadField(from, out var encoded), $"field {from} is in the archive");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
        for (var index = 0; index < 12; index++)
        {
            var gateway = section + 0x38 + index * 24;
            if (BitConverter.ToInt16(bytes, gateway + 18) == to)
            {
                yield return (BitConverter.ToInt16(bytes, gateway + 12), BitConverter.ToInt16(bytes, gateway + 14),
                    BitConverter.ToUInt16(bytes, gateway + 16));
            }
        }
    }

    private static int[] Neighbours(FieldWalkmeshTriangle triangle) =>
        [triangle.Adjacent0, triangle.Adjacent1, triangle.Adjacent2];

    private static bool Contains(FieldWalkmeshTriangle triangle, double x, double y)
    {
        double Cross(FieldWalkmeshVertex a, FieldWalkmeshVertex b) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
        var sides = new[] { Cross(triangle.Vertex0, triangle.Vertex1), Cross(triangle.Vertex1, triangle.Vertex2), Cross(triangle.Vertex2, triangle.Vertex0) };
        return sides.All(side => side >= 0) || sides.All(side => side <= 0);
    }

    private static FieldNavigationTarget PublishedExit(
        FieldScriptNavigationCatalog catalog,
        int field,
        string stableId,
        int triangle,
        int destination)
    {
        var exits = catalog.ReadField(field).Exits.Where(exit => exit.StableId == stableId).ToArray();
        True(exits.Length == 1, $"{stableId} is published");
        var exit = exits[0];
        True(exit.Category == FieldNavigationCategory.Exits && exit.CompletesOnArrival &&
             exit.TriggerEntityId < 0 && exit.TriggerLine is null,
            $"{stableId} is an exit finished by getting there, with no LINE to be enabled");
        True(exit.DestinationFieldIds is [var only] && only == destination, $"{stableId} leads to {destination}");
        True(exit.CompletionTriangles is [var single] && single == triangle, $"{stableId} is finished on triangle {triangle}");
        return exit;
    }

    private static FieldWalkmesh StandsOnItsTriangle(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldNavigationTarget exit,
        int triangle)
    {
        var read = createWalkmeshReader(exit.FieldId).Read(
            new FieldPositionSnapshot(FieldPositionReader.FieldModule, exit.FieldId, 0, exit.X, exit.Y, exit.Z, (ushort)triangle, 0));
        True(read.IsUsable && read.Walkmesh is not null, $"field {exit.FieldId} walkmesh: {read.Diagnostic}");
        var walkmesh = read.Walkmesh!;
        var tri = walkmesh.Triangles[triangle];
        long Cross(FieldWalkmeshVertex a, FieldWalkmeshVertex b) =>
            (long)(b.X - a.X) * (exit.Y - a.Y) - (long)(b.Y - a.Y) * (exit.X - a.X);
        var sides = new[] { Cross(tri.Vertex0, tri.Vertex1), Cross(tri.Vertex1, tri.Vertex2), Cross(tri.Vertex2, tri.Vertex0) };
        True(sides.All(side => side >= 0) || sides.All(side => side <= 0),
            $"{exit.StableId} stands inside triangle {triangle}");
        True(Math.Abs(tri.GetCentroid().Z - exit.Z) <= 1, $"{exit.StableId} is at triangle {triangle}'s height");
        return walkmesh;
    }

    private static bool HasOpcode(FieldScriptNavigationCatalog catalog, int field, int entity, int script, string hex) =>
        catalog.ReadScriptOpcodes(field, entity, script)
            .Any(opcode => Convert.ToHexString(opcode.Bytes.ToArray()).StartsWith(hex, StringComparison.OrdinalIgnoreCase));

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Field triangle exits: " + message);
        }
    }
}
