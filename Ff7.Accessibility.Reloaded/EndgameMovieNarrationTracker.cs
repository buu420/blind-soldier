namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Describes the final films from the decoder's clock. The existing short recordings
/// match the reviewed movie sidecars, so attaching late or recovering from a pause
/// describes the current shot without replaying a long recording from its beginning.
/// This owns its output separately from FIELD, which ends before these films start.
/// </summary>
public sealed class EndgameMovieNarrationTracker : IDisposable
{
    private static readonly TimeSpan FrameStallAllowance = TimeSpan.FromMilliseconds(250);
    private readonly Func<string, IReadOnlyList<MovieNarrationCue>> readCues;
    private readonly Func<string, IFieldMovieNarrationOutput?> createOutput;
    private readonly Func<string, bool> speak;
    private readonly Action<string> log;
    private readonly object sync = new();
    private IReadOnlyList<MovieNarrationCue> cues = [];
    private NativeMoviePlaybackSample? previous;
    private string? currentFilm;
    private int nextCue;
    private int outputCue = -1;
    private int textOnlyCue = -1;
    private IFieldMovieNarrationOutput? output;
    private DateTime lastFrameProgressUtc;
    private bool outputPaused;
    private bool suspended;
    private bool disposed;

    public EndgameMovieNarrationTracker(Func<string, IReadOnlyList<MovieNarrationCue>> readCues,
        Func<string, IFieldMovieNarrationOutput?> createOutput, Func<string, bool> speak,
        Action<string> log)
    {
        this.readCues = readCues ?? throw new ArgumentNullException(nameof(readCues));
        this.createOutput = createOutput ?? throw new ArgumentNullException(nameof(createOutput));
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsPlaying { get { lock (sync) return output?.IsPlaying == true; } }

    public static string? ResolveFilm(NativeMoviePlaybackSample sample) =>
        sample.Module == 25 && string.Equals(sample.FilmFileName, "ending2.avi", StringComparison.OrdinalIgnoreCase)
            ? "ending2.avi"
            : sample.Module == 28 && string.Equals(sample.FilmFileName, "ending3.avi", StringComparison.OrdinalIgnoreCase)
                ? "ending3.avi" : null;

    public void Observe(NativeMoviePlaybackSample? sample, DateTime nowUtc,
        bool enabled, bool foreground)
    {
        lock (sync)
        {
            if (disposed || suspended) return;
            if (!enabled || !foreground || sample is null)
            {
                StopOutput("endgame movie observation suspended", retryCurrent: true);
                return;
            }

            var native = sample.Value;
            var film = ResolveFilm(native);
            if (film is null || !native.IsPlaying || !double.IsFinite(native.FramesPerSecond)
                || native.FramesPerSecond is < 1d or > 240d)
            {
                StopOutput("the final film ended or was replaced", retryCurrent: false);
                previous = null;
                currentFilm = null;
                cues = [];
                return;
            }

            var newEpisode = currentFilm != film || previous is not { } before
                || before.Module != native.Module || before.Generation != native.Generation
                || native.Frame < before.Frame;
            if (newEpisode)
            {
                StopOutput("a new final movie episode started", retryCurrent: false);
                currentFilm = film;
                nextCue = 0;
                textOnlyCue = -1;
                lastFrameProgressUtc = nowUtc;
                try { cues = readCues(film); }
                catch (Exception ex)
                {
                    cues = [];
                    SafeLog($"Endgame movie cue schedule unavailable ({film}): {ex.Message}");
                }
                SafeLog($"Endgame movie narration observing {film} in module {native.Module} at {native.PositionSeconds:0.##}s.");
            }
            else if (previous?.Frame != native.Frame)
            {
                lastFrameProgressUtc = nowUtc;
            }

            previous = native;
            var position = native.PositionSeconds;
            var paused = native.IsPaused || nowUtc - lastFrameProgressUtc >= FrameStallAllowance;
            if (output is not null)
            {
                if (outputCue < 0 || outputCue >= cues.Count || position > cues[outputCue].End + 0.75d)
                    StopOutput("the decoder passed this described shot", retryCurrent: false);
                else if (paused != outputPaused)
                {
                    if (output is IFieldMovieNarrationPause pause && pause.SetPaused(paused))
                        outputPaused = paused;
                    else if (paused)
                        StopOutput("the decoder paused", retryCurrent: true);
                }

                if (output is not null && !paused && !output.IsPlaying)
                {
                    var completed = output is IFieldMovieNarrationCompletion completion && completion.CompletedNormally;
                    if (!completed) textOnlyCue = outputCue;
                    StopOutput(completed ? "the recorded cue finished" : "the recorded cue failed", retryCurrent: !completed);
                }
            }

            if (paused || output is not null) return;
            while (nextCue < cues.Count && position > cues[nextCue].End + 0.75d) nextCue++;
            if (nextCue >= cues.Count || position < cues[nextCue].Start) return;

            var cue = cues[nextCue];
            IFieldMovieNarrationOutput? candidate = null;
            try
            {
                candidate = textOnlyCue == nextCue ? null : createOutput(cue.Text);
                if (candidate?.Start($"{film} cue at native {position:0.##}s") == true)
                {
                    output = candidate;
                    outputCue = nextCue++;
                    outputPaused = false;
                    return;
                }
            }
            catch (Exception ex) { SafeLog($"Endgame recorded cue unavailable: {ex.Message}"); }
            try { candidate?.Dispose(); } catch (Exception) { }
            // A missing recording or failed device still receives its reviewed text.
            // Actual backend acceptance is the commit; refusal keeps the current cue.
            try { if (speak(cue.Text)) nextCue++; }
            catch (Exception ex) { SafeLog($"Endgame spoken cue declined: {ex.Message}"); }
        }
    }

    private void StopOutput(string reason, bool retryCurrent)
    {
        if (output is null) return;
        if (retryCurrent && outputCue >= 0) nextCue = Math.Min(nextCue, outputCue);
        try { output.Stop(reason); } catch (Exception ex) { SafeLog($"Endgame cue stop failed: {ex.Message}"); }
        try { output.Dispose(); } catch (Exception) { }
        output = null;
        outputCue = -1;
        outputPaused = false;
    }

    public void Suspend()
    {
        lock (sync)
        {
            suspended = true;
            StopOutput("mod suspended", retryCurrent: true);
        }
    }

    public void Resume() { lock (sync) suspended = false; }

    public void Interrupt(string reason)
    {
        lock (sync) StopOutput(reason, retryCurrent: true);
    }

    private void SafeLog(string message) { try { log(message); } catch { } }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            StopOutput("endgame narration unloaded", retryCurrent: false);
        }
    }
}
