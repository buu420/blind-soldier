using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
namespace Ff7.Accessibility.Reloaded.Tests;
internal static class NativeKernel2TextTests
{
    internal static void Run()
    {
        LoadedOverridesWinAndUnreadableNativeTextDoesNotFallBack();
        var memory = new Memory();
        var reader = new NativeKernel2TextReader(memory);
        Require(reader.ResolveItemName(0) is null,"Unloaded native text must remain unavailable.");
        Populate(memory);
        Require(reader.HasSupportedLookup,"Exact native getter layout must be distinguishable from missing text.");
        (int bank,int id,string expected)[] examples=[(8,2,"Native command"),(0,2,"Command help"),(9,128,"Override limit"),
            (1,128,"Limit help"),(10,1,"Native item"),(2,1,"Item help"),(11,7,"Native weapon"),(3,7,"Weapon help"),
            (12,4,"Native armor"),(4,4,"Armor help"),(13,3,"Native accessory"),(5,3,"Accessory help"),
            (14,90,"AP Plus"),(6,90,"Native materia help"),(16,5,"Battle text"),(17,2,"Summon banner")];
        foreach(var (bank,id,text) in examples) Put(memory,bank,id,text);
        foreach(var (bank,id,text) in examples)
            Require(reader.TryRead(bank,id,out var actual)&&actual==text,$"Native bank {bank} id {id} must read selected loaded text.");
        Require(reader.ResolveCommandName(2)=="Native command"&&reader.ResolveCommandDescription(2)=="Command help","Command wrappers.");
        Require(reader.ResolveBattleActionName(0)=="Override limit"&&reader.ResolveBattleActionDescription(0)=="Limit help","Raw limit action uses native shifted index.");
        Require(reader.ResolveInventoryObjectName(135)=="Native weapon"&&reader.ResolveInventoryObjectDescription(291)=="Accessory help","Object categories preserve actual IDs.");
        Require(reader.ResolveMateriaName(90)=="AP Plus"&&reader.ResolveMateriaDescription(90)=="Native materia help","Loaded extra materia.");
        Require(reader.ResolveBattleText(5)=="Battle text"&&reader.ResolveSummonBannerText(2)=="Summon banner","Native banner banks.");
        Require(!reader.TryRead(-1,0,out _)&&!reader.TryRead(18,0,out _)&&!reader.TryRead(10,128,out _)&&reader.ResolveBattleActionName(127)is null,"Invalid categories and IDs cannot alias another text.");
        memory.Write(0x9A7FC8+20,BitConverter.GetBytes((ushort)0x6C00));
        Require(reader.HasSupportedLookup,"Unreadable loaded text must retain the supported getter identity.");
        Require(reader.ResolveItemName(1)is null,"Section beyond native pool rejected.");
        Populate(memory);Put(memory,10,1,"Native item");
        memory.Write(0x9A13C8+10*0x600+2,BitConverter.GetBytes((ushort)2));
        Require(reader.ResolveItemName(1)is null,"Entry pointing into offset table rejected.");
        Populate(memory); Put(memory,10,1,"Native item");
        var tableReads=0;memory.BeforeRead=(address)=>{if(address==0x9A7FC8&&++tableReads==2)memory.Write(address+20,BitConverter.GetBytes((ushort)1));};
        Require(reader.ResolveItemName(1)is null,"Replaced loaded table during read rejected.");
        memory.BeforeRead=null;Populate(memory);Put(memory,10,1,"Native item");
        var textAddress=0x9A13C8u+10*0x600u+256u;var textReads=0;
        memory.BeforeRead=(address)=>{if(address==textAddress&&++textReads==2)memory.Write(address,[33]);};
        Require(reader.ResolveItemName(1)is null,"Changed string during read rejected.");
        memory.BeforeRead=null;Populate(memory);Put(memory,10,1,"Native item");
        textReads=0;
        memory.BeforeRead=(address)=>{if(address==textAddress&&++textReads==2)memory.Write(0x9A7FC8+20,BitConverter.GetBytes((ushort)1));};
        Require(reader.ResolveItemName(1)is null,"Table replaced during the second text read rejected.");
        memory.BeforeRead=null;Populate(memory);memory.Write(textAddress,new byte[512]);
        Require(reader.ResolveItemName(1)is null,"Unterminated loaded text rejected.");
        Put(memory,10,1,"Native item");memory.Write(0x419457,[0]);
        Require(reader.ResolveItemName(1)is null,"Unknown engine lookup layout rejected.");
    }
    private static void LoadedOverridesWinAndUnreadableNativeTextDoesNotFallBack()
    {
        using var bytes=new MemoryStream();
        using (var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,leaveOpen:true))
        {
            for(var bank=0;bank<Counts.Length;bank++)
            {
                byte[] text=[..(bank==10 ? "Disk item" : "Disk text").Select(c=>(byte)(c-32)),255];
                writer.Write(Counts[bank]*2+text.Length);
                for(var id=0;id<Counts[bank];id++)writer.Write((ushort)(Counts[bank]*2));
                writer.Write(text);
            }
        }
        var database=Kernel2TextDatabase.TryCreateFromDecodedKernel2(bytes.ToArray()) ??
            throw new InvalidOperationException("Independent disk text fixture must parse.");
        Require(database.ResolveItemName(1)=="Disk item","Original independent disk fixture must be distinct.");
        var memory=new Memory();Populate(memory);Put(memory,10,1,"Loaded override item");
        database.UseNativeText(memory);
        Require(database.ResolveItemName(1)=="Loaded override item","Selected loaded override must win over disk text.");
        memory.Write(0x9A13C8+10*0x600,BitConverter.GetBytes((ushort)0));
        Require(database.ResolveItemName(1)is null,"Supported native getter with unreadable entry must fail closed, without stale disk text.");
        memory.Write(0x419457,[0]);
        Require(database.ResolveItemName(1)=="Disk item","Unknown native getter layout preserves existing original disk behavior.");
        memory.Write(0x419457,[0xE9,0,0,0,0]);
        Require(database.ResolveItemName(1)is null,"A redirected native text getter cannot substitute stale disk text while its loader is still binding.");
    }
    private static readonly int[] Counts=[32,256,128,128,32,32,96,64,32,256,128,128,32,32,96,64,128,16];
    private static void Populate(Memory m)
    {
        m.Write(0x419457,Convert.FromHexString("558BEC83EC088B450803451033C9668B0C45C87F9A0081C1C8139A00"));
        for(var bank=0;bank<18;bank++){m.Write(0x9A7FC8u+(uint)bank*2,BitConverter.GetBytes((ushort)(bank*0x600)));m.Write(0x9A13C8u+(uint)bank*0x600,BitConverter.GetBytes((ushort)(Counts[bank]*2)));}
    }
    private static void Put(Memory m,int bank,int id,string value)
    {
        var section=0x9A13C8u+(uint)bank*0x600;
        m.Write(section+(uint)id*2,BitConverter.GetBytes((ushort)(Counts[bank]*2)));
        m.Write(section+(uint)Counts[bank]*2,[..value.Select(c=>(byte)(c-32)),255]);
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class Memory:ILegacyAddressSpace
    {
        private readonly Dictionary<uint,byte> data=[];internal Action<uint>? BeforeRead;
        internal void Write(uint address,byte[] bytes){for(var i=0;i<bytes.Length;i++)data[address+(uint)i]=bytes[i];}
        public bool TryRead(uint address,Span<byte> destination){BeforeRead?.Invoke(address);for(var i=0;i<destination.Length;i++){if(!data.TryGetValue(address+(uint)i,out destination[i]))return false;}return true;}
    }
}
