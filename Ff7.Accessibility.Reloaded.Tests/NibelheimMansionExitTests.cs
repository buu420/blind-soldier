using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Every way out of the Shinra Mansion, walked on the installed walkmesh.
///
/// <para><c>NibelheimStoryTests.EveryMansionRoomOffersAWayBackOut</c> checks the Story
/// catalog, which is authored data. It says nothing about the Exits category, which is
/// read live from each field's own gateway table and then filtered by whether a route to
/// the gateway can actually be planned. A room whose Story row is fine can still offer the
/// player fewer Exits than the game has - which is what the reported session shows.</para>
///
/// <para>The reported 2026-09-22 x64 capture: field 300 reports
/// <c>nativeExits=2, reachableExits=1</c> in most samples and <c>2, 2</c> in a few.
/// Field 297 is <b>not</b> always full either - its latest-day samples include
/// <c>(7,0)</c>, <c>(7,1)</c>, <c>(7,5)</c> and <c>(7,7)</c>. Reachability is a live,
/// position-dependent filter, so per-field maxima say nothing about what the player saw
/// at any given moment, and an earlier version of this comment was wrong to claim every
/// other field reported its full count.</para>
///
/// <para>The 300 case has a known cause and a fix:
/// <c>Steam2026FailClosedFieldRoutePlanner</c> did not implement
/// <see cref="IFieldNavigationNativeBoundaryStatus"/>, so a door held shut by the
/// Director's <c>IDLCK</c> was indistinguishable from a door that is not there and was
/// dropped. See <c>Steam2026MansionSecretDoorExitTests</c>.</para>
///
/// <para>The gateway lines and native entry points below are the installed fields' own,
/// from both archives, which agree.</para>
/// </summary>
internal static class NibelheimMansionExitTests
{
    /// <summary>
    /// Field id, its gateway index, destination, and the gateway's own trigger line -
    /// exactly what <see cref="FieldGatewayTargetReader"/> builds an Exit target from.
    /// </summary>
    private static readonly (int Field, int Index, int Destination, int X1, int Y1, int Z1, int X2, int Y2, int Z2)[] Gateways =
    [
        (297, 0, 282, -66, -16, 0, 66, -20, 0),
        (297, 1, 298, -57, 803, -13, 56, 800, -12),
        (297, 2, 298, -436, 376, 0, -416, 228, 0),
        (297, 3, 298, 456, 372, 0, 428, 228, 0),
        (297, 4, 299, -472, 961, 311, -424, 738, 311),
        (297, 5, 300, 468, 963, 311, 427, 747, 311),
        (298, 0, 297, -58, 667, 0, 58, 667, 0),
        (298, 1, 297, -338, 262, 0, -331, 147, 0),
        (298, 2, 297, 344, 262, 0, 344, 146, 0),
        (299, 0, 297, -302, 822, 277, -307, 684, 277),
        (300, 0, 297, 315, 826, 277, 318, 665, 277),
        (300, 1, 301, 911, 698, 339, 984, 633, 339),
        (301, 0, 300, 149, -162, 717, 281, -109, 720),
        (301, 1, 302, -157, -130, -610, 164, -120, -610),
        (302, 0, 301, -32, 877, 226, 56, 877, 226),
        (302, 1, 303, -240, -572, 0, 211, -468, 5),
        (303, 0, 302, 246, -269, 0, -246, -312, 0),
        (303, 1, 302, -311, -648, 0, -246, -312, 0),
        (303, 2, 304, -182, -1114, 0, -281, -1093, 0),
        (304, 0, 307, -117, 92, 0, 151, 84, 0),
        (304, 1, 303, -474, -127, 0, -433, -50, 0),
        (305, 0, 308, -117, 92, 0, 151, 84, 0),
        (305, 1, 303, -474, -127, 0, -433, -50, 0),
        (306, 0, 308, -117, 92, 0, 151, 84, 0),
        (306, 1, 303, -474, -127, 0, -433, -50, 0),
        (307, 0, 309, -138, 3297, 0, 587, 3213, 0),
        (307, 1, 304, 116, 38, 0, -425, 420, 0),
        (307, 2, 304, 116, 38, 0, 682, -17, 0),
        (307, 3, 304, 767, 176, 0, 682, -17, 0),
        (308, 0, 310, -138, 3297, 0, 587, 3213, 0),
        (308, 1, 305, 116, 38, 0, -425, 420, 0),
        (308, 2, 305, 116, 38, 0, 682, -17, 0),
        (308, 3, 305, 767, 176, 0, 682, -17, 0),
        (309, 0, 307, -33, 818, 0, 142, 790, 0),
        (310, 0, 308, -33, 818, 0, 142, 790, 0)
    ];

