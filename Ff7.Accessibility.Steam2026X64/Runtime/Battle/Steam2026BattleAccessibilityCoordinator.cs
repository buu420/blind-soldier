using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal enum Steam2026BattleSpeechDomain
{
    Menu,
    Target,
    Message,
    Results,
    Damage,
    Encounter,
    Action,
    Status
}

internal readonly record struct Steam2026BattleSpeech(
    Steam2026BattleSpeechDomain Domain,
    string Text,
    bool Interrupt);

internal readonly record struct Steam2026BattleAccessibilityOptions(
    bool Menu,
    bool Target,
    bool Message,
    bool Results,
    bool Damage,
    bool Encounter,
    bool EnemyAction,
    bool Status)
{
    internal static Steam2026BattleAccessibilityOptions AllEnabled { get; } =
        new(true, true, true, true, true, true, true, true);

    internal bool AnyEnabled =>
        Menu || Target || Message || Results || Damage || Encounter || EnemyAction || Status;
}

/// <summary>
/// Checked battle lifecycle. Exact translated callbacks feed the shared x86
/// speech trackers at their native event boundaries without exposing hidden
/// enemy state.
/// </summary>
internal sealed class Steam2026BattleAccessibilityCoordinator
{
    private readonly object sync = new();
    private readonly Steam2026BattleObservationReader reader;
    private readonly Steam2026BattleMenuCoordinator menuCoordinator;
    private readonly Steam2026BattleAccessibilityOptions options;
    private readonly BattleEncounterSpeechTracker encounterTracker = new();
    private readonly BattleEnemyActionSpeechTracker enemyActionTracker = new();
    private readonly BattleTargetSpeechTracker targetTracker = new();
    private readonly BattleSenseSpeechCoordinator senseSpeechCoordinator;
    private readonly BattleDamageSpeechTracker damageTracker = new();
    private readonly BattleStatusSpeechTracker statusTracker = new();
    private readonly BattleResultsSpeechTracker resultsTracker = new();
    private readonly TifaSlotSpeechTracker tifaSlotTracker = new();
    private readonly Queue<Steam2026BattleSpeech> pendingSpeech = new();
    private Steam2026BattleRawEnemyActionIngressSnapshot lastRawEnemyAction;
    private bool hasLastRawEnemyAction;
    private long lastSequence;
    private int ownedModule = -1;
    private bool victoryActive;

    internal Steam2026BattleAccessibilityCoordinator(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        Steam2026BattleTextResolvers textResolvers,
        Steam2026BattleAccessibilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(textResolvers);
        if (!options.AnyEnabled)
        {
            throw new ArgumentException(
                "At least one checked battle accessibility domain must be enabled.",
                nameof(options));
        }

        reader = new Steam2026BattleObservationReader(
            fingerprint,
            moduleBase,
            memory,
            textResolvers);
        menuCoordinator = new Steam2026BattleMenuCoordinator(reader);
        senseSpeechCoordinator = new BattleSenseSpeechCoordinator(
            reader.ResolveBattleTextDetailed,
            actorIndex => reader.TryReadSenseResult(actorIndex, out var result)
                ? result
                : null,
            reader.ResolveElementName);
        this.options = options;
    }

    internal void ProcessBatch(
        IReadOnlyList<Steam2026BattleRendererIngressSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        lock (sync)
        {
            Steam2026BattleRendererIngressSnapshot? pendingMenu = null;
            var pendingBattleUpdates = new List<Steam2026BattleRendererIngressSnapshot>();
            foreach (var snapshot in snapshots)
            {
                if (snapshot.Sequence <= lastSequence
                    || snapshot.TimestampUtc.Kind != DateTimeKind.Utc
                    || !Enum.IsDefined(snapshot.Kind))
                {
                    continue;
                }

                lastSequence = snapshot.Sequence;
                if (snapshot.Kind == Steam2026BattleRendererCallbackKind.MenuRenderer)
                {
                    FlushPendingBattleUpdates(pendingBattleUpdates);
                    if (snapshot.RendererState == 0x1B)
                    {
                        FlushPendingMenu(ref pendingMenu);
                        pendingMenu = snapshot;
                        FlushPendingMenu(ref pendingMenu);
                    }
                    else if (Steam2026BattleRendererState.IsCapturable(snapshot.RendererState))
                    {
                        pendingMenu = snapshot;
                    }

                    continue;
                }

                if (snapshot.Kind == Steam2026BattleRendererCallbackKind.BattleUpdate)
                {
                    FlushPendingMenu(ref pendingMenu);
                    pendingBattleUpdates.Add(snapshot);
                    continue;
                }

                FlushPendingMenu(ref pendingMenu);
                FlushPendingBattleUpdates(pendingBattleUpdates);
                ProcessNonMenu(snapshot);
            }

            FlushPendingMenu(ref pendingMenu);
            FlushPendingBattleUpdates(pendingBattleUpdates);
        }
    }

