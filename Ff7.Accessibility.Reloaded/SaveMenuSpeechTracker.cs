namespace Ff7.Accessibility.Reloaded;

public sealed class SaveMenuSpeechTracker
{
    public const int InGameMenuModule = 5;
    public const int RootMainMenuWidgetAddress = 0x00DC1150;

    private readonly TimeSpan settleTime;
    private readonly object sync = new();
    private bool isActive;
    private SaveMenuPage? observedPage;
    private int pageVisit;
    private string observedKey = string.Empty;
    private string lastSpokenKey = string.Empty;
    private PendingSpeech? pending;
    private long nextSpeechId;

    public SaveMenuSpeechTracker(TimeSpan settleTime)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
    }

    public bool IsActive
    {
        get
        {
            lock (sync)
            {
                return isActive;
            }
        }
    }

    public void ObserveModule(int currentModule)
    {
        if (currentModule == InGameMenuModule)
        {
            return;
        }

        Reset();
    }

    public void ObserveWidget(ActiveMenuWidgetSnapshot widget)
    {
        lock (sync)
        {
            ObserveWidgetCore(widget);
        }
    }

    /// <summary>
    /// Reconciles host ownership with one optional checked active-widget
    /// observation. The exact native Save-file widget is stronger evidence
    /// than the legacy name-entry mode word, which can remain ambiguous while
    /// the Save transaction is visibly active. Once acquired, only exact root,
    /// module, or foreground evidence revokes the Save flow.
    /// </summary>
    public void ObserveHostState(
        int currentModule,
        bool isForeground,
        bool isNameEntryActive,
        ActiveMenuWidgetSnapshot? observedWidget = null)
    {
        lock (sync)
        {
            if (currentModule != InGameMenuModule || !isForeground)
            {
                ResetCore();
                isActive = false;
                return;
            }

            if (observedWidget is { } widget)
            {
                ObserveWidgetCore(widget);
            }

            if (isNameEntryActive && !isActive)
            {
                ResetCore();
                isActive = false;
            }
        }
    }

    public void Observe(SaveMenuStateSnapshot snapshot, DateTime now)
    {
        lock (sync)
        {
            if (!isActive || now.Kind != DateTimeKind.Utc)
            {
                return;
            }

            var enteringPage = observedPage != snapshot.Page;
            if (enteringPage)
            {
                observedPage = snapshot.Page;
                pageVisit++;
                observedKey = string.Empty;
            }

            string text;
            string selectionKey;
            switch (snapshot.Page)
            {
                case SaveMenuPage.SaveFiles when snapshot.SaveFileNumber is >= 1 and <= 10:
                    text = $"Save {snapshot.SaveFileNumber}.";
                    selectionKey = $"file:{snapshot.SaveFileNumber}";
                    break;

                case SaveMenuPage.Games when snapshot.GameNumber is >= 1 and <= Ff7PcSaveFileReader.SlotsPerFile:
                    text = SaveSlotSpeechFormatter.FormatGame(snapshot.GameNumber, snapshot.Preview) ?? string.Empty;
                    selectionKey = $"game:{snapshot.SaveFileNumber}:{snapshot.GameNumber}:{CreatePreviewKey(snapshot.Preview)}";
                    break;

                case SaveMenuPage.Checking:
                    text = "Checking save data.";
                    selectionKey = "checking";
                    break;

                case SaveMenuPage.Saving:
                    text = "Saving.";
                    selectionKey = "saving";
                    break;

                case SaveMenuPage.Confirmation
                    when snapshot.GameNumber is >= 1 and <= Ff7PcSaveFileReader.SlotsPerFile &&
                        snapshot.ConfirmationCursor is 0 or 1:
                    var choice = snapshot.ConfirmationCursor == 0 ? "Yes" : "No";
                    text = enteringPage
                        ? $"Are you sure? {choice}."
                        : $"{choice}.";
                    selectionKey = $"confirm:{snapshot.SaveFileNumber}:{snapshot.GameNumber}:{snapshot.ConfirmationCursor}";
                    break;

                default:
                    pending = null;
                    return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                pending = null;
                return;
            }

            var key = $"{pageVisit}:{snapshot.Page}:{selectionKey}";
            if (string.Equals(key, observedKey, StringComparison.Ordinal))
            {
                return;
            }

            observedKey = key;
            pending = new PendingSpeech(++nextSpeechId, text, key, now);
        }
    }

    public SaveMenuPendingSpeech? Peek(DateTime now)
    {
        lock (sync)
        {
            if (!isActive || pending is not { } candidate || now - candidate.SeenAt < settleTime)
            {
                return null;
            }

            if (string.Equals(candidate.Key, lastSpokenKey, StringComparison.Ordinal))
            {
                pending = null;
                return null;
            }

            return new SaveMenuPendingSpeech(candidate.Id, candidate.Text);
        }
    }

    public bool Acknowledge(long id)
    {
        lock (sync)
        {
            if (pending is not { } candidate || candidate.Id != id)
            {
                return false;
            }

            lastSpokenKey = candidate.Key;
            pending = null;
            return true;
        }
    }

    public string? Poll(DateTime now)
    {
        var speech = Peek(now);
        if (speech is not { } candidate || !Acknowledge(candidate.Id))
        {
            return null;
        }

        return candidate.Text;
    }

    public void Reset()
    {
        lock (sync)
        {
            ResetCore();
            isActive = false;
        }
    }

    private void ResetCore()
    {
        observedPage = null;
        pageVisit = 0;
        observedKey = string.Empty;
        lastSpokenKey = string.Empty;
        pending = null;
    }

    private void ObservePageSignal(SaveMenuPage page)
    {
        if (observedPage == page)
        {
            return;
        }

        observedPage = page;
        pageVisit++;
        observedKey = string.Empty;
        pending = null;
    }

    private void ObserveWidgetCore(ActiveMenuWidgetSnapshot widget)
    {
        if (widget.Address == SaveMenuStateReader.AddressSaveFileWidget &&
            widget.Columns == 5 && widget.Rows == 2 &&
            widget.First is >= 0 and < 5 && widget.Cursor is >= 0 and < 2)
        {
            if (!isActive)
            {
                ResetCore();
                isActive = true;
            }

            ObservePageSignal(SaveMenuPage.SaveFiles);
            return;
        }

        if (isActive && widget.Address == SaveMenuStateReader.AddressGameWidget &&
            widget.Columns == 1 && widget.Rows is 3 or 4 &&
            widget.Cursor is >= 0 and <= 3)
        {
            ObservePageSignal(SaveMenuPage.Games);
            return;
        }

        if (isActive && widget.Address == SaveMenuStateReader.AddressConfirmationWidget &&
            widget.Columns == 1 && widget.Rows == 2 &&
            widget.Cursor is 0 or 1)
        {
            ObservePageSignal(SaveMenuPage.Confirmation);
            return;
        }

        if (widget.Address == RootMainMenuWidgetAddress ||
            widget.Kind == MenuWidgetKind.RootMainMenu)
        {
            ResetCore();
            isActive = false;
        }
    }

    private static string CreatePreviewKey(Ff7SaveSlotPreview? preview) => preview is not { } value
        ? "unavailable"
        : $"{value.IsEmpty}:{value.Level}:{value.CurrentHp}:{value.MaxHp}:{value.CurrentMp}:" +
            $"{value.MaxMp}:{value.Gil}:{value.PlaySeconds}:{value.LeadCharacterName}:{value.Location}";

    private readonly record struct PendingSpeech(long Id, string Text, string Key, DateTime SeenAt);
}

public readonly record struct SaveMenuPendingSpeech(long Id, string Text);
