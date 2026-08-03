namespace Ff7.Accessibility.Reloaded;

public static class FieldAudibleCueTickSequence
{
    public static void Run(
        Action updateSuppressionState,
        Action tickFootsteps,
        Action tickNavigation,
        Action tickObjectCues,
        Action tickLadderCues,
        Action tickExitCues)
    {
        updateSuppressionState();
        tickFootsteps();
        tickNavigation();
        tickObjectCues();
        tickLadderCues();
        tickExitCues();
    }
}
