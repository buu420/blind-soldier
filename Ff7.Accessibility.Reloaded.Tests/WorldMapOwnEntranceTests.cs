using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// "Locations, Weapon Seller. Route unavailable." - on foot, standing outside the house.
///
/// <para>From <c>ff7_accessibility_steam2026_x64 (9).log</c>, 2026-09-23: the party left field
/// 79 onto the world map at <c>129288,380,174065</c> and at 08:51:01 and 08:51:05 the mod
/// refused to route back in. The planner's own diagnostic was "no route to Weapon Seller for
/// model 0 that avoids another native field entrance". There is no other entrance: mesh
/// cell (15,21) holds one native handler, script 7, and all twenty of its triangles are the
/// seller's.</para>
///
/// <para>The trigger is a box. Eight roof slopes tile the footprint in X/Z and are the only
/// triangles with a point a route can end on; twelve walls and gables stand exactly
/// vertical between them and the ground. Only the roof slopes were exempt from entrance
/// avoidance, so the seller's own walls counted as somebody else's door - and every path
/// from the ground has to cross a wall. The same log records the game entering field 79
/// three times, 08:50:58, 08:51:07 and 08:52:27, each a step from just outside a wall going
/// in, each with the walkmap reading terrain 16 script 7 at the position the step began.</para>
/// </summary>
internal static class WorldMapOwnEntranceTests
{
    private const int SellerMeshX = 15;
    private const int SellerMeshZ = 21;
    private const int SellerScript = 7;

    /// <summary>The three native entries the log recorded, at the position the step began.</summary>
    private static readonly (string When, int X, int Y, int Z)[] EntryWitnesses =
    [
        ("08:50:58", 129347, 409, 173056),
        ("08:51:07", 129017, 408, 173487),
        ("08:52:27", 129242, 407, 173591),
    ];

    /// <summary>Where the mod said "Route unavailable", on foot.</summary>
    private static readonly (string When, int X, int Y, int Z)[] RefusedStarts =
    [
        ("08:51:05", 129111, 375, 174080),
        ("08:50:59", 129288, 380, 174065),
    ];

    /// <summary>
    /// South of the house, where a party put ashore by the Tiny Bronco arrives from. The
    /// 08:50:58 witness entered through this wall, from 129347,409,173056 on its line.
    /// </summary>
    private static readonly (string When, int X, int Y, int Z)[] SouthernStarts =
    [
        ("south of the 08:50:58 entry", 129347, 400, 172600),
        ("south-west of the house", 129100, 400, 172500),
        ("south-east of the house", 129500, 400, 172700),
    ];

    public static void RunWithInstalledGameData()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            Console.WriteLine("world own entrance: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheSellersWallsAreItsOwnEntrance(map, catalog);
        TheSellerRoutesOnFootFromWhereTheLogWasRefused(map, catalog);
        AnotherTownsWallStillStopsTheWalk(map, catalog);
        TheLoggedEntriesAreArrivals(map, catalog);
        AutoWalkStepsIntoTheSellersFootprint(map, catalog);
        Console.WriteLine("world map own entrance tests passed with installed game data.");
    }

