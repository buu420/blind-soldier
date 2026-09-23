using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// "Wutai navigation needs work apparently you can't get to the town."
///
/// <para>From <c>ff7_accessibility_steam2026_x64 (10).log</c>, 2026-09-23. On foot, every
/// automatic walk to Wutai ended in "Could not get closer": at 11:23:19 pinned against the
/// edge of a pass at 52254,1938,156844, and at 11:23:55 pacing up and down the Wutai Bridge at
/// 39069..39623,142381..142459 while the route line stayed 600 to 790 units away. The 11:19:08
/// walk had stuck at the same pass and been taken into the story scene on it - world entry 34,
/// field 572, "Wait, wait, wait, wai----t!!" - which is the game's own behaviour, played once.</para>
///
/// <para>The rules the game walks by, from ff7_en.exe: <c>FUN_0074CECA</c> lets models 0, 1
/// and 2 onto terrain in mask 0x721B6F83, which has no bit 21 - and 172 of the map's 236
/// triangles of terrain 21 are the sides of the Wutai and Corel bridges. From a bridge, 13 or
/// 14, the mask is 0x20006000: only 13, 14 and 29.
/// <c>FUN_00751EFC</c> moves the party only where its centre and the points 200 units along
/// each axis all pass that test. Wutai's own handler, 0x96C4 at mesh (4,10) script 7, enters
/// location 23 for models 0, 1 and 2.</para>
///
/// <para>What stopped the walks, in the order a replay under those rules meets them: the
/// route ran beside the bridges on terrain 21; the pulled string hugged the edge of where
/// the party fits in the pass, and cut the corner of the mountain between; steering straight
/// at a waypoint 4,200 units off on eight directions drifted 300 units off its leg; and at
/// the western bridgehead a steep face over the flat ground hid the way on.</para>
/// </summary>
internal static class WorldMapWutaiNavigationTests
{
    private const int WalkStep = 0x1E;
    private const int WalkContactRadius = 0xC8;

    /// <summary>Where the log has the party on foot asking for Wutai, and the camera then.</summary>
    private static readonly (string When, int X, int Y, int Z, int Camera)[] LoggedStarts =
    [
        ("11:23:13 beside the pass", 52227, 1900, 156981, 0),
        ("11:23:29 before the bridges", 50628, 2677, 155252, 3888),
        ("11:19:08 on the beach", 58217, 227, 166580, 3600),
        ("09:32:39 ashore from the boat", 52938, 138, 160100, 3104),
    ];

    /// <summary>
    /// Every camera the log recorded, and 512. A quarter turn of the camera turns the eight
    /// directions onto themselves, so the walk repeats every 1024 units and these are five
    /// different walks: 0 and 512 put the directions on the axes and the diagonals, and the
    /// logged 3104, 3600 and 3888 fall between.
    /// </summary>
    private static readonly int[] Cameras = [0, 512, 3104, 3600, 3888];

    public static void RunWithInstalledGameData()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            Console.WriteLine("world Wutai navigation: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheLoggedBridgeAndPassFollowTheNativeRules(map);
        NoWalkingRouteToWutaiCrossesGroundThePartyCannotWalk(map, catalog);
        AFaceStandingOnTheBridgeheadDoesNotHideTheWayOn(map, catalog);
        ThePartyIsWalkedIntoWutaiFromEveryLoggedStart(map, catalog);
        WutaiIsEnteredOnFootOnly(map, catalog);
        Console.WriteLine("world map Wutai navigation tests passed with installed game data.");
    }

