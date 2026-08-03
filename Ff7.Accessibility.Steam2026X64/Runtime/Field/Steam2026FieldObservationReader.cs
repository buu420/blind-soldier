using System.Collections.ObjectModel;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Reads research-only field evidence from the Steam 2026 translated x86 guest
/// address space. This component creates no hooks, publishes no events, speaks
/// nothing, and does not own a runtime capability lifecycle.
/// </summary>
public sealed class Steam2026FieldObservationReader
{
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldPositionReader positionReader;
    private readonly FieldScriptContextReader scriptReader;
    private readonly Steam2026FieldAudibleCueStateReader cueReader;
    private readonly FieldNavigationControlReader controlReader;
    private readonly FieldBoundaryStateReader boundaryReader;

    public Steam2026FieldObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
        : this(ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory))
    {
    }

    internal Steam2026FieldObservationReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        positionReader = new FieldPositionReader(addressSpace);
        scriptReader = new FieldScriptContextReader(addressSpace);
        cueReader = new Steam2026FieldAudibleCueStateReader(addressSpace);
        controlReader = new FieldNavigationControlReader(addressSpace);
        boundaryReader = new FieldBoundaryStateReader(addressSpace);
    }

    public bool TryReadResearchSnapshot(out Steam2026FieldResearchSnapshot snapshot) =>
        TryReadResearchSnapshotCore(null, out snapshot);

    public bool TryReadResearchSnapshot(
        int verifiedTriangleCount,
        out Steam2026FieldResearchSnapshot snapshot) =>
        TryReadResearchSnapshotCore(verifiedTriangleCount, out snapshot);

    public bool TryReadFieldFrame(out FieldFrameObservation observation) =>
        TryReadFieldFrameCore(null, out observation);

    public bool TryReadFieldFrame(int verifiedTriangleCount, out FieldFrameObservation observation) =>
        TryReadFieldFrameCore(verifiedTriangleCount, out observation);

    private bool TryReadFieldFrameCore(int? verifiedTriangleCount, out FieldFrameObservation observation)
    {
        observation = null!;
        if (!TryReadResearchSnapshotCore(verifiedTriangleCount, out var snapshot))
        {
            return !verifiedTriangleCount.HasValue &&
                TryReadMovementFieldFrame(out observation);
        }

        observation = CreateFieldFrame(snapshot);
        return true;
    }

    internal bool TryReadMovementFieldFrame(out FieldFrameObservation observation)
    {
        observation = null!;
        var before = positionReader.Read();
        var cueAvailable = cueReader.TryRead(out var cue);
        var after = positionReader.Read();
        if (!before.IsUsable ||
            !cueAvailable ||
            !after.IsUsable ||
            !HasStableMovementOwnership(before, after, cue))
        {
            return false;
        }

        var position = after.Position;
        observation = new FieldFrameObservation(
            position.FieldId,
            position.ModelIndex,
            position.X,
            position.Y,
            position.Z,
            position.TriangleId,
            !cue.IsSuppressed,
            EntityId: -1,
            ScriptId: -1,
            ScriptByteIndex: -1);
        return true;
    }

    private static bool HasStableMovementOwnership(
        FieldPositionReadResult before,
        FieldPositionReadResult after,
        FieldAudibleCueState cue)
    {
        var beforePosition = before.Position;
        var afterPosition = after.Position;
        return before.ModelBase == after.ModelBase &&
            beforePosition.CurrentModule == FieldPositionReader.FieldModule &&
            beforePosition.CurrentModule == afterPosition.CurrentModule &&
            beforePosition.CurrentModule == cue.Module &&
            beforePosition.FieldId == afterPosition.FieldId &&
            beforePosition.ModelIndex == afterPosition.ModelIndex;
    }

    internal static FieldFrameObservation CreateFieldFrame(
        Steam2026FieldResearchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var position = snapshot.Position;
        var script = snapshot.Script;
        return new FieldFrameObservation(
            position.FieldId,
            position.PlayerModelId,
            position.X,
            position.Y,
            position.Z,
            position.TriangleId,
            !snapshot.Cue.IsSuppressed,
            script.EntityId,
            script.ScriptId,
            script.ByteIndex);
    }

    private bool TryReadResearchSnapshotCore(
        int? verifiedTriangleCount,
        out Steam2026FieldResearchSnapshot snapshot)
    {
        snapshot = null!;
        if (verifiedTriangleCount is <= 0 or > FieldBoundaryStateReader.MaximumTriangleCount ||
            !TryCaptureOwnership(verifiedTriangleCount, out var before) ||
            !TryReadDomains(verifiedTriangleCount, out var candidate) ||
            !TryCaptureOwnership(verifiedTriangleCount, out var middle) ||
            before != middle ||
            !TryReadDomains(verifiedTriangleCount, out var confirmation) ||
            candidate != confirmation ||
            !TryCaptureOwnership(verifiedTriangleCount, out var after) ||
            before != after ||
            !MatchesOwnership(candidate, before))
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    private bool TryReadDomains(
        int? verifiedTriangleCount,
        out Steam2026FieldResearchSnapshot snapshot)
    {
        snapshot = null!;
        var positionResult = positionReader.Read();
        if (!positionResult.IsUsable ||
            !scriptReader.TryRead(out var script) ||
            script.FieldId != positionResult.Position.FieldId ||
            !cueReader.TryRead(out var cue) ||
            cue.Module != positionResult.Position.CurrentModule)
        {
            return false;
        }

        var control = controlReader.Read(positionResult.Position);
        if (!control.IsUsable)
        {
            return false;
        }

        Steam2026FieldBoundaryResearchSnapshot? boundary = null;
        if (verifiedTriangleCount.HasValue)
        {
            var boundaryResult = boundaryReader.Read(
                positionResult.Position,
                verifiedTriangleCount.Value);
            if (!boundaryResult.IsUsable)
            {
                return false;
            }

            boundary = new Steam2026FieldBoundaryResearchSnapshot(
                verifiedTriangleCount.Value,
                boundaryResult.State.ActiveBoundaryTriangles);
        }

        var position = positionResult.Position;
        snapshot = new Steam2026FieldResearchSnapshot(
            new Steam2026FieldPositionResearchSnapshot(
                position.FieldId,
                position.ModelIndex,
                position.X,
                position.Y,
                position.Z,
                position.TriangleId,
                position.Direction),
            new Steam2026FieldScriptResearchSnapshot(
                script.EntityId,
                script.ScriptId,
                script.ByteIndex,
                checked((byte)script.Opcode)),
            new Steam2026FieldCueResearchSnapshot(
                cue.IsSuppressed,
                cue.Reason,
                cue.UserControl,
                cue.ActiveMessageCount,
                cue.MovieActive),
            new Steam2026FieldControlResearchSnapshot(
                control.Transform.SignedControlDirection),
            boundary);
        return true;
    }

    private bool TryCaptureOwnership(int? verifiedTriangleCount, out FieldOwnershipSnapshot ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldCurrentModelId, out var modelId) ||
            !addressSpace.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var modelCount) ||
            !addressSpace.TryReadUInt32((uint)FieldPositionReader.AddressFieldModelsPtr, out var modelTable) ||
            modelTable == 0 ||
            !addressSpace.TryReadUInt32((uint)FieldScriptContextReader.AddressFieldScriptPtr, out var scriptPointer) ||
            scriptPointer == 0 ||
            !addressSpace.TryReadByte((uint)FieldScriptContextReader.AddressCurrentEntityId, out var entityId) ||
            !TryAdd((uint)FieldScriptContextReader.AddressCurrentEntityScriptPriority, entityId, out var priorityAddress) ||
            !addressSpace.TryReadByte(priorityAddress, out var priority) ||
            priority >= FieldScriptContextReader.ScriptSlotsPerEntity ||
            !TryAddScaled(
                (uint)FieldScriptContextReader.AddressCurrentEntityScriptId,
                entityId,
                FieldScriptContextReader.ScriptSlotsPerEntity,
                priority,
                out var scriptIdAddress) ||
            !addressSpace.TryReadByte(scriptIdAddress, out var scriptId) ||
            scriptId >= FieldScriptContextReader.ScriptOffsetsPerEntity ||
            !TryAddScaled(
                (uint)FieldScriptContextReader.AddressFieldCurrScriptPosition,
                entityId,
                sizeof(ushort),
                0,
                out var scriptPositionAddress) ||
            !addressSpace.TryReadUInt16(scriptPositionAddress, out var absoluteScriptPosition) ||
            !addressSpace.TryReadByte((uint)FieldAudibleCueStateReader.AddressUserControl, out var userControl) ||
            !addressSpace.TryReadByte((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, out var activeMessageCount) ||
            !addressSpace.TryReadUInt16((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, out var movieActive) ||
            !addressSpace.TryReadUInt32((uint)FieldNavigationControlReader.AddressFieldTriggersPtr, out var triggerPointer) ||
            triggerPointer == 0 ||
            !TryAdd(triggerPointer, FieldNavigationControlReader.ControlDirectionOffset, out var controlAddress) ||
            !addressSpace.TryReadByte(controlAddress, out var controlDirection))
        {
            return false;
        }

        uint fieldGlobalPointer = 0;
        if (verifiedTriangleCount.HasValue &&
            (!addressSpace.TryReadUInt32(
                 (uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr,
                 out fieldGlobalPointer) ||
             fieldGlobalPointer == 0))
        {
            return false;
        }

        ownership = new FieldOwnershipSnapshot(
            module,
            fieldId,
            modelId,
            modelCount,
            modelTable,
            scriptPointer,
            entityId,
            priority,
            scriptId,
            absoluteScriptPosition,
            userControl,
            activeMessageCount,
            movieActive,
            triggerPointer,
            controlDirection,
            fieldGlobalPointer);
        return true;
    }

    private static bool MatchesOwnership(
        Steam2026FieldResearchSnapshot snapshot,
        FieldOwnershipSnapshot ownership) =>
        ownership.Module == FieldPositionReader.FieldModule &&
        snapshot.Position.FieldId == ownership.FieldId &&
        snapshot.Position.PlayerModelId == ownership.ModelId &&
        snapshot.Script.EntityId == ownership.EntityId &&
        snapshot.Script.ScriptId == ownership.ScriptId &&
        snapshot.Cue.UserControl == ownership.UserControl &&
        snapshot.Cue.ActiveMessageCount == ownership.ActiveMessageCount &&
        snapshot.Cue.MovieActive == ownership.MovieActive &&
        snapshot.Control.SignedControlDirection == unchecked((sbyte)ownership.ControlDirection) &&
        (snapshot.Boundary is null || ownership.FieldGlobalPointer != 0);

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        result = 0;
        if (offset < 0)
        {
            return false;
        }

        var sum = (ulong)address + (uint)offset;
        if (sum > uint.MaxValue)
        {
            return false;
        }

        result = (uint)sum;
        return true;
    }

    private static bool TryAddScaled(
        uint address,
        byte index,
        int stride,
        byte trailingOffset,
        out uint result)
    {
        result = 0;
        if (stride < 0)
        {
            return false;
        }

        var sum = (ulong)address + ((ulong)index * (uint)stride) + trailingOffset;
        if (sum > uint.MaxValue)
        {
            return false;
        }

        result = (uint)sum;
        return true;
    }

    private readonly record struct FieldOwnershipSnapshot(
        byte Module,
        ushort FieldId,
        ushort ModelId,
        byte ModelCount,
        uint ModelTable,
        uint ScriptPointer,
        byte EntityId,
        byte Priority,
        byte ScriptId,
        ushort AbsoluteScriptPosition,
        byte UserControl,
        byte ActiveMessageCount,
        ushort MovieActive,
        uint TriggerPointer,
        byte ControlDirection,
        uint FieldGlobalPointer);
}

