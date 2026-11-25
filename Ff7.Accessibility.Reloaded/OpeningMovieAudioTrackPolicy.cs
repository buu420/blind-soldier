namespace Ff7.Accessibility.Reloaded;

public static class OpeningMovieAudioTrackPolicy
{
    public static bool ShouldUseReloadedPlayback(bool enabled, bool ffnxLoaded, bool useRecordedDescriptions = true)
    {
        _ = ffnxLoaded;
        return enabled && useRecordedDescriptions;
    }

    public static bool ShouldUseScreenReader(bool recordedEnabled, bool spokenEnabled, bool useRecordedDescriptions) =>
        spokenEnabled || (recordedEnabled && !useRecordedDescriptions);
}
