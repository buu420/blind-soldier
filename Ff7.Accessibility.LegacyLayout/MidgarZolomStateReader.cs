using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the stock world-map Midgar Zolom position history. The x64 runtime
/// uses the same addresses through its translated legacy guest address space.
/// </summary>
public sealed class MidgarZolomStateReader
{
    public const int AddressEnabled = 0x00E29F40;
    public const int AddressPositionHistoryStart = 0x00E29F80;
    public const int AddressPositionHistoryEnd = 0x00E2A100;
    public const int AddressCurrentPositionPointer = 0x00E2A18C;
    public const int PositionRecordSize = 8;
    public const int WorldXOrigin = 0x34000;
    public const int WorldZOrigin = 0x20000;

    private readonly ILegacyAddressSpace memory;

    public MidgarZolomStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public MidgarZolomStateReadResult Read()
    {
        if (!TryReadFrame(out var first, out var diagnostic))
        {
            return MidgarZolomStateReadResult.Invalid(first.State, diagnostic);
        }

        if (!TryReadFrame(out var second, out var secondDiagnostic))
        {
            return MidgarZolomStateReadResult.Invalid(first.State, secondDiagnostic);
        }

        if (first != second)
        {
            return MidgarZolomStateReadResult.Invalid(
                first.State,
                "Midgar Zolom state changed during read");
        }

        return MidgarZolomStateReadResult.Valid(
            first.State,
            first.Enabled
                ? $"active pointer=0x{first.PositionPointer:X8}, position={first.State.X},{first.State.Z}, direction={first.Direction}"
                : "inactive");
    }

    private bool TryReadFrame(out MidgarZolomFrame frame, out string diagnostic)
    {
        frame = default;
        diagnostic = "Midgar Zolom header read failed";
        if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) ||
            !memory.TryReadInt32((uint)WorldMapStateReader.AddressWorldMapType, out var mapType) ||
            !memory.TryReadByte((uint)AddressEnabled, out var enabled) ||
            !memory.TryReadUInt32((uint)AddressCurrentPositionPointer, out var positionPointer))
        {
            return false;
        }

        frame = new MidgarZolomFrame(
            module,
            mapType,
            enabled != 0,
            positionPointer,
            0,
            0,
            0,
            0);
        if (module != WorldMapStateReader.WorldModule)
        {
            diagnostic = $"module={module}, not world map";
            return false;
        }

        if (mapType != 0)
        {
            diagnostic = $"world map type={mapType}, not overworld";
            return false;
        }

        if (enabled == 0)
        {
            diagnostic = string.Empty;
            return true;
        }

        if (positionPointer < AddressPositionHistoryStart ||
            positionPointer > AddressPositionHistoryEnd - PositionRecordSize ||
            (positionPointer - AddressPositionHistoryStart) % PositionRecordSize != 0)
        {
            diagnostic = $"Midgar Zolom position pointer 0x{positionPointer:X8} is outside or misaligned within its native history ring";
            return false;
        }

        diagnostic = "Midgar Zolom position record read failed";
        if (!memory.TryReadUInt16(positionPointer, out var relativeX) ||
            !memory.TryReadUInt16(positionPointer + 2, out var relativeZ) ||
            !memory.TryReadUInt16(positionPointer + 4, out var direction) ||
            !memory.TryReadUInt16(positionPointer + 6, out var auxiliaryWord))
        {
            return false;
        }

        frame = frame with
        {
            RelativeX = relativeX,
            RelativeZ = relativeZ,
            Direction = direction,
            AuxiliaryWord = auxiliaryWord
        };
        diagnostic = string.Empty;
        return true;
    }

    private readonly record struct MidgarZolomFrame(
        byte Module,
        int MapType,
        bool Enabled,
        uint PositionPointer,
        ushort RelativeX,
        ushort RelativeZ,
        ushort Direction,
        ushort AuxiliaryWord)
    {
        public MidgarZolomStateSnapshot State => Enabled
            ? new(
                true,
                WorldXOrigin + RelativeX,
                WorldZOrigin + RelativeZ,
                Direction)
            : default;
    }
}

public readonly record struct MidgarZolomStateSnapshot(
    bool IsActive,
    int X,
    int Z,
    ushort Direction);

public readonly record struct MidgarZolomStateReadResult(
    bool IsUsable,
    MidgarZolomStateSnapshot State,
    string Diagnostic)
{
    public static MidgarZolomStateReadResult Valid(
        MidgarZolomStateSnapshot state,
        string diagnostic) =>
        new(true, state, diagnostic);

    public static MidgarZolomStateReadResult Invalid(
        MidgarZolomStateSnapshot state,
        string diagnostic) =>
        new(false, state, diagnostic);
}
