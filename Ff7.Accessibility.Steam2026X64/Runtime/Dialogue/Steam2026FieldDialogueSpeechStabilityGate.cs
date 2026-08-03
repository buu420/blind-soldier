using Ff7.Accessibility.Core;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Holds coherent visible-window observations until their exact sighted text
/// has stopped growing for the configured field-message stability interval.
/// Raw dialogue ownership remains available to the readers that need it.
/// </summary>
internal sealed class Steam2026FieldDialogueSpeechStabilityGate
{
    private readonly TimeSpan stableWindow;
    private readonly Queue<PendingDelivery> deliveryQueue = new();
    private PendingCandidate? activeCandidate;
    private DialoguePageObservation? lastDeliveredSourcePage;
    private long lastDeliveredSourceGeneration = -1;
    private long sourceGeneration;
    private bool sourceClosed;

    internal Steam2026FieldDialogueSpeechStabilityGate(TimeSpan stableWindow)
    {
        this.stableWindow = stableWindow < TimeSpan.Zero
            ? TimeSpan.Zero
            : stableWindow;
    }

    internal RuntimeDomainUpdate<DialoguePageObservation> Observe(
        RuntimeDomainUpdate<DialoguePageObservation> update,
        DateTime nowUtc)
    {
        if (update.Kind == RuntimeDomainUpdateKind.Unchanged)
        {
            return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
        }

        if (update.Kind == RuntimeDomainUpdateKind.Closed)
        {
            activeCandidate = null;
            DiscardSuppressedDeliveries();
            if (!sourceClosed)
            {
                sourceClosed = true;
                sourceGeneration = sourceGeneration == long.MaxValue
                    ? 1
                    : sourceGeneration + 1;
                deliveryQueue.Enqueue(PendingDelivery.Closed);
            }

            return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
        }

        if (update.Value is not { IsOpen: true } page ||
            string.IsNullOrWhiteSpace(page.VisibleText) && page.Choices.Length == 0)
        {
            return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
        }

        sourceClosed = false;
        if (page.Choices.Length > 0)
        {
            ObserveExactAskPage(page, nowUtc);
            return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
        }

        if (activeCandidate is null || !SamePage(activeCandidate.SourcePage, page))
        {
            activeCandidate = new PendingCandidate(page, nowUtc, sourceGeneration);
            return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
        }

        if (!activeCandidate.IsStable && nowUtc < activeCandidate.SinceUtc)
        {
            activeCandidate.SinceUtc = nowUtc;
            return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
        }

        if (!activeCandidate.IsStable && nowUtc - activeCandidate.SinceUtc >= stableWindow)
        {
            activeCandidate.IsStable = true;
            activeCandidate.Page = CreateDeliveryPage(activeCandidate);
            Enqueue(activeCandidate);
        }

        return NextDeliveryOr(RuntimeDomainUpdateKind.Unchanged);
    }

    internal bool MarkDeliverySuppressed(
        DialoguePageObservation page,
        bool suppressed)
    {
        ArgumentNullException.ThrowIfNull(page);
        foreach (var pending in deliveryQueue)
        {
            if (pending.Kind != RuntimeDomainUpdateKind.Present
                || pending.Candidate is null
                || !SamePage(pending.Candidate.Page, page))
            {
                continue;
            }

            pending.Candidate.IsSpeechSuppressed = suppressed;
            return true;
        }

        return false;
    }

    internal bool AcknowledgeDelivery(DialoguePageObservation deliveredPage)
    {
        ArgumentNullException.ThrowIfNull(deliveredPage);
        if (deliveryQueue.Count == 0)
        {
            return false;
        }

        var next = deliveryQueue.Peek();
        if (next.Kind != RuntimeDomainUpdateKind.Present ||
            next.Candidate is null ||
            !SamePage(next.Candidate.Page, deliveredPage))
        {
            return false;
        }

        _ = deliveryQueue.Dequeue();
        next.Candidate.IsQueued = false;
        lastDeliveredSourcePage = next.Candidate.SourcePage;
        lastDeliveredSourceGeneration = next.Candidate.SourceGeneration;
        return true;
    }

    internal bool AcknowledgeClose()
    {
        if (deliveryQueue.Count == 0 ||
            deliveryQueue.Peek().Kind != RuntimeDomainUpdateKind.Closed)
        {
            return false;
        }

        _ = deliveryQueue.Dequeue();
        return true;
    }

    internal string DescribeState()
    {
        var active = activeCandidate is null
            ? "none"
            : DescribeCandidate(activeCandidate);
        var queued = deliveryQueue.Count == 0
            ? "none"
            : string.Join(
                ',',
                deliveryQueue.Select(pending =>
                    pending.Kind == RuntimeDomainUpdateKind.Closed
                        ? "closed"
                        : DescribeCandidate(pending.Candidate!)));
        return
            $"sourceClosed={sourceClosed}, generation={sourceGeneration}, active={active}, queue={queued}";
    }

    private RuntimeDomainUpdate<DialoguePageObservation> NextDeliveryOr(
        RuntimeDomainUpdateKind fallbackKind)
    {
        if (deliveryQueue.Count > 0)
        {
            var next = deliveryQueue.Peek();
            return next.Kind == RuntimeDomainUpdateKind.Closed
                ? RuntimeDomainUpdate<DialoguePageObservation>.Closed
                : RuntimeDomainUpdate<DialoguePageObservation>.Present(
                    next.Candidate!.Page);
        }

        return fallbackKind == RuntimeDomainUpdateKind.Closed
            ? RuntimeDomainUpdate<DialoguePageObservation>.Closed
            : RuntimeDomainUpdate<DialoguePageObservation>.Unchanged;
    }

