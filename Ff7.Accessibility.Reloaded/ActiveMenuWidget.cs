namespace Ff7.Accessibility.Reloaded;

public sealed class ActiveMenuWidgetFrameBridge
{
    private readonly ActiveMenuWidgetReader reader;
    private readonly ActiveMenuFrameSpeechCoordinator coordinator;
    private readonly Func<ActiveMenuWidgetSnapshot, ActiveMenuWidgetSnapshot>? enrichSnapshot;

    public ActiveMenuWidgetFrameBridge(
        ActiveMenuWidgetReader reader,
        ActiveMenuFrameSpeechCoordinator coordinator,
        Func<ActiveMenuWidgetSnapshot, ActiveMenuWidgetSnapshot>? enrichSnapshot = null)
    {
        this.reader = reader;
        this.coordinator = coordinator;
        this.enrichSnapshot = enrichSnapshot;
    }

    public ActiveMenuWidgetSnapshot? CompleteBeforeUpdate(
        int address,
        DateTime now,
        Action nativeUpdate)
    {
        ActiveMenuWidgetSnapshot? captured = null;
        try
        {
            if (reader.TryRead(address, out var snapshot))
            {
                captured = enrichSnapshot?.Invoke(snapshot) ?? snapshot;
                coordinator.CompleteFrame(captured.Value, now);
            }
        }
        finally
        {
            nativeUpdate();
        }

        return captured;
    }
}
