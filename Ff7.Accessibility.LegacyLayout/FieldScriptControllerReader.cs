using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Which script a named field entity is running right now, and what its model is
/// visibly doing. This is the identity every arcade readout needs: the field temp
/// bank is shared by every script in the room, so a plausible-looking set of bytes
/// is not evidence that a particular minigame owns them.
/// </summary>
/// <param name="IsControllerActive">
/// The named entity is executing the named script at some priority.
/// </param>
/// <param name="HasModel">
/// The entity has a field model at all. This is only the entity-to-model mapping;
/// it says nothing about whether that model is being drawn.
/// </param>
/// <param name="IsModelVisible">
/// The model's own visibility byte is set. A minigame readout that describes a pose
/// needs this, not merely <paramref name="HasModel"/>: an entity keeps its model
/// through <c>VISI</c> off.
/// </param>
/// <param name="AnimationId">
/// The model's current animation, or -1 when it has none. Signed, because the native
/// handler sign-extends this byte.
/// </param>
/// <param name="CurrentFrameFixed">
/// The current frame in the native <c>frame &lt;&lt; 4</c> units the handler writes.
/// </param>
/// <param name="EndFrameIndex">
/// The last frame of the running segment, as a plain frame index. This is *not* in
/// the same units as <paramref name="CurrentFrameFixed"/>.
/// </param>
/// <param name="AnimationRunState">The model's animation run state.</param>
public readonly record struct FieldScriptControllerState(
    bool IsControllerActive,
    bool HasModel,
    bool IsModelVisible,
    int AnimationId,
    int CurrentFrameFixed,
    int EndFrameIndex,
    int AnimationRunState)
{
    /// <summary>
    /// True when the model is displaying the given animation whose segment ends on
    /// <paramref name="endFrame"/>, and that model is actually being drawn. The end
    /// frame is what separates the several partial plays of one animation from each
    /// other, and it is a plain frame index, exactly as the native handler stores it.
    /// </summary>
    public bool IsPlayingSegment(int animationId, int endFrame) =>
        HasModel &&
        IsModelVisible &&
        AnimationId == animationId &&
        EndFrameIndex == endFrame;

    /// <summary>
    /// True once the segment has reached its last frame and is being held there.
    ///
    /// The comparison is the updater's own: FUN_0060CE26 reads the current frame at
    /// model + 0x68, arithmetic-shifts it right by four, and compares that against
    /// the plain end index at model + 0x6A. On reaching it the updater sets the run
    /// state to 4 and writes the end index back shifted left by four, so a held pose
    /// reads as current == end &lt;&lt; 4.
    /// </summary>
    public bool HasSegmentSettled(int animationId, int endFrame) =>
        IsPlayingSegment(animationId, endFrame) &&
        (CurrentFrameFixed >> FieldScriptControllerReader.FrameFixedShift) >= EndFrameIndex;
}

/// <summary>
/// Reads the native field script's own controller and model state.
///
/// Every address here is either already used elsewhere in this project or verified
/// against the installed executable. FUN_00614E3E, the CANM!2 handler, is the
/// clearest single witness for the layout: it indexes the entity-to-model byte array
/// at 0x00CBFB70 by the current entity, treats 0xFF as "no model", switches on the
/// animation run state at 0x00CC0980[model], and writes the animation id to
/// model + 0x64, the rate to + 0x66, the current frame to + 0x68 and the segment end
/// to + 0x6A, through the model table pointer at 0x00CC0B60 with stride 0x88.
///
/// The two frame fields are in *different* units, which is easy to get wrong and was
/// wrong here before. The handler assigns model + 0x68 as the start frame shifted
/// left by four, and model + 0x6A as min(animationLength - 1, endFrame / slowness)
/// with no shift at all. FUN_0060CE26, the per-frame updater, confirms it from the
/// other side: it compares the sign-extended model + 0x6A against the sign-extended
/// model + 0x68 shifted arithmetically right by four. For the basketball wind-up's
/// <c>BC 0C 08 10 01</c> the end word is 16, never 256.
///
/// Both frame fields are read signed, as the native code reads them. A negative end
/// index is not a very large unsigned frame; it is an invalid record, and is
/// reported as such rather than being believed.
///
/// The script identity follows the pattern already established by
/// <c>SquatMinigameStateReader</c>: an entity's current priority lives at
/// 0x00CC0B30[entity] and the script running at that priority at
/// 0x00CBF9E8[entity * 8 + priority]. Two captures must agree, and they agree on the
/// native identity - script table pointer, entity count, priority, running script and
/// model index - as well as the derived pose, so two different entities or a reloaded
/// field cannot pass as stable merely by yielding the same few pose values.
///
/// The globally current entity at 0x00CC0964 is deliberately *not* used to decide
/// whether a controller is active: a worker can observe the room while the VM is
/// part-way through some other entity, and the controller would look inactive.
/// </summary>
public sealed class FieldScriptControllerReader
{
    public const int ScriptSlotsPerEntity = 8;
    public const uint FieldScriptEntityCountOffset = 2;
    public const int NoModel = 0xFF;

    /// <summary>The handler stores the current frame as <c>frame &lt;&lt; 4</c>.</summary>
    public const int FrameFixedShift = 4;

    public const int AddressFieldScriptPointer = 0x00CBF5E8;
    public const int AddressEntityScriptIds = 0x00CBF9E8;
    public const int AddressEntityScriptPriorities = 0x00CC0B30;

