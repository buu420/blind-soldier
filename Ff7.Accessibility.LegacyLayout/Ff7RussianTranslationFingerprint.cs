using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Read-only evidence of ayumosa/FFRTT 1.02's enabled English-engine Hext overlay.</summary>
internal static class Ff7RussianTranslationFingerprint
{
    public static bool IsLoaded(ILegacyAddressSpace memory)
    {
        // Literal menu "Магия" and default "Сефирот" strings from Text.txt.
        // A discovered/downloaded IRO or a stale manager profile is not evidence
        // of the data the game actually loaded. Recheck both reads as one sample.
        ReadOnlySpan<byte> magic = [0x2d, 0x41, 0x6b, 0x6f, 0xb8, 0xff];
        ReadOnlySpan<byte> sephiroth = [0x23, 0x6c, 0x79, 0x6f, 0x50, 0x4f, 0x78, 0xff];
        Span<byte> observedMagic = stackalloc byte[6];
        Span<byte> observedName = stackalloc byte[8];
        for (var pass = 0; pass < 2; pass++)
        {
            if (!memory.TryRead(0x0091a8d4, observedMagic) || !observedMagic.SequenceEqual(magic) ||
                !memory.TryRead(0x00922430, observedName) || !observedName.SequenceEqual(sephiroth))
                return false;
        }
        return true;
    }
}
