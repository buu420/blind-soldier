using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Field 300's secret panel, as the game actually holds it shut, shared by the legacy and
/// Steam 2026 sides of the same comparison.
///
/// <para>sinin2_2's Director Init runs <c>IDLCK</c> on triangles 143, 67 and 120, and
/// entity 7's Talk clears all three. Triangle 143 is where the party arrives from the
/// spiral staircase, and gateway 1 - the one back to 301 - sits on it. So with the panel
/// shut there is genuinely no route to that door from the rest of the room, and the
/// correct behaviour is to keep offering it and say it is shut, never to drop it.</para>
/// </summary>
internal static class MansionSecretDoorExitFixture
{
    internal const int Field = 300;

    /// <summary>The triangles sinin2_2's Director Init locks.</summary>
    internal static readonly int[] LockedTriangles = [143, 67, 120];

    /// <summary>The hall-side arrival, on the open part of the floor.</summary>
    internal const int EntryX = 354;
    internal const int EntryY = 788;
    internal const int EntryTriangle = 151;

    /// <summary>Gateway 0, back to the first floor: always open.</summary>
    internal static FieldNavigationTarget ToHall() =>
        Gateway(0, 297, 315, 826, 277, 318, 665, 277);

    /// <summary>Gateway 1, to the spiral staircase, behind the locked panel.</summary>
    internal static FieldNavigationTarget ToStaircase() =>
        Gateway(1, 301, 911, 698, 339, 984, 633, 339);

    internal static IReadOnlyList<FieldNavigationTarget> BothDoors() =>
        [ToHall(), ToStaircase()];

    internal static FieldPositionSnapshot Entry(FieldWalkmesh mesh) =>
        new(
            FieldPositionReader.FieldModule,
            Field,
            0,
            EntryX,
            EntryY,
            FloorHeight(mesh, EntryTriangle, EntryX, EntryY),
            EntryTriangle,
            0);

    /// <summary>
    /// A boundary reader that reports exactly the locks the Director sets, through the
    /// production reader rather than a stand-in for it.
    /// </summary>
    internal static FieldBoundaryStateReader LockedPanel()
    {
        const int fieldState = 0x00200000;
        var bits = new byte[FieldBoundaryStateReader.BoundaryByteCount];
        foreach (var triangle in LockedTriangles)
        {
            bits[triangle >> 3] |= (byte)(1 << (triangle & 7));
        }

        return new FieldBoundaryStateReader(
            readInt32: address =>
                address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? fieldState : 0,
            readByte: address =>
            {
                // The reader proves the frame belongs to the field being asked about
                // before it trusts the bits, so the module and field id have to be here
                // too - the same check the live reader makes.
                if (address == FieldPositionReader.AddressCurrentModule)
                {
                    return (byte)FieldPositionReader.FieldModule;
                }

                if (address == FieldPositionReader.AddressFieldId)
                {
                    return (byte)(Field & 0xFF);
                }

                if (address == FieldPositionReader.AddressFieldId + 1)
                {
                    return (byte)((Field >> 8) & 0xFF);
                }

                var offset = address - (fieldState + FieldBoundaryStateReader.BoundaryBitsOffset);
                return offset >= 0 && offset < bits.Length ? bits[offset] : (byte)0;
            },
            isReadableMemory: (_, _) => true);
    }

    private static FieldNavigationTarget Gateway(
        int index, int destination, int x1, int y1, int z1, int x2, int y2, int z2) =>
        new(
            Field,
            FieldNavigationCategory.Exits,
            destination == 297 ? "Exit to the first floor" : "Exit to the staircase",
            (x1 + x2) / 2,
            (y1 + y2) / 2,
            (z1 + z2) / 2,
            $"gateway:{Field}:{index}:{destination}",
            CompletesOnArrival: true,
            DestinationFieldIds: [destination],
            TriggerLine: new FieldNavigationTriggerLine(x1, y1, z1, x2, y2, z2));

    internal static int FloorHeight(FieldWalkmesh mesh, int triangleIndex, int x, int y)
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
}
