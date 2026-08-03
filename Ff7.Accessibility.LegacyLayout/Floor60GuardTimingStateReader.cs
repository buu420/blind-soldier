using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct Floor60GuardTimingSnapshot(
    bool IsUsable,
    ushort FirstLeftTicks,
    ushort FirstMiddleTicks,
    ushort FirstRightTicks,
    ushort SecondLeftTicks,
    ushort SecondMiddleTicks,
    ushort SecondRightTicks,
    string Diagnostic)
{
    public ushort GetRemainingTicks(int lineIndex) =>
        lineIndex switch
        {
            0 => FirstLeftTicks,
            1 => FirstMiddleTicks,
            2 => FirstRightTicks,
            3 => SecondLeftTicks,
            4 => SecondMiddleTicks,
            5 => SecondRightTicks,
            _ => 0
        };

    public static Floor60GuardTimingSnapshot Invalid(string diagnostic) =>
        new(false, 0, 0, 0, 0, 0, 0, diagnostic);
}

/// <summary>
/// Reads the native WAIT countdown for the four Floor 60 watch guards and
/// assigns each counter to the guarded line at that guard's current station.
/// </summary>
public sealed class Floor60GuardTimingStateReader
{
    public const ushort FloorId = 239;
    public const int AddressEntityWaitCounters = 0x00CC0900;
    public const int FirstGuardEntityId = 30;
    public const int LastGuardEntityId = 33;
    public const int FirstGuardModelIndex = 5;
    public const int RequiredModelCount = 9;
    public const int StationToleranceUnits = 12;

    private static readonly int[] GuardStationX =
    [
        -482,
        -346,
        -166,
        173,
        361,
        505
    ];

    private readonly ILegacyAddressSpace addressSpace;

    public Floor60GuardTimingStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public Floor60GuardTimingSnapshot Read()
    {
        if (!TryCapture(out var before, out var diagnostic))
        {
            return Floor60GuardTimingSnapshot.Invalid(diagnostic);
        }

        if (!TryCapture(out var after, out var afterDiagnostic))
        {
            return Floor60GuardTimingSnapshot.Invalid(afterDiagnostic);
        }

        if (before.Module != after.Module ||
            before.FieldId != after.FieldId ||
            before.ModelCount != after.ModelCount ||
            before.ModelTable != after.ModelTable ||
            !before.Guards.SequenceEqual(after.Guards))
        {
            return Floor60GuardTimingSnapshot.Invalid("floor 60 guard timing changed during read");
        }

        Span<ushort> remainingTicks = stackalloc ushort[GuardStationX.Length];
        foreach (var guard in before.Guards)
        {
            if (guard.WaitTicks == 0)
            {
                continue;
            }

            var lineIndex = FindStationIndex(guard.X);
            if (lineIndex >= 0)
            {
                remainingTicks[lineIndex] = Math.Max(
                    remainingTicks[lineIndex],
                    guard.WaitTicks);
            }
        }

        return new Floor60GuardTimingSnapshot(
            true,
            remainingTicks[0],
            remainingTicks[1],
            remainingTicks[2],
            remainingTicks[3],
            remainingTicks[4],
            remainingTicks[5],
            $"field={before.FieldId}, guards=" +
            string.Join(
                ";",
                before.Guards.Select(
                    guard => $"{guard.EntityId}/model{guard.ModelIndex}:x={guard.X},wait={guard.WaitTicks}")));
    }

    private bool TryCapture(out GuardTimingCapture capture, out string diagnostic)
    {
        capture = new GuardTimingCapture(
            0,
            0,
            0,
            0,
            Array.Empty<GuardTimingSample>());
        diagnostic = "floor 60 guard timing primitive read failed";
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !addressSpace.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var modelCount) ||
            !addressSpace.TryReadUInt32((uint)FieldPositionReader.AddressFieldModelsPtr, out var modelTable))
        {
            return false;
        }

        if (module != FieldPositionReader.FieldModule || fieldId != FloorId)
        {
            diagnostic = $"not Floor 60 field gameplay: module={module}, field={fieldId}";
            return false;
        }

        if (modelTable == 0 || modelCount < RequiredModelCount)
        {
            diagnostic = $"Floor 60 guard models unavailable: pointer=0x{modelTable:X8}, count={modelCount}";
            return false;
        }

        var guards = new GuardTimingSample[LastGuardEntityId - FirstGuardEntityId + 1];
        for (var offset = 0; offset < guards.Length; offset++)
        {
            var entityId = FirstGuardEntityId + offset;
            var modelIndex = FirstGuardModelIndex + offset;
            if (!TryAddScaled(
                    (uint)AddressEntityWaitCounters,
                    entityId,
                    sizeof(ushort),
                    out var waitAddress) ||
                !TryAddScaled(
                    modelTable,
                    modelIndex,
                    FieldPositionReader.FieldModelStride,
                    out var modelBase) ||
                !TryAdd(modelBase, FieldPositionReader.ModelXOffset, out var xAddress) ||
                !addressSpace.TryReadUInt16(waitAddress, out var waitTicks) ||
                !addressSpace.TryReadInt32(xAddress, out var x))
            {
                diagnostic = $"Floor 60 guard {entityId} timing read failed";
                return false;
            }

            guards[offset] = new GuardTimingSample(entityId, modelIndex, x, waitTicks);
        }

        capture = new GuardTimingCapture(module, fieldId, modelCount, modelTable, guards);
        diagnostic = string.Empty;
        return true;
    }

    private static int FindStationIndex(int x)
    {
        var bestIndex = -1;
        var bestDistance = int.MaxValue;
        for (var index = 0; index < GuardStationX.Length; index++)
        {
            var distance = Math.Abs((long)x - GuardStationX[index]);
            if (distance <= StationToleranceUnits && distance < bestDistance)
            {
                bestIndex = index;
                bestDistance = (int)distance;
            }
        }

        return bestIndex;
    }

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        result = 0;
        if (offset < 0)
        {
            return false;
        }

        var value = (ulong)address + (uint)offset;
        if (value > uint.MaxValue)
        {
            return false;
        }

        result = (uint)value;
        return true;
    }

    private static bool TryAddScaled(
        uint address,
        int index,
        int stride,
        out uint result)
    {
        result = 0;
        if (index < 0 || stride < 0)
        {
            return false;
        }

        var value = (ulong)address + (ulong)(uint)index * (uint)stride;
        if (value > uint.MaxValue)
        {
            return false;
        }

        result = (uint)value;
        return true;
    }

    private sealed record GuardTimingCapture(
        byte Module,
        ushort FieldId,
        byte ModelCount,
        uint ModelTable,
        IReadOnlyList<GuardTimingSample> Guards);

    private readonly record struct GuardTimingSample(
        int EntityId,
        int ModelIndex,
        int X,
        ushort WaitTicks);
}