    internal bool TrySpeakPending(
        Func<Steam2026BattleSpeech, bool> trySpeak,
        out Steam2026BattleSpeech spoken)
    {
        ArgumentNullException.ThrowIfNull(trySpeak);
        spoken = default;
        lock (sync)
        {
            if (pendingSpeech.Count == 0)
            {
                return false;
            }

            var candidate = pendingSpeech.Peek();
            bool accepted;
            try
            {
                accepted = trySpeak(candidate);
            }
            catch
            {
                return false;
            }

            if (!accepted)
            {
                return false;
            }

            pendingSpeech.Dequeue();
            spoken = candidate;
            return true;
        }
    }

    internal void Reset()
    {
        lock (sync)
        {
            ResetAll(clearPendingSpeech: true);
            lastSequence = 0;
        }
    }

    private void ProcessNonMenu(Steam2026BattleRendererIngressSnapshot ingress)
    {
        if (ingress.Kind == Steam2026BattleRendererCallbackKind.ResultsUpdate)
        {
            ProcessCapturedResults(ingress.ResultsBefore);
            ProcessCapturedResults(ingress.ResultsAfter);
            return;
        }

        if (!reader.TryReadCurrentModule(out var module))
        {
            return;
        }

        ObserveOwnership(module);
        if (victoryActive &&
            ingress.Kind is Steam2026BattleRendererCallbackKind.TextActivation or
                Steam2026BattleRendererCallbackKind.DamageDisplay)
        {
            return;
        }

        switch (ingress.Kind)
        {
            case Steam2026BattleRendererCallbackKind.TextActivation
                when module == BattleStateReader.BattleModule && options.Message:
                senseSpeechCoordinator.ObserveActiveBuffer(ingress.TextBufferIndex);
                QueueSingle(
                    Steam2026BattleSpeechDomain.Message,
                    senseSpeechCoordinator.Poll(),
                    preferredInterrupt: true);
                break;
            case Steam2026BattleRendererCallbackKind.DamageDisplay
                when module == BattleStateReader.BattleModule:
                ProcessDamage(ingress.CapturedDamage);
                break;
        }
    }

    private void ProcessBattleTrackerState(bool hasNativeBoundaryCapture)
    {
        var hasTrackerSnapshot = reader.TryReadBattleTrackerSnapshot(
            includePolledEnemyAction: !hasNativeBoundaryCapture,
            out var snapshot);
        if (hasTrackerSnapshot && options.Encounter)
        {
            encounterTracker.Observe(snapshot.Encounter);
            QueueSingle(
                Steam2026BattleSpeechDomain.Encounter,
                encounterTracker.Poll(),
                preferredInterrupt: true);
        }

        if (!hasTrackerSnapshot)
        {
            return;
        }

        if (options.Damage)
        {
            damageTracker.SeedActors(snapshot.Actors);
        }

        if (options.EnemyAction)
        {
            if (!hasNativeBoundaryCapture)
            {
                ObserveEnemyAction(
                    snapshot.EnemyAction,
                    snapshot.Actors);
            }
        }

        if (options.Status)
        {
            statusTracker.Observe(snapshot.Actors);
            QueueAll(
                Steam2026BattleSpeechDomain.Status,
                statusTracker.Poll,
                preferredInterrupt: false);
        }

        if (options.Menu)
        {
            menuCoordinator.ObserveRootCommandMenuActive(snapshot.RootCommandMenuActive);
        }

        if (options.Results)
        {
            resultsTracker.ObserveBattleProgress(snapshot.PartyProgress);
        }

        if (options.Target)
        {
            targetTracker.Observe(snapshot.Target);
            QueueSingle(
                Steam2026BattleSpeechDomain.Target,
                targetTracker.Poll(),
                preferredInterrupt: true);
        }
    }

    private void ProcessCapturedEnemyActions(
        Steam2026BattleRendererIngressSnapshot ingress)
    {
        if (!options.EnemyAction)
        {
            return;
        }

        if (ingress.EnemyActionBefore.WasCaptured)
        {
            ObserveEnemyAction(ingress.EnemyActionBefore);
        }

        if (ingress.EnemyActionAfter.WasCaptured)
        {
            ObserveEnemyAction(ingress.EnemyActionAfter);
        }
    }

