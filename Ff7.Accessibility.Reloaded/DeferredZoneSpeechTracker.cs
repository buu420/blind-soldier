namespace Ff7.Accessibility.Reloaded;

public sealed class DeferredZoneSpeechTracker
{
    public const int OpeningFieldId = 116;

    private readonly TimeSpan fieldSettleWindow;
    private int currentFieldId = -1;
    private DateTime currentFieldSeenAt = DateTime.MinValue;
    private string pendingZone = string.Empty;
    private bool announcedCurrentEntry;
    private bool pendingAnnouncementWasBlocked;

    public DeferredZoneSpeechTracker()
        : this(TimeSpan.FromMilliseconds(500))
    {
    }

    public DeferredZoneSpeechTracker(TimeSpan fieldSettleWindow)
    {
        this.fieldSettleWindow = fieldSettleWindow < TimeSpan.Zero
            ? TimeSpan.Zero
            : fieldSettleWindow;
    }

    public string? Observe(
        int fieldId,
        FieldMessageCandidate candidate,
        DateTime now,
        bool announcementBlocked)
    {
        ObserveField(fieldId, now);
        if (announcementBlocked)
        {
            pendingAnnouncementWasBlocked = true;
        }

        if (IsZoneCandidate(candidate))
        {
            pendingZone = candidate.Text;
        }

        if (announcementBlocked ||
            announcedCurrentEntry ||
            pendingZone.Length == 0 ||
            !IsCurrentFieldSettled(fieldId, now))
        {
            return null;
        }

        // Delivery owns the state transition.  Prism can reject a request
        // transiently, so merely returning a candidate must not consume it.
        return pendingZone;
    }

    public bool Acknowledge(string speech)
    {
        if (announcedCurrentEntry ||
            string.IsNullOrEmpty(speech) ||
            !string.Equals(pendingZone, speech, StringComparison.Ordinal))
        {
            return false;
        }

        announcedCurrentEntry = true;
        pendingZone = string.Empty;
        return true;
    }

    public bool Acknowledge(int fieldId, string speech) =>
        fieldId == currentFieldId && Acknowledge(speech);

    public bool IsCurrentFieldSettled(int fieldId, DateTime now) =>
        fieldId >= 0 &&
        fieldId == currentFieldId &&
        currentFieldSeenAt != DateTime.MinValue &&
        now - currentFieldSeenAt >= fieldSettleWindow;

    public bool ShouldInterruptPendingAnnouncement => !pendingAnnouncementWasBlocked;

    public void LeaveField()
    {
        currentFieldId = -1;
        currentFieldSeenAt = DateTime.MinValue;
        pendingZone = string.Empty;
        announcedCurrentEntry = false;
        pendingAnnouncementWasBlocked = false;
    }

    public static bool ShouldBlockForOpeningMovie(
        int fieldId,
        bool movieDetected,
        bool movieFileActive,
        bool descriptionRunning) =>
        fieldId == OpeningFieldId &&
        (!movieDetected || movieFileActive || descriptionRunning);

    public static bool ShouldBlockForFieldEntry(
        int fieldId,
        bool openingMovieBlocked,
        byte activeMessageCount,
        byte userControl,
        bool narrationPending,
        bool narrationProtected) =>
        openingMovieBlocked ||
        activeMessageCount != 0 ||
        userControl != 0 ||
        narrationPending ||
        narrationProtected;

    public static bool IsZoneCandidate(FieldMessageCandidate candidate) =>
        string.Equals(candidate.Source, "line", StringComparison.Ordinal) &&
        LooksLikeZoneName(candidate.Text);

    private void ObserveField(int fieldId, DateTime now)
    {
        if (fieldId == currentFieldId)
        {
            return;
        }

        currentFieldId = fieldId;
        currentFieldSeenAt = now;
        pendingZone = string.Empty;
        announcedCurrentEntry = false;
        pendingAnnouncementWasBlocked = false;
    }

    private static bool LooksLikeZoneName(string text)
    {
        if (text.Length == 0 || text.Length > 48)
        {
            return false;
        }

        if (text.Contains(':', StringComparison.Ordinal) ||
            text.Contains('!', StringComparison.Ordinal) ||
            text.Contains('?', StringComparison.Ordinal) ||
            text.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        return text.Any(char.IsLetter);
    }
}
