namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldNavigationNativeMovementState(
    int FixedX, int FixedY, int FixedZ, int TriangleId, byte Heading);

public readonly record struct FieldNavigationNativeMovementStepResult(
    bool IsSupported, bool Moved, FieldNavigationNativeMovementState State,
    byte CandidateHeading, int AttemptCount);

public static class FieldNavigationNativeProbeMovement
{
    private const int FixedScale = 4096;
    private const int MaximumMovementSpeed = 2048;

    // Installed ff7_en.exe SHA256 4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225,
    // table00908E30. Every one of the256 native sine/cosine pairs was verified
    // against this quarter-wave reconstruction; no floating-point trig is used
    // in the native step itself.
    private static readonly short[] QuarterSine =
    [
        0, 100, 200, 301, 401, 501, 601, 700, 799, 897, 995, 1092, 1189,
        1284, 1379, 1474, 1567, 1659, 1751, 1841, 1930, 2018, 2105, 2191, 2275, 2358,
        2439, 2519, 2598, 2675, 2750, 2824, 2896, 2966, 3034, 3101, 3166, 3229, 3289,
        3348, 3405, 3460, 3513, 3563, 3612, 3658, 3702, 3744, 3784, 3821, 3856, 3889,
        3919, 3947, 3973, 3996, 4017, 4035, 4051, 4065, 4076, 4084, 4091, 4094, 4096
    ];

    // This is the verified flat-plane subset of00636C41/006367B7, not a
    // replacement for slope, scripted movement or native LINE processing.
    public static bool IsSupportedWalkmesh(FieldWalkmesh walkmesh)
    {
        if (walkmesh is null || walkmesh.Triangles.Count == 0) return false;
        for (var index = 0; index < walkmesh.Triangles.Count; index++)
        {
            var triangle = walkmesh.Triangles[index];
            if (triangle.Index != index || triangle.Vertex0.Z != 0 ||
                triangle.Vertex1.Z != 0 || triangle.Vertex2.Z != 0) return false;
            var a = triangle.Vertex0;
            var b = triangle.Vertex1;
            var c = triangle.Vertex2;
            if ((long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X) >= 0) return false;
            for (var edge = 0; edge < 3; edge++)
                if (triangle.GetAdjacentTriangle(edge) >= walkmesh.Triangles.Count) return false;
        }
        return true;
    }

