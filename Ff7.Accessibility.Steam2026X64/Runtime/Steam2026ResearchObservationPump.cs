using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Lifecycle;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

namespace Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>
/// Live-research observation pump for checked lifecycle, main-menu, dialogue,
/// and field state. Hook-owned cutscene, deeper-menu, object, and battle slices
/// remain outside this polling component.
/// </summary>
internal sealed class Steam2026ResearchObservationPump
{
    private readonly Steam2026LifecycleObservationReader lifecycleReader;
    private readonly Steam2026MenuObservationReader menuReader;
    private readonly Steam2026FieldDialogueObservationReader dialogueReader;
    private readonly Steam2026FieldDialogueSpeechStabilityGate dialogueSpeechStabilityGate;
    private readonly Steam2026FieldObservationReader fieldReader;
    private readonly FieldCountdownReader countdownReader;
    private readonly CondorBattleStateReader condorBattleReader;
    private readonly CondorBattleSpeechTracker condorBattleSpeechTracker;
    private readonly CondorCursorSteering condorCursorSteering;

    /// <summary>
    /// Navigation presses seen between two state readings, held until there is a
    /// coherent snapshot to act on.
    /// </summary>
    private readonly List<CondorNavigationAction> pendingNavigation = new();
    private readonly Action<string> log;
    private bool inCondorBattle;
    private DateTime lastCondorBattleReadUtc = DateTime.MinValue;
    private readonly FieldCountdownSpeechCoordinator countdownSpeechCoordinator = new();

    /// <summary>
    /// The Shinra Mansion safe dial, read through the same translated address space as
    /// everything else here. It gates itself on native numeric window 0 being a two
    /// digit display in field 299, so it costs one window read per field frame and
    /// answers nothing anywhere else.
    /// </summary>
    private readonly ShinraMansionSafeDialReadout safeDialReadout = new();
    private readonly FieldActivityStateReader fieldActivityStateReader;
    private readonly AccessibilityConfig config;
    private string? pendingSafeDialSpeech;
    private readonly RootMainMenuRenderEvidenceTracker rootMainMenuRenderEvidenceTracker =
        new(TimeSpan.FromMilliseconds(300));
    private string? lastMainMenuStateKey;
    private int mainMenuRevision;

    internal Steam2026FieldResearchSnapshot? CurrentFieldResearchSnapshot { get; private set; }

    internal string LastDialoguePipelineDiagnostic { get; private set; } = "not observed";

    internal Steam2026ResearchObservationPump(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        TimeSpan fieldMessageStableWindow,
        AccessibilityConfig config,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        // Held rather than copied, so a toggle the player changes mid-session is seen
        // on the next frame exactly as the legacy runtime sees it.
        this.config = config;
        this.log = log ?? (_ => { });
        lifecycleReader = new Steam2026LifecycleObservationReader(
            fingerprint,
            moduleBase,
            memory);
        menuReader = new Steam2026MenuObservationReader(
            fingerprint,
            moduleBase,
            memory,
            _ => null,
            _ => null);
        // The writer is handed over only when the host actually supports one, and
        // it exists for a single purpose: the Fort Condor cursor jump. Everything
        // else on this address space is a read.
        var translatedAddressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory,
            memory as INativeMemoryWriter);
        dialogueReader = new Steam2026FieldDialogueObservationReader(translatedAddressSpace);
        countdownReader = new FieldCountdownReader(translatedAddressSpace);
        fieldActivityStateReader = new FieldActivityStateReader(translatedAddressSpace);
        dialogueSpeechStabilityGate = new Steam2026FieldDialogueSpeechStabilityGate(
            fieldMessageStableWindow);
        fieldReader = new Steam2026FieldObservationReader(
            fingerprint,
            moduleBase,
            memory);

        // Module 9 has no text to intercept on either executable, so the same
        // reader and the same wording are used here as on x86.
        condorBattleReader = new CondorBattleStateReader(translatedAddressSpace);
        condorBattleSpeechTracker = new CondorBattleSpeechTracker(
            this.log,
            config.EnableCondorBattleLineAnnouncements,
            config.EnableCondorEnemyArrivalAnnouncements);

