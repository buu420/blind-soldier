using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.NameEntry;

/// <summary>
/// Converts checked, pointer-free Steam 2026 name-editor snapshots through the
/// same native state tracker used by the x86 runtime.
/// </summary>
internal sealed class Steam2026NameEntrySpeechCoordinator
{
    private readonly bool enabled;
    private readonly NameEntryNativeNameTracker tracker;
    private readonly Action<string, bool> speak;
    private readonly Action<string> log;
    private string? pendingSpeech;
    private string? pendingDiagnostic;

    internal Steam2026NameEntrySpeechCoordinator(
        bool enabled,
        TimeSpan initialAnnouncementDelay,
        Action<string, bool> speak,
        Action<string> log)
    {
        this.enabled = enabled;
        tracker = new NameEntryNativeNameTracker(initialAnnouncementDelay);
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal void Observe(
        NameEntryStateSnapshot? snapshot,
        bool isHostForeground,
        DateTime nowUtc)
    {
        if (!enabled || !isHostForeground || snapshot is null)
        {
            Reset();
            return;
        }

        TrySpeakPending();
        var speech = tracker.Observe(
            snapshot.IsActive,
            snapshot.Focus,
            snapshot.GridColumn,
            snapshot.GridRow,
            snapshot.CommandRow,
            snapshot.SelectedSlot,
            snapshot.NameBuffer,
            nowUtc);
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        pendingSpeech = speech;
        pendingDiagnostic =
            $"Native Steam 2026 Name entry native speech: focus={snapshot.Focus} " +
            $"grid={snapshot.GridColumn},{snapshot.GridRow} " +
            $"command={snapshot.CommandRow} slot={snapshot.SelectedSlot} text={speech}";
        TrySpeakPending();
    }

    internal void Reset()
    {
        pendingSpeech = null;
        pendingDiagnostic = null;
        tracker.Reset();
    }

    private void TrySpeakPending()
    {
        if (string.IsNullOrWhiteSpace(pendingSpeech))
        {
            return;
        }

        var speech = pendingSpeech;
        var diagnostic = pendingDiagnostic;
        speak(speech, true);
        pendingSpeech = null;
        pendingDiagnostic = null;
        if (!string.IsNullOrWhiteSpace(diagnostic))
        {
            log(diagnostic);
        }
    }
}
