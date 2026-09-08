using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Where a named field entity's model actually is and which way it is actually
/// pointing, right now.
///
/// <para><paramref name="Direction"/> is the model's own facing byte, a full turn in
/// 256 units, taken from the render model table rather than from whatever a script
/// last asked for. A hand that has been told to move to a new hour reads its new hour
/// in the script bank immediately and reaches it on screen some frames later; a player
/// watching the clock sees the second, not the first.</para>
/// </summary>
/// <param name="AnimationId">
/// The animation the model is playing, or -1 when it has none. This is what a sighted
/// player is actually watching when a character is tied to a chair and working a limb
/// loose: the pose changes and the coordinates do not.
/// </param>
public readonly record struct FieldActivityModelState(
    bool HasModel,
    bool IsVisible,
    int X,
    int Y,
    int Z,
    int Triangle,
    int Direction,
    int AnimationId = -1);

/// <summary>
/// How an attempt to look at an entity turned out. The three are deliberately
/// separate: a model the field is not drawing is a fact about the room, and a read
/// that could not be trusted is not. Collapsing the second into the first is how a
/// readout ends up telling a player a corridor is clear because memory was busy.
/// </summary>
public enum FieldActivityReadStatus
{
    /// <summary>The read failed or two samples disagreed. Nothing is known.</summary>
    Unreadable = 0,

    /// <summary>The entity has no model, or its model is not being drawn.</summary>
    Hidden = 1,

    /// <summary>The model is on screen and its pose was read coherently.</summary>
    Visible = 2
}

/// <summary>
/// A native numeric window - the kind the cliff fields draw a body temperature in.
/// The value is stored separately from the text a window carries, so a reader that
/// only sees strings gets the word "Degrees" and never the number in front of it.
/// </summary>
/// <param name="IsUsable">
/// The window is open, is a numeric window, and its value was read coherently. A stale
/// bank value read outside the climb is not a temperature on screen.
/// </param>
public readonly record struct FieldActivityNumericWindow(
    bool IsUsable,
    int Value,
    int DigitLimit);

/// <summary>One entity's pose, together with whether it could be read at all.</summary>
public readonly record struct FieldActivityModelReading(
    int EntityId,
    FieldActivityReadStatus Status,
    FieldActivityModelState Model)
{
    public static FieldActivityModelReading Unreadable(int entityId) =>
        new(entityId, FieldActivityReadStatus.Unreadable, default);
}

/// <summary>
/// A place in a script where the field stops and waits for a button, identified by the
/// opcode that does the waiting.
/// </summary>
/// <param name="Offset">The wait opcode's own offset inside its script.</param>
/// <param name="Opcode">
/// <c>IFKEY</c> (0x30) for a held key or <c>IFKEYON</c> (0x31) for a fresh press.
/// </param>
/// <param name="KeyMask">The key bit field the opcode tests.</param>
/// <param name="LoopStart">First offset of the loop the field circles while waiting.</param>
/// <param name="LoopEnd">Last offset of that loop, inclusive.</param>
public readonly record struct FieldActivityWaitAnchor(
    int Offset,
    byte Opcode,
    int KeyMask,
    int LoopStart,
    int LoopEnd);

/// <summary>
/// Which of an entity's waits, if any, its script is actually sitting in.
/// </summary>
/// <param name="IsReadable">
/// The cursor was read coherently and the script it points into was proved to be the
/// expected one. False means nothing is known, which is not the same as not waiting.
/// </param>
/// <param name="WaitIndex">
/// Index into the anchors supplied, or -1 when the script is somewhere else - which is
/// the automatic part of the same script.
/// </param>
public readonly record struct FieldActivityWaitState(
    bool IsReadable,
    int WaitIndex,
    int RelativeOffset)
{
    public bool IsWaiting => IsReadable && WaitIndex >= 0;

    public static readonly FieldActivityWaitState Unreadable = new(false, -1, -1);
}

