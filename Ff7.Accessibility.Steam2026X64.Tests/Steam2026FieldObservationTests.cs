using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldObservationTests
{
    private const uint AddressFieldWindowLifecyclePhases = 0x00CFF5E4;
    private const uint FieldWindowLifecycleStride = 0x30;

    private static Steam2026FingerprintResult supportedFingerprint = null!;
    private static Steam2026FingerprintResult unsupportedFingerprint = null!;

    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        supportedFingerprint = supported;
        unsupportedFingerprint = unsupported;
        ReadsEquivalentPointerFreeSnapshotsFromDirectAndTranslatedMemory();
        NormalizesOnlyCompleteNativeStateWithoutScalingCoordinates();
        ReacquiresFieldHotkeysAfterStalePostBattleMessageCount();
        ReleasesAssignedWindowAtClosedLifecyclePhase();
        KeepsAssignedWindowAtActiveLifecyclePhase();
        ReleasesMovableNewPageWindowAtPhaseFourteen();
        ReleasesOnlyPermanentNonClosableWindowAtCompletedPhase();
        KeepsRealOrUnverifiableDialogueFailClosed();
        UsesLaterCoordinatesForIndependentMovementFallback();
        RejectsUnmappedTranslatedDomains();
        RejectsTranslatedPageRemapping();
        RejectsTornNativeDomainState();
        RejectsInvalidCallerSuppliedTriangleCounts();
        PublicReaderRequiresExactTranslatedResolver();
        KeepsResearchSurfaceHookSpeechAndCapabilityFree();
    }

    private static void ReadsEquivalentPointerFreeSnapshotsFromDirectAndTranslatedMemory()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        var directReader = new Steam2026FieldObservationReader(fixture.Direct);
        var translatedReader = new Steam2026FieldObservationReader(
            supportedFingerprint,
            FieldObservationFixture.ModuleBase,
            fixture.Native);

        Equal(true, directReader.TryReadResearchSnapshot(16, out var direct), "direct field research snapshot");
        Equal(true, translatedReader.TryReadResearchSnapshot(16, out var translated), "translated field research snapshot");
        Equal(direct, translated, "direct and translated research snapshots match");

        Equal(116, translated.Position.FieldId, "native field id");
        Equal(1, translated.Position.PlayerModelId, "native player model id");
        Equal(100, translated.Position.X, "native field X");
        Equal(-200, translated.Position.Y, "native field Y");
        Equal(300, translated.Position.Z, "native field Z");
        Equal((ushort)9, translated.Position.TriangleId, "native triangle id");
        Equal((byte)0xC0, translated.Position.Direction, "native facing direction");
        Equal(1, translated.Script.EntityId, "native script entity id");
        Equal(3, translated.Script.ScriptId, "native script id");
        Equal(0x10, translated.Script.ByteIndex, "native script byte index");
        Equal((byte)0x40, translated.Script.Opcode, "native script opcode");
        Equal(false, translated.Cue.IsSuppressed, "native cue suppression state");
        Equal("gameplay", translated.Cue.Reason, "native cue reason");
        Equal(-96, translated.Control.SignedControlDirection, "native signed control direction");
        Equal(16, translated.Boundary!.TriangleCount, "verified boundary triangle count");
        SequenceEqual([0, 2, 15], translated.Boundary.ActiveTriangleIds, "native active boundary triangles");

        var outputTypes = new[]
        {
            typeof(Steam2026FieldResearchSnapshot),
            typeof(Steam2026FieldPositionResearchSnapshot),
            typeof(Steam2026FieldScriptResearchSnapshot),
            typeof(Steam2026FieldCueResearchSnapshot),
            typeof(Steam2026FieldControlResearchSnapshot),
            typeof(Steam2026FieldBoundaryResearchSnapshot)
        };
        foreach (var outputType in outputTypes)
        {
            foreach (var property in outputType.GetProperties())
            {
                Equal(false, property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase), $"{outputType.Name}.{property.Name} is pointer-free");
                Equal(false, property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase), $"{outputType.Name}.{property.Name} is address-free");
                Equal(false, property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr), $"{outputType.Name}.{property.Name} has no host pointer type");
            }
        }
    }

    private static void NormalizesOnlyCompleteNativeStateWithoutScalingCoordinates()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        var reader = new Steam2026FieldObservationReader(
            supportedFingerprint,
            FieldObservationFixture.ModuleBase,
            fixture.Native);

        Equal(true, reader.TryReadFieldFrame(out var withoutBoundary), "field frame without unverified boundary metadata");
        Equal(true, reader.TryReadFieldFrame(16, out var withBoundary), "field frame with verified boundary metadata");
        Equal(withoutBoundary, withBoundary, "optional boundary evidence does not rewrite normalized field state");
        Equal(
            new FieldFrameObservation(116, 1, 100f, -200f, 300f, 9, true, 1, 3, 0x10),
            withBoundary,
            "normalized field frame preserves native values without scaling");

        fixture.WriteByte(FieldAudibleCueStateReader.AddressUserControl, 1);
        Equal(true, reader.TryReadFieldFrame(out var locked), "coherent control-lock field frame");
        Equal(false, locked.HasControl, "native scripted control lock is preserved");

        fixture.UnmapGuestPage(FieldObservationFixture.ScriptPointer);
        Equal(
            true,
            reader.TryReadFieldFrame(out var lockedMovementFrame),
            "independent movement still publishes a checked scripted-control frame");
        Equal(false, lockedMovementFrame.HasControl, "movement fallback preserves the checked control lock");

        fixture.WriteByte(FieldAudibleCueStateReader.AddressUserControl, 0);
        Equal(
            true,
            reader.TryReadFieldFrame(out var movementFrame),
            "independent movement remains available when script state is unavailable");
        Equal(
            new FieldFrameObservation(116, 1, 100f, -200f, 300f, 9, true, -1, -1, -1),
            movementFrame,
            "movement fallback preserves checked native position without inventing script state");
        Equal(
            false,
            reader.TryReadResearchSnapshot(out _),
            "incomplete script state still rejects the aggregate research snapshot");
    }

    private static void ReacquiresFieldHotkeysAfterStalePostBattleMessageCount()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            fixture,
            activeMessageCount: 1,
            hasReadableWindow: false);
        var reader = new Steam2026FieldObservationReader(fixture.Direct);

        Equal(
            true,
            reader.TryReadResearchSnapshot(out var snapshot),
            "stale post-battle dialogue count remains a coherent field snapshot");
        Equal(
            false,
            snapshot.Cue.IsSuppressed,
            "stale post-battle dialogue count without a visible window releases field hotkeys");
        Equal("gameplay", snapshot.Cue.Reason, "stale post-battle dialogue count cue reason");
        Equal(
            true,
            reader.TryReadFieldFrame(out var frame),
            "stale post-battle dialogue count publishes a field frame");
        Equal(true, frame.HasControl, "stale post-battle dialogue count restores field control");

        var cue = new FieldAudibleCueState(
            snapshot.Cue.IsSuppressed,
            snapshot.Cue.Reason,
            FieldPositionReader.FieldModule,
            snapshot.Cue.UserControl,
            snapshot.Cue.ActiveMessageCount,
            snapshot.Cue.MovieActive);
        Equal(
            false,
            Steam2026FieldNavigationCoordinator.IsNavigationSuppressed(
                cue,
                FieldLadderStateSnapshot.NotMounted,
                isLadderStateCoherent: true),
            "stale post-battle dialogue count does not discard the six navigation hotkeys");

        Equal(
            "Steam2026FieldAudibleCueStateReader",
            GetCueReaderTypeName(typeof(Steam2026FieldObservationReader)),
            "field publication uses checked dialogue-window ownership");
        Equal(
            "Steam2026FieldAudibleCueStateReader",
            GetCueReaderTypeName(typeof(Steam2026FieldObjectObservationReader)),
            "field object cues use checked dialogue-window ownership");
        Equal(
            "Steam2026FieldAudibleCueStateReader",
            GetCueReaderTypeName(typeof(Steam2026FieldNavigationCoordinator)),
            "field navigation actions use checked dialogue-window ownership");
    }

    private static void ReleasesAssignedWindowAtClosedLifecyclePhase()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            fixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            lifecyclePhase: 0);
        var reader = new Steam2026FieldAudibleCueStateReader(fixture.Direct);

        Equal(
            true,
            reader.TryRead(out var cue),
            "closed lifecycle phase remains a coherent dialogue-ownership read");
        Equal(
            false,
            cue.IsSuppressed,
            "nonfree assignment at lifecycle phase zero releases field hotkeys");
        Equal("gameplay", cue.Reason, "closed lifecycle phase cue reason");
        Equal(
            true,
            reader.LastDiagnostic.Contains(
                "windows=[00/0000,FF/--,FF/--,FF/--]",
                StringComparison.Ordinal),
            "closed lifecycle diagnostic exposes raw assignment and phase");
    }

    private static void KeepsAssignedWindowAtActiveLifecyclePhase()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            fixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            lifecyclePhase: 2,
            assignedWindowIndex: 1);
        var reader = new Steam2026FieldAudibleCueStateReader(fixture.Direct);

        Equal(
            true,
            reader.TryRead(out var cue),
            "active lifecycle phase remains a coherent dialogue-ownership read");
        Equal(
            true,
            cue.IsSuppressed,
            "nonfree assignment at lifecycle phase two retains dialogue ownership");
        Equal("dialogue", cue.Reason, "active lifecycle phase cue reason");
        Equal(
            true,
            reader.LastDiagnostic.Contains(
                "windows=[FF/--,00/0002,FF/--,FF/--]",
                StringComparison.Ordinal),
            "active lifecycle diagnostic exposes the slot stride and raw phase");
    }

    private static void ReleasesMovableNewPageWindowAtPhaseFourteen()
    {
        var ambientFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            ambientFixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            lifecyclePhase: 14,
            assignedWindowIndex: 1,
            assignedWindowOwner: 4);
        var ambientReader = new Steam2026FieldAudibleCueStateReader(ambientFixture.Direct);

        Equal(
            true,
            ambientReader.TryRead(out var ambientCue),
            "movable new-page ownership remains coherent");
        Equal(
            false,
            ambientCue.IsSuppressed,
            "phase-fourteen proximity dialogue preserves field navigation while user control is enabled");
        Equal("gameplay", ambientCue.Reason, "movable new-page cue reason");
        Equal(
            true,
            ambientReader.LastDiagnostic.Contains(
                "windows=[FF/--,04/000E,FF/--,FF/--]",
                StringComparison.Ordinal),
            "movable new-page diagnostic exposes the observed shopkeeper state");

        var modalFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            modalFixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            lifecyclePhase: 14,
            assignedWindowIndex: 1,
            assignedWindowOwner: 4);
        modalFixture.WriteByte(FieldAudibleCueStateReader.AddressUserControl, 1);
        var modalReader = new Steam2026FieldAudibleCueStateReader(modalFixture.Direct);

        Equal(true, modalReader.TryRead(out var modalCue), "locked new-page ownership read");
        Equal(true, modalCue.IsSuppressed, "script-locked phase-fourteen dialogue remains modal");
        Equal("scripted control lock", modalCue.Reason, "locked new-page cue reason");
    }

    private static void ReleasesOnlyPermanentNonClosableWindowAtCompletedPhase()
    {
        var modalFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            modalFixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            lifecyclePhase: 6,
            assignedWindowIndex: 3,
            assignedWindowOwner: 2,
            lifecycleFlags: 0);
        var modalReader = new Steam2026FieldAudibleCueStateReader(modalFixture.Direct);
        Equal(true, modalReader.TryRead(out var modalCue), "modal phase-six ownership read");
        Equal(true, modalCue.IsSuppressed, "confirmable phase-six window remains modal");

        var permanentFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            permanentFixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            lifecyclePhase: 6,
            assignedWindowIndex: 3,
            assignedWindowOwner: 2,
            lifecycleFlags: 1);
        var permanentReader = new Steam2026FieldAudibleCueStateReader(permanentFixture.Direct);
        Equal(
            true,
            permanentReader.TryRead(out var permanentCue),
            "permanent non-closable phase-six ownership read");
        Equal(
            false,
            permanentCue.IsSuppressed,
            "completed permanent non-closable window releases field accessibility");
        Equal("gameplay", permanentCue.Reason, "permanent window cue reason");
        Equal(
            true,
            permanentReader.LastDiagnostic.Contains(
                "windows=[FF/--,FF/--,FF/--,02/0006]; windowFlags=[--,--,--,0001]",
                StringComparison.Ordinal),
            "permanent window diagnostic exposes owner, phase, and flags");
    }

    private static void KeepsRealOrUnverifiableDialogueFailClosed()
    {
        var visibleFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            visibleFixture,
            activeMessageCount: 1,
            hasReadableWindow: true);
        var visibleReader = new Steam2026FieldObservationReader(visibleFixture.Direct);
        Equal(
            true,
            visibleReader.TryReadResearchSnapshot(out var visible),
            "real readable dialogue remains coherent");
        Equal(true, visible.Cue.IsSuppressed, "real readable dialogue keeps field hotkeys suppressed");
        Equal("dialogue", visible.Cue.Reason, "real readable dialogue cue reason");

        var blankActiveFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            blankActiveFixture,
            activeMessageCount: 1,
            hasReadableWindow: true,
            hasReadableText: false);
        var blankActiveReader = new Steam2026FieldObservationReader(blankActiveFixture.Direct);
        Equal(
            true,
            blankActiveReader.TryReadResearchSnapshot(out var blankActive),
            "active blank dialogue window remains a coherent ownership snapshot");
        Equal(
            true,
            blankActive.Cue.IsSuppressed,
            "active blank dialogue window does not release field hotkeys");
        Equal("dialogue", blankActive.Cue.Reason, "active blank dialogue window cue reason");

        var unreadableFixture = FieldObservationFixture.CreatePopulated();
        unreadableFixture.WriteByte(
            FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
            1);
        var unreadableReader = new Steam2026FieldObservationReader(unreadableFixture.Direct);
        Equal(
            false,
            unreadableReader.TryReadResearchSnapshot(out _),
            "unreadable dialogue-window ownership remains fail closed");

        var tornFixture = FieldObservationFixture.CreatePopulated();
        WriteDialogueOwnership(
            tornFixture,
            activeMessageCount: 1,
            hasReadableWindow: false);
        var watchedWindowState = tornFixture.GetHostAddress(
            (uint)FieldMessageReader.AddressFieldWindowStates);
        var tearingMemory = new TearingNativeMemoryReader(
            tornFixture.Native,
            watchedWindowState,
            triggerRead: 2,
            () => tornFixture.Write(
                (uint)FieldMessageReader.AddressFieldWindowStates,
                [0, 0xff, 0xff, 0xff]));
        var tornReader = new Steam2026FieldObservationReader(
            supportedFingerprint,
            FieldObservationFixture.ModuleBase,
            tearingMemory);
        Equal(
            false,
            tornReader.TryReadResearchSnapshot(out _),
            "torn dialogue-window ownership remains fail closed");
    }

    private static void WriteDialogueOwnership(
        FieldObservationFixture fixture,
        byte activeMessageCount,
        bool hasReadableWindow,
        bool hasReadableText = true,
        ushort lifecyclePhase = 2,
        int assignedWindowIndex = 0,
        byte assignedWindowOwner = 0,
        ushort lifecycleFlags = 0)
    {
        const uint messageDataPointer = 0x00080000;
        var windowStates = new byte[FieldMessageReader.WindowCount];
        Array.Fill(windowStates, FieldMessageReader.FreeWindowState);
        if (hasReadableWindow)
        {
            windowStates[assignedWindowIndex] = assignedWindowOwner;
        }

        fixture.Write(
            (uint)FieldMessageReader.AddressFieldMessageDataPointer,
            BitConverter.GetBytes(messageDataPointer));
        fixture.Write(
            (uint)FieldMessageReader.AddressFieldWindowStates,
            windowStates);
        if (hasReadableWindow)
        {
            fixture.Write(
                AddressFieldWindowLifecyclePhases +
                    ((uint)assignedWindowIndex * FieldWindowLifecycleStride),
                BitConverter.GetBytes(lifecyclePhase));
            fixture.Write(
                AddressFieldWindowLifecyclePhases +
                    ((uint)assignedWindowIndex * FieldWindowLifecycleStride) + sizeof(ushort),
                BitConverter.GetBytes(lifecycleFlags));
        }

        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            fixture.Write(
                (uint)(FieldMessageReader.AddressFieldWindowMessagePointers + index * sizeof(uint)),
                BitConverter.GetBytes(
                    hasReadableWindow && index == assignedWindowIndex
                        ? messageDataPointer + 0x20u
                        : 0u));
        }

        if (hasReadableWindow)
        {
            var buffer = new byte[FieldMessageReader.FieldTextBufferLength];
            var terminatorIndex = hasReadableText ? 1 : 0;
            if (hasReadableText)
            {
                buffer[0] = 0x21;
            }

            buffer[terminatorIndex] = 0xff;
            fixture.Write(
                (uint)(FieldMessageReader.AddressFieldWindowTextBuffers +
                    (assignedWindowIndex * FieldMessageReader.WindowTextBufferStride)),
                buffer);
        }

        fixture.WriteByte(
            FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
            activeMessageCount);
    }

    private static string GetCueReaderTypeName(Type ownerType) =>
        ownerType.GetField(
            "cueReader",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)?.FieldType.Name
        ?? "<missing>";

    private static void UsesLaterCoordinatesForIndependentMovementFallback()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.UnmapGuestPage(FieldObservationFixture.ScriptPointer);
        var xAddress = FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset;
        var movingMemory = new TearingNativeMemoryReader(
            fixture.Native,
            fixture.GetHostAddress(xAddress),
            triggerRead: 5,
            () => fixture.Write(xAddress, BitConverter.GetBytes(175)));
        var reader = new Steam2026FieldObservationReader(
            supportedFingerprint,
            FieldObservationFixture.ModuleBase,
            movingMemory);

        Equal(
            true,
            reader.TryReadFieldFrame(out var movementFrame),
            "independent movement accepts stable ownership with changed coordinates");
        Equal(175f, movementFrame.X, "movement fallback publishes the later checked X coordinate");
        Equal(-1, movementFrame.EntityId, "movement fallback does not invent a script entity");
        Equal(-1, movementFrame.ScriptId, "movement fallback does not invent a script id");
        Equal(-1, movementFrame.ScriptByteIndex, "movement fallback does not invent a script position");
    }

    private static void RejectsUnmappedTranslatedDomains()
    {
        var cases = new (uint GuestAddress, string Label)[]
        {
            ((uint)FieldPositionReader.AddressCurrentModule, "unmapped module"),
            ((uint)FieldPositionReader.AddressFieldId, "unmapped field"),
            ((uint)FieldPositionReader.AddressFieldModelsPtr, "unmapped model pointer"),
            (FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, "unmapped model state"),
            (FieldObservationFixture.ScriptPointer + FieldObservationFixture.ScriptAbsolutePosition, "unmapped script state"),
            ((uint)FieldAudibleCueStateReader.AddressUserControl, "unmapped cue state"),
            (FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, "unmapped control state"),
            (FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, "unmapped boundary state")
        };

        foreach (var testCase in cases)
        {
            var fixture = FieldObservationFixture.CreatePopulated();
            fixture.UnmapGuestPage(testCase.GuestAddress);
            var reader = new Steam2026FieldObservationReader(
                supportedFingerprint,
                FieldObservationFixture.ModuleBase,
                fixture.Native);
            Equal(false, reader.TryReadResearchSnapshot(16, out var snapshot), testCase.Label);
            Equal<Steam2026FieldResearchSnapshot?>(null, snapshot, $"{testCase.Label} returns no snapshot");
        }
    }

    private static void RejectsTranslatedPageRemapping()
    {
        var cases = new (uint GuestAddress, string Label)[]
        {
            ((uint)FieldPositionReader.AddressCurrentModule, "remapped module page"),
            ((uint)FieldPositionReader.AddressFieldId, "remapped field page"),
            ((uint)FieldPositionReader.AddressFieldModelsPtr, "remapped pointer page"),
            (FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, "remapped model page"),
            (FieldObservationFixture.ScriptPointer + FieldObservationFixture.ScriptAbsolutePosition, "remapped script page"),
            ((uint)FieldAudibleCueStateReader.AddressUserControl, "remapped cue page"),
            (FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, "remapped control page"),
            (FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, "remapped boundary page")
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var testCase = cases[index];
            var fixture = FieldObservationFixture.CreatePopulated();
            var watchedEntry = fixture.GetPageTableEntryAddress(testCase.GuestAddress);
            var replacementHostPage = 0x0000000700000000 + ((ulong)index * 0x2000);
            var remapping = new RemappingNativeMemoryReader(
                fixture.Native,
                watchedEntry,
                triggerRead: 2,
                () => fixture.MapGuestPage(testCase.GuestAddress, replacementHostPage));
            var reader = new Steam2026FieldObservationReader(
                supportedFingerprint,
                FieldObservationFixture.ModuleBase,
                remapping);

            Equal(false, reader.TryReadResearchSnapshot(16, out _), testCase.Label);
        }
    }

    private static void RejectsTornNativeDomainState()
    {
        var cases = new (uint GuestAddress, byte[] Replacement, string Label)[]
        {
            ((uint)FieldPositionReader.AddressCurrentModule, [2], "torn module"),
            ((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)117), "torn field"),
            ((uint)FieldPositionReader.AddressFieldModelsPtr, BitConverter.GetBytes(0x00020000u), "torn pointer"),
            (FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, BitConverter.GetBytes(101), "torn model"),
            (FieldObservationFixture.ScriptPointer + FieldObservationFixture.ScriptAbsolutePosition, [0x41], "torn script"),
            ((uint)FieldAudibleCueStateReader.AddressUserControl, [1], "torn cue"),
            (FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, [0x80], "torn control"),
            (FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, [0x04], "torn boundary")
        };

        foreach (var testCase in cases)
        {
            var fixture = FieldObservationFixture.CreatePopulated();
            var watchedHostAddress = fixture.GetHostAddress(testCase.GuestAddress);
            var tearing = new TearingNativeMemoryReader(
                fixture.Native,
                watchedHostAddress,
                triggerRead: 2,
                () => fixture.Write(testCase.GuestAddress, testCase.Replacement));
            var reader = new Steam2026FieldObservationReader(
                supportedFingerprint,
                FieldObservationFixture.ModuleBase,
                tearing);

            Equal(false, reader.TryReadResearchSnapshot(16, out var snapshot), testCase.Label);
            Equal<Steam2026FieldResearchSnapshot?>(null, snapshot, $"{testCase.Label} returns no snapshot");
        }
    }

    private static void RejectsInvalidCallerSuppliedTriangleCounts()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        var reader = new Steam2026FieldObservationReader(
            supportedFingerprint,
            FieldObservationFixture.ModuleBase,
            fixture.Native);

        Equal(false, reader.TryReadResearchSnapshot(0, out _), "zero verified triangle count rejected");
        Equal(false, reader.TryReadResearchSnapshot(FieldBoundaryStateReader.MaximumTriangleCount + 1, out _), "oversized verified triangle count rejected");
        Equal(false, reader.TryReadFieldFrame(0, out _), "invalid boundary metadata blocks normalized frame");
    }

    private static void KeepsResearchSurfaceHookSpeechAndCapabilityFree()
    {
        var readerType = typeof(Steam2026FieldObservationReader);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(readerType), "field research reader is not a runtime backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(readerType), "field research reader is not an event sink");
        Equal(false, readerType.GetMethods().Any(method => method.Name.Contains("Hook", StringComparison.OrdinalIgnoreCase)), "field research reader exposes no hooks");
        Equal(false, readerType.GetMethods().Any(method => method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)), "field research reader exposes no speech");
        using var backend = new Steam2026X64RuntimeBackend(supportedFingerprint);
        Equal(
            RuntimeCapability.None,
            backend.ValidateCapabilities().Available,
            "offline field evidence does not enable capabilities");
    }

    private static void PublicReaderRequiresExactTranslatedResolver()
    {
        var constructors = typeof(Steam2026FieldObservationReader).GetConstructors();
        Equal(1, constructors.Length, "field facade public constructor count");
        Equal(
            typeof(Steam2026FingerprintResult),
            constructors[0].GetParameters()[0].ParameterType,
            "field facade public constructor requires fingerprint");

        var unsupportedFixture = FieldObservationFixture.CreatePopulated();
        var unsupportedRejected = false;
        try
        {
            _ = new Steam2026FieldObservationReader(
                unsupportedFingerprint,
                FieldObservationFixture.ModuleBase,
                unsupportedFixture.Native);
        }
        catch (ArgumentException)
        {
            unsupportedRejected = true;
        }

        Equal(true, unsupportedRejected, "public field reader rejects unsupported fingerprint");

        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Native.Write(
            FieldObservationFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            [0x90]);

        var rejected = false;
        try
        {
            _ = new Steam2026FieldObservationReader(
                supportedFingerprint,
                FieldObservationFixture.ModuleBase,
                fixture.Native);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Equal(true, rejected, "public field reader requires exact translated resolver");
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
            throw new InvalidOperationException($"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }
}

internal sealed class FieldObservationFixture
{
    public const ulong ModuleBase = 0x0000000140000000;
    public const uint ModelTable = 0x00010000;
    public const uint ModelBase = ModelTable + FieldPositionReader.FieldModelStride;
    public const uint ScriptPointer = 0x00050000;
    public const ushort ScriptAbsolutePosition = 0x0090;
    public const uint TriggerPointer = 0x00060000;
    public const uint FieldGlobalPointer = 0x00070000;

    private readonly Dictionary<uint, ulong> hostPages = [];
    private ulong nextHostPage = 0x0000000500000000;

    private FieldObservationFixture()
    {
        Direct = new DirectGuestMemory();
        Native = new FakeNativeMemoryReader();
    }

    public DirectGuestMemory Direct { get; }

    public FakeNativeMemoryReader Native { get; }

    public static FieldObservationFixture CreatePopulated()
    {
        var fixture = new FieldObservationFixture();
        fixture.Populate();
        return fixture;
    }

    public void WriteByte(int address, byte value) => Write((uint)address, [value]);

    public void Write(uint address, IReadOnlyList<byte> values)
    {
        Direct.Write(address, values);
        for (var index = 0; index < values.Count; index++)
        {
            var guestAddress = checked(address + (uint)index);
            var hostAddress = GetOrMapHostAddress(guestAddress);
            Native.Write(hostAddress, [values[index]]);
        }
    }

    public ulong GetHostAddress(uint guestAddress)
    {
        var pageIndex = guestAddress >> 12;
        if (!hostPages.TryGetValue(pageIndex, out var hostPage))
        {
            throw new InvalidOperationException($"Guest page 0x{pageIndex:X5} is not mapped by the fixture.");
        }

        return hostPage + (guestAddress & 0xFFF);
    }

    public ulong GetPageTableEntryAddress(uint guestAddress) =>
        ModuleBase + TranslatedX86AddressSpace.PageTableRva + ((guestAddress >> 12) * sizeof(ulong));

    public void MapGuestPage(uint guestAddress, ulong hostPage)
    {
        hostPages[guestAddress >> 12] = hostPage;
        Native.MapVirtualPage(ModuleBase, guestAddress >> 12, hostPage);
    }

    public void UnmapGuestPage(uint guestAddress) => MapGuestPage(guestAddress, 0);

    private ulong GetOrMapHostAddress(uint guestAddress)
    {
        var pageIndex = guestAddress >> 12;
        if (!hostPages.TryGetValue(pageIndex, out var hostPage))
        {
            hostPage = nextHostPage;
            nextHostPage += 0x3000;
            MapGuestPage(guestAddress, hostPage);
        }

        return hostPage + (guestAddress & 0xFFF);
    }

    private void Populate()
    {
        Native.Write(
            ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            Convert.FromHexString(
                "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
        WriteByte(FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)116));
        Write((uint)FieldPositionReader.AddressFieldCurrentModelId, BitConverter.GetBytes((ushort)1));
        WriteByte(FieldPositionReader.AddressFieldNumModels, 2);
        Write((uint)FieldPositionReader.AddressFieldModelsPtr, BitConverter.GetBytes(ModelTable));
        Write(ModelBase + FieldPositionReader.ModelXOffset, BitConverter.GetBytes(100));
        Write(ModelBase + FieldPositionReader.ModelYOffset, BitConverter.GetBytes(-200));
        Write(ModelBase + FieldPositionReader.ModelZOffset, BitConverter.GetBytes(300));
        Write(ModelBase + FieldPositionReader.ModelDirectionOffset, [0xC0]);
        var objectBase = (uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride;
        Write(objectBase + FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)9));

        Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, BitConverter.GetBytes(ScriptPointer));
        Write(ScriptPointer + 2, [2]);
        Write(ScriptPointer + 6, BitConverter.GetBytes((ushort)4));
        Write((uint)FieldScriptContextReader.AddressCurrentEntityId, [1]);
        Write((uint)(FieldScriptContextReader.AddressCurrentEntityScriptPriority + 1), [2]);
        Write((uint)(FieldScriptContextReader.AddressCurrentEntityScriptId + 10), [3]);
        Write((uint)(FieldScriptContextReader.AddressFieldCurrScriptPosition + 2), BitConverter.GetBytes(ScriptAbsolutePosition));
        var scriptOffsetTable = ScriptPointer
            + 16
            + FieldScriptContextReader.ScriptOffsetTableHeaderSize
            + 16
            + FieldScriptContextReader.ScriptOffsetEntityStride;
        Write(scriptOffsetTable + 6, BitConverter.GetBytes((ushort)0x80));
        Write(ScriptPointer + ScriptAbsolutePosition, [0x40]);

        WriteByte(FieldAudibleCueStateReader.AddressUserControl, 0);
        WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
        Write((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, BitConverter.GetBytes((ushort)0));

        Write((uint)FieldNavigationControlReader.AddressFieldTriggersPtr, BitConverter.GetBytes(TriggerPointer));
        Write(TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, [0xA0]);

        Write((uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, BitConverter.GetBytes(FieldGlobalPointer));
        Write(FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, [0x05, 0x80]);
    }
}
