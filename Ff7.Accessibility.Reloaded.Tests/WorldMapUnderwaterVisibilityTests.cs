using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// What a sighted player can see of an underwater object, from the game's own renderer: drawn
/// this frame, not faded into the fog, projected inside the viewport, and not behind the seabed.
/// The scenes are built in guest memory the way the world map leaves it after a frame.
/// </summary>
internal static class WorldMapUnderwaterVisibilityTests
{
    private const uint EmeraldPointer = 0x01000100;
    private const uint GelnikaPointer = 0x01000200;
    private const uint PlayerPointer = 0x01000300;
    private const uint ReactorPointer = 0x01000400;
    private const int RefX = 150 * 1024;
    private const int RefZ = 150 * 1024;
    private const int BaseY = -3000;
    private const int CameraDistance = 4000;

    internal static void Run()
    {
        List<Exception> failures = [];
        foreach (var test in new Action[]
        {
            AnEmeraldInFrontOfTheCameraAboveOpenSeabedIsVisible,
            TheModelRecordIsFoundThroughTheNativeSlotTable,
            ADrawFlagOnAnotherModelsRecordDoesNotMakeEmeraldVisible,
            HiddenOrUndrawnOrUnloadedModelsAreNotVisible,
            AModelFadedIntoTheUnderseaFogIsNotVisible,
            AModelInsideTheFadeButInsideTheCurvatureRangeIsNotClaimed,
            AModelOutsideTheViewportOrBehindTheCameraIsNotVisible,
            ARidgeBetweenTheCameraAndEmeraldHidesIt,
            ARidgeBehindEmeraldDoesNotHideIt,
            TheCameraOriginFollowsTheNativeCameraRotation,
            MissingSeabedUnderTheRayFailsClosed,
            ARecordFromAnotherFrameOrEntityIsNotTrusted,
            TornReadsAreNeverUsable,
            ThePlayerAndUnlistedModelsAreNeverReported,
            OnlyTheUnderwaterMapIsRead,
            AThinRidgeAnywhereOnTheSightLineHidesEmerald,
            ARidgeJustShortOfEmeraldHidesIt,
            ANarrowGapInTheSeabedFailsClosed,
            ASightLineAcrossTheWorldWrapIsTested,
            ASeabedRestingObjectIsNotHiddenByItsOwnSeabed,
            ASkewedOrMirroredCameraIsNeverUsable,
            AStaleCallerPositionIsNotLabelledVisible,
            AVerticalTerrainFaceBetweenTheCameraAndEmeraldHidesIt
        })
        {
            try { test(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        if (failures.Count != 0) throw new AggregateException(failures);
        Console.WriteLine("world-map underwater visibility tests passed (23 scenarios).");
    }

    private static void AnEmeraldInFrontOfTheCameraAboveOpenSeabedIsVisible()
    {
        var scene = Scene.Standard();
        var result = scene.Read();
        Check(result.IsUsable, "a coherent frame is usable: " + result.Diagnostic);
        Check(result.VisibleEntities.Contains(EmeraldPointer), "Emerald drawn ahead over open water is visible: " + result.Diagnostic);
        Check(!result.VisibleEntities.Contains(PlayerPointer), "the player is never reported");
    }

    private static void AVerticalTerrainFaceBetweenTheCameraAndEmeraldHidesIt()
    {
        foreach (var offset in new[] { 2000, 9000 })
        {
            var scene = Scene.Standard();
            var triangles = scene.Map.Triangles.ToList();
            var a = new WorldMapVertex(RefX - 1000, -5000, RefZ + offset);
            var b = new WorldMapVertex(RefX + 1000, -5000, RefZ + offset);
            var c = new WorldMapVertex(RefX + 1000, -2000, RefZ + offset);
            var d = new WorldMapVertex(RefX - 1000, -2000, RefZ + offset);
            var mx = RefX / 8192; var mz = (RefZ + offset) / 8192;
            triangles.Add(new(triangles.Count, 0, mx, mz, 0, 0, a, b, c, 3, 0, 0, 0, false, []));
            triangles.Add(new(triangles.Count, 0, mx, mz, 0, 0, a, c, d, 3, 0, 0, 0, false, []));
            scene.Map = new(2, 0, 9, 7, 12, triangles, [], "vertical terrain face");
            Check(scene.Read().VisibleEntities.Contains(EmeraldPointer) == (offset > 4000),
                "a vertical terrain face only hides Emerald when between it and the camera");
        }
    }

    private static void TheModelRecordIsFoundThroughTheNativeSlotTable()
    {
        // The underwater model list puts Emerald in slot 7; another load may put it elsewhere.
        var scene = Scene.Standard(emeraldSlot: 3);
        Check(scene.Read().VisibleEntities.Contains(EmeraldPointer), "Emerald's record follows its slot");
        scene = Scene.Standard();
        scene.Memory.PutByte(0x00E3B0F8 + 30, 0xFF);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "an unloaded model (slot -1) has no record");
        scene = Scene.Standard();
        scene.Memory.PutInt(0x00E3A8A8, 0);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "the model list not ready means nothing is drawn");
    }

    private static void ADrawFlagOnAnotherModelsRecordDoesNotMakeEmeraldVisible()
    {
        var scene = Scene.Standard();
        var emeraldRecord = Scene.Record(7);
        scene.Memory.PutInt(emeraldRecord + 0x24, 0);
        scene.Memory.PutInt(Scene.Record(1) + 0x24, 1);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "Emerald's own record must be drawn this frame");
    }

