namespace Ff7.Accessibility.LegacyLayout;

public readonly record struct HighwayRoadStateSnapshot(
    byte Module,
    double CloudLateralUnits,
    double RoadHalfWidthUnits,
    int RoadSampleIndex);

/// <summary>
/// Reads the original highway module's live road ring independently from the
/// combat actor block. The layout and index expression come from FUN_006539b2.
/// </summary>
public sealed class HighwayRoadStateReader
{
    public const int AddressRoadScroll = 0x00D85978;
    public const int AddressRoadStatePointer = 0x00D8D440;
    public const uint RoadRingPointerOffset = 0x0C;
    public const int RoadSampleCount = 80;
    public const int RoadSampleStride = 0xB0;
    public const uint RoadWidthSampleOffset = 0x44;
    public const int MinimumRoadHalfWidthUnits = 32;
    public const int MaximumRoadHalfWidthUnits = 2048;

    private readonly ILegacyAddressSpace addressSpace;

    public HighwayRoadStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public bool TryRead(out HighwayRoadStateSnapshot snapshot)
    {
        snapshot = default;
        if (!addressSpace.TryReadByte(
                (uint)HighwayStateReader.AddressCurrentModule,
                out var moduleBefore))
        {
            LastDiagnostic = "highway road module read failed";
            return false;
        }

        if (moduleBefore != HighwayStateReader.HighwayModule)
        {
            LastDiagnostic = $"module={moduleBefore}, not highway";
            return false;
        }

        var cloudLateralAddress =
            (uint)HighwayStateReader.AddressActorTable + HighwayStateReader.ActorLateralOffset;
        var cloudLongitudinalAddress =
            (uint)HighwayStateReader.AddressActorTable + HighwayStateReader.ActorLongitudinalOffset;
        if (!addressSpace.TryReadInt32(cloudLateralAddress, out var cloudLateralFixed) ||
            !addressSpace.TryReadInt32(cloudLongitudinalAddress, out var cloudLongitudinalFixed) ||
            !addressSpace.TryReadInt32((uint)AddressRoadScroll, out var roadScroll) ||
            !addressSpace.TryReadUInt32((uint)AddressRoadStatePointer, out var roadStatePointer) ||
            roadStatePointer == 0 ||
            !TryAdd(roadStatePointer, RoadRingPointerOffset, out var ringPointerAddress) ||
            !addressSpace.TryReadUInt32(ringPointerAddress, out var roadRingPointer) ||
            roadRingPointer == 0)
        {
            LastDiagnostic = "highway road primitive or pointer read failed";
            return false;
        }

        var nativeAccumulator = unchecked(roadScroll + (cloudLongitudinalFixed >> 8));
        var sampleIndex = (nativeAccumulator >> 8) % RoadSampleCount;
        if (sampleIndex < 0)
        {
            sampleIndex += RoadSampleCount;
        }

        var sampleOffset =
            (ulong)(uint)(sampleIndex * RoadSampleStride) + RoadWidthSampleOffset;
        if (!TryAdd(roadRingPointer, sampleOffset, out var widthSampleAddress) ||
            !addressSpace.TryReadInt16(widthSampleAddress, out var signedWidthSample) ||
            !addressSpace.TryReadByte(
                (uint)HighwayStateReader.AddressCurrentModule,
                out var moduleAfter))
        {
            LastDiagnostic = "highway road sample read failed";
            return false;
        }

        if (moduleAfter != moduleBefore)
        {
            LastDiagnostic = $"highway module changed during road read: {moduleBefore}->{moduleAfter}";
            return false;
        }

        var halfWidthUnits = Math.Abs((signedWidthSample >> 1) - 0x60);
        if (halfWidthUnits is < MinimumRoadHalfWidthUnits or > MaximumRoadHalfWidthUnits)
        {
            LastDiagnostic =
                $"invalid highway road half-width {halfWidthUnits} at sample {sampleIndex}";
            return false;
        }

        snapshot = new HighwayRoadStateSnapshot(
            moduleBefore,
            cloudLateralFixed / 256d,
            halfWidthUnits,
            sampleIndex);
        LastDiagnostic =
            $"module={moduleBefore}, sample={sampleIndex}, lateral={snapshot.CloudLateralUnits:0.0}, " +
            $"halfWidth={snapshot.RoadHalfWidthUnits:0.0}";
        return true;
    }

    private static bool TryAdd(uint address, ulong offset, out uint result)
    {
        var sum = (ulong)address + offset;
        if (sum > uint.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (uint)sum;
        return true;
    }
}
