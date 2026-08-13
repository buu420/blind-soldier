namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Owns the scripted Reform/PHS party screen from its exact native title,
/// rendered character names, and Ghidra-verified cursor contexts.
/// </summary>
public sealed class PartyFormationSpeechTracker
{
    public const int MenuModule = 5;

    private const int RootMainMenuContext = 0x3A83126F;
    private const int PartyTextContext = 0x3DCCCCCD;
    private const int ReserveNameContext = 0x3DCED917;
    private const int ActivePartyCursorContext = 0x3DCF0D84;
    private const int ReserveCursorContext = 0x3DCD0679;
    private static readonly TimeSpan ScreenEvidenceWindow = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MinimumSelectionSettle = TimeSpan.FromMilliseconds(75);

    private readonly object sync = new();
    private readonly TimeSpan settleTime;
    private readonly Dictionary<int, ObservedName> activeNames = [];
    private DateTime lastTitleSeenUtc = DateTime.MinValue;
    private int screenGeneration;
    private bool introPending;
    private string promptState = string.Empty;
    private string passivePromptState = string.Empty;
    private string promptInstruction = string.Empty;
    private string screenTitle = string.Empty;
    private PendingSelection? pendingSelection;
    private PendingSpeech? pendingStatus;
    private string? reserveName;
    private DateTime reserveNameSeenUtc = DateTime.MinValue;
    private string lastCursorKey = string.Empty;
    private string lastSpokenKey = string.Empty;