    private static void HiddenOrUndrawnOrUnloadedModelsAreNotVisible()
    {
        foreach (var (flags, why) in new[] { ((byte)0x00, "its terrain chunk was not drawn"), ((byte)0x0A, "it is hidden") })
        {
            var scene = Scene.Standard();
            scene.Memory.PutByte(EmeraldPointer + 0x51, flags);
            Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "not visible when " + why);
        }
    }

    private static void AModelFadedIntoTheUnderseaFogIsNotVisible()
    {
        // A wide curvature-free range isolates the fade: 255 - ((z/4 - 4000) >> 3) reaches 0 at 24160.
        var faded = Scene.Standard(depth: 24200, curvatureStart: 9000);
        Check(!faded.Read().VisibleEntities.Contains(EmeraldPointer), "fully faded into the fog");
        var faint = Scene.Standard(depth: 23000, curvatureStart: 9000);
        Check(faint.Read().VisibleEntities.Contains(EmeraldPointer), "faint but still drawn: " + faint.Read().Diagnostic);
    }

    private static void AModelInsideTheFadeButInsideTheCurvatureRangeIsNotClaimed()
    {
        // With the native 5000 the round-earth drop starts near depth 20000; nothing beyond is claimed.
        var scene = Scene.Standard(depth: 21000);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "no claim where the native drop changes the picture");
    }

    private static void AModelOutsideTheViewportOrBehindTheCameraIsNotVisible()
    {
        var aside = Scene.Standard(sideways: 9000);
        Check(!aside.Read().VisibleEntities.Contains(EmeraldPointer), "projected beyond the viewport edge");
        var behind = Scene.Standard(depth: -6000);
        Check(!behind.Read().VisibleEntities.Contains(EmeraldPointer), "behind the camera");
    }

    private static void ARidgeBetweenTheCameraAndEmeraldHidesIt()
    {
        var scene = Scene.Standard(ridgeAhead: 3000);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "a ridge on the line of sight hides Emerald");
    }

    private static void ARidgeBehindEmeraldDoesNotHideIt()
    {
        var scene = Scene.Standard(ridgeAhead: 9000);
        Check(scene.Read().VisibleEntities.Contains(EmeraldPointer), "terrain beyond Emerald does not hide it: " + scene.Read().Diagnostic);
    }

    private static void TheCameraOriginFollowsTheNativeCameraRotation()
    {
        // Turned a quarter: the camera now sits to the side of the reference, not behind it, so
        // a ridge on the old unrotated sight line no longer lies between the camera and Emerald.
        var turned = Scene.Standard(quarterTurn: true, ridgeAhead: 3000);
        Check(turned.Read().VisibleEntities.Contains(EmeraldPointer), "the rotated camera sees past the unrotated ridge: " + turned.Read().Diagnostic);
        var blocked = Scene.Standard(quarterTurn: true, ridgeAside: 3000);
        Check(!blocked.Read().VisibleEntities.Contains(EmeraldPointer), "a ridge on the rotated sight line hides Emerald");
    }

    private static void MissingSeabedUnderTheRayFailsClosed()
    {
        var scene = Scene.Standard(holeAhead: 3000);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "an unknown seabed is never assumed clear");
    }

    private static void ARecordFromAnotherFrameOrEntityIsNotTrusted()
    {
        var scene = Scene.Standard();
        scene.Memory.PutFloat(Scene.Record(7) + 0x48, 700f);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "a drawn position that is not this entity under this camera is rejected");
    }

    private static void TornReadsAreNeverUsable()
    {
        foreach (var (address, label) in new (uint, string)[]
        {
            (0x00E3B0F8 + 30, "model slot header"),
            (0x00E3A8A8, "model list readiness"),
            (0x00DFC448, "camera rotation"),
            (Scene.Record(7) + 0x50, "model render record"),
            (EmeraldPointer + 0x0C, "entity world position"),
            (0x00E39A00, "entity list identity"),
            (Scene.Context + 0x8D0, "projection")
        })
        {
            var scene = Scene.Standard();
            scene.Memory.ChangeEveryRead(address);
            var result = scene.Read();
            Check(!result.IsUsable && result.VisibleEntities.Count == 0, "a changing " + label + " is never usable");
        }
    }

    private static void ThePlayerAndUnlistedModelsAreNeverReported()
    {
        var scene = Scene.Standard();
        var result = scene.Read();
        Check(!result.VisibleEntities.Contains(ReactorPointer), "model 18 is not one of the reported destinations");
        Check(!result.VisibleEntities.Contains(PlayerPointer), "the submarine itself is not reported");
    }

    private static void OnlyTheUnderwaterMapIsRead()
    {
        var scene = Scene.Standard();
        scene.Memory.PutInt(0x00E045E8, 0);
        var result = scene.Read(stateMapType: 0);
        Check(!result.IsUsable && result.VisibleEntities.Count == 0, "the overworld is not this reader's map");
    }

    private static void AThinRidgeAnywhereOnTheSightLineHidesEmerald()
    {
        // Twenty units wide, placed between where any fixed sampling step would look.
        foreach (var ahead in new[] { 3055, 1013, -2977 })
        {
            var scene = Scene.Standard(ridgeAhead: ahead, ridgeHalfWidth: 10);
            Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), $"a 20-unit ridge {ahead} ahead hides Emerald");
        }
    }

    private static void ARidgeJustShortOfEmeraldHidesIt()
    {
        // Emerald floats; nothing near it is its own footprint.
        var scene = Scene.Standard(ridgeAhead: 3900, ridgeHalfWidth: 20);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "a ridge 100 units before Emerald hides it");
    }

    private static void ANarrowGapInTheSeabedFailsClosed()
    {
        var scene = Scene.Standard(holeAhead: 3055, holeHalfWidth: 10);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "a 20-unit stretch of unknown seabed is never assumed clear");
    }

    private static void ASightLineAcrossTheWorldWrapIsTested()
    {
        var clear = Scene.Standard(nearWorldEdge: true);
        Check(clear.Read().VisibleEntities.Contains(EmeraldPointer), "open seabed across the wrap: " + clear.Read().Diagnostic);
        var blocked = Scene.Standard(nearWorldEdge: true, ridgeAhead: 3055, ridgeHalfWidth: 10);
        Check(!blocked.Read().VisibleEntities.Contains(EmeraldPointer), "a ridge on the far side of the wrap hides Emerald");
    }

    private static void ASeabedRestingObjectIsNotHiddenByItsOwnSeabed()
    {
        var gelnika = Scene.Standard(objectModel: 17, objectY: -5000);
        Check(gelnika.Read().VisibleEntities.Contains(EmeraldPointer), "the Gelnika on open seabed is visible: " + gelnika.Read().Diagnostic);
        var blocked = Scene.Standard(objectModel: 17, objectY: -5000, ridgeAhead: 3055, ridgeHalfWidth: 10);
        Check(!blocked.Read().VisibleEntities.Contains(EmeraldPointer), "a ridge well before its footprint still hides it");
    }

    private static void ASkewedOrMirroredCameraIsNeverUsable()
    {
        foreach (var (rotation, what) in new (short[], string)[]
        {
            ([4096, 0, 0, 2896, 2896, 0, 0, 0, 4096], "skewed"),
            // Unit rows and a determinant within rounding of 1, but rows not perpendicular.
            ([4096, 0, 0, 1229, 3907, 0, 0, 0, 4096], "mildly skewed"),
            ([-4096, 0, 0, 0, 4096, 0, 0, 0, 4096], "mirrored")
        })
        {
            var scene = Scene.Standard();
            scene.PutRotation(rotation);
            var result = scene.Read();
            Check(!result.IsUsable && result.VisibleEntities.Count == 0, "a " + what + " camera matrix is never usable");
        }
    }

    private static void AStaleCallerPositionIsNotLabelledVisible()
    {
        var scene = Scene.Standard();
        scene.MakeCallerPositionStale(byX: 100);
        Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer), "a position the caller holds from another frame is not labelled visible");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Underwater visibility: " + message);
    }

    /// <summary>A world-map frame in guest memory, as the native draw leaves it.</summary>
    private sealed class Scene
    {
        internal const uint Context = 0x05000000;
        internal const int WrapZ = 0x38000;
        internal readonly ByteMemory Memory = new();
        internal readonly List<WorldMapEntitySnapshot> Entities = [];
        internal WorldMapData Map = null!;
        internal int ReferenceZ = RefZ;

        internal static uint Record(int slot) => (uint)(0x00E2C808 + (slot - 1) * 0xB8);

        /// <param name="depth">The observed object's camera-space depth.</param>
        /// <param name="ridgeAhead">A ridge across the sight line, this far ahead of the reference.</param>
        /// <param name="holeAhead">A gap in the seabed across the sight line, this far ahead.</param>
        /// <param name="nearWorldEdge">Puts the reference just short of the world's Z wrap.</param>
        internal static Scene Standard(
            int depth = 8000,
            int sideways = 0,
            int emeraldSlot = 7,
            int curvatureStart = 5000,
            bool quarterTurn = false,
            int ridgeAhead = 0,
            int ridgeHalfWidth = 200,
            int ridgeAside = 0,
            int holeAhead = 0,
            int holeHalfWidth = 200,
            bool nearWorldEdge = false,
            int objectModel = 30,
            int objectY = -4250)
        {
            var scene = new Scene();
            if (nearWorldEdge) scene.ReferenceZ = WrapZ - 1500;
            var refZ = scene.ReferenceZ;
            var m = scene.Memory;
            m.PutByte((uint)WorldMapStateReader.AddressCurrentModule, 3);
            m.PutInt(0x00E045E8, 2);
            m.PutInt(0x00E3A8A8, 1);
            for (uint model = 0; model < 0x2B; model++) m.PutByte(0x00E3B0F8 + model, 0xFF);
            m.PutByte(0x00E3B0F8 + 13, 1);
            m.PutByte(0x00E3B0F8 + 18, 3);
            m.PutByte(0x00E3B0F8 + (uint)objectModel, (byte)emeraldSlot);
            if (objectModel != 17) m.PutByte(0x00E3B0F8 + 17, 2);
            m.PutInt(0x00E045D8, curvatureStart);
            m.PutInt(0x00DE6A04, BaseY);
            m.PutInt(0x00E04918, RefX);
            m.PutInt(0x00E04918 + 4, BaseY);
            m.PutInt(0x00E04918 + 8, refZ);

            // Camera: rotation about the vertical axis only, translation (0, 0, distance), as
            // FUN_0074E8CE builds D_00DFC448 and D_00DE6A20 when the camera is level.
            short[] rotation = quarterTurn
                ? [0, 0, 4096, 0, 4096, 0, -4096, 0, 0]
                : [4096, 0, 0, 0, 4096, 0, 0, 0, 4096];
            scene.PutRotation(rotation);
            m.PutInt(0x00DE6A20 + 0x14, 0);
            m.PutInt(0x00DE6A20 + 0x18, 0);
            m.PutInt(0x00DE6A20 + 0x1C, CameraDistance);

            // Renderer context: a pinhole whose screen point is (160 + 160x/z, 120 + 160y/z).
            m.PutInt(0x00DB2BB8, unchecked((int)Context));
            m.PutInt(Context + 0x848, 0);
            m.PutInt(Context + 0x84C, 0);
            m.PutInt(Context + 0x850, 320);
            m.PutInt(Context + 0x854, 240);
            float[] projection = [160, 0, 160, 0, 0, 160, 120, 0, 0, 0, 1, 0, 0, 0, 1, 0];
            for (uint i = 0; i < 16; i++) m.PutFloat(Context + 0x8D0 + i * 4, projection[i]);

            // The observed object, in camera space at (sideways, objectY - base, depth): world
            // placement follows from the camera, so turning the camera turns where it stands.
            var camX = sideways;
            var camY = objectY - BaseY;
            var camZ = depth;
            var (relX, relZ) = quarterTurn
                ? (-(camZ - CameraDistance), camX)
                : (camX, camZ - CameraDistance);
            var objectX = RefX + relX;
            var objectZ = ((refZ + relZ) % WrapZ + WrapZ) % WrapZ;

            scene.AddEntity(EmeraldPointer, objectModel, 0x02, objectX, objectY, objectZ);
            if (objectModel != 17) scene.AddEntity(GelnikaPointer, 17, 0x00, RefX + 40000, -4365, (refZ + 40000) % WrapZ);
            scene.AddEntity(ReactorPointer, 18, 0x02, RefX - 2000, -5120, (refZ + 3000) % WrapZ);
            scene.AddEntity(PlayerPointer, 13, 0x82, RefX, BaseY, refZ);
            m.PutInt(0x00E3A7D0, unchecked((int)PlayerPointer));

            var record = Record(emeraldSlot);
            m.PutInt(record + 0x24, 1);
            m.PutFloat(record + 0x48, camX);
            m.PutFloat(record + 0x4C, camY);
            m.PutFloat(record + 0x50, camZ);
            m.PutInt(Record(3) + 0x24, 1);

            // Seabed: flat at -5000 everywhere, with at most one exact ridge or gap.
            var builder = new SeabedBuilder(refZ);
            if (ridgeAhead != 0) builder.Cut(builder.Band(quarterTurn ? -ridgeAhead : 0, ridgeAhead, quarterTurn, false, ridgeHalfWidth), -2000);
            if (ridgeAside != 0) builder.Cut(builder.Band(-ridgeAside, 0, true, true, ridgeHalfWidth), -2000);
            if (holeAhead != 0) builder.Cut(builder.Band(0, holeAhead, false, false, holeHalfWidth), null);
            scene.Map = builder.Build();
            return scene;
        }

        internal void PutRotation(short[] rotation)
        {
            for (uint i = 0; i < 9; i++) Memory.PutShort(0x00DFC448 + i * 2, rotation[i]);
        }

        /// <summary>Gives the caller's list an older position for the observed object.</summary>
        internal void MakeCallerPositionStale(int byX)
        {
            var index = Entities.FindIndex(entity => entity.GuestPointer == EmeraldPointer);
            Entities[index] = Entities[index] with { X = Entities[index].X + byX };
        }

        private void AddEntity(uint pointer, int model, byte flags, int x, int y, int z)
        {
            var previousHead = Entities.Count == 0 ? 0u : Entities[^1].GuestPointer;
            Memory.PutInt(pointer, unchecked((int)previousHead));
            Memory.PutInt(pointer + 0x0C, x);
            Memory.PutInt(pointer + 0x10, y);
            Memory.PutInt(pointer + 0x14, z);
            Memory.PutShort(pointer + 0x44, 0);
            Memory.PutByte(pointer + 0x50, (byte)model);
            Memory.PutByte(pointer + 0x51, flags);
            Memory.PutInt(0x00E39A00, unchecked((int)pointer));
            Entities.Add(new WorldMapEntitySnapshot(pointer, previousHead, pointer == PlayerPointer, x, y, z, 3, 0, model, flags));
        }

        internal WorldMapUnderwaterVisibilityReadResult Read(int stateMapType = 2)
        {
            var reader = new WorldMapUnderwaterVisibilityReader(Memory, Map);
            var state = new WorldMapStateSnapshot(3, stateMapType, 0, 0, RefX, BaseY, ReferenceZ, 0, 0, 3, 0, 13, 0, 0, default);
            var list = Entities.AsEnumerable().Reverse().ToList();
            return reader.Read(state, list);
        }
    }

    /// <summary>
    /// Two triangles per 8192 mesh cell over the whole 9x7 underwater frame. A feature cuts its
    /// cells exactly along its own edges, so a ridge or gap can be any width at all.
    /// </summary>
    private sealed class SeabedBuilder(int referenceZ)
    {
        private const int Mesh = 0x2000;
        private readonly Dictionary<(int, int), List<(int X0, int Z0, int X1, int Z1, int? Y)>> cells = Flat();

        private static Dictionary<(int, int), List<(int, int, int, int, int?)>> Flat()
        {
            var flat = new Dictionary<(int, int), List<(int, int, int, int, int?)>>();
            for (var mz = 0; mz < 28; mz++)
                for (var mx = 0; mx < 36; mx++)
                    flat[(mx, mz)] = [(mx * Mesh, mz * Mesh, (mx + 1) * Mesh, (mz + 1) * Mesh, -5000)];
            return flat;
        }

        /// <summary>A band across (or along) the sight line, centred at an offset from the reference.</summary>
        internal (int X0, int Z0, int X1, int Z1) Band(int offsetX, int offsetZ, bool turned, bool alongSight, int halfWidth)
        {
            var cx = RefX + offsetX;
            var cz = ((referenceZ + offsetZ) % Scene.WrapZ + Scene.WrapZ) % Scene.WrapZ;
            var acrossX = turned ? (alongSight ? halfWidth : 4000) : 4000;
            var acrossZ = turned ? (alongSight ? 4000 : halfWidth) : halfWidth;
            return (cx - acrossX, cz - acrossZ, cx + acrossX, cz + acrossZ);
        }

        /// <summary>Re-tiles every cell the band touches along the band's exact edges.</summary>
        internal void Cut((int X0, int Z0, int X1, int Z1) band, int? height)
        {
            for (var mz = band.Z0 / Mesh; mz <= (band.Z1 - 1) / Mesh; mz++)
            {
                for (var mx = band.X0 / Mesh; mx <= (band.X1 - 1) / Mesh; mx++)
                {
                    int x0 = mx * Mesh, z0 = mz * Mesh, x1 = x0 + Mesh, z1 = z0 + Mesh;
                    int[] xs = [x0, Math.Clamp(band.X0, x0, x1), Math.Clamp(band.X1, x0, x1), x1];
                    int[] zs = [z0, Math.Clamp(band.Z0, z0, z1), Math.Clamp(band.Z1, z0, z1), z1];
                    var parts = new List<(int, int, int, int, int?)>();
                    for (var j = 0; j < 3; j++)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            if (xs[i + 1] <= xs[i] || zs[j + 1] <= zs[j]) continue;
                            var inside = xs[i] >= band.X0 && xs[i + 1] <= band.X1 && zs[j] >= band.Z0 && zs[j + 1] <= band.Z1;
                            parts.Add((xs[i], zs[j], xs[i + 1], zs[j + 1], inside ? height : -5000));
                        }
                    }

                    cells[(mx, mz)] = parts;
                }
            }
        }

        internal WorldMapData Build()
        {
            var triangles = new List<WorldMapTriangle>();
            foreach (var ((mx, mz), parts) in cells)
            {
                foreach (var (x0, z0, x1, z1, y) in parts)
                {
                    if (y is not { } height) continue;
                    var a = new WorldMapVertex(x0, height, z0);
                    var b = new WorldMapVertex(x1, height, z0);
                    var c = new WorldMapVertex(x1, height, z1);
                    var d = new WorldMapVertex(x0, height, z1);
                    triangles.Add(new WorldMapTriangle(triangles.Count, 0, mx, mz, 0, 0, a, b, c, 3, 0, 0, 0, false, []));
                    triangles.Add(new WorldMapTriangle(triangles.Count, 0, mx, mz, 0, 0, a, c, d, 3, 0, 0, 0, false, []));
                }
            }

            return new WorldMapData(2, 0, 9, 7, 12, triangles, [], "test seabed");
        }
    }

    /// <summary>Little-endian guest memory; an address can be made to change on every read.</summary>
    private sealed class ByteMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private uint? changing;

        internal void ChangeEveryRead(uint address) => changing = address;
        internal void PutByte(uint address, byte value) => bytes[address] = value;
        internal void PutShort(uint address, short value) => Put(address, BitConverter.GetBytes(value));
        internal void PutInt(uint address, int value) => Put(address, BitConverter.GetBytes(value));
        internal void PutFloat(uint address, float value) => Put(address, BitConverter.GetBytes(value));

        private void Put(uint address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (uint)i] = value[i];
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                destination[i] = bytes.GetValueOrDefault(virtualAddress + (uint)i);
            if (changing is { } address && address >= virtualAddress && address < virtualAddress + destination.Length)
                bytes[address] = unchecked((byte)(bytes.GetValueOrDefault(address) + 1));
            return true;
        }
    }
}
