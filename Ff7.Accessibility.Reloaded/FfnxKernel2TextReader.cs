using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the selected kernel2 entry from a verified installed FFNx loader.
/// PDB and Ghidra prove the 20 heap section pointers and getter/allocator/reset
/// lifecycle. No game function is invoked, and no text bank is enumerated.
/// DLL addresses are native x86 addresses, never translated Steam guest addresses.
/// </summary>
internal sealed class FfnxKernel2TextReader
{
    private const string Ffnx1243Hash = "7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A";
    private const string Ffnx1252Hash = "9C9859B1F66EE3A25D29BD5FC43171A3C298F4372A6AEE589808E8EC7431C77B";
    private const uint OriginalGetter = 0x00419457;
    private const int MaximumTextLength = 512;
    private static readonly int[] Counts = [32,256,128,128,32,32,96,64,32,256,128,128,32,32,96,64,128,16];
    // Full function hashes with only PE HIGHLOW absolute-address relocations
    // normalized to RVA. Function bytes remain in the user's licensed driver.
    private static readonly ModuleLayout Ffnx1243 = new("1.24.3", 0x021118D8, 0x02111928,
    [
        (0x003BED80, 70, [5,27,37,43,48], "86EE7A2FB83CFC5F65EE8CAEC42208F4DCB557B6C207CEC218DF6E1D2E6DDBC5"),
        (0x003BED20, 91, [4,12,28,40,45,51,56,69,76,82], "9844CFB2116B4CA168CE94EF11BB226D71E6963A9016C35A2F1899D68C1D732D"),
        (0x003BEDD0, 100, [2,10,16,21,38,51,58,64,74,82,94], "0E0344E078CD03FAD2C6FACC9ACD3CDCE72F81B9A7D7BAFBB6EB1D3FB3075902")
    ]);
    private static readonly ModuleLayout Ffnx1252 = new("1.25.2", 0x0283AC98, 0x0283ACE8,
    [
        (0x0044DE60, 70, [5,27,37,43,48], "CE4685AFF5183F610962DAD6D162DC5CD83E6F0066BBEF6EDEF055E4768F795C"),
        (0x0044DE00, 91, [4,12,28,40,45,51,56,69,76,82], "7F549EE0DF0BB2C5076EAC0A781E005DA04C60E65AB1A5002E963C13642E45FF"),
        (0x0044DEB0, 100, [2,10,16,21,38,51,58,64,74,82,94], "DEB0F455369A8D2891411385349675451B35F719D9498BD13AD37F2FB66BB167")
    ]);

    private readonly ModuleLayout layout;
    private readonly uint moduleBase;
    private readonly ILegacyAddressSpace memory;
    private readonly Func<uint, int, bool, bool> isCommittedRegion;

    private FfnxKernel2TextReader(ModuleLayout layout, uint moduleBase, ILegacyAddressSpace memory,
        Func<uint, int, bool, bool> isCommittedRegion)
    {
        this.layout = layout;
        this.moduleBase = moduleBase;
        this.memory = memory;
        this.isCommittedRegion = isCommittedRegion;
    }

    internal static bool TryCreate(Process process, ILegacyAddressSpace memory,
        out FfnxKernel2TextReader? reader, out string diagnostic)
    {
        reader = null;
        diagnostic = "Supported native FFNx kernel text owner is unavailable.";
        try
        {
            if (Environment.Is64BitProcess || process.Id != Environment.ProcessId || memory is null) return false;
            foreach (ProcessModule module in process.Modules)
            {
                if (!FfnxRuntimeDetector.IsFfnxModule(module.ModuleName,
                    FileVersionInfo.GetVersionInfo(module.FileName).ProductName)) continue;
                using var stream = File.OpenRead(module.FileName);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                var nativeBase = (ulong)(nuint)module.BaseAddress;
                if (nativeBase > uint.MaxValue) return false;
                var base32 = (uint)nativeBase;
                var imageSize = module.ModuleMemorySize;
                return TryCreateVerifiedModule(base32, imageSize, hash, memory,
                    (address, length, code) => IsNativeCommittedRegion(address, length, code, base32, imageSize),
                    out reader, out diagnostic, allowPendingOwnership: true);
            }
        }
        catch (Exception ex) { diagnostic = "FFNx kernel text identity unavailable: " + ex.Message; }
        return false;
    }

