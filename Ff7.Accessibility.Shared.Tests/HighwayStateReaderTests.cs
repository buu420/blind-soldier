using Ff7.Accessibility.LegacyLayout;

internal static class HighwayStateReaderTests
{
    internal static void Run()
    {
        ReadsCompleteStableNativeHighwayState();
        ExcludesTheNonEnemyActorAndPreservesEnemyLifecycleState();
        AcceptsNormalDynamicHighwayTransitions();
        OmitsTransientPartyEntriesWithoutDroppingCombatState();
        RejectsUnreadableInvalidAndUnownedSnapshots();
    }

    private static void ReadsCompleteStableNativeHighwayState()
    {
        var memory = CreateStableMemory();
        var reader = new HighwayStateReader(memory);

        Equal(true, reader.TryRead(out var snapshot), "stable highway snapshot");
        Equal(HighwayStateReader.HighwayModule, snapshot.Module, "native highway module");
        Equal(1000d, snapshot.Cloud.LateralUnits, "Cloud fixed-point lateral position");
        Equal(2000d, snapshot.Cloud.LongitudinalUnits, "Cloud fixed-point longitudinal position");
        Equal(-250d, snapshot.Truck.LateralUnits, "truck fixed-point lateral position");
        Equal(3200d, snapshot.Truck.LongitudinalUnits, "truck fixed-point longitudinal position");
        Equal(3210, snapshot.Score, "native highway score");
        Equal(true, snapshot.IsStoryChase, "native story chase flag");

        Equal(4, snapshot.PartyHealth.Count, "initialized party health entries only");
        Equal("Cloud", snapshot.PartyHealth[0].Name, "party slot zero name");
        Equal(700, snapshot.PartyHealth[0].CurrentHp, "party current HP");
        Equal(900, snapshot.PartyHealth[0].MaximumHp, "party maximum HP");
        Equal("Red XIII", snapshot.PartyHealth[4 - 1].Name, "last initialized chase member name");
        Contains(reader.LastDiagnostic, "enemies=3", "stable snapshot diagnostic");
    }

    private static void ExcludesTheNonEnemyActorAndPreservesEnemyLifecycleState()
    {
        var memory = CreateStableMemory();
        var reader = new HighwayStateReader(memory);

        Equal(true, reader.TryRead(out var snapshot), "enemy slot snapshot");
        Equal(3, snapshot.Enemies.Count, "only native enemy slots two through four");
        Equal(2, snapshot.Enemies[0].Slot, "first enemy slot");
        Equal(10, snapshot.Enemies[0].Type, "first enemy native AI type");
        Equal(true, snapshot.Enemies[0].IsActive, "state zero living enemy is active");
        Equal(3, snapshot.Enemies[1].Slot, "second enemy slot");
        Equal(true, snapshot.Enemies[1].IsActive, "state one living enemy is active");
        Equal(4, snapshot.Enemies[2].Slot, "third enemy slot");
        Equal(false, snapshot.Enemies[2].IsActive, "destroyed native enemy remains visible as inactive state");
        Equal(false, snapshot.Enemies.Any(enemy => enemy.Slot == 5), "slot five is never published as an enemy");

        var specialActorAddress =
            (uint)HighwayStateReader.AddressActorTable +
            (uint)(5 * HighwayStateReader.ActorStride + HighwayStateReader.ActorStateOffset);
        memory.Remove(specialActorAddress);
        Equal(
            true,
            new HighwayStateReader(memory).TryRead(out _),
            "the unrelated sixth actor is not required for highway accessibility coherence");

        var irrelevantCloudActorHp = CreateStableMemory();
        WriteInt32(
            irrelevantCloudActorHp,
            ActorAddress(0, HighwayStateReader.ActorHitPointsOffset),
            -1);
        Equal(
            true,
            new HighwayStateReader(irrelevantCloudActorHp).TryRead(out _),
            "enemy-only HP validation does not guess a meaning for Cloud's actor field");
    }

    private static void AcceptsNormalDynamicHighwayTransitions()
    {
        var moving = CreateStableMemory();
        var replacement = new byte[HighwayStateReader.ActorCount * HighwayStateReader.ActorStride];
        Equal(
            true,
            moving.TryRead((uint)HighwayStateReader.AddressActorTable, replacement),
            "actor table fixture snapshot");
        replacement[HighwayStateReader.ActorLateralOffset] ^= 0x40;
        var movingActors = new TearingLegacyAddressSpace(
            moving,
            (uint)HighwayStateReader.AddressActorTable,
            replacement);
        Equal(
            true,
            new HighwayStateReader(movingActors).TryRead(out _),
            "ordinary actor movement between polls does not invalidate module ownership");

        var defeated = CreateStableMemory();
        WriteInt32(
            defeated,
            ActorAddress(4, HighwayStateReader.ActorHitPointsOffset),
            -40);
        Equal(
            true,
            new HighwayStateReader(defeated).TryRead(out var defeatedSnapshot),
            "native negative defeated HP remains a valid highway snapshot");
        Equal(false, defeatedSnapshot.Enemies[2].IsActive, "negative HP actor is inactive");
    }

    private static void OmitsTransientPartyEntriesWithoutDroppingCombatState()
    {
        var partial = CreateStableMemory();
        WritePartyHealth(partial, 1, current: 0, maximum: ushort.MaxValue);
        var reader = new HighwayStateReader(partial);

        Equal(
            true,
            reader.TryRead(out var snapshot),
            "partially initialized optional party entry does not reject combat state");
        Equal(3, snapshot.PartyHealth.Count, "partial optional party entry is omitted");
        Equal(3, snapshot.Enemies.Count, "enemy state remains available with partial party HP");
        Equal(3210, snapshot.Score, "score remains available with partial party HP");
    }

