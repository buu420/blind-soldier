namespace Ff7.Accessibility.Reloaded;

public sealed class ActiveMenuFrameSpeechCoordinator
{
    private const int RootMainMenuContext = 0x3A83126F;
    private const int ConfigHelpContext = 0x3DCCCCCD;
    private const int MagicTextContext = 0x3E4CCCCD;
    private const int LimitLevelContext = 0x3E99999A;
    private const int LimitNameContext = 0x3DCED917;
    private const int ItemArrangeContext = 0x3C23D70A;
    private const int MagicCategoryRowSpacing = 17;
    private const int MenuModule = 5;
    private const int CursorVerticalTolerance = 18;
    private const int CursorHorizontalOverlapTolerance = 16;
    private const int CursorHorizontalLeadMax = 180;
    private static readonly TimeSpan WidgetSessionGap = TimeSpan.FromMilliseconds(500);

    private readonly object sync = new();
    private readonly List<MenuTextRenderEntry> frameText = new();
    private readonly List<MenuCursorDrawObservation> frameCursors = new();
    private readonly Dictionary<uint, string> lastSpokenKeysByWidget = new();
    private readonly Dictionary<uint, NativeWidgetState> lastObservedStatesByWidget = new();
    private readonly Dictionary<uint, DateTime> lastObservedAtByWidget = new();
    private string[]? cachedItemCommandLabels;
    private string[]? cachedItemArrangeLabels;
    private SpeechCandidate? pending;
    private uint? lastCompletedWidgetAddress;
    private bool limitConfirmationPromptPending;

    public void ObserveDraw(MenuTextRenderEntry entry)
    {
        if (!LooksLikeSpeechCandidate(entry.Text))
        {
            return;
        }

        lock (sync)
        {
            frameText.Add(entry);
        }
    }

    public void ObserveCursor(MenuCursorDrawObservation cursor)
    {
        if (cursor.CurrentModule != MenuModule)
        {
            return;
        }

        lock (sync)
        {
            frameCursors.Add(cursor);
        }
    }

