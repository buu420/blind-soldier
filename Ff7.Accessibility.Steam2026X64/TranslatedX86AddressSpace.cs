using System.Buffers;
using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Steam2026X64;

internal sealed class TranslatedX86AddressSpace : ILegacyAddressSpace, ILegacyMemoryWriter
{
    public const ulong ResolverRva = 0x000000000003F0A0;
    public const ulong PageTableRva = 0x0000000001739010;
    public const ulong UnmappedSentinelRva = 0x0000000001738D00;
    public const int PageSize = 0x1000;
    public const int PageCount = 1 << 20;

    private const ulong PageTableByteLength = (ulong)PageCount * sizeof(ulong);

    private static readonly byte[] ResolverSignature = Convert.FromHexString(
        "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3");

    private readonly ulong moduleBase;
    private readonly ulong pageTableAddress;
    private readonly ulong pageTableEndExclusive;
    private readonly ulong unmappedSentinelAddress;
    private readonly INativeMemoryReader memory;

    /// <summary>
    /// Optional, and null everywhere except where a write is actually intended.
    /// Reading is the default capability; writing has to be handed over on purpose.
    /// </summary>
    private readonly INativeMemoryWriter? writer;

    public TranslatedX86AddressSpace(
        ulong moduleBase,
        INativeMemoryReader memory,
        INativeMemoryWriter? writer = null)
    {
        if (moduleBase == 0
            || moduleBase > ulong.MaxValue - PageTableRva - PageTableByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        this.moduleBase = moduleBase;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.writer = writer;
        pageTableAddress = checked(moduleBase + PageTableRva);
        pageTableEndExclusive = checked(pageTableAddress + PageTableByteLength);
        unmappedSentinelAddress = checked(moduleBase + UnmappedSentinelRva);
    }

    /// <summary>
    /// Writes four bytes into the guest's own backing page.
    /// </summary>
    /// <remarks>
    /// The page table resolves a guest address to the game's actual storage rather
    /// than to a mirror, so a write here is visible to translated guest code. It
    /// mirrors <see cref="TryRead"/>'s resolution exactly, including the trusted
    /// page-table region check and the unmapped sentinel, and adds what a write
    /// needs on top:
    ///
    /// <list type="bullet">
    /// <item>the whole value inside one guest page, because a split write would
    /// lose the atomicity that keeps the game from seeing half a cursor;</item>
    /// <item>the page-table entry re-read afterwards and required to be unchanged,
    /// so a write that raced a re-map is not reported as success;</item>
    /// <item>the value read back through the ordinary read path, so the caller is
    /// told the truth about whether it took.</item>
    /// </list>
    ///
    /// <para>Nothing is cached between calls. The translator re-resolves on every
    /// operation and a retained host pointer would outlive the mapping.</para>
    /// </remarks>
    public bool TryWriteInt32(uint virtualAddress, int value)
    {
        if (writer is null || virtualAddress == 0 || (virtualAddress & 3) != 0)
        {
            return false;
        }

        var pageOffset = (int)(virtualAddress & (PageSize - 1));
        if (pageOffset + sizeof(int) > PageSize)
        {
            // Straddles two guest pages, whose host pages need not be adjacent.
            return false;
        }

        var pageEntryAddress = pageTableAddress + (((ulong)virtualAddress >> 12) * sizeof(ulong));
        if (!HasTrustedPageTableEntryRegion(pageEntryAddress)
            || !memory.TryReadUInt64(pageEntryAddress, out var hostPage)
            || hostPage == 0
            || hostPage == unmappedSentinelAddress)
        {
            return false;
        }

        if (!writer.TryExchangeInt32(hostPage + (ulong)pageOffset, value))
        {
            return false;
        }

        // The mapping must not have moved under the write, and the guest must now
        // read back what was asked for. Either failing means the write did not
        // land where the game will look for it.
        if (!memory.TryReadUInt64(pageEntryAddress, out var hostPageAfter)
            || hostPageAfter != hostPage)
        {
            return false;
        }

        Span<byte> verification = stackalloc byte[sizeof(int)];
        return TryRead(virtualAddress, verification)
            && BitConverter.ToInt32(verification) == value;
    }

    public bool HasExpectedResolverSignature()
    {
        Span<byte> first = stackalloc byte[ResolverSignature.Length];
        Span<byte> second = stackalloc byte[ResolverSignature.Length];
        var resolverAddress = moduleBase + ResolverRva;
        return memory.TryRead(resolverAddress, first)
               && memory.TryRead(resolverAddress, second)
               && first.SequenceEqual(second)
               && first.SequenceEqual(ResolverSignature);
    }

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        destination.Clear();
        if (virtualAddress == 0)
        {
            return false;
        }

        if (destination.IsEmpty)
        {
            return true;
        }

        var endExclusive = (ulong)virtualAddress + (ulong)destination.Length;
        if (endExclusive > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        var firstPageIndex = (ulong)virtualAddress >> 12;
        var lastPageIndex = (endExclusive - 1) >> 12;
        var mappedPageCount = checked((int)(lastPageIndex - firstPageIndex + 1));
        var rentedPages = ArrayPool<ulong>.Shared.Rent(mappedPageCount);
        var mappedPages = rentedPages.AsSpan(0, mappedPageCount);

        try
        {
            var currentAddress = (ulong)virtualAddress;
            var destinationOffset = 0;
            var mappedPageOffset = 0;
            while (destinationOffset < destination.Length)
            {
                var pageIndex = currentAddress >> 12;
                var pageOffset = (int)(currentAddress & (PageSize - 1));
                var pageEntryAddress = pageTableAddress + (pageIndex * sizeof(ulong));
                if (!HasTrustedPageTableEntryRegion(pageEntryAddress)
                    || !memory.TryReadUInt64(pageEntryAddress, out var hostPage)
                    || hostPage == 0
                    || hostPage == unmappedSentinelAddress)
                {
                    destination.Clear();
                    return false;
                }

                // The game's mapper obtains each backing buffer from its heap
                // allocator and stores buffer + 0x1000*n in the page table.
                // Those bases are allocator-aligned, not necessarily 4K-aligned.
                mappedPages[mappedPageOffset++] = hostPage;
                var chunkLength = Math.Min(PageSize - pageOffset, destination.Length - destinationOffset);
                if (hostPage > ulong.MaxValue - (uint)pageOffset
                    || !memory.TryRead(
                        hostPage + (uint)pageOffset,
                        destination.Slice(destinationOffset, chunkLength)))
                {
                    destination.Clear();
                    return false;
                }

                destinationOffset += chunkLength;
                currentAddress += (uint)chunkLength;
            }

            for (var index = 0; index < mappedPageCount; index++)
            {
                var pageEntryAddress = pageTableAddress
                                       + ((firstPageIndex + (ulong)index) * sizeof(ulong));
                if (!HasTrustedPageTableEntryRegion(pageEntryAddress)
                    || !memory.TryReadUInt64(pageEntryAddress, out var currentHostPage)
                    || currentHostPage != mappedPages[index])
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(rentedPages, clearArray: true);
        }
    }

    private bool HasTrustedPageTableEntryRegion(ulong pageEntryAddress)
    {
        if (pageEntryAddress < pageTableAddress
            || pageEntryAddress > pageTableEndExclusive - sizeof(ulong)
            || !memory.TryQueryRegion(pageEntryAddress, out var region)
            || !region.IsCommitted
            || !region.IsReadable
            || !region.IsImage
            || region.AllocationBase != moduleBase
            || region.Size == 0
            || pageEntryAddress < region.BaseAddress
            || region.BaseAddress > ulong.MaxValue - (region.Size - 1))
        {
            return false;
        }

        var regionEnd = region.BaseAddress + region.Size - 1;
        return pageEntryAddress <= ulong.MaxValue - (sizeof(ulong) - 1)
               && pageEntryAddress + sizeof(ulong) - 1 <= regionEnd;
    }

    public bool TryReadByte(uint virtualAddress, out byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        var success = TryRead(virtualAddress, buffer);
        value = success ? buffer[0] : (byte)0;
        return success;
    }

    public bool TryReadUInt16(uint virtualAddress, out ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        var success = TryRead(virtualAddress, buffer);
        value = success ? BinaryPrimitives.ReadUInt16LittleEndian(buffer) : (ushort)0;
        return success;
    }

    public bool TryReadInt16(uint virtualAddress, out short value)
    {
        var success = TryReadUInt16(virtualAddress, out var raw);
        value = success ? unchecked((short)raw) : (short)0;
        return success;
    }

    public bool TryReadUInt32(uint virtualAddress, out uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        var success = TryRead(virtualAddress, buffer);
        value = success ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : 0;
        return success;
    }

    public bool TryReadInt32(uint virtualAddress, out int value)
    {
        var success = TryReadUInt32(virtualAddress, out var raw);
        value = success ? unchecked((int)raw) : 0;
        return success;
    }

    public bool TryReadSingle(uint virtualAddress, out float value)
    {
        var success = TryReadInt32(virtualAddress, out var raw);
        value = success ? BitConverter.Int32BitsToSingle(raw) : 0.0f;
        return success;
    }
}
