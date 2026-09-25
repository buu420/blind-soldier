using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class WorldMapDialogueTests
{
    public static void Run()
    {
        var memory = new Memory();
        var reader = new WorldMapDialogueReader(memory);
        memory.SetWindow(6, "Not even a Chocobo could make it across.");
        Require(reader.TryRead(out var page), "native world warning is readable");
        Require(page.IsBlockingMovement && page.Windows.Single().Text.Contains("Chocobo"),
            "the visible warning owns input while the script waits for Confirm");
        Require(page.Windows[0].IsWaitingForInput, "completed text is ready to speak");
        var tracker = new WorldMapDialogueTracker();
        Require(tracker.Observe(page)?.Contains("Chocobo") == true, "ready warning speaks");
        Require(tracker.Observe(page) is null, "waiting for the player does not repeat the warning");
        memory.SetWindow(2, "Not even");
        Require(reader.TryRead(out page) && !page.Windows[0].IsWaitingForInput,
            "letters still appearing are not announced as a complete sentence");
        Require(tracker.Observe(page) is null, "typing does not interrupt a spoken sentence");
        memory.SetWindow(0, "stale warning");
        memory.Control = 1;
        Require(reader.TryRead(out page) && !page.IsBlockingMovement && page.Windows.Count == 0,
            "closed buffers do not become stale dialogue");
        Require(tracker.Observe(page) is null, "closing produces no speech");
        memory.Control = 0;
        Require(reader.TryRead(out page) && page.IsBlockingMovement,
            "scripted pushback before the warning also suspends movement");
        memory.Module = 1;
        Require(!reader.TryRead(out _), "field ownership cannot publish old world text");
        memory.Module = 3;
        memory.SetWindow(6, "Warning");
        Require(reader.TryRead(out page) && tracker.Observe(page) == "Warning",
            "a later visible window is announced");
        memory.SetWindow(6, "Warning");
        memory.TearText = true;
        Require(!reader.TryRead(out _), "a changing rendered buffer is rejected");
        memory.TearText = false;
        memory.FailRead = true;
        Require(!reader.TryRead(out _), "unreadable state is not fabricated as a zero value");
        Console.WriteLine("World dialogue reader tests passed.");
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = new();
        private int textReads;
        public byte Module = 3;
        public int Control;
        public bool TearText;
        public bool FailRead;

        public void SetWindow(ushort state, string text)
        {
            textReads = 0;
            for (var window=0;window<4;window++)
            {
                Write(WorldMapDialogueReader.WindowStateAddress + (uint)(window*0x30),
                    BitConverter.GetBytes(window==0 ? state : (ushort)0));
                bytes[(uint)FieldMessageReader.AddressFieldWindowStates + (uint)window] =
                    window==0 && state!=0 ? (byte)0 : (byte)0xFF;
                Write(WorldMapDialogueReader.TextPointerAddress + (uint)(window*4), BitConverter.GetBytes(0x00123400u));
            }
            var encoded=Enumerable.Repeat((byte)0xFF,256).ToArray();
            for(var i=0;i<text.Length;i++) encoded[i]=(byte)(text[i]-32);
            Write(WorldMapDialogueReader.TextBufferAddress,encoded);
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            if(FailRead) return false;
            if(address==(uint)WorldMapStateReader.AddressCurrentModule && destination.Length==1)
            { destination[0]=Module; return true; }
            if(address==WorldMapDialogueReader.ControlAddress && destination.Length==4)
            { BitConverter.GetBytes(Control).CopyTo(destination); return true; }
            for(var i=0;i<destination.Length;i++)
            {
                if(!bytes.TryGetValue(address+(uint)i,out var value)) return false;
                destination[i]=value;
            }
            if(address==WorldMapDialogueReader.TextBufferAddress && ++textReads>1 && TearText)
                destination[0]++;
            return true;
        }

        private void Write(uint address, byte[] value)
        { for(var i=0;i<value.Length;i++) bytes[address+(uint)i]=value[i]; }
    }
}
