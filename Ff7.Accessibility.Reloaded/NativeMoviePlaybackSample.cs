namespace Ff7.Accessibility.Reloaded;

/// <summary>A coherent copy of the decoder state for the film actually being shown.</summary>
public readonly record struct NativeMoviePlaybackSample(
    string FilmFileName,
    int Module,
    uint Frame,
    bool IsPlaying,
    bool IsPaused,
    long Generation = 0,
    double FramesPerSecond = 15d)
{
    public double PositionSeconds => Frame / FramesPerSecond;
}
