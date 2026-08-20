using Ff7.Accessibility.Reloaded;

/// <summary>
/// A field built as a tower stacks walkable floors over the same X and Y. A
/// scripted trigger authored on an upper floor must anchor to that floor, never
/// to the one underneath it.
///
/// Fort Condor's convil_1 is the case that exposed this. Its ladder trigger
/// <c>ladder:355:12</c> is authored at (1080, 270, 671); no triangle at that
/// height covers those coordinates, and the save room's triangle 11 does - 653
/// units below. The planner therefore believed a ladder led up out of the save
/// room floor, walked the player to the exact spot beneath it, and left them
/// oscillating there: 663 units of route remained and no horizontal move could
/// close any of it. Two of that field's fifteen transitions anchored that way.
/// </summary>
internal static class TowerFieldTransitionAnchoringTests
{
    private const double Tolerance = 192d;

    internal static void Run()
    {
        AnchorsAnUpperFloorTriggerToTheUpperFloor();
        PrefersTheCoveringTriangleWhenItIsOnTheRightFloor();
        RefusesToAnchorWhenNoFloorIsAnywhereNearTheTrigger();
        StillAnchorsTriggersThatSitJustOffTheirPlatformEdge();
    }

    /// <summary>
    /// The convil_1 case: the trigger's X and Y fall inside a ground-floor
    /// triangle, but its Z belongs to the storey above.
    /// </summary>
    private static void AnchorsAnUpperFloorTriggerToTheUpperFloor()
    {
        var walkmesh = new FieldWalkmesh(
        [
            // 0: ground floor, directly beneath the trigger.
            Triangle(0, x: 1080, y: 270, z: 18, size: 400),
            // 1: the upper storey, offset in X and Y but at the trigger's height.
            Triangle(1, x: 1300, y: 270, z: 671, size: 400)
        ]);

        var anchored = FieldWalkmeshPathfinder.ResolveTriangleAtElevation(
            walkmesh, 1080, 270, 671, Tolerance);

        Equal(1, anchored, "a trigger authored at z=671 must not anchor to the floor 653 units below it");
    }

    private static void PrefersTheCoveringTriangleWhenItIsOnTheRightFloor()
    {
        var walkmesh = new FieldWalkmesh(
        [
            Triangle(0, x: 1080, y: 270, z: 18, size: 400),
            Triangle(1, x: 1080, y: 270, z: 671, size: 400)
        ]);

        Equal(
            0,
            FieldWalkmeshPathfinder.ResolveTriangleAtElevation(walkmesh, 1080, 270, 20, Tolerance),
            "a ground-level trigger anchors to the ground floor");
        Equal(
            1,
            FieldWalkmeshPathfinder.ResolveTriangleAtElevation(walkmesh, 1080, 270, 671, Tolerance),
            "an upper-floor trigger anchors to the upper floor");
    }

    /// <summary>
    /// Answering with the wrong storey is worse than not answering: the caller
    /// drops the link, and a ladder the player cannot reach yet simply does not
    /// appear, rather than appearing in the wrong room.
    /// </summary>
    private static void RefusesToAnchorWhenNoFloorIsAnywhereNearTheTrigger()
    {
        var walkmesh = new FieldWalkmesh([Triangle(0, x: 1080, y: 270, z: 18, size: 400)]);

        Equal(
            -1,
            FieldWalkmeshPathfinder.ResolveTriangleAtElevation(walkmesh, 1080, 270, 671, Tolerance),
            "with no triangle on the trigger's storey the anchor must fail rather than pick another");
    }

    /// <summary>
    /// The trigger at the top of a ladder often sits just past the lip of the
    /// platform it serves, so falling outside every triangle in plan view must
    /// still anchor - provided the height matches.
    /// </summary>
    private static void StillAnchorsTriggersThatSitJustOffTheirPlatformEdge()
    {
        var walkmesh = new FieldWalkmesh(
        [
            Triangle(0, x: 1080, y: 270, z: 18, size: 400),
            Triangle(1, x: 1300, y: 270, z: 671, size: 100)
        ]);

        // (1420, 270) is outside triangle 1's footprint but level with it.
        Equal(
            1,
            FieldWalkmeshPathfinder.ResolveTriangleAtElevation(walkmesh, 1420, 270, 671, Tolerance),
            "a trigger just off the platform edge anchors to that platform");
    }

    /// <summary>A triangle centred on (x, y) at height z, in plan view a square corner.</summary>
    private static FieldWalkmeshTriangle Triangle(int index, int x, int y, int z, int size)
    {
        var half = (short)(size / 2);
        return new FieldWalkmeshTriangle(
            index,
            new FieldWalkmeshVertex((short)(x - half), (short)(y - half), (short)z),
            new FieldWalkmeshVertex((short)(x + half), (short)(y - half), (short)z),
            new FieldWalkmeshVertex((short)x, (short)(y + half), (short)z),
            -1,
            -1,
            -1);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