    /// <summary>
    /// The native rules checked against what the log recorded. The party paced the Wutai
    /// Bridge, 700 units of terrain 14 over and beside terrain 21, and stood on the deck at
    /// every sample; and no bridge triangle on the installed map touches any walkable
    /// terrain other than 13, 14 and 29, so leaving a bridge anywhere but a bridgehead is
    /// not possible.
    /// </summary>
    private static void TheLoggedBridgeAndPassFollowTheNativeRules(WorldMapData map)
    {
        foreach (var (x, z) in new[] { (39069, 142438), (39623, 142424), (39392, 142405) })
        {
            Equal(true, TryFindWalkSurface(map, x, z, 3170, out var surface) && surface.TerrainId == 14,
                $"the logged bridge position {x},{z} is on the Wutai Bridge deck, not the terrain 21 about it");
        }

        var sideExits = map.Triangles
            .Where(t => t.TerrainId is 13 or 14)
            .SelectMany(t => t.Neighbors.Select(n => map.Triangles[n]))
            .Count(n => n.TerrainId is not (13 or 14 or 29) && IsNativeWalkingTerrain(n.TerrainId));
        Equal(0, sideExits, "no bridge triangle borders walkable ground other than bridge and bridgehead");
        Equal(false, IsNativeWalkingTerrain(21), "terrain 21 is not in the native walking mask 0x721B6F83");
    }

    /// <summary>
    /// The route the mod plans to Wutai may only use ground the game lets the party walk on.
    /// The shipped rule counted terrain 21 as walking ground, so the route ran beside all
    /// three bridges instead of over them.
    /// </summary>
    private static void NoWalkingRouteToWutaiCrossesGroundThePartyCannotWalk(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var wutai = catalog.Locations.Single(target => target.Label == "Wutai");
        foreach (var (when, x, y, z, camera) in LoggedStarts)
        {
            var state = FootState(map, x, y, z, camera);
            Equal(true, runtime.Planner.TryBuildRoute(state, wutai, out var route),
                $"{when}: a walking route to Wutai builds: {runtime.Planner.LastDiagnostic}");
            var refused = route.TrianglePath
                .Select(id => map.Triangles[id])
                .Where(t => !IsNativeWalkingTerrain(t.TerrainId))
                .Select(t => $"{t.Id} terrain {t.TerrainId}")
                .ToArray();
            Equal(0, refused.Length,
                $"{when}: the route crosses ground the party cannot walk: {string.Join(", ", refused.Take(6))}");
        }
    }

    /// <summary>
    /// At the western bridgehead a steep face, 83096, stands on the flat bridgehead, 83163:
    /// the two hold the same point four units apart, and the party resolves to the face at
    /// every other step of the approach. Followed from the face alone, a straight line on can
    /// only leave through the face's own twin or the bridge side, so the controller was told
    /// the way on was blocked, turned back to the waypoint behind, stepped forward again
    /// and gave up there: once the pass and the bridges were walkable, seven of ten cameras
    /// replaying the 11:23:29 start stopped at this spot.
    /// </summary>
    private static void AFaceStandingOnTheBridgeheadDoesNotHideTheWayOn(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var wutai = catalog.Locations.Single(target => target.Label == "Wutai");
        var onward = new WorldMapRouteWaypoint(31115, 2622, 142502);
        foreach (var x in new[] { 32878, 32908 })
        {
            var state = FootState(map, x, 3216, 142310, 0);
            Equal(true, runtime.Planner.TryResolvePlayerTriangle(state, out var resolved),
                $"the party at {x},142310 resolves to a triangle");
            Equal(true, runtime.Planner.CanTraverseSegment(state, onward, null, wutai.NativeEntranceExemptions),
                $"from {x},142310 on triangle {resolved} the bridgehead leads on to {onward.X},{onward.Z}");
        }

        foreach (var id in new[] { 83096, 83163 })
        {
            var triangle = map.Triangles[id];
            Equal(29, triangle.TerrainId, $"triangle {id} is bridgehead");
            Equal(true, IsInsideNative(triangle, 32878, 142310), $"and holds 32878,142310");
        }
    }

