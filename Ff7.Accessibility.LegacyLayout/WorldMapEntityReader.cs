using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the native linked list created by FUN_007610b3 and rooted at
/// DAT_00E39A00.  Ghidra confirms offset zero is next_ptr and the remaining
/// offsets match FFNx's world_event_data definition.
/// </summary>
public sealed class WorldMapEntityReader
{
    public const int AddressEntityListHead = 0x00E39A00;
    private const int MaximumEntities = 64;
    private const int NextPointerOffset = 0x00;
    private const int FlagsOffset = 0x51;

    private readonly ILegacyAddressSpace memory;

    public WorldMapEntityReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public WorldMapEntityReadResult Read()
    {
        if (!TryReadFrame(out var first, out var firstDiagnostic))
        {
            return WorldMapEntityReadResult.Invalid(firstDiagnostic);
        }

        if (!TryReadFrame(out var second, out var secondDiagnostic))
        {
            return WorldMapEntityReadResult.Invalid(secondDiagnostic);
        }

        if (!IsSameList(first, second))
        {
            return WorldMapEntityReadResult.Invalid("world entity list changed during read");
        }

        // The later frame. Both describe the same list, so the fresher positions are the
        // truthful ones and nothing here is ever a position the reader kept from before.
        return WorldMapEntityReadResult.Valid(second, $"native world entities={second.Count}");
    }

    /// <summary>
    /// Whether the two frames are the same list, rather than the same list standing still.
    ///
    /// <para>The guard catches the native list being rebuilt underneath the traversal:
    /// reading half of one list and half of another is how a vehicle gets invented. It
    /// used to compare whole snapshots, which made it a test of whether the world had
    /// moved, and something always is. Every other scan then published nothing and the
    /// parked vehicles disappeared from Transportation.</para>
    ///
    /// <para>So only identity and topology are compared: the chain, which node is the
    /// player, and the model. Position, the walkmap terrain and region an entity is
    /// standing on, and the animation flag at +0x51 are all per-frame state that a live
    /// entity changes on its own, and none of them says the list was rebuilt.</para>
    /// </summary>
    private static bool IsSameList(
        IReadOnlyList<WorldMapEntitySnapshot> first,
        IReadOnlyList<WorldMapEntitySnapshot> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            var before = first[index];
            var after = second[index];
            if (before.GuestPointer != after.GuestPointer ||
                before.NextGuestPointer != after.NextGuestPointer ||
                before.IsPlayer != after.IsPlayer ||
                before.ModelId != after.ModelId)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReadFrame(out IReadOnlyList<WorldMapEntitySnapshot> entities, out string diagnostic)
    {
        entities = Array.Empty<WorldMapEntitySnapshot>();
        diagnostic = "world entity header read failed";
        if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt32((uint)AddressEntityListHead, out var head) ||
            !memory.TryReadUInt32((uint)WorldMapStateReader.AddressWorldPlayerEntityPointer, out var player))
        {
            return false;
        }

        if (module != WorldMapStateReader.WorldModule)
        {
            diagnostic = $"module={module}, not world map";
            return false;
        }

        var snapshots = new List<WorldMapEntitySnapshot>();
        var visited = new HashSet<uint>();
        var current = head;
        while (current != 0)
        {
            if (snapshots.Count >= MaximumEntities)
            {
                diagnostic = $"world entity list exceeds {MaximumEntities} nodes";
                return false;
            }

            if (!visited.Add(current))
            {
                diagnostic = $"world entity list loops at 0x{current:X8}";
                return false;
            }

            if (!TryReadEntity(current, player, out var snapshot))
            {
                diagnostic = $"world entity 0x{current:X8} is unreadable";
                return false;
            }

            snapshots.Add(snapshot);
            current = snapshot.NextGuestPointer;
        }

        if (!memory.TryReadUInt32((uint)AddressEntityListHead, out var endingHead) ||
            !memory.TryReadUInt32((uint)WorldMapStateReader.AddressWorldPlayerEntityPointer, out var endingPlayer) ||
            endingHead != head || endingPlayer != player)
        {
            diagnostic = "world entity header changed during traversal";
            return false;
        }

        entities = snapshots;
        diagnostic = string.Empty;
        return true;
    }

    private bool TryReadEntity(uint address, uint playerPointer, out WorldMapEntitySnapshot snapshot)
    {
        snapshot = default;
        if (!TryAdd(address, NextPointerOffset, out var nextAddress) ||
            !TryAdd(address, WorldMapStateReader.PositionXOffset, out var xAddress) ||
            !TryAdd(address, WorldMapStateReader.PositionYOffset, out var yAddress) ||
            !TryAdd(address, WorldMapStateReader.PositionZOffset, out var zAddress) ||
            !TryAdd(address, WorldMapStateReader.WalkmapTypeOffset, out var walkmapAddress) ||
            !TryAdd(address, WorldMapStateReader.ModelIdOffset, out var modelAddress) ||
            !TryAdd(address, FlagsOffset, out var flagsAddress) ||
            !memory.TryReadUInt32(nextAddress, out var next) ||
            !memory.TryReadInt32(xAddress, out var x) ||
            !memory.TryReadInt32(yAddress, out var y) ||
            !memory.TryReadInt32(zAddress, out var z) ||
            !memory.TryReadUInt16(walkmapAddress, out var walkmap) ||
            !memory.TryReadByte(modelAddress, out var model) ||
            !memory.TryReadByte(flagsAddress, out var flags))
        {
            return false;
        }

        snapshot = new WorldMapEntitySnapshot(
            address,
            next,
            address == playerPointer,
            x,
            y,
            z,
            walkmap & 0x1F,
            (walkmap >> 9) & 0x1F,
            model,
            flags);
        return true;
    }

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        try
        {
            result = checked(address + (uint)offset);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }
}

public readonly record struct WorldMapEntitySnapshot(
    uint GuestPointer,
    uint NextGuestPointer,
    bool IsPlayer,
    int X,
    int Y,
    int Z,
    int TerrainId,
    int RegionId,
    int ModelId,
    byte Flags);

public readonly record struct WorldMapEntityReadResult(
    bool IsUsable,
    IReadOnlyList<WorldMapEntitySnapshot> Entities,
    string Diagnostic)
{
    public static WorldMapEntityReadResult Valid(
        IReadOnlyList<WorldMapEntitySnapshot> entities,
        string diagnostic) => new(true, entities, diagnostic);

    public static WorldMapEntityReadResult Invalid(string diagnostic) =>
        new(false, Array.Empty<WorldMapEntitySnapshot>(), diagnostic);
}
