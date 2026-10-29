using System.Buffers.Binary;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// A loaded field script, identified by its canonical prefix: the bytes as loaded, with the
/// field engine's own KAWAI packet writes undone (see <see cref="NativeFieldScriptPrefixCanonicalizer"/>).
/// It stays the same while the engine writes into the running script.
/// </summary>
public readonly record struct LoadedFieldScriptIdentity(
    int FieldId,
    string ScriptPrefixSha256)
{
    // The public observation is an identity, not an address into native memory.
    // Legacy lifecycle consumers retain the pointer within the runtime assembly.
    internal uint ScriptPointer { get; init; }

    internal LoadedFieldScriptIdentity(int fieldId, uint scriptPointer, string scriptPrefixSha256)
        : this(fieldId, scriptPrefixSha256) => ScriptPointer = scriptPointer;
}

/// <summary>
/// What a canonical identity undid, for diagnostics only: the actual fingerprint of the loaded
/// bytes, the native writes restored, and the native-writable bytes holding a value the engine
/// could not have written there (left as loaded).
/// </summary>
public readonly record struct LoadedFieldScriptPrefixNormalization
{
    private readonly string? rawScriptPrefixSha256;
    private readonly IReadOnlyList<int>? restoredOffsets;

    public LoadedFieldScriptPrefixNormalization(string rawScriptPrefixSha256, IReadOnlyList<int> restoredOffsets,
        int unexplainedNativeWrites)
    {
        this.rawScriptPrefixSha256 = rawScriptPrefixSha256;
        this.restoredOffsets = restoredOffsets;
        UnexplainedNativeWrites = unexplainedNativeWrites;
    }

    public string RawScriptPrefixSha256 => rawScriptPrefixSha256 ?? string.Empty;

    /// <summary>Prefix offsets of each restored packet[1], ascending.</summary>
    public IReadOnlyList<int> RestoredOffsets => restoredOffsets ?? [];

    public int UnexplainedNativeWrites { get; }
}

/// <summary>A KAWAI setup packet the engine can run, and the entity that runs it.</summary>
internal readonly record struct NativeKawaiWriteSite(int PacketOffset, byte SubOpcode, int Owner)
{
    /// <summary>packet[1]: KAWAI caches script + pc + 3 as the packet.</summary>
    public int ValueOffset => PacketOffset + 4;
}

/// <summary>
/// The only bytes the field engine writes below the text offset of a loaded script.
///
/// <para>KAWAI (00620136) stores a pointer to its packet (script + pc + 3) in the executing
/// model's state (00CC1670 + slot * 0x88 + 4). Each frame 006392BB hands a pending packet to
/// 0064A070, whose LIGHT (6), 7 and 8/9 cases store the model slot in packet[1] when packet[0]
/// is 0 (0064A259, 0064A487, 0064A5F9), before 0064F420, 0064EFC0 or 00645150 read it as that
/// slot. The slot is 00CBFB70[entity], which CHAR (00614353) assigns from a load-order counter,
/// not from its operand.</para>
///
/// <para>A write site is such a packet in code that exactly one entity's slots can run, with that
/// entity as owner when it runs exactly one CHAR (so its slot cannot change after a write) and
/// no other reachable instruction decodes packet[1]. A byte is restored only when it holds that
/// owner's live slot; any other value stays as loaded, so a script the engine did not produce
/// keeps an identity no review matches. Code no slot reaches is never a site.</para>
/// </summary>
internal static class NativeFieldScriptPrefixCanonicalizer
{
    public const int NoSoleOwner = -1;

    private const int SharedCode = -2;
    private const byte KawaiOpcode = 0x28;
    private const byte CharOpcode = 0xA1;
    private const int MinimumWritablePacketLength = 5;