    private static void RejectsUnreadableInvalidAndUnownedSnapshots()
    {
        var unreadable = CreateStableMemory();
        unreadable.Remove((uint)HighwayStateReader.AddressActorTable + HighwayStateReader.ActorLateralOffset);
        Equal(
            false,
            new HighwayStateReader(unreadable).TryRead(out _),
            "one unreadable actor byte rejects the whole snapshot");

        var invalidHp = CreateStableMemory();
        WriteUInt16(
            invalidHp,
            (uint)HighwayStateReader.AddressPartyHealth + HighwayStateReader.PartyCurrentHpOffset,
            901);
        Equal(
            false,
            new HighwayStateReader(invalidHp).TryRead(out _),
            "current HP above maximum rejects the whole snapshot");

        var invalidActiveType = CreateStableMemory();
        WriteInt32(
            invalidActiveType,
            ActorAddress(2, HighwayStateReader.ActorTypeOffset),
            99);
        Equal(
            false,
            new HighwayStateReader(invalidActiveType).TryRead(out _),
            "unknown active enemy type rejects the whole snapshot");

        var invalidEnemyHp = CreateStableMemory();
        WriteInt32(
            invalidEnemyHp,
            ActorAddress(2, HighwayStateReader.ActorHitPointsOffset),
            -100_001);
        Equal(
            false,
            new HighwayStateReader(invalidEnemyHp).TryRead(out _),
            "enemy HP outside the bounded transient range rejects the snapshot");

        var stable = CreateStableMemory();
        var tornModule = new TearingLegacyAddressSpace(
            stable,
            (uint)HighwayStateReader.AddressCurrentModule,
            [1]);
        Equal(
            false,
            new HighwayStateReader(tornModule).TryRead(out _),
            "module transition rejects the whole snapshot");
    }

    private static ContiguousLegacyAddressSpace CreateStableMemory()
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)HighwayStateReader.AddressCurrentModule, [HighwayStateReader.HighwayModule]);
        memory.Write(
            (uint)HighwayStateReader.AddressActorTable,
            new byte[HighwayStateReader.ActorCount * HighwayStateReader.ActorStride]);
        memory.Write(
            (uint)HighwayStateReader.AddressPartyHealth,
            new byte[HighwayStateReader.PartySlotCount * HighwayStateReader.PartyHealthStride]);

        WriteActor(memory, 0, state: 0, lateralUnits: 1000, longitudinalUnits: 2000, hp: 100, type: 0);
        WriteActor(memory, 1, state: 0, lateralUnits: -250, longitudinalUnits: 3200, hp: 100, type: 0);
        WriteActor(memory, 2, state: 0, lateralUnits: 850, longitudinalUnits: 2200, hp: 5, type: 10);
        WriteActor(memory, 3, state: 1, lateralUnits: 1200, longitudinalUnits: 3000, hp: 30, type: 12);
        WriteActor(memory, 4, state: 2, lateralUnits: 400, longitudinalUnits: 1800, hp: 0, type: 11);

        WritePartyHealth(memory, 0, current: 700, maximum: 900);
        WritePartyHealth(memory, 1, current: 610, maximum: 650);
        WritePartyHealth(memory, 2, current: 540, maximum: 700);
        WritePartyHealth(memory, 3, current: ushort.MaxValue, maximum: ushort.MaxValue);
        WritePartyHealth(memory, 4, current: 430, maximum: 600);

        WriteInt32(memory, (uint)HighwayStateReader.AddressStoryMode, 0);
        WriteInt32(memory, (uint)HighwayStateReader.AddressScore, 3210);
        return memory;
    }

    private static void WriteActor(
        ContiguousLegacyAddressSpace memory,
        int slot,
        int state,
        int lateralUnits,
        int longitudinalUnits,
        int hp,
        int type)
    {
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorStateOffset), state);
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorSecondaryStateOffset), 0);
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorLateralOffset), checked(lateralUnits * 256));
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorLongitudinalOffset), checked(longitudinalUnits * 256));
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorHitPointsOffset), hp);
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorTypeOffset), type);
        WriteInt32(memory, ActorAddress(slot, HighwayStateReader.ActorAttackTimerOffset), 0);
    }

    private static void WritePartyHealth(
        ContiguousLegacyAddressSpace memory,
        int slot,
        ushort current,
        ushort maximum)
    {
        var address = (uint)HighwayStateReader.AddressPartyHealth +
            (uint)(slot * HighwayStateReader.PartyHealthStride);
        WriteUInt16(memory, address + HighwayStateReader.PartyMaximumHpOffset, maximum);
        WriteUInt16(memory, address + HighwayStateReader.PartyCurrentHpOffset, current);
    }

    private static uint ActorAddress(int slot, int offset) =>
        (uint)HighwayStateReader.AddressActorTable +
        (uint)(slot * HighwayStateReader.ActorStride + offset);

    private static void WriteInt32(ContiguousLegacyAddressSpace memory, uint address, int value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void WriteUInt16(ContiguousLegacyAddressSpace memory, uint address, ushort value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void Contains(string actual, string expected, string label)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{actual}' to contain '{expected}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
