using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// Routes only exact translated title-load widgets and renderer text into the
/// shared Continue-screen tracker. Lifecycle ownership is supplied separately
/// so delayed callbacks cannot leak speech after leaving module 20.
/// </summary>
internal sealed class Steam2026TitleLoadMenuSpeechBridge
{
    private readonly TitleLoadMenuSpeechTracker tracker;
    private bool lifecycleOwned;
    private long lastSequence;

    internal Steam2026TitleLoadMenuSpeechBridge(
        TimeSpan settleTime,
        Func<int, bool?> saveFileHasData,
        Func<int, int, Ff7SaveSlotPreview?> readGame)
    {
        tracker = new TitleLoadMenuSpeechTracker(
            settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime,
            saveFileHasData ?? throw new ArgumentNullException(nameof(saveFileHasData)),
            readGame ?? throw new ArgumentNullException(nameof(readGame)));
    }

    internal bool HasOwnership => lifecycleOwned && tracker.IsActive;

    internal void SetOwnership(bool ownsTitleModule)
    {
        if (ownsTitleModule == lifecycleOwned)
        {
            return;
        }

        lifecycleOwned = ownsTitleModule;
        lastSequence = 0;
        if (!ownsTitleModule)
        {
            tracker.ObserveModule(-1);
        }
    }

    internal void Observe(TranslatedMenuIngressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!lifecycleOwned || snapshot.Sequence <= lastSequence ||
            snapshot.TimestampUtc.Kind != DateTimeKind.Utc)
        {
            return;
        }

        lastSequence = snapshot.Sequence;
        switch (snapshot.CallbackKind)
        {
            case Steam2026MenuCallbackKind.ActiveWidgetUpdate
                when snapshot.ActiveWidget is { } widget &&
                    snapshot.Cursor is null && snapshot.Text is null &&
                    widget.WidgetIdentity is
                        TitleLoadMenuSpeechTracker.SaveFileWidgetAddress or
                        TitleLoadMenuSpeechTracker.TitleRootWidgetAddress:
                tracker.ObserveWidget(
                    new ActiveMenuWidgetSnapshot(
                        widget.WidgetIdentity,
                        widget.VerifiedName,
                        widget.Kind,
                        widget.First,
                        widget.Cursor,
                        widget.Columns,
                        widget.Rows,
                        widget.ScrollOffset,
                        widget.ScrollDelta,
                        widget.ScrollState),
                    TitleMenuCursorReader.TitleModule,
                    snapshot.TimestampUtc);
                break;

            case Steam2026MenuCallbackKind.EncodedTextA:
            case Steam2026MenuCallbackKind.EncodedTextB:
            case Steam2026MenuCallbackKind.AsciiRenderer:
                if (snapshot.Text is { } text && text.Source == snapshot.CallbackKind &&
                    snapshot.Cursor is null && snapshot.ActiveWidget is null)
                {
                    tracker.ObserveDraw(
                        new MenuTextRenderEntry(
                            text.Text,
                            unchecked((uint)text.X),
                            unchecked((uint)text.Y),
                            text.Color,
                            text.Context),
                        TitleMenuCursorReader.TitleModule,
                        snapshot.TimestampUtc);
                }

                break;
        }
    }

    internal void ObserveState(TitleLoadMenuStateSnapshot snapshot, DateTime now)
    {
        if (!lifecycleOwned || now.Kind != DateTimeKind.Utc)
        {
            return;
        }

        tracker.ObserveState(snapshot, TitleMenuCursorReader.TitleModule, now);
    }

    internal void ResetIngress() => lastSequence = 0;

    internal string? Poll(DateTime now) =>
        lifecycleOwned && now.Kind == DateTimeKind.Utc ? tracker.Poll(now) : null;
}
