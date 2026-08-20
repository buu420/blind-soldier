namespace Ff7.Accessibility.Reloaded;

public sealed class BattleMenuFrameSpeechCoordinator
{
    private readonly object sync = new();
    private short frameState = -1;
    private string? pending;
    private readonly Dictionary<short, string> lastSelectionKeys = new();
    private int lastActorSlot = -1;
    private bool rootCommandMenuActive;

    public void ObserveRootCommandMenuActive(bool active)
    {
        lock (sync)
        {
            if (rootCommandMenuActive && !active)
            {
                lastSelectionKeys.Clear();
                lastActorSlot = -1;
            }

            rootCommandMenuActive = active;
        }
    }

    public void BeginFrame(short rendererState)
    {
        lock (sync)
        {
            frameState = rendererState;
        }
    }

    public void ObserveDraw(MenuTextRenderEntry entry)
    {
        // Battle selection speech is intentionally driven by native menu state.
    }

    public void ObserveCursor(MenuCursorDrawObservation cursor)
    {
        // The battle cursor is rendered outside some menu callbacks and is not a selection signal.
    }

    public void CompleteFrame(BattleMenuStateSnapshot snapshot)
    {
        lock (sync)
        {
            pending = null;
            if (!snapshot.IsValid || snapshot.RendererState != frameState ||
                snapshot.Selection is not { } selection)
            {
                if (!snapshot.IsValid)
                {
                    lastSelectionKeys.Remove(frameState);
                }

                return;
            }

            var selectionKey = $"{snapshot.PartySlot}\u001f{frameState}\u001f{selection.SlotIndex}\u001f{selection.EntryId}\u001f{selection.Name}\u001f{selection.IsAvailable}";
            if (lastSelectionKeys.TryGetValue(frameState, out var lastSelectionKey) &&
                string.Equals(selectionKey, lastSelectionKey, StringComparison.Ordinal))
            {
                return;
            }

            var body = FormatSelection(selection.Name.Trim(), selection);
            if (!selection.IsAvailable)
            {
                body = $"{body}. Unavailable in battle";
            }

            var description = selection.Description;
            if (!string.IsNullOrWhiteSpace(description) &&
                !string.Equals(description, selection.Name, StringComparison.Ordinal))
            {
                body = $"{body}. {description}";
            }

            pending = snapshot.PartySlot != lastActorSlot
                ? $"{snapshot.Actor.Name}. {body}"
                : body;
            lastActorSlot = snapshot.PartySlot;
            lastSelectionKeys[frameState] = selectionKey;
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            var result = pending;
            pending = null;
            return result;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            frameState = -1;
            pending = null;
            lastSelectionKeys.Clear();
            lastActorSlot = -1;
            rootCommandMenuActive = false;
        }
    }

    private static string FormatSelection(
        string name,
        BattleMenuSelectionSnapshot nativeSelection)
    {
        if (nativeSelection.Quantity is { } quantity)
        {
            return $"{name} x{quantity}";
        }

        return nativeSelection.MpCost is { } mpCost
            ? $"{name}. {mpCost} MP"
            : name;
    }
}