public sealed record Steam2026FieldResearchSnapshot(
    Steam2026FieldPositionResearchSnapshot Position,
    Steam2026FieldScriptResearchSnapshot Script,
    Steam2026FieldCueResearchSnapshot Cue,
    Steam2026FieldControlResearchSnapshot Control,
    Steam2026FieldBoundaryResearchSnapshot? Boundary);

public sealed record Steam2026FieldPositionResearchSnapshot(
    int FieldId,
    int PlayerModelId,
    int X,
    int Y,
    int Z,
    ushort TriangleId,
    byte Direction);

public sealed record Steam2026FieldScriptResearchSnapshot(
    int EntityId,
    int ScriptId,
    int ByteIndex,
    byte Opcode);

public sealed record Steam2026FieldCueResearchSnapshot(
    bool IsSuppressed,
    string Reason,
    byte UserControl,
    byte ActiveMessageCount,
    ushort MovieActive);

public sealed record Steam2026FieldControlResearchSnapshot(int SignedControlDirection);

public sealed class Steam2026FieldBoundaryResearchSnapshot : IEquatable<Steam2026FieldBoundaryResearchSnapshot>
{
    private readonly ReadOnlyCollection<int> activeTriangleIds;

    internal Steam2026FieldBoundaryResearchSnapshot(
        int triangleCount,
        IEnumerable<int> activeTriangleIds)
    {
        TriangleCount = triangleCount;
        this.activeTriangleIds = Array.AsReadOnly(activeTriangleIds.ToArray());
    }

    public int TriangleCount { get; }

    public IReadOnlyList<int> ActiveTriangleIds => activeTriangleIds;

    public bool Equals(Steam2026FieldBoundaryResearchSnapshot? other) =>
        other is not null &&
        TriangleCount == other.TriangleCount &&
        activeTriangleIds.SequenceEqual(other.activeTriangleIds);

    public override bool Equals(object? obj) =>
        obj is Steam2026FieldBoundaryResearchSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TriangleCount);
        foreach (var triangleId in activeTriangleIds)
        {
            hash.Add(triangleId);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"triangles={TriangleCount}, active=[{string.Join(',', activeTriangleIds)}]";
}
