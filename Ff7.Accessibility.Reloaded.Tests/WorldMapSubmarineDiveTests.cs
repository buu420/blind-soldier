using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapSubmarineDiveTests
{
    private static readonly DateTime Epoch = new(2026, 10, 6, 17, 10, 9, DateTimeKind.Utc);

    internal static void Run()
    {
        NormalCancelIsBoundedAndDestinationSurvivesTheNativeReload();
        ManualGuidanceNeverPressesCancel();
        CancellationAndTimeoutCannotResumeOrRepeatTheDive();
        UnreadableFirstUnderwaterSampleRetainsTheDestination();
        GenuineSailingInputFailureCannotRestartAfterManualDive();
        DiveBindingIsIsolatedAndReadLive();
        AvailabilityUsesCoherentNativeAllocationFlags();
        NativeGateAndConfirmedSlowLoad();
        StagingMotionIsBoundedAndSuspensionRestartsItsClock();
        InstalledDeepSeaRouteReachesDiveableSea();
        Console.WriteLine("world-map automatic submarine dive tests passed.");
    }

    private static void NormalCancelIsBoundedAndDestinationSurvivesTheNativeReload()
    {
        var fixture = new Fixture();
        fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Deep, Epoch);
        fixture.Surface.NoteAutoWalkStarted();
        fixture.Surface.Observe(fixture.Deep, Epoch, true, 0);
        Equal(false, fixture.Surface.AutomaticInputRequestsSubmarineDive, "Deep sea cannot dive");
        var first = fixture.Surface.Observe(fixture.Sea, Epoch.AddMilliseconds(100), true, 0);
        Contains(first?.Speech, "Diving", "automatic sea handoff");
        fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out _);
        Equal(false, fixture.Surface.AutomaticInputRequestsSubmarineDive, "neutral interval precedes a new press");
        fixture.Surface.Observe(fixture.Sea, Epoch.AddMilliseconds(200), true, 0);
        Equal(true, fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out var direction), "dive action is owned");
        Equal(FieldNavigationInput.None, direction, "dive has no direction or thrust");
        Equal(true, fixture.Surface.AutomaticInputRequestsSubmarineDive, "host receives dive action");
        var sink = new Sink(); var memory = new Memory();
        using var auto = new NavigationAutoWalkController(sink, new HighwayDirectionInputMappingResolver(memory));
        auto.TryStart(NavigationAutoWalkDomain.WorldMap, true);
        auto.Drive(direction, true, true, worldSubmarine: true, requestSubmarineDive: true);
        Equal(true, sink.Held.SetEquals([0x2C]), "only live Cancel slot6 is delivered");
        fixture.Surface.NoteSubmarineDiveDelivered(Epoch.AddMilliseconds(200));
        fixture.Surface.Observe(fixture.Sea, Epoch.AddMilliseconds(300), true, 0x40);
        Equal(false, fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out direction), "observed Cancel releases pulse");
        auto.Drive(direction, false, true, worldSubmarine: true);
        Equal(0, sink.Held.Count, "pulse release before reload");
        fixture.Surface.PauseForNativeControl();
        auto.Suspend();
        fixture.Surface.Suspend("map2 loaded", preserveSubmarineJourney: true);
        var continued = fixture.Underwater.Observe(fixture.Undersea, Epoch.AddMilliseconds(1200), true, 0);
        Contains(continued?.Speech, "Continuing to Sunken Gelnika", "destination retained across map reload");
        Equal(true, continued?.StartAutoWalk == true && fixture.Underwater.BeaconEnabled, "automatic underwater leg resumes");
        Equal("world-underwater:17", fixture.Underwater.Probe.TargetId, "fixed original underwater destination");
        fixture.Underwater.TryResolveAutomaticInput(fixture.Undersea, out _);
        Equal(false, fixture.Underwater.AutomaticInputRequestsSubmarineDive, "no repeated Cancel can surface again");
    }

    private static void ManualGuidanceNeverPressesCancel()
    {
        var fixture = new Fixture();
        fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Sea, Epoch);
        var prompt = fixture.Surface.Observe(fixture.Sea, Epoch, false, 0);
        Contains(prompt?.Speech, "Press Cancel", "spoken route names native action");
        fixture.Surface.Observe(fixture.Sea, Epoch.AddSeconds(1), false, 0);
        fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out _);
        Equal(false, fixture.Surface.AutomaticInputRequestsSubmarineDive, "A guidance does not dive automatically");
        fixture.Surface.Suspend("manual dive changed map", preserveSubmarineJourney: true);
        var continued = fixture.Underwater.Observe(fixture.Undersea, Epoch.AddSeconds(2), false, 0);
        Equal(false, continued?.StartAutoWalk == true, "manual dive preserves spoken-only navigation");
        Equal(true, fixture.Underwater.BeaconEnabled, "manual dive retains destination");
    }

    private static void CancellationAndTimeoutCannotResumeOrRepeatTheDive()
    {
        foreach (var stop in new[] { "cancel", "timeout", "reset" })
        {
            var fixture = new Fixture();
            fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Sea, Epoch);
            fixture.Surface.NoteAutoWalkStarted();
            fixture.Surface.Observe(fixture.Sea, Epoch, true, 0x40);
            fixture.Surface.Observe(fixture.Sea, Epoch.AddMilliseconds(200), true, 0x40);
            fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out _);
            Equal(false, fixture.Surface.AutomaticInputRequestsSubmarineDive, "physical held Cancel must be released");
            fixture.Surface.Observe(fixture.Sea, Epoch.AddMilliseconds(300), true, 0);
            fixture.Surface.Observe(fixture.Sea, Epoch.AddMilliseconds(400), true, 0);
            fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out _);
            Equal(true, fixture.Surface.AutomaticInputRequestsSubmarineDive, "fresh neutral interval makes one pulse");
            fixture.Surface.NoteSubmarineDiveDelivered(Epoch.AddMilliseconds(400));
            fixture.Surface.PauseForNativeControl();
            if (stop == "cancel") fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Sea, Epoch.AddSeconds(1));
            else if (stop == "reset") fixture.Surface.Reset();
            else
            {
                var failed = fixture.Surface.Observe(fixture.Sea, Epoch.AddSeconds(9), true, 0);
                Equal(true, failed?.StopAutoWalk == true, "unconfirmed dive times out");
                Contains(failed?.Speech, "did not confirm", "failed dive explains the stop");
            }
            fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out _);
            Equal(false, fixture.Surface.AutomaticInputRequestsSubmarineDive, $"{stop} releases dive request");
            fixture.Underwater.Observe(fixture.Undersea, Epoch.AddSeconds(10), true, 0);
            Equal(false, fixture.Underwater.BeaconEnabled, $"{stop} cannot revive a stale underwater route");
        }
    }

    private static void DiveBindingIsIsolatedAndReadLive()
    {
        var sink = new Sink(); var memory = new Memory();
        using var auto = new NavigationAutoWalkController(sink, new HighwayDirectionInputMappingResolver(memory));
        auto.TryStart(NavigationAutoWalkDomain.WorldMap, true);
        Equal(true, auto.Drive(FieldNavigationInput.None, true, true, worldSubmarine: true, requestSubmarineDive: true).Success, "configured dive");
        memory.Write(0, 6, 0x30);
        auto.Drive(FieldNavigationInput.None, true, true, worldSubmarine: true, requestSubmarineDive: true);
        Equal(true, sink.Held.SetEquals([0x30]), "Cancel remap releases previous token");
        memory.Write(1, 4, 0x30);
        Equal(false, auto.Drive(FieldNavigationInput.None, true, true, worldSubmarine: true, requestSubmarineDive: true).Success,
            "Cancel alias with Menu is rejected across banks");
        Equal(0, sink.Held.Count, "alias failure releases every key");
        memory.Write(1, 4, 0); memory.Readable = false;
        auto.TryStart(NavigationAutoWalkDomain.WorldMap, true);
        Equal(false, auto.Drive(FieldNavigationInput.None, true, true, worldSubmarine: true, requestSubmarineDive: true).Success,
            "unreadable binding never guesses a key");
        auto.Suspend(); Equal(0, sink.Held.Count, "focus/pause release works without reading controls");
        memory.Readable = true;
        auto.TryStart(NavigationAutoWalkDomain.Field, true);
        auto.Drive(FieldNavigationInput.None, true, true, worldSubmarine: true, requestSubmarineDive: true);
        Equal(0, sink.Held.Count, "a field input owner cannot issue a submarine dive");
    }

    private static void UnreadableFirstUnderwaterSampleRetainsTheDestination()
    {
        foreach (var model in new[] { 26, 28 })
        {
            var fixture = new Fixture(model) { UnderwaterRowAvailable = false };
            fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Sea, Epoch);
            fixture.Surface.NoteAutoWalkStarted();
            fixture.Surface.Suspend("native dive loaded map2", preserveSubmarineJourney: true);
            var waiting = fixture.Underwater.Observe(fixture.Undersea, Epoch, true, 0);
            Contains(waiting?.Speech, "Waiting to confirm", "unreadable allocation is not a disappearance");
            Equal(true, fixture.Underwater.IsHoldingDestination, "underwater goal remains cancellable");
            Equal(null, fixture.Underwater.Observe(fixture.Undersea, Epoch.AddMilliseconds(100), true, 0)?.Speech,
                "retry message does not repeat");
            fixture.UnderwaterRowAvailable = true;
            Equal(true, fixture.Underwater.Observe(fixture.Undersea, Epoch.AddMilliseconds(200), true, 0)?.StartAutoWalk == true,
                "coherent next sample resumes the originally requested automatic leg");
        }
        var absent = new Fixture(26) { UnderwaterRowAvailable = false };
        absent.Journey.ReadSubmarineAvailability = () => new(true, false, false);
        absent.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, absent.Sea, Epoch);
        var stopped = absent.Underwater.Observe(absent.Undersea, Epoch, true, 0);
        Contains(stopped?.Speech, "no longer available", "coherent native absence stops immediately");
        Equal(false, absent.Underwater.IsHoldingDestination, "proven absent goal is cleared");
        var timeout = new Fixture(28) { UnderwaterRowAvailable = false };
        timeout.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, timeout.Sea, Epoch);
        timeout.Underwater.Observe(timeout.Undersea, Epoch, true, 0);
        Equal(true, timeout.Underwater.Observe(timeout.Undersea, Epoch.AddSeconds(9), true, 0)?.StopAutoWalk == true,
            "unreadable underwater allocation retry is bounded");
        Equal(false, timeout.Underwater.IsHoldingDestination, "failed retry cannot revive a later route");
    }

    private static void GenuineSailingInputFailureCannotRestartAfterManualDive()
    {
        var fixture = new Fixture();
        fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Deep, Epoch);
        fixture.Surface.NoteAutoWalkStarted();
        var sink = new Sink(); var memory = new Memory { Readable = false };
        using var auto = new NavigationAutoWalkController(sink, new HighwayDirectionInputMappingResolver(memory));
        auto.TryStart(NavigationAutoWalkDomain.WorldMap, true);
        Equal(false, auto.Drive(FieldNavigationInput.Right, true, true,
            worldSubmarine: true, holdSubmarineThrust: true).Success, "sailing mapping failure stops the owner");
        fixture.Surface.NoteAutoWalkStopped();
        Contains(fixture.Surface.Observe(fixture.Sea, Epoch.AddSeconds(1), false, 0)?.Speech,
            "Press Cancel", "failed automatic sailing retains ordinary manual guidance");
        fixture.Surface.Suspend("player dived manually", preserveSubmarineJourney: true);
        Equal(false, fixture.Underwater.Observe(fixture.Undersea, Epoch.AddSeconds(2), false, 0)?.StartAutoWalk == true,
            "a manual dive after an input failure cannot restart automatic sailing");
    }

    private static void AvailabilityUsesCoherentNativeAllocationFlags()
    {
        var memory = new Memory();
        memory.Wreck = 8; memory.Collection = 0;
        Equal(new WorldMapSubmarineAvailability(true, true, false), WorldMapSubmarineAvailability.Read(memory)!.Value,
            "bank0 bits6787/6800/6801 use exact native base and shift");
        memory.Collection = 3;
        Equal(new WorldMapSubmarineAvailability(true, false, true), WorldMapSubmarineAvailability.Read(memory)!.Value,
            "collected Key absent while wreck stays present");
        memory.Tear = true;
        Equal(true, WorldMapSubmarineAvailability.Read(memory) is null, "torn flags have no guessed availability");
    }

    private static void NativeGateAndConfirmedSlowLoad()
    {
        foreach (var blocked in new[] { new WorldMapSubmarineDiveState(1,0,0,1), new(1,1,1,1), new(1,1,0,0), new(0,1,0,1) })
        {
            var fixture = new Fixture { Gate = blocked };
            fixture.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, fixture.Sea, Epoch);
            fixture.Surface.NoteAutoWalkStarted();
            fixture.Surface.Observe(fixture.Sea, Epoch, true, 0);
            fixture.Surface.Observe(fixture.Sea, Epoch.AddSeconds(1), true, 0);
            fixture.Surface.TryResolveAutomaticInput(fixture.Sea, out _);
            Equal(false, fixture.Surface.AutomaticInputRequestsSubmarineDive, $"native gate prevents a wasted Cancel: {blocked}");
        }
        var unknown = new Fixture { Gate = null };
        unknown.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, unknown.Sea, Epoch);
        unknown.Surface.NoteAutoWalkStarted();
        Equal(true, unknown.Surface.Observe(unknown.Sea, Epoch, true, 0)?.StopAutoWalk == true,
            "unreadable native gate stops with an explanation");
        var slow = new Fixture();
        slow.Surface.HandleAction(FieldNavigationAction.ToggleBeacon, slow.Sea, Epoch);
        slow.Surface.NoteAutoWalkStarted();
        slow.Surface.Observe(slow.Sea, Epoch, true, 0);
        slow.Surface.Observe(slow.Sea, Epoch.AddMilliseconds(100), true, 0);
        slow.Surface.TryResolveAutomaticInput(slow.Sea, out _);
        slow.Surface.NoteSubmarineDiveDelivered(Epoch.AddMilliseconds(100));
        slow.Gate = new(4, 0, 0, 0);
        Equal(true, slow.Journey.ObserveNativeTransition(Epoch.AddMilliseconds(300)), "host pulse timer releases before scan throttle");
        slow.Surface.PauseForNativeControl();
        Equal(false, slow.Surface.Observe(slow.Sea, Epoch.AddSeconds(20), true, 0)?.StopAutoWalk == true,
            "native accepted dive is retained through a slow fade");
        slow.Surface.Suspend("map2 finally loaded", preserveSubmarineJourney: true);
        Equal(true, slow.Underwater.Observe(slow.Undersea, Epoch.AddSeconds(21), true, 0)?.StartAutoWalk == true,
            "accepted native dive resumes after more than eight seconds");

        var memory = new Memory();
        memory.Words[WorldMapDialogueReader.NativeWorldMainStateAddress] = 1;
        memory.Words[WorldMapDialogueReader.ControlAddress] = 1;
        memory.Words[WorldMapDialogueReader.NativeControlLockAddress] = 0;
        memory.Words[WorldMapDialogueReader.NativeMovementEnabledAddress] = 1;
        var reader = new WorldMapDialogueReader(memory);
        Equal(true, reader.TryReadSubmarineDiveState(out var gate) && gate.CanRequestDive, "native dive gate reads exact guest words");
        memory.TearWord = WorldMapDialogueReader.NativeControlLockAddress;
        Equal(false, reader.TryReadSubmarineDiveState(out _), "native gate rejects torn observations");
    }

    private static void InstalledDeepSeaRouteReachesDiveableSea()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (root is null || source is null) { Console.WriteLine("installed dive route checks skipped: roots unavailable."); return; }
        WorldMapTargetCatalog Catalog(WorldMapData map) => WorldMapTargetCatalog.Load(map,
            Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var surfaceMap = WorldMapDataLoader.Load(Path.Combine(root, "data", "wm", "wm0.map"), 0, 3);
        var catalog = Catalog(surfaceMap);
        var underwaterMap = WorldMapDataLoader.Load(Path.Combine(root, "data", "wm", "wm2.map"), 2, 0);
        Equal(true, underwaterMap.Triangles[34303].Neighbors.Contains(34602),
            "native underwater floor seam joins Lucrecia tunnel despite differing vertex heights");
        catalog.UnderwaterCatalog = Catalog(underwaterMap);
        Equal(true, catalog.UnderwaterCatalog.ReadTargets(WorldMapNavigationCategory.Locations,
            State(2, 124000, -3000, 116752, 3), []).Any(t => t.Label.StartsWith("Lucrecia", StringComparison.Ordinal)),
            "Lucrecia lake surfacing point is offered as a fixed underwater destination");
        var lake = catalog.UnderwaterCatalog.Locations.Single(t => t.SubmarineSurfacingPoint is not null);
        NativeSteeringPrerequisitesReleaseEstablishedApproaches(underwaterMap, catalog.UnderwaterCatalog, lake);
        FrozenSubmarineFinalApproachStops(underwaterMap, lake);
        WorldMapSubmarineNavigationTests.ReplayInstalledRoute(underwaterMap, catalog.UnderwaterCatalog,
            catalog.UnderwaterCatalog.Locations.Single(t => t.Label == "Sunken Gelnika"), lake);
        foreach (var (ox, oz) in new[] { (0, 0), (-200, 0), (200, 0), (0, -200), (0, 200) })
            Equal(true, WorldMapBroncoLanding.TryFindSurface(surfaceMap, lake.X + ox, lake.Z + oz, out var lakeSurface) &&
                lakeSurface.TerrainId == 3, "lake handoff has room for surfaced submarine footprint");
        var lakeSurfaceState = State(0, lake.X, 1761, lake.Z, 3);
        var docks = catalog.ReadTargets(WorldMapNavigationCategory.Regions, lakeSurfaceState, [])
            .Where(t => t.ArrivalTriangleIds.All(id => surfaceMap.Triangles[id].TerrainId == 18)).ToArray();
        Equal(1, docks.Length, "small Lucrecia lake dock is available in Regions");
        var grass = surfaceMap.Triangles.Where(t => t.TerrainId == 0)
            .MinBy(t => Math.Abs(t.Centroid.X - 171258) + Math.Abs(t.Centroid.Z - 147716))!;
        var highwind = new WorldMapEntitySnapshot(0x123400, 0, false, grass.Centroid.X, grass.Centroid.Y,
            grass.Centroid.Z, 0, grass.RegionId & 31, 3, 0);
        var foot = State(0, highwind.X, highwind.Y, highwind.Z, 0) with { PlayerModelId = 0, RegionId = highwind.RegionId };
        var boarding = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, foot, [highwind]).Single();
        Equal(true, boarding.NativeVehicleContact is not null && boarding.VehicleContactPoints.Count > 0,
            "Highwind approach uses native boarding contact rather than a nearby triangle");
        var contact = boarding.NativeVehicleContact!;
        var point = boarding.VehicleContactPoints.First();
        Equal(true, boarding.HasArrived(foot with { X = point.Value.X, Z = point.Value.Z }, point.Key),
            "Highwind boarding arrival occupies its measured native mask");
        var stale = foot with { X = highwind.X + 1024, NativeContactEntityPointer = highwind.GuestPointer };
        Equal(false, contact.IsSatisfiedBy(stale), "old Highwind contact cannot board outside native reach");
        var witnessed = foot with { X = highwind.X + 1000, NativeContactEntityPointer = highwind.GuestPointer };
        Equal(true, contact.IsWitnessedBy(witnessed), "rolled-back native Highwind contact is recognized");
        foreach (var flag in new byte[] { 0x10, 0x80 })
        {
            var blocked = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, foot,
                [highwind with { Flags = flag }]).Single();
            Equal(0, blocked.ArrivalTriangleIds.Count, "native disabled Highwind has no invented boarding point");
        }
        var aboard = foot with { PlayerModelId = 13, TerrainId = 3 };
        var transportNav = new WorldMapNavigationController(surfaceMap, new(surfaceMap),
            (s, c) => catalog.ReadTargets(c, s, [highwind]));
        transportNav.HandleAction(FieldNavigationAction.NextCategory, aboard, Epoch);
        Contains(transportNav.HandleAction(FieldNavigationAction.NextCategory, aboard, Epoch)?.Speech,
            "Submarine pen", "Highwind selection explains the dock and dismount needed first");
        Contains(transportNav.HandleAction(FieldNavigationAction.ToggleBeacon, aboard, Epoch)?.Speech,
            "press Cancel", "Highwind route does not claim a submarine can drive on land");
        HighwindRollbackContactIsARealBoardingArrival(surfaceMap, catalog);
        WorldMapSubmarineAvailability? flags = new(true, true, false);
        catalog.ReadSubmarineAvailability = () => flags;
        catalog.UnderwaterCatalog.ReadSubmarineAvailability = () => flags;
        Equal(true, catalog.UnderwaterCatalog.ReadTargets(WorldMapNavigationCategory.Locations,
            State(2, 124000, -3000, 116752, 3), []).Any(t => t.Label == "Key of the Ancients"),
            "first empty entity sample does not hide a present fixed Key");
        Equal(true, catalog.UnderwaterCatalog.ReadTargets(WorldMapNavigationCategory.Story,
            State(2, 124000, -3000, 116752, 3), []).Any(t => t.Label == "Huge Materia, red submarine wreck"),
            "underwater Story names the uncollected Huge Materia wreck");
        var state = State(0, 121103, -240, 113875, 26) with { CameraFront = 3672 };
        var rows = catalog.ReadTargets(WorldMapNavigationCategory.Locations, state, []);
        var weapon = highwind with { ModelId = 10 };
        Equal(true, catalog.ReadTargets(WorldMapNavigationCategory.Events, state, [weapon])
            .Any(t => t.Label == "Diamond Weapon"), "dive event appends rather than hiding visible native events");
        Equal(true, catalog.ReadTargets(WorldMapNavigationCategory.Story, state with { GameMoment = 1500 }, [weapon])
            .Any(t => t.Label == "Diamond Weapon"), "underwater Story choices preserve native surface Story");
        Equal(true, rows.Any(t => t.Label == "Sunken Gelnika") && rows.Any(t => t.Label == "Key of the Ancients") &&
            rows.Any(t => t.Label == "Red submarine wreck"), "surface sub lists native underwater destinations");
        Equal(true, catalog.ReadTargets(WorldMapNavigationCategory.Story, state, [])
            .Any(t => t.Label == "Huge Materia, red submarine wreck"), "uncollected Huge Materia named in Story");
        flags = new(true, false, true);
        rows = catalog.ReadTargets(WorldMapNavigationCategory.Locations, state, []);
        Equal(false, rows.Any(t => t.Label == "Key of the Ancients"), "collected Key isn't still offered");
        Equal(true, rows.Any(t => t.Label == "Red submarine wreck"), "wreck survives collection as in native allocation");
        Equal(false, catalog.ReadTargets(WorldMapNavigationCategory.Story, state, [])
            .Any(t => t.Label.Contains("Huge Materia")), "collected Materia isn't described as remaining");
        flags = null;
        Equal(true, catalog.ReadTargets(WorldMapNavigationCategory.Events, state, []).Any(t => t.Label == "Dive underwater"),
            "unreadable progress still offers ordinary dive route");
        var planner = new WorldMapRoutePlanner(surfaceMap) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var nav = new WorldMapNavigationController(surfaceMap, planner, (s, c) => catalog.ReadTargets(c, s, []))
            { SubmarineJourney = new() { ReadNativeDiveState = () => new(1, 1, 0, 1) } };
        nav.HandleAction(FieldNavigationAction.NextCategory, state, Epoch); // Story
        nav.HandleAction(FieldNavigationAction.NextCategory, state, Epoch); // Transportation
        nav.HandleAction(FieldNavigationAction.NextCategory, state, Epoch); // Events
        nav.HandleAction(FieldNavigationAction.ToggleBeacon, state, Epoch);
        nav.NoteAutoWalkStarted();
        Equal(true, nav.BeaconEnabled, $"log position has safe Sea route: {nav.LastDiagnostic}");
        var reached = false;
        for (var sample = 0; sample < 500; sample++)
        {
            var now = Epoch.AddMilliseconds(sample * 100);
            nav.Observe(state, now, true, 0);
            var moving = nav.TryResolveAutomaticInput(state, out var direction);
            if (nav.AutomaticInputRequestsSubmarineDive)
            { Equal(3, state.TerrainId, "replayed dive only at native Sea"); reached = true; break; }
            if (!moving) continue;
            Equal(true, WorldMapSubmarineSurfaceSteering.TryGetMask(direction, out var mask), "replayed native direction");
            for (var frame = 0; frame < 3; frame++)
            {
                var step = WorldMapSubmarineSurfaceSteering.Step(state.NativeCameraMode, state.NativeFrameMultiplier, state.CameraFront, mask);
                state = state with { X = state.X + step.DeltaX, Z = state.Z + step.DeltaZ, CameraFront = step.CameraFront };
                Equal(true, WorldMapBroncoLanding.TryFindSurface(surfaceMap, state.X, state.Z, out var floor), "accepted native surface");
                Equal(true, WorldMapTerrainPassability.CanTraverse(13, 0, floor.TerrainId), "route never leaves sub's native mask");
                state = state with { TerrainId = floor.TerrainId, TerrainScriptId = floor.TerrainScriptId, RegionId = floor.RegionId & 31 };
            }
        }
        Equal(true, reached, $"logged Deep sea reaches native dive handoff: {state.X},{state.Z}; {nav.LastDiagnostic}");
        Console.WriteLine($"installed automatic dive replay reached Sea at {state.X},{state.Z} from logged terrain26.");
    }

    private static void StagingMotionIsBoundedAndSuspensionRestartsItsClock()
    {
        var guard = new WorldMapSubmarineStagingConvergenceTracker();
        var state = State(2, 101925, -3000, 144448, 15);
        for (var tick = 0; tick < 300; tick++)
        {
            state = state with { CameraFront = tick * 16 % 4096 };
            Equal(false, guard.Observe(1, state, Epoch.AddMilliseconds(tick * 100), 0x48000, 0x38000, out _),
                "real turning keeps positioning alive before its absolute limit");
        }
        Equal(true, guard.Observe(1, state, Epoch.AddSeconds(30), 0x48000, 0x38000, out var timedOut),
            "continuous turning cannot keep a positioning leg alive indefinitely");
        Equal(true, timedOut, "absolute positioning limit is distinct from no motion");
        Equal(false, guard.Observe(2, state, Epoch.AddSeconds(30.1), 0x48000, 0x38000, out _),
            "a genuinely new positioning leg starts a fresh clock");
        guard.Reset();
        Equal(false, guard.Observe(2, state, Epoch.AddSeconds(34), 0x48000, 0x38000, out _), "explicit pause resets the baseline");
        Equal(false, guard.Observe(2, state, Epoch.AddSeconds(36.1), 0x48000, 0x38000, out _),
            "a suspended observation gap starts a fresh baseline");
        for (var tick = 1; tick < 50; tick++)
            Equal(false, guard.Observe(2, state, Epoch.AddSeconds(36.1).AddMilliseconds(tick * 100),
                0x48000, 0x38000, out _), "suspension does not consume the no-motion allowance");
        Equal(true, guard.Observe(2, state, Epoch.AddSeconds(41.1), 0x48000, 0x38000, out timedOut),
            "five seconds without native motion stops positioning");
        Equal(false, timedOut, "no-motion stop retains its own explanation");
    }

    private static void NativeSteeringPrerequisitesReleaseEstablishedApproaches(WorldMapData map, WorldMapTargetCatalog catalog, WorldMapNavigationTarget lake)
    {
        foreach (var (x, z, camera, phase) in new[]
        {
            (102338, 145010, 3584, "Final"),
            (101596, 144006, 3029, "Maneuver")
        })
        {
            Equal(true, WorldMapBroncoLanding.TryFindSurface(map, x, z, out var floor), "native control fixture has seabed");
            var state = State(2, x, -3000, z, floor.TerrainId) with
            { CameraFront = camera, RegionId = floor.RegionId & 31, TerrainScriptId = floor.TerrainScriptId };
            var invalid = new[]
            {
                state with { HasNativeControlMode = false },
                state with { NativeCameraMode = 0 }, state with { NativeCameraMode = 1 },
                state with { NativeFrameMultiplier = 0 }, state with { NativeFrameMultiplier = 5 },
                state with { CameraFront = -1 }, state with { CameraFront = 4096 }
            };
            foreach (var unreadable in invalid)
            {
                var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
                var nav = new WorldMapNavigationController(map, planner, (_, _) => [lake]);
                nav.HandleAction(FieldNavigationAction.ToggleBeacon, state, Epoch);
                Equal(true, nav.BeaconEnabled, "native control fixture starts its actual route");
                for (var poll = 0; poll < 500 && nav.SubmarineTerminalState != phase; poll++)
                    nav.TryResolveAutomaticInput(state, out _);
                Equal(phase, nav.SubmarineTerminalState, "installed fixture establishes its actual checked approach");
                var sink = new Sink();
                using var owner = new NavigationAutoWalkController(sink, new HighwayDirectionInputMappingResolver(new Memory()));
                owner.TryStart(NavigationAutoWalkDomain.WorldMap, true);
                Equal(true, owner.Drive(FieldNavigationInput.Left, true, true, worldSubmarine: true).Success,
                    "an actual owner has prior submarine steering input");
                Equal(true, sink.Held.Count > 0, "the recording sink contains a prior held native key");
                var resolved = nav.TryResolveAutomaticInput(unreadable, out var input);
                Equal(false, resolved, $"{phase} cannot bypass unreadable controls: {unreadable.HasNativeControlMode}/{unreadable.NativeCameraMode}/{unreadable.NativeFrameMultiplier}/{unreadable.CameraFront}");
                Equal(FieldNavigationInput.None, input, "unreadable native controls emit no direction");
                Equal(false, nav.AutomaticInputHoldsSubmarineThrust, "unreadable native controls emit no thrust");
                Equal("None", nav.SubmarineTerminalState, "unreadable native controls invalidate the prior terminal plan");
                Equal(true, owner.Drive(input, resolved, nav.BeaconEnabled,
                    worldSubmarine: nav.AutomaticInputIsWorldSubmarine,
                    holdSubmarineThrust: nav.AutomaticInputHoldsSubmarineThrust).Success, "the host releases prior input");
                Equal(0, sink.Held.Count, "the actual input owner released every prior key");
                Equal(true, owner.Enabled && nav.BeaconEnabled, "temporary unreadability retains intent and ordinary guidance");
                Equal(true, nav.TryResolveAutomaticInput(state with { NativeCameraMode = 3 }, out input), "coherent mode3 controls recover");
                Equal("Prepare", nav.SubmarineTerminalState, "recovery requires a new neutral preparation poll");
                Equal(true, input == FieldNavigationInput.None && !nav.AutomaticInputHoldsSubmarineThrust,
                    "coherent recovery cannot reuse the old movement command");
            }
        }
        Console.WriteLine("native submarine steering prerequisites: Final and Maneuver fail closed, release actual input, retain guidance and recover through neutral preparation.");
    }

    private static void FrozenSubmarineFinalApproachStops(WorldMapData map, WorldMapNavigationTarget lake)
    {
        foreach (var (x, z, camera) in new[]
        {
            (101925, 144448, 3221)
        })
        {
            var planner = new WorldMapRoutePlanner(map);
            Equal(true, planner.HasSubmarineFootprint(x, z), "installed frozen approach fits the native submarine body");
            Equal(true, WorldMapBroncoLanding.TryFindSurface(map, x, z, out var floor), "frozen approach has a native floor");
            var state = State(2, x, -3000, z, floor.TerrainId) with
                { CameraFront = camera, RegionId = floor.RegionId & 31, TerrainScriptId = floor.TerrainScriptId };
            var nav = new WorldMapNavigationController(map, planner, (_, _) => [lake]);
            nav.HandleAction(FieldNavigationAction.ToggleBeacon, state, Epoch);
            Equal(true, nav.BeaconEnabled, "frozen approach starts navigation");
            var stopped = false;
            for (var tick = 0; tick <= 140; tick++)
            {
                var output = nav.Observe(state, Epoch.AddMilliseconds(tick * 50), true);
                if (output?.StopAutoWalk == true)
                {
                    Contains(output.Value.Speech, "Could not get closer", "frozen submarine explains the stop");
                    Equal(false, nav.AutomaticInputHoldsSubmarineThrust, "frozen approach releases thrust");
                    stopped = true;
                    break;
                }
                nav.TryResolveAutomaticInput(state, out _);
            }
            Equal(true, stopped, $"blocked native motion must stop within seven seconds at {x},{z}, camera {camera}");
        }
    }

    private static void HighwindRollbackContactIsARealBoardingArrival(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        Equal(true, WorldMapBroncoLanding.TryFindSurface(map, 171487, 147725, out var parkedFloor),
            "installed Highwind boundary fixture has ground");
        var highwind = new WorldMapEntitySnapshot(0x123400, 0, false, 171487, parkedFloor.Centroid.Y,
            147725, parkedFloor.TerrainId, parkedFloor.RegionId & 31, 3, 0);
        Equal(true, WorldMapBroncoLanding.TryFindSurface(map, 170991, 147437, out var rollbackFloor),
            "native rollback position has ground");
        var player = State(0, 170991, rollbackFloor.Centroid.Y, 147437, rollbackFloor.TerrainId) with
            { PlayerModelId = 0, RegionId = rollbackFloor.RegionId & 31,
              TerrainScriptId = rollbackFloor.TerrainScriptId, NativePlayerEntityPointer = 0x120000 };
        var target = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, player, [highwind]).Single();
        Equal(92871, rollbackFloor.Id, "native rollback face from installed map");
        Equal(false, target.ArrivalTriangleIds.Contains(rollbackFloor.Id), "rollback face has no native mask point");
        Equal(true, WorldMapVehicleObstacles.Blocks(0, 170991, 147477, 3, highwind.X, highwind.Z,
            map.WrapWidth, map.WrapHeight), "refused next step touches native Highwind mask");
        Equal(false, target.HasArrived(player, rollbackFloor.Id), "neighboring rollback face alone is not boarding proof");
        var nav = new WorldMapNavigationController(map,
            new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds }, (_, _) => [target]);
        Equal(true, nav.HandleAction(FieldNavigationAction.ToggleBeacon, player, Epoch) is not null && nav.BeaconEnabled,
            "Highwind boundary fixture begins its approach");
        player = player with { NativeContactEntityPointer = highwind.GuestPointer };
        Equal(true, target.HasArrived(player, rollbackFloor.Id), "credible rejected-step witness is enough to board");
        Contains(nav.Observe(player, Epoch.AddMilliseconds(50), true)?.Speech, "Press Confirm to board",
            "controller stops at witnessed Highwind contact across the face boundary");
        Equal(false, nav.BeaconEnabled, "witnessed boarding arrival stops further walking");
    }

    private static WorldMapStateSnapshot State(int map, int x, int y, int z, int terrain) =>
        new(3, map, map == 0 ? 3 : 0, 1396, x, y, z, 0, 0, terrain, 0, 13, 0, 0, default)
        { HasNativeControlMode = true, NativeCameraMode = 2, NativeFrameMultiplier = 2 };

    private sealed class Fixture
    {
        internal WorldMapNavigationController Surface, Underwater;
        internal WorldMapSubmarineDiveState? Gate = new(1, 1, 0, 1);
        internal WorldMapSubmarineJourney Journey;
        internal WorldMapStateSnapshot Deep = State(0, 300, -240, 300, 26);
        internal WorldMapStateSnapshot Sea = State(0, 1400, -240, 300, 3);
        internal WorldMapStateSnapshot Undersea = State(2, 1400, -3000, 300, 3);
        internal bool UnderwaterRowAvailable = true;
        internal Fixture(int model = 17)
        {
            var surface = Map(0); var under = Map(2); var journey = new WorldMapSubmarineJourney()
                { ReadNativeDiveState = () => Gate };
            Journey = journey;
            var label = model switch { 26 => "Key of the Ancients", 28 => "Red submarine wreck", _ => "Sunken Gelnika" };
            var destination = new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
                label, 1600, -4400, 600, 2, 0, $"world-underwater:{model}", new HashSet<int> { 2, 3 })
                { NativeUnderwaterArrival = new(under, model, 1600, -4400, 600, 975, new HashSet<int>()) };
            var proxy = new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
                label, 1500, 0, 300, 2, 0, $"dive:{model}", new HashSet<int> { 2, 3 })
                { SubmarineDiveDestination = new(destination.StableId, destination.Label) { NativeModelId = model } };
            Surface = new(surface, new(surface), (_, _) => [proxy]) { SubmarineJourney = journey };
            Underwater = new(under, new(under), (_, _) => UnderwaterRowAvailable ? [destination] : []) { SubmarineJourney = journey };
        }
        private static WorldMapData Map(int type)
        {
            var a = new WorldMapVertex(0, -4500, 0); var b = new WorldMapVertex(1000, -4500, 0);
            var c = new WorldMapVertex(0, -4500, 1000); var d = new WorldMapVertex(1000, -4500, 1000);
            var e = new WorldMapVertex(2000, -4500, 0); var f = new WorldMapVertex(2000, -4500, 1000);
            WorldMapTriangle T(int id, WorldMapVertex x, WorldMapVertex y, WorldMapVertex z, int terrain, int[] neighbors) =>
                new(id, 0, 0, 0, 0, id, x, y, z, terrain, 0, 0, 0, false, neighbors);
            return new(type, type == 0 ? 3 : 0, 9, 7, 12,
                [T(0,a,b,c,type == 0 ? 26 : 3,[1]), T(1,b,d,c,type == 0 ? 26 : 3,[0,2]),
                 T(2,b,e,d,3,[1,3]), T(3,e,f,d,3,[2])], [], "dive fixture");
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly byte[] table = new byte[HighwayDirectionInputMappingResolver.MappingTableSize];
        internal bool Readable = true, Tear;
        internal byte Wreck, Collection;
        private int flagReads;
        internal readonly Dictionary<uint, int> Words = [];
        internal uint TearWord;
        private int wordReads;
        internal Memory() { Write(0, 5, 0x2D); Write(0, 6, 0x2C); Write(0,12,0x48); Write(0,13,0x4D); Write(0,14,0x50); Write(0,15,0x4B); }
        internal void Write(int bank, int slot, uint token) => BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(bank * 0x64 + slot * 4, 4), token);
        public bool TryRead(uint address, Span<byte> bytes)
        {
            if (!Readable) return false;
            if (bytes.Length == 1 && address == WorldMapStateReader.AddressCurrentModule) { bytes[0] = 3; return true; }
            if (bytes.Length == 4 && Words.TryGetValue(address, out var word))
            { if (address == TearWord && ++wordReads > 1) word++;
              BinaryPrimitives.WriteInt32LittleEndian(bytes, word); return true; }
            if (address == HighwayDirectionInputMappingResolver.MappingTableAddress && bytes.Length == table.Length)
            { table.CopyTo(bytes); return true; }
            if (bytes.Length == 1 && address is WorldMapSubmarineAvailability.WreckFlagsAddress or WorldMapSubmarineAvailability.CollectionFlagsAddress)
            { bytes[0] = address == WorldMapSubmarineAvailability.WreckFlagsAddress ? Wreck : Collection;
              if (Tear && ++flagReads > 2) bytes[0] ^= 1; return true; }
            return false;
        }
    }
    private sealed class Sink : IHighwayKeyboardInputSink
    {
        internal readonly HashSet<int> Held = [];
        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        { foreach (var t in transitions) { var key = t.ScanCode | (t.IsExtended ? 0x80 : 0); if (t.IsKeyDown) Held.Add(key); else Held.Remove(key); }
          return new(transitions.Count, 0); }
    }
    private static void Contains(string? text, string expected, string label) => Equal(true,
        text?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true, $"{label}: {text}");
    private static void Equal<T>(T expected, T actual, string label)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}"); }
}
