using System.Buffers.Binary;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.LegacyLayout;

/// <summary>
/// Reads one requested entry from the text the native kernel2 loader actually owns.
/// FUN_00419457 uses a fixed 0x6C00-byte pool and an 18-word section-offset table.
/// Callers select an entry through visible UI state; this reader never enumerates
/// contents, falls back to files, or exposes a host pointer.
/// </summary>
public sealed class NativeKernel2TextReader
{
    public const uint AddressTextPool = 0x009A13C8;
    public const uint AddressSectionOffsets = 0x009A7FC8;
    public const int TextPoolCapacity = 0x6C00;
    private const int MaximumTextLength = 512;
    private static readonly byte[] LookupPrefix = Convert.FromHexString("558BEC83EC088B450803451033C9668B0C45C87F9A0081C1C8139A00");
    private static readonly int[] SectionCounts = [32,256,128,128,32,32,96,64,32,256,128,128,32,32,96,64,128,16];
    private readonly ILegacyAddressSpace memory;
    private readonly Ff7GameLanguageDescriptor? language;
    private Func<int, int, byte[]?>? backend;
    private Func<bool>? ownsBackend;

    public NativeKernel2TextReader(ILegacyAddressSpace memory, Ff7GameLanguageDescriptor? language = null)
    { this.memory = memory ?? throw new ArgumentNullException(nameof(memory)); this.language = language; }

    /// <summary>Bind an independently verified loader. Binding remains authoritative
    /// if its owner or selected entry becomes unavailable; disk text must not replace it.</summary>
    public void BindVerifiedBackend(Func<int, int, byte[]?> readEncoded, Func<bool> ownsLookup)
    {
        ArgumentNullException.ThrowIfNull(readEncoded);
        ArgumentNullException.ThrowIfNull(ownsLookup);
        backend = readEncoded;
        ownsBackend = ownsLookup;
    }

    /// <summary>A native lookup or redirected loader owns the text. An unbound
    /// redirect remains unavailable rather than reverting to the disk database.</summary>
    public bool HasSupportedLookup
    {
        get
        {
            if (backend is not null) return true;
            Span<byte> prefix=stackalloc byte[LookupPrefix.Length];
            return memory.TryRead(0x00419457,prefix) &&
                (prefix.SequenceEqual(LookupPrefix) || prefix[0] == 0xE9);
        }
    }

    public bool TryRead(int bank, int id, out string text)
    {
        text = string.Empty;
        if (backend is not null)
        {
            try
            {
                if (bank is < 0 or >= 18 || id < 0 || id >= SectionCounts[bank] || !ownsBackend!()) return false;
                var bytes = backend(bank, id);
                if (bytes is null || bytes.Length is < 1 or > MaximumTextLength ||
                    bytes.AsSpan().IndexOf((byte)255) != bytes.Length - 1 || !ownsBackend()) return false;
                return TryDecode(bytes, out text);
            }
            catch { return false; }
        }
        if (bank is < 0 or >= 18 || id < 0 || id >= SectionCounts[bank] ||
            !TryCapture(bank,id,out var before) || !TryCapture(bank,id,out var after) ||
            !before.Table.AsSpan().SequenceEqual(after.Table) || before.FirstOffset != after.FirstOffset ||
            before.EntryOffset != after.EntryOffset || !before.Bytes.AsSpan().SequenceEqual(after.Bytes) ||
            !MatchesBookend(bank,id,after)) return false;
        return TryDecode(before.Bytes, out text);
    }

    private bool TryDecode(byte[] bytes, out string text)
    {
        text = string.Empty;
        var decoded = language is null
            ? Ff7EncodedTextDecoder.DecodeKernelTerminated(bytes)
            : Ff7EncodedTextDecoder.DecodeKernelTerminated(bytes, language);
        decoded = decoded.Replace("“",string.Empty,StringComparison.Ordinal).Replace("”",string.Empty,StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(decoded) || decoded.Contains('\uFFFD')) return false;
        text = decoded;
        return true;
    }

    private bool MatchesBookend(int bank,int id,Candidate candidate)
    {
        Span<byte> prefix=stackalloc byte[LookupPrefix.Length];
        Span<byte> table=stackalloc byte[18*sizeof(ushort)];
        if (!memory.TryRead(0x00419457,prefix) || !prefix.SequenceEqual(LookupPrefix) ||
            !memory.TryRead(AddressSectionOffsets,table) || !table.SequenceEqual(candidate.Table)) return false;
        var section=AddressTextPool+BinaryPrimitives.ReadUInt16LittleEndian(table.Slice(bank*2,2));
        return memory.TryReadUInt16(section,out var first) && first==candidate.FirstOffset &&
            memory.TryReadUInt16(section+(uint)id*2,out var entry) && entry==candidate.EntryOffset;
    }

