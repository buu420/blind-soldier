namespace Ff7.Accessibility.Reloaded;

public sealed class JunonTimingCueTracker
{
    private bool hasCuedSequence;
    private byte lastCuedSequence;

    public bool Observe(JunonMinigameSnapshot snapshot)
    {
        if (snapshot.Kind != JunonMinigameKind.WelcomeParade || !snapshot.Parade.IsActive)
        {
            Reset();
            return false;
        }

        var parade = snapshot.Parade;
        if (!parade.JoinedFormation || !parade.IsNowPromptVisible || parade.MovieActive != 0)
        {
            return false;
        }

        // The visual Now contains this native counter. A held window or an
        // unreadable frame cannot repeat it, while a new number is detectable
        // even when both the close and the reopen occurred between polls.
        if (hasCuedSequence && lastCuedSequence == parade.NowPromptSequence)
        {
            return false;
        }

        hasCuedSequence = true;
        lastCuedSequence = parade.NowPromptSequence;
        return true;
    }

    public void Reset()
    {
        hasCuedSequence = false;
        lastCuedSequence = 0;
    }
}
