using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Optional interactions the walkthrough sends the player back to, each a model-less LINE the
/// NPC reader cannot publish (it needs a model) and that no other catalog offered.
///
/// <para>Tifa's piano (niv_ti2 17) had been a flashback-only Location, so the Disc 2 visits
/// that earn the Elemental materia and Final Heaven found nothing. Wonder Square's machines
/// (games_2) were first-visit Story steps only, and its Snow Game, second G Bike and Submarine
/// Game were never offered. The Battle Square prize windows (coloin1), the rocket's control
/// panel (rcktin4), Mideel's weapon-shop back door and key spot (itown_w, itown1a) and the
/// Honey Bee Inn's room doors (onna_4) had no target at all, and neither had two shop
/// counters whose buy menu only their LINE opens: in the Under Junon weapon store the logged
/// visit talked to the shopkeeper four times ("Hmm, what?") and left without the shop.</para>
///
/// <para>Each row is checked through the production object reader against live state - the
/// moment, the flag that retires it, the LINE's own enable bit and the leader's radius - and,
/// with installed data, against the field's own bytes and a walk from the ways into the field.
/// What playing them earns stays the game's decision.</para>
/// </summary>
internal static class GuideRevisitInteractionTests
{
    private sealed record Row(
        int Field, int Entity, string Name, string Label,
        int X, int Y, int Z, int Minimum = -1,
        int CollectedBank = -1, int CollectedAddress = -1, byte CollectedMask = 0, int Maximum = -1);

    private static readonly Row[] Rows =
    [
        new(287, 17, "piano", "Tifa's piano", -237, -249, 0),
        new(507, 10, "kakul1", "3D Battler", -166, -144, 32, 445),
        new(507, 11, "kakul2", "3D Battler, other side", -110, -201, 32, 445),
        new(507, 18, "mogu", "Mog House", 3, -252, 0, 445),
        new(507, 19, "bikeg", "G Bike", 241, 174, 0, 445),
        new(507, 20, "bikeg2", "G Bike, second machine", 149, 145, 0),
        new(507, 21, "snowb", "Snow Game", -55, 278, 0, 790),
        new(507, 22, "snowb2", "Snow Game, second machine", -176, 177, 0, 790),
        new(507, 23, "subm", "Submarine Game", -337, 42, 21, 1299),
        new(507, 24, "la", "Fortune Telling", 299, 153, 0, 445),
        new(506, 11, "udel", "Arm Wrestling machine", 183, 1610, -255, 445),
        new(506, 14, "ufo1", "Wonder Catcher", 286, 1345, -255, 445),
        new(506, 15, "ufo2", "Wonder Catcher, other side", 358, 1418, -255, 445),
        new(506, 16, "bsl", "Basketball Game", -229, 1664, -255, 445),
        new(500, 18, "lent1", "Battle points exchange counter", 273, -3289, -152),
        new(500, 19, "lent2", "Battle points exchange counter, second window", -244, -3283, -152),
        new(566, 7, "consl", "Control panel", -1, -120, 0, CollectedBank: 3, CollectedAddress: 134, CollectedMask: 0x20),
        new(717, 12, "line02", "Weapon shop back door", 109, 192, 0),
        new(712, 17, "oldkey", "Something to examine", -941, -818, 390, CollectedBank: 15, CollectedAddress: 177, CollectedMask: 0x40),
        new(218, 17, "border5", "Occupied room door, 1 of 2", 261, 138, 26),
        new(218, 18, "border6", "Occupied room door, 2 of 2", 261, -145, 26),
        new(218, 19, "border7", "Free room door, 1 of 2", -260, 149, 26),
        new(218, 20, "border8", "Free room door, 2 of 2", -269, -151, 26),
        new(432, 2, "wswelcm", "Weapon shop counter", 138, 343, 0),
        new(443, 6, "border2", "Materia shop counter", 662, 1849, 0),
        new(287, 5, "tansu", "Drawer in Tifa's room", -99, 213, 0, CollectedBank: 3, CollectedAddress: 19, CollectedMask: 0x20, Maximum: 383),
        new(531, 16, "LINES1", "Sealed door", -97, 349, -1, 514),
        new(699, 5, "cure3", "Healing spot", 48, 166, 0),
        new(633, 3, "line1", "Something to examine", 57, 266, 0),
        new(636, 6, "line3", "Something to examine", 224, -387, 67),
        new(78, 5, "l1", "Something to examine", 41, 499, -12)
    ];

    public static void Run()
    {
        EachRowIsItsNativeLine();
        ThePianoStaysForEveryVisit();
        WonderSquareMachinesFollowTheFirstVisitAndTheSnowGameMoment();
        WonderSquareFirstFloorMachinesStayAfterTheFirstVisit();
        WonderSquareOkMachinesAskForFacing();
        RetiredAndSwitchedOffRowsDisappear();
        NoneOfThemBecomesStory();
        TheShellHouseRestSaysWhatTheGameAsks();
        Console.WriteLine($"Guide revisit interactions: {Rows.Length} optional LINE objects publish by moment, flag, LINE state and the leader's radius.");
    }

    private static void EachRowIsItsNativeLine()
    {
        foreach (var row in Rows)
        {
            var definition = Definition(row.Field, row.Entity);
            Equal(row.Label, definition.Label, $"{row.Field}/{row.Entity} label");
            Equal(row.Name, definition.SourceEntityName, $"{row.Label} is entity {row.Name}");
            Equal(FieldNavigationObjectTargetKind.Line, definition.TargetKind, $"{row.Label} is its LINE");
            Equal(true, definition.UsesPlayerCollisionRadius, $"{row.Label} is reached on the engine's own LINE touch");
            Equal((row.X, row.Y, row.Z), (definition.StaticX, definition.StaticY, definition.StaticZ), $"{row.Label} sits on its LINE");
            Equal(row.Minimum, definition.MinimumGameMoment, $"{row.Label} minimum moment");
            Equal(row.Maximum, definition.MaximumGameMoment, $"{row.Label} maximum moment");
            Equal((row.CollectedBank, row.CollectedAddress, row.CollectedMask),
                (definition.CollectedBank, definition.CollectedAddress, definition.CollectedMask), $"{row.Label} retiring flag");
            Equal(-1, definition.RequiredBank, $"{row.Label} needs no catalog flag; the game decides the outcome");
        }
    }