    private bool TryCapture(int bank,int id,out Candidate candidate)
    {
        candidate = default;
        Span<byte> prefix = stackalloc byte[LookupPrefix.Length];
        if (!memory.TryRead(0x00419457,prefix) || !prefix.SequenceEqual(LookupPrefix)) return false;
        var table = new byte[18*sizeof(ushort)];
        if (!memory.TryRead(AddressSectionOffsets,table)) return false;
        var start = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(bank*2,2));
        var end = bank == 17 ? TextPoolCapacity : BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan((bank+1)*2,2));
        if (end <= start || end > TextPoolCapacity) return false;
        var section = AddressTextPool + start;
        if (!memory.TryReadUInt16(section,out var firstOffset) || firstOffset != SectionCounts[bank]*2 ||
            !memory.TryReadUInt16(section + (uint)id*2,out var entryOffset) ||
            entryOffset < firstOffset || entryOffset >= end-start) return false;
        var available = Math.Min(MaximumTextLength,end-start-entryOffset);
        var bytes = new List<byte>(available);
        for(var i=0;i<available;i++)
        {
            if (!memory.TryReadByte(section + entryOffset + (uint)i,out var value)) return false;
            bytes.Add(value);
            if (value == 0xFF)
            {
                candidate = new Candidate(table,firstOffset,entryOffset,bytes.ToArray());
                return true;
            }
        }
        return false;
    }
    private readonly record struct Candidate(byte[] Table,ushort FirstOffset,ushort EntryOffset,byte[] Bytes);
    private string? Resolve(int bank,int id) => TryRead(bank,id,out var text) ? text : null;
    public string? ResolveItemName(int id) => Resolve(10,id);
    public string? ResolveItemDescription(int id) => Resolve(2,id);
    public string? ResolveSpellName(int id) => Resolve(9,id);
    public string? ResolveSpellDescription(int id) => Resolve(1,id);
    public string? ResolveWeaponName(int id) => Resolve(11,id);
    public string? ResolveWeaponDescription(int id) => Resolve(3,id);
    public string? ResolveArmorName(int id) => Resolve(12,id);
    public string? ResolveArmorDescription(int id) => Resolve(4,id);
    public string? ResolveAccessoryName(int id) => Resolve(13,id);
    public string? ResolveAccessoryDescription(int id) => Resolve(5,id);
    public string? ResolveCommandName(int id) => Resolve(8,id);
    public string? ResolveCommandDescription(int id) => Resolve(0,id);
    public string? ResolveMateriaName(int id) => Resolve(14,id);
    public string? ResolveMateriaDescription(int id) => Resolve(6,id);
    public string? ResolveBattleText(int id) => Resolve(16,id);
    public string? ResolveInventoryObjectName(int id) => id switch
    { >=0 and <128 => ResolveItemName(id), >=128 and <256 => ResolveWeaponName(id-128),
      >=256 and <288 => ResolveArmorName(id-256), >=288 and <320 => ResolveAccessoryName(id-288), _ => null };
    public string? ResolveInventoryObjectDescription(int id) => id switch
    { >=0 and <128 => ResolveItemDescription(id), >=128 and <256 => ResolveWeaponDescription(id-128),
      >=256 and <288 => ResolveArmorDescription(id-256), >=288 and <320 => ResolveAccessoryDescription(id-288), _ => null };
    public string? ResolveBattleActionName(int rawActionId) => ResolveAction(9,rawActionId);
    public string? ResolveBattleActionDescription(int rawActionId) => ResolveAction(1,rawActionId);
    public string? ResolveSummonBannerText(int action) => action switch
    { >=0 and <16 => NormalizeAction(Resolve(17,action)), >=16 and <=255 => NormalizeAction(Resolve(9,action)), _ => null };
    private string? ResolveAction(int bank,int action)
    {
        if(action is <0 or >255 or 0x7F) return null;
        var shifted=action+0x80;
        return NormalizeAction(Resolve(bank,shifted<0xE0 ? shifted : action));
    }
    private static string? NormalizeAction(string? text)
    {
        var value=text?.Trim().TrimStart('"').Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

