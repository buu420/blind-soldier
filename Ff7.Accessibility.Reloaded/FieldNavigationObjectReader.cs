using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationObjectKind
{
    Item,
    Materia,
    Named,
    SavePoint
}

public enum FieldNavigationObjectTargetKind
{
    Model,
    Line,
    Location
}

public readonly record struct FieldNavigationObjectDefinition(
    int FieldId,
    int EntityId,
    FieldNavigationObjectKind Kind,
    int NativeId = -1,
    string? Label = null,
    int Quantity = 1,
    int CollectedBank = -1,
    int CollectedAddress = -1,
    byte CollectedMask = 0,
    int RequiredBank = -1,
    int RequiredAddress = -1,
    byte RequiredMask = 0,
    byte RequiredValue = 0,
    string? SourceFieldName = null,
    string? SourceEntityName = null,
    string? SourceModelResource = null,
    FieldNavigationObjectTargetKind TargetKind = FieldNavigationObjectTargetKind.Model,
    int StaticX = 0,
    int StaticY = 0,
    int StaticZ = 0,
    FieldObjectCueKind? CueKindOverride = null,
    int MinimumGameMoment = -1,
    int MaximumGameMoment = -1);

public static class FieldNavigationObjectCatalog
{
    private const string ResourceName = "Ff7.Accessibility.Reloaded.Assets.navigation.field_objects.json";
    private static readonly Lazy<FieldNavigationObjectCatalogDocument> FullCatalog = new(Load);
    private static readonly Lazy<IReadOnlyList<FieldNavigationObjectDefinition>> AllDefinitions = new(
        () =>
        [
            .. FullCatalog.Value.Definitions,
            .. ShinraElevatorObjectCatalog.Create()
        ]);

    public static IReadOnlyList<FieldNavigationObjectDefinition> CreateAllFields() =>
        AllDefinitions.Value;

    public static IReadOnlyList<FieldNavigationObjectDefinition> CreateOpeningReactor() =>
        FullCatalog.Value.Definitions
            .Where(definition => definition.FieldId is >= 116 and <= 132)
            .ToArray();

    public static string SourceCommit => FullCatalog.Value.SourceCommit;

    private static FieldNavigationObjectCatalogDocument Load()
    {
        using var stream = typeof(FieldNavigationObjectCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded field navigation object catalog {ResourceName}.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var document = JsonSerializer.Deserialize<FieldNavigationObjectCatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Embedded field navigation object catalog is empty.");
        if (document.SchemaVersion != 2 || document.Definitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unsupported field navigation object catalog schema {document.SchemaVersion}.");
        }

        return document;
    }

    private sealed class FieldNavigationObjectCatalogDocument
    {
        public int SchemaVersion { get; set; }

        public string SourceCommit { get; set; } = string.Empty;

        public List<FieldNavigationObjectDefinition> Definitions { get; set; } = [];
    }
}

public static class FieldNavigationObjectCueClassifier
{
    public static FieldObjectCueKind Classify(FieldNavigationObjectDefinition definition)
    {
        if (definition.CueKindOverride is { } cueKindOverride)
        {
            return cueKindOverride;
        }

        if (definition.Kind == FieldNavigationObjectKind.Materia)
        {
            return FieldObjectCueKind.Materia;
        }

        if (definition.Kind != FieldNavigationObjectKind.Item)
        {
            return FieldObjectCueKind.None;
        }

        var resource = definition.SourceModelResource ?? string.Empty;
        return resource.Contains("fieldbg_trb_", StringComparison.OrdinalIgnoreCase) ||
               resource.Contains("fieldbg_trbox", StringComparison.OrdinalIgnoreCase)
            ? FieldObjectCueKind.Chest
            : FieldObjectCueKind.Item;
    }
}

public sealed class FieldNavigationObjectReader
{
    public const int ModelPositionFixedPointScale = 4096;
    public const int DefaultInteractionRadius = 48;
    public const int AddressFieldModelIdArray = 0x00CBFB70;
    public const int AddressFieldEventDataPtr = 0x00CC0B60;
    public const int AddressFieldBankBase = 0x00DC08DC;
    public const int AddressTemporaryFieldBankBase = 0x00CC14D0;
    public const int FieldEventDataStride = 0x88;
    public const int PositionXOffset = 0x0C;
    public const int PositionYOffset = 0x10;
    public const int PositionZOffset = 0x14;
    public const int VisibilityOffset = 0x62;

    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets = Array.Empty<FieldNavigationTarget>();

    private readonly Func<int, int> readInt32;
    private readonly Func<int, byte> readByte;
    private readonly Func<int, string?> resolveItemName;
    private readonly Func<int, string?> resolveMateriaName;
    private readonly Func<int, bool> isLineEnabled;
    private readonly Func<FieldNavigationObjectDefinition, byte> resolveCollectedMask;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<FieldNavigationObjectDefinition>> definitionsByField;

