using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class BattleSenseSpeechTests
{
    internal static void Run()
    {
        DecodesNativeSenseControlsWithoutEatingSeparators();
        ReadsLocalizedElementNamesFromTheNativeTable();
        RedactsNativeSenseStateUntilThePersistentFlagIsSet();
        RejectsInvalidSceneEnemyIndices();
        RejectsStableMalformedSenseState();
        FailsClosedForAnUnsensedSenseHeader();
        FailsClosedForAnIncoherentSenseRead();
        FailsClosedWhenANativeWeaknessNameCannotResolve();
        KeepsFailClosedSuppressionAfterAnUnresolvedNativeFragment();
        KeepsFailClosedProtectionAcrossSplitWeaknessFragments();
        AdvancesFailClosedProtectionPastAnUnresolvedHpFragment();
        ProtectsWeaknessesAfterAnUnresolvedCombinedHpMpFragment();
        ReleasesAnUnrelatedOneNumberMessageAtTheHpStage();
        ReleasesAnUnrelatedOneNumberMessageAtTheMpStage();
        SpeaksOneCompleteSenseResultAndOnlyItsNativeFragmentsAreSuppressed();
    }

    private static void ReadsLocalizedElementNamesFromTheNativeTable()
    {
        var memory = new SenseMemory();
        string[] names =
        {
            "Feu", "Glace", "Foudre", "Terre", "Poison", "Gravite", "Eau", "Vent", "Sacre"
        };
        for (var index = 0; index < names.Length; index++)
        {
            memory.WriteFf7Text(
                BattleElementNameReader.AddressElementNames +
                    index * BattleElementNameReader.ElementNameSize,
                names[index],
                BattleElementNameReader.ElementNameSize);
        }

        var reader = new BattleElementNameReader(
            memory,
            Ff7GameLanguages.Get(Ff7GameLanguage.French));
        Equal("Feu", reader.Resolve(0), "localized native Fire element");
        Equal("Glace", reader.Resolve(1), "localized native Ice element");
        Equal(null, reader.Resolve(9), "out-of-range native element");
    }

    private static void DecodesNativeSenseControlsWithoutEatingSeparators()
    {
        var memory = new SenseMemory();
        WriteRuntimeText(memory, 0, 0x120,
        [
            BattleRuntimeTextReader.TargetNameControl, 0, 4,
            0xE3,
            BattleRuntimeTextReader.TargetIdControl, 0, 1,
            0xE3,
            BattleRuntimeTextReader.ElementControl, 0, 0,
            0xE2,
            BattleRuntimeTextReader.ElementControl, 0, 1,
            0xE3,
            0xFF
        ]);
        var reader = new BattleRuntimeTextReader(
            memory,
            _ => null,
            _ => null,
            actor => actor == 4 ? "Guard Hound" : null,
            _ => null,
            element => element switch { 0 => "Feu", 1 => "Glace", _ => null });

        var resolution = reader.ResolveDetailed(0x100);
        Equal("Guard Hound. B. Feu, Glace.", resolution?.Text, "decoded Sense controls");
        Equal(
            "TargetName:4,TargetId:1,Element:0,Element:1",
            string.Join(',', resolution!.Controls.Select(control => $"{control.Kind}:{control.Argument}")),
            "typed Sense controls");
    }

    private static void RedactsNativeSenseStateUntilThePersistentFlagIsSet()
    {
        var memory = CreateSenseMemory();
        var reader = CreateStateReader(memory);

        Equal(true, reader.TryReadSenseResult(4, out var hidden), "unsensed snapshot is coherent");
        Equal(false, hidden.IsSensed, "native sensed bit is absent");
        Equal(null, hidden.Level, "unsensed level is redacted");
        Equal(null, hidden.CurrentHp, "unsensed HP is redacted");
        Equal(null, hidden.CurrentMp, "unsensed MP is redacted");
        Equal(0, hidden.WeaknessElementIds.Count, "unsensed weakness is redacted");

        memory.WriteByte(
            BattleStateReader.AddressPersistentActorRecords +
                4 * BattleStateReader.PersistentActorRecordSize,
            BattleStateReader.SensedInformationFlag);
        Equal(true, reader.TryReadSenseResult(4, out var visible), "sensed snapshot is coherent");
        Equal(true, visible.IsSensed, "native sensed bit is set");
        Equal(3, visible.Level, "native enemy level");
        Equal(42, visible.CurrentHp, "native enemy HP");
        Equal(50, visible.MaximumHp, "native enemy maximum HP");
        Equal(7, visible.CurrentMp, "native enemy MP");
        Equal(9, visible.MaximumMp, "native enemy maximum MP");
        Equal("0,1", string.Join(',', visible.WeaknessElementIds), "native double-damage elements");
        Equal(true, reader.TryReadBattleActor(4, out var ordinaryTarget), "post-Sense target help snapshot");
        Equal(42, ordinaryTarget.CurrentHp, "post-Sense target help keeps earned HP");
        var targetTracker = new BattleTargetSpeechTracker();
        targetTracker.Observe(new BattleTargetSnapshot(
            true,
            true,
            1 << 4,
            4,
            0,
            0,
            ordinaryTarget));
        Equal(
            "Guard Hound. HP 42 of 50",
            targetTracker.Poll(),
            "post-Sense ordinary target speech stays name and HP only");

        var tornMemory = CreateSenseMemory();
        tornMemory.WriteByte(
            BattleStateReader.AddressPersistentActorRecords +
                4 * BattleStateReader.PersistentActorRecordSize,
            BattleStateReader.SensedInformationFlag);
        tornMemory.ReplaceBeforeRead(
            BattleStateReader.AddressEnemySceneIndexRecords,
            readNumber: 3,
            replacement: 1);
        Equal(
            false,
            CreateStateReader(tornMemory).TryReadSenseResult(4, out _),
            "torn actor/scene Sense state fails closed");
    }

    private static void RejectsStableMalformedSenseState()
    {
        var malformedMp = CreateSenseMemory();
        malformedMp.WriteByte(
            BattleStateReader.AddressPersistentActorRecords +
                4 * BattleStateReader.PersistentActorRecordSize,
            BattleStateReader.SensedInformationFlag);
        var actor = BattleStateReader.AddressBattleActors + 4 * BattleStateReader.BattleActorSize;
        malformedMp.WriteUInt16(actor + BattleStateReader.ActorCurrentMpOffset, 10);
        malformedMp.WriteUInt16(actor + BattleStateReader.ActorMaxMpOffset, 9);
        Equal(
            false,
            CreateStateReader(malformedMp).TryReadSenseResult(4, out _),
            "stable current MP above maximum fails closed");

        var malformedWeakness = CreateSenseMemory();
        malformedWeakness.WriteByte(
            BattleStateReader.AddressPersistentActorRecords +
                4 * BattleStateReader.PersistentActorRecordSize,
            BattleStateReader.SensedInformationFlag);
        malformedWeakness.WriteByte(
            BattleStateReader.AddressEnemyData + BattleStateReader.EnemyElementIdsOffset,
            BattleElementNameReader.ElementCount);
        Equal(
            false,
            CreateStateReader(malformedWeakness).TryReadSenseResult(4, out _),
            "stable unsupported weakness ID fails closed");
    }

    private static void RejectsInvalidSceneEnemyIndices()
    {
        (ushort Value, string Label)[] invalidIndices =
        [
            (0x0100, "high-byte scene enemy index"),
            (0xFFFF, "negative scene enemy index"),
            (3, "scene enemy index 3"),
            (4, "scene enemy index 4"),
            (5, "scene enemy index 5")
        ];

        foreach (var invalid in invalidIndices)
        {
            var memory = CreateSenseMemory();
            memory.WriteUInt16(BattleStateReader.AddressEnemySceneIndexRecords, invalid.Value);
            Equal(
                false,
                CreateStateReader(memory).TryReadSenseResult(4, out _),
                $"direct x86 {invalid.Label} fails closed");
        }
    }

    private static void FailsClosedForAnUnsensedSenseHeader()
    {
        var coordinator = CreateFailClosedCoordinator(
            new BattleSenseObservation(
                4,
                "Guard Hound",
                true,
                false,
                3,
                42,
                50,
                7,
                9,
                [0]));

        AssertPrivateSenseSequenceIsSilent(coordinator, "unsensed");
        coordinator.ObserveActiveBuffer(9);
        Equal("Damage 99.", coordinator.Poll(), "numeric message after fail-closed Sense remains audible");
    }

    private static void FailsClosedForAnIncoherentSenseRead()
    {
        var coordinator = CreateFailClosedCoordinator(null);

        AssertPrivateSenseSequenceIsSilent(coordinator, "incoherent");
        coordinator.ObserveActiveBuffer(7);
        Equal("Couldn't sense.", coordinator.Poll(), "native failure remains audible after fail-closed suppression");
    }

    private static void FailsClosedWhenANativeWeaknessNameCannotResolve()
    {
        var coordinator = CreateFailClosedCoordinator(
            new BattleSenseObservation(
                4,
                "Guard Hound",
                true,
                true,
                3,
                42,
                50,
                7,
                9,
                [1]));

        AssertPrivateSenseSequenceIsSilent(coordinator, "unresolved weakness name");
    }

    private static BattleSenseSpeechCoordinator CreateFailClosedCoordinator(
        BattleSenseObservation? observation)
    {
        var messages = new Dictionary<int, BattleRuntimeTextResolution>
        {
            [0x100] = Resolution(
                "Guard Hound B Level 3",
                new(BattleRuntimeTextControlKind.TargetName, 4),
                new(BattleRuntimeTextControlKind.TargetId, 1),
                new(BattleRuntimeTextControlKind.Number, 3)),
            [0x101] = Resolution(
                "HP 42/50",
                new(BattleRuntimeTextControlKind.Number, 42),
                new(BattleRuntimeTextControlKind.Number, 50)),
            [0x102] = Resolution(
                "MP 7/9",
                new(BattleRuntimeTextControlKind.Number, 7),
                new(BattleRuntimeTextControlKind.Number, 9)),
            [0x103] = Resolution(
                "Weak against Feu.",
                new BattleRuntimeTextControl(BattleRuntimeTextControlKind.Element, 0)),
            [0x104] = Resolution(
                "Weak against Glace.",
                new BattleRuntimeTextControl(BattleRuntimeTextControlKind.Element, 1)),
            [7] = Resolution("Couldn't sense."),
            [9] = Resolution(
                "Damage 99.",
                new BattleRuntimeTextControl(BattleRuntimeTextControlKind.Number, 99))
        };
        return new BattleSenseSpeechCoordinator(
            id => messages.GetValueOrDefault(id),
            _ => observation,
            element => element == 0 ? "Feu" : null);
    }

    private static void KeepsFailClosedProtectionAcrossSplitWeaknessFragments()
    {
        var coordinator = CreateFailClosedCoordinator(null);

        foreach (var fragment in new short[] { 0x100, 0x101, 0x102, 0x103, 0x104 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"split fail-closed fragment {fragment:X} stays private");
        }
    }

    private static void AdvancesFailClosedProtectionPastAnUnresolvedHpFragment()
    {
        var coordinator = CreateFailClosedCoordinator(null);

        foreach (var fragment in new short[] { 0x100, 0x105, 0x102, 0x103, 0x104 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"unresolved-HP sequence fragment {fragment:X} stays private");
        }
    }

    private static void ProtectsWeaknessesAfterAnUnresolvedCombinedHpMpFragment()
    {
        var coordinator = CreateFailClosedCoordinator(null);

        foreach (var fragment in new short[] { 0x100, 0x105, 0x103, 0x104 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"unresolved-combined sequence fragment {fragment:X} stays private");
        }
    }

    private static void ReleasesAnUnrelatedOneNumberMessageAtTheHpStage()
    {
        var coordinator = CreateFailClosedCoordinator(null);

        coordinator.ObserveActiveBuffer(0x100);
        Equal(null, coordinator.Poll(), "fail-closed header stays private before unrelated numeric text");
        coordinator.ObserveActiveBuffer(9);
        Equal("Damage 99.", coordinator.Poll(), "one-number text is not a native HP fragment");
    }

    private static void ReleasesAnUnrelatedOneNumberMessageAtTheMpStage()
    {
        var coordinator = CreateFailClosedCoordinator(null);

        foreach (var fragment in new short[] { 0x100, 0x101 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"private pre-MP fragment {fragment:X} stays silent");
        }

        coordinator.ObserveActiveBuffer(9);
        Equal("Damage 99.", coordinator.Poll(), "one-number text is not a native MP fragment");
    }

    private static void AssertPrivateSenseSequenceIsSilent(
        BattleSenseSpeechCoordinator coordinator,
        string label)
    {
        foreach (var fragment in new short[] { 0x100, 0x101, 0x102, 0x103 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"{label} Sense fragment {fragment:X} fails closed");
        }
    }

    private static void KeepsFailClosedSuppressionAfterAnUnresolvedNativeFragment()
    {
        var messages = new Dictionary<int, BattleRuntimeTextResolution>
        {
            [0x100] = Resolution(
                "Guard Hound B Level 3",
                new(BattleRuntimeTextControlKind.TargetName, 4),
                new(BattleRuntimeTextControlKind.TargetId, 1),
                new(BattleRuntimeTextControlKind.Number, 3)),
            [0x101] = Resolution(
                "HP 42/50",
                new(BattleRuntimeTextControlKind.Number, 42),
                new(BattleRuntimeTextControlKind.Number, 50)),
            [0x102] = Resolution(
                "MP 7/9",
                new(BattleRuntimeTextControlKind.Number, 7),
                new(BattleRuntimeTextControlKind.Number, 9)),
            [0x104] = Resolution(
                "Glace.",
                new BattleRuntimeTextControl(BattleRuntimeTextControlKind.Element, 1))
        };
        var sensed = new BattleSenseObservation(
            4,
            "Guard Hound",
            true,
            true,
            3,
            42,
            50,
            7,
            9,
            [0, 1]);
        var coordinator = new BattleSenseSpeechCoordinator(
            id => messages.GetValueOrDefault(id),
            _ => sensed,
            element => element switch { 0 => "Feu", 1 => "Glace", _ => null });

        coordinator.ObserveActiveBuffer(0x100);
        Equal(
            "Guard Hound B. Level 3. HP 42 of 50. MP 7 of 9. Weak against Feu and Glace.",
            coordinator.Poll(),
            "atomic speech before unresolved fragment");
        foreach (var fragment in new short[] { 0x101, 0x102 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"resolved private fragment {fragment:X} is suppressed");
        }

        coordinator.ObserveActiveBuffer(0x103);
        Equal(null, coordinator.Poll(), "unresolved native fragment remains silent");
        coordinator.ObserveActiveBuffer(0x104);
        Equal(null, coordinator.Poll(), "later weakness remains private after unresolved fragment");
    }

    private static void SpeaksOneCompleteSenseResultAndOnlyItsNativeFragmentsAreSuppressed()
    {
        var messages = new Dictionary<int, BattleRuntimeTextResolution>
        {
            [0x100] = Resolution(
                "Guard Hound B Level 3",
                new(BattleRuntimeTextControlKind.TargetName, 4),
                new(BattleRuntimeTextControlKind.TargetId, 1),
                new(BattleRuntimeTextControlKind.Number, 3)),
            [0x101] = Resolution(
                "HP 42/50",
                new(BattleRuntimeTextControlKind.Number, 42),
                new(BattleRuntimeTextControlKind.Number, 50)),
            [0x102] = Resolution(
                "MP 7/9",
                new(BattleRuntimeTextControlKind.Number, 7),
                new(BattleRuntimeTextControlKind.Number, 9)),
            [0x103] = Resolution(
                "Weak against Feu and Glace.",
                new(BattleRuntimeTextControlKind.Element, 0),
                new(BattleRuntimeTextControlKind.Element, 1)),
            [7] = Resolution("Couldn't sense."),
            [8] = Resolution("Limit break ready.")
        };
        var sensed = new BattleSenseObservation(
            4,
            "Guard Hound",
            true,
            true,
            3,
            42,
            50,
            7,
            9,
            [0, 1]);
        var coordinator = new BattleSenseSpeechCoordinator(
            id => messages.GetValueOrDefault(id),
            actor => actor == 4 ? sensed : null,
            element => element switch { 0 => "Feu", 1 => "Glace", _ => null });

        coordinator.ObserveActiveBuffer(0x100);
        Equal(
            "Guard Hound B. Level 3. HP 42 of 50. MP 7 of 9. Weak against Feu and Glace.",
            coordinator.Poll(),
            "complete native Sense result");
        foreach (var fragment in new short[] { 0x101, 0x102, 0x103 })
        {
            coordinator.ObserveActiveBuffer(fragment);
            Equal(null, coordinator.Poll(), $"Sense fragment {fragment:X} is suppressed");
        }

        coordinator.ObserveActiveBuffer(8);
        Equal("Limit break ready.", coordinator.Poll(), "unrelated native message remains audible");
        coordinator.ObserveActiveBuffer(7);
        Equal("Couldn't sense.", coordinator.Poll(), "native Sense failure remains audible");
    }

    private static BattleRuntimeTextResolution Resolution(
        string text,
        params BattleRuntimeTextControl[] controls) =>
        new(text, controls);

    private static SenseMemory CreateSenseMemory()
    {
        var memory = new SenseMemory();
        memory.WriteByte(BattleStateReader.AddressCurrentModule, BattleStateReader.BattleModule);
        memory.WriteUInt16(
            BattleStateReader.AddressEnemySceneIndexRecords,
            0);
        memory.WriteFf7Text(BattleStateReader.AddressEnemyData, "Guard Hound", BattleStateReader.EnemyNameLength);
        memory.WriteByte(
            BattleStateReader.AddressEnemyData + BattleStateReader.EnemyLevelOffset,
            3);
        for (var index = 0; index < BattleStateReader.EnemyElementSlotCount; index++)
        {
            memory.WriteByte(
                BattleStateReader.AddressEnemyData + BattleStateReader.EnemyElementIdsOffset + index,
                checked((byte)index));
            memory.WriteByte(
                BattleStateReader.AddressEnemyData + BattleStateReader.EnemyElementRatesOffset + index,
                index < 2 ? BattleStateReader.WeaknessElementRate : (byte)0);
        }

        var actor = BattleStateReader.AddressBattleActors + 4 * BattleStateReader.BattleActorSize;
        memory.WriteByte(actor + BattleStateReader.ActorInstanceIdOffset, 0);
        memory.WriteUInt16(actor + BattleStateReader.ActorCurrentMpOffset, 7);
        memory.WriteUInt16(actor + BattleStateReader.ActorMaxMpOffset, 9);
        memory.WriteInt32(actor + BattleStateReader.ActorCurrentHpOffset, 42);
        memory.WriteInt32(actor + BattleStateReader.ActorMaxHpOffset, 50);
        return memory;
    }

    private static BattleStateReader CreateStateReader(SenseMemory memory) =>
        new(memory, new SavemapPartyReader(memory));

    private static void WriteRuntimeText(SenseMemory memory, int slot, ushort offset, byte[] text)
    {
        memory.WriteUInt16(
            BattleRuntimeTextReader.AddressRuntimeTextOffsets + slot * sizeof(ushort),
            offset);
        memory.Write(BattleRuntimeTextReader.AddressRuntimeTextBuffer + offset, text);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}; actual {actual}");
        }
    }

    private sealed class SenseMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, Tear> tears = [];

        public bool TryRead(uint guestAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var address = checked(guestAddress + (uint)index);
                if (tears.TryGetValue(address, out var tear))
                {
                    tear.Reads++;
                    if (tear.Reads == tear.ReadNumber)
                    {
                        bytes[address] = tear.Replacement;
                    }
                }

                destination[index] = bytes.GetValueOrDefault(address);
            }

            return true;
        }

        internal void Write(int address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[checked((uint)address + (uint)index)] = values[index];
            }
        }

        internal void WriteByte(int address, byte value) => Write(address, [value]);

        internal void WriteUInt16(int address, int value) =>
            Write(address, BitConverter.GetBytes(checked((ushort)value)));

        internal void WriteInt32(int address, int value) =>
            Write(address, BitConverter.GetBytes(value));

        internal void WriteFf7Text(int address, string text, int capacity)
        {
            var encoded = text.Select(character => checked((byte)(character - 0x20))).ToList();
            encoded.Add(0xFF);
            while (encoded.Count < capacity)
            {
                encoded.Add(0);
            }

            Write(address, encoded.Take(capacity).ToArray());
        }

        internal void ReplaceBeforeRead(
            int address,
            int readNumber,
            byte replacement) =>
            tears[checked((uint)address)] = new Tear(readNumber, replacement);

        private sealed class Tear(int readNumber, byte replacement)
        {
            internal int ReadNumber { get; } = readNumber;

            internal byte Replacement { get; } = replacement;

            internal int Reads { get; set; }
        }
    }
}