    private static void ThePianoStaysForEveryVisit()
    {
        var memory = new ObjectMemory();
        var enabled = true;
        var reader = memory.Reader(_ => enabled);
        var position = new FieldPositionSnapshot(1, 287, 0, -300, -250, 0, 0, 0);
        // Flashback, present-day Disc 1, the Disc 2 reward visits, and after them.
        foreach (var moment in new[] { 344, 383, 384, 530, 796, 1200, 1998 })
        {
            memory.GameMoment = moment;
            var piano = reader.ReadTargets(position).SingleOrDefault(t => t.Label == "Tifa's piano");
            Equal(true, piano.Label is not null, $"the piano is offered at moment {moment}");
            Equal(FieldNavigationCategory.Objects, piano.Category, "the piano is an optional object, not Story");
            Equal(ObjectMemory.LeaderRadius - 1, piano.InteractionRadius,
                "the approach ends inside the leader's collision radius (+0x72), not a generic reach");
        }

        // A reward already taken changes what the piano says, not whether it is there.
        memory.Bank3[9] = 2;
        memory.Bank13[1] = 0x07;
        memory.GameMoment = 1500;
        Equal(true, reader.ReadTargets(position).Any(t => t.Label == "Tifa's piano"), "after its rewards the piano is still a piano");

        enabled = false;
        Equal(false, reader.ReadTargets(position).Any(t => t.Label == "Tifa's piano"), "a switched-off LINE is not offered");
        enabled = true;
        memory.LeaderRadius0 = true;
        Equal(false, reader.ReadTargets(position).Any(t => t.Label == "Tifa's piano"),
            "without the leader's radius there is no reach to guess");
        memory.LeaderRadius0 = false;

        var live = new FieldNavigationTriggerLine(-237, -140, 0, -238, -359, 0);
        var liveReader = memory.Reader(_ => true, entity => entity == 17 ? live : null);
        var touched = liveReader.ReadTargets(position).Single(t => t.Label == "Tifa's piano");
        Equal(live, touched.TriggerLine, "with the live segment the piano is reached on the LINE itself");
        Equal(ObjectMemory.LeaderRadius, touched.LineActivationRadius, "inside the leader's radius");
    }

    private static void WonderSquareMachinesFollowTheFirstVisitAndTheSnowGameMoment()
    {
        var memory = new ObjectMemory();
        var reader = memory.Reader(_ => true);
        var position = new FieldPositionSnapshot(1, 507, 0, 0, 0, 0, 0, 0);
        string[] Offered(int moment)
        {
            memory.GameMoment = moment;
            return reader.ReadTargets(position).Select(t => t.Label).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        }

        // During 440..444 the five first-visit machines are Story steps; only the one it
        // never named is an object, so nothing is offered twice. The Submarine Game is not:
        // customer m6 stands solid on its LINE, calling it out of order, until moment 1299.
        Equal("G Bike, second machine", string.Join("|", Offered(442)), "first visit");
        var storyEntities = FieldStoryEventCatalog.CreateAllFields()
            .Where(r => r.FieldId == 507 && r.EntityId >= 0).ToArray();
        foreach (var step in storyEntities)
        {
            var definition = FieldNavigationObjectCatalog.CreateAllFields()
                .SingleOrDefault(d => d.FieldId == 507 && d.EntityId == step.EntityId);
            if (definition.Label is null) continue;
            Equal(true, definition.MinimumGameMoment > step.MaximumGameMoment,
                $"{definition.Label} starts only after the first-visit step {step.Label} ends");
        }

        var revisit = Offered(600);
        Equal(6, revisit.Length, $"the Keystone visit offers every playable machine: {string.Join(", ", revisit)}");
        Equal(false, revisit.Any(l => l.StartsWith("Snow Game", StringComparison.Ordinal)),
            "before moment 790 the Snow Game shows a customer's line, not the game");
        Equal(8, Offered(790).Length, "from moment 790 both Snow Game machines play");
        Equal(false, Offered(1298).Contains("Submarine Game"), "the Submarine Game is blocked until moment 1299");
        Equal(9, Offered(1299).Length, "from moment 1299 the Submarine Game is free too");
    }

