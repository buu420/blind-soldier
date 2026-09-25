namespace WholeGameStateAudit;

internal sealed record Vertex(int X, int Y, int Z);

/// <summary>A gateway: walking across <see cref="Line"/> sends the party to <see cref="Destination"/>.</summary>
internal sealed record Gateway(int Index, Vertex[] Line, int Destination, int ArrivalX, int ArrivalY, int ArrivalTriangle, int Direction);

internal sealed record BackgroundTrigger(int Index, Vertex[] Line, int Parameter, int State, int Behavior, int Sound);

/// <summary>The exit arrows a sighted player sees; type 0 is invisible, 1 red, 2 green.</summary>
internal sealed record ExitArrow(int Index, int X, int Z, int Y, int Type, int DisplayFlag);

/// <summary>
/// The decoded field container: nine length-prefixed sections behind a blank word, a
/// section count and nine offsets. Only the script, model loader, walkmesh and trigger
/// sections are read.
/// </summary>
internal sealed class FieldFile
{
    private const int TriggerDoorsOffset = 0x38;
    private const int TriggerTriggersOffset = 0x158;
    private const int TriggerDisplayArrowOffset = 0x218;
    private const int TriggerArrowsOffset = 0x224;
    private const int UnusedGatewayField = 0x7FFF;

    public FieldFile(byte[] bytes)
    {
        Bytes = bytes;
        SectionCount = bytes.Length >= 6 ? BitConverter.ToInt32(bytes, 2) : 0;
    }

    public byte[] Bytes { get; }

    public int SectionCount { get; }

    public List<string> Issues { get; } = [];

    public byte[]? Section(int index)
    {
        if (index < 0 || index >= SectionCount || 6 + index * 4 + 4 > Bytes.Length)
        {
            return null;
        }

        var offset = BitConverter.ToInt32(Bytes, 6 + index * 4);
        if (offset <= 0 || offset + 4 > Bytes.Length)
        {
            return null;
        }

        var length = BitConverter.ToInt32(Bytes, offset);
        if (length < 0 || offset + 4 + length > Bytes.Length)
        {
            Issues.Add($"section {index} length {length} at {offset} overruns {Bytes.Length}");
            return null;
        }

        return Bytes.AsSpan(offset + 4, length).ToArray();
    }

    public int? WalkmeshTriangleCount()
    {
        var walkmesh = Section(4);
        return walkmesh is { Length: >= 4 } ? BitConverter.ToInt32(walkmesh, 0) : null;
    }

    public (List<Gateway> Gateways, List<BackgroundTrigger> Triggers, List<ExitArrow> Arrows, int Control)? ReadTriggers()
    {
        var data = Section(7);
        if (data is null || data.Length < TriggerArrowsOffset + 12 * 16)
        {
            Issues.Add($"trigger section is {data?.Length.ToString() ?? "missing"} bytes");
            return null;
        }

        var gateways = new List<Gateway>();
        for (var index = 0; index < 12; index++)
        {
            var at = TriggerDoorsOffset + index * 24;
            var destination = BitConverter.ToUInt16(data, at + 18);
            if (destination == UnusedGatewayField)
            {
                continue;
            }

            gateways.Add(new Gateway(
                index,
                [ReadVertex(data, at), ReadVertex(data, at + 6)],
                destination,
                BitConverter.ToInt16(data, at + 12),
                BitConverter.ToInt16(data, at + 14),
                BitConverter.ToUInt16(data, at + 16),
                data[at + 20]));
        }

        var triggers = new List<BackgroundTrigger>();
        for (var index = 0; index < 12; index++)
        {
            var at = TriggerTriggersOffset + index * 16;
            var line = new[] { ReadVertex(data, at), ReadVertex(data, at + 6) };
            if (line.All(vertex => vertex.X == 0 && vertex.Y == 0 && vertex.Z == 0))
            {
                continue;
            }

            triggers.Add(new BackgroundTrigger(index, line, data[at + 12], data[at + 13], data[at + 14], data[at + 15]));
        }

        var arrows = new List<ExitArrow>();
        for (var index = 0; index < 12; index++)
        {
            var at = TriggerArrowsOffset + index * 16;
            var type = BitConverter.ToInt32(data, at + 12);
            var x = BitConverter.ToInt32(data, at);
            var z = BitConverter.ToInt32(data, at + 4);
            var y = BitConverter.ToInt32(data, at + 8);
            if (type == 0 && x == 0 && y == 0 && z == 0)
            {
                continue;
            }

            arrows.Add(new ExitArrow(index, x, z, y, type, data[TriggerDisplayArrowOffset + index]));
        }

        return (gateways, triggers, arrows, data[9]);
    }

    /// <summary>
    /// The model loader's resource names in loader order, so entry n is what <c>CHAR n</c>
    /// loads. The same record walk the shipping catalog and FieldInteractionAudit use.
    /// </summary>
    public IReadOnlyList<string> ModelResourceNames()
    {
        var data = Section(2);
        if (data is null || data.Length < 6)
        {
            return [];
        }

        var count = BitConverter.ToUInt16(data, 2);
        var position = 6;
        var names = new List<string>(count);
        for (var model = 0; model < count; model++)
        {
            if (position + 2 > data.Length)
            {
                return [];
            }

            var nameLength = BitConverter.ToUInt16(data, position);
            position += 2;
            if (nameLength == 0 || position + nameLength + 46 > data.Length)
            {
                return [];
            }

            names.Add(System.Text.Encoding.ASCII.GetString(data, position, nameLength).TrimEnd('\0'));
            position += nameLength;
            var animations = BitConverter.ToUInt16(data, position + 14);
            position += 46;
            for (var animation = 0; animation < animations; animation++)
            {
                if (position + 2 > data.Length)
                {
                    return [];
                }

                position += 2 + BitConverter.ToUInt16(data, position) + 2;
                if (position > data.Length)
                {
                    return [];
                }
            }
        }

        return names;
    }

    private static Vertex ReadVertex(byte[] data, int at) =>
        new(BitConverter.ToInt16(data, at), BitConverter.ToInt16(data, at + 2), BitConverter.ToInt16(data, at + 4));
}