    internal static bool TryCreateVerifiedModule(uint moduleBase, int imageSize, string moduleSha256,
        ILegacyAddressSpace memory, Func<uint, int, bool, bool> isCommittedRegion,
        out FfnxKernel2TextReader? reader, out string diagnostic, bool allowPendingOwnership = false)
    {
        reader = null;
        diagnostic = "Exact FFNx kernel text cohort and getter ownership are unavailable.";
        var layout = moduleSha256?.ToUpperInvariant() switch
        {
            Ffnx1243Hash => Ffnx1243,
            Ffnx1252Hash => Ffnx1252,
            _ => null
        };
        if (layout is null || memory is null || isCommittedRegion is null ||
            moduleBase < 0x10000 || (moduleBase & 4095) != 0 ||
            imageSize < layout.CounterRva + 4 || (ulong)moduleBase + (uint)imageSize > 0x80000000UL) return false;
        var candidate = new FfnxKernel2TextReader(layout, moduleBase, memory, isCommittedRegion);
        // DLL code is available before FFNx installs its main-executable hook.
        // Keep the verified candidate, but never read its heap without ownership.
        if (!candidate.MatchesCohort() || (!allowPendingOwnership && !candidate.HasCurrentOwnership)) return false;
        reader = candidate;
        diagnostic = candidate.HasCurrentOwnership
            ? $"Verified FFNx {layout.Version} heap kernel text owner; selected entries remain individually checked."
            : $"Verified FFNx {layout.Version} kernel text loader; waiting for its native getter hook.";
        return true;
    }

    internal bool HasCurrentOwnership
    {
        get
        {
            try { return MatchesHook() && MatchesCohort() && MatchesHook(); }
            catch { return false; }
        }
    }

    internal byte[]? ReadEncoded(int bank, int id)
    {
        try
        {
            if (bank is < 0 or >= 18 || id < 0 || id >= Counts[bank] || !HasCurrentOwnership ||
                !TryCapture(bank, id, out var first) || !TryCapture(bank, id, out var second) ||
                first.Counter != second.Counter || first.Section != second.Section ||
                first.Header != second.Header || first.Offset != second.Offset ||
                !first.Bytes.AsSpan().SequenceEqual(second.Bytes) || !HasCurrentOwnership ||
                !MatchesOwnerBookend(bank, id, second)) return null;
            return first.Bytes;
        }
        catch { return null; }
    }

    private bool TryCapture(int bank, int id, out Candidate candidate)
    {
        candidate = default;
        if (!TryReadWord32(moduleBase + layout.CounterRva, out var count) || count is < 18 or > 20 ||
            !TryReadWord32(moduleBase + layout.SectionsRva + (uint)bank * 4, out var section) ||
            section is < 0x10000 or >= 0x80000000 || (section & 3) != 0 ||
            !TryReadWord16(section, out var header) || header != Counts[bank] * 2 ||
            !TryReadWord16(section + (uint)id * 2, out var offset) || offset < header) return false;
        var available = Math.Min(MaximumTextLength, 65536 - offset);
        var bytes = new List<byte>(available);
        Span<byte> next = stackalloc byte[1];
        for (var index = 0; index < available; index++)
        {
            if (!Read(section + offset + (uint)index, next)) return false;
            bytes.Add(next[0]);
            if (next[0] != 255) continue;
            candidate = new(count, section, header, offset, bytes.ToArray());
            return true;
        }
        return false;
    }

