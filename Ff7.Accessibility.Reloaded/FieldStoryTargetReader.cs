using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ff7.Accessibility.Reloaded;

public enum FieldStoryTargetKind
{
    Model,
    Location
}

public readonly record struct FieldStoryStateCondition(
    int Bank,
    int Address,
    byte Mask,
    byte Value,
    bool AnyBitSet = false);

public readonly record struct FieldStoryEventDefinition(
    int FieldId,
    FieldStoryTargetKind Kind,
    string Label,
    int EntityId = -1,
    int X = 0,
    int Y = 0,
    int Z = 0,
    int TargetGameMoment = -1,
    int MinimumGameMoment = -1,
    int MaximumGameMoment = -1,
    int Priority = 100,
    FieldStoryStateCondition RequiredCondition = default,
    FieldStoryStateCondition CompletedCondition = default,
    string? SourceFieldName = null,
    string? SourceEntityName = null,
    string? SourceScriptType = null,
    FieldNavigationTriggerLine? TriggerLine = null,
    bool CompletesOnArrival = true,
    FieldStoryStateCondition[]? RequiredConditions = null,
    FieldNavigationRouteDetour? RouteDetour = null,
    FieldNavigationRouteDetour[]? RouteDetours = null,
    int[]? RequiredPlayerTriangles = null,
    int[]? ExcludedPlayerTriangles = null);

public static class FieldStoryEventCatalog
{
    private const string ResourceName = "Ff7.Accessibility.Reloaded.Assets.navigation.field_story_events.json";
    private static readonly Lazy<FieldStoryEventCatalogDocument> FullCatalog = new(Load);

    public static IReadOnlyList<FieldStoryEventDefinition> CreateAllFields() =>
        FullCatalog.Value.Definitions;

    public static IReadOnlyList<FieldStoryEventDefinition> CreateOpeningReactor() =>
        FullCatalog.Value.Definitions
            .Where(definition => definition.FieldId is >= 116 and <= 132)
            .ToArray();

    public static string SourceCommit => FullCatalog.Value.SourceCommit;

    private static FieldStoryEventCatalogDocument Load()
    {
        using var stream = typeof(FieldStoryEventCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded field story event catalog {ResourceName}.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var document = JsonSerializer.Deserialize<FieldStoryEventCatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Embedded field story event catalog is empty.");
        if (document.SchemaVersion != 1 || document.Definitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unsupported field story event catalog schema {document.SchemaVersion}.");
        }

        return document;
    }

    private sealed class FieldStoryEventCatalogDocument
    {
        public int SchemaVersion { get; set; }

        public string SourceCommit { get; set; } = string.Empty;

        public List<FieldStoryEventDefinition> Definitions { get; set; } = [];
    }
}

public sealed class FieldStoryTargetReader
{
    // Ghidra: field interaction reach is the player event +0x72 collision
    // radius plus the target event +0x74 talk radius.
    private const int ModelCollisionRadiusOffset = 0x72;
    private const int ModelTalkRadiusOffset = 0x74;
    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets = Array.Empty<FieldNavigationTarget>();

    private readonly Func<int, int> readInt32;
    private readonly Func<int, short> readInt16;
    private readonly Func<int, byte> readByte;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<FieldStoryEventDefinition>> definitionsByField;

    public FieldStoryTargetReader(
        Func<int, int> readInt32,
        Func<int, byte> readByte,
        IEnumerable<FieldStoryEventDefinition> definitions)
        : this(readInt32, _ => 0, readByte, definitions)
    {
    }

