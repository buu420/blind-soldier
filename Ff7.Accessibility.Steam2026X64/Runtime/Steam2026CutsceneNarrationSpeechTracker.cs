namespace Ff7.Accessibility.Steam2026X64.Runtime;

internal sealed class Steam2026CutsceneNarrationSpeechTracker
{
    private int narrationFieldId = -1;
    private bool observedSpeaking;
    private bool completed;

    internal void Begin(int fieldId)
    {
        narrationFieldId = fieldId;
        observedSpeaking = false;
        completed = false;
    }

    internal bool ShouldProtectDialogue(
        int fieldId,
        bool estimatedProtection,
        bool speechStateAvailable,
        bool speechIsActive)
    {
        if (!estimatedProtection)
        {
            return false;
        }

        if (fieldId != narrationFieldId)
        {
            return true;
        }

        if (completed)
        {
            return false;
        }

        if (!speechStateAvailable)
        {
            return true;
        }

        if (speechIsActive)
        {
            observedSpeaking = true;
            return true;
        }

        if (!observedSpeaking)
        {
            return true;
        }

        completed = true;
        return false;
    }

    internal void Reset()
    {
        narrationFieldId = -1;
        observedSpeaking = false;
        completed = false;
    }
}
