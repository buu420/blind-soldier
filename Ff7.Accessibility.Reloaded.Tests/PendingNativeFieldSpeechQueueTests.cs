using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PendingNativeFieldSpeechQueueTests
{
    public static void Run()
    {
        PreservesPromptAndCoalescesChoiceUpdates();
        CancelsEveryEntryForTheExactIdentity();
        PreservesFifoOrderAcrossDifferentIdentities();
        RejectsOverflowWithoutDisplacingQueuedSpeech();
        CancelingAStaleHeadPreservesTheNewCurrentPrompt();
        RejectsAnExactQueuedDuplicate();
        RequeuePreservesPromptOrderAndNewestConcurrentChoice();
        RepeatedPollingFallbackRegistrationPreservesRecoveryProgress();
        PromptChoiceCompositionKeepsSuffixCollisionChoice();
        LifecycleCloseAndEmissionCommitLinearize();
        ResultZeroInvalidatesThePublishedAskSynchronously();
    }

    private static void PreservesPromptAndCoalescesChoiceUpdates()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 4);
        var identity = Ask(dialogId: 38);
        var now = Utc(0);

        Equal(true, queue.TryEnqueue(Entry(identity, "Prompt", "prompt", now, NativeFieldSpeechKind.Prompt)), "initial prompt queues");
        Equal(true, queue.TryEnqueue(Entry(identity, "Choice one", "choice-1", now.AddTicks(1), NativeFieldSpeechKind.ChoiceUpdate)), "first choice queues");
        Equal(true, queue.TryEnqueue(Entry(identity, "Choice two", "choice-2", now.AddTicks(2), NativeFieldSpeechKind.ChoiceUpdate)), "new choice coalesces");
        Equal(2, queue.Count, "coalescing never overwrites the unspoken prompt");

        Equal(true, queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var prompt), "prompt becomes ready first");
        Equal("Prompt", prompt.Candidate.Text, "initial prompt remains first");
        Equal(NativeFieldSpeechKind.Prompt, prompt.Kind, "first item remains a prompt");
        Equal(true, queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var choice), "coalesced choice becomes ready second");
        Equal("Choice two", choice.Candidate.Text, "only the newest choice update remains");
    }

    private static void CancelsEveryEntryForTheExactIdentity()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 4);
        var identity = Ask(dialogId: 38);
        var other = Ask(dialogId: 39);
        var now = Utc(0);
        queue.TryEnqueue(Entry(identity, "Prompt", "prompt", now, NativeFieldSpeechKind.Prompt));
        queue.TryEnqueue(Entry(identity, "Choice", "choice", now, NativeFieldSpeechKind.ChoiceUpdate));
        queue.TryEnqueue(Entry(other, "Other prompt", "other", now, NativeFieldSpeechKind.Prompt));

        var canceled = queue.Cancel(identity);

        Equal(1, canceled.Count, "exact cancellation returns ownership once");
        Equal(identity, canceled[0], "exact cancellation returns the canceled owner");
        Equal(1, queue.Count, "exact cancellation removes prompt and choice only");
        Equal(true, queue.TryTakeReady(now, TimeSpan.Zero, out var remaining), "unrelated prompt remains queued");
        Equal(other, remaining.OwnershipIdentity, "unrelated ownership remains exact");
    }

    private static void PreservesFifoOrderAcrossDifferentIdentities()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 4);
        var first = Ask(dialogId: 38);
        var second = Ask(dialogId: 39);
        var now = Utc(0);
        queue.TryEnqueue(Entry(first, "First prompt", "first", now, NativeFieldSpeechKind.Prompt));
        queue.TryEnqueue(Entry(second, "Second prompt", "second", now.AddTicks(1), NativeFieldSpeechKind.Prompt));

        queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var firstResult);
        queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var secondResult);

        Equal(first, firstResult.OwnershipIdentity, "first identity is never overwritten");
        Equal(second, secondResult.OwnershipIdentity, "second identity follows FIFO order");
    }

    private static void RejectsOverflowWithoutDisplacingQueuedSpeech()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 1);
        var now = Utc(0);
        var first = Entry(Ask(38), "First prompt", "first", now, NativeFieldSpeechKind.Prompt);
        var overflow = Entry(Ask(39), "Overflow prompt", "overflow", now, NativeFieldSpeechKind.Prompt);

        Equal(true, queue.TryEnqueue(first), "bounded queue accepts capacity");
        Equal(false, queue.TryEnqueue(overflow), "bounded queue rejects overflow");
        Equal(1, queue.Count, "overflow cannot displace accepted speech");
        queue.TryTakeReady(now, TimeSpan.Zero, out var remaining);
        Equal(first, remaining, "accepted speech remains unchanged after overflow");
    }

    private static void CancelingAStaleHeadPreservesTheNewCurrentPrompt()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 4);
        var stale = Ask(38);
        var current = Ask(39);
        var now = Utc(0);
        queue.TryEnqueue(Entry(stale, "Stale", "stale", now, NativeFieldSpeechKind.Prompt));
        queue.TryEnqueue(Entry(current, "Current", "current", now.AddTicks(1), NativeFieldSpeechKind.Prompt));

        queue.Cancel(stale);

        Equal(true, queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var result), "current prompt survives stale-head cancellation");
        Equal(current, result.OwnershipIdentity, "stale cancellation is identity scoped");
        Equal("Current", result.Candidate.Text, "new current prompt remains exact");
    }

    private static void RejectsAnExactQueuedDuplicate()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 2);
        var entry = Entry(Ask(38), "Prompt", "same-key", Utc(0), NativeFieldSpeechKind.Prompt);

        Equal(true, queue.TryEnqueue(entry), "first exact entry queues");
        Equal(false, queue.TryEnqueue(entry with { SeenAt = Utc(1) }), "exact queued key is rejected");
        Equal(1, queue.Count, "exact duplicate does not consume capacity");
    }

    private static void RequeuePreservesPromptOrderAndNewestConcurrentChoice()
    {
        var queue = new PendingNativeFieldSpeechQueue(capacity: 4);
        var identity = Ask(38);
        var other = Ask(39);
        var now = Utc(0);
        var prompt = Entry(identity, "Question", "prompt", now, NativeFieldSpeechKind.Prompt) with
        {
            AttemptCount = 1
        };
        var failedChoice = Entry(identity, "Old choice", "choice-old", now, NativeFieldSpeechKind.ChoiceUpdate) with
        {
            AttemptCount = 1,
            CompletesVisibleContent = true
        };

        queue.TryEnqueue(Entry(other, "Later prompt", "later", now, NativeFieldSpeechKind.Prompt));
        queue.TryEnqueue(Entry(identity, "Newest choice", "choice-new", now.AddTicks(1), NativeFieldSpeechKind.ChoiceUpdate) with
        {
            CompletesVisibleContent = true
        });

        Equal(true, queue.TryRequeueFront([prompt, failedChoice]), "failed prompt/choice sequence returns to the FIFO head");
        Equal(3, queue.Count, "concurrent newer choice replaces rather than duplicates failed highlight");
        queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var retryPrompt);
        queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var retryChoice);
        queue.TryTakeReady(now.AddSeconds(1), TimeSpan.Zero, out var laterPrompt);
        Equal("Question", retryPrompt.Candidate.Text, "retry keeps prompt first");
        Equal(1, retryPrompt.AttemptCount, "retry metadata remains attached to the exact prompt");
        Equal("Newest choice", retryChoice.Candidate.Text, "retry uses the newest concurrent cursor state");
        Equal(true, retryChoice.CompletesVisibleContent, "replacement choice retains content completeness metadata");
        Equal("Later prompt", laterPrompt.Candidate.Text, "unrelated later native speech stays behind the retry");
    }

    private static void RepeatedPollingFallbackRegistrationPreservesRecoveryProgress()
    {
        var state = new NativeAskPollingFallbackStateTracker();
        var questionFirst = Ask(38);
        Equal(true, state.Begin(questionFirst), "first exact fallback registration initializes recovery");
        Equal(false, state.MarkQuestionRecovered(questionFirst), "question alone keeps selected-row recovery pending");
        Equal(true, state.IsRecoveryPending(questionFirst), "question-only fallback remains ordered");
        Equal(true, state.MarkChoiceDelivered(questionFirst), "question plus exact choice completes fallback ordering");
        Equal(false, state.IsRecoveryPending(questionFirst), "combined fallback delivery consumes recovery boundary");
        Equal(false, state.Begin(questionFirst), "repeat registration for same exact token is idempotent");
        Equal(true, state.IsQuestionRecovered(questionFirst), "repeat registration cannot forget acknowledged question");
        Equal(false, state.IsRecoveryPending(questionFirst), "repeat registration cannot recreate consumed boundary");

        var choiceFirst = Ask(39);
        Equal(true, state.Begin(choiceFirst), "second exact lifecycle initializes independently");
        Equal(false, state.MarkChoiceDelivered(choiceFirst), "timed-out choice alone retains question recovery boundary");
        Equal(false, state.Begin(choiceFirst), "repeat timeout registration is also idempotent");
        Equal(true, state.IsRecoveryPending(choiceFirst), "timeout boundary survives repeat registration");
        Equal(true, state.MarkQuestionRecovered(choiceFirst), "later polling question completes choice-first fallback");
        Equal(false, state.IsRecoveryPending(choiceFirst), "later question ACK consumes exact timeout boundary");
    }

    private static void PromptChoiceCompositionKeepsSuffixCollisionChoice()
    {
        var result = NativeFieldSpeechBatchComposer.MergePromptAndChoice(
            new FieldMessageCandidate("prompt", "Cloud: Say No"),
            new FieldMessageCandidate("choice", "No"));

        Equal("Cloud: Say No. No", result.Text, "prompt-only suffix collision cannot hide highlighted native choice");
    }

    private static void LifecycleCloseAndEmissionCommitLinearize()
    {
        var canceledFirst = Ask(38);
        canceledFirst.SpeechLifecycle.Close();
        Equal(false, canceledFirst.SpeechLifecycle.TryCommitEmission(), "close linearized first prevents stale emission");

        var emittedFirst = Ask(39);
        Equal(true, emittedFirst.SpeechLifecycle.TryCommitEmission(), "open lifecycle commits emission");
        emittedFirst.SpeechLifecycle.Close();
        Equal(false, emittedFirst.SpeechLifecycle.TryCommitEmission(), "no later emission commits after close");
    }

    private static void ResultZeroInvalidatesThePublishedAskSynchronously()
    {
        NativeFieldMessageIdentity? active = Ask(38);
        var published = active;
        Equal(true, NativeFieldAskCloseInvalidator.Invalidate(ref active, published!, result: 0), "result zero invalidates the exact published ASK");
        Equal<NativeFieldMessageIdentity?>(null, active, "result-zero invalidation is visible before deferred draining");

        active = Ask(39);
        Equal(false, NativeFieldAskCloseInvalidator.Invalidate(ref active, published!, result: 0), "an older close cannot erase a newer ASK");
        Equal(39, active!.DialogId, "newer ASK survives stale close");
        Equal(false, NativeFieldAskCloseInvalidator.Invalidate(ref active, active, result: 1), "nonzero result keeps the ASK active");
        Equal(39, active!.DialogId, "nonzero ASK remains published");

        var olderSameCoordinates = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 39, 1);
        var newerSameCoordinates = new NativeFieldMessageIdentity(FieldOpcodeKind.Ask, 117, 0, 39, 2);
        active = newerSameCoordinates;
        Equal(false, NativeFieldAskCloseInvalidator.Invalidate(ref active, olderSameCoordinates, result: 0), "older same-coordinate close cannot erase newer token");
        Equal(true, ReferenceEquals(newerSameCoordinates, active), "new same-coordinate lifecycle reference survives old deferred close");
        Equal(true, olderSameCoordinates.SpeechLifecycle.IsClosed, "old lifecycle emission state closes even when newer token is active");
        Equal(false, newerSameCoordinates.SpeechLifecycle.IsClosed, "newer lifecycle emission state remains open");
    }

    private static PendingNativeFieldSpeech Entry(
        NativeFieldMessageIdentity identity,
        string text,
        string key,
        DateTime seenAt,
        NativeFieldSpeechKind kind) =>
        new(new FieldMessageCandidate("test", text), identity, key, seenAt, kind);

    private static NativeFieldMessageIdentity Ask(int dialogId) =>
        new(FieldOpcodeKind.Ask, 117, 0, dialogId);

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
