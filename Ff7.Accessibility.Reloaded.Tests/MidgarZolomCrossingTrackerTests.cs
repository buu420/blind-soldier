using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MidgarZolomCrossingTrackerTests
{
    internal static void Run()
    {
        AnnouncesTheFarmSideFarCornerOnItsRisingEdge();
        UsesTheOppositeFarCornerFromTheMineSide();
        RequiresTheVisibleShorelineAndAnOnFootPlayer();
        ResolvesTheMarshBoundaryFromTheNativeWorldMesh();
    }

    private static void AnnouncesTheFarmSideFarCornerOnItsRisingEdge()
    {
        var tracker = new MidgarZolomCrossingTracker();
        var player = Player(x: 230_723, z: 142_596);
        var safe = Zolom(MidgarZolomCrossingTracker.FarmApproachFarX,
                          MidgarZolomCrossingTracker.FarmApproachFarZ);

        Equal(true, tracker.Observe(player, safe, isAtMarshShore: true), "first visible far-side window");
        Equal(false, tracker.Observe(player, safe, isAtMarshShore: true), "stable far-side window does not repeat");

        Equal(false, tracker.Observe(player, Zolom(230_000, 145_000), true), "Zolom leaves the far-side window");
        Equal(true, tracker.Observe(player, safe, true), "a later visible far-side window rearms");
        Equal(
            "Midgar Zolom is at the far side. Run now.",
            MidgarZolomCrossingTracker.CueText,
            "crossing cue text");
    }

    private static void UsesTheOppositeFarCornerFromTheMineSide()
    {
        var tracker = new MidgarZolomCrossingTracker();
        var mineSidePlayer = Player(x: 216_000, z: 153_000);

        Equal(
            false,
            tracker.Observe(
                mineSidePlayer,
                Zolom(MidgarZolomCrossingTracker.FarmApproachFarX,
                       MidgarZolomCrossingTracker.FarmApproachFarZ),
                true),
            "farm-side anchor is not the mine-side opportunity");
        Equal(
            true,
            tracker.Observe(
                mineSidePlayer,
                Zolom(MidgarZolomCrossingTracker.MineApproachFarX,
                       MidgarZolomCrossingTracker.MineApproachFarZ),
                true),
            "mine-side far corner is announced");
    }

    private static void RequiresTheVisibleShorelineAndAnOnFootPlayer()
    {
        var safe = Zolom(MidgarZolomCrossingTracker.FarmApproachFarX,
                          MidgarZolomCrossingTracker.FarmApproachFarZ);

        Equal(
            false,
            new MidgarZolomCrossingTracker().Observe(
                Player(230_723, 142_596), safe, isAtMarshShore: false),
            "deep-marsh and distant observations stay silent");
        Equal(
            false,
            new MidgarZolomCrossingTracker().Observe(
                Player(230_723, 142_596, model: 4), safe, isAtMarshShore: true),
            "caught Chocobo needs no run-now cue");
        Equal(
            false,
            new MidgarZolomCrossingTracker().Observe(
                Player(230_723, 142_596),
                MidgarZolomStateReadResult.Invalid(default, "torn"),
                true),
            "unreadable native state fails silent");
    }

    private static void ResolvesTheMarshBoundaryFromTheNativeWorldMesh()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var map = WorldMapDataLoader.Load(
            Path.Combine(dataRoot, "data", "wm", "WM0.MAP"),
            worldMapType: 0,
            worldProgress: 0);
        var planner = new WorldMapRoutePlanner(map);
        var marshShore = map.Triangles.First(triangle =>
            triangle.TerrainId == 7 &&
            triangle.Neighbors.Any(id => map.Triangles[id].TerrainId != 7));
        var landShore = map.Triangles[marshShore.Neighbors.First(id => map.Triangles[id].TerrainId != 7)];
        var marshInterior = map.Triangles.First(triangle =>
            triangle.TerrainId == 7 &&
            triangle.Neighbors.Count > 0 &&
            triangle.Neighbors.All(id => map.Triangles[id].TerrainId == 7));

        Equal(true, WorldMapTerrainProximity.IsAtBoundary(map, planner, PlayerOn(marshShore), 7),
            "marsh-side shoreline triangle");
        Equal(true, WorldMapTerrainProximity.IsAtBoundary(map, planner, PlayerOn(landShore), 7),
            "land-side shoreline triangle");
        Equal(false, WorldMapTerrainProximity.IsAtBoundary(map, planner, PlayerOn(marshInterior), 7),
            "interior marsh triangle is not the crossing shoreline");
    }

    private static WorldMapStateSnapshot Player(int x, int z, int model = 0) =>
        new(
            WorldMapStateReader.WorldModule,
            0,
            0,
            385,
            x,
            0,
            z,
            0,
            0,
            0,
            1,
            model,
            30,
            0,
            new FieldNavigationControlTransform(0));

    private static WorldMapStateSnapshot PlayerOn(WorldMapTriangle triangle)
    {
        var state = Player(triangle.Centroid.X, triangle.Centroid.Z);
        return state with
        {
            Y = triangle.Centroid.Y,
            TerrainId = triangle.TerrainId,
            RegionId = triangle.RegionId & 0x1F
        };
    }

    private static MidgarZolomStateReadResult Zolom(int x, int z) =>
        MidgarZolomStateReadResult.Valid(
            new MidgarZolomStateSnapshot(true, x, z, 0),
            $"position={x},{z}");

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}
