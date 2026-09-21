using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The observatory's first visit, from the report: the party walks into Bugenhagen's house
/// with Red XIII standing in front of them and is told Story: none.
///
/// <para><c>01:18:53Z field=544 ... Field navigation story: field=544: none</c>, then
/// <c>01:18:56Z Speak: ... Story none</c> while <c>Field navigation NPCs: field=544:
/// Red XIII@(167,-96,-624)</c>. The player navigated to him as an NPC instead, and at
/// <c>01:19:03</c> the scene the catalog had no row for started by itself.</para>
///
/// <para>The gate is bugin2 entity 5 RED's own Init, and it is a complete one. It returns at
/// once unless <c>Bank[3][161]</c> bit 3 is set - Red XIII has to have been spoken to down in
/// the canyon first - and returns again once <c>Bank[3][170]</c> bit 0 is set. Only between
/// those two does it reach the <c>TLKON</c>, the <c>VISI</c> and the two <c>IDLCK</c>s at 38
/// and 42 that hold triangles 23 and 18 while he stands there. His Talk is three bytes:
/// <c>REQ AD 10 Script 3</c>, and it is that script's only caller. AD 3 plays the
/// introduction and sets <c>Bank[3][170]</c> bit 0 at byte 102, so the flag that hides him
/// again is the one the scene itself writes.</para>
///
/// <para>These cases walk the whole first visit through the shipped catalog: the scene, and
/// then each of the objectives it hands over to.</para>
/// </summary>
internal static class CosmoObservatoryStoryTests
{
    private const int Observatory = 541;
    private const int ResearchCentre = 544;

    /// <summary>Bank 3, address 161, bit 3: Red XIII has been spoken to in the canyon.</summary>
    private const int RedSpokenTo = 0x08;

    /// <summary>Bank 3, address 170, bit 0: the observatory scene has played.</summary>
    private const int ObservatorySeen = 0x01;

    /// <summary>Bank 3, address 170, bit 1: the party for the demonstration is confirmed.</summary>
    private const int PartyConfirmed = 0x02;

    /// <summary>Bank 3, address 170, bit 6: the demonstration itself is over.</summary>
    private const int DemonstrationDone = 0x40;

    public static void Run()
    {
        TheSceneIsTheOnlyObjectiveUntilItHasPlayed();
        RedXiiiHasToHaveBeenMetInTheCanyonFirst();
        TheSceneHandsOverToFindingTheOthers();
        AConfirmedPartyOpensTheUpperDoor();
        TheDemonstrationIsBugenhagensOwnObjective();
        AfterTheLectureTheWayOutIsTheObjective();
    }

    /// <summary>
    /// The reported frame itself. Standing where the log has the party, with the state the
    /// log has them in, the room has one objective and it is Red XIII.
    /// </summary>
    private static void TheSceneIsTheOnlyObjectiveUntilItHasPlayed()
    {
        var memory = FirstVisit();
        var targets = memory.StoryReader().ReadTargets(ResearchCentreEntry);

        Equal(1, targets.Count,
            "the room the report was standing in must offer exactly one objective, not none");
        Equal("Talk to Red XIII", targets[0].Label,
            "and it must be the only thing in the room that moves the story");
        Equal(5, targets[0].TriggerEntityId,
            "resolved from entity 5, whose Talk is the scene's only caller");
        Equal(167, targets[0].X, "at Red XIII's own live position, as the NPC list read it");
        Equal(-96, targets[0].Y, "at Red XIII's own live position, as the NPC list read it");
    }

    /// <summary>
    /// The first half of RED's Init. Without <c>Bank[3][161]</c> bit 3 the entity returns
    /// before it is ever shown, so there is nobody in the room to talk to and the catalog
    /// must not say there is.
    /// </summary>
    private static void RedXiiiHasToHaveBeenMetInTheCanyonFirst()
    {
        var memory = FirstVisit();
        memory.ClearBank3(161, RedSpokenTo);

        Equal(0, memory.StoryReader().ReadTargets(ResearchCentreEntry).Count,
            "a room whose only actor has not been shown offers nothing");
    }

