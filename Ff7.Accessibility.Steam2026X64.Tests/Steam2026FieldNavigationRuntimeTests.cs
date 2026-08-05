using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

internal static class Steam2026FieldNavigationRuntimeTests
{
    internal static void Run()
    {
        RoutesTheExactSixLegacyNavigationKeysInStableOrder();
        KeepsNavigationActiveForPermanentNonClosableMessage();
        SuspendsTemporaryNonFieldOwnershipWithoutDiscardingNavigationIntent();
        TreatsNativeLadderMovementAsNavigationInsteadOfGenericScriptSuppression();
        RetainsNavigationKeyEdgesUntilAUsableFieldSnapshot();
        RetainsOrderedNavigationActionsUntilTheirTargetDomainIsCoherent();
        BeaconOffPreemptsAndCancelsOlderPendingNavigationEdges();
        AllowsStoryAndObjectSelectionWhileExitRoutesAreIncoherent();
        PausesLiveTrackingWhileItsTargetOrRouteDomainIsIncoherent();
        DistinguishesCoherentBlockedRoutesFromNativeReadFailures();
        RetainsToggleWhenBoundaryTurnsUnreadableDuringActionPreflight();
        ReplaysOptionalManualRoutePreflightWithoutRereadingNativeState();
        SkipsDirectionalInputWhenOnlySpatialFieldFeaturesOwnTheRuntime();
        IncludesDynamicDestinationInGatewayIdentity();
        FiltersTrainScriptExitsByNativeProgressionState();
        PublishesOnlyStableExitSnapshots();
        KeepsExitCueOwnershipIndependentFromNavigationSpeech();
        KeepsLadderCueOwnershipIndependentFromNavigationAndExitCues();
        InterruptsSupersededDynamicGuidance();
        PlaysOnlyCoherentForegroundReachableExitPoints();
        RejectsTornNativeLadderStateAcrossOwnershipBookends();
        RejectsNativeLadderEventTablePointerSwapAcrossOwnershipBookends();
        AcceptsDoubleReadNativeLadderStateWithStableOwnership();
        RejectsTornNativeNpcTargetsAcrossOwnershipBookends();
        AcceptsDoubleReadNativeNpcTargetsWithStableOwnership();
        PlaysOnlyCoherentForegroundUnmountedLadders();
        PrioritizesTheObjectiveRouteLadderEntrance();
        ExposesTheExactCommittedRouteForDiagnostics();
        ExposesTheSelectedNativeTargetForDiagnostics();
        CapturesNativeTriangleResolutionAndBoundariesForPendingFootstep();
    }

    private static void SuspendsTemporaryNonFieldOwnershipWithoutDiscardingNavigationIntent()
    {
        Equal(
            Steam2026FieldNavigationOwnershipDisposition.Active,
            Steam2026FieldNavigationCoordinator.ResolveOwnershipDisposition(
                enableNavigationAssistant: true,
                enableExitProximityCues: true,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: FieldPositionReader.FieldModule,
                isProcessForeground: true,
                enableLadderProximityCues: true),
            "foreground field navigation owns its native state");
        Equal(
            Steam2026FieldNavigationOwnershipDisposition.Suspended,
            Steam2026FieldNavigationCoordinator.ResolveOwnershipDisposition(
                enableNavigationAssistant: true,
                enableExitProximityCues: true,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: BattleStateReader.BattleModule,
                isProcessForeground: true,
                enableLadderProximityCues: true),
            "battle temporarily suspends rather than destroys field navigation");
        Equal(
            Steam2026FieldNavigationOwnershipDisposition.Suspended,
            Steam2026FieldNavigationCoordinator.ResolveOwnershipDisposition(
                enableNavigationAssistant: true,
                enableExitProximityCues: true,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: FieldPositionReader.FieldModule,
                isProcessForeground: false,
                enableLadderProximityCues: true),
            "backgrounding temporarily suspends field navigation");
        Equal(
            Steam2026FieldNavigationOwnershipDisposition.Reset,
            Steam2026FieldNavigationCoordinator.ResolveOwnershipDisposition(
                enableNavigationAssistant: true,
                enableExitProximityCues: true,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: TitleMenuCursorReader.TitleModule,
                isProcessForeground: true,
                enableLadderProximityCues: true),
            "returning to the title screen terminates stale field navigation");
        Equal(
            Steam2026FieldNavigationOwnershipDisposition.Reset,
            Steam2026FieldNavigationCoordinator.ResolveOwnershipDisposition(
                enableNavigationAssistant: true,
                enableExitProximityCues: true,
                isLifecycleForeground: true,
                isShuttingDown: true,
                moduleId: FieldPositionReader.FieldModule,
                isProcessForeground: true,
                enableLadderProximityCues: true),
            "shutdown still clears field navigation");
    }

    private static void TreatsNativeLadderMovementAsNavigationInsteadOfGenericScriptSuppression()
    {
        var scriptedLock = new FieldAudibleCueState(
            true,
            "scripted control lock",
            FieldPositionReader.FieldModule,
            1,
            0,
            0);
        var mounted = FieldLadderStateSnapshot.NotMounted with
        {
            IsMounted = true,
            Phase = FieldLadderPhase.Climbing,
            RequiredInput = FieldNavigationInput.Up,
            Target = new FieldNavigationRouteWaypoint(10, 20, 900),
            TargetTriangle = 42
        };

        Equal(
            false,
            Steam2026FieldNavigationCoordinator.IsNavigationSuppressed(
                scriptedLock,
                mounted,
                isLadderStateCoherent: true),
            "checked native LADER movement remains navigation-active during its control lock");
        Equal(
            true,
            Steam2026FieldNavigationCoordinator.IsNavigationSuppressed(
                scriptedLock,
                FieldLadderStateSnapshot.NotMounted,
                isLadderStateCoherent: true),
            "ordinary scripted movement remains navigation-suppressed");
        Equal(
            true,
            Steam2026FieldNavigationCoordinator.IsNavigationSuppressed(
                scriptedLock,
                mounted,
                isLadderStateCoherent: false),
            "unverified ladder state cannot bypass scripted suppression");
    }

    private static void RoutesTheExactSixLegacyNavigationKeysInStableOrder()
    {
        var expected = new[]
        {
            FieldNavigationAction.PreviousCategory,
            FieldNavigationAction.NextCategory,
            FieldNavigationAction.PreviousTarget,
            FieldNavigationAction.NextTarget,
            FieldNavigationAction.RepeatTarget,
            FieldNavigationAction.ToggleBeacon
        };
        var seenKeys = new List<int>();
        var actions = Steam2026FieldNavigationKeyRouter.ReadActions(key =>
        {
            seenKeys.Add(key);
            return true;
        });

        SequenceEqual([0x55, 0x4F, 0x4A, 0x4C, 0x4B, 0x49], seenKeys, "exact U/O/J/L/K/I scan order");
        SequenceEqual(expected, actions, "exact x86 navigation action mapping");
        Equal(
            0,
            Steam2026FieldNavigationKeyRouter.ReadActions(_ => false).Count,
            "no rising edges produce no navigation actions");
    }

