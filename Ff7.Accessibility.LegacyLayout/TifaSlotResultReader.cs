using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum TifaSlotSymbol : byte
{
    Miss = 0,
    Hit = 1,
    Yeah = 2
}

public readonly record struct TifaSlotReelSnapshot(
    int ReelIndex,
    short Position,
    bool IsStopped,
    bool IsAligned,
    TifaSlotSymbol Symbol);

public readonly record struct TifaSlotResultSnapshot(
    bool IsValid,
    IReadOnlyList<TifaSlotReelSnapshot> Reels)
{
    public static TifaSlotResultSnapshot Invalid { get; } =
        new(false, Array.Empty<TifaSlotReelSnapshot>());
}

public readonly record struct TifaSlotCommittedResultSnapshot(
    bool IsValid,
    IReadOnlyList<TifaSlotSymbol> Symbols)
{
    public static TifaSlotCommittedResultSnapshot Invalid { get; } =
        new(false, Array.Empty<TifaSlotSymbol>());
}

/// <summary>
/// Reads Tifa's native slot-machine state. The symbol lookup mirrors the
/// game's own result commit routine instead of inferring a result from pixels.
/// </summary>
public sealed class TifaSlotResultReader
{
    private static readonly HashSet<byte> ValidReelThemes = [0, 1, 3, 4, 6, 7, 9];
    public const int AddressCurrentModule = BattleStateReader.AddressCurrentModule;
    public const int AddressResultTable = 0x0091EAD0;
    public const int AddressReelThemes = 0x00DC3A58;
    public const int AddressReelCount = 0x00DC3BAC;
    public const int AddressReelPositions = 0x00DC3C00;
    public const int AddressReelStopped = 0x00DC3C18;
    public const int AddressCurrentReel = 0x00DC3C24;
    public const int AddressCommittedResults = 0x009A88B4;
    public const int MaximumReelCount = 7;

    private readonly Func<int, byte> readByte;
    private readonly Func<int, ushort> readUInt16;
    private bool readFailed;
    private readonly bool tracksReadFailures;

    public TifaSlotResultReader(
        Func<int, byte> readByte,
        Func<int, ushort> readUInt16)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
    }

    public TifaSlotResultReader(ILegacyAddressSpace addressSpace)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        tracksReadFailures = true;
        readByte = address =>
        {
            if (addressSpace.TryReadByte(unchecked((uint)address), out var value))
            {
                return value;
            }

            readFailed = true;
            return byte.MaxValue;
        };
        readUInt16 = address =>
        {
            if (addressSpace.TryReadUInt16(unchecked((uint)address), out var value))
            {
                return value;
            }

            readFailed = true;
            return ushort.MaxValue;
        };
    }

    public TifaSlotResultSnapshot Read()
    {
        try
        {
            readFailed = false;
            var first = ReadCore();
            var firstReadFailed = tracksReadFailures && readFailed;
            readFailed = false;
            var second = ReadCore();
            var secondReadFailed = tracksReadFailures && readFailed;
            return !firstReadFailed &&
                   !secondReadFailed &&
                   AreEqual(first, second)
                ? second
                : TifaSlotResultSnapshot.Invalid;
        }
        catch
        {
            return TifaSlotResultSnapshot.Invalid;
        }
    }

    public TifaSlotCommittedResultSnapshot ReadCommitted()
    {
        try
        {
            readFailed = false;
            var first = ReadCommittedCore();
            var firstReadFailed = tracksReadFailures && readFailed;
            readFailed = false;
            var second = ReadCommittedCore();
            var secondReadFailed = tracksReadFailures && readFailed;
            return !firstReadFailed &&
                   !secondReadFailed &&
                   AreEqual(first, second)
                ? second
                : TifaSlotCommittedResultSnapshot.Invalid;
        }
        catch
        {
            return TifaSlotCommittedResultSnapshot.Invalid;
        }
    }

    private TifaSlotResultSnapshot ReadCore()
    {
        if (readByte(AddressCurrentModule) != BattleStateReader.BattleModule)
        {
            return TifaSlotResultSnapshot.Invalid;
        }

        var reelCount = readByte(AddressReelCount);
        if (reelCount is < 1 or > MaximumReelCount)
        {
            return TifaSlotResultSnapshot.Invalid;
        }

        var reels = new TifaSlotReelSnapshot[reelCount];
        for (var reelIndex = 0; reelIndex < reelCount; reelIndex++)
        {
            var theme = readByte(AddressReelThemes + reelIndex);
            var stopped = readByte(AddressReelStopped + reelIndex);
            if (!ValidReelThemes.Contains(theme) || stopped > 1)
            {
                return TifaSlotResultSnapshot.Invalid;
            }

            var position = unchecked((short)readUInt16(AddressReelPositions + (reelIndex * 2)));
            var adjustedQuarter = (position + ((position >> 31) & 3)) >> 2;
            var resultIndex = (2 - adjustedQuarter) & 0x0F;
            var symbolValue = readByte(AddressResultTable + (theme * 16) + resultIndex);
            if (symbolValue > (byte)TifaSlotSymbol.Yeah)
            {
                return TifaSlotResultSnapshot.Invalid;
            }

            reels[reelIndex] = new TifaSlotReelSnapshot(
                reelIndex,
                position,
                stopped != 0,
                (position & 3) == 0,
                (TifaSlotSymbol)symbolValue);
        }

        return new TifaSlotResultSnapshot(true, reels);
    }

    private static bool AreEqual(
        TifaSlotResultSnapshot left,
        TifaSlotResultSnapshot right) =>
        left.IsValid == right.IsValid &&
        (!left.IsValid || left.Reels.SequenceEqual(right.Reels));

    private TifaSlotCommittedResultSnapshot ReadCommittedCore()
    {
        if (readByte(AddressCurrentModule) != BattleStateReader.BattleModule)
        {
            return TifaSlotCommittedResultSnapshot.Invalid;
        }

        if (readByte(BattleStateReader.AddressMenuWindowStates + 0x1B) != 3)
        {
            return TifaSlotCommittedResultSnapshot.Invalid;
        }

        var reelCount = readByte(AddressReelCount);
        var currentReel = readByte(AddressCurrentReel);
        if (reelCount is < 1 or > MaximumReelCount || currentReel != reelCount)
        {
            return TifaSlotCommittedResultSnapshot.Invalid;
        }

        var symbols = new TifaSlotSymbol[reelCount];
        for (var reelIndex = 0; reelIndex < reelCount; reelIndex++)
        {
            if (readByte(AddressReelStopped + reelIndex) != 1)
            {
                return TifaSlotCommittedResultSnapshot.Invalid;
            }

            var value = readByte(AddressCommittedResults + reelIndex);
            if (value > (byte)TifaSlotSymbol.Yeah)
            {
                return TifaSlotCommittedResultSnapshot.Invalid;
            }

            symbols[reelIndex] = (TifaSlotSymbol)value;
        }

        return new TifaSlotCommittedResultSnapshot(true, symbols);
    }

    private static bool AreEqual(
        TifaSlotCommittedResultSnapshot left,
        TifaSlotCommittedResultSnapshot right) =>
        left.IsValid == right.IsValid &&
        (!left.IsValid || left.Symbols.SequenceEqual(right.Symbols));
}
