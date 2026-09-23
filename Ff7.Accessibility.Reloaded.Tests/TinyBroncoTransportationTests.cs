using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The party got off the Tiny Bronco and Transportation went silent.
///
/// <para>From <c>ff7_accessibility_steam2026_x64 (8).log</c>: at 16:06:46 the category
/// spoke <c>Transportation, Buggy. down 3</c> while the player entity was
/// <c>0x00E3A1C8, model=5</c> - the party was flying the Bronco. At 16:09:28 the player
/// entity became <c>0x00E3A288, model=0</c> and the list went from nine entities to ten,
/// because the Bronco keeps the entity the player had been. Every reading of the category
/// from 16:09:32 to the end of the log is <c>Transportation: none available</c>, and the
/// Buggy disappeared along with the Bronco.</para>
///
/// <para>Two separate faults, each proved from that log and each with its own case
/// below.</para>
/// </summary>
internal static class TinyBroncoTransportationTests
{
    // The two world states either side of the disembark, exactly as the log recorded them.
    private const int BroncoX = 109072;
    private const int BroncoY = 102;
    private const int BroncoZ = 162804;
    private const int BroncoTerrain = 5;
    private const int PartyX = 109923;
    private const int PartyY = 258;
    private const int PartyZ = 162872;
    private const int PartyTerrain = 11;
    private const int Region = 4;

    // The entity the party had been flying, which the native disembark leaves parked.
    private const uint BroncoEntity = 0x00E3A1C8;

    // The one triangle the old neighbour-ring rule offered: the far bank of the river,
    // on a separate 1630-triangle island, with no position at all inside the boat's mask.
    private const int WrongBankTriangle = 89731;

    public static void Run()
    {
        AMovingEntityDoesNotDiscardTheParkedVehiclesBesideIt();
        AListThatIsRelinkedMidReadIsStillRefused();
        Console.WriteLine("tiny bronco transportation tests passed.");
    }

    public static void RunWithInstalledGameData()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            Console.WriteLine("tiny bronco: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheParkedBroncoStaysOnTheTransportationList(map, catalog);
        TheApproachIsTheShoreTheNativeMaskReaches(map, catalog);
        TheWalkBackToTheBoatEndsInNativeContact(map, catalog);
        ABoatNothingCanTouchIsStillRefused(map, catalog);
        TheSelectedVehicleIsNotDetouredAroundItself(map, catalog);
        TheWitnessedNativeContactEndsTheApproach(map, catalog);
        AWitnessThatDoesNotNameThisBoatIsNotAnArrival(map, catalog);
        ABoatThatIsGoneOrRiddenStopsSteering(map, catalog);
        ABoatThatDriftsInsideItsArrivalTriangleIsReplanned(map, catalog);
        AShoreThatIsOnlyAFieldEntranceIsNoApproach(map, catalog);
        ARemountedBoatIsNotAParkedOne(map, catalog);
        AnUnreachablePlaceIsStillNotListed(map, catalog);
        Console.WriteLine("tiny bronco transportation tests passed with installed game data.");
    }

    /// <summary>
    /// The fault the user reported: standing where the game had just put them, the boat
    /// they had been flying was not on the list at all.
    /// </summary>
    private static void TheParkedBroncoStaysOnTheTransportationList(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var onFoot = State(PartyX, PartyY, PartyZ, PartyTerrain, 0, Region, 0);
        runtime.UpdateEntities([ParkedBronco()]);

        var catalogTargets = runtime.Catalog.ReadTargets(
            WorldMapNavigationCategory.Transportation, onFoot, runtime.Entities);
        Equal(1, catalogTargets.Count,
            "the catalog must build the parked Bronco from the live entity");
        Equal("Tiny Bronco", catalogTargets[0].Label, "and it must be the Bronco");
        Equal(true, runtime.Planner.CanReach(onFoot, catalogTargets[0]),
            "and from the shore the game dismounted them onto it must be walkable to: " +
            runtime.Planner.LastDiagnostic);

        var now = new DateTime(2026, 9, 22, 16, 9, 32, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, onFoot, now);
        }

