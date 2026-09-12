using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime;

internal interface ISteam2026MovieNarrationPlayback : IDisposable
{
    /// <summary>
    /// Opens the track and the output device ahead of the film, off the caller's thread.
    /// The opening film is the one case where starting has to be instant.
    /// </summary>
    bool Prepare(string reason);

    bool Start(string reason);

    bool Stop(string reason);
}

internal sealed class Steam2026MovieNarrationPlayback : ISteam2026MovieNarrationPlayback
{
    private readonly OpeningMovieAudioTrackPlayer player;

    internal Steam2026MovieNarrationPlayback(
        string absolutePath,
        int volumePercent,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(log);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException(
                "The Steam 2026 opening narration path must be absolute.",
                nameof(absolutePath));
        }

        player = new OpeningMovieAudioTrackPlayer(
            Path.GetFullPath(absolutePath),
            volumePercent,
            log);
    }

    public bool Prepare(string reason) => player.Prepare(reason);

    public bool Start(string reason) => player.Start(reason);

    public bool Stop(string reason) => player.Stop(reason);

    public void Dispose() => player.Dispose();
}
