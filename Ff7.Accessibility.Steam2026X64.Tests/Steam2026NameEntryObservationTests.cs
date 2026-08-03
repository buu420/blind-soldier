using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;
using Ff7.Accessibility.Steam2026X64.Runtime.NameEntry;

internal static class Steam2026NameEntryObservationTests
{
    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        ReadsEquivalentStablePointerFreeSnapshots(supported);
        PublicConstructionRequiresExactFingerprintAndResolver(supported, unsupported);
        RejectsUnreadableAndRemappedGuestDomains(supported);
        RejectsTornOrInvalidEditorState(supported);
        ConvertsCheckedNameEntrySnapshotsIntoNativeSpeech();
        RetriesNativeNameSpeechAfterOutputFailure();
        SpeaksTheVisibleNameEntryPromptFromTranslatedDraws();
        KeepsResearchSurfaceHookSpeechAndCapabilityFree(supported);
    }

    private static void ConvertsCheckedNameEntrySnapshotsIntoNativeSpeech()
    {
        var spoken = new List<(string Text, bool Interrupt)>();
        var logged = new List<string>();
        var coordinator = new Steam2026NameEntrySpeechCoordinator(
            enabled: true,
            initialAnnouncementDelay: TimeSpan.Zero,
            (text, interrupt) => spoken.Add((text, interrupt)),
            logged.Add);
        var now = DateTime.SpecifyKind(new DateTime(2026, 7, 21, 12, 0, 0), DateTimeKind.Utc);
        var fixture = CreatePopulatedNameEntryFixture();
        fixture.Write((uint)NameEntryStateReader.AddressCommandRow, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressGridColumn, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressGridRow, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressSelectedSlot, [5]);
        fixture.Write(
            (uint)NameEntryStateReader.AddressNameBuffer,
            [0x23, 0x4C, 0x4F, 0x55, 0x44, 0xFF, 0xFF, 0xFF, 0xFF]);
        var reader = new Steam2026NameEntryObservationReader(fixture.Direct);
        Equal(true, reader.TryReadSnapshot(out var initial), "checked active name-entry speech fixture");

        coordinator.Observe(initial, isHostForeground: true, now);
        Equal(0, spoken.Count, "name-entry activation waits for one stable checked sample");
        coordinator.Observe(initial, isHostForeground: true, now.AddMilliseconds(1));
        Equal(1, spoken.Count, "stable checked name-entry activation speaks");
        Equal(true, spoken[0].Interrupt, "name-entry selection interrupts stale menu speech");
        Equal(
            "Current name: Cloud. Character grid: capital A.",
            spoken[0].Text,
            "initial name-entry speech includes the native name and sighted grid selection");

        fixture.Write((uint)NameEntryStateReader.AddressFocus, BitConverter.GetBytes(1));
        fixture.Write((uint)NameEntryStateReader.AddressCommandRow, BitConverter.GetBytes(1));
        Equal(true, reader.TryReadSnapshot(out var moved), "checked moved name-entry speech fixture");
        coordinator.Observe(moved, isHostForeground: true, now.AddMilliseconds(2));
        Equal(2, spoken.Count, "native command-row movement speaks immediately");
        Equal("Command: Delete.", spoken[1].Text, "native command selection text");
        Equal(
            true,
            logged.Any(line => line.Contains("Name entry native speech", StringComparison.Ordinal)),
            "x64 name-entry speech is diagnosable");

        coordinator.Observe(null, isHostForeground: true, now.AddMilliseconds(3));
        coordinator.Observe(initial, isHostForeground: true, now.AddMilliseconds(4));
        Equal(2, spoken.Count, "an unreadable ownership boundary resets stale editor state");
    }

    private static void RetriesNativeNameSpeechAfterOutputFailure()
    {
        var attempts = 0;
        var delivered = new List<string>();
        var coordinator = new Steam2026NameEntrySpeechCoordinator(
            enabled: true,
            initialAnnouncementDelay: TimeSpan.Zero,
            (text, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("synthetic speech failure");
                }

                delivered.Add(text);
            },
            _ => { });
        var now = DateTime.SpecifyKind(
            new DateTime(2026, 7, 21, 12, 30, 0),
            DateTimeKind.Utc);
        var fixture = CreatePopulatedNameEntryFixture();
        fixture.Write((uint)NameEntryStateReader.AddressGridColumn, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressGridRow, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressCommandRow, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressSelectedSlot, [5]);
        fixture.Write(
            (uint)NameEntryStateReader.AddressNameBuffer,
            [0x23, 0x4C, 0x4F, 0x55, 0x44, 0xFF, 0xFF, 0xFF, 0xFF]);
        var reader = new Steam2026NameEntryObservationReader(fixture.Direct);
        Equal(true, reader.TryReadSnapshot(out var snapshot), "retryable native name snapshot");

        coordinator.Observe(snapshot, isHostForeground: true, now);
        try
        {
            coordinator.Observe(snapshot, isHostForeground: true, now.AddMilliseconds(1));
        }
        catch (InvalidOperationException)
        {
        }

        coordinator.Observe(snapshot, isHostForeground: true, now.AddMilliseconds(2));
        Equal(2, attempts, "native name speech retries output failure");
        Equal(1, delivered.Count, "retried native name speech is delivered once");
        Equal(
            "Current name: Cloud. Character grid: capital A.",
            delivered[0],
            "retried native name speech preserves its exact pending text");
    }

    private static void SpeaksTheVisibleNameEntryPromptFromTranslatedDraws()
    {
        var spoken = new List<(string Text, bool Interrupt)>();
        var logged = new List<string>();
        var coordinator = new Steam2026NameEntryPromptSpeechCoordinator(
            enabled: true,
            stableTime: TimeSpan.FromMilliseconds(100),
            (text, interrupt) => spoken.Add((text, interrupt)),
            logged.Add);
        var now = DateTime.SpecifyKind(
            new DateTime(2026, 7, 21, 12, 0, 0),
            DateTimeKind.Utc);
        var prompt = new TranslatedMenuTextObservation(
            Steam2026MenuCallbackKind.EncodedTextB,
            "Please enter a name.",
            53,
            30,
            7,
            0x3DCED917);

        coordinator.SetOwnership(true);
        coordinator.Observe(new TranslatedMenuIngressSnapshot(
            Steam2026MenuCallbackKind.EncodedTextB,
            1,
            now,
            null,
            null,
            prompt));
        coordinator.Observe(new TranslatedMenuIngressSnapshot(
            Steam2026MenuCallbackKind.EncodedTextB,
            2,
            now.AddMilliseconds(50),
            null,
            null,
            prompt));
        coordinator.Poll(now.AddMilliseconds(120));

        Equal(1, spoken.Count, "translated name-entry prompt speaks once");
        Equal("Please enter a name.", spoken[0].Text, "translated name-entry prompt text");
        Equal(true, spoken[0].Interrupt, "translated name-entry prompt interrupts stale speech");
        Equal(
            true,
            logged.Any(line => line.Contains("name-entry prompt", StringComparison.OrdinalIgnoreCase)),
            "translated name-entry prompt is diagnosable");

        var revokedSpoken = new List<string>();
        var revoked = new Steam2026NameEntryPromptSpeechCoordinator(
            enabled: true,
            stableTime: TimeSpan.FromMilliseconds(100),
            (text, _) => revokedSpoken.Add(text),
            _ => { });
        revoked.SetOwnership(true);
        revoked.Observe(new TranslatedMenuIngressSnapshot(
            Steam2026MenuCallbackKind.EncodedTextB,
            1,
            now,
            null,
            null,
            prompt));
        revoked.Observe(new TranslatedMenuIngressSnapshot(
            Steam2026MenuCallbackKind.EncodedTextB,
            2,
            now.AddMilliseconds(50),
            null,
            null,
            prompt));
        revoked.SetOwnership(false);
        revoked.Poll(now.AddMilliseconds(120));
        Equal(0, revokedSpoken.Count, "revoked name-entry prompt ownership clears queued speech");
    }

    private static void ReadsEquivalentStablePointerFreeSnapshots(
        Steam2026FingerprintResult supported)
    {
        var fixture = CreatePopulatedNameEntryFixture();
        var directReader = new Steam2026NameEntryObservationReader(fixture.Direct);
        var translatedReader = new Steam2026NameEntryObservationReader(
            supported,
            FieldObservationFixture.ModuleBase,
            fixture.Native);

        Equal(true, directReader.TryReadSnapshot(out var direct), "direct name-entry snapshot");
        Equal(true, translatedReader.TryReadSnapshot(out var translated), "translated name-entry snapshot");
        Equal(direct, translated, "direct and translated name-entry snapshots match");
        Equal(true, translated.IsActive, "translated name-entry active");
        Equal(2, translated.GridColumn, "translated name-entry grid column");
        Equal(3, translated.GridRow, "translated name-entry grid row");
        Equal(1, translated.CommandRow, "translated name-entry command row");
        Equal(4, translated.SelectedSlot, "translated name-entry selected slot");
        SequenceEqual(
            new byte[] { 0x23, 0x24, 0xFF, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66 },
            translated.NameBuffer,
            "translated native name buffer");

        fixture.Write((uint)NameEntryStateReader.AddressNameBuffer, [0x44]);
        Equal((byte)0x23, translated.NameBuffer[0], "published x64 name buffer is immutable");

        var inactive = FieldObservationFixture.CreatePopulated();
        inactive.Write((uint)NameEntryStateReader.AddressMenuState, [0]);
        var inactiveReader = new Steam2026NameEntryObservationReader(inactive.Direct);
        Equal(true, inactiveReader.TryReadSnapshot(out var inactiveSnapshot), "stable inactive name-entry ownership");
        Equal(false, inactiveSnapshot.IsActive, "inactive name-entry snapshot");
        Equal(0, inactiveSnapshot.NameBuffer.Length, "inactive snapshot publishes no stale editor bytes");
    }

    private static void PublicConstructionRequiresExactFingerprintAndResolver(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        var constructors = typeof(Steam2026NameEntryObservationReader).GetConstructors();
        Equal(1, constructors.Length, "name-entry facade public constructor count");
        Equal(
            typeof(Steam2026FingerprintResult),
            constructors[0].GetParameters()[0].ParameterType,
            "name-entry facade public constructor requires exact fingerprint evidence");

        var unsupportedFixture = CreatePopulatedNameEntryFixture();
        Equal(
            true,
            Throws<ArgumentException>(() => _ = new Steam2026NameEntryObservationReader(
                unsupported,
                FieldObservationFixture.ModuleBase,
                unsupportedFixture.Native)),
            "legacy fingerprint cannot construct x64 name-entry facade");

        var badResolver = CreatePopulatedNameEntryFixture();
        badResolver.Native.Write(
            FieldObservationFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            [0x90]);
        Equal(
            true,
            Throws<InvalidOperationException>(() => _ = new Steam2026NameEntryObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                badResolver.Native)),
            "corrupt translated resolver cannot construct name-entry facade");
    }

    private static void RejectsUnreadableAndRemappedGuestDomains(
        Steam2026FingerprintResult supported)
    {
        var addresses = new uint[]
        {
            (uint)NameEntryStateReader.AddressCurrentModule,
            (uint)NameEntryStateReader.AddressMenuState,
            (uint)NameEntryStateReader.AddressFocus,
            (uint)NameEntryStateReader.AddressGridColumn,
            (uint)NameEntryStateReader.AddressGridRow,
            (uint)NameEntryStateReader.AddressCommandRow,
            (uint)NameEntryStateReader.AddressSelectedSlot,
            (uint)NameEntryStateReader.AddressNameBuffer
        };

        for (var index = 0; index < addresses.Length; index++)
        {
            var address = addresses[index];
            var unmapped = CreatePopulatedNameEntryFixture();
            unmapped.UnmapGuestPage(address);
            var unmappedReader = new Steam2026NameEntryObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                unmapped.Native);
            Equal(false, unmappedReader.TryReadSnapshot(out _), $"unmapped name-entry domain 0x{address:X8}");

            var remapped = CreatePopulatedNameEntryFixture();
            var watchedEntry = remapped.GetPageTableEntryAddress(address);
            var replacementHostPage = 0x0000000900000000 + ((ulong)index * 0x2000);
            var remappingMemory = new RemappingNativeMemoryReader(
                remapped.Native,
                watchedEntry,
                triggerRead: 2,
                () => remapped.MapGuestPage(address, replacementHostPage));
            var remappedReader = new Steam2026NameEntryObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                remappingMemory);
            Equal(false, remappedReader.TryReadSnapshot(out _), $"remapped name-entry domain 0x{address:X8}");
        }
    }

    private static void RejectsTornOrInvalidEditorState(
        Steam2026FingerprintResult supported)
    {
        var cases = new (uint Address, byte[] Replacement, string Label)[]
        {
            ((uint)NameEntryStateReader.AddressCurrentModule, [1], "torn name-entry module"),
            ((uint)NameEntryStateReader.AddressMenuState, [0], "torn name-entry menu state"),
            ((uint)NameEntryStateReader.AddressFocus, BitConverter.GetBytes(1), "torn name-entry focus"),
            ((uint)NameEntryStateReader.AddressGridColumn, BitConverter.GetBytes(4), "torn name-entry grid column"),
            ((uint)NameEntryStateReader.AddressGridRow, BitConverter.GetBytes(5), "torn name-entry grid row"),
            ((uint)NameEntryStateReader.AddressCommandRow, BitConverter.GetBytes(2), "torn name-entry command row"),
            ((uint)NameEntryStateReader.AddressSelectedSlot, [5], "torn name-entry selected slot"),
            ((uint)NameEntryStateReader.AddressNameBuffer, [0x23, 0x25, 0xFF, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66], "torn name-entry buffer")
        };

        foreach (var testCase in cases)
        {
            var fixture = CreatePopulatedNameEntryFixture();
            var watchedHostAddress = fixture.GetHostAddress(testCase.Address);
            var tearingMemory = new TearingNativeMemoryReader(
                fixture.Native,
                watchedHostAddress,
                triggerRead: 2,
                () => fixture.Write(testCase.Address, testCase.Replacement));
            var reader = new Steam2026NameEntryObservationReader(
                supported,
                FieldObservationFixture.ModuleBase,
                tearingMemory);
            Equal(false, reader.TryReadSnapshot(out _), testCase.Label);
        }

        var invalidSlot = CreatePopulatedNameEntryFixture();
        invalidSlot.Write(
            (uint)NameEntryStateReader.AddressSelectedSlot,
            [NameEntryStateReader.NameSlotCount]);
        var invalidSlotReader = new Steam2026NameEntryObservationReader(
            supported,
            FieldObservationFixture.ModuleBase,
            invalidSlot.Native);
        Equal(false, invalidSlotReader.TryReadSnapshot(out _), "invalid native name-entry slot");
    }

    private static void KeepsResearchSurfaceHookSpeechAndCapabilityFree(
        Steam2026FingerprintResult supported)
    {
        var readerType = typeof(Steam2026NameEntryObservationReader);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(readerType), "name-entry facade is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(readerType), "name-entry facade is not an event sink");
        Equal(
            false,
            readerType.GetMethods().Any(method => method.Name.Contains("Hook", StringComparison.OrdinalIgnoreCase)),
            "name-entry facade exposes no hooks");
        Equal(
            false,
            readerType.GetMethods().Any(method => method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)),
            "name-entry facade exposes no speech");

        using var backend = new Steam2026X64RuntimeBackend(supported);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "name-entry evidence does not enable capabilities");
    }

    private static FieldObservationFixture CreatePopulatedNameEntryFixture()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Write((uint)NameEntryStateReader.AddressCurrentModule, [NameEntryStateReader.NameEntryModule]);
        fixture.Write((uint)NameEntryStateReader.AddressMenuState, [1]);
        fixture.Write((uint)NameEntryStateReader.AddressFocus, BitConverter.GetBytes(0));
        fixture.Write((uint)NameEntryStateReader.AddressGridColumn, BitConverter.GetBytes(2));
        fixture.Write((uint)NameEntryStateReader.AddressGridRow, BitConverter.GetBytes(3));
        fixture.Write((uint)NameEntryStateReader.AddressCommandRow, BitConverter.GetBytes(1));
        fixture.Write((uint)NameEntryStateReader.AddressSelectedSlot, [4]);
        fixture.Write(
            (uint)NameEntryStateReader.AddressNameBuffer,
            [0x23, 0x24, 0xFF, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66]);
        return fixture;
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
