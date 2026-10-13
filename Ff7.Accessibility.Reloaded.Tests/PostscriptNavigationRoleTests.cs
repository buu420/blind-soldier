using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
namespace Ff7.Accessibility.Reloaded.Tests;
internal static class PostscriptNavigationRoleTests
{
    internal static void Run()
    {
        var field=PartyField(null);
        var bytes=field.BuildFieldFile();var untouched=(byte[])bytes.Clone();
        var remapped=FieldScriptNavigationCatalog.ReadFieldFromBytes(900,"synthfld",bytes,0);
        Require(remapped.IsUsable,"Semantic role field must parse.");
        var tracker=new FieldScriptNavigationTransitionTracker(TimeSpan.Zero,()=>DateTime.UtcNow);
        Require(tracker.Resolve(900,remapped.Transitions,_=>true,e=>e==1,()=>4).Count==1,
            "The original HRC entity's copy must follow actual mapped party role4.");
        Require(tracker.Resolve(900,remapped.Transitions,_=>true,e=>e==0,()=>4).Count==0,
            "A matching party role cannot substitute for the actual controlled model.");
        Require(tracker.Resolve(900,remapped.Transitions,_=>true,e=>e==1,()=>0).Count==0,
            "Canonical ID0 cannot substitute for the mapped live leader4.");
        Require(bytes.AsSpan().SequenceEqual(untouched),"Semantic analysis must not mutate native input bytes.");
        var inequality=PartyField(c=>c.IfUb(3,9,0,4,1,"else").PrqEw(0,3).Ret()
            .Label("else").PrqEw(0,4).Ret());
        var native=FieldScriptNavigationCatalog.ReadFieldFromBytes(900,"synthfld",inequality.BuildFieldFile(),0);
        Require(Offered(native,4)=="22","Native !=4 stays raw: mapped actualleader4 must take its false branch.");
        Require(Offered(inequality.Read(),0).Split(',').Contains("11"),"Stock !=4 retains its existing ordinary true branch.");
        var equality=PartyField(c=>c.IfUb(3,9,0,0,0,"else").PrqEw(0,3).Ret()
            .Label("else").PrqEw(0,4).Ret());
        var eq=FieldScriptNavigationCatalog.ReadFieldFromBytes(900,"synthfld",equality.BuildFieldFile(),0);
        Require(Offered(eq,4)=="11","Native ==canonical0 maps RHS to4 while preserving actualleader read.");
        var seedOne=FieldScriptNavigationCatalog.ReadFieldFromBytes(900,"synthfld",field.BuildFieldFile(),1);
        Require(Offered(seedOne,1)=="11"&&Offered(seedOne,4)=="","Another seed must not reuse the preceding actual-role domain.");
        NativeModesAndGuardOperandsAreChecked();
    }

