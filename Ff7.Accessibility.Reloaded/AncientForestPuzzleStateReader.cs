using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>One coherent look at an Ancient Forest puzzle field.</summary>
/// <param name="Bank">Bank 5, the field's temporary bytes, exactly as <see cref="AncientForestPuzzleStateReader.Read"/> returns it.</param>
/// <param name="ShakingPlants">
/// The plants, numbered west to east, whose own background entity is running its native shake.
/// Null when that could not be read coherently in this sample; empty when no plant is shaking.
/// </param>
/// <param name="Models">
/// Each creature's model, keyed by its stock entity id whatever the loaded layout. A model that
/// could not be read stays in the list as unreadable rather than being left out.
/// </param>
internal sealed record AncientForestPuzzleSnapshot(
    byte[] Bank,
    IReadOnlySet<int>? ShakingPlants,
    IReadOnlyList<FieldActivityModelReading> Models);

/// <summary>
/// A coherent native snapshot, admitted only for reviewed loaded forest scripts.
///
/// <para><b>Shaking.</b> A fed pitcher plant's director - the unnamed entity that owns it - closes
/// the plant, waits 300 frames, and then has the plant's own background entity (utubo_N) run its
/// script 3 twice before opening it. Script 3 cycles the plant's background states 2, 3 and 4: the
/// shake a sighted player sees just before the plant opens. So the plant entity running script 3 is
/// the shake itself, from its first frame. The counter that script keeps (5[61..63] in 620,
/// 5[47..49] in 622, 5[67..70] in 623) still reads 0 through the whole first pass, so it is never
/// used as the evidence.</para>
///
/// <para>The running script is read per entity: its priority at 0x00CC0B30, and the script at
/// that priority at 0x00CBF9E8. The globally current VM entity is never consulted. Two captures
/// must agree. The entry point is proved by the script's own first bytes (IFUB that plant's
/// counter &lt; 3), and the entity's program counter at 0x00CC0CF8 must lie inside the script.
/// Anything else is unknown, never "still".</para>
///
/// <para><b>Layouts.</b> Only the exact English stock prefix and the reviewed Echo-S 1.24 prefixes
/// are admitted, and each has its own entity map. The maps were checked against the installed
/// fields and all five Echo-S fixtures. Echo-S drops 620's escdir, so every later entity moves down
/// one. It inserts FrogFix in 623 ahead of the plants, so they move up one. 622 keeps its
/// indices.</para>
/// </summary>
internal sealed class AncientForestPuzzleStateReader
{
    private const int ShakeScriptId = 3;
    private const int BankLength = 128;

    // Each plant's shake: the counter it keeps, the skip of its opening IFUB, and its length. Echo-S
    // 620's first plant has its own longer shake, which clears that plant's flag 5[17] during the
    // second pass (5[255] = 1); the rest are stock.
    private static readonly IReadOnlyList<Shake> Forest1StockShakes = [new(0x3D, 0x31, 59), new(0x3E, 0x31, 59), new(0x3F, 0x31, 59)];
    private static readonly IReadOnlyList<Shake> Forest1EchoShakes = [new(0x3D, 0x3B, 69), new(0x3E, 0x31, 59), new(0x3F, 0x31, 59)];

    private static readonly ForestLayout Forest1Stock = new([37, 38, 39], Forest1StockShakes,
        [(7, 7), (8, 8), (9, 9), (10, 10), (16, 16)]);
    private static readonly ForestLayout Forest1Echo = new([36, 37, 38], Forest1EchoShakes,
        [(7, 6), (8, 7), (9, 8), (10, 9), (16, 15)]);
    private static readonly ForestLayout Forest3 = new([28, 29, 30],
        [new(0x2F, 0x31, 59), new(0x30, 0x31, 59), new(0x31, 0x31, 59)],
        [(6, 6), (7, 7), (8, 8), (9, 9), (10, 10), (11, 11)]);
    private static readonly IReadOnlyList<Shake> Forest4Shakes =
        [new(0x43, 0x31, 59), new(0x44, 0x31, 59), new(0x45, 0x31, 59), new(0x46, 0x31, 59)];
    private static readonly ForestLayout Forest4Stock = new([41, 42, 43, 44], Forest4Shakes,
        [(6, 6), (7, 7), (8, 8), (9, 9), (10, 10), (16, 16)]);
    private static readonly ForestLayout Forest4Echo = new([42, 43, 44, 45], Forest4Shakes,
        [(6, 6), (7, 7), (8, 8), (9, 9), (10, 10), (16, 16)]);

