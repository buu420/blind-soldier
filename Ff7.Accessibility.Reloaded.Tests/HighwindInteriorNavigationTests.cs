using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>Ordinary Highwind doors and crew, independently of milestone Story targets.</summary>
internal static class HighwindInteriorNavigationTests
{
    private static readonly (string Id, string Label)[] DoorLabels =
    [
        ("gateway:66:0:74", "Enter Highwind corridor"),
        ("gateway:67:0:74", "Enter Highwind corridor"),
        ("script-exit:70:6:74", "Return to Highwind corridor"),
        ("gateway:72:0:74", "Return to Highwind corridor"),
        ("gateway:73:0:74", "Return to Highwind corridor"),
        ("script-exit:73:3:72", "Enter Highwind bridge"),
        ("gateway:74:0:66", "Go to Highwind outside deck"),
        ("gateway:74:1:73", "Enter Highwind operations room"),
        ("gateway:74:2:76", "Enter Highwind Chocobo hold"),
        ("script-exit:74:6:68,70,72", "Enter Highwind bridge"),
        ("script-exit:74:7:744", "Leave Highwind for Northern Crater"),
        ("gateway:75:0:66", "Go to Highwind outside deck"),
        ("gateway:75:1:73", "Enter Highwind operations room"),
        ("gateway:75:2:76", "Enter Highwind Chocobo hold"),
        ("gateway:76:0:74", "Return to Highwind corridor")
    ];

