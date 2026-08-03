using System.Collections.ObjectModel;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Produces pointer-free, catalog-backed pickup targets from checked reads of
/// the translated legacy address space. Any unreadable or changing domain
/// invalidates the complete snapshot; partial target lists are never exposed.
/// </summary>
public sealed class Steam2026FieldObjectObservationReader
{
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldPositionReader positionReader;
    private readonly FieldScriptLineStateReader lineStateReader;
    private readonly Steam2026FieldAudibleCueStateReader cueReader;
    private readonly FieldNavigationControlReader controlReader;
    private readonly FieldNavigationObjectReader navigationObjectReader;
    private readonly Func<int, string?> resolveItemName;
    private readonly Func<int, string?> resolveMateriaName;
    private readonly IReadOnlyDictionary<int, FieldNavigationObjectDefinition[]> definitionsByField;
    private readonly HashSet<int> navigationFieldsWithModelDefinitions;

    public string LastDiagnostic { get; private set; } = "not read";

    public Steam2026FieldObjectObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        Func<int, string?> resolveItemName,
        Func<int, string?> resolveMateriaName)
        : this(
            ValidatedTranslatedX86AddressSpaceFactory.Create(
                fingerprint,
                moduleBase,
                memory),
            resolveItemName,
            resolveMateriaName,
            FieldNavigationObjectCatalog.CreateAllFields())
    {
    }

    internal Steam2026FieldObjectObservationReader(
        ILegacyAddressSpace addressSpace,
        Func<int, string?> resolveItemName,
        Func<int, string?> resolveMateriaName,
        IEnumerable<FieldNavigationObjectDefinition> definitions)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveItemName = resolveItemName ?? throw new ArgumentNullException(nameof(resolveItemName));
        this.resolveMateriaName = resolveMateriaName ?? throw new ArgumentNullException(nameof(resolveMateriaName));
        ArgumentNullException.ThrowIfNull(definitions);
        var allDefinitions = definitions.ToArray();
        positionReader = new FieldPositionReader(addressSpace);
        lineStateReader = new FieldScriptLineStateReader(addressSpace);
        cueReader = new Steam2026FieldAudibleCueStateReader(addressSpace);
        controlReader = new FieldNavigationControlReader(addressSpace);
        navigationObjectReader = new FieldNavigationObjectReader(
            ReadCheckedInt32,
            ReadCheckedByte,
            resolveItemName,
            resolveMateriaName,
            allDefinitions,
            lineStateReader.IsEnabled);
        navigationFieldsWithModelDefinitions = allDefinitions
            .Where(definition => definition.TargetKind == FieldNavigationObjectTargetKind.Model)
            .Select(definition => definition.FieldId)
            .ToHashSet();
        definitionsByField = allDefinitions
            .Where(definition =>
                FieldNavigationObjectCueClassifier.Classify(definition) != FieldObjectCueKind.None)
            .GroupBy(definition => definition.FieldId)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
    }

    public bool TryReadSnapshot(out Steam2026FieldObjectResearchSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            if (!TryCaptureOwnership(out var before))
            {
                LastDiagnostic = "cue-bearing object ownership before-read is unavailable";
                return false;
            }

            if (!TryReadCandidate(before, out var candidate) ||
                !candidate.MatchesOwnership(before))
            {
                LastDiagnostic = "cue-bearing object candidate is unreadable or foreign-owned";
                return false;
            }

            if (!TryCaptureOwnership(out var middle) || before != middle)
            {
                LastDiagnostic = "cue-bearing object ownership changed before confirmation";
                return false;
            }

            if (!TryReadCandidate(middle, out var confirmation) ||
                !confirmation.MatchesOwnership(middle))
            {
                LastDiagnostic = "cue-bearing object confirmation is unreadable or foreign-owned";
                return false;
            }

            if (!candidate.Matches(confirmation))
            {
                LastDiagnostic = "cue-bearing object state changed between checked reads";
                return false;
            }

            if (!TryCaptureOwnership(out var after) || before != after)
            {
                LastDiagnostic = "cue-bearing object ownership changed after confirmation";
                return false;
            }

            snapshot = candidate.ToSnapshot();
            LastDiagnostic =
                $"field={snapshot.Position.FieldId}, playerModel={snapshot.Position.ModelIndex}, " +
                $"targets={snapshot.Targets.Count}, cue={snapshot.Cue.Reason}";
            return true;
        }
        catch (Exception ex)
        {
            snapshot = null!;
            LastDiagnostic = $"cue-bearing object read failed closed: {ex.Message}";
            return false;
        }
    }

    internal bool TryReadNavigationTargets(
        FieldPositionSnapshot expectedPosition,
        out IReadOnlyList<FieldNavigationTarget> targets)
    {
        targets = Array.Empty<FieldNavigationTarget>();
        try
        {
            if (!FieldPositionReader.IsUsable(expectedPosition) ||
                !TryCaptureOwnership(out var before) ||
                !MatchesExpectedPosition(before, expectedPosition) ||
                navigationFieldsWithModelDefinitions.Contains(before.FieldId) && before.EventTable == 0)
            {
                return false;
            }

            var candidate = navigationObjectReader.ReadTargets(expectedPosition).ToArray();
            if (!TryCaptureOwnership(out var middle) ||
                before != middle ||
                !MatchesExpectedPosition(middle, expectedPosition))
            {
                return false;
            }

            var confirmation = navigationObjectReader.ReadTargets(expectedPosition).ToArray();
            if (!candidate.AsSpan().SequenceEqual(confirmation) ||
                !TryCaptureOwnership(out var after) ||
                before != after)
            {
                return false;
            }

            targets = Array.AsReadOnly(candidate);
            LastDiagnostic =
                $"field={expectedPosition.FieldId}, playerModel={expectedPosition.ModelIndex}, " +
                $"navigationTargets={targets.Count}";
            return true;
        }
        catch (Exception ex)
        {
            targets = Array.Empty<FieldNavigationTarget>();
            LastDiagnostic = $"navigation object read failed closed: {ex.Message}";
            return false;
        }
    }

    private bool TryCaptureOwnership(out ObjectOwnership ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte(
                (uint)FieldPositionReader.AddressCurrentModule,
                out var module) ||
            !addressSpace.TryReadUInt16(
                (uint)FieldPositionReader.AddressFieldId,
                out var fieldId) ||
            !addressSpace.TryReadUInt16(
                (uint)FieldPositionReader.AddressFieldCurrentModelId,
                out var playerModelId) ||
            !addressSpace.TryReadByte(
                (uint)FieldPositionReader.AddressFieldNumModels,
                out var modelCount) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldPositionReader.AddressFieldModelsPtr,
                out var playerModelTable) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
                out var eventTable) ||
            module != FieldPositionReader.FieldModule ||
            playerModelTable == 0 ||
            playerModelId >= modelCount ||
            HasModelDefinitions(fieldId) && eventTable == 0)
        {
            return false;
        }

        ownership = new ObjectOwnership(
            module,
            fieldId,
            playerModelId,
            modelCount,
            playerModelTable,
            eventTable);
        return true;
    }

    private bool TryReadCandidate(
        ObjectOwnership ownership,
        out ObjectReadCandidate candidate)
    {
        candidate = null!;
        var positionResult = positionReader.Read();
        if (!positionResult.IsUsable ||
            !TryCalculatePlayerModelBase(ownership, out var expectedPlayerModelBase) ||
            positionResult.ModelBase != expectedPlayerModelBase ||
            positionResult.Position.CurrentModule != ownership.Module ||
            positionResult.Position.FieldId != ownership.FieldId ||
            positionResult.Position.ModelIndex != ownership.PlayerModelId ||
            !addressSpace.TryReadUInt16(
                (uint)FieldNavigationObjectReader.AddressFieldBankBase,
                out var gameMoment))
        {
            return false;
        }

        var control = controlReader.Read(positionResult.Position);
        if (!control.IsUsable || !cueReader.TryRead(out var cue) ||
            cue.Module != positionResult.Position.CurrentModule)
        {
            return false;
        }

        if (!definitionsByField.TryGetValue(ownership.FieldId, out var definitions))
        {
            candidate = new ObjectReadCandidate(
                positionResult.ModelBase,
                positionResult.Position,
                cue,
                control.Transform,
                gameMoment,
                [],
                []);
            return true;
        }

        var evidence = new DefinitionEvidence[definitions.Length];
        var targets = new List<FieldNavigationTarget>(definitions.Length);
        for (var index = 0; index < definitions.Length; index++)
        {
            if (!TryReadDefinition(
                    definitions[index],
                    ownership,
                    gameMoment,
                    out evidence[index],
                    out var target))
            {
                return false;
            }

            if (target is { } visibleTarget)
            {
                targets.Add(visibleTarget);
            }
        }

        candidate = new ObjectReadCandidate(
            positionResult.ModelBase,
            positionResult.Position,
            cue,
            control.Transform,
            gameMoment,
            evidence,
            targets.ToArray());
        return true;
    }

    private bool TryReadDefinition(
        FieldNavigationObjectDefinition definition,
        ObjectOwnership ownership,
        ushort gameMoment,
        out DefinitionEvidence evidence,
        out FieldNavigationTarget? target)
    {
        evidence = default;
        target = null;
        var identity = CreateDefinitionIdentity(definition);
        if (!TryValidateDefinition(definition, ownership.FieldId))
        {
            return false;
        }

        if (definition.MinimumGameMoment >= 0 && gameMoment < definition.MinimumGameMoment ||
            definition.MaximumGameMoment >= 0 && gameMoment > definition.MaximumGameMoment)
        {
            evidence = DefinitionEvidence.Create(identity, DefinitionState.OutsideGameMoment);
            return true;
        }

        byte? requiredValue = null;
        if (HasCondition(
                definition.RequiredBank,
                definition.RequiredAddress,
                definition.RequiredMask))
        {
            if (!TryReadBankByte(
                    definition.RequiredBank,
                    definition.RequiredAddress,
                    out var value))
            {
                return false;
            }

            requiredValue = value;
            if ((value & definition.RequiredMask) != definition.RequiredValue)
            {
                evidence = DefinitionEvidence.Create(
                    identity,
                    DefinitionState.RequiredStateNotMet,
                    requiredValue: requiredValue);
                return true;
            }
        }

        byte? collectedValue = null;
        if (HasCondition(
                definition.CollectedBank,
                definition.CollectedAddress,
                definition.CollectedMask))
        {
            if (!TryReadBankByte(
                    definition.CollectedBank,
                    definition.CollectedAddress,
                    out var value))
            {
                return false;
            }

            collectedValue = value;
            if ((value & definition.CollectedMask) == definition.CollectedMask)
            {
                evidence = DefinitionEvidence.Create(
                    identity,
                    DefinitionState.Collected,
                    requiredValue,
                    collectedValue);
                return true;
            }
        }

        return definition.TargetKind switch
        {
            FieldNavigationObjectTargetKind.Model => TryReadModelDefinition(
                definition,
                ownership,
                identity,
                requiredValue,
                collectedValue,
                out evidence,
                out target),
            FieldNavigationObjectTargetKind.Line => TryReadLineDefinition(
                definition,
                identity,
                requiredValue,
                collectedValue,
                out evidence,
                out target),
            _ => false
        };
    }

    private bool TryReadModelDefinition(
        FieldNavigationObjectDefinition definition,
        ObjectOwnership ownership,
        string identity,
        byte? requiredValue,
        byte? collectedValue,
        out DefinitionEvidence evidence,
        out FieldNavigationTarget? target)
    {
        evidence = default;
        target = null;
        if (ownership.EventTable == 0 ||
            !TryAdd(
                (uint)FieldNavigationObjectReader.AddressFieldModelIdArray,
                definition.EntityId,
                out var mappingAddress) ||
            !addressSpace.TryReadByte(mappingAddress, out var modelId))
        {
            return false;
        }

        if (modelId == byte.MaxValue || modelId >= ownership.ModelCount)
        {
            evidence = DefinitionEvidence.Create(
                identity,
                DefinitionState.ModelUnavailable,
                requiredValue,
                collectedValue,
                modelId: modelId);
            return true;
        }

        if (!TryAddScaled(
                ownership.EventTable,
                modelId,
                FieldNavigationObjectReader.FieldEventDataStride,
                out var eventAddress) ||
            !TryAdd(eventAddress, FieldNavigationObjectReader.VisibilityOffset, out var visibilityAddress) ||
            !addressSpace.TryReadByte(visibilityAddress, out var visibility))
        {
            return false;
        }

        if (visibility == 0)
        {
            evidence = DefinitionEvidence.Create(
                identity,
                DefinitionState.Hidden,
                requiredValue,
                collectedValue,
                modelId,
                visibility);
            return true;
        }

        if (!TryReadModelCoordinates(eventAddress, out var rawX, out var rawY, out var rawZ))
        {
            return false;
        }

        var label = ResolveLabel(definition);
        if (string.IsNullOrWhiteSpace(label))
        {
            evidence = DefinitionEvidence.Create(
                identity,
                DefinitionState.NameUnavailable,
                requiredValue,
                collectedValue,
                modelId,
                visibility,
                rawX,
                rawY,
                rawZ);
            return true;
        }

        target = CreateTarget(
            definition,
            label,
            FromModelFixedPoint(rawX),
            FromModelFixedPoint(rawY),
            FromModelFixedPoint(rawZ));
        evidence = DefinitionEvidence.Create(
            identity,
            DefinitionState.Visible,
            requiredValue,
            collectedValue,
            modelId,
            visibility,
            rawX,
            rawY,
            rawZ,
            label: label);
        return true;
    }

    private bool TryReadLineDefinition(
        FieldNavigationObjectDefinition definition,
        string identity,
        byte? requiredValue,
        byte? collectedValue,
        out DefinitionEvidence evidence,
        out FieldNavigationTarget? target)
    {
        evidence = default;
        target = null;
        if (!lineStateReader.TryRead(definition.EntityId, out var checkedEnabled) ||
            !TryAdd(
                (uint)FieldScriptLineStateReader.AddressFieldLineIndexByEntity,
                definition.EntityId,
                out var mappingAddress) ||
            !addressSpace.TryReadByte(mappingAddress, out var lineIndex) ||
            !TryAddScaled(
                (uint)FieldScriptLineStateReader.AddressFieldLineStates,
                lineIndex,
                FieldScriptLineStateReader.LineStateStride,
                out var stateAddress) ||
            !addressSpace.TryReadByte(stateAddress, out var lineState))
        {
            return false;
        }

        if (checkedEnabled != (lineState != 0))
        {
            return false;
        }

        if (lineState == 0)
        {
            evidence = DefinitionEvidence.Create(
                identity,
                DefinitionState.LineDisabled,
                requiredValue,
                collectedValue,
                lineIndex: lineIndex,
                lineState: lineState);
            return true;
        }

        var label = ResolveLabel(definition);
        if (string.IsNullOrWhiteSpace(label))
        {
            evidence = DefinitionEvidence.Create(
                identity,
                DefinitionState.NameUnavailable,
                requiredValue,
                collectedValue,
                lineIndex: lineIndex,
                lineState: lineState);
            return true;
        }

        target = CreateTarget(
            definition,
            label,
            definition.StaticX,
            definition.StaticY,
            definition.StaticZ);
        evidence = DefinitionEvidence.Create(
            identity,
            DefinitionState.Visible,
            requiredValue,
            collectedValue,
            lineIndex: lineIndex,
            lineState: lineState,
            label: label);
        return true;
    }

    private bool TryReadModelCoordinates(
        uint eventAddress,
        out int rawX,
        out int rawY,
        out int rawZ)
    {
        rawX = rawY = rawZ = 0;
        return TryAdd(eventAddress, FieldNavigationObjectReader.PositionXOffset, out var xAddress) &&
            TryAdd(eventAddress, FieldNavigationObjectReader.PositionYOffset, out var yAddress) &&
            TryAdd(eventAddress, FieldNavigationObjectReader.PositionZOffset, out var zAddress) &&
            addressSpace.TryReadInt32(xAddress, out rawX) &&
            addressSpace.TryReadInt32(yAddress, out rawY) &&
            addressSpace.TryReadInt32(zAddress, out rawZ);
    }

    private bool TryReadBankByte(int bank, int index, out byte value)
    {
        value = default;
        return TryResolveBankAddress(bank, index, out var address) &&
            addressSpace.TryReadByte(address, out value);
    }

    private static bool TryResolveBankAddress(int bank, int index, out uint address)
    {
        address = 0;
        if (index < 0)
        {
            return false;
        }

        var baseAddress = bank switch
        {
            1 => (uint)FieldNavigationObjectReader.AddressFieldBankBase,
            3 => (uint)FieldNavigationObjectReader.AddressFieldBankBase + 0x100u,
            5 => (uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase,
            11 => (uint)FieldNavigationObjectReader.AddressFieldBankBase + 0x200u,
            13 => (uint)FieldNavigationObjectReader.AddressFieldBankBase + 0x300u,
            15 => (uint)FieldNavigationObjectReader.AddressFieldBankBase + 0x400u,
            _ => 0u
        };
        return baseAddress != 0 && TryAdd(baseAddress, index, out address);
    }

    private static bool TryValidateDefinition(
        FieldNavigationObjectDefinition definition,
        int expectedFieldId)
    {
        if (definition.FieldId != expectedFieldId ||
            definition.EntityId is < 0 or > byte.MaxValue ||
            definition.NativeId < 0 ||
            definition.Quantity <= 0 ||
            definition.MinimumGameMoment < -1 ||
            definition.MaximumGameMoment < -1 ||
            definition.MinimumGameMoment >= 0 &&
            definition.MaximumGameMoment >= 0 &&
            definition.MinimumGameMoment > definition.MaximumGameMoment ||
            !IsConditionValid(
                definition.RequiredBank,
                definition.RequiredAddress,
                definition.RequiredMask) ||
            !IsConditionValid(
                definition.CollectedBank,
                definition.CollectedAddress,
                definition.CollectedMask) ||
            (definition.RequiredValue & definition.RequiredMask) != definition.RequiredValue)
        {
            return false;
        }

        return definition.Kind is FieldNavigationObjectKind.Item or FieldNavigationObjectKind.Materia;
    }

    private static bool IsConditionValid(int bank, int index, byte mask)
    {
        var absent = bank == -1 && index == -1 && mask == 0;
        var present = bank is 1 or 3 or 5 or 11 or 13 or 15 && index >= 0 && mask != 0;
        return absent || present;
    }

    private static bool HasCondition(int bank, int index, byte mask) =>
        bank >= 0 && index >= 0 && mask != 0;

    private bool HasModelDefinitions(ushort fieldId) =>
        definitionsByField.TryGetValue(fieldId, out var definitions) &&
        definitions.Any(definition => definition.TargetKind == FieldNavigationObjectTargetKind.Model);

    private static bool MatchesExpectedPosition(
        ObjectOwnership ownership,
        FieldPositionSnapshot expectedPosition) =>
        ownership.Module == expectedPosition.CurrentModule &&
        ownership.FieldId == expectedPosition.FieldId &&
        ownership.PlayerModelId == expectedPosition.ModelIndex;

    private int ReadCheckedInt32(int address) =>
        address >= 0 && addressSpace.TryReadInt32((uint)address, out var value)
            ? value
            : throw new InvalidDataException(
                $"Unreadable translated field-object int32 at 0x{address:X8}.");

    private byte ReadCheckedByte(int address) =>
        address >= 0 && addressSpace.TryReadByte((uint)address, out var value)
            ? value
            : throw new InvalidDataException(
                $"Unreadable translated field-object byte at 0x{address:X8}.");

    private string? ResolveLabel(FieldNavigationObjectDefinition definition)
    {
        var name = definition.Kind == FieldNavigationObjectKind.Item
            ? resolveItemName(definition.NativeId)
            : resolveMateriaName(definition.NativeId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var label = definition.Kind == FieldNavigationObjectKind.Materia
            ? $"{name} Materia"
            : name;
        return definition.Quantity > 1
            ? $"{label}, quantity {definition.Quantity}"
            : label;
    }

    private static FieldNavigationTarget CreateTarget(
        FieldNavigationObjectDefinition definition,
        string label,
        int x,
        int y,
        int z) =>
        new(
            definition.FieldId,
            FieldNavigationCategory.Objects,
            label,
            x,
            y,
            z,
            CreateStableId(definition),
            FieldNavigationObjectCueClassifier.Classify(definition));

    private static string CreateStableId(FieldNavigationObjectDefinition definition) =>
        $"object:{definition.FieldId}:{definition.EntityId}:{definition.Kind}:{definition.NativeId}:" +
        $"{definition.TargetKind}:{definition.StaticX}:{definition.StaticY}:{definition.StaticZ}:" +
        $"{definition.RequiredBank}:{definition.RequiredAddress}:{definition.RequiredMask}:{definition.RequiredValue}";

    private static string CreateDefinitionIdentity(FieldNavigationObjectDefinition definition) =>
        $"{CreateStableId(definition)}:{definition.CollectedBank}:{definition.CollectedAddress}:" +
        $"{definition.CollectedMask}:{definition.MinimumGameMoment}:{definition.MaximumGameMoment}:" +
        $"{definition.Quantity}:{FieldNavigationObjectCueClassifier.Classify(definition)}";

    private static int FromModelFixedPoint(int value) =>
        value / FieldNavigationObjectReader.ModelPositionFixedPointScale;

    private static bool TryCalculatePlayerModelBase(
        ObjectOwnership ownership,
        out uint modelBase) =>
        TryAddScaled(
            ownership.PlayerModelTable,
            ownership.PlayerModelId,
            FieldPositionReader.FieldModelStride,
            out modelBase);

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        result = 0;
        if (offset < 0)
        {
            return false;
        }

        var candidate = (ulong)address + (uint)offset;
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        result = (uint)candidate;
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

        var candidate = (ulong)address + ((ulong)(uint)index * (uint)stride);
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        result = (uint)candidate;
        return true;
    }

    private readonly record struct ObjectOwnership(
        byte Module,
        ushort FieldId,
        ushort PlayerModelId,
        byte ModelCount,
        uint PlayerModelTable,
        uint EventTable);

    private enum DefinitionState
    {
        OutsideGameMoment,
        RequiredStateNotMet,
        Collected,
        ModelUnavailable,
        Hidden,
        LineDisabled,
        NameUnavailable,
        Visible
    }

    private readonly record struct DefinitionEvidence(
        string Identity,
        DefinitionState State,
        byte? RequiredValue,
        byte? CollectedValue,
        byte? ModelId,
        byte? Visibility,
        int? RawX,
        int? RawY,
        int? RawZ,
        byte? LineIndex,
        byte? LineState,
        string? Label)
    {
        public static DefinitionEvidence Create(
            string identity,
            DefinitionState state,
            byte? requiredValue = null,
            byte? collectedValue = null,
            byte? modelId = null,
            byte? visibility = null,
            int? rawX = null,
            int? rawY = null,
            int? rawZ = null,
            byte? lineIndex = null,
            byte? lineState = null,
            string? label = null) =>
            new(
                identity,
                state,
                requiredValue,
                collectedValue,
                modelId,
                visibility,
                rawX,
                rawY,
                rawZ,
                lineIndex,
                lineState,
                label);
    }

    private sealed record ObjectReadCandidate(
        uint PlayerModelBase,
        FieldPositionSnapshot Position,
        FieldAudibleCueState Cue,
        FieldNavigationControlTransform Control,
        ushort GameMoment,
        DefinitionEvidence[] Evidence,
        FieldNavigationTarget[] Targets)
    {
        public bool Matches(ObjectReadCandidate other) =>
            PlayerModelBase == other.PlayerModelBase &&
            Position == other.Position &&
            Cue == other.Cue &&
            Control == other.Control &&
            GameMoment == other.GameMoment &&
            Evidence.AsSpan().SequenceEqual(other.Evidence) &&
            Targets.AsSpan().SequenceEqual(other.Targets);

        public bool MatchesOwnership(ObjectOwnership ownership) =>
            Position.CurrentModule == ownership.Module &&
            Position.FieldId == ownership.FieldId &&
            Position.ModelIndex == ownership.PlayerModelId &&
            TryCalculatePlayerModelBase(ownership, out var expectedModelBase) &&
            PlayerModelBase == expectedModelBase;

        public Steam2026FieldObjectResearchSnapshot ToSnapshot() =>
            new(Position, Cue, Control, Targets);
    }
}

