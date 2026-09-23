using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Navigating from the Tiny Bronco to a town: to a shore where the game's own get-off puts
/// the party on ground that leads there, then on foot.
///
/// <para>The rule is read out of ff7_en.exe (see <see cref="WorldMapBroncoLanding"/>): a
/// short Cancel moves the boat 800 units along its model rotation, and the party lands
/// there if all five contact points - the centre and 350 units along each axis - are
/// riverside or beach. The logs hold three native get-offs to check it against:</para>
///
/// <list type="bullet">
/// <item>2026-09-22 16:09:28, boat 109072,102,162804, party 109923,258,162872.</item>
/// <item>2026-09-23 08:38:59, boat at rest 117815,0,167791, party 117489,75,168520.</item>
/// <item>2026-09-23 08:42:14, boat at rest 123254,0,170464, party 123073,111,171243,
/// camera 2193.</item>
/// </list>
/// </summary>
internal static class WorldMapBroncoLandingTests
{
    private const int BroncoEntity = 0x00E3A1C8;
    private const int PartyEntity = 0x00E3A288;

    private static readonly (string When, int BoatX, int BoatZ, int PartyX, int PartyZ, bool AtRest)[] NativeGetOffs =
    [
        ("2026-09-22 16:09:28", 109072, 162804, 109923, 162872, false),
        ("2026-09-23 08:38:59", 117815, 167791, 117489, 168520, true),
        ("2026-09-23 08:42:14", 123254, 170464, 123073, 171243, true),
    ];

    /// <summary>Where the boat was each time the player asked for Gongaga, and the camera.</summary>
    private static readonly (string When, int X, int Z, int Camera)[] BoatStarts =
    [
        ("08:36:08", 112773, 167101, 2273),
        ("08:38:58", 117815, 167791, 2449),
        ("08:42:12", 123254, 170464, 2193),
    ];

    private static readonly string[] Destinations = ["Gongaga", "Weapon Seller"];

    public static void Run()
    {
        TheReaderCarriesTheRotationTheLandingNeeds();
        TheProbeIsTheNativeRotation();
        Console.WriteLine("world map Tiny Bronco landing tests passed.");
    }

    public static void RunWithInstalledGameData()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            Console.WriteLine("world Tiny Bronco landing: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheNativeSurfaceTestFitsTheInstalledMap(map);
        TheLoggedGetOffsFollowTheNativeRule(map);
        NoLandingIsOfferedWithoutTheRotation(map, catalog);
        EveryLoggedBoatHasALandingForGongagaAndTheSeller(map, catalog);
        EveryLegOfTheBoatsRouteKeepsItOnWater(map, catalog);
        ALandingIsOnlyPromisedWhenTheBoatFacesIt(map, catalog);
        TheBoatIsTakenToALandingAndThePartyWalksIn(map, catalog);
        Console.WriteLine("world map Tiny Bronco landing tests passed with installed game data.");
    }

    /// <summary>
    /// +0x3C, +0x3E and +0x51 are read with the rest of the entity, and a host that cannot
    /// read them still gets the world state - just no landing promise.
    /// </summary>
    private static void TheReaderCarriesTheRotationTheLandingNeeds()
    {
        const int player = 0x0012_3000;
        var bytes = new Dictionary<int, byte>();
        void Write(int address, long value, int size)
        {
            for (var index = 0; index < size; index++)
            {
                bytes[address + index] = (byte)(value >> (index * 8));
            }
        }

        Write(WorldMapStateReader.AddressCurrentModule, WorldMapStateReader.WorldModule, 1);
        Write(WorldMapStateReader.AddressWorldMapType, 0, 4);
        Write(WorldMapStateReader.AddressWorldProgress, 0, 4);
        Write(WorldMapStateReader.AddressGameMoment, 566, 2);
        Write(WorldMapStateReader.AddressWorldPlayerEntityPointer, player, 4);
        Write(WorldMapStateReader.AddressWorldCameraFront, 2193, 4);
        Write(player + WorldMapStateReader.ContactEntityOffset, 0, 4);
        Write(player + WorldMapStateReader.PositionXOffset, 123254, 4);
        Write(player + WorldMapStateReader.PositionYOffset, 0, 4);
        Write(player + WorldMapStateReader.PositionZOffset, 170464, 4);
        Write(player + WorldMapStateReader.FacingOffset, -145, 2);
        Write(player + WorldMapStateReader.WalkmapTypeOffset, 6 | (1 << 5) | (5 << 9), 2);
        Write(player + WorldMapStateReader.DirectionOffset, -145, 2);
        Write(player + WorldMapStateReader.ModelIdOffset, 5, 1);
        Write(player + WorldMapStateReader.MovementSpeedOffset, 60, 1);

        var withoutRotation = new WorldMapStateReader(new ByteMemory(bytes)).Read();
        Equal(true, withoutRotation.IsUsable, "a world state without the rotation bytes is still a world state");
        Equal(false, withoutRotation.State.HasModelRotation, "but it carries no rotation to land by");

        Write(player + WorldMapStateReader.ModelRotationOffset, 3951, 2);
        Write(player + WorldMapStateReader.SlideRotationOffset, -4, 2);
        Write(player + WorldMapStateReader.EntityFlagsOffset, 0x03, 1);
        var read = new WorldMapStateReader(new ByteMemory(bytes)).Read();
        Equal(true, read.State.HasModelRotation, "the rotation is read with the entity");
        Equal((short)3951, read.State.ModelRotation, "entity +0x3C");
        Equal((short)-4, read.State.SlideRotation, "entity +0x3E");
        Equal((byte)0x03, read.State.EntityFlags, "entity +0x51");
        Equal((short)-145, read.State.Facing, "entity +0x40");
        Equal(true, read.Diagnostic.Contains("rotation=3951-4", StringComparison.Ordinal),
            $"and the log line records it, which the 2026-09-23 log could not; said \"{read.Diagnostic}\"");
    }

