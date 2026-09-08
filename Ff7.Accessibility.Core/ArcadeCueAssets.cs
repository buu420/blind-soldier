namespace Ff7.Accessibility.Core;

/// <summary>
/// The packaged arcade cue waves, in one place so the config defaults, both
/// runtimes' fallbacks and the package validation cannot drift apart. A blank
/// configured path falls back to the asset for that cue and never to another one:
/// a top-of-rise fallback that quietly plays the rise tick is worse than silence,
/// because the player cannot tell the two moments apart.
/// </summary>
public static class ArcadeCueAssets
{
    public const string Directory = @"Assets\arcade";

    public const string BasketballRiseTick = @"Assets\arcade\basketball_rise_tick.wav";
    public const string BasketballPoseTop = @"Assets\arcade\basketball_pose_top.wav";
    public const string BasketballCredits = @"Assets\arcade\basketball_cues.credits.txt";

    public const string ArmWrestlingLevel = @"Assets\arcade\arm_wrestling_level.wav";
    public const string ArmWrestlingPushAhead = @"Assets\arcade\arm_wrestling_push_ahead.wav";
    public const string ArmWrestlingPushedBack = @"Assets\arcade\arm_wrestling_pushed_back.wav";
    public const string ArmWrestlingCredits = @"Assetsrcaderm_wrestling_cues.credits.txt";

    /// <summary>
    /// The cue for a native field script that has stopped and is waiting for a button.
    /// Two pips rather than one tone, so it cannot be confused with either arcade cue,
    /// and on its own device so a held button cannot cut it off.
    /// </summary>
    public const string FieldActivityButtonReady = @"Assets\arcade\field_activity_button_ready.wav";
    public const string FieldActivityCredits = @"Assets\arcade\field_activity_cues.credits.txt";

    /// <summary>Every packaged arcade cue file, waves and attributions.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        BasketballRiseTick,
        BasketballPoseTop,
        BasketballCredits,
        ArmWrestlingLevel,
        ArmWrestlingPushAhead,
        ArmWrestlingPushedBack,
        ArmWrestlingCredits,
        FieldActivityButtonReady,
        FieldActivityCredits
    ];
}

