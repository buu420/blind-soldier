using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CreatureApproachReaderTests
{
    internal static void Run()
    {
        var tests = new Action[]
        {
            ChangedNativeOffsetsAndOwnedOpaqueTrianglesProduceASample,
            FirstUpdateIsStillUnmovedAndCannotNarrate,
            EasedNativeProgressAlsoUsesActualChangedOffsets,
            StockEchoAndSecondApproachUseTheirRealHeldCursor,
            GlobalCurrentEntityDoesNotReplaceTheCreatureCursor,
            HiddenUndrawnUnloadedAndWrongActorRecordsCannotNarrate,
            MovieMasksFadesAndWrongRendererOwnerReject,
            TornStateAndGeometryAreUnavailable,
            NativeControllerGenerationCannotChangeDuringCapture,
            UnknownAndMalformedScriptIdentityReject,
            OutOfRangeStepsAndUnscheduledMovementsReject,
            OnlyTheCurrentlyOwnedPolygonSetCanProduceASample,
            PointerReplacementInvalidatesAnObservedSample,
            PointerArithmeticNeverWrapsIntoReadableMemory,
            NativeCountsBoundWorkBeforeReadingArrays,
            BothProjectedWindingsAreRequired,
            FullyClippedGeometryAndCornerOnlyBoundsReject,
            PartialHomogeneousClippingRetainsVisibleTriangles,
            NearFarAndBehindCameraPlanesReject,
            RowVectorWorldAndProjectionTransformsDetermineVisibility,
            NativeWorldOnlyHrcInheritsItsFieldOwnersProjection,
            EndSceneRetainsQueuedGeometryButCannotCreateANewDrawObservation,
            NonIdentityViewAndNonFiniteGeometryReject,
            TransparentTexturedDeferredInstancedAndUnknownVertexPathsReject,
            VertexAndLitVertexPathsBothRequireActualOpaqueGeometry,
            QueuedInformationFreshlyRevalidatesVisibilityAfterMovementEnds,
            CurrentMovingRevalidationRejectsChangedState
        };
        var failures = new List<Exception>();
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine($"PASS {test.Method.Name}"); }
            catch (Exception error) { failures.Add(new Exception(test.Method.Name, error)); Console.WriteLine($"FAIL {test.Method.Name}: {error.Message}"); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
    }

    private static void ChangedNativeOffsetsAndOwnedOpaqueTrianglesProduceASample()
    {
        var f = new Fixture(); var sample = Capture(f);
        Equal(new FieldScriptContext(767, 12, 3, 12, 0xC4), sample.Context, "actual held creature cursor");
        Equal(2505, sample.CurrentX, "actual native x offset"); Equal(-806, sample.CurrentZ, "signed int32 z offset");
        Equal((ushort)2, sample.Step, "native post-update step"); Equal((ushort)30, sample.Duration, "actual native duration");
        Equal(Fixture.ObjectTable + 8 * 0x88u, sample.ObjectBase, "entity binding identifies model8 event record");
        True(new FieldCreatureApproachReader(f).IsCurrent(sample), "same presented moving sample revalidates");
    }
    private static void FirstUpdateIsStillUnmovedAndCannotNarrate()
    { var f = new Fixture(); f.I32(Fixture.Actor + 0x40, 2574); f.I32(Fixture.Actor + 0x48, 2672); f.I32(Fixture.Actor + 0x50, -836); f.U16(Fixture.Actor + 0x5A, 1); Reject(f, "step1 has not moved from the initial pose"); }
    private static void EasedNativeProgressAlsoUsesActualChangedOffsets()
    { var f = new Fixture(); f.Byte(Fixture.Actor + 0x5C, 2); True(CanCapture(f), "native phase2 is actual eased progress"); }
    private static void StockEchoAndSecondApproachUseTheirRealHeldCursor()
    {
        var echo = new Fixture(); echo.Echo(); Equal(14, Capture(echo).Context.ByteIndex, "Echo VISI precedes OFST");
        var second = new Fixture(); second.Second(); Equal(new FieldScriptContext(767, 12, 4, 22, 0xC4), Capture(second).Context, "second held native offset movement");
    }
    private static void GlobalCurrentEntityDoesNotReplaceTheCreatureCursor()
    { var f = new Fixture(); f.Byte(0x00CC0964, 3); Equal(12, Capture(f).Context.EntityId, "draw callback can occur while global entity refers to Cloud"); }
    private static void HiddenUndrawnUnloadedAndWrongActorRecordsCannotNarrate()
    {
        MutationsReject((f => f.Byte(Fixture.Actor + 0x62, 0), "hidden"), (f => f.U32(Fixture.Model + 0x168, 0), "not prepared"),
            (f => f.U32(Fixture.Model + 0x16C, 0), "not shown to native preparer"), (f => f.U32(Fixture.Frame, 0), "frame draw disabled"),
            (f => f.Byte(0x00CBFB7C, 0xFF), "model not loaded"), (f => f.Byte(0x00CBFB7C, 7), "another model"),
            (f => f.Byte(Fixture.Actor + 0x5D, 3), "another entity owns record"), (f => f.U16(Fixture.Actor + 0x6C, 7), "different CHAR identity"),
            (f => f.U32(0x00CFFA68, 0), "native suppression"));
    }
    private static void MovieMasksFadesAndWrongRendererOwnerReject()
    {
        MutationsReject((f => f.Byte(0x00CBF9DC, 2), "wrong module"), (f => f.U16(0x00CC15D0, 768), "wrong field"),
            (f => f.U16(0x00CC1638, 0), "no native movie"), (f => f.Byte(0x00CC0B68, 1), "native field tiles drawn"),
            (f => f.I32(0x009A0718, 1), "alternate background path"), (f => f.Byte(0x00CC0DC0, 1), "content skipped"),
            (f => f.I32(0x00CFFA98, 1), "special draw"), (f => f.I32(0x00CFFEF8, 1), "fade overlay"),
            (f => f.I32(Fixture.Game + 0x81C, 0), "outside scene"), (f => f.U32(0x00DB2BB8, Fixture.Game + 4), "different renderer owner"),
            (f => f.I32(Fixture.Game + 0x850, 0), "empty viewport"));
    }
    private static void TornStateAndGeometryAreUnavailable()
    {
        foreach (var address in new uint[] { 0x00CC1638, 0x00CBF5E8, Fixture.Actor + 0x40, Fixture.Model + 0x178, Fixture.World, Fixture.Vertices })
        {
            var f = new Fixture(); var changed = false;
            f.AfterRead = (a, length) => { if (!changed && a <= address && (ulong)a + (uint)length > address) { f.Byte(address, (byte)(f.Value(address) ^ 1)); changed = true; } };
            Reject(f, $"torn memory at {address:X8}");
        }
    }
    private static void UnknownAndMalformedScriptIdentityReject()
    {
        MutationsReject((f => f.Byte(0x00CC0B3C, 8), "invalid priority"), (f => f.Byte(0x00CBFA4A, 32), "unknown script"),
            (f => f.U16(0x00CC0D10, 0x060B), "cursor before expected wait"), (f => f.Byte(Fixture.Script + 0x060C, 0), "not C4"),
            (f => f.Byte(Fixture.Script + 0x0604, 1), "wrong OFST target"), (f => f.Byte(Fixture.Script + 2, 12), "entity missing"));
    }
    private static void NativeControllerGenerationCannotChangeDuringCapture()
    {
        var priority = new Fixture(); priority.Byte(0x00CBFA4B, 3); var changed = false;
        priority.AfterRead = (a, _) => { if (a == 0x00CC0B3C && !changed) { priority.Byte(a, 3); changed = true; } };
        Reject(priority, "priority changes even if both slots happen to name script3");
        var count = new Fixture(); changed = false;
        count.AfterRead = (a, _) => { if (a == 0x00CFF73E && !changed) { count.Byte(a, 10); changed = true; } };
        Reject(count, "native model count changes during the capture");
        var header = new Fixture(); header.U16(Fixture.Script + 0x00A8 + 12 * 64 + 3 * 2, 0x0600); changed = false;
        header.AfterRead = (a, _) => { if (a == Fixture.Script && !changed) { header.Byte(Fixture.Script + 2, 17); changed = true; } };
        Reject(header, "script header generation changes even if an alternative entry table has the same cursor");
    }
    private static void OutOfRangeStepsAndUnscheduledMovementsReject()
    {
        MutationsReject((f => f.U16(Fixture.Actor + 0x58, 0), "no duration"), (f => f.U16(Fixture.Actor + 0x5A, 31), "step after duration"),
            (f => f.Byte(Fixture.Actor + 0x5C, 0), "not interpolating"), (f => f.Byte(Fixture.Actor + 0x5C, 3), "completed rather than progressing"),
            (f => f.U16(Fixture.Actor + 0x46, 495), "unexpected target"));
        var fps = new Fixture(); fps.U16(Fixture.Actor + 0x58, 120); True(CanCapture(fps), "FFNx duration scaling preserves current movement proof");
    }
    private static void OnlyTheCurrentlyOwnedPolygonSetCanProduceASample()
    { var f = new Fixture(); f.U32(Fixture.Rsd + 4, Fixture.Polygon + 4); Reject(f, "callback polygon is not owned by current HRC"); }
    private static void PointerReplacementInvalidatesAnObservedSample()
    {
        foreach (var address in new uint[] { 0x00CBF5E8, 0x00CC0B60, 0x00CFF738, Fixture.Model + 0x178, Fixture.Frame + 0xB4, Fixture.Rsd + 4 })
        { var f = new Fixture(); var sample = Capture(f); f.U32(address, 0x00F00000); True(!new FieldCreatureApproachReader(f).IsCurrent(sample, false), $"replacement at {address:X8} invalidates prior observation"); }
    }
    private static void PointerArithmeticNeverWrapsIntoReadableMemory()
    {
        MutationsReject((f => f.U32(0x00CC0B60, 0xFFFFFF00), "event table wrap"), (f => f.U32(0x00CFF738, 0xFFFFFF00), "render table wrap"),
            (f => f.U32(Fixture.Hrc + 0x14, 0xFFFFFFF0), "bone header wrap"), (f => f.U32(Fixture.Primitive + 0x10, 0xFFFFFFF0), "vertex array wrap"));
    }
    private static void NativeCountsBoundWorkBeforeReadingArrays()
    {
        MutationsReject((f => f.U16(0x00CFF73E, 0x0109), "native model count is a full WORD"),
            (f => f.U32(Fixture.Hrc + 0x10, 65), "bone cap"), (f => f.U32(Fixture.Bones + 0x10, 65), "RSD cap"),
            (f => f.U32(Fixture.Polygon + 0x10, 33), "group cap"), (f => f.U32(Fixture.Primitive + 0x14, 4097), "vertex cap"),
            (f => f.U32(Fixture.Primitive + 0x1C, 24579), "index cap"), (f => f.U32(Fixture.Primitive + 0x1C, 5), "incomplete triangle list"),
            (f => f.U16(Fixture.Indices, 3), "index outside vertex array"));
    }
    private static void BothProjectedWindingsAreRequired()
    { var f = new Fixture(); f.U16(Fixture.Indices + 6, 0); f.U16(Fixture.Indices + 8, 1); f.U16(Fixture.Indices + 10, 2); Reject(f, "one inherited cull mode could remove every triangle"); }
    private static void FullyClippedGeometryAndCornerOnlyBoundsReject()
    {
        var f = new Fixture(); f.VerticesAt((2, 0, .5f), (3, 0, .5f), (2, 1, .5f)); Reject(f, "fully offscreen");
        var corner = new Fixture(); corner.VerticesAt((.9f, 1.5f, .5f), (1.5f, .9f, .5f), (1.5f, 1.5f, .5f)); Reject(corner, "bounding box overlap is not triangle visibility");
        var flat = new Fixture(); flat.VerticesAt((-.5f, 0, .5f), (0, 0, .5f), (.5f, 0, .5f)); Reject(flat, "zero screen area");
    }
    private static void PartialHomogeneousClippingRetainsVisibleTriangles()
    { var f = new Fixture(); f.VerticesAt((-.5f, -.5f, .5f), (2, -.5f, .5f), (-.5f, 2, .5f)); True(CanCapture(f), "partially clipped native triangles remain visible"); }
    private static void NearFarAndBehindCameraPlanesReject()
    {
        foreach (var z in new[] { -.1f, 1.1f }) { var f = new Fixture(); f.VerticesAt((-.5f, -.5f, z), (.5f, -.5f, z), (0, .5f, z)); Reject(f, "Direct3D z clip plane"); }
        var behind = new Fixture(); behind.Single(Fixture.Projection + 60, -1); Reject(behind, "negative homogeneous w");
        var nearClip = new Fixture(); nearClip.VerticesAt((-.5f, -.5f, -.5f), (.5f, -.5f, .5f), (0, .5f, .5f)); True(CanCapture(nearClip), "near-plane crossing is clipped rather than rejected wholesale");
    }
    private static void RowVectorWorldAndProjectionTransformsDetermineVisibility()
    {
        var translated = new Fixture(); translated.Single(Fixture.World + 48, 4); Reject(translated, "row-vector world translation moves native polygon offscreen");
        var cancelled = new Fixture(); cancelled.Single(Fixture.World + 48, 4); cancelled.Single(Fixture.Projection + 48, -4); True(CanCapture(cancelled), "world then projection translations cancel");
        var scale = new Fixture(); scale.Single(Fixture.World, .2f); scale.Single(Fixture.World + 20, .2f); True(CanCapture(scale), "finite scaling retains actual projected area");
    }
    private static void NonIdentityViewAndNonFiniteGeometryReject()
    {
        var view = new Fixture(); view.U32(Fixture.MatrixSet + 0x18, Fixture.View); view.Identity(Fixture.View); True(CanCapture(view), "explicit identity view is known");
        view.Single(Fixture.View + 48, 1); Reject(view, "unknown nonidentity view cannot be guessed");
        MutationsReject((f => f.Single(Fixture.World, float.NaN), "nonfinite world matrix"), (f => f.Single(Fixture.Projection, float.PositiveInfinity), "nonfinite projection"),
            (f => f.Single(Fixture.Vertices, float.NaN), "nonfinite source vertex"));
    }
    private static void NativeWorldOnlyHrcInheritsItsFieldOwnersProjection()
    {
        var f = new Fixture(); f.U32(Fixture.MatrixSet + 0x1C, 0);
        True(CanCapture(f), "native 674701 world-only matrix set inherits the field owner's default projection");
        f.Single(Fixture.DefaultProjection + 48, 4); Reject(f, "actual inherited projection moves the creature out of view");
        var explicitProjection = new Fixture(); explicitProjection.Single(Fixture.DefaultProjection + 48, 4);
        True(CanCapture(explicitProjection), "a loaded per-polygon projection overrides the inherited default");
        MutationsReject((g => g.U32(Fixture.Game + 0x2EC, 0), "missing native default polygon owner"),
            (g => g.U32(Fixture.DefaultPolygon + 0x40, 0), "missing default matrix set"),
            (g => g.U32(Fixture.DefaultMatrixSet + 0x1C, 0), "missing inherited projection storage"),
            (g => g.Single(Fixture.DefaultView + 48, 1), "unknown inherited nonidentity view"));
    }
    private static void EndSceneRetainsQueuedGeometryButCannotCreateANewDrawObservation()
    {
        foreach (var inherited in new[] { false, true })
        {
            var f = new Fixture(); if (inherited) f.U32(Fixture.MatrixSet + 0x1C, 0); var sample = Capture(f);
            f.I32(Fixture.Game + 0x81C, 0); f.I32(Fixture.Game + 0x928, 1); f.U32(Fixture.DefaultPolygon, 0);
            True(new FieldCreatureApproachReader(f).IsCurrent(sample, false), "native EndScene closes the scope while the same rendered creature stays displayed");
            Reject(f, "between-frame state cannot create a native draw observation");
            f.Byte(Fixture.Actor + 0x62, 0); True(!new FieldCreatureApproachReader(f).IsCurrent(sample, false), "native hidden state still retires between-frame visibility");
        }
    }
    private static void TransparentTexturedDeferredInstancedAndUnknownVertexPathsReject()
    {
        MutationsReject((f => f.U32(Fixture.Hundred + 0x5C, 0), "transparent render state"), (f => f.Byte(Fixture.Vertices + 0x13, 0), "transparent vertex"),
            (f => f.U32(Fixture.Hundred + 0x14, 1), "unknown texture transparency"), (f => f.U32(Fixture.Hundred + 0x48, 1), "deferred zsort"),
            (f => f.U32(Fixture.Hundred + 8, 0x38201), "unknown options"), (f => f.U32(Fixture.Hundred + 0xC, 0x20003), "unknown features"),
            (f => f.U32(Fixture.Hundred + 0x44, 1), "unknown blending"), (f => f.U32(Fixture.Polygon + 0x14, 1), "instancing"),
            (f => f.U32(Fixture.Primitive + 0xC, 3), "TLVERTEX"), (f => f.U32(Fixture.Primitive + 8, 5), "not triangle list"),
            (f => f.U32(Fixture.Primitive + 4, 28), "unknown vertex size"));
    }
    private static void VertexAndLitVertexPathsBothRequireActualOpaqueGeometry()
    {
        var vertex = new Fixture(); vertex.U32(Fixture.Primitive + 0xC, 1); for (uint i = 0; i < 3; i++) { vertex.Single(Fixture.Vertices + i * 32 + 12, 0); vertex.Single(Fixture.Vertices + i * 32 + 16, 0); vertex.Single(Fixture.Vertices + i * 32 + 20, 1); }
        True(CanCapture(vertex), "native VERTEX normals do not masquerade as alpha");
        var perGroup = new Fixture(); perGroup.U32(Fixture.Polygon + 0x38, 1); perGroup.U32(Fixture.Polygon + 0x3C, Fixture.HundredArray); perGroup.U32(Fixture.HundredArray, Fixture.Hundred); True(CanCapture(perGroup), "owned per-group opaque state is read");
    }
    private static void QueuedInformationFreshlyRevalidatesVisibilityAfterMovementEnds()
    {
        var f = new Fixture(); var sample = Capture(f); f.Byte(Fixture.Actor + 0x5C, 0); f.U16(Fixture.Actor + 0x58, 0); f.U16(Fixture.Actor + 0x5A, 0); f.U16(0x00CC0D10, 0x060D); f.Byte(Fixture.Script + 0x060D, 0);
        True(new FieldCreatureApproachReader(f).IsCurrent(sample, false), "truthful queued movement can finish while the same creature stays visible");
        f.Single(Fixture.World + 48, 4); True(!new FieldCreatureApproachReader(f).IsCurrent(sample, false), "queued observation cannot reuse stale in-view geometry");
    }
    private static void CurrentMovingRevalidationRejectsChangedState()
    { var f = new Fixture(); var sample = Capture(f); f.I32(Fixture.Actor + 0x40, 2504); True(!new FieldCreatureApproachReader(f).IsCurrent(sample), "post-original movement snapshot must remain exact"); }

    private static void MutationsReject(params (Action<Fixture> change, string reason)[] mutations)
    { foreach (var (change, reason) in mutations) { var f = new Fixture(); change(f); Reject(f, reason); } }
    private static FieldCreatureApproachSample Capture(Fixture f)
    { True(new FieldCreatureApproachReader(f).TryCapture(Fixture.Polygon, Fixture.Game, out var sample), "changed visible native creature must be captured"); return sample; }
    private static bool CanCapture(Fixture f) => new FieldCreatureApproachReader(f).TryCapture(Fixture.Polygon, Fixture.Game, out _);
    private static void Reject(Fixture f, string reason) => True(!CanCapture(f), reason);
    private static void True(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Equal<T>(T expected, T actual, string reason) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}"); }

    // Literal native layouts, independent of production constants. No field scripts,
    // models or matrix helpers from the reader are used to compute expected results.
    private sealed class Fixture : ILegacyAddressSpace
    {
        public const uint Script = 0x00100000, ObjectTable = 0x00110000, RenderTable = 0x00120000;
        public const uint Actor = ObjectTable + 8 * 0x88, Model = RenderTable + 8 * 400, Frame = 0x00130000;
        public const uint Hrc = 0x00140000, Bones = 0x00150000, RsdArray = 0x00160000, Rsd = 0x00170000;
        public const uint Polygon = 0x00180000, Hundred = 0x00190000, MatrixSet = 0x001A0000;
        public const uint World = 0x001B0000, Projection = 0x001C0000, PrimitiveArray = 0x001D0000;
        public const uint Primitive = 0x001E0000, Vertices = 0x001F0000, Indices = 0x00200000;
        public const uint Game = 0x00210000, View = 0x00220000, HundredArray = 0x00230000;
        public const uint DefaultPolygon = 0x00240000, DefaultMatrixSet = 0x00250000, DefaultView = 0x00260000, DefaultProjection = 0x00270000;
        private readonly Dictionary<uint, byte[]> regions = [];
        public Action<uint, int>? AfterRead;
        public Fixture()
        {
            Region(0x009A0000, 0x1000); Region(0x00CBF000, 0x2000); Region(0x00CC0000, 0x3000); Region(0x00CFF000, 0x1000); Region(0x00DB2000, 0x1000);
            Region(Script, 0x1000); Region(ObjectTable, 0x1000); Region(RenderTable, 0x2000); Region(Frame, 0x100); Region(Hrc, 0x100);
            Region(Bones, 0x100); Region(RsdArray, 0x100); Region(Rsd, 0x100); Region(Polygon, 0x100); Region(Hundred, 0x100);
            Region(MatrixSet, 0x100); Region(World, 0x100); Region(Projection, 0x100); Region(PrimitiveArray, 0x100); Region(Primitive, 0x100);
            Region(Vertices, 0x100); Region(Indices, 0x100); Region(Game, 0x1000); Region(View, 0x100); Region(HundredArray, 0x100);
            Region(DefaultPolygon, 0x100); Region(DefaultMatrixSet, 0x100); Region(DefaultView, 0x100); Region(DefaultProjection, 0x100);
            Byte(0x00CBF9DC, 1); U16(0x00CC15D0, 767); U16(0x00CC1638, 1); U32(0x00CBF5E8, Script); Byte(Script + 2, 16); Byte(Script + 3, 9);
            Byte(0x00CC0B3C, 2); Byte(0x00CBFA4A, 3); U16(0x00CC0D10, 0x060C);
            // Header table is 0x20 +16*8 (headerWords=0), not the code's location.
            U16(Script + 0x00A0 + 12 * 64 + 3 * 2, 0x0600); U16(Script + 0x00A0 + 12 * 64 + 4 * 2, 0x0640);
            Bytes(Script + 0x0600, Convert.FromHexString("C3000001EE01CC015C001E00C400"));
            Bytes(Script + 0x0640, Convert.FromHexString("2428005401F10045023FC300000178001A0008001400C400"));
            U32(0x00CC0B60, ObjectTable); U32(0x00CFF738, RenderTable); Byte(0x00CFF73E, 9); Byte(0x00CBFB7C, 8);
            Byte(Actor + 0x5D, 12); Byte(Actor + 0x62, 1); U16(Actor + 0x6C, 8);
            I32(Actor + 0x40, 2505); I32(Actor + 0x48, 2599); I32(Actor + 0x50, -806);
            U16(Actor + 0x44, 2574); U16(Actor + 0x4C, 2672); U16(Actor + 0x54, unchecked((ushort)-836));
            U16(Actor + 0x46, 494); U16(Actor + 0x4E, 460); U16(Actor + 0x56, 92); U16(Actor + 0x58, 30); U16(Actor + 0x5A, 2); Byte(Actor + 0x5C, 1);
            U32(Model + 0x168, 1); U32(Model + 0x16C, 1); U32(Model + 0x178, Frame); U32(Frame, 1); U32(Frame + 0xB4, Hrc); U32(0x00CFFA68, 1);
            U32(Hrc + 0x10, 1); U32(Hrc + 0x14, Bones); U32(Bones + 0x10, 1); U32(Bones + 0x18, RsdArray); U32(RsdArray + 4, Rsd); U32(Rsd + 4, Polygon);
            U32(Polygon, 1); U32(Polygon + 0x10, 1); U32(Polygon + 0x2C, 1); U32(Polygon + 0x34, Hundred); U32(Polygon + 0x40, MatrixSet); U32(Polygon + 0x50, PrimitiveArray);
            U32(Hundred + 8, 0x38200); U32(Hundred + 0xC, 0x20002); U32(Hundred + 0x44, 4); U32(Hundred + 0x5C, 255);
            U32(MatrixSet + 0x14, World); U32(MatrixSet + 0x1C, Projection); Identity(World); Identity(Projection);
            U32(PrimitiveArray, Primitive); U32(Primitive + 4, 32); U32(Primitive + 8, 4); U32(Primitive + 0xC, 2); U32(Primitive + 0x10, Vertices); U32(Primitive + 0x14, 3); U32(Primitive + 0x18, Indices); U32(Primitive + 0x1C, 6);
            VerticesAt((-.5f, -.5f, .5f), (.5f, -.5f, .5f), (0, .5f, .5f));
            for (uint i = 0; i < 3; i++) U32(Vertices + i * 32 + 0x10, 0xFFFFFFFF);
            ushort[] indices = [0, 1, 2, 2, 1, 0]; for (uint i = 0; i < indices.Length; i++) U16(Indices + i * 2, indices[i]);
            U32(0x00DB2BB8, Game); I32(Game + 0x81C, 1); I32(Game + 0x850, 320); I32(Game + 0x854, 224); U32(Game + 0x934, 0x00300000);
            U32(Game + 0x2EC, DefaultPolygon); U32(DefaultPolygon, 1); U32(DefaultPolygon + 0x40, DefaultMatrixSet);
            U32(DefaultMatrixSet + 0x18, DefaultView); U32(DefaultMatrixSet + 0x1C, DefaultProjection); Identity(DefaultView); Identity(DefaultProjection);
        }
        public void Echo() { Bytes(Script + 0x0600, Convert.FromHexString("A401C3000001EE01CC015C001E00C400")); U16(0x00CC0D10, 0x060E); }
        public void Second()
        {
            Byte(0x00CBFA4A, 4); U16(0x00CC0D10, 0x0656); I32(Actor + 0x40, 476); I32(Actor + 0x48, 439); I32(Actor + 0x50, 88);
            U16(Actor + 0x44, 494); U16(Actor + 0x4C, 460); U16(Actor + 0x54, 92); U16(Actor + 0x46, 120); U16(Actor + 0x4E, 26); U16(Actor + 0x56, 8); U16(Actor + 0x58, 20);
        }
        public void VerticesAt(params (float x, float y, float z)[] values)
        { for (uint i = 0; i < values.Length; i++) { Single(Vertices + i * 32, values[i].x); Single(Vertices + i * 32 + 4, values[i].y); Single(Vertices + i * 32 + 8, values[i].z); } }
        public void Identity(uint address) { for (uint i = 0; i < 16; i++) Single(address + i * 4, i % 5 == 0 ? 1 : 0); }
        private void Region(uint address, int length) => regions.Add(address, new byte[length]);
        public byte Value(uint address) { foreach (var (start, data) in regions) if (address >= start && (ulong)address < (ulong)start + (uint)data.Length) return data[address - start]; throw new InvalidOperationException($"unmapped fixture address {address:X8}"); }
        public void Byte(uint address, byte value) { foreach (var (start, data) in regions) if (address >= start && (ulong)address < (ulong)start + (uint)data.Length) { data[address - start] = value; return; } throw new InvalidOperationException($"unmapped fixture write {address:X8}"); }
        public void Bytes(uint address, ReadOnlySpan<byte> values) { for (var i = 0; i < values.Length; i++) Byte(address + (uint)i, values[i]); }
        public void U16(uint address, ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, value); Bytes(address, b); }
        public void U32(uint address, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); Bytes(address, b); }
        public void I32(uint address, int value) => U32(address, unchecked((uint)value));
        public void Single(uint address, float value) => I32(address, BitConverter.SingleToInt32Bits(value));
        public bool TryRead(uint address, Span<byte> destination)
        {
            foreach (var (start, data) in regions)
                if (address >= start && (ulong)address + (uint)destination.Length <= (ulong)start + (uint)data.Length)
                { data.AsSpan((int)(address - start), destination.Length).CopyTo(destination); AfterRead?.Invoke(address, destination.Length); return true; }
            return false;
        }
    }
}
