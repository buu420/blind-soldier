using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the Fort Condor battle's live state out of the module 9 globals.
/// </summary>
/// <remarks>
/// Addresses and layouts come from
/// <c>analysis/ghidra/fort-condor-live-battle-state-20260821.md</c>, which
/// established each one from the executable's own readers and writers rather
/// than from watching memory change. The cursor pair was additionally confirmed
/// in a live battle on 2026-08-21, as was the Setting Menu's modal state and its
/// available-unit list.
///
/// <para>Every read must succeed. A partial snapshot would be worse than none:
/// a missing HP read that silently became zero would have the mod announce a
/// healthy unit as dead, and the player has no way to check it against the
/// screen.</para>
/// </remarks>
public sealed class CondorBattleStateReader
{
    /// <summary>The module the fort battle runs in.</summary>
    public const byte CondorModule = 9;

    /// <summary>
    /// How often the battle state is worth re-reading. The cursor moves four
    /// world units per input step and repeats, so the reader has to sample
    /// faster than a player can cross a unit's hit box.
    /// </summary>
    /// <remarks>
    /// Shared so both runtimes read at the same cadence. This is a dual-runtime
    /// mod and module 9 has to sound the same on either executable.
    /// </remarks>
    public static readonly TimeSpan ReadInterval = TimeSpan.FromMilliseconds(100);

    private const uint AddressInteractionMode = 0x00C74C50;
    private const uint AddressModalState = 0x00C625E0;
    private const uint AddressAllyUnitCommandRow = 0x00CBC930;
    private const uint AddressAllyUnitCommandCount = 0x00C752D4;
    private const uint AddressAllyUnitCommand0 = 0x00C74CA8;
    private const uint AddressAllyUnitCommandStride = 8;
    private const uint AddressSettingMenuRow = 0x00CBCCA0;
    private const uint AddressSettingMenuRotation = 0x00C75254;
    private const uint AddressSettingMenuCount = 0x00C75264;
    private const uint AddressAvailableTypeIds = 0x00C75278;
    private const uint AddressStartGameSelection = 0x00CBC7D8;
    private const uint AddressDirectionSelection = 0x00C625D0;
    private const uint AddressCrowdedUnitPointers = 0x00C60980;
    private const uint AddressCrowdedUnitCount = 0x00C61BF4;
    private const uint AddressCrowdedUnitRow = 0x00C74C68;
    private const uint AddressGil = 0x00CBC7E0;
    private const uint AddressDestinationX = 0x00C75268;
    private const uint AddressDestinationY = 0x00C7526A;
    private const uint AddressGameSpeed = 0x00C752B4;

    /// <summary>
    /// Module 9's own held-input mask. FUN_005FD958 tests it as
    /// <c>mask &amp; 0xF000</c> and repeats the cursor from those bits.
    /// </summary>
    private const uint AddressHeldInput = 0x00C72E80;

    /// <summary>The direction bits within <see cref="AddressHeldInput"/>.</summary>
    private const uint HeldDirectionBits = 0xF000;
    /// <summary>
    /// The cursor pair. X and Y are adjacent 16-bit values, so a host that can
    /// write a 32-bit word moves both at once and the game never observes half a
    /// move.
    /// </summary>
    public const uint AddressCursor = 0x00CBCCC0;

    private const uint AddressCursorX = AddressCursor;
    private const uint AddressCursorY = AddressCursor + 2;
    private const uint AddressCursorPlacementLegal = 0x00CBCC9C;
    private const uint AddressUnitUnderCursor = 0x00C6097C;
    private const uint AddressLiveUnits = 0x00CBCCD8;
    private const uint AddressAlliedCount = 0x00C60AD0;
    private const uint AddressEnemyCount = 0x00CBC7A4;
    private const uint AddressOutcome = 0x00CBEDC0;
    private const uint AddressMessageId = 0x00901B70;
    private const uint AddressPhase = 0x00C625D4;
    private const uint AddressReportState = 0x00C72DEC;
    private const uint AddressReportMessageCell = 0x00C60AC4;
    private const uint AddressReportUnitSlot = 0x00C72E3C;
    private const uint AddressDeploymentFrontierY = 0x00C60AE8;
    private const uint AddressEnemyAdvance = 0x00CBCCAC;
    private const uint AddressCollisionCount = 0x00C60AA4;
    private const uint AddressCollisionRecords = 0x00C625E8;