    private void ProcessDamage(BattleDamagePopupSnapshot capturedPopup)
    {
        if (!reader.TryReadDamageTrackerSnapshot(capturedPopup, out var snapshot))
        {
            return;
        }

        if (options.Damage)
        {
            damageTracker.Observe(snapshot.Popup, snapshot.Actor);
            QueueSingle(
                Steam2026BattleSpeechDomain.Damage,
                damageTracker.Poll(),
                preferredInterrupt: false);
        }

        if (options.Status)
        {
            statusTracker.ConfirmVisibleDamageOutcome(
                snapshot.Popup,
                snapshot.VisibleActor);
            QueueAll(
                Steam2026BattleSpeechDomain.Status,
                statusTracker.Poll,
                preferredInterrupt: false);
        }
    }

    private void ObserveEnemyAction(
        Steam2026BattleEnemyActionIngressSnapshot snapshot)
    {
        if (snapshot.Raw.IsCoherent)
        {
            if (hasLastRawEnemyAction && snapshot.Raw == lastRawEnemyAction)
            {
                return;
            }
        }

        if (!reader.TryResolveCapturedEnemyAction(
                snapshot,
                out var action,
                out var attacker))
        {
            return;
        }

        if (snapshot.Raw.IsCoherent)
        {
            lastRawEnemyAction = snapshot.Raw;
            hasLastRawEnemyAction = true;
        }

        ObserveEnemyAction(
            action,
            action.IsValid ? [attacker] : []);
    }

    private void ObserveEnemyAction(
        BattleEnemyActionSnapshot action,
        IReadOnlyList<BattleActorSnapshot> actors)
    {
        enemyActionTracker.Observe(action, actors);
        QueueSingle(
            Steam2026BattleSpeechDomain.Action,
            enemyActionTracker.Poll(),
            preferredInterrupt: false);
    }

    private void ProcessCapturedResults(Steam2026BattleResultsIngressSnapshot captured)
    {
        if (!options.Results)
        {
            return;
        }

        if (captured.WasCaptured)
        {
            ObserveOwnership(BattleResultsReader.ResultsModule);
            if (reader.TryReadCapturedResultsTrackerSnapshot(captured, out var snapshot))
            {
                resultsTracker.ObserveResults(snapshot.Results, snapshot.PartyProgress);
            }
        }

        DrainResultsSpeech();
    }

    private void ObserveCapturedVictory(Steam2026BattleVictoryIngressSnapshot captured)
    {
        if (!options.Results || !captured.WasCaptured)
        {
            return;
        }

        if (captured.IsVictory && !victoryActive)
        {
            victoryActive = true;
            ResetInteractionTrackers();
            DiscardPendingInteractionSpeech();
        }
        else if (victoryActive && !captured.IsVictory)
        {
            // Victory is a lifecycle latch. A transient cleared capture while
            // the battle module is fading must not reopen interaction speech
            // or manufacture a second victory edge.
            return;
        }

        resultsTracker.ObserveVictorySignal(captured.IsVictory);
        DrainResultsSpeech();
    }

    private void DrainResultsSpeech()
    {
        while (resultsTracker.PollSpeech() is { } speech)
        {
            QueueSingle(
                Steam2026BattleSpeechDomain.Results,
                speech.Text,
                speech.Interrupt);
        }
    }

    private void FlushPendingMenu(
        ref Steam2026BattleRendererIngressSnapshot? pendingMenu)
    {
        if (pendingMenu is not { } snapshot)
        {
            return;
        }

        pendingMenu = null;
        if (!reader.TryReadCurrentModule(out var module))
        {
            return;
        }

        ObserveOwnership(module);
        if (module != BattleStateReader.BattleModule)
        {
            return;
        }

        if (victoryActive)
        {
            return;
        }

        if (snapshot.RendererState == 0x1B)
        {
            if (options.Message)
            {
                tifaSlotTracker.ObserveFrame(
                    snapshot.TifaSlotsBefore,
                    snapshot.TifaSlotsAfter);
                QueueTifaSlotResults();
            }

            return;
        }

        if (!options.Menu)
        {
            return;
        }

        _ = menuCoordinator.Observe(snapshot);
        while (menuCoordinator.TrySpeakPending(_ => true, out var text))
        {
            QueueSingle(
                Steam2026BattleSpeechDomain.Menu,
                text,
                preferredInterrupt: true);
        }
    }