        // The same shared steering the x86 runtime uses. The jump used to exist
        // on one executable only; routing both through one implementation is
        // what stops that happening again. It presses the game's own direction
        // keys rather than writing the cursor, so it is handed the translated
        // address space to read the live control table through - the keys are
        // whatever the player has actually bound, not the arrows.
        condorCursorSteering = new CondorCursorSteering(
            HighwayAutoSteeringController.CreateCurrentProcess(translatedAddressSpace),
            this.log);
    }

    /// <summary>
    /// Fort Condor's battle, read straight from the module 9 globals through the
    /// translated address space.
    /// </summary>
    /// <remarks>
    /// The battle draws every word of its interface as a texture, so there is
    /// nothing to hook on either runtime and the whole thing is rebuilt from
    /// state. Sharing the reader and the tracker with x86 is the point: this is
    /// a dual-runtime mod, and a player who learns what the fort sounds like on
    /// one executable must not have to learn it again on the other.
    /// </remarks>
    internal IReadOnlyList<(string Text, bool Interrupt)> ObserveCondorBattle(
        int moduleId,
        bool statusRequested,
        DateTime now,
        IReadOnlyList<CondorNavigationAction>? navigationActions = null,
        bool placementLineRequested = false)
    {
        if (moduleId != CondorBattleStateReader.CondorModule)
        {
            if (inCondorBattle)
            {
                // Counted before the reset clears it.
                log(
                    $"Fort Condor battle reader: left module 9 after " +
                    $"{condorBattleSpeechTracker.PlacementDisagreements} placement flag disagreement(s).");
                ResetCondorBattle();
            }

            return [];
        }

        // Banked before the throttle, for the same reason the status key is: the
        // state is read ten times a second and an ordinary tap lands between two
        // reads. Dropped here, the key simply appears not to work.
        if (statusRequested)
        {
            condorBattleSpeechTracker.RequestStatus();
        }

        // P, banked for the same reason: the battle line moves during the battle
        // and a press landing between two reads would otherwise vanish.
        if (placementLineRequested)
        {
            condorBattleSpeechTracker.RequestPlacementLine();
        }

        if (navigationActions is { Count: > 0 })
        {
            pendingNavigation.AddRange(navigationActions);
        }

        if (!condorBattleSpeechTracker.HasPendingStatusRequest &&
            !condorBattleSpeechTracker.HasPendingPlacementLineRequest &&
            pendingNavigation.Count == 0 &&
            // A running jump is holding keys down, so it is read as often as
            // this pump runs rather than at the ordinary ten times a second.
            // Every reading it does not get is distance the cursor travels
            // before anything notices it has arrived.
            !condorCursorSteering.IsSteering &&
            now - lastCondorBattleReadUtc < CondorBattleStateReader.ReadInterval)
        {
            return [];
        }

        lastCondorBattleReadUtc = now;

        var snapshot = condorBattleReader.TryRead();
        if (snapshot is null)
        {
            // A jump steered on a stale position is a jump steered blind, and it
            // is holding keys down right now. Told before returning, so it lets
            // go rather than driving on what it last saw.
            var blind = StepCondorCursorSteering(snapshot: null);

            // A partial read is not a battle state. Saying nothing is right: a
            // fabricated snapshot would announce healthy units as dead. The banked
            // presses are kept rather than thrown away, so the player's key acts on
            // the next coherent reading instead of vanishing into a torn one.
            log("Fort Condor battle reader: module 9 state is not ready or could not be read coherently.");
            return blind is null ? [] : [(blind, true)];
        }

        var enteringBattle = !inCondorBattle;
        if (enteringBattle)
        {
            inCondorBattle = true;
            log(
                $"Fort Condor battle reader: entered module 9 with {snapshot.AlliedCount} allied and " +
                $"{snapshot.EnemyCount} enemy units, {snapshot.Gil} gil, {snapshot.Units.Count} live slots.");
        }

        var lines = new List<(string Text, bool Interrupt)>();

        // Stepped before anything is said, so a jump that has just arrived has
        // already released its keys by the time the cursor readout below
        // announces where the cursor came to rest.
        if (StepCondorCursorSteering(snapshot) is { } steeringSpeech)
        {
            lines.Add((steeringSpeech, true));
        }

        if (condorBattleSpeechTracker.ConsumeRequestedStatus(
                snapshot,
                openingStatusWillBeSpoken: enteringBattle) is { } status)
        {
            log($"Fort Condor status: {status}");
            lines.Add((status, true));
        }

        if (condorBattleSpeechTracker.ConsumeRequestedPlacementLine(snapshot) is { } placementLine)
        {
            log($"Fort Condor placement line: {placementLine}");
            lines.Add((placementLine, true));
        }

        // A cursor-only batch interrupts: the player wants where the cursor is
        // now, not the rows it passed through on the way. Anything carrying an
        // event queues instead, so a banner or a casualty is never cut short.
        var observation = condorBattleSpeechTracker.Observe(
            snapshot,
            cursorJumpInProgress: condorCursorSteering.IsSteering);
        var supersedes = condorBattleSpeechTracker.LastObservationSupersedesSpeech;
        foreach (var line in observation)
        {
            log($"Fort Condor speech: {line}");
            lines.Add((line, supersedes));
        }

        // After Observe, so a unit that fell on this reading is already in the
        // losses list if the player asks for it in the same pass.
        var banked = pendingNavigation.ToArray();
        pendingNavigation.Clear();
        foreach (var action in banked)
        {
            var spoken = condorBattleSpeechTracker.Navigate(
                action,
                target => BeginCondorCursorJump(snapshot, target));
            if (string.IsNullOrEmpty(spoken))
            {
                continue;
            }

            log($"Fort Condor navigation: {action} -> {spoken}");
            lines.Add((spoken, true));
        }

        return lines;
    }

    /// <summary>
    /// Sets the battlefield cursor going towards a point, and answers whether
    /// the jump was accepted - not whether it arrived.
    /// </summary>
    /// <remarks>
    /// The cursor cannot be written to: it is camera-relative, and it is also
    /// the hire position, so a teleport followed by a purchase spends real gil
    /// placing a unit off the field. See <see cref="CondorCursorMover"/>, which
    /// refuses that write and always will. This holds the game's own direction
    /// keys instead and lets go when the cursor gets there.
    /// </remarks>
    private bool BeginCondorCursorJump(
        CondorBattleSnapshot snapshot,
        CondorNavigationTarget target)
    {
        // The shared controller selects the battlefield or destination cursor
        // from this coherent snapshot, retains the selected unit's stable slot,
        // and enforces the mode/modal/report and held-input gates identically on
        // x86 and x64. This host never injects OK; the controller only owns the
        // four mapped direction keys.
        return condorCursorSteering.TryBegin(target, snapshot);
    }

    /// <summary>
    /// One closed-loop steering pass, returning anything the jump needs said. A
    /// null snapshot means this reading could not see the battle at all, which
    /// ends any running jump rather than steering it on a stale position.
    /// </summary>
    private string? StepCondorCursorSteering(CondorBattleSnapshot? snapshot)
    {
        if (!condorCursorSteering.IsSteering)
        {
            return null;
        }

        var step = condorCursorSteering.Step(snapshot);

        if (step.Speech is { } speech)
        {
            log($"Fort Condor steering: {speech}");
        }

        return step.Speech;
    }

    /// <summary>
    /// Starts a fresh Fort Condor observation epoch after a module exit,
    /// runtime suspend/resume, or shutdown.
    /// </summary>
    internal void ResetCondorBattle()
    {
        inCondorBattle = false;
        lastCondorBattleReadUtc = DateTime.MinValue;

        // Presses banked for a battle that has ended have nothing left to act on.
        pendingNavigation.Clear();

        // Before anything else. A jump still running when the battle ends would
        // be left holding direction keys down in whatever comes next.
        condorCursorSteering.Cancel("the Fort Condor observation epoch ended");
        condorBattleSpeechTracker.Reset();

        // Terrain is cached for one battle. A reset can span a translated
        // guest-runtime reinitialization even when no non-module-9 frame was
        // observed, so the cache belongs to the epoch too.
        condorBattleReader.Reset();
    }

    internal void BeginShutdown()
    {
        CurrentFieldResearchSnapshot = null;
        ResetCondorBattle();
        countdownSpeechCoordinator.Reset();
        ResetSafeDialSpeech();
        rootMainMenuRenderEvidenceTracker.Reset();
        lifecycleReader.BeginShutdown();
    }

    internal bool TryGetPendingCountdown(out FieldCountdownAnnouncement announcement) =>
        countdownSpeechCoordinator.TryGetPending(out announcement);

    internal void AcknowledgeCountdown(FieldCountdownAnnouncement announcement) =>
        countdownSpeechCoordinator.Acknowledge(announcement);

    internal bool AcknowledgeDialogueSpeech(DialoguePageObservation deliveredPage) =>
        dialogueSpeechStabilityGate.AcknowledgeDelivery(deliveredPage);

    internal bool AcknowledgeDialogueClose() =>
        dialogueSpeechStabilityGate.AcknowledgeClose();

    internal bool MarkDialogueDeliverySuppressed(
        DialoguePageObservation page,
        bool suppressed) =>
        dialogueSpeechStabilityGate.MarkDeliverySuppressed(page, suppressed);

    internal void ObserveAskCursorCapture(Steam2026AskCursorIngressSnapshot snapshot) =>
        dialogueReader.ObserveAskCursorCapture(snapshot);

    internal void ResetAskCursorIngress() =>
        dialogueReader.ResetAskCursorIngress();

    internal void ObserveMessageLifecycle(Steam2026FieldMessageIngressSnapshot snapshot) =>
        dialogueReader.ObserveMessageLifecycle(snapshot);

    internal void ResetMessageIngress() =>
        dialogueReader.ResetMessageIngress();

    internal void ResetCountdownSpeech() =>
        countdownSpeechCoordinator.Reset();

    /// <summary>
    /// The safe dial's number, when one has not been spoken yet and still describes
    /// what the dial says. A line that has been overtaken, or whose value cannot
    /// currently be vouched for, is dropped here rather than handed out late.
    /// </summary>
    internal bool TryGetPendingSafeDialSpeech(out string speech)
    {
        if (pendingSafeDialSpeech is { } pending && safeDialReadout.IsCurrentSpeech(pending))
        {
            speech = pending;
            return true;
        }

        pendingSafeDialSpeech = null;
        speech = string.Empty;
        return false;
    }

    internal void AcknowledgeSafeDialSpeech(string speech)
    {
        if (string.Equals(pendingSafeDialSpeech, speech, StringComparison.Ordinal))
        {
            pendingSafeDialSpeech = null;
        }
    }

    /// <summary>
    /// What the dial says right now, for the repeat key. Null when it is not up, which
    /// is when repeating the last thing said is the right answer instead.
    /// </summary>
    internal string? SafeDialCurrentLine => safeDialReadout.DescribeForRepeat();

    internal void ResetSafeDialSpeech()
    {
        safeDialReadout.Reset();
        pendingSafeDialSpeech = null;
    }

    internal void ResetMenuIngress() => rootMainMenuRenderEvidenceTracker.Reset();

    internal void ObserveMenuIngress(TranslatedMenuIngressSnapshot snapshot)
    {
        if (snapshot.Text is not { } text ||
            snapshot.Cursor is not null ||
            snapshot.ActiveWidget is not null ||
            text.Source != snapshot.CallbackKind)
        {
            return;
        }

        rootMainMenuRenderEvidenceTracker.Observe(
            new MenuTextRenderEntry(
                text.Text,
                unchecked((uint)text.X),
                unchecked((uint)text.Y),
                text.Color,
                text.Context),
            snapshot.TimestampUtc);
    }

    internal bool TryReadFrame(out RuntimeFrameObservation frame)
    {
        frame = null!;
        if (!lifecycleReader.TryRead(out var lifecycle))
        {
            countdownSpeechCoordinator.Reset();
            safeDialReadout.ObserveUnavailable();
            pendingSafeDialSpeech = null;
            return false;
        }

        ReadSafeDialUpdate(lifecycle.ModuleId);
        ReadCountdownUpdate(lifecycle.ModuleId);
        var menu = ReadMainMenuUpdate(lifecycle.ModuleId);
        var dialogue = ReadDialogueUpdate(lifecycle.ModuleId);
        var field = ReadFieldUpdate(lifecycle.ModuleId);
        frame = new RuntimeFrameObservation(
            DateTime.UtcNow,
            lifecycle,
            menu,
            dialogue,
            field,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);
        return true;
    }

    private void ReadCountdownUpdate(int moduleId)
    {
        if (moduleId != FieldPositionReader.FieldModule ||
            !countdownReader.TryReadSnapshot(out var snapshot))
        {
            countdownSpeechCoordinator.Observe(null);
            return;
        }

        countdownSpeechCoordinator.Observe(snapshot);

        // The safe dial and this clock can be on screen together, and there is one
        // voice. The dial takes the threshold in that case, unspoken.
        _ = safeDialReadout.TrySuppressCountdown(countdownSpeechCoordinator);
    }

    /// <summary>
    /// Whether the safe dial may look at anything this frame.
    ///
    /// <para>The same option that decides whether its numbers are spoken decides whether
    /// it owns the clock and its own window. Those two came apart once: the pump took
    /// both the moment the native window opened, while the session refused to speak
    /// under the same configuration, so turning the readout off left the timer and the
    /// dial's blank page silenced by a readout that was not running.</para>
    /// </summary>
    internal static bool ShouldObserveSafeDial(AccessibilityConfig config, int moduleId)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.EnableFieldActivityReadout && moduleId == FieldPositionReader.FieldModule;
    }

    private void ReadSafeDialUpdate(int moduleId)
    {
        if (!ShouldObserveSafeDial(config, moduleId))
        {
            // A readout the player has turned off owns nothing.
            ResetSafeDialSpeech();
            return;
        }

        // A coherent read that finds no numeric window ends the attempt; a read that
        // failed or tore says nothing at all and leaves it standing. Collapsing the two
        // handed the clock back mid-dial and re-announced the safe a frame later.
        var cue = fieldActivityStateReader.TryReadNumericWindow(
            ShinraMansionSafeDialReadout.FieldId,
            ShinraMansionSafeDialReadout.DialWindowId,
            out var dialWindow)
            ? safeDialReadout.Observe(
                ShinraMansionSafeDialReadout.FieldId,
                dialWindow,
                DateTime.UtcNow)
            : safeDialReadout.ObserveUnavailable();

        pendingSafeDialSpeech = RetainSafeDialSpeech(pendingSafeDialSpeech, cue, safeDialReadout);
    }

    /// <summary>
    /// What is left waiting to be said after one dial sample.
    ///
    /// <para>A new line replaces whatever was waiting - the dial moves once per native
    /// frame, so an older number is stale rather than queued. A line that was composed
    /// and not delivered, because the host was not in the foreground or the speaker
    /// refused the call, is kept only while it still describes the number on screen.
    /// After a torn read nothing is current, so nothing is retried: a value that cannot
    /// be vouched for now must not be spoken as though it could.</para>
    /// </summary>
    internal static string? RetainSafeDialSpeech(
        string? pending,
        ShinraMansionSafeDialCue cue,
        ShinraMansionSafeDialReadout safeDial)
    {
        ArgumentNullException.ThrowIfNull(safeDial);
        return safeDial.RetainSpeech(pending, cue);
    }

    private RuntimeDomainUpdate<FieldFrameObservation> ReadFieldUpdate(int moduleId)
    {
        if (moduleId != FieldPositionReader.FieldModule)
        {
            CurrentFieldResearchSnapshot = null;
            return RuntimeDomainUpdate<FieldFrameObservation>.Closed;
        }

        if (!fieldReader.TryReadResearchSnapshot(out var researchSnapshot))
        {
            CurrentFieldResearchSnapshot = null;
            return fieldReader.TryReadMovementFieldFrame(out var movementFrame)
                ? RuntimeDomainUpdate<FieldFrameObservation>.Present(movementFrame)
                : RuntimeDomainUpdate<FieldFrameObservation>.Unchanged;
        }

        CurrentFieldResearchSnapshot = researchSnapshot;
        return RuntimeDomainUpdate<FieldFrameObservation>.Present(
            Steam2026FieldObservationReader.CreateFieldFrame(researchSnapshot));
    }

    internal static RuntimeDomainUpdate<FieldFrameObservation> NormalizeFieldUpdate(
        int moduleId,
        bool readSucceeded,
        FieldFrameObservation? observation)
    {
        if (moduleId != FieldPositionReader.FieldModule)
        {
            return RuntimeDomainUpdate<FieldFrameObservation>.Closed;
        }

        return readSucceeded && observation is not null
            ? RuntimeDomainUpdate<FieldFrameObservation>.Present(observation)
            : RuntimeDomainUpdate<FieldFrameObservation>.Unchanged;
    }

    private RuntimeDomainUpdate<MenuFrameObservation> ReadMainMenuUpdate(int moduleId)
    {
        var shopOwnershipRead = false;
        var ownsShop = false;
        if (moduleId == ShopMenuStateReader.ShopModule)
        {
            shopOwnershipRead = menuReader.TryReadShopMenuOwnership(out ownsShop);
        }

        var ownershipUpdate = NormalizeMainMenuOwnershipUpdate(
            moduleId,
            shopOwnershipRead,
            ownsShop,
            rootMainMenuRenderEvidenceTracker.IsActive(DateTime.UtcNow),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            ref lastMainMenuStateKey);
        if (ownershipUpdate.Kind == RuntimeDomainUpdateKind.Closed)
        {
            return ownershipUpdate;
        }

        if (menuReader.TryReadQuitConfirmation(out var quitConfirmation))
        {
            var quitStateKey = $"quit\u001f{quitConfirmation.Selection}";
            if (!TryAdvanceMenuRevision(quitStateKey))
            {
                return RuntimeDomainUpdate<MenuFrameObservation>.Unchanged;
            }

            return RuntimeDomainUpdate<MenuFrameObservation>.Present(
                CreateQuitConfirmationMenuFrame(quitConfirmation, mainMenuRevision));
        }

        if (!menuReader.TryReadMainMenu(out var snapshot))
        {
            return RuntimeDomainUpdate<MenuFrameObservation>.Unchanged;
        }

        if (snapshot.State.MenuOpen == 0)
        {
            lastMainMenuStateKey = null;
            return RuntimeDomainUpdate<MenuFrameObservation>.Closed;
        }

        if (snapshot.Selection is not { } selection)
        {
            return RuntimeDomainUpdate<MenuFrameObservation>.Unchanged;
        }

        var rows = new List<MenuRowObservation>(MainMenuStateReader.Labels.Length);
        for (var index = 0; index < MainMenuStateReader.Labels.Length; index++)
        {
            var bit = 1u << index;
            if ((snapshot.State.EnabledMask & bit) == 0)
            {
                continue;
            }

            rows.Add(new MenuRowObservation(
                index,
                MainMenuStateReader.Labels[index],
                (snapshot.State.DisabledMask & bit) == 0,
                index == selection.Index));
        }

        if (rows.Count == 0 || rows.Count(row => row.Selected) != 1)
        {
            return RuntimeDomainUpdate<MenuFrameObservation>.Unchanged;
        }

        var stateKey = string.Join(
            '\u001f',
            snapshot.State.State,
            selection.Index,
            snapshot.State.EnabledMask,
            snapshot.State.DisabledMask);
        if (!TryAdvanceMenuRevision(stateKey))
        {
            return RuntimeDomainUpdate<MenuFrameObservation>.Unchanged;
        }

        return RuntimeDomainUpdate<MenuFrameObservation>.Present(
            new MenuFrameObservation(
                "Main Menu",
                isOpen: true,
                mainMenuRevision,
                rows));
    }

    internal static RuntimeDomainUpdate<MenuFrameObservation> NormalizeMainMenuOwnershipUpdate(
        int moduleId,
        bool shopOwnershipRead,
        bool ownsShop,
        bool rootMenuRecentlyRendered,
        RuntimeDomainUpdate<MenuFrameObservation> update,
        ref string? lastStateKey)
    {
        if (HasMainMenuOwnership(
                moduleId,
                shopOwnershipRead,
                ownsShop,
                rootMenuRecentlyRendered))
        {
            return update;
        }

        lastStateKey = null;
        return RuntimeDomainUpdate<MenuFrameObservation>.Closed;
    }

    private static bool HasMainMenuOwnership(
        int moduleId,
        bool shopOwnershipRead,
        bool ownsShop,
        bool rootMenuRecentlyRendered)
    {
        if (moduleId == ShopMenuStateReader.ShopModule)
        {
            return shopOwnershipRead && !ownsShop && rootMenuRecentlyRendered;
        }

        return rootMenuRecentlyRendered &&
            moduleId is FieldPositionReader.FieldModule or WorldMapStateReader.WorldModule;
    }

    internal static MenuFrameObservation CreateQuitConfirmationMenuFrame(
        QuitConfirmationSnapshot snapshot,
        int revision)
    {
        var rows = new[]
        {
            new MenuRowObservation(0, "Yes", true, snapshot.Selection == 0),
            new MenuRowObservation(1, "No", true, snapshot.Selection == 1)
        };
        return new MenuFrameObservation(
            "Quit Confirmation",
            isOpen: true,
            revision,
            rows);
    }

    private bool TryAdvanceMenuRevision(string stateKey)
    {
        if (string.Equals(stateKey, lastMainMenuStateKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (mainMenuRevision == int.MaxValue)
        {
            return false;
        }

        mainMenuRevision++;
        lastMainMenuStateKey = stateKey;
        return true;
    }

    private RuntimeDomainUpdate<DialoguePageObservation> ReadDialogueUpdate(int moduleId)
    {
        RuntimeDomainUpdate<DialoguePageObservation> rawUpdate;
        if (moduleId != FieldPositionReader.FieldModule)
        {
            rawUpdate = RuntimeDomainUpdate<DialoguePageObservation>.Closed;
        }
        else
        {
            rawUpdate = dialogueReader.TryReadUpdate(out var update)
                ? update
                : RuntimeDomainUpdate<DialoguePageObservation>.Unchanged;
        }

        var stabilized = dialogueSpeechStabilityGate.Observe(rawUpdate, DateTime.UtcNow);
        var filtered = SuppressSafeDialWindowDialogue(
            SuppressClockWindowDialogue(
                stabilized,
                countdownSpeechCoordinator,
                dialogueSpeechStabilityGate.AcknowledgeDelivery),
            safeDialReadout,
            dialogueSpeechStabilityGate.AcknowledgeDelivery);
        LastDialoguePipelineDiagnostic =
            $"reader=({dialogueReader.LastDiagnostic}); raw={DescribeDialogueUpdate(rawUpdate)}; " +
            $"gate=({dialogueSpeechStabilityGate.DescribeState()}); " +
            $"output={DescribeDialogueUpdate(filtered)}";
        return filtered;
    }

    internal static RuntimeDomainUpdate<DialoguePageObservation> SuppressClockWindowDialogue(
        RuntimeDomainUpdate<DialoguePageObservation> update,
        FieldCountdownSpeechCoordinator countdown,
        Func<DialoguePageObservation, bool> acknowledge)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        ArgumentNullException.ThrowIfNull(acknowledge);
        if (update.Kind == RuntimeDomainUpdateKind.Present &&
            update.Value is { } page &&
            countdown.OwnsWindow(page.WindowId))
        {
            _ = acknowledge(page);
            return RuntimeDomainUpdate<DialoguePageObservation>.Unchanged;
        }

        return update;
    }

    /// <summary>
    /// The safe dial's own window carries a blank placeholder page that the native
    /// message path re-posts every frame the dial is drawn. The dial owns that page
    /// while it is up; the question that opens the safe and the Success and Fail pages
    /// use the same window and are not numeric, so they stay ordinary dialogue.
    /// </summary>
    internal static RuntimeDomainUpdate<DialoguePageObservation> SuppressSafeDialWindowDialogue(
        RuntimeDomainUpdate<DialoguePageObservation> update,
        ShinraMansionSafeDialReadout safeDial,
        Func<DialoguePageObservation, bool> acknowledge)
    {
        ArgumentNullException.ThrowIfNull(safeDial);
        ArgumentNullException.ThrowIfNull(acknowledge);
        if (update.Kind == RuntimeDomainUpdateKind.Present &&
            update.Value is { } page &&
            safeDial.OwnsWindow(page.WindowId))
        {
            _ = acknowledge(page);
            return RuntimeDomainUpdate<DialoguePageObservation>.Unchanged;
        }

        return update;
    }

    private static string DescribeDialogueUpdate(
        RuntimeDomainUpdate<DialoguePageObservation> update)
    {
        if (update.Kind != RuntimeDomainUpdateKind.Present || update.Value is not { } page)
        {
            return update.Kind.ToString();
        }

        var text = page.VisibleText
            .Replace('\u001f', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        if (text.Length > 72)
        {
            text = text[..72] + "...";
        }

        return
            $"Present(w{page.WindowId}/r{page.PageRevision}/choices={page.Choices.Length}/text={text})";
    }
}
