namespace Ff7.Accessibility.Reloaded;

public readonly record struct EchoSDisclaimerSpeechCandidate(
    uint ScriptPointer,
    int MessageId,
    string Text);

public sealed class EchoSDisclaimerSpeechTracker
{
    private const int DisclaimerFieldId = 109;
    private const int FirstMessageId = 1;
    private const int LastMessageId = 4;

    private readonly Queue<int> pendingMessageIds = [];
    private readonly HashSet<int> queuedMessageIds = [];
    private readonly HashSet<int> spokenMessageIds = [];
    private int fieldId = -1;
    private uint scriptPointer;

    public bool HasPending => pendingMessageIds.Count > 0;

    public bool Queue(int observedFieldId, int messageId)
    {
        if (observedFieldId != DisclaimerFieldId ||
            messageId is < FirstMessageId or > LastMessageId ||
            queuedMessageIds.Contains(messageId) ||
            spokenMessageIds.Contains(messageId) ||
            pendingMessageIds.Count >= LastMessageId - FirstMessageId + 1)
        {
            return false;
        }

        pendingMessageIds.Enqueue(messageId);
        queuedMessageIds.Add(messageId);
        return true;
    }

    public EchoSDisclaimerSpeechCandidate? TryResolve(LoadedFieldScriptIdentity identity)
    {
        ObserveLifecycle(identity);
        if (!EchoSCompatibilityManifest.IsSupportedDisclaimer(identity))
        {
            return null;
        }

        while (pendingMessageIds.Count > 0 && spokenMessageIds.Contains(pendingMessageIds.Peek()))
        {
            queuedMessageIds.Remove(pendingMessageIds.Dequeue());
        }

        if (pendingMessageIds.Count == 0)
        {
            return null;
        }

        var messageId = pendingMessageIds.Peek();
        var text = EchoSCompatibilityManifest.ResolveDisclaimerSpeechText(identity, messageId);
        return text is null
            ? null
            : new EchoSDisclaimerSpeechCandidate(identity.ScriptPointer, messageId, text);
    }

    public void Acknowledge(EchoSDisclaimerSpeechCandidate candidate, bool delivered)
    {
        if (!delivered ||
            scriptPointer != candidate.ScriptPointer ||
            pendingMessageIds.Count == 0 ||
            pendingMessageIds.Peek() != candidate.MessageId)
        {
            return;
        }

        pendingMessageIds.Dequeue();
        queuedMessageIds.Remove(candidate.MessageId);
        spokenMessageIds.Add(candidate.MessageId);
    }

    public void ObserveLifecycle(LoadedFieldScriptIdentity identity)
    {
        if (fieldId == identity.FieldId && scriptPointer == identity.ScriptPointer)
        {
            return;
        }

        var preserveIdentityRaceCandidate =
            identity.FieldId == DisclaimerFieldId && fieldId != DisclaimerFieldId;
        fieldId = identity.FieldId;
        scriptPointer = identity.ScriptPointer;
        spokenMessageIds.Clear();
        if (!preserveIdentityRaceCandidate)
        {
            pendingMessageIds.Clear();
            queuedMessageIds.Clear();
        }
    }

    public bool OwnsVisibleSpeech(LoadedFieldScriptIdentity identity) =>
        EchoSCompatibilityManifest.IsSupportedDisclaimer(identity) &&
        fieldId == identity.FieldId &&
        scriptPointer == identity.ScriptPointer &&
        spokenMessageIds.Count > 0;

    public void Reset()
    {
        fieldId = -1;
        scriptPointer = 0;
        pendingMessageIds.Clear();
        queuedMessageIds.Clear();
        spokenMessageIds.Clear();
    }
}