    /// <summary>
    /// A destination owns its whole native trigger - the mesh cell and script its handler is
    /// registered for - not only the part of it a route can end on.
    /// </summary>
    private static void TheSellersWallsAreItsOwnEntrance(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var seller = catalog.Locations.Single(target => target.Label == "Weapon Seller");
        var handler = map.Triangles
            .Where(t => t.MeshX == SellerMeshX && t.MeshZ == SellerMeshZ && t.TerrainScriptId == SellerScript)
            .Select(t => t.Id)
            .ToHashSet();
        Equal(20, handler.Count, "the seller's native handler is twenty triangles on the installed map");
        var vertical = handler.Where(id => HasNoPlanArea(map.Triangles[id])).ToArray();
        Equal(12, vertical.Length, "twelve of them are walls and gables standing exactly vertical");
        Equal(false, vertical.Any(seller.ArrivalTriangleIds.Contains),
            "none of the vertical ones can hold an arrival point, which is how they were left out");

        Equal(true, handler.SetEquals(seller.NativeEntranceExemptions),
            "the seller must own every triangle of its own trigger, walls included");
        Equal(true, handler.All(catalog.EntranceTriangleIds.Contains),
            "and every one of them is still a native entrance, so nothing else may cross it");

        // No other destination is handed the seller's walls.
        foreach (var other in catalog.Locations.Where(target => target.Label != "Weapon Seller"))
        {
            Equal(false, other.NativeEntranceExemptions.Overlaps(handler),
                $"\"{other.Label}\" must not be allowed onto the Weapon Seller's trigger");
        }
    }

    /// <summary>The two refusals the user heard, now routes, and routes that stay out of other doors.</summary>
    private static void TheSellerRoutesOnFootFromWhereTheLogWasRefused(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var seller = catalog.Locations.Single(target => target.Label == "Weapon Seller");
        foreach (var (when, x, y, z) in RefusedStarts)
        {
            var onFoot = State(x, y, z, 0);
            Equal(true, planner.CanReach(onFoot, seller),
                $"{when}: the seller shares the walkable ground the party is standing on");
            Equal(true, planner.TryBuildRoute(onFoot, seller, out var plan),
                $"{when}: and a route must build from where the mod refused it: {planner.LastDiagnostic}");
            var foreign = plan.TrianglePath
                .Where(id => catalog.EntranceTriangleIds.Contains(id) && !seller.NativeEntranceExemptions.Contains(id))
                .ToArray();
            Equal(0, foreign.Length,
                $"{when}: the route must not cross any other destination's entrance: [{string.Join(",", foreign)}]");
        }
    }

    /// <summary>
    /// The walls pass the step probe only for the destination they belong to. Global entrance
    /// avoidance is untouched: a segment into the seller's footprint is refused when the
    /// seller is not what the player chose.
    /// </summary>
    private static void AnotherTownsWallStillStopsTheWalk(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var seller = catalog.Locations.Single(target => target.Label == "Weapon Seller");
        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");

        // Just outside the north wall, stepping straight in: the approach of 08:52:27.
        var outside = State(129300, 407, 173620, 0);
        var inside = new WorldMapRouteWaypoint(129300, 700, 173400);

        Equal(true, planner.CanTraverseSegment(outside, inside, null, seller.NativeEntranceExemptions),
            "walking into the seller's own house must pass when the seller is the destination");
        Equal(false, planner.CanTraverseSegment(outside, inside, null, gongaga.NativeEntranceExemptions),
            "and must still be refused when the player chose somewhere else");
        Equal(false, planner.CanTraverseSegment(outside, inside, null, new HashSet<int>()),
            "and refused when nothing is exempt at all");
    }

    /// <summary>
    /// The native test is the terrain script under the party in the handler's mesh cell - the
    /// walkmap word the log recorded at every entry - not where the mod resolves the party to
    /// be. At two of the three entries that position is outside the house.
    /// </summary>
    private static void TheLoggedEntriesAreArrivals(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var seller = catalog.Locations.Single(target => target.Label == "Weapon Seller");
        foreach (var (when, x, y, z) in EntryWitnesses)
        {
            var witnessed = State(x, y, z, 16) with { TerrainScriptId = SellerScript };
            planner.TryResolvePlayerTriangle(witnessed, out var triangle);
            Equal(true, seller.HasArrived(witnessed, triangle),
                $"{when}: the walkmap reading script 7 in the seller's cell is the game entering it");
            Equal(false, seller.HasArrived(witnessed with { TerrainId = 0, TerrainScriptId = 0 }, triangle),
                $"{when}: and the same position with the ordinary ground's script 0 is not");
        }

        // Script 7 somewhere else is somebody else's handler.
        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");
        var inGongaga = State(gongaga.X, gongaga.Y, gongaga.Z, 16) with { TerrainScriptId = SellerScript };
        planner.TryResolvePlayerTriangle(inGongaga, out var gongagaTriangle);
        Equal(false, seller.HasArrived(inGongaga, gongagaTriangle),
            "script 7 in Gongaga's cell is Gongaga's entrance, not the seller's");
    }

