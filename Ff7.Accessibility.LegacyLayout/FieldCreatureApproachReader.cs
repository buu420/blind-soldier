using System.Buffers;
using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Observes only the two reviewed creature approaches in las4_4. The polygon
/// callback is below HRC pose construction and above GPU clipping, so script
/// visibility is combined with owned, opaque, currently clipped geometry.
/// All addresses are guest addresses in both runtimes. This reader never writes.
/// </summary>
public sealed class FieldCreatureApproachReader
{
    private readonly ILegacyAddressSpace memory;
    private const int EntityId = 12;
    private const byte CreatureModelId = 8;
    private const int MaxBones = 64, MaxRsdTotal = 64, MaxGroups = 32;
    private const int MaxVertices = 4096, MaxIndices = 24576, MaxVerticesTotal = 16384, MaxIndicesTotal = 98304;
    private static ReadOnlySpan<byte> FirstOfst => [0xC3, 0, 0, 1, 0xEE, 1, 0xCC, 1, 0x5C, 0, 0x1E, 0];
    private static ReadOnlySpan<byte> SecondOfst => [0xC3, 0, 0, 1, 0x78, 0, 0x1A, 0, 8, 0, 0x14, 0];

    public FieldCreatureApproachReader(ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public bool TryCapture(uint polygonSet, uint gameObject, out FieldCreatureApproachSample sample)
    {
        sample = default;
        if (!TryCaptureOnce(polygonSet, gameObject, true, null, out var before) ||
            !TryCaptureOnce(polygonSet, gameObject, true, null, out var after) || before != after)
            return false;
        sample = before;
        return true;
    }

    public bool IsCurrent(FieldCreatureApproachSample sample, bool requireMoving = true)
    {
        if (sample.ScriptPointer == 0 || sample.Context.FieldId != 767 || sample.Context.EntityId != EntityId)
            return false;
        if (requireMoving)
            return TryCapture(sample.PolygonSet, sample.GameObject, out var current) && current == sample;
        // The original observed action stays the queue key. Its actor can finish
        // moving, but its identity, original script bytes and current visible
        // geometry must still survive a new coherent capture.
        return TryCaptureOnce(sample.PolygonSet, sample.GameObject, false, sample, out var before) &&
               TryCaptureOnce(sample.PolygonSet, sample.GameObject, false, sample, out var after) && before == after &&
               before.ScriptPointer == sample.ScriptPointer && before.ObjectTable == sample.ObjectTable &&
               before.RenderTable == sample.RenderTable && before.ObjectBase == sample.ObjectBase &&
               before.RenderModel == sample.RenderModel && before.RenderFrame == sample.RenderFrame &&
               before.HrcModel == sample.HrcModel && before.GraphicsDriver == sample.GraphicsDriver &&
               before.ProjectionOwner == sample.ProjectionOwner && before.ProjectionOwnerMatrixSet == sample.ProjectionOwnerMatrixSet &&
               before.DefaultProjection == sample.DefaultProjection && before.DefaultView == sample.DefaultView &&
               before.WorldMatrix == sample.WorldMatrix && before.ActorView == sample.ActorView && before.EffectiveProjection == sample.EffectiveProjection &&
               before.ModelId == sample.ModelId;
    }

    private bool TryCaptureOnce(uint polygonSet, uint gameObject, bool requireMoving,
        FieldCreatureApproachSample? observed, out FieldCreatureApproachSample sample)
    {
        sample = default;
        var hash = 14695981039346656037UL;
        if (polygonSet == 0 || gameObject == 0 || !Gates(gameObject, requireMoving) ||
            !memory.TryReadUInt32(0x00CBF5E8, out var scriptPointer) || scriptPointer == 0 ||
            !Context(scriptPointer, out var context, out var scriptEntry, ref hash) ||
            !memory.TryReadByte(0x00CBFB7C, out var modelId) || modelId != CreatureModelId ||
            !memory.TryReadUInt16(0x00CFF73E, out var modelCount) || modelCount <= modelId || modelCount > 32 ||
            !memory.TryReadUInt32(0x00CC0B60, out var objects) || objects == 0 ||
            !memory.TryReadUInt32(0x00CFF738, out var renders) || renders == 0 ||
            !Add(objects, modelId * 0x88u, out var actor) || !Add(renders, modelId * 400u, out var render))
            return false;
        Feed(ref hash, modelCount);
        Span<byte> actorBytes = stackalloc byte[0x88];
        Span<byte> renderBytes = stackalloc byte[0x180];
        if (!Read(actor, actorBytes, ref hash) || !Read(render, renderBytes, ref hash) ||
            actorBytes[0x62] != 1 || actorBytes[0x5D] != EntityId || U16(actorBytes, 0x6C) != CreatureModelId ||
            U32(renderBytes, 0x168) != 1 || U32(renderBytes, 0x16C) != 1 ||
            !memory.TryReadInt32(0x00CFFA48 + modelId * 4u, out var enabled) || enabled != 1)
            return false;
        var phase = actorBytes[0x5C]; var duration = U16(actorBytes, 0x58); var step = U16(actorBytes, 0x5A);
        var x = I32(actorBytes, 0x40); var y = I32(actorBytes, 0x48); var z = I32(actorBytes, 0x50);
        var ix = I16(actorBytes, 0x44); var iy = I16(actorBytes, 0x4C); var iz = I16(actorBytes, 0x54);
        if (requireMoving)
        {
            if (!Anchor(scriptPointer, context, scriptEntry) || phase is not (1 or 2) || duration == 0 ||
                duration > short.MaxValue || step > duration || (x == ix && y == iy && z == iz)) return false;
            var first = context.ScriptId == 3;
            if (I16(actorBytes, 0x46) != (first ? 494 : 120) || I16(actorBytes, 0x4E) != (first ? 460 : 26) ||
                I16(actorBytes, 0x56) != (first ? 92 : 8) || ix != (first ? 2574 : 494) ||
                iy != (first ? 2672 : 460) || iz != (first ? -836 : 92)) return false;
        }
        else
        {
            if (observed is not { } original || !OriginalAnchor(scriptPointer, original) || phase > 3 ||
                duration > short.MaxValue || (duration != 0 && step > duration)) return false;
        }
        var frame = U32(renderBytes, 0x178);
        Span<byte> frameBytes = stackalloc byte[0xB8];
        if (!Read(frame, frameBytes, ref hash) || U32(frameBytes, 0) != 1) return false;
        var hrc = U32(frameBytes, 0xB4);
        if (!OwnsPolygon(hrc, polygonSet, ref hash) || !Geometry(polygonSet, gameObject, requireMoving, ref hash, out var matrices) ||
            !Gates(gameObject, requireMoving) || !memory.TryReadUInt32(0x00CBF5E8, out var scriptAfter) || scriptAfter != scriptPointer)
            return false;
        if (!ReadU32(gameObject, 0x934, out var driver) || driver == 0) return false;
        sample = new(context, scriptPointer, objects, renders, actor, frame, hrc, polygonSet, gameObject,
            modelId, x, y, z, ix, iy, iz, step, duration, phase, render, scriptEntry, driver, hash,
            matrices.Owner, matrices.OwnerSet, matrices.DefaultProjection, matrices.DefaultView,
            matrices.World, matrices.ActorView, matrices.EffectiveProjection);
        return true;
    }

    private bool Gates(uint gameObject, bool requireDrawScope) =>
        memory.TryReadByte(0x00CBF9DC, out var module) && module == 1 &&
        memory.TryReadUInt16(0x00CC15D0, out var field) && field == 767 &&
        memory.TryReadUInt16(0x00CC1638, out var movie) && movie == 1 &&
        memory.TryReadByte(0x00CC0B68, out var background) && background == 0 &&
        memory.TryReadInt32(0x009A0718, out var alternate) && alternate == 0 &&
        memory.TryReadByte(0x00CC0DC0, out var skip) && skip == 0 &&
        memory.TryReadInt32(0x00CFFA98, out var special) && special == 0 &&
        memory.TryReadInt32(0x00CFFEF8, out var fade) && fade == 0 &&
        memory.TryReadUInt32(0x00DB2BB8, out var owner) && owner == gameObject &&
        ReadU32(gameObject, 0x81C, out var scene) && (!requireDrawScope || scene != 0);

    private bool Context(uint script, out FieldScriptContext context, out ushort entry, ref ulong hash)
    {
        context = default; entry = 0;
        Span<byte> header = stackalloc byte[8];
        if (!Read(script, header, ref hash) || header[2] <= EntityId || header[2] > 32 ||
            !memory.TryReadByte(0x00CC0B3C, out var priority) || priority >= 8 ||
            !memory.TryReadByte(0x00CBF9E8 + EntityId * 8u + priority, out var scriptId) || scriptId >= 32 ||
            !memory.TryReadUInt16(0x00CC0CF8 + EntityId * 2u, out var pc) ||
            !Entry(script, header, scriptId, out entry) || pc < entry ||
            !Add(script, pc, out var opcodeAddress) || !memory.TryReadByte(opcodeAddress, out var opcode)) return false;
        Feed(ref hash, priority); Feed(ref hash, entry); Feed(ref hash, pc);
        context = new(767, EntityId, scriptId, pc - entry, opcode);
        return true;
    }

    private bool Entry(uint script, ReadOnlySpan<byte> header, int scriptId, out ushort entry)
    {
        entry = 0;
        var table = 0x20u + ((uint)U16(header, 6) << 2) + header[2] * 8u;
        var end = table + header[2] * 64u;
        return U16(header, 6) <= 8191 && end <= ushort.MaxValue && Add(script, table + EntityId * 64u + (uint)scriptId * 2, out var address) && Range(address, 2) &&
               memory.TryReadUInt16(address, out entry) && entry >= end;
    }

    private bool OriginalAnchor(uint script, FieldCreatureApproachSample original)
    {
        Span<byte> header = stackalloc byte[8];
        return script == original.ScriptPointer && Read(script, header) && header[2] > EntityId && header[2] <= 32 &&
               Entry(script, header, original.Context.ScriptId, out var entry) && entry == original.ScriptEntry &&
               Anchor(script, original.Context, entry);
    }

    private bool Anchor(uint script, FieldScriptContext context, ushort entry)
    {
        if (context.Opcode != 0xC4 || !((context.ScriptId == 3 && context.ByteIndex is 12 or 14) ||
                                      (context.ScriptId == 4 && context.ByteIndex == 22))) return false;
        Span<byte> bytes = stackalloc byte[15];
        var preceding = context.ByteIndex - 12;
        if (!Add(script, (uint)entry + (uint)preceding, out var start) || !Read(start, bytes[..13]) ||
            !bytes[..12].SequenceEqual(context.ScriptId == 3 ? FirstOfst : SecondOfst) || bytes[12] != 0xC4) return false;
        if (context.ScriptId == 3 && context.ByteIndex == 14)
        {
            return Add(script, entry, out var begin) && Read(begin, bytes[..2]) && bytes[0] == 0xA4 && bytes[1] == 1;
        }
        return true;
    }

    private bool OwnsPolygon(uint hrc, uint polygon, ref ulong hash)
    {
        Span<byte> header = stackalloc byte[0x18]; Span<byte> bone = stackalloc byte[0x1C];
        Span<byte> rsdSlot = stackalloc byte[8]; Span<byte> rsd = stackalloc byte[8];
        if (!Read(hrc, header, ref hash)) return false;
        var count = U32(header, 0x10); var bones = U32(header, 0x14);
        if (count == 0 || count > MaxBones || bones == 0 || !Range(bones, count * 0x1C)) return false;
        var total = 0u; var owned = false;
        for (var i = 0u; i < count; i++)
        {
            if (!Read(bones + i * 0x1C, bone, ref hash)) return false;
            var rsdCount = U32(bone, 0x10); var array = U32(bone, 0x18);
            if (rsdCount > MaxRsdTotal || total + rsdCount > MaxRsdTotal || (rsdCount != 0 && (array == 0 || !Range(array, rsdCount * 8)))) return false;
            total += rsdCount;
            for (var j = 0u; j < rsdCount; j++)
            {
                if (!Read(array + j * 8, rsdSlot, ref hash) || !Read(U32(rsdSlot, 4), rsd, ref hash)) return false;
                owned |= U32(rsd, 4) == polygon;
            }
        }
        return owned;
    }

    private bool Geometry(uint polygon, uint gameObject, bool requireDrawScope, ref ulong hash, out MatrixOwnership ownership)
    {
        ownership = default;
        Span<byte> poly = stackalloc byte[0x54]; Span<byte> set = stackalloc byte[0x20];
        Span<byte> defaultPoly = stackalloc byte[0x44]; Span<byte> defaultSet = stackalloc byte[0x20];
        Span<byte> viewportBytes = stackalloc byte[16]; Span<double> world = stackalloc double[16]; Span<double> projection = stackalloc double[16];
        if (!ReadU32(gameObject, 0x928, out var drawPass) || drawPass > 1 || (requireDrawScope && drawPass != 0) ||
            !ReadU32(gameObject, 0x2EC, out var projectionOwner) || !Read(projectionOwner, defaultPoly, ref hash) ||
            U32(defaultPoly, 0) > 1 || (requireDrawScope && U32(defaultPoly, 0) != 1) ||
            !Read(U32(defaultPoly, 0x40), defaultSet, ref hash) ||
            !Matrix(U32(defaultSet, 0x1C), stackalloc double[16], false, ref hash) ||
            (U32(defaultSet, 0x18) != 0 && !Matrix(U32(defaultSet, 0x18), stackalloc double[16], true, ref hash))) return false;
        if (!Read(polygon, poly, ref hash) || U32(poly, 0) == 0 || U32(poly, 0x14) != 0 || U32(poly, 0x38) > 1 ||
            !Read(U32(poly, 0x40), set, ref hash) || !Matrix(U32(set, 0x14), world, false, ref hash) ||
            !Matrix(U32(set, 0x1C) != 0 ? U32(set, 0x1C) : U32(defaultSet, 0x1C), projection, false, ref hash) ||
            (U32(set, 0x18) != 0 && !Matrix(U32(set, 0x18), stackalloc double[16], true, ref hash)) ||
            !Add(gameObject, 0x848, out var viewportAddress) || !Read(viewportAddress, viewportBytes, ref hash)) return false;
        // Native 674701 allocates world only. BeginScene's default polygon owns
        // the inherited P; EndScene disables that default draw and closes the
        // scope without retiring P or the already presented actor geometry.
        ownership = new(projectionOwner, U32(defaultPoly, 0x40), U32(defaultSet, 0x1C), U32(defaultSet, 0x18),
            U32(set, 0x14), U32(set, 0x18), U32(set, 0x1C) != 0 ? U32(set, 0x1C) : U32(defaultSet, 0x1C));
        var width = I32(viewportBytes, 8); var height = I32(viewportBytes, 12);
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return false;
        var groups = U32(poly, 0x10); var primitives = U32(poly, 0x50);
        var perGroup = U32(poly, 0x38) == 1; var hundreds = U32(poly, perGroup ? 0x3C : 0x34);
        if (groups == 0 || groups > MaxGroups || primitives == 0 || hundreds == 0 ||
            (!perGroup && U32(poly, 0x2C) == 0) || !Range(primitives, groups * 4) ||
            !Range(hundreds, groups * (perGroup ? 4u : 0x64u))) return false;
        var totalVertices = 0u; var totalIndices = 0u; var positive = false; var negative = false;
        Span<byte> hundred = stackalloc byte[0x64]; Span<byte> primitive = stackalloc byte[0x20];
        for (var group = 0u; group < groups; group++)
        {
            if (!memory.TryReadUInt32(primitives + group * 4, out var primitiveAddress)) return false;
            uint hundredAddress;
            if (perGroup) { if (!memory.TryReadUInt32(hundreds + group * 4, out hundredAddress)) return false; }
            else hundredAddress = hundreds + group * 0x64;
            if (!Read(hundredAddress, hundred, ref hash) || U32(hundred, 8) != 0x38200 || U32(hundred, 0xC) != 0x20002 ||
                U32(hundred, 0x14) != 0 || U32(hundred, 0x44) != 4 || U32(hundred, 0x48) != 0 || U32(hundred, 0x5C) != 255 ||
                !Read(primitiveAddress, primitive, ref hash) || U32(primitive, 4) != 32 || U32(primitive, 8) != 4 ||
                U32(primitive, 0xC) is not (1 or 2)) return false;
            var nVertices = U32(primitive, 0x14); var nIndices = U32(primitive, 0x1C);
            totalVertices += nVertices; totalIndices += nIndices;
            if (nVertices < 3 || nVertices > MaxVertices || nIndices < 3 || nIndices > MaxIndices || nIndices % 3 != 0 ||
                totalVertices > MaxVerticesTotal || totalIndices > MaxIndicesTotal) return false;
            var vertexBytes = ArrayPool<byte>.Shared.Rent((int)nVertices * 32); var indexBytes = ArrayPool<byte>.Shared.Rent((int)nIndices * 2);
            var transformed = ArrayPool<ClipPoint>.Shared.Rent((int)nVertices);
            try
            {
                var vertices = vertexBytes.AsSpan(0, (int)nVertices * 32); var indices = indexBytes.AsSpan(0, (int)nIndices * 2);
                if (!Read(U32(primitive, 0x10), vertices, ref hash) || !Read(U32(primitive, 0x18), indices, ref hash)) return false;
                var vertexType = U32(primitive, 0xC);
                for (var i = 0; i < nVertices; i++)
                {
                    var vertex = vertices.Slice(i * 32, 32);
                    var vx = Single(vertex, 0); var vy = Single(vertex, 4); var vz = Single(vertex, 8);
                    if (!float.IsFinite(vx) || !float.IsFinite(vy) || !float.IsFinite(vz) ||
                        !float.IsFinite(Single(vertex, 24)) || !float.IsFinite(Single(vertex, 28)) ||
                        (vertexType == 2 && vertex[0x13] != 255) ||
                        (vertexType == 1 && (!float.IsFinite(Single(vertex, 12)) || !float.IsFinite(Single(vertex, 16)) || !float.IsFinite(Single(vertex, 20))))) return false;
                    transformed[i] = Transform(Transform(new(vx, vy, vz, 1), world), projection);
                    if (!transformed[i].Finite) return false;
                }
                for (var i = 0; i < nIndices; i += 3)
                {
                    var a = U16(indices, i * 2); var b = U16(indices, (i + 1) * 2); var c = U16(indices, (i + 2) * 2);
                    if (a >= nVertices || b >= nVertices || c >= nVertices) return false;
                    var area = ClippedArea(transformed[a], transformed[b], transformed[c], width, height);
                    if (!double.IsFinite(area)) return false;
                    positive |= area > 0.000001; negative |= area < -0.000001;
                }
            }
            finally { ArrayPool<byte>.Shared.Return(vertexBytes); ArrayPool<byte>.Shared.Return(indexBytes); ArrayPool<ClipPoint>.Shared.Return(transformed); }
        }
        return positive && negative;
    }

    private bool Matrix(uint address, Span<double> values, bool identity, ref ulong hash)
    {
        Span<byte> bytes = stackalloc byte[64];
        if (!Read(address, bytes, ref hash)) return false;
        for (var i = 0; i < 16; i++)
        { var value = Single(bytes, i * 4); if (!float.IsFinite(value) || (identity && value != (i % 5 == 0 ? 1 : 0))) return false; values[i] = value; }
        return true;
    }

    private static ClipPoint Transform(ClipPoint p, ReadOnlySpan<double> m) => new(
        p.X * m[0] + p.Y * m[4] + p.Z * m[8] + p.W * m[12],
        p.X * m[1] + p.Y * m[5] + p.Z * m[9] + p.W * m[13],
        p.X * m[2] + p.Y * m[6] + p.Z * m[10] + p.W * m[14],
        p.X * m[3] + p.Y * m[7] + p.Z * m[11] + p.W * m[15]);

    private static double ClippedArea(ClipPoint a, ClipPoint b, ClipPoint c, int width, int height)
    {
        Span<ClipPoint> input = stackalloc ClipPoint[16]; Span<ClipPoint> output = stackalloc ClipPoint[16];
        input[0] = a; input[1] = b; input[2] = c; var count = 3;
        for (var plane = 0; plane < 7 && count != 0; plane++)
        {
            var n = 0; var previous = input[count - 1]; var previousDistance = Distance(previous, plane);
            for (var i = 0; i < count; i++)
            {
                var current = input[i]; var distance = Distance(current, plane);
                if ((distance >= 0) != (previousDistance >= 0))
                {
                    var t = previousDistance / (previousDistance - distance);
                    if (n >= output.Length) return double.NaN;
                    output[n++] = ClipPoint.Lerp(previous, current, t);
                }
                if (distance >= 0) { if (n >= output.Length) return double.NaN; output[n++] = current; }
                previous = current; previousDistance = distance;
            }
            output[..n].CopyTo(input); count = n;
        }
        if (count < 3) return 0;
        var area = 0.0;
        for (var i = 0; i < count; i++)
        {
            var p = input[i]; var q = input[(i + 1) % count];
            if (!p.Finite || !q.Finite || p.W <= 0 || q.W <= 0) return double.NaN;
            // Viewport origin cancels from signed area; actual native dimensions
            // determine degeneracy without a broad projected bounding box.
            var px = (p.X / p.W + 1) * width / 2; var py = (1 - p.Y / p.W) * height / 2;
            var qx = (q.X / q.W + 1) * width / 2; var qy = (1 - q.Y / q.W) * height / 2;
            area += px * qy - qx * py;
        }
        return area / 2;
    }

    private static double Distance(ClipPoint p, int plane) => plane switch
    { 0 => p.X + p.W, 1 => p.W - p.X, 2 => p.Y + p.W, 3 => p.W - p.Y, 4 => p.Z, 5 => p.W - p.Z, _ => p.W - 0.000001 };
    private readonly record struct ClipPoint(double X, double Y, double Z, double W)
    {
        public bool Finite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) && double.IsFinite(W);
        public static ClipPoint Lerp(ClipPoint a, ClipPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t);
    }
    private readonly record struct MatrixOwnership(uint Owner, uint OwnerSet, uint DefaultProjection, uint DefaultView, uint World, uint ActorView, uint EffectiveProjection);
    private bool Read(uint address, Span<byte> bytes) => address != 0 && Range(address, (uint)bytes.Length) && memory.TryRead(address, bytes);
    private bool Read(uint address, Span<byte> bytes, ref ulong hash)
    {
        if (!Read(address, bytes)) return false;
        hash = unchecked((hash ^ address) * 1099511628211UL);
        foreach (var value in bytes) hash = unchecked((hash ^ value) * 1099511628211UL);
        return true;
    }
    private bool ReadU32(uint pointer, uint offset, out uint value)
    { value = 0; return Add(pointer, offset, out var address) && Range(address, 4) && memory.TryReadUInt32(address, out value); }
    private static void Feed(ref ulong hash, uint value) => hash = unchecked((hash ^ value) * 1099511628211UL);
    private static bool Add(uint address, uint offset, out uint result)
    { var sum = (ulong)address + offset; result = sum <= uint.MaxValue ? (uint)sum : 0; return address != 0 && result != 0; }
    private static bool Range(uint address, uint length) => address != 0 && length != 0 && (ulong)address + length - 1 <= uint.MaxValue;
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static int I32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    private static short I16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static float Single(ReadOnlySpan<byte> bytes, int offset) => BitConverter.Int32BitsToSingle(I32(bytes, offset));
}

public readonly record struct FieldCreatureApproachSample(
    FieldScriptContext Context, uint ScriptPointer, uint ObjectTable, uint RenderTable,
    uint ObjectBase, uint RenderFrame, uint HrcModel, uint PolygonSet, uint GameObject,
    byte ModelId, int CurrentX, int CurrentY, int CurrentZ, int InitialX, int InitialY, int InitialZ,
    ushort Step, ushort Duration, byte MovementPhase, uint RenderModel = 0,
    ushort ScriptEntry = 0, uint GraphicsDriver = 0, ulong GeometryFingerprint = 0,
    uint ProjectionOwner = 0, uint ProjectionOwnerMatrixSet = 0, uint DefaultProjection = 0,
    uint DefaultView = 0, uint WorldMatrix = 0, uint ActorView = 0, uint EffectiveProjection = 0);