    private readonly ILegacyAddressSpace memory;
    private readonly Func<LoadedFieldScriptIdentity?> identity;
    private readonly FieldActivityStateReader models;

    public AncientForestPuzzleStateReader(ILegacyAddressSpace memory,
        Func<LoadedFieldScriptIdentity?>? readIdentity = null)
    {
        this.memory = memory;
        var reader = new LoadedFieldScriptIdentityReader(memory);
        identity = readIdentity ?? (() => reader.TryRead(out var found) ? found : null);
        models = new FieldActivityStateReader(memory);
    }

    /// <summary>Bank 5 alone, for callers that need nothing else.</summary>
    public byte[]? Read(int fieldId) =>
        TryReadBank(fieldId, out _, out _, out var bank) ? bank : null;

    /// <summary>
    /// Bank 5, the shaking plants and the creatures' models, for one field activity
    /// observation. Null when the field is not a reviewed forest puzzle, the room changed or the
    /// bank could not be read coherently.
    /// </summary>
    public AncientForestPuzzleSnapshot? ReadState(int fieldId)
    {
        if (!TryReadBank(fieldId, out var script, out var variant, out var bank) ||
            LayoutFor(fieldId, variant) is not { } layout)
        {
            return null;
        }

        var shaking = ReadShaking(script.ScriptPointer, layout);
        var creatures = new FieldActivityModelReading[layout.Creatures.Count];
        for (var index = 0; index < creatures.Length; index++)
        {
            var (canonical, loaded) = layout.Creatures[index];
            creatures[index] = models.ReadModel(fieldId, loaded) with { EntityId = canonical };
        }

        // The optional cursor/model reads can span a native update. Recheck the
        // observed bank facts after them, so a pickup or plant change cannot be
        // assembled from opposite sides of that update.
        if (!TryReadBank(fieldId, out var afterScript, out var afterVariant, out var afterBank) ||
            afterScript != script || afterVariant != variant || !SameObservedFacts(fieldId, bank, afterBank)) return null;
        return new AncientForestPuzzleSnapshot(afterBank, shaking, creatures);
    }

    /// <summary>The plant entities, west to east, and each creature's stock and loaded entity.</summary>
    internal static (IReadOnlyList<int> Plants, IReadOnlyList<(int Canonical, int Loaded)> Creatures)? Layout(
        int fieldId, SupportedFieldScriptVariant variant) =>
        LayoutFor(fieldId, variant) is { } layout ? (layout.Plants, layout.Creatures) : null;

    private static ForestLayout? LayoutFor(int fieldId, SupportedFieldScriptVariant variant) => (fieldId, variant) switch
    {
        (620, SupportedFieldScriptVariant.Vanilla) => Forest1Stock,
        (620, SupportedFieldScriptVariant.EchoS124) => Forest1Echo,
        (622, SupportedFieldScriptVariant.Vanilla or SupportedFieldScriptVariant.EchoS124) => Forest3,
        (623, SupportedFieldScriptVariant.Vanilla) => Forest4Stock,
        (623, SupportedFieldScriptVariant.EchoS124) => Forest4Echo,
        _ => null
    };

