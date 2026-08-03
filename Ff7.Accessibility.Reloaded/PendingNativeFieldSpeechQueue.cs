namespace Ff7.Accessibility.Reloaded;

internal enum NativeFieldSpeechKind
{
    Prompt,
    ChoiceUpdate
}

internal readonly record struct PendingNativeFieldSpeech(
    FieldMessageCandidate Candidate,
    NativeFieldMessageIdentity? OwnershipIdentity,
    string Key,
    DateTime SeenAt,
    NativeFieldSpeechKind Kind,
    int AttemptCount = 0,
    bool CompletesVisibleContent = false);

internal enum PendingNativeFieldSpeechEnqueueResult
{
    Invalid,
    Duplicate,
    Full,
    Enqueued,
    Coalesced
}

/// <summary>
/// Bounded FIFO for native field speech. Prompts retain native arrival order;
/// only a queued choice update for the same exact ASK may be replaced.
/// </summary>
internal sealed class PendingNativeFieldSpeechQueue
{
    private readonly int capacity;
    private readonly object sync = new();
    private readonly List<PendingNativeFieldSpeech> entries = [];

    public PendingNativeFieldSpeechQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return entries.Count;
            }
        }
    }

    public bool HasOwnedCandidate
    {
        get
        {
            lock (sync)
            {
                return entries.Any(entry => entry.OwnershipIdentity is not null);
            }
        }
    }

    public bool Contains(NativeFieldMessageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (sync)
        {
            return entries.Any(entry => entry.OwnershipIdentity == identity);
        }
    }

    public bool TryEnqueue(PendingNativeFieldSpeech entry)
    {
        var result = Enqueue(entry);
        return result is PendingNativeFieldSpeechEnqueueResult.Enqueued or
            PendingNativeFieldSpeechEnqueueResult.Coalesced;
    }

    public PendingNativeFieldSpeechEnqueueResult Enqueue(
        PendingNativeFieldSpeech entry)
    {
        if (entry.Candidate.Text.Length == 0 || string.IsNullOrEmpty(entry.Key))
        {
            return PendingNativeFieldSpeechEnqueueResult.Invalid;
        }

        if (entry.OwnershipIdentity is { IsValid: false })
        {
            return PendingNativeFieldSpeechEnqueueResult.Invalid;
        }

        lock (sync)
        {
            if (entries.Any(queued => string.Equals(queued.Key, entry.Key, StringComparison.Ordinal)))
            {
                return PendingNativeFieldSpeechEnqueueResult.Duplicate;
            }

            if (entry.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                entry.OwnershipIdentity is not null)
            {
                for (var index = entries.Count - 1; index >= 0; index--)
                {
                    var queued = entries[index];
                    if (queued.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                        queued.OwnershipIdentity == entry.OwnershipIdentity)
                    {
                        entries[index] = entry;
                        return PendingNativeFieldSpeechEnqueueResult.Coalesced;
                    }
                }
            }

            if (entries.Count >= capacity)
            {
                return PendingNativeFieldSpeechEnqueueResult.Full;
            }

            entries.Add(entry);
            return PendingNativeFieldSpeechEnqueueResult.Enqueued;
        }
    }

    public bool TryTakeReady(
        DateTime now,
        TimeSpan settleTime,
        out PendingNativeFieldSpeech entry)
    {
        settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
        lock (sync)
        {
            if (entries.Count == 0 || now - entries[0].SeenAt < settleTime)
            {
                entry = default;
                return false;
            }

            entry = entries[0];
            entries.RemoveAt(0);
            return true;
        }
    }

    public bool TryPeekReady(
        DateTime now,
        TimeSpan settleTime,
        out PendingNativeFieldSpeech entry)
    {
        settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
        lock (sync)
        {
            if (entries.Count == 0 || now - entries[0].SeenAt < settleTime)
            {
                entry = default;
                return false;
            }

            entry = entries[0];
            return true;
        }
    }

    public bool TryTakeReadyChoiceFor(
        NativeFieldMessageIdentity identity,
        DateTime now,
        TimeSpan settleTime,
        out PendingNativeFieldSpeech entry)
    {
        ArgumentNullException.ThrowIfNull(identity);
        settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
        lock (sync)
        {
            if (entries.Count == 0 ||
                entries[0].Kind != NativeFieldSpeechKind.ChoiceUpdate ||
                entries[0].OwnershipIdentity != identity ||
                now - entries[0].SeenAt < settleTime)
            {
                entry = default;
                return false;
            }

            entry = entries[0];
            entries.RemoveAt(0);
            return true;
        }
    }

    public IReadOnlyList<NativeFieldMessageIdentity> Cancel(
        NativeFieldMessageIdentity? identity)
    {
        lock (sync)
        {
            var canceled = entries
                .Where(entry =>
                    entry.OwnershipIdentity is not null &&
                    (identity is null || entry.OwnershipIdentity == identity))
                .Select(entry => entry.OwnershipIdentity!)
                .Distinct()
                .ToArray();

            entries.RemoveAll(entry =>
                identity is null || entry.OwnershipIdentity == identity);
            return canceled;
        }
    }

    public bool TryRequeueFront(
        IReadOnlyList<PendingNativeFieldSpeech> retryEntries)
    {
        ArgumentNullException.ThrowIfNull(retryEntries);
        if (retryEntries.Count == 0)
        {
            return true;
        }

        lock (sync)
        {
            var remaining = entries.ToList();
            var front = new List<PendingNativeFieldSpeech>(retryEntries.Count);
            var retryKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var retry in retryEntries)
            {
                if (retry.Candidate.Text.Length == 0 ||
                    string.IsNullOrEmpty(retry.Key) ||
                    retry.OwnershipIdentity is { IsValid: false })
                {
                    return false;
                }

                var effective = retry;
                if (retry.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                    retry.OwnershipIdentity is not null)
                {
                    // A cursor callback can publish a newer highlighted choice
                    // while the previous choice is in Prism. Preserve that newer
                    // exact-token entry and move it into the retried FIFO slot;
                    // never reinsert both the stale and current highlights.
                    var newerChoiceIndex = remaining.FindLastIndex(entry =>
                        entry.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                        entry.OwnershipIdentity == retry.OwnershipIdentity);
                    if (newerChoiceIndex >= 0)
                    {
                        effective = remaining[newerChoiceIndex];
                        remaining.RemoveAll(entry =>
                            entry.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                            entry.OwnershipIdentity == retry.OwnershipIdentity);
                    }
                }

                if (!retryKeys.Add(effective.Key) ||
                    remaining.Any(entry => string.Equals(entry.Key, effective.Key, StringComparison.Ordinal)))
                {
                    return false;
                }

                front.Add(effective);
            }

            if (remaining.Count + front.Count > capacity)
            {
                return false;
            }

            entries.Clear();
            entries.AddRange(front);
            entries.AddRange(remaining);
            return true;
        }
    }
}

