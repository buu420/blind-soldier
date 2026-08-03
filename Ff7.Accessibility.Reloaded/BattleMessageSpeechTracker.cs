namespace Ff7.Accessibility.Reloaded;

public sealed class BattleMessageSpeechTracker
{
    private readonly object sync = new();
    private readonly Func<int, string?> resolveBattleText;
    private int activeBuffer = -1;
    private string? pending;

    public BattleMessageSpeechTracker(Func<int, string?> resolveBattleText)
    {
        this.resolveBattleText = resolveBattleText;
    }

    public void ObserveActiveBuffer(short bufferIndex)
    {
        lock (sync)
        {
            pending = null;
            if (bufferIndex < 0)
            {
                activeBuffer = -1;
                return;
            }

            if (bufferIndex == activeBuffer)
            {
                return;
            }

            activeBuffer = bufferIndex;
            var text = resolveBattleText(bufferIndex);
            if (!string.IsNullOrWhiteSpace(text))
            {
                pending = text.Trim();
            }
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            var result = pending;
            pending = null;
            return result;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            activeBuffer = -1;
            pending = null;
        }
    }
}
