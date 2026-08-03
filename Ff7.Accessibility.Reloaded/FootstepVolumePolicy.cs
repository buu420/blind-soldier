namespace Ff7.Accessibility.Reloaded;

public static class FootstepVolumePolicy
{
    public const int MaxVolumePercent = 400;

    public static int ClampVolumePercent(int volumePercent) =>
        Math.Clamp(volumePercent, 0, MaxVolumePercent);

    public static float ToGain(int volumePercent) =>
        ClampVolumePercent(volumePercent) / 100f;
}