    private static void NativeModesAndGuardOperandsAreChecked()
    {
        var m=new Memory();m.Write((uint)FieldScriptContextReader.AddressCurrentModule,[1]);
        m.Write((uint)FieldScriptContextReader.AddressCurrentFieldId,BitConverter.GetBytes((ushort)900));
        m.Write((uint)FieldScriptContextReader.AddressFieldScriptPtr,BitConverter.GetBytes(0x500000u));
        m.Write(0x500000,[2,5,1,0,0,1,0,0]);m.Write(0xDC09E5,[4]);
        var loaded=new LoadedFieldScriptIdentityReader(m);
        Require(loaded.ReadGuard(new(3,9,false,false,0,4,true))==true,"Stock raw bank equality unchanged.");
        m.Write(0x631FCA,Convert.FromHexString("E8712238008945FC90908B4DFC"));
        Require(!loaded.TryReadNavigationMode(out _)&&loaded.ReadGuard(new(3,9,false,false,0,4,true))is null,
            "Requested remapping with unknown full native cohort must not reuse stock guards.");
        var fixture=Environment.GetEnvironmentVariable("BLIND_SOLDIER_POSTSCRIPT_CHARACTER_FIXTURE");
        if(string.IsNullOrWhiteSpace(fixture)||!File.Exists(fixture))
        {Console.WriteLine("Postscript navigation positive native integration skipped: private character fixture unavailable; semantic navigation and stock/unknown-mode guards ran.");return;}
        foreach(var line in File.ReadLines(fixture))
        {
            var parts=line.Split('=',2,StringSplitOptions.TrimEntries);
            if(parts.Length==2&&uint.TryParse(parts[0],System.Globalization.NumberStyles.HexNumber,null,out var address))
                m.Write(address,Convert.FromHexString(parts[1].Replace(" ",string.Empty,StringComparison.Ordinal)));
        }
        m.Write(0x753A59,Convert.FromHexString("8B550869D2658B585D83C201895508"));
        m.Write(0x753BE8,Convert.FromHexString("558BECA138A1E20083C001A338A1E200"));
        m.Write(0xDC0E08,BitConverter.GetBytes((ushort)0));
        Require(loaded.TryReadNavigationMode(out var seed)&&seed==0,"Verified native cohort admits real seed zero.");
        Require(loaded.ReadGuard(new(3,9,false,false,0,0,true))==true,
            "Byte equality compares actualleader4 against mapped canonical RHS0.");
        Require(loaded.ReadGuard(new(3,9,false,false,1,4,true))==false,
            "Byte inequality keeps literal4 raw, without mapping actual bank value.");
        Require(loaded.ReadGuard(new(3,9,false,false,1,0,true))==true,
            "Inequality against raw0 must not accidentally invert canonical mapping.");
        m.Write(0xDC09E6,[0]);
        Require(loaded.ReadGuard(new(3,9,true,false,0,4,true))==true,"Word comparator remains unpatched.");
        var reads=0;m.BeforeRead=address=>{if(address==0xDC0E08&&++reads==2)m.Write(address,BitConverter.GetBytes((ushort)1));};
        Require(!loaded.TryReadNavigationMode(out _),"Torn saved seed rejects semantic mode.");
        m.BeforeRead=null;m.Write(0xDC0E08,BitConverter.GetBytes((ushort)0));reads=0;
        m.BeforeRead=address=>{if(address==0x9B42A0&&++reads==2)m.Write(address,[0]);};
        Require(loaded.ReadGuard(new(3,9,false,false,0,0,true))is null,"Changed equality helper rejects guard, without stock fallback.");
    }

    private sealed class Memory:ILegacyAddressSpace
    {
        private readonly Dictionary<uint,byte> data=[];internal Action<uint>? BeforeRead;
        internal void Write(uint address,byte[] bytes){for(var i=0;i<bytes.Length;i++)data[address+(uint)i]=bytes[i];}
        public bool TryRead(uint address,Span<byte> destination){BeforeRead?.Invoke(address);for(var i=0;i<destination.Length;i++){if(!data.TryGetValue(address+(uint)i,out destination[i]))return false;}return true;}
    }
    private static string Offered(FieldScriptNavigationReadResult result,int leader) =>
        string.Join(",",new FieldScriptNavigationTransitionTracker(TimeSpan.Zero,()=>DateTime.UtcNow)
            .Resolve(900,result.Transitions,_=>true,e=>e==1,()=>leader).Select(t=>t.TargetTriangle).Order());
    private static SyntheticFieldScript PartyField(Action<SyntheticScriptCode>? go)
    {
        var f=new SyntheticFieldScript();var c=f.Code;
        c.Label("lineInit").Line(0,0,0,100,0,0).Ret().Label("lineMain").Ret().Label("lineOk").Ret().Label("go");
        if(go is null)c.PrqEw(0,3).Ret();else go(c);
        c.Label("personInit").Char(0).Pc(0).Ret().Label("personMain").Ret().Label("personIdle").Ret()
            .Label("person3").FieldJump(10,20,11,1).Ret().Label("person4").FieldJump(30,40,22,1).Ret();
        f.Entity("line",(0,"lineInit"),(1,"lineOk"),(4,"go"));
        f.Entity("person",(0,"personInit"),(1,"personIdle"),(3,"person3"),(4,"person4"));return f;
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
