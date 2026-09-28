using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// One battle damage popup on its first drawn frame, plus the engine's own
/// record of the result it displays. Every field comes from a structure the
/// battle engine keeps for the animation it is playing; nothing is inferred
/// from numbers that happen to appear together.
/// </summary>
public readonly record struct BattleVisibleResultSnapshot(
    bool IsValid,
    int EffectIndex,
    int TargetActor,
    int Value,
    int Flags,
    bool HasProvenance = false,
    int AttackerActor = -1,
    int ResultRow = -1,
    int ResultFlags = 0,
    bool IsDrainRecovery = false,
    ushort DrainSourceMask = 0)
{
    // FUN_005da3f5 and FUN_005df30b build the damage-row flags: 1 is a recovery,
    // which FUN_005bb048 draws with the recovery palette, and 4 is an MP amount,
    // which FUN_005bb756 labels "MP".
    public const int RecoveryFlag = 0x01;
    public const int MpFlag = 0x04;

    // FUN_005da5da sets result flag 4 when the target is down after the row, and
    // FUN_00436cf2 keeps it only on that target's last row of the action.
    public const int LethalResultFlag = 0x04;

    // FUN_005bb756 draws these values as words instead of digits. FUN_005db593
    // assigns -2 to an elemental instant death and -3 to an elemental full
    // restore, which sets both HP and MP to their maximum.
    public const int MissValue = -1;
    public const int InstantDeathValue = -2;
    public const int FullRestoreValue = -3;

    public static BattleVisibleResultSnapshot Invalid { get; } = new(false, -1, -1, 0, 0);

    public bool IsMiss => IsValid && Value == MissValue;

    public bool ShowsNumber => IsValid && Value >= 0;

    public bool IsRecovery => (Flags & RecoveryFlag) != 0;

    public bool IsMp => (Flags & MpFlag) != 0;

    public bool IsLethal => IsValid && HasProvenance && (ResultFlags & LethalResultFlag) != 0;

    /// <summary>The popup alone, in the shape the older popup callers accept.</summary>
    public BattleDamagePopupSnapshot ToPopupSnapshot() =>
        IsValid && (Value > 0 || Value == MissValue)
            ? new BattleDamagePopupSnapshot(true, EffectIndex, TargetActor, Value, Flags)
            : BattleDamagePopupSnapshot.Invalid;

    internal BattleVisibleResultSnapshot WithoutProvenance() =>
        new(IsValid, EffectIndex, TargetActor, Value, Flags);
}

/// <summary>
/// Reads the popup FUN_005bb410 is about to draw for the first time and follows
/// the engine's links back to its result: the popup's FUN_00425e5f sibling names
/// the damage row and the result row, the result row names the target, the
/// attacker and whether the hit was lethal, and the damage row must match the
/// drawn number exactly. A drain recovery is recognised only by the animation
/// event FUN_005da380 queues for it.
/// </summary>
public sealed class BattleVisibleResultReader
{
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;

    // The 60-slot effect list run by FUN_005bf1c2. Slots hold guest function
    // addresses (the Steam 2026 translation stores the same values).
    public const int AddressCurrentEffectIndex = 0x00BF2DF4;
    public const int AddressEffectFunctions = 0x00BFB1B0;
    public const int AddressEffectData = 0x00BFC3A0;
    public const int EffectRecordSize = 0x20;
    public const int EffectCount = 60;
    public const int EffectStateOffset = 0x02;
    public const uint DamagePopupFunction = 0x005BB410;
    public const int PopupValueOffset = 0x0E;
    public const int PopupTargetOffset = 0x10;
    public const int PopupFlagsOffset = 0x14;

    // FUN_00425d29 and FUN_00425fc4 allocate this sibling right after the popup.
    // Three frames later it copies the damage row's HP and MP into the HUD.
    public const uint DamageCommitFunction = 0x00425E5F;
    public const int CommitDamageRowOffset = 0x08;
    public const int CommitResultRowOffset = 0x0A;

    // FUN_00436c66: animation events, twelve bytes each. Offset 0x0A is the
    // event's result-row cursor; FUN_0042cbf9 advances it to the event's
    // terminator row when the event starts.
    public const int AddressAnimationEvents = BattleStateReader.AddressAnimationEventQueue;
    public const int AddressAnimationEventCount = 0x009AEA9C;
    public const int AnimationEventSize = BattleStateReader.AnimationEventSize;
    public const int AnimationEventCapacity = BattleStateReader.AnimationEventCount;