/// <summary>
/// The live half of the field activity readouts: model poses and script positions read
/// straight out of the running game.
///
/// Every address here is one this project already relies on elsewhere. The entity to
/// model byte array, the 0x88-stride object table behind the pointer at 0x00CC0B60 and
/// its visibility byte are <see cref="FieldScriptControllerReader"/>'s; the walkmesh
/// coordinates at 0x0C/0x10/0x14 and the triangle at 0x78 in that same record, and the
/// facing byte at 0x1C in the 400-stride render table behind 0x00CFF738, are
/// <see cref="FieldPositionReader"/>'s.
///
/// The per-entity program counter at 0x00CC0CF8 is the one the native conditional
/// handlers use: FUN_0061171f reads the word at <c>0xcc0cf8 + entity * 2</c>, adds the
/// branch it is taking and writes it back. It is an offset into the script section, in
/// the same units as the entry points in that section's own header, so subtracting the
/// running script's entry point gives the offset a decompiler prints.
///
/// Both kinds of read are double-sampled with their native identity included in the
/// comparison, and the identity is everything the answer was derived through: for a
/// pose, the script section, the entity and model counts, the model index and both the
/// object and render table pointers; for a cursor, the script section, entity count,
/// priority, running script, entry point and counter. A field reload between two
/// samples changes a pointer or a count even when the pose bytes happen to match, and a
/// model index left over from the field before this one is outside the count this field
/// actually has. On any disagreement the answer is "unreadable", never a guess.
/// </summary>
public sealed class FieldActivityStateReader
{
    /// <summary>The per-entity script program counter array.</summary>
    public const int AddressEntityProgramCounters = 0x00CC0CF8;

    /// <summary>
    /// The native window records. WSPCL (0x36, handler 0061FD5C) writes the display
    /// type at 0x00CFF5D3 + window * 0x30 and the digit limit at 0x00CFF5D5 + stride;
    /// WNUMB (0x37, handler 0061FE26) writes the displayed signed value at
    /// 0x00CFF5D8 + stride. A window is only on screen while its own state byte at
    /// 0x00CC0961 + window is not the free value.
    /// </summary>
    public const int AddressWindowDisplayType = 0x00CFF5D3;
    public const int AddressWindowDigitLimit = 0x00CFF5D5;
    public const int AddressWindowNumericValue = 0x00CFF5D8;
    public const int WindowRecordStride = 0x30;
    /// <summary>
    /// The per-window owner array. Base 0x00CC0960 indexed by the window's own id, which
    /// is what 631586 reads and writes and what <see cref="FieldMessageReader"/> already
    /// uses. Starting a byte later meant window one's temperature was decided by whether
    /// window two happened to be open: a real reading vanished whenever the neighbour was
    /// free, and a closed one spoke stale degrees whenever the neighbour was not.
    /// </summary>
    public const int AddressWindowStates = 0x00CC0960;

    /// <summary>The display type a numeric window carries.</summary>
    public const int NumericWindowDisplayType = 2;

    /// <summary>Window initialisation resets the state byte to this.</summary>
    public const int FreeWindowState = 0xFF;

    /// <summary>Field script section header offsets.</summary>
    public const int ScriptHeaderEntityCountOffset = 2;
    public const int ScriptHeaderAkaoCountOffset = 6;
    public const int ScriptHeaderEntityNamesOffset = 32;
    public const int ScriptHeaderEntityNameLength = 8;
    /// <summary>
    /// Entry points per entity in the script section header. Thirty-two, not eight: the
    /// eight is the priority table the running script id is looked up in, which is a
    /// different table. The field data itself settles it - every entity in the installed
    /// fields carries thirty-two routine offsets - and so does the party-member request
    /// opcode, whose function number is five bits wide.
    /// </summary>
    public const int ScriptEntryPointsPerEntity = 32;

    /// <summary>
    /// Field object coordinates are stored shifted left by twelve, exactly as
    /// <see cref="FieldPositionReader"/> reads them for navigation.
    /// </summary>
    public const int FieldPositionFixedPointShift = 12;

    /// <summary>
    /// A ceiling on the object table, used only to reject nonsense before the field's
    /// own model count has been read. The count itself is the real bound.
    /// </summary>
    public const int MaximumModelCount = 64;