    private bool MatchesOwnerBookend(int bank, int id, Candidate candidate) =>
        TryReadWord32(moduleBase + layout.CounterRva, out var count) && count == candidate.Counter &&
        TryReadWord32(moduleBase + layout.SectionsRva + (uint)bank * 4, out var section) && section == candidate.Section &&
        TryReadWord16(section, out var header) && header == candidate.Header &&
        TryReadWord16(section + (uint)id * 2, out var offset) && offset == candidate.Offset && HasCurrentOwnership;

    private bool MatchesHook()
    {
        Span<byte> first = stackalloc byte[5]; Span<byte> second = stackalloc byte[5];
        if (!isCommittedRegion(OriginalGetter, 5, true) || !memory.TryRead(OriginalGetter, first) ||
            !memory.TryRead(OriginalGetter, second) || !first.SequenceEqual(second) || first[0] != 0xE9) return false;
        var target = (long)OriginalGetter + 5 + BinaryPrimitives.ReadInt32LittleEndian(first[1..]);
        return target == (long)moduleBase + layout.Cohort[0].Rva;
    }

    private bool MatchesCohort()
    {
        foreach (var (rva, length, relocations, hash) in layout.Cohort)
        {
            var first = new byte[length]; var second = new byte[length]; var address = moduleBase + rva;
            if (!isCommittedRegion(address, length, true) || !memory.TryRead(address, first) ||
                !memory.TryRead(address, second) || !first.AsSpan().SequenceEqual(second)) return false;
            foreach (var relocation in relocations)
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(relocation, 4));
                if (value < moduleBase) return false;
                BinaryPrimitives.WriteUInt32LittleEndian(first.AsSpan(relocation, 4), value - moduleBase);
            }
            if (!SHA256.HashData(first).AsSpan().SequenceEqual(Convert.FromHexString(hash))) return false;
        }
        return true;
    }

    private bool Read(uint address, Span<byte> destination) => address >= 0x10000 &&
        (ulong)address + (uint)destination.Length <= 0x80000000UL &&
        isCommittedRegion(address, destination.Length, false) && memory.TryRead(address, destination);
    private bool TryReadWord32(uint address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4]; value = 0;
        if (!Read(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
    private bool TryReadWord16(uint address, out ushort value)
    {
        Span<byte> bytes = stackalloc byte[2]; value = 0;
        if (!Read(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt16LittleEndian(bytes); return true;
    }
    private readonly record struct Candidate(uint Counter, uint Section, ushort Header, ushort Offset, byte[] Bytes);

    private sealed record ModuleLayout(string Version, uint SectionsRva, uint CounterRva,
        (uint Rva, int Length, int[] Relocations, string Hash)[] Cohort);

    private static bool IsNativeCommittedRegion(uint address, int length, bool code, uint moduleBase, int imageSize)
    {
        if (length <= 0 || VirtualQuery((nint)address, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0 ||
            region.State != 0x1000 || (region.Protection & 0x101) != 0) return false;
        var start = (ulong)region.BaseAddress; var size = (ulong)region.RegionSize;
        if (start > address || size < (ulong)length || address - start > size - (ulong)length) return false;
        var protection = region.Protection & 255;
        var inModule = address >= moduleBase && (ulong)address + (uint)length <= (ulong)moduleBase + (uint)imageSize;
        if (code) return region.Type == 0x1000000 && protection is 0x20 or 0x40 or 0x80 &&
            (inModule && (ulong)region.AllocationBase == moduleBase || address == OriginalGetter && (ulong)region.AllocationBase == 0x00400000);
        if (inModule) return region.Type == 0x1000000 && (ulong)region.AllocationBase == moduleBase && protection is 0x02 or 0x04 or 0x08;
        return region.Type == 0x20000 && protection is 0x04 or 0x08;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    { public nuint BaseAddress, AllocationBase; public uint AllocationProtection; public nuint RegionSize; public uint State, Protection, Type; }
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryRegion region, nuint length);
}
