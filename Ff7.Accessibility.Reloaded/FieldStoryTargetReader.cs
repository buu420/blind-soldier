using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ff7.Accessibility.Reloaded;

public enum FieldStoryTargetKind
{
    Model,
    Location
}

/// <summary>
/// A test a row has to satisfy before it is offered.
///
/// <para><paramref name="PartyMemberId"/> is the field scripts' own <c>IFPRTY</c>:
/// whether a named character is in the party right now, read from the three slots the
/// engine keeps at Bank[3][9], [10] and [11]. It cannot be expressed as a mask over one
/// byte, because the character can be in any of the three, and it is not the same
/// question as whether their model is on screen - the Whirlwind Maze keeps Barret and
/// Red standing there whether or not they are in the party, and only the ones who are
/// not will take the Black Materia. When it is set, the bank fields are unused.</para>
/// </summary>
public readonly record struct FieldStoryStateCondition(
    int Bank,
    int Address,
    byte Mask,
    byte Value,
    bool AnyBitSet = false,
    int? MinimumSetBits = null,
    int? MaximumSetBits = null,
    /// <summary>
    /// The masked byte read as a number, compared the way the script compares it. This
    /// is not the same test as counting set bits: 208:11's Talk waits for 5[2] to reach
    /// three, and 5[2] goes on counting to five as the remaining men are spoken to.
    /// </summary>
    int? MinimumValue = null,
    int? MaximumValue = null,
    int? PartyMemberId = null,
    bool RequirePartyMember = false);

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
    int[]? ExcludedPlayerTriangles = null,
    int[]? CompletionPlayerTriangles = null,
    int? RequiredEnabledLineEntityId = null,
    bool UsesPlayerCollisionRadius = false,
    bool UsesContactRange = false,
    string? ManualNavigationGuidance = null,
    bool UsesHiddenTalkTarget = false);

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
    private readonly Func<int, bool>? isLineEnabled;
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
        IEnumerable<FieldStoryEventDefinition> definitions,
        Func<int, bool>? isLineEnabled = null)
    {
        this.readInt32 = readInt32;
        this.readInt16 = readInt16;
        this.readByte = readByte;
        this.isLineEnabled = isLineEnabled;
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
                (definition.RequiredEnabledLineEntityId is { } requiredLine &&
                 isLineEnabled?.Invoke(requiredLine) != true) ||
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
            // Narrowing to the nearest milestone stops two different chapters of the story
            // being offered at once. It is not meant to hide a step on the way to that
            // milestone, and a row that declares no milestone is exactly that: it completes
            // nothing, so it cannot be the wrong one to offer alongside the moment it leads
            // to. Reactor 5's upper piping is the case that made this visible - the doors at
            // either end carry moments 123 and 128, and with those present the three jumps
            // that cross the pipe were filtered out and the room went silent in the middle.
            current = current
                .Where(candidate =>
                    candidate.Definition.TargetGameMoment < 0 ||
                    candidate.Definition.TargetGameMoment == nextMilestone)
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
            if (definition.UsesPlayerCollisionRadius)
            {
                // Native Go handlers require distance to the LINE strictly less
                // than the player's event+0x72 radius (FUN_00637ABB). A configured
                // navigation threshold can stop outside the activation region.
                //
                // A row that says this must be a line and must have a readable radius:
                // without either there is no reach to measure, and offering an approach
                // whose activation region is unknown would send the player to a spot the
                // game may not accept. The row that carries the same crossing without
                // this switch is the one to use when the reach is not the point.
                var playerTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
                var count = readByte(FieldPositionReader.AddressFieldNumModels);
                if (definition.TriggerLine is null || playerTable == 0 || position.ModelIndex >= count)
                    return null;
                var playerAddress = playerTable + position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
                var radius = (int)readInt16(playerAddress + ModelCollisionRadiusOffset);
                if (radius <= 1)
                    return null;
                return CreateTarget(definition, definition.X, definition.Y, definition.Z, radius - 1);
            }
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

        // A character who has not joined the party yet is an ordinary field NPC, and
        // several required conversations belong to one - Cid standing in his own house
        // before he joins, for instance. The same entity becomes the party leader
        // later, and the field engine then maps it to the model the player is moving.
        // Offering a Talk with the model under the player's own control would send
        // them to walk into themselves.
        if (modelId == position.ModelIndex)
        {
            return null;
        }

        var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
        if (definition.UsesHiddenTalkTarget)
        {
            // One step is a Talk with an entity that is not drawn. Wutai's shaking pot is
            // uutai1/YUFI placed by XYZI with TLKON on, TALKR 180 and no VISI, and her Talk
            // is the only caller of the capture - whose own VISI 1 is what first shows her.
            // The script cannot work unless the game accepts that Talk while she is hidden,
            // so this row asks only what TLKON says: whether she can be talked to now.
            if (readByte(eventAddress + FieldNavigationNpcReader.TalkDisabledOffset) != 0)
            {
                return null;
            }
        }
        else if (readByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset) == 0)
        {
            return null;
        }

        var playerEventAddress =
            eventTable + position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
        var playerCollisionRadius = Math.Max(
            0,
            (int)readInt16(playerEventAddress + ModelCollisionRadiusOffset));

        // Contact is not Talk at a different distance; it is a different test.
        //
        // Talk (00636284) takes the model the party faces within ninety degrees whose centre
        // is nearer than the player's collision radius plus the model's own talk radius at
        // +0x74. Contact is the movement probes instead: 00636C41 tests each step the
        // player's collision width ahead, along the heading and forty-five degrees either
        // side, and FUN_00637724 touches a model - never one whose collision is off - when a
        // probe lands inside half the sum of the two widths at +0x72 and the height
        // difference is strictly inside (-127, 128). FieldNavigationNpcReader.ContactReach is
        // where that puts the party's centre. Those four stands are storage slots that
        // 566:14:4 fills in the order the materia were brought in, so none of them is a
        // fixed colour or a fixed mission.
        if (definition.UsesContactRange &&
            readByte(eventAddress + FieldNavigationNpcReader.CollisionDisabledOffset) != 0)
        {
            return null;
        }

        var interactionRadius = definition.UsesContactRange
            ? FieldNavigationNpcReader.ContactReach(
                (ushort)readInt16(playerEventAddress + ModelCollisionRadiusOffset),
                (ushort)readInt16(eventAddress + ModelCollisionRadiusOffset))
            : playerCollisionRadius + Math.Max(
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
            RouteDetours: definition.RouteDetours,
            CompletionTriangles: definition.CompletionPlayerTriangles,
            ManualNavigationGuidance: definition.ManualNavigationGuidance,
            Activation:
                definition.UsesContactRange
                    ? FieldNavigationActivation.Contact
                    : definition.Kind == FieldStoryTargetKind.Model
                        ? FieldNavigationActivation.Talk
                        : FieldNavigationActivation.Default);

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

    /// <summary>The three party slots the engine keeps, in Bank[3].</summary>
    private static readonly int[] PartySlotAddresses = [9, 10, 11];

    /// <summary>No character occupies this slot.</summary>
    private const byte EmptyPartySlot = 0xFF;

    private bool MeetsCondition(FieldStoryStateCondition condition)
    {
        if (condition.PartyMemberId is { } partyMemberId)
        {
            var present = false;
            foreach (var slot in PartySlotAddresses)
            {
                if (!TryResolveByteBankAddress(3, slot, out var slotAddress))
                {
                    return false;
                }

                var occupant = readByte(slotAddress);
                if (occupant != EmptyPartySlot && occupant == partyMemberId)
                {
                    present = true;
                    break;
                }
            }

            return present == condition.RequirePartyMember;
        }

        if (condition.Mask == 0)
        {
            // An omitted condition is a wildcard; an explicitly counted
            // prerequisite with no bits selected is not an omitted condition.
            return condition.MinimumSetBits is null && condition.MaximumSetBits is null &&
                condition.MinimumValue is null && condition.MaximumValue is null;
        }

        if (!TryResolveByteBankAddress(condition.Bank, condition.Address, out var address))
        {
            return false;
        }

        var maskedValue = readByte(address) & condition.Mask;
        if (condition.MinimumValue is not null || condition.MaximumValue is not null)
        {
            return maskedValue >= (condition.MinimumValue ?? int.MinValue) &&
                maskedValue <= (condition.MaximumValue ?? int.MaxValue);
        }

        if (condition.MinimumSetBits is not null || condition.MaximumSetBits is not null)
        {
            // shpin_2/EARITH2 Talk counts bank 3[184] bits 0..6 before
            // testing >= 3. Count the saved flags, not its temporary tally.
            var minimum = condition.MinimumSetBits ?? 0;
            var maximum = condition.MaximumSetBits ?? 8;
            var count = System.Numerics.BitOperations.PopCount((uint)maskedValue);
            return minimum >= 0 && maximum <= 8 && minimum <= maximum &&
                count >= minimum && count <= maximum;
        }
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
