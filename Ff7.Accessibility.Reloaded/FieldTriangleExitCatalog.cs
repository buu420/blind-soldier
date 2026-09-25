namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Rooms whose way out is a triangle a Director's Main polls rather than a gateway or a LINE,
/// reviewed one by one against the installed scripts and walkmesh. The whole-game audit found
/// 63 looping position and key polls that MAPJUMP; most are ladder ends, crawls, scenes, a map
/// view or the desert's random wander (reports/claude-field-repair.md). These two are ordinary
/// exits, each the only one its room has, so without them the exit list of that room is empty.
/// </summary>
public static class FieldTriangleExitCatalog
{
    private static readonly IReadOnlyList<FieldNavigationTarget> Exits =
    [
        // convil_4, the top of Fort Condor. Init sets 6[2] to 14 (8160020E00); init's Main reads
        // whoever leads - GETAI of Cloud's, Tifa's or Cid's own entity (B9060504, B9060804,
        // B9060A04) after IFPRTYQ - and IFSW 6[4] == 6[2] (1666040002000004) runs event script 1,
        // MAPJUMP convil_2 (6064014CFFDBFF050040). Triangle 14 is the dead end beside the arrival
        // triangle 15 from convil_2.
        Exit(358, 14, 356, 855, -649, 2281),

        // junpb_2, the Respectable Inn. direct's Main sets 6[17] to 45 (8160112D00), reads party
        // slot 0's triangle (PXYZI 7566660004060802; AXYZI of cl_hei in the game moment 406
        // scene) and IFSW 6[2] == 6[17] (166602001100000B) is MAPJUMP junmin1
        // (606F01AE00C8002C0000). Triangle 45 is the landing the room's ladder climbs to
        // (ladder:378:4:*:3:45), where the party also arrives from junmin1.
        Exit(378, 45, 367, -206, 300, 381)
    ];

    public static IReadOnlyList<FieldNavigationTarget> ForField(int fieldId) =>
        Exits.Where(exit => exit.FieldId == fieldId).ToArray();

    private static FieldNavigationTarget Exit(int fieldId, int triangle, int destination, int x, int y, int z) =>
        new(fieldId, FieldNavigationCategory.Exits, "Scripted exit", x, y, z,
            $"triangle-exit:{fieldId}:{triangle}:{destination}",
            CompletesOnArrival: true, DestinationFieldIds: [destination],
            CompletionTriangles: [triangle]);
}