    // FUN_00436cb5: per-target result rows. [0] target (0xFF ends an event),
    // [1] attacker, [2] hit animation, [3] damage row, +4 flags, +8 status after.
    public const int AddressResultRows = 0x009ACB98;
    public const int AddressResultRowCount = 0x009AEAA0;
    public const int ResultRowSize = 0x0C;
    public const int ResultRowCapacity = 0x80;

    // FUN_00436da7 and FUN_005da562: fourteen-byte damage rows. +0 target,
    // +2 displayed value, +4 flags. The rest holds HP and MP after the hit and
    // is deliberately never read here.
    public const int AddressDamageRows = 0x009ABA08;
    public const int DamageRowSize = 0x0E;
    public const int DamageRowCapacity = 0x80;

    // Only the fields used: a result row's target, attacker, hit animation,
    // damage row and flags, and a damage row's target, value and flags.
    private const int ResultRowFieldsSize = 6;
    private const int DamageRowFieldsSize = 6;

    // FUN_005da380 queues the drainer's recovery as its own kind 1 event with
    // animation script 0x2E, no command, no action and no camera, and
    // FUN_005da3f5 gives its single self-targeted row the same hit animation.
    public const byte DrainRecoveryScript = 0x2E;

    private const byte EndOfRows = 0xFF;
    private const int EventAttackerOffset = 0x00;
    private const int EventKindOffset = 0x01;
    private const int EventEffectOffset = 0x02;
    private const int EventCommandOffset = 0x03;
    private const int EventFlagsOffset = 0x04;
    private const int EventScriptOffset = 0x05;
    private const int EventActionOffset = 0x06;
    private const int EventCameraOffset = 0x08;
    private const int EventCursorOffset = 0x0A;
    private const byte ActionEventKind = 1;

    private readonly ILegacyAddressSpace addressSpace;

    public BattleVisibleResultReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public BattleVisibleResultSnapshot Read()
    {
        var candidate = ReadCore();
        var bookend = ReadCore();
        if (!candidate.IsValid ||
            !bookend.IsValid ||
            candidate.WithoutProvenance() != bookend.WithoutProvenance())
        {
            return BattleVisibleResultSnapshot.Invalid;
        }

        // A torn provenance read still leaves a trustworthy popup.
        return candidate == bookend ? candidate : candidate.WithoutProvenance();
    }

    private BattleVisibleResultSnapshot ReadCore()
    {
        try
        {
            if (!TryReadPopup(out var popup))
            {
                return BattleVisibleResultSnapshot.Invalid;
            }

            return TryReadProvenance(popup, out var result) ? result : popup;
        }
        catch (OverflowException)
        {
            return BattleVisibleResultSnapshot.Invalid;
        }
    }

    // The popup alone needs only the record FUN_005bb410 is running (the same
    // acceptance as BattleDamagePopupReader, plus the drawn 0 and the -2/-3 words).
    private bool TryReadPopup(out BattleVisibleResultSnapshot popup)
    {
        popup = BattleVisibleResultSnapshot.Invalid;
        if (!addressSpace.TryReadByte((uint)AddressCurrentModule, out var module) ||
            module != BattleStateReader.BattleModule ||
            !addressSpace.TryReadInt16((uint)AddressCurrentEffectIndex, out var effectIndex) ||
            effectIndex is < 0 or >= EffectCount)
        {
            return false;
        }

        // Value (+0x0E), target (+0x10) and flags (+0x14) are contiguous.
        Span<byte> fields = stackalloc byte[PopupFlagsOffset + sizeof(int) - PopupValueOffset];
        var record = EffectRecordAddress(effectIndex);
        if (!addressSpace.TryReadByte(record + EffectStateOffset, out var state) ||
            state != 0 ||
            !addressSpace.TryRead(record + PopupValueOffset, fields))
        {
            return false;
        }

        var value = BinaryPrimitives.ReadInt16LittleEndian(fields);
        var target = BinaryPrimitives.ReadInt32LittleEndian(fields[(PopupTargetOffset - PopupValueOffset)..]);
        var flags = BinaryPrimitives.ReadInt32LittleEndian(fields[(PopupFlagsOffset - PopupValueOffset)..]);
        if (!IsBattleActor(target) || value < BattleVisibleResultSnapshot.FullRestoreValue)
        {
            return false;
        }

        popup = new BattleVisibleResultSnapshot(true, effectIndex, target, value, flags);
        return true;
    }

