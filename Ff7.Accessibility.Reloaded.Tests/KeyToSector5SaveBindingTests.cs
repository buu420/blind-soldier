using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KeyToSector5SaveBindingTests
{
    internal static void Run()
    {
        var (knowledge,tracker,rooms,battle)=Create();
        tracker.ObserveNewGame();
        Check(!knowledge.HasClue,"native new-game observation must clear the Story clue along with the playthrough");
        foreach(var success in new[]{false,true})
        {
            (knowledge,tracker,rooms,battle)=Create();
            tracker.ObserveSaveMenu(Page(SaveMenuPage.Games,3,8),new(false,0));
            tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation,3,8),new(false,0));
            tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving,3,8),new(false,0));
            tracker.ObserveSaveMenu(null,new(true,success?NativeSaveResultPopupReader.SuccessTextIdentity:NativeSaveResultPopupReader.FailureTextIdentity));
            knowledge.History.BeginNewGame();knowledge.History.LoadSave(3,8);
            Check(knowledge.HasClue==success,"only the native successful save result may transfer Story knowledge to its destination");
        }
        (knowledge,tracker,rooms,battle)=Create();
        knowledge.History.SaveGame(2,11);knowledge.History.BeginNewGame();
        tracker.ObserveLoadMenu(2,11,false);
        Check(!knowledge.HasClue,"highlighting Continue must not bind the Story clue");
        tracker.ObserveLoadMenu(2,null,true);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveEnteredPlayableModule();
        Check(knowledge.HasClue,"the actual successful native Continue must bind the clue history");
        rooms.ResetActiveSave();battle.ResetActiveSave();
        Check(knowledge.HasClue,"room and battle description resets must not clear quest knowledge");
        (knowledge,tracker,rooms,battle)=Create();
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games,4,5),new(false,0));
        tracker.ObserveSaveMenu(Page(SaveMenuPage.SaveFiles,0,0),new(false,0));
        tracker.ObserveSaveMenu(null,new(true,NativeSaveResultPopupReader.SuccessTextIdentity));
        knowledge.History.BeginNewGame();knowledge.History.LoadSave(4,5);
        Check(!knowledge.HasClue,"a cancelled save cannot spend another slot's clue knowledge");
        Console.WriteLine("Key to Sector 5 native save/load/new-game binding and separate history reset checks passed.");
    }
    private static (KeyToSector5StoryKnowledge,FieldAreaDescriptionSaveTracker,FieldAreaDescriptionHistory,FieldAreaDescriptionHistory) Create()
    {
        var knowledge=new KeyToSector5StoryKnowledge(null);
        const string clue="I dropped the key during that excavation tour.";
        knowledge.ObserveVisibleClue(1,170,new[]{new FieldVisibleWindowSnapshot(0,1,clue,0x01010000)},true,
            knowledge.History.PlaythroughRevision,new[]{clue});
        Check(knowledge.HasClue,"the fixture must establish the actual clue first");
        var rooms=new FieldAreaDescriptionHistory(null);var battle=new FieldAreaDescriptionHistory(null);
        return (knowledge,new FieldAreaDescriptionSaveTracker(rooms,null,battle,knowledge.History),rooms,battle);
    }
    private static SaveMenuStateSnapshot Page(SaveMenuPage page,int file,int game)=>new(page,file,game,null,0);
    private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
}
