using System.Text;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct FfnxVoicePlaybackEvent(
    string FieldName,
    int WindowId,
    int DialogId,
    int Page,
    bool Played,
    long Timestamp)
{
    public int FieldId { get; init; } = -1;
}

/// <summary>
/// Allocation-free capture queue used by the FFNx play_voice detour.
/// </summary>
public sealed class FfnxVoicePlaybackEventQueue
{
    private sealed class Slot
    {
        public readonly byte[] FieldName;
        public int FieldNameLength;
        public int WindowId;
        public int DialogId;
        public int Page;
        public bool Played;
        public long Timestamp;
        public int FieldId;

        public Slot(int maxFieldNameBytes)
        {
            FieldName = new byte[maxFieldNameBytes];
        }
    }

    private readonly Slot[] slots;
    private long readSequence;
    private long writeSequence;
    private long droppedCount;
    private int captureGate;

    public FfnxVoicePlaybackEventQueue(int capacity, int maxFieldNameBytes)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (maxFieldNameBytes < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFieldNameBytes));
        }

        slots = Enumerable.Range(0, capacity)
            .Select(_ => new Slot(maxFieldNameBytes))
            .ToArray();
    }

    public long DroppedCount => Interlocked.Read(ref droppedCount);

    public unsafe bool TryCapture(
        byte* fieldName,
        byte windowId,
        byte dialogId,
        byte page,
        bool played,
        long timestamp,
        int fieldId = -1)
    {
        if (fieldName is null || Interlocked.Exchange(ref captureGate, 1) != 0)
        {
            Interlocked.Increment(ref droppedCount);
            return false;
        }

        try
        {
            var write = Volatile.Read(ref writeSequence);
            if (write - Volatile.Read(ref readSequence) >= slots.Length)
            {
                Interlocked.Increment(ref droppedCount);
                return false;
            }

            var slot = slots[(int)(write % slots.Length)];
            var length = 0;
            while (length < slot.FieldName.Length - 1 && fieldName[length] != 0)
            {
                slot.FieldName[length] = fieldName[length];
                length++;
            }

            if (length == 0)
            {
                Interlocked.Increment(ref droppedCount);
                return false;
            }

            slot.FieldNameLength = length;
            slot.WindowId = windowId;
            slot.DialogId = dialogId;
            slot.Page = page;
            slot.Played = played;
            slot.Timestamp = timestamp;
            slot.FieldId = fieldId;
            Volatile.Write(ref writeSequence, write + 1);
            return true;
        }
        finally
        {
            Volatile.Write(ref captureGate, 0);
        }
    }

    public bool TryDequeue(out FfnxVoicePlaybackEvent observation)
    {
        var read = Volatile.Read(ref readSequence);
        if (read >= Volatile.Read(ref writeSequence))
        {
            observation = default;
            return false;
        }

        var slot = slots[(int)(read % slots.Length)];
        observation = new FfnxVoicePlaybackEvent(
            Encoding.ASCII.GetString(slot.FieldName, 0, slot.FieldNameLength),
            slot.WindowId,
            slot.DialogId,
            slot.Page,
            slot.Played,
            slot.Timestamp) { FieldId = slot.FieldId };
        Volatile.Write(ref readSequence, read + 1);
        return true;
    }

    public unsafe void WarmUp()
    {
        var name = stackalloc byte[] { (byte)'x', 0 };
        TryCapture(name, 0, 0, 0, false, 0);
        TryDequeue(out _);
        Interlocked.Exchange(ref droppedCount, 0);
    }
}

public sealed class FfnxVoicePlaybackTracker
{
    private readonly long maximumAgeTicks;
    private readonly Dictionary<(int WindowId, int DialogId), FfnxVoicePlaybackEvent> voices = [];
    private readonly Dictionary<int, ActiveMessage> activeMessages = [];
    private int currentFieldId = -1;

    public FfnxVoicePlaybackTracker(TimeSpan maximumAge, long timestampFrequency)
    {
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        maximumAgeTicks = Math.Max(1, (long)Math.Ceiling(maximumAge.TotalSeconds * timestampFrequency));
    }

    public void ObserveVoice(FfnxVoicePlaybackEvent observation)
    {
        // World messages use another native reader and cannot own a field window.
        if (observation.FieldName.Equals("_world", StringComparison.OrdinalIgnoreCase)) return;
        voices[(observation.WindowId, observation.DialogId)] = observation;
    }

    public void ObserveMessage(int fieldId, int windowId, int dialogId, long timestamp)
    {
        ObserveFieldTransition(fieldId);
        activeMessages[windowId] = new ActiveMessage(fieldId, windowId, dialogId, timestamp);
    }

    public void ObserveFieldTransition(int fieldId)
    {
        if (currentFieldId == fieldId)
        {
            return;
        }

        // Retain the bounded voice observations because play_voice can be
        // captured just before the native MESSAGE event on the same frame.
        currentFieldId = fieldId;
        activeMessages.Clear();
    }

    public bool ShouldSuppressPrism(int fieldId, int windowId, long timestamp)
    {
        if (!activeMessages.TryGetValue(windowId, out var message) ||
            message.FieldId != fieldId ||
            message.WindowId != windowId ||
            !voices.TryGetValue((message.WindowId, message.DialogId), out var voice) ||
            !voice.Played ||
            (voice.FieldId >= 0 && voice.FieldId != fieldId))
        {
            return false;
        }

        var age = timestamp - voice.Timestamp;
        return age >= 0 && age <= maximumAgeTicks;
    }

    public bool ShouldSuppressPolling(
        int fieldId,
        int windowId,
        long timestamp,
        bool verifiedVoiceHookInstalled,
        bool hasActiveAsk,
        bool readVoicedDialogue = false)
    {
        // Playback is observed through a separately verified FFNx hook. A field
        // need not have a reviewed description-script fingerprint to own its voice.
        return !readVoicedDialogue && verifiedVoiceHookInstalled &&
            !hasActiveAsk &&
            ShouldSuppressPrism(fieldId, windowId, timestamp);
    }

    public bool ShouldSuppressDraw(int fieldId, string text, IReadOnlyList<FieldVisibleWindowSnapshot> windows,
        long timestamp, bool verifiedVoiceHookInstalled, bool hasActiveAsk, bool readVoicedDialogue)
    {
        // A raw draw has no window ID. Match it to the currently displayed text,
        // and keep it readable if any matching window lacks voice evidence.
        var matching = windows.Where(window => window.ChoicePage is null &&
            window.SupplementalSpeech is null && window.Text.Contains(text, StringComparison.Ordinal)).ToArray();
        return matching.Length > 0 && matching.All(window => ShouldSuppressPolling(
            fieldId, window.WindowId, timestamp, verifiedVoiceHookInstalled, hasActiveAsk, readVoicedDialogue));
    }

    public void ObserveNoMessages() => activeMessages.Clear();

    public void Reset()
    {
        activeMessages.Clear();
        currentFieldId = -1;
        voices.Clear();
    }

    private readonly record struct ActiveMessage(
        int FieldId,
        int WindowId,
        int DialogId,
        long Timestamp);
}