    /// <summary>
    /// A route to a roof is not the same as a walk through the door. This drives the shipped
    /// controller with the pre-existing raw-triangle movement oracle from both refused
    /// starts, at five camera fronts and three step lengths.
    ///
    /// <para>That oracle treats vertical faces as impassable, which the game does not do for
    /// a trigger wall: the log's three entries are each a step from outside a wall going in.
    /// So entry is modelled the way the log shows it - a commanded step whose X/Z lands in
    /// the footprint the roof slopes tile - and the rule is first checked against those
    /// three recorded steps. The walk must reach it without auto walk giving up.</para>
    /// </summary>
    private static void AutoWalkStepsIntoTheSellersFootprint(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var roof = map.Triangles
            .Where(t => t.MeshX == SellerMeshX && t.MeshZ == SellerMeshZ &&
                        t.TerrainScriptId == SellerScript && !HasNoPlanArea(t))
            .ToArray();
        bool InFootprint(double x, double z) => roof.Any(t => Contains(t, x, z));

        // The rule reproduces the log: from each recorded pre-entry position, the smallest
        // native walking step towards the house centre lands in the footprint.
        const double centreX = 129280;
        const double centreZ = 173309;
        foreach (var (when, x, _, z) in EntryWitnesses)
        {
            var length = Math.Sqrt((centreX - x) * (centreX - x) + (centreZ - z) * (centreZ - z));
            Equal(true, InFootprint(x + (centreX - x) / length * 30, z + (centreZ - z) / length * 30),
                $"{when}: one walking step in from the recorded position must reach the footprint");
        }

        // From the south the route crosses the wall line, and the string used to be pulled
        // through the vertical faces beyond it: waypoints stacked on the wall at different
        // heights, one of them a corner edge that is a single point in plan. Auto walk paced
        // along the wall and never stepped in. It now has to step in from this side too.
        foreach (var (when, sx, sy, sz) in RefusedStarts.Concat(SouthernStarts))
        {
            foreach (var camera in new[] { 0, 1024, 2048, 3072, 4000 })
            {
                foreach (var speed in new[] { 30, 60, 120 })
                {
                    var direction = -(camera / 16);
                    var state = State(sx, sy, sz, 0) with
                    {
                        CameraFront = camera,
                        ControlTransform = new FieldNavigationControlTransform(
                            direction < -128 ? direction + 256 : direction)
                    };
                    var runtime = CreateShippedRuntime(map, catalog);
                    var now = new DateTime(2026, 9, 23, 8, 51, 5, DateTimeKind.Utc);
                    while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Locations)
                    {
                        runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, now);
                    }

                    var selected = string.Empty;
                    for (var step = 0; step < catalog.Locations.Count && !selected.Contains("Weapon Seller"); step++)
                    {
                        selected = runtime.Navigation
                            .HandleAction(FieldNavigationAction.NextTarget, state, now)?.Speech ?? string.Empty;
                    }

                    var started = runtime.Navigation
                        .HandleAction(FieldNavigationAction.ToggleBeacon, state, now)?.Speech ?? string.Empty;
                    Equal(true, runtime.Navigation.BeaconEnabled,
                        $"{when} camera {camera}: navigating to the seller must start; said \"{started}\"");
                    runtime.Planner.TryResolvePlayerTriangle(state, out var triangle);

                    var entered = false;
                    var spoken = started;
                    for (var frame = 0; frame < 300 && !entered; frame++)
                    {
                        var output = runtime.Navigation.Observe(
                            state, now.AddMilliseconds(frame * 80), automaticWalkActive: true);
                        if (output is { } said)
                        {
                            spoken = said.Speech ?? spoken;
                            Equal(false, spoken.Contains("Could not get closer", StringComparison.Ordinal),
                                $"{when} camera {camera} speed {speed}: auto walk gave up at " +
                                $"({state.X},{state.Z}) before reaching the door");
                        }

                        if (!runtime.Navigation.TryResolveAutomaticInput(state, out var input))
                        {
                            continue;
                        }

                        var (dx, dz) = NativeStep(input, camera, speed);
                        if (InFootprint(state.X + dx, state.Z + dz))
                        {
                            entered = true;

                            // What the game then reports, exactly as recorded at each entry.
                            var witnessed = state with { TerrainId = 16, TerrainScriptId = SellerScript };
                            var arrival = runtime.Navigation.Observe(
                                witnessed, now.AddMilliseconds((frame + 1) * 80), automaticWalkActive: true);
                            Equal(true, (arrival?.Speech ?? string.Empty).Contains("Arrived at Weapon Seller", StringComparison.Ordinal),
                                $"{when} camera {camera} speed {speed}: the native entry must be announced; " +
                                $"said \"{arrival?.Speech}\"");
                            Equal(false, runtime.Navigation.BeaconEnabled,
                                $"{when} camera {camera} speed {speed}: and navigation must end there");
                            break;
                        }

                        state = WorldMapVehicleApproachReplayTests.MoveOnRawNativeTriangles(
                            map, state, ref triangle, dx, dz) with
                        {
                            CameraFront = state.CameraFront,
                            ControlTransform = state.ControlTransform
                        };
                    }

                    Equal(true, entered,
                        $"{when} camera {camera} speed {speed}: auto walk must step into the seller's " +
                        $"footprint; it ended at ({state.X},{state.Z}) saying \"{spoken}\"");
                }
            }
        }
    }

    private static bool HasNoPlanArea(WorldMapTriangle t) =>
        (long)(t.Vertex1.X - t.Vertex0.X) * (t.Vertex2.Z - t.Vertex0.Z) ==
        (long)(t.Vertex2.X - t.Vertex0.X) * (t.Vertex1.Z - t.Vertex0.Z);

    private static bool Contains(WorldMapTriangle t, double x, double z)
    {
        double Side(WorldMapVertex a, WorldMapVertex b) => (x - b.X) * (a.Z - b.Z) - (a.X - b.X) * (z - b.Z);
        var first = Side(t.Vertex0, t.Vertex1);
        var second = Side(t.Vertex1, t.Vertex2);
        var third = Side(t.Vertex2, t.Vertex0);
        return (first >= 0 && second >= 0 && third >= 0) || (first <= 0 && second <= 0 && third <= 0);
    }

    /// <summary>FUN0074EA48: four axes, diagonals at three quarters, negative camera rotation.</summary>
    private static (double X, double Z) NativeStep(FieldNavigationInput input, int camera, int distance)
    {
        var x = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -1 :
            input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? 1 : 0;
        var z = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -1 :
            input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? 1 : 0;
        var scale = x != 0 && z != 0 ? distance * 0.75d : distance;
        var angle = camera * Math.PI * 2d / 4096d;
        return ((x * Math.Cos(angle) - z * Math.Sin(angle)) * scale,
            (x * Math.Sin(angle) + z * Math.Cos(angle)) * scale);
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

    private static WorldMapStateSnapshot State(int x, int y, int z, int terrain) =>
        new(3, 0, 0, 566, x, y, z, 0, 0, terrain, 5, 0, 30, 4000,
            new FieldNavigationControlTransform(0))
        {
            TerrainScriptId = 0
        };

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"World map own entrance - {label}: expected {expected}, got {actual}.");
        }
    }
}