    public void CompleteFrame(ActiveMenuWidgetSnapshot widget, DateTime now)
    {
        lock (sync)
        {
            var text = frameText
                .GroupBy(CreateScreenKey, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();
            var cursors = frameCursors.ToList();
            frameText.Clear();
            frameCursors.Clear();
            UpdatePersistentItemLabels(text);

            var widgetBecameActive = lastCompletedWidgetAddress != widget.Address;
            lastCompletedWidgetAddress = widget.Address;
            if (widgetBecameActive)
            {
                limitConfirmationPromptPending = widget.Kind == MenuWidgetKind.LimitConfirmation;
                if (limitConfirmationPromptPending)
                {
                    lastSpokenKeysByWidget.Remove(widget.Address);
                }
            }
            else if (widget.Kind != MenuWidgetKind.LimitConfirmation)
            {
                limitConfirmationPromptPending = false;
            }

            if (widget.Kind == MenuWidgetKind.RootMainMenu)
            {
                return;
            }

            ObserveNativeState(widget, now);

            if (widget.ScrollState != 0)
            {
                ClearPendingSelection(widget.Address);
                return;
            }

            string? speech;
            string? nativeSelectionIdentity = null;
            if (widget.Kind == MenuWidgetKind.MagicCategory)
            {
                if (!TryBuildMagicCategorySpeech(widget, text, out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (widget.Kind == MenuWidgetKind.ItemCommand &&
                widget.Columns == 3 && widget.Rows == 1)
            {
                if (!TryBuildItemCommandSpeech(widget, text, out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (widget.Kind == MenuWidgetKind.ItemArrange)
            {
                if (!TryBuildItemArrangeSpeech(widget, text, out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (widget.Kind == MenuWidgetKind.LimitCommand)
            {
                if (widget.Columns != 2 || widget.Rows != 1 || widget.First is < 0 or > 1)
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }

                // The legacy Limit screen stores Set/Check in the widget's
                // first field and can update it without drawing a cursor.
                speech = widget.First == 0 ? "Set" : "Check";
                nativeSelectionIdentity = $"limit-command:{widget.First}";
            }
            else if (widget.Kind is MenuWidgetKind.CharacterList or
                MenuWidgetKind.EquipmentSlot or
                MenuWidgetKind.EquipmentList or
                MenuWidgetKind.MateriaList or
                MenuWidgetKind.ConfigSoundVolume or
                MenuWidgetKind.ItemTarget or
                MenuWidgetKind.MagicTarget)
            {
                if (widget.NativeSelection is not { Text.Length: > 0 } nativeSelection)
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }

                speech = FormatNativeSelection(nativeSelection);
                nativeSelectionIdentity = nativeSelection.Key;
            }
            else if (widget.Kind == MenuWidgetKind.ItemList)
            {
                if (widget.InventoryItem is not { Name.Length: > 0 } item)
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }

                speech = FormatInventoryItem(item);
            }
            else if (widget.Kind == MenuWidgetKind.MagicList)
            {
                if (widget.MagicSpell is not { Name.Length: > 0 } spell)
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }

                speech = FormatMagicSpell(spell);
            }
            else if (widget.Kind == MenuWidgetKind.MateriaSlot)
            {
                if (widget.NativeSelection is { Text.Length: > 0 } materiaSelection)
                {
                    speech = FormatNativeSelection(materiaSelection);
                    nativeSelectionIdentity = materiaSelection.Key;
                }
                else if (!TryBuildMateriaSlotSpeech(widget, text, out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (widget.Kind == MenuWidgetKind.LimitLevel)
            {
                if (!TryBuildLimitLevelSpeech(widget, text, out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (widget.Kind == MenuWidgetKind.LimitMoveList)
            {
                if (!TryBuildLimitMoveSpeech(text, cursors, out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (widget.Kind == MenuWidgetKind.LimitConfirmation)
            {
                if (!TryBuildLimitConfirmationSpeech(
                        widget,
                        text,
                        limitConfirmationPromptPending,
                        out speech))
                {
                    ClearPendingSelection(widget.Address);
                    return;
                }
            }
            else if (TryFindCursorSelection(text, cursors, out var selection))
            {
                speech = AppendFrameDescription(widget.Kind, selection.Text, text);
            }
            else
            {
                ClearPendingSelection(widget.Address);
                return;
            }

            if (!LooksLikeSpeechCandidate(speech))
            {
                ClearPendingSelection(widget.Address);
                return;
            }

            var selectionIdentity = nativeSelectionIdentity is { Length: > 0 }
                ? $"native:{nativeSelectionIdentity}"
                : $"speech:{speech}";
            var key = $"{widget.Address:X8}\u001f{widget.First}\u001f{widget.Cursor}\u001f{widget.ScrollOffset}\u001f{selectionIdentity}";
            pending = new SpeechCandidate(widget.Address, speech, key, now);
            if (widget.Kind == MenuWidgetKind.LimitConfirmation)
            {
                limitConfirmationPromptPending = false;
            }
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            if (pending is not { } candidate)
            {
                return null;
            }

            pending = null;
            if (lastSpokenKeysByWidget.TryGetValue(candidate.WidgetAddress, out var lastSpokenKey) &&
                string.Equals(candidate.Key, lastSpokenKey, StringComparison.Ordinal))
            {
                return null;
            }

            lastSpokenKeysByWidget[candidate.WidgetAddress] = candidate.Key;
            return candidate.Text;
        }
    }

    public void DiscardPending()
    {
        lock (sync)
        {
            pending = null;
            frameText.Clear();
            frameCursors.Clear();
            cachedItemCommandLabels = null;
            cachedItemArrangeLabels = null;
            lastCompletedWidgetAddress = null;
            limitConfirmationPromptPending = false;
        }
    }

    private void ObserveNativeState(ActiveMenuWidgetSnapshot widget, DateTime now)
    {
        if (lastObservedAtByWidget.TryGetValue(widget.Address, out var lastObservedAt) &&
            now - lastObservedAt > WidgetSessionGap)
        {
            lastObservedStatesByWidget.Remove(widget.Address);
            lastSpokenKeysByWidget.Remove(widget.Address);
        }

        lastObservedAtByWidget[widget.Address] = now;
        var state = new NativeWidgetState(
            widget.First,
            widget.Cursor,
            widget.ScrollOffset,
            widget.ScrollState);
        if (!lastObservedStatesByWidget.TryGetValue(widget.Address, out var previous) || previous != state)
        {
            lastObservedStatesByWidget[widget.Address] = state;
            lastSpokenKeysByWidget.Remove(widget.Address);
        }
    }

    private void ClearPendingSelection(uint widgetAddress)
    {
        if (pending is { WidgetAddress: var pendingAddress } && pendingAddress == widgetAddress)
        {
            pending = null;
        }
    }

    private static bool TryFindCursorSelection(
        IReadOnlyList<MenuTextRenderEntry> text,
        IReadOnlyList<MenuCursorDrawObservation> cursors,
        out MenuTextRenderEntry selection)
    {
        selection = default;
        var best = text
            .Where(IsSelectableText)
            .SelectMany(entry => cursors.Select(cursor => CreateCandidate(entry, cursor)))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .Where(candidate => candidate.VerticalDistance <= CursorVerticalTolerance)
            .Where(candidate => candidate.HorizontalGap >= -CursorHorizontalOverlapTolerance)
            .Where(candidate => candidate.HorizontalGap <= CursorHorizontalLeadMax)
            .OrderBy(candidate => candidate.VerticalDistance)
            .ThenBy(candidate => Math.Abs(candidate.HorizontalGap - 57))
            .ThenBy(candidate => candidate.HorizontalGap)
            .FirstOrDefault();
        if (best.Entry.Text is null)
        {
            return false;
        }

        selection = best.Entry;
        return true;
    }

    private static CursorCandidate? CreateCandidate(
        MenuTextRenderEntry entry,
        MenuCursorDrawObservation cursor)
    {
        if (entry.X > 4096 || entry.Y > 4096)
        {
            return null;
        }

        return new CursorCandidate(
            entry,
            Math.Abs((int)entry.Y - cursor.Y),
            (int)entry.X - cursor.X);
    }

    private static string AppendFrameDescription(
        MenuWidgetKind kind,
        string selection,
        IReadOnlyList<MenuTextRenderEntry> text)
    {
        MenuTextRenderEntry? description = null;
        if (kind is MenuWidgetKind.MagicList or
            MenuWidgetKind.SummonList or
            MenuWidgetKind.EnemySkillList or
            MenuWidgetKind.MateriaList)
        {
            description = text.LastOrDefault(IsMagicDescription);
        }
        else
        {
            description = text.LastOrDefault(IsHelpText);
        }

        if (description is not { } help ||
            string.IsNullOrWhiteSpace(help.Text) ||
            string.Equals(help.Text, selection, StringComparison.Ordinal))
        {
            return selection;
        }

        return $"{selection}. {help.Text}";
    }

    private static bool TryBuildMagicCategorySpeech(
        ActiveMenuWidgetSnapshot widget,
        IReadOnlyList<MenuTextRenderEntry> text,
        out string speech)
    {
        speech = string.Empty;
        if (widget.First != 0 || widget.Columns != 1 || widget.Rows != 3 ||
            widget.Cursor is < 0 or > 2)
        {
            return false;
        }

        // FUN_00710dfa always draws the first category and conditionally draws
        // the other two at fixed 0x11-row intervals. This screen has no cursor
        // draw in the affected native path, so correlate the checked native row
        // with its localized rendered label instead.
        var rows = text
            .Where(entry => entry.Context == LimitNameContext &&
                entry.X is >= 200 and <= 640 &&
                entry.Y is >= 20 and <= 160 &&
                LooksLikeSpeechCandidate(entry.Text))
            .OrderBy(entry => entry.Y)
            .ThenBy(entry => entry.X)
            .ToArray();
        if (rows.Length == 0)
        {
            return false;
        }

        var firstRow = rows[0];
        var coordinateScale = firstRow.X >= 400 ? 2 : 1;
        var expectedY = firstRow.Y +
            (uint)(widget.Cursor * MagicCategoryRowSpacing * coordinateScale);
        var tolerance = 4u * (uint)coordinateScale;
        var selected = rows
            .Where(entry => Math.Abs((long)entry.Y - expectedY) <= tolerance)
            .OrderBy(entry => Math.Abs((long)entry.X - firstRow.X))
            .FirstOrDefault();
        if (selected.Text is not { Length: > 0 })
        {
            return false;
        }

        speech = selected.Text;
        return true;
    }

    private bool TryBuildItemArrangeSpeech(
        ActiveMenuWidgetSnapshot widget,
        IReadOnlyList<MenuTextRenderEntry> text,
        out string speech)
    {
        speech = string.Empty;
        if (widget.First != 0 || widget.Columns != 1 || widget.Rows != 8 ||
            widget.Cursor is < 0 or >= 8)
        {
            return false;
        }

        // FUN_00715105 stores the highlighted Arrange row in the widget's
        // cursor field, but this screen does not emit a usable cursor draw in
        // either runtime. Match that checked row to the eight localized labels
        // rendered in one vertical column instead.
        var column = TryExtractItemArrangeLabels(text) ?? cachedItemArrangeLabels;
        if (column is null)
        {
            return false;
        }

        speech = column[widget.Cursor].Trim();
        return LooksLikeSpeechCandidate(speech);
    }

    private static string[]? TryExtractItemArrangeLabels(
        IReadOnlyList<MenuTextRenderEntry> text) => text
            .Where(entry => entry.Context == ItemArrangeContext &&
                entry.X <= 4096 &&
                entry.Y <= 4096 &&
                LooksLikeSpeechCandidate(entry.Text))
            .GroupBy(entry => entry.X)
            .Select(group => group.OrderBy(entry => entry.Y).ToArray())
            .Where(rows => rows.Length == 8)
            .Where(rows => rows.Zip(rows.Skip(1), (first, second) => second.Y > first.Y).All(value => value))
            .OrderBy(rows => rows[0].Y)
            .ThenBy(rows => rows[0].X)
            .Select(rows => rows.Select(entry => entry.Text.Trim()).ToArray())
            .FirstOrDefault();

    private bool TryBuildItemCommandSpeech(
        ActiveMenuWidgetSnapshot widget,
        IReadOnlyList<MenuTextRenderEntry> text,
        out string speech)
    {
        speech = string.Empty;
        if (widget.Columns != 3 || widget.Rows != 1 || widget.Cursor != 0 ||
            widget.First is < 0 or >= 3)
        {
            return false;
        }

        // FUN_00715105 stores Use/Arrange/Key Items in First and moves the
        // native cursor directly, so this flow can change selection without
        // emitting either cursor-render callback. Correlate First with the
        // three localized labels drawn across the command row.
        var commandRow = TryExtractItemCommandLabels(text) ?? cachedItemCommandLabels;
        if (commandRow is null)
        {
            return false;
        }

        speech = commandRow[widget.First].Trim();
        return LooksLikeSpeechCandidate(speech);
    }

    private static string[]? TryExtractItemCommandLabels(
        IReadOnlyList<MenuTextRenderEntry> text) => text
            .Where(entry => entry.Context == LimitNameContext &&
                entry.X <= 640 && entry.Y <= 48 &&
                LooksLikeSpeechCandidate(entry.Text))
            .GroupBy(entry => entry.Y)
            .Select(group => group
                .GroupBy(entry => entry.X)
                .Select(entries => entries.Last())
                .OrderBy(entry => entry.X)
                .ToArray())
            .Where(entries => entries.Length == 3)
            .OrderBy(entries => entries[0].Y)
            .Select(entries => entries.Select(entry => entry.Text.Trim()).ToArray())
            .FirstOrDefault();

    private void UpdatePersistentItemLabels(IReadOnlyList<MenuTextRenderEntry> text)
    {
        if (TryExtractItemCommandLabels(text) is { } commandLabels)
        {
            cachedItemCommandLabels = commandLabels;
        }

        if (TryExtractItemArrangeLabels(text) is { } arrangeLabels)
        {
            cachedItemArrangeLabels = arrangeLabels;
        }
    }

    private static bool TryBuildMateriaSlotSpeech(
        ActiveMenuWidgetSnapshot widget,
        IReadOnlyList<MenuTextRenderEntry> text,
        out string speech)
    {
        speech = string.Empty;
        if (widget.Columns != 8 || widget.Rows != 2 ||
            widget.First is < 0 or >= 8 ||
            widget.Cursor is < 0 or >= 2)
        {
            return false;
        }

        var row = widget.Cursor == 0 ? "Weapon" : "Armor";
        var slot = $"{row} materia slot {widget.First + 1}";
        var selectedName = text
            .LastOrDefault(entry =>
                entry.Context == LimitNameContext &&
                entry.X is >= 20 and <= 120 &&
                entry.Y is >= 190 and <= 240)
            .Text?
            .Trim();
        if (!LooksLikeSpeechCandidate(selectedName))
        {
            speech = $"{slot}, empty";
            return true;
        }

        speech = $"{slot}, {selectedName}";
        var description = text.LastOrDefault(IsMagicDescription).Text?.Trim();
        if (LooksLikeSpeechCandidate(description) &&
            !string.Equals(description, selectedName, StringComparison.Ordinal))
        {
            speech = $"{speech}. {description}";
        }

        return true;
    }

    private static string FormatInventoryItem(InventoryItemSnapshot item)
    {
        var name = item.Quantity > 0 ? $"{item.Name} x{item.Quantity}" : item.Name!;
        if (string.IsNullOrWhiteSpace(item.Description) ||
            string.Equals(item.Description, name, StringComparison.Ordinal))
        {
            return name;
        }

        return $"{name}. {item.Description}";
    }

    private static string FormatNativeSelection(NativeMenuSelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.Description) ||
            string.Equals(selection.Description, selection.Text, StringComparison.Ordinal))
        {
            return selection.Text;
        }

        return $"{selection.Text}. {selection.Description}";
    }

    private static string FormatMagicSpell(MagicMenuSpellSnapshot spell)
    {
        var speech = $"{spell.Name}. {spell.MpCost} MP";
        if (string.IsNullOrWhiteSpace(spell.Description) ||
            string.Equals(spell.Description, spell.Name, StringComparison.Ordinal))
        {
            return speech;
        }

        return $"{speech}. {spell.Description}";
    }

    private static bool TryBuildLimitLevelSpeech(
        ActiveMenuWidgetSnapshot widget,
        IReadOnlyList<MenuTextRenderEntry> text,
        out string speech)
    {
        speech = string.Empty;
        if (widget.Columns != 2 || widget.Rows != 2 ||
            widget.First is < 0 or > 1 || widget.Cursor is < 0 or > 1)
        {
            return false;
        }

        var levelNumber = widget.Cursor * widget.Columns + widget.First + 1;
        var expectedLabel = $"LEVEL {levelNumber}";
        var level = text.LastOrDefault(entry =>
            entry.Context == LimitLevelContext &&
            string.Equals(entry.Text.Trim(), expectedLabel, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(level.Text))
        {
            return false;
        }

        var rightBoundary = text
            .Where(entry => entry.Context == LimitLevelContext)
            .Where(entry => Math.Abs((int)entry.Y - (int)level.Y) <= 10)
            .Where(entry => entry.X > level.X)
            .Select(entry => (int)entry.X)
            .DefaultIfEmpty((int)level.X + 292)
            .Min();
        var bottomBoundary = text
            .Where(entry => entry.Context == LimitLevelContext)
            .Where(entry => entry.Y > level.Y)
            .Select(entry => (int)entry.Y)
            .DefaultIfEmpty((int)level.Y + 137)
            .Min();
        var names = text
            .Where(entry => entry.Context == LimitNameContext)
            .Where(entry => entry.X >= level.X && entry.X < rightBoundary)
            .Where(entry => entry.Y > level.Y && entry.Y < bottomBoundary)
            .Select(entry => TrimLimitText(entry.Text))
            .Where(LooksLikeSpeechCandidate)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        speech = names.Count == 0
            ? level.Text.Trim()
            : $"{level.Text.Trim()}. {string.Join(". ", names)}";
        return true;
    }

    private static bool TryBuildLimitMoveSpeech(
        IReadOnlyList<MenuTextRenderEntry> text,
        IReadOnlyList<MenuCursorDrawObservation> cursors,
        out string speech)
    {
        speech = string.Empty;
        var moveText = text
            .Where(entry => entry.Context == LimitNameContext && entry.Y >= 180)
            .ToList();
        var moveCursors = cursors
            .Where(cursor => cursor.Y >= 180)
            .ToList();
        if (!TryFindCursorSelection(moveText, moveCursors, out var selectedMove))
        {
            return false;
        }

        var description = text
            .Where(entry => entry.Context == LimitNameContext)
            .Where(entry => entry.X <= 80 && entry.Y is >= 110 and < 180)
            .LastOrDefault();
        var name = TrimLimitText(selectedMove.Text);
        var descriptionText = TrimLimitText(description.Text ?? string.Empty);
        if (!LooksLikeSpeechCandidate(name) || !LooksLikeSpeechCandidate(descriptionText))
        {
            return false;
        }

        speech = string.Equals(name, descriptionText, StringComparison.Ordinal)
            ? name
            : $"{name}. {descriptionText}";
        return true;
    }

    private static bool TryBuildLimitConfirmationSpeech(
        ActiveMenuWidgetSnapshot widget,
        IReadOnlyList<MenuTextRenderEntry> text,
        bool includePrompt,
        out string speech)
    {
        speech = string.Empty;
        if (widget.Columns != 1 || widget.Rows != 2 || widget.First != 0 ||
            widget.Cursor is < 0 or > 1)
        {
            return false;
        }

        var visible = text
            .Where(entry => entry.Context == 0 && entry.X <= 4096 && entry.Y <= 4096)
            .Where(entry => LooksLikeSpeechCandidate(entry.Text))
            .OrderBy(entry => entry.Y)
            .ThenBy(entry => entry.X)
            .ToList();
        var choices = visible
            .GroupBy(entry => entry.X)
            .Select(group => group.OrderBy(entry => entry.Y).ToList())
            .Where(group => group.Count == widget.Rows)
            .Where(group => group[1].Y > group[0].Y && group[1].Y - group[0].Y <= 64)
            .Where(group => visible.Count(entry => entry.Y < group[0].Y) >= 2)
            .OrderByDescending(group => group[0].Y)
            .FirstOrDefault();
        if (choices is null)
        {
            return false;
        }

        var selected = choices[widget.Cursor].Text.Trim();
        if (!includePrompt)
        {
            speech = selected;
            return true;
        }

        var prompt = visible
            .Where(entry => entry.Y < choices[0].Y)
            .Where(entry => entry.X <= choices[0].X)
            .Select(entry => entry.Text.Trim())
            .Where(LooksLikeSpeechCandidate)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (prompt.Count < 2)
        {
            return false;
        }

        speech = $"{string.Join(" ", prompt)} {selected}";
        return true;
    }

    private static string TrimLimitText(string text)
    {
        var trimmed = text.Trim();
        const string switchControl = "[SWITCH]";
        while (trimmed.StartsWith(switchControl, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[switchControl.Length..].TrimStart();
        }

        var firstLetterOrDigit = 0;
        while (firstLetterOrDigit < trimmed.Length &&
               !char.IsLetterOrDigit(trimmed[firstLetterOrDigit]))
        {
            firstLetterOrDigit++;
        }

        return trimmed[firstLetterOrDigit..].Trim();
    }

    private static bool IsSelectableText(MenuTextRenderEntry entry) =>
        LooksLikeSpeechCandidate(entry.Text) &&
        entry.Context != RootMainMenuContext &&
        !IsMagicDescription(entry) &&
        !IsHelpText(entry);

    private static bool IsMagicDescription(MenuTextRenderEntry entry) =>
        entry.Context == MagicTextContext &&
        entry.X <= 40 &&
        entry.Y is >= 140 and <= 210;

    private static bool IsHelpText(MenuTextRenderEntry entry) =>
        entry.Context == ConfigHelpContext &&
        entry.X <= 32 &&
        entry.Y <= 32;

    private static bool LooksLikeSpeechCandidate(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Length >= 2 &&
        text.Any(char.IsLetterOrDigit);

    private static string CreateScreenKey(MenuTextRenderEntry entry) =>
        $"{entry.Text}\u001f{entry.X}\u001f{entry.Y}\u001f{entry.Color}\u001f{entry.Context}";

    private readonly record struct CursorCandidate(
        MenuTextRenderEntry Entry,
        int VerticalDistance,
        int HorizontalGap);

    private readonly record struct SpeechCandidate(uint WidgetAddress, string Text, string Key, DateTime SeenAt);

    private readonly record struct NativeWidgetState(int First, int Cursor, int ScrollOffset, int ScrollState);
}
