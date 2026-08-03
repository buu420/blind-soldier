namespace Ff7.Accessibility.Reloaded;

public enum NativeTextDrawSource : byte
{
    MenuRenderer,
    InGameA,
    InGameB
}

public readonly record struct NativeTextDrawEvent(
    NativeTextDrawSource Source,
    int X,
    int Y,
    int Color,
    int Context,
    byte CurrentModule,
    byte[] TextBytes);

public sealed class NativeTextDrawEventQueue
{
    private sealed class Slot
    {
        public Slot(int maxTextBytes)
        {
            TextBytes = new byte[maxTextBytes];
        }

        public NativeTextDrawSource Source;
        public int X;
        public int Y;
        public int Color;
        public int Context;
        public byte CurrentModule;
        public int TextLength;
        public byte[] TextBytes { get; }
    }

    private readonly Slot[] slots;
    private readonly int maxTextBytes;
    private long readSequence;
    private long writeSequence;
    private long droppedCount;
    private int captureGate;

    public NativeTextDrawEventQueue(int capacity, int maxTextBytes)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (maxTextBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTextBytes));
        }

        this.maxTextBytes = maxTextBytes;
        slots = Enumerable.Range(0, capacity)
            .Select(_ => new Slot(maxTextBytes))
            .ToArray();
    }

    public long DroppedCount => Interlocked.Read(ref droppedCount);

    public unsafe bool TryCapture(
        NativeTextDrawSource source,
        int x,
        int y,
        byte* text,
        int color,
        int context,
        byte currentModule)
    {
        if (text == null || Interlocked.Exchange(ref captureGate, 1) != 0)
        {
            Interlocked.Increment(ref droppedCount);
            return false;
        }

        try
        {
            var write = Volatile.Read(ref writeSequence);
            var read = Volatile.Read(ref readSequence);
            if (write - read >= slots.Length)
            {
                Interlocked.Increment(ref droppedCount);
                return false;
            }

            var slot = slots[(int)(write % slots.Length)];
            var terminator = source == NativeTextDrawSource.MenuRenderer ? (byte)0 : (byte)0xff;
            var length = 0;
            while (length < maxTextBytes)
            {
                var value = text[length];
                slot.TextBytes[length++] = value;
                if (value == terminator)
                {
                    break;
                }
            }

            slot.Source = source;
            slot.X = x;
            slot.Y = y;
            slot.Color = color;
            slot.Context = context;
            slot.CurrentModule = currentModule;
            slot.TextLength = length;
            Volatile.Write(ref writeSequence, write + 1);
            return true;
        }
        finally
        {
            Volatile.Write(ref captureGate, 0);
        }
    }

    public bool TryDequeue(out NativeTextDrawEvent drawEvent)
    {
        var read = Volatile.Read(ref readSequence);
        if (read >= Volatile.Read(ref writeSequence))
        {
            drawEvent = default;
            return false;
        }

        var slot = slots[(int)(read % slots.Length)];
        var textBytes = new byte[slot.TextLength];
        Array.Copy(slot.TextBytes, textBytes, slot.TextLength);
        drawEvent = new NativeTextDrawEvent(
            slot.Source,
            slot.X,
            slot.Y,
            slot.Color,
            slot.Context,
            slot.CurrentModule,
            textBytes);
        Volatile.Write(ref readSequence, read + 1);
        return true;
    }

    public unsafe void WarmUp()
    {
        byte[] text = [0xff];
        fixed (byte* pointer = text)
        {
            TryCapture(NativeTextDrawSource.InGameA, 0, 0, pointer, 0, 0, 0);
        }

        TryDequeue(out _);
        Interlocked.Exchange(ref droppedCount, 0);
    }
}