    private const int CollisionRecordStride = 0x4C;

    /// <summary>
    /// The shipped archive has 333 collision triangles. A count outside this
    /// bound means the array is not loaded and the snapshot is not coherent.
    /// </summary>
    private const int MaximumCollisionRecords = 4096;

    private const int UnitSlots = 40;
    private const int FirstEnemySlot = 20;
    private const int UnitStride = 0x78;
    private const int MaximumAllyUnitCommands = 3;

    private const int UnitAllocated = 0x00;
    private const int UnitRemovalState = 0x05;
    private const int UnitTypeId = 0x06;
    private const int UnitCurrentHp = 0x10;
    private const int UnitMaximumHp = 0x11;
    private const int UnitAttack = 0x12;
    private const int UnitWidth = 0x22;
    private const int UnitHeightAbove = 0x23;
    private const int UnitX = 0x48;
    private const int UnitY = 0x4A;

    /// <summary>
    /// The largest Setting Menu the builder can produce is ten entries. A count
    /// outside this range means the list is not built and the snapshot is not
    /// coherent.
    /// </summary>
    private const int MaximumAvailableTypes = 10;

    /// <summary>
    /// FUN_005FD958 dispatches these three interaction modes. Zero is the
    /// pre-initialization value and anything outside the range is not a coherent
    /// live battle state.
    /// </summary>
    private const int FirstValidInteractionMode = 1;
    private const int LastValidInteractionMode = 3;

    private readonly ILegacyAddressSpace memory;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// The collision triangles, which are copied from vert.bin once when the
    /// battle loads and never change while it runs. Reading twenty-five kilobytes
    /// several times a second to re-learn the same hill would be waste.
    /// </summary>
    private IReadOnlyList<CondorCollisionTriangle>? collisionTriangles;
    private int collisionTriangleCount = -1;
    private bool initializationConfirmed;
    private InitializationCandidate? initializationCandidate;