    private bool TryReadProvenance(
        BattleVisibleResultSnapshot popup,
        out BattleVisibleResultSnapshot result)
    {
        result = popup;
        if (!TryReadEffectFunctions(out var functions) ||
            functions[popup.EffectIndex] != DamagePopupFunction ||
            !TryFindCommitSibling(popup.EffectIndex, functions, out var damageRow, out var resultRow) ||
            !addressSpace.TryReadInt32((uint)AddressResultRowCount, out var rowCount) ||
            rowCount is < 1 or > ResultRowCapacity ||
            resultRow >= rowCount)
        {
            return false;
        }

        Span<byte> row = stackalloc byte[ResultRowFieldsSize];
        Span<byte> damage = stackalloc byte[DamageRowFieldsSize];
        if (!addressSpace.TryRead(ResultRowAddress(resultRow), row) ||
            row[0] != popup.TargetActor ||
            row[3] != damageRow ||
            row[1] >= 10 ||
            !addressSpace.TryRead(DamageRowAddress(damageRow), damage) ||
            BinaryPrimitives.ReadInt16LittleEndian(damage) != popup.TargetActor ||
            BinaryPrimitives.ReadInt16LittleEndian(damage[2..]) != popup.Value ||
            BinaryPrimitives.ReadInt16LittleEndian(damage[4..]) != unchecked((short)popup.Flags))
        {
            return false;
        }

        var attacker = row[1];
        var isDrain = false;
        ushort sources = 0;
        if (attacker == popup.TargetActor && row[2] == DrainRecoveryScript)
        {
            ReadDrainEvent(resultRow, attacker, rowCount, out isDrain, out sources);
        }

        result = popup with
        {
            HasProvenance = true,
            AttackerActor = attacker,
            ResultRow = resultRow,
            ResultFlags = BinaryPrimitives.ReadUInt16LittleEndian(row[4..]),
            IsDrainRecovery = isDrain,
            DrainSourceMask = isDrain ? sources : (ushort)0
        };
        return true;
    }

    // The sibling is the first not-yet-run FUN_00425e5f after the popup. Effects
    // allocated in the same pass take the lowest free slots in order, so an
    // unrun popup found first means this popup's sibling is missing.
    private bool TryFindCommitSibling(
        int popupIndex,
        uint[] functions,
        out int damageRow,
        out int resultRow)
    {
        damageRow = -1;
        resultRow = -1;
        Span<byte> rows = stackalloc byte[CommitResultRowOffset + sizeof(short) - CommitDamageRowOffset];
        for (var slot = popupIndex + 1; slot < EffectCount; slot++)
        {
            var function = functions[slot];
            if (function is not (DamageCommitFunction or DamagePopupFunction))
            {
                continue;
            }

            var record = EffectRecordAddress(slot);
            if (!addressSpace.TryReadByte(record + EffectStateOffset, out var state))
            {
                return false;
            }

            if (state != 0)
            {
                continue;
            }

            if (function == DamagePopupFunction ||
                !addressSpace.TryRead(record + CommitDamageRowOffset, rows))
            {
                return false;
            }

            damageRow = BinaryPrimitives.ReadInt16LittleEndian(rows);
            resultRow = BinaryPrimitives.ReadInt16LittleEndian(rows[(CommitResultRowOffset - CommitDamageRowOffset)..]);
            return damageRow is >= 0 and < DamageRowCapacity &&
                   resultRow is >= 0 and < ResultRowCapacity;
        }

        return false;
    }

