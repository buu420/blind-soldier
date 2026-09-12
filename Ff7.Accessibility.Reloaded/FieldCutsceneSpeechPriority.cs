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

    public void BeginNarration(int fieldId, string text, DateTime now)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var durationMs = Math.Clamp(
            NarrationPaddingMs + words * MillisecondsPerWord,
            MinimumNarrationMs,
            MaximumNarrationMs);
        BeginNarration(fieldId, TimeSpan.FromMilliseconds(durationMs), now);
    }

    /// <summary>
    /// Reserves the window for a description whose real length is known - a recording
    /// rather than screen-reader speech.
    ///
    /// <para>The word-count estimate above exists because nobody can know how long a
    /// screen reader will take. A recording's length is not a guess, so it is used as
    /// it stands: clamping it to the estimate's bounds would hold the window open
    /// after a two-second clip had finished, or release it in the middle of a long
    /// one.</para>
    /// </summary>
    public void BeginNarration(int fieldId, TimeSpan duration, DateTime now)
    {
        lock (sync)
        {
            narrationFieldId = fieldId;
            narrationProtectedUntil = now + (duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
        }
    }

    public bool ShouldQueueDialogue(int fieldId, DateTime now)
    {
        lock (sync)
        {
            return fieldId == narrationFieldId && now < narrationProtectedUntil;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            narrationFieldId = -1;
            narrationProtectedUntil = DateTime.MinValue;
        }
    }

    public static bool ShouldWaitForDialogue(byte activeMessageCount, bool hasReadableActiveMessage) =>
        activeMessageCount != 0 && hasReadableActiveMessage;
}