    /// <summary>FUN_00766417 / FUN_00753D00: (0, 0, 800) turned about Y.</summary>
    private static void TheProbeIsTheNativeRotation()
    {
        Equal((0, 800), WorldMapBroncoLanding.ProbeOffset(0), "rotation 0 probes along +Z");
        Equal((800, 0), WorldMapBroncoLanding.ProbeOffset(1024), "rotation 1024 probes along +X");
        Equal((0, -800), WorldMapBroncoLanding.ProbeOffset(2048), "rotation 2048 probes along -Z");
        Equal((-800, 0), WorldMapBroncoLanding.ProbeOffset(-1024), "rotation -1024 probes along -X");
    }

    /// <summary>
    /// FUN_0076085F takes a point as on a triangle when no edge's cross product is positive.
    /// That only works if the map is wound for it, which the installed one is.
    /// </summary>
    private static void TheNativeSurfaceTestFitsTheInstalledMap(WorldMapData map)
    {
        var withArea = 0;
        var found = 0;
        foreach (var triangle in map.Triangles)
        {
            if ((long)(triangle.Vertex1.X - triangle.Vertex0.X) * (triangle.Vertex2.Z - triangle.Vertex0.Z) ==
                (long)(triangle.Vertex2.X - triangle.Vertex0.X) * (triangle.Vertex1.Z - triangle.Vertex0.Z))
            {
                continue;
            }

            withArea++;
            var x = (triangle.Vertex0.X + triangle.Vertex1.X + triangle.Vertex2.X) / 3;
            var z = (triangle.Vertex0.Z + triangle.Vertex1.Z + triangle.Vertex2.Z) / 3;
            if (WorldMapBroncoLanding.TryFindSurface(map, x, z, out _))
            {
                found++;
            }
        }

        Equal(true, found >= withArea * 0.998,
            $"the native surface test must find ground under almost every centroid: {found} of {withArea}");
    }

    /// <summary>
    /// The three recorded get-offs, against the rule. Every one landed on a five-point
    /// footprint of riverside and beach, from a boat whose own footprint was water; the two
    /// taken at rest landed 800 units away, and the one with a settled camera landed where
    /// that camera's facing puts the probe.
    /// </summary>
    private static void TheLoggedGetOffsFollowTheNativeRule(WorldMapData map)
    {
        foreach (var (when, boatX, boatZ, partyX, partyZ, atRest) in NativeGetOffs)
        {
            Equal(true, WorldMapBroncoLanding.HasBoatFootprint(map, boatX, boatZ),
                $"{when}: the boat's own five points are on water the Bronco may occupy");
            Equal(true, WorldMapBroncoLanding.IsLandingFootprint(map, partyX, partyZ, out var landed),
                $"{when}: the party landed on five points of riverside or beach");
            Equal(true, WorldMapBroncoLanding.IsLandingGround(landed.TerrainId),
                $"{when}: the landing triangle is terrain 11 or 17, not {landed.TerrainId}");
            if (!atRest)
            {
                continue;
            }

            var dx = partyX - boatX;
            var dz = partyZ - boatZ;
            var distance = Math.Sqrt(dx * (double)dx + dz * (double)dz);
            Equal(true, Math.Abs(distance - WorldMapBroncoLanding.ProbeDistance) <= 2,
                $"{when}: a boat at rest lands the party 800 units away, not {distance:0}");
            var implied = (int)Math.Round(Math.Atan2(dx, dz) * 4096d / (2d * Math.PI));
            Equal(true, WorldMapBroncoLanding.TryPredictDirectLanding(
                    map, boatX, boatZ, implied, out var predicted, out var landingX, out var landingZ),
                $"{when}: the unturned probe at rotation {implied} is a landing the game accepts");
            Equal(true, Math.Abs(landingX - partyX) <= 3 && Math.Abs(landingZ - partyZ) <= 3,
                $"{when}: and it is where the party was put: predicted {landingX},{landingZ}");
            Equal(landed.Id, predicted.Id, $"{when}: on the same triangle");
        }

        // 08:42:14 had been at rest with its camera at 2193. With Up the last thing pressed,
        // FUN_0074EA48 leaves the facing at 0x800 - camera, and at rest the drawn rotation
        // settles on the facing: -145. That puts the party within a few units of the log.
        Equal(true, WorldMapBroncoLanding.TryPredictDirectLanding(
                map, 123254, 170464, 0x800 - 2193, out _, out var fromCameraX, out var fromCameraZ),
            "08:42:14: the camera's own facing gives a landing");
        Equal(true, Math.Abs(fromCameraX - 123073) <= 8 && Math.Abs(fromCameraZ - 171243) <= 8,
            $"08:42:14: and it is where the log has the party: {fromCameraX},{fromCameraZ}");
    }

