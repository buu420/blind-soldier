using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Hash-bound, read-only observation of FFNx's window1 voice for the field767 warning.
/// A caller must separately prove the current MESSAGE/page and successful playback event.
/// No native audio method is called: isVoicePlaying uses inserting map lookups and treats
/// a paused handle as inactive, neither of which establishes completed dialogue.
/// </summary>
internal sealed class FfnxCreatureVoiceCompletionReader
{
    internal const string SupportedModuleSha256 = "7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A";
    internal const uint AudioEngineRva = 0x0209A8F0;
    internal const uint VoiceSlotsRva = 0x0210B6F0;
    internal const uint MessageStatusRva = 0x02115328;
    private const int MaximumMapNodes = 32;
    private static readonly (uint Rva, byte[] Prefix)[] cohort =
    [
        (0x003766A0, Convert.FromHexString("558BEC568BF18D450857508D8E000E0700E82AC3FFFF8D4E08")),
        (0x015FD050, Convert.FromHexString("558BEC568B75088BC62500F0FFFF578BF93D00F0FFFF7450")),
        (0x015FCBD0, Convert.FromHexString("558BEC56578BF9E814BCFFFF8B75088BCF56E889210000")),
    ];
    private readonly uint moduleBase;
    private readonly ILegacyAddressSpace memory;
    private readonly Func<uint, int, bool, bool> isCommittedRegion;

    private FfnxCreatureVoiceCompletionReader(uint moduleBase, ILegacyAddressSpace memory,
        Func<uint, int, bool, bool> isCommittedRegion)
    {
        this.moduleBase = moduleBase;
        this.memory = memory;
        this.isCommittedRegion = isCommittedRegion;
    }