    /// <summary>
    /// Wonder Square's first floor (games_1). The logged revisit (Nibel Area, well past the
    /// first visit) heard "Objects: none" there while the floor above listed its games: the
    /// four machines were first-visit Story steps (440..444) and nothing took over after. Their
    /// LINEs are defined by every Init and nothing in games_1 switches one off, so from 445 on
    /// they are Objects, exactly like games_2's.
    /// </summary>
    private static void WonderSquareFirstFloorMachinesStayAfterTheFirstVisit()
    {
        var memory = new ObjectMemory();
        var enabled = new HashSet<int> { 11, 14, 15, 16 };
        var reader = memory.Reader(entity => enabled.Contains(entity));
        var position = new FieldPositionSnapshot(1, 506, 0, 0, 0, 0, 0, 0);
        string Offered(int moment)
        {
            memory.GameMoment = moment;
            return string.Join("|", reader.ReadTargets(position).Select(t => t.Label).OrderBy(l => l, StringComparer.Ordinal));
        }

        const string machines = "Arm Wrestling machine|Basketball Game|Wonder Catcher|Wonder Catcher, other side";
        Equal("", Offered(442), "during the first visit the Story steps name the machines, so nothing is offered twice");
        foreach (var moment in new[] { 445, 600, 1008, 1299, 1998 })
        {
            Equal(machines, Offered(moment), $"at moment {moment} the first floor offers its four machines");
        }

        var story = FieldStoryEventCatalog.CreateAllFields().Where(r => r.FieldId == 506 && r.EntityId >= 0).ToArray();
        foreach (var entity in new[] { 11, 14, 15, 16 })
        {
            var step = story.Single(r => r.RequiredEnabledLineEntityId == entity);
            Equal((440, 444), (step.MinimumGameMoment, step.MaximumGameMoment), $"{step.Label} stays the first-visit step");
            Equal(true, Definition(506, entity).MinimumGameMoment > step.MaximumGameMoment,
                $"{Definition(506, entity).Label} starts only after the first-visit step ends");
        }

        memory.GameMoment = 600;
        enabled.Remove(14);
        Equal(false, reader.ReadTargets(position).Any(t => t.Label == "Wonder Catcher"), "a switched-off LINE is not offered");
    }

    /// <summary>
    /// The Wonder Square machines that run from their OK slot (00637D35) are marked so, and the
    /// reader carries it to the target: OK there also needs the leader facing the line, so auto
    /// walk turns to it before saying it has arrived. The Basketball Game and the 3D Battler run
    /// from their Go slot, on the touch, and are not marked.
    /// </summary>
    private static void WonderSquareOkMachinesAskForFacing()
    {
        var marked = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(d => d.FieldId is 506 or 507 && d.ActivatesOnOk)
            .Select(d => $"{d.FieldId}/{d.EntityId}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Equal("506/11|506/14|506/15|507/18|507/19|507/20|507/21|507/22|507/23|507/24", string.Join("|", marked),
            "the OK machines of both floors are marked, and only they");

        var memory = new ObjectMemory { GameMoment = 1300 };
        var reader = memory.Reader(_ => true, entity => entity is 10 or 19
            ? new FieldNavigationTriggerLine(0, 0, 0, 10, 0, 0)
            : null);
        var targets = reader.ReadTargets(new FieldPositionSnapshot(1, 507, 0, 0, 0, 0, 0, 0));
        Equal(true, targets.Single(t => t.Label == "G Bike").LineActivatesOnOk, "the G Bike target asks for facing");
        Equal(false, targets.Single(t => t.Label == "3D Battler").LineActivatesOnOk, "the 3D Battler, a Go line, does not");
    }
    private static void RetiredAndSwitchedOffRowsDisappear()
    {
        var memory = new ObjectMemory { GameMoment = 1320 };
        var enabled = new HashSet<int>();
        var reader = memory.Reader(entity => enabled.Contains(entity));
        FieldPositionSnapshot At(int field) => new(1, field, 0, 0, 0, 0, 0, 0);
        bool Offers(int field, string label) => reader.ReadTargets(At(field)).Any(t => t.Label == label);

        enabled.UnionWith([7, 12, 17, 18, 19, 20]);
        Equal(true, Offers(566, "Control panel"), "the control panel before it has been tried");
        memory.Bank3[134] = 0x20;
        Equal(false, Offers(566, "Control panel"), "3[134] bit 5 is set by the attempt; its OK does nothing after");

        memory.GameMoment = 1100;
        Equal(true, Offers(717, "Weapon shop back door"), "the back door answers on every visit");
        memory.Bank15[178] = 0x04;
        memory.Bank15[177] = 0xC0;
        Equal(true, Offers(717, "Weapon shop back door"), "and still answers once the quest is done");
        Equal(false, Offers(712, "Something to examine"), "the key spot is done once 15[177] bit 6 is set");
        memory.Bank15[177] = 0;
        Equal(true, Offers(712, "Something to examine"), "the key spot before the key is taken");
        enabled.Remove(17);
        Equal(false, Offers(712, "Something to examine"), "its Init switches the LINE off, and the target follows");

        memory.GameMoment = 190;
        enabled.UnionWith([17, 18, 19, 20]);
        Equal(4, reader.ReadTargets(At(218)).Count(t => t.Label.Contains("room door", StringComparison.Ordinal)),
            "the Honey Bee Inn's four room doors");
        enabled.ExceptWith([19, 20]);
        Equal(2, reader.ReadTargets(At(218)).Count(t => t.Label.Contains("room door", StringComparison.Ordinal)),
            "once a room is chosen the free-room LINEs are off and so are their targets");

        memory.GameMoment = 1100;
        enabled.UnionWith([2, 6]);
        foreach (var field in new[] { 432, 443 })
        {
            Equal(1, reader.ReadTargets(At(field)).Count(t => t.Label.EndsWith("shop counter", StringComparison.Ordinal)),
                $"field {field} offers its shop counter");
        }

        // The flashback drawer answers once, and only in the flashback.
        memory.GameMoment = 360;
        enabled.UnionWith([5, 16]);
        Equal(true, Offers(287, "Drawer in Tifa's room"), "the drawer in the flashback");
        memory.Bank3[19] = 0x20;
        Equal(false, Offers(287, "Drawer in Tifa's room"), "3[19] bit 5 is set by the drawer; after that OK says nothing");
        memory.Bank3[19] = 0;
        memory.GameMoment = 384;
        Equal(false, Offers(287, "Drawer in Tifa's room"), "from moment 384 the drawer's script is silent");
        memory.GameMoment = 513;
        Equal(false, Offers(531, "Sealed door"), "before 514 the sealed door says nothing");
        memory.GameMoment = 514;
        Equal(true, Offers(531, "Sealed door"), "from 514 it says only Bugenhagen can open it");

        memory.GameMoment = 570;
        enabled.UnionWith([18, 19]);
        Equal(2, reader.ReadTargets(At(500)).Count(t => t.Label.StartsWith("Battle points exchange counter", StringComparison.Ordinal)),
            "both prize windows");
    }

