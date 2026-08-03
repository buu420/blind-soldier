using System.Buffers.Binary;
using System.Collections.Immutable;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Saves;

/// <summary>
/// Validates only the statically proven native container envelope, occupancy
/// mask, and occupied-slot checksums. It intentionally does not interpret any
/// save payload field or expose save contents.
/// </summary>
public sealed class Steam2026SaveContainerProbe
{
    public const uint Magic = 0x06277371;
    public const int HeaderSize = 9;
    public const int SlotSize = 0x10F4;
    public const int SlotsPerContainer = 15;
    public const int ContainerSize = HeaderSize + (SlotSize * SlotsPerContainer);

    private const uint ValidOccupancyMask = (1u << SlotsPerContainer) - 1;
    private const int ChecksumSize = sizeof(uint);
    private readonly Func<string, byte[]?> readCompleteFile;

    public Steam2026SaveContainerProbe(Steam2026FingerprintResult fingerprint)
        : this(ReadCompleteFile)
    {
        RequireSupportedFingerprint(fingerprint);
    }

    internal Steam2026SaveContainerProbe(Func<string, byte[]?> readCompleteFile)
    {
        this.readCompleteFile = readCompleteFile
            ?? throw new ArgumentNullException(nameof(readCompleteFile));
    }

    public bool TryProbe(
        Steam2026SaveContainerCandidate candidate,
        out Steam2026SaveContainerContractSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            if (!TryValidateCandidate(candidate, out var path))
            {
                return false;
            }

            var before = readCompleteFile(path);
            var after = readCompleteFile(path);
            if (before is null ||
                after is null ||
                before.Length != ContainerSize ||
                after.Length != ContainerSize ||
                !before.AsSpan().SequenceEqual(after) ||
                !TryValidateContainer(before, out _, out _, out var occupied))
            {
                return false;
            }

            snapshot = new Steam2026SaveContainerContractSnapshot(
                occupied,
                candidate.FileIndex == Steam2026SaveCandidateDiscovery.StaticAutosaveContainerIndex &&
                occupied.Contains(Steam2026SaveCandidateDiscovery.StaticAutosaveSlotIndex));
            return true;
        }
        catch
        {
            snapshot = null!;
            return false;
        }
    }

    private static bool TryValidateCandidate(
        Steam2026SaveContainerCandidate? candidate,
        out string path)
    {
        path = string.Empty;
        if (candidate is null ||
            candidate.FileIndex is < 0 or >= Steam2026SaveCandidateDiscovery.ContainerCount ||
            string.IsNullOrWhiteSpace(candidate.FileName) ||
            string.IsNullOrWhiteSpace(candidate.FullPath))
        {
            return false;
        }

        var expectedName = $"save{candidate.FileIndex:00}.ff7";
        if (!string.Equals(candidate.FileName, expectedName, StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetFileName(candidate.FullPath),
                expectedName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = Path.GetFullPath(candidate.FullPath);
        return true;
    }

    private static bool TryValidateContainer(
        ReadOnlySpan<byte> bytes,
        out byte rawSelection,
        out uint occupancyMask,
        out ImmutableArray<int> occupiedSlots)
    {
        rawSelection = 0;
        occupancyMask = 0;
        occupiedSlots = [];
        if (bytes.Length != ContainerSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
        {
            return false;
        }

        rawSelection = bytes[4];
        occupancyMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes[5..]);
        if ((occupancyMask & ~ValidOccupancyMask) != 0)
        {
            return false;
        }

        var occupied = ImmutableArray.CreateBuilder<int>();
        for (var slot = 0; slot < SlotsPerContainer; slot++)
        {
            if ((occupancyMask & (1u << slot)) == 0)
            {
                continue;
            }

            var slotOffset = checked(HeaderSize + (slot * SlotSize));
            var slotBytes = bytes.Slice(slotOffset, SlotSize);
            var storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(slotBytes);
            var calculatedChecksum = CalculateChecksum(slotBytes[ChecksumSize..]);
            if (storedChecksum != calculatedChecksum)
            {
                rawSelection = 0;
                occupancyMask = 0;
                occupiedSlots = [];
                return false;
            }

            occupied.Add(slot);
        }

        occupiedSlots = occupied.ToImmutable();
        return true;
    }

    private static uint CalculateChecksum(ReadOnlySpan<byte> payload)
    {
        var result = 0xFFFFu;
        foreach (var value in payload)
        {
            result ^= (uint)value << 8;
            for (var bit = 0; bit < 8; bit++)
            {
                result = (result & 0x8000) != 0
                    ? (result << 1) ^ 0x1021u
                    : result << 1;
            }

            result &= 0xFFFF;
        }

        return (result ^ 0xFFFF) & 0xFFFF;
    }

    private static byte[]? ReadCompleteFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length != ContainerSize)
        {
            return null;
        }

        var bytes = new byte[ContainerSize];
        stream.ReadExactly(bytes);
        return stream.Length == ContainerSize ? bytes : null;
    }

    private static void RequireSupportedFingerprint(
        Steam2026FingerprintResult fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported ||
            !fingerprint.Identity.Is64Bit ||
            !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal) ||
            !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Native save probing requires the exact supported Steam 2026 x64 executable fingerprint.",
                nameof(fingerprint));
        }
    }
}

public sealed record Steam2026SaveContainerContractSnapshot(
    ImmutableArray<int> VerifiedOccupiedSlotIndices,
    bool StaticAutosaveSlotIsOccupied);
