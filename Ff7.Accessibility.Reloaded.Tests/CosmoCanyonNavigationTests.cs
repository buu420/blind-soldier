using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-21 session's two navigation defects, both quoted from the log.
///
/// <para><b>Cosmo interiors.</b> Field 541 is Bugenhagen's observatory and 544 the research
/// centre below it. Selecting "Go back down from the observatory" at 01:28:10 answered
/// "direction unavailable" and then "Route unavailable ... Navigation off", and the same
/// happened to "Go through to the room at the top" at 01:23:21. The offline probe shows the
/// walkmesh routes fine in every one of those cases with no boundary configured, so the
/// route is not lost - the game is holding the door shut. The log's own live boundary for
/// 541 is triangle 1, which is the exit's target triangle, and the door opens at 01:28:57.
/// Nothing here routes through a native lock; the destination is kept instead of
/// cancelled.</para>
///
/// <para><b>Gongaga zoning.</b> At 01:11:09 the party walked toward their parked Buggy,
/// crossed world position (112477,536,184181) - terrain 16, terrain script 7, mesh (13,22),
/// which is Gongaga's own native entry trigger - and were dropped into the jungle, field
/// 515. The Buggy itself sits at (113151,593,181046), inside that same trigger cell, so
/// every approach repeated it.</para>
/// </summary>
internal static class CosmoCanyonNavigationTests
{
    private const int Observatory = 541;
    private const int ResearchCentre = 544;