    /// <summary>
    /// The Forgotten City rest (losinn, field 636). Four generated Story rows already carried
    /// the step - each bound to its LINE's enable state (entities 8..11) and to target moment
    /// 664 - but under the generic "Continue on from here". The labels now say what the game
    /// asks; everything else about the rows is as extracted. The step is offered while the
    /// counter is below 664, and resting (Bank[3][132] bit 4, counter 664) hands over to
    /// "Go back outside".
    /// </summary>
    private static void TheShellHouseRestSaysWhatTheGameAsks()
    {
        var rows = FieldStoryEventCatalog.CreateAllFields().Where(r => r.FieldId == 636).ToArray();
        var expected = new (int Line, string Entity, string Label)[]
        {
            (8, "line5", "Rest in a bed: face it and press OK (bed 1 of 3)"),
            (9, "line6", "Rest in a bed: face it and press OK (bed 2 of 3)"),
            (10, "line7", "Rest in a bed: face it and press OK (bed 3 of 3)"),
            (11, "line8", "Walk up to the beds; the game asks whether to rest")
        };
        foreach (var (line, entity, label) in expected)
        {
            var matching = rows.Where(r => r.RequiredEnabledLineEntityId == line).ToArray();
            Equal(1, matching.Length, $"one rest row is bound to LINE entity {line}");
            var row = matching[0];
            Equal(label, row.Label, $"LINE entity {line} says what the game asks");
            Equal(entity, row.SourceEntityName, $"LINE entity {line} is {entity}");
            Equal(664, row.TargetGameMoment, $"{label} is the step that writes 664");
            Equal((-1, -1), (row.MinimumGameMoment, row.MaximumGameMoment), $"{label} keeps its extracted window");
            Equal(true, row.TriggerLine is not null && row.UsesPlayerCollisionRadius, $"{label} keeps its native LINE and radius");
        }

        Equal(false, rows.Any(r => r.Label == "Continue on from here"), "no rest row keeps the generic label");
        Equal(4, rows.Count(r => r.TargetGameMoment == 664), "no duplicate rest objective was added");

        var memory = new StoryMemory { GameMoment = 660 };
        var enabled = new HashSet<int> { 8, 9, 10, 11 };
        var reader = memory.Reader(entity => enabled.Contains(entity));
        var position = new FieldPositionSnapshot(1, 636, 0, 0, 0, 0, 0, 0);
        string[] Offered() => reader.ReadTargets(position).Select(t => t.Label).OrderBy(l => l, StringComparer.Ordinal).ToArray();

        Equal(string.Join("|", expected.Select(e => e.Label).OrderBy(l => l, StringComparer.Ordinal)), string.Join("|", Offered()),
            "before the rest, the four ways to rest are the step");
        enabled.Remove(10);
        Equal(false, Offered().Contains("Rest in a bed: face it and press OK (bed 3 of 3)"), "a switched-off LINE is not offered");
        enabled.Add(10);

        memory.GameMoment = 664;
        memory.Bank3[132] = 0x10;
        Equal("Go back outside", string.Join("|", Offered()), "resting hands over to the way out");
    }

    private sealed class StoryMemory
    {
        private const int Events = 0x02500000;
        private const int Base = FieldNavigationObjectReader.AddressFieldBankBase;
        public byte[] Bank3 { get; } = new byte[256];
        public int GameMoment { get; set; }