    public FieldStoryTargetReader(
        Func<int, int> readInt32,
        Func<int, short> readInt16,
        Func<int, byte> readByte,
        IEnumerable<FieldStoryEventDefinition> definitions)
    {
        this.readInt32 = readInt32;
        this.readInt16 = readInt16;
        this.readByte = readByte;
        definitionsByField = definitions
            .GroupBy(definition => definition.FieldId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FieldStoryEventDefinition>)group.ToArray());
    }

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position) ||
            !definitionsByField.TryGetValue(position.FieldId, out var definitions))
        {
            return EmptyTargets;
        }

        var gameMoment = ReadGameMoment();
        var candidates = new List<(FieldStoryEventDefinition Definition, FieldNavigationTarget Target)>();
        foreach (var definition in definitions)
        {
            if (!IsInGameMomentRange(definition, gameMoment) ||
                !MeetsCondition(definition.RequiredCondition) ||
                !MeetsConditions(definition.RequiredConditions) ||
                !MeetsPlayerTriangleConditions(definition, position.TriangleId) ||
                MeetsCompletedCondition(definition.CompletedCondition))
            {
                continue;
            }

            var target = ResolveTarget(definition, position);
            if (target is not null)
            {
                candidates.Add((definition, target.Value));
            }
        }

        if (candidates.Count == 0)
        {
            return EmptyTargets;
        }

        var priority = candidates.Min(candidate => candidate.Definition.Priority);
        var current = candidates
            .Where(candidate => candidate.Definition.Priority == priority)
            .ToArray();
        var nextMilestone = current
            .Where(candidate => candidate.Definition.TargetGameMoment >= 0)
            .Select(candidate => candidate.Definition.TargetGameMoment)
            .DefaultIfEmpty(-1)
            .Min();
        if (nextMilestone >= 0)
        {
            current = current
                .Where(candidate => candidate.Definition.TargetGameMoment == nextMilestone)
                .ToArray();
        }

        return current.Select(candidate => candidate.Target).ToArray();
    }

    private FieldNavigationTarget? ResolveTarget(
        FieldStoryEventDefinition definition,
        FieldPositionSnapshot position)
    {
        if (definition.Kind == FieldStoryTargetKind.Location)
        {
            return CreateTarget(definition, definition.X, definition.Y, definition.Z);
        }

        if (definition.EntityId < 0)
        {
            return null;
        }

        var eventTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
        if (eventTable == 0 || modelCount == 0)
        {
            return null;
        }

        var modelId = readByte(FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId);
        if (modelId == 0xFF || modelId >= modelCount)
        {
            return null;
        }

        var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
        if (readByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset) == 0)
        {
            return null;
        }

        var playerEventAddress =
            eventTable + position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
        var playerCollisionRadius = Math.Max(
            0,
            (int)readInt16(playerEventAddress + ModelCollisionRadiusOffset));
        var interactionRadius = playerCollisionRadius + Math.Max(
            0,
            (int)readInt16(eventAddress + ModelTalkRadiusOffset));
        return CreateTarget(
            definition,
            FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionXOffset)),
            FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionYOffset)),
            FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionZOffset)),
            interactionRadius);
    }

    private static FieldNavigationTarget CreateTarget(
        FieldStoryEventDefinition definition,
        int x,
        int y,
        int z,
        int interactionRadius = 0) =>
        new(
            definition.FieldId,
            FieldNavigationCategory.Story,
            definition.Label,
            x,
            y,
            z,
            $"story:{definition.FieldId}:{definition.EntityId}:{definition.TargetGameMoment}:{definition.Label}",
            TriggerEntityId:
                definition.Kind == FieldStoryTargetKind.Model
                    ? definition.EntityId
                    : -1,
            CompletesOnArrival:
                definition.Kind == FieldStoryTargetKind.Location &&
                definition.CompletesOnArrival,
            InteractionRadius: interactionRadius,
            TriggerLine: definition.TriggerLine,
            RouteDetour: definition.RouteDetour,
            RouteDetours: definition.RouteDetours);

    private int ReadGameMoment() =>
        readByte(FieldNavigationObjectReader.AddressFieldBankBase) |
        (readByte(FieldNavigationObjectReader.AddressFieldBankBase + 1) << 8);

    private static bool IsInGameMomentRange(FieldStoryEventDefinition definition, int gameMoment)
    {
        if (definition.MinimumGameMoment >= 0 && gameMoment < definition.MinimumGameMoment)
        {
            return false;
        }

        if (definition.MaximumGameMoment >= 0 && gameMoment > definition.MaximumGameMoment)
        {
            return false;
        }

        return definition.TargetGameMoment < 0 || gameMoment < definition.TargetGameMoment;
    }

    private bool MeetsCompletedCondition(FieldStoryStateCondition condition) =>
        condition.Mask != 0 && MeetsCondition(condition);

    private bool MeetsConditions(IReadOnlyList<FieldStoryStateCondition>? conditions) =>
        conditions is null || conditions.All(MeetsCondition);

    private static bool MeetsPlayerTriangleConditions(
        FieldStoryEventDefinition definition,
        ushort playerTriangle)
    {
        if (definition.RequiredPlayerTriangles is { Length: > 0 } required &&
            !required.Contains(playerTriangle))
        {
            return false;
        }

        return definition.ExcludedPlayerTriangles is not { Length: > 0 } excluded ||
               !excluded.Contains(playerTriangle);
    }

    private bool MeetsCondition(FieldStoryStateCondition condition)
    {
        if (condition.Mask == 0)
        {
            return true;
        }

        if (!TryResolveByteBankAddress(condition.Bank, condition.Address, out var address))
        {
            return false;
        }

        var maskedValue = readByte(address) & condition.Mask;
        return condition.AnyBitSet
            ? maskedValue != 0
            : maskedValue == condition.Value;
    }

    private static bool TryResolveByteBankAddress(int bank, int index, out int address)
    {
        address = bank switch
        {
            1 => FieldNavigationObjectReader.AddressFieldBankBase + index,
            3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + index,
            5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + index,
            11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200 + index,
            13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + index,
            15 => FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + index,
            _ => 0
        };
        return address != 0;
    }

    private static int FromModelFixedPoint(int value) =>
        value / FieldNavigationObjectReader.ModelPositionFixedPointScale;
}
