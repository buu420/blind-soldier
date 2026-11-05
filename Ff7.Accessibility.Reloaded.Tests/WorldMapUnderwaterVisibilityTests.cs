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
            AllThreeCameraTranslationComponentsUseThePackedNativeLayout,
            TheNativeCameraFrameMakesEmeraldSelectableAndTrackable,
            TheLiveNativeEmeraldIsTrackedBeforeAnySightingAndThroughItsLifecycle,
            HiddenEmeraldPositionsComeFromTheCheckedNativeCapture,
            TwoLiveEmeraldNodesAreNeverTracked,
            OrdinarySubmarineRoutesAvoidTheLiveEmeraldPosition,
            RuntimeRetainsVisibilityRejectionReasonsAndResetsThem,
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
            AStaleCallerPositionIsReplacedByTheCheckedDrawPosition,
            MovingEmeraldUsesOneCheckedFrameForVisibilityAndNavigation,
            EmeraldOverUntraversableSeabedHasANativeApproachWhetherOrNotDrawn,
            UnderwaterEventsAnnounceTheirPositionWhenEmeraldJoinsTheList,
            UnderwaterEventsDoNotSubstituteTheAnnouncedTarget,
            PatrolDriftReplansBeforeTheContactVolumeIsMissed,
            RefreshedPositionsCannotReidentifyANodeAsThePlayerOrAnotherModel,
            AVerticalTerrainFaceBetweenTheCameraAndEmeraldHidesIt
        })
        {
            try { test(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        if (failures.Count != 0) throw new AggregateException(failures);
        Console.WriteLine("world-map underwater visibility and live Emerald tracking tests passed (36 scenarios).");
    }

    /// <summary>The live Emerald contact: tracking, pursuit, the checked read path, identity and ordinary-route avoidance.</summary>
    internal static void RunPersistentEmerald()
    {
        List<Exception> failures = [];
        foreach (var test in new Action[]
        {
            TheLiveNativeEmeraldIsTrackedBeforeAnySightingAndThroughItsLifecycle,
            TheNativeCameraFrameMakesEmeraldSelectableAndTrackable,
            HiddenEmeraldPositionsComeFromTheCheckedNativeCapture,
            TwoLiveEmeraldNodesAreNeverTracked,
            OrdinarySubmarineRoutesAvoidTheLiveEmeraldPosition
        })
        {
            try { test(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        if (failures.Count != 0) throw new AggregateException(failures);
        Console.WriteLine("live Emerald tracking, pursuit, read-path, identity and ordinary-route avoidance checks passed (5 scenarios).");
    }

    /// <summary>
    /// The user's 0.10.10 correction: Emerald is tracked whenever wm2.ev's live model-30 node
    /// exists, before any sighting, so a player can approach or avoid it as they choose.
    /// C_007641A7 runs every entity's script each frame, drawn or not, and Emerald's function 2
    /// both moves its patrol and tests the encounter distance there, so the live node's position
    /// is where the encounter happens. The renderer only decides what may be called visible.
    /// </summary>
    private static void TheLiveNativeEmeraldIsTrackedBeforeAnySightingAndThroughItsLifecycle()
    {
        if (LoadInstalledScene("installed live Emerald tracking checks", 1396) is not { } installed) return;
        var (scene, catalog, runtime, state) = installed;
        var clock = new DateTime(2026, 11, 4, 21, 0, 0, DateTimeKind.Utc);
        void ObserveFrame() => ObserveNativeFrame(installed);
        WorldMapEntitySnapshot Live() => runtime.Entities.Single(e => !e.IsPlayer && e.ModelId == 30);
        WorldMapNavigationTarget? Row() => catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities)
            .SingleOrDefault(t => t.Label == "Emerald Weapon");
        bool RouteEndsInsideTheLiveContactVolume() =>
            Row()?.NativeUnderwaterArrival is { } arrival &&
            runtime.Navigation.Probe.Route?.Waypoints is { Count: > 0 } route &&
            arrival.DistanceFrom(route[^1].X, route[^1].Y, route[^1].Z) + 120 + 24 <= arrival.ManhattanBound;
        bool PursuitDescends() =>
            runtime.Navigation.TryResolveAutomaticInput(state, out var input) && input == FieldNavigationInput.Up;

        // The first underwater frame: the renderer has never drawn Emerald.
        scene.HideEmeraldFromTheRenderer();
        ObserveFrame();
        Check(!Live().IsVisibleUnderwater && runtime.ObserveUnderwaterSightings(state, nowUtc: clock) is null,
            "an undrawn Emerald is neither visible nor announced");
        WorldMapNavigationOutput? menu = null;
        for (var i = 0; i < 3; i++) menu = runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state, clock);
        var heard = menu?.Speech ?? string.Empty;
        Check(heard.StartsWith("Events, Emerald Weapon", StringComparison.Ordinal) &&
            !heard.Contains("unknown", StringComparison.OrdinalIgnoreCase) &&
            !heard.Contains("last-seen", StringComparison.OrdinalIgnoreCase) &&
            !heard.Contains("Route unavailable", StringComparison.Ordinal),
            "the live native Emerald is an ordinary Events row before any sighting: " + heard);
        var first = Row();
        Check(first is not null && first.X == RefX && first.Y == -4250 && first.Z == RefZ + 4000 &&
            first.NativeUnderwaterArrival?.ModelId == 30,
            "the row is the live native position with Emerald's native contact volume");
        var started = runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, clock);
        Check(runtime.Navigation.BeaconEnabled && runtime.Navigation.Probe.TargetId == first!.StableId,
            "the never-sighted Emerald can be pursued: " + started?.Speech + "; " + runtime.Navigation.LastDiagnostic);
        Check(PursuitDescends(), "explicit pursuit descends toward Emerald's approach depth without any sighting");
        Check(RouteEndsInsideTheLiveContactVolume(), "the pursuit route ends inside the live contact volume");

        // It patrols out of view. Each frame's live position moves the row and the pursuit, and
        // render loss neither pauses, ends nor re-announces the route.
        for (var step = 1; step <= 8; step++)
        {
            scene.PatrolOutOfView(300);
            ObserveFrame();
            var output = runtime.Navigation.Observe(state, clock.AddMilliseconds(step * 100), automaticWalkActive: true);
            Check(!Live().IsVisibleUnderwater && Live().X == RefX + step * 300,
                "the published position is the live patrol position: " + runtime.LastUnderwaterVisibilityDiagnostic);
            Check(output?.StopAutoWalk != true && runtime.Navigation.BeaconEnabled,
                "out-of-view patrol never ends the pursuit: " + output?.Speech);
            Check(output?.Speech is null, "the moving target's route refreshes without speech: " + output?.Speech);
            Check(Row()?.X == RefX + step * 300, "Events follows the out-of-view patrol");
            Check(PursuitDescends(), "pursuit input continues while Emerald is out of view");
            Check(RouteEndsInsideTheLiveContactVolume(), "the refreshed route still ends inside the live contact volume");
        }
        Check(runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddSeconds(1)) is null,
            "live tracking is never announced as a sighting");

        // The renderer's own state is unusable: a skewed camera, then no guest renderer at all.
        scene.PutRotation([4096, 0, 0, 2896, 2896, 0, 0, 0, 4096]);
        scene.PatrolOutOfView(300);
        ObserveFrame();
        Check(runtime.LastUnderwaterVisibilityDiagnostic.Contains("camera rotation", StringComparison.Ordinal) &&
            !Live().IsVisibleUnderwater && Live().X == RefX + 2700,
            "an unusable camera withholds visibility, never the live position: " + runtime.LastUnderwaterVisibilityDiagnostic);
        var skewed = runtime.Navigation.Observe(state, clock.AddMilliseconds(900), automaticWalkActive: true);
        Check(skewed?.StopAutoWalk != true && PursuitDescends(), "camera loss alone does not interrupt the pursuit: " + skewed?.Speech);
        scene.PutRotation([4096, 0, 0, 0, 4096, 0, 0, 0, 4096]);
        runtime.UpdateEntities(state, new WorldMapEntityReader(scene.Memory).Read().Entities, null);
        Check(!Live().IsVisibleUnderwater && Live().X == RefX + 2700 && PursuitDescends(),
            "without renderer memory the checked entity scan still carries the live position");

        // A torn world-state read releases input at once; the next valid frame is current again.
        var outage = clock.AddSeconds(2);
        runtime.ObserveUnavailableState(outage);
        Check(!runtime.Navigation.TryResolveAutomaticInput(state, out _), "an unavailable state releases pursuit input immediately");
        scene.PatrolOutOfView(600);
        ObserveFrame();
        var resumed = runtime.Navigation.Observe(state, outage.AddMilliseconds(100), automaticWalkActive: true);
        Check(resumed?.StopAutoWalk != true && runtime.Navigation.BeaconEnabled && Live().X == RefX + 3300 &&
            RouteEndsInsideTheLiveContactVolume() && PursuitDescends(),
            "the next valid frame resumes at the live position, never the one before the outage: " + resumed?.Speech);
        runtime.ObserveUnavailableState(outage.AddSeconds(1));
        runtime.ObserveUnavailableState(outage.AddSeconds(2));
        scene.PatrolOutOfView(300);
        ObserveFrame();
        var afterOutage = runtime.Navigation.Observe(state, outage.AddSeconds(2).AddMilliseconds(100), automaticWalkActive: true);
        Check(afterOutage?.StopAutoWalk != true && runtime.Navigation.BeaconEnabled && RouteEndsInsideTheLiveContactVolume(),
            "after a sustained outage only the live node is tracked: " + afterOutage?.Speech);

        // A battle, defeat or reload retires the native node.
        scene.RetireEmeraldFromTheNativeList();
        ObserveFrame();
        Check(!runtime.Entities.Any(e => e.ModelId == 30) && Row() is null,
            "a retired node leaves no contact, row or remembered position");
        Check(!runtime.Navigation.TryResolveAutomaticInput(state, out _),
            "retirement releases pursuit input before the next observation");
        var retired = runtime.Navigation.Observe(state, outage.AddSeconds(3), automaticWalkActive: true);
        Check(retired?.StopAutoWalk == true && !runtime.Navigation.BeaconEnabled &&
            retired.Value.Speech?.Contains("no longer there", StringComparison.Ordinal) == true,
            "the next observation ends the retired pursuit: " + retired?.Speech);

        // The pool rebuilt the node at another address: the announced selection never moves onto it.
        scene.ReplaceEmeraldNode(EmeraldPointer + 0x1000);
        ObserveFrame();
        var stale = runtime.Navigation.HandleAction(FieldNavigationAction.RepeatTarget, state, outage.AddSeconds(4))?.Speech ?? string.Empty;
        Check(stale.Contains("no longer available", StringComparison.Ordinal),
            "a replaced node does not inherit the announced selection: " + stale);
        var replacement = runtime.Navigation.HandleAction(FieldNavigationAction.NextTarget, state, outage.AddSeconds(4))?.Speech ?? string.Empty;
        Check(replacement.StartsWith("Events, Emerald Weapon", StringComparison.Ordinal) &&
            Row()?.StableId == $"world-entity:{EmeraldPointer + 0x1000:X8}:30",
            "an explicit choice selects the replacement node: " + replacement);

        // A new world or save starts from the live frame alone.
        runtime.Reset();
        Check(runtime.Entities.Count == 0 && !runtime.Navigation.BeaconEnabled, "reset retains no contact or route");
        ObserveFrame();
        Check(Row() is { } afterReset && afterReset.StableId == $"world-entity:{EmeraldPointer + 0x1000:X8}:30" &&
            afterReset.X == Live().X && !runtime.Navigation.BeaconEnabled,
            "after a reset the live node is offered again from its current position only");
        Console.WriteLine("installed live Emerald tracking before sighting, silent pursuit refresh, camera loss, outage, retirement, replacement and reset checks passed.");
    }

    /// <summary>
    /// Both hosts give the runtime a checked native entity scan and the guest address space. An
    /// undrawn Emerald's position comes from the same coherent capture as visibility or, with no
    /// coherent underwater capture, from that checked scan: never from an older list, and never
    /// for a node a newer coherent capture of the same world no longer has.
    /// </summary>
    private static void HiddenEmeraldPositionsComeFromTheCheckedNativeCapture()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT"))) return;
        static WorldMapNavigationTarget? Row(WorldMapRuntimeContext runtime, Scene scene) =>
            runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, scene.State, runtime.Entities)
                .SingleOrDefault(t => t.Label == "Emerald Weapon");

        var scene = Scene.Standard();
        scene.HideEmeraldFromTheRenderer();
        scene.MakeCallerPositionStale(byX: 700);
        var runtime = CreateRuntime(scene.Map);
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        var hidden = runtime.Entities.Single(e => e.ModelId == 30);
        Check(!hidden.IsVisibleUnderwater && hidden.X == RefX && hidden.Z == RefZ + 4000,
            "an undrawn Emerald is published at the checked native capture, not the older caller scan: " + hidden.X);
        Check(Row(runtime, scene)?.X == RefX, "Events uses that same checked capture");

        scene = Scene.Standard();
        scene.HideEmeraldFromTheRenderer();
        scene.RetireEmeraldFromTheNativeList();
        runtime = CreateRuntime(scene.Map);
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        Check(!runtime.Entities.Any(e => e.ModelId == 30) && Row(runtime, scene) is null,
            "a node the newer coherent capture no longer lists is not tracked from the older scan");

        scene = Scene.Standard();
        scene.Memory.ChangeEveryRead(0x00DFC448);
        runtime = CreateRuntime(scene.Map);
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        var scanned = runtime.Entities.Single(e => e.ModelId == 30);
        Check(runtime.LastUnderwaterVisibilityDiagnostic.Contains("torn", StringComparison.Ordinal) &&
            !scanned.IsVisibleUnderwater && scanned.X == RefX && Row(runtime, scene)?.X == RefX,
            "without a coherent capture the checked entity scan still tracks Emerald, never as visible: " +
            runtime.LastUnderwaterVisibilityDiagnostic);

        // World changes belong to the hosts' double-sampled state read. A capture that already
        // sees another map neither confirms nor retires the scan, so a battle starting in the
        // gap cannot be announced as Emerald leaving.
        scene = Scene.Standard();
        scene.Memory.PutInt(0x00E045E8, 0);
        scene.MakeCallerPositionStale(byX: 700);
        runtime = CreateRuntime(scene.Map);
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        var unconfirmed = runtime.Entities.Single(e => e.ModelId == 30);
        Check(!unconfirmed.IsVisibleUnderwater && unconfirmed.X == RefX + 700,
            "a capture of another map leaves the checked scan in place and is never visible");
    }

    /// <summary>wm2.ev allocates one model-30 node. Two cannot be told apart, so neither is tracked.</summary>
    private static void TwoLiveEmeraldNodesAreNeverTracked()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT"))) return;
        var scene = Scene.Standard();
        var runtime = CreateRuntime(scene.Map);
        var single = scene.Entities.Single(e => e.ModelId == 30) with { IsVisibleUnderwater = true };
        var other = single with { GuestPointer = EmeraldPointer + 0x2000, X = single.X + 3000 };
        int Rows() => runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, scene.State, runtime.Entities)
            .Count(t => t.Label == "Emerald Weapon");
        runtime.UpdateEntities([single, other]);
        Check(Rows() == 0, "two model-30 nodes cannot be told apart, even when both claim to be drawn");
        runtime.UpdateEntities([single with { IsVisibleUnderwater = false }]);
        Check(Rows() == 1, "the single live node is tracked, drawn or not");

        // Pursuit input needs that single node in the same frame.
        var planner = new WorldMapRoutePlanner(scene.Map);
        var state = scene.State with { Y = -4250, HasNativeControlMode = true, NativeCameraMode = 3, NativeFrameMultiplier = 1 };
        var aim = new WorldMapRouteWaypoint(RefX, -4250, RefZ - 1000);
        Check(planner.TryResolvePlayerTriangle(state with { X = aim.X, Z = aim.Z }, out var goal), "pursuit approach has native floor");
        var entity = single with { X = aim.X + 1024, Y = aim.Y, Z = aim.Z, IsVisibleUnderwater = false };
        var target = new WorldMapNavigationTarget(WorldMapNavigationCategory.Events, WorldMapTargetKind.Event,
            "Emerald Weapon", entity.X, entity.Y, entity.Z, goal, 0, $"world-entity:{EmeraldPointer:X8}:30", new HashSet<int> { goal })
        {
            NativeUnderwaterArrival = new(scene.Map, 30, entity.X, entity.Y, entity.Z, 1215, new HashSet<int> { goal }),
            UnderwaterApproachPoints = new Dictionary<int, WorldMapRouteWaypoint> { [goal] = aim }
        };
        IReadOnlyList<WorldMapEntitySnapshot> frame = [entity];
        var navigation = new WorldMapNavigationController(scene.Map, planner,
            (_, category) => category == WorldMapNavigationCategory.Events ? [target] : [], entityProvider: () => frame);
        for (var i = 0; i < 3; i++) navigation.HandleAction(FieldNavigationAction.NextCategory, state);
        navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Check(navigation.BeaconEnabled && navigation.TryResolveAutomaticInput(state, out _),
            "a single live node drives the pursuit, drawn or not: " + navigation.LastDiagnostic);
        frame = [entity, other with { IsVisibleUnderwater = false }];
        Check(!navigation.TryResolveAutomaticInput(state, out _) && !navigation.AutomaticInputHoldsSubmarineThrust,
            "a second model-30 node releases pursuit input in that same frame");
    }

    /// <summary>
    /// Ordinary submarine travel keeps entry depth, where wm2.ev's Emerald (Y = -4250, contact
    /// at a wrapped three-axis Manhattan distance of 1215) cannot reach it. Leaving that depth is
    /// the player's own descent at a destination, so the handoff, and any change while holding
    /// there, names the danger from the same live position a pursuit would use.
    /// </summary>
    private static void OrdinarySubmarineRoutesAvoidTheLiveEmeraldPosition()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (dataRoot is null || sourceRoot is null)
        {
            Console.WriteLine("installed ordinary-route Emerald avoidance checks skipped: data/source roots were not provided.");
            return;
        }

        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm2.map"), 2, 0);
        var catalog = CreateRuntime(map).Catalog;
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var gelnika = catalog.Locations.Single(t => t.Label == "Sunken Gelnika");
        var top = new WorldMapStateSnapshot(3, 2, 0, 1396, gelnika.X, -3000, gelnika.Z, 0, 0, 3, 0, 13, 0, 0, default)
            { HasNativeControlMode = true, NativeCameraMode = 2, NativeFrameMultiplier = 2 };
        WorldMapEntitySnapshot EmeraldAt(int east) =>
            new(EmeraldPointer, 0, false, gelnika.X + east, -4250, gelnika.Z, 3, 0, 30, 0x02);
        IReadOnlyList<WorldMapEntitySnapshot> frame = [EmeraldAt(900)];
        var clock = new DateTime(2026, 11, 4, 22, 0, 0, DateTimeKind.Utc);
        string Handoff(out WorldMapNavigationController navigation)
        {
            navigation = new WorldMapNavigationController(map, planner,
                (_, category) => category == WorldMapNavigationCategory.Locations ? [gelnika] : [],
                entityProvider: () => frame);
            navigation.HandleAction(FieldNavigationAction.ToggleBeacon, top, clock);
            return navigation.Observe(top, clock.AddMilliseconds(100), automaticWalkActive: true)?.Speech ?? string.Empty;
        }

        var handoff = Handoff(out var holding);
        Check(handoff.Contains("Hold Up to descend", StringComparison.Ordinal) &&
            handoff.Contains("Emerald Weapon is close to this descent, 2 counts away", StringComparison.Ordinal) &&
            !handoff.Contains("may be nearby", StringComparison.Ordinal),
            "the descent handoff names the live Emerald danger: " + handoff);
        Check(holding.BeaconEnabled && !holding.TryResolveAutomaticInput(top, out _),
            "an ordinary route still leaves the descent to the player");
        frame = [EmeraldAt(6000)];
        var away = holding.Observe(top, clock.AddMilliseconds(200), automaticWalkActive: true)?.Speech ?? string.Empty;
        Check(away.Contains("Emerald Weapon has moved away from this descent, 12 counts away", StringComparison.Ordinal),
            "the live patrol leaving the descent is announced: " + away);
        Check(holding.Observe(top, clock.AddMilliseconds(300), automaticWalkActive: true)?.Speech is null,
            "an unchanged danger is not repeated");
        frame = [EmeraldAt(1500)];
        var back = holding.Observe(top, clock.AddMilliseconds(400), automaticWalkActive: true)?.Speech ?? string.Empty;
        Check(back.Contains("Emerald Weapon is approaching this descent, 3 counts away", StringComparison.Ordinal),
            "the live patrol returning toward the descent is announced: " + back);
        var repeated = holding.HandleAction(FieldNavigationAction.RepeatTarget, top, clock.AddMilliseconds(500))?.Speech ?? string.Empty;
        Check(repeated.Contains("Emerald Weapon is close to this descent, 3 counts away", StringComparison.Ordinal),
            "repeating the handoff reports the current live danger: " + repeated);

        frame = [EmeraldAt(6000)];
        var far = Handoff(out _);
        Check(far.Contains("Emerald Weapon is 12 counts away.", StringComparison.Ordinal) &&
            !far.Contains("close", StringComparison.Ordinal),
            "a distant live Emerald is reported without a danger warning: " + far);
        frame = [];
        var absent = Handoff(out _);
        Check(absent.Contains("Hold Up to descend", StringComparison.Ordinal) && !absent.Contains("Emerald", StringComparison.Ordinal),
            "with no live Emerald node there is no Emerald warning: " + absent);
        frame = [EmeraldAt(900), EmeraldAt(900) with { GuestPointer = EmeraldPointer + 0x2000 }];
        var ambiguous = Handoff(out _);
        Check(ambiguous.Contains("Emerald Weapon may be nearby at this depth.", StringComparison.Ordinal),
            "two Emerald nodes cannot be placed, so the general warning remains: " + ambiguous);
        Console.WriteLine("installed ordinary-route descent warnings follow the live Emerald position.");
    }

    private sealed record InstalledEmeraldScene(Scene Scene, WorldMapTargetCatalog Catalog,
        WorldMapRuntimeContext Runtime, WorldMapStateSnapshot State);

    /// <summary>The standard native frame over the installed underwater map; null when the roots are absent.</summary>
    private static InstalledEmeraldScene? LoadInstalledScene(string checks, int gameMoment)
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (dataRoot is null || sourceRoot is null)
        {
            Console.WriteLine(checks + " skipped: data/source roots were not provided.");
            return null;
        }

        var scene = Scene.Standard();
        scene.Map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm2.map"), 2, 0);
        var runtime = CreateRuntime(scene.Map);
        var state = scene.State with
        {
            GameMoment = gameMoment, HasNativeControlMode = true, NativeCameraMode = 3,
            NativeFrameMultiplier = 1, CameraFront = 2048
        };
        Check(WorldMapBroncoLanding.TryFindSurface(scene.Map, state.X, state.Z, out var floor),
            "the native submarine frame has a floor");
        state = state with { TerrainId = floor.TerrainId, TerrainScriptId = floor.TerrainScriptId, RegionId = floor.RegionId & 31 };
        return new(scene, runtime.Catalog, runtime, state);
    }

    /// <summary>What both hosts do each scan: a checked native entity scan, then the runtime's own checked capture.</summary>
    private static void ObserveNativeFrame(InstalledEmeraldScene installed)
    {
        var native = new WorldMapEntityReader(installed.Scene.Memory).Read();
        Check(native.IsUsable, "native entity frame is usable: " + native.Diagnostic);
        installed.Runtime.UpdateEntities(installed.State, native.Entities, installed.Scene.Memory);
    }

    private static void AnEmeraldInFrontOfTheCameraAboveOpenSeabedIsVisible()
    {
        var scene = Scene.Standard();
        var result = scene.Read();
        Check(result.IsUsable, "a coherent frame is usable: " + result.Diagnostic);
        Check(result.VisibleEntities.Contains(EmeraldPointer), "Emerald drawn ahead over open water is visible: " + result.Diagnostic);
        Check(!result.VisibleEntities.Contains(PlayerPointer), "the player is never reported");
    }

    private static void RuntimeRetainsVisibilityRejectionReasonsAndResetsThem()
    {
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(source)) return;
        var scene = Scene.Standard();
        var catalog = WorldMapTargetCatalog.Load(scene.Map,
            Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var runtime = new WorldMapRuntimeContext(scene.Map, catalog, null, 256, TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(300), 1, 10, TimeSpan.FromSeconds(2));
        string Diagnostic() => runtime.LastUnderwaterVisibilityDiagnostic;
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        Check(Diagnostic()?.Contains("visible=", StringComparison.Ordinal) == true,
            "the runtime exposes the coherent native visibility observation for its host log");
        scene.Memory.PutByte(EmeraldPointer + 0x51, 0x0A);
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        Check(Diagnostic()?.Contains("30:not drawn", StringComparison.Ordinal) == true &&
            !runtime.Entities.Single(e => e.ModelId == 30).IsVisibleUnderwater,
            "a hidden Emerald retains the renderer rejection reason without granting visibility");
        runtime.UpdateEntities(scene.State, scene.Entities, null);
        Check(Diagnostic()?.Contains("memory unavailable", StringComparison.Ordinal) == true &&
            !runtime.Entities.Single(e => e.ModelId == 30).IsVisibleUnderwater,
            "unavailable memory clears visibility and reports the observation failure");
        runtime.Reset();
        Check(Diagnostic() == string.Empty, "reset cannot leave the previous map's visibility diagnostic active");
    }

    private static void AllThreeCameraTranslationComponentsUseThePackedNativeLayout()
    {
        var scene = Scene.Standard();
        // Native MATRIX is 30 bytes, packed: nine shorts immediately followed by three
        // ints. Literal addresses come from FUN_0074D50E / Steam FUN_7ff7029e1950,
        // independently of the reader's constants. No four-byte alignment padding.
        scene.Memory.PutInt(0x00DE6A32, 384);
        scene.Memory.PutInt(0x00DE6A36, -256);
        scene.Memory.PutInt(0x00DE6A3A, CameraDistance);
        scene.Memory.PutFloat(Scene.Record(7) + 0x48, 384);
        scene.Memory.PutFloat(Scene.Record(7) + 0x4C, -1506);
        Check(scene.Read().VisibleEntities.Contains(EmeraldPointer),
            "the drawn Emerald matches all three packed translation components: " + scene.Read().Diagnostic);
    }

    private static void TheNativeCameraFrameMakesEmeraldSelectableAndTrackable()
    {
        if (LoadInstalledScene("installed Emerald visibility/navigation checks", 1620) is not { } installed) return;
        var (scene, catalog, runtime, state) = installed;
        void ObserveFrame() => ObserveNativeFrame(installed);
        ObserveFrame();
        Check(runtime.ObserveUnderwaterSightings(state) == "Emerald Weapon is visible nearby. Select it in Events.",
            "a positively drawn Emerald is announced through the runtime: " + scene.Read().Diagnostic);
        var target = catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities)
            .Single(t => t.Label == "Emerald Weapon");
        Check(target.X == RefX && target.Z == RefZ + 4000 && target.NativeUnderwaterArrival?.ModelId == 30,
            "Events offers the observed Emerald position with its native approach");
        for (var i = 0; i < 3; i++) runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state);
        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Check(runtime.Navigation.BeaconEnabled && runtime.Navigation.Probe.TargetId == target.StableId,
            "the visible Emerald can be selected in Events: " + runtime.Navigation.LastDiagnostic);
        Check(runtime.Navigation.TryResolveAutomaticInput(state, out var direction) && direction == FieldNavigationInput.Up,
            "explicit Emerald selection permits approach depth");

        // A new coherent draw follows Emerald's patrol, without manufacturing visibility.
        var olderScan = new WorldMapEntityReader(scene.Memory).Read();
        scene.MoveEmeraldBy(400);
        runtime.UpdateEntities(state, olderScan.Entities, scene.Memory);
        runtime.Navigation.Observe(state);
        Check(runtime.Navigation.Probe.Route!.Waypoints[^1].X == RefX + 400,
            "tracking follows the fresh visible patrol position");

        // Render gaps decide only what may be called visible: the row, the route and the
        // pursuit input all stay on the live node, and nothing is said about the gaps.
        var clock = new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);
        for (var frame = 0; frame < 5; frame++)
        {
            var drawn = frame % 2 == 1;
            scene.Memory.PutInt(Scene.Record(7) + 0x24, drawn ? 1 : 0);
            ObserveFrame();
            var row = catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities)
                .SingleOrDefault(t => t.Label == "Emerald Weapon");
            Check(row?.X == RefX + 400 && runtime.Entities.Single(e => e.ModelId == 30).IsVisibleUnderwater == drawn,
                "a render gap keeps the live row; only visibility follows the renderer");
            var gap = runtime.Navigation.Observe(state, clock.AddMilliseconds(frame * 150), automaticWalkActive: true);
            Check(gap?.StopAutoWalk != true && gap?.Speech is null && runtime.Navigation.BeaconEnabled &&
                runtime.Navigation.TryResolveAutomaticInput(state, out direction) && direction == FieldNavigationInput.Up,
                "render gaps neither pause, end nor narrate the pursuit: " + gap?.Speech);
        }

        // 500 ms without a positive frame re-arms the sighting cue; a resighting during the
        // pursuit says it is still the navigation target.
        scene.Memory.PutInt(Scene.Record(7) + 0x24, 0);
        ObserveFrame();
        runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddMilliseconds(400));
        runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddMilliseconds(900));
        scene.Memory.PutInt(Scene.Record(7) + 0x24, 1);
        ObserveFrame();
        Check(runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddMilliseconds(1000)) ==
            "Emerald Weapon is visible nearby. It remains your navigation target.",
            "resighting during an active Emerald route does not ask the player to select it again");

        // The native hidden bit gates only the renderer (FUN_0076328F); wm2.ev never hides model 30.
        scene.Memory.PutByte(EmeraldPointer + 0x51, 0x0A);
        ObserveFrame();
        Check(!runtime.Entities.Single(e => e.ModelId == 30).IsVisibleUnderwater &&
            catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities).Any(t => t.Label == "Emerald Weapon") &&
            runtime.Navigation.TryResolveAutomaticInput(state, out direction) && direction == FieldNavigationInput.Up,
            "a natively hidden Emerald is never visible but its live node stays tracked");
        Check(runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddMilliseconds(1100)) is null,
            "a natively hidden Emerald is never announced");

        // Ordinary journeys still rise to the safe travel depth, drawn or not.
        scene.Memory.PutByte(EmeraldPointer + 0x51, 0x02);
        ObserveFrame();
        var gelnika = catalog.Locations.Single(t => t.Label == "Sunken Gelnika");
        var ordinary = new WorldMapNavigationController(scene.Map, runtime.Planner, (_, _) => [gelnika],
            entityProvider: () => runtime.Entities);
        var deep = state with { Y = -4250 };
        ordinary.HandleAction(FieldNavigationAction.ToggleBeacon, deep);
        Check(ordinary.TryResolveAutomaticInput(deep, out direction) && direction == FieldNavigationInput.Down,
            "an ordinary destination keeps Emerald avoidance and rises before travel");
        scene.HideEmeraldFromTheRenderer();
        ObserveFrame();
        Check(ordinary.TryResolveAutomaticInput(deep, out direction) && direction == FieldNavigationInput.Down &&
            !ordinary.AutomaticInputHoldsSubmarineThrust,
            "an undrawn live Emerald changes nothing about ordinary depth recovery");
        Console.WriteLine("installed Emerald native-camera selection, patrol tracking, render gaps, resighting and avoidance checks passed.");
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
            (0x00DE6A32, "packed camera translation X"),
            (0x00DE6A3A, "packed camera translation Z"),
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

    private static void AStaleCallerPositionIsReplacedByTheCheckedDrawPosition()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT"))) return;
        var scene = Scene.Standard();
        scene.MakeCallerPositionStale(byX: 100);
        var runtime = CreateRuntime(scene.Map);
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        var observed = runtime.Entities.Single(e => e.ModelId == 30);
        Check(observed.IsVisibleUnderwater && observed.X == RefX,
            "a checked native draw replaces the stale caller position before publishing visibility: " +
            runtime.LastUnderwaterVisibilityDiagnostic);
    }

    private static void MovingEmeraldUsesOneCheckedFrameForVisibilityAndNavigation()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT"))) return;
        var scene = Scene.Standard();
        var runtime = CreateRuntime(scene.Map);
        var prior = scene.Entities.ToArray();
        foreach (var movement in new[] { 60, 120, -60 })
        {
            scene.MoveEmeraldBy(movement);
            runtime.UpdateEntities(scene.State, prior, scene.Memory);
            var observed = runtime.Entities.Single(e => e.ModelId == 30);
            var live = scene.Entities.Single(e => e.ModelId == 30);
            Check(observed.IsVisibleUnderwater && observed.X == live.X && observed.Y == live.Y && observed.Z == live.Z,
                "Emerald's patrol between the entity scan and draw scan remains visible at the fresh position: " +
                runtime.LastUnderwaterVisibilityDiagnostic);
            var target = runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, scene.State, runtime.Entities)
                .SingleOrDefault(t => t.Label == "Emerald Weapon");
            Check(target is not null && target.X == live.X && target.Z == live.Z,
                "Events selection uses the same native coordinates that passed the visibility checks");
            prior = scene.Entities.ToArray();
        }
        scene.Memory.PutByte(EmeraldPointer + 0x51, 0x0A);
        scene.MoveEmeraldBy(90);
        runtime.UpdateEntities(scene.State, prior, scene.Memory);
        var hidden = runtime.Entities.Single(e => e.ModelId == 30);
        var tracked = runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, scene.State, runtime.Entities)
            .SingleOrDefault(t => t.Label == "Emerald Weapon");
        Check(!hidden.IsVisibleUnderwater && hidden.X == RefX + 210 && tracked?.X == RefX + 210,
            "the native hidden flag ends visibility, not the same checked frame's live position");
    }

    private static WorldMapRuntimeContext CreateRuntime(WorldMapData map)
    {
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT")!;
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return new(map, catalog,
            null, 256, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(300), 1, 10, TimeSpan.FromSeconds(2));
    }

    private static void EmeraldOverUntraversableSeabedHasANativeApproachWhetherOrNotDrawn()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT"))) return;
        var map = WorldMapDataLoader.Load(Path.Combine(root, "data", "wm", "wm2.map"), 2, 0);
        var runtime = CreateRuntime(map);
        // Native-map witness near the October 21 log: Emerald can float above
        // terrain 0 while the submarine must approach on nearby terrain 3.
        var state = new WorldMapStateSnapshot(3, 2, 0, 1370, 158182, -4250, 150565,
            1745, 303, 3, 0, 13, 0, 0, new(-109))
            { HasNativeControlMode = true, NativeCameraMode = 3, NativeFrameMultiplier = 1 };
        var emerald = new WorldMapEntitySnapshot(EmeraldPointer, 0, false, 167139, -4250, 151447, 0, 0, 30, 0x02)
            { IsVisibleUnderwater = true };
        Check(WorldMapBroncoLanding.TryFindSurface(map, emerald.X, emerald.Z, out var beneath) && beneath.TerrainId == 0,
            "the witness really floats above a non-submarine floor");
        foreach (var moment in new[] { 1370, 1396, 1620 })
        {
            state = state with { GameMoment = moment };
            runtime.UpdateEntities([emerald]);
            var target = runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities)
                .Single(t => t.Label == "Emerald Weapon");
            Check(runtime.Planner.TryBuildRoute(state, target, out var route),
                "visible Emerald has an approach before and after disc 3: " + runtime.Planner.LastDiagnostic);
            var end = route.Waypoints[^1];
            Check(runtime.Planner.HasSubmarineFootprint(end.X, end.Z), "the approach keeps the native submarine footprint");
            Check(target.HasArrived(state with { X = end.X, Y = end.Y, Z = end.Z }, route.TargetTriangleId),
                "the approach meets Emerald's native XYZ distance test");
            Check(target.X == emerald.X && target.Z == emerald.Z, "the target still reports the observed creature");
            foreach (var point in target.UnderwaterApproachPoints.Values)
            {
                var away = Math.Sign(WorldMapTargetCatalog.WrappedDelta(emerald.X, point.X, map.WrapWidth));
                if (away == 0) away = 1;
                Check(target.HasArrived(state with { X = point.X + away * 23, Y = point.Y + 120, Z = point.Z }, -1),
                    "an approach still reaches the native volume at the steering depth/terminal tolerances");
            }
        }
        for (var i = 0; i < 3; i++) runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, state);
        runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Check(runtime.Navigation.BeaconEnabled, "the nearby-water approach starts through the real controller");
        var committed = runtime.Navigation.Probe.Route;
        var clock = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++)
        {
            runtime.UpdateEntities([emerald]);
            runtime.Navigation.Observe(state, clock.AddMilliseconds(i * 300));
            Check(ReferenceEquals(committed, runtime.Navigation.Probe.Route),
                "an offset contact approach is not mistaken for a moving creature on every scan");
        }
        var moved = emerald with { X = emerald.X + 128 };
        runtime.UpdateEntities([moved]);
        runtime.Navigation.Observe(state, clock.AddSeconds(2));
        var refreshed = runtime.Navigation.Probe.Route;
        Check(runtime.Navigation.BeaconEnabled && refreshed is not null, "the observed patrol keeps a usable route");
        var latest = runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities)
            .Single(t => t.Label == "Emerald Weapon");
        var pointAfterDrift = refreshed!.Waypoints[^1];
        Check(latest.HasArrived(state with { X = pointAfterDrift.X - 23, Y = pointAfterDrift.Y + 120, Z = pointAfterDrift.Z }, -1),
            "patrol movement smaller than the replan threshold cannot invalidate native arrival margin");
        runtime.UpdateEntities([moved with { IsVisibleUnderwater = false }]);
        var undrawn = runtime.Catalog.ReadTargets(WorldMapNavigationCategory.Events, state, runtime.Entities)
            .SingleOrDefault(t => t.Label == "Emerald Weapon");
        Check(undrawn is not null && undrawn.X == moved.X && undrawn.Z == moved.Z &&
            undrawn.UnderwaterApproachPoints.Count == latest.UnderwaterApproachPoints.Count,
            "an undrawn live Emerald keeps the same native approach at its live position");
        var held = runtime.Navigation.Observe(state, clock.AddSeconds(3));
        Check(held?.StopAutoWalk != true && runtime.Navigation.BeaconEnabled &&
            ReferenceEquals(refreshed, runtime.Navigation.Probe.Route),
            "losing sight of an unmoved Emerald changes nothing about its pursuit: " + held?.Speech);
        Console.WriteLine("PASS Emerald native terrain-0 approaches at early and later progress, drawn or not.");
    }

    private static void UnderwaterEventsAnnounceTheirPositionWhenEmeraldJoinsTheList()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT"))) return;
        var scene = Scene.Standard();
        var runtime = CreateRuntime(scene.Map);
        WorldMapNavigationTarget Row(string name) => new(WorldMapNavigationCategory.Events, WorldMapTargetKind.Event,
            name, RefX, BaseY, RefZ, 0, 0, name, new HashSet<int> { 0 });
        IReadOnlyList<WorldMapNavigationTarget> rows = [Row("Red submarine wreck"), Row("Sunken Gelnika")];
        var navigation = new WorldMapNavigationController(scene.Map, runtime.Planner,
            (_, category) => category == WorldMapNavigationCategory.Events ? rows : []);
        for (var i = 0; i < 3; i++) navigation.HandleAction(FieldNavigationAction.NextCategory, scene.State);
        navigation.HandleAction(FieldNavigationAction.NextTarget, scene.State);
        navigation.HandleAction(FieldNavigationAction.PreviousCategory, scene.State);
        rows = [Row("Emerald Weapon"), .. rows];
        runtime.UpdateEntities(scene.State, scene.Entities, scene.Memory);
        var speech = navigation.HandleAction(FieldNavigationAction.NextCategory, scene.State)?.Speech ?? "";
        Check(speech.Contains("Sunken Gelnika", StringComparison.Ordinal) && speech.Contains("3 of 3", StringComparison.Ordinal),
            "a retained event cursor announces that a newly visible creature is another selectable row: " + speech);
        Check(runtime.ObserveUnderwaterSightings(scene.State)?.Contains("Events", StringComparison.Ordinal) == true,
            "the sighting says which category contains Emerald");
    }

    private static void UnderwaterEventsDoNotSubstituteTheAnnouncedTarget()
    {
        var scene = Scene.Standard();
        var planner = new WorldMapRoutePlanner(scene.Map);
        Check(planner.TryResolvePlayerTriangle(scene.State, out var triangle), "selection test has native floor");
        var center = scene.Map.Triangles[triangle].Centroid;
        var state = scene.State with { X = center.X, Z = center.Z, HasNativeControlMode = true,
            NativeCameraMode = 3, NativeFrameMultiplier = 1 };
        WorldMapNavigationTarget Row(string label) => new(WorldMapNavigationCategory.Events, WorldMapTargetKind.Event,
            label, center.X + 500, state.Y, center.Z, triangle, 0, label, new HashSet<int> { triangle })
            { NativeUnderwaterArrival = new(scene.Map, 17, center.X + 500, state.Y, center.Z, 16, new HashSet<int> { triangle }) };
        var emerald = Row("Emerald Weapon") with { StableId = $"world-entity:{EmeraldPointer:X8}:30" };
        var wreck = Row("Red submarine wreck");
        var ship = Row("Sunken Gelnika");
        IReadOnlyList<WorldMapNavigationTarget> rows = [wreck, ship];
        WorldMapNavigationController Controller()
        {
            var controller = new WorldMapNavigationController(scene.Map, planner,
                (_, category) => category == WorldMapNavigationCategory.Events ? rows : []);
            for (var i = 0; i < 3; i++) controller.HandleAction(FieldNavigationAction.NextCategory, state);
            return controller;
        }
        var navigation = Controller();
        navigation.HandleAction(FieldNavigationAction.NextTarget, state); // Heard Sunken Gelnika.
        rows = [emerald, wreck, ship];
        navigation.FocusSightedEmerald(EmeraldPointer);
        var started = navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state)?.Speech ?? "";
        Check(navigation.BeaconEnabled && navigation.Probe.TargetId == "Sunken Gelnika",
            "an inserted Emerald cannot change which announced destination Pursue starts");
        Check(started.StartsWith("Navigation on.", StringComparison.Ordinal) && started.Contains("Sunken Gelnika", StringComparison.Ordinal),
            "the underwater start confirmation names the selected destination: " + started);
        var deep = state with { Y = -4965 };
        var repeated = navigation.HandleAction(FieldNavigationAction.RepeatTarget, deep)?.Speech ?? "";
        Check(repeated.Contains("Sunken Gelnika", StringComparison.Ordinal),
            "depth guidance still names the active destination when the menu is reopened: " + repeated);
        var relocked = navigation.HandleAction(FieldNavigationAction.NextTarget, deep)?.Speech ?? "";
        Check(relocked.Contains("Emerald Weapon", StringComparison.Ordinal),
            "browsing an engaged route announces the newly selected destination: " + relocked);

        navigation = Controller(); // Heard Emerald.
        rows = [wreck, ship];
        var lost = navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state)?.Speech ?? "";
        Check(!navigation.BeaconEnabled && lost.Contains("Emerald Weapon", StringComparison.Ordinal) &&
            lost.Contains("no longer", StringComparison.Ordinal), "a vanished selection starts nothing and explains why: " + lost);
        var next = navigation.HandleAction(FieldNavigationAction.NextTarget, state)?.Speech ?? "";
        Check(next.Contains("Red submarine wreck", StringComparison.Ordinal), "an explicit next-target action can choose a replacement");
        navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Check(navigation.BeaconEnabled && navigation.Probe.TargetId == "Red submarine wreck", "only that explicit choice starts the replacement");

        rows = [wreck, ship];
        navigation = Controller();
        navigation.HandleAction(FieldNavigationAction.NextTarget, state);
        navigation.HandleAction(FieldNavigationAction.PreviousCategory, state);
        rows = [emerald, wreck, ship];
        navigation.FocusSightedEmerald(EmeraldPointer);
        Check(navigation.CurrentCategory == WorldMapNavigationCategory.Transportation && !navigation.BeaconEnabled,
            "a sighting changes neither category nor movement");
        var focus = navigation.HandleAction(FieldNavigationAction.NextCategory, state)?.Speech ?? "";
        Check(focus.StartsWith("Events, Emerald Weapon, 1 of 3", StringComparison.Ordinal),
            "the next Events preview focuses the positively sighted creature: " + focus);
    }

    private static void PatrolDriftReplansBeforeTheContactVolumeIsMissed()
    {
        var scene = Scene.Standard();
        var planner = new WorldMapRoutePlanner(scene.Map);
        var state = scene.State with { Y = -4250, HasNativeControlMode = true, NativeCameraMode = 3, NativeFrameMultiplier = 1 };
        var aim = new WorldMapRouteWaypoint(RefX, -4250, RefZ - 1000);
        Check(planner.TryResolvePlayerTriangle(state with { X = aim.X, Z = aim.Z }, out var goal), "contact approach has native floor");
        var entity = scene.Entities.Single(e => e.ModelId == 30) with
            { X = aim.X + 1024, Y = aim.Y, Z = aim.Z, IsVisibleUnderwater = true };
        WorldMapNavigationTarget Target() => new(WorldMapNavigationCategory.Events, WorldMapTargetKind.Event,
            "Emerald Weapon", entity.X, entity.Y, entity.Z, goal, 0, "patrol", new HashSet<int> { goal })
            {
                NativeUnderwaterArrival = new(scene.Map, 30, entity.X, entity.Y, entity.Z, 1215, new HashSet<int> { goal }),
                UnderwaterApproachPoints = new Dictionary<int, WorldMapRouteWaypoint> { [goal] = aim }
            };
        var target = Target() with { StableId = $"world-entity:{entity.GuestPointer:X8}:30" };
        var navigation = new WorldMapNavigationController(scene.Map, planner,
            (_, category) => category == WorldMapNavigationCategory.Events ? [target] : [], entityProvider: () => [entity]);
        for (var i = 0; i < 3; i++) navigation.HandleAction(FieldNavigationAction.NextCategory, state);
        navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Check(navigation.BeaconEnabled, "the original native approach starts");
        // 1024 + 120 + 23 is inside 1215. A patrol shift of 128 makes the
        // old endpoint miss it, even though its point drift is not >128.
        entity = entity with { X = entity.X + 128 };
        aim = aim with { X = aim.X + 128 };
        target = Target() with { StableId = target.StableId };
        navigation.Observe(state, new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc));
        var endpoint = navigation.Probe.Route!.Waypoints[^1];
        Check(target.HasArrived(state with { X = endpoint.X - 23, Y = endpoint.Y + 120, Z = endpoint.Z }, -1),
            "the refreshed route ends inside the current contact volume, including steering tolerances");
    }

    private static void RefreshedPositionsCannotReidentifyANodeAsThePlayerOrAnotherModel()
    {
        foreach (var corrupt in new Action<Scene>[]
        {
            scene => scene.Memory.PutByte(EmeraldPointer + 0x50, 18),
            scene => scene.Memory.PutInt(0x00E3A7D0, unchecked((int)EmeraldPointer)),
            scene =>
            {
                scene.Memory.PutInt(GelnikaPointer, 0);
                scene.Memory.PutInt(EmeraldPointer, unchecked((int)GelnikaPointer));
                scene.Memory.PutInt(ReactorPointer, unchecked((int)EmeraldPointer));
            }
        })
        {
            var scene = Scene.Standard();
            corrupt(scene);
            Check(!scene.Read().VisibleEntities.Contains(EmeraldPointer),
                "refreshing movement cannot trust a changed model, player identity or node topology");
        }
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
            m.PutInt(0x00DE6A32, 0);
            m.PutInt(0x00DE6A36, 0);
            m.PutInt(0x00DE6A3A, CameraDistance);

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

        /// <summary>The renderer did not draw Emerald's model this frame.</summary>
        internal void HideEmeraldFromTheRenderer() => Memory.PutInt(Record(7) + 0x24, 0);

        /// <summary>
        /// Emerald's native patrol moves its node while nothing draws it: only the world position
        /// changes, and the render record keeps whatever it held.
        /// </summary>
        internal void PatrolOutOfView(int deltaX)
        {
            var index = Entities.FindIndex(e => e.GuestPointer == EmeraldPointer);
            var moved = Entities[index] with { X = Entities[index].X + deltaX };
            Entities[index] = moved;
            Memory.PutInt(EmeraldPointer + 0x0C, moved.X);
        }

        /// <summary>
        /// A battle, defeat or reload unlinks Emerald's node from the native list (the Gelnika's
        /// node points at it); <see cref="Entities"/> keeps the older scan that still lists it.
        /// </summary>
        internal void RetireEmeraldFromTheNativeList() => Memory.PutInt(GelnikaPointer, 0);

        /// <summary>The native pool rebuilt Emerald's node at another address, where the old one was.</summary>
        internal void ReplaceEmeraldNode(uint pointer)
        {
            var index = Entities.FindIndex(e => e.GuestPointer == EmeraldPointer);
            var emerald = Entities[index];
            Memory.PutInt(pointer, 0);
            Memory.PutInt(pointer + 0x0C, emerald.X);
            Memory.PutInt(pointer + 0x10, emerald.Y);
            Memory.PutInt(pointer + 0x14, emerald.Z);
            Memory.PutShort(pointer + 0x44, 0);
            Memory.PutShort(pointer + 0x4A, 3);
            Memory.PutByte(pointer + 0x50, (byte)emerald.ModelId);
            Memory.PutByte(pointer + 0x51, emerald.Flags);
            Memory.PutInt(GelnikaPointer, unchecked((int)pointer));
            Entities[index] = emerald with { GuestPointer = pointer };
            var gelnika = Entities.FindIndex(e => e.GuestPointer == GelnikaPointer);
            Entities[gelnika] = Entities[gelnika] with { NextGuestPointer = pointer };
        }

        private void AddEntity(uint pointer, int model, byte flags, int x, int y, int z)
        {
            var previousHead = Entities.Count == 0 ? 0u : Entities[^1].GuestPointer;
            Memory.PutInt(pointer, unchecked((int)previousHead));
            Memory.PutInt(pointer + 0x0C, x);
            Memory.PutInt(pointer + 0x10, y);
            Memory.PutInt(pointer + 0x14, z);
            Memory.PutShort(pointer + 0x44, 0);
            Memory.PutShort(pointer + 0x4A, 3);
            Memory.PutByte(pointer + 0x50, (byte)model);
            Memory.PutByte(pointer + 0x51, flags);
            Memory.PutInt(0x00E39A00, unchecked((int)pointer));
            Entities.Add(new WorldMapEntitySnapshot(pointer, previousHead, pointer == PlayerPointer, x, y, z, 3, 0, model, flags));
        }

        internal WorldMapStateSnapshot State =>
            new(3, 2, 0, 0, RefX, BaseY, ReferenceZ, 0, 0, 3, 0, 13, 0, 0, default);

        internal void MoveEmeraldBy(int deltaX)
        {
            var index = Entities.FindIndex(e => e.GuestPointer == EmeraldPointer);
            var moved = Entities[index] with { X = Entities[index].X + deltaX };
            Entities[index] = moved;
            Memory.PutInt(EmeraldPointer + 0x0C, moved.X);
            Memory.PutFloat(Record(7) + 0x48, moved.X - RefX);
        }

        internal WorldMapUnderwaterVisibilityReadResult Read(int stateMapType = 2)
        {
            var reader = new WorldMapUnderwaterVisibilityReader(Memory, Map);
            var state = State with { WorldMapType = stateMapType };
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
