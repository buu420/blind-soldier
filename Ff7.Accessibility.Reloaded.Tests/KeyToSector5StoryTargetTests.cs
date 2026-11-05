using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KeyToSector5StoryTargetTests
{
    internal static void Run()
    {
        TheForemanIsAStoryStepOnlyAfterTheClue();
        EveryPendingResultIsCollectedBeforeAnotherDig();
        EitherNativeKeyFlagRetiresTheErrand();
        DigTargetsUseNativeKeyTrianglesIndependentlyOfTheWorkersRoll();
        OtherPhasesAndUnreadablePlacementOfferNoDigTarget();
        ChangedStateOrSaveDoesNotPublishAnOldTarget();
        Console.WriteLine("Key to Sector 5 clue-backed foreman, exact dig sites and results Story stages passed.");
    }

    private static KeyToSector5StoryKnowledge Known()
    {
        var state=new KeyToSector5StoryKnowledge(null);
        const string text="That's funny... I dropped the key to this gate somewhere. Must've been on that excavation tour.";
        state.ObserveVisibleClue(1,170,new[]{new FieldVisibleWindowSnapshot(0,1,text,0x01010000)},true,
            state.History.PlaythroughRevision,new[]{text});
        Check(state.HasClue,"the fixture must first show the native clue");
        return state;
    }

    private static KeyToSector5StoryTargetReader Reader(Scene scene, KeyToSector5StoryKnowledge? knowledge=null,
        Func<int,bool>? reviewed=null) =>
        new(scene.Int,scene.Short,scene.Byte,knowledge??Known(),reviewed??(_=>true));

    private static void TheForemanIsAStoryStepOnlyAfterTheClue()
    {
        var scene=Scene.Village(0);
        var knowledge=new KeyToSector5StoryKnowledge(null);
        Check(Reader(scene,knowledge).ReadTargets(scene.Position).Count==0,"progress alone cannot offer the key quest");
        var targets=Reader(scene).ReadTargets(scene.Position);
        Check(targets.Count==1 && targets[0].TriggerEntityId==4,
            "the observed clue must offer the native excavation foreman in Story");
        Check(targets[0].Category==FieldNavigationCategory.Story && targets[0].Activation==FieldNavigationActivation.Talk,
            "the foreman step must use normal Talk reach and facing");
        scene.Moment(1195);
        Check(Reader(scene).ReadTargets(scene.Position).Count==0,"the native key gate is strictly above1195");
        scene.Moment(1196);
        Check(Reader(scene,reviewed:_=>false).ReadTargets(scene.Position).Count==0,"unreviewed field layouts stay unavailable");
        scene.ModelByte(4,FieldNavigationNpcReader.TalkDisabledOffset,1);
        Check(Reader(scene).ReadTargets(scene.Position).Count==0,"a Talk-disabled foreman cannot be offered");
    }

    private static void EveryPendingResultIsCollectedBeforeAnotherDig()
    {
        for(var result=1;result<=9;result++)
        {
            var scene=Scene.Village(result);
            var targets=Reader(scene).ReadTargets(scene.Position);
            Check(targets.Count==1 && targets[0].TriggerEntityId==13,
                "any pending native result must point to the chest before another dig can overwrite it");
            Check(targets[0].Label == "Check the excavation results",
                "a closed results chest must not promise the unknown reward");
            Check(targets[0].Activation==FieldNavigationActivation.Talk,"results use the native Talk interaction");
        }
        var emptyModel=Scene.Village(5);
        emptyModel.ModelByte(13,FieldNavigationObjectReader.VisibilityOffset,0);
        Check(Reader(emptyModel).ReadTargets(emptyModel.Position).Count==0,"an invisible chest is not a target");
    }

    private static void EitherNativeKeyFlagRetiresTheErrand()
    {
        foreach(var field in new[]{617,772})foreach(var held in new[]{false,true})
        {
            var scene=field==617?Scene.Village(5):Scene.Dig(1,3,false);
            scene.Bank(held?1:15,held?67:38,held?(byte)0x20:(byte)0x08);
            Check(Reader(scene).ReadTargets(scene.Position).Count==0,"inventory or one-time reward ownership retires every stage");
        }
    }

    private static void DigTargetsUseNativeKeyTrianglesIndependentlyOfTheWorkersRoll()
    {
        foreach(var request in new[]{2,3})foreach(var roll in new[]{0,59,60,119,120,179,180,255})foreach(var upper in new[]{false,true})
        {
            var scene=Scene.Dig(1,request,upper);
            scene.Temp(15,(byte)roll);
            var targets=Reader(scene).ReadTargets(scene.Position);
            Check(targets.Count==1,"the key search must offer a deterministic native site after the blast");
            var target=targets[0];
            Check(target.Category==FieldNavigationCategory.Story && target.CompletesOnArrival,
                "the site must be a routed Story location, leaving the native choice button to the player");
            Check(target.CompletionTriangles is {Count:1} triangles &&
                (upper?triangles[0] is 8 or 12:triangles[0]==60),"a key site completes on its exact native result triangle on the current level");
        }
    }

    private static void OtherPhasesAndUnreadablePlacementOfferNoDigTarget()
    {
        foreach(var phase in new[]{0,2,3,4,5,6,7,8})
        {
            var scene=Scene.Dig(phase,3,false);
            Check(Reader(scene).ReadTargets(scene.Position).Count==0,"placement and blast remain under the existing activity controls");
        }
        var climbing=Scene.Dig(1,3,false);climbing.Temp(43,1);
        Check(Reader(climbing).ReadTargets(climbing.Position).Count==0,"native climbing must finish before a dig choice is targeted");
        var torn=Scene.Dig(1,3,false);
        foreach(var site in BoneVillageDigSpot.Sites.Where(site=>site.DigResult is >=5 and <=7))torn.TempWord(site.BankSlot,999);
        Check(Reader(torn).ReadTargets(torn.Position).Count==0,"unverified GETAI marker triangles cannot invent key sites");
    }

    private static void ChangedStateOrSaveDoesNotPublishAnOldTarget()
    {
        var changing=Scene.Village(0);
        changing.ChangePending=true;
        Check(Reader(changing).ReadTargets(changing.Position).Count==0,"a mid-read pending result change must not publish an old stage");
        var scene=Scene.Village(0);var knowledge=Known();
        scene.DuringModel=()=>knowledge.History.LoadSave(4,5);
        Check(Reader(scene,knowledge).ReadTargets(scene.Position).Count==0,"a different save cannot inherit an in-flight Story target");
    }

    private static void Check(bool ok,string why) {if(!ok)throw new InvalidOperationException(why);}

    private sealed class Scene
    {
        private const int Table=0x02600000;
        private readonly Dictionary<int,int> ints=[];
        private readonly Dictionary<int,byte> bytes=[];
        private int pendingReads;
        public bool ChangePending;
        public Action? DuringModel;
        public FieldPositionSnapshot Position;
        public static Scene Village(int result)
        {
            var s=new Scene(617);
            s.Position=new(1,617,0,-213,-346,-27,84,0);
            s.Bank(1,234,(byte)result);
            s.Model(4,-283,63,18,28);s.Model(13,-327,-209,-27,130);
            return s;
        }
        public static Scene Dig(int phase,int request,bool upper)
        {
            var s=new Scene(772);s.Position=new(1,772,0,upper?170:-214,upper?514:0,upper?328:-110,(ushort)(upper?99:105),0);
            s.Temp(12,(byte)phase);s.Bank(1,235,(byte)request);
            foreach(var site in BoneVillageDigSpot.Sites){s.Model(site.EntityId,site.X,site.Y,site.Z,site.Triangle);s.TempWord(site.BankSlot,site.Triangle);}
            return s;
        }
        private Scene(int field)
        {
            ints[FieldNavigationObjectReader.AddressFieldEventDataPtr]=Table;
            bytes[FieldPositionReader.AddressCurrentModule]=1;
            Word(FieldPositionReader.AddressFieldId,field);
            bytes[FieldPositionReader.AddressFieldNumModels]=20;
            for(var i=0;i<20;i++)bytes[FieldNavigationObjectReader.AddressFieldModelIdArray+i]=(byte)i;
            ModelWord(0,FieldNavigationNpcReader.CollisionRadiusOffset,80);Moment(1196);
        }
        public void Moment(int value)=>Word(FieldNavigationObjectReader.AddressFieldBankBase,value);
        public void Bank(int bank,int index,byte value)=>bytes[FieldNavigationObjectReader.AddressFieldBankBase+(bank==15?0x400:0)+index]=value;
        public void Temp(int index,byte value)=>bytes[FieldNavigationObjectReader.AddressTemporaryFieldBankBase+index]=value;
        public void TempWord(int index,int value)=>Word(FieldNavigationObjectReader.AddressTemporaryFieldBankBase+index,value);
        private void Word(int address,int value){bytes[address]=(byte)value;bytes[address+1]=(byte)(value>>8);}
        public void ModelByte(int model,int offset,byte value)=>bytes[Table+model*FieldNavigationObjectReader.FieldEventDataStride+offset]=value;
        private void ModelWord(int model,int offset,int value)=>Word(Table+model*FieldNavigationObjectReader.FieldEventDataStride+offset,value);
        private void Model(int model,int x,int y,int z,int triangle)
        {
            var a=Table+model*FieldNavigationObjectReader.FieldEventDataStride;
            ints[a+FieldNavigationObjectReader.PositionXOffset]=x*4096;ints[a+FieldNavigationObjectReader.PositionYOffset]=y*4096;ints[a+FieldNavigationObjectReader.PositionZOffset]=z*4096;
            ModelByte(model,FieldNavigationObjectReader.VisibilityOffset,1);ModelWord(model,FieldPositionReader.ObjectTriangleOffset,triangle);
            ModelWord(model,FieldNavigationNpcReader.TalkRadiusOffset,34);
        }
        public int Int(int address){if(address>=Table){var action=DuringModel;DuringModel=null;action?.Invoke();}return ints.GetValueOrDefault(address);}
        public short Short(int address)=>(short)(Byte(address)|(Byte(address+1)<<8));
        public byte Byte(int address)
        {
            if(ChangePending && address==FieldNavigationObjectReader.AddressFieldBankBase+234 && pendingReads++>0)return 5;
            return bytes.GetValueOrDefault(address);
        }
    }
}