    public static void Run(Func<int, FieldWalkmeshReader>? createMesh = null)
    {
        RoomDoorsHaveDistinctNames();
        CrewNamesRespectLiveModels();
        OrdinaryCategoriesDoNotDependOnStory();
        if (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } root)
        {
            NativeDoorsAndCrewBindToTheInstalledArchive(root);
            NativeProgressionStillGatesTheBridgeAndCrater(root);
            NativeDeparturePublishesItsCurrentDestination(root);
            if (createMesh is not null) RoomRoutesUseNativeArrivals(root, createMesh);
        }
        Console.WriteLine("Highwind interior room, crew, Story coexistence and native route checks passed.");
    }

    private static FieldExitLabelResolver Labels() => new(
        _ => new FieldMapNameResolution(true, ["Highwind"]), () => "Highwind");

    private static FieldNavigationTarget Door(string id)
    {
        var parts = id.Split(':');
        return new(int.Parse(parts[1]), FieldNavigationCategory.Exits, "Exit", 0, 0, 0, id,
            TriggerEntityId: parts[0] == "script-exit" ? int.Parse(parts[2]) : -1,
            DestinationFieldIds: parts[3].Split(',').Select(int.Parse).ToArray());
    }

    private static void RoomDoorsHaveDistinctNames()
    {
        foreach (var (id, label) in DoorLabels)
            Equal(label, Labels().Resolve([Door(id)]).Single().Label, id + " names its visible destination");
        var corridor = Labels().Resolve(DoorLabels.Where(d => d.Id.StartsWith("gateway:74:") ||
            d.Id == "script-exit:74:6:68,70,72").Select(d => Door(d.Id)).ToArray());
        Equal(4, corridor.Select(t => t.Label).Distinct().Count(), "four rooms do not collapse to one Highwind label");
        Equal("Exit to Highwind", Labels().Resolve([Door("gateway:999:0:73")]).Single().Label,
            "unreviewed doors keep their ordinary resolution");
    }

    private static void CrewNamesRespectLiveModels()
    {
        foreach (var (field, entity, name, model, label) in new[]
                 {
                     (70, 17, "crew3", "rocket_crew1.char", "Pilot"),
                     (72, 16, "crew3", "rocket_crew1.char", "Pilot"),
                     (73, 12, "crew", "rocket_crew2.char", "Operations crew member"),
                     (76, 6, "crew", "rocket_crew2.char", "Chocobo handler")
                 })
        {
            var state = new State(1650);
            state.Show(entity);
            var definition = new FieldScriptNpcDefinition(field,entity,name,[9],ModelResourceName:model);
            var reader = state.Npcs(_ => [definition]);
            Equal(label, reader.ReadTargets(Position(field)).Single().Label, $"native {field}/{entity} has a usable role");
            state.Bytes[State.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset] = 1;
            Equal(0, reader.ReadTargets(Position(field)).Count, "non-talkable crew stay absent");
            state.Bytes[State.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset] = 0;
            state.Bytes[State.Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 0;
            Equal(0, reader.ReadTargets(Position(field)).Count, "hidden crew stay absent");
            state.Bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 255;
            Equal(0, reader.ReadTargets(Position(field)).Count, "no model means no crew target");
        }
    }

    private static void OrdinaryCategoriesDoNotDependOnStory()
    {
        foreach (var moment in new[] {1031,1650})
        {
            var state = new State(moment);
            state.Bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 22] = 8;
            var story = new FieldStoryTargetReader(state.ReadInt,state.ReadShort,state.ReadByte,
                FieldStoryEventCatalog.CreateAllFields(), _ => true);
            var exits = Labels().Resolve(DoorLabels.Where(d => d.Id.StartsWith("gateway:74:")).Select(d => Door(d.Id)).ToArray());
            var source = new FieldNavigationTargetSource([],storyTargetProvider: story.ReadTargets, exitTargetProvider: _ => exits);
            Equal(3,source.GetTargets(Position(74),FieldNavigationCategory.Exits).Count,
                $"ordinary corridor rooms at moment {moment}");
            var storyTargets = source.GetTargets(Position(74),FieldNavigationCategory.Story);
            Equal(moment == 1031,storyTargets.Count > 0,"native Story appears only when its chapter requires it");
            var controller = new FieldNavigationController(source);
            var spoken = controller.HandleAction(FieldNavigationAction.NextTarget,Position(74),new(0));
            True(spoken?.Speech.Contains("Highwind",StringComparison.Ordinal) == true,
                "the existing selector speaks an ordinary named room with or without Story");
        }
    }

    private static void NativeDoorsAndCrewBindToTheInstalledArchive(string root)
    {
        var catalog = new FieldScriptNavigationCatalog(root);
        foreach (var group in DoorLabels.GroupBy(d => int.Parse(d.Id.Split(':')[1])))
        {
            var exits = Gateways(root,group.Key).Concat(catalog.ReadField(group.Key).Exits).ToArray();
            foreach (var (id,label) in group)
            {
                var target = exits.SingleOrDefault(t => t.StableId == id);
                Equal(id,target.StableId,"the named door exists in the installed native archive");
                Equal(label,Labels().Resolve([target]).Single().Label,"native door is named through the production resolver");
                True(target.TriggerLine is not null,"door remains anchored to native gateway/LINE geometry");
            }
        }
        foreach (var (field,entity,label) in new[] {(70,17,"Pilot"),(72,16,"Pilot"),(73,12,"Operations crew member"),(76,6,"Chocobo handler")})
        {
            var state = new State(1650);
            state.Show(entity);
            var reader = state.Npcs(id => catalog.ReadField(id).Npcs);
            Equal(label,reader.ReadTargets(Position(field)).Single().Label,"installed crew binding publishes a named NPC");
            var talk = catalog.ReadScriptOpcodes(field,entity,1);
            True(talk.Any(o => o.Opcode is 0x40 or 0x48),"crew has native dialogue/choices");
            if (label == "Pilot") True(talk.Any(o => o.Opcode == 0x60),"pilot's own native Talk can take off");
            if (field == 73)
            {
                True(talk.Any(o => o.Opcode == 0x49 && o.Bytes[2] == 7),"operations crew opens native PHS");
                True(talk.Any(o => o.Opcode == 0x49 && o.Bytes[2] == 14),"operations crew opens native Save");
                True(talk.Any(o => o.Opcode == 0x3E),"operations crew restores HP/MP through the game");
            }
        }
    }

    private static void NativeProgressionStillGatesTheBridgeAndCrater(string root)
    {
        var native = new FieldScriptNavigationCatalog(root).ReadField(74);
        foreach (var (moment,destination) in new[] {(1100,70),(1198,70),(1199,72),(1300,72),(1613,72),(1614,68),(1615,72),(1650,72)})
        {
            var state = new State(moment);
            var exits = FieldScriptExitBranchPolicy.Resolve(74,moment,
                FieldScriptExitGuards.Apply(native.Exits,native.ExitGuards,state));
            var bridge = exits.Single(t => t.TriggerEntityId == 6);
            Equal(1,bridge.DestinationFieldIds!.Count,"one native bridge destination is offered at this moment");
            Equal(destination,bridge.DestinationFieldIds!.Single(),"native moment branch selects only the current bridge variant");
            Equal("Enter Highwind bridge",Labels().Resolve([bridge]).Single().Label,"bridge label survives native destination filtering");
            True(exits.All(t => t.TriggerEntityId != 7),"closed Northern Crater access is not invented");
            state.Bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + 91] = 0x80;
            True(FieldScriptExitGuards.Apply(native.Exits,native.ExitGuards,state).Any(t => t.TriggerEntityId == 7),
                "native flag opens the existing Northern Crater exit");
        }
        var script = new FieldScriptNavigationCatalog(root).ReadScriptOpcodes(74,6,2);
        foreach(var (constant,operation) in new[] {(1199,3),(1614,0)})
            True(script.Any(o => o.Opcode == 0x16 && o.Bytes[1] == 0x20 &&
                BitConverter.ToUInt16(o.Bytes.ToArray(),2) == 0 &&
                BitConverter.ToUInt16(o.Bytes.ToArray(),4) == constant && o.Bytes[6] == operation),
                "bridge policy boundaries match the installed native IFSW tests");
    }

    private static void NativeDeparturePublishesItsCurrentDestination(string root)
    {
        var native = new FieldScriptNavigationCatalog(root).ReadField(74);
        var gateways = Gateways(root,74);
        Equal(gateways.Single(t => t.StableId == "gateway:74:0:66").TriggerLine,
            native.Exits.Single(t => t.StableId == "script-exit:74:7:744").TriggerLine,
            "outside-deck gateway and guarded crater LINE occupy the same native doorway");
        var presentation = new FieldExitPresentationPolicy(() => false);
        foreach (var steamGatewayShape in new[] {false,true})
        {
            // The Steam coordinator retains gateway midpoints, without legacy LINE
            // geometry. Presentation must agree for both production target shapes.
            var runtimeGateways = steamGatewayShape
                ? gateways.Select(t => t with { TriggerLine = null, CompletesOnArrival = false }).ToArray()
                : gateways;
            foreach (var craterOpen in new[] {false,true})
            {
                var state = new State(1650);
                state.Bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + 91] = craterOpen ? (byte)0x80 : (byte)0;
                var scripts = FieldScriptExitBranchPolicy.Resolve(74,1650,
                    FieldScriptExitGuards.Apply(native.Exits,native.ExitGuards,state));
                var exits = presentation.Apply(Labels().Resolve(runtimeGateways.Concat(scripts).ToArray()));
                Equal(!craterOpen,exits.Any(t => t.StableId == "gateway:74:0:66"),
                    "combined Exits offers the outside deck only while the native doorway still leads there");
                Equal(craterOpen,exits.Any(t => t.StableId == "script-exit:74:7:744"),
                    "combined Exits offers Northern Crater only under the live native departure flag");
                Equal(4,exits.Count,"one doorway destination is replaced without losing other Highwind rooms");
                True(exits.Any(t => t.Label == "Enter Highwind operations room") &&
                    exits.Any(t => t.Label == "Enter Highwind Chocobo hold") &&
                    exits.Any(t => t.Label == "Enter Highwind bridge"),
                    "ordinary room navigation remains available in either departure state");
                var disabledLine = presentation.Apply(Labels().Resolve(runtimeGateways.Concat(
                    scripts.Where(t => t.TriggerEntityId != 7)).ToArray()));
                True(disabledLine.Any(t => t.StableId == "gateway:74:0:66"),
                    "an absent or disabled crater LINE cannot suppress the ordinary deck door");
            }
        }
    }

    private static void RoomRoutesUseNativeArrivals(string root,Func<int,FieldWalkmeshReader> createMesh)
    {
        var scripts = new FieldScriptNavigationCatalog(root);
        foreach (var (field,x,y,triangle) in new[]
                 {
                     (66,6,-782,8),(70,-369,-3911,14),(72,-369,-3911,14),
                     (73,0,-378,2),(74,22,151,29),(74,887,-810,46),(74,7,74,17),(74,872,-82,12),
                     (76,-120,-176,17)
                 })
        {
            var meshReader = createMesh(field);
            var native = scripts.ReadField(field);
            var position = new FieldPositionSnapshot(1,field,0,x,y,0,(ushort)triangle,0);
            var mesh = meshReader.Read(position).Walkmesh!;
            position = position with { Z = (int)mesh.Triangles[triangle].GetCentroid().Z };
            var planner = new FieldWalkmeshRoutePlanner(meshReader,transitionProvider: _ => native.Transitions);
            var exits = Labels().Resolve(Gateways(root,field).Concat(native.Exits)
                .Where(t => t.TriggerEntityId != 7 && !(field == 73 && t.TriggerEntityId == 3)).ToArray());
            True(exits.Count > 0,"ordinary native room has a return route");
            foreach (var exit in exits)
                True(planner.TryBuildRoute(position,exit,out _),$"native arrival {field}/{triangle} reaches {exit.Label}: {planner.LastDiagnostic}");
        }
    }

    private static IReadOnlyList<FieldNavigationTarget> Gateways(string root,int field)
    {
        var source = new FlevelDataSource(root);
        True(source.TryReadField(field,out var encoded),"installed field is present");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var start = BitConverter.ToInt32(bytes,6+7*4)+4;
        var state = new State(1650);
        state.Write(FieldPositionReader.AddressCurrentModule,1);
        state.Write(FieldPositionReader.AddressFieldId,field,2);
        state.Write(FieldNavigationControlReader.AddressFieldTriggersPtr,State.Triggers,4);
        state.Write(FieldGatewayTargetReader.AddressScriptContextPointer,State.Context,4);
        state.Write(State.Context+FieldGatewayTargetReader.GatewaysDisabledOffset,field == 70 ? 1 : 0);
        for(var i=0;i<FieldGatewayTargetReader.GatewayCount*FieldGatewayTargetReader.GatewayStride;i++)
            state.Bytes[State.Triggers+FieldGatewayTargetReader.GatewaysOffset+i] = bytes[start+FieldGatewayTargetReader.GatewaysOffset+i];
        return new FieldGatewayTargetReader(state).ReadTargets(Position(field));
    }

    private static FieldPositionSnapshot Position(int field) => new(1,field,0,0,0,0,0,0);
    private static void True(bool condition,string message) { if(!condition) throw new InvalidOperationException("Highwind interior: "+message); }
    private static void Equal<T>(T expected,T actual,string message) => True(EqualityComparer<T>.Default.Equals(expected,actual),$"{message}: expected {expected}, got {actual}");

    private sealed class State : ILegacyAddressSpace
    {
        public const int Events=0x02404000, Triggers=0x02406000, Context=0x02408000;
        public Dictionary<int,byte> Bytes {get;} = new();
        public State(int moment)
        {
            Write(FieldNavigationObjectReader.AddressFieldBankBase,moment,2);
            Write(FieldPositionReader.AddressFieldNumModels,2);
            Write(Events+FieldNavigationNpcReader.CollisionRadiusOffset,20,2);
            for(var i=0;i<256;i++) Bytes[FieldNavigationObjectReader.AddressFieldModelIdArray+i]=255;
        }
        public void Show(int entity)
        {
            Write(FieldNavigationObjectReader.AddressFieldModelIdArray+entity,1);
            Write(Events+FieldNavigationObjectReader.FieldEventDataStride+FieldNavigationObjectReader.VisibilityOffset,1);
            Write(Events+FieldNavigationObjectReader.FieldEventDataStride+FieldNavigationNpcReader.TalkRadiusOffset,40,2);
        }
        public void Write(int address,int value,int size=1) { for(var i=0;i<size;i++) Bytes[address+i]=(byte)(value>>(i*8)); }
        public byte ReadByte(int address) => Bytes.GetValueOrDefault(address);
        public short ReadShort(int address) => (short)(ReadByte(address)|(ReadByte(address+1)<<8));
        public int ReadInt(int address) => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events :
            ReadByte(address)|(ReadByte(address+1)<<8)|(ReadByte(address+2)<<16)|(ReadByte(address+3)<<24);
        public FieldNavigationNpcReader Npcs(Func<int,IReadOnlyList<FieldScriptNpcDefinition>> definitions) =>
            new(ReadInt,ReadShort,ReadByte,(_,_) => [],definitions);
        public bool TryRead(uint address,Span<byte> destination) { for(var i=0;i<destination.Length;i++) destination[i]=ReadByte((int)address+i); return true; }
        public bool IsReadable(uint address,int byteCount) => true;
    }
}
