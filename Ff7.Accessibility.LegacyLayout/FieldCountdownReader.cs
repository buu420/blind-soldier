using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldCountdownSnapshot(
    bool IsActive,
    int RemainingSeconds,
    byte ClockWindowMask)
{
    public bool OwnsWindow(int windowId) =>
        windowId is >= 0 and < FieldCountdownReader.WindowCount &&
        (ClockWindowMask & (1 << windowId)) != 0;
}

public sealed class FieldCountdownReader
{
    public const int AddressRemainingSeconds = 0x00DC08BC;
    public const int AddressTimerDirectionFlags = 0x00DC093B;
    public const int AddressFieldWindowObjects = 0x00CFF5B8;
    public const int WindowStride = 0x30;
    public const int WindowSpecialDisplayTypeOffset = 0x1B;
    public const int WindowDrawableOffset = 0x2C;
    public const int WindowCount = FieldMessageReader.WindowCount;
    public const byte ClockDisplayType = 1;
    public const byte CountUpFlag = 1 << 1;

    private readonly ILegacyAddressSpace memory;

    public FieldCountdownReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public bool TryReadSnapshot(out FieldCountdownSnapshot snapshot)
    {
        snapshot = default;
        if (!TryCaptureFrame(out var before) ||
            !TryCaptureFrame(out var after) ||
            !before.Equals(after))
        {
            return false;
        }

        if (before.Module != FieldPositionReader.FieldModule)
        {
            return true;
        }

        byte clockWindowMask = 0;
        for (var windowId = 0; windowId < WindowCount; windowId++)
        {
            if (before.WindowStates[windowId] != FieldMessageReader.FreeWindowState &&
                before.SpecialDisplayTypes[windowId] == ClockDisplayType &&
                before.DrawableStates[windowId] != 0)
            {
                clockWindowMask |= (byte)(1 << windowId);
            }
        }

        var isCountdown =
            (before.DirectionFlags & CountUpFlag) == 0 &&
            before.RemainingSeconds >= 0 &&
            clockWindowMask != 0;
        snapshot = new FieldCountdownSnapshot(
            isCountdown,
            before.RemainingSeconds,
            clockWindowMask);
        return true;
    }

    public static uint WindowAddress(int windowId, int offset)
    {
        if (windowId is < 0 or >= WindowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId));
        }

        if (offset is < 0 or >= WindowStride)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return checked((uint)(AddressFieldWindowObjects + windowId * WindowStride + offset));
    }

    private bool TryCaptureFrame(out CheckedFrame frame)
    {
        frame = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId))
        {
            return false;
        }

        var states = new byte[WindowCount];
        var types = new byte[WindowCount];
        var drawableStates = new ushort[WindowCount];
        if (module != FieldPositionReader.FieldModule)
        {
            frame = new CheckedFrame(module, fieldId, 0, 0, states, types, drawableStates);
            return true;
        }

        if (!memory.TryReadByte((uint)AddressTimerDirectionFlags, out var directionFlags) ||
            !memory.TryReadInt32((uint)AddressRemainingSeconds, out var remainingSeconds))
        {
            return false;
        }

        for (var windowId = 0; windowId < WindowCount; windowId++)
        {
            if (!memory.TryReadByte(
                    (uint)(FieldMessageReader.AddressFieldWindowStates + windowId),
                    out states[windowId]) ||
                !memory.TryReadByte(
                    WindowAddress(windowId, WindowSpecialDisplayTypeOffset),
                    out types[windowId]) ||
                !memory.TryReadUInt16(
                    WindowAddress(windowId, WindowDrawableOffset),
                    out drawableStates[windowId]))
            {
                return false;
            }
        }

        frame = new CheckedFrame(
            module,
            fieldId,
            directionFlags,
            remainingSeconds,
            states,
            types,
            drawableStates);
        return true;
    }

    private readonly struct CheckedFrame : IEquatable<CheckedFrame>
    {
        public CheckedFrame(
            byte module,
            ushort fieldId,
            byte directionFlags,
            int remainingSeconds,
            byte[] windowStates,
            byte[] specialDisplayTypes,
            ushort[] drawableStates)
        {
            Module = module;
            FieldId = fieldId;
            DirectionFlags = directionFlags;
            RemainingSeconds = remainingSeconds;
            WindowStates = windowStates;
            SpecialDisplayTypes = specialDisplayTypes;
            DrawableStates = drawableStates;
        }

        public byte Module { get; }
        public ushort FieldId { get; }
        public byte DirectionFlags { get; }
        public int RemainingSeconds { get; }
        public byte[] WindowStates { get; }
        public byte[] SpecialDisplayTypes { get; }
        public ushort[] DrawableStates { get; }

        public bool Equals(CheckedFrame other) =>
            Module == other.Module &&
            FieldId == other.FieldId &&
            DirectionFlags == other.DirectionFlags &&
            RemainingSeconds == other.RemainingSeconds &&
            WindowStates.AsSpan().SequenceEqual(other.WindowStates) &&
            SpecialDisplayTypes.AsSpan().SequenceEqual(other.SpecialDisplayTypes) &&
            DrawableStates.AsSpan().SequenceEqual(other.DrawableStates);
    }
}