    private bool TryReadBank(int fieldId, out LoadedFieldScriptIdentity script,
        out SupportedFieldScriptVariant variant, out byte[] bank)
    {
        script = default;
        variant = SupportedFieldScriptVariant.Unknown;
        bank = [];
        if (!AncientForestPuzzleReadout.HasPuzzle(fieldId) || identity() is not { } loaded ||
            loaded.FieldId != fieldId) return false;
        variant = EchoSCompatibilityManifest.ResolveVariant(loaded);
        if (variant is not (SupportedFieldScriptVariant.Vanilla or SupportedFieldScriptVariant.EchoS124) ||
            !IsSameContext(loaded)) return false;

        var first = new byte[BankLength];
        var second = new byte[first.Length];
        if (!memory.TryRead((uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase, first) ||
            !memory.TryRead((uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase, second) ||
            !IsSameContext(loaded)) return false;

        // PXYZI and hidden countdowns change in this bank too. Compare only the visible
        // facts we use, never require those unrelated moving bytes to stand still.
        if (!SameObservedFacts(fieldId, first, second)) return false;
        script = loaded;
        bank = second;
        return true;
    }

    private static bool SameObservedFacts(int fieldId, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var plantStart = fieldId switch { 620 => 17, 622 => 16, _ => 18 };
        var plantCount = fieldId == 623 ? 4 : 3;
        if (first[9] != second[9]) return false;
        for (var i = 0; i < plantCount; i++)
            if (first[plantStart + i] != second[plantStart + i]) return false;
        foreach (var (_, address) in AncientForestPuzzleReadout.Flytraps(fieldId))
            if (first[address] != second[address]) return false;
        return true;
    }

    private IReadOnlySet<int>? ReadShaking(uint section, ForestLayout layout)
    {
        var shaking = new HashSet<int>();
        for (var index = 0; index < layout.Plants.Count; index++)
        {
            var entity = layout.Plants[index];
            if (!TryReadCursor(section, entity, out var before) ||
                !TryReadCursor(section, entity, out var after) ||
                before != after)
            {
                return null;
            }

            if (before.Script != ShakeScriptId)
            {
                continue;
            }

            if (!IsShake(section, before, layout.Shakes[index]))
            {
                return null;
            }

            shaking.Add(index + 1);
        }

        return shaking;
    }

    /// <summary>
    /// The entity's own cursor: its priority, the script at that priority and, for script 3 only,
    /// its program counter and that script's entry point. False on any failed read or an entity
    /// the loaded section does not have.
    /// </summary>
    private bool TryReadCursor(uint section, int entity, out Cursor cursor)
    {
        cursor = default;
        if (!memory.TryReadByte(section + FieldActivityStateReader.ScriptHeaderEntityCountOffset, out var count) ||
            entity >= count ||
            !memory.TryReadByte((uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity), out var priority))
        {
            return false;
        }

        if (priority >= FieldScriptControllerReader.ScriptSlotsPerEntity)
        {
            cursor = new Cursor(priority, byte.MaxValue, 0, 0);
            return true;
        }

        if (!memory.TryReadByte(
                (uint)(FieldScriptControllerReader.AddressEntityScriptIds +
                    entity * FieldScriptControllerReader.ScriptSlotsPerEntity + priority),
                out var running))
        {
            return false;
        }

        if (running != ShakeScriptId)
        {
            cursor = new Cursor(priority, running, 0, 0);
            return true;
        }

        if (!memory.TryReadUInt16((uint)(FieldActivityStateReader.AddressEntityProgramCounters + entity * 2), out var counter) ||
            !memory.TryReadUInt16(section + FieldActivityStateReader.ScriptHeaderAkaoCountOffset, out var akao))
        {
            return false;
        }

        var entryAddress = section +
            FieldActivityStateReader.ScriptHeaderEntityNamesOffset +
            (uint)(count * FieldActivityStateReader.ScriptHeaderEntityNameLength) +
            (uint)(akao * 4) +
            (uint)((entity * FieldActivityStateReader.ScriptEntryPointsPerEntity + running) * 2);
        if (!memory.TryReadUInt16(entryAddress, out var entry))
        {
            return false;
        }

        cursor = new Cursor(priority, running, counter, entry);
        return true;
    }

    /// <summary>The script really is this plant's shake, and the entity is inside it.</summary>
    private bool IsShake(uint section, Cursor cursor, Shake shake)
    {
        Span<byte> prefix = stackalloc byte[6];
        if (!memory.TryRead(section + cursor.Entry, prefix))
        {
            return false;
        }

        ReadOnlySpan<byte> expected = [0x14, 0x50, shake.Counter, 0x03, 0x03, shake.Skip];
        return prefix.SequenceEqual(expected) &&
            cursor.ProgramCounter >= cursor.Entry &&
            cursor.ProgramCounter - cursor.Entry < shake.Length;
    }

    private bool IsSameContext(LoadedFieldScriptIdentity script) =>
        memory.TryReadByte((uint)FieldScriptContextReader.AddressCurrentModule, out var module) &&
        module == FieldPositionReader.FieldModule &&
        memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var field) && field == script.FieldId &&
        memory.TryReadUInt32((uint)FieldScriptContextReader.AddressFieldScriptPtr, out var pointer) &&
        pointer == script.ScriptPointer;

    private sealed record ForestLayout(
        IReadOnlyList<int> Plants,
        IReadOnlyList<Shake> Shakes,
        IReadOnlyList<(int Canonical, int Loaded)> Creatures);

    private readonly record struct Shake(byte Counter, byte Skip, int Length);

    private readonly record struct Cursor(byte Priority, byte Script, ushort ProgramCounter, ushort Entry);
}
