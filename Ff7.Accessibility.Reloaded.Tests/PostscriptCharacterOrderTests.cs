using Ff7.Accessibility.LegacyLayout;
namespace Ff7.Accessibility.Reloaded.Tests;
internal static class PostscriptCharacterOrderTests
{
    internal static void Run()
    {
        var memory = new Memory();
        var reader = new PostscriptCharacterOrderReader(memory);
        Require(!reader.TryReadDisplayedCharacter(0, out _), "Stock memory must not invent a randomized actor.");
        // Expected permutations come from Unicorn executing the recovered original x86 instructions.
        (ushort seed, int[] values)[] vectors = [(0,[4,5,8,1,3,0,7,6,2]),(1,[1,4,0,8,2,3,6,5,7]),
            (2,[4,7,1,8,2,6,3,5,0]),(42,[4,6,1,0,5,3,8,7,2]),(12345,[1,0,2,5,4,6,3,8,7]),
            (32768,[4,6,0,5,8,3,7,2,1]),(65535,[5,7,2,6,1,8,4,3,0])];
        foreach (var (seed, values) in vectors)
            for (var canonical=0;canonical<9;canonical++)
                Require(PostscriptCharacterOrderReader.ResolveVerifiedRole(seed,canonical)==values[canonical],
                    $"Independent native numeric vector {seed}:{canonical}.");
        var fixture=Environment.GetEnvironmentVariable("BLIND_SOLDIER_POSTSCRIPT_CHARACTER_FIXTURE");
        if (string.IsNullOrWhiteSpace(fixture) || !File.Exists(fixture))
        {
            Console.WriteLine("Postscript character positive native integration skipped: private fixture BLIND_SOLDIER_POSTSCRIPT_CHARACTER_FIXTURE unavailable; all numeric vectors and stock rejection ran.");
            return;
        }
        Witnesses(memory,fixture);
        Require(reader.IsRemappingRequested,"Native name-route witness must distinguish requested remapping.");
        foreach (var (seed, values) in vectors)
        {
            memory.Write(0xDC0E08, BitConverter.GetBytes(seed));
            for (var canonical = 0; canonical < 9; canonical++)
                Require(reader.TryReadDisplayedCharacter(canonical, out var actual) && actual == values[canonical],
                    $"Native seed {seed}, actor {canonical} must be {values[canonical]}.");
        }
        Require(!reader.TryReadDisplayedCharacter(-1,out _) && !reader.TryReadDisplayedCharacter(9,out _),
            "Only explicitly requested ordinary party roles may resolve; no future roster inventory.");
        memory.Write(0x631FCA,[0]);
        Require(!reader.TryReadDisplayedCharacter(0,out _), "Altered name mapping must fail closed.");
        Witnesses(memory,fixture); memory.Remove(0xDC0E08);
        Require(!reader.TryReadDisplayedCharacter(0,out _), "Unreadable seed is not seed zero.");
        memory.Write(0xDC0E08,BitConverter.GetBytes((ushort)42));
        var seedReads=0; memory.BeforeRead = (address,count) => { if(address==0xDC0E08 && ++seedReads==2) memory.Write(address,BitConverter.GetBytes((ushort)1)); };
        Require(!reader.TryReadDisplayedCharacter(0,out _), "Seed changed during observation must fail closed.");
        memory.BeforeRead=null; memory.Write(0xDC0E08,BitConverter.GetBytes((ushort)42));
        var mapperReads=0; memory.BeforeRead=(address,count)=> { if(address==0x9B4240 && ++mapperReads==2) memory.Write(address,[0]); };
        Require(!reader.TryReadDisplayedCharacter(0,out _), "Mapping code changed during observation must fail closed.");
    }
    private static void Witnesses(Memory m,string fixture)
    {
        m.Write(0x631FCA,Convert.FromHexString("E8712238008945FC90908B4DFC"));
        m.Write(0x61BCF4,Convert.FromHexString("0FB6441101E8428539008945FC8BC890"));
        foreach(var line in File.ReadLines(fixture))
        {
            var parts=line.Split('=',2,StringSplitOptions.TrimEntries);
            if(parts.Length==2 && uint.TryParse(parts[0],System.Globalization.NumberStyles.HexNumber,null,out var address) &&
                address is 0x9B4240 or 0x9B412F)
                m.Write(address,Convert.FromHexString(parts[1].Replace(" ",string.Empty,StringComparison.Ordinal)));
        }
        m.Write(0x753A59,Convert.FromHexString("8B550869D2658B585D83C201895508"));
        m.Write(0x753BE8,Convert.FromHexString("558BECA138A1E20083C001A338A1E200"));
    }
    private static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    private sealed class Memory: ILegacyAddressSpace
    {
        private readonly Dictionary<uint,byte> data=[]; private readonly Dictionary<uint,int> counts=[];
        internal Action<uint,int>? BeforeRead;
        internal void Write(uint address,byte[] bytes) { for(var i=0;i<bytes.Length;i++) data[address+(uint)i]=bytes[i]; }
        internal void Remove(uint address)=>data.Remove(address);
        public bool TryRead(uint address,Span<byte> destination)
        {counts.TryGetValue(address,out var count);counts[address]=++count;BeforeRead?.Invoke(address,count);
         for(var i=0;i<destination.Length;i++){if(!data.TryGetValue(address+(uint)i,out destination[i]))return false;}return true;}
    }
}