    /// <summary>Whether any byte could hold a native write: a setup packet shape with a non-zero packet[1].</summary>
    public static bool MayHoldNativeWrite(ReadOnlySpan<byte> prefix)
    {
        for (var at = 0; at + 4 < prefix.Length; at++)
        {
            if (IsSetupShape(prefix, at) && prefix[at + 4] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every KAWAI setup packet some entity's code can run, ascending, with its owner.</summary>
    public static IReadOnlyList<NativeKawaiWriteSite> FindWriteSites(byte[] prefix)
    {
        if (!HasSetupShape(prefix) || FieldScriptProgram.TryParse(prefix, out _) is not { } program)
        {
            return [];
        }

        var runners = new Dictionary<int, int>();
        var instructions = new Dictionary<int, FieldScriptInstruction>();
        var setups = new SortedDictionary<int, byte>();
        var characters = new int[program.EntityCount];
        for (var entity = 0; entity < program.EntityCount; entity++)
        {
            var seen = new HashSet<int>();
            foreach (var instruction in Runs(program, entity))
            {
                if (!seen.Add(instruction.Offset))
                {
                    continue;
                }

                instructions[instruction.Offset] = instruction;
                if (instruction.Id == CharOpcode)
                {
                    characters[entity]++;
                }

                if (IsSetupPacket(instruction))
                {
                    setups[instruction.Offset] = instruction.Bytes[2];
                }

                runners[instruction.Offset] = runners.TryGetValue(instruction.Offset, out var other) && other != entity
                    ? SharedCode
                    : entity;
            }
        }

        // A packet[1] that some other reachable instruction also decodes is not the packet's alone.
        var coverage = new byte[prefix.Length];
        foreach (var instruction in instructions.Values)
        {
            for (var at = instruction.Offset; at < instruction.Next && at < coverage.Length; at++)
            {
                if (coverage[at] < 2)
                {
                    coverage[at]++;
                }
            }
        }

        return setups
            .Select(setup => new NativeKawaiWriteSite(setup.Key, setup.Value,
                runners[setup.Key] is var owner and >= 0 && characters[owner] == 1 && coverage[setup.Key + 4] == 1
                    ? owner
                    : NoSoleOwner))
            .ToArray();
    }

    /// <summary>
    /// The prefix with each site's packet[1] restored to the reviewed zero where it holds exactly
    /// its owner's live model slot. The same array when nothing is restored.
    /// </summary>
    public static byte[] Restore(byte[] raw, IReadOnlyList<NativeKawaiWriteSite> sites, Func<int, byte?> liveModelSlot,
        out int[] restoredOffsets, out int unexplained)
    {
        var canonical = raw;
        var restored = new List<int>();
        unexplained = 0;
        foreach (var site in sites)
        {
            var value = raw[site.ValueOffset];
            if (value == 0)
            {
                continue;
            }

            if (site.Owner >= 0 && liveModelSlot(site.Owner) is { } slot && slot == value)
            {
                if (ReferenceEquals(canonical, raw))
                {
                    canonical = (byte[])raw.Clone();
                }

                canonical[site.ValueOffset] = 0;
                restored.Add(site.ValueOffset);
            }
            else
            {
                unexplained++;
            }
        }

        restoredOffsets = restored.ToArray();
        return canonical;
    }

    private static IEnumerable<FieldScriptInstruction> Runs(FieldScriptProgram program, int entity)
    {
        foreach (var instruction in program.InitAndMainReach(entity))
        {
            yield return instruction;
        }

        foreach (var (pointer, _) in program.DistinctEntries(entity))
        {
            foreach (var instruction in program.Reach(pointer))
            {
                yield return instruction;
            }
        }
    }

    private static bool IsSetupPacket(FieldScriptInstruction instruction) =>
        instruction.Id == KawaiOpcode &&
        instruction.Bytes.Length >= MinimumWritablePacketLength &&
        instruction.Bytes[1] >= MinimumWritablePacketLength &&
        instruction.Bytes[2] is 6 or 7 or 8 or 9 &&
        instruction.Bytes[3] == 0;

    private static bool HasSetupShape(ReadOnlySpan<byte> prefix)
    {
        for (var at = 0; at + 4 < prefix.Length; at++)
        {
            if (IsSetupShape(prefix, at))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSetupShape(ReadOnlySpan<byte> code, int at) =>
        code[at] == KawaiOpcode &&
        code[at + 1] >= MinimumWritablePacketLength &&
        (code[at + 2] is 6 or 7 or 8 or 9) &&
        code[at + 3] == 0;
}

/// <summary>
/// Identifies the field script bytes that are actually loaded after FFNx and
/// 7th Heaven have applied their virtual-file overrides. The hash stops at the
/// native text-table offset, matching the offline compatibility analyzer, and is
/// taken over the canonical prefix: the engine's KAWAI packet writes are undone,
/// so the identity does not drift while the reviewed script runs.
/// </summary>
public sealed class LoadedFieldScriptIdentityReader
{
    private const int HeaderLength = 8;
    private const int MinimumScriptPrefixLength = 32;
    private const int MaximumLoggedStates = 64;
    private readonly ILegacyAddressSpace memory;
    private readonly Action<string>? diagnostic;
    private readonly HashSet<string> loggedStates = [];
    private WriteSiteCache? writeSites;

    /// <param name="diagnostic">Optional log for prefixes holding native writes, once per raw state.</param>
    public LoadedFieldScriptIdentityReader(ILegacyAddressSpace memory, Action<string>? diagnostic = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.diagnostic = diagnostic;
    }

    public bool TryRead(out LoadedFieldScriptIdentity identity) => TryReadPrefix(out identity, out _, out _);

    internal bool TryReadNavigationMode(out ushort? seed) =>
        new PostscriptCharacterOrderReader(memory).TryReadNavigationMode(out seed);

    internal bool? ReadGuard(FieldScriptGuardTest test)
    {
        var block = test.Block switch
        {
            1 => FieldScriptExitGuards.AddressSavemapFieldBanks,
            3 => FieldScriptExitGuards.AddressSavemapFieldBanks + 0x100,
            5 => FieldScriptExitGuards.AddressTemporaryFieldBank,
            11 => FieldScriptExitGuards.AddressSavemapFieldBanks + 0x200,
            13 => FieldScriptExitGuards.AddressSavemapFieldBanks + 0x300,
            15 => FieldScriptExitGuards.AddressSavemapFieldBanks + 0x400,
            _ => -1
        };
        if (block < 0 || test.Address is < 0 or > 255 || !TryCaptureHeader(out var before) || before.Module != 1 ||
            !TryReadNavigationMode(out var characterSeed))
            return null;
        var address = (uint)(block + test.Address);
        bool Read(out int value)
        {
            value = 0;
            if (!memory.TryReadByte(address, out var low)) return false;
            value = low;
            if (test.Word)
            {
                if (!memory.TryReadByte(address + 1, out var high)) return false;
                value |= high << 8;
            }
            return true;
        }
        if (!Read(out var first) || !Read(out var second) || first != second ||
            !TryCaptureHeader(out var after) || before != after ||
            !TryReadNavigationMode(out var afterSeed) || characterSeed!=afterSeed) return null;
        // 0061182C changes byte equality's RHS, never its actual live bank value.
        var effective = characterSeed is { } seed && test.Block==3 && test.Address is >=9 and <=11 &&
            !test.Word && test.Operator==0 && test.Value is >=0 and <=8
            ? test with { Value = PostscriptCharacterOrderReader.ResolveVerifiedRole(seed,test.Value) } : test;
        return effective.IsMetBy(first);
    }

    /// <summary>The same coherent observation, including the canonical script prefix it identifies.</summary>
    public bool TryReadPrefix(out LoadedFieldScriptIdentity identity, out byte[] prefix) =>
        TryReadPrefix(out identity, out prefix, out _);

    /// <summary>
    /// The coherent observation with what its canonical prefix restored. Two identical raw reads
    /// under an unchanged header come first; native writes are only undone in that snapshot.
    /// </summary>
    public bool TryReadPrefix(out LoadedFieldScriptIdentity identity, out byte[] prefix,
        out LoadedFieldScriptPrefixNormalization normalization)
    {
        identity = default;
        prefix = [];
        normalization = default;
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

        var raw = Convert.ToHexString(SHA256.HashData(first));
        var canonical = first;
        int[] restored = [];
        var unexplained = 0;
        if (NativeFieldScriptPrefixCanonicalizer.MayHoldNativeWrite(first) &&
            WriteSites(raw, first) is { Count: > 0 } sites)
        {
            canonical = NativeFieldScriptPrefixCanonicalizer.Restore(first, sites, LiveModelSlot,
                out restored, out unexplained);

            // The model slots belong to this observation only while the same script is loaded.
            if (!TryCaptureHeader(out var settled) || !before.Equals(settled))
            {
                return false;
            }
        }

        var canonicalHash = ReferenceEquals(canonical, first) ? raw : Convert.ToHexString(SHA256.HashData(canonical));
        identity = new LoadedFieldScriptIdentity(before.FieldId, before.ScriptPointer, canonicalHash);
        prefix = canonical;
        normalization = new LoadedFieldScriptPrefixNormalization(raw, restored, unexplained);
        Report(before.FieldId, normalization, canonicalHash);
        return true;
    }

    private IReadOnlyList<NativeKawaiWriteSite> WriteSites(string raw, byte[] prefix)
    {
        if (Volatile.Read(ref writeSites) is { } cached && cached.RawSha256 == raw)
        {
            return cached.Sites;
        }

        var sites = NativeFieldScriptPrefixCanonicalizer.FindWriteSites(prefix);
        Volatile.Write(ref writeSites, new WriteSiteCache(raw, sites));
        return sites;
    }

    /// <summary>The entity's model slot as KAWAI and the dispatcher see it, or null.</summary>
    private byte? LiveModelSlot(int entity) =>
        memory.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var count) &&
        memory.TryReadByte((uint)(FieldScriptControllerReader.AddressEntityModelIds + entity), out var slot) &&
        slot != FieldScriptControllerReader.NoModel && slot < count
            ? slot
            : null;

    private void Report(int fieldId, LoadedFieldScriptPrefixNormalization normalization, string canonical)
    {
        if (diagnostic is null ||
            normalization.RestoredOffsets.Count == 0 && normalization.UnexplainedNativeWrites == 0)
        {
            return;
        }

        lock (loggedStates)
        {
            if (loggedStates.Count >= MaximumLoggedStates)
            {
                loggedStates.Clear();
            }

            if (!loggedStates.Add($"{fieldId}:{normalization.RawScriptPrefixSha256}"))
            {
                return;
            }
        }

        var offsets = normalization.RestoredOffsets.Count == 0
            ? string.Empty
            : " at " + string.Join(',', normalization.RestoredOffsets.Select(offset => $"0x{offset:X5}"));
        diagnostic(
            $"Loaded field script native KAWAI writes: field={fieldId}, restored={normalization.RestoredOffsets.Count}{offsets}, " +
            $"unexplained={normalization.UnexplainedNativeWrites}, raw={normalization.RawScriptPrefixSha256}, canonical={canonical}.");
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

    private sealed record WriteSiteCache(string RawSha256, IReadOnlyList<NativeKawaiWriteSite> Sites);

    private readonly record struct HeaderSnapshot(
        byte Module,
        ushort FieldId,
        uint ScriptPointer,
        byte EntityCount,
        ushort TextOffset);
}
