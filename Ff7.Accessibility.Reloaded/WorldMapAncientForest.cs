namespace Ff7.Accessibility.Reloaded;

public sealed partial class WorldMapTargetCatalog
{
    private const string AncientForestId = "world-location:55:Ancient Forest";
    private WorldMapNavigationTarget? liveAncientForest;

    /// <summary>
    /// wm0.ev model25 fn0 places (11,21)+(2848,7699); fn3 (5903) enters location55,
    /// field622, on Confirm for Cloud/Tifa/Cid on foot. This is model collision,
    /// not a terrain-script entrance. Native stage4 terrain opens the basin.
    /// </summary>
    private static WorldMapNavigationTarget? CreateAncientForestTarget(WorldMapData map,
        int x = 92960, int z = 179731, uint pointer = 0)
    {
        if (map.WorldMapType != 0) return null;
        var contacts = WorldMapVehicleShoreApproach.FindContactPoints(map, 0, 25, x, z);
        if (contacts.Count == 0) return null;
        var floor = WorldMapBroncoLanding.TryFindSurface(map, x, z, out var center)
            ? center : map.Triangles[contacts.Keys.Min()];
        return new(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
            "Ancient Forest", x, floor.Centroid.Y, z, floor.Id, floor.RegionId & 31,
            AncientForestId, contacts.Keys.ToHashSet())
        {
            RequiresOnFootEntry = true,
            NativeEntityModelId = 25,
            NativeVehicleContact = new(pointer, 25, x, z, map.WrapWidth, map.WrapHeight),
            VehicleContactPoints = contacts
        };
    }

    private IReadOnlyList<WorldMapNavigationTarget> ReadAncientForestLocations(
        IReadOnlyList<WorldMapEntitySnapshot> entities)
    {
        if (entities.Count == 0 || !Locations.Any(t => t.StableId == AncientForestId)) return Locations;
        var models = entities.Where(e => !e.IsPlayer && e.ModelId == 25).Take(2).ToArray();
        WorldMapNavigationTarget? forest = null;
        if (models.Length == 1 && (models[0].Flags & 0x88) == 0)
        {
            var model = models[0];
            if (liveAncientForest?.NativeVehicleContact is not { } contact ||
                contact.VehicleEntityPointer != model.GuestPointer || contact.VehicleX != model.X || contact.VehicleZ != model.Z)
                liveAncientForest = CreateAncientForestTarget(map, model.X, model.Z, model.GuestPointer);
            forest = liveAncientForest;
        }
        // A coherent pool that actually omits or hides the entrance overrides its static
        // script placement. An unavailable pool is handled by both hosts' input gate.
        return Locations.Select(t => t.StableId == AncientForestId ? forest : t)
            .OfType<WorldMapNavigationTarget>().ToArray();
    }
}
