namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The order both hosts have to observe a scan in, in one place so they cannot drift.
///
/// <para>The order is the whole point. Entering a playable module is what promotes a
/// pending load, and it is also what decides a new game when no load succeeded - so if the
/// module is examined before the Continue machine's readiness word, the first playable
/// scan of a successful load looks exactly like a new game. It clears the pending load,
/// and the readiness that arrives later in the same scan has nothing left to bind. Both
/// hosts had it that way round.</para>
///
/// <para>So: the load's own success first, then the save menu, and only then whether this
/// scan is the first playable one.</para>
/// </summary>
public static class FieldAreaDescriptionSaveObserver
{
    /// <param name="module">The native module byte this scan.</param>
    /// <param name="save">The checked save-menu snapshot, or null when it was not read.</param>
    /// <param name="popup">The save result popup, or null when it could not be read.</param>
    /// <param name="loadReadiness">
    /// The Continue machine's readiness word, or null when it could not be read. Read
    /// directly rather than through the menu reader, which only reports while readiness is
    /// interactive and so can never see the loaded value.
    /// </param>
    /// <param name="load">The title Continue snapshot, or null when it was not readable.</param>
    /// <param name="playableModuleSeen">
    /// Whether the previous scan was already playable, so entry is observed once per
    /// arrival rather than every frame.
    /// </param>
    public static void Observe(
        FieldAreaDescriptionSaveTracker tracker,
        int module,
        SaveMenuStateSnapshot? save,
        NativeSaveResultPopup? popup,
        int? loadReadiness,
        TitleLoadMenuStateSnapshot? load,
        ref bool playableModuleSeen)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (loadReadiness is { } readiness)
        {
            tracker.ObserveLoadReadiness(readiness);
        }

        // Menu buffers can remain valid after leaving the title. Only the
        // native title module owns a new selection or a new-game transition.
        if (module == TitleMenuCursorReader.TitleModule && load is { } menu)
        {
            tracker.ObserveLoadMenu(
                menu.SaveFileNumber > 0 ? menu.SaveFileNumber : null,
                menu.GameNumber > 0 ? menu.GameNumber : null,
                menu.Page == TitleLoadMenuPage.Loading);

            // Back at the file list or the title root: whatever was highlighted was
            // cancelled, or a load failed and the menu returned.
            if (menu.Page is TitleLoadMenuPage.SaveFiles or TitleLoadMenuPage.TitleRoot)
            {
                tracker.ObserveLoadAbandoned();
            }
        }

        tracker.ObserveSaveMenu(save, popup);

        var playable = module == FieldPositionReader.FieldModule ||
            module == WorldMapStateReader.WorldModule;
        if (playable && !playableModuleSeen)
        {
            playableModuleSeen = true;
            tracker.ObserveEnteredPlayableModule();
        }
        else if (!playable)
        {
            playableModuleSeen = false;
        }
    }
}