    /// <summary>
    /// The second half of RED's Init, and the handover. AD 3 sets bit 0 and leaves the party
    /// as Cloud alone, so Red XIII is gone and the objective is the way back out.
    /// </summary>
    private static void TheSceneHandsOverToFindingTheOthers()
    {
        var memory = FirstVisit();
        memory.PlayTheObservatoryScene();

        var labels = memory.StoryReader().ReadTargets(ResearchCentreEntry)
            .Select(target => target.Label).ToArray();
        Equal(false, labels.Contains("Talk to Red XIII"),
            "the scene that hides Red XIII must take its own objective with it");
        Equal(true, labels.Contains("Go back out to find the others"),
            "and must hand over to the companions the player now has to collect");
    }

    /// <summary>
    /// Once the party is whole again and confirmed, the upper door is the way on - the one
    /// entity 18 DOOR holds shut and entity 19 LINEW opens.
    /// </summary>
    private static void AConfirmedPartyOpensTheUpperDoor()
    {
        var memory = FirstVisit();
        memory.PlayTheObservatoryScene();
        memory.FillTheParty();
        memory.SetBank3(170, PartyConfirmed);

        var labels = memory.StoryReader().ReadTargets(ResearchCentreEntry)
            .Select(target => target.Label).ToArray();
        Equal(true, labels.Contains("Go through to the room at the top"),
            "a confirmed party is sent up to the observatory");
        Equal(false, labels.Contains("Go back out to find the others"),
            "and is not still being sent out to look for itself");
    }

    /// <summary>
    /// Upstairs, with the party confirmed and the demonstration still to come, the objective
    /// is Bugenhagen himself - bugin1a entity 11, whose Talk sets bit 6 at byte 470.
    /// </summary>
    private static void TheDemonstrationIsBugenhagensOwnObjective()
    {
        var memory = FirstVisit();
        memory.PlayTheObservatoryScene();
        memory.FillTheParty();
        memory.SetBank3(170, PartyConfirmed);
        memory.SetField(Observatory);

        var targets = memory.StoryReader().ReadTargets(ObservatoryPosition);
        Equal(true, targets.Any(target => target.Label == "Talk to Bugenhagen in the observatory"),
            "the demonstration is asked for by talking to the man who runs it");

        memory.SetBank3(170, DemonstrationDone);
        Equal(false,
            memory.StoryReader().ReadTargets(ObservatoryPosition)
                .Any(target => target.Label == "Talk to Bugenhagen in the observatory"),
            "and stops being an objective once his own Talk has set bit 6");
    }

    /// <summary>
    /// After the lecture, bugin1c writes 493 and both rooms become a way out: down from the
    /// observatory, and then out of the house to the campfire.
    /// </summary>
    private static void AfterTheLectureTheWayOutIsTheObjective()
    {
        var memory = FirstVisit();
        memory.PlayTheObservatoryScene();
        memory.FillTheParty();
        memory.SetBank3(170, PartyConfirmed | DemonstrationDone);
        memory.SetGameMoment(493);

        memory.SetField(Observatory);
        Equal(true,
            memory.StoryReader().ReadTargets(ObservatoryPosition)
                .Any(target => target.Label == "Go back down from the observatory"),
            "the way down is what is left upstairs once the lecture is over");

        memory.SetField(ResearchCentre);
        Equal(true,
            memory.StoryReader().ReadTargets(ResearchCentreEntry)
                .Any(target => target.Label == "Leave Bugenhagen's house"),
            "and the way out of the house is what is left downstairs");
    }

    /// <summary>
    /// <c>01:18:53Z Field position: module=1, field=544, model=0/12, x=-349, y=-319, z=-635,
    /// triangle=1</c>.
    /// </summary>
    private static readonly FieldPositionSnapshot ResearchCentreEntry =
        new(FieldPositionReader.FieldModule, ResearchCentre, 0, -349, -319, -635, 1, 96);

    private static readonly FieldPositionSnapshot ObservatoryPosition =
        new(FieldPositionReader.FieldModule, Observatory, 0, -72, 103, -38, 38, 40);

    /// <summary>The state the report's log was in: arrived, Red XIII met, scene still to play.</summary>
    private static BugenhagenMemory FirstVisit()
    {
        var memory = new BugenhagenMemory(ResearchCentre);
        memory.SetGameMoment(469);
        memory.SetBank3(161, RedSpokenTo);
        memory.EmptyTheParty();
        return memory;
    }

