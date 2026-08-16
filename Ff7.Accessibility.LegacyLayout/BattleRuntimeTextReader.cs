using System.Collections.Immutable;
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
    public const byte NumberControl = 0xEC;
    public const byte TargetNameControl = 0xED;
    public const byte AttackNameControl = 0xEE;
    public const byte TargetIdControl = 0xEF;
    public const byte ElementControl = 0xF0;

    private const int RuntimeBufferBase = 0x100;
    private const int RuntimeSlotCount = 64;
    private const int RuntimeTextCapacity = 0x800;
    private const int MaxEncodedTextLength = 256;
    private readonly ILegacyAddressSpace addressSpace;
    private readonly Func<int, string?> resolveStaticText;
    private readonly Func<int, string?> resolveInventoryObjectName;
    private readonly Func<int, string?> resolveTargetName;
    private readonly Func<int, string?> resolveAttackName;
    private readonly Func<int, string?> resolveElementName;

    public BattleRuntimeTextReader(
        ILegacyAddressSpace addressSpace,
        Func<int, string?> resolveStaticText,
        Func<int, string?> resolveInventoryObjectName,
        Func<int, string?>? resolveTargetName = null,
        Func<int, string?>? resolveAttackName = null,
        Func<int, string?>? resolveElementName = null,
        Ff7GameLanguageDescriptor? language = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveStaticText = resolveStaticText ?? throw new ArgumentNullException(nameof(resolveStaticText));
        this.resolveInventoryObjectName = resolveInventoryObjectName
            ?? throw new ArgumentNullException(nameof(resolveInventoryObjectName));
        this.resolveTargetName = resolveTargetName ?? (_ => null);
        this.resolveAttackName = resolveAttackName ?? (_ => null);
        this.resolveElementName = resolveElementName ??
            (language is null
                ? _ => null
                : new BattleElementNameReader(addressSpace, language).Resolve);
    }

    public string? Resolve(int bufferIndex) => ResolveDetailed(bufferIndex)?.Text;

    public string? ResolveElementName(int elementId) => Normalize(resolveElementName(elementId));

    public BattleRuntimeTextResolution? ResolveDetailed(int bufferIndex)
    {
        if (bufferIndex < RuntimeBufferBase)
        {
            // Record 47 is a template ("Stole" plus an item-name control),
            // never a complete public result. The engine activates its
            // substituted runtime-ring record instead.
            var text = bufferIndex == 47 ? null : Normalize(resolveStaticText(bufferIndex));
            return text is null ? null : new BattleRuntimeTextResolution(text, []);
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

    private BattleRuntimeTextResolution? Decode(ReadOnlySpan<byte> encoded)
    {
        var text = new StringBuilder(encoded.Length);
        var controls = new List<BattleRuntimeTextControl>();
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

            if (value is >= ItemNameControl and <= ElementControl)
            {
                if (index + 2 >= encoded.Length)
                {
                    return null;
                }

                var argument = (encoded[index + 1] << 8) | encoded[index + 2];
                if (!AppendRuntimeValue(text, controls, value, argument))
                {
                    return null;
                }

                index += 3;
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

        var normalized = Normalize(Ff7EncodedTextDecoder.NormalizeWhitespace(text.ToString()));
        return normalized is null ? null : new BattleRuntimeTextResolution(normalized, controls);
    }

    private bool AppendRuntimeValue(
        StringBuilder text,
        ICollection<BattleRuntimeTextControl> controls,
        byte control,
        int argument)
    {
        switch (control)
        {
            case ItemNameControl:
                controls.Add(new(BattleRuntimeTextControlKind.ItemName, argument));
                text.Append(resolveInventoryObjectName(argument) ?? "an item");
                return true;
            case NumberControl:
                controls.Add(new(BattleRuntimeTextControlKind.Number, argument));
                text.Append(argument);
                return true;
            case TargetNameControl:
                controls.Add(new(BattleRuntimeTextControlKind.TargetName, argument));
                text.Append(resolveTargetName(argument) ?? "target");
                return true;
            case AttackNameControl:
                controls.Add(new(BattleRuntimeTextControlKind.AttackName, argument));
                text.Append(resolveAttackName(argument) ?? "attack");
                return true;
            case TargetIdControl:
                controls.Add(new(BattleRuntimeTextControlKind.TargetId, argument));
                text.Append(argument is >= 0 and < 26 ? (char)('A' + argument) : '?');
                return true;
            case ElementControl:
                var elementName = ResolveElementName(argument);
                if (elementName is null)
                {
                    return false;
                }

                controls.Add(new(BattleRuntimeTextControlKind.Element, argument));
                text.Append(elementName);
                return true;
            default:
                return false;
        }
    }

    private static string? Normalize(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}

public enum BattleRuntimeTextControlKind
{
    ItemName,
    Number,
    TargetName,
    AttackName,
    TargetId,
    Element
}

public readonly record struct BattleRuntimeTextControl(
    BattleRuntimeTextControlKind Kind,
    int Argument);

public sealed record BattleRuntimeTextResolution
{
    public BattleRuntimeTextResolution(
        string text,
        IEnumerable<BattleRuntimeTextControl> controls)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text.Trim();
        Controls = controls?.ToImmutableArray()
            ?? throw new ArgumentNullException(nameof(controls));
    }

    public string Text { get; }

    public ImmutableArray<BattleRuntimeTextControl> Controls { get; }
}
