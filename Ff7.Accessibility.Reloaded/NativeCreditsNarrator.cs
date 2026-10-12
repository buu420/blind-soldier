using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Describes an observed credits renderer and reads only its current viewport.</summary>
public sealed class NativeCreditsNarrator : IDisposable
{
    public const string Introduction = "Names and production credits scroll upward across the screen.";
    private readonly Func<IFieldMovieNarrationOutput?> createIntroduction;
    private readonly Func<string, bool> speak;
    private readonly Action<string> log;
    private readonly object sync = new();
    private readonly HashSet<int> acceptedRows = [];
    private IFieldMovieNarrationOutput? output;
    private bool introductionHeard;
    private bool textOnly;
    private bool suspended;
    private bool disposed;
    private int previousFirstRow = -1;
    private DateTime nextSpeechUtc;

    public NativeCreditsNarrator(Func<IFieldMovieNarrationOutput?> createIntroduction,
        Func<string, bool> speak, Action<string> log)
    {
        this.createIntroduction = createIntroduction ?? throw new ArgumentNullException(nameof(createIntroduction));
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Observe(NativeCreditsSnapshot? snapshot, DateTime nowUtc, bool enabled, bool foreground)
    {
        lock (sync)
        {
            if (disposed || suspended) return;
            if (!enabled || !foreground || snapshot is null)
            {
                StopOutput("credits observation suspended");
                return;
            }
            if (previousFirstRow > snapshot.FirstRowIndex) ResetEpisode();
            previousFirstRow = snapshot.FirstRowIndex;
            if (snapshot.VisibleRows.Count == 0)
            {
                StopOutput("no credits text is currently visible");
                return;
            }
            if (output is not null)
            {
                if (output.IsPlaying) return;
                introductionHeard = output is IFieldMovieNarrationCompletion completion && completion.CompletedNormally;
                textOnly = !introductionHeard;
                StopOutput(introductionHeard ? "credits introduction completed" : "credits introduction failed");
            }
            if (nowUtc < nextSpeechUtc) return;
            if (!introductionHeard)
            {
                if (!textOnly)
                {
                    IFieldMovieNarrationOutput? candidate = null;
                    try
                    {
                        candidate = createIntroduction();
                        if (candidate is not null)
                        {
                            if (candidate.Start("native staff credits renderer observed"))
                            {
                                output = candidate;
                                return;
                            }
                        }
                    }
                    catch (Exception ex) { SafeLog("Credits recording unavailable: " + ex.Message); }
                    finally
                    {
                        if (candidate is not null && !ReferenceEquals(output, candidate))
                            try { candidate.Dispose(); } catch { }
                    }
                    textOnly = true;
                }
                introductionHeard = TrySpeak(Introduction, nowUtc);
                return;
            }

            // No backlog: a declined row that scrolls offscreen is never spoken later.
            var rows = snapshot.VisibleRows.Where(row => !acceptedRows.Contains(row.Index))
                .OrderBy(row => row.Y).Take(2).ToArray();
            if (rows.Length == 0) return;
            var text = string.Join(". ", rows.Select(row => row.Text));
            if (TrySpeak(text, nowUtc)) foreach (var row in rows) acceptedRows.Add(row.Index);
        }
    }

    private bool TrySpeak(string text, DateTime nowUtc)
    {
        bool accepted;
        try { accepted = speak(text); }
        catch (Exception ex) { SafeLog("Credits speech declined: " + ex.Message); accepted = false; }
        nextSpeechUtc = nowUtc.AddSeconds(accepted ? Math.Max(0.8d, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 3d) : 0.5d);
        return accepted;
    }

    private void StopOutput(string reason)
    {
        if (output is null) return;
        try { output.Stop(reason); } catch (Exception ex) { SafeLog("Credits output stop failed: " + ex.Message); }
        try { output.Dispose(); } catch { }
        output = null;
    }
    private void ResetEpisode()
    {
        StopOutput("staff credits ended or restarted");
        acceptedRows.Clear();
        introductionHeard = false;
        textOnly = false;
        previousFirstRow = -1;
        nextSpeechUtc = DateTime.MinValue;
    }
    public void EndEpisode() { lock (sync) ResetEpisode(); }
    public void Interrupt(string reason) { lock (sync) StopOutput(reason); }
    public void Suspend() { lock (sync) { suspended = true; StopOutput("mod suspended"); } }
    public void Resume() { lock (sync) suspended = false; }
    private void SafeLog(string message) { try { log(message); } catch { } }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; StopOutput("credits narration unloaded"); }
    }
}
