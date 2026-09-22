namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Binds the room-description history to the native save the player is actually playing.
///
/// <para>The history itself only knows how to keep a set of rooms per save file and game
/// slot. Deciding <em>which</em> save that is has to come from the game, and the two events
/// that decide it are a successful Continue and a successful Save. Everything else - a save
/// highlighted and cancelled, a load that failed its checksum, a popup left over from an
/// earlier save - must leave the binding exactly as it was, because binding to the wrong
/// slot silently merges two playthroughs' histories and there is no way for a player to
/// notice, let alone undo it.</para>
///
/// <para><b>Save.</b> The page machine at <c>FUN_006FEDB0</c> goes 1 (games) or 7
/// (confirmation) and then 4 (saving) only on a confirmed Yes, so the selection to remember
/// is whatever the games/confirmation page last showed. Page 4 arms the transaction. What
/// completes it is not the return to page 1 - failure returns there too - but the result
/// popup <c>FUN_006C497C</c> raises afterwards, read by
/// <see cref="NativeSaveResultPopupReader"/>. Only a popup that goes up <em>while armed</em>
/// counts: one that was already up when the transaction armed belongs to an earlier save
/// and is ignored until it has gone away again.</para>
///
/// <para><b>Load.</b> The Continue machine's Games page carries the selection; its Loading
/// page does not (its game number reads zero), so the preceding checked selection is
/// retained for the same file. That selection is only promoted once the game actually
/// reaches a playable module, because a failed checksum returns to page 1 with an error and
/// a cancelled highlight never loads anything at all.</para>
///
/// <para>Until one of those two things happens the history has no slot and deduplicates in
/// memory only, which is what a brand new game gets.</para>
/// </summary>
public sealed class FieldAreaDescriptionSaveTracker
{
    /// <summary>
    /// How many scans an armed save transaction waits for its result popup before the
    /// tracker concludes it missed it. Bounded so a save menu torn down mid-transaction
    /// cannot leave the tracker armed and let a later, unrelated popup bind the slot.
    /// </summary>
    public const int MaximumArmedScansWithoutResult = 240;

    /// <summary>The Continue machine's readiness value once a slot has loaded.</summary>
    public const int LoadedReadiness = 2;

    private readonly FieldAreaDescriptionHistory history;
    private readonly Action<string>? log;

    private int pendingSaveFile;
    private int pendingSaveGame;
    private bool armed;
    private bool awaitingPopupRelease;
    private bool popupWasActive;
    private int armedScans;

    private int pendingLoadFile;
    private int pendingLoadGame;
    private bool loadingObserved;
    private bool loadSucceeded;
    private bool titleSeenSinceGameplay;

    public FieldAreaDescriptionSaveTracker(FieldAreaDescriptionHistory history, Action<string>? log = null)
    {
        this.history = history ?? throw new ArgumentNullException(nameof(history));
        this.log = log;
    }

    /// <summary>What the tracker is currently bound to, for diagnostics and tests.</summary>
    public string LastDiagnostic { get; private set; } = "no native save observed";

    /// <summary>
    /// One scan of the in-game save menu.
    /// </summary>
    /// <param name="snapshot">
    /// The checked save-menu snapshot, or null when the host does not own the save menu or
    /// could not read it. Null never binds anything.
    /// </param>
    /// <param name="popup">
    /// The result popup, or null when it could not be read coherently. A read that failed
    /// is not a save that failed: an armed transaction simply keeps waiting.
    /// </param>
    public void ObserveSaveMenu(SaveMenuStateSnapshot? snapshot, NativeSaveResultPopup? popup)
    {
        if (snapshot is not { } save)
        {
            // The menu is gone. An armed transaction still gets its bounded chance to see
            // the popup, because the popup outlives the menu that raised it.
            if (armed)
            {
                TickArmedTransaction(popup);
            }

            return;
        }

        if (save.Page is SaveMenuPage.Games or SaveMenuPage.Confirmation &&
            IsBindableSlot(save.SaveFileNumber, save.GameNumber))
        {
            pendingSaveFile = save.SaveFileNumber;
            pendingSaveGame = save.GameNumber;
        }

        if (save.Page is not (SaveMenuPage.Games or SaveMenuPage.Confirmation or SaveMenuPage.Saving) &&
            !armed)
        {
            // Backed out to the file list, or the menu moved somewhere this transaction
            // has nothing to do with. The selection is not carried forward.
            pendingSaveFile = 0;
            pendingSaveGame = 0;
        }

        if (save.Page == SaveMenuPage.Saving && !armed &&
            IsBindableSlot(pendingSaveFile, pendingSaveGame) &&
            save.SaveFileNumber == pendingSaveFile)
        {
            armed = true;
            armedScans = 0;

            // A popup that is already up belongs to whatever happened before this
            // transaction. It has to go away before an activation can mean this save.
            awaitingPopupRelease = popup is null || popup.Value.IsActive;
            popupWasActive = popup?.IsActive ?? true;
            LastDiagnostic = $"save armed for {pendingSaveFile}:{pendingSaveGame}";
        }

        if (armed)
        {
            TickArmedTransaction(popup);
        }
    }