    /// <summary>
    /// The landing is wherever the boat is facing. Without the live rotation, or with the
    /// flag that switches the Bronco to a different probe, nothing is promised.
    /// </summary>
    private static void NoLandingIsOfferedWithoutTheRotation(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");
        var (_, x, z, camera) = BoatStarts[2];
        var boat = BoatState(map, x, z, camera, rotation: 0x800 - camera, facing: 0x800 - camera);
        Equal(true, WorldMapBroncoLanding.TryPlan(runtime.Planner, map, boat, gongaga, out _),
            "the live boat has a landing for Gongaga");
        Equal(false, WorldMapBroncoLanding.TryPlan(runtime.Planner, map, boat with { HasModelRotation = false }, gongaga, out _),
            "a boat whose rotation could not be read is never sent to a landing it cannot confirm");
        Equal(false, WorldMapBroncoLanding.TryPlan(runtime.Planner, map, boat with { EntityFlags = 0x80 }, gongaga, out _),
            "flag 0x80 gives the Bronco a 100-unit, water-only get-off, which is not this landing");
        Equal(false, WorldMapBroncoLanding.CanPredict(boat with { PlayerModelId = 0 }),
            "and nobody on foot is predicted a boat's landing");
    }

    /// <summary>
    /// From every place the log has the boat, Gongaga and the Weapon Seller have a landing:
    /// the boat fits where it stops, the landing holds a quarter turn's worth of error either
    /// side, it is not somebody's field trigger, and the walk from it builds.
    /// </summary>
    private static void EveryLoggedBoatHasALandingForGongagaAndTheSeller(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        foreach (var (when, x, z, camera) in BoatStarts)
        {
            var boat = BoatState(map, x, z, camera, rotation: 0x800 - camera, facing: 0x800 - camera);
            foreach (var label in Destinations)
            {
                var destination = catalog.Locations.Single(target => target.Label == label);
                Equal(false, runtime.Planner.CanReach(boat, destination),
                    $"{when} {label}: no town can be sailed into");
                Equal(true, WorldMapBroncoLanding.TryPlan(runtime.Planner, map, boat, destination, out var plan),
                    $"{when} {label}: a landing that leads there on foot must be found");
                var option = plan.Option;
                Equal(true, WorldMapBroncoLanding.HasBoatFootprint(map, option.X, option.Z),
                    $"{when} {label}: the boat fits where it is told to stop");
                Equal(true, WorldMapBroncoLanding.HasBoatFootprint(map, plan.ApproachStart.X, plan.ApproachStart.Z),
                    $"{when} {label}: and where its straight run in starts");
                for (var turn = -WorldMapBroncoLanding.RotationTolerance;
                     turn <= WorldMapBroncoLanding.RotationTolerance;
                     turn += 16)
                {
                    Equal(true, WorldMapBroncoLanding.TryPredictDirectLanding(
                            map, option.X, option.Z, option.Rotation + turn, out var landing, out _, out _),
                        $"{when} {label}: facing {turn} off the planned heading still lands");
                    Equal(true, plan.DestinationFootComponents.Contains(
                            runtime.Planner.GetComponentId(0, map.WorldMapType, landing.Id)),
                        $"{when} {label}: facing {turn} off still lands on ground that leads there");
                    Equal(true, landing.TerrainScriptId < 3,
                        $"{when} {label}: facing {turn} off does not land on a field trigger");
                }

                Equal(true, WorldMapBroncoLanding.TryFindSurface(map, option.LandingX, option.LandingZ, out var ground),
                    $"{when} {label}: the landing has ground under it");
                var onFoot = new WorldMapStateSnapshot(
                    3, 0, 0, 566, option.LandingX,
                    WorldMapBroncoLanding.SurfaceHeight(ground, option.LandingX, option.LandingZ),
                    option.LandingZ, 0, 0, ground.TerrainId, ground.RegionId & 31, 0, 30, camera,
                    new FieldNavigationControlTransform(0))
                {
                    TerrainScriptId = ground.TerrainScriptId
                };
                Equal(true, runtime.Planner.TryBuildRoute(onFoot, destination, out _),
                    $"{when} {label}: and the walk from the landing builds: {runtime.Planner.LastDiagnostic}");
            }
        }
    }

