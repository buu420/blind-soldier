using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Read-only witnesses for the known Gameplay Tweaks 1.78 native patches. A fourth
/// equipment row or a non-stock materia chain alone is never evidence of this mod.
/// Guest addresses work through either runtime's ILegacyAddressSpace translation.
/// </summary>
public sealed class PostscriptEquipmentCompatibility
{
    private readonly ILegacyAddressSpace? memory;
    private readonly Func<int, byte>? readByte;

    private static readonly byte[] AccessoryAssignment = Convert.FromHexString(
        "83F802740383EA038B45FC31C98D44101E8A0880E1FF894DDC3008908A4DE8080890908B55DC80FAFF7418");
    private static readonly byte[] AccessoryJump = Convert.FromHexString("E9E001000090");
    private static readonly byte[] AccessoryStatus = Convert.FromHexString(
        "31DB8B4DF869C9840000008B55FC31C029DA8A44111E3CFF");

    public PostscriptEquipmentCompatibility(ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public PostscriptEquipmentCompatibility(Func<int, byte> readByte) =>
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));

    public bool HasTwoAccessories() =>
        Matches(0x00707571, AccessoryAssignment) &&
        Matches(0x00707383, AccessoryJump) &&
        Matches(0x00705CB8, [4]) &&
        Matches(0x007046B1, AccessoryStatus);

    public bool HasMultiLinkedSlots() =>
        Matches(0x005CB41D, [0x83, 0x7D, 0xE8, 0x07]) &&
        Matches(0x005CB496, [0x83, 0x7D, 0xE8, 0x07]) &&
        Matches(0x005CB8E4, [0xEB, 0x2A]) &&
        Matches(0x005CB919, [0x39, 0xC9, 0x90]) &&
        Matches(0x005CB921, [0x83, 0xE2, 0x03, 0x90, 0x90, 0x90, 0x83, 0xFA, 0x03]);

    public bool HasEquipmentBonusDisplay() =>
        Matches(0x00707896, [0x6C]) &&
        Matches(0x00707A44, [0x8B, 0x45, 0xBC, 0x89, 0x45, 0xD4]) &&
        Matches(0x00707979, Convert.FromHexString("31C0380C1074094083F80475F531C0C38A441004C3")) &&
        Matches(0x00707BA1, Convert.FromHexString("31C08D9134E7DB008A42048945C08A020345A883C210E856FEFFFF90")) &&
        Matches(0x00707BD6, Convert.FromHexString("6BC8248D91E2CCDB008A020345A08945D08A42028945E08A420103459C8945E49083C216B102E878FDFFFF0345B08945D48A42ED8945D831C9E865FDFFFF0345A48945F0"));

    private bool Matches(int address, ReadOnlySpan<byte> expected)
    {
        Span<byte> actual = stackalloc byte[expected.Length];
        if (memory is not null)
            return memory.TryRead((uint)address, actual) && actual.SequenceEqual(expected);
        try
        {
            for (var index = 0; index < expected.Length; index++)
                if (readByte!(address + index) != expected[index]) return false;
            return true;
        }
        catch { return false; }
    }
}
