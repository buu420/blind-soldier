namespace Ff7.Accessibility.Reloaded;

/// <summary>The optional Bone Village key errand, gated by the native payout.</summary>
public sealed class KeyToSector5StoryTargetReader
{
    private readonly Func<int, int> readInt32;
    private readonly Func<int, byte> readByte;
    private readonly KeyToSector5StoryKnowledge knowledge;
    private readonly Func<int, bool> isReviewedLayout;
    private readonly FieldStoryTargetReader village;
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> SiteTriangles =
        BoneVillageDigSpot.Sites.Where(site => site.DigResult is >= 5 and <= 7).ToDictionary(
            site => site.EntityId, site => (IReadOnlyList<int>)Array.AsReadOnly(new[] { site.Triangle }));

    public KeyToSector5StoryTargetReader(Func<int, int> readInt32, Func<int, short> readInt16,
        Func<int, byte> readByte, KeyToSector5StoryKnowledge knowledge, Func<int, bool> isReviewedLayout,
        Func<FieldStoryEventDefinition, FieldStoryEventDefinition?>? remapDefinition = null)
    {
        this.readInt32 = readInt32;
        this.readByte = readByte;
        this.knowledge = knowledge;
        this.isReviewedLayout = isReviewedLayout;
        var definitions = new List<FieldStoryEventDefinition>
        {
            VillageStep(4, "Search for the Key to Sector 5: excavation foreman", 0)
        };
        definitions.AddRange(Enumerable.Range(1, 9).Select(result =>
            VillageStep(13, "Check the excavation results", result)));
        village = new FieldStoryTargetReader(readInt32, readInt16, readByte, definitions,
            remapDefinition: remapDefinition);
    }

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position) =>
        ReadTargets(position, FieldNavigationCategory.Story);

    internal FieldNavigationTarget? ReadObjectDigSpot(FieldPositionSnapshot position) =>
        ReadTargets(position, FieldNavigationCategory.Objects) is [var spot] ? spot : null;

    private IReadOnlyList<FieldNavigationTarget> ReadTargets(
        FieldPositionSnapshot position, FieldNavigationCategory category)
    {
        if (!FieldPositionReader.IsUsable(position) || position.FieldId is not (617 or BoneVillageDigSpot.FieldId) ||
            (category == FieldNavigationCategory.Objects && position.FieldId != BoneVillageDigSpot.FieldId))
            return [];
        var revision = knowledge.History.PlaythroughRevision;
        var before = ReadState();
        if (before.Module != FieldPositionReader.FieldModule || before.Field != position.FieldId ||
            !BoneVillageDigSpot.IsKeyAvailable(before.Moment, before.Inventory, before.Reward) ||
            !isReviewedLayout(position.FieldId))
            return [];

        var targets = position.FieldId == 617
            ? before.Result <= 9 ? village.ReadTargets(position).Where(IsUsableTalkTarget).ToArray() : []
            : ReadDigSite(position, before, category);
        return ReadState() == before && knowledge.History.PlaythroughRevision == revision &&
            isReviewedLayout(position.FieldId) ? targets : [];
    }

    private static FieldStoryEventDefinition VillageStep(int entity, string label, int result) =>
        new(617, FieldStoryTargetKind.Model, label, EntityId: entity,
            MinimumGameMoment: BoneVillageDigSpot.KeyMinimumGameMoment, Priority: 0,
            RequiredCondition: new(1, 234, 0xff, (byte)result), SourceFieldName: "bonevil",
            SourceEntityName: entity == 4 ? "" : "box1", SourceScriptType: "Talk");

    private bool IsUsableTalkTarget(FieldNavigationTarget target)
    {
        if (target.TriggerEntityId < 0) return false;
        var table = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var model = readByte(FieldNavigationObjectReader.AddressFieldModelIdArray + target.TriggerEntityId);
        if (table == 0 || model == 0xff || model >= readByte(FieldPositionReader.AddressFieldNumModels)) return false;
        var address = table + model * FieldNavigationObjectReader.FieldEventDataStride;
        return readByte(address + FieldNavigationObjectReader.VisibilityOffset) != 0 &&
            readByte(address + FieldNavigationNpcReader.TalkDisabledOffset) == 0;
    }

    private IReadOnlyList<FieldNavigationTarget> ReadDigSite(
        FieldPositionSnapshot position, State state, FieldNavigationCategory category)
    {
        var objectTarget = category == FieldNavigationCategory.Objects;
        var treasureSearch = state.Request == BoneVillageDigSpot.NormalTreasureRequest ||
            (!objectTarget && state.Request == BoneVillageDigSpot.GoodTreasureRequest);
        if (state.Phase is < 1 or > 7 || state.Climbing != 0 || !treasureSearch) return [];

        // These markers are placed during Init, and the final Switch checks their triangles.
        // Prefer a site on the current level so the player can find it before the blast.
        foreach (var site in BoneVillageDigSpot.Sites.Where(site => site.DigResult is >= 5 and <= 7)
                     .OrderBy(site => Math.Abs(position.Z - site.Z)).ThenBy(site => site.EntityId))
        {
            if (!TryReadSite(site, out var x, out var y, out var z)) continue;
            return [new(BoneVillageDigSpot.FieldId, category,
                objectTarget ? "Dig spot: Key to Sector 5" : "Dig for the Key to Sector 5", x, y, z,
                objectTarget ? $"object:772:dig-spot:{site.EntityId}" : $"key-story:772:dig:{site.EntityId}",
                CompletesOnArrival: true,
                CompletionTriangles: SiteTriangles[site.EntityId])];
        }
        return [];
    }

    private bool TryReadSite(BoneVillageDigSpot.Site site, out int x, out int y, out int z)
    {
        x = y = z = 0;
        var slot = FieldNavigationObjectReader.AddressTemporaryFieldBankBase + site.BankSlot;
        if (Word(slot) != site.Triangle) return false;
        var table = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var count = readByte(FieldPositionReader.AddressFieldNumModels);
        var mapAddress = FieldNavigationObjectReader.AddressFieldModelIdArray + site.EntityId;
        var model = readByte(mapAddress);
        if (table == 0 || model == 0xff || model >= count) return false;
        var address = table + model * FieldNavigationObjectReader.FieldEventDataStride;
        if (Word(address + FieldPositionReader.ObjectTriangleOffset) != site.Triangle) return false;
        x = readInt32(address + FieldNavigationObjectReader.PositionXOffset) /
            FieldNavigationObjectReader.ModelPositionFixedPointScale;
        y = readInt32(address + FieldNavigationObjectReader.PositionYOffset) /
            FieldNavigationObjectReader.ModelPositionFixedPointScale;
        z = readInt32(address + FieldNavigationObjectReader.PositionZOffset) /
            FieldNavigationObjectReader.ModelPositionFixedPointScale;
        return Math.Abs(x - site.X) <= 2 && Math.Abs(y - site.Y) <= 2 &&
            Word(slot) == site.Triangle && Word(address + FieldPositionReader.ObjectTriangleOffset) == site.Triangle &&
            readByte(mapAddress) == model && readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr) == table;
    }

    private int Word(int address) => readByte(address) | (readByte(address + 1) << 8);

    private State ReadState()
    {
        var bank = FieldNavigationObjectReader.AddressFieldBankBase;
        var temporary = FieldNavigationObjectReader.AddressTemporaryFieldBankBase;
        return new(readByte(FieldPositionReader.AddressCurrentModule), Word(FieldPositionReader.AddressFieldId),
            Word(bank), readByte(bank + 67), readByte(bank + 0x400 + 38), readByte(bank + 234),
            readByte(temporary + BoneVillageDigSpot.PhaseIndex), readByte(bank + BoneVillageDigSpot.RequestIndex),
            readByte(temporary + 43),
            readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr), readByte(FieldPositionReader.AddressFieldNumModels));
    }

    private readonly record struct State(int Module, int Field, int Moment, byte Inventory, byte Reward,
        byte Result, byte Phase, byte Request, byte Climbing, int EventTable, byte ModelCount);
}
