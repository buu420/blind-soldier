namespace Ff7.Accessibility.Reloaded;

public sealed class TitleLoadMenuSpeechTracker
{
    public const int SaveFileWidgetAddress = 0x00DD6D98;
    public const int TitleRootWidgetAddress = 0x00DD6F20;

    private readonly object sync = new();
    private readonly TimeSpan settleTime;
    private readonly Func<int, bool?> saveFileHasData;
    private readonly Func<int, int, Ff7SaveSlotPreview?> readGame;
    private LoadScreen screen;
    private int selectedSaveFile = 1;
    private int observedSaveFile = -1;
    private int observedGame = -1;
    private bool gameHeaderArmed;
    private DateTime gameHeaderSeenAt;
    private bool announceScreen;
    private string observedKey = string.Empty;
    private string lastSpokenKey = string.Empty;
    private PendingSpeech? pending;

    public TitleLoadMenuSpeechTracker(
        TimeSpan settleTime,
        Func<int, bool?> saveFileHasData,
        Func<int, int, Ff7SaveSlotPreview?> readGame)
    {
        this.settleTime = settleTime;
        this.saveFileHasData = saveFileHasData;
        this.readGame = readGame;
    }

    public bool IsActive
    {
        get
        {
            lock (sync)
            {
                return screen != LoadScreen.None;
            }
        }
    }

    public void ObserveWidget(ActiveMenuWidgetSnapshot widget, int currentModule, DateTime now)
    {
        lock (sync)
        {
            if (currentModule != TitleMenuCursorReader.TitleModule)
            {
                ResetCore();
                return;
            }

            if (widget.Address == TitleRootWidgetAddress)
            {
                ResetCore();
                return;
            }

            if (widget.Address != SaveFileWidgetAddress ||
                !IsVerifiedSaveFileGrid(widget) ||
                widget.First is < 0 || widget.First >= widget.Columns ||
                widget.Cursor is < 0 || widget.Cursor >= widget.Rows)
            {
                return;
            }

            var entering = screen != LoadScreen.SaveFiles;
            screen = LoadScreen.SaveFiles;
            selectedSaveFile = widget.Cursor * widget.Columns + widget.First + 1;
            if (!entering && selectedSaveFile == observedSaveFile)
            {
                return;
            }

            var hasData = saveFileHasData(selectedSaveFile);
            if (!hasData.HasValue)
            {
                pending = null;
                return;
            }

            observedSaveFile = selectedSaveFile;
            var key = $"save-file:{selectedSaveFile}:{hasData}";
            if (!entering && string.Equals(key, observedKey, StringComparison.Ordinal))
            {
                return;
            }

            observedKey = key;
            var label = hasData.Value
                ? $"Save {selectedSaveFile}."
                : $"Save {selectedSaveFile}, empty.";
            Queue(entering ? $"Select a save data file. {label}" : label, key, now);
        }
    }

    private static bool IsVerifiedSaveFileGrid(ActiveMenuWidgetSnapshot widget) =>
        (widget.Columns == 5 && widget.Rows == 2) ||
        (widget.Columns == 2 && widget.Rows == 5);

    public void ObserveDraw(MenuTextRenderEntry entry, int currentModule, DateTime now)
    {
        lock (sync)
        {
            if (currentModule != TitleMenuCursorReader.TitleModule)
            {
                ResetCore();
                return;
            }

            var text = entry.Text.Trim();
            if (IsGameSelectionPrompt(entry, text))
            {
                if (screen is not (LoadScreen.SaveFiles or LoadScreen.Games))
                {
                    return;
                }

                if (screen != LoadScreen.Games)
                {
                    screen = LoadScreen.Games;
                    announceScreen = true;
                    observedGame = -1;
                    observedKey = string.Empty;
                    pending = null;
                }

                return;
            }

            if (screen != LoadScreen.Games)
            {
                return;
            }

            if (IsGameHeader(entry, text))
            {
                gameHeaderArmed = true;
                gameHeaderSeenAt = now;
                return;
            }

            if (!gameHeaderArmed || now - gameHeaderSeenAt > TimeSpan.FromMilliseconds(100) ||
                !int.TryParse(text, out var gameNumber) ||
                gameNumber is < 1 or > Ff7PcSaveFileReader.SlotsPerFile)
            {
                return;
            }

            gameHeaderArmed = false;
            var preview = readGame(selectedSaveFile, gameNumber);
            if (preview is null)
            {
                pending = null;
                return;
            }

            var key = CreateGameKey(selectedSaveFile, gameNumber, preview);
            if (gameNumber == observedGame && string.Equals(key, observedKey, StringComparison.Ordinal))
            {
                return;
            }

            observedGame = gameNumber;
            observedKey = key;
            var speech = SaveSlotSpeechFormatter.FormatGame(gameNumber, preview);
            if (speech is null)
            {
                return;
            }
            if (announceScreen)
            {
                announceScreen = false;
                speech = $"Select a save game. {speech}";
            }

            Queue(speech, key, now);
        }
    }