    // 01:27:46 field navigation story: "Go back down from the observatory"@(-378,30,-36),
    // listed as Exit to Bugen Research Center@(-379,31,-36).
    private static readonly FieldPositionSnapshot ObservatoryStall = new(1, Observatory, 0, -72, 103, -38, 38, 40);
    private static readonly FieldNavigationTarget WayBackDown = new(
        Observatory,
        FieldNavigationCategory.Story,
        "Go back down from the observatory",
        -378,
        30,
        -36,
        StableId: "cosmo:541:back-down");

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        AShutNativeDoorIsNotAMissingRoute(createWalkmeshReader);
        AShutDoorKeepsTheDestinationAndStartsWhenItOpens(createWalkmeshReader);
        TheResearchCentreUpperDoorBehavesTheSameWay(createWalkmeshReader);
        AShutExitStaysSelectableSoItCanBeExplained(createWalkmeshReader);
        NoSafeRouteIsAnAnswer();
        WaypointSelectionObeysTheEntranceRule();
        OnlyTheSelectedPlaceLicensesItsOwnEntrance();
        RunWorldCases();
        TheShippedRuntimeGuardsZoneEntry();
        CosmoCanyonNavigationCallerTests.Run(createWalkmeshReader);
    }

    /// <summary>
    /// The guard has to be on the planner the game builds, not on one a test configured.
    ///
    /// <para>Every other world case here sets <c>EntranceTriangleIds</c> by hand, so all of
    /// them would keep passing while the shipped runtime routed with an empty guard and
    /// walked the party straight into the jungle again. This one constructs the real
    /// <see cref="WorldMapRuntimeContext"/> - the same type Mod.cs and the x64 world
    /// coordinator build - and touches nothing on it.</para>
    /// </summary>
    private static void TheShippedRuntimeGuardsZoneEntry()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            return;
        }

        var runtime = CreateShippedRuntime(map, catalog);

        Equal(true, runtime.Planner.EntranceTriangleIds.Count > 0,
            "the shipped planner must carry the native entrance metadata");
        Equal(true, runtime.Planner.EntranceTriangleIds.SetEquals(catalog.EntranceTriangleIds),
            "and it must be the catalog's own set, not a subset assembled elsewhere");

        // 01:11:05, on foot, walking to the Buggy parked in Gongaga's trigger cell.
        var start = State(112443, 370, 185139, 25, 0, 5, 0);
        var buggy = new WorldMapEntitySnapshot(0x1000, 0, false, 113151, 593, 181046, 25, 5, 6, 0);
        runtime.UpdateEntities([buggy]);

        var vehicle = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, start, runtime.Entities)
            .Single(target => target.Label == "Buggy");
        Equal(true, runtime.Planner.TryBuildRoute(start, vehicle, out var plan),
            $"the shipped runtime must still reach the Buggy: {runtime.Planner.LastDiagnostic}");
        var crossed = plan.TrianglePath
            .Where(catalog.EntranceTriangleIds.Contains)
            .ToArray();
        Equal(0, crossed.Length,
            $"the shipped route must go around Gongaga, crossed [{string.Join(',', crossed)}]");

        // Selecting the town itself still walks in through its own door.
        var gongaga = runtime.Catalog.Locations.Single(target => target.Label == "Gongaga");
        Equal(true, runtime.Planner.TryBuildRoute(start, gongaga, out var townPlan),
            $"the shipped runtime must still enter a selected town: {runtime.Planner.LastDiagnostic}");
        Equal(true, gongaga.ArrivalTriangleIds.Contains(townPlan.TargetTriangleId),
            "and must end on its own entrance");
        Equal(0,
            townPlan.TrianglePath.Count(id =>
                catalog.EntranceTriangleIds.Contains(id) && !gongaga.ArrivalTriangleIds.Contains(id)),
            "without passing anybody else's");

        // Driving the shipped controller: selecting the Buggy and walking must never
        // produce a key press that lands on a trigger the route avoided.
        var now = new DateTime(2026, 9, 21, 1, 11, 0, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, start, now);
        }

        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        Equal(true, runtime.Navigation.BeaconEnabled,
            $"the shipped controller must accept the Buggy: {runtime.Navigation.LastDiagnostic}");

        var walked = start;
        for (var frame = 0; frame < 240; frame++)
        {
            runtime.Navigation.Observe(walked, now.AddMilliseconds(frame * 80), automaticWalkActive: true);
            if (!runtime.Navigation.TryResolveAutomaticInput(walked, out var input))
            {
                break;
            }

            var (dx, dz) = NativeStep(input, walked.CameraFront, 60);
            var next = walked with
            {
                X = Wrap(walked.X + (int)Math.Round(dx), map.WrapWidth),
                Z = Wrap(walked.Z + (int)Math.Round(dz), map.WrapHeight)
            };
            if (!runtime.Planner.TryResolvePlayerTriangle(next, out var standing))
            {
                break;
            }

            Equal(false,
                catalog.EntranceTriangleIds.Contains(standing) &&
                !vehicle.ArrivalTriangleIds.Contains(standing),
                $"frame {frame}: automatic walking stepped onto entrance triangle {standing} " +
                $"at {next.X},{next.Z}");
            walked = next with
            {
                TerrainId = map.Triangles[standing].TerrainId,
                RegionId = map.Triangles[standing].RegionId & 0x1F,
                TerrainScriptId = map.Triangles[standing].TerrainScriptId
            };
        }
    }

    /// <summary>
    /// Line-of-sight waypoint selection must obey the same entrance rule as the route it
    /// walks. `ResolveAutomaticWaypoint` passed no exemptions, and a null set turns the
    /// entrance check off, so a party projected some way along a route could keep aiming
    /// across somebody else's doorway while the final step probe refused it.
    ///
    /// <para>Pinned on the position that actually changes behaviour rather than on the rule
    /// alone: at (111242, 529, 183033) the shipped runtime projects onto waypoint 2 of a
    /// five-waypoint route to the Buggy, and the unexempted sight line to that waypoint
    /// crosses one of Gongaga's entrance triangles. Removing the exemption from the call
    /// site makes this case fail; asserting the rule in isolation does not.</para>
    /// </summary>
    private static void WaypointSelectionObeysTheEntranceRule()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            return;
        }

        var runtime = CreateShippedRuntime(map, catalog);
        var buggy = new WorldMapEntitySnapshot(0x1000, 0, false, 113151, 593, 181046, 25, 5, 6, 0);
        runtime.UpdateEntities([buggy]);

        var state = State(111242, 529, 183033, 25, 0, 5, 0);
        Equal(true, runtime.Planner.TryResolvePlayerTriangle(state, out var standing),
            "the probed position must resolve");
        Equal(false, catalog.EntranceTriangleIds.Contains(standing),
            "and must not itself be a doorway, or the escape rule would apply instead");

        var vehicle = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, state, runtime.Entities)
            .Single(target => target.Label == "Buggy");
        var now = new DateTime(2026, 9, 21, 1, 11, 0, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, now);
        }

        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        Equal(true, runtime.Navigation.BeaconEnabled,
            $"the Buggy must be selectable here: {runtime.Navigation.LastDiagnostic}");

        runtime.Navigation.Observe(state, now.AddMilliseconds(80), automaticWalkActive: true);
        runtime.Navigation.TryResolveAutomaticInput(state, out _);

        var probe = runtime.Navigation.Probe;
        var route = probe.Route;
        Equal(true, route is not null, "the shipped controller must hold a route here");
        Equal(true, probe.WaypointIndex > 0,
            "this position must project past the first waypoint, or the case proves nothing");
        Equal(true,
            runtime.Planner.CanTraverseSegment(
                state, route!.Waypoints[probe.WaypointIndex], null, vehicle.NativeEntranceExemptions),
            $"waypoint {probe.WaypointIndex} of {route.Waypoints.Count} was aimed at across another " +
            "place's entrance");

        // And the rule the call site depends on, stated once: only the place that owns a
        // doorway may be aimed at through it, and a null set still means no check at all.
        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");
        foreach (var doorway in gongaga.ArrivalTriangleIds.Select(id => map.Triangles[id]))
        {
            var centre = doorway.Centroid;
            foreach (var (dx, dz) in new[] { (600, 0), (0, 600), (450, 450), (450, -450) })
            {
                var from = State(centre.X - dx, centre.Y, centre.Z - dz,
                    doorway.TerrainId, 0, doorway.RegionId & 0x1F, 0);
                var to = new WorldMapRouteWaypoint(centre.X + dx, centre.Y, centre.Z + dz);
                if (!runtime.Planner.TryResolvePlayerTriangle(from, out var side) ||
                    catalog.EntranceTriangleIds.Contains(side) ||
                    !runtime.Planner.CanTraverseSegment(from, to))
                {
                    continue;
                }

                Equal(false,
                    runtime.Planner.CanTraverseSegment(from, to, null, vehicle.NativeEntranceExemptions),
                    $"a vehicle must not be aimed at across entrance triangle {doorway.Id}");
                Equal(true,
                    runtime.Planner.CanTraverseSegment(from, to, null, gongaga.NativeEntranceExemptions),
                    "but the town that owns the doorway may be aimed at through it");
                return;
            }
        }

        throw new InvalidOperationException(
            "the installed map must offer a sight line across a Gongaga doorway");
    }

    /// <summary>The runtime both Mod.cs and the x64 world coordinator construct.</summary>
    private static WorldMapRuntimeContext CreateShippedRuntime(
        WorldMapData map, WorldMapTargetCatalog catalog) =>
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

    /// <summary>FUN0074EA48: four direction axes, diagonals at three quarters, negative camera rotation.</summary>
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

    private static int Wrap(int value, int extent) =>
        extent > 0 ? (value % extent + extent) % extent : value;

    /// <summary>
    /// An exit the game is holding shut must still be offered. Dropping it left the player
    /// unable to select the way out at all, so they could not even be told it was locked.
    /// An exit that fails for any other reason is still dropped.
    /// </summary>
    private static void AShutExitStaysSelectableSoItCanBeExplained(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var exit = new FieldNavigationTarget(
            Observatory, FieldNavigationCategory.Exits, "Exit to Bugen Research Center",
            -379, 31, -36, StableId: "exit:541:bugen");
        var gone = exit with { FieldId = ResearchCentre, Label = "Exit to nowhere" };

        var open = new ReachableFieldExitTargetProvider(
            _ => [exit], new FieldWalkmeshRoutePlanner(createWalkmeshReader(Observatory)));
        Equal(1, open.ReadTargets(ObservatoryStall).Count,
            $"an open exit is offered: {open.LastDiagnostic}");

        var shut = new ReachableFieldExitTargetProvider(
            _ => [exit], Planner(createWalkmeshReader, Observatory, [1]));
        Equal(1, shut.ReadTargets(ObservatoryStall).Count,
            $"a shut exit must stay selectable: {shut.LastDiagnostic}");
        Contains("shut by native boundary", shut.LastDiagnostic,
            "and must be recorded as shut rather than as an open way out");

        var broken = new ReachableFieldExitTargetProvider(
            _ => [gone], Planner(createWalkmeshReader, Observatory, [1]));
        Equal(0, broken.ReadTargets(ObservatoryStall).Count,
            $"an exit that fails for any other reason is still dropped: {broken.LastDiagnostic}");
    }

    /// <summary>
    /// A synthetic corridor whose only way to the goal runs through another place's
    /// entrance. There is no fallback: a route that walks the party into a town they did not
    /// ask for is not a success, and the step probe would refuse it anyway, so the two would
    /// disagree. A truthful "no safe route" is the only other answer.
    /// </summary>
    private static void NoSafeRouteIsAnAnswer()
    {
        // t0 - t1 - t2 - t3, laid out west to east; t2 is somebody else's doorway.
        var corridor = Corridor([(0, 1), (1, 2), (2, 3)]);
        var goal = Target(WorldMapTargetKind.Transportation, "Buggy", [3]);
        var planner = new WorldMapRoutePlanner(corridor) { EntranceTriangleIds = new HashSet<int> { 2 } };

        Equal(false, planner.TryBuildRoute(SyntheticState(corridor, 0), goal, out _),
            "the only path runs through another entrance, so there is no safe route");
        Contains("avoids another native field entrance", planner.LastDiagnostic,
            "and the reason must say so rather than blaming the map");

        // The same corridor with a bypass: t1 - t4 - t3.
        var bypass = Corridor([(0, 1), (1, 2), (2, 3), (1, 4), (4, 3)]);
        var around = new WorldMapRoutePlanner(bypass) { EntranceTriangleIds = new HashSet<int> { 2 } };
        Equal(true, around.TryBuildRoute(SyntheticState(bypass, 0), goal, out var plan),
            $"a way round must be taken: {around.LastDiagnostic}");
        Equal(false, plan.TrianglePath.Contains(2), "and must not touch the doorway");

        // Standing on the doorway must not trap the party.
        var escaping = new WorldMapRoutePlanner(corridor) { EntranceTriangleIds = new HashSet<int> { 2 } };
        Equal(true, escaping.TryBuildRoute(SyntheticState(corridor, 2), goal, out _),
            $"a party standing on a doorway must still be routed off it: {escaping.LastDiagnostic}");
    }

    /// <summary>
    /// Only a place the player chose to enter licenses its own trigger. A vehicle or a
    /// chocobo track that happens to sit on one does not.
    /// </summary>
    private static void OnlyTheSelectedPlaceLicensesItsOwnEntrance()
    {
        var overlapping = new HashSet<int> { 2 };
        foreach (var kind in new[]
                 {
                     WorldMapTargetKind.Transportation,
                     WorldMapTargetKind.Event,
                     WorldMapTargetKind.ChocoboTracks,
                     WorldMapTargetKind.TerrainArea
                 })
        {
            Equal(0, Target(kind, kind.ToString(), [2]).NativeEntranceExemptions.Count,
                $"a {kind} target overlapping a trigger must not license entering it");
        }

        foreach (var kind in new[] { WorldMapTargetKind.Location, WorldMapTargetKind.Story })
        {
            Equal(true, Target(kind, kind.ToString(), [2]).NativeEntranceExemptions.SetEquals(overlapping),
                $"a selected {kind} must license its own entrance");
        }

        // A chocobo track laid on a doorway is therefore unreachable, truthfully.
        var corridor = Corridor([(0, 1), (1, 2), (2, 3)]);
        var planner = new WorldMapRoutePlanner(corridor) { EntranceTriangleIds = new HashSet<int> { 2 } };
        Equal(false, planner.TryBuildRoute(
                SyntheticState(corridor, 0),
                Target(WorldMapTargetKind.ChocoboTracks, "Chocobo tracks", [2]),
                out _),
            "tracks on a doorway are not a reason to walk in");
        Equal(true, planner.TryBuildRoute(
                SyntheticState(corridor, 0),
                Target(WorldMapTargetKind.Location, "That town", [2]),
                out var townPlan),
            $"but selecting the place itself still works: {planner.LastDiagnostic}");
        Equal(2, townPlan.TargetTriangleId, "and ends on its own entrance");
    }

    private static WorldMapNavigationTarget Target(
        WorldMapTargetKind kind, string label, int[] arrivals) =>
        new(kind switch
            {
                WorldMapTargetKind.Location => WorldMapNavigationCategory.Locations,
                WorldMapTargetKind.Story => WorldMapNavigationCategory.Story,
                WorldMapTargetKind.ChocoboTracks => WorldMapNavigationCategory.ChocoboTracks,
                WorldMapTargetKind.TerrainArea => WorldMapNavigationCategory.Regions,
                WorldMapTargetKind.Event => WorldMapNavigationCategory.Events,
                _ => WorldMapNavigationCategory.Transportation
            },
            kind, label, 0, 0, 0, arrivals[0], 0, $"synthetic:{label}",
            new HashSet<int>(arrivals));

    /// <summary>
    /// A chain of right triangles along X inside mesh (0,0), adjacency exactly as declared.
    /// Small enough to reason about, and walked by the production planner unchanged.
    /// </summary>
    private static WorldMapData Corridor(IReadOnlyList<(int From, int To)> edges)
    {
        var count = edges.SelectMany(edge => new[] { edge.From, edge.To }).Max() + 1;
        var neighbors = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        foreach (var (from, to) in edges)
        {
            neighbors[from].Add(to);
            neighbors[to].Add(from);
        }

        var triangles = Enumerable.Range(0, count)
            .Select(index => new WorldMapTriangle(
                index, 0, 0, 0, 0, index,
                new WorldMapVertex(index * 1000, 0, 0),
                new WorldMapVertex(index * 1000 + 999, 0, 0),
                new WorldMapVertex(index * 1000, 0, 999),
                TerrainId: 0,
                TerrainScriptId: 0,
                TextureId: 0,
                RegionId: 0,
                HasChocoboTracks: false,
                Neighbors: neighbors[index]))
            .ToArray();
        return new WorldMapData(0, 0, 1, 1, 1, triangles, Array.Empty<WorldMapReplacementBlock>(), "synthetic");
    }

    private static WorldMapStateSnapshot SyntheticState(WorldMapData map, int triangleId)
    {
        var centroid = map.Triangles[triangleId].Centroid;
        return State(centroid.X, centroid.Y, centroid.Z, 0, 0, 0, 0);
    }

    /// <summary>
    /// The planner must refuse the locked route - and must say that the lock is the only
    /// reason, because "shut for now" and "no such route" are different answers.
    /// </summary>
    private static void AShutNativeDoorIsNotAMissingRoute(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var open = new FieldWalkmeshRoutePlanner(createWalkmeshReader(Observatory));
        Equal(true, open.TryBuildRoute(ObservatoryStall, WayBackDown, out _),
            $"the observatory exit routes on installed triangles when nothing is locked: {open.LastDiagnostic}");
        Equal(false, open.LastFailureWasNativeBoundary, "an open door is not a boundary failure");

        var shut = Planner(createWalkmeshReader, Observatory, [1]);
        Equal(false, shut.TryBuildRoute(ObservatoryStall, WayBackDown, out _),
            "nothing may route through Bugenhagen's lock");
        Equal(true, shut.LastFailureWasNativeBoundary,
            $"the failure must be attributed to the native boundary: {shut.LastDiagnostic}");
        SequenceEqual([1], shut.LastBlockingBoundaryTriangles, "the blocking triangle must be named");

        // A destination that fails for any other reason must stay an ordinary failure, or
        // the hold would latch onto things that are never going to open.
        var elsewhere = WayBackDown with { FieldId = ResearchCentre, Label = "elsewhere" };
        Equal(false, shut.TryBuildRoute(ObservatoryStall, elsewhere, out _),
            "a target in another field has no route here");
        Equal(false, shut.LastFailureWasNativeBoundary,
            "only a boundary failure may be held");
    }

    /// <summary>
    /// The reported failure. Asking for the way down during the lecture used to cancel the
    /// destination outright; now it is held, nothing is walked, and it starts by itself
    /// when the script releases triangle 1 - which the log shows happening at 01:28:57.
    /// </summary>
    private static void AShutDoorKeepsTheDestinationAndStartsWhenItOpens(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var boundary = new MutableBoundaryMemory(Observatory, [1]);
        var planner = new FieldWalkmeshRoutePlanner(
            createWalkmeshReader(Observatory),
            new FieldBoundaryStateReader(boundary.ReadInt32, boundary.ReadByte, (_, _) => true));
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([WayBackDown]),
            planner);
        var transform = new FieldNavigationControlTransform(0);

        controller.HandleAction(FieldNavigationAction.NextCategory, ObservatoryStall, transform);
        while (controller.CurrentCategory != FieldNavigationCategory.Story)
            controller.HandleAction(FieldNavigationAction.NextCategory, ObservatoryStall, transform);

        var shut = controller.HandleAction(FieldNavigationAction.ToggleBeacon, ObservatoryStall, transform);
        Contains("shut just now", shut?.Speech, "a locked door must be explained, not reported as missing");
        Equal(WayBackDown.Label, controller.HeldForNativeBoundaryLabel,
            "the destination must be kept, not cancelled");

        // This case used to assert that nothing was walked anywhere while the door was
        // shut. That was the wrong contract for this particular door, and holding to it is
        // what left the player waiting for an event that could not happen: 541's lock is
        // released by entity 15 LINEW script 5, which only runs when the party crosses the
        // line it declares. The approach to that line is started here; what must still be
        // true is that the destination is kept and that the real route is what eventually
        // starts. ObservatoryApproachNavigationTests owns the approach behaviour itself.
        Equal(WayBackDown.Label, controller.HeldForNativeBoundaryLabel,
            "the real destination is still the one being waited for");

        // Still shut: still held. The approach may speak its own guidance, but the held
        // destination must not be cancelled or completed while the door stays locked.
        for (var sample = 0; sample < 5; sample++)
        {
            controller.UpdateLiveTracking(
                ObservatoryStall, default, transform, isSuppressed: false);
            Equal(WayBackDown.Label, controller.HeldForNativeBoundaryLabel, "the hold must survive");
        }

        // 01:28:57: the script releases the door.
        boundary.Clear();
        var opened = controller.UpdateLiveTracking(
            ObservatoryStall, default, transform, isSuppressed: false);
        Contains("is open", opened?.Speech, "the player must be told the way opened");
        Equal(true, controller.BeaconEnabled, "navigation must start by itself once the door opens");
        Equal(string.Empty, controller.HeldForNativeBoundaryLabel, "the hold ends when it starts");
        Equal(WayBackDown.Label, controller.CurrentTargetLabel, "the original destination must be the one started");
    }

    /// <summary>
    /// 01:23:21 in field 544, the other half of the same report: "Go through to the room at
    /// the top"@(225,-189,-624) from the entry triangle, while Bugenhagen walks the party in.
    /// </summary>
    private static void TheResearchCentreUpperDoorBehavesTheSameWay(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var entry = new FieldPositionSnapshot(1, ResearchCentre, 0, -376, -337, -635, 0, 216);
        var target = new FieldNavigationTarget(
            ResearchCentre, FieldNavigationCategory.Story, "Go through to the room at the top",
            225, -189, -624, StableId: "cosmo:544:room-at-top");

        var open = new FieldWalkmeshRoutePlanner(createWalkmeshReader(ResearchCentre));
        Equal(true, open.TryBuildRoute(entry, target, out _),
            $"the upper door routes when it is not locked: {open.LastDiagnostic}");

        // Triangle 5 is the door's own target triangle, per the probe.
        var shut = Planner(createWalkmeshReader, ResearchCentre, [5]);
        Equal(false, shut.TryBuildRoute(entry, target, out _), "nothing may route through the shut upper door");
        Equal(true, shut.LastFailureWasNativeBoundary,
            $"the upper door failure must be attributed to the boundary: {shut.LastDiagnostic}");
    }

    /// <summary>
    /// The world half. Walking to a parked vehicle must not cross another place's native
    /// entry trigger, and the vehicle's own approach must not be offered on one.
    /// </summary>
    private static void RunWorldCases()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return;
        }

        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        var catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var guarded = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var unguarded = new WorldMapRoutePlanner(map);

        // 01:11:05 on foot near the Buggy; the Buggy itself from 01:13:23.
        var start = State(112443, 370, 185139, 25, 0, 5, 0);
        var buggy = new WorldMapEntitySnapshot(0x1000, 0, false, 113151, 593, 181046, 25, 5, 6, 0);

        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");
        Equal(true, gongaga.ArrivalTriangleIds.Count > 0, "Gongaga must have native entrance triangles");
        Equal(true, gongaga.ArrivalTriangleIds.All(catalog.EntranceTriangleIds.Contains),
            "the shared entrance set must cover the selected town's own entrance");

        var vehicle = catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, start, [buggy])
            .Single(target => target.Label == "Buggy");
        Equal(false, vehicle.ArrivalTriangleIds.Any(catalog.EntranceTriangleIds.Contains),
            "a vehicle parked in a trigger cell must not be approached through the trigger");

        // The defect: the route the player actually walked crossed Gongaga's entrance.
        Equal(true, unguarded.TryBuildRoute(start, vehicle, out var oldPlan),
            $"baseline route to the Buggy: {unguarded.LastDiagnostic}");
        var oldCrossings = oldPlan.TrianglePath
            .Where(id => catalog.EntranceTriangleIds.Contains(id))
            .ToArray();
        Equal(true, oldCrossings.Length > 0,
            "the unguarded route must reproduce the zoning, or this case proves nothing");

        Equal(true, guarded.TryBuildRoute(start, vehicle, out var plan),
            $"the Buggy must still be reachable: {guarded.LastDiagnostic}");
        var crossed = plan.TrianglePath
            .Where(id => catalog.EntranceTriangleIds.Contains(id) && !vehicle.ArrivalTriangleIds.Contains(id))
            .ToArray();
        Equal(0, crossed.Length,
            $"the route to the Buggy must go around every entrance, crossed [{string.Join(',', crossed)}]");

        // A vehicle parked with nothing but trigger ground around it has no safe approach,
        // and saying so is the answer. Walking into a town to reach a car is not.
        //
        // "Around it" is now the native mask reach rather than the ring of neighbours: a
        // vehicle's approach is resolved from FUN00762A21's own footprint, which extends
        // 1024 units, so a triangle whose immediate neighbours are all triggers can still
        // have ordinary ground inside boarding range. The case wants the vehicle that
        // genuinely has none, so it asks the same question the builder now asks.
        var buried = map.Triangles.FirstOrDefault(triangle =>
        {
            if (!catalog.EntranceTriangleIds.Contains(triangle.Id))
            {
                return false;
            }

            var centre = triangle.Centroid;
            var reach = WorldMapVehicleShoreApproach.FindContactPoints(map, 0, 6, centre.X, centre.Z);
            return reach.Count > 0 && reach.Keys.All(catalog.EntranceTriangleIds.Contains);
        });
        Equal(true, buried is not null, "the installed map must contain a fully enclosed trigger triangle");
        var sunkCentre = buried!.Centroid;
        var sunk = catalog
            .ReadTargets(
                WorldMapNavigationCategory.Transportation,
                start,
                [new WorldMapEntitySnapshot(
                    0x2000, 0, false, sunkCentre.X, sunkCentre.Y, sunkCentre.Z,
                    buried.TerrainId, buried.RegionId & 0x1F, 6, 0)])
            .SingleOrDefault(target => target.Label == "Buggy");
        if (sunk is not null)
        {
            Equal(0, sunk.ArrivalTriangleIds.Count,
                "a vehicle whose every candidate is a trigger keeps no arrival at all");
            Equal(0, sunk.NativeEntranceExemptions.Count, "and licenses no entrance either");
            Equal(false, guarded.TryBuildRoute(start, sunk, out _),
                "so there is no safe approach, and that is the answer");
            Contains("no traversable native arrival", guarded.LastDiagnostic,
                $"stated truthfully: {guarded.LastDiagnostic}");
        }

        // Selecting Gongaga itself must still walk onto Gongaga's own entrance.
        Equal(true, guarded.TryBuildRoute(start, gongaga, out var townPlan),
            $"Gongaga itself must remain reachable: {guarded.LastDiagnostic}");
        Equal(true, gongaga.ArrivalTriangleIds.Contains(townPlan.TargetTriangleId),
            "the selected town's route must end on its own entrance");
        var townCrossed = townPlan.TrianglePath
            .Where(id => catalog.EntranceTriangleIds.Contains(id) && !gongaga.ArrivalTriangleIds.Contains(id))
            .ToArray();
        Equal(0, townCrossed.Length,
            $"even the town route must not pass another town's door, crossed [{string.Join(',', townCrossed)}]");

        // Starting on a trigger must not trap the party: they can still be walked off it.
        var doorway = map.Triangles[gongaga.ArrivalTriangleIds.First()];
        var onTrigger = State(doorway.Centroid.X, doorway.Centroid.Y, doorway.Centroid.Z,
            doorway.TerrainId, doorway.TerrainScriptId, doorway.RegionId & 0x1F, 0);
        Equal(true, guarded.TryResolvePlayerTriangle(onTrigger, out var standing),
            "a party standing on a doorway must resolve");
        Equal(true, guarded.IsUnwantedEntrance(standing, vehicle.ArrivalTriangleIds) is false ||
                    guarded.IsUnwantedEntrance(standing, vehicle.ArrivalTriangleIds, standing) is false,
            "the triangle the party is standing on must never be treated as closed to them");
        Equal(true, guarded.TryBuildRoute(onTrigger, vehicle, out _),
            $"a party standing on a trigger must still be routed away from it: {guarded.LastDiagnostic}");
    }

    private static WorldMapStateSnapshot State(
        int x, int y, int z, int terrain, int terrainScript, int region, int model) =>
        new(3, 0, 0, 469, x, y, z, 0, 0, terrain, region, model, 30, 0,
            new FieldNavigationControlTransform(0))
        {
            TerrainScriptId = terrainScript
        };

    private static FieldWalkmeshRoutePlanner Planner(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int field, int[] shut)
    {
        var memory = new MutableBoundaryMemory(field, shut);
        return new FieldWalkmeshRoutePlanner(
            createWalkmeshReader(field),
            new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
    }

    private sealed class MutableBoundaryMemory
    {
        private const int FieldState = 0x02800000;
        private readonly Dictionary<int, byte> bytes = [];

        public MutableBoundaryMemory(int field, IEnumerable<int> triangles)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            for (var index = 0; index < 4; index++)
                bytes[FieldBoundaryStateReader.AddressFieldGlobalObjectPtr + index] = (byte)(FieldState >> (index * 8));
            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] = (byte)(ReadByte(address) | (1 << (triangle & 7)));
            }
        }

        /// <summary>The script releasing the door, as it does at 01:28:57.</summary>
        public void Clear()
        {
            for (var offset = 0; offset < FieldBoundaryStateReader.BoundaryByteCount; offset++)
                bytes[FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + offset] = 0;
        }

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void SequenceEqual(IReadOnlyList<int> expected, IReadOnlyList<int> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}]");
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label}: expected '{expected}' in '{actual}'");
    }
}
