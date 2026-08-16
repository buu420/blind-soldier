using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KalmExitPresentationTests
{
    private static readonly (string StableId, string Label)[] KalmTownGateways =
    [
        ("gateway:335:0:329", "Enter Item Store"),
        ("gateway:335:1:330", "Enter Bar"),
        ("gateway:335:2:328", "Enter Materia Store"),
        ("gateway:335:3:328", "Enter Weapon Store"),
        ("gateway:335:4:341", "Enter Kalm Traveler's house"),
        ("gateway:335:5:338", "Enter house with rear tower"),
        ("gateway:335:6:336", "Enter west house"),
        ("gateway:335:7:333", "Enter house beside the inn"),
        ("gateway:335:8:331", "Enter Kalm Inn")
    ];

    private static readonly (string StableId, string Label)[] KalmRearTowerExits =
    [
        ("gateway:338:2:340", "Enter rear tower"),
        ("gateway:340:0:338", "Return to house"),
        ("script-exit:340:4:335", "Exit rear tower to Kalm")
    ];

    private static readonly (
        int FieldId,
        int NativeId,
        string Label,
        FieldNavigationObjectTargetKind TargetKind,
        int CollectedBank,
        int CollectedAddress,
        byte CollectedMask)[] KalmTreasures =
    [
        (332, 6, "Megalixir", FieldNavigationObjectTargetKind.Line, 15, 85, 0x80),
        (337, 3, "Ether", FieldNavigationObjectTargetKind.Line, 15, 81, 0x01),
        (339, 72, "Guard Source", FieldNavigationObjectTargetKind.Line, 15, 85, 0x08),
        (341, 3, "Ether", FieldNavigationObjectTargetKind.Line, 15, 85, 0x10),
        (333, 3, "Ether", FieldNavigationObjectTargetKind.Model, 15, 85, 0x02),
        (340, 247, "Peacemaker", FieldNavigationObjectTargetKind.Model, 15, 84, 0x80)
    ];

    internal static void Run()
    {
        StableKalmGatewayIdsUseVerifiedLabels();
        KalmWorldMapSegmentsUseOneNativeGatedExit();
        NativeExitProviderAppliesKalmPresentationAfterDiscovery();
        KalmTreasureCatalogPublishesEveryCollectibleRecord();
    }

    private static void StableKalmGatewayIdsUseVerifiedLabels()
    {
        var targets = KalmTownGateways
            .Select((gateway, index) => Exit(335, gateway.Label, index, gateway.StableId))
            .Concat(
            [
                Exit(335, "Exit", 9, "gateway:335:9:2"),
                Exit(335, "Exit", 10, "gateway:335:10:2"),
                Exit(328, "Exit", 0, "gateway:328:0:335"),
                Exit(328, "Exit", 1, "gateway:328:1:335")
            ])
            .Concat(KalmRearTowerExits.Select((exit, index) =>
                Exit(
                    int.Parse(exit.StableId.Split(':')[1]),
                    "Exit",
                    index,
                    exit.StableId)))
            .ToArray();
        var resolved = new FieldExitLabelResolver(
            fieldId => fieldId == 335
                ? FieldMapNameResolution.Known(["Kalm"])
                : FieldMapNameResolution.Unknown,
            () => "Kalm").Resolve(targets);

        foreach (var gateway in KalmTownGateways)
        {
            Equal(
                gateway.Label,
                resolved.Single(target => target.StableId == gateway.StableId).Label,
                $"{gateway.StableId} stable Kalm label");
        }

        Equal(
            "Leave Materia Store for Kalm",
            resolved.Single(target => target.StableId == "gateway:328:0:335").Label,
            "Materia Store return is distinct");
        Equal(
            "Leave Weapon Store for Kalm",
            resolved.Single(target => target.StableId == "gateway:328:1:335").Label,
            "Weapon Store return is distinct");
        foreach (var exit in KalmRearTowerExits)
        {
            Equal(
                exit.Label,
                resolved.Single(target => target.StableId == exit.StableId).Label,
                $"{exit.StableId} stable Kalm rear-tower label");
        }
    }

    private static void KalmTreasureCatalogPublishesEveryCollectibleRecord()
    {
        var catalog = FieldNavigationObjectCatalog.CreateAllFields();
        foreach (var treasure in KalmTreasures)
        {
            var records = catalog.Where(definition =>
                definition.FieldId == treasure.FieldId &&
                definition.Kind == FieldNavigationObjectKind.Item &&
                definition.NativeId == treasure.NativeId).ToArray();
            Equal(1, records.Length, $"Kalm {treasure.Label} catalog count");
            var definition = records.Single();
            Equal(treasure.TargetKind, definition.TargetKind, $"Kalm {treasure.Label} target kind");
            Equal(treasure.CollectedBank, definition.CollectedBank, $"Kalm {treasure.Label} collection bank");
            Equal(treasure.CollectedAddress, definition.CollectedAddress, $"Kalm {treasure.Label} collection address");
            Equal(treasure.CollectedMask, definition.CollectedMask, $"Kalm {treasure.Label} collection mask");

            var memory = new Dictionary<int, byte>();
            if (definition.TargetKind == FieldNavigationObjectTargetKind.Model)
            {
                ConfigureVisibleModel(memory, definition.EntityId);
            }

            var reader = new FieldNavigationObjectReader(
                address => ReadInt32(memory, address),
                address => memory.GetValueOrDefault(address),
                nativeId => nativeId == treasure.NativeId ? treasure.Label : null,
                _ => null,
                [definition],
                _ => true);
            var published = reader.ReadTargets(Position(definition.FieldId));
            Equal(1, published.Count, $"collectible Kalm {treasure.Label} publication count");
            Equal(treasure.Label, published.Single().Label, $"collectible Kalm {treasure.Label} label");

            memory[ResolveBankAddress(definition.CollectedBank, definition.CollectedAddress)] =
                definition.CollectedMask;
            Equal(
                0,
                reader.ReadTargets(Position(definition.FieldId)).Count,
                $"collected Kalm {treasure.Label} must not publish");
        }

    }

    private static void NativeExitProviderAppliesKalmPresentationAfterDiscovery()
    {
        bool? complete = false;
        var provider = new NativeFieldExitTargetProvider(
            new FieldGatewayTargetReader(new DictionaryLegacyAddressSpace(new Dictionary<int, byte>())),
            _ =>
            [
                Exit(335, "Exit", 9, "gateway:335:9:2"),
                Exit(335, "Exit", 10, "gateway:335:10:2")
            ],
            TimeSpan.Zero,
            TimeSpan.Zero,
            () => new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            new FieldExitLabelResolver(_ => FieldMapNameResolution.Unknown, () => "Kalm"),
            new FieldExitPresentationPolicy(() => complete));
        var position = Position(335);

        Equal(0, provider.ReadTargets(position).Count, "new Kalm field waits for native exits to settle");
        Equal(0, provider.ReadTargets(position).Count, "incomplete Kalm hides both world-boundary segments");

        complete = true;
        var published = provider.ReadTargets(position);
        Equal(1, published.Count, "native exit provider publishes one completed Kalm world-map exit");
        Equal("Leave Kalm for the World Map", published.Single().Label, "native exit provider applies Kalm presentation");
    }

    private static void KalmWorldMapSegmentsUseOneNativeGatedExit()
    {
        bool? complete = false;
        var policy = new FieldExitPresentationPolicy(() => complete);
        FieldNavigationTarget[] targets =
        [
            Exit(335, "Exit to World Map", 90, "gateway:335:9:2"),
            Exit(335, "Exit to World Map", 100, "gateway:335:10:2"),
            Exit(328, "Leave Materia Store for Kalm", 0, "gateway:328:0:335"),
            Exit(328, "Leave Weapon Store for Kalm", 10, "gateway:328:1:335")
        ];

        Equal(
            2,
            policy.Apply(targets).Count,
            "Kalm world-map boundary stays hidden until the native completion flag is set");

        complete = null;
        Equal(
            2,
            policy.Apply(targets).Count,
            "unreadable Kalm completion state fails closed");

        complete = true;
        var published = policy.Apply(targets);
        Equal(3, published.Count, "only the two Kalm world-boundary segments collapse");
        var worldExit = published.Single(target => target.Label == "Leave Kalm for the World Map");
        Equal("gateway:335:9:2", worldExit.StableId, "collapsed Kalm exit retains a navigable native segment");
        Equal(900, worldExit.X, "collapsed Kalm exit retains the first native position");
        Equal(
            2,
            published.Count(target => target.FieldId == 328),
            "same-destination Kalm storefront returns remain independent");
    }

    private static FieldNavigationTarget Exit(int fieldId, string label, int index, string stableId) =>
        new(
            fieldId,
            FieldNavigationCategory.Exits,
            label,
            index * 10,
            0,
            0,
            stableId,
            DestinationFieldIds: [int.Parse(stableId.Split(':')[3])]);

    private static FieldPositionSnapshot Position(int fieldId) =>
        new(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0);

    private static void ConfigureVisibleModel(IDictionary<int, byte> memory, int entityId)
    {
        const int eventTable = 0x02500000;
        WriteInt32(memory, FieldNavigationObjectReader.AddressFieldEventDataPtr, eventTable);
        memory[FieldPositionReader.AddressFieldNumModels] = 2;
        memory[FieldNavigationObjectReader.AddressFieldModelIdArray + entityId] = 1;
        memory[eventTable + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 1;
    }

    private static int ReadInt32(IReadOnlyDictionary<int, byte> memory, int address) =>
        memory.GetValueOrDefault(address) |
        memory.GetValueOrDefault(address + 1) << 8 |
        memory.GetValueOrDefault(address + 2) << 16 |
        memory.GetValueOrDefault(address + 3) << 24;

    private static int ResolveBankAddress(int bank, int address) => bank switch
    {
        1 => FieldNavigationObjectReader.AddressFieldBankBase + address,
        3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + address,
        5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + address,
        11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200 + address,
        13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + address,
        15 => FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + address,
        _ => throw new ArgumentOutOfRangeException(nameof(bank))
    };

    private static void WriteInt32(IDictionary<int, byte> memory, int address, int value)
    {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
        memory[address + 2] = (byte)(value >> 16);
        memory[address + 3] = (byte)(value >> 24);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
