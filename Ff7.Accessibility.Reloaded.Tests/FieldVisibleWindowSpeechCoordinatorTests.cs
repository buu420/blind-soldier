using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldVisibleWindowSpeechCoordinatorTests
{
    public static void Run()
    {
        SpeaksSimultaneousWindowsOnceInNativeOrder();
        HoldsReadyLaterWindowBehindEarlierSettlingWindow();
        DispatchesReadyEarlierWindowAndQueuesLaterSettlingWindow();
        LateJoiningOverlapQueuesWithoutInterruptingVisibleSpeech();
        KeepsReadyEarlierWindowWhenLaterSiblingOutlivesIt();
        RetainsHeldLaterWindowAfterItClosesBeforeEarlierSettles();
        FlushesStableHeldTextWhenTheWholeNativeSetCloses();
        NativeOwnedBlockerMustBeAcknowledgedBeforeHeldPollingSpeech();
        NativeAcknowledgementBeforeSuppressedPollIsConsumedOnce();
        DeliveredNativeBlockerKeepsClosedHeldWindowNonInterrupting();
        DeliveredOwnershipRehydratesAfterCoordinatorReset();
        ConsecutiveSameWindowAskIdentitiesDoNotShareAcknowledgement();
        ConsecutiveSameExactAskIdentityRequiresNewDelivery();
        LostNativeCandidateCannotLeavePermanentPendingBlocker();
        SamePointerWindowSpeaksNewTextAfterNativeOwnershipClears();
        NativeMessageLifecycleReopensIdenticalSamePointerText();
        RetainedOldLifecycleDoesNotCollapseIdenticalReopen();
        PendingNativeSpeechRequiresCurrentExactAskIdentity();
        CancelingNativeSpeechReleasesHeldPollingWindows();
        TransientUnavailableScanPreservesHeldText();
        NativeAskWaitsForOlderRetainedPollingSpeech();
        NativeAskDoesNotWaitForLaterHeldWindow();
        FailedPollingPredecessorKeepsNativeAskBlocked();
        FailedLaterBatchItemRetriesWithoutInterruptingEarlierSpeech();
        OpenHigherWindowSpeechMakesLowerAskNonInterrupting();
        UnavailableObservationWaitIsBounded();
        UnavailableCapturedPredecessorWaitIsBounded();
        ObservedThenUnavailablePredecessorWaitIsBounded();
        NativeRetryRechecksNewPredecessors();
        PartialNativePromptRecoveryDoesNotInterruptPrompt();
        PartialNativePromptKeepsLaterSiblingBlocked();
        ActiveZeroRetainsLaterSiblingForUnboundPartialPrompt();
        PromptWithSettlingChoiceKeepsInterveningWindowNonInterrupting();
        PendingAskRebindsHeldSiblingAcrossCountZero();
        PendingAskKeepsClosedHeldSiblingAcrossCountZero();
        PendingAskKeepsClosedHeldSiblingWithoutCountZero();
        PollingFallbackChoiceWaitsForQuestionRecovery();
        PollingFallbackChoiceWaitIsBoundedWithoutReader();
        CompletedFallbackChoiceDoesNotReblockLaterRetry();
        TimedOutFallbackChoiceRetainsLaterSiblingUntilQuestionAck();
        ActiveZeroFlushesOnlyAskPredecessors();
        MergesRetainedWindowsBackIntoCurrentNativeOrder();
        PreservesRetainedChronologyWithinOneNativeWindow();
        ReopenedSamePointerWindowGetsANewLifecycleToken();
        PreservesOrderAfterNativeOwnedWindowIsSuppressed();
        ResetsDedupOnlyAfterTheNativeWindowLifecycleCloses();
        UnqueuedAskIdentityDoesNotClaimPollingWindow();
        AskOwnershipDoesNotSuppressOverlappingTextCollisions();
        TypewriterGrowthAfterAVisiblePauseSpeaksOnlyTheNewSuffix();
    }

    private static void SpeaksSimultaneousWindowsOnceInNativeOrder()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot[] windows =
        [
            new(0, 2, "Barret: Move out!", 0x700040),
            new(2, 1, "Cloud: Right behind you.", 0x700080)
        ];

        Equal(0, coordinator.Observe(windows, 1, now).Count, "simultaneous windows wait for stable native text");
        var speech = coordinator.Observe(windows, 1, now.AddMilliseconds(100));

        Equal(2, speech.Count, "every simultaneously visible native window is preserved");
        Equal(0, speech[0].WindowId, "first dispatch keeps first native window");
        Equal("Barret: Move out!", speech[0].Text, "first dispatch keeps exact native text");
        Equal(true, speech[0].Interrupt, "first dispatch starts the speech batch");
        Equal(2, speech[1].WindowId, "second dispatch keeps second native window");
        Equal("Cloud: Right behind you.", speech[1].Text, "second dispatch keeps exact native text");
        Equal(false, speech[1].Interrupt, "later dispatch queues without interrupting earlier native text");
        Equal(0, coordinator.Observe(windows, 1, now.AddMilliseconds(200)).Count, "unchanged simultaneous windows do not repeat");
    }

    private static void HoldsReadyLaterWindowBehindEarlierSettlingWindow()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Later window.", 0x700080);
        FieldVisibleWindowSnapshot earlier = new(0, 2, "Barret: Earlier window.", 0x700040);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([earlier, later], 1, now.AddMilliseconds(50));
        Equal(
            0,
            coordinator.Observe([earlier, later], 1, now.AddMilliseconds(100)).Count,
            "ready later window waits behind an earlier native window that is still settling");

        var speech = coordinator.Observe([earlier, later], 1, now.AddMilliseconds(150));
        Equal(2, speech.Count, "held later window dispatches with the earlier window once native order is ready");
        Equal(0, speech[0].WindowId, "staggered batch dispatches earlier native window first");
        Equal(true, speech[0].Interrupt, "earlier staggered window starts the batch");
        Equal(1, speech[1].WindowId, "staggered batch dispatches held later window second");
        Equal(false, speech[1].Interrupt, "held later window cannot interrupt the earlier window");
    }

    private static void DispatchesReadyEarlierWindowAndQueuesLaterSettlingWindow()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot earlier = new(0, 2, "Barret: Earlier window.", 0x700040);
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Later window.", 0x700080);

        coordinator.Observe([earlier], 1, now);
        coordinator.Observe([earlier, later], 1, now.AddMilliseconds(50));
        var earlierSpeech = coordinator.Observe([earlier, later], 1, now.AddMilliseconds(100));
        Equal(1, earlierSpeech.Count, "ready earlier window is not lost behind a later settling sibling");
        Equal(0, earlierSpeech[0].WindowId, "ready native-order prefix dispatches its earlier window");
        Equal(true, earlierSpeech[0].Interrupt, "first utterance opens the overlapping speech batch");

        var laterSpeech = coordinator.Observe([earlier, later], 1, now.AddMilliseconds(150));
        Equal(1, laterSpeech.Count, "later sibling dispatches when its own native text is stable");
        Equal(1, laterSpeech[0].WindowId, "later-settling sibling retains its native position");
        Equal(false, laterSpeech[0].Interrupt, "later-settling sibling cannot cut off the earlier utterance");
    }

    private static void LateJoiningOverlapQueuesWithoutInterruptingVisibleSpeech()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot earlier = new(0, 2, "Barret: I am still talking.", 0x700040);
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: I joined later.", 0x700080);

        coordinator.Observe([earlier], 1, now);
        var earlierSpeech = coordinator.Observe([earlier], 1, now.AddTicks(1));
        Equal(true, earlierSpeech[0].Interrupt, "first visible window opens its speech batch");

        coordinator.Observe([earlier, later], 1, now.AddTicks(2));
        var laterSpeech = coordinator.Observe([earlier, later], 1, now.AddTicks(3));
        Equal(1, laterSpeech.Count, "late-joining overlapping window speaks once stable");
        Equal(1, laterSpeech[0].WindowId, "late-joining overlapping window remains identifiable");
        Equal(false, laterSpeech[0].Interrupt, "late-joining overlap queues behind visible speech");
    }

    private static void KeepsReadyEarlierWindowWhenLaterSiblingOutlivesIt()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot earlier = new(0, 2, "Barret: Do not lose this line.", 0x700040);
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Still settling.", 0x700080);

        coordinator.Observe([earlier], 1, now);
        coordinator.Observe([earlier, later], 1, now.AddMilliseconds(50));
        var earlierSpeech = coordinator.Observe([earlier, later], 1, now.AddMilliseconds(100));
        Equal(1, earlierSpeech.Count, "ready earlier window dispatches before it closes");
        Equal(0, earlierSpeech[0].WindowId, "closing earlier window is not dropped behind its sibling");

        Equal(0, coordinator.Observe([later], 1, now.AddMilliseconds(125)).Count, "surviving sibling may continue settling");
        var laterSpeech = coordinator.Observe([later], 1, now.AddMilliseconds(150));
        Equal(1, laterSpeech.Count, "surviving sibling eventually speaks");
        Equal(false, laterSpeech[0].Interrupt, "surviving sibling remains in the open overlap batch after the earlier window closes");
    }

    private static void RetainsHeldLaterWindowAfterItClosesBeforeEarlierSettles()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Visible long enough to read.", 0x700080);
        FieldVisibleWindowSnapshot earlier = new(0, 2, "Barret: Still settling.", 0x700040);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([earlier, later], 1, now.AddMilliseconds(50));
        Equal(
            0,
            coordinator.Observe([earlier, later], 1, now.AddMilliseconds(100)).Count,
            "ready later window waits behind an earlier native-order blocker");
        Equal(
            0,
            coordinator.Observe([earlier], 1, now.AddMilliseconds(125)).Count,
            "closed held window remains queued while its earlier blocker is settling");

        var speech = coordinator.Observe([earlier], 1, now.AddMilliseconds(150));
        Equal(2, speech.Count, "held sighted-visible text survives closure until native order can dispatch");
        Equal(0, speech[0].WindowId, "earlier blocker dispatches first once stable");
        Equal(true, speech[0].Interrupt, "earlier blocker opens the retained batch");
        Equal(1, speech[1].WindowId, "closed held later window retains its native order");
        Equal(false, speech[1].Interrupt, "closed held later window queues without cutting off the earlier utterance");
    }

    private static void FlushesStableHeldTextWhenTheWholeNativeSetCloses()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: This stable visible line must survive close.", 0x700080);
        FieldVisibleWindowSnapshot earlier = new(0, 2, "Barret: Never stabilized.", 0x700040);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([earlier, later], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([earlier, later], 1, now.AddMilliseconds(100)).Count, "stable later line is held in native order");

        var speech = coordinator.Observe([], 0, now.AddMilliseconds(125));
        Equal(1, speech.Count, "stable sighted-visible held text is flushed instead of lost when all windows close");
        Equal(1, speech[0].WindowId, "closed-set flush retains held window identity");
        Equal(true, speech[0].Interrupt, "held-only close flush starts one final speech batch");
        Equal(0, coordinator.Observe([], 0, now.AddMilliseconds(150)).Count, "close flush clears the held lifecycle exactly once");
    }

    private static void NativeOwnedBlockerMustBeAcknowledgedBeforeHeldPollingSpeech()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Queue me after the ASK prompt.", 0x700080);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([ask, later], 1, now.AddMilliseconds(100)).Count, "later polling line is held behind settling ASK prompt");
        Equal(
            0,
            coordinator.Observe(
                [ask, later],
                1,
                now.AddMilliseconds(125),
                window => window.WindowId == 0,
                askIdentity).Count,
            "native-owned ASK blocker does not release polling speech before its queued utterance is emitted");

        coordinator.AcknowledgeNativeSpeech(askIdentity);
        var speech = coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(150),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: true);
        Equal(1, speech.Count, "held polling line releases after native ASK speech acknowledgement");
        Equal(1, speech[0].WindowId, "held polling line retains the later native window");
        Equal(false, speech[0].Interrupt, "held polling line queues behind acknowledged native ASK speech");
    }

    private static void NativeAcknowledgementBeforeSuppressedPollIsConsumedOnce()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Release after a skipped scan.", 0x700080);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([ask, later], 1, now.AddMilliseconds(100)).Count, "later line is retained before the skipped ownership scan");

        coordinator.AcknowledgeNativeSpeech(askIdentity);
        var speech = coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(150),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: true);
        Equal(1, speech.Count, "pre-observation native acknowledgement releases retained polling speech");
        Equal(1, speech[0].WindowId, "pre-observation acknowledgement retains later window identity");
        Equal(false, speech[0].Interrupt, "pre-observation acknowledgement opens the native speech batch first");

        Equal(
            0,
            coordinator.Observe(
                [ask, later],
                1,
                now.AddMilliseconds(175),
                window => window.WindowId == 0,
                askIdentity).Count,
            "one-shot native acknowledgement is consumed without duplicating retained speech");
    }

    private static void DeliveredNativeBlockerKeepsClosedHeldWindowNonInterrupting()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: I closed after being visible.", 0x700080);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([ask, later], 1, now.AddMilliseconds(100)).Count, "later line is retained behind ASK before it closes");

        var speech = coordinator.Observe(
            [ask],
            1,
            now.AddMilliseconds(125),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: true);
        Equal(1, speech.Count, "closed retained line releases after delivered native ASK");
        Equal(1, speech[0].WindowId, "closed retained line keeps its identity");
        Equal(false, speech[0].Interrupt, "closed retained line cannot cut off the delivered native ASK prompt");
    }

    private static void DeliveredOwnershipRehydratesAfterCoordinatorReset()
    {
        var now = Utc(0);
        var ownership = new NativeFieldMessageOwnershipTracker(TimeSpan.FromSeconds(5));
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        ownership.ObserveNative(askIdentity, "Choose an answer.", now);
        ownership.MarkSpeechDelivered(askIdentity, now.AddTicks(1));
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        coordinator.Reset();
        FieldVisibleWindowSnapshot[] windows =
        [
            new(0, 2, "Choose an answer.", 0x700040),
            new(1, 2, "Cloud: Still accessible after a reader reset.", 0x700080)
        ];

        coordinator.Observe(
            windows,
            1,
            now.AddTicks(2),
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now.AddTicks(2)),
            askIdentity,
            ownership.WasSpeechDelivered(askIdentity, now.AddTicks(2)));
        var speech = coordinator.Observe(
            windows,
            1,
            now.AddTicks(3),
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now.AddTicks(3)),
            askIdentity,
            ownership.WasSpeechDelivered(askIdentity, now.AddTicks(3)));
        Equal(1, speech.Count, "delivered exact ownership survives coordinator reset without recreating a pending blocker");
        Equal(1, speech[0].WindowId, "unrelated polling window remains available after ownership rehydration");
        Equal(false, speech[0].Interrupt, "rehydrated delivered native ownership remains the first speech in its batch");
        Equal(0, coordinator.Observe([], 0, now.AddTicks(4)).Count, "rehydrated delivered ownership does not block native close reset");
        Equal(0, coordinator.Observe([], 0, now.AddTicks(5)).Count, "native close reset remains idempotent after delivered ownership");
    }

    private static void ConsecutiveSameWindowAskIdentitiesDoNotShareAcknowledgement()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Wait behind both prompts.", 0x700080);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        var firstAsk = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        var secondAsk = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 39);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(50));
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(100));
        Equal(
            0,
            coordinator.Observe(
                [ask, later],
                1,
                now.AddMilliseconds(125),
                window => window.WindowId == 0,
                firstAsk).Count,
            "first exact ASK owns the native-order blocker while queued");
        coordinator.AcknowledgeNativeSpeech(firstAsk);

        Equal(
            0,
            coordinator.Observe(
                [ask, later],
                1,
                now.AddMilliseconds(130),
                window => window.WindowId == 0,
                secondAsk,
                nativeOwnershipDelivered: false).Count,
            "second dialog on the same native window cannot inherit the first ASK acknowledgement");

        coordinator.AcknowledgeNativeSpeech(secondAsk);
        var speech = coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(150),
            window => window.WindowId == 0,
            secondAsk,
            nativeOwnershipDelivered: true);
        Equal(1, speech.Count, "held polling speech releases only after the second exact ASK is delivered");
        Equal(false, speech[0].Interrupt, "held polling speech queues behind the second exact ASK");
    }

    private static void ConsecutiveSameExactAskIdentityRequiresNewDelivery()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Wait behind the reopened prompt.", 0x700080);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(50));
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(100));
        coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(125),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: false);
        coordinator.AcknowledgeNativeSpeech(askIdentity);

        Equal(
            0,
            coordinator.Observe(
                [ask, later],
                1,
                now.AddMilliseconds(130),
                window => window.WindowId == 0,
                askIdentity,
                nativeOwnershipDelivered: false).Count,
            "reopened exact ASK identity supersedes the prior delivered lifecycle");

        coordinator.AcknowledgeNativeSpeech(askIdentity);
        var speech = coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(150),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: true);
        Equal(1, speech.Count, "held polling speech releases after the reopened exact ASK is delivered");
        Equal(false, speech[0].Interrupt, "held polling speech queues behind the reopened exact ASK utterance");
    }

    private static void LostNativeCandidateCannotLeavePermanentPendingBlocker()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Do not strand this stable line.", 0x700080);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(50));
        coordinator.Observe([ask, later], 1, now.AddMilliseconds(100));
        coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(125),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: false,
            nativeOwnershipSpeechPending: true);

        Equal(
            0,
            coordinator.Observe(
                [],
                0,
                now.AddMilliseconds(130),
                nativeOwnershipIdentity: askIdentity,
                nativeOwnershipSpeechPending: true).Count,
            "active native speech candidate keeps the exact ordering blocker while awaiting emission");
        var speech = coordinator.Observe(
            [],
            0,
            now.AddMilliseconds(150),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: false);
        Equal(1, speech.Count, "lost native speech candidate releases stable retained text instead of deadlocking forever");
        Equal(1, speech[0].WindowId, "lost-candidate recovery retains the stable polling window");
        Equal(true, speech[0].Interrupt, "lost-candidate recovery starts a polling batch because no native utterance was delivered");
    }

    private static void SamePointerWindowSpeaksNewTextAfterNativeOwnershipClears()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Choose an answer.", 0x700040);
        FieldVisibleWindowSnapshot overlap = new(1, 2, "Cloud: Overlapping line.", 0x700080);

        coordinator.Observe(
            [ask, overlap],
            1,
            now,
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: true);
        coordinator.Observe(
            [ask, overlap],
            1,
            now.AddTicks(1),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipDelivered: true);

        var normal = ask with { Text = "Barret: New normal message in the same native buffer." };
        Equal(0, coordinator.Observe([normal, overlap], 1, now.AddTicks(2)).Count, "new same-pointer polling text gets its own stability observation");
        var speech = coordinator.Observe([normal, overlap], 1, now.AddTicks(3));
        Equal(1, speech.Count, "cleared native ownership cannot permanently bypass a reused native window buffer");
        Equal(0, speech[0].WindowId, "same-pointer reused window remains identifiable");
        Equal("Barret: New normal message in the same native buffer.", speech[0].Text, "same-pointer reused window speaks exact new native text");
    }

    private static void NativeMessageLifecycleReopensIdenticalSamePointerText()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot repeated = new(0, 2, "Barret: Identical reopened message.", 0x700040);
        FieldVisibleWindowSnapshot overlap = new(1, 2, "Cloud: Keeps the active count nonzero.", 0x700080);
        FieldVisibleWindowSnapshot[] windows = [repeated, overlap];

        coordinator.Observe(windows, 1, now);
        Equal(2, coordinator.Observe(windows, 1, now.AddTicks(1)).Count, "first normal native message lifecycle speaks");

        coordinator.BeginNativeMessageLifecycle(
            new NativeFieldMessageIdentity(FieldOpcodeKind.Message, 117, 0, 38));
        Equal(0, coordinator.Observe(windows, 1, now.AddTicks(2)).Count, "reopened identical native lifecycle gets a fresh stability observation");
        var speech = coordinator.Observe(windows, 1, now.AddTicks(3));
        Equal(1, speech.Count, "identical same-pointer normal message speaks again after native lifecycle reopen");
        Equal(0, speech[0].WindowId, "reopened identical normal message retains its window identity");
        Equal("Barret: Identical reopened message.", speech[0].Text, "reopened identical normal message retains exact native text");
    }

    private static void RetainedOldLifecycleDoesNotCollapseIdenticalReopen()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot blocker = new(0, 2, "Barret: Blocking lifecycle.", 0x700040);
        FieldVisibleWindowSnapshot repeated = new(1, 2, "Cloud: Identical across lifecycles.", 0x700080);

        coordinator.Observe([repeated], 1, now);
        coordinator.Observe([blocker, repeated], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([blocker, repeated], 1, now.AddMilliseconds(100)).Count, "old lifecycle text is retained behind blocker");

        coordinator.BeginNativeMessageLifecycle(
            new NativeFieldMessageIdentity(FieldOpcodeKind.Message, 117, 1, 38));
        blocker = blocker with { Text = "Barret: Still blocking lifecycle." };
        coordinator.Observe([blocker, repeated], 1, now.AddMilliseconds(110));
        var speech = coordinator.Observe([repeated], 1, now.AddMilliseconds(210));
        Equal(2, speech.Count, "retained old lifecycle and identical reopened lifecycle both remain observable");
        Equal("Cloud: Identical across lifecycles.", speech[0].Text, "old retained lifecycle speaks first");
        Equal("Cloud: Identical across lifecycles.", speech[1].Text, "identical reopened lifecycle speaks second");
        Equal(true, speech[0].Interrupt, "identical lifecycle batch interrupts once");
        Equal(false, speech[1].Interrupt, "identical reopened lifecycle queues behind retained old lifecycle");
    }

    private static void PendingNativeSpeechRequiresCurrentExactAskIdentity()
    {
        var identity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        var otherDialog = new NativeFieldMessageIdentity(
            identity.Kind,
            identity.FieldId,
            identity.WindowId,
            39,
            identity.LifecycleToken);
        Equal(
            true,
            NativeFieldSpeechIdentityValidator.IsCurrent(
                identity,
                identity,
                identity,
                FieldPositionReader.FieldModule,
                117,
                0,
                38),
            "pending ASK speech validates only against coherent exact current identity");
        Equal(false, NativeFieldSpeechIdentityValidator.IsCurrent(identity, null, null, FieldPositionReader.FieldModule, 117, 0, 38), "result-zero identity reset cancels pending ASK speech");
        Equal(false, NativeFieldSpeechIdentityValidator.IsCurrent(identity, identity, identity, 0, 117, 0, 38), "module leave cancels pending ASK speech");
        Equal(false, NativeFieldSpeechIdentityValidator.IsCurrent(identity, identity, identity, FieldPositionReader.FieldModule, 118, 0, 38), "field transition cancels pending ASK speech");
        Equal(false, NativeFieldSpeechIdentityValidator.IsCurrent(identity, identity, otherDialog, FieldPositionReader.FieldModule, 117, 0, 38), "identity change during validation cancels pending ASK speech");
        Equal(false, NativeFieldSpeechIdentityValidator.IsCurrent(identity, otherDialog, otherDialog, FieldPositionReader.FieldModule, 117, 0, 39), "new ASK cannot validate a displaced pending candidate");
        var valueEqualClone = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        Equal(false, NativeFieldSpeechIdentityValidator.IsCurrent(identity, valueEqualClone, valueEqualClone, FieldPositionReader.FieldModule, 117, 0, 38), "value-equal lifecycle clone cannot validate the exact published token reference");
    }

    private static void CancelingNativeSpeechReleasesHeldPollingWindows()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        FieldVisibleWindowSnapshot ask = new(0, 2, "Native ASK prompt.", 0x700040);
        FieldVisibleWindowSnapshot overlap = new(1, 2, "Cloud: Unrelated window remains visible.", 0x700080);

        Equal(
            0,
            coordinator.Observe([overlap], 1, now).Count,
            "overlap starts its stability observation before native ASK ownership");
        Equal(
            0,
            coordinator.Observe(
                [ask, overlap],
                1,
                now.AddTicks(1),
                window => window.WindowId == 0,
                askIdentity,
                nativeOwnershipDelivered: false,
                nativeOwnershipSpeechPending: true).Count,
            "unspoken native ASK blocks later polling speech");

        coordinator.CancelNativeSpeech(askIdentity);
        var speech = coordinator.Observe([overlap], 1, now.AddTicks(2));

        Equal(1, speech.Count, "result-zero cancellation releases held polling text while another window remains active");
        Equal(1, speech[0].WindowId, "only the unrelated native window is released");
        Equal("Cloud: Unrelated window remains visible.", speech[0].Text, "released polling text remains exact");
    }

    private static void TransientUnavailableScanPreservesHeldText()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38, 1);
        FieldVisibleWindowSnapshot ask = new(0, 2, "ASK", 0x700040);
        FieldVisibleWindowSnapshot overlap = new(1, 2, "Retained through one torn scan.", 0x700080);
        coordinator.Observe([overlap], 1, now);
        coordinator.Observe(
            [ask, overlap],
            1,
            now.AddTicks(1),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipSpeechPending: true);

        coordinator.ObserveUnavailable();
        coordinator.CancelNativeSpeech(askIdentity);
        var speech = coordinator.Observe([overlap], 1, now.AddTicks(2));

        Equal(1, speech.Count, "one unavailable scan cannot discard stable held native text");
        Equal("Retained through one torn scan.", speech[0].Text, "retained text stays exact after torn scan");
    }

    private static void NativeAskWaitsForOlderRetainedPollingSpeech()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot blocker = new(0, 2, "Barret: Earlier line.", 0x700040);
        FieldVisibleWindowSnapshot retained = new(1, 2, "Cloud: Older retained line.", 0x700080);
        coordinator.Observe([retained], 1, now);
        coordinator.Observe([blocker, retained], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([blocker, retained], 1, now.AddMilliseconds(100)).Count, "older line is held behind earlier settling line");

        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 39, 2);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(101), out _), "new ASK waits behind older retained native chronology");

        FieldVisibleWindowSnapshot ask = new(1, 2, "New ASK", retained.GuestPointer);
        var olderSpeech = coordinator.Observe(
            [blocker, ask],
            1,
            now.AddMilliseconds(150),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true);
        Equal(2, olderSpeech.Count, "earlier and retained lines dispatch before new ASK");
        Equal("Barret: Earlier line.", olderSpeech[0].Text, "earliest native line remains first");
        Equal("Cloud: Older retained line.", olderSpeech[1].Text, "retained reused-window line remains before ASK");
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(150), out var interrupt), "ASK releases only after predecessors are delivered");
        Equal(false, interrupt, "ASK queues behind predecessor speech instead of cutting it off");
    }

    private static void NativeAskDoesNotWaitForLaterHeldWindow()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot later = new(1, 2, "Later sibling.", 0x700080);
        coordinator.Observe([later], 1, now);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38, 3);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        FieldVisibleWindowSnapshot ask = new(0, 2, "ASK first", 0x700040);
        coordinator.Observe(
            [ask, later],
            1,
            now.AddMilliseconds(100),
            window => window.WindowId == 0,
            askIdentity,
            nativeOwnershipSpeechPending: true);

        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(100), out var interrupt), "later held sibling cannot deadlock earlier ASK");
        Equal(true, interrupt, "ASK starts the batch when it has no real predecessor");
    }

    private static void FailedPollingPredecessorKeepsNativeAskBlocked()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot predecessor = new(0, 2, "Earlier polling line.", 0x700040);
        coordinator.Observe([predecessor], 1, now);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 4);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        FieldVisibleWindowSnapshot ask = new(1, 2, "ASK", 0x700080);
        var firstAttempt = coordinator.Observe(
            [predecessor, ask],
            1,
            now.AddTicks(1),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, firstAttempt.Count, "earlier polling line is attempted before ASK");
        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(1), out _), "unacknowledged predecessor keeps ASK blocked");

        coordinator.AcknowledgePollingSpeech(firstAttempt[0].DispatchToken, delivered: false);
        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(2), out _), "failed predecessor remains retained and blocks ASK");
        var retry = coordinator.Observe(
            [predecessor, ask],
            1,
            now.AddTicks(2),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, retry.Count, "failed predecessor is retried exactly");
        coordinator.AcknowledgePollingSpeech(retry[0].DispatchToken, delivered: true);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(2), out var interrupt), "ASK releases after retry succeeds");
        Equal(false, interrupt, "ASK queues behind successful predecessor retry");
    }

    private static void FailedLaterBatchItemRetriesWithoutInterruptingEarlierSpeech()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot first = new(0, 2, "First batch line.", 0x700040);
        FieldVisibleWindowSnapshot second = new(1, 2, "Second batch line.", 0x700080);
        coordinator.Observe([first, second], 1, now);
        var batch = coordinator.Observe(
            [first, second],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        Equal(2, batch.Count, "two-line polling batch is prepared");
        coordinator.AcknowledgePollingSpeech(batch[0].DispatchToken, delivered: true);
        coordinator.AcknowledgePollingSpeech(batch[1].DispatchToken, delivered: false);

        var retry = coordinator.Observe(
            [first, second],
            1,
            now.AddTicks(2),
            requireDeliveryAcknowledgement: true);
        Equal(1, retry.Count, "only failed later line retries");
        Equal("Second batch line.", retry[0].Text, "retry keeps exact failed later text");
        Equal(false, retry[0].Interrupt, "later retry cannot cut off accepted first line");
    }

    private static void OpenHigherWindowSpeechMakesLowerAskNonInterrupting()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot higher = new(1, 2, "Already speaking higher window.", 0x700080);
        coordinator.Observe([higher], 1, now);
        coordinator.Observe([higher], 1, now.AddTicks(1));
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38, 5);
        coordinator.BeginNativeAskLifecycle(askIdentity);

        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(2), out var interrupt), "open speech does not block lower ASK readiness");
        Equal(false, interrupt, "lower ASK queues behind any already-open speech batch");
    }

    private static void UnavailableObservationWaitIsBounded()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38, 7);
        coordinator.BeginNativeAskLifecycle(
            askIdentity,
            requireCoherentObservation: true,
            now,
            maximumObservationWait: TimeSpan.FromMilliseconds(100));
        coordinator.ObserveUnavailable();

        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(99), out _), "native ASK briefly waits for coherent ordering scan");
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(100), out var interrupt), "unavailable polling reader cannot starve exact native ASK forever");
        Equal(false, interrupt, "bounded unavailable fallback queues conservatively");
    }

    private static void UnavailableCapturedPredecessorWaitIsBounded()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(450));
        FieldVisibleWindowSnapshot predecessor = new(0, 2, "Still typing.", 0x700040);
        coordinator.Observe([predecessor], 1, now);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 70);
        coordinator.BeginNativeAskLifecycle(
            askIdentity,
            requireCoherentObservation: true,
            now,
            maximumObservationWait: TimeSpan.FromMilliseconds(100));
        coordinator.ObserveUnavailable(now);

        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(99), out _), "captured pending predecessor remains ordered during the bounded outage window");
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(100), out var interrupt), "stale captured lifecycle cannot starve exact ASK after outage deadline");
        Equal(false, interrupt, "timed-out captured predecessor forces conservative noninterrupt speech");
    }

    private static void ObservedThenUnavailablePredecessorWaitIsBounded()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(450));
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 71);
        coordinator.BeginNativeAskLifecycle(
            askIdentity,
            requireCoherentObservation: true,
            now,
            maximumObservationWait: TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot predecessor = new(0, 2, "Observed but unfinished.", 0x700040);
        coordinator.Observe(
            [predecessor],
            1,
            now.AddMilliseconds(10),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        coordinator.ObserveUnavailable(now.AddMilliseconds(20));

        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(119), out _), "recent coherent predecessor remains ordered during reader outage");
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(120), out var interrupt), "observed predecessor cannot remain stale forever after reader loss");
        Equal(false, interrupt, "observed-then-unavailable timeout remains noninterrupting");
    }

    private static void NativeRetryRechecksNewPredecessors()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 72);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now, out var firstInterrupt), "first native attempt is initially ready");
        Equal(true, firstInterrupt, "first native attempt may interrupt when nothing precedes it");

        FieldVisibleWindowSnapshot predecessor = new(0, 2, "New lower window.", 0x700040);
        coordinator.Observe(
            [predecessor],
            1,
            now.AddTicks(1),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(1), out _), "failed native attempt keeps barrier live for a newly published predecessor");
        var polling = coordinator.Observe(
            [predecessor],
            1,
            now.AddTicks(2),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, polling.Count, "new predecessor becomes the next polling utterance");
        coordinator.AcknowledgePollingSpeech(polling[0].DispatchToken, delivered: true);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(3), out var retryInterrupt), "native retry releases only after new predecessor succeeds");
        Equal(false, retryInterrupt, "native retry queues behind predecessor discovered during backoff");
    }

    private static void PartialNativePromptRecoveryDoesNotInterruptPrompt()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 73);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.AcknowledgeNativeSpeech(askIdentity, visibleContentComplete: false);
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);

        coordinator.Observe(
            [ask],
            1,
            now,
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        var recovery = coordinator.Observe(
            [ask],
            1,
            now.AddTicks(1),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, recovery.Count, "full ASK text recovers after partial native prompt");
        Equal(false, recovery[0].Interrupt, "full ASK recovery cannot cut off its already-spoken native prompt");
    }

    private static void PartialNativePromptKeepsLaterSiblingBlocked()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 74);
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        FieldVisibleWindowSnapshot later = new(2, 2, "Later sibling.", 0x7000c0);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.Observe(
            [ask, later],
            1,
            now,
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true);
        coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(1),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true);
        coordinator.AcknowledgeNativeSpeech(askIdentity, visibleContentComplete: false);

        var waiting = coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(2),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(0, waiting.Count, "later sibling remains behind incomplete ASK while polling text restabilizes");
        var recovered = coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(3),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, recovered.Count, "polling recovers ASK while exact selected-row completion still holds later sibling");
        Equal(1, recovered[0].WindowId, "incomplete ASK recovers before later sibling");
        Equal(false, recovered[0].Interrupt, "ASK recovery continues native prompt");
        coordinator.AcknowledgePollingSpeech(recovered[0].DispatchToken, delivered: true);
        coordinator.AcknowledgeNativeSpeech(askIdentity, visibleContentComplete: true);
        var afterChoice = coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(4),
            nativeOwnershipIdentity: askIdentity,
            requireDeliveryAcknowledgement: true);
        Equal(1, afterChoice.Count, "later sibling releases only after exact native choice completes ASK");
        Equal(2, afterChoice[0].WindowId, "later sibling stays after completed ASK");
        Equal(false, afterChoice[0].Interrupt, "later sibling continues completed ASK batch");
    }

    private static void ActiveZeroRetainsLaterSiblingForUnboundPartialPrompt()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot later = new(2, 2, "Retained later sibling.", 0x7000c0);
        coordinator.Observe([later], 1, now);
        var prepared = coordinator.Observe(
            [later],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        coordinator.AcknowledgePollingSpeech(prepared[0].DispatchToken, delivered: false);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 75);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.AcknowledgeNativeSpeech(askIdentity, visibleContentComplete: false);

        var retained = coordinator.Observe(
            [],
            0,
            now.AddTicks(2),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(0, retained.Count, "count-zero cannot flush later sibling while unbound native prompt is incomplete");
        coordinator.AcknowledgeNativeSpeech(askIdentity, visibleContentComplete: true);
        var released = coordinator.Observe(
            [],
            0,
            now.AddTicks(3),
            nativeOwnershipIdentity: askIdentity,
            requireDeliveryAcknowledgement: true);
        Equal(1, released.Count, "later sibling releases after exact ASK completion");
        Equal(false, released[0].Interrupt, "later sibling queues behind successfully spoken native prompt");
    }

    private static void PromptWithSettlingChoiceKeepsInterveningWindowNonInterrupting()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var ownership = new NativeFieldMessageOwnershipTracker(TimeSpan.FromSeconds(5));
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 76);
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        ownership.ObserveNative(askIdentity, "Question.", now);
        coordinator.Observe(
            [ask],
            1,
            now,
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now),
            askIdentity,
            nativeOwnershipSpeechPending: true);

        // Production has spoken the prompt, but a newer native choice remains
        // queued inside its settle window. Ownership becomes Partial while the
        // queue separately keeps the exact lifecycle pending.
        ownership.ObserveNative(askIdentity, "Yes", now.AddTicks(1));
        coordinator.AcknowledgeNativeSpeech(
            askIdentity,
            visibleContentComplete: false,
            consumeOrderingBarrier: false);
        ownership.MarkSpeechDelivered(
            askIdentity,
            now.AddTicks(1),
            visibleContentComplete: false);
        FieldVisibleWindowSnapshot lower = new(0, 2, "Intervening lower row.", 0x700040);
        coordinator.Observe(
            [lower, ask],
            1,
            now.AddTicks(1),
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now.AddTicks(1)),
            askIdentity,
            nativeOwnershipSpeechPending: true);
        var speech = coordinator.Observe(
            [lower, ask],
            1,
            now.AddTicks(2),
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now.AddTicks(2)),
            askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(2, speech.Count, "intervening row and recovered ASK form one open batch");
        Equal(0, speech[0].WindowId, "native lower row retains sighted order");
        Equal(false, speech[0].Interrupt, "intervening row cannot cut off successfully spoken prompt");
    }

    private static void PendingAskRebindsHeldSiblingAcrossCountZero()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot later = new(2, 2, "Held later row.", 0x7000c0);
        coordinator.Observe([later], 1, now);
        var initial = coordinator.Observe(
            [later],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        coordinator.AcknowledgePollingSpeech(initial[0].DispatchToken, delivered: false);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 77);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        Equal(0, coordinator.Observe(
            [],
            0,
            now.AddTicks(2),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true).Count,
            "pending ASK retains later row through zero-count opening frame");
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        var reopened = coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(3),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(0, reopened.Count, "same-pointer retained later row cannot escape before pending ASK after count zero");
    }

    private static void PendingAskKeepsClosedHeldSiblingAcrossCountZero()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot later = new(2, 2, "Closed held later row.", 0x7000c0);
        coordinator.Observe([later], 1, now);
        var initial = coordinator.Observe(
            [later],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        coordinator.AcknowledgePollingSpeech(initial[0].DispatchToken, delivered: false);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 79);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.Observe(
            [],
            0,
            now.AddTicks(2),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        var closedLater = coordinator.Observe(
            [ask],
            1,
            now.AddTicks(3),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(0, closedLater.Count, "closed retained later row remains behind exact pending ASK boundary");
    }

    private static void PendingAskKeepsClosedHeldSiblingWithoutCountZero()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot later = new(2, 2, "Closed before first ASK scan.", 0x7000c0);
        coordinator.Observe([later], 1, now);
        var initial = coordinator.Observe(
            [later],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        coordinator.AcknowledgePollingSpeech(initial[0].DispatchToken, delivered: false);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 82);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        var firstAskScan = coordinator.Observe(
            [ask],
            1,
            now.AddTicks(2),
            window => window.WindowId == 1,
            askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(0, firstAskScan.Count, "closed retained later row cannot escape before ASK without a count-zero frame");
    }

    private static void PollingFallbackChoiceWaitsForQuestionRecovery()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var ownership = new NativeFieldMessageOwnershipTracker(TimeSpan.FromSeconds(5));
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 78);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.RequirePollingRecoveryBeforeNativeChoice(
            askIdentity,
            pollingAvailable: true,
            now,
            maximumWait: TimeSpan.FromSeconds(1));
        ownership.ObserveNative(askIdentity, "Yes", now);
        ownership.MarkSpeechDelivered(askIdentity, now, visibleContentComplete: false);
        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now, out _), "fallback cursor cannot speak before polling question is observed");

        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        coordinator.Observe(
            [ask],
            1,
            now,
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        Equal(false, coordinator.CanDispatchNativeSpeech(askIdentity, now, out _), "fallback cursor waits while polling question stabilizes");
        var prompt = coordinator.Observe(
            [ask],
            1,
            now.AddTicks(1),
            window => FieldWindowPollingOwnership.IsSuppressed(window, askIdentity, ownership, 1, now.AddTicks(1)),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, prompt.Count, "polling recovers the question before native cursor");
        coordinator.AcknowledgePollingSpeech(prompt[0].DispatchToken, delivered: true);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(2), out var interrupt), "fallback cursor releases after question delivery");
        Equal(false, interrupt, "fallback cursor queues behind recovered question");
    }

    private static void PollingFallbackChoiceWaitIsBoundedWithoutReader()
    {
        var now = Utc(0);
        var disabledCoordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var disabledAsk = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 80);
        disabledCoordinator.BeginNativeAskLifecycle(disabledAsk);
        disabledCoordinator.RequirePollingRecoveryBeforeNativeChoice(
            disabledAsk,
            pollingAvailable: false,
            now,
            maximumWait: TimeSpan.FromSeconds(1));
        Equal(true, disabledCoordinator.CanDispatchNativeSpeech(disabledAsk, now, out var disabledInterrupt), "disabled polling reader cannot deadlock exact cursor speech");
        Equal(false, disabledInterrupt, "cursor speech without polling recovery remains conservative");

        var outageCoordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var outageAsk = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 81);
        outageCoordinator.BeginNativeAskLifecycle(outageAsk);
        outageCoordinator.RequirePollingRecoveryBeforeNativeChoice(
            outageAsk,
            pollingAvailable: true,
            now,
            maximumWait: TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot unstablePrompt = new(1, 2, "Unstable polling question.", 0x700080);
        outageCoordinator.Observe(
            [unstablePrompt],
            1,
            now,
            nativeOwnershipIdentity: outageAsk,
            nativeOwnershipSpeechPending: true);
        Equal(false, outageCoordinator.CanDispatchNativeSpeech(outageAsk, now.AddMilliseconds(99), out _), "configured polling recovery gets a bounded chance to speak question first");
        Equal(true, outageCoordinator.CanDispatchNativeSpeech(outageAsk, now.AddMilliseconds(100), out var outageInterrupt), "unreadable polling cannot permanently silence exact cursor speech");
        Equal(false, outageInterrupt, "timed-out recovery cursor remains noninterrupting");
    }

    private static void CompletedFallbackChoiceDoesNotReblockLaterRetry()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 83);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.RequirePollingRecoveryBeforeNativeChoice(
            askIdentity,
            pollingAvailable: true,
            now,
            maximumWait: TimeSpan.FromSeconds(1));
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question. Yes. No.", 0x700080);
        coordinator.Observe(
            [ask],
            1,
            now,
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        var prompt = coordinator.Observe(
            [ask],
            1,
            now.AddTicks(1),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        coordinator.AcknowledgePollingSpeech(prompt[0].DispatchToken, delivered: true);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(2), out _), "fallback choice becomes ready after polling prompt");
        coordinator.AcknowledgeNativeSpeech(
            askIdentity,
            visibleContentComplete: false,
            consumeOrderingBarrier: true);

        FieldVisibleWindowSnapshot later = new(2, 2, "Later retry.", 0x7000c0);
        coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(3),
            nativeOwnershipIdentity: askIdentity);
        var firstLater = coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(4),
            nativeOwnershipIdentity: askIdentity,
            requireDeliveryAcknowledgement: true);
        Equal(1, firstLater.Count, "later row dispatches after recovered prompt and fallback choice");
        coordinator.AcknowledgePollingSpeech(firstLater[0].DispatchToken, delivered: false);
        var retry = coordinator.Observe(
            [ask, later],
            1,
            now.AddTicks(5),
            nativeOwnershipIdentity: askIdentity,
            requireDeliveryAcknowledgement: true);
        Equal(1, retry.Count, "failed later row retries without a recreated native boundary");
        Equal(2, retry[0].WindowId, "exact later row remains retryable");
    }

    private static void TimedOutFallbackChoiceRetainsLaterSiblingUntilQuestionAck()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot later = new(2, 2, "Later held through fallback timeout.", 0x7000c0);
        coordinator.Observe([later], 1, now);
        var laterAttempt = coordinator.Observe(
            [later],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        coordinator.AcknowledgePollingSpeech(laterAttempt[0].DispatchToken, delivered: false);

        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 84);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        coordinator.RequirePollingRecoveryBeforeNativeChoice(
            askIdentity,
            pollingAvailable: true,
            now,
            maximumWait: TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot ask = new(1, 2, "Question delayed by outage. Yes. No.", 0x700080);
        coordinator.Observe(
            [ask],
            1,
            now,
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddMilliseconds(100), out var choiceInterrupt), "bounded timeout eventually permits exact selected row");
        Equal(false, choiceInterrupt, "timed-out selected row remains noninterrupting");
        coordinator.AcknowledgeNativeSpeech(
            askIdentity,
            visibleContentComplete: false,
            consumeOrderingBarrier: false);
        var duringOutage = coordinator.Observe(
            [],
            0,
            now.AddMilliseconds(101),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(0, duringOutage.Count, "later sibling stays blocked after choice when question was only timed out");

        coordinator.Observe(
            [ask],
            1,
            now.AddMilliseconds(102),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true);
        var recoveredQuestion = coordinator.Observe(
            [ask],
            1,
            now.AddMilliseconds(103),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, recoveredQuestion.Count, "question becomes recoverable after polling returns");
        var recoveredIdentity = coordinator.AcknowledgePollingSpeech(
            recoveredQuestion[0].DispatchToken,
            delivered: true);
        Equal(askIdentity, recoveredIdentity, "polling ACK carries the exact fallback lifecycle identity");
        coordinator.AcknowledgeNativeSpeech(
            askIdentity,
            visibleContentComplete: true,
            consumeOrderingBarrier: true);
        var releasedLater = coordinator.Observe(
            [],
            0,
            now.AddMilliseconds(104),
            nativeOwnershipIdentity: askIdentity,
            requireDeliveryAcknowledgement: true);
        Equal(1, releasedLater.Count, "later sibling releases only after question ACK completes fallback ordering");
        Equal(2, releasedLater[0].WindowId, "held later sibling remains exact");
        Equal(false, releasedLater[0].Interrupt, "later sibling queues behind choice and recovered question");
    }

    private static void ActiveZeroFlushesOnlyAskPredecessors()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot first = new(0, 2, "Predecessor zero.", 0x700040);
        FieldVisibleWindowSnapshot later = new(2, 2, "Later window two.", 0x7000c0);
        coordinator.Observe([first, later], 1, now);
        var prepared = coordinator.Observe(
            [first, later],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        foreach (var item in prepared)
        {
            coordinator.AcknowledgePollingSpeech(item.DispatchToken, delivered: false);
        }

        var askIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 1, 38, 6);
        coordinator.BeginNativeAskLifecycle(askIdentity);
        var beforeAsk = coordinator.Observe(
            [],
            0,
            now.AddTicks(2),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: true,
            requireDeliveryAcknowledgement: true);
        Equal(1, beforeAsk.Count, "count-zero opening flushes only ASK predecessor");
        Equal(0, beforeAsk[0].WindowId, "window zero remains before window-one ASK");
        coordinator.AcknowledgePollingSpeech(beforeAsk[0].DispatchToken, delivered: true);
        Equal(true, coordinator.CanDispatchNativeSpeech(askIdentity, now.AddTicks(2), out var interrupt), "ASK releases after count-zero predecessor delivery");
        Equal(false, interrupt, "ASK queues behind flushed predecessor");

        coordinator.AcknowledgeNativeSpeech(askIdentity);
        var afterAsk = coordinator.Observe(
            [],
            0,
            now.AddTicks(3),
            nativeOwnershipIdentity: askIdentity,
            nativeOwnershipSpeechPending: false,
            requireDeliveryAcknowledgement: true);
        Equal(1, afterAsk.Count, "later window remains retained until after ASK");
        Equal(2, afterAsk[0].WindowId, "window two remains after window-one ASK");
        Equal(false, afterAsk[0].Interrupt, "later retained window queues behind ASK");
    }

    private static void MergesRetainedWindowsBackIntoCurrentNativeOrder()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot first = new(0, 2, "Barret: First.", 0x700040);
        FieldVisibleWindowSnapshot second = new(1, 2, "Tifa: Second.", 0x700080);
        FieldVisibleWindowSnapshot third = new(2, 2, "Cloud: Third.", 0x7000c0);

        coordinator.Observe([third], 1, now);
        coordinator.Observe([first, third], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([first, third], 1, now.AddMilliseconds(100)).Count, "third window is retained behind the first blocker");
        first = first with { Text = "Barret: First, changing once." };
        coordinator.Observe([first, second, third], 1, now.AddMilliseconds(120));
        first = first with { Text = "Barret: First, changing twice." };
        coordinator.Observe([first, second, third], 1, now.AddMilliseconds(180));
        Equal(0, coordinator.Observe([first, second, third], 1, now.AddMilliseconds(220)).Count, "second and third windows remain retained while first changes");

        first = first with { Text = "Barret: First, now stable." };
        coordinator.Observe([first, second, third], 1, now.AddMilliseconds(230));
        var speech = coordinator.Observe([first, second, third], 1, now.AddMilliseconds(330));
        Equal(3, speech.Count, "current and retained windows merge into one complete batch");
        Equal(0, speech[0].WindowId, "merged batch starts with native window zero");
        Equal(1, speech[1].WindowId, "retained insertion chronology cannot move native window two ahead of window one");
        Equal(2, speech[2].WindowId, "merged batch ends with native window two");
        Equal(true, speech[0].Interrupt, "merged batch interrupts only on its first native window");
        Equal(false, speech[1].Interrupt, "merged second window queues");
        Equal(false, speech[2].Interrupt, "merged third window queues");

        var closingCoordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        first = new FieldVisibleWindowSnapshot(0, 2, "Barret: Closing blocker.", 0x700040);
        second = new FieldVisibleWindowSnapshot(1, 2, "Tifa: Closing second.", 0x700080);
        third = new FieldVisibleWindowSnapshot(2, 2, "Cloud: Closing third.", 0x7000c0);
        closingCoordinator.Observe([third], 1, now);
        closingCoordinator.Observe([first, third], 1, now.AddMilliseconds(50));
        closingCoordinator.Observe([first, third], 1, now.AddMilliseconds(100));
        first = first with { Text = "Barret: Closing blocker changed once." };
        closingCoordinator.Observe([first, second, third], 1, now.AddMilliseconds(120));
        first = first with { Text = "Barret: Closing blocker changed twice." };
        closingCoordinator.Observe([first, second, third], 1, now.AddMilliseconds(180));
        Equal(0, closingCoordinator.Observe([first, second, third], 1, now.AddMilliseconds(220)).Count, "two later windows are retained before whole-set close");

        var closeSpeech = closingCoordinator.Observe([], 0, now.AddMilliseconds(225));
        Equal(2, closeSpeech.Count, "whole-set close flushes both stable retained windows");
        Equal(1, closeSpeech[0].WindowId, "whole-set close merges retained window one before window two");
        Equal(2, closeSpeech[1].WindowId, "whole-set close preserves native window two last");
        Equal(true, closeSpeech[0].Interrupt, "whole-set close flush interrupts only once");
        Equal(false, closeSpeech[1].Interrupt, "whole-set close queues its later retained window");
    }

    private static void PreservesRetainedChronologyWithinOneNativeWindow()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot blocker = new(0, 2, "Barret: Blocking.", 0x700040);
        FieldVisibleWindowSnapshot later = new(1, 2, "Cloud: Old stable line.", 0x700080);

        coordinator.Observe([later], 1, now);
        coordinator.Observe([blocker, later], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([blocker, later], 1, now.AddMilliseconds(100)).Count, "old stable line is retained behind blocker");

        blocker = blocker with { Text = "Barret: Still blocking." };
        later = later with { Text = "Cloud: New stable line." };
        coordinator.Observe([blocker, later], 1, now.AddMilliseconds(110));
        var speech = coordinator.Observe([later], 1, now.AddMilliseconds(210));
        Equal(2, speech.Count, "old retained and new current lines both remain available");
        Equal("Cloud: Old stable line.", speech[0].Text, "same-window retained chronology dispatches old stable text first");
        Equal("Cloud: New stable line.", speech[1].Text, "same-window retained chronology dispatches new stable text second");
        Equal(true, speech[0].Interrupt, "same-window chronological batch interrupts only once");
        Equal(false, speech[1].Interrupt, "same-window newer text queues behind older retained text");
    }

    private static void ReopenedSamePointerWindowGetsANewLifecycleToken()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.FromMilliseconds(100));
        FieldVisibleWindowSnapshot blocker = new(0, 2, "Barret: Blocking.", 0x700040);
        FieldVisibleWindowSnapshot repeated = new(1, 2, "Cloud: Identical reopened text.", 0x700080);

        coordinator.Observe([repeated], 1, now);
        coordinator.Observe([blocker, repeated], 1, now.AddMilliseconds(50));
        Equal(0, coordinator.Observe([blocker, repeated], 1, now.AddMilliseconds(100)).Count, "old lifecycle is retained behind blocker");

        coordinator.Observe([blocker], 1, now.AddMilliseconds(110));
        coordinator.Observe([blocker, repeated], 1, now.AddMilliseconds(120));
        var speech = coordinator.Observe([repeated], 1, now.AddMilliseconds(220));

        Equal(2, speech.Count, "closed and reopened same-pointer lifecycles both remain observable");
        Equal("Cloud: Identical reopened text.", speech[0].Text, "old retained lifecycle remains first");
        Equal("Cloud: Identical reopened text.", speech[1].Text, "reopened lifecycle receives a distinct token");
        Equal(true, speech[0].Interrupt, "reopened lifecycle batch interrupts once");
        Equal(false, speech[1].Interrupt, "reopened identical text queues after retained text");
    }

    private static void PreservesOrderAfterNativeOwnedWindowIsSuppressed()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        var nativeIdentity = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        FieldVisibleWindowSnapshot[] windows =
        [
            new(0, 2, "Already spoken by the native hook.", 0x700040),
            new(1, 2, "Still needs polling speech.", 0x700080),
            new(3, 2, "And this remains last.", 0x7000c0)
        ];

        Equal(0, coordinator.Observe(windows, 1, now, window => window.WindowId == 0, nativeIdentity).Count, "zero-settle text still requires a confirming observation");
        coordinator.AcknowledgeNativeSpeech(nativeIdentity);
        var speech = coordinator.Observe(
            windows,
            1,
            now.AddTicks(1),
            window => window.WindowId == 0,
            nativeIdentity,
            nativeOwnershipDelivered: true);

        Equal(2, speech.Count, "native-owned text is excluded without dropping unrelated windows");
        Equal(1, speech[0].WindowId, "first unsuppressed window keeps native order");
        Equal(false, speech[0].Interrupt, "first unsuppressed dispatch queues behind acknowledged native speech");
        Equal(3, speech[1].WindowId, "last unsuppressed window keeps native order");
        Equal(false, speech[1].Interrupt, "last unsuppressed dispatch queues behind the first");
    }

    private static void ResetsDedupOnlyAfterTheNativeWindowLifecycleCloses()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot[] windows = [new(1, 2, "Jessie: This way.", 0x700040)];

        coordinator.Observe(windows, 1, now);
        Equal(1, coordinator.Observe(windows, 1, now.AddTicks(1)).Count, "first native lifecycle speaks once");
        Equal(0, coordinator.Observe(windows, 1, now.AddTicks(2)).Count, "visible lifecycle does not duplicate speech");

        Equal(0, coordinator.Observe([], 0, now.AddTicks(3)).Count, "closed native lifecycle is silent");
        coordinator.Observe(windows, 1, now.AddTicks(4));
        Equal(1, coordinator.Observe(windows, 1, now.AddTicks(5)).Count, "same exact text may speak after native close and reopen");
    }

    private static void TypewriterGrowthAfterAVisiblePauseSpeaksOnlyTheNewSuffix()
    {
        var now = Utc(0);
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        const uint windowPointer = 0x700040;
        const string prefix = "Cloud I know... no one lives in the slums";
        const string complete = "Cloud I know... no one lives in the slums because they want to.";

        coordinator.Observe([new(0, 2, prefix, windowPointer)], 1, now);
        var first = coordinator.Observe(
            [new(0, 2, prefix, windowPointer)],
            1,
            now.AddTicks(1),
            requireDeliveryAcknowledgement: true);
        Equal(1, first.Count, "visible x86 typewriter prefix is delivered after its pause");
        Equal(prefix, first[0].Text, "visible x86 prefix remains exact");
        coordinator.AcknowledgePollingSpeech(first[0].DispatchToken, delivered: true);

        coordinator.Observe([new(0, 2, complete, windowPointer)], 1, now.AddTicks(2));
        var continuation = coordinator.Observe(
            [new(0, 2, complete, windowPointer)],
            1,
            now.AddTicks(3),
            requireDeliveryAcknowledgement: true);

        Equal(1, continuation.Count, "continued x86 page produces one new utterance");
        Equal(
            "because they want to.",
            continuation[0].Text,
            "continued x86 page does not repeat the already delivered prefix");
    }

    private static void AskOwnershipDoesNotSuppressOverlappingTextCollisions()
    {
        var now = Utc(0);
        var ownership = new NativeFieldMessageOwnershipTracker(TimeSpan.FromSeconds(5));
        var ask = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 38);
        ownership.ObserveNative(ask, "Jessie: That light means we are nearing the surface.", now);

        FieldVisibleWindowSnapshot[] sameTextWindows =
        [
            new(0, 2, "Ready?", 0x700040),
            new(1, 2, "Ready?", 0x700080)
        ];
        var sameTextCoordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        sameTextCoordinator.Observe(
            sameTextWindows,
            1,
            now,
            window => FieldWindowPollingOwnership.IsSuppressed(window, ask, ownership, 1, now),
            ask);
        sameTextCoordinator.AcknowledgeNativeSpeech(ask);
        var sameTextSpeech = sameTextCoordinator.Observe(
            sameTextWindows,
            1,
            now.AddTicks(1),
            window => FieldWindowPollingOwnership.IsSuppressed(window, ask, ownership, 1, now.AddTicks(1)),
            ask,
            nativeOwnershipDelivered: true);
        Equal(1, sameTextSpeech.Count, "ASK ownership suppresses only its exact native window when text is identical");
        Equal(1, sameTextSpeech[0].WindowId, "unrelated identical-text overlap remains audible");
        Equal("Ready?", sameTextSpeech[0].Text, "unrelated identical text remains exact");

        FieldVisibleWindowSnapshot[] substringWindows =
        [
            new(0, 2, "That light means we are nearing the surface.", 0x700040),
            new(2, 2, "light means we are nearing", 0x7000c0)
        ];
        var substringCoordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        substringCoordinator.Observe(
            substringWindows,
            1,
            now.AddTicks(2),
            window => FieldWindowPollingOwnership.IsSuppressed(window, ask, ownership, 1, now.AddTicks(2)),
            ask);
        substringCoordinator.AcknowledgeNativeSpeech(ask);
        var substringSpeech = substringCoordinator.Observe(
            substringWindows,
            1,
            now.AddTicks(3),
            window => FieldWindowPollingOwnership.IsSuppressed(window, ask, ownership, 1, now.AddTicks(3)),
            ask,
            nativeOwnershipDelivered: true);
        Equal(1, substringSpeech.Count, "native text substring cannot claim another window");
        Equal(2, substringSpeech[0].WindowId, "unrelated substring overlap remains audible");
        Equal("light means we are nearing", substringSpeech[0].Text, "unrelated substring remains exact");
    }

    private static void UnqueuedAskIdentityDoesNotClaimPollingWindow()
    {
        var now = Utc(0);
        var ownership = new NativeFieldMessageOwnershipTracker(TimeSpan.FromSeconds(5));
        var resultZeroAsk = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 41);
        FieldVisibleWindowSnapshot[] windows =
        [
            new(0, 2, "A prompt that native speech did not queue.", 0x700040),
            new(1, 2, "An overlapping message.", 0x700080)
        ];
        var coordinator = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);

        coordinator.Observe(
            windows,
            1,
            now,
            window => FieldWindowPollingOwnership.IsSuppressed(window, resultZeroAsk, ownership, 1, now));
        var speech = coordinator.Observe(
            windows,
            1,
            now.AddTicks(1),
            window => FieldWindowPollingOwnership.IsSuppressed(window, resultZeroAsk, ownership, 1, now.AddTicks(1)));

        Equal(2, speech.Count, "result-zero or empty native ASK speech leaves polling available");
        Equal(0, speech[0].WindowId, "unqueued ASK window remains first in native polling order");
        Equal(1, speech[1].WindowId, "overlapping window remains available after unqueued ASK");
    }

    private static DateTime Utc(int seconds) =>
        new(2026, 7, 19, 12, 0, seconds, DateTimeKind.Utc);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
