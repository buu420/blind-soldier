using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KeyToSector5StoryKnowledgeTests
{
    private const string StockClue = "That's funny... I dropped the key to this gate somewhere. Must've been on that excavation tour.";
    private const string EchoClue = "That's odd. Where's my key? Did I drop it somewhere? Maybe during that excavation tour...";
    internal static void Run()
    {
        foreach (var text in new[] { StockClue, EchoClue,
            "Komisch... Hab' ich den Schlüssel für das Tor verloren? Vielleicht während der Ausgrabung." })
        {
            var state = new KeyToSector5StoryKnowledge(null);
            Check(!state.HasClue, "story progress alone must not invent the clue");
            Feed(state, text, expectedText:text);
            Check(state.HasClue, "the actual stock/Echo visible clue must establish Story knowledge");
        }
        RejectOtherFrames();
        SaveIsolation();
        RejectOldRevision();
        Console.WriteLine("Key to Sector 5 visible clue and per-save knowledge checks passed.");
    }
    private static void Feed(KeyToSector5StoryKnowledge state, string text,
        int module=1, int field=170, int window=0, bool reviewed=true, byte nativeState=1, uint pointer=0x01010000,
        string expectedText=StockClue)
    {
        state.ObserveVisibleClue(module,field,
            new[] { new FieldVisibleWindowSnapshot(window,nativeState,text,pointer) },
            reviewed,state.History.PlaythroughRevision,new[]{expectedText});
    }
    private static void RejectOtherFrames()
    {
        foreach(var kind in Enumerable.Range(0,8))
        {
            var state = new KeyToSector5StoryKnowledge(null);
            switch(kind)
            {
                case 0: Feed(state,StockClue,module:5); break;
                case 1: Feed(state,StockClue,field:115); break;
                case 2: Feed(state,StockClue,window:1); break;
                case 3: Feed(state,StockClue,reviewed:false); break;
                case 4: Feed(state,StockClue,nativeState:FieldMessageReader.FreeWindowState); break;
                case 5: Feed(state,StockClue,pointer:0); break;
                case 6: Feed(state,"President Shinra: We'll destroy Sector 7."); break;
                case 7: Feed(state,"I found a key in the shop."); break;
            }
            Check(!state.HasClue,"unrelated, unsupported or retired native window must not establish the clue");
        }
    }
    private static void SaveIsolation()
    {
        var state=new KeyToSector5StoryKnowledge(null);
        state.History.LoadSave(2,4);
        Feed(state,StockClue);
        state.History.LoadSave(2,5);
        Check(!state.HasClue,"another loaded slot cannot inherit this clue");
        state.History.LoadSave(2,4);
        Check(state.HasClue,"the slot that actually showed the clue must retain it");
        state.History.BeginNewGame();
        Check(!state.HasClue,"new game must clear current knowledge");
        Feed(state,EchoClue,expectedText:EchoClue);
        state.History.SaveGame(3,8);
        state.History.BeginNewGame();
        state.History.LoadSave(3,8);
        Check(state.HasClue,"saving an unsaved playthrough must carry its observed clue");
        state.History.BeginNewGame();
        state.History.SaveGame(3,8);
        state.History.LoadSave(3,8);
        Check(!state.HasClue,"overwritten save must replace its old clue history");
    }
    private static void RejectOldRevision()
    {
        var state=new KeyToSector5StoryKnowledge(null);
        var revision=state.History.PlaythroughRevision;
        state.History.LoadSave(4,3);
        state.ObserveVisibleClue(1,170,new[] {new FieldVisibleWindowSnapshot(0,1,StockClue,0x01010000)},true,revision,new[]{StockClue});
        Check(!state.HasClue,"a delayed observation from an old playthrough must not mark a new save");
    }
    private static void Check(bool ok,string why) {if(!ok)throw new InvalidOperationException(why);}
}
