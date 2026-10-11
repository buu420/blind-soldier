using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Which underwater objects a sighted player can actually see this frame: the Sunken Gelnika
/// (17), the Key of the Ancients (26), the red submarine wreck (28) and Emerald Weapon (30).
///
/// <para>Everything comes from what the world map's own renderer did in the frame, on both
/// runtimes through the same guest addresses:</para>
/// <list type="bullet">
/// <item>FUN_0076315C clears every entity's <c>+0x51 &amp; 7</c> and every model render
/// record's <c>+0x24</c> at the start of a frame. FUN_00762D74 sets <c>+0x51 | 2</c> on each
/// entity standing in a terrain chunk the camera draws. FUN_0076328F draws only entities with
/// bit 2 and without the hidden bit 8, through FUN_0075E0BA.</item>
/// <item>FUN_0075E0BA stores the drawn instance's exact camera-space position into the model's
/// render record (<c>+0x48/+0x4C/+0x50</c>, floats; FUN_006611DA divides by 1.0) and sets
/// <c>+0x24 = 1</c>. Underwater models fade against the far colour with alpha
/// <c>255 - ((z/4 - 4000) &gt;&gt; 3)</c>. The record is FUN_00768A37's
/// <c>0xE2C808 + (slot[0xE3B0F8 + model] - 1) * 0xB8</c> while <c>0xE3A8A8</c> is set.</item>
/// <item>The camera is FUN_0074E8CE's rotation D_00DFC448 and translation D_00DE6A20.t; an
/// entity is placed relative to the reference D_00E04918 (X, Z wrapped) and the base height
/// D_00DE6A04 (FUN_0076328F, FUN_0074D50E); terrain shares that frame (FUN_0074D33A).</item>
/// <item>The renderer context <c>[0xDB2BB8]</c> carries the projection at +0x8D0 and the
/// viewport at +0x848 (FUN_00676578, FUN_0067D2BF), as the submarine mission reader uses.</item>
/// </list>
///
/// <para>Not native: the renderer has no CPU-side occlusion test, so the seabed between the
/// camera and the object is tested here against the exact underwater geometry. The test is
/// claimed only where neither the object's nor the terrain's round-earth drop (FUN_00762F9A,
/// FUN_0075F0AD) applies, so that flat geometry is the picture; beyond it nothing is claimed.
/// Anything torn, missing, unreadable or out of range is never visible.</para>
/// </summary>
public sealed class WorldMapUnderwaterVisibilityReader
{
    internal const uint AddressEntityListHead = 0x00E39A00;
    internal const uint AddressModelSlotsReady = 0x00E3A8A8;
    internal const uint AddressModelSlots = 0x00E3B0F8;
    internal const uint AddressModelRecords = 0x00E2C808;
    internal const int ModelRecordStride = 0xB8;
    internal const int ModelRecordCount = 0x0D;
    internal const int RecordDrawnOffset = 0x24;
    internal const int RecordViewOffset = 0x48;
    internal const uint AddressCameraRotation = 0x00DFC448;
    // Native MATRIX is packed: 9 int16 rotation entries followed immediately by
    // 3 int32 translations. FUN_0074D50E (x64 FUN_7ff7029e1950) reads
    // DE6A32/DE6A36/DE6A3A; aligning the vector to +0x14 misreads every component.
    internal const uint AddressCameraTranslation = 0x00DE6A20 + 0x12;
    internal const uint AddressReferencePoint = 0x00E04918;
    internal const uint AddressBaseHeight = 0x00DE6A04;
    internal const uint AddressCurvatureStart = 0x00E045D8;
    internal const uint AddressRendererContext = 0x00DB2BB8;
    internal const int ProjectionOffset = 0x8D0;
    internal const int ViewportOffset = 0x848;

    private const int EntityModelOffset = 0x50;
    private const int EntityFlagsOffset = 0x51;
    private const int EntityDrawOffsetY = 0x44;
    private const byte ChunkDrawnBit = 0x02;
    private const byte HiddenBit = 0x08;
    private const int MaximumEntities = 64;
    private const int MaximumAttempts = 4;
    private const int WrapX = 0x48000;
    private const int WrapZ = 0x38000;
    private const int MaximumModelId = 0x2B;
    private const uint MaximumContextAddress = 0xFFFF0000;
    private const int MaximumViewportExtent = 8192;