    public PartyFormationSpeechTracker(TimeSpan settleTime)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
    }

    public void ObserveDraw(MenuTextRenderEntry entry, int currentModule, DateTime now)
    {
        lock (sync)
        {
            if (currentModule != MenuModule || now.Kind != DateTimeKind.Utc)
            {
                ResetCore();
                return;
            }

            var text = entry.Text.Trim();
            if (IsReformTitle(entry, text))
            {
                if (!IsActiveCore(now))
                {
                    BeginScreen(text);
                }
                else
                {
                    screenTitle = text;
                }

                lastTitleSeenUtc = now;
                return;
            }

            if (!IsActiveCore(now))
            {
                return;
            }

            // An exact root-menu draw means module 5 has already left Reform.
            if (entry.Context == RootMainMenuContext)
            {
                ResetCore();
                return;
            }

            if (TryClassifyPrompt(entry, text, out var prompt))
            {
                ObservePrompt(prompt, now);
                return;
            }

            if (TryMapActiveNameSlot(entry, out var activeSlot) && text.Length > 0)
            {
                var changed = !activeNames.TryGetValue(activeSlot, out var previous) ||
                    !string.Equals(previous.Text, text, StringComparison.Ordinal);
                activeNames[activeSlot] = new ObservedName(text, now);
                if (changed &&
                    pendingSelection is null &&
                    TryParseCurrentCursor(out var current) &&
                    current.Kind == SelectionKind.ActiveParty &&
                    current.Index == activeSlot)
                {
                    pendingSelection = current with { SeenAtUtc = now };
                }

                return;
            }

            if (IsReserveHighlightedName(entry) &&
                text.Length > 0 &&
                pendingSelection is { Kind: SelectionKind.Reserve } pending &&
                now >= pending.SeenAtUtc)
            {
                reserveName = text;
                reserveNameSeenUtc = now;
            }
        }
    }

    public void ObserveCursor(MenuCursorDrawObservation cursor, DateTime now)
    {
        lock (sync)
        {
            if (cursor.CurrentModule != MenuModule || now.Kind != DateTimeKind.Utc)
            {
                ResetCore();
                return;
            }

            if (!IsActiveCore(now) || !TryClassifyCursor(cursor, out var kind, out var index))
            {
                return;
            }

            var cursorKey = $"{screenGeneration}:{kind}:{index}";
            if (string.Equals(cursorKey, lastCursorKey, StringComparison.Ordinal))
            {
                return;
            }

            lastCursorKey = cursorKey;
            pendingSelection = new PendingSelection(kind, index, cursorKey, now);
            reserveName = null;
            reserveNameSeenUtc = DateTime.MinValue;
        }
    }

    public bool IsActive(DateTime now)
    {
        lock (sync)
        {
            return IsActiveCore(now);
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            if (!IsActiveCore(now))
            {
                ResetCore();
                return null;
            }

            if (pendingStatus is { } status && now - status.SeenAtUtc >= settleTime)
            {
                pendingStatus = null;
                return Emit(status.Text, status.Key);
            }

            if (pendingSelection is not { } selection ||
                now - selection.SeenAtUtc < SelectionSettleTime())
            {
                return null;
            }

            pendingSelection = null;
            var selectionText = selection.Kind switch
            {
                SelectionKind.ActiveParty => FormatActivePartySelection(selection.Index),
                SelectionKind.Reserve when reserveName is { Length: > 0 } &&
                    reserveNameSeenUtc >= selection.SeenAtUtc => $"Available member, {reserveName}.",
                SelectionKind.Reserve => "Empty.",
                _ => null
            };
            if (selectionText is null)
            {
                return null;
            }

            var speech = selectionText;
            var key = $"{selection.Key}:{selectionText}";
            if (introPending)
            {
                var instruction = promptInstruction.Length > 0
                    ? promptInstruction
                    : "Press Start when finished.";
                var title = screenTitle.Length > 0 ? screenTitle : "Reform";
                speech = $"{title}. {selectionText} {instruction}";
                key = $"{key}:intro:{promptState}";
                introPending = false;
            }

            return Emit(speech, key);
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            ResetCore();
        }
    }

    private void ObservePrompt(PromptObservation prompt, DateTime now)
    {
        // Ghidra FUN_00700c90 shows that the normal selection instruction and
        // temporary party-validation text share the same draw coordinates.
        // The renderer returns to that normal instruction after a validation
        // message. It is not a second status transition and must not re-arm
        // the validation message on every draw cycle.
        if (prompt.IsPassiveInstruction || passivePromptState.Length == 0)
        {
            promptInstruction = prompt.Instruction;
            if (passivePromptState.Length == 0)
            {
                passivePromptState = prompt.State;
                promptState = prompt.State;
            }

            return;
        }

        if (string.Equals(prompt.State, passivePromptState, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(promptState, prompt.State, StringComparison.Ordinal))
        {
            return;
        }

        var previous = promptState;
        promptState = prompt.State;
        if (previous.Length == 0)
        {
            return;
        }

        pendingStatus = new PendingSpeech(
            prompt.TransitionSpeech,
            $"{screenGeneration}:prompt:{prompt.State}",
            now);
    }

    private string FormatActivePartySelection(int slot)
    {
        if (activeNames.TryGetValue(slot, out var observed) &&
            lastTitleSeenUtc - observed.SeenAtUtc <= ScreenEvidenceWindow)
        {
            return $"Party slot {slot + 1}, {observed.Text}.";
        }

        return $"Party slot {slot + 1}, empty.";
    }

    private string? Emit(string text, string key)
    {
        if (string.Equals(key, lastSpokenKey, StringComparison.Ordinal))
        {
            return null;
        }

        lastSpokenKey = key;
        return text;
    }

    private bool TryParseCurrentCursor(out PendingSelection selection)
    {
        if (pendingSelection is { } pending)
        {
            selection = pending;
            return true;
        }

        if (lastCursorKey.Length > 0)
        {
            var parts = lastCursorKey.Split(':');
            if (parts.Length == 3 &&
                Enum.TryParse(parts[1], out SelectionKind kind) &&
                int.TryParse(parts[2], out var index))
            {
                selection = new PendingSelection(kind, index, lastCursorKey, lastTitleSeenUtc);
                return true;
            }
        }

        selection = default;
        return false;
    }

    private static bool TryClassifyCursor(
        MenuCursorDrawObservation cursor,
        out SelectionKind kind,
        out int index)
    {
        if (cursor.Context == ActivePartyCursorContext &&
            (TryMapGridIndex(cursor.X, cursor.Y, 0, 120, 1, 137, 1, 3, out index) ||
             TryMapGridIndex(cursor.X, cursor.Y, 0, 61, 1, 69, 1, 3, out index)))
        {
            kind = SelectionKind.ActiveParty;
            return true;
        }

        if (cursor.Context == ReserveCursorContext &&
            (TryMapGridIndex(cursor.X, cursor.Y, 326, 223, 77, 99, 3, 3, out index) ||
             TryMapGridIndex(cursor.X, cursor.Y, 163, 113, 39, 50, 3, 3, out index)))
        {
            kind = SelectionKind.Reserve;
            return true;
        }

        kind = default;
        index = default;
        return false;
    }

    private static bool TryMapActiveNameSlot(MenuTextRenderEntry entry, out int slot)
    {
        slot = default;
        if (entry.Context != PartyTextContext)
        {
            return false;
        }

        return (IsNear(entry.X, 134, 12) && TryMapAxis(entry.Y, 77, 137, 3, 12, out slot)) ||
            (IsNear(entry.X, 67, 8) && TryMapAxis(entry.Y, 39, 69, 3, 8, out slot));
    }

    private static bool IsReserveHighlightedName(MenuTextRenderEntry entry) =>
        entry.Context == ReserveNameContext &&
        ((IsNear(entry.X, 438, 16) && IsNear(entry.Y, 68, 12)) ||
         (IsNear(entry.X, 219, 10) && IsNear(entry.Y, 35, 8)));

    private static bool IsReformTitle(MenuTextRenderEntry entry, string text) =>
        entry.Context == 0 &&
        text.Any(char.IsLetterOrDigit) &&
        ((IsNear(entry.X, 508, 16) && IsNear(entry.Y, 14, 10)) ||
         (IsNear(entry.X, 262, 10) && IsNear(entry.Y, 7, 6)));

    private static bool TryClassifyPrompt(
        MenuTextRenderEntry entry,
        string text,
        out PromptObservation prompt)
    {
        prompt = default;
        if (entry.Context != PartyTextContext ||
            !((IsNear(entry.X, 26, 12) && IsNear(entry.Y, 13, 10)) ||
              (IsNear(entry.X, 13, 8) && IsNear(entry.Y, 7, 6))))
        {
            return false;
        }

        if (string.Equals(text, "Select with START button.", StringComparison.OrdinalIgnoreCase))
        {
            prompt = new PromptObservation(
                "complete",
                "Press Start when finished.",
                "Party complete. Press Start when finished.",
                true);
            return true;
        }

        if (string.Equals(text, "Select with Menu button.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Select with [MENU] button.", StringComparison.OrdinalIgnoreCase))
        {
            prompt = new PromptObservation(
                "select-instruction",
                text,
                text,
                true);
            return true;
        }

        if (string.Equals(text, "Please make a party of three.", StringComparison.OrdinalIgnoreCase))
        {
            prompt = new PromptObservation(
                "incomplete",
                "Please make a party of three.",
                "Please make a party of three.",
                false);
            return true;
        }

        if (text.Length < 2 || !text.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        prompt = new PromptObservation($"native:{text}", text, text, false);
        return true;
    }

    private static bool TryMapGridIndex(
        int x,
        int y,
        int originX,
        int originY,
        int strideX,
        int strideY,
        int columns,
        int rows,
        out int index)
    {
        if (!TryMapAxis(x, originX, strideX, columns, 10, out var column) ||
            !TryMapAxis(y, originY, strideY, rows, 10, out var row))
        {
            index = default;
            return false;
        }

        index = row * columns + column;
        return true;
    }

    private static bool TryMapAxis(
        int value,
        int origin,
        int stride,
        int count,
        int tolerance,
        out int index)
    {
        index = (int)Math.Round((double)(value - origin) / stride, MidpointRounding.AwayFromZero);
        return index >= 0 && index < count && Math.Abs(value - (origin + index * stride)) <= tolerance;
    }

    private static bool TryMapAxis(
        uint value,
        int origin,
        int stride,
        int count,
        int tolerance,
        out int index)
    {
        if (value > int.MaxValue)
        {
            index = default;
            return false;
        }

        return TryMapAxis((int)value, origin, stride, count, tolerance, out index);
    }

    private static bool IsNear(uint actual, int expected, int tolerance) =>
        actual <= int.MaxValue && Math.Abs((int)actual - expected) <= tolerance;

    private TimeSpan SelectionSettleTime() =>
        settleTime > MinimumSelectionSettle ? settleTime : MinimumSelectionSettle;

    private bool IsActiveCore(DateTime now) =>
        now.Kind == DateTimeKind.Utc &&
        lastTitleSeenUtc != DateTime.MinValue &&
        now >= lastTitleSeenUtc &&
        now - lastTitleSeenUtc <= ScreenEvidenceWindow;

    private void BeginScreen(string title)
    {
        screenGeneration++;
        introPending = true;
        promptState = string.Empty;
        passivePromptState = string.Empty;
        promptInstruction = string.Empty;
        screenTitle = title;
        pendingSelection = null;
        pendingStatus = null;
        reserveName = null;
        reserveNameSeenUtc = DateTime.MinValue;
        activeNames.Clear();
        lastCursorKey = string.Empty;
        lastSpokenKey = string.Empty;
    }

    private void ResetCore()
    {
        lastTitleSeenUtc = DateTime.MinValue;
        introPending = false;
        promptState = string.Empty;
        passivePromptState = string.Empty;
        promptInstruction = string.Empty;
        screenTitle = string.Empty;
        pendingSelection = null;
        pendingStatus = null;
        reserveName = null;
        reserveNameSeenUtc = DateTime.MinValue;
        activeNames.Clear();
        lastCursorKey = string.Empty;
        lastSpokenKey = string.Empty;
    }

    private enum SelectionKind
    {
        ActiveParty,
        Reserve
    }

    private readonly record struct ObservedName(string Text, DateTime SeenAtUtc);

    private readonly record struct PendingSelection(
        SelectionKind Kind,
        int Index,
        string Key,
        DateTime SeenAtUtc);

    private readonly record struct PendingSpeech(string Text, string Key, DateTime SeenAtUtc);

    private readonly record struct PromptObservation(
        string State,
        string Instruction,
        string TransitionSpeech,
        bool IsPassiveInstruction);
}
