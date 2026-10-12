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
        (before.MovieActive == 0 || before.MovieActive == 1 && before.BackgroundMode == 1);

    private bool TryRead(out Presentation state)
    {
        state = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var field) ||
            !memory.TryReadUInt16((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, out var movie) ||
            !memory.TryReadByte((uint)FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode, out var background)) return false;
        state = new(module,field,movie,background);
        return true;
    }

    private readonly record struct Presentation(byte Module, ushort Field, ushort MovieActive, byte BackgroundMode);
}
