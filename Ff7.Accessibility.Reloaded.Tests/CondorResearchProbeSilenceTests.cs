using Ff7.Accessibility.Core;

/// <summary>
/// Guards the rule that a research instrument must not speak over the reader
/// that shipped from its findings.
/// </summary>
/// <remarks>
/// <c>CondorMinigameProbe</c> existed to locate the Fort Condor battle's state in
/// memory, because module 9 draws no text any hook can see. It found the
/// addresses, they were written down, and <c>CondorBattleSpeechTracker</c> was
/// built on them - and the probe was left enabled, still wired to real speech.
///
/// <para>In the 2026-08-21 session it queued 1,090 utterances against the
/// tracker's 595: raw cursor coordinates ("428, 706") 393 times, "Unit 255." 398
/// times, "cursor" 168 times, plus a duplicate of every hire line prefixed "Row
/// N. Id N.". Peak sixteen in a single second. They are queued rather than
/// interrupting, so the backlog never drains and the option under the cursor
/// arrives long after the player has moved on. The reported symptom was that the
/// Setting Menu read its title and then none of its options - the options were
/// spoken, but buried.</para>
///
/// <para>The probe stays in the tree: the enemy unit names are still unmapped and
/// it is the instrument that would close that gap. It just has to be off unless
/// someone is deliberately using it.</para>
/// </remarks>
internal static class CondorResearchProbeSilenceTests
{
    public static void Run()
    {
        TheFortCondorResearchProbeIsOffByDefault();
        NoResearchProbeSpeaksByDefault();
    }

    private static void TheFortCondorResearchProbeIsOffByDefault()
    {
        var config = new AccessibilityConfig();
        if (config.EnableCondorMinigameProbe)
        {
            throw new InvalidOperationException(
                "EnableCondorMinigameProbe defaults to true, so the research probe " +
                "speaks its diagnostics over the Fort Condor reader and the player " +
                "cannot hear the menu options.");
        }
    }

    /// <summary>
    /// The same mistake, stated once for every diagnostic channel that reaches the
    /// speaker rather than the log.
    /// </summary>
    private static void NoResearchProbeSpeaksByDefault()
    {
        var config = new AccessibilityConfig();
        foreach (var (name, enabled) in new[]
                 {
                     ("EnableCondorMinigameProbe", config.EnableCondorMinigameProbe),
                     ("EnableInGameMenuTextDrawSpeech", config.EnableInGameMenuTextDrawSpeech)
                 })
        {
            if (enabled)
            {
                throw new InvalidOperationException(
                    $"{name} defaults to true. Diagnostics belong in the log; a player " +
                    "hears whatever this speaks on top of the real reader.");
            }
        }
    }
}