    /// <summary>
    /// The boat's route is sailable, not merely wet. FUN_00751EFC moves model 5 only where
    /// its centre and the points 350 units along each axis are all water; a string pulled
    /// tight through the triangles hugs the inside of every bend, and from the 08:42:12 boat
    /// the trip to the seller's own beach wedged it against the bank at 132047,170053 with
    /// no fanned step left. Every leg after the first - which starts wherever the boat
    /// happens to be - has to keep the whole footprint on water one native frame at a time.
    /// </summary>
    private static void EveryLegOfTheBoatsRouteKeepsItOnWater(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        foreach (var (when, x, z, camera) in BoatStarts)
        {
            foreach (var label in Destinations)
            {
                var runtime = CreateShippedRuntime(map, catalog);
                var boat = BoatState(map, x, z, camera, rotation: 0x800 - camera, facing: 0x800 - camera);
                var now = new DateTime(2026, 9, 23, 8, 42, 12, DateTimeKind.Utc);
                for (var step = 0; step < catalog.Locations.Count; step++)
                {
                    if ((runtime.Navigation.HandleAction(FieldNavigationAction.NextTarget, boat, now)?.Speech ?? string.Empty)
                        .Contains($", {label}.", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, boat, now);
                var route = runtime.Navigation.Probe.Route;
                Equal(true, route is not null && route.Waypoints.Count > 0, $"{when} {label}: a boat route to the landing");
                for (var leg = 1; leg < route!.Waypoints.Count; leg++)
                {
                    var from = route.Waypoints[leg - 1];
                    var to = route.Waypoints[leg];
                    var dx = WorldMapTargetCatalog.WrappedDelta(from.X, to.X, map.WrapWidth);
                    var dz = WorldMapTargetCatalog.WrappedDelta(from.Z, to.Z, map.WrapHeight);
                    var samples = (int)Math.Ceiling(Math.Sqrt(dx * (double)dx + dz * (double)dz) / 60d);
                    for (var sample = 1; sample < samples; sample++)
                    {
                        var px = from.X + (int)Math.Round(dx * sample / (double)samples);
                        var pz = from.Z + (int)Math.Round(dz * sample / (double)samples);
                        Equal(true, WorldMapBroncoLanding.HasBoatFootprint(map, px, pz),
                            $"{when} {label}: leg {leg} from {from.X},{from.Z} to {to.X},{to.Z} puts the boat's " +
                            $"footprint on land at {px},{pz}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Being at the right place is not enough: the landing is wherever the boat faces. At
    /// the planned spot facing a quarter turn away, the controller says so and presses
    /// nothing; when the player turns the boat to face the landing, it says that too - and
    /// while the drawn rotation is still swinging through ground that leads elsewhere, it
    /// waits rather than promise.
    /// </summary>
    private static void ALandingIsOnlyPromisedWhenTheBoatFacesIt(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");
        var (_, startX, startZ, camera) = BoatStarts[2];
        var start = BoatState(map, startX, startZ, camera, rotation: 0x800 - camera, facing: 0x800 - camera);
        Equal(true, WorldMapBroncoLanding.TryPlan(runtime.Planner, map, start, gongaga, out var plan),
            "a landing for Gongaga");
        var option = plan.Option;
        var feet = plan.DestinationFootComponents;
        var planned = BoatState(map, option.X, option.Z, camera, option.Rotation, option.Rotation);
        Equal(true, WorldMapBroncoLanding.IsLinedUp(map, runtime.Planner, planned, feet, out _),
            "facing the planned heading at the spot is lined up");
        foreach (var away in new[] { 1024, 2048, -1024 })
        {
            var turned = BoatState(map, option.X, option.Z, camera, option.Rotation + away, option.Rotation + away);
            Equal(false, WorldMapBroncoLanding.IsLinedUp(map, runtime.Planner, turned, feet, out _),
                $"facing {away} away from it is not");
        }

        Equal(false, WorldMapBroncoLanding.IsLinedUp(map, runtime.Planner,
                planned with { ModelRotation = (short)(option.Rotation + 1024) }, feet, out _),
            "nor is a boat still swinging round to face it: every rotation on the way has to land");

        // The controller, at the spot facing away.
        var now = new DateTime(2026, 9, 23, 8, 42, 12, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Locations)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, start, now);
        }

        for (var step = 0; step < catalog.Locations.Count; step++)
        {
            if ((runtime.Navigation.HandleAction(FieldNavigationAction.NextTarget, start, now)?.Speech ?? string.Empty)
                .Contains(", Gongaga.", StringComparison.Ordinal))
            {
                break;
            }
        }

        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        Equal(true, runtime.Navigation.BeaconEnabled, "navigation to the landing starts");
        var facingAway = BoatState(map, option.X, option.Z, camera, option.Rotation + 1024, option.Rotation + 1024);
        var said = new List<WorldMapNavigationOutput>();
        for (var sample = 0; sample < 40; sample++)
        {
            if (runtime.Navigation.Observe(facingAway, now.AddMilliseconds(sample * 33), automaticWalkActive: true) is { } output)
            {
                said.Add(output);
            }

            if (said.Any(output => output.Speech?.Contains("not lined up", StringComparison.Ordinal) == true))
            {
                break;
            }

            // Pinned at the spot facing away, whatever is pressed: the replay is of a boat
            // that arrives wrong, not of the steering.
            runtime.Navigation.TryResolveAutomaticInput(facingAway, out _);
        }

        var notLinedUp = said.LastOrDefault();
        Equal(true, notLinedUp.Speech?.Contains("not lined up", StringComparison.Ordinal) == true,
            $"arriving facing away is said plainly; said \"{string.Join(" / ", said.Select(output => output.Speech))}\"");
        Equal(true, notLinedUp.StopAutoWalk, "and auto walk is released");
        Equal(false, said.Any(output => output.Speech?.Contains("Press Cancel", StringComparison.Ordinal) == true),
            "and nobody is told to get off");
        Equal(false, runtime.Navigation.TryResolveAutomaticInput(facingAway, out _),
            "nothing is steered while the player sorts it out");

        // The player turns the boat to face the landing.
        var turnedBack = runtime.Navigation.Observe(planned, now.AddSeconds(5), automaticWalkActive: false);
        Equal(true, turnedBack?.Speech?.Contains("Lined up to land for Gongaga. Press Cancel once", StringComparison.Ordinal) == true,
            $"turning to face it is announced; said \"{turnedBack?.Speech}\"");
        var repeated = runtime.Navigation.HandleAction(FieldNavigationAction.RepeatTarget, planned, now.AddSeconds(6));
        Equal(true, repeated?.Speech?.Contains("Press Cancel once", StringComparison.Ordinal) == true,
            $"and repeating the target says it again; said \"{repeated?.Speech}\"");
    }

    /// <summary>
    /// The whole trip, replayed under the native rules: the controller drives the boat, the
    /// boat moves as FUN_0074EA48 and FUN_00751EFC move it, the controller stops at the
    /// landing and says to press Cancel, the get-off lands the party where FUN_00766417 puts
    /// them, and the controller walks them in. Both camera modes: 0 keeps the camera at 0
    /// and faces the way the party moves; 2 turns the camera with left and right and faces
    /// between the camera and the move.
    /// </summary>
    private static void TheBoatIsTakenToALandingAndThePartyWalksIn(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var seller = map.Triangles
            .Where(t => t.MeshX == 15 && t.MeshZ == 21 && t.TerrainScriptId == 7 &&
                        (long)(t.Vertex1.X - t.Vertex0.X) * (t.Vertex2.Z - t.Vertex0.Z) !=
                        (long)(t.Vertex2.X - t.Vertex0.X) * (t.Vertex1.Z - t.Vertex0.Z))
            .ToArray();
        foreach (var (when, startX, startZ, loggedCamera) in BoatStarts)
        {
            foreach (var label in Destinations)
            {
                foreach (var cameraMode in new[] { 0, 2 })
                {
                    var name = $"{when} {label} camera mode {cameraMode}";
                    var runtime = CreateShippedRuntime(map, catalog);
                    var boat = new NativeBoat(map, startX, startZ, cameraMode == 2 ? loggedCamera : 0, cameraMode);
                    var now = new DateTime(2026, 9, 23, 8, 36, 8, DateTimeKind.Utc);
                    var state = boat.Snapshot();
                    while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Locations)
                    {
                        runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, now);
                    }

                    var selected = string.Empty;
                    for (var step = 0; step < catalog.Locations.Count && !selected.Contains($", {label}.", StringComparison.Ordinal); step++)
                    {
                        selected = runtime.Navigation
                            .HandleAction(FieldNavigationAction.NextTarget, state, now)?.Speech ?? string.Empty;
                    }

                    Equal(true, selected.Contains("By Tiny Bronco to a shore landing, then on foot", StringComparison.Ordinal),
                        $"{name}: selecting it aboard says how it is reached; said \"{selected}\"");
                    var started = runtime.Navigation
                        .HandleAction(FieldNavigationAction.ToggleBeacon, state, now)?.Speech ?? string.Empty;
                    Equal(true, runtime.Navigation.BeaconEnabled, $"{name}: navigation starts; said \"{started}\"");
                    Equal(true, started.Contains("shore landing", StringComparison.Ordinal),
                        $"{name}: and says it is going to a landing; said \"{started}\"");
                    var plannedLanding = runtime.Navigation.LastDiagnostic;
                    var trace = new Queue<string>();

                    // The boat, driven by the controller until it hands over.
                    var handedOver = false;
                    var spoken = started;
                    var frame = 0;
                    for (; frame < 6000 && !handedOver; frame++)
                    {
                        var output = runtime.Navigation.Observe(
                            state, now.AddMilliseconds(frame * 33), automaticWalkActive: true);
                        if (output is { } said && said.Speech is { } text)
                        {
                            spoken = text;
                            Equal(false, text.Contains("Could not get closer", StringComparison.Ordinal),
                                $"{name}: auto walk gave up at {state.X},{state.Z}: \"{text}\"" + $" plan: {plannedLanding}; route: {string.Join(" ", runtime.Navigation.Probe.Route!.Waypoints.Select(w => $"{w.X},{w.Z}"))}; last frames: {string.Join(" | ", trace)}" + " keys: " + DescribeKeys(map, runtime.Planner, state));
                            Equal(false, text.Contains("not lined up", StringComparison.Ordinal),
                                $"{name}: arrived at {state.X},{state.Z} rotation {state.ModelRotation}" +
                                $"{state.SlideRotation:+0;-0;+0} facing {state.Facing} not lined up: \"{text}\" " +
                                $"({runtime.Navigation.LastDiagnostic})" + $" plan: {plannedLanding}; last frames: {string.Join(" | ", trace)}");
                            if (text.Contains("Press Cancel once", StringComparison.Ordinal))
                            {
                                Equal(true, said.StopAutoWalk, $"{name}: the handover releases auto walk");
                                handedOver = true;
                                break;
                            }
                        }

                        var hasInput = runtime.Navigation.TryResolveAutomaticInput(state, out var input);
                        trace.Enqueue($"{state.X},{state.Z} f{state.Facing} r{state.ModelRotation} {(hasInput ? input : FieldNavigationInput.None)}");
                        if (trace.Count > 14)
                        {
                            trace.Dequeue();
                        }

                        boat.Frame(hasInput ? input : FieldNavigationInput.None);
                        state = boat.Snapshot();
                        Equal(true, WorldMapBroncoLanding.IsBoatWater(state.TerrainId),
                            $"{name}: the boat never leaves the water it may occupy");
                    }

                    Equal(true, handedOver,
                        $"{name}: the boat must be brought to a landing and handed over within {frame} frames; " +
                        $"it ended at {state.X},{state.Z} saying \"{spoken}\" ({runtime.Navigation.LastDiagnostic})" + $" plan: {plannedLanding}; route: {string.Join(" ", runtime.Navigation.Probe.Route!.Waypoints.Select(w => $"{w.X},{w.Z}"))}; last frames: {string.Join(" | ", trace)}");

                    // Held there with nothing pressed while the player gets off, and still
                    // lined up as the drawn rotation settles.
                    for (var wait = 0; wait < 45; wait++)
                    {
                        Equal(false, runtime.Navigation.TryResolveAutomaticInput(state, out _),
                            $"{name}: nothing is steered while waiting for the player's Cancel");
                        var output = runtime.Navigation.Observe(
                            state, now.AddMilliseconds((frame + wait) * 33), automaticWalkActive: false);
                        Equal(true, output?.Speech is null,
                            $"{name}: the landing stays lined up while the boat settles; said \"{output?.Speech}\"");
                        boat.Frame(FieldNavigationInput.None);
                        state = boat.Snapshot();
                    }

                    // The player's Cancel: FUN_00766417 and the ground follow, from the
                    // boat's real rotation at that moment.
                    var rotation = state.ModelRotation + state.SlideRotation;
                    Equal(true, WorldMapBroncoLanding.TryPredictDirectLanding(
                            map, state.X, state.Z, rotation, out var ground, out var landingX, out var landingZ),
                        $"{name}: the get-off from {state.X},{state.Z} at rotation {rotation} lands");
                    var destination = catalog.Locations.Single(target => target.Label == label);
                    Equal(true, WorldMapBroncoLanding.DestinationFootComponents(runtime.Planner, map, destination)
                            .Contains(runtime.Planner.GetComponentId(0, map.WorldMapType, ground.Id)),
                        $"{name}: on ground that leads to {label}");

                    var direction = -(boat.Camera / 16);
                    var party = new WorldMapStateSnapshot(
                        3, 0, 0, 566, landingX, WorldMapBroncoLanding.SurfaceHeight(ground, landingX, landingZ),
                        landingZ, (short)rotation, (short)rotation, ground.TerrainId, ground.RegionId & 31, 0, 30,
                        boat.Camera, new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
                    {
                        TerrainScriptId = ground.TerrainScriptId,
                        HasModelRotation = true,
                        ModelRotation = (short)rotation,
                        NativePlayerEntityPointer = PartyEntity
                    };
                    var ashore = runtime.Navigation.Observe(party, now.AddMilliseconds((frame + 46) * 33),
                        automaticWalkActive: true);
                    Equal(true, runtime.Navigation.BeaconEnabled,
                        $"{name}: on foot the route to {label} carries on; said \"{ashore?.Speech}\"");
                    Equal(false, (ashore?.Speech ?? string.Empty).Contains("unavailable", StringComparison.OrdinalIgnoreCase),
                        $"{name}: and it is not refused; said \"{ashore?.Speech}\"");

                    // And walked in.
                    runtime.Planner.TryResolvePlayerTriangle(party, out var triangle);
                    var arrived = false;
                    var walked = party;
                    for (var step = 0; step < 6000 && !arrived; step++)
                    {
                        var output = runtime.Navigation.Observe(
                            walked, now.AddMilliseconds((frame + 50 + step) * 33), automaticWalkActive: true);
                        if (output?.Speech is { } text)
                        {
                            spoken = text;
                            Equal(false, text.Contains("Could not get closer", StringComparison.Ordinal),
                                $"{name}: the walk from the landing gave up at {walked.X},{walked.Z}: \"{text}\"");
                            if (text.Contains($"Arrived at {label}", StringComparison.Ordinal))
                            {
                                arrived = true;
                                break;
                            }
                        }

                        if (!runtime.Navigation.TryResolveAutomaticInput(walked, out var input))
                        {
                            continue;
                        }

                        var (dx, dz) = NativeStep(input, walked.CameraFront, 30);
                        if (label == "Weapon Seller" && seller.Any(t => Contains(t, walked.X + dx, walked.Z + dz)))
                        {
                            // The seller is entered through its wall; the log's witness of it.
                            walked = walked with { TerrainId = 16, TerrainScriptId = 7 };
                            continue;
                        }

                        walked = WorldMapVehicleApproachReplayTests.MoveOnRawNativeTriangles(
                            map, walked, ref triangle, dx, dz) with
                        {
                            CameraFront = walked.CameraFront,
                            ControlTransform = walked.ControlTransform
                        };
                    }

                    Equal(true, arrived,
                        $"{name}: the party must be walked from the landing into {label}; " +
                        $"ended at {walked.X},{walked.Z} saying \"{spoken}\"");
                }
            }
        }
    }

    /// <summary>
    /// The Tiny Bronco under FUN_0074EA48's movement and facing, FUN_00751EFC's ground
    /// follow with the model 5 footprint, and FUN_00761C07 / FUN_00761DF5's easing.
    /// </summary>
    private sealed class NativeBoat
    {
        private const int Speed = 0x3C;
        private readonly WorldMapData map;
        private readonly int cameraMode;
        private int facing;
        private int modelRotation;
        private int slideRotation;
        private int lastTurn;
        private int preferredSide = 1;

        public NativeBoat(WorldMapData map, int x, int z, int camera, int cameraMode)
        {
            this.map = map;
            this.cameraMode = cameraMode;
            X = x;
            Z = z;
            Camera = camera;
            facing = 0x800 - camera;
            modelRotation = Normalize(facing);
        }

        public int X { get; private set; }

        public int Z { get; private set; }

        public int Camera { get; private set; }

        public WorldMapStateSnapshot Snapshot()
        {
            if (!WorldMapBroncoLanding.TryFindSurface(map, X, Z, out var surface))
            {
                throw new InvalidOperationException($"the boat left the map at {X},{Z}");
            }

            var direction = -(Camera / 16);
            return new WorldMapStateSnapshot(
                3, 0, 0, 566, X, WorldMapBroncoLanding.SurfaceHeight(surface, X, Z), Z,
                (short)facing, (short)facing, surface.TerrainId, surface.RegionId & 31,
                WorldMapBroncoLanding.BroncoModelId, Speed, Camera,
                new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
            {
                TerrainScriptId = surface.TerrainScriptId,
                HasModelRotation = true,
                ModelRotation = (short)modelRotation,
                SlideRotation = (short)slideRotation,
                EntityFlags = 0,
                NativePlayerEntityPointer = BroncoEntity
            };
        }

        public void Frame(FieldNavigationInput input)
        {
            var up = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight;
            var down = input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight;
            var left = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft;
            var right = input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight;

            // Input direction and stick vector.
            var turn = -1;
            double moveX = 0;
            double moveZ = 0;
            if (left)
            {
                moveX = -Speed;
                turn = -0x400;
            }

            if (right)
            {
                moveX = Speed;
                turn = 0x400;
            }

            if (up)
            {
                if (turn == -1)
                {
                    moveZ = -Speed;
                    turn = 0x800;
                }
                else
                {
                    moveX = (int)(moveX * 3) >> 2;
                    moveZ = (-Speed * 3) >> 2;
                    turn += turn >> 1;
                }
            }

            if (down)
            {
                if (turn == -1)
                {
                    moveZ = Speed;
                    turn = 0;
                }
                else
                {
                    moveX = (int)(moveX * 3) >> 2;
                    moveZ = (Speed * 3) >> 2;
                    turn -= turn >> 1;
                }
            }

            if (cameraMode == 2)
            {
                // Left and right steer the camera; reversing swaps them.
                var factor = up || down ? 1 : 2;
                if (down ? right : left)
                {
                    Camera -= 8 * factor;
                }

                if (down ? left : right)
                {
                    Camera += 8 * factor;
                }

                Camera = ((Camera % 0x1000) + 0x1000) % 0x1000;
                var eased = lastTurn;
                if (turn != -1)
                {
                    eased = (short)(turn + 0x800) > 0x800 ? turn - 0x800 : turn + 0x800;
                    if (!down)
                    {
                        eased >>= 1;
                    }

                    lastTurn = eased;
                }

                facing = 0x800 - Camera + eased;
            }
            else if (turn != -1)
            {
                facing = turn;
            }

            // Rotated by the negative camera angle, then truncated like the native __ftol.
            var angle = Camera * Math.PI * 2d / 4096d;
            var deltaX = (int)(moveX * Math.Cos(angle) - moveZ * Math.Sin(angle));
            var deltaZ = (int)(moveX * Math.Sin(angle) + moveZ * Math.Cos(angle));

            var moved = false;
            var acceptedTurn = 0;
            if (deltaX != 0 || deltaZ != 0)
            {
                if (WorldMapBroncoLanding.HasBoatFootprint(map, X + deltaX, Z + deltaZ))
                {
                    X += deltaX;
                    Z += deltaZ;
                    moved = true;
                }
                else
                {
                    foreach (var side in new[] { preferredSide, -preferredSide })
                    {
                        for (var sample = 1; sample < 8 && !moved; sample++)
                        {
                            var slide = side * sample * 160;
                            var radians = slide * Math.PI * 2d / 4096d;
                            var sx = (int)(deltaX * Math.Cos(radians) + deltaZ * Math.Sin(radians));
                            var sz = (int)(-deltaX * Math.Sin(radians) + deltaZ * Math.Cos(radians));
                            if (WorldMapBroncoLanding.HasBoatFootprint(map, X + sx, Z + sz))
                            {
                                X += sx;
                                Z += sz;
                                moved = true;
                                acceptedTurn = slide;
                                preferredSide = side;
                            }
                        }

                        if (moved)
                        {
                            break;
                        }
                    }
                }
            }

            X = ((X % map.WrapWidth) + map.WrapWidth) % map.WrapWidth;
            Z = ((Z % map.WrapHeight) + map.WrapHeight) % map.WrapHeight;

            // FUN_00761DF5 at thirty frames a second.
            slideRotation = moved
                ? (slideRotation * 15 + acceptedTurn) >> 4
                : (slideRotation * 3 + acceptedTurn) >> 2;

            // FUN_00761C07: an eighth of the way to the facing, the short way round.
            var target = facing < 0 ? facing + 0x1000 : facing;
            var direct = Math.Abs(modelRotation - target);
            var below = Math.Abs(modelRotation - target + 0x1000);
            var above = Math.Abs(modelRotation - target - 0x1000);
            if (below < direct)
            {
                target = above < below ? target + 0x1000 : target - 0x1000;
            }
            else if (above < direct)
            {
                target += 0x1000;
            }

            modelRotation = Normalize((modelRotation * 7 + target) >> 3);
        }

        private static int Normalize(int rotation) => ((rotation % 0x1000) + 0x1000) % 0x1000;
    }

    private static string DescribeKeys(WorldMapData map, WorldMapRoutePlanner planner, WorldMapStateSnapshot state)
    {
        var parts = new List<string>();
        foreach (var key in Enum.GetValues<FieldNavigationInput>().Where(k => k is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft))
        {
            var stick = FieldNavigationMovementObserver.ToStickDirection(key);
            var scale = stick.X != 0f && stick.Y != 0f ? 0.75d : 1d;
            var angle = state.CameraFront * Math.PI * 2d / 4096d;
            var wx = (Math.Sign(stick.X) * Math.Cos(angle) - Math.Sign(stick.Y) * Math.Sin(angle)) * scale;
            var wz = (Math.Sign(stick.X) * Math.Sin(angle) + Math.Sign(stick.Y) * Math.Cos(angle)) * scale;
            string At(int distance)
            {
                var x = state.X + (int)Math.Round(wx * distance);
                var z = state.Z + (int)Math.Round(wz * distance);
                return $"{distance}:{(WorldMapBroncoLanding.HasBoatFootprint(map, x, z) ? "fits" : "hits")}/{(planner.CanTraverseSegment(state, new WorldMapRouteWaypoint(x, state.Y, z)) ? "seg" : "noseg")}";
            }

            parts.Add($"{key}({wx:0.00},{wz:0.00}) {At(60)} {At(120)}");
        }

        return string.Join("; ", parts);
    }

    private sealed class ByteMemory(IReadOnlyDictionary<int, byte> bytes) : ILegacyAddressSpace
    {
        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue((int)virtualAddress + index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }
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

    private static bool Contains(WorldMapTriangle t, double x, double z)
    {
        double Side(WorldMapVertex a, WorldMapVertex b) => (x - b.X) * (a.Z - b.Z) - (a.X - b.X) * (z - b.Z);
        var first = Side(t.Vertex0, t.Vertex1);
        var second = Side(t.Vertex1, t.Vertex2);
        var third = Side(t.Vertex2, t.Vertex0);
        return (first >= 0 && second >= 0 && third >= 0) || (first <= 0 && second <= 0 && third <= 0);
    }

    private static WorldMapStateSnapshot BoatState(WorldMapData map, int x, int z, int camera, int rotation, int facing)
    {
        if (!WorldMapBroncoLanding.TryFindSurface(map, x, z, out var surface))
        {
            throw new InvalidOperationException($"no ground under the logged boat at {x},{z}");
        }

        var direction = -(camera / 16);
        return new WorldMapStateSnapshot(
            3, 0, 0, 566, x, WorldMapBroncoLanding.SurfaceHeight(surface, x, z), z,
            (short)facing, (short)facing, surface.TerrainId, surface.RegionId & 31,
            WorldMapBroncoLanding.BroncoModelId, 60, camera,
            new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
        {
            TerrainScriptId = surface.TerrainScriptId,
            HasModelRotation = true,
            ModelRotation = (short)rotation,
            NativePlayerEntityPointer = BroncoEntity
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
                $"World map Tiny Bronco landing - {label}: expected {expected}, got {actual}.");
        }
    }
}