internal static class NativeFieldSpeechBatchComposer
{
    public static FieldMessageCandidate MergePromptAndChoice(
        FieldMessageCandidate prompt,
        FieldMessageCandidate choice)
    {
        var promptText = Ff7EncodedTextDecoder.NormalizeWhitespace(prompt.Text);
        var choiceText = Ff7EncodedTextDecoder.NormalizeWhitespace(choice.Text);
        if (promptText.Length == 0)
        {
            return choice with { Text = choiceText };
        }

        if (choiceText.Length == 0)
        {
            return prompt with { Text = promptText };
        }

        var separator = promptText[^1] is '.' or '?' or '!' or ':'
            ? " "
            : ". ";
        return prompt with { Text = $"{promptText}{separator}{choiceText}" };
    }
}

internal static class NativeFieldSpeechIdentityValidator
{
    public static bool IsCurrent(
        NativeFieldMessageIdentity expected,
        NativeFieldMessageIdentity? before,
        NativeFieldMessageIdentity? after,
        byte module,
        int fieldId,
        int windowId,
        int dialogId) =>
        expected.IsValid &&
        expected.Kind == FieldOpcodeKind.Ask &&
        ReferenceEquals(before, expected) &&
        ReferenceEquals(after, expected) &&
        module == FieldPositionReader.FieldModule &&
        fieldId == expected.FieldId &&
        windowId == expected.WindowId &&
        dialogId == expected.DialogId;
}

internal static class NativeFieldAskCloseInvalidator
{
    public static bool Invalidate(
        ref NativeFieldMessageIdentity? activeIdentity,
        NativeFieldMessageIdentity publishedIdentity,
        int result)
    {
        ArgumentNullException.ThrowIfNull(publishedIdentity);
        if (result != 0)
        {
            return false;
        }

        publishedIdentity.SpeechLifecycle.Close();

        return ReferenceEquals(
            Interlocked.CompareExchange(ref activeIdentity, null, publishedIdentity),
            publishedIdentity);
    }
}

internal static class NativeFieldAskDeferredClosePolicy
{
    public static bool MayClearPublishedCoordinates(
        NativeFieldMessageIdentity? currentIdentity) =>
        currentIdentity is null;
}
