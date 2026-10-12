using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldRoomPresentationTests
{
    public static void Run()
    {
        var memory = new Memory();
        var reader = new FieldRoomPresentationReader(memory);
        memory.Set(1,777,0,0);
        Check(true,reader.IsRoomVisible(777),"ordinary visible room without a control-byte read");
        memory.Set(1,777,1,0);
        Check(false,reader.IsRoomVisible(777),"full-screen native movie hides room");
        memory.Set(1,777,1,1);
        Check(true,reader.IsRoomVisible(777),"background movie presents the field room");
        memory.Set(1,777,0,1);
        Check(true,reader.IsRoomVisible(777),"inactive background movie presents room");
        Check(false,reader.IsRoomVisible(776),"expected room must match actual native field");
        memory.Set(2,777,0,0);
        Check(false,reader.IsRoomVisible(777),"non-field module is not a room");
        memory.Set(1,777,2,0);
        Check(false,reader.IsRoomVisible(777),"unknown native movie state");
        memory.Set(1,777,0,2);
        Check(false,reader.IsRoomVisible(777),"unknown native background mode");
        foreach (var address in new[] {(uint)FieldPositionReader.AddressCurrentModule,(uint)FieldPositionReader.AddressFieldId+1,
            (uint)FieldAudibleCueStateReader.AddressFieldMovieActive+1,(uint)FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode})
        {
            memory.Set(1,777,0,0);memory.Bytes.Remove(address);
            Check(false,reader.IsRoomVisible(777),"unreadable native presentation fails closed");
        }
        foreach (var address in new[] {(uint)FieldPositionReader.AddressCurrentModule,(uint)FieldPositionReader.AddressFieldId,
            (uint)FieldAudibleCueStateReader.AddressFieldMovieActive,(uint)FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode})
        {
            memory.Set(1,777,0,0);memory.Tear=address;
            Check(false,reader.IsRoomVisible(777),"changing native presentation fails closed");
        }
        Console.WriteLine("PASS coherent native room presentation, full-screen/background movies, unknown/unreadable/torn flags");
    }

    private static void Check(bool expected,bool actual,string message)
    { if(expected!=actual)throw new InvalidOperationException(message); }

    private sealed class Memory : ILegacyAddressSpace
    {
        internal readonly Dictionary<uint,byte> Bytes = new();
        internal uint? Tear;
        internal void Set(byte module,ushort field,ushort movie,byte background)
        {
            Bytes.Clear();Tear=null;Bytes[(uint)FieldPositionReader.AddressCurrentModule]=module;
            Word((uint)FieldPositionReader.AddressFieldId,field);
            Word((uint)FieldAudibleCueStateReader.AddressFieldMovieActive,movie);
            Bytes[(uint)FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode]=background;
        }
        private void Word(uint at,ushort value)
        { Bytes[at]=(byte)value;Bytes[at+1]=(byte)(value>>8); }
        public bool TryRead(uint address,Span<byte> destination)
        {
            for(var i=0;i<destination.Length;i++)
            {
                var at=address+(uint)i;if(!Bytes.TryGetValue(at,out destination[i]))return false;
                if(Tear==at){Bytes[at]++;Tear=null;}
            }
            return true;
        }
    }
}
