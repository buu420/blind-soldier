using Ff7.Accessibility.Reloaded;
namespace Ff7.Accessibility.Reloaded.Tests;
internal static class CreatureWarningSpeechGateTests
{
    internal static void Run()
    {
        const string vanilla="25A95C3A8CEEB6EC00B6E04089276ED80657816BB41ED4FDA883D8F2EC05825B";
        var identity=new LoadedFieldScriptIdentity(767,0x100000,vanilla);
        FieldCreatureWarning? current=new(identity,10,0x200000,"Cloud\nIt's coming!!");
        var gate=new FieldCreatureWarningSpeechGate(()=>current);
        Check(!gate.IsCompleted(false),"idle without delivery does not prove completion");
        gate.NotePrismAccepted(1,current.Value.Text);
        Check(!gate.IsCompleted(false),"backend idle before accepting queued speech is not completion");
        Check(!gate.IsCompleted(true),"active original warning blocks description");
        Check(!gate.IsCompleted(null),"unknown backend state blocks description");
        Check(gate.IsCompleted(false),"accepted page with observed speech ending permits description");
        current=current.Value with{WindowPointer=0x200100};
        Check(!gate.IsCompleted(false),"replaced window cannot inherit speech completion");
        current=current.Value with{Identity=new LoadedFieldScriptIdentity(767,0x100100,vanilla)};
        Check(!gate.IsCompleted(false),"reloaded script cannot inherit speech completion");
        current=null;Check(!gate.IsCompleted(false),"missing warning expires completion");
        current=new(identity,10,0x200000,"Cloud\nIt's coming!!");
        Check(!gate.IsCompleted(false),"a returning warning still needs fresh acceptance");
        gate.NotePrismAccepted(0,current.Value.Text);gate.NotePrismAccepted(1,"Jenova?");
        Check(!gate.IsCompleted(true)&&!gate.IsCompleted(false),"wrong window or text never establishes completion");
        gate.NotePrismAccepted(1,current.Value.Text);gate.IsCompleted(true);gate.Reset();
        Check(!gate.IsCompleted(false),"suspend reset invalidates completion");
        Console.WriteLine("PASS native warning speech acceptance, actual backend completion and replacement/reset guards");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