    /// <summary>Entity to model index; 0xFF means the entity has no model.</summary>
    public const int AddressEntityModelIds = 0x00CBFB70;
    public const int AddressModelTablePointer = 0x00CC0B60;
    public const int AddressModelAnimationRunState = 0x00CC0980;
    public const int ModelStride = 0x88;

    public const int ModelVisibilityOffset = 0x62;
    public const int ModelAnimationIdOffset = 0x64;
    public const int ModelAnimationRateOffset = 0x66;
    public const int ModelCurrentFrameOffset = 0x68;
    public const int ModelEndFrameOffset = 0x6A;

    private readonly ILegacyAddressSpace memory;

    public FieldScriptControllerReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>The native fixed-point form of a script frame index.</summary>
    public static int ToFixedFrame(int frame) => frame << FrameFixedShift;

    /// <summary>
    /// Reads the controller state for one entity/script pair. Returns false on an
    /// unreadable or torn frame, which is silence rather than an implied reset.
    /// </summary>
    public bool TryRead(
        int fieldId,
        int entityId,
        int scriptId,
        out FieldScriptControllerState state)
    {
        state = default;
        if (entityId < 0 || scriptId < 0)
        {
            return false;
        }

        if (!TryCapture(fieldId, entityId, scriptId, out var before) ||
            !TryCapture(fieldId, entityId, scriptId, out var after) ||
            before != after)
        {
            return false;
        }

        state = before.State;
        return true;
    }

    private bool TryCapture(
        int fieldId,
        int entityId,
        int scriptId,
        out Capture capture)
    {
        capture = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var currentFieldId))
        {
            return false;
        }

        if (module != FieldPositionReader.FieldModule || currentFieldId != fieldId)
        {
            capture = new Capture(module, currentFieldId, 0, 0, byte.MaxValue, byte.MaxValue, NoModel, default);
            return true;
        }

        // The script table has to exist and actually contain this entity before any
        // per-entity slot can be trusted.
        if (!memory.TryReadUInt32((uint)AddressFieldScriptPointer, out var scriptPointer) ||
            scriptPointer == 0 ||
            scriptPointer > uint.MaxValue - FieldScriptEntityCountOffset ||
            !memory.TryReadByte(scriptPointer + FieldScriptEntityCountOffset, out var entityCount) ||
            entityCount <= entityId)
        {
            return false;
        }

        if (!memory.TryReadByte((uint)(AddressEntityScriptPriorities + entityId), out var priority))
        {
            return false;
        }

        var runningScriptId = byte.MaxValue;
        var isControllerActive = false;
        if (priority < ScriptSlotsPerEntity)
        {
            if (!memory.TryReadByte(
                    (uint)(AddressEntityScriptIds + (entityId * ScriptSlotsPerEntity) + priority),
                    out runningScriptId))
            {
                return false;
            }

            isControllerActive = runningScriptId == scriptId;
        }

        if (!memory.TryReadByte((uint)(AddressEntityModelIds + entityId), out var modelId))
        {
            return false;
        }

        if (modelId == NoModel)
        {
            capture = new Capture(
                module,
                currentFieldId,
                scriptPointer,
                entityCount,
                priority,
                runningScriptId,
                modelId,
                new FieldScriptControllerState(
                    isControllerActive,
                    HasModel: false,
                    IsModelVisible: false,
                    AnimationId: -1,
                    CurrentFrameFixed: 0,
                    EndFrameIndex: -1,
                    AnimationRunState: 0));
            return true;
        }

        if (!memory.TryReadUInt32((uint)AddressModelTablePointer, out var modelTable) ||
            modelTable == 0 ||
            !memory.TryReadByte((uint)(AddressModelAnimationRunState + modelId), out var runState))
        {
            return false;
        }

        var modelBase = modelTable + (uint)(modelId * ModelStride);
        if (modelBase < modelTable ||
            !memory.TryReadByte(modelBase + ModelVisibilityOffset, out var visibility) ||
            !memory.TryReadByte(modelBase + ModelAnimationIdOffset, out var animationId) ||
            !memory.TryReadInt16(modelBase + ModelCurrentFrameOffset, out var currentFrame) ||
            !memory.TryReadInt16(modelBase + ModelEndFrameOffset, out var endFrame))
        {
            return false;
        }

        capture = new Capture(
            module,
            currentFieldId,
            scriptPointer,
            entityCount,
            priority,
            runningScriptId,
            modelId,
            new FieldScriptControllerState(
                isControllerActive,
                HasModel: true,
                IsModelVisible: visibility != 0,
                // Signed, as the native handler sign-extends it.
                AnimationId: (sbyte)animationId,
                CurrentFrameFixed: currentFrame,
                // A negative end index is an invalid record, not a huge frame.
                EndFrameIndex: endFrame < 0 ? -1 : endFrame,
                AnimationRunState: runState));
        return true;
    }

    /// <summary>
    /// The derived pose together with the native identity it was read through. The
    /// identity is part of the equality check so a field reload, an entity whose
    /// model index changed, or a different running script cannot be mistaken for a
    /// stable frame just because the pose values happened to match.
    /// </summary>
    private readonly record struct Capture(
        byte Module,
        ushort FieldId,
        uint ScriptPointer,
        byte EntityCount,
        byte Priority,
        byte RunningScriptId,
        byte ModelId,
        FieldScriptControllerState State);
}