    public CondorBattleStateReader(
        ILegacyAddressSpace memory,
        TimeProvider? timeProvider = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Forgets cached terrain and readiness, so a new battle proves its own.</summary>
    public void Reset()
    {
        collisionTriangles = null;
        collisionTriangleCount = -1;
        initializationConfirmed = false;
        initializationCandidate = null;
    }

    public CondorBattleSnapshot? TryRead()
    {
        var snapshot = TryReadRaw(out var nativeCollisionCount);
        if (snapshot is null)
        {
            RejectUnconfirmedCandidate();
            return null;
        }

        return ConfirmInitialization(snapshot, nativeCollisionCount);
    }

    private CondorBattleSnapshot? TryReadRaw(out int collisionCount)
    {
        collisionCount = 0;
        if (!TryReadInt32(AddressInteractionMode, out var interactionMode) ||
            !TryReadInt32(AddressModalState, out var modalState) ||
            !TryReadInt16(AddressSettingMenuRow, out var settingMenuRow) ||
            !TryReadInt16(AddressSettingMenuRotation, out var rotation) ||
            !TryReadInt16(AddressSettingMenuCount, out var availableCount) ||
            !TryReadInt32(AddressGil, out var gil) ||
            !TryReadInt16(AddressCursorX, out var cursorX) ||
            !TryReadInt16(AddressCursorY, out var cursorY) ||
            !TryReadUInt16(AddressCursorPlacementLegal, out var placementLegal) ||
            !TryReadInt16(AddressUnitUnderCursor, out var unitUnderCursor) ||
            !TryReadInt32(AddressAlliedCount, out var alliedCount) ||
            !TryReadInt32(AddressEnemyCount, out var enemyCount) ||
            !TryReadInt16(AddressOutcome, out var outcome) ||
            !TryReadInt32(AddressMessageId, out var messageId) ||
            !TryReadInt32(AddressPhase, out var phase) ||
            !TryReadInt16(AddressReportState, out var reportState) ||
            !TryReadInt16(AddressReportMessageCell, out var reportMessageCell) ||
            !TryReadInt16(AddressReportUnitSlot, out var reportUnitSlot) ||
            !TryReadInt16(AddressStartGameSelection, out var startGameSelection) ||
            !TryReadInt16(AddressDirectionSelection, out var directionSelection) ||
            !TryReadInt16(AddressGameSpeed, out var gameSpeed) ||
            !TryReadUInt32(AddressHeldInput, out var heldInput) ||
            !TryReadInt32(AddressDeploymentFrontierY, out var frontierY) ||
            !TryReadInt16(AddressEnemyAdvance, out var enemyAdvance) ||
            !TryReadInt32(AddressCollisionCount, out collisionCount))
        {
            return null;
        }

        var availableTypeIds = ReadAvailableTypeIds(availableCount);
        if (availableTypeIds is null)
        {
            return null;
        }

        if (gameSpeed is < 1 or > 4)
        {
            return null;
        }

        var destinationX = cursorX;
        var destinationY = cursorY;
        if (interactionMode == CondorBattleSnapshot.DestinationInteractionMode &&
            modalState == 0 && reportState == 0)
        {
            if (!TryReadInt16(AddressDestinationX, out var activeDestinationX) ||
                !TryReadInt16(AddressDestinationY, out var activeDestinationY))
            {
                return null;
            }

            destinationX = activeDestinationX;
            destinationY = activeDestinationY;
        }

        var units = ReadUnits();
        if (units is null)
        {
            return null;
        }

        var terrain = ReadCollisionTriangles(collisionCount);
        if (terrain is null)
        {
            return null;
        }

        if (!TryReadAllyUnitMenu(interactionMode, modalState, reportState, out var allyUnitMenu) ||
            !TryReadCrowdedUnitMenu(modalState, units, out var crowdedUnitMenu) ||
            !TryValidateActiveChoice(modalState, startGameSelection, directionSelection) ||
            !TryValidateReport(reportState, reportMessageCell, reportUnitSlot, units) ||
            !TryConfirmDestinationCursor(
                interactionMode,
                modalState,
                reportState,
                destinationX,
                destinationY) ||
            !TryConfirmInterfaceAnchors(interactionMode, modalState, reportState))
        {
            return null;
        }

        return new CondorBattleSnapshot(
            interactionMode,
            modalState,
            settingMenuRow,
            rotation,
            availableTypeIds,
            gil,
            cursorX,
            cursorY,
            placementLegal != 0,
            unitUnderCursor,
            units,
            alliedCount,
            enemyCount,
            outcome,
            messageId,
            phase,
            reportState,
            frontierY,
            enemyAdvance,
            terrain,
            allyUnitMenu,
            startGameSelection,
            crowdedUnitMenu,
            directionSelection,
            reportMessageCell,
            reportUnitSlot)
        {
            DestinationX = destinationX,
            DestinationY = destinationY,
            GameSpeed = gameSpeed,
            HeldDirectionMask = heldInput & HeldDirectionBits
        };
    }

    private bool TryReadAllyUnitMenu(
        int interactionMode,
        int modalState,
        int reportState,
        out CondorAllyUnitMenu? menu)
    {
        menu = null;
        if (interactionMode != CondorBattleSnapshot.AllyUnitInteractionMode ||
            modalState != 0 || reportState != 0)
        {
            return true;
        }

        if (!TryReadByte(AddressAllyUnitCommandCount, out var count) ||
            !TryReadInt16(AddressAllyUnitCommandRow, out var row) ||
            count > MaximumAllyUnitCommands ||
            (count == 0 ? row != 0 : row < 0 || row >= count))
        {
            return false;
        }

        var commandIds = new int[count];
        for (var index = 0; index < count; index++)
        {
            if (!TryReadByte(
                    AddressAllyUnitCommand0 + (uint)(index * AddressAllyUnitCommandStride),
                    out var commandId) ||
                commandId is not (0 or 2 or 3 or 5))
            {
                return false;
            }

            commandIds[index] = commandId;
        }

        // These globals are rebuilt independently. If the player moved the row
        // or closed the list while the translated-memory read was in flight,
        // retry rather than name a command that is no longer highlighted.
        if (!TryReadByte(AddressAllyUnitCommandCount, out var confirmedCount) ||
            !TryReadInt16(AddressAllyUnitCommandRow, out var confirmedRow) ||
            confirmedCount != count || confirmedRow != row)
        {
            return false;
        }

        menu = new CondorAllyUnitMenu(row, commandIds);
        return true;
    }

    private bool TryReadCrowdedUnitMenu(
        int modalState,
        IReadOnlyList<CondorBattleUnit> units,
        out CondorCrowdedUnitMenu? menu)
    {
        menu = null;
        if (modalState != CondorBattleSnapshot.CrowdedUnitModalState)
        {
            return true;
        }

        if (!TryReadInt16(AddressCrowdedUnitCount, out var count) ||
            !TryReadInt16(AddressCrowdedUnitRow, out var row) ||
            count is < 2 or > UnitSlots || row < 0 || row >= count)
        {
            return false;
        }

        var slots = new int[count];
        for (var index = 0; index < count; index++)
        {
            if (!TryReadUInt32(AddressCrowdedUnitPointers + (uint)(index * 8), out var pointer) ||
                pointer < AddressLiveUnits)
            {
                return false;
            }

            var offset = pointer - AddressLiveUnits;
            if (offset % UnitStride != 0)
            {
                return false;
            }

            var slot = (int)(offset / UnitStride);
            if (slot is < 0 or >= UnitSlots || units.All(unit => unit.Slot != slot))
            {
                return false;
            }

            slots[index] = slot;
        }

        if (!TryReadInt16(AddressCrowdedUnitCount, out var confirmedCount) ||
            !TryReadInt16(AddressCrowdedUnitRow, out var confirmedRow) ||
            confirmedCount != count || confirmedRow != row)
        {
            return false;
        }

        menu = new CondorCrowdedUnitMenu(row, slots);
        return true;
    }

    private bool TryValidateActiveChoice(
        int modalState,
        int startGameSelection,
        int directionSelection)
    {
        if (modalState == CondorBattleSnapshot.StartGameModalState &&
            (startGameSelection is not (0 or 0x10) ||
             !TryReadInt16(AddressStartGameSelection, out var confirmedStart) ||
             confirmedStart != startGameSelection))
        {
            return false;
        }

        if (modalState is not (
                CondorBattleSnapshot.NewUnitDirectionModalState or
                CondorBattleSnapshot.CommandDirectionModalState))
        {
            return true;
        }

        return directionSelection is >= 0 and <= 0x400 &&
               directionSelection % 0x20 == 0 &&
               TryReadInt16(AddressDirectionSelection, out var confirmedDirection) &&
               confirmedDirection == directionSelection;
    }

    private bool TryValidateReport(
        int reportState,
        int reportMessageCell,
        int reportUnitSlot,
        IReadOnlyList<CondorBattleUnit> units)
    {
        if (reportState == 0)
        {
            return true;
        }

        // FUN_006027C2 publishes state = textureCell + 1 and associates one
        // live slot with the report. A mismatch is a read through the middle of
        // that native writer, not a meaningful overlay.
        if (reportMessageCell is not (0 or 3 or 10) ||
            reportState != reportMessageCell + 1 ||
            reportUnitSlot is < 0 or >= UnitSlots ||
            units.All(unit => unit.Slot != reportUnitSlot))
        {
            return false;
        }

        return TryReadInt16(AddressReportState, out var confirmedState) &&
               TryReadInt16(AddressReportMessageCell, out var confirmedCell) &&
               TryReadInt16(AddressReportUnitSlot, out var confirmedSlot) &&
               confirmedState == reportState &&
               confirmedCell == reportMessageCell &&
               confirmedSlot == reportUnitSlot;
    }

    private bool TryConfirmInterfaceAnchors(
        int interactionMode,
        int modalState,
        int reportState) =>
        TryReadInt32(AddressInteractionMode, out var confirmedMode) &&
        TryReadInt32(AddressModalState, out var confirmedModal) &&
        TryReadInt16(AddressReportState, out var confirmedReport) &&
        confirmedMode == interactionMode &&
        confirmedModal == modalState &&
        confirmedReport == reportState;

    private bool TryConfirmDestinationCursor(
        int interactionMode,
        int modalState,
        int reportState,
        int destinationX,
        int destinationY)
    {
        if (interactionMode != CondorBattleSnapshot.DestinationInteractionMode ||
            modalState != 0 || reportState != 0)
        {
            return true;
        }

        // The x64 runtime resolves each guest read through a translated page
        // table. Re-read the adjacent pair so a game-frame update between the
        // two reads cannot turn into a destination the game never displayed.
        return TryReadInt16(AddressDestinationX, out var confirmedX) &&
               TryReadInt16(AddressDestinationY, out var confirmedY) &&
               confirmedX == destinationX &&
               confirmedY == destinationY;
    }

    /// <summary>
    /// Refuses the short phase-one window in which geometry exists but the
    /// module's unit/cursor initializer has not necessarily finished. A later
    /// phase is already initialized and is accepted immediately: observing
    /// setup is never a prerequisite for speaking a battle that was joined
    /// after setup had ended.
    /// </summary>
    private CondorBattleSnapshot? ConfirmInitialization(
        CondorBattleSnapshot snapshot,
        int nativeCollisionCount)
    {
        if (initializationConfirmed)
        {
            return snapshot;
        }

        if (nativeCollisionCount is <= 0 or > MaximumCollisionRecords ||
            snapshot.InteractionMode is < FirstValidInteractionMode or > LastValidInteractionMode)
        {
            initializationCandidate = null;
            return null;
        }

        if (snapshot.Phase != CondorPlacementRegion.SetupPhase)
        {
            initializationConfirmed = true;
            initializationCandidate = null;
            return snapshot;
        }

        var signature = new InitializationSignature(
            snapshot.Phase,
            snapshot.InteractionMode,
            nativeCollisionCount);
        var now = timeProvider.GetTimestamp();
        if (initializationCandidate is not { } candidate ||
            candidate.Signature != signature)
        {
            initializationCandidate = new InitializationCandidate(signature, now);
            return null;
        }

        if (timeProvider.GetElapsedTime(candidate.ObservedAt, now) < ReadInterval)
        {
            return null;
        }

        initializationConfirmed = true;
        initializationCandidate = null;
        return snapshot;
    }

    private void RejectUnconfirmedCandidate()
    {
        if (!initializationConfirmed)
        {
            initializationCandidate = null;
        }
    }

    /// <summary>
    /// The battlefield's collision triangles, cached for the life of the battle.
    /// </summary>
    private IReadOnlyList<CondorCollisionTriangle>? ReadCollisionTriangles(int count)
    {
        if (count is <= 0 or > MaximumCollisionRecords)
        {
            // Module 9 becomes observable before its battlefield initialization
            // finishes. The working gil and unit state are not coherent until the
            // collision records have been loaded, so this is not a valid empty
            // battlefield snapshot and must not be spoken - it is what announced
            // "0 gil. 0 units. 0 enemies. cursor at 0, 0." on 2026-08-22.
            //
            // Returning the cache rather than a bare null, deliberately. Before
            // this battle's geometry has ever loaded the cache is null and the
            // snapshot is correctly refused. Once it has loaded, a count that
            // later reads zero must NOT silence the reader: the battle result is
            // announced from a module 9 snapshot, and losing "Battle won" is a
            // far worse outcome than carrying a triangle list that cannot go
            // stale inside one battle anyway - the terrain is deliberately cached
            // for the life of the battle, and Reset drops it on the way out.
            return collisionTriangles;
        }

        if (collisionTriangles is not null && collisionTriangleCount == count)
        {
            return collisionTriangles;
        }

        var triangles = new CondorCollisionTriangle[count];
        Span<byte> record = stackalloc byte[CollisionRecordStride];
        for (var index = 0; index < count; index++)
        {
            if (!memory.TryRead(AddressCollisionRecords + (uint)(index * CollisionRecordStride), record))
            {
                return null;
            }

            var ax = BitConverter.ToInt16(record[0x28..]);
            var ay = BitConverter.ToInt16(record[0x2A..]);
            var bx = BitConverter.ToInt16(record[0x30..]);
            var by = BitConverter.ToInt16(record[0x32..]);
            var cx = BitConverter.ToInt16(record[0x38..]);
            var cy = BitConverter.ToInt16(record[0x3A..]);

            // The record carries its own inclusive bounds, biased by 0x4000. The
            // game applies them before the triangle test and every record in the
            // shipped file satisfies bound = 0x4000 + the matching extreme, so
            // they are used as stored rather than recomputed.
            triangles[index] = new CondorCollisionTriangle(
                ax, ay, bx, by, cx, cy,
                BitConverter.ToInt16(record[0x40..]) - 0x4000,
                BitConverter.ToInt16(record[0x42..]) - 0x4000,
                BitConverter.ToInt16(record[0x44..]) - 0x4000,
                BitConverter.ToInt16(record[0x46..]) - 0x4000);
        }

        collisionTriangles = triangles;
        collisionTriangleCount = count;
        return triangles;
    }

    private IReadOnlyList<int>? ReadAvailableTypeIds(int count)
    {
        // Outside the Setting Menu the count is whatever the last build left
        // behind, so an out-of-range value is normal rather than a fault. An
        // empty list is the honest answer; it stops the menu reader from
        // indexing a list that is not there.
        if (count is <= 0 or > MaximumAvailableTypes)
        {
            return Array.Empty<int>();
        }

        Span<byte> buffer = stackalloc byte[MaximumAvailableTypes];
        var ids = buffer[..count];
        if (!memory.TryRead(AddressAvailableTypeIds, ids))
        {
            return null;
        }

        var result = new int[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = ids[index];
        }

        return result;
    }

    private IReadOnlyList<CondorBattleUnit>? ReadUnits()
    {
        var units = new List<CondorBattleUnit>();
        Span<byte> record = stackalloc byte[UnitStride];

        for (var slot = 0; slot < UnitSlots; slot++)
        {
            if (!memory.TryRead(AddressLiveUnits + (uint)(slot * UnitStride), record))
            {
                return null;
            }

            var allocated = BitConverter.ToUInt16(record[UnitAllocated..]);
            if (allocated == 0)
            {
                continue;
            }

            var currentHp = record[UnitCurrentHp];
            var removalState = (sbyte)record[UnitRemovalState];

            units.Add(new CondorBattleUnit(
                slot,
                slot >= FirstEnemySlot,
                BitConverter.ToUInt16(record[UnitTypeId..]),
                currentHp,
                record[UnitMaximumHp],
                record[UnitAttack],
                BitConverter.ToInt16(record[UnitX..]),
                BitConverter.ToInt16(record[UnitY..]),
                currentHp == 0 || removalState != 0,
                record[UnitWidth],
                record[UnitHeightAbove],
                removalState != 0));
        }

        return units;
    }

    private bool TryReadInt32(uint address, out int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt32(buffer);
        return true;
    }

    private bool TryReadInt16(uint address, out short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt16(buffer);
        return true;
    }

    private bool TryReadUInt16(uint address, out ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt16(buffer);
        return true;
    }

    private bool TryReadUInt32(uint address, out uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt32(buffer);
        return true;
    }

    private bool TryReadByte(uint address, out byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = buffer[0];
        return true;
    }

    private readonly record struct InitializationSignature(
        int Phase,
        int InteractionMode,
        int CollisionCount);

    private readonly record struct InitializationCandidate(
        InitializationSignature Signature,
        long ObservedAt);
}