    private readonly ILegacyAddressSpace memory;

    public FieldActivityStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>
    /// Reads one entity's model pose while the named field is the one actually loaded.
    /// The result always says which of the three things happened.
    /// </summary>
    public FieldActivityModelReading ReadModel(int fieldId, int entityId)
    {
        if (entityId < 0)
        {
            return FieldActivityModelReading.Unreadable(entityId);
        }

        if (!TryCaptureModel(fieldId, entityId, out var before) ||
            !TryCaptureModel(fieldId, entityId, out var after) ||
            before != after)
        {
            return FieldActivityModelReading.Unreadable(entityId);
        }

        return new FieldActivityModelReading(
            entityId,
            before.State is { HasModel: true, IsVisible: true }
                ? FieldActivityReadStatus.Visible
                : FieldActivityReadStatus.Hidden,
            before.State);
    }

    /// <summary>
    /// The number a native numeric window is currently showing.
    ///
    /// Everything has to agree before this is a number on screen: the named field is
    /// loaded, the window's own state says it is open, and its display type is the
    /// numeric one. Reading the script bank instead would hand back the last
    /// temperature of a climb the party is no longer on.
    /// </summary>
    public FieldActivityNumericWindow ReadNumericWindow(int fieldId, int windowId)
    {
        // Sampled twice with the field's own identity in the comparison, like every
        // other read here. Checking the field once at the start and then reading the
        // window is how a number from the room the party has just left gets spoken as
        // if it were still on screen.
        if (!TryCaptureNumericWindow(fieldId, windowId, out var before) ||
            !TryCaptureNumericWindow(fieldId, windowId, out var after) ||
            before != after)
        {
            return default;
        }

        return before.Window;
    }

    private bool TryCaptureNumericWindow(int fieldId, int windowId, out NumericWindowCapture capture)
    {
        capture = default;
        if (windowId < 0 ||
            !TryReadFieldIdentity(fieldId, out var scriptSection, out var entityCount))
        {
            return false;
        }

        var stride = (uint)(windowId * WindowRecordStride);
        if (!memory.TryReadByte((uint)(AddressWindowStates + windowId), out var owner))
        {
            return false;
        }

        if (owner == FreeWindowState)
        {
            // Nobody owns it, so nothing is on screen. That is a fact about the room,
            // and it is reported as "no window" rather than as a temperature of zero.
            capture = new NumericWindowCapture(scriptSection, entityCount, owner, default);
            return true;
        }

        if (!memory.TryReadByte((uint)AddressWindowDisplayType + stride, out var displayType) ||
            !memory.TryReadByte((uint)AddressWindowDigitLimit + stride, out var digitLimit) ||
            !memory.TryReadInt32((uint)AddressWindowNumericValue + stride, out var value))
        {
            return false;
        }

        capture = new NumericWindowCapture(
            scriptSection,
            entityCount,
            owner,
            displayType == NumericWindowDisplayType
                ? new FieldActivityNumericWindow(true, value, digitLimit)
                : default);
        return true;
    }

