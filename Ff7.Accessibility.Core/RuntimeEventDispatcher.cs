using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Core;

public sealed class RuntimeEventDispatcher
{
    private const float MaximumGain = 4.0f;

    private readonly AccessibilityConfig config;
    private readonly IAccessibilityOutput output;
    private readonly Action<string> log;
    private readonly HashSet<AccessibilityCueKind> activeCues = [];
    private string? lastMenuSelectionKey;
    private string? lastDialoguePageKey;
    private string? lastDialogueChoiceKey;

    public RuntimeEventDispatcher(
        AccessibilityConfig config,
        IAccessibilityOutput output,
        Action<string> log)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public RuntimeCapability HandledCapabilities =>
        RuntimeCapability.Lifecycle |
        RuntimeCapability.Menus |
        RuntimeCapability.Dialogue |
        RuntimeCapability.Movies;

    public void Dispatch(RuntimeDispatchBatch batch, DateTime utcNow)
    {
        _ = DispatchWithDialogueAcknowledgement(batch, utcNow);
    }

    public DialoguePageObservation? DispatchWithDialogueAcknowledgement(
        RuntimeDispatchBatch batch,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _ = utcNow;
        DialoguePageObservation? dialogueAcknowledgement = null;

        if (batch.Frame is not null)
        {
            try
            {
                dialogueAcknowledgement = DispatchFrame(batch.Frame);
            }
            catch (Exception ex)
            {
                log($"Runtime frame dispatch failed: {ex.Message}");
                throw;
            }
        }

        foreach (var runtimeEvent in batch.Events)
        {
            try
            {
                DispatchEvent(runtimeEvent);
            }
            catch (Exception ex)
            {
                log($"Runtime event dispatch failed for {runtimeEvent.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        return dialogueAcknowledgement;
    }

    public bool Cleanup(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        foreach (var kind in activeCues.ToArray())
        {
            try
            {
                output.StopCue(kind);
                activeCues.Remove(kind);
            }
            catch (Exception ex)
            {
                log(
                    $"Accessibility cue cleanup failed for {kind} {context}; " +
                    $"the cue remains active for a bounded retry: {ex.Message}");
            }
        }

        return activeCues.Count == 0;
    }

    private void DispatchEvent(RuntimeEvent runtimeEvent)
    {
        if (runtimeEvent is not MovieLifecycleEvent movie)
        {
            return;
        }

        if (!string.Equals(movie.NativeMovieKey, "opening", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (movie.Kind == MovieLifecycleKind.Started)
        {
            if (!config.EnableOpeningMovieAudioTrack
                || string.IsNullOrWhiteSpace(config.OpeningMovieAudioTrackPath))
            {
                return;
            }

            output.PlayCue(new AccessibilityCue(
                AccessibilityCueKind.MovieNarration,
                config.OpeningMovieAudioTrackPath,
                Math.Clamp(config.OpeningMovieAudioTrackVolumePercent / 100.0f, 0.0f, MaximumGain),
                0.0f,
                false));
            activeCues.Add(AccessibilityCueKind.MovieNarration);
            return;
        }

        output.StopCue(AccessibilityCueKind.MovieNarration);
        activeCues.Remove(AccessibilityCueKind.MovieNarration);
    }

    private DialoguePageObservation? DispatchFrame(RuntimeFrameObservation frame)
    {
        if (!frame.Lifecycle.IsForeground || frame.Lifecycle.IsShuttingDown)
        {
            ResetTextualState();
            return null;
        }

        DispatchMenu(frame.Menu);
        return DispatchDialogue(frame.Dialogue);
    }

    private void DispatchMenu(RuntimeDomainUpdate<MenuFrameObservation> update)
    {
        if (update.Kind == RuntimeDomainUpdateKind.Unchanged)
        {
            return;
        }

        if (update.Kind == RuntimeDomainUpdateKind.Closed ||
            update.Value is not { IsOpen: true } menu ||
            !config.EnableSpeech ||
            !config.EnableRuntimeMenuSpeech ||
            string.IsNullOrWhiteSpace(menu.Screen) ||
            menu.Rows.Length is 0 or > 512)
        {
            lastMenuSelectionKey = null;
            return;
        }

        var seenIndices = new HashSet<int>();
        MenuRowObservation? selected = null;
        foreach (var row in menu.Rows)
        {
            if (row.Index < 0 || !seenIndices.Add(row.Index))
            {
                lastMenuSelectionKey = null;
                return;
            }

            if (!row.Selected)
            {
                continue;
            }

            if (selected is not null || string.IsNullOrWhiteSpace(row.Text))
            {
                lastMenuSelectionKey = null;
                return;
            }

            selected = row;
        }

        if (selected is null)
        {
            lastMenuSelectionKey = null;
            return;
        }

        var selection = selected;
        var key = string.Join('\u001f', menu.Screen, selection.Index, selection.Text, selection.Enabled);
        if (string.Equals(key, lastMenuSelectionKey, StringComparison.Ordinal))
        {
            return;
        }

        var speech = selection.Enabled ? selection.Text : $"{selection.Text} unavailable";
        output.Speak(speech, interrupt: true);
        lastMenuSelectionKey = key;
    }

    private DialoguePageObservation? DispatchDialogue(
        RuntimeDomainUpdate<DialoguePageObservation> update)
    {
        if (update.Kind == RuntimeDomainUpdateKind.Unchanged)
        {
            return null;
        }

        if (update.Kind == RuntimeDomainUpdateKind.Closed ||
            update.Value is not { IsOpen: true } page ||
            !config.EnableSpeech ||
            !config.EnableRuntimeDialogueSpeech ||
            page.WindowId < 0 ||
            page.PageRevision < 0 ||
            page.Choices.Length > 64)
        {
            ResetDialogueState();
            return null;
        }

        var pageKey = string.Join(
            '\u001f',
            page.WindowId,
            page.PageRevision,
            page.Speaker,
            page.VisibleText);
        var hasPageSpeech =
            !string.IsNullOrWhiteSpace(page.Speaker) ||
            !string.IsNullOrWhiteSpace(page.VisibleText);
        var pageWasDelivered =
            hasPageSpeech &&
            string.Equals(pageKey, lastDialoguePageKey, StringComparison.Ordinal);
        if (!string.Equals(pageKey, lastDialoguePageKey, StringComparison.Ordinal))
        {
            var hasSpeaker = !string.IsNullOrWhiteSpace(page.Speaker);
            if (hasSpeaker)
            {
                output.Speak(page.Speaker, interrupt: true);
            }

            if (!string.IsNullOrWhiteSpace(page.VisibleText))
            {
                output.Speak(page.VisibleText, interrupt: !hasSpeaker);
            }

            lastDialoguePageKey = pageKey;
            pageWasDelivered = hasPageSpeech;
        }

        var seenIndices = new HashSet<int>();
        DialogueChoiceObservation? selected = null;
        foreach (var choice in page.Choices)
        {
            if (choice.Index < 0 || !seenIndices.Add(choice.Index))
            {
                lastDialogueChoiceKey = null;
                return pageWasDelivered ? page : null;
            }

            if (!choice.Selected)
            {
                continue;
            }

            if (selected is not null || string.IsNullOrWhiteSpace(choice.Text))
            {
                lastDialogueChoiceKey = null;
                return pageWasDelivered ? page : null;
            }

            selected = choice;
        }

        if (selected is null)
        {
            lastDialogueChoiceKey = null;
            return pageWasDelivered ? page : null;
        }

        var selection = selected;
        var choiceKey = string.Join(
            '\u001f',
            page.WindowId,
            page.PageRevision,
            selection.Index,
            selection.Text,
            selection.Enabled);
        if (string.Equals(choiceKey, lastDialogueChoiceKey, StringComparison.Ordinal))
        {
            return page;
        }

        var speech = selection.Enabled ? selection.Text : $"{selection.Text} unavailable";
        output.Speak(speech, interrupt: true);
        lastDialogueChoiceKey = choiceKey;
        return page;
    }

    private void ResetTextualState()
    {
        lastMenuSelectionKey = null;
        ResetDialogueState();
    }

    private void ResetDialogueState()
    {
        lastDialoguePageKey = null;
        lastDialogueChoiceKey = null;
    }
}
