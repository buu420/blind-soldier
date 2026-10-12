using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>One caption per observed native display; device failure retains text fallback.</summary>
public sealed class NativeObservedCaptionNarrator : IDisposable
{
    private readonly object sync = new();
    private readonly Func<string, IFieldMovieNarrationOutput?> createOutput;
    private readonly Func<string, bool> speak;
    private readonly Action<string> log;
    private string? currentText;
    private IFieldMovieNarrationOutput? output;
    private bool accepted;
    private bool textOnly;
    private bool suspended;
    private bool disposed;
    private DateTime nextAttemptUtc;

    public NativeObservedCaptionNarrator(Func<string, IFieldMovieNarrationOutput?> createOutput,
        Func<string, bool> speak, Action<string> log)
    {
        this.createOutput = createOutput;
        this.speak = speak;
        this.log = log;
    }
    public void Observe(NativeCaptionObservation? observation, DateTime nowUtc, bool enabled, bool foreground)
    {
        lock (sync)
        {
            if (disposed || suspended) return;
            if (!enabled || !foreground || observation is null || observation.ObservedUtc > nowUtc
                || nowUtc - observation.ObservedUtc > TimeSpan.FromMilliseconds(500))
            {
                Stop("native caption observation unavailable");
                return;
            }
            if (currentText != observation.Text)
            {
                EndEpisode();
                currentText = observation.Text;
            }
            if (output is not null)
            {
                if (output.IsPlaying) return;
                accepted = output is IFieldMovieNarrationCompletion completion && completion.CompletedNormally;
                textOnly = !accepted;
                Stop(accepted ? "native caption recording completed" : "native caption recording failed");
            }
            if (accepted || nowUtc < nextAttemptUtc) return;
            if (!textOnly)
            {
                IFieldMovieNarrationOutput? candidate = null;
                try
                {
                    candidate = createOutput(observation.Text);
                    if (candidate?.Start("native caption renderer observed") == true) { output = candidate; return; }
                }
                catch (Exception ex) { SafeLog("Native caption recording unavailable: " + ex.Message); }
                finally
                {
                    if (candidate is not null && !ReferenceEquals(output, candidate))
                        try { candidate.Dispose(); } catch { }
                }
                textOnly = true;
            }
            try { accepted = speak(observation.Text); }
            catch (Exception ex) { SafeLog("Native caption speech declined: " + ex.Message); }
            nextAttemptUtc = nowUtc.AddMilliseconds(500);
        }
    }
    private void Stop(string reason)
    {
        if (output is null) return;
        try { output.Stop(reason); } catch (Exception ex) { SafeLog("Native caption stop failed: " + ex.Message); }
        try { output.Dispose(); } catch { }
        output = null;
    }
    public void EndEpisode()
    {
        lock (sync) { Stop("native caption ended"); currentText = null; accepted = false; textOnly = false; nextAttemptUtc = DateTime.MinValue; }
    }
    public void Suspend() { lock (sync) { suspended = true; Stop("mod suspended"); } }
    public void Resume() { lock (sync) suspended = false; }
    public void Interrupt(string reason) { lock (sync) Stop(reason); }
    private void SafeLog(string message) { try { log(message); } catch { } }

    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; Stop("native caption unloaded"); } }
}
