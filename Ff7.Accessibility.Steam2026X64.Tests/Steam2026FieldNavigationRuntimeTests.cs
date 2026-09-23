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
        RunPlannerCapabilities();
        RetainsToggleWhenBoundaryTurnsUnreadableDuringActionPreflight();
        ReplaysOptionalManualRoutePreflightWithoutRereadingNativeState();
        ManualFallRewardGuidanceNeedsObjectCoherenceWithoutRouteCoherence();
        SkipsDirectionalInputWhenOnlySpatialFieldFeaturesOwnTheRuntime();
        IncludesDynamicDestinationInGatewayIdentity();
        FiltersScriptExitsByNativeProgressionState();
        PublishesOnlyStableExitSnapshots();
        KeepsExitCueOwnershipIndependentFromNavigationSpeech();
        KeepsLadderCueOwnershipIndependentFromNavigationAndExitCues();
        InterruptsSupersededDynamicGuidance();
        PlaysOnlyCoherentForegroundReachableExitPoints();
        RejectsTornNativeLadderStateAcrossOwnershipBookends();
        RejectsNativeLadderEventTablePointerSwapAcrossOwnershipBookends();
        AcceptsDoubleReadNativeLadderStateWithStableOwnership();
        AcceptsAnActorWalkingBetweenNativeNpcConfirmations();
        RejectsRealNativeNpcChangesAcrossOwnershipBookends();
        AcceptsDoubleReadNativeNpcTargetsWithStableOwnership();
        PlaysOnlyCoherentForegroundUnmountedLadders();
        PrioritizesTheObjectiveRouteLadderEntrance();
        SeparatesTraversalAndMountCueAtTheActiveEntrance();
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
        var ordinary = RunDynamicGuidance(renderOffsetZ: 0);
        var airportLift = RunDynamicGuidance(renderOffsetZ: 624);
        SequenceEqual(ordinary, airportLift,
            "x64 runtime navigation must not change its route or speech when only OFST changes");
    }

    private static IReadOnlyList<(string Text, bool Interrupt)> RunDynamicGuidance(int renderOffsetZ)
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Write(
            FieldObservationFixture.ModelBase + FieldPositionReader.ModelZOffset,
            BitConverter.GetBytes(300 + renderOffsetZ));
        PopulateSingleTriangleWalkmesh(fixture, stackedFloor: true);
        fixture.Write(
            (uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride +
                FieldPositionReader.ObjectTriangleOffset,
            BitConverter.GetBytes((ushort)0));
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
        var autoWalkKeyDown = false;
        var foregroundInput = new Steam2026ForegroundInputAdapter(
            () => (nint)1,
            _ => processId,
            key => (navigationKeysDown &&
                    (key == Steam2026FieldNavigationKeyRouter.VirtualKeyU ||
                     key == Steam2026FieldNavigationKeyRouter.VirtualKeyI)) ||
                   (autoWalkKeyDown && key == NavigationAutoWalkKeyRouter.VirtualKeyP)
                ? unchecked((short)0x8000)
                : (short)0,
            processId);
        var spoken = new List<(string Text, bool Interrupt)>();
        var diagnostics = new List<string>();
        var autoWalkSink = new RecordingKeyboardInputSink();
        var autoWalk = new NavigationAutoWalkController(autoWalkSink);
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
            diagnostics.Add,
            autoWalk: autoWalk);
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
        autoWalkKeyDown = true;
        coordinator.Observe(frame, now + TimeSpan.FromSeconds(6));
        autoWalkKeyDown = false;
        coordinator.Observe(frame, now + TimeSpan.FromSeconds(8));

        Equal(
            true,
            spoken.Count >= 3,
            "active navigation emits subsequent dynamic guidance; " +
            $"diagnostics={string.Join(" | ", diagnostics)}");
        Equal(
            true,
            spoken[^1].Interrupt,
            "new dynamic guidance supersedes queued stale directions");
        Equal(
            true,
            spoken.Any(item => string.Equals(item.Text, "Auto walk on.", StringComparison.Ordinal)),
            "P starts auto walk on the already selected coherent route");
        Equal(
            true,
            autoWalkSink.Batches.SelectMany(batch => batch).Any(transition => transition.IsKeyDown),
            "x64 field integration injects the route-owned directional key");

        AssertReassertsASwallowedDirection(coordinator, fixture, autoWalkSink, autoWalk, frame, now);
        return spoken;
    }

    /// <summary>
    /// The native runtime must pass the direction the game is actually acting on into
    /// <c>Drive</c>, or the shared controller's reassertion can never run: it only
    /// triggers on an observed <c>None</c>, and the optional argument defaults to null,
    /// which skips the check altogether. Both native call sites omitted it while x86 has
    /// always passed it, so a direction the game swallowed stayed swallowed for ever.
    /// </summary>
    private static void AssertReassertsASwallowedDirection(
        Steam2026FieldNavigationCoordinator coordinator,
        FieldObservationFixture fixture,
        RecordingKeyboardInputSink autoWalkSink,
        NavigationAutoWalkController autoWalk,
        RuntimeFrameObservation frame,
        DateTime now)
    {
        // The game is reporting no direction at all while auto walk holds one down:
        // exactly the swallowed-key case. Three such reads are the shared controller's
        // threshold, so four frames must produce a release and a fresh press.
        fixture.Write(
            (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
            BitConverter.GetBytes(0u));
        var beforeReassert = autoWalkSink.Batches.Count;
        Equal(true, autoWalk.Enabled, "the native autowalk is active before input recovery");
        for (var step = 1; step <= 4; step++)
        {
            coordinator.Observe(frame, now + TimeSpan.FromSeconds(8) + TimeSpan.FromMilliseconds(step * 100));
        }

        var reassertBatches = autoWalkSink.Batches.Skip(beforeReassert).ToArray();
        Equal(
            true,
            reassertBatches.SelectMany(batch => batch).Any(transition => !transition.IsKeyDown),
            "a direction the game is not acting on is released");
        Equal(
            true,
            reassertBatches.SelectMany(batch => batch).Any(transition => transition.IsKeyDown),
            "and pressed again, which is the reassertion the null observed input disabled");

        // Now the game reports the same direction the route commanded. The key stays
        // held and nothing is re-sent: reasserting a direction that is being honoured
        // would drop a frame of movement on every pass.
        Equal(true, autoWalk.Enabled, "input recovery leaves the native autowalk active");
        var held = autoWalkSink.Batches.Count;
        fixture.Write(
            (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
            BitConverter.GetBytes(
                FieldNavigationInputReader.DownMask | FieldNavigationInputReader.RightMask));
        for (var step = 5; step <= 12; step++)
        {
            coordinator.Observe(frame, now + TimeSpan.FromSeconds(8) + TimeSpan.FromMilliseconds(step * 100));
        }

        Equal(
            0,
            autoWalkSink.Batches.Count - held,
            "an observed direction that matches the commanded one is left alone");
        Equal(true, autoWalk.Enabled, "no extra key batches means held movement, not a stopped autowalk");
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

    internal static void RunPlannerCapabilities()
    {
        PreservesNativePlannerCapabilitiesThroughTheController();
        OptionalPlannerReadsPreserveArgumentsAndFailClosed();
        PreparedActionRoutesKeepTheirSnapshotWithoutOptionalRereads();
        MissingOptionalPlannerCapabilitiesNeverAuthorizeMovement();
    }

    private static readonly FieldPositionSnapshot CapabilityPosition = new(1, 453, 0, 138, 163, 0, 7, 0)
    {
        NativeFixedPosition = new(567420, 668896, 0)
    };

    private static readonly FieldNavigationTarget CapabilityTarget = new(
        453, FieldNavigationCategory.Exits, "Exit to North Corel", 138, -217, 0, "453:wrapper-exit");

    private static void PreservesNativePlannerCapabilitiesThroughTheController()
    {
        var inner = new CapabilityRoutePlanner();
        var wrapper = new Steam2026FailClosedFieldRoutePlanner(inner);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([CapabilityTarget]), wrapper);
        var control = new FieldNavigationControlTransform(-128);
        wrapper.BeginObservation();
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, CapabilityPosition, control);
        Equal(true, controller.TryResolveAutomaticInput(CapabilityPosition, control, 80, out var input),
            "a clear native short step remains usable through the x64 controller wrapper");
        Equal(FieldNavigationInput.Down, input, "the short step preserves the native Down input");
        Equal(1, inner.Calls.Count(call => call == "native automatic movement"),
            "x64 must actually consult native movement feasibility instead of hiding its optional interface");
        Equal(CapabilityPosition, inner.LastPosition, "the controller forwards the complete precise snapshot");
        Equal((byte?)0, inner.LastRequestedHeading, "the control transform's exact native heading survives the wrapper");
        Equal(CapabilityTarget, inner.LastTarget, "the native check receives the active target");

        wrapper.BeginObservation();
        controller.UpdateLiveTracking(CapabilityPosition, new(0, FieldNavigationInput.None), control, false, 80,
            observedAt: DateTime.UnixEpoch);
        Equal(1, inner.Calls.Count(call => call == "corridor"), "live tracking reaches the native corridor observer");
        Equal(CapabilityPosition, inner.LastPosition, "corridor observation retains precise player coordinates");
        Equal(true, controller.CurrentRouteGuidance?.UsesNativeProbeClearance == true,
            "the x64 route retains the native probe mode");

        wrapper.BeginObservation();
        inner.Coherent = false;
        var calls = inner.Calls.Count;
        Equal(false, controller.TryResolveAutomaticInput(CapabilityPosition, control, 80, out input),
            "an unreadable native movement check cannot produce automatic input");
        Equal(FieldNavigationInput.None, input, "read failure yields no held direction");
        Equal(calls + 1, inner.Calls.Count, "the controller cannot retry other inputs after the observation became incoherent");
        Equal(true, wrapper.HadReadFailure, "native feasibility failure reaches the x64 coherence gate");
        inner.Coherent = true;
        wrapper.BeginObservation();
        Equal(true, controller.TryResolveAutomaticInput(CapabilityPosition, control, 80, out _),
            "a later coherent observation can resume the same route");
    }

    private static void OptionalPlannerReadsPreserveArgumentsAndFailClosed()
    {
        var inner = new CapabilityRoutePlanner();
        var wrapper = new Steam2026FailClosedFieldRoutePlanner(inner);
        var movement = Capability<IFieldNavigationAutomaticMovementPlanner>(wrapper);
        var corridor = Capability<IFieldNavigationCorridorLookaheadPlanner>(wrapper);
        var refresh = Capability<IFieldNavigationRouteRefreshPlanner>(wrapper);
        var endpoint = new FieldNavigationRouteWaypoint(138, 155, 0);
        var action = new FieldNavigationRouteAction(FieldNavigationTransitionKind.Ladder, "ladder:pending",
            new(138, 120, 0), FieldNavigationInput.Up, new(138, 100, 0), 7, true, 0);
        var heading = new FieldNavigationRouteHeading(true, 0, -8, "exact route heading", true, 9, 1);
        FieldNavigationCorridorObservation observation = default;
        FieldNavigationRoutePlan refreshed = null!;
        (string Name, Func<bool> Read)[] reads =
        [
            ("automatic movement", () => movement.IsAutomaticMovementClear(CapabilityPosition, CapabilityTarget, endpoint)),
            ("native continuation", () => movement.IsNativeProbeMovementClear(CapabilityPosition, CapabilityTarget, endpoint)),
            ("native automatic movement", () => movement.IsNativeProbeAutomaticMovementClear(
                CapabilityPosition, CapabilityTarget, endpoint, 52)),
            ("corridor", () => corridor.TryObserveCorridor(CapabilityPosition, inner.Plan,
                inner.Plan.StableWaypointsOverride!, 1, action, heading, out observation)),
            ("refresh", () => refresh.TryBuildRouteFromCurrentTriangle(
                CapabilityPosition, CapabilityTarget, 7, out refreshed))
        ];

        foreach (var read in reads)
        {
            wrapper.BeginObservation();
            inner.Result = true;
            inner.Coherent = true;
            inner.Throws = false;
            Equal(true, read.Read(), read.Name + " success is delegated");
            Equal(read.Name, inner.Calls[^1], read.Name + " uses its distinct native method");
            Equal(CapabilityPosition, inner.LastPosition, read.Name + " preserves exact metadata");
            Equal(inner.LastDiagnostic, wrapper.LastDiagnostic, read.Name + " preserves the native diagnostic");
            if (read.Name == "corridor")
            {
                Equal(inner.Observation, observation, "corridor output is forwarded without inventing clearance");
                Equal(true, ReferenceEquals(inner.Plan, inner.LastPlan), "corridor receives the committed plan unchanged");
                Equal(true, ReferenceEquals(inner.Plan.StableWaypointsOverride, inner.LastSteps), "corridor keeps the stable step sequence");
                Equal(1, inner.LastWaypointIndex, "corridor keeps the current step index");
                Equal((FieldNavigationRouteAction?)action, inner.LastAction, "corridor keeps pending native action gates");
                Equal(heading, inner.LastRouteHeading, "corridor keeps recovery and heading evidence");
            }
            else if (read.Name == "refresh")
            {
                Equal(true, ReferenceEquals(inner.Plan, refreshed), "refresh preserves native plan flags and geometry");
                Equal(7, inner.LastResolvedTriangle, "refresh preserves the already resolved triangle");
            }
            else
            {
                Equal(endpoint, inner.LastDestination, read.Name + " preserves the bounded destination");
                if (read.Name == "native automatic movement")
                    Equal((byte?)52, inner.LastRequestedHeading, "non-cardinal native heading is not reconstructed");
            }

            wrapper.BeginObservation();
            inner.Result = false;
            Equal(false, read.Read(), read.Name + " coherent blocked result remains blocked");
            Equal(false, wrapper.HadReadFailure, read.Name + " blockage is not a read failure");
            AssertNoOutput();

            wrapper.BeginObservation();
            inner.Result = true;
            inner.Coherent = false;
            Equal(false, read.Read(), read.Name + " cannot expose a successful result from an incoherent read");
            Equal(true, wrapper.HadReadFailure, read.Name + " marks the observation incoherent");
            AssertNoOutput();
            var count = inner.Calls.Count;
            var diagnostic = wrapper.LastDiagnostic;
            inner.Coherent = true;
            Equal(false, read.Read(), read.Name + " stays failed closed until BeginObservation");
            Equal(count, inner.Calls.Count, read.Name + " cannot reread past a sticky failure");
            Equal(diagnostic, wrapper.LastDiagnostic, read.Name + " retains the failure diagnostic");

            wrapper.BeginObservation();
            inner.Throws = true;
            Equal(false, read.Read(), read.Name + " translated read exception fails closed");
            Equal(true, wrapper.HadReadFailure, read.Name + " exception marks native data unreadable");
            Equal(true, wrapper.LastDiagnostic.Contains("native capability read changed", StringComparison.Ordinal),
                read.Name + " exception diagnostic is preserved");
            AssertNoOutput();
            inner.Throws = false;
            wrapper.BeginObservation();
            Equal(true, read.Read(), read.Name + " recovers only on a new observation");

            void AssertNoOutput()
            {
                if (read.Name == "corridor") Equal(default(FieldNavigationCorridorObservation), observation,
                    "failed corridor read cannot leak stale waypoint guidance");
                if (read.Name == "refresh") Equal<FieldNavigationRoutePlan?>(null, refreshed,
                    "failed refresh cannot leak a stale route");
            }
        }
    }

    private static void PreparedActionRoutesKeepTheirSnapshotWithoutOptionalRereads()
    {
        var inner = new CapabilityRoutePlanner();
        var wrapper = new Steam2026FailClosedFieldRoutePlanner(inner);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([CapabilityTarget]), wrapper);
        var pending = new Steam2026FieldNavigationPendingActionBuffer();
        var coherence = new Steam2026FieldNavigationDomainCoherence(true, true, true, true, true);
        var control = new FieldNavigationControlTransform(-128);
        foreach (var action in new[] { FieldNavigationAction.ToggleBeacon, FieldNavigationAction.NextTarget })
        {
            wrapper.BeginObservation();
            inner.Calls.Clear();
            pending.Capture([action]);
            Equal(true, Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(pending, 453, controller,
                wrapper, CapabilityPosition, control, ref coherence, out _, out var speech),
                "the real pending executor commits a coherent cached action");
            Equal(0, pending.Count, "a coherent action is consumed once");
            Equal("resolve,build", string.Join(',', inner.Calls), "action replay performs no optional native reads after preflight");
            Equal(true, controller.BeaconEnabled, "selection speech does not discard the active route");
            Equal(new FieldNavigationRouteWaypoint(138, 120, 0), controller.CurrentRouteGuidance!.Value.Waypoint,
                "cached startup guidance survives local selection tracker handling");
            Equal(true, controller.CurrentRouteGuidance.Value.UsesNativeProbeClearance, "relock keeps native probe mode");
            Equal(true, speech is { } selectedSpeech &&
                        selectedSpeech.Speech.Contains("Exit to North Corel", StringComparison.Ordinal) &&
                        selectedSpeech.Speech.Contains("down", StringComparison.OrdinalIgnoreCase) &&
                        !selectedSpeech.Speech.Contains("at destination", StringComparison.OrdinalIgnoreCase),
                "the pending action retains useful direction speech from its cached route");
            Equal(true, coherence.Route, "suppressing optional action-time reads does not invent incoherence");
        }

        wrapper.BeginObservation();
        Equal(true, wrapper.PrepareActionRoute(CapabilityPosition, CapabilityTarget).IsCoherent,
            "precision test prepares a coherent route");
        inner.Calls.Clear();
        var refresh = Capability<IFieldNavigationRouteRefreshPlanner>(wrapper);
        Equal(true, refresh.TryBuildRouteFromCurrentTriangle(CapabilityPosition, CapabilityTarget, 7, out var cached),
            "matching refresh replays the prepared plan");
        Equal(true, ReferenceEquals(inner.Plan, cached), "prepared refresh returns the same native plan");
        Equal(0, inner.Calls.Count, "prepared refresh never rereads native state");
        var changedFraction = CapabilityPosition with { NativeFixedPosition = new(567421, 668896, 0) };
        Equal(false, wrapper.TryBuildRoute(changedFraction, CapabilityTarget, out _),
            "identical integer XYZ cannot replay preflight from a different native fraction");
        Equal(true, wrapper.HadReadFailure, "precise preflight identity mismatch fails closed");
        Equal(0, inner.Calls.Count, "mismatched precision does not cause an unprepared native reread");
        wrapper.CompletePreparedActionRoute();
    }

    private static void MissingOptionalPlannerCapabilitiesNeverAuthorizeMovement()
    {
        var wrapper = new Steam2026FailClosedFieldRoutePlanner(new RecordingRoutePlanner(false, false));
        var movement = Capability<IFieldNavigationAutomaticMovementPlanner>(wrapper);
        wrapper.BeginObservation();
        Equal(false, movement.IsAutomaticMovementClear(CapabilityPosition, CapabilityTarget, new(138, 155, 0)),
            "missing movement capability is not unconditional clearance");
        Equal(false, movement.IsNativeProbeMovementClear(CapabilityPosition, CapabilityTarget, new(138, 155, 0)),
            "missing native continuation capability is not replaced with a weaker check");
        Equal(false, movement.IsNativeProbeAutomaticMovementClear(CapabilityPosition, CapabilityTarget, new(138, 155, 0), 0),
            "missing immediate native capability is not unconditional clearance");
        var inner = new CapabilityRoutePlanner();
        Equal(false, Capability<IFieldNavigationCorridorLookaheadPlanner>(wrapper).TryObserveCorridor(
            CapabilityPosition, inner.Plan, inner.Plan.StableWaypointsOverride!, 0, null, default, out _),
            "missing corridor capability cannot invent visible geometry");
        Equal(false, Capability<IFieldNavigationRouteRefreshPlanner>(wrapper).TryBuildRouteFromCurrentTriangle(
            CapabilityPosition, CapabilityTarget, 7, out _), "missing refresh capability fails closed");
    }

    private static T Capability<T>(object planner) where T : class => planner as T ??
        throw new InvalidOperationException("The x64 fail-closed wrapper hides " + typeof(T).Name + ".");

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

    private static void ManualFallRewardGuidanceNeedsObjectCoherenceWithoutRouteCoherence()
    {
        var target = FieldManualObjectGuidanceTests.CreateVisibleFallReward();
        var position = new FieldPositionSnapshot(1, 463, 0, 0, 0, 0, 0, 0);
        var nativePlanner = new CountingIncoherentRoutePlanner();
        var routePlanner = new Steam2026FailClosedFieldRoutePlanner(nativePlanner);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), routePlanner);
        while (controller.CurrentCategory != FieldNavigationCategory.Objects)
            controller.HandleAction(FieldNavigationAction.NextCategory, position);
        var pending = new Steam2026FieldNavigationPendingActionBuffer();
        pending.Capture([FieldNavigationAction.ToggleBeacon]);
        var coherence = new Steam2026FieldNavigationDomainCoherence(
            Exits: false, Story: false, Npcs: false, Objects: false, Route: false);
        Equal(false, Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(pending, 463, controller,
            routePlanner, position, new(0), ref coherence, out _, out _),
            "manual reward speech still requires coherent native Objects and collection state");
        Equal(1, pending.Count, "incoherent Objects retain the user's pending request");
        coherence = coherence with { Objects = true };
        Equal(true, Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(pending, 463, controller,
            routePlanner, position, new(0), ref coherence, out _, out var result),
            "a coherent manual reward must explain its controls even while the walking route is unreadable");
        Equal(0, pending.Count, "manual guidance consumes the user's P/beacon request");
        Equal(false, controller.BeaconEnabled, "manual reward instructions do not start navigation");
        Equal(0, nativePlanner.ResolveCalls, "manual guidance must not probe the unrelated unreadable walkmesh");
        Equal(true, result?.Speech.Contains("repeatedly press OK", StringComparison.OrdinalIgnoreCase) == true,
            "the manual fall controls remain audible through the x64 action executor");

        var emptyController = new FieldNavigationController(new FieldNavigationTargetSource([]), routePlanner);
        while (emptyController.CurrentCategory != FieldNavigationCategory.Objects)
            emptyController.HandleAction(FieldNavigationAction.NextCategory, position);
        pending.Capture([FieldNavigationAction.ToggleBeacon]);
        Equal(false, Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(pending, 463, emptyController,
            routePlanner, position, new(0), ref coherence, out _, out _),
            "having no selected target must not inherit the manual-reward exception to route coherence");
        Equal(1, pending.Count, "ordinary no-target route-incoherent toggles retain their original pending behavior");
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

    private static void FiltersScriptExitsByNativeProgressionState()
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

        var conditionalElevatorExit = new[]
        {
            ScriptExit(121, 7, 120, 122, 128, 129)
        };
        AssertReactorElevatorDestination(12, 122);
        AssertReactorElevatorDestination(27, 120);
        AssertReactorElevatorDestination(120, 129);
        AssertReactorElevatorDestination(127, 128);

        void AssertReactorElevatorDestination(int gameMoment, int expectedDestination)
        {
            var filtered = Steam2026FieldScriptExitPolicy.Filter(
                121,
                gameMoment,
                conditionalElevatorExit);
            Equal(1, filtered.Count, $"field 121 exposes one exit at moment {gameMoment}");
            SequenceEqual(
                [expectedDestination],
                filtered[0].DestinationFieldIds ?? [],
                $"field 121 resolves its native elevator branch at moment {gameMoment}");
            Equal(
                $"script-exit:121:7:{expectedDestination}",
                filtered[0].StableId,
                $"field 121 branch identity follows its real destination at moment {gameMoment}");
        }
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

    private static void SeparatesTraversalAndMountCueAtTheActiveEntrance()
    {
        var traversalPlayback = new RecordingLadderPlayback();
        var mountPlayback = new RecordingLadderPlayback();
        using var coordinator = new Steam2026FieldLadderSpatialCoordinator(
            new FieldLadderProximityCueTracker(10, 110, TimeSpan.Zero),
            traversalPlayback,
            _ => { },
            mountTracker: new FieldLadderMountCueTracker(
                entranceRange: 56,
                pulseInterval: TimeSpan.FromMilliseconds(700)),
            mountPlayback: mountPlayback);
        var ladder = new FieldScriptNavigationTransition(
            123,
            FieldNavigationTransitionKind.Ladder,
            10,
            100,
            0,
            0,
            100,
            0,
            500,
            1,
            "ladder:123:10:separate",
            FieldNavigationInput.Down,
            RequiresAction: true);
        var approach = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            123,
            0,
            40,
            0,
            0,
            0,
            0);
        var entrance = approach with { X = 100 };
        var now = new DateTime(2026, 8, 5, 18, 50, 0, DateTimeKind.Utc);

        coordinator.Observe(
            approach,
            new FieldNavigationControlTransform(0),
            [ladder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            true,
            now,
            ladder.StableId);
        Equal(1, traversalPlayback.Calls.Count, "the original traversal sound should own the approach");
        Equal(0, mountPlayback.Calls.Count, "214 must stay silent before the exact entrance");

        coordinator.Observe(
            entrance,
            new FieldNavigationControlTransform(0),
            [ladder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            true,
            now.AddMilliseconds(100),
            ladder.StableId);
        Equal(1, traversalPlayback.Calls.Count, "the traversal locator should yield to the mount cue at the entrance");
        Equal(1, mountPlayback.Calls.Count, "214 should play at the active ladder entrance");

        coordinator.Observe(
            entrance,
            new FieldNavigationControlTransform(0),
            [ladder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            true,
            now.AddMilliseconds(799),
            ladder.StableId);
        Equal(1, mountPlayback.Calls.Count, "214 should honor its entrance repeat interval");
        coordinator.Observe(
            entrance,
            new FieldNavigationControlTransform(0),
            [ladder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            true,
            now.AddMilliseconds(800),
            ladder.StableId);
        Equal(2, mountPlayback.Calls.Count, "214 should repeat continuously until mount");

        coordinator.Observe(
            approach,
            new FieldNavigationControlTransform(0),
            [ladder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            true,
            now.AddMilliseconds(810),
            ladder.StableId);
        Equal(1, mountPlayback.StopAllCount, "moving away should stop the mount cue immediately");
        coordinator.Observe(
            entrance,
            new FieldNavigationControlTransform(0),
            [ladder],
            FieldLadderStateSnapshot.NotMounted,
            true,
            false,
            true,
            now.AddMilliseconds(820),
            ladder.StableId);
        Equal(3, mountPlayback.Calls.Count, "returning to the entrance should restart 214 immediately");
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

    /// <summary>
    /// Field NPCs walk. A model a unit or two further along between the two
    /// confirmations is the ordinary case, and throwing the whole list away for it left
    /// Rocket Town's street reporting no NPCs in 213 of 541 samples with three people
    /// standing in it. The later read is published, so what the player is given is where
    /// everybody is now.
    /// </summary>
    private static void AcceptsAnActorWalkingBetweenNativeNpcConfirmations()
    {
        var position = NpcObservationPosition();
        var positions = NpcObservationBookends();
        var standing = new FieldNavigationTarget(
            123,
            FieldNavigationCategory.Npcs,
            "Jessie",
            100,
            200,
            0,
            "npc:123:4",
            TriggerEntityId: 4,
            InteractionRadius: 240);
        var walking = standing with { X = 101, Y = 207 };
        var snapshots = new Queue<IReadOnlyList<FieldNavigationTarget>>([[standing], [walking]]);
        var reader = new Steam2026FieldNpcObservationReader(
            () => positions.Dequeue(),
            _ => snapshots.Dequeue(),
            () => 0x2000u);

        Equal(true, reader.TryRead(position, out var targets),
            $"a walking NPC is still the same NPC: {reader.LastDiagnostic}");
        Equal(1, targets.Count, "and the list is published whole");
        Equal(walking, targets[0], "with the position the later read gave");
        Equal(true, reader.LastDiagnostic.Contains("moving=1", StringComparison.Ordinal),
            $"and the movement is recorded: {reader.LastDiagnostic}");
    }

    /// <summary>
    /// Everything that is not somebody walking still fails closed: a different cast, a
    /// different name, a different reach, a different way of being interacted with, a
    /// model that has been placed somewhere else outright, and memory that cannot be
    /// read at all.
    /// </summary>
    private static void RejectsRealNativeNpcChangesAcrossOwnershipBookends()
    {
        var position = NpcObservationPosition();
        var jessie = new FieldNavigationTarget(
            123,
            FieldNavigationCategory.Npcs,
            "Jessie",
            100,
            200,
            0,
            "npc:123:4",
            TriggerEntityId: 4,
            InteractionRadius: 240);

        var cases = new (string Label, FieldNavigationTarget[] Confirmation)[]
        {
            ("an actor leaving the list", []),
            ("an actor joining the list", [jessie, jessie with { StableId = "npc:123:5", TriggerEntityId = 5 }]),
            ("a different entity behind the row", [jessie with { StableId = "npc:123:5", TriggerEntityId = 5 }]),
            ("a different name", [jessie with { Label = "Biggs" }]),
            ("a different reach", [jessie with { InteractionRadius = 120 }]),
            ("a different activation", [jessie with { Activation = FieldNavigationActivation.Contact }]),
            ("a proxy line appearing", [jessie with { TriggerLine = new FieldNavigationTriggerLine(0, 0, 0, 8, 0, 0) }]),
            ("a model placed somewhere else", [jessie with { X = 100 + 1024 }]),
        };

        foreach (var (label, confirmation) in cases)
        {
            var positions = NpcObservationBookends();
            var snapshots = new Queue<IReadOnlyList<FieldNavigationTarget>>([[jessie], confirmation]);
            var reader = new Steam2026FieldNpcObservationReader(
                () => positions.Dequeue(),
                _ => snapshots.Dequeue(),
                () => 0x2000u);
            Equal(false, reader.TryRead(position, out var targets), $"{label} must fail closed");
            Equal(0, targets.Count, $"{label} publishes nothing");
        }

        // Ownership is unchanged as a category, but the event table moved under the read.
        var swappedPositions = NpcObservationBookends();
        var swappedTables = new Queue<uint?>([0x2000u, 0x2000u, 0x3000u]);
        var swapped = new Steam2026FieldNpcObservationReader(
            () => swappedPositions.Dequeue(),
            _ => [jessie],
            () => swappedTables.Dequeue());
        Equal(false, swapped.TryRead(position, out _),
            "an event table that moves between the bookends must fail closed");

        // And memory that cannot be read at all.
        var throwingPositions = NpcObservationBookends();
        var throwing = new Steam2026FieldNpcObservationReader(
            () => throwingPositions.Dequeue(),
            _ => throw new InvalidOperationException("torn native read"),
            () => 0x2000u);
        Equal(false, throwing.TryRead(position, out _),
            "an unreadable native table must fail closed");
    }

    private static FieldPositionSnapshot NpcObservationPosition() =>
        new(FieldPositionReader.FieldModule, 123, 0, 10, 20, 30, 1, 0);

    /// <summary>
    /// The player is walking too, which is ownership-neutral: the bookends check the
    /// module, the field, the player model and the model base, not where anybody is.
    /// </summary>
    private static Queue<FieldPositionReadResult> NpcObservationBookends()
    {
        var position = NpcObservationPosition();
        return new Queue<FieldPositionReadResult>(
        [
            FieldPositionReadResult.Valid(0x1000, position, "before"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 11 }, "middle"),
            FieldPositionReadResult.Valid(0x1000, position with { X = 12 }, "after")
        ]);
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

    private static void PopulateSingleTriangleWalkmesh(FieldObservationFixture fixture, bool stackedFloor = false)
    {
        const uint fieldDataBase = 0x00080000;
        const int sectionOffset = 0x100;
        var triangleCount = stackedFloor ? 2 : 1;
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
                triangleCount * (FieldWalkmeshReader.TriangleSize + FieldWalkmeshReader.AccessSize)));
        var payload = fieldDataBase + sectionOffset + sizeof(int);
        fixture.Write(payload, BitConverter.GetBytes(triangleCount));
        var triangleBase = payload + sizeof(int);
        var vertices = new short[]
        {
            0, -300, 300, 0,
            300, -300, 300, 0,
            0, 0, 300, 0
        };
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            for (var index = 0; index < vertices.Length; index++)
            {
                // A disconnected upper floor makes a rendered-position leak
                // observable: it would pick the wrong storey and lose the route.
                var value = (short)(vertices[index] + (index % 4 == 2 ? 624 * triangle : 0));
                fixture.Write(
                    triangleBase + (uint)(triangle * FieldWalkmeshReader.TriangleSize + index * sizeof(short)),
                    BitConverter.GetBytes(value));
            }
        }

        var accessBase = triangleBase + (uint)(triangleCount * FieldWalkmeshReader.TriangleSize);
        for (var adjacency = 0; adjacency < triangleCount * 3; adjacency++)
        {
            fixture.Write(accessBase + (uint)(sizeof(short) * adjacency), BitConverter.GetBytes((short)-1));
        }
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

    private sealed class CapabilityRoutePlanner : IFieldNavigationRoutePlanner,
        IFieldNavigationAutomaticMovementPlanner, IFieldNavigationCorridorLookaheadPlanner,
        IFieldNavigationRouteRefreshPlanner, IFieldNavigationRouteReadStatus
    {
        internal bool Result { get; set; } = true;
        internal bool Coherent { get; set; } = true;
        internal bool Throws { get; set; }
        internal List<string> Calls { get; } = [];
        internal FieldPositionSnapshot LastPosition { get; private set; }
        internal FieldNavigationTarget LastTarget { get; private set; }
        internal FieldNavigationRouteWaypoint LastDestination { get; private set; }
        internal byte? LastRequestedHeading { get; private set; }
        internal FieldNavigationRoutePlan? LastPlan { get; private set; }
        internal IReadOnlyList<FieldNavigationRouteStep>? LastSteps { get; private set; }
        internal int LastWaypointIndex { get; private set; }
        internal FieldNavigationRouteAction? LastAction { get; private set; }
        internal FieldNavigationRouteHeading LastRouteHeading { get; private set; }
        internal int LastResolvedTriangle { get; private set; }
        public bool LastReadWasCoherent { get; private set; } = true;
        public string LastDiagnostic { get; private set; } = "not read";
        internal FieldNavigationRoutePlan Plan { get; } = new(453, $"{CapabilityTarget.FieldId}:{CapabilityTarget.StableId}",
            [7], [], new(138, -217, 0), 7,
            StableWaypointsOverride: [new(new(138, 120, 0), 0, true, true, true), new(new(138, -217, 0), 0)],
            UsesNativeProbeClearance: true);
        internal FieldNavigationCorridorObservation Observation { get; } = new(7, new(138, 120, 0), 0,
            FieldNavigationLookaheadMode.VisibleStep, true, "native corridor evidence");

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = 7;
            return Record("resolve", position);
        }

        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = Plan;
            LastTarget = target;
            return Record("build", position);
        }

        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new(138, 120, 0);
            LastTarget = target;
            return Record("waypoint", position);
        }

        public bool TryBuildRouteFromCurrentTriangle(FieldPositionSnapshot position, FieldNavigationTarget target,
            int resolvedTriangle, out FieldNavigationRoutePlan plan)
        {
            plan = Plan;
            LastTarget = target;
            LastResolvedTriangle = resolvedTriangle;
            return Record("refresh", position);
        }

        public bool IsAutomaticMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination) => RecordMovement("automatic movement", position, target, destination, null);

        public bool IsNativeProbeMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination) => RecordMovement("native continuation", position, target, destination, null);

        public bool IsNativeProbeAutomaticMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination, byte? requestedHeading = null) =>
            RecordMovement("native automatic movement", position, target, destination, requestedHeading);

        public bool TryObserveCorridor(FieldPositionSnapshot position, FieldNavigationRoutePlan plan,
            IReadOnlyList<FieldNavigationRouteStep> stableWaypoints, int waypointIndex,
            FieldNavigationRouteAction? nextAction, FieldNavigationRouteHeading heading,
            out FieldNavigationCorridorObservation observation)
        {
            observation = Observation;
            LastPlan = plan;
            LastSteps = stableWaypoints;
            LastWaypointIndex = waypointIndex;
            LastAction = nextAction;
            LastRouteHeading = heading;
            return Record("corridor", position);
        }

        private bool RecordMovement(string operation, FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination, byte? requestedHeading)
        {
            LastTarget = target;
            LastDestination = destination;
            LastRequestedHeading = requestedHeading;
            return Record(operation, position);
        }

        private bool Record(string operation, FieldPositionSnapshot position)
        {
            Calls.Add(operation);
            LastPosition = position;
            LastReadWasCoherent = Coherent;
            LastDiagnostic = operation + (Coherent ? " coherent native result" : " unreadable native snapshot");
            if (Throws) throw new InvalidDataException("native capability read changed");
            return Result;
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

    private sealed class RecordingKeyboardInputSink : IHighwayKeyboardInputSink
    {
        internal List<IReadOnlyList<HighwayKeyboardTransition>> Batches { get; } = [];

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            Batches.Add(transitions.ToArray());
            return new HighwayKeyboardSendResult(transitions.Count, 0);
        }
    }
}
