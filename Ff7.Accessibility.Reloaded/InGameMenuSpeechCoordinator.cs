namespace Ff7.Accessibility.Reloaded;

public sealed class InGameMenuSpeechCoordinator
{
    private const int RootMainMenuContext = 0x3A83126F;
    private const int ConfigHelpContext = 0x3DCCCCCD;
    private const int ItemMenuContext = 0x3DCED917;
    private const int FieldStatusContext = 0x3E99999A;
    private const int ClockGilContext = 0x3E4CCCCD;
    private const int DialogPromptContext = 0x3C23D70A;
    private static readonly TimeSpan RecentTextWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan WidgetSelectionTextWindow = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CurrentHelpTextWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan InventoryDescriptionRefreshGrace = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RecentCursorWindow = TimeSpan.FromMilliseconds(600);
    private const int CursorVerticalTolerance = 18;
    private const int CursorHorizontalOverlapTolerance = 16;
    private const int CursorHorizontalLeadMax = 160;
    // These two visible labels share the character-name rectangle in the
    // legacy Status layout. There is no separate native row identity for the
    // renderer fallback, so use the exact shipped-language semantic set while
    // the authoritative savemap-backed character selection remains preferred.
    private static readonly HashSet<string> NonCharacterStatusLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "next level",
        "Limit level",
        "Niveau suivant",
        "Niveau de limite",
        "Nächste Stufe",
        "Limit-Stufe",
        "Siguiente nivel",
        "Nivel de límite",
        "次のレベルまで",
        "リミットレベル"
    };
    private readonly TimeSpan settleTime;
    private readonly Func<string, string?>? resolveDescriptionByName;
    private readonly object sync = new();
    private readonly List<ObservedText> recentText = new();
    private readonly List<ObservedCursor> recentCursors = new();
    private readonly Dictionary<string, ObservedWidgetState> lastStateByWidget = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeferredWidgetState> deferredWidgetStates = new(StringComparer.Ordinal);
    private ObservedCursor? lastCursor;
    private MenuWidgetState? deferredInventoryState;
    private MenuWidgetState? deferredMagicSpellState;
    private MenuWidgetState? latestMagicSpellState;
    private Candidate? pending;
    private string lastSpokenKey = string.Empty;

    public InGameMenuSpeechCoordinator(TimeSpan settleTime, Func<string, string?>? resolveDescriptionByName = null)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
        this.resolveDescriptionByName = resolveDescriptionByName;
    }

    public void ObserveDraw(MenuTextRenderEntry entry, DateTime now)
    {
        if (!LooksLikeSpeechCandidate(entry.Text))
        {
            return;
        }

        lock (sync)
        {
            AddRecent(entry, now);
            if (IsMagicSpellGridText(entry) && latestMagicSpellState is { } magicState)
            {
                deferredMagicSpellState = magicState;
            }

            if (IsRootMainMenu(entry))
            {
                return;
            }

            if (IsTitleMenuChoiceText(entry))
            {
                return;
            }

            if (IsHelpText(entry))
            {
                QueueCandidate(entry.Text, now, CandidatePriority.HelpText, $"help\u001f{entry.Text}");
            }

            TryQueueConfigMainCursorSelection(entry, now);

            if (IsHighlightedChoice(entry))
            {
                QueueCandidate(entry.Text, now, CandidatePriority.HighlightedChoice, $"highlight\u001f{entry.Context}\u001f{entry.Text}");
            }

            TryQueueDeferredInventory(now);
            TryQueueDeferredMagicSpell(now);
            TryQueueDeferredWidgets(now);
        }
    }

    public void ObserveCursor(MenuCursorDrawObservation observation, DateTime now)
    {
        lock (sync)
        {
            lastCursor = new ObservedCursor(observation, now);
            recentCursors.Add(lastCursor.Value);
            PruneRecent(now);
            TryQueueDeferredMagicSpell(now);
        }
    }

    public void ObserveWidget(MenuWidgetState state, DateTime now)
    {
        if (state.Cursor < 0 ||
            state.Columns <= 0 ||
            state.Rows <= 0 ||
            state.Cursor >= state.Columns * state.Rows)
        {
            return;
        }

        lock (sync)
        {
            var hasLastState = lastStateByWidget.TryGetValue(state.Name, out var lastState);
            if (IsMagicSpellGridProbe(state))
            {
                latestMagicSpellState = state;
            }

            var nativeInventoryKey = CreateNativeInventoryKey(state.InventoryItem);
            var nativeSelectionKey = state.NativeSelection?.Key ?? string.Empty;
            var selectionChanged =
                !hasLastState ||
                lastState.Cursor != state.Cursor ||
                ((UsesFirstAsSelection(state) || IsMagicSpellGridProbe(state)) && lastState.First != state.First) ||
                !string.Equals(lastState.NativeInventoryKey, nativeInventoryKey, StringComparison.Ordinal) ||
                !string.Equals(lastState.NativeSelectionKey, nativeSelectionKey, StringComparison.Ordinal);
            var listScrollSettled = (IsInventoryListProbe(state) || IsMagicSpellGridProbe(state)) &&
                hasLastState &&
                lastState.Cursor == state.Cursor &&
                lastState.F14 != 0 &&
                state.F14 == 0;
            lastStateByWidget[state.Name] = new ObservedWidgetState(state.Cursor, state.First, state.F14, nativeInventoryKey, nativeSelectionKey);
            if (!selectionChanged && !listScrollSettled)
            {
                return;
            }

            PruneRecent(now);
            if (IsRootMainMenuProbe(state))
            {
                return;
            }

            if (IsIgnoredWidgetProbe(state, now))
            {
                return;
            }

            string? text;
            InventoryItemSnapshot? selectedInventoryItem = null;
            NativeMenuSelection? selectedNativeSelection = null;
            var usedInventorySelection = false;
            var usedMagicSpellSelection = false;
            if (state.NativeSelection is { } nativeSelection)
            {
                deferredInventoryState = null;
                text = FormatCharacterSelection(state, nativeSelection.Text, now);
                selectedNativeSelection = nativeSelection;
                deferredWidgetStates.Remove(state.Name);
            }
            else if (IsInventoryListProbe(state) && IsInventoryContext(state, now))
            {
                usedInventorySelection = true;
                var selection = FindInventorySelection(state, now, out var shouldRetry);
                if (selection is null)
                {
                    deferredInventoryState = shouldRetry ? state : null;
                    if (!shouldRetry)
                    {
                        ClearSilentSelectionState();
                    }

                    return;
                }

                text = AppendCurrentInventoryHelpText(selection.Value, now, out var shouldRetryDescription);
                if (text is null)
                {
                    deferredInventoryState = shouldRetryDescription ? state : null;
                    return;
                }

                selectedInventoryItem = selection.Value.InventoryItem;
                deferredInventoryState = null;
            }
            else if (IsMagicSpellGridProbe(state) && IsMagicSpellGridContext(now))
            {
                usedMagicSpellSelection = true;
                var selection = FindMagicSpellSelection(state, now, out var shouldRetry);
                if (selection is null)
                {
                    deferredMagicSpellState = shouldRetry ? state : null;
                    if (!shouldRetry)
                    {
                        ClearSilentSelectionState();
                    }

                    return;
                }

                text = AppendCurrentMagicSpellHelpText(selection.Value, now, out var shouldRetryDescription);
                if (text is null)
                {
                    deferredMagicSpellState = shouldRetryDescription ? state : null;
                    return;
                }

                deferredMagicSpellState = null;
            }
            else
            {
                deferredInventoryState = null;
                text = FindWidgetSelection(state, now);
                if (text is null)
                {
                    deferredWidgetStates[state.Name] = new DeferredWidgetState(state, now);
                    if (IsCharacterSelectionProbe(state) || IsMagicCategoryProbe(state))
                    {
                        ClearSilentSelectionState();
                    }

                    return;
                }

                deferredWidgetStates.Remove(state.Name);
            }

            if (text is null)
            {
                return;
            }

            if (!usedInventorySelection && !usedMagicSpellSelection)
            {
                text = AppendCurrentHelpText(text, now);
            }

            QueueCandidate(text, now, GetWidgetCandidatePriority(state), CreateWidgetCandidateKey(state, text, selectedInventoryItem, selectedNativeSelection));
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            if (pending is null)
            {
                return null;
            }

            var candidate = pending.Value;
            if (now - candidate.SeenAt < settleTime)
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

    private void AddRecent(MenuTextRenderEntry entry, DateTime now)
    {
        recentText.Add(new ObservedText(entry, now));
        PruneRecent(now);
    }

    private void PruneRecent(DateTime now)
    {
        recentText.RemoveAll(item => now - item.SeenAt > RecentTextWindow);
        recentCursors.RemoveAll(item => now - item.SeenAt > RecentCursorWindow);
    }

    private string? FindHorizontalSelection(MenuWidgetState state, DateTime now)
    {
        var selectableText = GetSelectableTextForWidget(state, now);
        if (TryFindTextNearCursor(selectableText, now, out var cursorSelection))
        {
            return FormatCharacterSelection(state, cursorSelection.Entry.Text, now);
        }

        if (IsConfigChoiceProbe(state))
        {
            return null;
        }

        var rows = BuildRows(selectableText);
        var selectedIndex = state.Cursor % state.Columns;
        foreach (var row in rows
            .Where(row => row.Count >= state.Columns && row.Count > selectedIndex)
            .OrderByDescending(row => row.Max(item => item.SeenAt))
            .ThenBy(row => row.Min(item => item.Entry.Y)))
        {
            return row.OrderBy(item => item.Entry.X).ElementAt(selectedIndex).Entry.Text;
        }

        return null;
    }

    private InventorySelection? FindInventorySelection(MenuWidgetState state, DateTime now, out bool shouldRetry)
    {
        shouldRetry = false;
        if (state.Columns != 1 || state.F14 != 0)
        {
            return null;
        }

        var selectableText = GetSelectableTextForWidget(state, now);
        var itemRows = selectableText
            .Where(item => IsInventoryItemName(item.Entry))
            .GroupBy(item => item.Entry.Text, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.SeenAt).First())
            .OrderBy(item => item.Entry.Y)
            .ToList();
        if (state.InventoryItem is { } nativeItem)
        {
            if (!string.IsNullOrWhiteSpace(nativeItem.Name))
            {
                return new InventorySelection(FormatInventoryItem(nativeItem.Name, nativeItem.Quantity), null, nativeItem);
            }

            if (state.Cursor < itemRows.Count)
            {
                return new InventorySelection(FormatInventoryItem(itemRows[state.Cursor].Entry.Text, nativeItem.Quantity), null, nativeItem);
            }
        }

        if (TryFindTextNearInventoryCursor(itemRows, state.Cursor, now, out var cursorSelection, out var cursor))
        {
            var cursorIndex = itemRows.FindIndex(item =>
                string.Equals(CreateScreenKey(item.Entry), CreateScreenKey(cursorSelection.Entry), StringComparison.Ordinal));
            if (cursorIndex >= 0 && cursorIndex != state.Cursor)
            {
                shouldRetry = state.Cursor < itemRows.Count;
                return null;
            }

            return new InventorySelection(cursorSelection.Entry.Text, cursor.SeenAt, null);
        }

        if (HasRecentInventoryCursor(now) && state.Cursor < itemRows.Count)
        {
            shouldRetry = true;
            return null;
        }

        if (state.Cursor >= itemRows.Count)
        {
            return null;
        }

        return new InventorySelection(itemRows[state.Cursor].Entry.Text, null, null);
    }

    private MagicSpellSelection? FindMagicSpellSelection(MenuWidgetState state, DateTime now, out bool shouldRetry)
    {
        shouldRetry = false;
        if (state.F14 != 0)
        {
            return null;
        }

        var spellRows = GetSelectableTextForWidget(state, now);
        if (!TryGetRecentMagicSpellCursor(state, now, out var cursor))
        {
            shouldRetry = spellRows.Count > 0 || HasRecentMagicSpellCursor(now);
            return null;
        }

        if (!TryFindTextNearCursor(spellRows, cursor.Observation, out var selected))
        {
            return null;
        }

        return new MagicSpellSelection(selected.Entry.Text, cursor.SeenAt);
    }

    private bool IsInventoryContext(MenuWidgetState state, DateTime now)
    {
        if (!IsInventoryListProbe(state))
        {
            return false;
        }

        if (state.InventoryItem is { } item)
        {
            var rows = GetSelectableTextForWidget(state, now)
                .Where(observed => IsInventoryItemName(observed.Entry))
                .GroupBy(observed => observed.Entry.Text, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(observed => observed.SeenAt).First())
                .OrderBy(observed => observed.Entry.Y)
                .ToList();
            if (rows.Count == 0)
            {
                return HasRecentItemMenuCommandText(now);
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                return true;
            }

            return state.Cursor < rows.Count &&
                string.Equals(rows[state.Cursor].Entry.Text, item.Name, StringComparison.Ordinal);
        }

        var itemRows = GetSelectableTextForWidget(state, now)
            .Where(observed => IsInventoryItemName(observed.Entry))
            .GroupBy(observed => observed.Entry.Text, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(observed => observed.SeenAt).First())
            .OrderBy(observed => observed.Entry.Y)
            .ToList();
        if (itemRows.Count == 0)
        {
            return HasRecentItemMenuCommandText(now);
        }

        if (HasRecentItemMenuCommandText(now) || HasRecentItemDescription(now))
        {
            return true;
        }

        if (state.Cursor < itemRows.Count &&
            resolveDescriptionByName?.Invoke(itemRows[state.Cursor].Entry.Text) is not null)
        {
            return false;
        }

        return true;
    }

    private bool HasRecentItemMenuCommandText(DateTime now) =>
        recentText.Any(item =>
            now - item.SeenAt <= WidgetSelectionTextWindow &&
            IsItemMenuCommandText(item.Entry));

    private bool HasRecentItemDescription(DateTime now) =>
        recentText.Any(item =>
            now - item.SeenAt <= WidgetSelectionTextWindow &&
            IsItemDescription(item.Entry));

    private void TryQueueDeferredInventory(DateTime now)
    {
        if (deferredInventoryState is not { } state)
        {
            return;
        }

        var selection = FindInventorySelection(state, now, out var shouldRetry);
        if (selection is null)
        {
            if (!shouldRetry)
            {
                deferredInventoryState = null;
                ClearSilentSelectionState();
            }

            return;
        }

        var text = AppendCurrentInventoryHelpText(selection.Value, now, out var shouldRetryDescription);
        if (text is null)
        {
            if (!shouldRetryDescription)
            {
                deferredInventoryState = null;
                ClearSilentSelectionState();
            }

            return;
        }

        deferredInventoryState = null;
        QueueCandidate(text, now, CandidatePriority.WidgetSelection, CreateWidgetCandidateKey(state, text, selection.Value.InventoryItem, null));
    }

    private void TryQueueDeferredMagicSpell(DateTime now)
    {
        if (deferredMagicSpellState is not { } state)
        {
            return;
        }

        var selection = FindMagicSpellSelection(state, now, out var shouldRetry);
        if (selection is null)
        {
            if (!shouldRetry)
            {
                deferredMagicSpellState = null;
                ClearSilentSelectionState();
            }

            return;
        }

        var text = AppendCurrentMagicSpellHelpText(selection.Value, now, out var shouldRetryDescription);
        if (text is null)
        {
            if (!shouldRetryDescription)
            {
                deferredMagicSpellState = null;
                ClearSilentSelectionState();
            }

            return;
        }

        deferredMagicSpellState = null;
        QueueCandidate(text, now, CandidatePriority.WidgetSelection, CreateWidgetCandidateKey(state, text, null, null));
    }

    private void TryQueueDeferredWidgets(DateTime now)
    {
        if (deferredWidgetStates.Count == 0)
        {
            return;
        }

        foreach (var pair in deferredWidgetStates.ToArray())
        {
            var deferred = pair.Value;
            if (now - deferred.SeenAt > WidgetSelectionTextWindow)
            {
                deferredWidgetStates.Remove(pair.Key);
                continue;
            }

            var state = deferred.State;
            if (IsRootMainMenuProbe(state) || IsIgnoredWidgetProbe(state, now))
            {
                deferredWidgetStates.Remove(pair.Key);
                continue;
            }

            var text = FindWidgetSelection(state, now);
            if (text is null)
            {
                continue;
            }

            deferredWidgetStates.Remove(pair.Key);
            text = AppendCurrentHelpText(text, now);
            QueueCandidate(text, now, GetWidgetCandidatePriority(state), CreateWidgetCandidateKey(state, text, null, state.NativeSelection));
        }
    }

    private string? FindWidgetSelection(MenuWidgetState state, DateTime now)
    {
        if (IsMagicCategoryProbe(state))
        {
            return FindVerticalSelection(state, now);
        }

        if (IsCommandRowProbe(state))
        {
            return FindCommandSelection(state, now);
        }

        return state.Rows == 1 && state.Columns > 1
            ? FindHorizontalSelection(state, now)
            : FindVerticalSelection(state, now);
    }

    private string? FindCommandSelection(MenuWidgetState state, DateTime now)
    {
        var commandText = GetRecentText(now, WidgetSelectionTextWindow)
            .Where(item => IsCommandRowText(item.Entry))
            .ToList();
        var selectedIndex = UsesFirstAsSelection(state)
            ? state.First
            : state.Columns > 1 ? state.Cursor % state.Columns : state.Cursor;
        if (selectedIndex < 0)
        {
            return null;
        }

        foreach (var row in BuildRows(commandText)
            .Where(row => row.Count > selectedIndex)
            .OrderByDescending(row => row.Max(item => item.SeenAt))
            .ThenBy(row => row.Min(item => item.Entry.Y)))
        {
            return row.OrderBy(item => item.Entry.X).ElementAt(selectedIndex).Entry.Text;
        }

        return null;
    }

    private string? FindVerticalSelection(MenuWidgetState state, DateTime now)
    {
        var selectableText = GetSelectableTextForWidget(state, now);
        if (TryFindTextNearCursor(selectableText, now, out var cursorSelection))
        {
            return FormatWidgetSelection(state, cursorSelection.Entry.Text, now);
        }

        if (state.Columns != 1)
        {
            return null;
        }

        var selectedIndex = state.Cursor;
        var minimumColumnRows = IsCharacterSelectionProbe(state) || IsMagicCategoryProbe(state)
            ? 1
            : Math.Min(state.Rows, 3);
        foreach (var column in BuildColumns(selectableText)
            .Where(column => column.Count > selectedIndex && column.Count >= minimumColumnRows)
            .OrderBy(column => column.Min(item => item.Entry.X)))
        {
            return FormatWidgetSelection(state, column.OrderBy(item => item.Entry.Y).ElementAt(selectedIndex).Entry.Text, now);
        }

        return null;
    }

    private string FormatWidgetSelection(MenuWidgetState state, string selectionText, DateTime now)
    {
        return FormatCharacterSelection(state, selectionText, now);
    }

    private string FormatCharacterSelection(MenuWidgetState state, string selectionText, DateTime now)
    {
        return selectionText;
    }

    private string? AppendCurrentMagicSpellHelpText(MagicSpellSelection selection, DateTime now, out bool shouldRetry)
    {
        shouldRetry = false;
        var help = recentText
            .Where(item => IsMagicSpellGridDescription(item.Entry) && now - item.SeenAt <= CurrentHelpTextWindow)
            .OrderByDescending(item => item.SeenAt)
            .FirstOrDefault();
        if (help.Entry.Text is null)
        {
            if (now - selection.CursorSeenAt <= InventoryDescriptionRefreshGrace)
            {
                shouldRetry = true;
                return null;
            }

            return selection.Text;
        }

        if (string.Equals(help.Entry.Text, selection.Text, StringComparison.Ordinal))
        {
            return selection.Text;
        }

        if (help.SeenAt < selection.CursorSeenAt)
        {
            if (now - selection.CursorSeenAt <= InventoryDescriptionRefreshGrace)
            {
                shouldRetry = true;
                return null;
            }

            return selection.Text;
        }

        return $"{selection.Text}. {help.Entry.Text}";
    }

    private List<ObservedText> GetSelectableText(DateTime now, TimeSpan? window = null)
    {
        return GetRecentText(now, window)
            .Where(item => IsSelectableText(item.Entry))
            .ToList();
    }

    private List<ObservedText> GetRecentText(DateTime now, TimeSpan? window = null)
    {
        PruneRecent(now);
        var maxAge = window ?? RecentTextWindow;
        return recentText
            .Where(item => now - item.SeenAt <= maxAge)
            .GroupBy(item => CreateScreenKey(item.Entry), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.SeenAt).First())
            .ToList();
    }

    private List<ObservedText> GetSelectableTextForWidget(MenuWidgetState state, DateTime now)
    {
        if (IsMagicCategoryProbe(state))
        {
            return GetRecentText(now, WidgetSelectionTextWindow)
                .Where(item => IsMagicCategoryText(item.Entry))
                .ToList();
        }

        if (IsCharacterSelectionProbe(state))
        {
            return GetRecentText(now, WidgetSelectionTextWindow)
                .Where(item => IsCharacterSelectionText(item.Entry))
                .ToList();
        }

        if (IsMagicSpellGridProbe(state))
        {
            return GetRecentText(now, WidgetSelectionTextWindow)
                .Where(item => IsSelectableForWidget(state, item.Entry))
                .ToList();
        }

        return GetSelectableText(now, WidgetSelectionTextWindow)
            .Where(item => IsSelectableForWidget(state, item.Entry))
            .ToList();
    }

    private static bool IsSelectableForWidget(MenuWidgetState state, MenuTextRenderEntry entry)
    {
        if (IsMagicSpellGridProbe(state))
        {
            return IsMagicSpellGridText(entry);
        }

        if (IsInventoryListProbe(state))
        {
            return IsSelectableText(entry);
        }

        if (IsConfigMainProbe(state))
        {
            return IsConfigMainLabel(entry);
        }

        if (IsConfigChoiceProbe(state))
        {
            return IsConfigChoiceValue(entry);
        }

        if (string.Equals(state.Name, "PHS party", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsCommandRowProbe(state))
        {
            return IsCommandRowText(entry);
        }

        return IsSelectableText(entry);
    }

    private string AppendCurrentHelpText(string selectionText, DateTime now)
    {
        var helpText = recentText
            .Where(item => IsHelpText(item.Entry) && now - item.SeenAt <= CurrentHelpTextWindow)
            .OrderByDescending(item => item.SeenAt)
            .Select(item => item.Entry.Text)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(helpText))
        {
            helpText = resolveDescriptionByName?.Invoke(selectionText);
        }

        if (string.IsNullOrWhiteSpace(helpText) || string.Equals(helpText, selectionText, StringComparison.Ordinal))
        {
            return selectionText;
        }

        return $"{selectionText}. {helpText}";
    }

    private string? AppendCurrentInventoryHelpText(InventorySelection selection, DateTime now, out bool shouldRetry)
    {
        shouldRetry = false;
        if (selection.InventoryItem?.Description is { Length: > 0 } nativeDescription)
        {
            return string.Equals(nativeDescription, selection.Text, StringComparison.Ordinal)
                ? selection.Text
                : $"{selection.Text}. {nativeDescription}";
        }

        var help = recentText
            .Where(item => IsDescriptiveText(item.Entry) && now - item.SeenAt <= CurrentHelpTextWindow)
            .OrderByDescending(item => item.SeenAt)
            .FirstOrDefault();
        if (help.Entry.Text is null ||
            string.Equals(help.Entry.Text, selection.Text, StringComparison.Ordinal))
        {
            return selection.Text;
        }

        if (selection.CursorSeenAt is { } cursorSeenAt && help.SeenAt < cursorSeenAt)
        {
            if (now - cursorSeenAt <= InventoryDescriptionRefreshGrace)
            {
                shouldRetry = true;
                return null;
            }

            return selection.Text;
        }

        return $"{selection.Text}. {help.Entry.Text}";
    }

    private static string FormatInventoryItem(string name, int quantity) =>
        quantity > 0 ? $"{name} x{quantity}" : name;

    private static string CreateWidgetCandidateKey(
        MenuWidgetState state,
        string text,
        InventoryItemSnapshot? inventoryItem,
        NativeMenuSelection? nativeSelection)
    {
        if (inventoryItem is { } item)
        {
            return $"widget\u001f{state.Name}\u001finventory\u001f{item.Slot}\u001f{item.ItemId}\u001f{item.Quantity}\u001f{item.Raw:X4}";
        }

        if (nativeSelection is { } selection)
        {
            return $"widget\u001f{state.Name}\u001fnative\u001f{selection.Key}";
        }

        return $"widget\u001f{state.Name}\u001f{text}";
    }

    private static string CreateNativeInventoryKey(InventoryItemSnapshot? inventoryItem)
    {
        if (inventoryItem is not { } item)
        {
            return string.Empty;
        }

        return $"{item.Slot}\u001f{item.ItemId}\u001f{item.Quantity}\u001f{item.Raw:X4}";
    }

    private bool TryQueueConfigMainCursorSelection(MenuTextRenderEntry entry, DateTime now)
    {
        if (!IsConfigMainLabel(entry) || !TryGetRecentCursor(now, out var cursor))
        {
            return false;
        }

        var candidate = CreateCursorCandidate(new ObservedText(entry, now), cursor.Observation);
        if (candidate is null ||
            candidate.Value.VerticalDistance > CursorVerticalTolerance ||
            candidate.Value.HorizontalGap < -CursorHorizontalOverlapTolerance ||
            candidate.Value.HorizontalGap > CursorHorizontalLeadMax)
        {
            return false;
        }

        var text = AppendCurrentHelpText(entry.Text, now);
        QueueCandidate(text, now, CandidatePriority.WidgetSelection, $"config-cursor\u001f{entry.Text}\u001f{entry.Y}");
        return true;
    }

    private void QueueCandidate(string text, DateTime now, CandidatePriority priority, string key)
    {
        if (!LooksLikeSpeechCandidate(text))
        {
            return;
        }

        var candidate = new Candidate(text, now, priority, key);
        if (pending is null || candidate.Priority >= pending.Value.Priority)
        {
            pending = candidate;
        }
    }

    private void ClearSilentSelectionState()
    {
        pending = null;
        lastSpokenKey = string.Empty;
    }

    private static List<List<ObservedText>> BuildRows(List<ObservedText> entries)
    {
        var rows = new List<List<ObservedText>>();
        foreach (var entry in entries.OrderBy(item => item.Entry.Y).ThenBy(item => item.Entry.X))
        {
            var row = rows.FirstOrDefault(existing => Math.Abs((int)existing[0].Entry.Y - (int)entry.Entry.Y) <= 6);
            if (row is null)
            {
                rows.Add([entry]);
            }
            else
            {
                row.Add(entry);
            }
        }

        return rows;
    }

    private static List<List<ObservedText>> BuildColumns(List<ObservedText> entries)
    {
        var columns = new List<List<ObservedText>>();
        foreach (var entry in entries.OrderBy(item => item.Entry.X).ThenBy(item => item.Entry.Y))
        {
            var column = columns.FirstOrDefault(existing => Math.Abs((int)existing[0].Entry.X - (int)entry.Entry.X) <= 12);
            if (column is null)
            {
                columns.Add([entry]);
            }
            else
            {
                column.Add(entry);
            }
        }

        return columns;
    }

    private bool TryFindTextNearCursor(IEnumerable<ObservedText> entries, DateTime now, out ObservedText selected)
    {
        selected = default;
        if (!TryGetRecentCursor(now, out var cursor))
        {
            return false;
        }

        var candidates = entries
            .Select(item => CreateCursorCandidate(item, cursor.Observation))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .Where(candidate => candidate.VerticalDistance <= CursorVerticalTolerance)
            .Where(candidate => candidate.HorizontalGap >= -CursorHorizontalOverlapTolerance)
            .Where(candidate => candidate.HorizontalGap <= CursorHorizontalLeadMax)
            .OrderBy(candidate => candidate.VerticalDistance)
            .ThenBy(candidate => Math.Abs(candidate.HorizontalGap - 32))
            .ThenBy(candidate => candidate.HorizontalGap)
            .ThenByDescending(candidate => candidate.Item.SeenAt)
            .FirstOrDefault();
        if (candidates.Item.Entry.Text is null)
        {
            return false;
        }

        selected = candidates.Item;
        return true;
    }

    private static bool TryFindTextNearCursor(
        IEnumerable<ObservedText> entries,
        MenuCursorDrawObservation cursor,
        out ObservedText selected)
    {
        selected = default;
        var candidate = entries
            .Select(item => CreateCursorCandidate(item, cursor))
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Where(item => item.VerticalDistance <= CursorVerticalTolerance)
            .Where(item => item.HorizontalGap >= -CursorHorizontalOverlapTolerance)
            .Where(item => item.HorizontalGap <= CursorHorizontalLeadMax)
            .OrderBy(item => item.VerticalDistance)
            .ThenBy(item => Math.Abs(item.HorizontalGap - 57))
            .ThenByDescending(item => item.Item.SeenAt)
            .FirstOrDefault();
        if (candidate.Item.Entry.Text is null)
        {
            return false;
        }

        selected = candidate.Item;
        return true;
    }

    private bool TryFindTextNearInventoryCursor(
        List<ObservedText> entries,
        int expectedIndex,
        DateTime now,
        out ObservedText selected,
        out ObservedCursor selectedCursor)
    {
        selected = default;
        selectedCursor = default;
        var candidates = new List<InventoryCursorCandidate>();
        for (var index = 0; index < entries.Count; index++)
        {
            foreach (var cursor in recentCursors
                .Where(item => now - item.SeenAt <= RecentCursorWindow)
                .Where(item => IsInventoryListCursor(item.Observation)))
            {
                var candidate = CreateCursorCandidate(entries[index], cursor.Observation);
                if (candidate is null ||
                    candidate.Value.VerticalDistance > CursorVerticalTolerance ||
                    candidate.Value.HorizontalGap < -CursorHorizontalOverlapTolerance ||
                    candidate.Value.HorizontalGap > CursorHorizontalLeadMax)
                {
                    continue;
                }

                candidates.Add(new InventoryCursorCandidate(entries[index], cursor, index, candidate.Value));
            }
        }

        var best = candidates
            .OrderBy(candidate => candidate.Index == expectedIndex ? 0 : 1)
            .ThenBy(candidate => candidate.CursorCandidate.VerticalDistance)
            .ThenByDescending(candidate => candidate.Cursor.SeenAt)
            .ThenBy(candidate => Math.Abs(candidate.CursorCandidate.HorizontalGap - 75))
            .FirstOrDefault();
        if (best.Item.Entry.Text is null)
        {
            return false;
        }

        selected = best.Item;
        selectedCursor = best.Cursor;
        return true;
    }

    private bool TryGetRecentCursor(DateTime now, out ObservedCursor cursor)
    {
        if (lastCursor is { } observed && now - observed.SeenAt <= RecentCursorWindow)
        {
            cursor = observed;
            return true;
        }

        cursor = default;
        return false;
    }

    private bool HasRecentInventoryCursor(DateTime now) =>
        recentCursors.Any(item => now - item.SeenAt <= RecentCursorWindow && IsInventoryListCursor(item.Observation));

    private bool HasRecentMagicSpellCursor(DateTime now) =>
        recentCursors.Any(item => now - item.SeenAt <= RecentCursorWindow && IsMagicSpellListCursor(item.Observation));

    private bool IsMagicSpellGridContext(DateTime now) =>
        recentText.Any(item =>
            now - item.SeenAt <= WidgetSelectionTextWindow &&
            (IsMagicSpellGridText(item.Entry) || IsMagicSpellGridDescription(item.Entry)));

    private bool TryGetRecentMagicSpellCursor(MenuWidgetState state, DateTime now, out ObservedCursor selected)
    {
        selected = recentCursors
            .Where(item => now - item.SeenAt <= RecentCursorWindow)
            .Where(item => IsMagicSpellListCursorForState(item.Observation, state))
            .OrderByDescending(item => item.SeenAt)
            .FirstOrDefault();
        return selected.Observation.Source is not null;
    }

    private static bool IsMagicSpellListCursor(MenuCursorDrawObservation cursor) =>
        cursor.CurrentModule == 5 &&
        cursor.Context == 0 &&
        ((cursor.X is >= 8 and <= 380 && cursor.Y is >= 105 and <= 245) ||
         (cursor.X is >= 16 and <= 760 && cursor.Y is >= 210 and <= 490));

    private static bool IsMagicSpellListCursorForState(MenuCursorDrawObservation cursor, MenuWidgetState state) =>
        IsMagicSpellListCursor(cursor) &&
        ((Math.Abs(cursor.X - (20 + state.First * 176)) <= 12 &&
          Math.Abs(cursor.Y - (230 + state.Cursor * 36)) <= 12) ||
         (Math.Abs(cursor.X - (10 + state.First * 88)) <= 6 &&
          Math.Abs(cursor.Y - (115 + state.Cursor * 18)) <= 6));

    private static CursorCandidate? CreateCursorCandidate(ObservedText item, MenuCursorDrawObservation cursor)
    {
        if (!TryGetScreenPoint(item.Entry, out var textX, out var textY))
        {
            return null;
        }

        var yDistance = Math.Abs(textY - cursor.Y);
        var xGap = textX - cursor.X;
        return new CursorCandidate(item, yDistance, xGap);
    }

    private static bool TryGetScreenPoint(MenuTextRenderEntry entry, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (entry.X > 4096 || entry.Y > 4096)
        {
            return false;
        }

        x = (int)entry.X;
        y = (int)entry.Y;
        return true;
    }

    private static bool IsRootMainMenu(MenuTextRenderEntry entry) =>
        entry.Context == RootMainMenuContext;

    private static bool IsHelpText(MenuTextRenderEntry entry) =>
        entry.Context == ConfigHelpContext &&
        entry.X <= 32 &&
        entry.Y <= 32;

    private static bool IsTitleMenuChoiceText(MenuTextRenderEntry entry)
    {
        if (entry.Context != ConfigHelpContext ||
            entry.X is < 220 or > 340 ||
            entry.Y is < 160 or > 260)
        {
            return false;
        }

        return LooksLikeSpeechCandidate(entry.Text);
    }

    private static bool IsSelectableText(MenuTextRenderEntry entry)
    {
        if (!LooksLikeSpeechCandidate(entry.Text) ||
            IsRootMainMenu(entry) ||
            IsHelpText(entry) ||
            IsTitleMenuChoiceText(entry) ||
            IsStatusOrHudText(entry) ||
            IsRightSideSubmenuTitle(entry) ||
            IsItemDescription(entry))
        {
            return false;
        }

        if (entry.Context == ItemMenuContext)
        {
            return true;
        }

        if (entry.Context == ConfigHelpContext)
        {
            return entry.X >= 40 && entry.Y is >= 60 and <= 460;
        }

        if (entry.Context == DialogPromptContext)
        {
            return entry.Y >= 260;
        }

        return false;
    }

    private static bool IsHighlightedChoice(MenuTextRenderEntry entry)
    {
        if (IsRootMainMenu(entry) ||
            IsStatusOrHudText(entry) ||
            IsHelpText(entry) ||
            IsTitleMenuChoiceText(entry) ||
            IsRightSideSubmenuTitle(entry) ||
            IsItemDescription(entry) ||
            !LooksLikeSpeechCandidate(entry.Text))
        {
            return false;
        }

        if (entry.Context == DialogPromptContext)
        {
            return entry.Y >= 260 && entry.Color == 7;
        }

        return false;
    }

    private static bool IsDescriptiveText(MenuTextRenderEntry entry) =>
        IsHelpText(entry) || IsItemDescription(entry);

    private static bool IsItemDescription(MenuTextRenderEntry entry) =>
        entry.Context == ItemMenuContext &&
        entry.X <= 40 &&
        entry.Y is >= 48 and <= 90;

    private static bool IsRightSideSubmenuTitle(MenuTextRenderEntry entry) =>
        entry.Context == ItemMenuContext &&
        entry.X >= 480 &&
        entry.Y <= 80;

    private static bool IsInventoryItemName(MenuTextRenderEntry entry) =>
        entry.Context == ItemMenuContext &&
        entry.X >= 300 &&
        entry.Y is >= 90 and <= 460 &&
        entry.Color is 0x107 or 7;

    private static bool IsInventoryListCursor(MenuCursorDrawObservation cursor) =>
        cursor.Context == ConfigHelpContext &&
        cursor.X is >= 250 and <= 340 &&
        cursor.Y is >= 80 and <= 460;

    private static bool IsStatusOrHudText(MenuTextRenderEntry entry) =>
        entry.Context == FieldStatusContext ||
        entry.Context == ClockGilContext;

    private bool IsIgnoredWidgetProbe(MenuWidgetState state, DateTime now)
    {
        if (state.Columns == 1 &&
            string.Equals(state.Name, "Item arrange", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(state.Name, "PHS party", StringComparison.Ordinal) &&
            HasRecentConfigMenuText(now);
    }

    private bool HasRecentConfigMenuText(DateTime now) =>
        recentText.Any(item => now - item.SeenAt <= WidgetSelectionTextWindow &&
            (IsConfigMainLabel(item.Entry) || IsConfigChoiceValue(item.Entry)));

    private static bool IsRootMainMenuProbe(MenuWidgetState state) =>
        string.Equals(state.Name, "Item/Main list", StringComparison.Ordinal) &&
        state.Columns == 1 &&
        state.Rows >= 8;

    private static bool IsInventoryListProbe(MenuWidgetState state) =>
        string.Equals(state.Name, "Item list", StringComparison.Ordinal);

    private static bool IsMagicSpellGridProbe(MenuWidgetState state) =>
        string.Equals(state.Name, "Magic list", StringComparison.Ordinal);

    private static bool IsConfigMainProbe(MenuWidgetState state) =>
        string.Equals(state.Name, "Config main", StringComparison.Ordinal);

    private static bool IsConfigChoiceProbe(MenuWidgetState state) =>
        state.Name.StartsWith("Config choice", StringComparison.Ordinal) ||
        state.Name.StartsWith("Config value", StringComparison.Ordinal);

    private static bool IsCharacterSelectionProbe(MenuWidgetState state) =>
        string.Equals(state.Name, "Equip character", StringComparison.Ordinal) ||
        string.Equals(state.Name, "Status character", StringComparison.Ordinal) ||
        string.Equals(state.Name, "Limit character", StringComparison.Ordinal);

    private static bool IsMagicCategoryProbe(MenuWidgetState state) =>
        string.Equals(state.Name, "Magic category", StringComparison.Ordinal);

    private static bool IsMagicCategoryText(MenuTextRenderEntry entry) =>
        LooksLikeSpeechCandidate(entry.Text) &&
        entry.Context == ItemMenuContext &&
        entry.X >= 480 &&
        entry.Y is >= 40 and <= 140;

    private static bool IsCharacterSelectionText(MenuTextRenderEntry entry) =>
        LooksLikeSpeechCandidate(entry.Text) &&
        (entry.Context == FieldStatusContext || entry.Context == ClockGilContext) &&
        entry.X is >= 80 and <= 220 &&
        entry.Y is >= 12 and <= 140 &&
        !NonCharacterStatusLabels.Contains(entry.Text.Trim());

    private static bool IsMagicSpellGridText(MenuTextRenderEntry entry) =>
        LooksLikeSpeechCandidate(entry.Text) &&
        entry.Context == ClockGilContext &&
        entry.X is >= 60 and <= 420 &&
        entry.Y is >= 180 and <= 460;

    private static bool IsMagicSpellGridDescription(MenuTextRenderEntry entry) =>
        LooksLikeSpeechCandidate(entry.Text) &&
        entry.Context == ClockGilContext &&
        entry.X <= 40 &&
        entry.Y is >= 140 and <= 210;

    private static bool IsCommandRowProbe(MenuWidgetState state) =>
        IsCommandTextWidget(state) &&
        state.Columns * state.Rows <= 4 &&
        (state.Rows == 1 || state.Columns == 1);

    private static bool UsesFirstAsSelection(MenuWidgetState state) =>
        string.Equals(state.Name, "Item submenu command", StringComparison.Ordinal) &&
        state.Columns == 3 &&
        state.Rows == 1;

    private static bool IsCommandTextWidget(MenuWidgetState state) =>
            (state.Name.Contains("command", StringComparison.OrdinalIgnoreCase) ||
             state.Name.Contains("category", StringComparison.OrdinalIgnoreCase) ||
             state.Name.Contains("arrange", StringComparison.OrdinalIgnoreCase));

    private static CandidatePriority GetWidgetCandidatePriority(MenuWidgetState state) =>
        IsCommandRowProbe(state) ? CandidatePriority.CommandSelection : CandidatePriority.WidgetSelection;

    private static bool IsConfigMainLabel(MenuTextRenderEntry entry) =>
        LooksLikeSpeechCandidate(entry.Text) &&
        entry.Context == ConfigHelpContext &&
        entry.X is >= 40 and <= 180 &&
        entry.Y is >= 60 and <= 460;

    private static bool IsConfigChoiceValue(MenuTextRenderEntry entry) =>
        entry.Context == ConfigHelpContext &&
        entry.X >= 220 &&
        entry.Y is >= 100 and <= 460 &&
        !IsHelpText(entry) &&
        !IsTitleMenuChoiceText(entry) &&
        !IsConfigMainLabel(entry);

    private static bool IsCommandRowText(MenuTextRenderEntry entry) =>
        entry.Context == ItemMenuContext &&
        entry.Y <= 48 &&
        entry.X >= 40 &&
        entry.X <= 340 &&
        !IsItemDescription(entry);

    private static bool IsItemMenuCommandText(MenuTextRenderEntry entry) =>
        IsCommandRowText(entry) && LooksLikeSpeechCandidate(entry.Text);

    private static string CreateScreenKey(MenuTextRenderEntry entry) =>
        $"{entry.Text}\u001f{entry.X}\u001f{entry.Y}\u001f{entry.Color}\u001f{entry.Context}";

    private static bool LooksLikeSpeechCandidate(string text)
    {
        if (text.Length < 2)
        {
            return false;
        }

        return text.Any(char.IsLetterOrDigit);
    }

    private readonly record struct Candidate(string Text, DateTime SeenAt, CandidatePriority Priority, string Key);

    private readonly record struct ObservedText(MenuTextRenderEntry Entry, DateTime SeenAt);

    private readonly record struct ObservedCursor(MenuCursorDrawObservation Observation, DateTime SeenAt);

    private readonly record struct CursorCandidate(ObservedText Item, int VerticalDistance, int HorizontalGap);

    private readonly record struct InventoryCursorCandidate(
        ObservedText Item,
        ObservedCursor Cursor,
        int Index,
        CursorCandidate CursorCandidate);

    private readonly record struct ObservedWidgetState(int Cursor, int First, int F14, string NativeInventoryKey, string NativeSelectionKey);

    private readonly record struct DeferredWidgetState(MenuWidgetState State, DateTime SeenAt);

    private readonly record struct InventorySelection(string Text, DateTime? CursorSeenAt, InventoryItemSnapshot? InventoryItem);

    private readonly record struct MagicSpellSelection(string Text, DateTime CursorSeenAt);
}

public enum CandidatePriority
{
    HelpText = 0,
    WidgetSelection = 1,
    HighlightedChoice = 1,
    CommandSelection = 2
}
