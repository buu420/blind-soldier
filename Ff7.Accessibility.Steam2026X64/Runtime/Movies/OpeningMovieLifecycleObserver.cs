using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Movies;

/// <summary>
/// Converts validated native movie callback captures into opening-only
/// lifecycle observations. It performs no hooks or publication.
/// </summary>
public sealed class OpeningMovieLifecycleObserver
{
    public const string OpeningMovieKey = "opening";

    private readonly object stateLock = new();
    private readonly string normalizedOpeningPath;
    private readonly NativeMovieCallbackContract contract;
    private bool openingPrepared;
    private bool openingActive;

    public OpeningMovieLifecycleObserver(
        string expectedOpeningPath,
        NativeMovieCallbackContract contract)
    {
        normalizedOpeningPath = NormalizeRequiredPath(expectedOpeningPath);
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
    }

    public MovieLifecycleEvent? Observe(NativeMovieCallbackCapture capture)
    {
        lock (stateLock)
        {
            return ObserveCore(capture);
        }
    }

    /// <summary>
    /// Attempts one lifecycle observation without waiting for observer state.
    /// Native callback paths use this fail-closed surface exclusively.
    /// </summary>
    internal bool TryObserve(
        NativeMovieCallbackCapture capture,
        out MovieLifecycleEvent? lifecycleEvent)
    {
        lifecycleEvent = null;
        if (!Monitor.TryEnter(stateLock))
        {
            return false;
        }

        try
        {
            lifecycleEvent = ObserveCore(capture);
            return true;
        }
        finally
        {
            Monitor.Exit(stateLock);
        }
    }

    /// <summary>
    /// Attempts to clear lifecycle state without waiting.
    /// </summary>
    internal bool TryReset()
    {
        if (!Monitor.TryEnter(stateLock))
        {
            return false;
        }

        try
        {
            ResetState();
            return true;
        }
        finally
        {
            Monitor.Exit(stateLock);
        }
    }

    public MovieLifecycleEvent? ObserveSkip(DateTime timestampUtc)
    {
        lock (stateLock)
        {
            return EndActive(timestampUtc, MovieLifecycleKind.Skipped);
        }
    }

    public MovieLifecycleEvent? ObserveModuleTransition(DateTime timestampUtc)
    {
        lock (stateLock)
        {
            return EndActive(timestampUtc, MovieLifecycleKind.Stopped);
        }
    }

    private MovieLifecycleEvent? ObserveCore(NativeMovieCallbackCapture capture)
    {
        if (!contract.IsCurrentCapture(capture))
        {
            ResetState();
            return null;
        }

        return capture.Identity.Metadata.Kind switch
        {
            NativeMovieCallbackKind.Prepare => ObservePrepare(capture),
            NativeMovieCallbackKind.Start => ObserveStart(capture),
            NativeMovieCallbackKind.Release or NativeMovieCallbackKind.Stop =>
                EndActive(capture.TimestampUtc, MovieLifecycleKind.Stopped),
            _ => ResetAndReject()
        };
    }

    private MovieLifecycleEvent? ObservePrepare(NativeMovieCallbackCapture capture)
    {
        var terminal = EndActive(capture.TimestampUtc, MovieLifecycleKind.Stopped);
        openingPrepared = capture.Succeeded
                          && TryNormalizePath(capture.CanonicalMoviePath, out var normalizedPath)
                          && string.Equals(
                              normalizedPath,
                              normalizedOpeningPath,
                              StringComparison.OrdinalIgnoreCase);
        return terminal;
    }

    private MovieLifecycleEvent? ObserveStart(NativeMovieCallbackCapture capture)
    {
        if (openingActive || !openingPrepared)
        {
            return null;
        }

        openingPrepared = false;
        if (capture.StateBefore != 0 || capture.StateAfter != 1)
        {
            return null;
        }

        openingActive = true;
        return CreateEvent(capture.TimestampUtc, MovieLifecycleKind.Started);
    }

    private MovieLifecycleEvent? EndActive(
        DateTime timestampUtc,
        MovieLifecycleKind terminalKind)
    {
        var wasActive = openingActive;
        openingPrepared = false;
        openingActive = false;
        return wasActive ? CreateEvent(timestampUtc, terminalKind) : null;
    }

    private MovieLifecycleEvent? ResetAndReject()
    {
        ResetState();
        return null;
    }

    private void ResetState()
    {
        openingPrepared = false;
        openingActive = false;
    }

    private static MovieLifecycleEvent CreateEvent(
        DateTime timestampUtc,
        MovieLifecycleKind kind)
    {
        return new MovieLifecycleEvent(timestampUtc, kind, OpeningMovieKey);
    }

    private static string NormalizeRequiredPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The opening movie path must be fully qualified.", nameof(path));
        }

        return NormalizePath(path);
    }

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            normalizedPath = NormalizePath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
    }
}
