using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KeyToSector5ClueObservationTests
{
    private const string Clue="Where's my key? Did I drop it somewhere? Maybe during that excavation tour...";
    internal static void Run()
    {
        ResolveTheReviewedNpcOwnMessage();
        var memory=Memory.WithClue();
        var knowledge=new KeyToSector5StoryKnowledge(null);
        var fixtureReader=new FieldMessageReader(memory);
        var tableOk=fixtureReader.TryReadMessagePagesById(23,out var fixturePages);
        var windowsOk=fixtureReader.TryReadVisibleWindows(out var fixtureWindows);
        Check(tableOk && windowsOk,"the native fixture must contain a readable message table and window");
        Check(new KeyToSector5ClueObservationReader(memory,_=>23).Observe(knowledge),
            "a coherent actually displayed native clue must be recorded without dialogue hooks or speech");
        Check(knowledge.HasClue,"the native-window adapter must feed the real clue history");
        foreach(var failure in Enumerable.Range(0,7))
        {
            memory=Memory.WithClue(); knowledge=new KeyToSector5StoryKnowledge(null);
            var reviewedCalls=0;
            Func<int,int?> reviewed=_=>failure!=0 && (failure!=6 || ++reviewedCalls==1) ? 23 : null;
            switch(failure)
            {
                case 1: memory.FailText=true; break;
                case 2: memory.ChangeField=true; break;
                case 3: memory.ChangeText=true; break;
                case 4: memory.DuringText=()=>knowledge.History.LoadSave(2,5); break;
                case 5: memory.Byte(FieldMessageReader.AddressFieldWindowStates,FieldMessageReader.FreeWindowState); break;
            }
            Check(!new KeyToSector5ClueObservationReader(memory,reviewed).Observe(knowledge),
                "unreviewed, unreadable, torn, retired or old-save native clue observations must be rejected");
            Check(!knowledge.HasClue,"a rejected frame must not contaminate the clue history");
        }
        Console.WriteLine("Key to Sector 5 checked native-window clue observations passed (8 cases).");
    }
    private static void ResolveTheReviewedNpcOwnMessage()
    {
        foreach(var id in new[]{141,23})
        {
            var npc=new FieldScriptNpcDefinition(170,7,"gman",new[]{id}) {OwnDialogIds=new[]{id}};
            var field=new FieldScriptNavigationReadResult(true,[],[],new[]{npc},[],"native fixture");
            Check(KeyToSector5ClueObservationReader.ResolveReviewedClueMessageId(field)==id,
                "reviewed stock/Echo clue must use the man's own native message");
            foreach(var rejected in new[]{
                field with {IsUsable=false},
                field with {Npcs=new[]{npc with {OwnDialogIds=null}}},
                field with {Npcs=new[]{npc with {OwnDialogIds=new[]{141,23}}}},
                field with {Npcs=new[]{npc with {FieldId=115}}},
                field with {Npcs=new[]{npc with {EntityId=8}}}})
                Check(KeyToSector5ClueObservationReader.ResolveReviewedClueMessageId(rejected) is null,
                    "unknown layouts and unrelated or ambiguous NPC messages cannot establish the clue");
        }
    }
    private static void Check(bool ok,string why) {if(!ok)throw new InvalidOperationException(why);}
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint,byte> bytes=[];
        private int fieldReads,textReads;
        public List<string> Misses=[];
        public bool FailText,ChangeField,ChangeText;
        public Action? DuringText;
        public static Memory WithClue()
        {
            var m=new Memory();
            m.Byte(FieldPositionReader.AddressCurrentModule,1);
            m.Word(FieldPositionReader.AddressFieldId,170);
            m.Dword(FieldMessageReader.AddressFieldMessageDataPointer,0x01010000);
            for(var i=0;i<0x1000;i++)m.Byte(0x01010000+i,0xff);
            for(var i=0;i<50;i++)m.Byte(0x01010000+i,0);
            m.Word(0x01010000,24);
            m.Word(0x01010000+2+23*2,0x200);
            for(var i=0;i<FieldMessageReader.FieldTextBufferLength;i++)m.Byte(0x01010200+i,0xff);
            for(var w=0;w<FieldMessageReader.WindowCount;w++)
            {
                m.Byte(FieldMessageReader.AddressFieldWindowStates+w,w==0?(byte)1:FieldMessageReader.FreeWindowState);
                m.Dword(FieldMessageReader.AddressFieldWindowMessagePointers+w*4,w==0?0x01010200u:0u);
                for(var i=0;i<FieldMessageReader.FieldTextBufferLength;i++)
                    m.Byte(FieldMessageReader.AddressFieldWindowTextBuffers+w*FieldMessageReader.WindowTextBufferStride+i,0xff);
            }
            for(var i=0;i<Clue.Length;i++)
            {
                m.Byte(FieldMessageReader.AddressFieldWindowTextBuffers+i,(byte)(Clue[i]-32));
                m.Byte(0x01010200+i,(byte)(Clue[i]-32));
            }
            return m;
        }
        public void Byte(int a,byte v)=>bytes[(uint)a]=v;
        private void Word(int a,ushort v) {Byte(a,(byte)v);Byte(a+1,(byte)(v>>8));}
        private void Dword(int a,uint v) {for(var i=0;i<4;i++)Byte(a+i,(byte)(v>>(i*8)));}
        public bool TryRead(uint address,Span<byte> destination)
        {
            if(address==FieldPositionReader.AddressFieldId && ChangeField && fieldReads++>0)
                Word(FieldPositionReader.AddressFieldId,617);
            if(address==FieldMessageReader.AddressFieldWindowTextBuffers)
            {
                if(FailText)return false;
                if(ChangeText && textReads++>0)Byte(FieldMessageReader.AddressFieldWindowTextBuffers,0x21);
                var action=DuringText;DuringText=null;action?.Invoke();
            }
            for(var i=0;i<destination.Length;i++)
            {if(!bytes.TryGetValue(address+(uint)i,out var value)){Misses.Add($"{address:X8}+{i}/{destination.Length}");return false;}destination[i]=value;}
            return true;
        }
    }
}
