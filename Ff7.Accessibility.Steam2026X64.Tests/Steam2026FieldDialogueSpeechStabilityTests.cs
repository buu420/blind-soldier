using Ff7.Accessibility.Core;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

internal static class Steam2026FieldDialogueSpeechStabilityTests
{
    private static readonly TimeSpan StableWindow = TimeSpan.FromMilliseconds(450);
    private static readonly DateTime Timestamp =
        new(2026, 7, 20, 23, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        PotionPrefixesWaitForFullStableText();
        BiggsTypewriterGrowthSpeaksOnlyTheCompleteVisibleMessage();
        ExplicitCloseAllowsAnIdenticalPickupToSpeakAgain();
        UnchangedObservationRetainsThePendingCandidate();
        FailedOutputRetriesTheCompletedCandidate();
        SuppressedStablePageIsDiscardedWhenTheNativeWindowCloses();
        FailedStablePageSurvivesCloseUntilSuccessfulRetry();
        SuppressedCloseResetsDispatcherBeforeIdenticalReopen();
        StablePagePrecedesSuccessorAfterCutsceneSuppression();
        StablePagePrecedesSuccessorAfterOutputFailure();
        StableSuppressedPageCloseAndIdenticalReopenStayOrdered();
        UnstablePrefixIsNotRetainedAcrossClose();
        SeparateSpeakerlessPagesEachInterrupt();
        NativeAskPromptAndSelectionChangesDispatchImmediately();
        SelectionOnlyAskAcknowledgesItsQueueHead();
        TypewriterGrowthAfterAVisiblePauseSpeaksOnlyTheNewSuffix();
    }

