using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Whether the current native renderer presents the field room, independently of player control.</summary>
public sealed class FieldRoomPresentationReader
{
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace memory;

    public FieldRoomPresentationReader(Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    // Native 00644075 draws the field during an active movie only in BGMOVIE mode.
    // Read the complete presentation twice; absent or changing flags prove nothing.
    public bool IsRoomVisible(int expectedFieldId) =>
        expectedFieldId is >= 0 and <= ushort.MaxValue &&
        TryRead(out var before) && TryRead(out var after) && before == after &&
        before.Module == FieldPositionReader.FieldModule && before.Field == expectedFieldId &&
        before.BackgroundMode is 0 or 1 &&
        (before.MovieActive == 0 || before.MovieActive == 1 && before.BackgroundMode == 1) &&
        (before.Field != 767 || before.Fade == 0 && before.Skip == 0 && before.Special == 0 &&
            (before.MovieActive == 0 || before.Tiles == 1 || before.AlternateTiles == 1));

    private bool TryRead(out Presentation state)
    {
        state = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var field) ||
            !memory.TryReadUInt16((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, out var movie) ||
            !memory.TryReadByte((uint)FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode, out var background)) return false;
        byte tiles = 0, skip = 0;
        var alternate = 0; var fade = 0; var special = 0;
        // las4_4 uses a full-screen looping movie behind the HRC actors. Native
        // 0063A60B suppresses all field tiles unless either independent tile flag
        // is set. BGMOVIE alone does not prove its static room can be seen.
        if (field == 767 && (!memory.TryReadByte(0x00CC0B68, out tiles) || tiles > 1 ||
            !memory.TryReadInt32(0x009A0718, out alternate) || alternate is not (0 or 1) ||
            !memory.TryReadInt32(0x00CFFEF8, out fade) ||
            !memory.TryReadByte(0x00CC0DC0, out skip) ||
            !memory.TryReadInt32(0x00CFFA98, out special))) return false;
        state = new(module,field,movie,background,tiles,alternate,fade,skip,special);
        return true;
    }

    private readonly record struct Presentation(byte Module, ushort Field, ushort MovieActive, byte BackgroundMode,
        byte Tiles, int AlternateTiles, int Fade, byte Skip, int Special);
}