    /// <summary>A drawn position may differ from this reader's GTE arithmetic by rounding only.</summary>
    private const float SameFrameTolerance = 2f;
    /// <summary>Distance the curvature-free check keeps from where the native drop starts.</summary>
    private const int CurvatureMargin = 16;
    /// <summary>The seabed must be this far below the sight line to count as clear.</summary>
    private const int SeabedClearance = 32;
    /// <summary>Seabed coverage may meet across a triangle edge within this much rounding.</summary>
    private const double CoverageTolerance = 0.5;
    /// <summary>
    /// A seabed-resting object's own footprint (17, 26, 28): its line ends this far before its
    /// origin, which would otherwise always graze the seabed it stands on. Emerald has none.
    /// </summary>
    private const int TargetFootprint = 256;
    private const int EmeraldModel = 30;

    private static readonly int[] ReportedModels = [17, 26, 28, 30];

    private readonly ILegacyAddressSpace memory;
    private readonly WorldMapData map;
    private readonly Dictionary<(int MeshX, int MeshZ), List<WorldMapTriangle>> seabedCells;

    public WorldMapUnderwaterVisibilityReader(ILegacyAddressSpace memory, WorldMapData map)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        seabedCells = [];
        if (map.WorldMapType != 2) return;
        foreach (var triangle in map.Triangles)
        {
            var cell = (triangle.MeshX, triangle.MeshZ);
            if (!seabedCells.TryGetValue(cell, out var list)) seabedCells[cell] = list = [];
            list.Add(triangle);
        }
    }

    public WorldMapUnderwaterVisibilityReadResult Read(
        WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapEntitySnapshot> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (state.WorldMapType != 2 || map.WorldMapType != 2 || seabedCells.Count == 0)
        {
            return Unusable("not the underwater map");
        }

        var candidates = entities
            .Where(entity => !entity.IsPlayer && ReportedModels.Contains(entity.ModelId))
            .ToList();
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            if (!TryCapture(candidates, out var first) || !TryCapture(candidates, out var second))
            {
                return Unusable("guest memory unreadable");
            }

            if (!first.SameAs(second))
            {
                continue;
            }

            return Evaluate(first, candidates);
        }

        return Unusable("torn frame on every attempt");
    }

    private WorldMapUnderwaterVisibilityReadResult Evaluate(Capture capture, List<WorldMapEntitySnapshot> candidates)
    {
        if (capture.Module != WorldMapStateReader.WorldModule || capture.MapType != 2)
        {
            return Unusable($"module {capture.Module}, map {capture.MapType}");
        }

        if (!capture.Camera.IsRotation())
        {
            return Unusable("camera rotation is not a rotation");
        }

        if (capture.Context == 0 || capture.Context > MaximumContextAddress ||
            capture.Viewport[2] is <= 0 or > MaximumViewportExtent ||
            capture.Viewport[3] is <= 0 or > MaximumViewportExtent ||
            capture.Projection.Any(value => !float.IsFinite(value)))
        {
            return Unusable("renderer context unavailable");
        }

        var visible = new HashSet<uint>();
        var notes = new List<string>();
        foreach (var candidate in candidates)
        {
            var reason = Judge(capture, candidate);
            if (reason is null) visible.Add(candidate.GuestPointer);
            else notes.Add($"{candidate.ModelId}:{reason}");
        }

        return new(true, visible,
            $"visible={visible.Count}" + (notes.Count == 0 ? string.Empty : " (" + string.Join(", ", notes) + ")"));
    }

    /// <summary>Null when the entity is visible; otherwise why it is not.</summary>
    private string? Judge(Capture capture, WorldMapEntitySnapshot candidate)
    {
        if (!capture.Entities.TryGetValue(candidate.GuestPointer, out var entity) ||
            entity.Model != candidate.ModelId)
        {
            return "not in the live list";
        }

        // The caller labels and steers by its own coordinates: they must be this frame's.
        if (entity.X != candidate.X || entity.Y != candidate.Y || entity.Z != candidate.Z)
        {
            return "caller position is from another frame";
        }

        // Model render records are per model: two live entities sharing one cannot be told apart.
        if (capture.Entities.Values.Count(other => other.Model == entity.Model) != 1)
        {
            return "shared model record";
        }

        if ((entity.Flags & (ChunkDrawnBit | HiddenBit)) != ChunkDrawnBit)
        {
            return "not drawn";
        }

        if (!capture.Records.TryGetValue(entity.Model, out var record) || !record.Drawn)
        {
            return "model not drawn this frame";
        }

        if (!float.IsFinite(record.X) || !float.IsFinite(record.Y) || !float.IsFinite(record.Z) || record.Z <= 0f)
        {
            return "behind the camera";
        }

        var depth = (int)record.Z;
        var alpha = Math.Clamp(255 - ((depth / 4 - 4000) >> 3), 0, 255);
        if (alpha <= 0)
        {
            return "faded";
        }

        // The entity, placed as FUN_0076328F places it, must be exactly what was drawn.
        var dx = Wrap(entity.X - capture.Reference[0], WrapX);
        var dz = Wrap(entity.Z - capture.Reference[2], WrapZ);
        var dy = entity.Y - capture.BaseHeight + entity.DrawOffsetY;
        if (!FitsShort(dx) || !FitsShort(dy) || !FitsShort(dz))
        {
            return "outside the native draw range";
        }

        var expected = capture.Camera.RotTrans(dx, dy, dz);
        if (Math.Abs(expected.X - record.X) > SameFrameTolerance ||
            Math.Abs(expected.Y - record.Y) > SameFrameTolerance ||
            Math.Abs(expected.Z - record.Z) > SameFrameTolerance)
        {
            return "drawn position is not this entity under this camera";
        }

        // FUN_00762F9A measures its drop at world height 0 (FUN_0074D33A puts -D_00DE6A04 in Y).
        var dropDepth = capture.Camera.RotTrans(dx, -capture.BaseHeight, dz).Z;
        if (((long)Math.Floor(dropDepth) >> 2) - capture.CurvatureStart > -CurvatureMargin)
        {
            return "beyond the curvature-free range";
        }

        if (!TryProject(capture, record.X, record.Y, record.Z, out var screenX, out var screenY) ||
            screenX < capture.Viewport[0] || screenX >= capture.Viewport[0] + capture.Viewport[2] ||
            screenY < capture.Viewport[1] || screenY >= capture.Viewport[1] + capture.Viewport[3])
        {
            return "off screen";
        }

        return SightLineIsClear(capture, dx, dy, dz, entity.Model) ? null : "seabed in the way";
    }
    /// <summary>
    /// The camera's own position solves <c>R·rel/4096 + T = 0</c>: <c>rel = -Rᵀ·T / 4096</c> for
    /// FUN_0074E8CE's rotation. The sight line from there to the drawn origin is tested exactly
    /// against the underwater seabed: every triangle it passes over contributes the stretch of
    /// the line above it, and on that stretch both the seabed plane and the line are linear, so
    /// their ends decide it. The stretches must cover the whole line - any gap is unknown
    /// seabed and is never clear. Only an object resting on the seabed (17, 26, 28) keeps a
    /// bounded footprint at its own end; Emerald floats, so its line is tested all the way.
    /// </summary>
    private bool SightLineIsClear(Capture capture, int targetX, int targetY, int targetZ, int model)
    {
        var camera = capture.Camera.Origin();
        // Unwrapped world frame around the reference; mesh cells beyond the world edge are
        // the wrapped copies of the cells at the other edge.
        double ax = capture.Reference[0] + camera.X, az = capture.Reference[2] + camera.Z;
        double bx = capture.Reference[0] + targetX, bz = capture.Reference[2] + targetZ;
        double ay = capture.BaseHeight + camera.Y, by = capture.BaseHeight + targetY;
        double dx = bx - ax, dz = bz - az, dy = by - ay;
        var horizontal = Math.Sqrt(dx * dx + dz * dz);
        if (horizontal < 1d)
        {
            return false;
        }

        var end = model == EmeraldModel ? 1d : 1d - TargetFootprint / horizontal;
        if (end <= 0d)
        {
            return true;
        }

        var endX = ax + dx * end;
        var endZ = az + dz * end;
        var covered = new List<(double From, double To)>();
        var firstCellX = (int)Math.Floor(Math.Min(ax, endX) / WorldMapDataLoader.MeshSize);
        var lastCellX = (int)Math.Floor(Math.Max(ax, endX) / WorldMapDataLoader.MeshSize);
        var firstCellZ = (int)Math.Floor(Math.Min(az, endZ) / WorldMapDataLoader.MeshSize);
        var lastCellZ = (int)Math.Floor(Math.Max(az, endZ) / WorldMapDataLoader.MeshSize);
        var cellsX = WrapX / WorldMapDataLoader.MeshSize;
        var cellsZ = WrapZ / WorldMapDataLoader.MeshSize;
        for (var cellZ = firstCellZ; cellZ <= lastCellZ; cellZ++)
        {
            for (var cellX = firstCellX; cellX <= lastCellX; cellX++)
            {
                var sourceX = ((cellX % cellsX) + cellsX) % cellsX;
                var sourceZ = ((cellZ % cellsZ) + cellsZ) % cellsZ;
                if (!seabedCells.TryGetValue((sourceX, sourceZ), out var cell))
                {
                    continue;
                }

                double shiftX = (cellX - sourceX) * (double)WorldMapDataLoader.MeshSize;
                double shiftZ = (cellZ - sourceZ) * (double)WorldMapDataLoader.MeshSize;
                foreach (var triangle in cell)
                {
                    // A cliff face can have zero X/Z area while still blocking the
                    // picture. Test the actual 3-D face before clipping its floor plane.
                    if (IntersectsFace(triangle, shiftX, shiftZ, ax, ay, az, dx, dy, dz, end))
                        return false;
                    if (!TryOverlap(triangle, shiftX, shiftZ, ax, az, dx, dz, end, out var from, out var to))
                    {
                        continue;
                    }

                    foreach (var t in new[] { from, to })
                    {
                        var seabed = PlaneHeight(triangle, ax + dx * t - shiftX, az + dz * t - shiftZ);
                        if (double.IsNaN(seabed) || seabed + SeabedClearance >= ay + dy * t)
                        {
                            return false;
                        }
                    }

                    covered.Add((from, to));
                }
            }
        }

        // Unknown seabed anywhere along the line is never assumed clear.
        var tolerance = CoverageTolerance / horizontal;
        var reached = 0d;
        foreach (var (from, to) in covered.OrderBy(interval => interval.From))
        {
            if (from > reached + tolerance)
            {
                return false;
            }

            reached = Math.Max(reached, to);
        }

        return reached + tolerance >= end;
    }

    private static bool IntersectsFace(WorldMapTriangle triangle, double shiftX, double shiftZ,
        double ax, double ay, double az, double dx, double dy, double dz, double end)
    {
        var a = triangle.Vertex0; var b = triangle.Vertex1; var c = triangle.Vertex2;
        var e1 = (X: (double)b.X - a.X, Y: (double)b.Y - a.Y, Z: (double)b.Z - a.Z);
        var e2 = (X: (double)c.X - a.X, Y: (double)c.Y - a.Y, Z: (double)c.Z - a.Z);
        var direction = (X: dx, Y: dy, Z: dz);
        var h = Cross3(direction, e2);
        var determinant = Dot(e1, h);
        if (Math.Abs(determinant) < 1e-8) return false;
        var s = (X: ax - a.X - shiftX, Y: ay - a.Y, Z: az - a.Z - shiftZ);
        var u = Dot(s, h) / determinant;
        if (u < -1e-8 || u > 1d + 1e-8) return false;
        var q = Cross3(s, e1);
        var v = Dot(direction, q) / determinant;
        if (v < -1e-8 || u + v > 1d + 1e-8) return false;
        var t = Dot(e2, q) / determinant;
        return t >= 0d && t <= end;

        static double Dot((double X, double Y, double Z) p, (double X, double Y, double Z) q) =>
            p.X * q.X + p.Y * q.Y + p.Z * q.Z;
        static (double X, double Y, double Z) Cross3((double X, double Y, double Z) p, (double X, double Y, double Z) q) =>
            (p.Y * q.Z - p.Z * q.Y, p.Z * q.X - p.X * q.Z, p.X * q.Y - p.Y * q.X);
    }

    /// <summary>
    /// The part of the line <c>a + t·d</c>, <c>0 ≤ t ≤ end</c>, that lies over the triangle
    /// (edges included), for the triangle's copy shifted by <paramref name="shiftX"/>/<paramref name="shiftZ"/>.
    /// </summary>
    private static bool TryOverlap(
        WorldMapTriangle triangle, double shiftX, double shiftZ,
        double ax, double az, double dx, double dz, double end,
        out double from, out double to)
    {
        from = 0d;
        to = end;
        var v0 = (X: triangle.Vertex0.X + shiftX, Z: triangle.Vertex0.Z + shiftZ);
        var v1 = (X: triangle.Vertex1.X + shiftX, Z: triangle.Vertex1.Z + shiftZ);
        var v2 = (X: triangle.Vertex2.X + shiftX, Z: triangle.Vertex2.Z + shiftZ);
        var orientation = Math.Sign((v1.X - v0.X) * (v2.Z - v0.Z) - (v1.Z - v0.Z) * (v2.X - v0.X));
        if (orientation == 0)
        {
            return false;
        }

        foreach (var (p, q) in new[] { (v0, v1), (v1, v2), (v2, v0) })
        {
            // Inside this edge: orientation · cross(q - p, point - p) >= 0, linear in t.
            var ex = q.X - p.X;
            var ez = q.Z - p.Z;
            var g0 = orientation * (ex * (az - p.Z) - ez * (ax - p.X));
            var g1 = orientation * (ex * dz - ez * dx);
            if (Math.Abs(g1) < 1e-12)
            {
                if (g0 < -1e-6) return false;
                continue;
            }

            var crossing = -g0 / g1;
            if (g1 > 0) from = Math.Max(from, crossing);
            else to = Math.Min(to, crossing);
            if (from > to) return false;
        }

        return true;
    }

    /// <summary>The seabed plane of the triangle at an unshifted point, NaN for a degenerate one.</summary>
    private static double PlaneHeight(WorldMapTriangle triangle, double x, double z)
    {
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (Math.Abs(denominator) < 1e-9)
        {
            return double.NaN;
        }

        var weightA = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / denominator;
        var weightB = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / denominator;
        return weightA * a.Y + weightB * b.Y + (1d - weightA - weightB) * c.Y;
    }

    /// <summary>The context's stored projection read by rows, as SubmarineMissionStateReader does.</summary>
    private static bool TryProject(Capture capture, float x, float y, float z, out float screenX, out float screenY)
    {
        var p = capture.Projection;
        var clipX = p[0] * x + p[1] * y + p[2] * z + p[3];
        var clipY = p[4] * x + p[5] * y + p[6] * z + p[7];
        var clipW = p[12] * x + p[13] * y + p[14] * z + p[15];
        screenX = 0f;
        screenY = 0f;
        if (!float.IsFinite(clipX) || !float.IsFinite(clipY) || !float.IsFinite(clipW) || clipW <= 0f)
        {
            return false;
        }

        screenX = clipX / clipW;
        screenY = clipY / clipW;
        return float.IsFinite(screenX) && float.IsFinite(screenY);
    }

    private bool TryCapture(List<WorldMapEntitySnapshot> candidates, out Capture capture)
    {
        capture = new Capture();
        if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) ||
            !memory.TryReadInt32((uint)WorldMapStateReader.AddressWorldMapType, out capture.MapType) ||
            !memory.TryReadInt32(AddressModelSlotsReady, out var slotsReady) ||
            !memory.TryReadInt32(AddressBaseHeight, out capture.BaseHeight) ||
            !memory.TryReadInt32(AddressCurvatureStart, out capture.CurvatureStart) ||
            !memory.TryReadUInt32(AddressRendererContext, out capture.Context))
        {
            return false;
        }

        capture.Module = module;
        capture.SlotsReady = slotsReady;
        for (var i = 0; i < 3; i++)
        {
            if (!memory.TryReadInt32(AddressReferencePoint + (uint)(i * 4), out capture.Reference[i]) ||
                !memory.TryReadInt32(AddressCameraTranslation + (uint)(i * 4), out capture.Camera.Translation[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < 9; i++)
        {
            if (!memory.TryReadInt16(AddressCameraRotation + (uint)(i * 2), out capture.Camera.Rotation[i]))
            {
                return false;
            }
        }

        if (capture.Context != 0 && capture.Context <= MaximumContextAddress)
        {
            for (var i = 0; i < 4; i++)
            {
                if (!memory.TryReadInt32(capture.Context + ViewportOffset + (uint)(i * 4), out capture.Viewport[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < 16; i++)
            {
                if (!memory.TryReadSingle(capture.Context + ProjectionOffset + (uint)(i * 4), out capture.Projection[i]))
                {
                    return false;
                }
            }
        }

        // The live list itself, so identity and world positions belong to this capture.
        if (!memory.TryReadUInt32(AddressEntityListHead, out var node))
        {
            return false;
        }

        var visited = new HashSet<uint>();
        while (node != 0)
        {
            if (visited.Count >= MaximumEntities || !visited.Add(node) ||
                !memory.TryReadUInt32(node, out var next) ||
                !memory.TryReadInt32(node + 0x0C, out var x) ||
                !memory.TryReadInt32(node + 0x10, out var y) ||
                !memory.TryReadInt32(node + 0x14, out var z) ||
                !memory.TryReadInt16(node + EntityDrawOffsetY, out var drawOffset) ||
                !memory.TryReadByte(node + EntityModelOffset, out var model) ||
                !memory.TryReadByte(node + EntityFlagsOffset, out var flags))
            {
                return false;
            }

            capture.Order.Add(node);
            capture.Entities[node] = new EntityState(x, y, z, drawOffset, model, flags);
            node = next;
        }

        foreach (var model in candidates.Select(candidate => candidate.ModelId).Distinct())
        {
            if (slotsReady == 0 || model is < 0 or >= MaximumModelId ||
                !memory.TryReadByte(AddressModelSlots + (uint)model, out var rawSlot))
            {
                continue;
            }

            var slot = unchecked((sbyte)rawSlot);
            if (slot < 1 || slot > ModelRecordCount)
            {
                continue;
            }

            var record = AddressModelRecords + (uint)((slot - 1) * ModelRecordStride);
            if (!memory.TryReadInt32(record + RecordDrawnOffset, out var drawn) ||
                !memory.TryReadSingle(record + RecordViewOffset, out var viewX) ||
                !memory.TryReadSingle(record + RecordViewOffset + 4, out var viewY) ||
                !memory.TryReadSingle(record + RecordViewOffset + 8, out var viewZ))
            {
                return false;
            }

            capture.Records[model] = new RecordState(slot, drawn == 1, viewX, viewY, viewZ);
        }

        return true;
    }

    private static int Wrap(int delta, int extent)
    {
        var half = extent / 2;
        if (delta < -half) delta += extent;
        else if (delta >= half) delta -= extent;
        return delta;
    }

    private static bool FitsShort(int value) => value is >= short.MinValue and <= short.MaxValue;

    private static WorldMapUnderwaterVisibilityReadResult Unusable(string why) =>
        new(false, new HashSet<uint>(), why);

    private readonly record struct EntityState(int X, int Y, int Z, short DrawOffsetY, byte Model, byte Flags);

    private readonly record struct RecordState(int Slot, bool Drawn, float X, float Y, float Z);

    private sealed class CameraState
    {
        internal readonly short[] Rotation = new short[9];
        internal readonly int[] Translation = new int[3];

        /// <summary>PSX RotTrans: <c>(R·v) &gt;&gt; 12 + T</c> with D_00DFC448 row-major.</summary>
        internal (float X, float Y, float Z) RotTrans(int x, int y, int z)
        {
            float Row(int row) =>
                (((long)Rotation[row * 3] * x + (long)Rotation[row * 3 + 1] * y + (long)Rotation[row * 3 + 2] * z) >> 12) +
                Translation[row];
            return (Row(0), Row(1), Row(2));
        }

        internal (int X, int Y, int Z) Origin()
        {
            int Column(int column) => (int)Math.Round(-(
                (double)Rotation[column] * Translation[0] +
                (double)Rotation[3 + column] * Translation[1] +
                (double)Rotation[6 + column] * Translation[2]) / 4096d);
            return (Column(0), Column(1), Column(2));
        }

        /// <summary>
        /// FUN_0074E8CE multiplies two rotations, so D_00DFC448 is orthonormal (at 4.12) with
        /// determinant +1: unit rows, perpendicular rows, no mirror. A skewed, scaled or mirrored
        /// matrix is a torn or foreign read, and its transpose would not be the camera's inverse.
        /// </summary>
        internal bool IsRotation()
        {
            const double unit = 4096d;
            const double tolerance = 0.02;
            double Row(int row, int column) => Rotation[row * 3 + column] / unit;
            double Dot(int first, int second) =>
                Row(first, 0) * Row(second, 0) + Row(first, 1) * Row(second, 1) + Row(first, 2) * Row(second, 2);
            for (var row = 0; row < 3; row++)
            {
                if (Math.Abs(Dot(row, row) - 1d) > tolerance) return false;
                for (var other = row + 1; other < 3; other++)
                {
                    if (Math.Abs(Dot(row, other)) > tolerance) return false;
                }
            }

            var determinant =
                Row(0, 0) * (Row(1, 1) * Row(2, 2) - Row(1, 2) * Row(2, 1)) -
                Row(0, 1) * (Row(1, 0) * Row(2, 2) - Row(1, 2) * Row(2, 0)) +
                Row(0, 2) * (Row(1, 0) * Row(2, 1) - Row(1, 1) * Row(2, 0));
            return Math.Abs(determinant - 1d) <= 3 * tolerance;
        }

        internal bool SameAs(CameraState other) =>
            Rotation.AsSpan().SequenceEqual(other.Rotation) && Translation.AsSpan().SequenceEqual(other.Translation);
    }

    private sealed class Capture
    {
        internal int Module;
        internal int SlotsReady;
        internal int MapType;
        internal int BaseHeight;
        internal int CurvatureStart;
        internal uint Context;
        internal readonly int[] Reference = new int[3];
        internal readonly CameraState Camera = new();
        internal readonly int[] Viewport = new int[4];
        internal readonly float[] Projection = new float[16];
        internal readonly List<uint> Order = [];
        internal readonly Dictionary<uint, EntityState> Entities = [];
        internal readonly Dictionary<int, RecordState> Records = [];

        internal bool SameAs(Capture other) =>
            Module == other.Module && SlotsReady == other.SlotsReady && MapType == other.MapType && BaseHeight == other.BaseHeight &&
            CurvatureStart == other.CurvatureStart && Context == other.Context &&
            Reference.AsSpan().SequenceEqual(other.Reference) && Camera.SameAs(other.Camera) &&
            Viewport.AsSpan().SequenceEqual(other.Viewport) &&
            Projection.Select(BitConverter.SingleToInt32Bits).SequenceEqual(other.Projection.Select(BitConverter.SingleToInt32Bits)) &&
            Order.SequenceEqual(other.Order) &&
            Entities.Count == other.Entities.Count && Entities.All(pair => other.Entities.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
            Records.Count == other.Records.Count && Records.All(pair => other.Records.TryGetValue(pair.Key, out var value) &&
                value.Slot == pair.Value.Slot && value.Drawn == pair.Value.Drawn &&
                BitConverter.SingleToInt32Bits(value.X) == BitConverter.SingleToInt32Bits(pair.Value.X) &&
                BitConverter.SingleToInt32Bits(value.Y) == BitConverter.SingleToInt32Bits(pair.Value.Y) &&
                BitConverter.SingleToInt32Bits(value.Z) == BitConverter.SingleToInt32Bits(pair.Value.Z));
    }
}

/// <summary>
/// One coherent observation. <see cref="VisibleEntities"/> holds the guest pointers of the
/// non-player underwater models 17, 26, 28 and 30 that are drawn on screen, not faded and not
/// behind the seabed this frame; it is empty whenever <see cref="IsUsable"/> is false.
/// </summary>
public readonly record struct WorldMapUnderwaterVisibilityReadResult(
    bool IsUsable,
    IReadOnlySet<uint> VisibleEntities,
    string Diagnostic);
