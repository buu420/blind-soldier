using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The native world-map collision between the party and the vehicles parked on the map.
///
/// <para>Ghidra: movement 0074EA48 calls 0076296E, which calls 00762993 and 00762A21. The
/// test reads the model from entity+0x50 and the flags from entity+0x51, skips anything
/// flagged 0x80, skips models 21, 22, 23 and everything from 26 up, and compares two 8x8
/// bit masks laid over 256-unit cells centred on a 1024-unit offset. A move is refused when
/// either participant's mask covers the other. The mask bytes live at 0096DDB0 + 8 * model.
/// </para>
///
/// <para>Only the models this repair was verified against are described here: the party on
/// foot, and the Buggy they park at the Cosmo Canyon entrance. An unlisted model is not
/// guessed at - it simply does not block, which is what the mod did before.</para>
/// </summary>
public static class WorldMapVehicleObstacles
{
    /// <summary>Entity flag 0x51 bit 0x80: the entity is skipped by native collision.</summary>
    public const byte SkippedFlag = 0x80;

    private const int CellSize = 256;
    private const int MaskCentreOffset = 1024;
    private const int MaskCells = 8;

    private static readonly IReadOnlyDictionary<int, byte[]> NativeMasks =
        new Dictionary<int, byte[]>
        {
            // Cloud, Tifa and Cid on foot: 00 00 00 18 18 00 00 00.
            [0] = [0x00, 0x00, 0x00, 0x18, 0x18, 0x00, 0x00, 0x00],
            [1] = [0x00, 0x00, 0x00, 0x18, 0x18, 0x00, 0x00, 0x00],
            [2] = [0x00, 0x00, 0x00, 0x18, 0x18, 0x00, 0x00, 0x00],

            // Tiny Bronco: 00 18 3c 7e 7e 3c 18 00. Read out of the installed
            // ff7_en.exe at 0096DDB0 + 5 * 8, the same way the Buggy's row was. A wider
            // diamond than the Buggy, which is why the boat can be touched from a shore
            // the party cannot walk onto the water from.
            [5] = [0x00, 0x18, 0x3c, 0x7e, 0x7e, 0x3c, 0x18, 0x00],

            // Buggy: 00 00 18 3c 3c 18 00 00.
            [6] = [0x00, 0x00, 0x18, 0x3c, 0x3c, 0x18, 0x00, 0x00]
        };