    private static void NativeAskPromptAndSelectionChangesDispatchImmediately()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);

        Dispatch(gate, dispatcher, Ask(4, selectedIndex: 0), Timestamp);
        Equal(2, output.Spoken.Count, "exact ASK prompt and selected choice dispatch without polling delay");
        Equal("What happened?", output.Spoken[0].Text, "exact ASK prompt speech");
        Equal("Buy one", output.Spoken[1].Text, "initial exact ASK selection speech");

        Dispatch(gate, dispatcher, Ask(4, selectedIndex: 1), Timestamp.AddMilliseconds(35));
        Equal(3, output.Spoken.Count, "exact ASK cursor change dispatches immediately");
        Equal("Forget it", output.Spoken[2].Text, "moved exact ASK selection speech");

        Dispatch(gate, dispatcher, Ask(4, selectedIndex: 1), Timestamp.AddMilliseconds(70));
        Dispatch(gate, dispatcher, Ask(4, selectedIndex: 1), Timestamp.AddMilliseconds(105));
        Equal(3, output.Spoken.Count, "unchanged native ASK callbacks do not replay the selected choice");

        Dispatch(gate, dispatcher, Present(5, "Flower girl Take care."), Timestamp.AddMilliseconds(140));
        Dispatch(gate, dispatcher, Present(5, "Flower girl Take care."), Timestamp.AddMilliseconds(590));
        Equal(4, output.Spoken.Count, "ordinary dialogue remains speakable after the ASK lifecycle");
        Equal("Flower girl Take care.", output.Spoken[3].Text, "ordinary successor speech remains exact");
    }

    private static void SelectionOnlyAskAcknowledgesItsQueueHead()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);

        var stabilized = gate.Observe(SelectionOnlyAsk(10, selectedIndex: 0), Timestamp);
        Equal(RuntimeDomainUpdateKind.Present, stabilized.Kind, "selection-only ASK dispatches immediately");

        var acknowledgement = DispatchUpdate(dispatcher, stabilized, Timestamp);
        Equal(1, output.Spoken.Count, "selection-only ASK speaks its selected choice");
        Equal("Buy one", output.Spoken[0].Text, "selection-only ASK selected choice speech");
        Equal(
            true,
            acknowledgement is not null,
            "successfully spoken selection-only ASK acknowledges its queue head");
        Equal(
            true,
            gate.AcknowledgeDelivery(acknowledgement!),
            "selection-only ASK delivery leaves no permanent queue head");

        Dispatch(
            gate,
            dispatcher,
            Present(11, "Flower girl Oh, thank you!"),
            Timestamp.AddMilliseconds(35));
        Dispatch(
            gate,
            dispatcher,
            Present(11, "Flower girl Oh, thank you!"),
            Timestamp.AddMilliseconds(485));
        Equal(2, output.Spoken.Count, "ordinary dialogue speaks after a selection-only ASK");
        Equal("Flower girl Oh, thank you!", output.Spoken[1].Text, "flower response is not blocked");
    }

    private static void TypewriterGrowthAfterAVisiblePauseSpeaksOnlyTheNewSuffix()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string prefix = "Cloud I know... no one lives in the slums";
        const string complete = "Cloud I know... no one lives in the slums because they want to.";

        Dispatch(gate, dispatcher, Present(1, prefix), Timestamp);
        Dispatch(gate, dispatcher, Present(1, prefix), Timestamp.AddMilliseconds(450));
        Dispatch(gate, dispatcher, Present(2, complete), Timestamp.AddMilliseconds(1000));
        Dispatch(gate, dispatcher, Present(2, complete), Timestamp.AddMilliseconds(1450));

        Equal(2, output.Spoken.Count, "scripted typewriter pause does not repeat the already spoken prefix");
        Equal(prefix, output.Spoken[0].Text, "visible prefix remains available when it pauses");
        Equal("because they want to.", output.Spoken[1].Text, "only newly visible typewriter suffix is spoken");
    }

    private static void PotionPrefixesWaitForFullStableText()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);

        Dispatch(gate, dispatcher, Present(1, "Recei"), Timestamp);
        Dispatch(
            gate,
            dispatcher,
            Present(2, "Received \"Potion\"!"),
            Timestamp.AddMilliseconds(35));
        Dispatch(
            gate,
            dispatcher,
            Present(2, "Received \"Potion\"!"),
            Timestamp.AddMilliseconds(484));
        Equal(0, output.Spoken.Count, "Potion typewriter fragments remain silent before stability");

        Dispatch(
            gate,
            dispatcher,
            Present(2, "Received \"Potion\"!"),
            Timestamp.AddMilliseconds(485));
        Dispatch(
            gate,
            dispatcher,
            Present(2, "Received \"Potion\"!"),
            Timestamp.AddMilliseconds(520));

        Equal(1, output.Spoken.Count, "complete Potion pickup is spoken once");
        Equal("Received \"Potion\"!", output.Spoken[0].Text, "native Potion text is not rewritten");
        Equal(true, output.Spoken[0].Interrupt, "speakerless dialogue starts a fresh speech batch");
    }

    private static void BiggsTypewriterGrowthSpeaksOnlyTheCompleteVisibleMessage()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        var fragments = new[]
        {
            "Biggs",
            "Biggs Wow! You used to",
            "Biggs Wow! You used to be in SOLDIE",
            "Biggs Wow! You used to be in SOLDIER, huh? ...",
            "Biggs Wow! You used to be in SOLDIER, huh? ... Not everyday",
            "Biggs Wow! You used to be in SOLDIER, huh? ... Not everyday ya fi",
            "Biggs Wow! You used to be in SOLDIER, huh? ... Not everyday ya find one in a g",
            "Biggs Wow! You used to be in SOLDIER, huh? ... Not everyday ya find one in a group l",
            "Biggs Wow! You used to be in SOLDIER, huh? ... Not everyday ya find one in a group like AVALANCH",
            "Biggs Wow! You used to be in SOLDIER, huh? ... Not everyday ya find one in a group like AVALANCHE."
        };

        for (var index = 0; index < fragments.Length; index++)
        {
            Dispatch(
                gate,
                dispatcher,
                Present(index + 1, fragments[index]),
                Timestamp.AddMilliseconds(index * 35));
        }

        var lastChange = Timestamp.AddMilliseconds((fragments.Length - 1) * 35);
        Dispatch(gate, dispatcher, Present(fragments.Length, fragments[^1]), lastChange.AddMilliseconds(449));
        Equal(0, output.Spoken.Count, "Biggs growth fragments remain silent before stability");

        Dispatch(gate, dispatcher, Present(fragments.Length, fragments[^1]), lastChange.AddMilliseconds(450));
        Equal(1, output.Spoken.Count, "Biggs complete visible message is spoken once");
        Equal(fragments[^1], output.Spoken[0].Text, "Biggs native visible text is preserved exactly");
    }

    private static void ExplicitCloseAllowsAnIdenticalPickupToSpeakAgain()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string pickup = "Received \"Potion\"!";

        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp);
        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp.AddMilliseconds(450));
        Dispatch(
            gate,
            dispatcher,
            RuntimeDomainUpdate<DialoguePageObservation>.Closed,
            Timestamp.AddMilliseconds(500));
        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp.AddMilliseconds(600));
        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp.AddMilliseconds(1050));

        Equal(2, output.Spoken.Count, "separate identical Potion pickup lifecycles both speak");
        Equal(pickup, output.Spoken[0].Text, "first Potion pickup text");
        Equal(pickup, output.Spoken[1].Text, "second Potion pickup text");
    }

    private static void UnchangedObservationRetainsThePendingCandidate()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string text = "Jessie SOLDIER? Aren't they the enemy?";

        Dispatch(gate, dispatcher, Present(1, text), Timestamp);
        Dispatch(
            gate,
            dispatcher,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            Timestamp.AddMilliseconds(200));
        Dispatch(gate, dispatcher, Present(1, text), Timestamp.AddMilliseconds(450));

        Equal(1, output.Spoken.Count, "transient unavailable observation preserves pending dialogue");
        Equal(text, output.Spoken[0].Text, "retained dialogue remains exact");
    }

    private static void FailedOutputRetriesTheCompletedCandidate()
    {
        var output = new Output { FailuresRemaining = 1 };
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string pickup = "Received \"Potion\"!";

        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp);
        var failed = false;
        try
        {
            Dispatch(gate, dispatcher, Present(1, pickup), Timestamp.AddMilliseconds(450));
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        Equal(true, failed, "first completed Potion output failure propagates");
        Equal(0, output.Spoken.Count, "failed output is not acknowledged");

        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp.AddMilliseconds(451));
        Equal(1, output.Spoken.Count, "completed Potion output retries after failure");
        Equal(pickup, output.Spoken[0].Text, "retried Potion text remains exact");
    }

    private static void SuppressedStablePageIsDiscardedWhenTheNativeWindowCloses()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string text = "Biggs Wow! You used to be in SOLDIER, huh?";

        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            gate.Observe(Present(1, text), Timestamp).Kind,
            "initial Biggs text waits for stability");
        var stable = gate.Observe(Present(1, text), Timestamp.AddMilliseconds(450));
        Equal(RuntimeDomainUpdateKind.Present, stable.Kind, "Biggs text becomes speech eligible");
        Equal(
            true,
            gate.MarkDeliverySuppressed(stable.Value!, suppressed: true),
            "cutscene ownership is attached to the exact pending dialogue page");

        var suppressedAcknowledgement = DispatchUpdate(
            dispatcher,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            Timestamp.AddMilliseconds(450));
        Equal<DialoguePageObservation?>(
            null,
            suppressedAcknowledgement,
            "cutscene-owned dispatcher call cannot acknowledge dialogue");

        var close = gate.Observe(
            RuntimeDomainUpdate<DialoguePageObservation>.Closed,
            Timestamp.AddMilliseconds(451));
        Equal(
            RuntimeDomainUpdateKind.Closed,
            close.Kind,
            "native close discards a cutscene-suppressed page the player skipped");
        var preservedClose = Steam2026ResearchSession.ApplyCutsceneDialogueSuppression(
            close,
            suppressDialogue: true);
        Equal(RuntimeDomainUpdateKind.Closed, preservedClose.Kind, "cutscene suppression preserves queued close");
        _ = DispatchUpdate(dispatcher, preservedClose, Timestamp.AddMilliseconds(451));
        Equal(true, gate.AcknowledgeClose(), "dispatcher reset acknowledges queued close");
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            gate.Observe(
                RuntimeDomainUpdate<DialoguePageObservation>.Closed,
                Timestamp.AddMilliseconds(452)).Kind,
            "discarded skipped dialogue cannot leak into later gameplay");
        Equal(0, output.Spoken.Count, "skipped cutscene-suppressed dialogue is never spoken late");
    }

    private static void FailedStablePageSurvivesCloseUntilSuccessfulRetry()
    {
        var output = new Output { FailuresRemaining = 1 };
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string pickup = "Received \"Potion\"!";

        gate.Observe(Present(1, pickup), Timestamp);
        var stable = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(450));
        var failed = false;
        try
        {
            _ = DispatchUpdate(dispatcher, stable, Timestamp.AddMilliseconds(450));
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        Equal(true, failed, "stable Potion output fails before acknowledgement");
        var retained = gate.Observe(
            RuntimeDomainUpdate<DialoguePageObservation>.Closed,
            Timestamp.AddMilliseconds(451));
        Equal(RuntimeDomainUpdateKind.Present, retained.Kind, "failed stable Potion survives native close");

        var acknowledgement = DispatchUpdate(dispatcher, retained, Timestamp.AddMilliseconds(452));
        Equal(pickup, acknowledgement?.VisibleText, "successful closed-window retry identifies Potion page");
        Equal(true, gate.AcknowledgeDelivery(acknowledgement!), "closed-window Potion retry is acknowledged");
        var closeAtHead = gate.Observe(
            RuntimeDomainUpdate<DialoguePageObservation>.Closed,
            Timestamp.AddMilliseconds(453));
        Equal(
            RuntimeDomainUpdateKind.Closed,
            closeAtHead.Kind,
            "failed Potion lifecycle close follows successful retry");
        _ = DispatchUpdate(dispatcher, closeAtHead, Timestamp.AddMilliseconds(453));
        Equal(true, gate.AcknowledgeClose(), "failed Potion lifecycle close is acknowledged");
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            gate.Observe(
                RuntimeDomainUpdate<DialoguePageObservation>.Closed,
                Timestamp.AddMilliseconds(454)).Kind,
            "acknowledged close is not requeued while native source remains closed");
        Equal(1, output.Spoken.Count, "failed-then-closed Potion is spoken exactly once");
    }

    private static void SuppressedCloseResetsDispatcherBeforeIdenticalReopen()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string pickup = "Received \"Potion\"!";

        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp);
        Dispatch(gate, dispatcher, Present(1, pickup), Timestamp.AddMilliseconds(450));
        Equal(1, output.Spoken.Count, "first identical lifecycle speaks");

        var close = gate.Observe(
            RuntimeDomainUpdate<DialoguePageObservation>.Closed,
            Timestamp.AddMilliseconds(500));
        var closeDuringCutscene = Steam2026ResearchSession.ApplyCutsceneDialogueSuppression(
            close,
            suppressDialogue: true);
        Equal(RuntimeDomainUpdateKind.Closed, closeDuringCutscene.Kind, "suppression cannot hide close");
        _ = DispatchUpdate(dispatcher, closeDuringCutscene, Timestamp.AddMilliseconds(500));
        Equal(true, gate.AcknowledgeClose(), "suppressed-lifecycle close reset is acknowledged");

        var reopened = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(600));
        Equal(RuntimeDomainUpdateKind.Unchanged, reopened.Kind, "identical reopen waits for stability");
        var reopenedStable = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(1050));
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            Steam2026ResearchSession.ApplyCutsceneDialogueSuppression(
                reopenedStable,
                suppressDialogue: true).Kind,
            "suppression still hides ordinary present dialogue");
        var reopenedForDispatch = Steam2026ResearchSession.ApplyCutsceneDialogueSuppression(
            reopenedStable,
            suppressDialogue: false);
        var acknowledgement = DispatchUpdate(
            dispatcher,
            reopenedForDispatch,
            Timestamp.AddMilliseconds(1050));
        Equal(true, gate.AcknowledgeDelivery(acknowledgement!), "identical reopened page is acknowledged");

        Equal(2, output.Spoken.Count, "identical reopened page speaks after suppressed close");
        Equal(pickup, output.Spoken[0].Text, "first identical lifecycle text");
        Equal(pickup, output.Spoken[1].Text, "reopened identical lifecycle text");
    }

    private static void StablePagePrecedesSuccessorAfterCutsceneSuppression()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string first = "First stable page.";
        const string second = "Second stable page.";

        gate.Observe(Present(1, first), Timestamp);
        var firstStable = gate.Observe(Present(1, first), Timestamp.AddMilliseconds(450));
        Equal(RuntimeDomainUpdateKind.Present, firstStable.Kind, "first page becomes stable");
        Equal<DialoguePageObservation?>(
            null,
            DispatchUpdate(
                dispatcher,
                RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
                Timestamp.AddMilliseconds(450)),
            "cutscene suppression does not acknowledge first page");

        var whileSecondStarts = gate.Observe(
            Present(2, second),
            Timestamp.AddMilliseconds(451));
        Equal(first, whileSecondStarts.Value?.VisibleText, "stable first page remains FIFO head");

        var whileSecondStabilizes = gate.Observe(
            Present(2, second),
            Timestamp.AddMilliseconds(901));
        Equal(first, whileSecondStabilizes.Value?.VisibleText, "stable successor queues behind first page");

        var firstAcknowledgement = DispatchUpdate(
            dispatcher,
            whileSecondStabilizes,
            Timestamp.AddMilliseconds(901));
        Equal(true, gate.AcknowledgeDelivery(firstAcknowledgement!), "first FIFO page is acknowledged");

        var secondStable = gate.Observe(
            Present(2, second),
            Timestamp.AddMilliseconds(902));
        Equal(second, secondStable.Value?.VisibleText, "second page follows acknowledged first page");
        var secondAcknowledgement = DispatchUpdate(
            dispatcher,
            secondStable,
            Timestamp.AddMilliseconds(902));
        Equal(true, gate.AcknowledgeDelivery(secondAcknowledgement!), "second FIFO page is acknowledged");

        Equal(2, output.Spoken.Count, "both suppressed FIFO pages are ultimately spoken");
        Equal(first, output.Spoken[0].Text, "first suppressed FIFO speech");
        Equal(second, output.Spoken[1].Text, "second suppressed FIFO speech");
    }

    private static void StablePagePrecedesSuccessorAfterOutputFailure()
    {
        var output = new Output { FailuresRemaining = 1 };
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string first = "First failed page.";
        const string second = "Second page after failure.";

        gate.Observe(Present(1, first), Timestamp);
        var firstStable = gate.Observe(Present(1, first), Timestamp.AddMilliseconds(450));
        try
        {
            _ = DispatchUpdate(dispatcher, firstStable, Timestamp.AddMilliseconds(450));
        }
        catch (InvalidOperationException)
        {
            // The first stable page remains unacknowledged for retry.
        }

        var whileSecondStarts = gate.Observe(
            Present(2, second),
            Timestamp.AddMilliseconds(451));
        Equal(first, whileSecondStarts.Value?.VisibleText, "failed first page remains FIFO head");
        var firstAcknowledgement = DispatchUpdate(
            dispatcher,
            whileSecondStarts,
            Timestamp.AddMilliseconds(452));
        Equal(true, gate.AcknowledgeDelivery(firstAcknowledgement!), "retried first page is acknowledged");

        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            gate.Observe(Present(2, second), Timestamp.AddMilliseconds(900)).Kind,
            "second page still waits for its own stability interval");
        var secondStable = gate.Observe(
            Present(2, second),
            Timestamp.AddMilliseconds(901));
        Equal(second, secondStable.Value?.VisibleText, "second page survives first-page output failure");
        var secondAcknowledgement = DispatchUpdate(
            dispatcher,
            secondStable,
            Timestamp.AddMilliseconds(901));
        Equal(true, gate.AcknowledgeDelivery(secondAcknowledgement!), "post-failure successor is acknowledged");

        Equal(2, output.Spoken.Count, "both failure-path FIFO pages are spoken");
        Equal(first, output.Spoken[0].Text, "retried first failure-path speech");
        Equal(second, output.Spoken[1].Text, "second failure-path speech");
    }

    private static void StableSuppressedPageCloseAndIdenticalReopenStayOrdered()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        const string pickup = "Received \"Potion\"!";

        gate.Observe(Present(1, pickup), Timestamp);
        var firstStable = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(450));
        Equal(RuntimeDomainUpdateKind.Present, firstStable.Kind, "first identical page becomes stable");
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            Steam2026ResearchSession.ApplyCutsceneDialogueSuppression(
                firstStable,
                suppressDialogue: true).Kind,
            "cutscene suppresses first identical page");

        var afterClose = gate.Observe(
            RuntimeDomainUpdate<DialoguePageObservation>.Closed,
            Timestamp.AddMilliseconds(451));
        Equal(pickup, afterClose.Value?.VisibleText, "pending first page remains ahead of close marker");

        var afterReopenStarts = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(500));
        Equal(pickup, afterReopenStarts.Value?.VisibleText, "first page remains ahead of reopened page");
        var afterReopenStabilizes = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(950));
        Equal(pickup, afterReopenStabilizes.Value?.VisibleText, "stable reopened page queues behind close");

        var firstAcknowledgement = DispatchUpdate(
            dispatcher,
            afterReopenStabilizes,
            Timestamp.AddMilliseconds(950));
        Equal(true, gate.AcknowledgeDelivery(firstAcknowledgement!), "first identical page is acknowledged");

        var closeAtHead = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(951));
        Equal(RuntimeDomainUpdateKind.Closed, closeAtHead.Kind, "close follows first page in FIFO order");
        var closeDuringSuppression = Steam2026ResearchSession.ApplyCutsceneDialogueSuppression(
            closeAtHead,
            suppressDialogue: true);
        Equal(RuntimeDomainUpdateKind.Closed, closeDuringSuppression.Kind, "suppression preserves queued close");
        _ = DispatchUpdate(dispatcher, closeDuringSuppression, Timestamp.AddMilliseconds(951));
        Equal(true, gate.AcknowledgeClose(), "dispatcher reset acknowledges queued close");

        var secondStable = gate.Observe(Present(1, pickup), Timestamp.AddMilliseconds(952));
        Equal(pickup, secondStable.Value?.VisibleText, "reopened identical page follows close marker");
        var secondAcknowledgement = DispatchUpdate(
            dispatcher,
            secondStable,
            Timestamp.AddMilliseconds(952));
        Equal(true, gate.AcknowledgeDelivery(secondAcknowledgement!), "reopened identical page is acknowledged");

        Equal(2, output.Spoken.Count, "identical pages on opposite sides of close both speak");
        Equal(pickup, output.Spoken[0].Text, "pre-close identical speech");
        Equal(pickup, output.Spoken[1].Text, "post-close identical speech");
    }

    private static void UnstablePrefixIsNotRetainedAcrossClose()
    {
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            gate.Observe(Present(1, "Recei"), Timestamp).Kind,
            "Potion prefix starts pending stability");
        Equal(
            RuntimeDomainUpdateKind.Closed,
            gate.Observe(
                RuntimeDomainUpdate<DialoguePageObservation>.Closed,
                Timestamp.AddMilliseconds(100)).Kind,
            "unstable Potion prefix is discarded on close");
    }

    private static void SeparateSpeakerlessPagesEachInterrupt()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var gate = new Steam2026FieldDialogueSpeechStabilityGate(StableWindow);

        Dispatch(gate, dispatcher, Present(1, "First visible page."), Timestamp);
        Dispatch(
            gate,
            dispatcher,
            Present(1, "First visible page."),
            Timestamp.AddMilliseconds(450));
        Dispatch(
            gate,
            dispatcher,
            Present(2, "Second visible page."),
            Timestamp.AddMilliseconds(500));
        Dispatch(
            gate,
            dispatcher,
            Present(2, "Second visible page."),
            Timestamp.AddMilliseconds(950));

        Equal(2, output.Spoken.Count, "two stable speakerless pages both speak");
        Equal(true, output.Spoken[0].Interrupt, "first speakerless page interrupts");
        Equal(true, output.Spoken[1].Interrupt, "second speakerless page interrupts");
    }

    private static RuntimeEventDispatcher CreateDispatcher(Output output) =>
        new(
            new AccessibilityConfig
            {
                EnableSpeech = true,
                EnableRuntimeDialogueSpeech = true
            },
            output,
            _ => { });

    private static RuntimeDomainUpdate<DialoguePageObservation> Present(
        int revision,
        string text) =>
        RuntimeDomainUpdate<DialoguePageObservation>.Present(
            new DialoguePageObservation(
                isOpen: true,
                windowId: 0,
                pageRevision: revision,
                speaker: string.Empty,
                visibleText: text,
                choices: Array.Empty<DialogueChoiceObservation>()));

    private static RuntimeDomainUpdate<DialoguePageObservation> Ask(
        int revision,
        int selectedIndex) =>
        RuntimeDomainUpdate<DialoguePageObservation>.Present(
            new DialoguePageObservation(
                isOpen: true,
                windowId: 0,
                pageRevision: revision,
                speaker: string.Empty,
                visibleText: "What happened?",
                choices:
                [
                    new DialogueChoiceObservation(0, "Buy one", true, selectedIndex == 0),
                    new DialogueChoiceObservation(1, "Forget it", true, selectedIndex == 1)
                ]));

    private static RuntimeDomainUpdate<DialoguePageObservation> SelectionOnlyAsk(
        int revision,
        int selectedIndex) =>
        RuntimeDomainUpdate<DialoguePageObservation>.Present(
            new DialoguePageObservation(
                isOpen: true,
                windowId: 0,
                pageRevision: revision,
                speaker: string.Empty,
                visibleText: string.Empty,
                choices:
                [
                    new DialogueChoiceObservation(0, "Buy one", true, selectedIndex == 0),
                    new DialogueChoiceObservation(1, "Forget it", true, selectedIndex == 1)
                ]));

    private static void Dispatch(
        Steam2026FieldDialogueSpeechStabilityGate gate,
        RuntimeEventDispatcher dispatcher,
        RuntimeDomainUpdate<DialoguePageObservation> update,
        DateTime now)
    {
        var stabilized = gate.Observe(update, now);
        var acknowledgement = DispatchUpdate(dispatcher, stabilized, now);
        if (acknowledgement is not null)
        {
            Equal(
                true,
                gate.AcknowledgeDelivery(acknowledgement),
                "dispatcher acknowledgement matches the current stable page");
        }
        else if (stabilized.Kind == RuntimeDomainUpdateKind.Closed)
        {
            Equal(true, gate.AcknowledgeClose(), "dispatcher acknowledges dialogue close");
        }
    }

    private static DialoguePageObservation? DispatchUpdate(
        RuntimeEventDispatcher dispatcher,
        RuntimeDomainUpdate<DialoguePageObservation> update,
        DateTime now)
    {
        var frame = new RuntimeFrameObservation(
            now,
            new GameLifecycleObservation(true, false, 1, 0),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            update,
            RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);
        return dispatcher.DispatchWithDialogueAcknowledgement(
            new RuntimeDispatchBatch(frame, Array.Empty<RuntimeEvent>(), null),
            now);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Output : IAccessibilityOutput
    {
        public List<(string Text, bool Interrupt)> Spoken { get; } = [];

        public int FailuresRemaining { get; set; }

        public void Speak(string text, bool interrupt)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("simulated Prism failure");
            }

            Spoken.Add((text, interrupt));
        }

        public void PlayCue(AccessibilityCue cue)
        {
        }

        public void StopCue(AccessibilityCueKind kind)
        {
        }
    }
}
