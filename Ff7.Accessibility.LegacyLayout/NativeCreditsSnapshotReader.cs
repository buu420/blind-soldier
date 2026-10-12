using System.Buffers.Binary;
using System.Text;

namespace Ff7.Accessibility.LegacyLayout;

public sealed record NativeCreditRow(int Index, int Style, string Text, int Y);
public sealed record NativeCreditsSnapshot(int FirstRowIndex, int ScrollOffset, IReadOnlyList<NativeCreditRow> VisibleRows,
    DateTime ObservedUtc = default);

public sealed class NativeCreditsSnapshotReader
{
    public const uint AddressCurrentModule = 0x00CBF9DC;
    public const int StaffCreditsModule = 0x1C;
    public const uint AddressCreditMode = 0x00F4F408;
    public const uint AddressCreditPhase = 0x00F4F414;
    public const uint AddressViewportHeight = 0x00F4F424;
    public const uint AddressPeoplePointer = 0x00F4F42C;
    public const uint AddressFirstRow = 0x00F4F328;
    public const uint AddressScrollOffset = 0x00F4F340;
    public const uint AddressScrolling = 0x00F4F44C;
    public const int RowStride = 0x40;
    public const int RowCount = 0x21B;
    public const int DrawWindowRowCount = 11;

    // 007A753A reads eleven records without a tail clamp. Legacy English update
    // 007A7A33 switches to title phase instead of incrementing FirstRow at 0x210
    // (528), so the last legal English staff window is records 528..538.

    /// <summary>
    /// Copies only the staff-credit rows whose baseline is in the viewport.
    /// Ghidra: 007A753A draws eleven records from 00F4F328, starting at the
    /// native scroll offset 00F4F340. Each record has text at +0xC and spacing
    /// at +0x3C. 007A7A33 and 007A7636 distinguish scrolling staff credits from
    /// the logos and the post-credit movie. The caller must additionally have
    /// observed the actual native text renderer in the current tick; initialized
    /// people.bin data alone cannot establish visibility when a movie replaces it.
    /// Partial rows whose baseline has already left the top are omitted.
    /// </summary>
    public bool TryRead(ILegacyAddressSpace memory, bool rendererObserved, out NativeCreditsSnapshot? snapshot)
    {
        snapshot = null;
        if (memory is null || !rendererObserved || !TryReadState(memory, out var state)) return false;
        var rows = new List<NativeCreditRow>();
        var y = state.ScrollOffset;
        Span<byte> first = stackalloc byte[RowStride];
        Span<byte> second = stackalloc byte[RowStride];
        for (var slot = 0; slot < DrawWindowRowCount; slot++)
        {
            var index = state.FirstRow + slot;
            if (index >= RowCount) return false;
            var address = state.People + (uint)(index * RowStride);
            if (!memory.TryReadInt32(address, out var style) || style is < 0 or > 5 ||
                !memory.TryReadInt32(address + 0x3C, out var spacing) || spacing is < 1 or > 1024)
                return false;
            if (y >= 0 && y < state.ViewportHeight)
            {
                if (!memory.TryRead(address, first) || !memory.TryRead(address, second) ||
                    !first.SequenceEqual(second) ||
                    BinaryPrimitives.ReadInt32LittleEndian(first) != style ||
                    BinaryPrimitives.ReadInt32LittleEndian(first[0x3C..]) != spacing ||
                    !TryDecode(first.Slice(0xC, 0x30), out var text)) return false;
                if (text.Length != 0) rows.Add(new NativeCreditRow(index, style, text, y));
            }
            y += spacing;
        }
        if (!TryReadState(memory, out var after) || state != after) return false;
        snapshot = new NativeCreditsSnapshot(state.FirstRow, state.ScrollOffset, rows);
        return true;
    }

    private static bool TryReadState(ILegacyAddressSpace memory, out CreditState state)
    {
        state = default;
        if (!memory.TryReadByte(AddressCurrentModule, out var module) || module != StaffCreditsModule ||
            !memory.TryReadInt32(AddressCreditMode, out var mode) || mode != 1 ||
            !memory.TryReadInt32(AddressCreditPhase, out var phase) || phase != 0 ||
            !memory.TryReadInt32(AddressScrolling, out var scrolling) || scrolling != 1 ||
            !memory.TryReadUInt32(AddressPeoplePointer, out var people) || people < 0x10000 ||
            people > uint.MaxValue - RowCount * RowStride ||
            !memory.TryReadInt32(AddressFirstRow, out var firstRow) || firstRow is < 0 or > RowCount - DrawWindowRowCount ||
            !memory.TryReadInt32(AddressScrollOffset, out var offset) || offset is < -4096 or > 4096 ||
            !memory.TryReadInt32(AddressViewportHeight, out var height) || height is < 64 or > 1024)
            return false;
        state = new CreditState(people, firstRow, offset, height);
        return true;
    }

    // PEOPLE.BIN uses a separate font alphabet, not the field text encoding.
    // Its original format research is corroborated by the installed cr_us.lgp
    // records and 007A8150's 0x7F terminator / 0x30 column-break handling.
    // Language-dependent glyphs 0x28..0x2E are not guessed.
    private static bool TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        text = string.Empty;
        var end = bytes.IndexOf((byte)0x7F);
        if (end < 0) return false;
        var result = new StringBuilder(end);
        foreach (var value in bytes[..end])
        {
            if (value <= 0x19) result.Append((char)('A' + value));
            else
            {
                var glyph = value switch
                {
                    0x1A => "1", 0x1B => "2", 0x1C => ":", 0x1D => ",", 0x1E => ".",
                    0x1F => "&", 0x20 => "(", 0x21 => ")", 0x22 => "'", 0x23 => "“",
                    0x24 => "”", 0x25 => "-", 0x26 => "3", 0x27 => "Å", 0x2F => " ",
                    0x30 => "; ", _ => null
                };
                if (glyph is null) return false;
                result.Append(glyph);
            }
        }
        text = result.ToString().Trim();
        return true;
    }

    private readonly record struct CreditState(uint People, int FirstRow, int ScrollOffset, int ViewportHeight);
}
