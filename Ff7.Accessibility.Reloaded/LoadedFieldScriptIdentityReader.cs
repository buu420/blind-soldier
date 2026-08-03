using System.Buffers.Binary;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct LoadedFieldScriptIdentity(
    int FieldId,
    uint ScriptPointer,
    string ScriptPrefixSha256);

/// <summary>
/// Identifies the field script bytes that are actually loaded after FFNx and
/// 7th Heaven have applied their virtual-file overrides. The hash stops at the
/// native text-table offset, matching the offline compatibility analyzer.
/// </summary>
public sealed class LoadedFieldScriptIdentityReader
{
    private const int HeaderLength = 8;
    private const int MinimumScriptPrefixLength = 32;
    private readonly ILegacyAddressSpace memory;

    public LoadedFieldScriptIdentityReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public bool TryRead(out LoadedFieldScriptIdentity identity)
    {
        identity = default;
        if (!TryCaptureHeader(out var before) ||
            before.Module != FieldPositionReader.FieldModule ||
            before.EntityCount == 0 ||
            before.TextOffset < MinimumScriptPrefixLength)
        {
            return false;
        }

        var first = new byte[before.TextOffset];
        var second = new byte[before.TextOffset];
        if (!memory.TryRead(before.ScriptPointer, first) ||
            !TryCaptureHeader(out var middle) ||
            !before.Equals(middle) ||
            !memory.TryRead(before.ScriptPointer, second) ||
            !first.AsSpan().SequenceEqual(second) ||
            !TryCaptureHeader(out var after) ||
            !before.Equals(after))
        {
            return false;
        }

        identity = new LoadedFieldScriptIdentity(
            before.FieldId,
            before.ScriptPointer,
            Convert.ToHexString(SHA256.HashData(first)));
        return true;
    }

    private bool TryCaptureHeader(out HeaderSnapshot snapshot)
    {
        snapshot = default;
        Span<byte> header = stackalloc byte[HeaderLength];
        if (!memory.TryReadByte((uint)FieldScriptContextReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldScriptContextReader.AddressCurrentFieldId, out var fieldId) ||
            !memory.TryReadUInt32((uint)FieldScriptContextReader.AddressFieldScriptPtr, out var scriptPointer) ||
            scriptPointer == 0 ||
            (ulong)scriptPointer + ushort.MaxValue > uint.MaxValue ||
            !memory.TryRead(scriptPointer, header))
        {
            return false;
        }

        var entityCount = header[2];
        var textOffset = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        if (textOffset < HeaderLength)
        {
            return false;
        }

        snapshot = new HeaderSnapshot(module, fieldId, scriptPointer, entityCount, textOffset);
        return true;
    }

    private readonly record struct HeaderSnapshot(
        byte Module,
        ushort FieldId,
        uint ScriptPointer,
        byte EntityCount,
        ushort TextOffset);
}