    private static void KeepsNavigationActiveForPermanentNonClosableMessage()
    {
        const uint lifecyclePhaseBase = 0x00CFF5E4;
        const uint lifecycleStride = 0x30;
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1);
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            fixture.WriteByte(
                FieldMessageReader.AddressFieldWindowStates + index,
                FieldMessageReader.FreeWindowState);
        }

        const int activeWindow = 3;
        fixture.WriteByte(FieldMessageReader.AddressFieldWindowStates + activeWindow, 2);
        fixture.Write(
            lifecyclePhaseBase + ((uint)activeWindow * lifecycleStride),
            BitConverter.GetBytes((ushort)6));
        fixture.Write(
            lifecyclePhaseBase + ((uint)activeWindow * lifecycleStride) + sizeof(ushort),
            BitConverter.GetBytes((ushort)1));
        fixture.Write(
            (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
            BitConverter.GetBytes(0u));
        fixture.Write(
            (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
            BitConverter.GetBytes(0u));

        const uint processId = 42;
        var keyDown = true;
        var foregroundInput = new Steam2026ForegroundInputAdapter(
            () => (nint)1,
            _ => processId,
            key => key == Steam2026FieldNavigationKeyRouter.VirtualKeyU && keyDown
                ? unchecked((short)0x8000)
                : (short)0,
            processId);
        var objectReader = new Steam2026FieldObjectObservationReader(
            fixture.Direct,
            _ => null,
            _ => null,
            Array.Empty<FieldNavigationObjectDefinition>());
        var spoken = new List<string>();
        var config = new AccessibilityConfig
        {
            EnableFieldNavigationAssistant = true,
            EnableFieldExitProximityCues = false,
            EnableFieldLadderProximityCues = false,
            FieldNavigationScanIntervalMs = 30
        };
        using var coordinator = new Steam2026FieldNavigationCoordinator(
            config,
            fixture.Direct,
            foregroundInput,
            objectReader,
            Path.GetTempPath(),
            AppContext.BaseDirectory,
            (text, _) => spoken.Add(text),
            _ => { });
        var now = new DateTime(2026, 7, 22, 6, 22, 36, DateTimeKind.Utc);
        var frame = new RuntimeFrameObservation(
            now,
            new GameLifecycleObservation(
                IsForeground: true,
                IsShuttingDown: false,
                ModuleId: FieldPositionReader.FieldModule,
                Revision: 0),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

        coordinator.Observe(frame, now);
        keyDown = false;

        Equal(1, spoken.Count, "permanent non-closable message keeps navigation active");
        Equal(
            true,
            spoken[0].Contains("Objects", StringComparison.Ordinal),
            "category selection speaks while a completed permanent message remains assigned");
    }

    private static void InterruptsSupersededDynamicGuidance()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        PopulateSingleTriangleWalkmesh(fixture);
        fixture.Write(
            FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset,
            [0, 0]);
        fixture.Write(
            (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
            BitConverter.GetBytes(0u));
        fixture.Write(
            (uint)FieldNavigationObjectReader.AddressFieldBankBase,
            BitConverter.GetBytes((ushort)0));
        const int targetEntityId = 35;
        const int targetLineIndex = 7;
        fixture.WriteByte(
            FieldScriptLineStateReader.AddressFieldLineIndexByEntity + targetEntityId,
            targetLineIndex);
        fixture.WriteByte(
            FieldScriptLineStateReader.AddressFieldLineStates +
                targetLineIndex * FieldScriptLineStateReader.LineStateStride,
            1);
        fixture.Write(
            (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
            BitConverter.GetBytes(0u));

        var gatewayTable = new byte[
            FieldGatewayTargetReader.GatewayCount * FieldGatewayTargetReader.GatewayStride];
        for (var index = 0; index < FieldGatewayTargetReader.GatewayCount; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                gatewayTable.AsSpan(
                    index * FieldGatewayTargetReader.GatewayStride +
                    FieldGatewayTargetReader.DestinationFieldOffset,
                    sizeof(short)),
                short.MaxValue);
        }

        fixture.Write(
            FieldObservationFixture.TriggerPointer + FieldGatewayTargetReader.GatewaysOffset,
            gatewayTable);

        var target = new FieldNavigationObjectDefinition(
            FieldId: 116,
            EntityId: targetEntityId,
            Kind: FieldNavigationObjectKind.Named,
            Label: "Test waypoint",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 20,
            StaticY: -50,
            StaticZ: 300);
        var objectReader = new Steam2026FieldObjectObservationReader(
            fixture.Direct,
            _ => null,
            _ => null,
            [target]);
        const uint processId = 42;
        var navigationKeysDown = false;
        var foregroundInput = new Steam2026ForegroundInputAdapter(
            () => (nint)1,
            _ => processId,
            key => navigationKeysDown &&
                (key == Steam2026FieldNavigationKeyRouter.VirtualKeyU ||
                 key == Steam2026FieldNavigationKeyRouter.VirtualKeyI)
                ? unchecked((short)0x8000)
                : (short)0,
            processId);
        var spoken = new List<(string Text, bool Interrupt)>();
        var diagnostics = new List<string>();
        var config = new AccessibilityConfig
        {
            EnableFieldNavigationAssistant = true,
            EnableFieldExitProximityCues = false,
            EnableFieldLadderProximityCues = false,
            FieldNavigationScanIntervalMs = 30,
            FieldNavigationSpeechIntervalMs = 1000
        };
        using var coordinator = new Steam2026FieldNavigationCoordinator(
            config,
            fixture.Direct,
            foregroundInput,
            objectReader,
            Path.GetTempPath(),
            AppContext.BaseDirectory,
            (text, interrupt) => spoken.Add((text, interrupt)),
            diagnostics.Add);
        var now = new DateTime(2026, 7, 23, 23, 0, 0, DateTimeKind.Utc);
        var frame = new RuntimeFrameObservation(
            now,
            new GameLifecycleObservation(
                IsForeground: true,
                IsShuttingDown: false,
                ModuleId: FieldPositionReader.FieldModule,
                Revision: 0),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

        coordinator.Observe(frame, now);
        navigationKeysDown = true;
        coordinator.Observe(frame, now + TimeSpan.FromSeconds(2));
        navigationKeysDown = false;
        coordinator.Observe(frame, now + TimeSpan.FromSeconds(4));

        Equal(
            true,
            spoken.Count >= 3,
            "active beacon emits subsequent dynamic guidance; " +
            $"diagnostics={string.Join(" | ", diagnostics)}");
        Equal(
            true,
            spoken[^1].Interrupt,
            "new dynamic guidance supersedes queued stale directions");
    }

    private static void PlaysOnlyCoherentForegroundReachableExitPoints()
    {
        var playback = new RecordingExitPlayback();
        using var coordinator = new Steam2026FieldExitSpatialCoordinator(
            new FieldExitProximityCueTracker(10, 110, TimeSpan.Zero),
            playback,
            _ => { });
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            117,
            0,
            0,
            0,
            0,
            0,
            0);
        var exits = new[]
        {
            new FieldNavigationTarget(
                117,
                FieldNavigationCategory.Exits,
                "Exit to Sector 1 Station",
                0,
                0,
                0,
                "gateway:0")
        };
        var now = new DateTime(2026, 7, 20, 22, 0, 0, DateTimeKind.Utc);

        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            exits,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            now);
        Equal(1, playback.Calls.Count, "nearby reachable exit plays one spatial pulse");
        Equal("Exit to Sector 1 Station", playback.Calls[0].TargetLabel, "native resolved exit label");
        Equal("here", playback.Calls[0].Direction, "an exit at the player position remains audible");
        Equal(
            NavigationBeaconMovementState.OnCourse,
            playback.Calls[0].MovementState,
            "exit points use the same proximity spatializer as x86");

        coordinator.Observe(position, default, exits, false, false, true, now.AddSeconds(1));
        coordinator.Observe(position, default, exits, true, true, true, now.AddSeconds(2));
        coordinator.Observe(position, default, exits, true, false, false, now.AddSeconds(3));
        Equal(1, playback.Calls.Count, "focus suppression and incoherent reads remain silent");
        Equal(1, playback.StopAllCount, "ownership loss stops active exit audio once");
    }

    private static void RetainsNavigationKeyEdgesUntilAUsableFieldSnapshot()
    {
        var pending = new Steam2026FieldNavigationPendingActionBuffer(capacity: 4);
        Equal(
            false,
            pending.TryTakeReadyForField(116, _ => true, out _),
            "first coherent field establishes ownership");

        pending.Capture([FieldNavigationAction.NextTarget]);
        Equal(1, pending.Count, "a rising edge remains pending during a transient read failure");
        Equal(
            true,
            pending.TryTakeReadyForField(116, _ => true, out var retained),
            "the next coherent unsuppressed snapshot consumes the retained edge once");
        Equal(FieldNavigationAction.NextTarget, retained, "retained action identity");
        Equal(0, pending.Count, "consumed edge is removed");

        pending.Capture([FieldNavigationAction.RepeatTarget]);
        Equal(
            false,
            pending.TryTakeReadyForField(117, _ => true, out _),
            "a field transition clears stale navigation actions instead of applying them elsewhere");
        pending.Capture([FieldNavigationAction.ToggleBeacon]);
        pending.Clear();
        Equal(0, pending.Count, "ownership loss or suppression clears retained actions");
    }

    private static void RetainsOrderedNavigationActionsUntilTheirTargetDomainIsCoherent()
    {
        var pending = new Steam2026FieldNavigationPendingActionBuffer();
        pending.Capture(
        [
            FieldNavigationAction.NextCategory,
            FieldNavigationAction.NextTarget
        ]);
        var incoherentStory = new Steam2026FieldNavigationDomainCoherence(
            Exits: true,
            Story: false,
            Npcs: true,
            Objects: true,
            Route: true);

        Equal(
            false,
            pending.TryTakeReadyForField(
                116,
                action => Steam2026FieldNavigationActionGate.IsReady(
                    action,
                    FieldNavigationCategory.Exits,
                    beaconEnabled: false,
                    incoherentStory),
                out _),
            "NextCategory remains pending until its destination Story domain is coherent");
        Equal(2, pending.Count, "an unready head action preserves it and every later edge");

        var coherentStory = incoherentStory with { Story = true };
        Equal(
            true,
            pending.TryTakeReadyForField(
                116,
                action => Steam2026FieldNavigationActionGate.IsReady(
                    action,
                    FieldNavigationCategory.Exits,
                    beaconEnabled: false,
                    coherentStory),
                out var categoryAction),
            "destination coherence releases the category edge");
        Equal(FieldNavigationAction.NextCategory, categoryAction, "ordered category action");
        Equal(
            true,
            pending.TryTakeReadyForField(
                116,
                action => Steam2026FieldNavigationActionGate.IsReady(
                    action,
                    FieldNavigationCategory.Story,
                    beaconEnabled: false,
                    coherentStory),
                out var targetAction),
            "the following edge is evaluated against the category changed by the first edge");
        Equal(FieldNavigationAction.NextTarget, targetAction, "ordered target action");
        Equal(0, pending.Count, "both ordered edges are consumed exactly once");
    }

    private static void BeaconOffPreemptsAndCancelsOlderPendingNavigationEdges()
    {
        var pending = new Steam2026FieldNavigationPendingActionBuffer();
        pending.Capture(
        [
            FieldNavigationAction.NextTarget,
            FieldNavigationAction.RepeatTarget,
            FieldNavigationAction.ToggleBeacon,
            FieldNavigationAction.NextCategory
        ]);

        Equal(
            true,
            pending.TryTakeEmergencyBeaconOff(
                116,
                beaconEnabled: true,
                out var emergencyAction),
            "beacon-off bypasses an older target-domain edge that cannot yet run");
        Equal(FieldNavigationAction.ToggleBeacon, emergencyAction, "emergency action identity");
        Equal(
            0,
            pending.Count,
            "beacon-off is a cancellation barrier: all edges captured before or with it are discarded, and queued trailing edges cannot immediately re-enable navigation");

        pending.Capture([FieldNavigationAction.ToggleBeacon]);
        Equal(
            false,
            pending.TryTakeEmergencyBeaconOff(116, beaconEnabled: false, out _),
            "a ToggleBeacon edge is not reordered while navigation is already off");
        Equal(1, pending.Count, "normal beacon-on remains ordered when no emergency off is needed");
    }

    private static void AllowsStoryAndObjectSelectionWhileExitRoutesAreIncoherent()
    {
        var domains = new Steam2026FieldNavigationDomainCoherence(
            Exits: false,
            Story: true,
            Npcs: true,
            Objects: true,
            Route: false);

        Equal(
            true,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.NextTarget,
                FieldNavigationCategory.Story,
                beaconEnabled: false,
                domains),
            "a coherent Story selection is independent from an incoherent exit route");
        Equal(
            true,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.RepeatTarget,
                FieldNavigationCategory.Objects,
                beaconEnabled: false,
                domains),
            "a coherent Objects selection is independent from an incoherent exit route");
        Equal(
            true,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.NextCategory,
                FieldNavigationCategory.Npcs,
                beaconEnabled: false,
                domains),
            "category movement into coherent Objects remains available");
        Equal(
            false,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.NextTarget,
                FieldNavigationCategory.Exits,
                beaconEnabled: false,
                domains),
            "an exit action remains pending while its own target domain is incoherent");
        Equal(
            false,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.ToggleBeacon,
                FieldNavigationCategory.Story,
                beaconEnabled: false,
                domains),
            "starting navigation waits for coherent route state instead of speaking Navigation off");
        Equal(
            true,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.ToggleBeacon,
                FieldNavigationCategory.Story,
                beaconEnabled: true,
                domains),
            "turning an existing beacon off never becomes trapped behind a read failure");
        Equal(
            false,
            Steam2026FieldNavigationActionGate.IsReady(
                FieldNavigationAction.NextTarget,
                FieldNavigationCategory.Npcs,
                beaconEnabled: false,
                domains with { Npcs = false }),
            "an NPC action remains pending while its checked native target domain is incoherent");
    }

    private static void PausesLiveTrackingWhileItsTargetOrRouteDomainIsIncoherent()
    {
        var coherent = new Steam2026FieldNavigationDomainCoherence(true, true, true, true, true);
        Equal(
            true,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Story,
                beaconEnabled: true,
                coherent),
            "coherent active Story navigation may update");
        Equal(
            false,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Story,
                beaconEnabled: true,
                coherent with { Story = false }),
            "a transient Story target tear cannot announce target unavailable or turn navigation off");
        Equal(
            false,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Objects,
                beaconEnabled: true,
                coherent with { Route = false }),
            "a transient route tear pauses object live tracking");
        Equal(
            false,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Npcs,
                beaconEnabled: true,
                coherent with { Npcs = false }),
            "a transient NPC target tear pauses native NPC live tracking");
        Equal(
            true,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Objects,
                beaconEnabled: false,
                coherent with { Objects = false, Route = false }),
            "inactive navigation has no live state to invalidate");
    }

    private static void DistinguishesCoherentBlockedRoutesFromNativeReadFailures()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            117,
            0,
            0,
            0,
            0,
            0,
            0);
        var target = new FieldNavigationTarget(
            117,
            FieldNavigationCategory.Exits,
            "Blocked exit",
            10,
            10,
            0,
            "gateway:117:0:116");
        var blocked = new Steam2026FailClosedFieldRoutePlanner(
            new RecordingRoutePlanner(result: false, throws: false));
        blocked.BeginObservation();
        Equal(false, blocked.TryBuildRoute(position, target, out _), "a native blocked route remains unavailable");
        Equal(false, blocked.HadReadFailure, "a coherent blocked route is not misclassified as a torn read");

        var unreadable = new Steam2026FailClosedFieldRoutePlanner(
            new RecordingRoutePlanner(result: false, throws: true));
        unreadable.BeginObservation();
        Equal(false, unreadable.TryBuildRoute(position, target, out _), "a throwing native route fails closed");
        Equal(true, unreadable.HadReadFailure, "a translated read exception marks the route domain incoherent");

        var nonThrowingInvalidBoundary = new Steam2026FailClosedFieldRoutePlanner(
            new FieldWalkmeshRoutePlanner(
                CreateSingleTriangleWalkmeshReader(),
                new FieldBoundaryStateReader(
                    _ => 0,
                    _ => 0,
                    (_, _) => true)));
        nonThrowingInvalidBoundary.BeginObservation();
        Equal(
            false,
            nonThrowingInvalidBoundary.TryBuildRoute(position, target, out _),
            "a boundary reader can report Invalid without throwing");
        Equal(
            true,
            nonThrowingInvalidBoundary.HadReadFailure,
            "nonthrowing Invalid boundary state marks the route domain incoherent instead of looking like a legitimate blocked route");
    }

    private static FieldWalkmeshReader CreateSingleTriangleWalkmeshReader()
    {
        const int fieldDataBase = 0x02000000;
        const int sectionOffset = 0x100;
        var memory = new Dictionary<int, int>
        {
            [FieldWalkmeshReader.AddressFieldDataPtr] = fieldDataBase
        };
        var sectionTableEntry = fieldDataBase + FieldWalkmeshReader.SectionOffsetsHeaderOffset +
            FieldWalkmeshReader.WalkmeshSectionIndex * sizeof(int);
        memory[sectionTableEntry] = sectionOffset;
        memory[sectionTableEntry + sizeof(int)] = sectionOffset + sizeof(int) + sizeof(int) +
            FieldWalkmeshReader.TriangleSize + FieldWalkmeshReader.AccessSize;
        var payload = fieldDataBase + sectionOffset + sizeof(int);
        memory[payload] = 1;
        var triangleBase = payload + sizeof(int);
        var values = new short[]
        {
            0, 0, 0, 0,
            100, 0, 0, 0,
            0, 100, 0, 0
        };
        for (var index = 0; index < values.Length; index++)
        {
            memory[triangleBase + index * sizeof(short)] = values[index];
        }

        var accessBase = triangleBase + FieldWalkmeshReader.TriangleSize;
        memory[accessBase] = -1;
        memory[accessBase + sizeof(short)] = -1;
        memory[accessBase + sizeof(short) * 2] = -1;
        return new FieldWalkmeshReader(
            address => memory.TryGetValue(address, out var value) ? value : 0,
            address => (short)(memory.TryGetValue(address, out var value) ? value : 0));
    }

    private static void RetainsToggleWhenBoundaryTurnsUnreadableDuringActionPreflight()
    {
        const int fieldId = 117;
        const int fieldGlobalObject = 0x02400000;
        var boundaryReadable = true;
        byte ReadBoundaryByte(int address) => address switch
        {
            FieldPositionReader.AddressCurrentModule => FieldPositionReader.FieldModule,
            FieldPositionReader.AddressFieldId => (byte)fieldId,
            FieldPositionReader.AddressFieldId + 1 => (byte)(fieldId >> 8),
            _ => 0
        };
        int ReadBoundaryInt32(int address) =>
            address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr
                ? fieldGlobalObject
                : 0;
        var inner = new FieldWalkmeshRoutePlanner(
            CreateSingleTriangleWalkmeshReader(),
            new FieldBoundaryStateReader(
                ReadBoundaryInt32,
                ReadBoundaryByte,
                (_, _) => boundaryReadable));
        var routePlanner = new Steam2026FailClosedFieldRoutePlanner(inner);
        var target = new FieldNavigationTarget(
            fieldId,
            FieldNavigationCategory.Exits,
            "Station exit",
            10,
            10,
            0,
            "gateway:117:0:116");
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            fieldId,
            0,
            5,
            5,
            0,
            0,
            0);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            routePlanner);
        routePlanner.BeginObservation();
        Equal(true, routePlanner.TryBuildRoute(position, target, out _), "the earlier checked field scan has coherent route state");
        Equal(false, routePlanner.HadReadFailure, "the earlier route state is coherent");

        routePlanner.BeginObservation();
        boundaryReadable = false;
        var pending = new Steam2026FieldNavigationPendingActionBuffer();
        pending.Capture([FieldNavigationAction.ToggleBeacon]);
        var coherence = new Steam2026FieldNavigationDomainCoherence(true, true, true, true, true);
        Equal(
            false,
            Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(
                pending,
                fieldId,
                controller,
                routePlanner,
                position,
                new FieldNavigationControlTransform(0),
                ref coherence,
                out _,
                out var speech),
            "a nonthrowing Invalid boundary discovered at action time prevents commit");
        Equal(1, pending.Count, "the ToggleBeacon edge remains pending for a later coherent scan");
        Equal(false, controller.BeaconEnabled, "the controller is not mutated by failed preflight");
        Equal(null, speech, "false Route unavailable speech is never produced");
        Equal(false, coherence.Route, "action-time unreadability marks the route domain incoherent");
    }

    private static void ReplaysOptionalManualRoutePreflightWithoutRereadingNativeState()
    {
        const int fieldId = 117;
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            fieldId,
            0,
            0,
            0,
            0,
            0,
            0);
        var storyTarget = new FieldNavigationTarget(
            fieldId,
            FieldNavigationCategory.Story,
            "Follow Avalanche",
            100,
            0,
            0,
            "story:117:1");
        var nativePlanner = new CountingIncoherentRoutePlanner();
        var routePlanner = new Steam2026FailClosedFieldRoutePlanner(nativePlanner);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([storyTarget]),
            routePlanner);
        controller.HandleAction(
            FieldNavigationAction.NextCategory,
            position,
            controlTransform: null);
        Equal(FieldNavigationCategory.Story, controller.CurrentCategory, "manual test enters Story without a route read");

        routePlanner.BeginObservation();
        var pending = new Steam2026FieldNavigationPendingActionBuffer();
        pending.Capture([FieldNavigationAction.RepeatTarget]);
        var coherence = new Steam2026FieldNavigationDomainCoherence(
            Exits: false,
            Story: true,
            Npcs: true,
            Objects: true,
            Route: true);
        Equal(
            true,
            Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(
                pending,
                fieldId,
                controller,
                routePlanner,
                position,
                new FieldNavigationControlTransform(0),
                ref coherence,
                out var action,
                out var result),
            "optional manual Story speech remains usable when its route read is incoherent");
        Equal(FieldNavigationAction.RepeatTarget, action, "manual action identity");
        Equal(0, pending.Count, "optional manual action is consumed once");
        Equal(1, nativePlanner.ResolveCalls, "HandleAction replays preflight instead of rereading invalid native state");
        Equal(true, result?.Speech.Contains("Follow Avalanche", StringComparison.Ordinal) == true, "manual target label is still spoken");
        Equal(false, controller.BeaconEnabled, "optional manual speech never starts navigation");
        Equal(false, coherence.Route, "optional invalid route is retained as an incoherent route domain");
    }

    private static void SkipsDirectionalInputWhenOnlySpatialFieldFeaturesOwnTheRuntime()
    {
        var invoked = false;
        Equal(
            true,
            Steam2026FieldNavigationCoordinator.TryReadNavigationInput(
                isRequired: false,
                () =>
                {
                    invoked = true;
                    throw new InvalidDataException("directional input unavailable");
                },
                out var input,
                out _),
            "exit and ladder proximity do not depend on directional input when navigation is disabled");
        Equal(false, invoked, "optional directional input is not touched");
        Equal(default(FieldNavigationInputSnapshot), input, "optional input defaults safely");

        Equal(
            false,
            Steam2026FieldNavigationCoordinator.TryReadNavigationInput(
                isRequired: true,
                () => throw new InvalidDataException("directional input unavailable"),
                out _,
                out var diagnostic),
            "navigation speech still fails closed when its required input is unreadable");
        Equal(true, diagnostic.Contains("directional input unavailable", StringComparison.Ordinal), "input failure diagnostic");
    }

    private static void IncludesDynamicDestinationInGatewayIdentity()
    {
        var first = Steam2026FieldNavigationCoordinator.CreateGatewayStableId(117, 0, 116);
        var changed = Steam2026FieldNavigationCoordinator.CreateGatewayStableId(117, 0, 118);
        Equal("gateway:117:0:116", first, "x86-parity native gateway identity");
        Equal(false, string.Equals(first, changed, StringComparison.Ordinal), "a destination swap invalidates stale selection and cue identity");
    }

    private static void FiltersTrainScriptExitsByNativeProgressionState()
    {
        static FieldNavigationTarget ScriptExit(
            int fieldId,
            int entityId,
            params int[] destinations) =>
            new(
                fieldId,
                FieldNavigationCategory.Exits,
                "Scripted exit",
                entityId,
                entityId * 2,
                0,
                $"script-exit:{fieldId}:{entityId}:{string.Join(',', destinations)}",
                TriggerEntityId: entityId,
                DestinationFieldIds: destinations);

        var freightArrival = new[] { ScriptExit(138, 10, 139) };
        Equal(
            0,
            Steam2026FieldScriptExitPolicy.Filter(138, 48, freightArrival).Count,
            "the automatic first freight-car arrival exposes no exit during its false control window");
        Equal(
            1,
            Steam2026FieldScriptExitPolicy.Filter(138, 51, freightArrival).Count,
            "the same hatch remains a real backtrack exit after reaching the passenger car");

        var passengerCar = new[]
        {
            ScriptExit(139, 25, 138),
            ScriptExit(139, 26, 140),
            ScriptExit(139, 27, 146),
            ScriptExit(139, 28, 140, 161),
            ScriptExit(139, 29, 140)
        };
        var firstRide = Steam2026FieldScriptExitPolicy.Filter(139, 51, passengerCar);
        Equal(1, firstRide.Count, "only the native rear-hatch backtrack is an exit on the first train ride");
        Equal(25, firstRide[0].TriggerEntityId, "the first-ride exit belongs to the rear-hatch line");
        SequenceEqual([138], firstRide[0].DestinationFieldIds ?? [], "the first-ride exit returns to the freight car");
        Equal(
            passengerCar.Length,
            Steam2026FieldScriptExitPolicy.Filter(139, 108, passengerCar).Count,
            "later train missions are not rewritten by the first-ride policy");
    }

    private static void PublishesOnlyStableExitSnapshots()
    {
        var gate = new Steam2026FieldExitPublicationGate(
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(100));
        var now = new DateTime(2026, 7, 22, 20, 30, 0, DateTimeKind.Utc);
        var rearHatch = new FieldNavigationTarget(
            139,
            FieldNavigationCategory.Exits,
            "Exit to Freight Car",
            10,
            20,
            0,
            "script-exit:139:25:138",
            TriggerEntityId: 25,
            DestinationFieldIds: [138]);

        Equal(0, gate.Observe(139, 0, [rearHatch], now).Count, "field entry starts an unpublished candidate");
        Equal(false, gate.IsStable, "field-entry candidate is not a coherent published exit domain");
        Equal(0, gate.Observe(139, 0, [rearHatch], now.AddMilliseconds(50)).Count, "two matching reads still honor the native settle window");
        Equal(false, gate.IsStable, "matching reads remain unpublished during the settle window");
        Equal(1, gate.Observe(139, 0, [rearHatch], now.AddMilliseconds(310)).Count, "stable field/model/target ownership publishes the exit");
        Equal(true, gate.IsStable, "settled native exit ownership is coherent for live tracking");

        gate.ObserveUnavailable(139, 0, now.AddMilliseconds(320));
        Equal(false, gate.IsStable, "a transient same-owner read failure pauses the current frame");
        Equal(
            1,
            gate.Observe(139, 0, [rearHatch], now.AddMilliseconds(330)).Count,
            "an identical coherent recovery resumes the established exit without another field settle");
        Equal(true, gate.IsStable, "the recovered identical exit domain is coherent");

        var changed = rearHatch with { X = 11 };
        Equal(0, gate.Observe(139, 0, [changed], now.AddMilliseconds(340)).Count, "a changed target fingerprint withdraws the stale exit");
        Equal(false, gate.IsStable, "changed exit ownership pauses live tracking instead of publishing a false empty domain");
        gate.ObserveUnavailable(139, 1, now.AddMilliseconds(500));
        Equal(false, gate.IsStable, "an unavailable frame owned by another player model discards the old candidate");
        Equal(0, gate.Observe(139, 1, [changed], now.AddMilliseconds(700)).Count, "a player-model ownership change starts a new candidate");
        Equal(false, gate.IsStable, "model ownership change remains unpublished");
    }

    private static void KeepsExitCueOwnershipIndependentFromNavigationSpeech()
    {
        Equal(
            true,
            Steam2026FieldNavigationCoordinator.ShouldOwnField(
                enableNavigationAssistant: false,
                enableExitProximityCues: true,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: FieldPositionReader.FieldModule,
                isProcessForeground: true),
            "exit cue runtime owns a coherent foreground field even when navigation speech is disabled");
        Equal(
            false,
            Steam2026FieldNavigationCoordinator.ShouldOwnField(
                enableNavigationAssistant: false,
                enableExitProximityCues: false,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: FieldPositionReader.FieldModule,
                isProcessForeground: true),
            "disabled field features do not retain runtime ownership");
    }

    private static void KeepsLadderCueOwnershipIndependentFromNavigationAndExitCues()
    {
        Equal(
            true,
            Steam2026FieldNavigationCoordinator.ShouldOwnField(
                enableNavigationAssistant: false,
                enableExitProximityCues: false,
                isLifecycleForeground: true,
                isShuttingDown: false,
                moduleId: FieldPositionReader.FieldModule,
                isProcessForeground: true,
                enableLadderProximityCues: true),
            "ladder cue runtime owns a coherent foreground field independently");
    }

    private static void PlaysOnlyCoherentForegroundUnmountedLadders()
    {
        var playback = new RecordingLadderPlayback();
        using var coordinator = new Steam2026FieldLadderSpatialCoordinator(
            new FieldLadderProximityCueTracker(10, 110, TimeSpan.Zero),
            playback,
            _ => { });
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            0,
            0,
            0,
            0,
            0);
        var actionLadder = new FieldScriptNavigationTransition(
            123,
            FieldNavigationTransitionKind.Ladder,
            4,
            10,
            0,
            0,
            100,
            0,
            0,
            1,
            "ladder:123:4",
            FieldNavigationInput.Up,
            RequiresAction: true);
        var automaticLadder = actionLadder with
        {
            StableId = "ladder:123:5",
            SourceEntityId = 5,
            SourceY = 5,
            RequiresAction = false
        };
        var now = new DateTime(2026, 7, 20, 22, 30, 0, DateTimeKind.Utc);

        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            [actionLadder, automaticLadder],
            FieldLadderStateSnapshot.NotMounted,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            now);
        Equal(2, playback.Calls.Count, "both live action-gated and automatic native ladders play");
        Equal("Ladder", playback.Calls[0].TargetLabel, "ladder uses the native proximity spatial cue");

        var mounted = FieldLadderStateSnapshot.NotMounted with
        {
            IsMounted = true,
            Phase = FieldLadderPhase.Climbing,
            RequiredInput = FieldNavigationInput.Up
        };
        coordinator.Observe(
            position,
            default,
            [actionLadder],
            mounted,
            true,
            false,
            true,
            now.AddSeconds(1));
        coordinator.Observe(
            position,
            default,
            [actionLadder],
            FieldLadderStateSnapshot.NotMounted,
            false,
            false,
            true,
            now.AddSeconds(2));
        coordinator.Observe(
            position,
            default,
            [actionLadder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            true,
            true,
            now.AddSeconds(3));
        coordinator.Observe(
            position,
            default,
            [actionLadder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            false,
            now.AddSeconds(4));
        Equal(2, playback.Calls.Count, "mounted focus suppression and incoherent reads remain silent");
        Equal(1, playback.StopAllCount, "mounted state immediately stops active ladder audio once");
    }

    private static void PrioritizesTheObjectiveRouteLadderEntrance()
    {
        var playback = new RecordingLadderPlayback();
        using var coordinator = new Steam2026FieldLadderSpatialCoordinator(
            new FieldLadderProximityCueTracker(10, 110, TimeSpan.Zero),
            playback,
            _ => { });
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            0,
            0,
            0,
            0,
            0);
        var wrongLadder = new FieldScriptNavigationTransition(
            123,
            FieldNavigationTransitionKind.Ladder,
            8,
            10,
            0,
            0,
            100,
            0,
            0,
            1,
            "ladder:123:8",
            FieldNavigationInput.Down);
        var routeLadder = wrongLadder with
        {
            SourceEntityId = 10,
            SourceY = 5,
            StableId = "ladder:123:10"
        };

        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            [wrongLadder, routeLadder],
            FieldLadderStateSnapshot.NotMounted,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            prioritizedTransitionId: routeLadder.StableId);

        Equal(1, playback.Calls.Count, "an active objective should play only its next ladder entrance");
        Equal(
            true,
            playback.Calls[0].DistanceUnits > 10d,
            "objective ladder spatial target should come from the prioritized entrance");
    }

    private static void RejectsTornNativeLadderStateAcrossOwnershipBookends()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            10,
            20,
            30,
            1,
            0);
        var positions = new Queue<FieldPositionReadResult>(
        [
            FieldPositionReadResult.Valid(0x1000, position, "before"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 11 }, "middle"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 12 }, "after")
        ]);
        var mounted = FieldLadderStateSnapshot.NotMounted with
        {
            IsMounted = true,
            Phase = FieldLadderPhase.Climbing,
            RequiredInput = FieldNavigationInput.Up,
            MovementMode = 4,
            Progress = 1
        };
        var ladderReads = new Queue<FieldLadderStateReadResult>(
        [
            new FieldLadderStateReadResult(true, FieldLadderStateSnapshot.NotMounted, "candidate"),
            new FieldLadderStateReadResult(true, mounted, "confirmation")
        ]);
        var reader = new Steam2026FieldLadderObservationReader(
            () => positions.Dequeue(),
            _ => ladderReads.Dequeue(),
            () => 0x2000u);

        Equal(
            false,
            reader.TryRead(position, out _),
            "a torn mounted flag must fail closed instead of being labeled coherent");
    }

    private static void RejectsNativeLadderEventTablePointerSwapAcrossOwnershipBookends()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            10,
            20,
            30,
            1,
            0);
        var positions = new Queue<FieldPositionReadResult>(
        [
            FieldPositionReadResult.Valid(0x1000, position, "before"),
            FieldPositionReadResult.Valid(0x1000, position, "middle"),
            FieldPositionReadResult.Valid(0x1000, position, "after")
        ]);
        var eventTables = new Queue<uint?>([0x2000u, 0x3000u, 0x3000u]);
        var reader = new Steam2026FieldLadderObservationReader(
            () => positions.Dequeue(),
            _ => new FieldLadderStateReadResult(
                true,
                FieldLadderStateSnapshot.NotMounted,
                "same values from changing tables"),
            () => eventTables.Dequeue());

        Equal(
            false,
            reader.TryRead(position, out _),
            "an event-table pointer swap fails closed even when ladder values remain identical");
    }

    private static void AcceptsDoubleReadNativeLadderStateWithStableOwnership()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            10,
            20,
            30,
            1,
            0);
        var positions = new Queue<FieldPositionReadResult>(
        [
            FieldPositionReadResult.Valid(0x1000, position, "before"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 11 }, "middle"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 12 }, "after")
        ]);
        var ladderReads = new Queue<FieldLadderStateReadResult>(
        [
            new FieldLadderStateReadResult(true, FieldLadderStateSnapshot.NotMounted, "candidate"),
            new FieldLadderStateReadResult(true, FieldLadderStateSnapshot.NotMounted, "confirmation")
        ]);
        var reader = new Steam2026FieldLadderObservationReader(
            () => positions.Dequeue(),
            _ => ladderReads.Dequeue(),
            () => 0x2000u);

        Equal(
            true,
            reader.TryRead(position, out var state),
            "two identical native ladder reads with stable ownership are coherent");
        Equal(FieldLadderStateSnapshot.NotMounted, state, "coherent ladder state");
    }

    private static void RejectsTornNativeNpcTargetsAcrossOwnershipBookends()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            10,
            20,
            30,
            1,
            0);
        var positions = new Queue<FieldPositionReadResult>(
        [
            FieldPositionReadResult.Valid(0x1000, position, "before"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 11 }, "middle"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 12 }, "after")
        ]);
        var candidate = new FieldNavigationTarget(
            123,
            FieldNavigationCategory.Npcs,
            "Jessie",
            100,
            200,
            0,
            "npc:123:4");
        var changed = candidate with { X = 101 };
        var snapshots = new Queue<IReadOnlyList<FieldNavigationTarget>>(
        [
            [candidate],
            [changed]
        ]);
        var reader = new Steam2026FieldNpcObservationReader(
            () => positions.Dequeue(),
            _ => snapshots.Dequeue(),
            () => 0x2000u);

        Equal(
            false,
            reader.TryRead(position, out _),
            "changing native NPC coordinates must fail closed");
    }

    private static void AcceptsDoubleReadNativeNpcTargetsWithStableOwnership()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            10,
            20,
            30,
            1,
            0);
        var positions = new Queue<FieldPositionReadResult>(
        [
            FieldPositionReadResult.Valid(0x1000, position, "before"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 11 }, "middle"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 12 }, "after")
        ]);
        var npc = new FieldNavigationTarget(
            123,
            FieldNavigationCategory.Npcs,
            "Jessie",
            100,
            200,
            0,
            "npc:123:4",
            TriggerEntityId: 4,
            InteractionRadius: 240);
        var reader = new Steam2026FieldNpcObservationReader(
            () => positions.Dequeue(),
            _ => [npc],
            () => 0x2000u);

        Equal(
            true,
            reader.TryRead(position, out var targets),
            "two identical native NPC reads with stable ownership are coherent");
        Equal(1, targets.Count, "coherent native NPC target count");
        Equal(npc, targets[0], "coherent native NPC target");
    }

    private static void ExposesTheExactCommittedRouteForDiagnostics()
    {
        var tracker = new FieldNavigationRouteTracker(new ProbeRoutePlanner());
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            120,
            0,
            100,
            200,
            0,
            4,
            0);
        var target = new FieldNavigationTarget(
            120,
            FieldNavigationCategory.Story,
            "Talk to Biggs",
            500,
            600,
            0,
            "story:120:biggs");

        Equal(true, tracker.TryStart(position, target, out _), "probe route should start");
        var snapshot = tracker.CurrentProbeSnapshot
            ?? throw new InvalidOperationException("committed route probe snapshot is missing");

        Equal(120, snapshot.FieldId, "committed route field");
        Equal("story:120:biggs", snapshot.TargetId, "committed route target");
        Equal(9, snapshot.TargetTriangle, "committed target triangle");
        Equal(4, snapshot.ResolvedTriangle, "current resolved player triangle");
        SequenceEqual([4, 7, 9], snapshot.TrianglePath, "exact committed triangle path");
        Equal(2, snapshot.Portals.Count, "exact committed portal count");
    }

    private static void ExposesTheSelectedNativeTargetForDiagnostics()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            120,
            0,
            100,
            200,
            0,
            4,
            0);
        var target = new FieldNavigationTarget(
            120,
            FieldNavigationCategory.Exits,
            "Reactor walkway exit",
            500,
            600,
            0,
            "gateway:120:0");
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            new ProbeRoutePlanner());

        _ = controller.HandleAction(
            FieldNavigationAction.ToggleBeacon,
            position,
            new FieldNavigationControlTransform(0));
        var snapshot = controller.CreateProbeSnapshot(position);

        Equal(true, snapshot.BeaconEnabled, "controller probe beacon state");
        Equal(120, snapshot.FieldId, "controller probe field");
        Equal(FieldNavigationCategory.Exits, snapshot.Category, "controller probe category");
        Equal("gateway:120:0", snapshot.TargetId, "controller probe stable target identity");
        Equal("Reactor walkway exit", snapshot.TargetLabel, "controller probe native target label");
        Equal(500, snapshot.TargetX, "controller probe target x");
        Equal(600, snapshot.TargetY, "controller probe target y");
        Equal(true, snapshot.Route is not null, "controller probe committed route");
    }

    private static void CapturesNativeTriangleResolutionAndBoundariesForPendingFootstep()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        PopulateSingleTriangleWalkmesh(fixture);
        var now = new DateTime(2026, 7, 23, 18, 10, 0, DateTimeKind.Utc);
        using var probe = new Steam2026FieldFootstepNavigationProbe(
            new FieldFootstepDistanceProbe(1),
            new AcceptingProbeLineWriter(),
            "navigation-capture-test",
            now,
            TimeSpan.FromMilliseconds(250),
            _ => { });
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            116,
            1,
            100,
            -200,
            300,
            9,
            0);
        probe.PublishFootstep(
            new Steam2026FootstepProbeSample(
                1,
                now,
                position,
                HasControl: true,
                FieldFootstepCadence.Walk,
                default,
                TrackName: "md1stin_159",
                SoundId: 5000,
                FileName: "5000.ogg",
                Steam2026FootstepMappingScope.Field,
                Source: "Cosmo md1stin_159/5000",
                PlaybackSucceeded: true));

        const uint processId = 42;
        var foregroundInput = new Steam2026ForegroundInputAdapter(
            () => (nint)1,
            _ => processId,
            _ => 0,
            processId);
        var objectReader = new Steam2026FieldObjectObservationReader(
            fixture.Direct,
            _ => null,
            _ => null,
            Array.Empty<FieldNavigationObjectDefinition>());
        var config = new AccessibilityConfig
        {
            EnableFieldNavigationAssistant = true,
            EnableFieldExitProximityCues = false,
            EnableFieldLadderProximityCues = false
        };
        using var coordinator = new Steam2026FieldNavigationCoordinator(
            config,
            fixture.Direct,
            foregroundInput,
            objectReader,
            Path.GetTempPath(),
            AppContext.BaseDirectory,
            (_, _) => { },
            _ => { },
            probe);
        var frame = new RuntimeFrameObservation(
            now,
            new GameLifecycleObservation(true, false, FieldPositionReader.FieldModule, 1),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            RuntimeDomainUpdate<FieldFrameObservation>.Present(
                new FieldFrameObservation(116, 1, 100, -200, 300, 9, true, 0, 0, 0)),
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

        var snapshot = coordinator.CaptureProbeSnapshot(frame, workerCycle: 1, now);

        Equal(Steam2026NavigationProbeAvailability.Coherent, snapshot.Availability, "probe snapshot coherence");
        Equal(0, snapshot.ResolvedTriangle, "geometric triangle from live walkmesh");
        Equal(1, snapshot.WalkmeshTriangleCount, "live walkmesh triangle count");
        SequenceEqual([0], snapshot.ActiveBoundaryTriangles, "live active IDLCK boundary triangles");
        Equal(true, snapshot.BoundaryFingerprint.Length != 0, "boundary fingerprint");
        Equal(9, snapshot.Position.TriangleId, "native triangle remains independently recorded");
    }

    private static void PopulateSingleTriangleWalkmesh(FieldObservationFixture fixture)
    {
        const uint fieldDataBase = 0x00080000;
        const int sectionOffset = 0x100;
        fixture.Write(
            (uint)FieldWalkmeshReader.AddressFieldDataPtr,
            BitConverter.GetBytes(fieldDataBase));
        var sectionTableEntry = fieldDataBase +
            FieldWalkmeshReader.SectionOffsetsHeaderOffset +
            (uint)(FieldWalkmeshReader.WalkmeshSectionIndex * sizeof(int));
        fixture.Write(sectionTableEntry, BitConverter.GetBytes(sectionOffset));
        fixture.Write(
            sectionTableEntry + sizeof(int),
            BitConverter.GetBytes(
                sectionOffset +
                sizeof(int) +
                sizeof(int) +
                FieldWalkmeshReader.TriangleSize +
                FieldWalkmeshReader.AccessSize));
        var payload = fieldDataBase + sectionOffset + sizeof(int);
        fixture.Write(payload, BitConverter.GetBytes(1));
        var triangleBase = payload + sizeof(int);
        var vertices = new short[]
        {
            0, -300, 300, 0,
            300, -300, 300, 0,
            0, 0, 300, 0
        };
        for (var index = 0; index < vertices.Length; index++)
        {
            fixture.Write(
                triangleBase + (uint)(index * sizeof(short)),
                BitConverter.GetBytes(vertices[index]));
        }

        var accessBase = triangleBase + FieldWalkmeshReader.TriangleSize;
        fixture.Write(accessBase, BitConverter.GetBytes((short)-1));
        fixture.Write(accessBase + sizeof(short), BitConverter.GetBytes((short)-1));
        fixture.Write(accessBase + sizeof(short) * 2, BitConverter.GetBytes((short)-1));
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
        }
    }

    private static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected=[{string.Join(',', expected)}], actual=[{string.Join(',', actual)}]");
        }
    }

    private sealed class RecordingExitPlayback : ISteam2026FieldExitSpatialPlayback
    {
        internal List<NavigationBeaconCue> Calls { get; } = [];

        internal int StopAllCount { get; private set; }

        public bool Play(NavigationBeaconCue cue, float gain)
        {
            Calls.Add(cue);
            return true;
        }

        public void StopAll() => StopAllCount++;

        public void Dispose()
        {
        }
    }

    private sealed class AcceptingProbeLineWriter : ISteam2026ProbeLineWriter
    {
        public bool TryEnqueue(string jsonLine) => true;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLadderPlayback : ISteam2026FieldLadderSpatialPlayback
    {
        internal List<NavigationBeaconCue> Calls { get; } = [];

        internal int StopAllCount { get; private set; }

        public bool Play(NavigationBeaconCue cue, float gain)
        {
            Calls.Add(cue);
            return true;
        }

        public void StopAll() => StopAllCount++;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingRoutePlanner : IFieldNavigationRoutePlanner
    {
        private readonly bool result;
        private readonly bool throws;

        internal RecordingRoutePlanner(bool result, bool throws)
        {
            this.result = result;
            this.throws = throws;
        }

        public string LastDiagnostic => result ? "route available" : "route blocked";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            ThrowIfConfigured();
            triangle = result ? 0 : -1;
            return result;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            ThrowIfConfigured();
            plan = null!;
            return result;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            ThrowIfConfigured();
            waypoint = default;
            return result;
        }

        private void ThrowIfConfigured()
        {
            if (throws)
            {
                throw new InvalidDataException("translated route memory changed");
            }
        }
    }

    private sealed class ProbeRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "probe route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = 4;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                target.StableId,
                [4, 7, 9],
                [
                    new FieldNavigationRoutePortal(
                        4,
                        7,
                        new FieldNavigationRouteWaypoint(200, 250, 0),
                        new FieldNavigationRouteWaypoint(220, 270, 0)),
                    new FieldNavigationRoutePortal(
                        7,
                        9,
                        new FieldNavigationRouteWaypoint(350, 400, 0),
                        new FieldNavigationRouteWaypoint(370, 420, 0))
                ],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                9);
            return true;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(200, 250, 0);
            return true;
        }
    }

    private sealed class CountingIncoherentRoutePlanner :
        IFieldNavigationRoutePlanner,
        IFieldNavigationRouteReadStatus
    {
        public int ResolveCalls { get; private set; }

        public bool LastReadWasCoherent { get; private set; }

        public string LastDiagnostic => "nonthrowing native route Invalid";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            ResolveCalls++;
            LastReadWasCoherent = false;
            triangle = -1;
            return false;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            LastReadWasCoherent = false;
            plan = null!;
            return false;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            LastReadWasCoherent = false;
            waypoint = default;
            return false;
        }
    }
}
