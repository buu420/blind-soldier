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
        lock (sync)
        {
            narrationFieldId = fieldId;
            narrationProtectedUntil = now.AddMilliseconds(durationMs);
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
