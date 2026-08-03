using System.Collections.ObjectModel;
using System.Globalization;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Produces research-only, pointer-free field-navigation observations from the
/// Steam 2026 translated x86 guest address space. It creates no hooks, performs
/// no writes, emits no speech, and enables no runtime capability.
/// </summary>
public sealed class Steam2026FieldNavigationObservationReader
{
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldPositionReader positionReader;
    private readonly FieldNavigationControlReader controlReader;
    private readonly FieldBoundaryStateReader boundaryReader;
    private readonly FieldGatewayTargetReader gatewayReader;

    public Steam2026FieldNavigationObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
        : this(CreateValidatedAddressSpace(fingerprint, moduleBase, memory))
    {
    }

    internal Steam2026FieldNavigationObservationReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        positionReader = new FieldPositionReader(addressSpace);
        controlReader = new FieldNavigationControlReader(addressSpace);
        boundaryReader = new FieldBoundaryStateReader(addressSpace);
        gatewayReader = new FieldGatewayTargetReader(addressSpace);
    }

    public bool TryReadSnapshot(
        int verifiedTriangleCount,
        out Steam2026FieldNavigationResearchSnapshot snapshot)
    {
        snapshot = null!;
        if (verifiedTriangleCount <= 0 ||
            verifiedTriangleCount > FieldBoundaryStateReader.MaximumTriangleCount)
        {
            return false;
        }

        try
        {
            if (!TryCaptureOwnership(out var before) ||
                !TryReadCandidate(verifiedTriangleCount, out var candidate) ||
                !TryCaptureOwnership(out var middle) ||
                before != middle ||
                !TryReadCandidate(verifiedTriangleCount, out var confirmation) ||
                !candidate.Matches(confirmation) ||
                !TryCaptureOwnership(out var after) ||
                before != after ||
                !candidate.MatchesOwnership(before))
            {
                return false;
            }

            snapshot = candidate.ToSnapshot();
            return true;
        }
        catch (Exception)
        {
            snapshot = null!;
            return false;
        }
    }

    private bool TryReadCandidate(int verifiedTriangleCount, out NavigationCandidate candidate)
    {
        candidate = default;
        var positionResult = positionReader.Read();
        if (!positionResult.IsUsable)
        {
            return false;
        }

        var position = positionResult.Position;
        var controlResult = controlReader.Read(position);
        if (!controlResult.IsUsable)
        {
            return false;
        }

        var boundaryResult = boundaryReader.Read(position, verifiedTriangleCount);
        if (!boundaryResult.IsUsable ||
            !gatewayReader.TryReadTargets(position, out var gatewayTargets) ||
            !TryNormalizeGateways(position, gatewayTargets, out var gateways))
        {
            return false;
        }

        candidate = new NavigationCandidate(
            positionResult.ModelBase,
            position,
            controlResult.Transform.SignedControlDirection,
            boundaryResult.State,
            gateways);
        return true;
    }

    private bool TryCaptureOwnership(out NavigationOwnership ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !addressSpace.TryReadUInt16(
                (uint)FieldPositionReader.AddressFieldCurrentModelId,
                out var modelId) ||
            !addressSpace.TryReadByte(
                (uint)FieldPositionReader.AddressFieldNumModels,
                out var modelCount) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldPositionReader.AddressFieldModelsPtr,
                out var modelTable) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldNavigationControlReader.AddressFieldTriggersPtr,
                out var triggerPointer) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr,
                out var fieldGlobalPointer) ||
            module != FieldPositionReader.FieldModule ||
            modelTable == 0 ||
            modelId >= modelCount ||
            triggerPointer == 0 ||
            fieldGlobalPointer == 0)
        {
            return false;
        }

        ownership = new NavigationOwnership(
            module,
            fieldId,
            modelId,
            modelCount,
            modelTable,
            triggerPointer,
            fieldGlobalPointer);
        return true;
    }

    private static bool TryNormalizeGateways(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget> targets,
        out GatewayCandidate[] gateways)
    {
        gateways = new GatewayCandidate[targets.Count];
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (target.FieldId != position.FieldId ||
                target.Category != FieldNavigationCategory.Exits ||
                !string.Equals(target.Label, "Exit", StringComparison.Ordinal) ||
                target.DestinationFieldIds is not { Count: 1 } destinations ||
                destinations[0] is < 0 or >= short.MaxValue ||
                !TryParseGatewayIdentity(
                    target.StableId,
                    position.FieldId,
                    destinations[0],
                    out var gatewayIndex))
            {
                gateways = [];
                return false;
            }

            gateways[index] = new GatewayCandidate(
                gatewayIndex,
                target.X,
                target.Y,
                target.Z,
                destinations[0]);
        }

        return true;
    }

    private static bool TryParseGatewayIdentity(
        string stableId,
        int expectedFieldId,
        int expectedDestinationFieldId,
        out int gatewayIndex)
    {
        gatewayIndex = -1;
        var parts = stableId.Split(':');
        return parts.Length == 4 &&
            string.Equals(parts[0], "gateway", StringComparison.Ordinal) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var fieldId) &&
            fieldId == expectedFieldId &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out gatewayIndex) &&
            gatewayIndex is >= 0 and < FieldGatewayTargetReader.GatewayCount &&
            int.TryParse(
                parts[3],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var destinationFieldId) &&
            destinationFieldId == expectedDestinationFieldId;
    }

    private static TranslatedX86AddressSpace CreateValidatedAddressSpace(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported ||
            !fingerprint.Identity.Is64Bit ||
            !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal) ||
            !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The field-navigation observation facade requires the exact supported Steam 2026 x64 fingerprint.",
                nameof(fingerprint));
        }

        return ValidatedTranslatedX86AddressSpaceFactory.Create(moduleBase, memory);
    }

    private static bool TryCalculateModelBase(
        NavigationOwnership ownership,
        out uint modelBase)
    {
        modelBase = 0;
        try
        {
            modelBase = checked(
                ownership.ModelTable +
                checked((uint)ownership.ModelId * (uint)FieldPositionReader.FieldModelStride));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private readonly record struct NavigationOwnership(
        byte Module,
        ushort FieldId,
        ushort ModelId,
        byte ModelCount,
        uint ModelTable,
        uint TriggerPointer,
        uint FieldGlobalPointer);

    private readonly record struct GatewayCandidate(
        int GatewayIndex,
        int X,
        int Y,
        int Z,
        int DestinationFieldId);

    private readonly record struct NavigationCandidate(
        uint ModelBase,
        FieldPositionSnapshot Position,
        int SignedControlDirection,
        FieldBoundaryState Boundary,
        GatewayCandidate[] Gateways)
    {
        public bool Matches(NavigationCandidate other) =>
            ModelBase == other.ModelBase &&
            Position == other.Position &&
            SignedControlDirection == other.SignedControlDirection &&
            Boundary.TriangleCount == other.Boundary.TriangleCount &&
            Boundary.Bits.AsSpan().SequenceEqual(other.Boundary.Bits.AsSpan()) &&
            Gateways.AsSpan().SequenceEqual(other.Gateways);

        public bool MatchesOwnership(NavigationOwnership ownership) =>
            ownership.Module == Position.CurrentModule &&
            ownership.FieldId == Position.FieldId &&
            ownership.ModelId == Position.ModelIndex &&
            TryCalculateModelBase(ownership, out var expectedModelBase) &&
            expectedModelBase == ModelBase;

        public Steam2026FieldNavigationResearchSnapshot ToSnapshot()
        {
            var position = new Steam2026FieldPositionResearchSnapshot(
                Position.FieldId,
                Position.ModelIndex,
                Position.X,
                Position.Y,
                Position.Z,
                Position.TriangleId,
                Position.Direction);
            var control = new Steam2026FieldControlResearchSnapshot(SignedControlDirection);
            var boundary = new Steam2026FieldBoundaryResearchSnapshot(
                Boundary.TriangleCount,
                Boundary.ActiveBoundaryTriangles);
            var gateways = Gateways.Select(gateway => new Steam2026FieldGatewayResearchSnapshot(
                gateway.GatewayIndex,
                "Exit",
                gateway.X,
                gateway.Y,
                gateway.Z,
                gateway.DestinationFieldId));
            return new Steam2026FieldNavigationResearchSnapshot(
                position,
                control,
                boundary,
                gateways);
        }
    }
}

public sealed record Steam2026FieldGatewayResearchSnapshot(
    int GatewayIndex,
    string VisibleLabel,
    int X,
    int Y,
    int Z,
    int DestinationFieldId);

public sealed class Steam2026FieldNavigationResearchSnapshot :
    IEquatable<Steam2026FieldNavigationResearchSnapshot>
{
    private readonly ReadOnlyCollection<Steam2026FieldGatewayResearchSnapshot> gateways;

    internal Steam2026FieldNavigationResearchSnapshot(
        Steam2026FieldPositionResearchSnapshot position,
        Steam2026FieldControlResearchSnapshot control,
        Steam2026FieldBoundaryResearchSnapshot boundary,
        IEnumerable<Steam2026FieldGatewayResearchSnapshot> gateways)
    {
        Position = position ?? throw new ArgumentNullException(nameof(position));
        Control = control ?? throw new ArgumentNullException(nameof(control));
        Boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        this.gateways = Array.AsReadOnly(
            (gateways ?? throw new ArgumentNullException(nameof(gateways))).ToArray());
    }

    public Steam2026FieldPositionResearchSnapshot Position { get; }

    public Steam2026FieldControlResearchSnapshot Control { get; }

    public Steam2026FieldBoundaryResearchSnapshot Boundary { get; }

    public IReadOnlyList<Steam2026FieldGatewayResearchSnapshot> Gateways => gateways;

    public bool Equals(Steam2026FieldNavigationResearchSnapshot? other) =>
        other is not null &&
        Position == other.Position &&
        Control == other.Control &&
        Boundary.Equals(other.Boundary) &&
        gateways.SequenceEqual(other.gateways);

    public override bool Equals(object? obj) =>
        obj is Steam2026FieldNavigationResearchSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(Control);
        hash.Add(Boundary);
        foreach (var gateway in gateways)
        {
            hash.Add(gateway);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"field={Position.FieldId}, model={Position.PlayerModelId}, " +
        $"control={Control.SignedControlDirection}, boundaries={Boundary}, gateways={gateways.Count}";
}
