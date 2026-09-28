using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The status masks the battle renderer is currently drawing, one per battle
/// actor, and the actors whose damage popup has been created but not drawn yet.
/// </summary>
public readonly struct BattleDisplayedStatusSnapshot : IEquatable<BattleDisplayedStatusSnapshot>
{
    private readonly uint[]? masks;

    public BattleDisplayedStatusSnapshot(IReadOnlyList<uint> masks, ushort pendingPopupActors)
    {
        ArgumentNullException.ThrowIfNull(masks);
        if (masks.Count != BattleDisplayedStatusReader.ActorCount)
        {
            throw new ArgumentException(
                $"Expected {BattleDisplayedStatusReader.ActorCount} displayed status masks.",
                nameof(masks));
        }

        this.masks = masks.ToArray();
        PendingPopupActors = pendingPopupActors;
    }

    public static BattleDisplayedStatusSnapshot Invalid => default;

    public bool IsValid => masks is not null;

    /// <summary>Actors with a popup allocated for a hit that has not drawn its first frame.</summary>
    public ushort PendingPopupActors { get; }

    public uint MaskFor(int actorIndex) =>
        masks is not null && actorIndex is >= 0 and < BattleDisplayedStatusReader.ActorCount
            ? masks[actorIndex]
            : 0;

    public bool HasPendingPopup(int actorIndex) =>
        actorIndex is >= 0 and < BattleDisplayedStatusReader.ActorCount &&
        (PendingPopupActors & (1 << actorIndex)) != 0;

    public bool Equals(BattleDisplayedStatusSnapshot other) =>
        PendingPopupActors == other.PendingPopupActors &&
        (masks is null
            ? other.masks is null
            : other.masks is not null && masks.AsSpan().SequenceEqual(other.masks));

    public override bool Equals(object? obj) =>
        obj is BattleDisplayedStatusSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PendingPopupActors);
        if (masks is not null)
        {
            foreach (var mask in masks)
            {
                hash.Add(mask);
            }
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(BattleDisplayedStatusSnapshot left, BattleDisplayedStatusSnapshot right) =>
        left.Equals(right);

    public static bool operator !=(BattleDisplayedStatusSnapshot left, BattleDisplayedStatusSnapshot right) =>
        !left.Equals(right);
}

/// <summary>
/// Reads the status each battle actor is drawn with. The damage calculation
/// writes the live mask (0x009AB0DC) before an action's animation starts; the
/// drawn copy changes only when the animation reaches the target: FUN_0042de61
/// copies the result row's status on the hit message, and the queue's status
/// event (kind 5, FUN_0042cbf9) or animation opcode 0x1E copy it for timed
/// changes. The Sleep and Silence icons (FUN_005b9b30), the Haste, Slow, Stop
/// and Confusion motion (FUN_005b9ec2), the Poison, Berserk, Regen and Petrify
/// tints (FUN_005ba1eb) and the Frog and Mini models (FUN_005bd5e9,
/// FUN_005bd847) all draw from this copy.
/// </summary>
public sealed class BattleDisplayedStatusReader
{
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;

    // FFNx g_small_battle_model_state (run_animation_script + 0x2BB9): ten
    // 0x74-byte records, field 0 is the drawn status mask. FUN_00428f59 clears
    // all ten when a battle quits (FUN_0041b87d), so each battle starts at zero.
    public const int AddressDisplayedStatus = 0x00BF23C0;
    public const int DisplayedStateSize = 0x74;
    public const int ActorCount = 10;

    private readonly ILegacyAddressSpace addressSpace;

    public BattleDisplayedStatusReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryRead(out BattleDisplayedStatusSnapshot snapshot)
    {
        var candidate = ReadCore();
        var bookend = ReadCore();
        if (!candidate.IsValid || candidate != bookend)
        {
            snapshot = BattleDisplayedStatusSnapshot.Invalid;
            return false;
        }

        snapshot = candidate;
        return true;
    }

    private BattleDisplayedStatusSnapshot ReadCore()
    {
        if (!addressSpace.TryReadByte((uint)AddressCurrentModule, out var module) ||
            module != BattleStateReader.BattleModule)
        {
            return BattleDisplayedStatusSnapshot.Invalid;
        }

        // One read spans all ten records (0x418 bytes inside one guest page).
        Span<byte> records = stackalloc byte[(ActorCount - 1) * DisplayedStateSize + sizeof(uint)];
        if (!addressSpace.TryRead((uint)AddressDisplayedStatus, records))
        {
            return BattleDisplayedStatusSnapshot.Invalid;
        }

        var masks = new uint[ActorCount];
        for (var actor = 0; actor < ActorCount; actor++)
        {
            masks[actor] = BinaryPrimitives.ReadUInt32LittleEndian(records[(actor * DisplayedStateSize)..]);
        }

        return TryReadPendingPopupActors(out var pending)
            ? new BattleDisplayedStatusSnapshot(masks, pending)
            : BattleDisplayedStatusSnapshot.Invalid;
    }

    // A hit message sets the drawn mask and queues its popup in the same frame
    // (FUN_0042de61 -> FUN_0042e05a -> FUN_00425fc4); the popup draws its first
    // frame on the next pass of the effect list.
    private bool TryReadPendingPopupActors(out ushort pending)
    {
        pending = 0;
        Span<byte> table = stackalloc byte[BattleVisibleResultReader.EffectCount * sizeof(uint)];
        if (!addressSpace.TryRead((uint)BattleVisibleResultReader.AddressEffectFunctions, table))
        {
            return false;
        }

        for (var slot = 0; slot < BattleVisibleResultReader.EffectCount; slot++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(table[(slot * sizeof(uint))..]) !=
                BattleVisibleResultReader.DamagePopupFunction)
            {
                continue;
            }

            var record = BattleVisibleResultReader.EffectRecordAddress(slot);
            if (!addressSpace.TryReadByte(record + BattleVisibleResultReader.EffectStateOffset, out var state) ||
                !addressSpace.TryReadInt32(record + BattleVisibleResultReader.PopupTargetOffset, out var target))
            {
                return false;
            }

            if (state == 0 && target is >= 0 and < 3 or >= 4 and <= 9)
            {
                pending |= (ushort)(1 << target);
            }
        }

        return true;
    }
}
