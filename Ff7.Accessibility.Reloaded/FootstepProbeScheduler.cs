namespace Ff7.Accessibility.Reloaded;

public sealed class FootstepProbeScheduler
{
    private readonly bool enabled;
    private readonly TimeSpan delay;
    private readonly DateTime startedAt;
    private readonly Action<string> play;
    private bool played;

    public FootstepProbeScheduler(bool enabled, TimeSpan delay, DateTime startedAt, Action<string> play)
    {
        this.enabled = enabled;
        this.delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        this.startedAt = startedAt;
        this.play = play;
    }

    public bool TryPlay(DateTime now)
    {
        if (!enabled || played || now - startedAt < delay)
        {
            return false;
        }

        played = true;
        play("probe");
        return true;
    }
}
