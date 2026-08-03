using System.Buffers.Binary;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Saves;

internal static class Steam2026SaveContainerProbeTests
{
    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        ValidatesOnlyStableHeaderOccupancyAndChecksums();
        RejectsInvalidOrTornContainerContracts();
        PublicConstructionRequiresExactFingerprint(supported, unsupported);
        KeepsProbeContentBlindAndCapabilityNeutral(supported);
    }

    private static void ValidatesOnlyStableHeaderOccupancyAndChecksums()
    {
        var bytes = CreateContainer(occupancyMask: (1u << 0) | (1u << 14), rawSelection: 0x9E);
        var candidate = Candidate(9);
        var probe = new Steam2026SaveContainerProbe(_ => (byte[])bytes.Clone());

        Equal(true, probe.TryProbe(candidate, out var snapshot), "stable native save container contract");
        SequenceEqual([0, 14], snapshot.VerifiedOccupiedSlotIndices, "checksum-validated occupied slots");
        Equal(true, snapshot.StaticAutosaveSlotIsOccupied, "static autosave target occupancy");

        Equal(
            true,
            probe.TryProbe(Candidate(0), out var manualContainer),
            "same verified slot layout in a manual container");
        Equal(
            false,
            manualContainer.StaticAutosaveSlotIsOccupied,
            "static autosave occupancy is tied to the proven container and slot");

        var differentRawSelection = CreateContainer(
            occupancyMask: (1u << 0) | (1u << 14),
            rawSelection: 0x01);
        var differentRawSelectionProbe = new Steam2026SaveContainerProbe(
            _ => (byte[])differentRawSelection.Clone());
        Equal(
            true,
            differentRawSelectionProbe.TryProbe(candidate, out var normalized),
            "alternate packed selection remains a valid content-blind contract");
        SequenceEqual(
            snapshot.VerifiedOccupiedSlotIndices,
            normalized.VerifiedOccupiedSlotIndices,
            "packed selection is absent from normalized save observations");
        Equal(
            snapshot.StaticAutosaveSlotIsOccupied,
            normalized.StaticAutosaveSlotIsOccupied,
            "packed selection cannot change normalized autosave occupancy");

        var empty = CreateContainer(occupancyMask: 0, rawSelection: 0);
        empty[Steam2026SaveContainerProbe.HeaderSize + 100] = 0x99;
        var emptyProbe = new Steam2026SaveContainerProbe(_ => (byte[])empty.Clone());
        Equal(true, emptyProbe.TryProbe(Candidate(0), out var emptySnapshot), "unoccupied stale slot bytes are not parsed");
        Equal(0, emptySnapshot.VerifiedOccupiedSlotIndices.Length, "empty occupancy publishes no slot contents");
    }

    private static void RejectsInvalidOrTornContainerContracts()
    {
        var valid = CreateContainer(occupancyMask: 1, rawSelection: 0);
        var cases = new List<(byte[] Bytes, string Label)>
        {
            (valid[..^1], "wrong native container length"),
            (Mutate(valid, 0, value => (byte)(value ^ 0x01)), "wrong native container magic"),
            (Mutate(valid, 5, _ => 0x00, writeUInt32: 0x00008000u), "occupancy mask outside fifteen slots"),
            (Mutate(valid, Steam2026SaveContainerProbe.HeaderSize + 4, value => (byte)(value ^ 0x01)), "occupied slot checksum mismatch")
        };

        foreach (var testCase in cases)
        {
            var probe = new Steam2026SaveContainerProbe(_ => (byte[])testCase.Bytes.Clone());
            Equal(false, probe.TryProbe(Candidate(0), out _), testCase.Label);
        }

        var reads = 0;
        var tornProbe = new Steam2026SaveContainerProbe(_ =>
        {
            var copy = (byte[])valid.Clone();
            if (++reads == 2)
            {
                copy[4] = 1;
            }

            return copy;
        });
        Equal(false, tornProbe.TryProbe(Candidate(0), out _), "torn native save replacement rejected");

        var failureProbe = new Steam2026SaveContainerProbe(_ => throw new InvalidOperationException("simulated diagnostic failure"));
        Equal(false, failureProbe.TryProbe(Candidate(0), out _), "native save read failure rejected");
        Equal(false, new Steam2026SaveContainerProbe(_ => valid).TryProbe(Candidate(10), out _), "out-of-range native file index rejected");
        Equal(
            false,
            new Steam2026SaveContainerProbe(_ => valid).TryProbe(
                new Steam2026SaveContainerCandidate(0, "save00.ff7", Path.Combine(Path.GetTempPath(), "save01.ff7"), false, -1),
                out _),
            "mismatched native candidate basename rejected");
        Equal(
            false,
            new Steam2026SaveContainerProbe(_ => valid).TryProbe(
                new Steam2026SaveContainerCandidate(0, "save00.ff7", "save00.ff7\0", false, -1),
                out _),
            "invalid native candidate path fails closed");
    }

    private static void PublicConstructionRequiresExactFingerprint(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        var constructors = typeof(Steam2026SaveContainerProbe).GetConstructors();
        Equal(1, constructors.Length, "save probe public constructor count");
        Equal(
            typeof(Steam2026FingerprintResult),
            constructors[0].GetParameters()[0].ParameterType,
            "save probe public constructor requires exact fingerprint");
        _ = new Steam2026SaveContainerProbe(supported);
        Equal(
            true,
            Throws<ArgumentException>(() => _ = new Steam2026SaveContainerProbe(unsupported)),
            "legacy fingerprint rejected by native save probe");
    }

    private static void KeepsProbeContentBlindAndCapabilityNeutral(
        Steam2026FingerprintResult supported)
    {
        var snapshotProperties = typeof(Steam2026SaveContainerContractSnapshot)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        SequenceEqual(
            ["VerifiedOccupiedSlotIndices", "StaticAutosaveSlotIsOccupied"],
            snapshotProperties,
            "save contract snapshot exposes only normalized occupancy evidence");
        var forbiddenFragments = new[]
        {
            "Name", "Location", "Level", "Gil", "Time", "Party", "Hp", "Mp", "Preview", "Bytes", "Payload",
            "Raw", "SelectionByte", "Mask"
        };
        foreach (var fragment in forbiddenFragments)
        {
            Equal(
                false,
                snapshotProperties.Any(name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
                $"save contract snapshot exposes no {fragment} content");
        }

        var type = typeof(Steam2026SaveContainerProbe);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(type), "save probe is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(type), "save probe is not an event sink");
        Equal(
            false,
            type.GetMethods().Any(method =>
                method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Convert", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)),
            "save probe exposes no write, conversion, or speech API");
        using var backend = new Steam2026X64RuntimeBackend(supported);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "save contract evidence does not enable capabilities");
    }

    private static Steam2026SaveContainerCandidate Candidate(int fileIndex)
    {
        var fileName = $"save{fileIndex:00}.ff7";
        return new Steam2026SaveContainerCandidate(
            fileIndex,
            fileName,
            Path.Combine(Path.GetTempPath(), fileName),
            fileIndex == 9,
            fileIndex == 9 ? 14 : -1);
    }

    private static byte[] CreateContainer(uint occupancyMask, byte rawSelection)
    {
        var bytes = new byte[Steam2026SaveContainerProbe.ContainerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Steam2026SaveContainerProbe.Magic);
        bytes[4] = rawSelection;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(5), occupancyMask);
        for (var slot = 0; slot < Steam2026SaveContainerProbe.SlotsPerContainer; slot++)
        {
            if ((occupancyMask & (1u << slot)) == 0)
            {
                continue;
            }

            var slotOffset = Steam2026SaveContainerProbe.HeaderSize + (slot * Steam2026SaveContainerProbe.SlotSize);
            var payload = bytes.AsSpan(slotOffset + sizeof(uint), Steam2026SaveContainerProbe.SlotSize - sizeof(uint));
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] = checked((byte)((slot + index) % 251));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(slotOffset),
                CalculateChecksum(payload));
        }

        return bytes;
    }

    private static byte[] Mutate(
        byte[] source,
        int offset,
        Func<byte, byte> mutation,
        uint? writeUInt32 = null)
    {
        var copy = (byte[])source.Clone();
        if (writeUInt32.HasValue)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), writeUInt32.Value);
        }
        else
        {
            copy[offset] = mutation(copy[offset]);
        }

        return copy;
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

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
