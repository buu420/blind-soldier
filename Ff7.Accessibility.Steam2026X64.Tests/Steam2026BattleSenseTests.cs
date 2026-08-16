using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal static class Steam2026BattleSenseTests
{
    internal static void Run()
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteByte(BattleStateReader.AddressEnemyData + BattleStateReader.EnemyLevelOffset, 3);
        for (var index = 0; index < BattleStateReader.EnemyElementSlotCount; index++)
        {
            fixture.WriteByte(
                BattleStateReader.AddressEnemyData + BattleStateReader.EnemyElementIdsOffset + index,
                checked((byte)index));
            fixture.WriteByte(
                BattleStateReader.AddressEnemyData + BattleStateReader.EnemyElementRatesOffset + index,
                index < 2 ? BattleStateReader.WeaknessElementRate : (byte)0);
        }
        string[] elementNames =
        {
            "Feu", "Glace", "Foudre", "Terre", "Poison", "Gravite", "Eau", "Vent", "Sacre"
        };
        for (var index = 0; index < elementNames.Length; index++)
        {
            WriteFf7Text(
                fixture,
                BattleElementNameReader.AddressElementNames +
                    index * BattleElementNameReader.ElementNameSize,
                elementNames[index],
                BattleElementNameReader.ElementNameSize);
        }

        var flagAddress = BattleStateReader.AddressPersistentActorRecords +
            4 * BattleStateReader.PersistentActorRecordSize;
        fixture.WriteByte(flagAddress, 0);
        var resolvers = CreateResolvers();
        var reader = CreateTranslatedReader(fixture, resolvers);
        Equal(true, reader.TryReadSenseResult(4, out var hidden), "translated unsensed Sense snapshot");
        Equal(false, hidden.IsSensed, "translated native flag absent");
        Equal(null, hidden.Level, "translated unsensed level redacted");
        Equal(0, hidden.WeaknessElementIds.Length, "translated unsensed weaknesses redacted");

        fixture.WriteByte(flagAddress, BattleStateReader.SensedInformationFlag);
        reader = CreateTranslatedReader(fixture, resolvers);
        Equal(true, reader.TryReadSenseResult(4, out var visible), "translated sensed Sense snapshot");
        Equal(true, visible.IsSensed, "translated native flag set");
        Equal(3, visible.Level, "translated enemy level");
        Equal("0,1", string.Join(',', visible.WeaknessElementIds), "translated weaknesses");

        WriteRuntimeText(fixture, 0, 0x120,
        [
            BattleRuntimeTextReader.TargetNameControl, 0, 4,
            BattleRuntimeTextReader.TargetIdControl, 0, 1,
            BattleRuntimeTextReader.NumberControl, 0, 3,
            0xFF
        ]);
        var coordinator = new Steam2026BattleAccessibilityCoordinator(
            SupportedFingerprint(),
            BattleObservationFixture.ModuleBase,
            fixture.Native,
            resolvers,
            new Steam2026BattleAccessibilityOptions(
                Menu: false,
                Target: false,
                Message: true,
                Results: false,
                Damage: false,
                Encounter: false,
                EnemyAction: false,
                Status: false));
        coordinator.ProcessBatch(
        [
            new Steam2026BattleRendererIngressSnapshot(
                1,
                DateTime.UtcNow,
                Steam2026BattleRendererCallbackKind.TextActivation,
                0x100,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default)
        ]);
        Equal(true, coordinator.TrySpeakPending(_ => true, out var speech), "translated atomic Sense speech");
        Equal(
            "Grunt. Level 3. HP 42 of 50. MP 12 of 18. Weak against Feu and Glace.",
            speech.Text,
            "translated complete Sense utterance");
        Equal(false, coordinator.TrySpeakPending(_ => true, out _), "translated single Sense utterance");
    }

    private static Steam2026BattleTextResolvers CreateResolvers() =>
        new(
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            language: Ff7GameLanguages.Get(Ff7GameLanguage.French));

    private static Steam2026BattleObservationReader CreateTranslatedReader(
        BattleObservationFixture fixture,
        Steam2026BattleTextResolvers resolvers) =>
        new(SupportedFingerprint(), BattleObservationFixture.ModuleBase, fixture.Native, resolvers);

    private static Steam2026FingerprintResult SupportedFingerprint() =>
        new(
            new RuntimeIdentity(
                Steam2026Fingerprint.SupportedRuntimeId,
                @"C:\fixture\FFVII.exe",
                Steam2026Fingerprint.SupportedSha256,
                true,
                string.Empty),
            true,
            "Exact supported fingerprint fixture.");

    private static void WriteRuntimeText(
        BattleObservationFixture fixture,
        int slot,
        ushort offset,
        byte[] text)
    {
        fixture.WriteUInt16(
            BattleRuntimeTextReader.AddressRuntimeTextOffsets + slot * sizeof(ushort),
            offset);
        fixture.Write((uint)(BattleRuntimeTextReader.AddressRuntimeTextBuffer + offset), text);
    }

    private static void WriteFf7Text(
        BattleObservationFixture fixture,
        int address,
        string text,
        int capacity)
    {
        var encoded = text.Select(character => checked((byte)(character - 0x20))).ToList();
        encoded.Add(0xFF);
        while (encoded.Count < capacity)
        {
            encoded.Add(0);
        }

        fixture.Write((uint)address, encoded.Take(capacity).ToArray());
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}; actual {actual}");
        }
    }
}