    // A row is a drain recovery only when FUN_005da380's event owns exactly this
    // row. The source list is then best effort: the drainer's own rows in the
    // action event queued before it.
    private void ReadDrainEvent(
        int drainRow,
        int drainer,
        int rowCount,
        out bool isDrain,
        out ushort sources)
    {
        isDrain = false;
        sources = 0;
        if (!addressSpace.TryReadInt32((uint)AddressAnimationEventCount, out var eventCount) ||
            eventCount is < 1 or > AnimationEventCapacity ||
            drainRow + 1 >= rowCount)
        {
            return;
        }

        var events = new byte[eventCount * AnimationEventSize];
        var rows = new byte[rowCount * ResultRowSize];
        if (!addressSpace.TryRead((uint)AddressAnimationEvents, events) ||
            !addressSpace.TryRead((uint)AddressResultRows, rows) ||
            rows[(drainRow + 1) * ResultRowSize] != EndOfRows)
        {
            return;
        }

        var drainEvent = -1;
        for (var index = 0; index < eventCount; index++)
        {
            if (IsDrainEvent(events, index) &&
                events[index * AnimationEventSize + EventAttackerOffset] == drainer &&
                Cursor(events, index) == drainRow + 1)
            {
                drainEvent = index;
                break;
            }
        }

        if (drainEvent < 0)
        {
            return;
        }

        isDrain = true;
        var mainEvent = -1;
        for (var index = drainEvent - 1; index >= 0; index--)
        {
            if (Cursor(events, index) >= 0 && !IsDrainEvent(events, index))
            {
                mainEvent = index;
                break;
            }
        }

        if (mainEvent < 0)
        {
            return;
        }

        var end = Cursor(events, mainEvent);
        var start = 0;
        for (var index = mainEvent - 1; index >= 0; index--)
        {
            var cursor = Cursor(events, index);
            if (cursor >= 0)
            {
                start = cursor + 1;
                break;
            }
        }

        if (start > end || end >= rowCount || rows[end * ResultRowSize] != EndOfRows)
        {
            return;
        }

        Span<byte> damage = stackalloc byte[DamageRowFieldsSize];
        for (var row = start; row < end; row++)
        {
            var offset = row * ResultRowSize;
            var target = rows[offset];
            var damageRow = rows[offset + 3];
            if (rows[offset + 1] != drainer ||
                target == drainer ||
                !IsBattleActor(target) ||
                damageRow >= DamageRowCapacity ||
                !addressSpace.TryRead(DamageRowAddress(damageRow), damage) ||
                BinaryPrimitives.ReadInt16LittleEndian(damage) != target ||
                BinaryPrimitives.ReadInt16LittleEndian(damage[2..]) < 0)
            {
                continue;
            }

            sources |= (ushort)(1 << target);
        }
    }

    private static bool IsDrainEvent(byte[] events, int index)
    {
        var offset = index * AnimationEventSize;
        return events[offset + EventKindOffset] == ActionEventKind &&
               events[offset + EventEffectOffset] == 0 &&
               events[offset + EventCommandOffset] == 0 &&
               events[offset + EventFlagsOffset] == 0 &&
               events[offset + EventScriptOffset] == DrainRecoveryScript &&
               BinaryPrimitives.ReadUInt16LittleEndian(events.AsSpan(offset + EventActionOffset)) == 0 &&
               BinaryPrimitives.ReadUInt16LittleEndian(events.AsSpan(offset + EventCameraOffset)) == ushort.MaxValue &&
               events[offset + EventAttackerOffset] < 10;
    }

    // FUN_00436e15 stores 0xFFFF here for events that own no result rows.
    private static int Cursor(byte[] events, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(
            events.AsSpan(index * AnimationEventSize + EventCursorOffset));

    private bool TryReadEffectFunctions(out uint[] functions)
    {
        functions = [];
        Span<byte> table = stackalloc byte[EffectCount * sizeof(uint)];
        if (!addressSpace.TryRead((uint)AddressEffectFunctions, table))
        {
            return false;
        }

        functions = new uint[EffectCount];
        for (var slot = 0; slot < EffectCount; slot++)
        {
            functions[slot] = BinaryPrimitives.ReadUInt32LittleEndian(table[(slot * sizeof(uint))..]);
        }

        return true;
    }

    public static uint EffectRecordAddress(int slot) =>
        checked((uint)(AddressEffectData + slot * EffectRecordSize));

    private static uint ResultRowAddress(int row) =>
        checked((uint)(AddressResultRows + row * ResultRowSize));

    private static uint DamageRowAddress(int row) =>
        checked((uint)(AddressDamageRows + row * DamageRowSize));

    private static bool IsBattleActor(int actorIndex) =>
        actorIndex is >= 0 and < 3 or >= 4 and <= 9;
}
