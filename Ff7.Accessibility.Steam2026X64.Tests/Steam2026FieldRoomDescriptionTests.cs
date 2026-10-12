using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldRoomDescriptionTests
{
    private static readonly DateTime Now = new(2026,10,12,12,0,0,DateTimeKind.Utc);
    public static void Run()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)777));
        var action = new FieldCutsceneDescriptionCue(777,0,0,4,"An action remains available.",FieldOpcodeAddressResolver.OpcodeWaitIndex);
        var film = new FieldCutsceneDescriptionCue(777,0,0,8,"A film remains available.",FieldOpcodeAddressResolver.OpcodeMovieIndex);
        var coordinator = new Steam2026FieldCutsceneDescriptionCoordinator(fixture.Direct,[action,film]);
        var history = new FieldAreaDescriptionHistory(null);
        coordinator.HistoryGate = new(history);
        void Movie(ushort active, byte background)
        {
            fixture.Write((uint)FieldAudibleCueStateReader.AddressFieldMovieActive,BitConverter.GetBytes(active));
            fixture.WriteByte(FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode,background);
        }
        Movie(1,0);
        coordinator.ObserveStableField(Now);
        coordinator.ObserveStableField(Now.AddSeconds(3));
        Check(false,coordinator.HasPendingNarration(777),"hidden observation-only room is never cold-start queued");
        Movie(1,1);
        coordinator.ObserveStableField(Now.AddSeconds(4));
        coordinator.ObserveStableField(Now.AddSeconds(7));
        Check(true,coordinator.HasPendingNarration(777),"visible background movie permits room arrival");
        Movie(1,0);
        Check(true,coordinator.Observe(new Steam2026FieldCutsceneIngressSnapshot(1,Now.AddSeconds(8),new(777,0,0,4,action.Opcode))),"native action queued behind held room");
        var spoken = new List<string>();
        Check(true,coordinator.TrySpeakPending(true,()=>false,text=>{spoken.Add(text);return true;},Now.AddSeconds(9),out var delivered),"action can pass held invisible room");
        Check(action.Text==delivered.Text,true,"actual action delivered");
        Check(false,history.HasHeard(777),"held invisible room does not spend playthrough history");
        Check(true,coordinator.Observe(new Steam2026FieldCutsceneIngressSnapshot(2,Now.AddSeconds(20),new(777,0,0,8,film.Opcode))),"native film queued behind held room");
        Check(true,coordinator.TrySpeakPending(true,()=>false,text=>{spoken.Add(text);return true;},Now.AddSeconds(60),out delivered),"film can pass held invisible room without film-tracks enabled");
        Check(film.Text==delivered.Text,true,"actual film description delivered");
        Check(false,history.HasHeard(777),"native film does not mark room heard");
        Check(false,coordinator.TrySpeakPending(true,()=>false,_=>true,Now.AddSeconds(120),out _),"invisible room remains held after actions");
        Movie(0,0);
        Check(true,coordinator.TrySpeakPending(true,()=>false,text=>{spoken.Add(text);return true;},Now.AddSeconds(180),out delivered),"room becomes eligible when actually visible");
        Check(true,FieldAreaDescriptionHistoryGate.IsRoomCue(delivered),"held room is delivered at visibility");
        Check(true,history.HasHeard(777),"only accepted visible room marks history");
        Check(3,spoken.Count,"rotation neither consumes nor duplicates cues");
        ManualRepeatRequiresVisibleRoom(fixture);
        LateMotionRequiresActualVisibleNativeActor();
        EchoLastmapMotionClassifiesCanonicalKeyButReadsLoadedActor();
        Console.WriteLine("PASS x64 room visibility, held queue rotation, action/movie delivery, history and manual repeat");
    }

    private static void LateMotionRequiresActualVisibleNativeActor()
    {
        foreach (var cue in FieldLateGameMotionActionDescriptions.CreateAll().Concat(FieldLifestreamMotionActionDescriptions.CreateAll()).Concat(FieldLateStoryMotionCompletionDescriptions.CreateAll()))
        {
            var fixture = FieldObservationFixture.CreatePopulated();
            fixture.Write((uint)FieldPositionReader.AddressFieldId,BitConverter.GetBytes((ushort)cue.FieldId));
            fixture.Write(FieldObservationFixture.ScriptPointer+2,[(byte)(cue.EntityId+1)]);
            fixture.WriteByte(FieldScriptControllerReader.AddressEntityModelIds+cue.EntityId,1);
            const uint objects=0x00090000;
            fixture.Write((uint)FieldScriptControllerReader.AddressModelTablePointer,BitConverter.GetBytes(objects));
            var body=objects+(uint)FieldScriptControllerReader.ModelStride;
            fixture.Write(body+FieldScriptControllerReader.ModelVisibilityOffset,[1]);
            fixture.Write(body+FieldScriptControllerReader.ModelAnimationIdOffset,[0]);
            fixture.Write(body+FieldPositionReader.ObjectXOffset,BitConverter.GetBytes(0));
            fixture.Write(body+FieldPositionReader.ObjectYOffset,BitConverter.GetBytes(0));
            fixture.Write(body+FieldPositionReader.ObjectZOffset,BitConverter.GetBytes(0));
            fixture.Write(body+FieldPositionReader.ObjectTriangleOffset,BitConverter.GetBytes((ushort)0));
            var wrapper=new TearingMemory(fixture,body+FieldScriptControllerReader.ModelVisibilityOffset);
            var coordinator=new Steam2026FieldCutsceneDescriptionCoordinator(wrapper,[cue]);
            var context=new FieldScriptContext(cue.FieldId,cue.EntityId,cue.ScriptId,cue.ByteIndex,cue.Opcode);
            fixture.Write(body+FieldScriptControllerReader.ModelVisibilityOffset,[0]);
            Check(false,coordinator.Observe(new(1,Now,context)),"hidden exact native actor cannot announce motion");
            fixture.Write(body+FieldScriptControllerReader.ModelVisibilityOffset,[1]);
            fixture.WriteByte(FieldPositionReader.AddressFieldNumModels,1);
            Check(false,coordinator.Observe(new(2,Now,context)),"unreadable actual entity-to-model binding cannot announce motion");
            fixture.WriteByte(FieldPositionReader.AddressFieldNumModels,2);
            wrapper.Tear=true;
            Check(false,coordinator.Observe(new(3,Now,context)),"torn native actor visibility cannot announce motion");
            fixture.Write(body+FieldScriptControllerReader.ModelVisibilityOffset,[1]);
            Check(true,coordinator.Observe(new(4,Now,context)),"later coherent visible actor retains unspent motion cue");
            Check(true,coordinator.TrySpeakPending(true,()=>false,_=>true,Now,out var delivered),"actual visible motion delivers");
            Check(cue.Text,delivered.Text,"delivered body motion follows actual native actor");
        }
        Console.WriteLine("PASS every new motion cue gates actual native entity-to-model visibility without spending hidden/torn keys");
    }

    private static void EchoLastmapMotionClassifiesCanonicalKeyButReadsLoadedActor()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Write((uint)FieldPositionReader.AddressFieldId,BitConverter.GetBytes((ushort)768));
        fixture.Write(FieldObservationFixture.ScriptPointer+2,[6]);
        fixture.WriteByte(FieldScriptControllerReader.AddressEntityModelIds+4,1);
        fixture.WriteByte(FieldScriptControllerReader.AddressEntityModelIds+5,0);
        const uint objects=0x00090000;
        fixture.Write((uint)FieldScriptControllerReader.AddressModelTablePointer,BitConverter.GetBytes(objects));
        var actualBody=objects+(uint)FieldScriptControllerReader.ModelStride;
        foreach (var body in new[] {objects,actualBody})
        {
            fixture.Write(body+FieldScriptControllerReader.ModelVisibilityOffset,[1]);
            fixture.Write(body+FieldScriptControllerReader.ModelAnimationIdOffset,[0]);
            fixture.Write(body+FieldPositionReader.ObjectXOffset,BitConverter.GetBytes(0));
            fixture.Write(body+FieldPositionReader.ObjectYOffset,BitConverter.GetBytes(0));
            fixture.Write(body+FieldPositionReader.ObjectZOffset,BitConverter.GetBytes(0));
            fixture.Write(body+FieldPositionReader.ObjectTriangleOffset,BitConverter.GetBytes((ushort)0));
        }
        var identity=new LoadedFieldScriptIdentity(768,EchoSCompatibilityManifest.GetEchoFingerprint(768)!);
        var canonical=new FieldCutsceneDescriptionKey(768,5,3,0);
        var context=new FieldScriptContext(768,4,3,0,FieldOpcodeAddressResolver.OpcodeAnimOnceIndex);
        Check(canonical,EchoSCompatibilityManifest.ResolveCanonicalCueKey(context,identity)!.Value,"reviewed Echo Cloud4 classifies as canonicalCloud5");
        Check(true,EchoSCompatibilityManifest.ResolveCanonicalCueKey(context,identity with { ScriptPrefixSha256=new string('0',64) }) is null,"unknown prefix cannot claim a canonical motion identity");
        var coordinator=new Steam2026FieldCutsceneDescriptionCoordinator(fixture.Direct,FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions(),useLoadedScriptIdentity:true,readIdentity:()=>identity);
        fixture.Write(actualBody+FieldScriptControllerReader.ModelVisibilityOffset,[0]);
        Check(false,coordinator.Observe(new(1,Now,context)),"visible decoy entity5 cannot authorize hidden actualEchoCloud4");
        fixture.Write(actualBody+FieldScriptControllerReader.ModelVisibilityOffset,[1]);
        fixture.Write(objects+FieldScriptControllerReader.ModelVisibilityOffset,[0]);
        Check(true,coordinator.Observe(new(2,Now,context)),"hidden decoy entity5 cannot suppress visible actualEchoCloud4");
        Check(true,coordinator.TrySpeakPending(true,()=>false,_=>true,Now,out var delivered),"actual reviewedEchoCloud motion delivered");
        Check(4,delivered.EntityId,"delivered key retains actual loaded actor rather than canonical entity");
        Check("Cloud gets to his feet.",delivered.Text,"actualEchoCloud receives the reviewed canonical motion");
        Console.WriteLine("PASS Echo lastmap Cloud5->4 canonical classification and actual actor visibility");
    }

    private sealed class TearingMemory(FieldObservationFixture fixture,uint visibility) : ILegacyAddressSpace
    {
        internal bool Tear;
        public bool TryRead(uint address,Span<byte> destination)
        {
            var result=fixture.Direct.TryRead(address,destination);
            if(result && Tear && address==visibility){fixture.Write(visibility,[0]);Tear=false;}
            return result;
        }
    }

    private static void ManualRepeatRequiresVisibleRoom(FieldObservationFixture fixture)
    {
        foreach (var sample in new[] { (Movie:(ushort)0, Background:(byte)0, Visible:true), (Movie:(ushort)1, Background:(byte)0, Visible:false), (Movie:(ushort)1, Background:(byte)1, Visible:true) })
        {
            fixture.Write((uint)FieldAudibleCueStateReader.AddressFieldMovieActive,BitConverter.GetBytes(sample.Movie));
            fixture.WriteByte(FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode,sample.Background);
            var room = new FieldRoomPresentationReader(fixture.Direct);
            var position = new FieldPositionSnapshot(1,777,0,0,0,0,0,0);
            var target = new FieldNavigationTarget(777,FieldNavigationCategory.Story,"Current target",0,0,0,"story:777:room-test");
            var route = new Steam2026FailClosedFieldRoutePlanner(new NoRoute());
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]),route);
            controller.HandleAction(FieldNavigationAction.NextCategory,position,controlTransform:null);
            route.BeginObservation();
            var pending = new Steam2026FieldNavigationPendingActionBuffer();pending.Capture([FieldNavigationAction.RepeatTarget]);
            var coherence = new Steam2026FieldNavigationDomainCoherence(true,true,true,true,true);
            Check(true,Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(pending,777,controller,route,position,new(0),FieldLadderStateSnapshot.NotMounted,ref coherence,out _,out var result,areaKnown:true,roomVisible:room.IsRoomVisible(777)),"repeat preserves ordinary target speech");
            var description = FieldAreaDescriptionStatus.Describe(777)!;
            Check(sample.Visible,result!.Value.Speech.Contains(description,StringComparison.Ordinal),"manual repeat appends room only at actual visibility");
        }
    }

    private static void Check<T>(T expected,T actual,string message)
    { if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException(message+$": expected {expected}; actual {actual}"); }

    private sealed class NoRoute : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "No route in isolated status fixture";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position,out int triangle){triangle=0;return true;}
        public bool TryBuildRoute(FieldPositionSnapshot position,FieldNavigationTarget target,out FieldNavigationRoutePlan plan){plan=default!;return false;}
        public bool TryGetNextWaypoint(FieldPositionSnapshot position,FieldNavigationTarget target,out FieldNavigationRouteWaypoint waypoint){waypoint=default;return false;}
    }
}