    /// <summary>
    /// Which of the supplied waits the entity's script is sitting in.
    ///
    /// The script's identity is proved rather than assumed: each anchor names the
    /// opcode and key mask the wait is made of, and the bytes at that offset in the
    /// script the cursor points into have to match. A wrong entry point, a different
    /// script or a torn read all fail that check, and all of them answer "unreadable".
    /// </summary>
    public FieldActivityWaitState ReadWaitState(
        int fieldId,
        int entityId,
        IReadOnlyList<FieldActivityWaitAnchor> anchors)
    {
        if (entityId < 0 || anchors is null || anchors.Count == 0)
        {
            return FieldActivityWaitState.Unreadable;
        }

        if (!TryCaptureCursor(fieldId, entityId, out var before) ||
            !TryCaptureCursor(fieldId, entityId, out var after) ||
            before != after ||
            !before.IsRunning)
        {
            return FieldActivityWaitState.Unreadable;
        }

        var relative = before.ProgramCounter - before.EntryOffset;
        if (relative < 0)
        {
            return FieldActivityWaitState.Unreadable;
        }

        // Prove the entry point found the script these anchors belong to before any of
        // them is believed. One anchor is enough: its opcode and key mask together are
        // four specific bytes at a specific place.
        var verified = false;
        foreach (var anchor in anchors)
        {
            if (TryVerifyAnchor(before.ScriptSection, before.EntryOffset, anchor))
            {
                verified = true;
                break;
            }
        }

        if (!verified)
        {
            return FieldActivityWaitState.Unreadable;
        }

        for (var index = 0; index < anchors.Count; index++)
        {
            var anchor = anchors[index];
            if (relative >= anchor.LoopStart && relative <= anchor.LoopEnd)
            {
                return new FieldActivityWaitState(true, index, relative);
            }
        }

        // Readable, and demonstrably not in any of the waits: this is the automatic
        // part of the same script.
        return new FieldActivityWaitState(true, -1, relative);
    }

    private bool TryVerifyAnchor(uint scriptSection, uint entryOffset, FieldActivityWaitAnchor anchor)
    {
        var address = scriptSection + entryOffset + (uint)anchor.Offset;
        if (address < scriptSection ||
            !memory.TryReadByte(address, out var opcode) ||
            opcode != anchor.Opcode ||
            !memory.TryReadUInt16(address + 1, out var mask))
        {
            return false;
        }

        return mask == anchor.KeyMask;
    }

