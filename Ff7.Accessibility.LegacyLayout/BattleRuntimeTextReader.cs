using System.Text;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Resolves both ordinary KERNEL2 battle strings and the native 64-slot
/// runtime text ring used after FFVII substitutes values such as a stolen
/// item's inventory-object id.
/// </summary>
public sealed class BattleRuntimeTextReader
{
    public const int AddressRuntimeTextBuffer = 0x009AD1E0;
    public const int AddressRuntimeTextOffsets = 0x009AD9E0;
    public const byte ItemNameControl = 0xEB;

    private const int RuntimeBufferBase = 0x100;
    private const int RuntimeSlotCount = 64;
    private const int RuntimeTextCapacity = 0x800;
    private const int MaxEncodedTextLength = 256;
    private const byte NumberControl = 0xEC;
    private const byte TargetNameControl = 0xED;
    private const byte AttackNameControl = 0xEE;
    private const byte SpecialNumberControl = 0xEF;
    private const byte TargetLetterControl = 0xF0;
    private readonly ILegacyAddressSpace addressSpace;
    private readonly Func<int, string?> resolveStaticText;
    private readonly Func<int, string?> resolveInventoryObjectName;
    private readonly Func<int, string?> resolveTargetName;
    private readonly Func<int, string?> resolveAttackName;

    public BattleRuntimeTextReader(
        ILegacyAddressSpace addressSpace,
        Func<int, string?> resolveStaticText,
        Func<int, string?> resolveInventoryObjectName,
        Func<int, string?>? resolveTargetName = null,
        Func<int, string?>? resolveAttackName = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveStaticText = resolveStaticText ?? throw new ArgumentNullException(nameof(resolveStaticText));
        this.resolveInventoryObjectName = resolveInventoryObjectName
            ?? throw new ArgumentNullException(nameof(resolveInventoryObjectName));
        this.resolveTargetName = resolveTargetName ?? (_ => null);
        this.resolveAttackName = resolveAttackName ?? (_ => null);
    }

    public string? Resolve(int bufferIndex)
    {
        if (bufferIndex < RuntimeBufferBase)
        {
            // Record 47 is a template ("Stole" plus an item-name control),
            // never a complete public result. The engine activates its
            // substituted runtime-ring record instead.
            return bufferIndex == 47 ? null : Normalize(resolveStaticText(bufferIndex));
        }

        var slot = bufferIndex - RuntimeBufferBase;
        if (slot is < 0 or >= RuntimeSlotCount || !TryReadEncoded(slot, out var encoded))
        {
            return null;
        }

        return Decode(encoded);
    }

    private bool TryReadEncoded(int slot, out byte[] encoded)
    {
        encoded = [];
        Span<byte> offsetBytes = stackalloc byte[sizeof(ushort)];
        var offsetAddress = checked((uint)(AddressRuntimeTextOffsets + slot * sizeof(ushort)));
        if (!addressSpace.TryRead(offsetAddress, offsetBytes))
        {
            return false;
        }

        var offsetBefore = (ushort)(offsetBytes[0] | (offsetBytes[1] << 8));
        if (offsetBefore >= RuntimeTextCapacity)
        {
            return false;
        }

        var bytes = new List<byte>(64);
        Span<byte> value = stackalloc byte[1];
        for (var index = 0;
             index < MaxEncodedTextLength && offsetBefore + index < RuntimeTextCapacity;
             index++)
        {
            if (!addressSpace.TryRead(
                    checked((uint)(AddressRuntimeTextBuffer + offsetBefore + index)),
                    value))
            {
                return false;
            }

            bytes.Add(value[0]);
            if (value[0] == 0xFF)
            {
                if (!addressSpace.TryRead(offsetAddress, offsetBytes))
                {
                    return false;
                }

                var offsetAfter = (ushort)(offsetBytes[0] | (offsetBytes[1] << 8));
                if (offsetAfter != offsetBefore)
                {
                    return false;
                }

                encoded = bytes.ToArray();
                return true;
            }
        }

        return false;
    }

    private string? Decode(ReadOnlySpan<byte> encoded)
    {
        var text = new StringBuilder(encoded.Length);
        for (var index = 0; index < encoded.Length;)
        {
            var value = encoded[index];
            if (value == 0xFF)
            {
                break;
            }

            if (value <= 0x5E)
            {
                text.Append((char)(value + 0x20));
                index++;
                continue;
            }

            if (value is >= ItemNameControl and <= TargetLetterControl)
            {
                if (index + 3 >= encoded.Length)
                {
                    return null;
                }

                var argument = (encoded[index + 1] << 8) | encoded[index + 2];
                AppendRuntimeValue(text, value, argument);
                index += 4;
                continue;
            }

            switch (value)
            {
                case 0xA9:
                case 0xE4:
                    text.Append("... ");
                    break;
                case 0xD0:
                case 0xE1:
                case 0xE7:
                case 0xE8:
                case 0xE9:
                    text.Append(' ');
                    break;
                case 0xE2:
                    text.Append(", ");
                    break;
                case 0xE3:
                    text.Append(". ");
                    break;
                case 0xF8:
                    index = Math.Min(encoded.Length, index + 3);
                    continue;
            }

            index++;
        }

        return Normalize(Ff7EncodedTextDecoder.NormalizeWhitespace(text.ToString()));
    }

    private void AppendRuntimeValue(StringBuilder text, byte control, int argument)
    {
        switch (control)
        {
            case ItemNameControl:
                text.Append(resolveInventoryObjectName(argument) ?? "an item");
                break;
            case NumberControl:
            case SpecialNumberControl:
                text.Append(argument);
                break;
            case TargetNameControl:
                text.Append(resolveTargetName(argument) ?? "target");
                break;
            case AttackNameControl:
                text.Append(resolveAttackName(argument) ?? "attack");
                break;
            case TargetLetterControl:
                text.Append(argument is >= 0 and < 26 ? (char)('A' + argument) : '?');
                break;
        }
    }

    private static string? Normalize(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