    /// <summary>The 8x8 mask the native routine reads for this model, when it has one.</summary>
    public static bool TryGetNativeMask(int modelId, out IReadOnlyList<byte> mask)
    {
        if (NativeMasks.TryGetValue(modelId, out var bytes))
        {
            mask = bytes;
            return true;
        }

        mask = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// Where a party of <paramref name="selfModelId"/> can stand and be in contact with
    /// <paramref name="otherModelId"/>, as the lower corner of each 256-unit cell measured
    /// <em>from the other entity outward</em>.
    ///
    /// <para>The native grid is indexed by <c>other - self</c>, so a cell at native column
    /// <c>c</c> is the party sitting at <c>-(c * 256 - 1024)</c> and below, which is the
    /// mirrored index. Every mask read so far happens to be centrally symmetric, so the
    /// two orderings cover the same ground; the mirror is applied anyway rather than
    /// resting on that.</para>
    /// </summary>
    public static IReadOnlyList<(int MinX, int MinZ)> ContactCells(int selfModelId, int otherModelId)
    {
        if (!NativeMasks.TryGetValue(selfModelId, out var self) ||
            !NativeMasks.TryGetValue(otherModelId, out var other))
        {
            return Array.Empty<(int, int)>();
        }

        var cells = new List<(int MinX, int MinZ)>();
        for (var row = 0; row < MaskCells; row++)
        {
            for (var column = 0; column < MaskCells; column++)
            {
                if (((self[row] >> column) & 1) != 0 ||
                    ((other[MaskCells - 1 - row] >> (MaskCells - 1 - column)) & 1) != 0)
                {
                    cells.Add((
                        (MaskCells - 1 - column) * CellSize - MaskCentreOffset,
                        (MaskCells - 1 - row) * CellSize - MaskCentreOffset));
                }
            }
        }

        return cells;
    }

    /// <summary>The width and height of one native mask cell, and the reach of the grid.</summary>
    public const int NativeCellSize = CellSize;

    public const int NativeReach = MaskCentreOffset;

    public static bool HasNativeMask(int modelId) => NativeMasks.ContainsKey(modelId);

    /// <summary>
    /// Whether the native test would refuse a party of <paramref name="selfModelId"/> standing
    /// at the given point because <paramref name="otherModelId"/> occupies its own.
    /// </summary>
    public static bool Blocks(
        int selfModelId,
        int selfX,
        int selfZ,
        int otherModelId,
        int otherX,
        int otherZ,
        int wrapWidth,
        int wrapHeight)
    {
        if (!NativeMasks.TryGetValue(selfModelId, out var self) ||
            !NativeMasks.TryGetValue(otherModelId, out var other))
        {
            return false;
        }

        var deltaX = WorldMapTargetCatalog.WrappedDelta(selfX, otherX, wrapWidth);
        var deltaZ = WorldMapTargetCatalog.WrappedDelta(selfZ, otherZ, wrapHeight);
        if (Math.Abs(deltaX) >= MaskCentreOffset || Math.Abs(deltaZ) >= MaskCentreOffset) return false;
        var column = (deltaX + MaskCentreOffset) >> 8;
        var row = (deltaZ + MaskCentreOffset) >> 8;
        if (column is < 0 or >= MaskCells || row is < 0 or >= MaskCells)
        {
            return false;
        }

        // Either participant's mask covering the other refuses the move. The second test
        // reads the other model's mask from its own point of view, which is the same grid
        // mirrored on both axes.
        return ((self[row] >> column) & 1) != 0 ||
               ((other[MaskCells - 1 - row] >> (MaskCells - 1 - column)) & 1) != 0;
    }

    /// <summary>
    /// Whether this entity takes part in the native test at all. The player is not an
    /// obstacle to itself, a skipped entity is ignored by the native routine, and a model
    /// with no verified mask is left alone.
    /// </summary>
    public static bool IsObstacle(WorldMapEntitySnapshot entity) =>
        !entity.IsPlayer &&
        (entity.Flags & SkippedFlag) == 0 &&
        NativeMasks.ContainsKey(entity.ModelId);

    public static bool IsParkedBuggy(WorldMapEntitySnapshot entity) => entity.ModelId == 6 && IsObstacle(entity);

    /// <summary>
    /// Whether any of these entities refuses the party this point.
    /// </summary>
    public static bool IsBlocked(
        IReadOnlyList<WorldMapEntitySnapshot> entities,
        int playerModelId,
        int x,
        int z,
        int wrapWidth,
        int wrapHeight)
    {
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (IsObstacle(entity) &&
                Blocks(playerModelId, x, z, entity.ModelId, entity.X, entity.Z, wrapWidth, wrapHeight))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether walking straight from one point to another would be refused.
    ///
    /// <para>FUN00762A21 refuses a step only when it collides <em>and</em> closes the
    /// wrapped Manhattan distance to the other entity; a colliding step that moves away
    /// returns 2 and is allowed. That matters here because the party dismounts standing
    /// inside the car's own footprint, and every direction would otherwise look refused.
    /// </para>
    ///
    /// <para>Check every crossed mask-cell boundary and Manhattan turning point. Endpoint
    /// sampling can miss a clipped corner, even when the step is shorter than a cell.</para>
    /// </summary>
    public static bool BlocksSegment(
        IReadOnlyList<WorldMapEntitySnapshot> entities,
        int playerModelId,
        int fromX,
        int fromZ,
        int toX,
        int toZ,
        int wrapWidth,
        int wrapHeight)
    {
        var deltaX = WorldMapTargetCatalog.WrappedDelta(fromX, toX, wrapWidth);
        var deltaZ = WorldMapTargetCatalog.WrappedDelta(fromZ, toZ, wrapHeight);
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (!IsObstacle(entity))
            {
                continue;
            }

            // Express the segment relative to each nearby image of the obstacle. The
            // world wraps, but an individual shortest segment never needs further images.
            var startX = -WorldMapTargetCatalog.WrappedDelta(fromX, entity.X, wrapWidth);
            var startZ = -WorldMapTargetCatalog.WrappedDelta(fromZ, entity.Z, wrapHeight);
            foreach (var offsetX in wrapWidth > 0 ? new[] { -wrapWidth, 0, wrapWidth } : new[] { 0 })
            foreach (var offsetZ in wrapHeight > 0 ? new[] { -wrapHeight, 0, wrapHeight } : new[] { 0 })
            {
                var sx = startX + offsetX; var sz = startZ + offsetZ;
                if (Math.Min(sx, sx + deltaX) > 1024 || Math.Max(sx, sx + deltaX) < -1024 ||
                    Math.Min(sz, sz + deltaZ) > 1024 || Math.Max(sz, sz + deltaZ) < -1024) continue;
                var times = new List<double> { 0, 1 };
                for (var edge = -1024; edge <= 1024; edge += CellSize)
                {
                    if (deltaX != 0) AddTime((edge - sx) / (double)deltaX);
                    if (deltaZ != 0) AddTime((edge - sz) / (double)deltaZ);
                }
                void AddTime(double t) { if (t > 0 && t < 1) times.Add(t); }
                times.Sort();
                for (var t = 1; t < times.Count; t++)
                {
                    var mid = (times[t - 1] + times[t]) / 2;
                    var x = sx + deltaX * mid; var z = sz + deltaZ * mid;
                    var slope = Math.Sign(x) * deltaX + Math.Sign(z) * deltaZ;
                    var before = times[t - 1]; var after = times[t];
                    var nearest = Math.Min(Math.Abs(sx + deltaX * before) + Math.Abs(sz + deltaZ * before),
                        Math.Abs(sx + deltaX * after) + Math.Abs(sz + deltaZ * after));
                    // A native step can straddle the closest point and end in this mask
                    // while still net-closing, even if this interval points outward.
                    // Manhattan distance along a straight leg is convex: rejecting mask
                    // overlap below the starting distance covers those straddling steps.
                    // Monotonic outward escape from an initial overlap remains allowed.
                    if (slope >= 0 && nearest >= Math.Abs(sx) + Math.Abs(sz) - 1e-7) continue;
                    var column = (int)Math.Floor((-x + MaskCentreOffset) / CellSize);
                    var row = (int)Math.Floor((-z + MaskCentreOffset) / CellSize);
                    if (NativeMasks.TryGetValue(playerModelId, out var own) && NativeMasks.TryGetValue(entity.ModelId, out var other) &&
                        column is >= 0 and < MaskCells && row is >= 0 and < MaskCells &&
                        (((own[row] >> column) & 1) != 0 || ((other[7 - row] >> (7 - column)) & 1) != 0)) return true;
                }
                // Integer endpoints exactly on a cell edge follow the native shift rule.
                if (Math.Abs(sx + deltaX) + Math.Abs(sz + deltaZ) < Math.Abs(sx) + Math.Abs(sz) &&
                    Blocks(playerModelId, toX, toZ, entity.ModelId, entity.X, entity.Z, wrapWidth, wrapHeight)) return true;
            }
        }

        return false;
    }

}
