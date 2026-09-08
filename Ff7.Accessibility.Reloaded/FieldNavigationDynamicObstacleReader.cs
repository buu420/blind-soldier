namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldNavigationDynamicObstacle(
    int ModelIndex,
    int X,
    int Y,
    int Z,
    double ClearanceRadius,
    double PlayerCollisionRadius = 0d);

public static class FieldNavigationDynamicObstacleGeometry
{
    public const int NativeMaximumVerticalSeparation = 127;

    public static bool IntersectsAny(
        FieldNavigationRouteWaypoint start,
        FieldNavigationRouteWaypoint end,
        IReadOnlyList<FieldNavigationDynamicObstacle>? obstacles)
    {
        if (obstacles is null || obstacles.Count == 0)
        {
            return false;
        }

        foreach (var obstacle in obstacles)
        {
            if (Intersects(start, end, obstacle))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Intersects(
        FieldNavigationRouteWaypoint start,
        FieldNavigationRouteWaypoint end,
        FieldNavigationDynamicObstacle obstacle)
    {
        if (!double.IsFinite(obstacle.ClearanceRadius) ||
            obstacle.ClearanceRadius <= 0d)
        {
            return false;
        }

        var segmentX = end.X - (double)start.X;
        var segmentY = end.Y - (double)start.Y;
        var segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;
        if (obstacle.PlayerCollisionRadius > 0d &&
            double.IsFinite(obstacle.PlayerCollisionRadius) &&
            segmentLengthSquared > 0.000001d)
        {
            // FUN_00636c41 checks the proposed movement at three points a
            // player radius ahead: the heading and its +/-45-degree flanks.
            // FUN_00637724 applies the half-sum clearance at each point, not
            // at Cloud's center. Omitting these probes cuts through models.
            var forwardScale = obstacle.PlayerCollisionRadius / Math.Sqrt(segmentLengthSquared);
            var forwardX = segmentX * forwardScale;
            var forwardY = segmentY * forwardScale;
            var diagonalScale = 1d / Math.Sqrt(2d);
            return IntersectsCylinder(start, end, obstacle, forwardX, forwardY) ||
                   IntersectsCylinder(start, end, obstacle,
                       (forwardX - forwardY) * diagonalScale,
                       (forwardY + forwardX) * diagonalScale) ||
                   IntersectsCylinder(start, end, obstacle,
                       (forwardX + forwardY) * diagonalScale,
                       (forwardY - forwardX) * diagonalScale);
        }

        return IntersectsCylinder(start, end, obstacle, 0d, 0d);
    }

    private static bool IntersectsCylinder(
        FieldNavigationRouteWaypoint start,
        FieldNavigationRouteWaypoint end,
        FieldNavigationDynamicObstacle obstacle,
        double offsetX,
        double offsetY)
    {
        var startX = start.X + offsetX;
        var startY = start.Y + offsetY;
        var endX = end.X + offsetX;
        var endY = end.Y + offsetY;
        var segmentX = endX - startX;
        var segmentY = endY - startY;
        var segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;
        var amount = segmentLengthSquared <= 0.000001d
            ? 0d
            : Math.Clamp(
                ((obstacle.X - startX) * segmentX +
                 (obstacle.Y - startY) * segmentY) /
                segmentLengthSquared,
                0d,
                1d);
        var closestX = startX + segmentX * amount;
        var closestY = startY + segmentY * amount;
        var closestZ = start.Z + (end.Z - start.Z) * amount;
        if (Math.Abs(obstacle.Z - closestZ) > NativeMaximumVerticalSeparation)
        {
            return false;
        }

        var distanceX = obstacle.X - closestX;
        var distanceY = obstacle.Y - closestY;
        var clearanceSquared = obstacle.ClearanceRadius * obstacle.ClearanceRadius;
        var closestDistanceSquared = distanceX * distanceX + distanceY * distanceY;
        if (closestDistanceSquared >= clearanceSquared)
        {
            return false;
        }

        // A forward probe can begin inside a model even though Cloud's center
        // is outside it. The native routine rejects that first movement step;
        // a distant endpoint outside the cylinder cannot authorize the path.
        if (offsetX != 0d || offsetY != 0d)
        {
            return true;
        }

        // A torn or boundary-rounded sample can put Cloud fractionally inside a
        // cylinder he is already leaving. Do not turn the escape direction into
        // another blockage; the native game permits the models to separate.
        var startDistanceSquared =
            Math.Pow(obstacle.X - startX, 2) +
            Math.Pow(obstacle.Y - startY, 2);
        var endDistanceSquared =
            Math.Pow(obstacle.X - endX, 2) +
            Math.Pow(obstacle.Y - endY, 2);
        return startDistanceSquared >= clearanceSquared ||
               endDistanceSquared <= startDistanceSquared + 0.000001d;
    }
}

/// <summary>
/// Reads the field-model collision cylinders used by the original PC movement
/// routine. The native check excludes the player and collision-disabled models,
/// accepts a vertical delta of at most 127 units, and compares planar distance
/// against half the sum of the two model collision widths at the player's
/// forward and +/-45-degree movement probes.
/// </summary>
public sealed class FieldNavigationDynamicObstacleReader
{
    public const int CollisionDisabledOffset = 0x5F;

    private static readonly IReadOnlyList<FieldNavigationDynamicObstacle> Empty =
        Array.Empty<FieldNavigationDynamicObstacle>();

    private readonly Func<int, int> readInt32;
    private readonly Func<int, short> readInt16;
    private readonly Func<int, byte> readByte;

    public FieldNavigationDynamicObstacleReader(
        Func<int, int> readInt32,
        Func<int, short> readInt16,
        Func<int, byte> readByte)
    {
        this.readInt32 = readInt32;
        this.readInt16 = readInt16;
        this.readByte = readByte;
    }

    public IReadOnlyList<FieldNavigationDynamicObstacle> Read(
        FieldPositionSnapshot position,
        FieldNavigationTarget? target)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return Empty;
        }

        var eventTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
        if (eventTable == 0 ||
            modelCount == 0 ||
            position.ModelIndex < 0 ||
            position.ModelIndex >= modelCount)
        {
            return Empty;
        }

        var excludedTargetModel = ResolveTargetModel(target, modelCount);
        var playerEvent =
            eventTable +
            position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
        var playerCollisionWidth = Math.Max(
            0,
            (int)readInt16(playerEvent + FieldNavigationNpcReader.CollisionRadiusOffset));
        var obstacles = new List<FieldNavigationDynamicObstacle>(modelCount - 1);
        for (var modelIndex = 0; modelIndex < modelCount; modelIndex++)
        {
            if (modelIndex == position.ModelIndex || modelIndex == excludedTargetModel)
            {
                continue;
            }

            var eventAddress =
                eventTable +
                modelIndex * FieldNavigationObjectReader.FieldEventDataStride;
            if (readByte(eventAddress + CollisionDisabledOffset) != 0)
            {
                continue;
            }

            var x = FromModelFixedPoint(
                readInt32(eventAddress + FieldNavigationObjectReader.PositionXOffset));
            var y = FromModelFixedPoint(
                readInt32(eventAddress + FieldNavigationObjectReader.PositionYOffset));
            var z = FromModelFixedPoint(
                readInt32(eventAddress + FieldNavigationObjectReader.PositionZOffset));
            if (Math.Abs(z - position.Z) >
                FieldNavigationDynamicObstacleGeometry.NativeMaximumVerticalSeparation)
            {
                continue;
            }

            var modelCollisionWidth = Math.Max(
                0,
                (int)readInt16(eventAddress + FieldNavigationNpcReader.CollisionRadiusOffset));
            var clearance = (playerCollisionWidth + modelCollisionWidth) / 2d;
            if (clearance <= 0d)
            {
                continue;
            }

            obstacles.Add(new FieldNavigationDynamicObstacle(
                modelIndex,
                x,
                y,
                z,
                clearance,
                PlayerCollisionRadius: playerCollisionWidth));
        }

        return obstacles.Count == 0 ? Empty : obstacles;
    }

    private int ResolveTargetModel(
        FieldNavigationTarget? target,
        int modelCount)
    {
        if (target?.TriggerEntityId is not int entityId || entityId < 0)
        {
            return -1;
        }

        var modelId = readByte(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId);
        return modelId < modelCount ? modelId : -1;
    }

    private static int FromModelFixedPoint(int value) =>
        value / FieldNavigationObjectReader.ModelPositionFixedPointScale;
}
