namespace Ff7.Accessibility.Reloaded;

public sealed class FieldZoneTransitionCueTracker
{
    public const int TitleModule = TitleMenuCursorReader.TitleModule;

    private readonly TimeSpan settleWindow;
    private int? confirmedFieldId;
    private int? candidateFieldId;
    private DateTime candidateSeenAt;

    public FieldZoneTransitionCueTracker(TimeSpan settleWindow)
    {
        this.settleWindow = settleWindow < TimeSpan.Zero ? TimeSpan.Zero : settleWindow;
    }

    public int PreviousFieldId { get; private set; } = -1;
    public int CurrentFieldId { get; private set; } = -1;

    public bool Observe(int currentModule, int fieldId, DateTime now)
    {
        if (currentModule == TitleModule)
        {
            Reset();
            return false;
        }

        if (currentModule != FieldPositionReader.FieldModule || fieldId < 0)
        {
            return false;
        }

        if (candidateFieldId != fieldId)
        {
            candidateFieldId = fieldId;
            candidateSeenAt = now;
            return false;
        }

        if (now - candidateSeenAt < settleWindow)
        {
            return false;
        }

        if (!confirmedFieldId.HasValue)
        {
            confirmedFieldId = fieldId;
            CurrentFieldId = fieldId;
            return false;
        }

        if (confirmedFieldId.Value == fieldId)
        {
            return false;
        }

        PreviousFieldId = confirmedFieldId.Value;
        CurrentFieldId = fieldId;
        confirmedFieldId = fieldId;
        return true;
    }

    public void Reset()
    {
        confirmedFieldId = null;
        candidateFieldId = null;
        candidateSeenAt = default;
        PreviousFieldId = -1;
        CurrentFieldId = -1;
    }
}
