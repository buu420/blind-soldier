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
    private readonly Action<string> log;
    private bool inCondorBattle;
    private DateTime lastCondorBattleReadUtc = DateTime.MinValue;
    private readonly FieldCountdownSpeechCoordinator countdownSpeechCoordinator = new();
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
        Action<string>? log = null)
    {
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
        var translatedAddressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory);
        dialogueReader = new Steam2026FieldDialogueObservationReader(translatedAddressSpace);
        countdownReader = new FieldCountdownReader(translatedAddressSpace);
        dialogueSpeechStabilityGate = new Steam2026FieldDialogueSpeechStabilityGate(
            fieldMessageStableWindow);
        fieldReader = new Steam2026FieldObservationReader(
            fingerprint,
            moduleBase,
            memory);

        // Module 9 has no text to intercept on either executable, so the same
        // reader and the same wording are used here as on x86.
        condorBattleReader = new CondorBattleStateReader(translatedAddressSpace);
        condorBattleSpeechTracker = new CondorBattleSpeechTracker(this.log);
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
        DateTime now)
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

        if (!statusRequested && now - lastCondorBattleReadUtc < CondorBattleStateReader.ReadInterval)
        {
            return [];
        }

        lastCondorBattleReadUtc = now;

        var snapshot = condorBattleReader.TryRead();
        if (snapshot is null)
        {
            // A partial read is not a battle state. Saying nothing is right: a
            // fabricated snapshot would announce healthy units as dead.
            log("Fort Condor battle reader: module 9 state could not be read coherently.");
            return [];
        }

        if (!inCondorBattle)
        {
            inCondorBattle = true;
            log(
                $"Fort Condor battle reader: entered module 9 with {snapshot.AlliedCount} allied and " +
                $"{snapshot.EnemyCount} enemy units, {snapshot.Gil} gil, {snapshot.Units.Count} live slots.");
        }

        var lines = new List<(string Text, bool Interrupt)>();
        if (statusRequested)
        {
            var status = condorBattleSpeechTracker.DescribeStatus(snapshot);
            log($"Fort Condor status: {status}");
            lines.Add((status, true));
        }

        foreach (var line in condorBattleSpeechTracker.Observe(snapshot))
        {
            log($"Fort Condor speech: {line}");
            lines.Add((line, false));
        }

        return lines;
    }

    /// <summary>
    /// Starts a fresh Fort Condor observation epoch after a module exit,
    /// runtime suspend/resume, or shutdown.
    /// </summary>
    internal void ResetCondorBattle()
    {
        inCondorBattle = false;
        lastCondorBattleReadUtc = DateTime.MinValue;
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
            return false;
        }

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
        var filtered = SuppressClockWindowDialogue(
            stabilized,
            countdownSpeechCoordinator,
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
