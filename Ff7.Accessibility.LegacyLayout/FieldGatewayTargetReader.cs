using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the fixed FFVII field-gateway table from a failure-aware guest address
/// space. Gateway destinations are metadata; the only raw visible label is
/// <c>Exit</c> until a separate authoritative name resolver supplies a label.
/// </summary>
public sealed class FieldGatewayTargetReader
{
    public const int GatewayCount = 12;
    public const int GatewaysOffset = 0x38;
    public const int GatewayStride = 0x18;
    public const int DestinationFieldOffset = 0x12;

    private const int GatewayTableByteCount = GatewayCount * GatewayStride;
    private const int SecondExitLineVertexOffset = 0x06;
    private const short UnusedDestinationField = short.MaxValue;

    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets =
        Array.Empty<FieldNavigationTarget>();

    private readonly ILegacyAddressSpace addressSpace;

    public FieldGatewayTargetReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        _ = TryReadTargets(position, out var targets);
        return targets;
    }

    public bool TryReadTargets(
        FieldPositionSnapshot position,
        out IReadOnlyList<FieldNavigationTarget> targets)
    {
        targets = EmptyTargets;
        if (!FieldPositionReader.IsUsable(position))
        {
            return Invalid($"field={position.FieldId}, not in field module", out targets);
        }

        if (position.FieldId is < 0 or > ushort.MaxValue)
        {
            return Invalid($"field={position.FieldId}, invalid field id", out targets);
        }

        try
        {
            if (!TryReadFrame(position, out var candidate, out var diagnostic))
            {
                return Invalid(diagnostic, out targets);
            }

            if (!TryReadFrame(position, out var confirmation, out var confirmationDiagnostic))
            {
                return Invalid(confirmationDiagnostic, out targets);
            }

            if (!candidate.Matches(confirmation))
            {
                return Invalid(
                    $"field={position.FieldId}, gateway state changed during read",
                    out targets);
            }

            targets = CreateTargets(position, candidate);
            return true;
        }
        catch (Exception ex)
        {
            return Invalid(
                $"field={position.FieldId}, gateway read failed: {ex.GetType().Name}: {ex.Message}",
                out targets);
        }
    }

    private bool TryReadFrame(
        FieldPositionSnapshot position,
        out FieldGatewayFrame frame,
        out string diagnostic)
    {
        frame = default;
        if (!TryReadOwnership(out var before))
        {
            diagnostic = $"field={position.FieldId}, gateway ownership read failed";
            return false;
        }

        if (!TryValidateOwnership(position, before, out diagnostic))
        {
            return false;
        }

        if (!TryCalculateGatewayTable(before.TriggerPointer, out var gatewayTable))
        {
            diagnostic = $"field={position.FieldId}, gateway table address overflowed";
            return false;
        }

        var bytes = new byte[GatewayTableByteCount];
        if (!addressSpace.TryRead(gatewayTable, bytes))
        {
            diagnostic =
                $"field={position.FieldId}, trigger=0x{before.TriggerPointer:X8}, gateways unreadable";
            return false;
        }

        if (!TryReadOwnership(out var after))
        {
            diagnostic = $"field={position.FieldId}, gateway ownership bookend read failed";
            return false;
        }

        if (before != after)
        {
            diagnostic = $"field={position.FieldId}, gateway ownership changed during read";
            return false;
        }

        frame = new FieldGatewayFrame(before, gatewayTable, bytes);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryReadOwnership(out FieldGatewayOwnership ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldNavigationControlReader.AddressFieldTriggersPtr,
                out var triggerPointer))
        {
            return false;
        }

        ownership = new FieldGatewayOwnership(module, fieldId, triggerPointer);
        return true;
    }

    private static bool TryValidateOwnership(
        FieldPositionSnapshot position,
        FieldGatewayOwnership ownership,
        out string diagnostic)
    {
        if (ownership.Module != position.CurrentModule || ownership.FieldId != position.FieldId)
        {
            diagnostic = $"field={position.FieldId}, field position is unavailable";
            return false;
        }

        if (ownership.TriggerPointer == 0)
        {
            diagnostic = $"field={position.FieldId}, trigger=0x00000000";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private IReadOnlyList<FieldNavigationTarget> CreateTargets(
        FieldPositionSnapshot position,
        FieldGatewayFrame frame)
    {
        var targets = new List<FieldNavigationTarget>(GatewayCount);
        var destinationFields = new List<int>(GatewayCount);
        var table = frame.Bytes.AsSpan();
        for (var gatewayIndex = 0; gatewayIndex < GatewayCount; gatewayIndex++)
        {
            var record = table.Slice(gatewayIndex * GatewayStride, GatewayStride);
            var destinationFieldId = ReadInt16(record, DestinationFieldOffset);
            if (destinationFieldId < 0 || destinationFieldId == UnusedDestinationField)
            {
                continue;
            }

            var x1 = ReadInt16(record, 0);
            var y1 = ReadInt16(record, 0x02);
            var z1 = ReadInt16(record, 0x04);
            var x2 = ReadInt16(record, SecondExitLineVertexOffset);
            var y2 = ReadInt16(record, SecondExitLineVertexOffset + 0x02);
            var z2 = ReadInt16(record, SecondExitLineVertexOffset + 0x04);
            targets.Add(new FieldNavigationTarget(
                position.FieldId,
                FieldNavigationCategory.Exits,
                "Exit",
                Midpoint(x1, x2),
                Midpoint(y1, y2),
                Midpoint(z1, z2),
                $"gateway:{position.FieldId}:{gatewayIndex}:{destinationFieldId}",
                CompletesOnArrival: true,
                DestinationFieldIds: Array.AsReadOnly([checked((int)destinationFieldId)]),
                TriggerLine: new FieldNavigationTriggerLine(x1, y1, z1, x2, y2, z2)));
            destinationFields.Add(destinationFieldId);
        }

        LastDiagnostic =
            $"field={position.FieldId}, trigger=0x{frame.Ownership.TriggerPointer:X8}, count={targets.Count}, " +
            $"destinations={(destinationFields.Count == 0 ? "none" : string.Join(',', destinationFields))}";
        return targets.Count == 0
            ? EmptyTargets
            : Array.AsReadOnly(targets.ToArray());
    }

    private bool Invalid(
        string diagnostic,
        out IReadOnlyList<FieldNavigationTarget> targets)
    {
        LastDiagnostic = diagnostic;
        targets = EmptyTargets;
        return false;
    }

    private static bool TryCalculateGatewayTable(uint triggerPointer, out uint gatewayTable)
    {
        gatewayTable = 0;
        try
        {
            gatewayTable = checked(triggerPointer + (uint)GatewaysOffset);
            _ = checked(gatewayTable + (uint)(GatewayTableByteCount - 1));
            return true;
        }
        catch (OverflowException)
        {
            gatewayTable = 0;
            return false;
        }
    }

    private static short ReadInt16(ReadOnlySpan<byte> record, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(record.Slice(offset, sizeof(short)));

    private static int Midpoint(short first, short second) =>
        (int)Math.Round((first + second) / 2d, MidpointRounding.AwayFromZero);

    private readonly record struct FieldGatewayOwnership(
        byte Module,
        ushort FieldId,
        uint TriggerPointer);

    private readonly record struct FieldGatewayFrame(
        FieldGatewayOwnership Ownership,
        uint GatewayTable,
        byte[] Bytes)
    {
        public bool Matches(FieldGatewayFrame other) =>
            Ownership == other.Ownership &&
            GatewayTable == other.GatewayTable &&
            Bytes.AsSpan().SequenceEqual(other.Bytes);
    }
}
