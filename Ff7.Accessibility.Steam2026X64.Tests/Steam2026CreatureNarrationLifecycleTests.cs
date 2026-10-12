using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026CreatureNarrationLifecycleTests
{
    internal static void Run()
    {
        foreach(var boundary in new[]{(Enabled:false,Foreground:true),(Enabled:true,Foreground:false),(Enabled:true,Foreground:true)})
        foreach(var owner in new[]{CutsceneVoiceOwner.FieldCreature,CutsceneVoiceOwner.FieldAction})
        {
            var fixture=FieldObservationFixture.CreatePopulated();
            var device=new Device();
            using var voice=new CutsceneVoicePlayer(CutsceneVoiceManifest.Parse(
                "{\"entries\":[{\"text\":\"test\",\"file\":\"test.ogg\",\"duration_seconds\":2}]}"),_=>device,_=>{});
            var coordinator=new Steam2026FieldCutsceneDescriptionCoordinator(fixture.Direct,[],cutsceneVoice:voice);
            if(voice.TrySpeak("test",owner,out _)!=CutsceneVoiceResult.Played)throw new InvalidOperationException("Test device refused.");
            coordinator.ObserveCreatureLifetime(boundary.Enabled,boundary.Foreground);
            if(device.IsPlaying!=(owner==CutsceneVoiceOwner.FieldAction))
                throw new InvalidOperationException("Creature configuration/focus/field lifetime did not stop only its owned clip.");
        }
        Console.WriteLine("PASS x64 creature configuration, menu ownership, focus and field lifetime preserve other descriptions");
    }
    private sealed class Device:IFieldMovieNarrationOutput
    {
        public bool IsPlaying{get;private set;}
        public bool Start(string reason){IsPlaying=true;return true;}
        public bool Stop(string reason){IsPlaying=false;return true;}
        public void Dispose()=>IsPlaying=false;
    }
}
