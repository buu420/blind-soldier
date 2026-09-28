using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum SnowboardPhase { Starting, Riding, Finishing }
public enum SnowboardTrackShape { Straight, Left, Right, Fork }

public readonly record struct SnowboardState(
    bool IsActive, bool IsPaused, SnowboardPhase Phase,
    int Segment, int Strip, SnowboardTrackShape Shape,
    SnowboardTrackShape Ahead, int AheadStrip,
    int LeftChild, int RightChild);

/// <summary>
/// Story-course geometry, shared by the legacy and translated x64 address spaces.
/// Native evidence: 00722F60 selects the story course; 00726015 sets the current
/// segment; 00724266 passes render-distance[strip] + 1 to 00726560, which starts
/// one strip behind the player. Thus the last visible strip is current + distance.
/// 0072698C indexes geometry records (stride 0x374). Their first short is straight,
/// left, right, or fork (0..3); the installed course's rotation/centre arrays also
/// confirm the direction. No unrendered branch or destination is announced.
/// </summary>
public sealed class SnowboardStateReader(ILegacyAddressSpace memory)
{
    public string LastDiagnostic { get; private set; } = string.Empty;
    public bool IsSnowboardModule { get; private set; }

    public bool TryRead(out SnowboardState state)
    {
        state = default;
        IsSnowboardModule = memory.TryReadByte(0x00CBF9DC, out var module) && module == 8;
        if (!IsSnowboardModule) return Fail("outside snowboarding");
        if (!memory.TryReadInt32(0x00DD865C, out var story) || story != 1 ||
            !memory.TryReadInt32(0x00DD7CAC, out var loaded) || loaded != 1 ||
            !memory.TryReadUInt32(0x00DD7CEC, out var callback) ||
            !memory.TryReadUInt32(0x00926290, out var address) || !Pointer(address, 0x264))
            return Fail("story course not ready");

        var phase = callback switch
        {
            0x00723A79 or 0x00723AD5 => SnowboardPhase.Starting,
            0x00724266 => SnowboardPhase.Riding,
            0x00724695 => SnowboardPhase.Finishing,
            _ => (SnowboardPhase?)null
        };
        if (phase is null) return Fail($"unsupported snowboard phase {callback:X8}");
        Span<byte> header = stackalloc byte[0x264];
        if (!memory.TryRead(address, header)) return Fail("snowboard state unreadable");
        var paused = BinaryPrimitives.ReadInt16LittleEndian(header[0x6C..]);
        var segment = BinaryPrimitives.ReadInt16LittleEndian(header[0xE8..]);
        var strip = BinaryPrimitives.ReadInt16LittleEndian(header[0xEA..]);
        if (paused is < 0 or > 1 || segment is < 0 or > 6 || strip < 0)
            return Fail("invalid course state");
        var shape = SnowboardTrackShape.Straight;
        var ahead = shape;
        var aheadStrip = -1;
        var left = -1;
        var right = -1;
        if (phase == SnowboardPhase.Riding)
        {
            var course = BinaryPrimitives.ReadUInt32LittleEndian(header[0x258..]);
            var geometry = BinaryPrimitives.ReadUInt32LittleEndian(header[0x23C..]);
            var recordAddress = BinaryPrimitives.ReadUInt32LittleEndian(header[0x13C..]);
            if (!Pointer(course, 7 * 32) || !Pointer(geometry, 76 * 0x374) ||
                recordAddress != course + (uint)segment * 32)
                return Fail("invalid course pointers");
            Span<byte> record = stackalloc byte[32];
            if (!memory.TryRead(recordAddress, record)) return Fail("segment unreadable");
            var count = BinaryPrimitives.ReadInt16LittleEndian(record);
            var tiles = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            var visibility = BinaryPrimitives.ReadUInt32LittleEndian(record[0x10..]);
            if (count is < 1 or > 512 || strip >= count || !Pointer(tiles, count) ||
                !Pointer(visibility, count) ||
                !memory.TryReadByte(visibility + (uint)strip, out var distance) || distance > 32)
                return Fail("invalid visible course range");
            left = record[4];
            right = record[5];
            if (left > 6 || right > 6) return Fail("invalid story branches");
            // The draw routine follows child segments only AFTER their fork.
            // Stop at that visible fork; never inspect either unseen route.
            var end = Math.Min(count - 1, strip + distance);
            for (var index = (int)strip; index <= end; index++)
            {
                if (!memory.TryReadByte(tiles + (uint)index, out var tile) || tile >= 76 ||
                    !memory.TryReadInt16(geometry + (uint)tile * 0x374, out var kind) || kind is < 0 or > 3)
                    return Fail("visible geometry unreadable");
                var current = (SnowboardTrackShape)kind;
                if (index == strip) shape = current;
                if (current == SnowboardTrackShape.Fork)
                {
                    ahead = current;
                    aheadStrip = index;
                    break;
                }
                if (aheadStrip < 0 && current != SnowboardTrackShape.Straight)
                {
                    ahead = current;
                    aheadStrip = index;
                }
            }
        }
        // The native thread can cross a segment or finish while we read it.
        if (!memory.TryReadUInt32(0x00DD7CEC, out var finalCallback) || callback != finalCallback ||
            !memory.TryReadInt16(address + 0xE8, out var finalSegment) || finalSegment != segment ||
            !memory.TryReadInt16(address + 0xEA, out var finalStrip) || finalStrip != strip)
            return Fail("course changed during observation");
        state = new SnowboardState(true, paused != 0, phase.Value, segment, strip,
            shape, ahead, aheadStrip, left, right);
        LastDiagnostic = $"phase={phase}, segment={segment}, strip={strip}, track={shape}, " +
            $"visible={ahead}@{aheadStrip}, paused={paused != 0}";
        return true;
    }

    private bool Fail(string reason) { LastDiagnostic = reason; return false; }
    private static bool Pointer(uint address, int length) => address >= 0x10000 &&
        length > 0 && (ulong)address + (uint)length < 0x80000000;
}