    public FieldNavigationObjectReader(
        Func<int, int> readInt32,
        Func<int, byte> readByte,
        Func<int, string?> resolveItemName,
        Func<int, string?> resolveMateriaName,
        IEnumerable<FieldNavigationObjectDefinition> definitions,
        Func<int, bool>? isLineEnabled = null,
        Func<FieldNavigationObjectDefinition, byte>? resolveCollectedMask = null)
    {
        this.readInt32 = readInt32;
        this.readByte = readByte;
        this.resolveItemName = resolveItemName;
        this.resolveMateriaName = resolveMateriaName;
        this.isLineEnabled = isLineEnabled ?? (_ => false);
        this.resolveCollectedMask = resolveCollectedMask ?? (definition => definition.CollectedMask);
        definitionsByField = definitions
            .GroupBy(definition => definition.FieldId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FieldNavigationObjectDefinition>)group.ToArray());
    }

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position) ||
            !definitionsByField.TryGetValue(position.FieldId, out var definitions))
        {
            return EmptyTargets;
        }

        var targets = new List<FieldNavigationTarget>(definitions.Count);
        var eventTable = 0;
        byte modelCount = 0;
        var modelStateRead = false;
        foreach (var definition in definitions)
        {
            if (!MeetsRequiredState(definition) || IsCollected(definition))
            {
                continue;
            }

            int x;
            int y;
            int z;
            if (definition.TargetKind == FieldNavigationObjectTargetKind.Line)
            {
                if (!isLineEnabled(definition.EntityId))
                {
                    continue;
                }

                x = definition.StaticX;
                y = definition.StaticY;
                z = definition.StaticZ;
            }
            else if (definition.TargetKind == FieldNavigationObjectTargetKind.Location)
            {
                x = definition.StaticX;
                y = definition.StaticY;
                z = definition.StaticZ;
            }
            else
            {
                if (!modelStateRead)
                {
                    eventTable = readInt32(AddressFieldEventDataPtr);
                    modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
                    modelStateRead = true;
                }

                if (eventTable == 0 || modelCount == 0)
                {
                    continue;
                }

                var modelId = readByte(AddressFieldModelIdArray + definition.EntityId);
                if (modelId == 0xFF || modelId >= modelCount)
                {
                    continue;
                }

                var eventAddress = eventTable + modelId * FieldEventDataStride;
                if (readByte(eventAddress + VisibilityOffset) == 0)
                {
                    continue;
                }

                x = FromModelFixedPoint(readInt32(eventAddress + PositionXOffset));
                y = FromModelFixedPoint(readInt32(eventAddress + PositionYOffset));
                z = FromModelFixedPoint(readInt32(eventAddress + PositionZOffset));
            }

            var label = ResolveLabel(definition);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            targets.Add(new FieldNavigationTarget(
                definition.FieldId,
                FieldNavigationCategory.Objects,
                label,
                x,
                y,
                z,
                CreateStableId(definition),
                FieldNavigationObjectCueClassifier.Classify(definition),
                TriggerEntityId:
                    definition.TargetKind == FieldNavigationObjectTargetKind.Model
                        ? definition.EntityId
                        : -1,
                CompletesOnArrival: definition.Kind == FieldNavigationObjectKind.SavePoint,
                InteractionRadius: DefaultInteractionRadius));
        }

        return targets.Count == 0 ? EmptyTargets : targets;
    }

    private bool IsCollected(FieldNavigationObjectDefinition definition)
    {
        var collectedMask = resolveCollectedMask(definition);
        if (definition.CollectedBank < 0 ||
            definition.CollectedAddress < 0 ||
            collectedMask == 0)
        {
            return false;
        }

        if (!TryResolveByteBankAddress(
                definition.CollectedBank,
                definition.CollectedAddress,
                out var address))
        {
            return true;
        }

        var value = readByte(address);
        return (value & collectedMask) == collectedMask;
    }

    private bool MeetsRequiredState(FieldNavigationObjectDefinition definition)
    {
        var gameMoment = ReadUInt16(AddressFieldBankBase);
        if (definition.MinimumGameMoment >= 0 && gameMoment < definition.MinimumGameMoment)
        {
            return false;
        }

        if (definition.MaximumGameMoment >= 0 && gameMoment > definition.MaximumGameMoment)
        {
            return false;
        }

        if (definition.RequiredBank < 0 ||
            definition.RequiredAddress < 0 ||
            definition.RequiredMask == 0)
        {
            return true;
        }

        if (!TryResolveByteBankAddress(
                definition.RequiredBank,
                definition.RequiredAddress,
                out var address))
        {
            return false;
        }

        return (readByte(address) & definition.RequiredMask) == definition.RequiredValue;
    }

    private static bool TryResolveByteBankAddress(int bank, int index, out int address)
    {
        address = bank switch
        {
            1 => AddressFieldBankBase + index,
            3 => AddressFieldBankBase + 0x100 + index,
            5 => AddressTemporaryFieldBankBase + index,
            11 => AddressFieldBankBase + 0x200 + index,
            13 => AddressFieldBankBase + 0x300 + index,
            15 => AddressFieldBankBase + 0x400 + index,
            _ => 0
        };
        return address != 0;
    }

    private string? ResolveLabel(FieldNavigationObjectDefinition definition)
    {
        if (definition.Kind is FieldNavigationObjectKind.Named or FieldNavigationObjectKind.SavePoint)
        {
            return string.IsNullOrWhiteSpace(definition.Label) && definition.Kind == FieldNavigationObjectKind.SavePoint
                ? "Save Point"
                : definition.Label;
        }

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

    private static string CreateStableId(FieldNavigationObjectDefinition definition) =>
        $"object:{definition.FieldId}:{definition.EntityId}:{definition.Kind}:{definition.NativeId}:" +
        $"{definition.TargetKind}:{definition.StaticX}:{definition.StaticY}:{definition.StaticZ}:" +
        $"{definition.RequiredBank}:{definition.RequiredAddress}:{definition.RequiredMask}:{definition.RequiredValue}";

    private ushort ReadUInt16(int address) =>
        (ushort)(readByte(address) | (readByte(address + 1) << 8));

    private static int FromModelFixedPoint(int value) => value / ModelPositionFixedPointScale;
}