        var spoken = runtime.Navigation
            .HandleAction(FieldNavigationAction.RepeatTarget, onFoot, now)!.Value.Speech;
        Equal(false, spoken.Contains("none available", StringComparison.OrdinalIgnoreCase),
            $"the parked Bronco must stay on the Transportation list; the category said \"{spoken}\"");
        Equal(true, spoken.Contains("Tiny Bronco", StringComparison.Ordinal),
            $"and it must be named, so the player knows the boat is still there; said \"{spoken}\"");
    }

    /// <summary>
    /// The approach is the ground the native mask reaches, not a ring of neighbours.
    ///
    /// <para>Ghidra <c>FUN_00762A21</c>: an 8x8 mask per model over 256-unit cells, contact
    /// when either participant's mask covers the other. The Tiny Bronco's row was read out
    /// of the installed executable at <c>0096DDB0 + 5 * 8</c> as
    /// <c>00 18 3c 7e 7e 3c 18 00</c>. Sampling the installed geometry against it, the far
    /// bank <c>t89731</c> - the only triangle the old ring rule offered - holds no position
    /// in contact at all, while <c>t89734</c> and <c>t89722</c>, the bank the party is
    /// standing on, hold thousands between them.</para>
    /// </summary>
    private static void TheApproachIsTheShoreTheNativeMaskReaches(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var onFoot = State(PartyX, PartyY, PartyZ, PartyTerrain, 0, Region, 0);
        runtime.UpdateEntities([ParkedBronco()]);
        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, onFoot, runtime.Entities)
            .Single(candidate => candidate.Label == "Tiny Bronco");

        Equal(true, target.ArrivalTriangleIds.Count > 0,
            "the boat must have an approach, or it is being hidden again");
        Equal(false, target.ArrivalTriangleIds.Contains(WrongBankTriangle),
            $"the far bank t{WrongBankTriangle} has no position inside the boat's native mask " +
            "and must not be offered as the way to it");

        // Every triangle offered must be somewhere the party can stand and be touching the
        // boat. Not "near", not "adjacent" - the native test has to say yes.
        foreach (var arrival in target.ArrivalTriangleIds)
        {
            Equal(true, target.VehicleContactPoints.ContainsKey(arrival),
                $"arrival t{arrival} must carry the contact point it was resolved from");
            var point = target.VehicleContactPoints[arrival];
            Equal(true,
                WorldMapVehicleShoreApproach.IsInNativeContact(
                    map,
                    onFoot with { X = point.X, Y = point.Y, Z = point.Z },
                    5, BroncoX, BroncoZ),
                $"the point offered in t{arrival} must be in native contact with the boat");
            Equal(true,
                WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[arrival].TerrainId),
                $"and t{arrival} must be ground the party on foot can stand on");
        }
    }

    /// <summary>
    /// The whole point: from where the party actually was later in the log, the walk back
    /// to the boat exists, stays on land, avoids everybody's front door, and finishes
    /// somewhere Confirm would board rather than somewhere in the general area.
    /// </summary>
    private static void TheWalkBackToTheBoatEndsInNativeContact(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);

        // 16:13:33 in the log, on foot, well away from the water.
        var away = State(111943, 465, 158639, 0, 0, Region, 0);
        runtime.UpdateEntities([ParkedBronco()]);
        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, away, runtime.Entities)
            .Single(candidate => candidate.Label == "Tiny Bronco");

        Equal(true, runtime.Planner.CanReach(away, target),
            $"the boat must be walkable to from where the party ended up: {runtime.Planner.LastDiagnostic}");
        Equal(true, runtime.Planner.TryBuildRoute(away, target, out var plan),
            $"and a route must actually build: {runtime.Planner.LastDiagnostic}");
        Equal(true, plan.TrianglePath.Count > 1,
            "this start must be far enough away to be a walk, or the case proves nothing");

        var crossedWater = plan.TrianglePath
            .Where(id => !WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[id].TerrainId))
            .ToArray();
        Equal(0, crossedWater.Length,
            $"the walk must stay on ground the party can walk on; crossed [{string.Join(",", crossedWater)}]");
        var crossedDoors = plan.TrianglePath.Where(catalog.EntranceTriangleIds.Contains).ToArray();
        Equal(0, crossedDoors.Length,
            $"and must not walk through anybody's front door; crossed [{string.Join(",", crossedDoors)}]");

        var final = plan.Waypoints[^1];
        Equal(true,
            WorldMapVehicleShoreApproach.IsInNativeContact(
                map, away with { X = final.X, Y = final.Y, Z = final.Z }, 5, BroncoX, BroncoZ),
            $"the route must end in native contact with the boat, not merely in its triangle; " +
            $"ended at ({final.X},{final.Y},{final.Z})");
        Equal(true,
            target.HasArrived(away with { X = final.X, Y = final.Y, Z = final.Z }, plan.TargetTriangleId),
            "and arrival must be announced there");

        // The arrival triangle is hundreds of units across. Standing in it is not arriving
        // at the boat, and saying so would be announcing something the player cannot do.
        var centroid = map.Triangles[plan.TargetTriangleId].Centroid;
        Equal(false,
            target.HasArrived(
                away with { X = centroid.X, Y = centroid.Y, Z = centroid.Z }, plan.TargetTriangleId),
            $"the far side of arrival triangle t{plan.TargetTriangleId} is not contact with the boat");
    }

    /// <summary>
    /// Narrowing the filter is not the same as removing it. A boat out in open water, with
    /// no shore inside its mask, still has no approach and still has to be refused in
    /// words rather than routed to.
    /// </summary>
    private static void ABoatNothingCanTouchIsStillRefused(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var onFoot = State(PartyX, PartyY, PartyZ, PartyTerrain, 0, Region, 0);
        runtime.UpdateEntities([ParkedBronco() with { X = BroncoX - 30000, Z = BroncoZ + 20000 }]);

        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, onFoot, runtime.Entities)
            .Single(candidate => candidate.Label == "Tiny Bronco");
        Equal(0, target.ArrivalTriangleIds.Count,
            "a boat with no shore inside its mask must be given no approach at all");
        Equal(false, runtime.Planner.CanReach(onFoot, target),
            "and must not be claimed as reachable");

        var now = new DateTime(2026, 9, 22, 16, 9, 32, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, onFoot, now);
        }

        var spoken = runtime.Navigation
            .HandleAction(FieldNavigationAction.RepeatTarget, onFoot, now)!.Value.Speech;
        Equal(true, spoken.Contains("Tiny Bronco", StringComparison.Ordinal),
            $"it is still the player's boat and still gets named; said \"{spoken}\"");
        var started = runtime.Navigation
            .HandleAction(FieldNavigationAction.ToggleBeacon, onFoot, now)!.Value.Speech;
        Equal(true, started.Contains("Route unavailable", StringComparison.OrdinalIgnoreCase),
            $"and walking to it must be refused in words; said \"{started}\"");
        Equal(false, runtime.Navigation.BeaconEnabled,
            "without leaving navigation running toward it");
    }

    /// <summary>
    /// The last step of the approach is into the vehicle's own footprint, because that is
    /// the only place the game will board it from. Automatic walking must not treat the
    /// destination as something to walk around - it has to still be willing to press
    /// toward it - while every other vehicle on the map stays an obstacle.
    ///
    /// <para>The Buggy is the one that proves this: it is in the obstacle set the detour
    /// planner uses, so before the exemption it would have refused its own last leg.</para>
    /// </summary>
    private static void TheSelectedVehicleIsNotDetouredAroundItself(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        // The user's own parked Buggy, and a foot position beside it.
        var buggy = new WorldMapEntitySnapshot(0x1000, 0, false, 113151, 593, 181046, 25, 5, 6, 0);
        var beside = State(112443, 370, 185139, 25, 0, 5, 0);
        var runtime = CreateShippedRuntime(map, catalog);
        runtime.UpdateEntities([buggy]);

        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, beside, runtime.Entities)
            .Single(candidate => candidate.Label == "Buggy");
        Equal(true, target.VehicleContactPoints.Count > 0,
            "the Buggy must also resolve its approach from the native mask");

        var controller = new WorldMapNavigationController(
            map,
            runtime.Planner,
            (_, _) => [target],
            guidanceInterval: TimeSpan.FromMinutes(1),
            entityProvider: () => [buggy]);
        var now = new DateTime(2026, 9, 22, 16, 9, 32, DateTimeKind.Utc);
        while (controller.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, beside, now);
        }

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, beside, now);
        Equal(true, controller.BeaconEnabled,
            $"navigating to the Buggy must start: {controller.LastDiagnostic}");

        // Standing one native step short of the contact point, with the Buggy the only
        // thing in front of the party. Walking on has to still be offered.
        var contact = target.VehicleContactPoints[
            runtime.Planner.TryBuildRoute(beside, target, out var plan)
                ? plan.TargetTriangleId
                : throw new InvalidOperationException("the Buggy must be routable from beside it")];
        var approach = beside with
        {
            X = contact.X + Math.Sign(beside.X - contact.X) * 120,
            Y = contact.Y,
            Z = contact.Z + Math.Sign(beside.Z - contact.Z) * 120
        };
        controller.Observe(approach, now.AddMilliseconds(80), automaticWalkActive: true);
        Equal(true, controller.TryResolveAutomaticInput(approach, out _),
            "the final step onto the Buggy's own footprint must still be offered, or the " +
            $"party is parked just outside the only cell that boards it: {controller.LastDiagnostic}");

        // A different vehicle in the way is still walked around.
        var otherCar = buggy with { GuestPointer = 0x3000, X = approach.X, Z = approach.Z };
        Equal(true,
            WorldMapVehicleObstacles.IsBlocked(
                [otherCar], 0, approach.X, approach.Z, map.WrapWidth, map.WrapHeight),
            "a second parked vehicle standing on the party must still be an obstacle");
    }

    /// <summary>
    /// Walking to a boat and being told you got there.
    ///
    /// <para>This is the case geometry alone cannot pass. <c>FUN_00762A21</c> refuses a
    /// step that collides <em>and</em> closes, so the approach never ends inside the mask -
    /// the party is rolled back to just outside it, every frame, forever. Replaying the
    /// real route over the raw native triangles with the verified Cloud and Tiny Bronco
    /// masks and that rollback, the old build spent sixty-odd blocked frames at every
    /// camera and speed and then gave up with "Could not get closer".</para>
    ///
    /// <para>What the game records during those refused steps is the contact pointer at
    /// <c>player + 4</c>, which is the slot Confirm reads. The replay below writes it
    /// exactly when the native test reports contact, which is what
    /// <c>FUN_00762993</c> does, and the approach has to finish on it.</para>
    /// </summary>
    private static void TheWitnessedNativeContactEndsTheApproach(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        foreach (var camera in new[] { 0, 1152, 2048, 3550 })
        {
            foreach (var speed in new[] { 30, 60, 120 })
            {
                var replay = ReplayTheWalkBack(map, catalog, camera, speed, witnessContact: true);
                Equal(true, replay.Arrived,
                    $"camera {camera} at speed {speed}: the approach must finish on the native " +
                    $"contact the game recorded; it said \"{replay.LastSpeech}\" after " +
                    $"{replay.Frames} frames with {replay.BlockedSteps} refused steps");
                Equal(true, replay.BlockedSteps > 0,
                    $"camera {camera} at speed {speed}: the last step must actually have been " +
                    "refused by native collision, or this case proves nothing");
                Equal(false, replay.LastSpeech.Contains("Could not get closer", StringComparison.Ordinal),
                    $"camera {camera} at speed {speed}: and it must not be reported as a failure");
            }
        }

        // Without the witness - a host that cannot read the slot - nothing is claimed.
        var unwitnessed = ReplayTheWalkBack(map, catalog, 1152, 60, witnessContact: false);
        Equal(false, unwitnessed.Arrived,
            "with no contact pointer to go on, arrival must not be announced anyway");
    }

    /// <summary>
    /// The slot is dynamic and nothing clears it for us, so a pointer on its own proves
    /// only that the party touched something once. It has to name this boat, the party
    /// must not be the boat, and the boat has to be inside the window the native test
    /// checks before it looks at either mask.
    /// </summary>
    private static void AWitnessThatDoesNotNameThisBoatIsNotAnArrival(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var onFoot = State(PartyX, PartyY, PartyZ, PartyTerrain, 0, Region, 0);
        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, onFoot, [ParkedBronco()])
            .Single(candidate => candidate.Label == "Tiny Bronco");
        var contact = target.NativeVehicleContact!;
        var arrival = target.ArrivalTriangleIds.First();

        // Standing well clear of the boat, so geometry alone can never carry these.
        var clear = onFoot with { X = BroncoX + 4000, Z = BroncoZ + 4000 };
        Equal(false, contact.OccupiesNativeMask(clear),
            "the position used for these must be outside the mask, or they prove nothing");

        Equal(false,
            target.HasArrived(clear with { NativePlayerEntityPointer = 0x00E3A288 }, arrival),
            "no contact pointer at all is not an arrival");
        Equal(false,
            target.HasArrived(
                clear with { NativePlayerEntityPointer = 0x00E3A288, NativeContactEntityPointer = 0x00E3A348 },
                arrival),
            "a pointer naming some other entity is not an arrival at this boat");

        // The right pointer, but the boat is four thousand units away: a leftover from a
        // collision somewhere else, which the native reach guard is what catches.
        Equal(false,
            target.HasArrived(
                clear with { NativePlayerEntityPointer = 0x00E3A288, NativeContactEntityPointer = BroncoEntity },
                arrival),
            "a stale pointer with the boat out of native reach is not an arrival");

        // The party is the boat. FUN_00762993 never writes the current entity into its own
        // slot, but a stale value that matches must not be read as touching yourself.
        var near = onFoot with
        {
            X = BroncoX + 200,
            Z = BroncoZ + 200,
            NativePlayerEntityPointer = BroncoEntity,
            NativeContactEntityPointer = BroncoEntity,
        };
        Equal(false, target.HasArrived(near, arrival),
            "the party being the entity in the slot is a remount, not an arrival");

        // And the one that should pass: the right pointer, the party is someone else, and
        // the boat is inside the native window.
        Equal(true,
            target.HasArrived(
                near with { NativePlayerEntityPointer = 0x00E3A288 }, arrival),
            "the witnessed contact this whole mechanism exists for must be accepted");
        Equal(false,
            target.HasArrived(
                near with { NativePlayerEntityPointer = 0x00E3A288 }, WrongBankTriangle),
            "but only where the approach was actually offered");
    }

    /// <summary>
    /// A vehicle is a live entity. When it stops being one - the native list no longer
    /// carries it, or the party is riding it again, which is the same entity flagged as
    /// the player - the coordinates the route was built from are last frame's, and
    /// steering to them walks the party to where their boat used to be.
    /// </summary>
    private static void ABoatThatIsGoneOrRiddenStopsSteering(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        foreach (var (what, mutate, after) in new (string, Func<WorldMapEntitySnapshot[]>, Func<WorldMapStateSnapshot, WorldMapStateSnapshot>)[]
                 {
                     ("the boat has left the native list", () => [], state => state),
                     ("the party is riding it again",
                         () => [ParkedBronco() with { IsPlayer = true }],
                         state => state with
                         {
                             PlayerModelId = 5, X = BroncoX, Y = BroncoY, Z = BroncoZ, TerrainId = BroncoTerrain,
                         }),
                 })
        {
            var runtime = CreateShippedRuntime(map, catalog);
            var away = State(111943, 465, 158639, 0, 0, Region, 0);
            runtime.UpdateEntities([ParkedBronco()]);
            var now = new DateTime(2026, 9, 22, 16, 14, 0, DateTimeKind.Utc);
            while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
            {
                runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, away, now);
            }

            runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, away, now);
            Equal(true, runtime.Navigation.BeaconEnabled,
                $"{what}: the route must start first, or the case proves nothing");

            runtime.UpdateEntities(mutate());
            var state = after(away);
            var output = runtime.Navigation.Observe(state, now.AddMilliseconds(80), automaticWalkActive: true);

            Equal(false, runtime.Navigation.BeaconEnabled,
                $"{what}: navigation must not keep running toward a boat that is not there");
            Equal(false, runtime.Navigation.TryResolveAutomaticInput(state, out _),
                $"{what}: and automatic walking must stop producing input");
            Equal(true, output?.StopAutoWalk == true,
                $"{what}: and the stop must be told to the walker, not left implicit");
            Equal(true, (output?.Speech ?? string.Empty).Contains("Tiny Bronco", StringComparison.Ordinal),
                $"{what}: and the player must be told which target went away; said " +
                $"\"{output?.Speech}\"");
        }
    }

    /// <summary>
    /// A boat can drift without leaving any of its arrival triangles - they are hundreds
    /// of units across and the ground that boards it is one 256-unit cell. Checking
    /// triangle membership alone leaves the route walking to where the boat was.
    /// </summary>
    private static void ABoatThatDriftsInsideItsArrivalTriangleIsReplanned(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var away = State(111943, 465, 158639, 0, 0, Region, 0);
        runtime.UpdateEntities([ParkedBronco()]);
        var now = new DateTime(2026, 9, 22, 16, 14, 0, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, away, now);
        }

        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, away, now);
        var before = runtime.Navigation.Probe.Route!;
        var endpointBefore = before.Waypoints[^1];

        var drifted = ParkedBronco() with { X = BroncoX + 300, Z = BroncoZ + 300 };
        runtime.UpdateEntities([drifted]);
        var moved = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, away, runtime.Entities)
            .Single(candidate => candidate.Label == "Tiny Bronco");
        Equal(true, moved.ArrivalTriangleIds.Contains(before.TargetTriangleId),
            "the drift must keep the same arrival triangle, or the old check would have caught it");

        runtime.Navigation.Observe(away, now.AddMilliseconds(80), automaticWalkActive: true);
        var after = runtime.Navigation.Probe.Route!;
        Equal(true, runtime.Navigation.BeaconEnabled, "a boat that merely moved is still the target");
        var endpointAfter = after.Waypoints[^1];
        Equal(true,
            endpointAfter.X != endpointBefore.X || endpointAfter.Z != endpointBefore.Z,
            $"the route must aim at where the boat is now; it still ends at " +
            $"({endpointAfter.X},{endpointAfter.Z})");
        Equal(true,
            moved.VehicleContactPoints[after.TargetTriangleId].X == endpointAfter.X &&
            moved.VehicleContactPoints[after.TargetTriangleId].Z == endpointAfter.Z,
            "and on the contact point the boat has now, not a triangle centre");
    }

    /// <summary>
    /// The route driven over the raw native triangles, with the native collision applied:
    /// a step that collides and closes is refused and the party keeps the position they
    /// had, exactly as FUN00762A21 leaves them. When <paramref name="witnessContact"/> is
    /// set the refused step also writes the boat into <c>player + 4</c>, which is what
    /// FUN00762993 does before the refusal.
    /// </summary>
    private static (bool Arrived, int Frames, int BlockedSteps, string LastSpeech) ReplayTheWalkBack(
        WorldMapData map,
        WorldMapTargetCatalog catalog,
        int camera,
        int speed,
        bool witnessContact)
    {
        const uint partyEntity = 0x00E3A288;
        var boat = ParkedBronco();
        var runtime = CreateShippedRuntime(map, catalog);
        runtime.UpdateEntities([boat]);
        var direction = -(camera / 16);
        var state = State(111943, 465, 158639, 0, 0, Region, 0) with
        {
            CameraFront = camera,
            ControlTransform = new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction),
            NativePlayerEntityPointer = partyEntity,
        };

        var now = new DateTime(2026, 9, 22, 16, 14, 0, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Transportation)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, now);
        }

        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        runtime.Planner.TryResolvePlayerTriangle(state, out var triangle);

        var blocked = 0;
        var lastSpeech = string.Empty;
        var frame = 0;
        for (; frame < 600; frame++)
        {
            var output = runtime.Navigation.Observe(state, now.AddMilliseconds(frame * 80), automaticWalkActive: true);
            if (output is { } spoken)
            {
                lastSpeech = spoken.Speech ?? string.Empty;
                if (spoken.StopAutoWalk || !runtime.Navigation.BeaconEnabled)
                {
                    break;
                }
            }

            if (!runtime.Navigation.TryResolveAutomaticInput(state, out var input))
            {
                continue;
            }

            var (dx, dz) = NativeStep(input, camera, speed);
            var moved = WorldMapVehicleApproachReplayTests.MoveOnRawNativeTriangles(
                map, state, ref triangle, dx, dz);
            var touching = WorldMapVehicleObstacles.Blocks(
                0, moved.X, moved.Z, boat.ModelId, boat.X, boat.Z, map.WrapWidth, map.WrapHeight);
            var closing =
                Math.Abs(boat.X - moved.X) + Math.Abs(boat.Z - moved.Z) <
                Math.Abs(boat.X - state.X) + Math.Abs(boat.Z - state.Z);
            if (touching && closing)
            {
                // Refused. The party does not move, and the game has already recorded what
                // they collided with.
                blocked++;
                state = state with
                {
                    NativeContactEntityPointer = witnessContact ? boat.GuestPointer : 0u,
                };
                continue;
            }

            state = moved with
            {
                CameraFront = state.CameraFront,
                ControlTransform = state.ControlTransform,
                NativePlayerEntityPointer = partyEntity,
                NativeContactEntityPointer = witnessContact && touching ? boat.GuestPointer : 0u,
            };
        }

        return (!runtime.Navigation.BeaconEnabled, frame, blocked, lastSpeech);
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

    /// <summary>
    /// A shore that is somebody's front door is not an approach. Parked where the user's
    /// Buggy sat, in Gongaga's trigger cell, the boat's mask reaches twelve triangles and
    /// seven of them are native entrances. Offering one would walk the party into the
    /// jungle instead of to their boat, which is the failure that rule was written for.
    /// </summary>
    private static void AShoreThatIsOnlyAFieldEntranceIsNoApproach(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var beside = State(112443, 370, 185139, 25, 0, 5, 0);
        var inTheTriggerCell = ParkedBronco() with { X = 113151, Y = 593, Z = 181046, TerrainId = 25, RegionId = 5 };

        var reached = WorldMapVehicleShoreApproach.FindContactPoints(
            map, 0, 5, inTheTriggerCell.X, inTheTriggerCell.Z);
        var doorways = reached.Keys.Where(catalog.EntranceTriangleIds.Contains).ToArray();
        Equal(true, doorways.Length > 0,
            "this position must actually put entrances inside the mask, or the case proves nothing");

        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, beside, [inTheTriggerCell])
            .Single(candidate => candidate.Label == "Tiny Bronco");
        foreach (var doorway in doorways)
        {
            Equal(false, target.ArrivalTriangleIds.Contains(doorway),
                $"t{doorway} is a native field entrance and must not be offered as a way to the boat");
            Equal(false, target.VehicleContactPoints.ContainsKey(doorway),
                $"and no route may be aimed at the contact point inside t{doorway}");
        }

        Equal(true, target.ArrivalTriangleIds.Count > 0,
            "the shore that is not a doorway is still an approach");
        Equal(true, target.ArrivalTriangleIds.Count < reached.Count,
            "and the doorways really were removed rather than never found");
    }

    /// <summary>
    /// The boat the party is sitting in is not a boat they can walk to. The native list
    /// keeps one entity for the vehicle the player currently is, and offering that would
    /// be routing the party to themselves.
    /// </summary>
    private static void ARemountedBoatIsNotAParkedOne(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var aboard = State(BroncoX, BroncoY, BroncoZ, BroncoTerrain, 1, Region, 5);
        var targets = runtime.Catalog.ReadTargets(
            WorldMapNavigationCategory.Transportation,
            aboard,
            [ParkedBronco() with { IsPlayer = true }]);
        Equal(0, targets.Count,
            "the vehicle the party is riding must not be offered as somewhere to walk");

        // And a stale copy, flagged out of the native test, is not offered either.
        Equal(0,
            runtime.Catalog.ReadTargets(
                WorldMapNavigationCategory.Transportation,
                State(PartyX, PartyY, PartyZ, PartyTerrain, 0, Region, 0),
                [ParkedBronco() with { Flags = WorldMapVehicleObstacles.SkippedFlag }])
                .Count(candidate => candidate.ArrivalTriangleIds.Count > 0),
            "an entity the native collision skips cannot be given a boarding approach");
    }

    /// <summary>
    /// The filter is not being removed, only narrowed. A place on another continent is
    /// still not offered, because walking there is not a thing the player can be told to
    /// do and a town is not something the party is carrying around with them.
    /// </summary>
    private static void AnUnreachablePlaceIsStillNotListed(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var state = State(midgar.X, midgar.Y, midgar.Z, 0, 0, midgar.RegionId, 0);
        if (!runtime.Planner.TryResolvePlayerTriangle(state, out _))
        {
            throw new InvalidOperationException("Midgar must resolve on the installed world map.");
        }

        var unreachable = catalog.Locations
            .Where(target => !runtime.Planner.CanReach(state, target))
            .ToArray();
        Equal(true, unreachable.Length > 0,
            "the eastern continent must still hold places Midgar cannot walk to");

        var listed = new List<string>();
        var now = new DateTime(2026, 9, 22, 16, 9, 32, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Locations)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, now);
        }

        for (var step = 0; step < catalog.Locations.Count; step++)
        {
            listed.Add(runtime.Navigation
                .HandleAction(FieldNavigationAction.NextTarget, state, now)!.Value.Speech);
        }

        foreach (var target in unreachable)
        {
            Equal(false, listed.Any(spoken => spoken.Contains(target.Label, StringComparison.Ordinal)),
                $"\"{target.Label}\" cannot be walked to from Midgar and must not be listed");
        }
    }

    /// <summary>
    /// The second fault, and the reason the first one flickered rather than simply
    /// failing. <see cref="WorldMapEntityReader"/> reads the native list twice and
    /// compares the two frames with <c>SequenceEqual</c> over the whole snapshot,
    /// positions included. Anything moving anywhere in the world - and something always
    /// is - makes the comparison fail, the host then publishes an empty list, and every
    /// parked vehicle disappears until the next scan happens to catch the world still.
    ///
    /// <para>The log shows this directly: <c>world entity list changed during read</c> and
    /// <c>native world entities=10</c> alternate every single scan for the last five
    /// minutes, and the diagnostic is only written when it changes.</para>
    /// </summary>
    private static void AMovingEntityDoesNotDiscardTheParkedVehiclesBesideIt()
    {
        var memory = new WorldEntityMemory(
        [
            new WorldEntityMemory.Node(0x00E3A288, IsPlayer: true, X: PartyX, Y: PartyY, Z: PartyZ,
                Terrain: PartyTerrain, Region: Region, Model: 0, Flags: 0, Drifts: true),
            new WorldEntityMemory.Node(BroncoEntity, IsPlayer: false, X: BroncoX, Y: BroncoY, Z: BroncoZ,
                Terrain: BroncoTerrain, Region: Region, Model: 5, Flags: 0, Drifts: false),
            new WorldEntityMemory.Node(0x00E3A348, IsPlayer: false, X: 113151, Y: 593, Z: 181046,
                Terrain: 25, Region: 5, Model: 6, Flags: 0, Drifts: false),
        ]);

        var result = new WorldMapEntityReader(memory).Read();
        Equal(true, result.IsUsable,
            $"a world where one entity is moving is still a readable world: {result.Diagnostic}");
        Equal(3, result.Entities.Count, "and every node must survive the read");
        Equal(true, result.Entities.Any(entity => entity.ModelId == 5),
            "including the parked Tiny Bronco, which did not move at all");
        Equal(true, result.Entities.Any(entity => entity.ModelId == 6),
            "and the parked Buggy beside it");

        // The moving entity is reported where the second frame found it, not at a position
        // the reader kept from before.
        var player = result.Entities.Single(entity => entity.IsPlayer);
        Equal(PartyX + WorldEntityMemory.DriftPerFrame * 2, player.X,
            "a moving entity is reported from the later frame, never from a stale one");
    }

    /// <summary>
    /// The frame guard still has to exist, and has to be about identity. A list whose
    /// chain, player or models change between the two frames is being rebuilt underneath
    /// the reader, and reading half of each is how a vehicle gets invented. The animation
    /// flag at +0x51 is not that: it is per-frame state a live entity changes on its own,
    /// and discarding the list for it is the same mistake position was.
    /// </summary>
    private static void AListThatIsRelinkedMidReadIsStillRefused()
    {
        static WorldEntityMemory Memory(WorldEntityMemory.Mutation mutation) => new(
        [
            new WorldEntityMemory.Node(0x00E3A288, IsPlayer: true, X: PartyX, Y: PartyY, Z: PartyZ,
                Terrain: PartyTerrain, Region: Region, Model: 0, Flags: 0, Drifts: false),
            new WorldEntityMemory.Node(BroncoEntity, IsPlayer: false, X: BroncoX, Y: BroncoY, Z: BroncoZ,
                Terrain: BroncoTerrain, Region: Region, Model: 5, Flags: 0, Drifts: false),
        ])
        { SecondFrame = mutation };

        foreach (var (mutation, what) in new (WorldEntityMemory.Mutation, string)[]
                 {
                     (WorldEntityMemory.Mutation.DropsTail, "a node leaving the list"),
                     (WorldEntityMemory.Mutation.ChangesModel, "an entity becoming another model"),
                     (WorldEntityMemory.Mutation.MovesPlayerPointer, "the player moving to another node"),
                 })
        {
            Equal(false, new WorldMapEntityReader(Memory(mutation)).Read().IsUsable,
                $"{what} between the two frames must invalidate the read, not be published");
        }

        var flagged = new WorldMapEntityReader(Memory(WorldEntityMemory.Mutation.ChangesFlags)).Read();
        Equal(true, flagged.IsUsable,
            "an entity's native flag byte changing is animation state, not a rebuilt list: " +
            flagged.Diagnostic);
        Equal(2, flagged.Entities.Count, "and the list is still published in full");
    }

    /// <summary>
    /// The boat as the native disembark leaves it.
    ///
    /// <para>These are the last coordinates the log records while the party was still the
    /// Bronco, one scan before the player entity changed. The parked entity's own position
    /// is never logged, so this is a close proxy for where the boat ended up and not a
    /// capture of it. Every case here is about the rule applied to a parked boat, not
    /// about these three numbers.</para>
    /// </summary>
    private static WorldMapEntitySnapshot ParkedBronco() =>
        new(BroncoEntity, 0, false, BroncoX, BroncoY, BroncoZ, BroncoTerrain, Region, 5, 0);

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

    private static WorldMapStateSnapshot State(
        int x, int y, int z, int terrain, int terrainScript, int region, int model) =>
        new(3, 0, 0, 469, x, y, z, 0, 0, terrain, region, model, 30, 0,
            new FieldNavigationControlTransform(0))
        {
            TerrainScriptId = terrainScript
        };

    /// <summary>
    /// The native linked list FUN_007610b3 builds, laid out at the offsets
    /// <see cref="WorldMapEntityReader"/> reads, with a world that keeps running between
    /// the reader's two frames.
    /// </summary>
    private sealed class WorldEntityMemory(IReadOnlyList<WorldEntityMemory.Node> nodes)
        : ILegacyAddressSpace
    {
        internal const int DriftPerFrame = 37;

        internal enum Mutation
        {
            None,
            DropsTail,
            ChangesModel,
            MovesPlayerPointer,
            ChangesFlags,
        }

        internal sealed record Node(
            uint Address,
            bool IsPlayer,
            int X,
            int Y,
            int Z,
            int Terrain,
            int Region,
            int Model,
            byte Flags,
            bool Drifts);

        internal Mutation SecondFrame { get; init; } = Mutation.None;

        // One whole pass of the reader over the list is a frame. The world advances
        // between passes, exactly as it does in the game.
        private int headReads;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == (uint)WorldMapStateReader.AddressCurrentModule && destination.Length == 1)
            {
                destination[0] = WorldMapStateReader.WorldModule;
                return true;
            }

            if (virtualAddress == (uint)WorldMapEntityReader.AddressEntityListHead && destination.Length == 4)
            {
                // The reader reads the head once opening the traversal and once closing
                // it; the frame turns over on the opening read.
                if (headReads++ % 2 == 0)
                {
                    frame++;
                }

                WriteUInt32(destination, nodes[0].Address);
                return true;
            }

            if (virtualAddress == (uint)WorldMapStateReader.AddressWorldPlayerEntityPointer && destination.Length == 4)
            {
                WriteUInt32(destination, PlayerPointer());
                return true;
            }

            foreach (var (node, index) in Visible().Select((node, index) => (node, index)))
            {
                if (virtualAddress < node.Address || virtualAddress >= node.Address + 0x60)
                {
                    continue;
                }

                var offset = (int)(virtualAddress - node.Address);
                var visible = Visible();
                switch (offset)
                {
                    case 0x00 when destination.Length == 4:
                        WriteUInt32(destination, index + 1 < visible.Count ? visible[index + 1].Address : 0u);
                        return true;
                    case WorldMapStateReader.PositionXOffset when destination.Length == 4:
                        WriteInt32(destination, node.X + (node.Drifts ? DriftPerFrame * frame : 0));
                        return true;
                    case WorldMapStateReader.PositionYOffset when destination.Length == 4:
                        WriteInt32(destination, node.Y);
                        return true;
                    case WorldMapStateReader.PositionZOffset when destination.Length == 4:
                        WriteInt32(destination, node.Z);
                        return true;
                    case WorldMapStateReader.WalkmapTypeOffset when destination.Length == 2:
                        WriteUInt16(destination, (ushort)((node.Region << 9) | node.Terrain));
                        return true;
                    case WorldMapStateReader.ModelIdOffset when destination.Length == 1:
                        destination[0] = (byte)(
                            SecondFrame == Mutation.ChangesModel && frame > 1 && !node.IsPlayer
                                ? node.Model + 1
                                : node.Model);
                        return true;
                    case 0x51 when destination.Length == 1:
                        destination[0] = (byte)(
                            SecondFrame == Mutation.ChangesFlags && frame > 1 && !node.IsPlayer
                                ? node.Flags + 1
                                : node.Flags);
                        return true;
                }
            }

            return false;
        }

        private int frame;

        private IReadOnlyList<Node> Visible() =>
            SecondFrame == Mutation.DropsTail && frame > 1
                ? nodes.Take(nodes.Count - 1).ToArray()
                : nodes;

        private uint PlayerPointer() =>
            SecondFrame == Mutation.MovesPlayerPointer && frame > 1
                ? nodes[^1].Address
                : nodes.First(node => node.IsPlayer).Address;

        private static void WriteUInt32(Span<byte> destination, uint value)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
        }

        private static void WriteInt32(Span<byte> destination, int value) =>
            WriteUInt32(destination, unchecked((uint)value));

        private static void WriteUInt16(Span<byte> destination, ushort value)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Tiny Bronco transportation - {label}: expected {expected}, got {actual}.");
        }
    }
}
