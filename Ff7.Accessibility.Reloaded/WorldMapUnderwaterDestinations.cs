namespace Ff7.Accessibility.Reloaded;

/// <summary>A point inside visible lake water with space for the surfaced sub's footprint.</summary>
internal sealed record WorldMapSubmarineSurfacingPoint(int X, int Z)
{
    internal bool IsSatisfiedBy(WorldMapStateSnapshot state) => state.WorldMapType == 2 &&
        state.PlayerModelId == 13 && state.TerrainId == 3 &&
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.X, X, 0x48000)) +
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.Z, Z, 0x38000)) <= 128;
}

/// <summary>Fixed script entrances that a deliberate deep Emerald approach must avoid.</summary>
internal static class WorldMapUnderwaterEntryGuard
{
    internal static readonly WorldMapRouteWaypoint Gelnika = new(132894, -4365, 154192);
    internal static readonly WorldMapRouteWaypoint RedWreck = new(143655, -4970, 184718);

    internal static bool TryFindBlockedSite(WorldMapStateSnapshot state, WorldMapRouteWaypoint end,
        bool redWreckPresent, out string site)
    {
        site = string.Empty;
        if (state.PlayerModelId != 13 || state.WorldMapType != 2) return false;
        if (Touches(Gelnika)) { site = "Sunken Gelnika"; return true; }
        if (redWreckPresent && Touches(RedWreck)) { site = "Red submarine wreck"; return true; }
        return false;

        bool Touches(WorldMapRouteWaypoint point)
        {
            // Horizontal Manhattan distance bounds the native three-axis condition at
            // every depth, including floor correction. Its minimum on a segment occurs
            // at an endpoint or where one of the two axis deltas crosses zero.
            var x = (double)WorldMapTargetCatalog.WrappedDelta(point.X, state.X, 0x48000);
            var z = (double)WorldMapTargetCatalog.WrappedDelta(point.Z, state.Z, 0x38000);
            var dx = (double)WorldMapTargetCatalog.WrappedDelta(state.X, end.X, 0x48000);
            var dz = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, end.Z, 0x38000);
            var minimum = Math.Min(Distance(0), Distance(1));
            if (dx != 0) minimum = Math.Min(minimum, Distance(Math.Clamp(-x / dx, 0, 1)));
            if (dz != 0) minimum = Math.Min(minimum, Distance(Math.Clamp(-z / dz, 0, 1)));
            return minimum <= 975;

            double Distance(double t) => Math.Abs(x + t * dx) + Math.Abs(z + t * dz);
        }
    }
}

/// <summary>wm2.ev's actual model proximity or seabed-clearance condition.</summary>
internal sealed record WorldMapUnderwaterArrival(WorldMapData Map, int ModelId, int X, int Y, int Z,
    int ManhattanBound, IReadOnlySet<int> TriggerTriangles)
{
    internal bool IsSatisfiedBy(WorldMapStateSnapshot state, int triangleId)
    {
        if (state.PlayerModelId != 13 || state.WorldMapType != 2) return false;
        if (ModelId == 26)
        {
            if (!TriggerTriangles.Contains(triangleId) || state.TerrainScriptId != 3) return false;
            var floor = FloorHeight(Map.Triangles[triangleId], state.X, state.Z);
            return double.IsFinite(floor) && state.Y - floor < 500;
        }
        return Math.Abs((long)WorldMapTargetCatalog.WrappedDelta(state.X, X, Map.WrapWidth)) +
            Math.Abs(state.Y - (long)Y) + Math.Abs((long)WorldMapTargetCatalog.WrappedDelta(state.Z, Z, Map.WrapHeight)) <= ManhattanBound;
    }

    internal static double FloorHeight(WorldMapTriangle t, int x, int z)
    {
        var a = t.Vertex0; var b = t.Vertex1; var c = t.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (Math.Abs(denominator) < 1e-8) return double.NaN;
        var wa = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var wb = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        if (wa < -1e-6 || wb < -1e-6 || wa + wb > 1 + 1e-6) return double.NaN;
        return Math.Truncate(wa * a.Y + wb * b.Y + (1 - wa - wb) * c.Y);
    }
}

public sealed partial class WorldMapTargetCatalog
{
    private static IReadOnlyList<WorldMapNavigationTarget> BuildUnderwaterLocations(WorldMapData map)
    {
        var resolver = new WorldMapRoutePlanner(map);
        // wm2.ev native mesh/local points: model17 (16,18)+(1822,6736),
        // model26 (17,11)+(4732,4703), model28 (17,22)+(4391,4494).
        var keyTriangles = map.Triangles.Where(t => t.MeshX == 17 && t.MeshZ == 11 && t.TerrainScriptId == 3)
            .Select(t => t.Id).ToHashSet();
        var ship = WorldMapUnderwaterEntryGuard.Gelnika;
        var wreck = WorldMapUnderwaterEntryGuard.RedWreck;
        var lakeState = new WorldMapStateSnapshot(3, 2, 0, 0, 101908, -3000, 144580,
            0, 0, 3, 0, 13, 0, 0, default);
        if (!resolver.TryResolvePlayerTriangle(lakeState, out var lakeTriangle))
            throw new InvalidDataException($"Lucrecia lake surfacing point is absent: {resolver.LastDiagnostic}");
        var lake = new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
            "Lucrecia's Cave, lake", lakeState.X, lakeState.Y, lakeState.Z, lakeTriangle,
            map.Triangles[lakeTriangle].RegionId & 31, "world-underwater:lucrecia-lake", new HashSet<int> { lakeTriangle })
            { SubmarineSurfacingPoint = new(lakeState.X, lakeState.Z) };
        return [Place("Sunken Gelnika", 17, ship.X, ship.Y, ship.Z, 975, new HashSet<int>()),
                Place("Key of the Ancients", 26, 143996, -4284, 94815, -1, keyTriangles),
                Place("Red submarine wreck", 28, wreck.X, wreck.Y, wreck.Z, 975, new HashSet<int>()), lake];

        WorldMapNavigationTarget Place(string label, int model, int x, int y, int z, int bound, IReadOnlySet<int> triggers)
        {
            var state = new WorldMapStateSnapshot(3, 2, 0, 0, x, y, z, 0, 0, model == 26 ? 15 : 3, 0, 13, 0, 0, default)
                { TerrainScriptId = model == 26 ? 3 : 0 };
            if (!resolver.TryResolvePlayerTriangle(state, out var triangleId))
                throw new InvalidDataException($"Native underwater destination {label} is absent: {resolver.LastDiagnostic}");
            return new(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location, label,
                x, y, z, triangleId, map.Triangles[triangleId].RegionId, $"world-underwater:{model}", new HashSet<int> { triangleId })
            {
                NativeTriggerTriangleIds = triggers,
                NativeUnderwaterArrival = new(map, model, x, y, z, bound, triggers)
            };
        }
    }
}
