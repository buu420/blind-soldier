namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Owns the scripted Reform/PHS party screen from its exact native title,
/// rendered character names, and Ghidra-verified cursor contexts.
/// </summary>
public sealed class PartyFormationSpeechTracker
{
    public const int FieldModule = 1;
    public const int WorldMapModule = 3;
    public const int MenuModule = 5;

    /// <summary>
    /// Not the PHS module. Module 19 is the quit/game-over state; PHS cannot be
    /// open there. Retained only because the x64 bridge still references it.
    /// </summary>
    public const int PhsModule = 19;

    private const int RootMainMenuContext = 0x3A83126F;
    private const int PartyTextContext = 0x3DCCCCCD;
    private const int ReserveNameContext = 0x3DCED917;
    private const int ActivePartyCursorContext = 0x3DCF0D84;
    private const int ReserveCursorContext = 0x3DCD0679;
    private static readonly TimeSpan ScreenEvidenceWindow = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MinimumSelectionSettle = TimeSpan.FromMilliseconds(75);

    private readonly object sync = new();
    private readonly TimeSpan settleTime;

    // The reserve grid is drawn as character portraits, never as text, so the
    // only way to name the cell under the cursor is to read the native roster.
    private readonly Func<int, string?>? resolveReserveName;
    private readonly Dictionary<int, ObservedName> activeNames = [];
    private DateTime lastTitleSeenUtc = DateTime.MinValue;
    private DateTime lastPartyNameDrawUtc = DateTime.MinValue;

    // The prompt draws before the cursor that opens the screen, so it has to be
    // held aside and adopted on open or the first announcement loses it.
    private string candidatePromptInstruction = string.Empty;
    private DateTime candidatePromptUtc = DateTime.MinValue;
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
        : this(settleTime, null)
    {
    }

    public PartyFormationSpeechTracker(
        TimeSpan settleTime,
        Func<int, string?>? resolveReserveName)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
        this.resolveReserveName = resolveReserveName;
    }

    public void ObserveDraw(MenuTextRenderEntry entry, int currentModule, DateTime now)
    {
        lock (sync)
        {
            if (!IsPartyModule(currentModule) || now.Kind != DateTimeKind.Utc)
            {
                ResetCore();
                return;
            }

            var text = entry.Text.Trim();

            // The three active-party names are the only part of the PHS layout that
            // reaches the text hook every frame. Record them even before the screen
            // is known to be open: they are what corroborates the active-party
            // cursor, whose context FUN_006c7b54 and FUN_006c885e also use.
            if (TryMapActiveNameSlot(entry, out var activeSlot) && text.Length > 0)
            {
                var changed = !activeNames.TryGetValue(activeSlot, out var previous) ||
                    !string.Equals(previous.Text, text, StringComparison.Ordinal);
                activeNames[activeSlot] = new ObservedName(text, now);
                lastPartyNameDrawUtc = now;
                if (IsActiveCore(now))
                {
                    lastTitleSeenUtc = now;
                    if (changed &&
                        pendingSelection is null &&
                        TryParseCurrentCursor(out var current) &&
                        current.Kind == SelectionKind.ActiveParty &&
                        current.Index == activeSlot)
                    {
                        pendingSelection = current with { SeenAtUtc = now };
                    }
                }

                return;
            }

            if (IsReformTitle(entry, text))
            {
                if (!IsActiveCore(now))
                {
                    BeginScreen(text, now);
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
                // Hold the prompt aside without acting on it. If this turns out
                // not to be PHS the screen never opens and it is never used.
                if (TryClassifyPrompt(entry, text, out var candidate))
                {
                    candidatePromptInstruction = candidate.Instruction;
                    candidatePromptUtc = now;
                }

                return;
            }

            // A root-menu draw does NOT mean the screen closed. Runtime logs show
            // the selected main-menu label still rendering at 508,13 on every PHS
            // frame, so treating it as an exit reset the screen once per frame and
            // guaranteed silence. Ownership is released by the evidence window
            // instead: once PHS stops drawing, nothing refreshes it.
            if (TryClassifyPrompt(entry, text, out var prompt))
            {
                // The PHS prompt redraws every frame, so it is what keeps the
                // screen alive while the player sits still on one selection.
                lastTitleSeenUtc = now;
                ObservePrompt(prompt, now);
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
            if (!IsPartyModule(cursor.CurrentModule) || now.Kind != DateTimeKind.Utc)
            {
                ResetCore();
                return;
            }

            if (!TryClassifyCursor(cursor, out var kind, out var index))
            {
                return;
            }

            if (!IsActiveCore(now))
            {
                // 0x3DCD0679 is passed to the cursor renderer by FUN_00700c90 and
                // by nothing else in the binary, so the reserve grid can open the
                // screen on its own. The active-party context is shared with two
                // other menus, so it additionally needs the PHS name layout.
                if (kind != SelectionKind.Reserve && !IsRecent(lastPartyNameDrawUtc, now))
                {
                    return;
                }

                BeginScreen(screenTitle, now);
            }

            lastTitleSeenUtc = now;

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
                SelectionKind.Reserve => FormatReserveSelection(selection.Index, selection.SeenAtUtc),
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
                var title = screenTitle.Length > 0 ? screenTitle : "PHS";
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

    /// <summary>
    /// The reserve grid renders each member as a portrait, so a sighted player
    /// reads a face where the text hook sees nothing. The native roster array is
    /// the only source for that name.
    /// </summary>
    private string FormatReserveSelection(int index, DateTime seenAtUtc)
    {
        var resolved = resolveReserveName?.Invoke(index);
        if (resolved is { Length: > 0 })
        {
            return $"Available member, {resolved}.";
        }

        if (reserveName is { Length: > 0 } && reserveNameSeenUtc >= seenAtUtc)
        {
            return $"Available member, {reserveName}.";
        }

        return "Empty.";
    }

    private static bool IsRecent(DateTime stamp, DateTime now) =>
        stamp != DateTime.MinValue &&
        now >= stamp &&
        now - stamp <= ScreenEvidenceWindow;

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

    /// <summary>
    /// PHS has no module of its own: it runs inside whatever module raised it.
    /// Opening it from the world map keeps module 3, from a field keeps module 1,
    /// and module 5 is the dedicated menu module. Runtime logs show real PHS
    /// sessions under module 3, which the old 5-or-19 gate rejected outright.
    /// <see cref="PhsModule"/> is quit/game-over and is deliberately absent.
    /// </summary>
    private static bool IsPartyModule(int module) =>
        module is FieldModule or WorldMapModule or MenuModule;

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

    private void BeginScreen(string title, DateTime now)
    {
        screenGeneration++;
        introPending = true;
        promptState = string.Empty;
        passivePromptState = string.Empty;

        // Adopt the prompt seen just before the screen opened; without it the
        // very first announcement loses the instruction the player can see.
        promptInstruction = IsRecent(candidatePromptUtc, now) ? candidatePromptInstruction : string.Empty;
        screenTitle = title;
        pendingSelection = null;
        pendingStatus = null;
        reserveName = null;
        reserveNameSeenUtc = DateTime.MinValue;

        // activeNames is deliberately kept: the name draws that corroborated this
        // screen arrive before it opens, and every entry is timestamp-guarded.
        lastCursorKey = string.Empty;
        lastSpokenKey = string.Empty;
    }

    private void ResetCore()
    {
        lastTitleSeenUtc = DateTime.MinValue;
        lastPartyNameDrawUtc = DateTime.MinValue;
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
