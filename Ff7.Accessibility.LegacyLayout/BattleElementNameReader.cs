using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Resolves the native localized element-name table used by battle text's
/// ELEMENT control. The fixed ten-byte rows are part of the translated legacy
/// address model in both supported runtimes.
/// </summary>
public sealed class BattleElementNameReader
{
    public const int AddressElementNames = 0x00920540;
    public const int ElementNameSize = 0x0A;
    public const int ElementCount = 9;

    private readonly ILegacyAddressSpace addressSpace;
    private readonly Ff7GameLanguageDescriptor language;

    public BattleElementNameReader(
        ILegacyAddressSpace addressSpace,
        Ff7GameLanguageDescriptor language)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.language = language ?? throw new ArgumentNullException(nameof(language));
    }

    public string? Resolve(int elementId)
    {
        if (elementId is < 0 or >= ElementCount)
        {
            return null;
        }

        var address = checked((uint)(AddressElementNames + elementId * ElementNameSize));
        Span<byte> first = stackalloc byte[ElementNameSize];
        Span<byte> second = stackalloc byte[ElementNameSize];
        if (!addressSpace.TryRead(address, first) ||
            !addressSpace.TryRead(address, second) ||
            !first.SequenceEqual(second) ||
            first.IndexOf((byte)0xFF) < 0)
        {
            return null;
        }

        var text = Ff7EncodedTextDecoder.DecodeKernel(first, language).Trim();
        return text.Length == 0 || text.Contains('\uFFFD') ? null : text;
    }
}
