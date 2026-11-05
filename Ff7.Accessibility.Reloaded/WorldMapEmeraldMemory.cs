namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Emerald Weapon as one checked native frame presents it. wm2.ev system function 0 allocates
/// a single model-30 node while defeat bit 0x03DC is clear and submarine bit 0x1A83 or 0x1A92
/// is set. C_007641A7 runs every node's script each frame whether or not it is drawn, and
/// Emerald's function 2 (0x5E02) both moves its patrol and tests the submarine's distance for
/// the encounter there, so the live node's position is where the encounter happens. Drawing it
/// is the renderer's business and decides only what may be called visible.
///
/// <para>Only that single node is Emerald: none means there is no live contact, and two cannot
/// be told apart. Nothing is kept between frames, so no sighting or position outlives the frame
/// it was read in, a retired node, another map or another save.</para>
/// </summary>
internal static class WorldMapLiveEmerald
{
    internal const int ModelId = 30;

    internal static bool TryFind(IReadOnlyList<WorldMapEntitySnapshot> frame, out WorldMapEntitySnapshot emerald)
    {
        emerald = default;
        var found = false;
        foreach (var entity in frame)
        {
            if (entity.IsPlayer || entity.ModelId != ModelId) continue;
            if (found)
            {
                emerald = default;
                return false;
            }

            found = true;
            emerald = entity;
        }

        if (found && emerald.GuestPointer != 0) return true;
        emerald = default;
        return false;
    }

    internal static string StableId(uint pointer) => $"world-entity:{pointer:X8}:{ModelId}";
}
