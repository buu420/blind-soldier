using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldNavigationObservationTests
{
    private static readonly uint GatewayTable =
        FieldObservationFixture.TriggerPointer + FieldGatewayTargetReader.GatewaysOffset;

    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        ReadsEquivalentCoherentPointerFreeNavigationSnapshots(supported);
        PublicConstructionRequiresExactFingerprintAndResolver(supported, unsupported);
        RejectsUnmappedAndOverflowingGatewayDomains(supported);
        RejectsTranslatedPageRemapping(supported);
        RejectsTornAndInconsistentSnapshots(supported);
        KeepsNavigationResearchSurfaceImmutableAndCapabilityNeutral(supported);
    }

    private static void ReadsEquivalentCoherentPointerFreeNavigationSnapshots(
        Steam2026FingerprintResult supported)
    {
        var fixture = CreatePopulatedNavigationFixture();
        var directReader = new Steam2026FieldNavigationObservationReader(fixture.Direct);
        var translatedReader = new Steam2026FieldNavigationObservationReader(
            supported,
            FieldObservationFixture.ModuleBase,
            fixture.Native);

        Equal(true, directReader.TryReadSnapshot(16, out var direct), "direct navigation snapshot");
        Equal(true, translatedReader.TryReadSnapshot(16, out var translated), "translated navigation snapshot");
        Equal(direct, translated, "direct and translated navigation snapshots match");

        Equal(116, translated.Position.FieldId, "navigation field id");
        Equal(1, translated.Position.PlayerModelId, "navigation player model id");
        Equal(100, translated.Position.X, "navigation player X");
        Equal(-200, translated.Position.Y, "navigation player Y");
        Equal(300, translated.Position.Z, "navigation player Z");
        Equal((ushort)9, translated.Position.TriangleId, "navigation player triangle");
        Equal((byte)0xC0, translated.Position.Direction, "navigation player direction");
        Equal(-96, translated.Control.SignedControlDirection, "navigation control transform");
        Equal(16, translated.Boundary.TriangleCount, "navigation boundary triangle count");
        SequenceEqual([0, 2, 15], translated.Boundary.ActiveTriangleIds, "navigation active boundaries");

        Equal(2, translated.Gateways.Count, "stable gateway count");
        Equal("Exit", translated.Gateways[0].VisibleLabel, "first generic gateway label");
        Equal(20, translated.Gateways[0].X, "first gateway midpoint X");
        Equal(30, translated.Gateways[0].Y, "first gateway midpoint Y");
        Equal(40, translated.Gateways[0].Z, "first gateway midpoint Z");
        Equal(117, translated.Gateways[0].DestinationFieldId, "first destination metadata");
        Equal(0, translated.Gateways[0].GatewayIndex, "first gateway index metadata");
        Equal("Exit", translated.Gateways[1].VisibleLabel, "second generic gateway label");
        Equal(118, translated.Gateways[1].DestinationFieldId, "second destination metadata");
        Equal(
            false,
            translated.Gateways.Any(gateway => gateway.VisibleLabel.Any(char.IsDigit)),
            "destination identifiers are never visible text");
    }

    private static void PublicConstructionRequiresExactFingerprintAndResolver(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        var constructors = typeof(Steam2026FieldNavigationObservationReader).GetConstructors();
        Equal(1, constructors.Length, "navigation facade public constructor count");
        Equal(
            typeof(Steam2026FingerprintResult),
            constructors[0].GetParameters()[0].ParameterType,
            "navigation facade public constructor requires fingerprint");

        var unsupportedFixture = CreatePopulatedNavigationFixture();
        Equal(
            true,
            Throws<ArgumentException>(() => _ = new Steam2026FieldNavigationObservationReader(
                unsupported,
                FieldObservationFixture.ModuleBase,
                unsupportedFixture.Native)),
            "navigation facade rejects unsupported fingerprint");

        var wrongResolver = CreatePopulatedNavigationFixture();
        wrongResolver.Native.Write(
            FieldObservationFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            [0x90]);
        Equal(
            true,
            Throws<InvalidOperationException>(() => _ = new Steam2026FieldNavigationObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                wrongResolver.Native)),
            "navigation facade rejects unexpected translated resolver signature");
    }

    private static void RejectsUnmappedAndOverflowingGatewayDomains(
        Steam2026FingerprintResult supported)
    {
        var cases = new (uint GuestAddress, string Label)[]
        {
            ((uint)FieldPositionReader.AddressCurrentModule, "unmapped navigation module"),
            ((uint)FieldPositionReader.AddressFieldId, "unmapped navigation field"),
            (FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, "unmapped navigation position"),
            (FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, "unmapped navigation control"),
            (FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, "unmapped navigation boundary"),
            (GatewayTable, "unmapped navigation gateway table")
        };

        foreach (var testCase in cases)
        {
            var fixture = CreatePopulatedNavigationFixture();
            fixture.UnmapGuestPage(testCase.GuestAddress);
            var reader = new Steam2026FieldNavigationObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                fixture.Native);
            Equal(false, reader.TryReadSnapshot(16, out var snapshot), testCase.Label);
            Equal<Steam2026FieldNavigationResearchSnapshot?>(
                null,
                snapshot,
                $"{testCase.Label} publishes no partial snapshot");
        }

        var overflowing = CreatePopulatedNavigationFixture();
        const uint overflowingTriggerPointer = uint.MaxValue - 0x20u;
        overflowing.Write(
            (uint)FieldNavigationControlReader.AddressFieldTriggersPtr,
            BitConverter.GetBytes(overflowingTriggerPointer));
        overflowing.Write(
            overflowingTriggerPointer + FieldNavigationControlReader.ControlDirectionOffset,
            [0xA0]);
        var overflowReader = new Steam2026FieldNavigationObservationReader(
            supported,
            FieldObservationFixture.ModuleBase,
            overflowing.Native);
        Equal(false, overflowReader.TryReadSnapshot(16, out _), "overflowing gateway table rejected");
        Equal(false, overflowReader.TryReadSnapshot(0, out _), "zero verified triangle count rejected");
        Equal(
            false,
            overflowReader.TryReadSnapshot(FieldBoundaryStateReader.MaximumTriangleCount + 1, out _),
            "oversized verified triangle count rejected");
    }

    private static void RejectsTranslatedPageRemapping(Steam2026FingerprintResult supported)
    {
        var cases = new (uint GuestAddress, string Label)[]
        {
            (FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, "remapped navigation position"),
            (FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, "remapped navigation control"),
            (FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, "remapped navigation boundary"),
            (GatewayTable, "remapped navigation gateway table")
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var testCase = cases[index];
            var fixture = CreatePopulatedNavigationFixture();
            var watchedEntry = fixture.GetPageTableEntryAddress(testCase.GuestAddress);
            var replacementHostPage = 0x0000000780000000 + ((ulong)index * 0x2000);
            var remapping = new RemappingNativeMemoryReader(
                fixture.Native,
                watchedEntry,
                triggerRead: 2,
                () => fixture.MapGuestPage(testCase.GuestAddress, replacementHostPage));
            var reader = new Steam2026FieldNavigationObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                remapping);

            Equal(false, reader.TryReadSnapshot(16, out _), testCase.Label);
        }
    }

    private static void RejectsTornAndInconsistentSnapshots(Steam2026FingerprintResult supported)
    {
        var cases = new (
            uint WatchedGuestAddress,
            uint MutatedGuestAddress,
            byte[] Replacement,
            string Label)[]
        {
            ((uint)FieldPositionReader.AddressFieldId, (uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)117), "torn navigation field"),
            (FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, BitConverter.GetBytes(101), "torn navigation position"),
            (FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, [0x80], "torn navigation control"),
            (FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, [0x04], "torn navigation boundary"),
            (GatewayTable, GatewayTable + FieldGatewayTargetReader.DestinationFieldOffset, BitConverter.GetBytes((short)119), "torn navigation gateway")
        };

        foreach (var testCase in cases)
        {
            var fixture = CreatePopulatedNavigationFixture();
            var watchedHostAddress = fixture.GetHostAddress(testCase.WatchedGuestAddress);
            var tearing = new TearingNativeMemoryReader(
                fixture.Native,
                watchedHostAddress,
                triggerRead: 2,
                () => fixture.Write(testCase.MutatedGuestAddress, testCase.Replacement));
            var reader = new Steam2026FieldNavigationObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                tearing);

            Equal(false, reader.TryReadSnapshot(16, out var snapshot), testCase.Label);
            Equal<Steam2026FieldNavigationResearchSnapshot?>(
                null,
                snapshot,
                $"{testCase.Label} publishes no partial snapshot");
        }
    }

    private static void KeepsNavigationResearchSurfaceImmutableAndCapabilityNeutral(
        Steam2026FingerprintResult supported)
    {
        var fixture = CreatePopulatedNavigationFixture();
        var reader = new Steam2026FieldNavigationObservationReader(
            supported,
            FieldObservationFixture.ModuleBase,
            fixture.Native);
        Equal(true, reader.TryReadSnapshot(16, out var snapshot), "immutable navigation fixture snapshot");

        var mutableGateways = (IList<Steam2026FieldGatewayResearchSnapshot>)snapshot.Gateways;
        Equal(
            true,
            Throws<NotSupportedException>(() => mutableGateways[0] = mutableGateways[1]),
            "published gateway collection rejects mutation");
        var mutableBoundaries = (IList<int>)snapshot.Boundary.ActiveTriangleIds;
        Equal(
            true,
            Throws<NotSupportedException>(() => mutableBoundaries[0] = 1),
            "published boundary collection rejects mutation");

        var outputTypes = new[]
        {
            typeof(Steam2026FieldNavigationResearchSnapshot),
            typeof(Steam2026FieldPositionResearchSnapshot),
            typeof(Steam2026FieldControlResearchSnapshot),
            typeof(Steam2026FieldBoundaryResearchSnapshot),
            typeof(Steam2026FieldGatewayResearchSnapshot)
        };
        foreach (var outputType in outputTypes)
        {
            Equal(true, outputType.IsSealed, $"{outputType.Name} is sealed");
            foreach (var property in outputType.GetProperties())
            {
                Equal(false, property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase), $"{outputType.Name}.{property.Name} is pointer-free");
                Equal(false, property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase), $"{outputType.Name}.{property.Name} is address-free");
                Equal(false, property.Name.Contains("Speech", StringComparison.OrdinalIgnoreCase), $"{outputType.Name}.{property.Name} is not a speech channel");
                Equal(false, property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr), $"{outputType.Name}.{property.Name} has no host pointer type");
            }
        }

        var readerType = typeof(Steam2026FieldNavigationObservationReader);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(readerType), "navigation research facade is not a backend");
        Equal(false, readerType.GetMethods().Any(method => method.Name.Contains("Hook", StringComparison.OrdinalIgnoreCase)), "navigation research facade exposes no hooks");
        Equal(false, readerType.GetMethods().Any(method => method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)), "navigation research facade exposes no speech");
        using var backend = new Steam2026X64RuntimeBackend(supported);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "navigation research does not enable capability bits");
    }

    private static FieldObservationFixture CreatePopulatedNavigationFixture()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        var table = new byte[FieldGatewayTargetReader.GatewayCount * FieldGatewayTargetReader.GatewayStride];
        for (var index = 0; index < FieldGatewayTargetReader.GatewayCount; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                table.AsSpan(
                    index * FieldGatewayTargetReader.GatewayStride +
                    FieldGatewayTargetReader.DestinationFieldOffset,
                    sizeof(short)),
                short.MaxValue);
        }

        WriteGateway(table, 0, 10, 20, 30, 30, 40, 50, 117);
        WriteGateway(table, 1, 100, 200, 300, 120, 220, 320, 118);
        fixture.Write(GatewayTable, table);
        return fixture;
    }

    private static void WriteGateway(
        Span<byte> table,
        int index,
        short x1,
        short y1,
        short z1,
        short x2,
        short y2,
        short z2,
        short destination)
    {
        var record = table.Slice(
            checked(index * FieldGatewayTargetReader.GatewayStride),
            FieldGatewayTargetReader.GatewayStride);
        BinaryPrimitives.WriteInt16LittleEndian(record, x1);
        BinaryPrimitives.WriteInt16LittleEndian(record[0x02..], y1);
        BinaryPrimitives.WriteInt16LittleEndian(record[0x04..], z1);
        BinaryPrimitives.WriteInt16LittleEndian(record[0x06..], x2);
        BinaryPrimitives.WriteInt16LittleEndian(record[0x08..], y2);
        BinaryPrimitives.WriteInt16LittleEndian(record[0x0A..], z2);
        BinaryPrimitives.WriteInt16LittleEndian(record[FieldGatewayTargetReader.DestinationFieldOffset..], destination);
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

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
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
}