    /// <summary>
    /// Where the game actually puts the party when it arrives in each field. Shared with
    /// <c>NibelheimStoryTests</c>'s own replay table and read from the same archives.
    /// </summary>
    private static readonly (int Field, (int X, int Y, int Triangle)[] Entries)[] NativeEntries =
    [
        (297, [(-367, 879, 51), (-327, 313, 116), (2, 36, 119), (4, 733, 90),
               (344, 305, 73), (393, 883, 61)]),
        (298, [(-378, 203, 116), (6, 739, 127), (410, 199, 124)]),
        (299, [(-345, 777, 72)]),
        (300, [(354, 788, 151), (909, 646, 143)]),
        (301, [(7, 70, 37), (225, -99, 202)]),
        (302, [(-52, -460, 29), (-50, -278, 24), (-13, 670, 4)]),
        (303, [(-225, -1043, 23), (-55, -411, 12)]),
        (304, [(-425, -125, 137), (53, 13, 113)]),
        (305, [(53, 13, 39), (-362, -145, 58)]),
        (307, [(292, 2884, 8), (307, 133, 11)]),
        (308, [(292, 2884, 8), (307, 133, 11)]),
        (309, [(77, 907, 43)]),
        (310, [(77, 907, 43)])
    ];

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")))
        {
            Console.WriteLine("nibelheim mansion exits: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        EveryMansionScriptExitCarriesALabelAndADestination();
        EveryMansionGatewayIsWalkableFromEveryNativeEntry(createWalkmeshReader);
        NoStandingPositionSeesOnlySomeOfARoomsDoors(createWalkmeshReader);
    }

    /// <summary>
    /// The half of the mansion's Exits that is static: the MAPJUMPs the script scan finds.
    /// An exit with no label is one a player cannot choose, and one with no destination
    /// cannot be routed or completed.
    /// </summary>
    private static void EveryMansionScriptExitCarriesALabelAndADestination()
    {
        var catalog = Scripts();
        var faults = new List<string>();
        var seen = 0;

        for (var field = 297; field <= 310; field++)
        {
            foreach (var exit in FieldScriptExitBranchPolicy.Resolve(
                         field, gameMoment: 0, catalog.ReadField(field).Exits))
            {
                seen++;
                if (string.IsNullOrWhiteSpace(exit.Label))
                {
                    faults.Add($"{field} {exit.StableId}: no label");
                }

                if (exit.DestinationFieldIds is not { Count: > 0 })
                {
                    faults.Add($"{field} {exit.StableId}: no destination");
                }
            }
        }

        if (faults.Count != 0)
        {
            throw new InvalidOperationException(
                $"{faults.Count} mansion script exits are unusable: " + string.Join("; ", faults));
        }

        Equal(true, seen > 0, "the mansion must have script exits to check");
        Console.WriteLine($"nibelheim mansion: {seen} script exits carry a label and a destination");
    }

    /// <summary>
    /// From every point the game can put the party down in a mansion room, every one of
    /// that room's native gateways has to be walkable - otherwise the Exits list silently
    /// loses a door and the player is told the room has fewer ways out than it has.
    /// </summary>
    private static void EveryMansionGatewayIsWalkableFromEveryNativeEntry(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var unreachable = new List<string>();
        var routed = 0;

        foreach (var (field, entries) in NativeEntries)
        {
            var gateways = Gateways.Where(gateway => gateway.Field == field).ToArray();
            if (gateways.Length == 0)
            {
                continue;
            }

            var reader = createWalkmeshReader(field);
            var mesh = reader.Read(At(field, 0, 0, 0)).Walkmesh;
            Equal(true, mesh is not null, $"field {field}: the installed walkmesh must be readable");
            var planner = new FieldWalkmeshRoutePlanner(reader);

            foreach (var (entryX, entryY, entryTriangle) in entries)
            {
                var start = At(
                    field,
                    entryX,
                    entryY,
                    FloorHeight(mesh!, entryTriangle, entryX, entryY),
                    entryTriangle);

                foreach (var gateway in gateways)
                {
                    if (planner.TryBuildRoute(start, ToTarget(gateway), out _))
                    {
                        routed++;
                        continue;
                    }

                    unreachable.Add(
                        $"field {field} entry ({entryX},{entryY}) t{entryTriangle} -> " +
                        $"gateway {gateway.Index} to {gateway.Destination}: {planner.LastDiagnostic}");
                }
            }
        }

        if (unreachable.Count != 0)
        {
            throw new InvalidOperationException(
                $"{unreachable.Count} mansion gateways are not walkable from a native entry: " +
                string.Join(" | ", unreachable));
        }

        Console.WriteLine($"nibelheim mansion: {routed} gateway routes from native entries");
    }

    /// <summary>
    /// The shape of the reported complaint, as an invariant: no place a player can stand
    /// may see only some of a room's doors.
    ///
    /// <para>The 2026-09-22 x64 capture has field 300 publishing two exits
    /// (<c>publication=stable (2 exits)</c>) while the reachability filter passes only
    /// one, in 256 of 268 samples and at every game moment the player was there. This
    /// walks every triangle of each upper room's own mesh and asserts that a position
    /// which can route to one gateway can route to all of them - a partial list is the
    /// defect, whereas a position that can reach none is an isolated piece of mesh and
    /// not somewhere the player stands.</para>
    /// </summary>
    private static void NoStandingPositionSeesOnlySomeOfARoomsDoors(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var partial = new List<string>();
        var probed = 0;

        foreach (var field in new[] { 297, 298, 299, 300, 301 })
        {
            var gateways = Gateways.Where(gateway => gateway.Field == field).ToArray();
            var reader = createWalkmeshReader(field);
            var mesh = reader.Read(At(field, 0, 0, 0)).Walkmesh;
            Equal(true, mesh is not null, $"field {field}: the installed walkmesh must be readable");
            var planner = new FieldWalkmeshRoutePlanner(reader);

            for (var triangle = 0; triangle < mesh!.Triangles.Count; triangle++)
            {
                var centroid = mesh.Triangles[triangle].GetCentroid();
                var start = At(
                    field,
                    (int)Math.Round(centroid.X),
                    (int)Math.Round(centroid.Y),
                    (int)Math.Round(centroid.Z),
                    triangle);

                var reached = gateways
                    .Where(gateway => planner.TryBuildRoute(start, ToTarget(gateway), out _))
                    .Select(gateway => gateway.Destination)
                    .ToArray();
                probed++;

                // Nowhere at all is an isolated island of mesh, not a partial list.
                if (reached.Length != 0 && reached.Length != gateways.Length)
                {
                    partial.Add(
                        $"field {field} t{triangle} ({start.X},{start.Y}) reaches " +
                        $"{string.Join("/", reached)} of {gateways.Length} doors");
                }
            }
        }

        if (partial.Count != 0)
        {
            throw new InvalidOperationException(
                $"{partial.Count} standing positions see only some of their room's doors: " +
                string.Join(" | ", partial.Take(20)));
        }

        Console.WriteLine($"nibelheim mansion: {probed} standing positions see every door of their room");
    }

    private static FieldNavigationTarget ToTarget(
        (int Field, int Index, int Destination, int X1, int Y1, int Z1, int X2, int Y2, int Z2) gateway) =>
        new(
            gateway.Field,
            FieldNavigationCategory.Exits,
            "Exit",
            (gateway.X1 + gateway.X2) / 2,
            (gateway.Y1 + gateway.Y2) / 2,
            (gateway.Z1 + gateway.Z2) / 2,
            $"gateway:{gateway.Field}:{gateway.Index}:{gateway.Destination}",
            CompletesOnArrival: true,
            DestinationFieldIds: [gateway.Destination],
            TriggerLine: new FieldNavigationTriggerLine(
                gateway.X1, gateway.Y1, gateway.Z1, gateway.X2, gateway.Y2, gateway.Z2));

    private static FieldPositionSnapshot At(int field, int x, int y, int z, int triangle = 0) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, (ushort)triangle, 0);

    /// <summary>The same barycentric floor height NibelheimStoryTests replays with.</summary>
    private static int FloorHeight(FieldWalkmesh mesh, int triangleIndex, int x, int y)
    {
        var triangle = mesh.Triangles[triangleIndex];
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (((double)b.Y - c.Y) * ((double)a.X - c.X)) +
                          (((double)c.X - b.X) * ((double)a.Y - c.Y));
        if (Math.Abs(denominator) < 1e-9d)
        {
            return (int)Math.Round(triangle.GetCentroid().Z);
        }

        var weightA = ((((double)b.Y - c.Y) * (x - c.X)) + (((double)c.X - b.X) * (y - c.Y))) / denominator;
        var weightB = ((((double)c.Y - a.Y) * (x - c.X)) + (((double)a.X - c.X) * (y - c.Y))) / denominator;
        return (int)Math.Round((weightA * a.Z) + (weightB * b.Z) + ((1d - weightA - weightB) * c.Z));
    }

    private static FieldScriptNavigationCatalog Scripts() =>
        new(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is not set."));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
