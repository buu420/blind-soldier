using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Whether the door the Pagoda's bell opens is open now.
///
/// <para>uutai2/AD holds triangle 128, the passage to gateway 0 and the Hidden Room,
/// unless temporary 5[3] is 1: <c>IFUB 5[3] == 1</c>, else <c>IDLCK 128 lock</c>. It sets
/// 5[3] itself when the party arrives back from uttmpin4. KANE, the bell, opens the door
/// only from <c>5[3] == 0</c>: <c>IDLCK 128 unlock</c>, then <c>SETBYTE 5[3] = 1</c>. So 5[3]
/// is the door, and 1 is the only value that means open.</para>
///
/// <para>The temporary bank belongs to whichever field is loaded, so the flag is read
/// between two reads of the field module and field id, and twice itself. Anything
/// unreadable, anything that changes during the read, and any field but 587 gives no
/// answer, and a caller must not treat no answer as an open door.</para>
/// </summary>
public sealed class WutaiBellDoorStateReader
{
    public const int PagodaFieldId = 587;

    /// <summary>uutai2's temporary 5[3].</summary>
    public const int DoorOpenFlagAddress = FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 3;

    /// <summary>What KANE's <c>SETBYTE</c> and AD's own test both mean by open.</summary>
    public const byte DoorOpenValue = 1;

    private readonly ILegacyAddressSpace addressSpace;

    public WutaiBellDoorStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    /// <returns>
    /// True or false from one coherent read of field 587; null when the door cannot be known.
    /// </returns>
    public bool? ReadDoorOpen()
    {
        if (!TryReadOwner(out var moduleBefore, out var fieldBefore) ||
            !addressSpace.TryReadByte(DoorOpenFlagAddress, out var first) ||
            !addressSpace.TryReadByte(DoorOpenFlagAddress, out var second) ||
            !TryReadOwner(out var moduleAfter, out var fieldAfter))
        {
            return null;
        }

        if (moduleBefore != FieldPositionReader.FieldModule ||
            fieldBefore != PagodaFieldId ||
            moduleAfter != moduleBefore ||
            fieldAfter != fieldBefore ||
            first != second)
        {
            return null;
        }

        return first == DoorOpenValue;
    }

    private bool TryReadOwner(out byte module, out ushort fieldId)
    {
        fieldId = 0;
        return addressSpace.TryReadByte(FieldPositionReader.AddressCurrentModule, out module) &&
               addressSpace.TryReadUInt16(FieldPositionReader.AddressFieldId, out fieldId);
    }
}