    internal static bool TryCreate(Process process, ILegacyAddressSpace memory,
        out FfnxCreatureVoiceCompletionReader? reader, out string diagnostic)
    {
        reader = null;
        diagnostic = "The supported native FFNx voice module is unavailable.";
        try
        {
            // These are native DLL addresses, never translated x86 guest addresses.
            if (Environment.Is64BitProcess || process.Id != Environment.ProcessId || memory is null) return false;
            foreach (ProcessModule module in process.Modules)
            {
                var candidate = module.ModuleName.Equals("AF3DN.P", StringComparison.OrdinalIgnoreCase)
                    || module.ModuleName.Equals("7H_GameDriver.dll", StringComparison.OrdinalIgnoreCase)
                    || module.ModuleName.Equals("FFNx.dll", StringComparison.OrdinalIgnoreCase);
                if (!candidate || !string.Equals(FileVersionInfo.GetVersionInfo(module.FileName).ProductName,
                        "FFNx", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = File.OpenRead(module.FileName);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                var imageBase = (ulong)(nuint)module.BaseAddress;
                if (imageBase > uint.MaxValue) return false;
                var base32 = (uint)imageBase;
                var imageSize = module.ModuleMemorySize;
                return TryCreateVerifiedModule(base32, imageSize, hash, memory,
                    (address, length, code) => IsNativeCommittedRegion(address, length, code, base32, imageSize),
                    out reader, out diagnostic);
            }
        }
        catch (Exception ex) { diagnostic = "FFNx warning voice identity is unavailable: " + ex.Message; }
        return false;
    }

    // Injectable memory/region boundary makes native pointer failure and replacement
    // tests possible without loading FFNx or issuing a native callable in the test host.
    internal static bool TryCreateVerifiedModule(uint moduleBase, int imageSize, string moduleSha256,
        ILegacyAddressSpace memory, Func<uint, int, bool, bool> isCommittedRegion,
        out FfnxCreatureVoiceCompletionReader? reader, out string diagnostic)
    {
        reader = null;
        diagnostic = "The exact FFNx warning voice cohort or committed state is unavailable.";
        if (memory is null || isCommittedRegion is null || moduleBase < 0x10000 || (moduleBase & 0xFFF) != 0
            || imageSize < MessageStatusRva + 8 || (ulong)moduleBase + (uint)imageSize > 0x80000000UL
            || !string.Equals(moduleSha256, SupportedModuleSha256, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var candidate = new FfnxCreatureVoiceCompletionReader(moduleBase, memory, isCommittedRegion);
            Span<byte> state = stackalloc byte[8];
            if (!candidate.MatchesNativeCohort()
                || !candidate.Read(moduleBase + VoiceSlotsRva, state)
                || !candidate.Read(moduleBase + MessageStatusRva, state)) return false;
            reader = candidate;
            diagnostic = "Verified read-only FFNx window1 voice handle and native validity cohort.";
            return true;
        }
        catch { return false; }
    }

    /// <returns>
    /// True only for a coherent assigned ordinary handle that has no matching native
    /// voice generation and whose FFNx lifecycle has settled inactive. False is a
    /// valid unpaused voice. Null includes absent slots, sentinels, pause, changed
    /// ownership, failed reads and unsupported windows. This does not authorize
    /// narration without the caller's exact current message/playback proof.
    /// </returns>
    internal bool? IsWarningVoiceFinished(int windowId)
    {
        if (windowId != 1) return null;
        try
        {
            if (!MatchesNativeCohort()
                || !TryObserve(windowId, out var first)
                || !TryObserve(windowId, out var second)
                || first.Result != second.Result
                || !first.Evidence.AsSpan().SequenceEqual(second.Evidence)
                || !MatchesNativeCohort()) return null;
            return second.Result;
        }
        catch { return null; }
    }

    private bool TryObserve(int windowId, out Observation observation)
    {
        observation = default;
        var evidence = new List<byte>();
        if (!TryFindSlot(moduleBase + VoiceSlotsRva, windowId, 0x20, evidence, out var voiceSlot)
            || !TryFindSlot(moduleBase + MessageStatusRva, windowId, 0x48, evidence, out var status)) return false;
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(voiceSlot.AsSpan(0x14));
        var acting = status[0x18];
        // FFNx's unused voice slot sentinel is not a completed successful play.
        var index = handle & 0xFFF;
        if (acting > 1 || handle == 0 || index == 0 || (handle & 0xFFFFF000) == 0xFFFFF000) return false;
        // Native getVoiceFromHandle_internal uses mVoice at +0x50 and index-1.
        // This equivalent +0x4C/index form is also in installed isValidVoiceHandle.
        var entry = moduleBase + AudioEngineRva + 8 + 0x50 + (index - 1) * 4;
        if (!ReadEvidence(entry, 4, evidence, out var pointerBytes)) return false;
        var voicePointer = BinaryPrimitives.ReadUInt32LittleEndian(pointerBytes);
        bool? result;
        if (voicePointer == 0)
        {
            result = acting == 0 ? true : null;
        }
        else
        {
            if (!IsPointer(voicePointer) || !ReadEvidence(voicePointer + 8, 12, evidence, out var voiceState)) return false;
            var generation = BinaryPrimitives.ReadUInt32LittleEndian(voiceState) & 0xFFFFF;
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(voiceState.AsSpan(8));
            result = generation != handle >> 12 ? acting == 0 ? true : null
                : (flags & 4) != 0 ? null : false;
        }
        observation = new(result, evidence.ToArray());
        return true;
    }

    private bool TryFindSlot(uint map, int windowId, int nodeLength, List<byte> evidence, out byte[] node)
    {
        node = [];
        if (!ReadEvidence(map, 8, evidence, out var header)) return false;
        var head = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (size is 0 or > MaximumMapNodes || !IsPointer(head)
            || !ReadEvidence(head, 0x14, evidence, out var sentinel) || sentinel[0xD] != 1) return false;
        var current = BinaryPrimitives.ReadUInt32LittleEndian(sentinel.AsSpan(4));
        var parent = head;
        var visited = new HashSet<uint>();
        while (current != head && visited.Count < size && visited.Count < MaximumMapNodes)
        {
            if (!IsPointer(current) || !visited.Add(current)
                || !ReadEvidence(current, nodeLength, evidence, out var candidate)
                || candidate[0xD] != 0 || candidate[0xC] > 1
                || BinaryPrimitives.ReadUInt32LittleEndian(candidate.AsSpan(4)) != parent) return false;
            var key = BinaryPrimitives.ReadInt32LittleEndian(candidate.AsSpan(0x10));
            if (key is < 0 or > 9) return false;
            if (key == windowId) { node = candidate; return true; }
            parent = current;
            current = BinaryPrimitives.ReadUInt32LittleEndian(candidate.AsSpan(windowId < key ? 0 : 8));
        }
        return false;
    }

    private bool ReadEvidence(uint address, int length, List<byte> evidence, out byte[] bytes)
    {
        bytes = new byte[length];
        if (!Read(address, bytes)) return false;
        Span<byte> location = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(location, address);
        evidence.AddRange(location.ToArray());
        evidence.AddRange(bytes);
        return true;
    }
    private bool Read(uint address, Span<byte> bytes) => IsPointer(address)
        && (ulong)address + (uint)bytes.Length <= 0x80000000UL
        && isCommittedRegion(address, bytes.Length, false) && memory.TryRead(address, bytes);
    private static bool IsPointer(uint value) => value is >= 0x10000 and < 0x80000000 && (value & 3) == 0;
    private bool MatchesNativeCohort()
    {
        Span<byte> firstBuffer = stackalloc byte[32];
        Span<byte> secondBuffer = stackalloc byte[32];
        foreach (var (rva, prefix) in cohort)
        {
            var address = moduleBase + rva;
            var first = firstBuffer[..prefix.Length];
            var second = secondBuffer[..prefix.Length];
            if (!isCommittedRegion(address, prefix.Length, true)
                || !memory.TryRead(address, first) || !memory.TryRead(address, second)
                || !first.SequenceEqual(second) || !first.SequenceEqual(prefix)) return false;
        }
        return true;
    }

    private readonly record struct Observation(bool? Result, byte[] Evidence);
    private static bool IsNativeCommittedRegion(uint address, int length, bool code,
        uint moduleBase, int imageSize)
    {
        if (length <= 0 || VirtualQuery((nint)address, out var region,
                (nuint)Marshal.SizeOf<MemoryRegion>()) == 0 || region.State != 0x1000
            || (region.Protection & 0x101) != 0) return false;
        var start = (ulong)region.BaseAddress;
        var size = (ulong)region.RegionSize;
        if (start > address || size < (ulong)length || address - start > size - (ulong)length) return false;
        var protection = region.Protection & 0xFF;
        var inModule = address >= moduleBase && (ulong)address + (uint)length <= (ulong)moduleBase + (uint)imageSize;
        if (code) return inModule && region.Type == 0x1000000 && (ulong)region.AllocationBase == moduleBase
            && protection is 0x20 or 0x40 or 0x80;
        if (inModule) return region.Type == 0x1000000 && (ulong)region.AllocationBase == moduleBase
            && protection is 0x02 or 0x04 or 0x08;
        return region.Type == 0x20000 && protection is 0x04 or 0x08;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    {
        public nuint BaseAddress, AllocationBase; public uint AllocationProtection;
        public nuint RegionSize; public uint State, Protection, Type;
    }
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryRegion region, nuint length);
}
