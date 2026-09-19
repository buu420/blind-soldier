namespace Ff7.Accessibility.Reloaded;

public sealed class FieldCutsceneSpeechPriority
{
    private const int MinimumNarrationMs = 1800;
    private const int MaximumNarrationMs = 15000;
    private const int MillisecondsPerWord = 400;
    private const int NarrationPaddingMs = 600;
    private readonly object sync = new();
    private int narrationFieldId = -1;
    private DateTime narrationProtectedUntil = DateTime.MinValue;
    private DateTime recordingHoldExpiresUtc = DateTime.MinValue;
    private bool recordingBacked;
    private Func<bool>? recordingIsPlaying;

    public void AttachRecordingProbe(Func<bool> isPlaying)
    {
        ArgumentNullException.ThrowIfNull(isPlaying);
        Volatile.Write(ref recordingIsPlaying, isPlaying);
    }

    // A recording uses a different device from Prism. Keep the line in the host's
    // queue until the clip ends; interrupt:false only queues inside the screen reader.
    public bool ShouldDeferDialogueDelivery(DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc || !IsRecordingPlaying()) return false;
        lock (sync)
        {
            // Covers the brief gap between output acceptance and reservation.
            if (recordingHoldExpiresUtc == DateTime.MinValue)
                recordingHoldExpiresUtc = now + TimeSpan.FromSeconds(30);
            return now < recordingHoldExpiresUtc;
        }
    }

    public bool TryDeliverDialogue(DateTime now, Func<bool> deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        return !ShouldDeferDialogueDelivery(now) && deliver();
    }

    private bool IsRecordingPlaying()
    {
        try { return Volatile.Read(ref recordingIsPlaying)?.Invoke() == true; }
        catch { return false; }
    }

    public void BeginNarration(int fieldId, string text, DateTime now)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var durationMs = Math.Clamp(NarrationPaddingMs + words * MillisecondsPerWord,
            MinimumNarrationMs, MaximumNarrationMs);
        Begin(fieldId, TimeSpan.FromMilliseconds(durationMs), now, false);
    }

    public void BeginNarration(int fieldId, TimeSpan duration, DateTime now) =>
        Begin(fieldId, duration, now, true);

    private void Begin(int fieldId, TimeSpan duration, DateTime now, bool isRecording)
    {
        lock (sync)
        {
            narrationFieldId = fieldId;
            narrationProtectedUntil = now + (duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
            // A stuck output must not silence dialogue indefinitely. Allow the declared
            // clip length plus device startup slack, with a 30-second minimum ceiling.
            recordingHoldExpiresUtc = now + TimeSpan.FromSeconds(Math.Max(30, duration.TotalSeconds + 5));
            recordingBacked = isRecording;
        }
    }

    public bool ShouldQueueDialogue(int fieldId, DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc) return false;
        var recordingHolds = ShouldDeferDialogueDelivery(now);
        lock (sync)
        {
            // Once a recording is accepted, device completion is authoritative even
            // if it finishes or is cancelled before the first observation tick.
            if (recordingBacked && Volatile.Read(ref recordingIsPlaying) is not null)
                return recordingHolds;
            return recordingHolds || (fieldId == narrationFieldId && now < narrationProtectedUntil);
        }
    }

    public void Reset()
    {
        var recordingContinues = IsRecordingPlaying();
        lock (sync)
        {
            narrationFieldId = -1;
            narrationProtectedUntil = DateTime.MinValue;
            // Film-owned cues can survive a field transition. Preserve their existing
            // deadline; a reset must not extend a stalled output's hold forever.
            if (!recordingContinues)
            {
                recordingBacked = false;
                recordingHoldExpiresUtc = DateTime.MinValue;
            }
        }
    }

    public static bool ShouldWaitForDialogue(byte activeMessageCount, bool hasReadableActiveMessage) =>
        activeMessageCount != 0 && hasReadableActiveMessage;
}