public sealed class Steam2026FieldObjectResearchSnapshot :
    IEquatable<Steam2026FieldObjectResearchSnapshot>
{
    private readonly ReadOnlyCollection<FieldNavigationTarget> targets;

    internal Steam2026FieldObjectResearchSnapshot(
        FieldPositionSnapshot position,
        FieldAudibleCueState cue,
        FieldNavigationControlTransform control,
        IEnumerable<FieldNavigationTarget> targets)
    {
        Position = position;
        Cue = cue;
        Control = control;
        this.targets = Array.AsReadOnly(
            (targets ?? throw new ArgumentNullException(nameof(targets))).ToArray());
    }

    public FieldPositionSnapshot Position { get; }

    public FieldAudibleCueState Cue { get; }

    public FieldNavigationControlTransform Control { get; }

    public IReadOnlyList<FieldNavigationTarget> Targets => targets;

    public bool Equals(Steam2026FieldObjectResearchSnapshot? other) =>
        other is not null &&
        Position == other.Position &&
        Cue == other.Cue &&
        Control == other.Control &&
        targets.SequenceEqual(other.targets);

    public override bool Equals(object? obj) =>
        obj is Steam2026FieldObjectResearchSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(Cue);
        hash.Add(Control);
        foreach (var target in targets)
        {
            hash.Add(target);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"field={Position.FieldId}, playerModel={Position.ModelIndex}, targets={targets.Count}";
}
