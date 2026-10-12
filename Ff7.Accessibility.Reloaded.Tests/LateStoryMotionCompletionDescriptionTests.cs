using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class LateStoryMotionCompletionDescriptionTests
{
    private static readonly (int F, int E, int S, int B, string Bytes, int Model,
        string Hrc, int Animation, string Resource, int Frames)[] Expected =
    [
        (717,2,5,7,"BA0301",1,"AAGB.HRC",3,"EXFE.tak",38),
        (717,2,5,17,"BA0401",1,"AAGB.HRC",4,"EWHE.tak",160),
        (707,3,3,0,"A30601",0,"AAAA.HRC",6,"HSHB.tak",60),
        (707,4,3,0,"A30401",1,"AAGB.HRC",4,"AJFD.tak",30),
        (709,10,3,21,"BA0401",0,"AAAA.HRC",4,"HQEA.hei",15),
        (763,2,17,4,"BA1001",0,"AAAA.HRC",16,"BICA.yos",30),
        (768,5,3,0,"AF0301",0,"AAAA.HRC",3,"HZED.tak",68)
    ];
    public static void Run()
    {
        var cues = FieldLateStoryMotionCompletionDescriptions.CreateAll();
        Check(cues.Count == 7 && cues.Select(c=>c.Key).Distinct().Count()==7,"seven distinct visible-action boundaries");
        foreach(var e in Expected)
        {
            var key=new FieldCutsceneDescriptionKey(e.F,e.E,e.S,e.B);
            var cue=cues.Single(c=>c.Key==key);
            Check(cue.Opcode==Convert.ToByte(e.Bytes[..2],16) && !cue.IsRecurring && !cue.ObservationOnly,"executed native motion required");
            Check(FieldLateStoryMotionCompletionDescriptions.RequiresVisibleActor(key),"coherent actual loaded actor visibility required");
            var tracker=new FieldCutsceneDescriptionTracker(cues);
            var context=new FieldScriptContext(e.F,e.E,e.S,e.B,(byte)cue.Opcode);
            Check(tracker.Observe(context)?.Text==cue.Text,"first actual callback delivered");
            for(var duplicate=0;duplicate<240;duplicate++)Check(tracker.Observe(context) is null,"duplicate callbacks never replay the action");
            Check(!FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions().Any(c=>c.Key==key && c.Text!=cue.Text),"existing text never overwritten");
        }
        // These are concrete misleading alternatives found in the animation review:
        // an unplayed recovery, a nonblocking rotation immediately covered by fade,
        // and later animation segments already described by one combined sentence.
        foreach(var key in new FieldCutsceneDescriptionKey[]
            {new(768,7,3,17),new(706,5,15,436),new(707,4,3,3),new(709,10,3,27),new(709,10,3,33),new(763,2,17,17)})
            Check(!cues.Any(c=>c.Key==key),"unplayed/covered/duplicate neighbouring action remains silent");
        Console.WriteLine("Final late motion batch: native boundaries, visible-actor contracts and duplicate suppression passed.");
    }
    public static void RunAgainstInstalledArchive()
    {
        var root=Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if(string.IsNullOrEmpty(root)||!Directory.Exists(root)){Console.WriteLine("Final late motion archive unavailable; skipped.");return;}
        var source=new FlevelDataSource(root);var scripts=new FieldScriptNavigationCatalog(root);
        var characters=new LgpArchiveReader(Path.Combine(root,"data","field","char.lgp"));
        foreach(var e in Expected)
        {
            AssertBytes(scripts,e.F,e.E,e.S,e.B,e.Bytes);
            Check(scripts.ReadScriptOpcodes(e.F,e.E,0).Any(op=>op.Opcode==0xA1 && op.Bytes.Count==2 && op.Bytes[1]==e.Model),"native actor-resource binding");
            Check(source.TryReadField(e.F,out var encoded),"native field available");
            var model=LateGameMotionActionDescriptionTests.ReadModelBindings(Ff7LzsDecoder.DecodeFieldFile(encoded))[e.Model];
            Check(model.Hrc==e.Hrc && model.Animations[e.Animation]==e.Resource,"reviewed skeleton/animation at exact slot");
            Check(characters.TryReadFile(Path.ChangeExtension(e.Resource,"A"),out var animation)
                && animation.Length>=36 && BitConverter.ToInt32(animation,4)==e.Frames,"actual native A-file playback length");
        }
        // Blocking animations and explicit waits establish time to render; combined
        // descriptions pin every following played segment, not just an arbitrary ID.
        foreach(var a in new(int,int,int,int,string)[] {
            (717,2,5,10,"243C00"),(707,3,3,3,"A20701"),(707,3,3,20,"40020A"),
            (707,4,3,3,"A30501"),(707,4,3,6,"A30601"),
            (709,10,3,24,"241400"),(709,10,3,27,"BA0501"),(709,10,3,30,"241400"),(709,10,3,33,"A30601"),
            (763,2,17,17,"BC11001801"),(763,2,17,22,"A21201"),
            (768,5,3,19,"400006"),(768,5,3,38,"400005"),(768,5,3,57,"400007"),
            (768,14,3,139,"0305C3"),(707,1,1,326,"0303C3"),(707,1,1,365,"0304C3"),(763,0,0,552,"030251"),
            (768,7,3,17,"BC03000001"),(768,7,3,22,"A401"),(706,5,15,436,"AE0D01"),(706,5,15,439,"6B0000FFFFFF060600")})
            AssertBytes(scripts,a.Item1,a.Item2,a.Item3,a.Item4,a.Item5);
        Console.WriteLine($"Final late motion batch: seven script/resource/frame bindings and full played chains checked against {root}.");
    }
    private static void AssertBytes(FieldScriptNavigationCatalog scripts,int f,int e,int s,int b,string bytes)
    {
        Check(Convert.ToHexString(scripts.ReadScriptOpcodes(f,e,s).Single(op=>op.ByteIndex==b).Bytes.ToArray())==bytes,"exact played instruction boundary");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("Final late motion: "+message);}
}
