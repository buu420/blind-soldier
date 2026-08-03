namespace Ff7.Accessibility.Core;

public readonly record struct HighwayAutoSteeringModeUpdate(
    bool Enabled,
    bool ShouldControl,
    string? Announcement);

/// <summary>
/// Pure session policy for the default-on motorcycle steering toggle. It owns
/// no keyboard state and accepts only already-checked module and foreground
/// observations from the runtime host.
/// </summary>
public sealed class HighwayAutoSteeringModeTracker
{
    public const string EnabledAnnouncement = "Motorcycle auto steering on.";
    public const string DisabledAnnouncement =
        "Motorcycle auto steering off. Steering beeps on.";

    private bool enabled;
    private bool sessionObserved;
    private bool sessionAnnounced;

    public HighwayAutoSteeringModeTracker(bool enabledByDefault)
    {
        enabled = enabledByDefault;
    }

    public bool Enabled => enabled;

    public HighwayAutoSteeringModeUpdate Observe(
        bool isHighway,
        bool isForeground,
        bool toggleRequested)
    {
        if (!isHighway)
        {
            sessionObserved = false;
            sessionAnnounced = false;
            return new HighwayAutoSteeringModeUpdate(enabled, false, null);
        }

        if (!sessionObserved)
        {
            sessionObserved = true;
            sessionAnnounced = false;
        }

        if (!isForeground)
        {
            return new HighwayAutoSteeringModeUpdate(enabled, false, null);
        }

        string? announcement = null;
        if (toggleRequested)
        {
            enabled = !enabled;
            sessionAnnounced = true;
            announcement = CurrentAnnouncement();
        }
        else if (!sessionAnnounced)
        {
            sessionAnnounced = true;
            announcement = CurrentAnnouncement();
        }

        return new HighwayAutoSteeringModeUpdate(enabled, enabled, announcement);
    }

    private string CurrentAnnouncement() =>
        enabled ? EnabledAnnouncement : DisabledAnnouncement;
}