        public FieldStoryTargetReader Reader(Func<int, bool> isLineEnabled) => new(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events : 0,
            address => (short)(ReadByte(address) | ReadByte(address + 1) << 8),
            ReadByte,
            FieldStoryEventCatalog.CreateAllFields(),
            isLineEnabled);

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressFieldNumModels) return 1;
            if (address == Events + FieldNavigationNpcReader.CollisionRadiusOffset) return ObjectMemory.LeaderRadius;
            var offset = address - Base;
            return offset switch
            {
                0 => (byte)GameMoment,
                1 => (byte)(GameMoment >> 8),
                >= 0x100 and < 0x200 => Bank3[offset - 0x100],
                _ => 0
            };
        }
    }

    /// <summary>
    /// Counters for people who stand where the party cannot walk (behind a counter, in a
    /// walkmesh piece of their own). The LINE in front of them is the only native way to
    /// address them, and it runs their lines; the NPC row now carries it, gated by the
    /// LINE's live enable state and the person's own visibility.
    /// </summary>
    private static readonly (int Field, int Entity, int Line, string Label)[] CounterPeople =
    [
        (178, 5, 9, "Man"),
        (178, 6, 8, "Child"),
        (443, 15, 7, "Tourist guide"),
        (507, 6, 25, "Man")
    ];

    private static List<(Row Row, FieldNavigationTarget Target)> CounterPeopleAreReachedThroughTheirLine(FieldScriptNavigationCatalog catalog,
        Func<int, IReadOnlyList<FieldScriptDefinition>> scripts)
    {
        var published = new List<(Row Row, FieldNavigationTarget Target)>();
        foreach (var (field, entity, line, label) in CounterPeople)
        {
            var b = scripts(field).Single(s => s.EntityId == line && s.ScriptId == 0).Opcodes.First().Bytes.ToArray();
            int C(int i) => BitConverter.ToInt16(b, 1 + i * 2);
            var segment = new FieldNavigationTriggerLine(C(0), C(1), C(2), C(3), C(4), C(5));
            const int events = 0x02500000;
            var npc = events + FieldNavigationObjectReader.FieldEventDataStride;
            var hidden = false;
            var lineOn = true;
            byte Byte(int a) => a == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
                a == FieldNavigationObjectReader.AddressFieldModelIdArray + entity ? (byte)1 :
                a == npc + FieldNavigationObjectReader.VisibilityOffset ? (hidden ? (byte)0 : (byte)1) :
                a >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                a < FieldNavigationObjectReader.AddressFieldModelIdArray + 256 ? (byte)255 : (byte)0;
            short Short(int a) => a == events + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)ObjectMemory.LeaderRadius : (short)0;
            var reader = new FieldNavigationNpcReader(
                a => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
                Short, Byte, (_, _) => [], f => catalog.ReadField(f).Npcs, isLineEnabled: e => e == line && lineOn);
            var position = new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0);
            var targets = reader.ReadTargets(position).Where(t => t.StableId == $"npc:{field}:{entity}").ToArray();
            Equal(1, targets.Length, $"{field}/{entity} is offered in NPCs");
            Equal(label, targets[0].Label, $"{field}/{entity} label");
            Equal(FieldNavigationCategory.Npcs, targets[0].Category, $"{field}/{entity} is a person, not an object");
            Equal(segment, targets[0].TriggerLine, $"{field}/{entity} is reached on its counter LINE {line}");
            Equal(line, targets[0].TriggerEntityId, $"{field}/{entity} activates through LINE {line}");
            Equal(ObjectMemory.LeaderRadius, targets[0].LineActivationRadius, $"{field}/{entity} inside the leader's radius");
            published.Add((new Row(field, entity, $"npc{entity}", label, targets[0].X, targets[0].Y, targets[0].Z), targets[0]));
            lineOn = false;
            Equal(false, reader.ReadTargets(position).Any(t => t.StableId == $"npc:{field}:{entity}"),
                $"{field}/{entity}: while its counter LINE is off the person cannot be addressed");
            lineOn = true;
            hidden = true;
            Equal(false, reader.ReadTargets(position).Any(t => t.StableId == $"npc:{field}:{entity}"),
                $"{field}/{entity}: a hidden person is not offered");
        }

        // The native reasons each LINE is that person's counter.
        string Hex(int field, int entity, int script) => string.Concat(scripts(field)
            .Single(s => s.EntityId == entity && s.ScriptId == script).Opcodes
            .Select(o => Convert.ToHexString(o.Bytes.ToArray())));
        Contains(Hex(178, 9, 4), "400004", "mds5_w LINEC shows the man's own dialog 4");
        Contains(Hex(178, 5, 1), "400004", "which is the man's Talk before moment 236");
        Contains(Hex(443, 7, 4), "0600C3", "del2 border3 runs the tourist-information script");
        Contains(Hex(507, 25, 1), "40011E", "games_2 bi shows the rider's dialog 30");
        Equal(true, scripts(507).Where(s => s.EntityId == 6).SelectMany(s => s.Opcodes).All(o => o.Opcode != 0x40),
            "the rider's own scripts say nothing, so the LINE is the only way to hear him");
        Console.WriteLine($"Guide revisit interactions: {CounterPeople.Length} people behind counters are offered through their native LINE.");
        return published;
    }

    private static void NoneOfThemBecomesStory()
    {
        var story = FieldStoryEventCatalog.CreateAllFields();
        foreach (var row in Rows.Where(r => r.Field is not (506 or 507)))
        {
            Equal(false, story.Any(s => s.FieldId == row.Field && s.EntityId == row.Entity),
                $"{row.Label} stays optional: no Story step points at it");
        }
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        var source = new FlevelDataSource(root);
        var world = new LgpArchiveReader(Path.Combine(root, "data", "wm", "world_us.lgp"));
        Equal(true, world.TryReadFile("field.tbl", out var fieldTable), "field.tbl is installed");
        var scripts = new Dictionary<int, IReadOnlyList<FieldScriptDefinition>>();
        IReadOnlyList<FieldScriptDefinition> Scripts(int field) =>
            scripts.TryGetValue(field, out var cached) ? cached : scripts[field] = catalog.ReadAllScriptOpcodes(field);
        string Hex(int field, int entity, int script) => string.Concat(Scripts(field)
            .Single(s => s.EntityId == entity && s.ScriptId == script).Opcodes
            .Select(o => Convert.ToHexString(o.Bytes.ToArray())));

        foreach (var row in Rows)
        {
            var own = Scripts(row.Field).Where(s => s.EntityId == row.Entity).ToArray();
            Equal(true, own.Length > 0 && own.All(s => s.EntityName == row.Name), $"{row.Label}: entity {row.Entity} is {row.Name}");
            var line = own.Single(s => s.ScriptId == 0).Opcodes.First().Bytes.ToArray();
            Equal(0xD0, (int)line[0], $"{row.Label}: its Init opens with its LINE");
            int C(int i) => BitConverter.ToInt16(line, 1 + i * 2);
            Equal((row.X, row.Y, row.Z), ((C(0) + C(3)) / 2, (C(1) + C(4)) / 2, (C(2) + C(5)) / 2), $"{row.Label}: the target is the LINE's midpoint");
            Equal(false, catalog.ReadField(row.Field).Npcs.Any(n => n.EntityId == row.Entity && !string.IsNullOrEmpty(n.ModelResourceName)),
                $"{row.Label} has no model, so the NPC reader never offered it");
        }

        // Each gate the rows carry, in the field's own bytes.
        Contains(Hex(287, 17, 1), "1620000080010326", "the piano's flashback branch is moment < 384");
        Contains(Hex(287, 17, 1), "162000001C030462", "and its Disc 2 branch is moment >= 796");
        Equal(false, Scripts(287).Any(s => s.Opcodes.Any(o => o.Opcode == 0xD1)), "nothing in niv_ti2 switches a LINE off");
        Contains(Hex(507, 21, 1), "1620000016030328", "the Snow Game plays only from moment 790");
        Contains(Hex(507, 22, 1), "1620000016030328", "both Snow Game machines");
        Contains(Hex(507, 9, 0), "1620000013050407", "customer m6 leaves the Submarine Game at moment 1299");
        Contains(Hex(507, 9, 0), "7E01C701A400", "from then its Init turns its talk and solidity off and hides it");
        Equal(false, Scripts(507).Any(s => s.Opcodes.Any(o => o.Opcode == 0xD1)), "no Wonder Square machine is switched off");
        // games_1: every machine runs from its own LINE on every visit, with no moment test.
        Equal(false, Scripts(506).Any(s => s.Opcodes.Any(o => o.Opcode == 0xD1)), "nothing in games_1 switches a LINE off");
        // Every definition marked as running on OK does so in the installed script: its OK slot
        // (1) has code and its Go (4) and Go once (5) slots have none.
        int Code(int field, int entity, int slot) => Scripts(field)
            .Where(s => s.EntityId == entity && s.ScriptId == slot).SelectMany(s => s.Opcodes).Count(o => o.Opcode != 0x00);
        foreach (var definition in FieldNavigationObjectCatalog.CreateAllFields().Where(d => d.ActivatesOnOk))
        {
            Equal((true, 0, 0), (Code(definition.FieldId, definition.EntityId, 1) > 0,
                    Code(definition.FieldId, definition.EntityId, 4), Code(definition.FieldId, definition.EntityId, 5)),
                $"{definition.FieldId}/{definition.EntityId} {definition.Label} runs on OK, not on Go");
        }
        Contains(Hex(506, 11, 1), "400105", "the arm-wrestling LINE's OK shows its card (dialog 5)");
        Contains(Hex(506, 11, 1), "3A0064000000", "and takes its 100 gil");
        Contains(Hex(506, 14, 1), "030148", "the Wonder Catcher's OK runs Cloud's catcher script 8");
        Contains(Hex(506, 15, 1), "030149", "and its other side script 9");
        Contains(Hex(506, 16, 4), "31200299", "the basketball LINE needs OK while the leader is on it");
        Contains(Hex(506, 16, 4), "400115", "and shows its card (dialog 21)");
        foreach (var entity in new[] { 11, 14, 15, 16 })
        {
            Equal(false, Scripts(506).Where(s => s.EntityId == entity).SelectMany(s => s.Opcodes)
                    .Any(o => o.Opcode is 0x16 or 0x17 or 0x18 or 0x19 && o.Bytes.Count >= 4 &&
                        o.Bytes[1] >> 4 == 2 && o.Bytes[2] == 0 && o.Bytes[3] == 0),
                $"games_1 machine {entity} has no moment test");
        }
        Contains(Hex(500, 18, 1), "40010E", "the prize window says how many battle points there are");
        Contains(Hex(566, 7, 1), "143086050A", "the control panel asks while 3[134] bit 5 is clear");
        Contains(Hex(566, 7, 1), "480500A6", "its question is dialog 166");
        Contains(Hex(712, 17, 0), "14F0B1400603D100", "the key spot's Init switches it off once 15[177] bit 6 is set");
        Contains(Hex(717, 12, 4), "0302B6", "the back door runs the leading member's own scene");
        Contains(Hex(218, 17, 1), "48050012", "an occupied room asks dialog 18");
        Contains(Hex(218, 19, 0), "D100", "a free room's Init switches its LINE off once a room is taken");

        // The key spot's second entity is the audible cue, not an inert copy: its GoOnce plays
        // SOUND 0x011D on the same LINE, which its own Init also switches off once the key is out.
        Contains(Hex(712, 18, 5), "F1001D01", "entity 18's GoOnce plays the clink");
        Contains(Hex(712, 18, 0), "14F0B1400603D100", "and stops once the key is out");

        // The shell-house rest: each bed asks only on OK while facing it, the stair line asks
        // once per visit on walking over it, and all four run the one rest routine.
        foreach (var (line, script, facing) in new[] { (8, 4, "14500098020A"), (9, 4, "14500078020A"), (10, 2, "14500058020A") })
        {
            Contains(Hex(636, line, script), "31200214", $"bed LINE {line} needs OK");
            Contains(Hex(636, line, script), facing, $"bed LINE {line} tests the leader's facing");
            Contains(Hex(636, line, script), "0302C3", $"bed LINE {line} runs the rest routine");
        }

        Contains(Hex(636, 11, 3), "143084040A", "the stair line asks only before the rest");

        // The examinables added in the text audit, each pinned to what it does.
        Contains(Hex(287, 5, 1), "1620000080010357", "the drawer answers only before moment 384");
        Contains(Hex(287, 5, 1), "82301305", "and sets 3[19] bit 5");
        Contains(Hex(531, 16, 1), "1620000002020416", "the sealed door speaks from moment 514");
        Contains(Hex(699, 5, 4), "3E", "the healing spot restores HP/MP");
        Contains(Hex(699, 5, 4), "400002", "and says so (dialog 2)");
        Contains(Hex(633, 3, 1), "31200214", "the Forgotten City spot needs OK");
        Contains(Hex(78, 5, 1), "400102", "the sleeping man mutters (dialog 2)");
        var counterTargets = CounterPeopleAreReachedThroughTheirLine(catalog, Scripts);
        Contains(Hex(636, 11, 3), "0302C3", "and runs the same routine");
        var rest = Hex(636, 2, 3);
        Contains(rest, "48050029", "the routine asks dialog 41, 'Here's a bed. Get some rest?'");
        Contains(rest, "82308404", "Yes sets Bank[3][132] bit 4");
        Contains(rest, "8120009802", "and the counter to 664");

        // The two counters open the buy menu, and the shopkeeper's own Talk does not. Where
        // the Talk does open it (Sector 7's weapon shop) or an NPC row already carries the
        // counter (Icicle Inn's welcom1), the counter is not repeated as an object.
        Contains(Hex(432, 2, 4), "49000812", "the Under Junon counter opens shop 18");
        Contains(Hex(443, 6, 4), "031123", "Costa del Sol's second stall runs its owner's script 3");
        Contains(Hex(443, 17, 3), "4900081B", "which opens materia shop 27");
        foreach (var (field, keeper) in new[] { (432, 6), (443, 17) })
        {
            Equal(false, Scripts(field).Single(s => s.EntityId == keeper && s.ScriptId == 1).Opcodes.Any(o => o.Opcode == 0x49),
                $"field {field}'s shopkeeper {keeper} opens no menu from his Talk");
        }

        Equal(true, catalog.ReadField(432).Npcs.All(n => n.EntityId != 6 || n.InteractionLineEntityId is null),
            "no NPC row carries the Under Junon counter for its shopkeeper");
        Equal(true, catalog.ReadField(650).Npcs.Any(n => n.EntityId == 10 && n.InteractionLineEntityId == 3),
            "Icicle Inn's shopkeeper row already carries welcom1");
        Contains(Hex(650, 3, 1), "4900082E", "and welcom1 opens his shop");

        Contains(Hex(148, 9, 1), "49000800", "Sector 7's weapon shopkeeper opens his shop himself");
        Equal(false, FieldNavigationObjectCatalog.CreateAllFields().Any(d => d.FieldId == 148 && d.EntityId == 6),
            "so his counter is not repeated as an object");

        // Static route connectivity, not autowalk. The planner gets the installed walkmesh and
        // the field's native transitions, and the target is the one the production object
        // reader publishes for the row (its native LINE segment and the fixture leader radius),
        // but there is no live boundary, body or model state. Every native way into the field
        // is tried and its outcome recorded; a failure passes only as a reviewed disposition.
        var outcomes = new List<string>();
        var unreviewed = new List<string>();
        var routes = 0;
        foreach (var field in Rows.Select(r => r.Field).Concat(counterTargets.Select(c => c.Row.Field)).Distinct())
        {
            var arrivals = Arrivals(catalog, source, fieldTable, Scripts, field).ToArray();
            Equal(true, arrivals.Length > 0, $"field {field} has a native way in");
            Equal(true, source.TryReadField(field, out var encoded), $"field {field} is installed");
            var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            const int pointer = 0x03000000;
            int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
                a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
            short Short(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
            var reader = new FieldWalkmeshReader(Int, Short);
            var mesh = reader.Read(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
            var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => catalog.ReadField(field).Transitions);
            var fieldTargets = new List<(Row Row, FieldNavigationTarget Target)>();
            foreach (var row in Rows.Where(r => r.Field == field))
            {
                var b = Scripts(field).Single(s => s.EntityId == row.Entity && s.ScriptId == 0).Opcodes.First().Bytes.ToArray();
                int C(int i) => BitConverter.ToInt16(b, 1 + i * 2);
                var segment = new FieldNavigationTriggerLine(C(0), C(1), C(2), C(3), C(4), C(5));
                var memory = new ObjectMemory { GameMoment = row.Maximum >= 0 ? row.Maximum : Math.Max(row.Minimum, 600) };
                var objects = memory.Reader(entity => entity == row.Entity, entity => entity == row.Entity ? segment : null);
                var published = objects.ReadTargets(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0))
                    .Where(t => t.Label == row.Label).ToArray();
                Equal(1, published.Length, $"{row.Label}: the object reader publishes one target");
                Equal(segment, published[0].TriggerLine, $"{row.Label}: the published target is its native LINE");
                fieldTargets.Add((row, published[0]));
            }

            // People behind counters, with the NPC reader's own published target.
            fieldTargets.AddRange(counterTargets.Where(c => c.Row.Field == field));
            foreach (var (row, target) in fieldTargets)
            {

                var reached = 0;
                foreach (var (x, y, triangle, origin) in arrivals
                    .OrderBy(a => a.Origin, StringComparer.Ordinal).ThenBy(a => a.X).ThenBy(a => a.Y))
                {
                    var key = $"{field}:{x}:{y}:{triangle}";
                    if (triangle >= mesh.Triangles.Count)
                    {
                        Record(row, origin, key, false, "arrival triangle is not on the installed walkmesh");
                        continue;
                    }

                    var position = new FieldPositionSnapshot(1, field, 0, x, y,
                        (int)Math.Round(mesh.Triangles[triangle].GetCentroid().Z), (ushort)triangle, 0);
                    var ok = planner.TryBuildRoute(position, target, out _);
                    if (ok) reached++;
                    Record(row, origin, key, ok, ok ? "route" : planner.LastDiagnostic);
                }

                Equal(true, reached > 0, $"{row.Label} is reachable from at least one native way into field {field}");
                routes += reached;
            }
        }

        foreach (var line in outcomes) Console.WriteLine(line);
        if (Environment.GetEnvironmentVariable("GUIDE_REVISIT_ROUTE_REPORT") != "1")
        {
            Equal(0, unreviewed.Count, "every failed approach has a reviewed disposition: " + string.Join(" | ", unreviewed));
        }

        Console.WriteLine($"Guide revisit interactions: native LINEs and gates match the installed archive; static route connectivity: {routes} successful approaches, {outcomes.Count(o => o.Contains(" FAIL ", StringComparison.Ordinal))} reviewed failed approaches.");

        void Record(Row row, string origin, string key, bool ok, string detail)
        {
            if (ok)
            {
                outcomes.Add($"  ROUTE ok   {row.Field}/{row.Entity} {row.Label} from {origin} {key}");
                return;
            }

            if (ReviewedFailures.TryGetValue(key, out var reason))
            {
                outcomes.Add($"  ROUTE FAIL {row.Field}/{row.Entity} {row.Label} from {origin} {key}: reviewed - {reason}");
            }
            else
            {
                outcomes.Add($"  ROUTE FAIL {row.Field}/{row.Entity} {row.Label} from {origin} {key}: UNREVIEWED - {detail}");
                unreviewed.Add($"{row.Label} from {origin} {key}: {detail}");
            }
        }
    }

    /// <summary>
    /// Native arrivals from which a revisit target is not statically reachable, each reviewed
    /// against the installed scripts. Keyed by field:x:y:triangle of the arrival.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ReviewedFailures = new Dictionary<string, string>
    {
    };

    /// <summary>
    /// Where the party lands in <paramref name="field"/>: gateways of its neighbours that lead
    /// here, MAPJUMPs in their scripts, and the world map's own entries.
    /// </summary>
    private static IEnumerable<(int X, int Y, int Triangle, string Origin)> Arrivals(
        FieldScriptNavigationCatalog catalog,
        FlevelDataSource source,
        byte[] fieldTable,
        Func<int, IReadOnlyList<FieldScriptDefinition>> scripts,
        int field)
    {
        var found = new Dictionary<(int, int, int), string>();
        var own = catalog.ReadField(field);
        var neighbours = new HashSet<int>(own.Exits.SelectMany(e => e.DestinationFieldIds ?? []));
        foreach (var gateway in Gateways(source, field)) neighbours.Add(gateway.Destination);
        foreach (var neighbour in neighbours.Where(n => n is > 0 and < 1200))
        {
            foreach (var gateway in Gateways(source, neighbour).Where(g => g.Destination == field))
                found.TryAdd((gateway.X, gateway.Y, gateway.Triangle), $"gateway of {neighbour}");
            IReadOnlyList<FieldScriptDefinition> list;
            try { list = scripts(neighbour); } catch (Exception) { continue; }
            foreach (var op in list.SelectMany(s => s.Opcodes).Where(o => o.Opcode == 0x60))
            {
                var b = op.Bytes.ToArray();
                if (BitConverter.ToUInt16(b, 1) == field)
                    found.TryAdd((BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7)), $"MAPJUMP in {neighbour}");
            }
        }

        for (var record = 0; record < fieldTable.Length / 12; record++)
        {
            if (BitConverter.ToUInt16(fieldTable, record * 12 + 6) == field)
                found.TryAdd((BitConverter.ToInt16(fieldTable, record * 12), BitConverter.ToInt16(fieldTable, record * 12 + 2),
                    BitConverter.ToUInt16(fieldTable, record * 12 + 4)), $"world map entry {record}");
        }

        return found.Select(entry => (entry.Key.Item1, entry.Key.Item2, entry.Key.Item3, entry.Value));
    }

    private static IEnumerable<(int Destination, int X, int Y, int Triangle)> Gateways(FlevelDataSource source, int field)
    {
        if (!source.TryReadField(field, out var encoded)) yield break;
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var trigger = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
        for (var index = 0; index < 12; index++)
        {
            var record = trigger + 0x38 + index * 24;
            var destination = BitConverter.ToUInt16(bytes, record + 18);
            if (destination is 0 or 0x7FFF) continue;
            yield return (destination, BitConverter.ToInt16(bytes, record + 12), BitConverter.ToInt16(bytes, record + 14),
                BitConverter.ToUInt16(bytes, record + 16));
        }
    }

    private static FieldNavigationObjectDefinition Definition(int field, int entity)
    {
        var matches = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(d => d.FieldId == field && d.EntityId == entity).ToArray();
        Equal(1, matches.Length, $"field {field} entity {entity} has one object definition");
        return matches[0];
    }

    private sealed class ObjectMemory
    {
        public const int LeaderRadius = 20;
        private const int Events = 0x02500000;
        private const int Base = FieldNavigationObjectReader.AddressFieldBankBase;
        public byte[] Bank3 { get; } = new byte[256];
        public byte[] Bank13 { get; } = new byte[256];
        public byte[] Bank15 { get; } = new byte[256];
        public int GameMoment { get; set; }
        public bool LeaderRadius0 { get; set; }

        public FieldNavigationObjectReader Reader(
            Func<int, bool> isLineEnabled,
            Func<int, FieldNavigationTriggerLine?>? readLiveLine = null) => new(
            ReadInt32,
            ReadByte,
            _ => "Item",
            _ => "Materia",
            FieldNavigationObjectCatalog.CreateAllFields(),
            isLineEnabled,
            readLiveLine: readLiveLine);

        private int ReadInt32(int address) =>
            address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events : ReadByte(address) | ReadByte(address + 1) << 8;

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressFieldNumModels) return 1;
            if (address == Events + FieldNavigationNpcReader.CollisionRadiusOffset) return LeaderRadius0 ? (byte)0 : (byte)LeaderRadius;
            var offset = address - Base;
            return offset switch
            {
                0 => (byte)GameMoment,
                1 => (byte)(GameMoment >> 8),
                >= 0x100 and < 0x200 => Bank3[offset - 0x100],
                >= 0x300 and < 0x400 => Bank13[offset - 0x300],
                >= 0x400 and < 0x500 => Bank15[offset - 0x400],
                _ => 0
            };
        }
    }

    private static void Contains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Guide revisit interactions: {message}. Expected {expected} in {text}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Guide revisit interactions: {message}. Expected {expected}, got {actual}.");
        }
    }
}
