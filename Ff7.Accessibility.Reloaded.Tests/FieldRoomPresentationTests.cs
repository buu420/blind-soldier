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
        memory.Set(1,767,1,1);
        memory.TileFlags(0,0,0);
        Check(false,reader.IsRoomVisible(767),"las4_4 movie-only branch suppresses every static field tile");
        memory.TileFlags(1,0,0);
        Check(true,reader.IsRoomVisible(767),"las4_4 native background layer explicitly enabled");
        memory.TileFlags(0,1,0);
        Check(true,reader.IsRoomVisible(767),"las4_4 alternate native background presentation");
        memory.Set(1,767,0,0);memory.TileFlags(0,0,0);
        Check(true,reader.IsRoomVisible(767),"las4_4 tiles return after movie stops");
        memory.TileFlags(0,0,1);
        Check(false,reader.IsRoomVisible(767),"las4_4 initial black fade must not begin a long room recording");
        foreach(var at in new uint[]{0x00CC0B68,0x009A0718,0x00CFFEF8,0x00CC0DC0,0x00CFFA98})
        {
            memory.Set(1,767,0,0);memory.TileFlags(0,0,0);memory.Bytes.Remove(at);
            Check(false,reader.IsRoomVisible(767),"las4_4 unknown tile/fade gate fails closed");
            memory.Set(1,767,0,0);memory.TileFlags(0,0,0);memory.Tear=at;
            Check(false,reader.IsRoomVisible(767),"las4_4 changing tile/fade gate fails closed");
        }
        memory.Set(1,777,0,1);
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
        internal void TileFlags(byte background,int alternate,int fade)
        {
            Bytes[0x00CC0B68]=background;Bytes[0x00CC0DC0]=0;
            Integer(0x009A0718,alternate);Integer(0x00CFFEF8,fade);Integer(0x00CFFA98,0);
        }
        private void Integer(uint at,int value)
        {for(var i=0;i<4;i++)Bytes[at+(uint)i]=(byte)(value>>(i*8));}
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