    private bool TryCaptureModel(int fieldId, int entityId, out ModelCapture capture)
    {
        capture = default;
        if (!TryReadFieldIdentity(fieldId, out var scriptSection, out var entityCount) ||
            entityId >= entityCount)
        {
            return false;
        }

        // How many models this field actually has right now. An entity-to-model byte
        // left over from the field before this one can still point at a plausible-
        // looking record, and without this the pose read out of it would be believed.
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var modelCount) ||
            modelCount == 0 ||
            modelCount > MaximumModelCount)
        {
            return false;
        }

        if (!memory.TryReadByte(
                (uint)(FieldScriptControllerReader.AddressEntityModelIds + entityId),
                out var modelId))
        {
            return false;
        }

        if (!memory.TryReadUInt32((uint)FieldScriptControllerReader.AddressModelTablePointer, out var objectTable) ||
            objectTable == 0)
        {
            return false;
        }

        if (modelId == FieldScriptControllerReader.NoModel)
        {
            capture = new ModelCapture(scriptSection, entityCount, modelCount, modelId, objectTable, 0, default);
            return true;
        }

        if (modelId >= modelCount)
        {
            return false;
        }

        var objectBase = objectTable + (uint)(modelId * FieldScriptControllerReader.ModelStride);
        if (objectBase < objectTable ||
            !memory.TryReadByte(objectBase + FieldScriptControllerReader.ModelVisibilityOffset, out var visibility) ||
            !memory.TryReadInt32(objectBase + FieldPositionReader.ObjectXOffset, out var x) ||
            !memory.TryReadInt32(objectBase + FieldPositionReader.ObjectYOffset, out var y) ||
            !memory.TryReadInt32(objectBase + FieldPositionReader.ObjectZOffset, out var z) ||
            !memory.TryReadUInt16(objectBase + FieldPositionReader.ObjectTriangleOffset, out var triangle))
        {
            return false;
        }

        // The facing byte lives in the render table, which is a different table with a
        // different stride from the one the coordinates come out of. Its pointer is part
        // of the identity as well: the field can be reloaded between two samples and
        // hand back the same pose values from a different table.
        if (!memory.TryReadUInt32((uint)FieldPositionReader.AddressFieldModelsPtr, out var renderTable) ||
            renderTable == 0)
        {
            return false;
        }

        var renderBase = renderTable + (uint)(modelId * FieldPositionReader.FieldModelStride);
        if (renderBase < renderTable ||
            !memory.TryReadByte(renderBase + FieldPositionReader.ModelDirectionOffset, out var direction))
        {
            return false;
        }

        if (!memory.TryReadByte(
                objectBase + FieldScriptControllerReader.ModelAnimationIdOffset,
                out var animationId))
        {
            return false;
        }

        capture = new ModelCapture(
            scriptSection,
            entityCount,
            modelCount,
            modelId,
            objectTable,
            renderTable,
            new FieldActivityModelState(
                HasModel: true,
                IsVisible: visibility != 0,
                X: x >> FieldPositionFixedPointShift,
                Y: y >> FieldPositionFixedPointShift,
                Z: z >> FieldPositionFixedPointShift,
                Triangle: triangle,
                Direction: direction,
                // Signed, as the native handler sign-extends it.
                AnimationId: (sbyte)animationId));
        return true;
    }

    private bool TryCaptureCursor(int fieldId, int entityId, out CursorCapture capture)
    {
        capture = default;
        if (!TryReadFieldIdentity(fieldId, out var scriptSection, out var entityCount) ||
            entityId >= entityCount)
        {
            return false;
        }

        if (!memory.TryReadByte(
                (uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entityId),
                out var priority) ||
            priority >= FieldScriptControllerReader.ScriptSlotsPerEntity)
        {
            capture = new CursorCapture(scriptSection, entityCount, priority, false, 0, 0, 0);
            return true;
        }

        if (!memory.TryReadByte(
                (uint)(FieldScriptControllerReader.AddressEntityScriptIds +
                    (entityId * FieldScriptControllerReader.ScriptSlotsPerEntity) + priority),
                out var runningScriptId) ||
            runningScriptId >= ScriptEntryPointsPerEntity)
        {
            capture = new CursorCapture(scriptSection, entityCount, priority, false, 0, 0, 0);
            return true;
        }

        if (!memory.TryReadUInt16(
                (uint)(AddressEntityProgramCounters + (entityId * 2)),
                out var programCounter))
        {
            return false;
        }

        // The entry points sit after the header, the per-entity names and the akao
        // offset table, which is the field script section's own documented layout.
        if (!memory.TryReadUInt16(scriptSection + ScriptHeaderAkaoCountOffset, out var akaoCount))
        {
            return false;
        }

        var entryTable = scriptSection +
            ScriptHeaderEntityNamesOffset +
            (uint)(entityCount * ScriptHeaderEntityNameLength) +
            (uint)(akaoCount * 4);
        var entryAddress = entryTable +
            (uint)(((entityId * ScriptEntryPointsPerEntity) + runningScriptId) * 2);
        if (entryAddress < entryTable ||
            !memory.TryReadUInt16(entryAddress, out var entryOffset))
        {
            return false;
        }

        capture = new CursorCapture(
            scriptSection,
            entityCount,
            priority,
            true,
            runningScriptId,
            entryOffset,
            programCounter);
        return true;
    }

    private bool TryReadFieldIdentity(int fieldId, out uint scriptSection, out byte entityCount)
    {
        scriptSection = 0;
        entityCount = 0;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var currentFieldId) ||
            module != FieldPositionReader.FieldModule ||
            currentFieldId != fieldId)
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                (uint)FieldScriptControllerReader.AddressFieldScriptPointer,
                out scriptSection) ||
            scriptSection == 0 ||
            !memory.TryReadByte(scriptSection + ScriptHeaderEntityCountOffset, out entityCount) ||
            entityCount == 0)
        {
            scriptSection = 0;
            return false;
        }

        return true;
    }

    private readonly record struct NumericWindowCapture(
        uint ScriptSection,
        byte EntityCount,
        byte Owner,
        FieldActivityNumericWindow Window);

    private readonly record struct ModelCapture(
        uint ScriptSection,
        byte EntityCount,
        byte ModelCount,
        byte ModelId,
        uint ObjectTable,
        uint RenderTable,
        FieldActivityModelState State);

    private readonly record struct CursorCapture(
        uint ScriptSection,
        byte EntityCount,
        byte Priority,
        bool IsRunning,
        byte RunningScriptId,
        ushort EntryOffset,
        ushort ProgramCounter);
}