    /// <summary>
    /// One scan of the title Continue menu. <paramref name="saveFile"/> and
    /// <paramref name="gameSlot"/> come from its checked Games selection; the Loading page
    /// does not carry a game number of its own, so the last checked selection is retained.
    /// </summary>
    public void ObserveLoadMenu(int? saveFile, int? gameSlot, bool loadingPageActive)
    {
        titleSeenSinceGameplay = true;
        if (saveFile is { } file && gameSlot is { } game && IsBindableSlot(file, game))
        {
            pendingLoadFile = file;
            pendingLoadGame = game;
        }

        if (!loadingPageActive)
        {
            if (loadingObserved && !loadSucceeded)
            {
                // Back off the Loading page without a readiness 2: the checksum failed
                // and the menu returned, or it was cancelled. Nothing is bound.
                ObserveLoadAbandoned();
            }

            return;
        }

        // The Loading page carries its own file but no game number. A selection made
        // against a different file is not this load's.
        if (saveFile is { } loadingFile && loadingFile != pendingLoadFile)
        {
            ObserveLoadAbandoned();
            return;
        }

        if (IsBindableSlot(pendingLoadFile, pendingLoadGame))
        {
            loadingObserved = true;
            LastDiagnostic = $"load pending for {pendingLoadFile}:{pendingLoadGame}";
        }
    }

    /// <summary>
    /// The Continue machine's readiness word. Two is the value it takes only after
    /// FUN_00720080 validated the slot's checksum, so it is the load's own success and
    /// the only thing that lets the pending selection reach gameplay.
    /// </summary>
    public void ObserveLoadReadiness(int readiness)
    {
        if (readiness == LoadedReadiness && loadingObserved)
        {
            loadSucceeded = true;
        }
    }

    /// <summary>
    /// The title menu was left without loading anything - cancelled, or a load that failed
    /// its checksum and went back to the file list. Nothing is bound.
    /// </summary>
    public void ObserveLoadAbandoned()
    {
        if (!loadingObserved && pendingLoadFile == 0)
        {
            return;
        }

        loadingObserved = false;
        loadSucceeded = false;
        pendingLoadFile = 0;
        pendingLoadGame = 0;
        LastDiagnostic = "load abandoned; nothing bound";
    }

    /// <summary>
    /// The game reached a real playable module. A load that was pending is now a load that
    /// happened, and this is the only thing that promotes it.
    /// </summary>
    public void ObserveEnteredPlayableModule()
    {
        var cameFromTitle = titleSeenSinceGameplay;
        titleSeenSinceGameplay = false;
        if (!loadingObserved || !loadSucceeded || !IsBindableSlot(pendingLoadFile, pendingLoadGame))
        {
            if (cameFromTitle)
            {
                // The title was on screen and no load completed: this is a new game.
                // Its rooms deduplicate in memory until it is first saved.
                ObserveNewGame();
            }

            return;
        }

        history.LoadSave(pendingLoadFile, pendingLoadGame);
        LastDiagnostic = $"bound to loaded save {pendingLoadFile}:{pendingLoadGame}";
        log?.Invoke($"Room description history bound to loaded save {pendingLoadFile}:{pendingLoadGame}.");
        loadingObserved = false;
        loadSucceeded = false;
        pendingLoadFile = 0;
        pendingLoadGame = 0;
    }

    /// <summary>
    /// A real new game. Its rooms deduplicate in memory until it is first saved, and it
    /// must never inherit the history of whatever was played before it.
    /// </summary>
    public void ObserveNewGame()
    {
        history.BeginNewGame();
        pendingSaveFile = 0;
        pendingSaveGame = 0;
        pendingLoadFile = 0;
        pendingLoadGame = 0;
        armed = false;
        loadingObserved = false;
        loadSucceeded = false;
        titleSeenSinceGameplay = false;
        LastDiagnostic = "new game; history is unbound";
    }

    private void TickArmedTransaction(NativeSaveResultPopup? popup)
    {
        if (++armedScans > MaximumArmedScansWithoutResult)
        {
            armed = false;
            LastDiagnostic = "save result never observed; nothing bound";
            return;
        }

        if (popup is not { } result)
        {
            // Unreadable. Wait; never guess.
            return;
        }

        if (!result.IsActive)
        {
            awaitingPopupRelease = false;
            popupWasActive = false;
            return;
        }

        if (awaitingPopupRelease || popupWasActive)
        {
            // Still the popup that was already up when this armed, or one we have already
            // seen this transaction. Not a fresh result.
            popupWasActive = true;
            return;
        }

        popupWasActive = true;
        armed = false;
        if (result.IsSuccess)
        {
            history.SaveGame(pendingSaveFile, pendingSaveGame);
            LastDiagnostic = $"bound to saved game {pendingSaveFile}:{pendingSaveGame}";
            log?.Invoke($"Room description history bound to saved game {pendingSaveFile}:{pendingSaveGame}.");
            return;
        }

        LastDiagnostic = result.IsFailure
            ? "save reported failure; nothing bound"
            : "save reported an unknown result; nothing bound";
    }

    private static bool IsBindableSlot(int saveFile, int gameSlot) =>
        saveFile is >= 1 and <= 10 && gameSlot is >= 1 and <= 15;
}
