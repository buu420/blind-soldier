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
    int MaximumGameMoment = -1,
    bool UsesTalkInteraction = false,
    string? ManualNavigationGuidance = null,
    bool UsesPlayerCollisionRadius = false,
    int? InteractionRadiusOverride = null,
    int[]? RequiredPlayerTriangles = null,
    int[]? ExcludedPlayerTriangles = null,
    // Offered only while the player stands on one side of this line: the sign of its cross
    // product with the player's position (1 or -1). A spot that works only once a line has been
    // crossed is offered as a point beyond it from each side, so the walk there crosses it.
    FieldNavigationTriggerLine? PlayerSideLine = null,
    int PlayerSide = 0,
    // The line a Location spot has to be walked across to work; given to the target so its
    // approach survives the player's side changing (see FieldNavigationTarget).
    FieldNavigationTriggerLine? CrossingLine = null);

public static class FieldNavigationObjectCatalog
{
    private const string ResourceName = "Ff7.Accessibility.Reloaded.Assets.navigation.field_objects.json";
    private static readonly Lazy<FieldNavigationObjectCatalogDocument> FullCatalog = new(Load);
    private static readonly Lazy<IReadOnlyList<FieldNavigationObjectDefinition>> AllDefinitions = new(
        () =>
        [
            .. FullCatalog.Value.Definitions,
            .. ShinraElevatorObjectCatalog.Create(),
            .. NibelheimObjectCatalog.Create(),
            .. TownInteractionObjectCatalog.Create(),
            .. MateriaCaveObjectCatalog.Create()
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
    private readonly Func<int, FieldNavigationTriggerLine?>? readLiveLine;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<FieldNavigationObjectDefinition>> definitionsByField;

    public FieldNavigationObjectReader(
        Func<int, int> readInt32,
        Func<int, byte> readByte,
        Func<int, string?> resolveItemName,
        Func<int, string?> resolveMateriaName,
        IEnumerable<FieldNavigationObjectDefinition> definitions,
        Func<int, bool>? isLineEnabled = null,
        Func<FieldNavigationObjectDefinition, byte>? resolveCollectedMask = null,
        Func<int, FieldNavigationTriggerLine?>? readLiveLine = null)
    {
        // A host that can read each LINE's live segment (FieldScriptLineStateReader.TryReadSegment)
        // gives a Line object its segment and the leader's collision radius, and the controller
        // then counts it reached only on the engine's own touch test. Without it a Line object is
        // the point at its static midpoint, as before.
        this.readLiveLine = readLiveLine;
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
            if (!MeetsRequiredState(definition) ||
                !MeetsPlayerTriangleConditions(definition, position.TriangleId) ||
                !MeetsPlayerSide(definition, position) ||
                IsCollected(definition))
            {
                continue;
            }

            int x;
            int y;
            int z;
            FieldNavigationTriggerLine? liveLine = null;
            var lineActivationRadius = 0;
            var interactionRadius = definition.InteractionRadiusOverride is > 0
                ? definition.InteractionRadiusOverride.Value
                : DefaultInteractionRadius;
            if (definition.TargetKind == FieldNavigationObjectTargetKind.Line)
            {
                if (!isLineEnabled(definition.EntityId))
                {
                    continue;
                }

                if (readLiveLine is not null)
                {
                    if (readLiveLine(definition.EntityId) is not { } segment ||
                        TryReadPlayerCollisionRadius(position, ref eventTable, ref modelCount, ref modelStateRead) is not { } collision)
                    {
                        continue;
                    }

                    liveLine = segment;
                    lineActivationRadius = collision;
                    interactionRadius = 0;
                }
                else if (definition.UsesPlayerCollisionRadius)
                {
                    if (!modelStateRead)
                    {
                        eventTable = readInt32(AddressFieldEventDataPtr);
                        modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
                        modelStateRead = true;
                    }

                    if (eventTable == 0 || position.ModelIndex < 0 || position.ModelIndex >= modelCount)
                    {
                        continue;
                    }

                    // The reviewed LINE's OK handler requires strict native
                    // distance < player event+0x72 (FUN_00637ABB/00637D35).
                    // Keep the midpoint approach inside that range; missing
                    // player geometry must not revive the old 48-unit guess.
                    var playerAddress = eventTable + position.ModelIndex * FieldEventDataStride;
                    var radius = unchecked((short)ReadUInt16(
                        playerAddress + FieldNavigationNpcReader.CollisionRadiusOffset));
                    if (radius <= 1)
                    {
                        continue;
                    }

                    interactionRadius = radius - 1;
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

                if (definition.UsesTalkInteraction)
                {
                    if (position.ModelIndex < 0 || position.ModelIndex >= modelCount ||
                        readByte(eventAddress + FieldNavigationNpcReader.TalkDisabledOffset) != 0)
                    {
                        continue;
                    }

                    var playerAddress = eventTable + position.ModelIndex * FieldEventDataStride;
                    interactionRadius = ReadUInt16(playerAddress + FieldNavigationNpcReader.CollisionRadiusOffset) +
                        ReadUInt16(eventAddress + FieldNavigationNpcReader.TalkRadiusOffset);
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
                InteractionRadius: interactionRadius,
                ManualNavigationGuidance: definition.ManualNavigationGuidance,
                TriggerLine: liveLine,
                ApproachCrossingLine: CrossingLineOf(definition),
                LineActivationRadius: lineActivationRadius));
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

    /// <summary>
    /// A control with two faces is two objects, and each is only the one in front of the
    /// party. uttmpin1's hanging scroll is the case: from the hall only its face is there to
    /// see, and from the room behind it only its back - offering the far side would name a
    /// room nobody has been shown, and it could not be reached through the scroll's lock.
    /// </summary>
    // The leader's collision radius (event +0x72), checked against the model table; null when
    // the event table, the leader's model or the radius is missing.
    private int? TryReadPlayerCollisionRadius(FieldPositionSnapshot position, ref int eventTable, ref byte modelCount, ref bool modelStateRead)
    {
        if (!modelStateRead)
        {
            eventTable = readInt32(AddressFieldEventDataPtr);
            modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
            modelStateRead = true;
        }

        if (eventTable == 0 || position.ModelIndex < 0 || position.ModelIndex >= modelCount)
        {
            return null;
        }

        var radius = unchecked((short)ReadUInt16(eventTable + position.ModelIndex * FieldEventDataStride + FieldNavigationNpcReader.CollisionRadiusOffset));
        return radius > 1 ? radius : null;
    }

    // Only a Location whose one live condition is where the player stands: nothing collected,
    // required or line-enabled can take it away, so its going is only the side or triangle.
    private static FieldNavigationTriggerLine? CrossingLineOf(FieldNavigationObjectDefinition definition) =>
        definition.CrossingLine is { } line &&
        definition.TargetKind == FieldNavigationObjectTargetKind.Location &&
        definition.CollectedBank < 0 && definition.RequiredBank < 0 &&
        definition.MinimumGameMoment < 0 && definition.MaximumGameMoment < 0
            ? line
            : null;

    /// <summary>Which side of <paramref name="line"/> (x, y) is on: 1, -1, or 0 on it.</summary>
    public static int SideOf(FieldNavigationTriggerLine line, int x, int y)
    {
        var cross = (long)(line.EndX - line.StartX) * (y - line.StartY) - (long)(line.EndY - line.StartY) * (x - line.StartX);
        return Math.Sign(cross);
    }

    private static bool MeetsPlayerSide(FieldNavigationObjectDefinition definition, FieldPositionSnapshot position) =>
        definition.PlayerSideLine is not { } line ||
        (definition.PlayerSide != 0 && SideOf(line, position.X, position.Y) == definition.PlayerSide);

    private static bool MeetsPlayerTriangleConditions(
        FieldNavigationObjectDefinition definition,
        ushort playerTriangle) =>
        (definition.RequiredPlayerTriangles is not { Length: > 0 } required ||
         required.Contains(playerTriangle)) &&
        (definition.ExcludedPlayerTriangles is not { Length: > 0 } excluded ||
         !excluded.Contains(playerTriangle));

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
