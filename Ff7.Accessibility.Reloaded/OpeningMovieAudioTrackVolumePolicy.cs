namespace Ff7.Accessibility.Reloaded;

public static class OpeningMovieAudioTrackVolumePolicy
{
    public static float ToGain(int volumePercent) => Math.Clamp(volumePercent, 0, 400) / 100.0f;
}