    public void ObserveState(
        TitleLoadMenuStateSnapshot snapshot,
        int currentModule,
        DateTime now)
    {
        lock (sync)
        {
            if (currentModule != TitleMenuCursorReader.TitleModule ||
                now.Kind != DateTimeKind.Utc)
            {
                ResetCore();
                return;
            }

            switch (snapshot.Page)
            {
                case TitleLoadMenuPage.SaveFiles
                    when snapshot.SaveFileNumber is >= 1 and <= 10:
                {
                    var entering = screen != LoadScreen.SaveFiles;
                    screen = LoadScreen.SaveFiles;
                    selectedSaveFile = snapshot.SaveFileNumber;
                    observedSaveFile = selectedSaveFile;
                    observedGame = -1;
                    gameHeaderArmed = false;
                    var key = $"save-file:{selectedSaveFile}:{snapshot.SaveFileHasData}";
                    if (!entering && string.Equals(key, observedKey, StringComparison.Ordinal))
                    {
                        return;
                    }

                    observedKey = key;
                    var label = snapshot.SaveFileHasData
                        ? $"Save {selectedSaveFile}."
                        : $"Save {selectedSaveFile}, empty.";
                    Queue(entering ? $"Select a save data file. {label}" : label, key, now);
                    return;
                }

                case TitleLoadMenuPage.Games
                    when snapshot.SaveFileNumber is >= 1 and <= 10 &&
                        snapshot.GameNumber is >= 1 and <= Ff7PcSaveFileReader.SlotsPerFile:
                {
                    var entering = screen != LoadScreen.Games;
                    screen = LoadScreen.Games;
                    selectedSaveFile = snapshot.SaveFileNumber;
                    observedGame = snapshot.GameNumber;
                    gameHeaderArmed = false;
                    var key = CreateGameKey(
                        snapshot.SaveFileNumber,
                        snapshot.GameNumber,
                        snapshot.Preview);
                    if (!entering && string.Equals(key, observedKey, StringComparison.Ordinal))
                    {
                        return;
                    }

                    observedKey = key;
                    var speech = SaveSlotSpeechFormatter.FormatGame(
                        snapshot.GameNumber,
                        snapshot.Preview);
                    if (speech is null)
                    {
                        pending = null;
                        return;
                    }
                    Queue(entering ? $"Select a save game. {speech}" : speech, key, now);
                    return;
                }

                case TitleLoadMenuPage.Checking:
                    ObserveStatus(LoadScreen.Checking, "checking", "Checking save data.", now);
                    return;

                case TitleLoadMenuPage.Loading:
                    ObserveStatus(LoadScreen.Loading, "loading", "Loading.", now);
                    return;

                case TitleLoadMenuPage.TitleRoot:
                    ResetCore();
                    return;

                default:
                    pending = null;
                    return;
            }
        }
    }

    public void ObserveModule(int currentModule)
    {
        if (currentModule == TitleMenuCursorReader.TitleModule)
        {
            return;
        }

        lock (sync)
        {
            ResetCore();
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            if (pending is not { } candidate || now - candidate.SeenAt < settleTime)
            {
                return null;
            }

            pending = null;
            if (string.Equals(candidate.Key, lastSpokenKey, StringComparison.Ordinal))
            {
                return null;
            }

            lastSpokenKey = candidate.Key;
            return candidate.Text;
        }
    }

    private void Queue(string text, string key, DateTime now)
    {
        pending = new PendingSpeech(text, key, now);
    }

    private void ObserveStatus(LoadScreen nextScreen, string key, string text, DateTime now)
    {
        var entering = screen != nextScreen;
        screen = nextScreen;
        if (!entering && string.Equals(key, observedKey, StringComparison.Ordinal))
        {
            return;
        }

        observedKey = key;
        Queue(text, key, now);
    }

    private void ResetCore()
    {
        screen = LoadScreen.None;
        selectedSaveFile = 1;
        observedSaveFile = -1;
        observedGame = -1;
        gameHeaderArmed = false;
        announceScreen = false;
        observedKey = string.Empty;
        lastSpokenKey = string.Empty;
        pending = null;
    }

    private static string CreateGameKey(int saveFile, int game, Ff7SaveSlotPreview? preview) =>
        preview is not { } value
            ? $"game:{saveFile}:{game}:missing"
            : $"game:{saveFile}:{game}:{value.IsEmpty}:{value.Level}:{value.PlaySeconds}:{value.Gil}:" +
                $"{value.LeadCharacterName}:{value.Location}";

    private static bool IsGameSelectionPrompt(MenuTextRenderEntry entry, string text) =>
        entry.X <= 32 &&
        entry.Y <= 32 &&
        text.Length >= 4 &&
        text.Any(char.IsLetterOrDigit);

    private static bool IsGameHeader(MenuTextRenderEntry entry, string text) =>
        entry.X is >= 320 and <= 380 &&
        entry.Y <= 32 &&
        text.Any(char.IsLetter);

    private enum LoadScreen
    {
        None,
        SaveFiles,
        Games,
        Checking,
        Loading
    }

    private readonly record struct PendingSpeech(string Text, string Key, DateTime SeenAt);
}
