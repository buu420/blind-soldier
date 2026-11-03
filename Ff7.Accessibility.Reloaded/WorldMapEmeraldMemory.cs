namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Remembers only the renderer's positive observations. Native existence keeps a menu row;
/// an off-screen entity's coordinates never become a destination or refresh this memory.
/// Nothing is persisted across an unavailable/rebuilt world or another save.
/// </summary>
internal sealed class WorldMapEmeraldMemory
{
    private sealed record Observation(uint Pointer, WorldMapEntitySnapshot? LastSeen);
    private Observation observation = new(0, null);

    internal void Observe(IReadOnlyList<WorldMapEntitySnapshot> entities)
    {
        var emeralds = entities.Where(e => !e.IsPlayer && e.ModelId == 30).Take(2).ToArray();
        var prior = Volatile.Read(ref observation);
        var next = emeralds.Length == 1 && emeralds[0].GuestPointer != 0
            ? new Observation(emeralds[0].GuestPointer,
                emeralds[0].IsVisibleUnderwater ? emeralds[0] :
                prior.Pointer == emeralds[0].GuestPointer ? prior.LastSeen : null)
            : new Observation(0, null);
        Volatile.Write(ref observation, next);
    }

    internal WorldMapNavigationTarget? ReadTarget(WorldMapStateSnapshot state, WorldMapTargetCatalog catalog,
        IReadOnlyList<WorldMapEntitySnapshot> entities, IReadOnlyList<WorldMapNavigationTarget> current)
    {
        if (state.WorldMapType != 2) return null;
        var emeralds = entities.Where(e => !e.IsPlayer && e.ModelId == 30).Take(2).ToArray();
        if (emeralds.Length != 1 || emeralds[0].GuestPointer == 0) return null;
        var id = $"world-entity:{emeralds[0].GuestPointer:X8}:30";
        if (current.FirstOrDefault(t => t.StableId == id) is { } visible)
            return visible with { NativeEntityModelId = 30 };

        var remembered = Volatile.Read(ref observation);
        if (remembered.Pointer == emeralds[0].GuestPointer && remembered.LastSeen is { } seen &&
            catalog.ReadTargets(WorldMapNavigationCategory.Events, state, [seen])
                .FirstOrDefault(t => t.StableId == id) is { } lastSeen)
            return lastSeen with { NativeEntityModelId = 30, LocationKnowledge = WorldMapTargetLocationKnowledge.LastSeen };

        // Explicit invalid sentinels, never an invented world origin. Every arrival and
        // planning entry point rejects Unknown before interpreting these coordinates.
        return new(WorldMapNavigationCategory.Events, WorldMapTargetKind.Event, "Emerald Weapon",
            int.MinValue, int.MinValue, int.MinValue, -1, -1, id, new HashSet<int>())
        { NativeEntityModelId = 30, LocationKnowledge = WorldMapTargetLocationKnowledge.Unknown };
    }
}