    /// <summary>
    /// The savemap and field state these rows are read from, with only the bits the native
    /// scripts themselves write.
    /// </summary>
    private sealed class BugenhagenMemory
    {
        private const int FieldState = 0x02800000;
        private const int EventTable = 0x03300000;

        /// <summary>Entity 5 RED in bugin2, and the model its own CHAR gives it.</summary>
        private const int RedEntityId = 5;
        private const int RedModelId = 4;

        /// <summary>Entity 11 BUGEN in bugin1a: CHAR 8, placed at (-107,54,-28) on triangle 38.</summary>
        private const int BugenhagenEntityId = 11;
        private const int BugenhagenModelId = 8;

        private readonly Dictionary<int, byte> bytes = [];

        public BugenhagenMemory(int field)
        {
            bytes[FieldPositionReader.AddressFieldNumModels] = 12;
            for (var entity = 0; entity < 32; entity++)
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 0xFF;
            }

            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + RedEntityId] = RedModelId;
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + BugenhagenEntityId] =
                BugenhagenModelId;

            // Red XIII where the log's own NPC list has him: Red XIII@(167,-96,-624), and
            // Bugenhagen where bugin1a entity 11's own XYZI puts him.
            SetModel(RedModelId, 167, -96, -624, visible: true);
            SetModel(BugenhagenModelId, -107, 54, -28, visible: true);
            SetModel(0, -349, -319, -635, visible: true);
            SetField(field);
        }

        public void SetField(int field)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = FieldPositionReader.FieldModule;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public void SetBank3(int address, int mask) =>
            bytes[Bank3(address)] = (byte)(ReadByte(Bank3(address)) | mask);

        public void ClearBank3(int address, int mask) =>
            bytes[Bank3(address)] = (byte)(ReadByte(Bank3(address)) & ~mask);

        /// <summary>Cloud alone, which is what AD 3 leaves behind.</summary>
        public void EmptyTheParty()
        {
            bytes[Bank3(10)] = 0xFF;
            bytes[Bank3(11)] = 0xFF;
        }

        public void FillTheParty()
        {
            bytes[Bank3(10)] = 3;
            bytes[Bank3(11)] = 5;
        }

        /// <summary>
        /// AD 10 Script 3: bit 0 of Bank[3][170] at byte 102, bit 4 of Bank[3][161] at 106,
        /// and Red XIII walks out - entity 5 Script 10 ends on VISI 0.
        /// </summary>
        public void PlayTheObservatoryScene()
        {
            SetBank3(170, ObservatorySeen);
            SetBank3(161, 0x10);
            SetModel(RedModelId, 167, -96, -624, visible: false);
        }

        public FieldStoryTargetReader StoryReader() =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields(), _ => true);

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public short ReadInt16(int address) =>
            address % FieldNavigationObjectReader.FieldEventDataStride == 0x72 &&
            address >= EventTable
                ? (short)40
                : (short)(ReadByte(address) | (ReadByte(address + 1) << 8));

        public int ReadInt32(int address) => address switch
        {
            FieldBoundaryStateReader.AddressFieldGlobalObjectPtr => FieldState,
            FieldNavigationObjectReader.AddressFieldEventDataPtr => EventTable,
            _ => ReadByte(address) | (ReadByte(address + 1) << 8) |
                 (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24),
        };

        private static int Bank3(int address) =>
            FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + address;

        private void SetModel(int modelId, int x, int y, int z, bool visible)
        {
            var record = EventTable + (modelId * FieldNavigationObjectReader.FieldEventDataStride);
            WriteInt32(record + FieldNavigationObjectReader.PositionXOffset,
                x * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            WriteInt32(record + FieldNavigationObjectReader.PositionYOffset,
                y * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            WriteInt32(record + FieldNavigationObjectReader.PositionZOffset,
                z * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            bytes[record + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
        }

        private void WriteInt32(int address, int value)
        {
            for (var index = 0; index < 4; index++)
            {
                bytes[address + index] = (byte)(value >> (index * 8));
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"cosmo observatory story - {label}: expected {expected}, got {actual}");
        }
    }
}
