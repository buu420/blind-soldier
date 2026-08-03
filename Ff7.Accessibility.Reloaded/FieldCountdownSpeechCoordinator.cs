using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldCountdownSpeechCoordinator
{
    private readonly FieldCountdownSpeechTracker tracker = new();
    private FieldCountdownAnnouncement? pending;
    private byte clockWindowMask;

    public void Observe(FieldCountdownSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            Reset();
            return;
        }

        clockWindowMask = snapshot.Value.ClockWindowMask;
        if (!snapshot.Value.IsActive)
        {
            tracker.Observe(false, snapshot.Value.RemainingSeconds);
            pending = null;
            return;
        }

        if (tracker.Observe(true, snapshot.Value.RemainingSeconds) is { } announcement)
        {
            pending = announcement;
        }
    }

    public bool TryGetPending(out FieldCountdownAnnouncement announcement)
    {
        if (pending is not { } value)
        {
            announcement = default;
            return false;
        }

        announcement = value;
        return true;
    }

    public void Acknowledge(FieldCountdownAnnouncement announcement)
    {
        if (pending == announcement)
        {
            pending = null;
        }
    }

    public bool ShouldSuppressWindow(FieldVisibleWindowSnapshot window) =>
        OwnsWindow(window.WindowId);

    public bool OwnsWindow(int windowId) =>
        windowId is >= 0 and < FieldCountdownReader.WindowCount &&
        (clockWindowMask & (1 << windowId)) != 0;

    public void Reset()
    {
        tracker.Reset();
        pending = null;
        clockWindowMask = 0;
    }
}
