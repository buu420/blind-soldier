namespace Ff7.Accessibility.Core;

/// <summary>
/// The player's description volume: a master level for the recorded descriptions - scene,
/// film, opening and battle recordings - and never for the screen reader's voice, the music
/// or the cue sounds. It multiplies each recording's own calibration (100 percent for most,
/// 300 for the quieter opening film), and <see cref="AudioDescriptionLimiter"/> keeps the
/// boosted result within full scale.
/// </summary>
public static class AudioDescriptionLevel
{
    public const int MinimumPercent = 50;
    public const int MaximumPercent = 300;
    public const int StepPercent = 25;
    public const int DefaultPercent = 100;

    /// <summary>The level applied: a hand-edited value is held within 50 to 300, never muted.</summary>
    public static int Effective(int percent) => Math.Clamp(percent, MinimumPercent, MaximumPercent);

    /// <summary>The next step up on the 25-percent grid, stopping at the maximum.</summary>
    public static int StepUp(int percent) =>
        Math.Min(MaximumPercent, ((Effective(percent) / StepPercent) + 1) * StepPercent);

    /// <summary>The next step down on the 25-percent grid, stopping at the minimum.</summary>
    public static int StepDown(int percent) =>
        Math.Max(MinimumPercent, (((Effective(percent) + StepPercent - 1) / StepPercent) - 1) * StepPercent);

    /// <summary>A recording's linear gain: its own calibration times the master level.</summary>
    public static float Gain(float trackGain, int masterPercent) => trackGain * (Effective(masterPercent) / 100f);
}
