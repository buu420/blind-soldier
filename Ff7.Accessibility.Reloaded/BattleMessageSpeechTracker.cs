namespace Ff7.Accessibility.Reloaded;

public sealed class BattleMessageSpeechTracker
{
    /// <summary>
    /// How soon a re-activation of the same text in the same buffer is still the same
    /// message. The native "LIMIT BREAK" banner blinks: its buffer is re-activated every
    /// few frames for about two seconds, and each re-activation used to be spoken again -
    /// fifteen interrupting announcements per Limit break in both 2026-09-27 logs, which
    /// talked over the Limit command and the Limit list. A message shown again after a
    /// real pause (the same enemy's next attack) is spoken as before.
    /// </summary>
    public static readonly TimeSpan FlickerWindow = TimeSpan.FromMilliseconds(600);

    private readonly object sync = new();
    private readonly Func<int, string?> resolveBattleText;
    private readonly Func<DateTime>? flickerClock;
    private int activeBuffer = -1;
    private string? pending;
    private int lastAnnouncedBuffer = -1;
    private string? lastAnnouncedText;
    private DateTime lastActivationAt;

    public BattleMessageSpeechTracker(Func<int, string?> resolveBattleText)
        : this(resolveBattleText, flickerClock: null)
    {
    }

    /// <param name="flickerClock">
    /// When given, the time of each native activation; identical re-activations within
    /// <see cref="FlickerWindow"/> of the last one are not spoken again. When null, every
    /// re-activation after an inactive marker is spoken, as before.
    /// </param>
    public BattleMessageSpeechTracker(Func<int, string?> resolveBattleText, Func<DateTime>? flickerClock)
    {
        this.resolveBattleText = resolveBattleText;
        this.flickerClock = flickerClock;
    }

    public void ObserveActiveBuffer(short bufferIndex)
    {
        ObserveActiveBuffer(
            bufferIndex,
            bufferIndex < 0 ? null : resolveBattleText(bufferIndex));
    }

    public void ObserveActiveBuffer(short bufferIndex, string? resolvedText)
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
            if (string.IsNullOrWhiteSpace(resolvedText))
            {
                return;
            }

            var text = resolvedText.Trim();
            if (flickerClock is not null)
            {
                var now = flickerClock();
                var isFlicker = bufferIndex == lastAnnouncedBuffer &&
                    string.Equals(text, lastAnnouncedText, StringComparison.Ordinal) &&
                    now >= lastActivationAt &&
                    now - lastActivationAt <= FlickerWindow;
                lastActivationAt = now;
                if (isFlicker)
                {
                    return;
                }

                lastAnnouncedBuffer = bufferIndex;
                lastAnnouncedText = text;
            }

            pending = text;
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
