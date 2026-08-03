namespace Ff7.Accessibility.Reloaded;

public sealed class OpeningMovieProbeLifetime
{
    private const int EchoSDisclaimerIdentityGraceObservations = 20;
    private bool openingFieldObserved;
    private int echoSDisclaimerIdentityMisses;

    public bool ShouldProbe { get; private set; } = true;

    public void Observe(
        int currentModule,
        int fieldId,
        bool movieDetected,
        bool movieFileActive,
        bool isSupportedEchoSDisclaimerField = false)
    {
        if (!ShouldProbe)
        {
            return;
        }

        if (movieDetected && !movieFileActive)
        {
            ShouldProbe = false;
            return;
        }

        if (!movieDetected &&
            currentModule == FieldPositionReader.FieldModule &&
            fieldId == DeferredZoneSpeechTracker.OpeningFieldId)
        {
            openingFieldObserved = true;
            echoSDisclaimerIdentityMisses = 0;
            return;
        }

        if (!movieDetected &&
            currentModule == FieldPositionReader.FieldModule &&
            fieldId != DeferredZoneSpeechTracker.OpeningFieldId)
        {
            if (fieldId == 109 && isSupportedEchoSDisclaimerField)
            {
                echoSDisclaimerIdentityMisses = 0;
                return;
            }

            if (openingFieldObserved && fieldId == 109)
            {
                echoSDisclaimerIdentityMisses++;
                if (echoSDisclaimerIdentityMisses <= EchoSDisclaimerIdentityGraceObservations)
                {
                    return;
                }
            }

            ShouldProbe = false;
        }
    }
}