    /// <summary>
    /// The whole walk, under the native rules, from every place the log had the party ask:
    /// the controller presses directions, the party moves only where its footprint fits and
    /// slides round what it touches, and the walk has to end with the game's own Wutai
    /// entry - the walkmap script turning to 7 in Wutai's mesh cell - announced.
    /// </summary>
    private static void ThePartyIsWalkedIntoWutaiFromEveryLoggedStart(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var wutai = catalog.Locations.Single(target => target.Label == "Wutai");
        foreach (var (when, x, y, z, _) in LoggedStarts)
        {
            foreach (var camera in Cameras)
            {
                var name = $"{when} camera {camera}";
                var runtime = CreateShippedRuntime(map, catalog);
                var walker = new NativeWalker(map, x, y, z, camera);
                var state = walker.Snapshot();
                var now = new DateTime(2026, 9, 23, 11, 23, 13, DateTimeKind.Utc);
                SelectWutai(runtime, catalog, state, now);
                var started = runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, now)?.Speech;
                Equal(true, runtime.Navigation.BeaconEnabled, $"{name}: navigation to Wutai starts; said \"{started}\"");

                var arrived = false;
                var spoken = started ?? string.Empty;
                var trace = new Queue<string>();
                for (var frame = 0; frame < 12000 && !arrived; frame++)
                {
                    var output = runtime.Navigation.Observe(state, now.AddMilliseconds(frame * 33), automaticWalkActive: true);
                    if (output?.Speech is { } text)
                    {
                        spoken = text;
                        if (text.Contains("Arrived at Wutai", StringComparison.Ordinal))
                        {
                            arrived = true;
                            break;
                        }

                        Equal(false, text.Contains("Could not get closer", StringComparison.Ordinal),
                            $"{name}: auto walk gave up at {state.X},{state.Y},{state.Z} terrain {state.TerrainId} " +
                            $"(\"{text}\"; {runtime.Navigation.LastDiagnostic}); last frames: {string.Join(" | ", trace)}");
                    }

                    var hasInput = runtime.Navigation.TryResolveAutomaticInput(state, out var input);
                    trace.Enqueue($"{state.X},{state.Z} t{state.TerrainId}s{state.TerrainScriptId} {(hasInput ? input : FieldNavigationInput.None)}");
                    if (trace.Count > 12)
                    {
                        trace.Dequeue();
                    }

                    walker.Frame(hasInput ? input : FieldNavigationInput.None);
                    state = walker.Snapshot();
                    Equal(true, IsNativeWalkingTerrain(state.TerrainId),
                        $"{name}: the party stands only on walkable ground, not terrain {state.TerrainId}");
                }

                Equal(true, arrived,
                    $"{name}: the party must be walked into Wutai; it ended at {state.X},{state.Y},{state.Z} " +
                    $"terrain {state.TerrainId} saying \"{spoken}\" ({runtime.Navigation.LastDiagnostic})");
                Equal(true, wutai.HasArrived(state, ResolveTriangle(runtime.Planner, state)),
                    $"{name}: and it arrived by the game's own test, script 7 in Wutai's mesh cell");
            }
        }
    }

    /// <summary>
    /// Wutai's world handler, 0x96C4, enters world entry 23 only when special 8 - the player
    /// model - is 0, 1 or 2. Anybody else on the trigger is not let in, so reaching it on a chocobo
    /// is told as the way in being on foot, not as an arrival, and the destination is kept
    /// until the party is on foot there.
    /// </summary>
    private static void WutaiIsEnteredOnFootOnly(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var wutai = catalog.Locations.Single(target => target.Label == "Wutai");
        var arrival = wutai.NativeLocationArrivals.First();
        var trigger = map.Triangles[arrival.TriangleId];
        Equal(7, arrival.TerrainScriptId, "Wutai's native trigger is walkmap script 7");
        var onFootAtTheGate = FootState(map, arrival.X, arrival.Y, arrival.Z, 0) with
        {
            TerrainId = trigger.TerrainId,
            RegionId = trigger.RegionId & 31,
            TerrainScriptId = arrival.TerrainScriptId
        };
        var ridingAtTheGate = onFootAtTheGate with { PlayerModelId = 4 };
        var start = LoggedStarts[0];
        var ridingAway = FootState(map, start.X, start.Y, start.Z, 0) with { PlayerModelId = 4 };

        var runtime = CreateShippedRuntime(map, catalog);
        var now = new DateTime(2026, 9, 23, 11, 23, 13, DateTimeKind.Utc);
        SelectWutai(runtime, catalog, ridingAway, now);
        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, ridingAway, now);
        Equal(true, runtime.Navigation.BeaconEnabled, "a route to Wutai starts on a chocobo");

        var handoff = runtime.Navigation.Observe(ridingAtTheGate, now.AddSeconds(1), automaticWalkActive: true);
        Equal(false, (handoff?.Speech ?? string.Empty).Contains("Arrived", StringComparison.Ordinal),
            $"a chocobo on Wutai's trigger has not arrived; said \"{handoff?.Speech}\"");
        Equal(true, (handoff?.Speech ?? string.Empty).Contains("on foot", StringComparison.Ordinal),
            $"the party is told the way in is on foot; said \"{handoff?.Speech}\"");
        Equal(true, runtime.Navigation.BeaconEnabled, "and Wutai stays the destination");

        var arrived = runtime.Navigation.Observe(onFootAtTheGate, now.AddSeconds(2), automaticWalkActive: true);
        Equal(true, (arrived?.Speech ?? string.Empty).Contains("Arrived at Wutai", StringComparison.Ordinal),
            $"on foot the same trigger is an arrival; said \"{arrived?.Speech}\"");
    }

    private static void SelectWutai(
        WorldMapRuntimeContext runtime,
        WorldMapTargetCatalog catalog,
        WorldMapStateSnapshot state,
        DateTime now)
    {
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Locations)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, now);
        }

        for (var step = 0; step < catalog.Locations.Count; step++)
        {
            if ((runtime.Navigation.HandleAction(FieldNavigationAction.NextTarget, state, now)?.Speech ?? string.Empty)
                .Contains(", Wutai.", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException("World map Wutai navigation - Wutai is not offered as a location.");
    }

    /// <summary>
    /// The party on foot under <c>FUN_0074EA48</c>'s movement and <c>FUN_00751EFC</c>'s ground
    /// follow: 30 units a frame (diagonals three quarters), and a step taken only if the
    /// five contact points pass <c>FUN_0074CECA</c> for models 0-2; otherwise the move turned
    /// by 160, 320 ... 1120 angle units to one side, then the other, the first that fits.
    ///
    /// <para>It is stricter than the game, not kinder: the logs have the real party closer to
    /// a bridge's edge than it allows, and up the steep climb before the pass at 11:19:16
    /// where it slides. Its camera is also held still, where the game's turns to follow the
    /// party; the cameras above are five different sets of directions it can be caught in.</para>
    /// </summary>
    private sealed class NativeWalker(WorldMapData map, int x, int y, int z, int camera)
    {
        private int preferredSide = 1;

        public int X { get; private set; } = x;

        public int Y { get; private set; } = y;

        public int Z { get; private set; } = z;

        public WorldMapStateSnapshot Snapshot()
        {
            if (!TryFindWalkSurface(map, X, Z, Y, out var surface))
            {
                throw new InvalidOperationException($"the party left the map at {X},{Z}");
            }

            var direction = -(camera / 16);
            return new WorldMapStateSnapshot(
                3, 0, 0, 900, X, Y, Z, 0, 0, surface.TerrainId, surface.RegionId & 31, 0, WalkStep, camera,
                new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
            {
                TerrainScriptId = surface.TerrainScriptId
            };
        }

        public void Frame(FieldNavigationInput input)
        {
            if (input is FieldNavigationInput.None or FieldNavigationInput.Conflicting)
            {
                return;
            }

            var stick = FieldNavigationMovementObserver.ToStickDirection(input);
            var scale = stick.X != 0f && stick.Y != 0f ? 0.75d : 1d;
            var angle = camera * Math.PI * 2d / 4096d;
            var sx = Math.Sign(stick.X) * scale * WalkStep;
            var sz = Math.Sign(stick.Y) * scale * WalkStep;
            var dx = (int)(sx * Math.Cos(angle) - sz * Math.Sin(angle));
            var dz = (int)(sx * Math.Sin(angle) + sz * Math.Cos(angle));
            if (!TryFindWalkSurface(map, X, Z, Y, out var here))
            {
                return;
            }

            var onBridge = here.TerrainId is 13 or 14;
            if (TryMove(dx, dz, onBridge))
            {
                return;
            }

            foreach (var side in new[] { preferredSide, -preferredSide })
            {
                for (var sample = 1; sample < 8; sample++)
                {
                    var radians = side * sample * 160 * Math.PI * 2d / 4096d;
                    var rx = (int)(dx * Math.Cos(radians) + dz * Math.Sin(radians));
                    var rz = (int)(-dx * Math.Sin(radians) + dz * Math.Cos(radians));
                    if (TryMove(rx, rz, onBridge))
                    {
                        preferredSide = side;
                        return;
                    }
                }
            }
        }

        private bool TryMove(int dx, int dz, bool onBridge)
        {
            var nx = X + dx;
            var nz = Z + dz;
            if (!NativeFootprintFits(map, nx, nz, Y, onBridge) ||
                !TryFindWalkSurface(map, nx, nz, Y, out var surface))
            {
                return false;
            }

            X = ((nx % map.WrapWidth) + map.WrapWidth) % map.WrapWidth;
            Z = ((nz % map.WrapHeight) + map.WrapHeight) % map.WrapHeight;
            Y = WorldMapBroncoLanding.SurfaceHeight(surface, X, Z);
            return true;
        }
    }

    /// <summary>FUN_0074CECA, models 0-2 away from a bridge: mask 0x721B6F83.</summary>
    private static bool IsNativeWalkingTerrain(int terrain) => ((0x721B6F83u >> terrain) & 1) != 0;

    private static bool NativeFootprintFits(WorldMapData map, int x, int z, int referenceHeight, bool onBridge)
    {
        foreach (var (ox, oz) in new[] { (0, 0), (WalkContactRadius, 0), (-WalkContactRadius, 0), (0, WalkContactRadius), (0, -WalkContactRadius) })
        {
            if (!TryFindWalkSurface(map, x + ox, z + oz, referenceHeight, out var surface) ||
                !(onBridge ? surface.TerrainId is 13 or 14 or 29 : IsNativeWalkingTerrain(surface.TerrainId)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// FUN_0074CC07 for a walking model: of the triangles in the point's mesh cell that hold
    /// it (FUN_0076085F, edges included), the one whose surface is nearest the party's height.
    /// </summary>
    private static bool TryFindWalkSurface(WorldMapData map, int x, int z, int referenceHeight, out WorldMapTriangle surface)
    {
        surface = null!;
        x = ((x % map.WrapWidth) + map.WrapWidth) % map.WrapWidth;
        z = ((z % map.WrapHeight) + map.WrapHeight) % map.WrapHeight;
        var meshX = x / WorldMapDataLoader.MeshSize;
        var meshZ = z / WorldMapDataLoader.MeshSize;
        var best = double.PositiveInfinity;
        var index = Buckets.GetValue(map, BuildBuckets);
        if (!index.TryGetValue((x / BucketSize, z / BucketSize), out var candidates))
        {
            return false;
        }

        foreach (var triangle in candidates)
        {
            if (triangle.MeshX != meshX || triangle.MeshZ != meshZ || !IsInsideNative(triangle, x, z))
            {
                continue;
            }

            var height = WorldMapBroncoLanding.SurfaceHeight(triangle, x, z);
            if (Math.Abs(height - referenceHeight) < best)
            {
                best = Math.Abs(height - referenceHeight);
                surface = triangle;
            }
        }

        return surface is not null;
    }

    private const int BucketSize = 256;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        WorldMapData, Dictionary<(int, int), List<WorldMapTriangle>>> Buckets = new();

    private static Dictionary<(int, int), List<WorldMapTriangle>> BuildBuckets(WorldMapData map)
    {
        var buckets = new Dictionary<(int, int), List<WorldMapTriangle>>();
        foreach (var triangle in map.Triangles)
        {
            var minX = Math.Min(triangle.Vertex0.X, Math.Min(triangle.Vertex1.X, triangle.Vertex2.X)) / BucketSize;
            var maxX = Math.Max(triangle.Vertex0.X, Math.Max(triangle.Vertex1.X, triangle.Vertex2.X)) / BucketSize;
            var minZ = Math.Min(triangle.Vertex0.Z, Math.Min(triangle.Vertex1.Z, triangle.Vertex2.Z)) / BucketSize;
            var maxZ = Math.Max(triangle.Vertex0.Z, Math.Max(triangle.Vertex1.Z, triangle.Vertex2.Z)) / BucketSize;
            for (var bx = minX; bx <= maxX; bx++)
            {
                for (var bz = minZ; bz <= maxZ; bz++)
                {
                    if (!buckets.TryGetValue((bx, bz), out var list))
                    {
                        list = [];
                        buckets[(bx, bz)] = list;
                    }

                    list.Add(triangle);
                }
            }
        }

        return buckets;
    }

    private static bool IsInsideNative(WorldMapTriangle triangle, int x, int z)
    {
        static long Cross(WorldMapVertex from, WorldMapVertex to, int px, int pz) =>
            (long)(to.Z - from.Z) * (px - from.X) - (long)(to.X - from.X) * (pz - from.Z);
        return Cross(triangle.Vertex0, triangle.Vertex1, x, z) <= 0 &&
               Cross(triangle.Vertex1, triangle.Vertex2, x, z) <= 0 &&
               Cross(triangle.Vertex2, triangle.Vertex0, x, z) <= 0;
    }

    private static int ResolveTriangle(WorldMapRoutePlanner planner, WorldMapStateSnapshot state) =>
        planner.TryResolvePlayerTriangle(state, out var triangle) ? triangle : -1;

    private static WorldMapStateSnapshot FootState(WorldMapData map, int x, int y, int z, int camera)
    {
        if (!TryFindWalkSurface(map, x, z, y, out var surface))
        {
            throw new InvalidOperationException($"no ground under the logged party at {x},{z}");
        }

        var direction = -(camera / 16);
        return new WorldMapStateSnapshot(
            3, 0, 0, 900, x, y, z, 0, 0, surface.TerrainId, surface.RegionId & 31, 0, WalkStep, camera,
            new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
        {
            TerrainScriptId = surface.TerrainScriptId
        };
    }

    private static WorldMapRuntimeContext CreateShippedRuntime(WorldMapData map, WorldMapTargetCatalog catalog) =>
        new(map,
            catalog,
            progressSink: null,
            distanceUnitsPerCount: 512,
            guidanceInterval: TimeSpan.FromSeconds(2),
            walkingFootstepInterval: TimeSpan.FromMilliseconds(500),
            chocoboFootstepInterval: TimeSpan.FromMilliseconds(300),
            entranceCueInnerRange: 80,
            entranceCueOuterRange: 400,
            entranceCueInterval: TimeSpan.FromSeconds(3));

    private static bool TryLoadInstalledWorld(out WorldMapData map, out WorldMapTargetCatalog catalog)
    {
        map = null!;
        catalog = null!;
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return true;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"World map Wutai navigation - {label}: expected {expected}, got {actual}.");
        }
    }
}
