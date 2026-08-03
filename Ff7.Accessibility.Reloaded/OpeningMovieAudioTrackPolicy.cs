namespace Ff7.Accessibility.Reloaded;

public static class OpeningMovieAudioTrackPolicy
{
    public static bool ShouldUseReloadedPlayback(bool enabled, bool ffnxLoaded)
    {
        _ = ffnxLoaded;
        return enabled;
    }
}