    private void ObserveExactAskPage(DialoguePageObservation page, DateTime nowUtc)
    {
        if (activeCandidate is null
            || activeCandidate.SourcePage.Choices.Length == 0
            || !SameAskContent(activeCandidate.SourcePage, page))
        {
            activeCandidate = new PendingCandidate(page, nowUtc, sourceGeneration)
            {
                IsStable = true
            };
            Enqueue(activeCandidate);
            return;
        }

        if (SamePage(activeCandidate.SourcePage, page))
        {
            return;
        }

        activeCandidate.SourcePage = page;
        activeCandidate.Page = page;
        activeCandidate.SinceUtc = nowUtc;
        activeCandidate.IsStable = true;
        Enqueue(activeCandidate);
    }

    private void Enqueue(PendingCandidate candidate)
    {
        if (candidate.IsQueued)
        {
            return;
        }

        candidate.IsQueued = true;
        deliveryQueue.Enqueue(PendingDelivery.ForPage(candidate));
    }

    private void DiscardSuppressedDeliveries()
    {
        if (deliveryQueue.Count == 0)
        {
            return;
        }

        var retained = new Queue<PendingDelivery>(deliveryQueue.Count);
        while (deliveryQueue.TryDequeue(out var pending))
        {
            if (pending.Kind == RuntimeDomainUpdateKind.Present
                && pending.Candidate is { IsSpeechSuppressed: true } candidate)
            {
                candidate.IsQueued = false;
                continue;
            }

            retained.Enqueue(pending);
        }

        while (retained.TryDequeue(out var pending))
        {
            deliveryQueue.Enqueue(pending);
        }
    }

    private DialoguePageObservation CreateDeliveryPage(PendingCandidate candidate)
    {
        var page = candidate.SourcePage;
        var previous = lastDeliveredSourcePage;
        if (previous is null
            || lastDeliveredSourceGeneration != candidate.SourceGeneration
            || previous.Choices.Length != 0
            || page.Choices.Length != 0
            || previous.WindowId != page.WindowId)
        {
            return page;
        }

        var deliveryText = VisibleTextContinuation.SelectDeliveryText(
            previous.VisibleText,
            page.VisibleText);
        return string.Equals(deliveryText, page.VisibleText, StringComparison.Ordinal)
            ? page
            : new DialoguePageObservation(
                page.IsOpen,
                page.WindowId,
                page.PageRevision,
                page.Speaker,
                deliveryText,
                page.Choices);
    }

    private static bool SamePage(
        DialoguePageObservation left,
        DialoguePageObservation right) =>
        left.IsOpen == right.IsOpen &&
        left.WindowId == right.WindowId &&
        left.PageRevision == right.PageRevision &&
        string.Equals(left.Speaker, right.Speaker, StringComparison.Ordinal) &&
        string.Equals(left.VisibleText, right.VisibleText, StringComparison.Ordinal) &&
        left.Choices.AsSpan().SequenceEqual(right.Choices.AsSpan());

    private static bool SameAskContent(
        DialoguePageObservation left,
        DialoguePageObservation right)
    {
        if (left.IsOpen != right.IsOpen
            || left.WindowId != right.WindowId
            || left.PageRevision != right.PageRevision
            || !string.Equals(left.Speaker, right.Speaker, StringComparison.Ordinal)
            || !string.Equals(left.VisibleText, right.VisibleText, StringComparison.Ordinal)
            || left.Choices.Length != right.Choices.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Choices.Length; index++)
        {
            var leftChoice = left.Choices[index];
            var rightChoice = right.Choices[index];
            if (leftChoice.Index != rightChoice.Index
                || leftChoice.Enabled != rightChoice.Enabled
                || !string.Equals(leftChoice.Text, rightChoice.Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeCandidate(PendingCandidate candidate)
    {
        var text = candidate.Page.VisibleText
            .Replace('\u001f', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        if (text.Length > 72)
        {
            text = text[..72] + "...";
        }

        return
            $"w{candidate.Page.WindowId}/r{candidate.Page.PageRevision}" +
            $"/stable={candidate.IsStable}/queued={candidate.IsQueued}" +
            $"/suppressed={candidate.IsSpeechSuppressed}/text={text}";
    }

    private sealed class PendingCandidate(
        DialoguePageObservation page,
        DateTime sinceUtc,
        long sourceGeneration)
    {
        internal DialoguePageObservation SourcePage { get; set; } = page;

        internal DialoguePageObservation Page { get; set; } = page;

        internal DateTime SinceUtc { get; set; } = sinceUtc;

        internal bool IsStable { get; set; }

        internal bool IsQueued { get; set; }

        internal bool IsSpeechSuppressed { get; set; }

        internal long SourceGeneration { get; } = sourceGeneration;
    }

    private readonly record struct PendingDelivery(
        RuntimeDomainUpdateKind Kind,
        PendingCandidate? Candidate)
    {
        internal static PendingDelivery Closed { get; } =
            new(RuntimeDomainUpdateKind.Closed, null);

        internal static PendingDelivery ForPage(PendingCandidate candidate) =>
            new(RuntimeDomainUpdateKind.Present, candidate);
    }
}
