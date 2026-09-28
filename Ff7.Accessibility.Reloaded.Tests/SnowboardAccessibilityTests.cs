using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class SnowboardAccessibilityTests
{
    public static void Run()
    {
        var memory = new SnowboardMemory();
        var reader = new SnowboardStateReader(memory);
        Check(reader.TryRead(out var state), "active story snowboard is readable");
        Check(state.Phase == SnowboardPhase.Riding && state.Shape == SnowboardTrackShape.Straight,
            "native phase and current track geometry");
        Check(state.Ahead == SnowboardTrackShape.Left && state.AheadStrip == 3,
            "reports the nearest visible bend");
        memory.Byte(0x00600000 + 4 * 0x374, 3);
        Check(reader.TryRead(out state) && state.Ahead != SnowboardTrackShape.Fork,
            "a fork beyond the native render distance remains unannounced");
        memory.Byte(0x00403001, 3);
        Check(reader.TryRead(out state) && state.Ahead == SnowboardTrackShape.Fork && state.AheadStrip == 4,
            "visible forks take priority over intervening bends");
        memory.Byte(0x00CBF9DC, 1);
        Check(!reader.TryRead(out _), "field gameplay does not read stale snowboard memory");
        memory.Byte(0x00CBF9DC, 8);
        memory.Int32(0x00DD865C, 0);
        Check(!reader.TryRead(out _), "arcade layout is not mistaken for the story course");
        memory.Int32(0x00DD865C, 1);
        memory.Int16(0x003000EA, 1000);
        Check(!reader.TryRead(out _), "corrupt strip is rejected");
        memory.Int16(0x003000EA, 1);
        memory.Int16(0x0030006C, 1);
        Check(reader.TryRead(out state) && state.IsPaused, "native pause state");
        memory.Int32(0x00DD7CEC, 0x00724695);
        Check(reader.TryRead(out state) && state.Phase == SnowboardPhase.Finishing, "native finish transition");
        memory.Int32(0x00DD7CEC, 0x00724266);
        memory.Bytes.Remove(0x00403001);
        Check(!reader.TryRead(out _), "missing native visibility data cannot reveal distant geometry");

        var readout = new SnowboardReadout();
        var now = new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc);
        var riding = new SnowboardState(true, false, SnowboardPhase.Riding, 0, 10,
            SnowboardTrackShape.Straight, SnowboardTrackShape.Left, 12, 1, 2);
        Check(readout.Observe(riding, now)?.Contains("Left bend", StringComparison.Ordinal) == true,
            "joining an active run immediately gives the visible bend");
        Check(readout.Observe(riding, now.AddSeconds(2)) is null, "unchanged bends are not repeated");
        var fork = riding with { Ahead = SnowboardTrackShape.Fork, AheadStrip = 15 };
        Check(readout.Observe(fork, now.AddMilliseconds(100))?.Contains("Fork", StringComparison.Ordinal) == true,
            "a newly visible fork bypasses ordinary speech throttling");
        Check(readout.Observe(fork, now.AddSeconds(3)) is null, "holding near a fork does not spam speech");
        var child = riding with { Segment = 1, Strip = 0, LeftChild = 3, RightChild = 4 };
        Check(readout.Observe(child, now.AddSeconds(4)) == "Left route.", "selected route confirmation");
        Check(readout.Observe(child with { IsPaused = true }, now.AddSeconds(5)) == "Snowboarding paused.",
            "pause has a short announcement");
        Check(readout.Observe(child with { IsPaused = true, Ahead = SnowboardTrackShape.Fork }, now.AddSeconds(7)) is null,
            "paused game suppresses track chatter");
        Check(readout.Observe(child, now.AddSeconds(8)) == "Snowboarding resumed.", "resume announcement");
        Check(readout.Observe(child with { Phase = SnowboardPhase.Finishing }, now.AddSeconds(9)) == "Run complete.",
            "finish once");
        Check(readout.Observe(child with { Phase = SnowboardPhase.Finishing }, now.AddSeconds(10)) is null,
            "finish deduplicated");
        readout.Observe(default, now.AddSeconds(11));
        Check(readout.Observe(riding, now.AddSeconds(12)) is not null, "new run resets cues");

        // Two left bends with a short straight between them: the second is its own bend.
        var bends = new SnowboardReadout();
        var inFirst = riding with { Strip = 20, Shape = SnowboardTrackShape.Left, Ahead = SnowboardTrackShape.Left, AheadStrip = 20 };
        Check(bends.Observe(inFirst, now) == "Left bend.", "first left bend");
        var passed = inFirst with { Strip = 23, Shape = SnowboardTrackShape.Straight, AheadStrip = 27 };
        Check(bends.Observe(passed, now.AddSeconds(2)) == "Left bend.", "a second left bend after a straight is announced");
        Check(bends.Observe(passed with { Strip = 24 }, now.AddSeconds(4)) is null, "and only once");

        // A fork already visible in the segment just entered does not swallow the route taken.
        var split = new SnowboardReadout();
        var approaching = riding with { Segment = 0, Ahead = SnowboardTrackShape.Fork, AheadStrip = 30, LeftChild = 1, RightChild = 2 };
        Check(split.Observe(approaching, now)?.StartsWith("Fork", StringComparison.Ordinal) == true, "first fork");
        var branch = approaching with { Segment = 2, Strip = 0, LeftChild = 5, RightChild = 6 };
        Check(split.Observe(branch, now.AddSeconds(1)) == "Right route. Fork ahead.",
            "route and new fork share one utterance so interrupting speech cannot erase the route");
        Check(split.Observe(branch, now.AddSeconds(1.02)) is null,
            "next worker tick cannot cut off the route confirmation");

        // Finishing and starting again inside module 8 announces the same forks again.
        var rerun = new SnowboardReadout();
        Check(rerun.Observe(approaching, now)?.StartsWith("Fork", StringComparison.Ordinal) == true, "first run's fork");
        rerun.Observe(approaching with { Phase = SnowboardPhase.Finishing }, now.AddSeconds(5));
        rerun.Observe(approaching with { Phase = SnowboardPhase.Starting }, now.AddSeconds(6));
        Check(rerun.Observe(approaching, now.AddSeconds(8))?.StartsWith("Fork", StringComparison.Ordinal) == true,
            "a new run's fork in the same segment is announced");
        Console.WriteLine("Snowboard native visibility and readout tests passed.");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var path = Path.Combine(root, "ff7_en.exe");
        if (!File.Exists(path)) return;
        var image = File.ReadAllBytes(path);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var imageBase = checked((uint)pe.PEHeaders.PEHeader!.ImageBase);
        var memory = new SnowboardMemory();
        foreach (var section in pe.PEHeaders.SectionHeaders)
        {
            for (var i = 0; i < section.SizeOfRawData; i++)
            {
                var address = imageBase + (uint)section.VirtualAddress + (uint)i;
                // Only the private installed native course tables are required.
                if (address >= 0x00926500 && address < 0x00939DC0)
                    memory.Byte(address, image[section.PointerToRawData + i]);
            }
        }
        memory.Int32(0x0030023C, 0x00926500);
        memory.Int32(0x00300258, 0x00939CE0);
        var reader = new SnowboardStateReader(memory);
        var samples = 0;
        var forks = 0;
        var finishes = 0;
        for (short segment = 0; segment < 7; segment++)
        {
            var record = 0x00939CE0U + (uint)segment * 32;
            Check(memory.TryReadInt16(record, out var count), "installed segment length");
            Check(memory.TryReadUInt32(record + 8, out var tiles), "installed tile index");
            Check(memory.TryReadUInt32(record + 0xC, out var flags), "installed finish flags");
            Check(memory.TryReadUInt32(record + 0x18, out var rotations), "installed track rotations");
            memory.Int16(0x003000E8, segment);
            memory.Int32(0x0030013C, (int)record);
            for (short strip = 0; strip < count; strip++)
            {
                memory.Int16(0x003000EA, strip);
                Check(reader.TryRead(out var state), $"installed segment {segment}, strip {strip}: {reader.LastDiagnostic}");
                Check(memory.TryReadByte(tiles + (uint)strip, out var tile) &&
                    memory.TryReadInt16(0x00926500 + (uint)tile * 0x374, out var kind) &&
                    state.Shape == (SnowboardTrackShape)kind, "reported shape matches installed geometry");
                if (state.Shape == SnowboardTrackShape.Fork) forks++;
                if (strip + 1 < count && state.Shape is SnowboardTrackShape.Left or SnowboardTrackShape.Right)
                {
                    Check(memory.TryReadInt16(rotations + (uint)strip * 2, out var from) &&
                        memory.TryReadInt16(rotations + (uint)(strip + 1) * 2, out _), "track rotation pair readable");
                    memory.TryReadInt16(rotations + (uint)(strip + 1) * 2, out var to);
                    var turn = ((to - from + 2048) & 4095) - 2048;
                    Check(state.Shape == SnowboardTrackShape.Left ? turn > 0 : turn < 0,
                        "bend directions agree with native course rotation, not guessed names");
                }
                if (strip == count - 1 && memory.TryReadByte(flags + (uint)strip, out var lastFlags) &&
                    (lastFlags & 0x80) != 0) finishes++;
                samples++;
            }
        }
        Check(samples == 601 && forks == 3 && finishes == 4,
            "all seven installed story segments, both second forks, and all four landings covered");
        Console.WriteLine($"Snowboard installed course: {samples} native positions, {forks} forks, {finishes} endings verified.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Snowboarding: " + message);
    }

    private sealed class SnowboardMemory : ILegacyAddressSpace
    {
        internal readonly Dictionary<uint, byte> Bytes = new();
        public SnowboardMemory()
        {
            Byte(0x00CBF9DC, 8);
            Int32(0x00DD865C, 1);
            Int32(0x00DD7CAC, 1);
            Int32(0x00DD7CEC, 0x00724266);
            Int32(0x00926290, 0x00300000);
            for (uint i = 0; i < 0x280; i++) Byte(0x00300000 + i, 0);
            Int16(0x003000EA, 1);
            Int32(0x0030013C, 0x00400000);
            Int32(0x00300258, 0x00400000);
            Int32(0x0030023C, 0x00600000);
            for (uint i = 0; i < 32; i++) Byte(0x00400000 + i, 0);
            Int16(0x00400000, 10);
            Byte(0x00400004, 1);
            Byte(0x00400005, 2);
            Int32(0x00400008, 0x00401000);
            Int32(0x0040000C, 0x00402000);
            Int32(0x00400010, 0x00403000);
            for (uint i = 0; i < 10; i++)
            {
                Byte(0x00401000 + i, (byte)i);
                Byte(0x00402000 + i, 0);
                Byte(0x00403000 + i, 2);
                Int16(0x00600000 + i * 0x374, i == 3 ? (short)1 : (short)0);
            }
        }
        internal void Byte(uint address, byte value) => Bytes[address] = value;
        internal void Int16(uint address, short value)
        {
            Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
            for (uint i = 0; i < bytes.Length; i++) Byte(address + i, bytes[(int)i]);
        }
        internal void Int32(uint address, int value)
        {
            Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            for (uint i = 0; i < bytes.Length; i++) Byte(address + i, bytes[(int)i]);
        }
        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                if (!Bytes.TryGetValue(address + (uint)i, out var value)) return false;
                destination[i] = value;
            }
            return true;
        }
    }
}