    private void FlushPendingBattleUpdates(
        List<Steam2026BattleRendererIngressSnapshot> pendingUpdates)
    {
        if (pendingUpdates.Count == 0)
        {
            return;
        }

        var hasNativeBoundaryCapture = false;
        foreach (var ingress in pendingUpdates)
        {
            if (options.Message && ingress.TifaSlotsCommittedAfter.IsValid)
            {
                tifaSlotTracker.ObserveCommitted(ingress.TifaSlotsCommittedAfter);
                QueueTifaSlotResults();
            }

            hasNativeBoundaryCapture |= ingress.EnemyActionBefore.WasCaptured
                                        || ingress.EnemyActionAfter.WasCaptured;
            if (ingress.VictoryAfter.WasCaptured)
            {
                ObserveOwnership(BattleStateReader.BattleModule);
            }

            ObserveCapturedVictory(ingress.VictoryAfter);
            if (!victoryActive)
            {
                ProcessCapturedEnemyActions(ingress);
            }
        }

        pendingUpdates.Clear();
        if (!reader.TryReadCurrentModule(out var module))
        {
            return;
        }

        if (module != BattleStateReader.BattleModule)
        {
            if (module == BattleResultsReader.ResultsModule)
            {
                ObserveOwnership(module);
            }

            return;
        }

        ObserveOwnership(module);
        if (module == BattleStateReader.BattleModule && !victoryActive)
        {
            ProcessBattleTrackerState(hasNativeBoundaryCapture);
        }
    }

    private void ObserveOwnership(int module)
    {
        if (module == ownedModule)
        {
            return;
        }

        if (module == BattleResultsReader.ResultsModule
            && ownedModule == BattleStateReader.BattleModule)
        {
            ResetInteractionTrackers();
            victoryActive = false;
            ownedModule = module;
            return;
        }

        if (module == BattleStateReader.BattleModule)
        {
            ResetAll(clearPendingSpeech: ownedModule >= 0);
            ownedModule = module;
            return;
        }

        if (module == BattleResultsReader.ResultsModule)
        {
            ResetAll(clearPendingSpeech: ownedModule >= 0);
            ownedModule = module;
            return;
        }

        ResetAll(clearPendingSpeech: true);
        ownedModule = module;
    }

    private void QueueAll(
        Steam2026BattleSpeechDomain domain,
        Func<string?> poll,
        bool preferredInterrupt)
    {
        string? text;
        while (!string.IsNullOrWhiteSpace(text = poll()))
        {
            QueueSingle(domain, text, preferredInterrupt);
        }
    }

    private void QueueTifaSlotResults()
    {
        var results = new List<string>();
        string? result;
        while (!string.IsNullOrWhiteSpace(result = tifaSlotTracker.Poll()))
        {
            results.Add(result);
        }

        if (results.Count > 0)
        {
            QueueSingle(
                Steam2026BattleSpeechDomain.Message,
                string.Join(", ", results),
                preferredInterrupt: true);
        }
    }

    private void QueueSingle(
        Steam2026BattleSpeechDomain domain,
        string? text,
        bool preferredInterrupt)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (victoryActive && domain != Steam2026BattleSpeechDomain.Results)
        {
            return;
        }

        var speech = new Steam2026BattleSpeech(
            domain,
            text.Trim(),
            preferredInterrupt);
        if (domain == Steam2026BattleSpeechDomain.Results &&
            preferredInterrupt &&
            pendingSpeech.Any(candidate =>
                candidate.Domain == Steam2026BattleSpeechDomain.Results && candidate.Interrupt))
        {
            var retained = pendingSpeech.Where(candidate =>
                candidate.Domain != Steam2026BattleSpeechDomain.Results || !candidate.Interrupt).ToArray();
            pendingSpeech.Clear();
            foreach (var candidate in retained)
            {
                pendingSpeech.Enqueue(candidate);
            }
        }

        pendingSpeech.Enqueue(speech);
    }

    private void ResetInteractionTrackers()
    {
        menuCoordinator.Reset();
        encounterTracker.Reset();
        enemyActionTracker.Reset();
        lastRawEnemyAction = default;
        hasLastRawEnemyAction = false;
        targetTracker.Reset();
        senseSpeechCoordinator.Reset();
        damageTracker.Reset();
        statusTracker.Reset();
        tifaSlotTracker.Reset();
    }

    private void ResetAll(bool clearPendingSpeech)
    {
        ResetInteractionTrackers();
        resultsTracker.Reset();
        victoryActive = false;
        if (clearPendingSpeech)
        {
            pendingSpeech.Clear();
        }
    }

    private void DiscardPendingInteractionSpeech()
    {
        var results = pendingSpeech
            .Where(speech => speech.Domain == Steam2026BattleSpeechDomain.Results)
            .ToArray();
        pendingSpeech.Clear();
        foreach (var speech in results)
        {
            pendingSpeech.Enqueue(speech);
        }
    }
}