    public static bool IsClear(FieldWalkmesh walkmesh, int startTriangle,
        FieldNavigationRouteWaypoint start, FieldNavigationRouteWaypoint end,
        double playerRadius, IReadOnlyList<FieldNavigationDynamicObstacle> obstacles,
        Func<int, bool>? isTriangleBlocked = null, byte? requestedHeading = null,
        FieldNavigationFixedPosition? nativeFixedPosition = null)
    {
        if (!double.IsFinite(playerRadius) || playerRadius <= 0 || playerRadius > ushort.MaxValue ||
            playerRadius != Math.Truncate(playerRadius)) return false;
        var dx = (double)end.X - start.X;
        var dy = (double)end.Y - start.Y;
        var distanceSquared = dx * dx + dy * dy;
        // Component rounding of a nominal eight-unit vector can produce(5,7):
        // sqrt74 is the maximum integer-vector length under that rounding.
        // Longer continuation legs remain outside this immediate-input policy.
        if (distanceSquared <= 0 || distanceSquared > 74d ||
            !TryFixed(start.X, out var fixedX) || !TryFixed(start.Y, out var fixedY) ||
            !TryFixed(start.Z, out var fixedZ)) return false;
        if (nativeFixedPosition is { } nativePosition)
        {
            // The coherent navigation read preserves fractions that can change
            // native probe/model collisions. Reject stale metadata rather than
            // silently retrying the same decision from rounded coordinates.
            if ((nativePosition.X >> 12) != start.X || (nativePosition.Y >> 12) != start.Y ||
                (nativePosition.Z >> 12) != start.Z) return false;
            fixedX = nativePosition.X;
            fixedY = nativePosition.Y;
            fixedZ = nativePosition.Z;
        }
        // Preserve the caller's native input/control angle when available.
        // Reconstructing it from rounded XY can select another retry sequence.
        var heading = requestedHeading ?? unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128d / Math.PI,
            MidpointRounding.AwayFromZero));
        var state = new FieldNavigationNativeMovementState(fixedX, fixedY, fixedZ, startTriangle, heading);
        // The scoped house fallback uses the reviewed ordinary walking speed.
        // A steering horizon or several held ticks must not become one larger
        // native movement; a short waypoint does not reduce walking speed.
        return KeepsForwardMovement(1024);

        bool KeepsForwardMovement(ushort candidateSpeed)
        {
            var result = Step(walkmesh, state, heading, candidateSpeed,
                (int)playerRadius, obstacles, isTriangleBlocked);
            return result.IsSupported && result.Moved &&
                ((double)result.State.FixedX - state.FixedX) * dx +
                ((double)result.State.FixedY - state.FixedY) * dy > 0;
        }
    }

    public static FieldNavigationNativeMovementStepResult Step(FieldWalkmesh walkmesh,
        FieldNavigationNativeMovementState state, byte requestedHeading, ushort movementSpeed,
        int playerCollisionRadius, IReadOnlyList<FieldNavigationDynamicObstacle> obstacles,
        Func<int, bool>? isTriangleBlocked = null, bool lineRetryActive = false)
    {
        var unsupported = new FieldNavigationNativeMovementStepResult(false, false, state, requestedHeading, 0);
        if (!IsSupportedWalkmesh(walkmesh) || (uint)state.TriangleId >= walkmesh.Triangles.Count ||
            movementSpeed == 0 || movementSpeed > MaximumMovementSpeed ||
            playerCollisionRadius <= 0 || playerCollisionRadius > ushort.MaxValue ||
            obstacles is null || !AreNativeModelsUsable(obstacles, playerCollisionRadius)) return unsupported;

        var heading = requestedHeading;
        var probeTriangle = state.TriangleId;
        var maximumAttempts = lineRetryActive ? 2 : 16;
        var candidateX = state.FixedX;
        var candidateY = state.FixedY;
        var candidateHeading = heading;
        var attemptCount = 0;
        var firstWall = 0;
        var secondWall = 0;
        var forwardWall = 0;
        var firstModel = false;
        var secondModel = false;
        var forwardModel = false;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            attemptCount++;
            candidateHeading = heading;
            // Flat native slope coefficients have absolute value4096. Signed
            // C# integer division matches the native toward-zero correction.
            candidateX = unchecked(state.FixedX + Sine(heading) * movementSpeed / 256);
            candidateY = unchecked(state.FixedY - Cosine(heading) * movementSpeed / 256);
            if (!Probe(unchecked((byte)(heading + 32)), ref probeTriangle, out firstWall, out firstModel)) return unsupported;
            probeTriangle = state.TriangleId;
            if (!Probe(unchecked((byte)(heading - 32)), ref probeTriangle, out secondWall, out secondModel)) return unsupported;
            probeTriangle = state.TriangleId;
            if (!Probe(heading, ref probeTriangle, out forwardWall, out forwardModel)) return unsupported;
            // The native forward-model flag is recorded only when its wall
            // check is clear; side model checks are retained independently.
            forwardModel &= forwardWall == 0;
            if (firstWall == 0 && secondWall == 0 && forwardWall == 0 &&
                !firstModel && !secondModel && !forwardModel) break;
            // Normal controlled player: any model collision bails immediately.
            // The NPC-only rotation around a forward wall/model is not applied.
            if (firstModel || secondModel || forwardModel || (firstWall != 0 && secondWall != 0)) break;
            if (firstWall == 0 && !firstModel)
            {
                if (secondWall != 0 || secondModel) heading = unchecked((byte)(heading + 8));
            }
            else heading = unchecked((byte)(heading - 8));
        }

        // Native final-center resolution changes the actual triangle reference
        // even on rejection. It never clamps the requested candidate's XY.
        var actualTriangle = state.TriangleId;
        if (!TryWall(walkmesh, ref actualTriangle, candidateX, candidateY,
                Sine(candidateHeading) * playerCollisionRadius, -Cosine(candidateHeading) * playerCollisionRadius,
                isTriangleBlocked, out var centerWall)) return unsupported;
        var moved = firstWall == 0 && secondWall == 0 && forwardWall == 0 && centerWall == 0 &&
                    !firstModel && !secondModel && !forwardModel;
        // Floor0 is the explicit verified-subset assumption. Script/LINE effects
        // and the captured runtime Z−10 provenance remain outside this helper.
        var next = moved
            ? new FieldNavigationNativeMovementState(candidateX, candidateY, 0, actualTriangle, heading)
            : state with { TriangleId = actualTriangle, Heading = heading };
        return new(true, moved, next, candidateHeading, attemptCount);

        bool Probe(byte probeHeading, ref int triangle, out int wall, out bool model)
        {
            var vx = Sine(probeHeading) * playerCollisionRadius;
            var vy = -Cosine(probeHeading) * playerCollisionRadius;
            var x = unchecked(candidateX + vx);
            var y = unchecked(candidateY + vy);
            model = false;
            if (!TryWall(walkmesh, ref triangle, x, y, vx, vy, isTriangleBlocked, out wall)) return false;
            model = HitsModel(x, y, obstacles, playerCollisionRadius);
            return true;
        }
    }

    private static bool TryWall(FieldWalkmesh walkmesh, ref int triangleIndex,
        int fixedX, int fixedY, int vectorX, int vectorY, Func<int, bool>? isTriangleBlocked, out int status)
    {
        status = 0;
        var x = fixedX / FixedScale;
        var y = fixedY / FixedScale;
        // A fixed point cannot legitimately revisit a triangle in this search.
        // Bound malformed adjacency without inventing a walkable fallback.
        for (var visited = 0; visited <= walkmesh.Triangles.Count; visited++)
        {
            if ((uint)triangleIndex >= walkmesh.Triangles.Count) return false;
            var triangle = walkmesh.Triangles[triangleIndex];
            var outsideEdge = -1;
            for (var edge = 0; edge < 3; edge++)
            {
                var (a, b) = triangle.GetEdge(edge);
                var cross = unchecked((b.Y - a.Y) * (x - a.X) - (b.X - a.X) * (y - a.Y));
                if (cross < 0) { outsideEdge = edge; break; }
            }
            if (outsideEdge < 0) return true;
            var adjacent = triangle.GetAdjacentTriangle(outsideEdge);
            if (adjacent < 0 || isTriangleBlocked?.Invoke(adjacent) == true)
            {
                var (a, b) = triangle.GetEdge(outsideEdge);
                status = unchecked((b.X - a.X) * vectorX + (b.Y - a.Y) * vectorY) < 0 ? -8 : 8;
                return true;
            }
            triangleIndex = adjacent;
        }
        return false;
    }

    private static bool AreNativeModelsUsable(IReadOnlyList<FieldNavigationDynamicObstacle> obstacles, int playerRadius)
    {
        foreach (var obstacle in obstacles)
        {
            var width = 2d * obstacle.ClearanceRadius - obstacle.PlayerCollisionRadius;
            if (obstacle.PlayerCollisionRadius != playerRadius || !double.IsFinite(width) ||
                width < 0 || width > ushort.MaxValue || width != Math.Truncate(width) ||
                !TryFixed(obstacle.X, out _) || !TryFixed(obstacle.Y, out _)) return false;
        }
        return true;
    }

    private static bool HitsModel(int fixedX, int fixedY,
        IReadOnlyList<FieldNavigationDynamicObstacle> obstacles, int playerRadius)
    {
        foreach (var obstacle in obstacles)
        {
            if (obstacle.Z <= -127 || obstacle.Z >= 128) continue;
            var dx = unchecked(obstacle.X * FixedScale - fixedX) >> 12;
            var dy = unchecked(obstacle.Y * FixedScale - fixedY) >> 12;
            var width = (int)(2d * obstacle.ClearanceRadius - obstacle.PlayerCollisionRadius);
            var radius = (playerRadius + width) / 2;
            if (unchecked(dx * dx + dy * dy) < unchecked(radius * radius)) return true;
        }
        return false;
    }

    private static bool TryFixed(int coordinate, out int fixedCoordinate)
    {
        var candidate = (long)coordinate * FixedScale;
        fixedCoordinate = (int)candidate;
        return candidate is >= int.MinValue and <= int.MaxValue;
    }

    private static int Sine(byte heading) => heading switch
    {
        <= 64 => QuarterSine[heading],
        <= 128 => QuarterSine[128 - heading],
        <= 192 => -QuarterSine[heading - 128],
        _ => -QuarterSine[256 - heading]
    };

    private static int Cosine(byte heading) => Sine(unchecked((byte)(heading + 64)));
}
