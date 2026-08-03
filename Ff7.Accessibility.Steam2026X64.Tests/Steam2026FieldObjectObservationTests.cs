using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldObjectObservationTests
{
    private const int FieldId = 500;
    private const uint PlayerModelTable = 0x00010000;
    private const ushort PlayerModelId = 1;
    private const uint EventTable = 0x00020000;
    private const uint TriggerTable = 0x00030000;

    internal static void Run()
    {
        LoadsTheSharedFullGameCatalog();
        ReadsStableAuthoritativeModelTargets();
        ReadsEveryPersistentAndTemporaryBank();
        RequiresCheckedLineStateForStaticPickups();
        RejectsUnreadableRequiredDomains();
        RejectsTornRawEvidenceEvenWhenTargetsWouldMatch();
        PublishesImmutablePointerFreeObservations();
    }

    private static void LoadsTheSharedFullGameCatalog()
    {
        var definitions = FieldNavigationObjectCatalog.CreateAllFields();

        Equal(true, definitions.Count >= 339, "shared full-game object catalog count");
        Equal(
            "f2316fa54f27ebd1ce4e19242fc3c789337c1eba",
            FieldNavigationObjectCatalog.SourceCommit,
            "shared object catalog source identity");
        Equal(
            true,
            definitions.Any(definition =>
                FieldNavigationObjectCueClassifier.Classify(definition) == FieldObjectCueKind.Chest),
            "shared catalog contains explicitly classified chest targets");
        Equal(
            14,
            definitions.Count(definition =>
                definition.TargetKind == FieldNavigationObjectTargetKind.Line &&
                definition.Kind == FieldNavigationObjectKind.Item),
            "shared catalog checked LINE item count");
        Equal(
            true,
            definitions.Any(definition =>
                definition.FieldId == 224 &&
                definition.EntityId == 11 &&
                definition.TargetKind == FieldNavigationObjectTargetKind.Line &&
                definition.Kind == FieldNavigationObjectKind.Named &&
                definition.Label == "Optional battery socket"),
            "shared catalog identifies the optional wall-climb socket before it yields an item");
        Equal(
            2,
            definitions.Count(definition =>
                definition.TargetKind == FieldNavigationObjectTargetKind.Line &&
                definition.Kind == FieldNavigationObjectKind.Materia),
            "shared catalog checked LINE Materia count");
    }

    private static void ReadsStableAuthoritativeModelTargets()
    {
        var memory = CreateBaseMemory(modelCount: 6);
        WriteGameMoment(memory, 150);
        WriteBankByte(memory, bank: 3, index: 12, value: 0x08);
        WriteBankByte(memory, bank: 15, index: 32, value: 0x00);
        WriteObjectModel(memory, entityId: 4, modelId: 2, x: 385, y: 3125, z: -272);
        WriteObjectModel(memory, entityId: 6, modelId: 3, x: -420, y: 900, z: 12);
        WriteObjectModel(memory, entityId: 8, modelId: 4, x: 510, y: 800, z: 20);
        WriteObjectModel(memory, entityId: 9, modelId: 5, x: 640, y: 820, z: 20);

        var definitions = new[]
        {
            new FieldNavigationObjectDefinition(
                FieldId,
                EntityId: 4,
                Kind: FieldNavigationObjectKind.Item,
                NativeId: 7,
                Quantity: 2,
                CollectedBank: 15,
                CollectedAddress: 32,
                CollectedMask: 0x08,
                RequiredBank: 3,
                RequiredAddress: 12,
                RequiredMask: 0x08,
                RequiredValue: 0x08,
                SourceModelResource: "fieldbg_trb_wood.char"),
            new FieldNavigationObjectDefinition(
                FieldId,
                EntityId: 6,
                Kind: FieldNavigationObjectKind.Materia,
                NativeId: 53,
                SourceModelResource: "restore_materia.char"),
            new FieldNavigationObjectDefinition(
                FieldId,
                EntityId: 8,
                Kind: FieldNavigationObjectKind.Named,
                Label: "Not a pickup"),
            new FieldNavigationObjectDefinition(
                FieldId,
                EntityId: 9,
                Kind: FieldNavigationObjectKind.SavePoint,
                Label: "Save Point")
        };
        var reader = CreateReader(
            memory,
            definitions,
            itemId => itemId == 7 ? "Phoenix Down" : null,
            materiaId => materiaId == 53 ? "Restore" : null);

        Equal(true, reader.TryReadSnapshot(out var snapshot), "stable field-object snapshot");
        Equal(
            true,
            reader.LastDiagnostic.Contains("targets=2", StringComparison.Ordinal),
            "successful object diagnostics expose the authoritative target count");
        Equal(FieldId, snapshot.Position.FieldId, "field-object player field");
        Equal(100, snapshot.Position.X, "field-object player X");
        Equal(-200, snapshot.Position.Y, "field-object player Y");
        Equal(false, snapshot.Cue.IsSuppressed, "object snapshot owns audible gameplay state");
        Equal("gameplay", snapshot.Cue.Reason, "object snapshot audible reason");
        Equal(-96, snapshot.Control.SignedControlDirection, "object snapshot owns control transform");
        Equal(2, snapshot.Targets.Count, "only cue-bearing objects are published");

        var chest = snapshot.Targets[0];
        Equal("Phoenix Down, quantity 2", chest.Label, "exact item name and quantity");
        Equal(FieldObjectCueKind.Chest, chest.ObjectCueKind, "treasure-box model classification");
        Equal(385, chest.X, "chest live X");
        Equal(3125, chest.Y, "chest live Y");
        Equal(-272, chest.Z, "chest live Z");

        var materia = snapshot.Targets[1];
        Equal("Restore Materia", materia.Label, "exact Materia name");
        Equal(FieldObjectCueKind.Materia, materia.ObjectCueKind, "Materia cue classification");
        Equal(-420, materia.X, "Materia live X");
        Equal(900, materia.Y, "Materia live Y");
        Equal(12, materia.Z, "Materia live Z");

        Equal(
            true,
            reader.TryReadNavigationTargets(snapshot.Position, out var navigationTargets),
            "all authoritative navigation objects are read independently from spatial cues");
        Equal(4, navigationTargets.Count, "navigation includes pickups, named objects, and save points");
        Equal(
            "Not a pickup",
            navigationTargets.Single(target => target.StableId.Contains(":Named:", StringComparison.Ordinal)).Label,
            "catalog-backed named object remains available to navigation");
        var savePoint = navigationTargets.Single(target =>
            target.StableId.Contains(":SavePoint:", StringComparison.Ordinal));
        Equal("Save Point", savePoint.Label, "save point label remains available to navigation");
        Equal(true, savePoint.CompletesOnArrival, "save point navigation completes on arrival");

        WriteBankByte(memory, bank: 15, index: 32, value: 0x08);
        Equal(true, reader.TryReadSnapshot(out snapshot), "collected-state snapshot remains coherent");
        Equal(1, snapshot.Targets.Count, "collected chest is removed by its persistent bit");
        Equal("Restore Materia", snapshot.Targets[0].Label, "uncollected Materia remains");
    }

    private static void ReadsEveryPersistentAndTemporaryBank()
    {
        var memory = CreateBaseMemory();
        WriteGameMoment(memory, 150);
        var banks = new[] { 1, 3, 5, 11, 13, 15 };
        var definitions = new List<FieldNavigationObjectDefinition>();
        for (var index = 0; index < banks.Length; index++)
        {
            var entityId = 20 + index;
            var bankIndex = 40 + index;
            WriteBankByte(memory, banks[index], bankIndex, 0x01);
            WriteLineState(memory, entityId, lineIndex: 10 + index, enabled: true);
            definitions.Add(new FieldNavigationObjectDefinition(
                FieldId,
                entityId,
                FieldNavigationObjectKind.Item,
                NativeId: 100 + index,
                RequiredBank: banks[index],
                RequiredAddress: bankIndex,
                RequiredMask: 0x01,
                RequiredValue: 0x01,
                TargetKind: FieldNavigationObjectTargetKind.Line,
                StaticX: 1000 + index,
                StaticY: 2000 + index,
                StaticZ: 3000 + index,
                CueKindOverride: FieldObjectCueKind.Item,
                MinimumGameMoment: 100,
                MaximumGameMoment: 200));
        }

        var reader = CreateReader(
            memory,
            definitions,
            itemId => $"Item {itemId}",
            _ => null);
        Equal(true, reader.TryReadSnapshot(out var snapshot), "all native banks readable");
        Equal(6, snapshot.Targets.Count, "all native bank conditions pass");

        for (var index = 0; index < banks.Length; index++)
        {
            WriteBankByte(memory, banks[index], 40 + index, 0x00);
            Equal(true, reader.TryReadSnapshot(out snapshot), $"bank {banks[index]} false branch coherent");
            Equal(5, snapshot.Targets.Count, $"bank {banks[index]} gates only its definition");
            WriteBankByte(memory, banks[index], 40 + index, 0x01);
        }

        WriteGameMoment(memory, 99);
        Equal(true, reader.TryReadSnapshot(out snapshot), "early game moment coherent");
        Equal(0, snapshot.Targets.Count, "minimum game moment suppresses future pickups");
        WriteGameMoment(memory, 201);
        Equal(true, reader.TryReadSnapshot(out snapshot), "late game moment coherent");
        Equal(0, snapshot.Targets.Count, "maximum game moment suppresses expired pickups");
    }

    private static void RequiresCheckedLineStateForStaticPickups()
    {
        var memory = CreateBaseMemory();
        WriteGameMoment(memory, 1008);
        WriteLineState(memory, entityId: 35, lineIndex: 7, enabled: true);
        var definition = new FieldNavigationObjectDefinition(
            FieldId,
            EntityId: 35,
            Kind: FieldNavigationObjectKind.Item,
            NativeId: 241,
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 122,
            StaticY: 830,
            StaticZ: 0,
            CueKindOverride: FieldObjectCueKind.Item,
            MinimumGameMoment: 1008);
        var reader = CreateReader(
            memory,
            [definition],
            itemId => itemId == 241 ? "HP Shout" : null,
            _ => null);

        Equal(true, reader.TryReadSnapshot(out var snapshot), "enabled checked LINE snapshot");
        var target = snapshot.Targets.Single();
        Equal("HP Shout", target.Label, "LINE pickup native item name");
        Equal(122, target.X, "LINE pickup static X");
        Equal(830, target.Y, "LINE pickup static Y");

        WriteLineState(memory, entityId: 35, lineIndex: 7, enabled: false);
        Equal(true, reader.TryReadSnapshot(out snapshot), "disabled checked LINE snapshot");
        Equal(0, snapshot.Targets.Count, "disabled LINE pickup is absent");

        memory.Remove((uint)(FieldScriptLineStateReader.AddressFieldLineStates + 7 * FieldScriptLineStateReader.LineStateStride));
        Equal(false, reader.TryReadSnapshot(out snapshot), "unreadable required LINE state fails closed");
        Equal<Steam2026FieldObjectResearchSnapshot?>(null, snapshot, "unreadable LINE publishes no partial snapshot");

        memory = CreateBaseMemory();
        WriteGameMoment(memory, 1008);
        WriteLineState(memory, entityId: 35, lineIndex: 7, enabled: true);
        var stateAddress = (uint)(FieldScriptLineStateReader.AddressFieldLineStates +
            7 * FieldScriptLineStateReader.LineStateStride);
        var tearing = new MutatingMemory(
            memory,
            stateAddress,
            triggerRead: 2,
            () => memory.WriteByte(stateAddress, 0));
        reader = CreateReader(
            tearing,
            [definition],
            itemId => itemId == 241 ? "HP Shout" : null,
            _ => null);
        Equal(false, reader.TryReadSnapshot(out snapshot), "torn checked LINE state fails closed");
        Equal<Steam2026FieldObjectResearchSnapshot?>(null, snapshot, "torn LINE publishes no partial snapshot");
    }

    private static void RejectsUnreadableRequiredDomains()
    {
        var definition = CreateCheckedModelDefinition();
        var cases = new (uint Address, int Length, string Label)[]
        {
            ((uint)FieldPositionReader.AddressCurrentModule, 1, "module"),
            ((uint)FieldPositionReader.AddressFieldId, 2, "field ownership"),
            ((uint)FieldPositionReader.AddressFieldNumModels, 1, "model count"),
            ((uint)FieldPositionReader.AddressFieldModelsPtr, 4, "player model table"),
            (PlayerModelTable + PlayerModelId * FieldPositionReader.FieldModelStride + FieldPositionReader.ModelXOffset, 4, "player position"),
            ((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, 4, "event table"),
            ((uint)FieldNavigationObjectReader.AddressFieldBankBase, 2, "game moment"),
            (ResolveBankAddress(bank: 3, index: 10), 1, "required persistent bank"),
            (ResolveBankAddress(bank: 5, index: 11), 1, "collected temporary bank"),
            ((uint)(FieldNavigationObjectReader.AddressFieldModelIdArray + 4), 1, "entity-to-model mapping"),
            (EventAddress(2) + FieldNavigationObjectReader.VisibilityOffset, 1, "model visibility"),
            (EventAddress(2) + FieldNavigationObjectReader.PositionXOffset, 4, "model X"),
            (EventAddress(2) + FieldNavigationObjectReader.PositionYOffset, 4, "model Y"),
            (EventAddress(2) + FieldNavigationObjectReader.PositionZOffset, 4, "model Z")
        };

        foreach (var testCase in cases)
        {
            var memory = CreateCheckedModelMemory();
            memory.RemoveRange(testCase.Address, testCase.Length);
            var reader = CreateReader(
                memory,
                [definition],
                _ => "Phoenix Down",
                _ => null);

            Equal(false, reader.TryReadSnapshot(out var snapshot), $"unreadable {testCase.Label} fails closed");
            Equal(
                true,
                reader.LastDiagnostic.Length > 0,
                $"unreadable {testCase.Label} exposes a live diagnostic");
            Equal<Steam2026FieldObjectResearchSnapshot?>(
                null,
                snapshot,
                $"unreadable {testCase.Label} publishes no partial snapshot");
        }
    }

    private static void RejectsTornRawEvidenceEvenWhenTargetsWouldMatch()
    {
        var definition = CreateCheckedModelDefinition();
        AssertTorn(
            definition,
            ResolveBankAddress(bank: 3, index: 10),
            memory => WriteBankByte(memory, bank: 3, index: 10, value: 0x03),
            "required bank changes while the masked branch stays true");
        AssertTorn(
            definition,
            ResolveBankAddress(bank: 5, index: 11),
            memory => WriteBankByte(memory, bank: 5, index: 11, value: 0x02),
            "temporary collected bank changes while the object stays uncollected");
        AssertTorn(
            definition,
            (uint)FieldNavigationObjectReader.AddressFieldBankBase,
            memory => WriteGameMoment(memory, 151),
            "game moment changes within the allowed range");
        AssertTorn(
            definition,
            (uint)(FieldNavigationObjectReader.AddressFieldModelIdArray + 4),
            memory => memory.WriteByte(
                (uint)(FieldNavigationObjectReader.AddressFieldModelIdArray + 4),
                3),
            "entity mapping changes to an identical live model");
        AssertTorn(
            definition,
            EventAddress(2) + FieldNavigationObjectReader.VisibilityOffset,
            memory => memory.WriteByte(
                EventAddress(2) + FieldNavigationObjectReader.VisibilityOffset,
                2),
            "visibility byte changes but remains visible");
        AssertTorn(
            definition,
            EventAddress(2) + FieldNavigationObjectReader.PositionXOffset,
            memory => memory.WriteInt32(
                EventAddress(2) + FieldNavigationObjectReader.PositionXOffset,
                385 * FieldNavigationObjectReader.ModelPositionFixedPointScale + 1),
            "raw fixed-point X changes without changing the displayed coordinate");
        AssertTorn(
            definition,
            (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
            memory => memory.WriteUInt32(
                (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
                EventTable + 0x1000),
            "event table ownership changes");
    }

    private static void PublishesImmutablePointerFreeObservations()
    {
        var memory = CreateCheckedModelMemory();
        var reader = CreateReader(
            memory,
            [CreateCheckedModelDefinition()],
            _ => "Phoenix Down",
            _ => null);
        Equal(true, reader.TryReadSnapshot(out var snapshot), "immutable object snapshot fixture");

        var mutableTargets = (IList<FieldNavigationTarget>)snapshot.Targets;
        Equal(
            true,
            Throws<NotSupportedException>(() => mutableTargets[0] = default),
            "published object targets reject mutation");
        Equal(
            false,
            typeof(Steam2026FieldObjectObservationReader).GetMethods()
                .Any(method => method.Name.Contains("Hook", StringComparison.OrdinalIgnoreCase)),
            "field-object reader exposes no hook surface");
        Equal(
            false,
            typeof(Steam2026FieldObjectObservationReader).GetMethods()
                .Any(method => method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)),
            "field-object reader exposes no speech surface");
        foreach (var property in typeof(Steam2026FieldObjectResearchSnapshot).GetProperties())
        {
            Equal(
                false,
                property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase),
                $"field-object snapshot property {property.Name} is pointer-free");
        }
    }

    private static void AssertTorn(
        FieldNavigationObjectDefinition definition,
        uint watchedAddress,
        Action<Memory> mutate,
        string label)
    {
        var memory = CreateCheckedModelMemory();
        WriteObjectModel(memory, entityId: 99, modelId: 3, x: 385, y: 3125, z: -272);
        var tearing = new MutatingMemory(memory, watchedAddress, triggerRead: 2, () => mutate(memory));
        var reader = CreateReader(
            tearing,
            [definition],
            _ => "Phoenix Down",
            _ => null);

        Equal(false, reader.TryReadSnapshot(out var snapshot), $"torn {label} fails closed");
        Equal<Steam2026FieldObjectResearchSnapshot?>(null, snapshot, $"torn {label} publishes no partial snapshot");
    }

    private static Steam2026FieldObjectObservationReader CreateReader(
        ILegacyAddressSpace memory,
        IEnumerable<FieldNavigationObjectDefinition> definitions,
        Func<int, string?> resolveItemName,
        Func<int, string?> resolveMateriaName) =>
        new(memory, resolveItemName, resolveMateriaName, definitions);

    private static FieldNavigationObjectDefinition CreateCheckedModelDefinition() =>
        new(
            FieldId,
            EntityId: 4,
            Kind: FieldNavigationObjectKind.Item,
            NativeId: 7,
            CollectedBank: 5,
            CollectedAddress: 11,
            CollectedMask: 0x04,
            RequiredBank: 3,
            RequiredAddress: 10,
            RequiredMask: 0x01,
            RequiredValue: 0x01,
            SourceModelResource: "fieldbg_trb_wood.char",
            MinimumGameMoment: 100,
            MaximumGameMoment: 200);

    private static Memory CreateCheckedModelMemory()
    {
        var memory = CreateBaseMemory(modelCount: 4);
        WriteGameMoment(memory, 150);
        WriteBankByte(memory, bank: 3, index: 10, value: 0x01);
        WriteBankByte(memory, bank: 5, index: 11, value: 0x00);
        WriteObjectModel(memory, entityId: 4, modelId: 2, x: 385, y: 3125, z: -272);
        WriteObjectModel(memory, entityId: 98, modelId: 3, x: 385, y: 3125, z: -272);
        return memory;
    }

    private static Memory CreateBaseMemory(byte modelCount = 2)
    {
        var memory = new Memory();
        memory.WriteByte((uint)FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        memory.WriteUInt16((uint)FieldPositionReader.AddressFieldId, FieldId);
        memory.WriteUInt16((uint)FieldPositionReader.AddressFieldCurrentModelId, PlayerModelId);
        memory.WriteByte((uint)FieldPositionReader.AddressFieldNumModels, modelCount);
        memory.WriteUInt32((uint)FieldPositionReader.AddressFieldModelsPtr, PlayerModelTable);
        var playerModelBase = PlayerModelTable + PlayerModelId * FieldPositionReader.FieldModelStride;
        memory.WriteInt32(playerModelBase + FieldPositionReader.ModelXOffset, 100);
        memory.WriteInt32(playerModelBase + FieldPositionReader.ModelYOffset, -200);
        memory.WriteInt32(playerModelBase + FieldPositionReader.ModelZOffset, 300);
        memory.WriteByte(playerModelBase + FieldPositionReader.ModelDirectionOffset, 0xC0);
        var playerObjectBase = (uint)FieldPositionReader.AddressFieldModelsObjs +
            PlayerModelId * FieldPositionReader.FieldObjectStride;
        memory.WriteUInt16(playerObjectBase + FieldPositionReader.ObjectTriangleOffset, 9);
        memory.WriteUInt32((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
        memory.WriteByte((uint)FieldAudibleCueStateReader.AddressUserControl, 0);
        memory.WriteByte((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
        memory.WriteUInt16((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 0);
        memory.WriteUInt32((uint)FieldNavigationControlReader.AddressFieldTriggersPtr, TriggerTable);
        memory.WriteByte(TriggerTable + FieldNavigationControlReader.ControlDirectionOffset, 0xA0);
        WriteGameMoment(memory, 0);
        return memory;
    }

    private static void WriteObjectModel(
        Memory memory,
        int entityId,
        byte modelId,
        int x,
        int y,
        int z,
        byte visibility = 1)
    {
        memory.WriteByte((uint)(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId), modelId);
        var eventAddress = EventAddress(modelId);
        memory.WriteByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset, visibility);
        memory.WriteInt32(
            eventAddress + FieldNavigationObjectReader.PositionXOffset,
            checked(x * FieldNavigationObjectReader.ModelPositionFixedPointScale));
        memory.WriteInt32(
            eventAddress + FieldNavigationObjectReader.PositionYOffset,
            checked(y * FieldNavigationObjectReader.ModelPositionFixedPointScale));
        memory.WriteInt32(
            eventAddress + FieldNavigationObjectReader.PositionZOffset,
            checked(z * FieldNavigationObjectReader.ModelPositionFixedPointScale));
    }

    private static void WriteLineState(
        Memory memory,
        int entityId,
        int lineIndex,
        bool enabled)
    {
        memory.WriteByte(
            (uint)(FieldScriptLineStateReader.AddressFieldLineIndexByEntity + entityId),
            checked((byte)lineIndex));
        memory.WriteByte(
            (uint)(FieldScriptLineStateReader.AddressFieldLineStates +
                lineIndex * FieldScriptLineStateReader.LineStateStride),
            enabled ? (byte)1 : (byte)0);
    }

    private static void WriteGameMoment(Memory memory, ushort value) =>
        memory.WriteUInt16((uint)FieldNavigationObjectReader.AddressFieldBankBase, value);

    private static void WriteBankByte(Memory memory, int bank, int index, byte value) =>
        memory.WriteByte(ResolveBankAddress(bank, index), value);

    private static uint ResolveBankAddress(int bank, int index) =>
        bank switch
        {
            1 => (uint)(FieldNavigationObjectReader.AddressFieldBankBase + index),
            3 => (uint)(FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + index),
            5 => (uint)(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + index),
            11 => (uint)(FieldNavigationObjectReader.AddressFieldBankBase + 0x200 + index),
            13 => (uint)(FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + index),
            15 => (uint)(FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + index),
            _ => throw new ArgumentOutOfRangeException(nameof(bank))
        };

    private static uint EventAddress(int modelId) =>
        EventTable + checked((uint)modelId * FieldNavigationObjectReader.FieldEventDataStride);

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        internal void WriteByte(uint address, byte value) => bytes[address] = value;

        internal void WriteUInt16(uint address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void WriteInt32(uint address, int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void WriteUInt32(uint address, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void Remove(uint address) => bytes.Remove(address);

        internal void RemoveRange(uint address, int length)
        {
            for (var index = 0; index < length; index++)
            {
                bytes.Remove(checked(address + (uint)index));
            }
        }

        private void Write(uint address, ReadOnlySpan<byte> values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                bytes[checked(address + (uint)index)] = values[index];
            }
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(checked(virtualAddress + (uint)index), out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class MutatingMemory(
        Memory inner,
        uint watchedAddress,
        int triggerRead,
        Action mutate) : ILegacyAddressSpace
    {
        private int reads;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == watchedAddress && ++reads == triggerRead)
            {
                mutate();
            }

            return inner.TryRead(virtualAddress, destination);
        }
    }
}
