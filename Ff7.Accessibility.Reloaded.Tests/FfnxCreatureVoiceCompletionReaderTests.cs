using System.Buffers.Binary;
using System.Reflection;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FfnxCreatureVoiceCompletionReaderTests
{
    public static void Run()
    {
        var type = typeof(CutsceneVoicePlayer).Assembly.GetType("Ff7.Accessibility.Reloaded.FfnxCreatureVoiceCompletionReader");
        Check(type is not null, "missing required FFNx creature voice completion reader");
        var memory = new Memory();
        var reader = Create(type!, memory);
        memory.U32(Memory.Entry - 4, 0);
        memory.U32(Memory.Entry + 4, 0);
        Equal(false, Finished(reader), "a valid unpaused warning voice is still playing");
        Equal(false, Finished(reader), "cleared adjacent slots cannot turn the current assigned voice into finished");
        memory.U32(Memory.Voice + 0x10, 4);
        Equal(null, Finished(reader), "paused audio is not completed dialogue");
        memory.U32(Memory.Voice + 0x10, 0);
        memory.U32(Memory.Voice + 8, 2);
        Equal(true, Finished(reader), "an assigned ordinary handle with an invalid generation is finished");
        memory.U32(Memory.Entry, 0);
        Equal(true, Finished(reader), "a coherent cleared voice entry is finished");
        memory.U32(Memory.VoiceNode + 0x14, 0xFFFFF000);
        Equal(null, Finished(reader), "an unassigned FFNx sentinel is not a heard voice");
        memory.U32(Memory.VoiceNode + 0x14, 0);
        Equal(null, Finished(reader), "a zero handle is unknown, not finished");
        memory.U32(Memory.VoiceNode + 0x14, Memory.Handle);
        Equal(null, Finished(reader, 0), "only the reviewed warning window1 is supported");
        memory.U32(Memory.VoiceNode + 0x10, 2);
        Equal(null, Finished(reader), "a different window slot does not authorize narration");
        memory.U32(Memory.VoiceNode + 0x10, 1);
        memory.U32(Memory.VoiceHead + 4, Memory.VoiceHead);
        Equal(null, Finished(reader), "a missing slot is not finished");
        memory.U32(Memory.VoiceHead + 4, Memory.VoiceNode);
        memory.U32(Memory.VoiceNode + 0x10, 0);
        memory.U32(Memory.VoiceNode + 8, Memory.VoiceNode);
        Equal(null, Finished(reader), "cyclic map traversal is bounded and unknown");
        memory.U32(Memory.VoiceNode + 0x10, 1);
        memory.U32(Memory.VoiceNode + 8, Memory.VoiceHead);
        memory.U32(Memory.Entry, Memory.Voice);
        memory.U32(Memory.Voice + 8, 2);
        memory.ChangeOnRead = Memory.Entry;
        Equal(null, Finished(reader), "a replacement during observation cannot report finished");
        memory.ChangeOnRead = null;
        memory.U32(Memory.Entry, Memory.Voice);
        memory.StatusActing(1);
        Equal(null, Finished(reader), "FFNx's still-active lifecycle is not committed finished");
        memory.StatusActing(0);
        memory.Denied = Memory.VoiceNode;
        Equal(null, Finished(reader), "unreadable slot is unknown");
        memory.Denied = null;
        memory.ThrowReads = true;
        Equal(null, Finished(reader), "native read exceptions fail closed");
        memory.ThrowReads = false;
        memory.CodeChanged = true;
        Equal(null, Finished(reader), "a changed native cohort invalidates an existing reader");
        Check(!TryCreate(type!, memory, Memory.Hash, out _), "altered native cohort rejects creation");
        memory.CodeChanged = false;
        Check(!TryCreate(type!, memory, new string('0', 64), out _), "unknown DLL fingerprint rejects creation");
        memory.RegionValid = false;
        Equal(null, Finished(reader), "lost native commitment invalidates an existing reader");
        Check(!TryCreate(type!, memory, Memory.Hash, out _), "uncommitted or wrong protection rejects creation");
        memory.RegionValid = true;
        memory.StateRegionValid = false;
        Check(!TryCreate(type!, memory, Memory.Hash, out _), "uncommitted native state rejects creation");
        memory.StateRegionValid = true;
        Check(!TryCreate(type!, memory, Memory.Hash, out _, uint.MaxValue - 10), "native address overflow rejects creation");
        Check(memory.CodeRegionCalls > 0 && memory.StateRegionCalls > 0,
            "the factory checks committed code and state separately");
        Console.WriteLine("FFNx warning voice assigned-slot/generation/pause/coherence and factory guards passed.");
    }

    private static object Create(Type type, Memory memory)
    {
        Check(TryCreate(type, memory, Memory.Hash, out var reader), "verified fixture creation");
        return reader!;
    }
    private static bool TryCreate(Type type, Memory memory, string hash, out object? reader,
        uint moduleBase = Memory.Base)
    {
        var factory = type.GetMethod("TryCreateVerifiedModule", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [moduleBase, Memory.ImageSize, hash, memory,
            (Func<uint, int, bool, bool>)memory.Region, null, null];
        var result = (bool)factory.Invoke(null, args)!;
        reader = args[5];
        return result;
    }
    private static bool? Finished(object reader, int window = 1) =>
        (bool?)reader.GetType().GetMethod("IsWarningVoiceFinished", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(reader, [window]);
    private static void Equal(bool? expected, bool? actual, string label) =>
        Check(expected == actual, $"{label}: expected {expected?.ToString() ?? "unknown"}, got {actual?.ToString() ?? "unknown"}");
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        internal const uint Base = 0x10000000;
        internal const int ImageSize = 0x02200000;
        internal const string Hash = "7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A";
        internal const uint Handle = 0x1001;
        internal const uint VoiceHead = 0x03000000, VoiceNode = 0x03000100, Voice = 0x03000200;
        internal const uint StatusHead = 0x03000300, StatusNode = 0x03000400;
        internal const uint Entry = Base + 0x0209A8F0 + 8 + 0x4C + 4;
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, string> code = new()
        {
            [Base + 0x003766A0] = "558BEC568BF18D450857508D8E000E0700E82AC3FFFF8D4E08",
            [Base + 0x015FD050] = "558BEC568B75088BC62500F0FFFF578BF93D00F0FFFF7450",
            [Base + 0x015FCBD0] = "558BEC56578BF9E814BCFFFF8B75088BCF56E889210000",
        };
        internal bool CodeChanged, RegionValid = true, StateRegionValid = true, ThrowReads;
        internal uint? Denied, ChangeOnRead;
        internal int CodeRegionCalls, StateRegionCalls;
        internal Memory()
        {
            foreach (var (address, hex) in code) Write(address, Convert.FromHexString(hex));
            U32(Base + 0x0210B6F0, VoiceHead); U32(Base + 0x0210B6F4, 1);
            Tree(VoiceHead, VoiceNode); Node(VoiceNode, VoiceHead, 1);
            U32(VoiceNode + 0x14, Handle);
            U32(Base + 0x02115328, StatusHead); U32(Base + 0x0211532C, 1);
            Tree(StatusHead, StatusNode); Node(StatusNode, StatusHead, 1);
            Write(StatusNode + 0x14, new byte[0x34]);
            U32(StatusNode + 0x14, 2u << 16);
            StatusActing(0); Write(Voice, new byte[0x14]); U32(Entry, Voice); U32(Voice + 8, 1); U32(Voice + 0x10, 0);
        }
        private void Tree(uint head, uint root)
        {
            Write(head, new byte[0x14]); U32(head, root); U32(head + 4, root); U32(head + 8, root);
            bytes[head + 0xD] = 1;
        }
        private void Node(uint node, uint parent, uint key)
        {
            Write(node, new byte[0x20]); U32(node, parent); U32(node + 4, parent); U32(node + 8, parent);
            U32(node + 0x10, key);
        }
        internal void StatusActing(byte acting) => bytes[StatusNode + 0x18] = acting;
        internal void U32(uint address, uint value)
        {
            Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value); Write(address, data);
        }
        private void Write(uint address, ReadOnlySpan<byte> data)
        { for (var i = 0; i < data.Length; i++) bytes[address + (uint)i] = data[i]; }
        internal bool Region(uint address, int length, bool codeRegion)
        {
            if (codeRegion) CodeRegionCalls++; else StateRegionCalls++;
            return RegionValid && (codeRegion || StateRegionValid);
        }
        public bool TryRead(uint address, Span<byte> target)
        {
            if (ThrowReads) throw new InvalidOperationException("fixture unavailable");
            if (Denied is { } denied && address <= denied && (ulong)address + (uint)target.Length > denied) return false;
            for (var i = 0; i < target.Length; i++)
            {
                if (!bytes.TryGetValue(address + (uint)i, out target[i])) return false;
            }
            if (CodeChanged && code.ContainsKey(address)) target[0] ^= 1;
            if (ChangeOnRead == address) U32(address,
                BinaryPrimitives.ReadUInt32LittleEndian(target) == 0 ? Voice : 0);
            return true;
        }
    }
}
