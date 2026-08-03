namespace Ff7.Accessibility.Reloaded;

internal enum NavigationProgressHotkeyAction
{
    Toggle,
    PreviousInterval,
    NextInterval
}

internal static class NavigationProgressHotkeyRouter
{
    internal const int VirtualKeyF5 = 0x74;
    internal const int VirtualKeyF6 = 0x75;
    internal const int VirtualKeyF7 = 0x76;

    internal static IReadOnlyList<NavigationProgressHotkeyAction> ReadActions(
        Func<int, bool> observeRisingEdge)
    {
        ArgumentNullException.ThrowIfNull(observeRisingEdge);
        var actions = new List<NavigationProgressHotkeyAction>(3);
        AddIfPressed(VirtualKeyF5, NavigationProgressHotkeyAction.Toggle);
        AddIfPressed(VirtualKeyF6, NavigationProgressHotkeyAction.PreviousInterval);
        AddIfPressed(VirtualKeyF7, NavigationProgressHotkeyAction.NextInterval);
        return actions;

        void AddIfPressed(int virtualKey, NavigationProgressHotkeyAction action)
        {
            if (observeRisingEdge(virtualKey))
            {
                actions.Add(action);
            }
        }
    }
}

internal sealed class NavigationProgressController
{
    private static readonly int[] SupportedIntervals = [5, 10, 15, 20];
    private readonly object sync = new();
    private bool enabled;
    private int intervalPercent;

    internal NavigationProgressController(bool enabled, int intervalPercent)
    {
        this.enabled = enabled;
        this.intervalPercent = NormalizeInterval(intervalPercent);
    }

    internal event Action? Changed;

    internal bool Enabled
    {
        get
        {
            lock (sync)
            {
                return enabled;
            }
        }
    }

    internal int IntervalPercent
    {
        get
        {
            lock (sync)
            {
                return intervalPercent;
            }
        }
    }

    internal int Quantize(int percent)
    {
        lock (sync)
        {
            return Quantize(percent, intervalPercent);
        }
    }

    internal string HandleAction(NavigationProgressHotkeyAction action)
    {
        string speech;
        lock (sync)
        {
            switch (action)
            {
                case NavigationProgressHotkeyAction.Toggle:
                    enabled = !enabled;
                    speech = enabled
                        ? "Navigation progress on."
                        : "Navigation progress off.";
                    break;
                case NavigationProgressHotkeyAction.PreviousInterval:
                    intervalPercent = SelectAdjacentInterval(intervalPercent, -1);
                    speech = $"Navigation progress interval {intervalPercent} percent.";
                    break;
                case NavigationProgressHotkeyAction.NextInterval:
                    intervalPercent = SelectAdjacentInterval(intervalPercent, 1);
                    speech = $"Navigation progress interval {intervalPercent} percent.";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        Changed?.Invoke();
        return speech;
    }

    private static int NormalizeInterval(int intervalPercent) =>
        Array.IndexOf(SupportedIntervals, intervalPercent) >= 0
            ? intervalPercent
            : SupportedIntervals[0];

    private static int SelectAdjacentInterval(int current, int direction)
    {
        var index = Array.IndexOf(SupportedIntervals, current);
        if (index < 0)
        {
            index = 0;
        }

        return SupportedIntervals[
            (index + direction + SupportedIntervals.Length) % SupportedIntervals.Length];
    }

    private static int Quantize(int percent, int intervalPercent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return clamped == 100
            ? 100
            : clamped / intervalPercent * intervalPercent;
    }
}

internal sealed class IntervalFieldNavigationProgressSink :
    IFieldNavigationProgressSink,
    IDisposable
{
    private readonly IFieldNavigationProgressSink inner;
    private readonly NavigationProgressController settings;
    private readonly object sync = new();
    private bool routeActive;
    private bool completed;
    private int currentPercent;
    private bool publishedActive;
    private int publishedPercent = -1;
    private bool disposed;

    internal IntervalFieldNavigationProgressSink(
        IFieldNavigationProgressSink inner,
        NavigationProgressController settings)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        settings.Changed += OnSettingsChanged;
    }

    public void Activate(int percent)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            routeActive = true;
            completed = false;
            currentPercent = Math.Clamp(percent, 0, 99);
            PublishCurrentAsActivation();
        }
    }

    public void SetValue(int percent)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (!routeActive)
            {
                return;
            }

            completed = false;
            currentPercent = Math.Clamp(percent, 0, 99);
            PublishCurrentAsValue();
        }
    }

    public void Complete()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            routeActive = true;
            completed = true;
            currentPercent = 100;
            if (!settings.Enabled)
            {
                return;
            }

            inner.Complete();
            publishedActive = true;
            publishedPercent = 100;
        }
    }

    public void Deactivate()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            routeActive = false;
            completed = false;
            if (publishedActive)
            {
                inner.Deactivate();
            }

            publishedActive = false;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            settings.Changed -= OnSettingsChanged;
        }
    }

    private void OnSettingsChanged()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            if (!settings.Enabled)
            {
                if (publishedActive)
                {
                    inner.Deactivate();
                }

                publishedActive = false;
                return;
            }

            if (!routeActive)
            {
                return;
            }

            if (completed)
            {
                inner.Complete();
                publishedActive = true;
                publishedPercent = 100;
                return;
            }

            PublishCurrentAsValue();
        }
    }

    private void PublishCurrentAsActivation()
    {
        if (!settings.Enabled)
        {
            return;
        }

        var quantized = settings.Quantize(currentPercent);
        inner.Activate(quantized);
        publishedActive = true;
        publishedPercent = quantized;
    }

    private void PublishCurrentAsValue()
    {
        if (!settings.Enabled)
        {
            return;
        }

        var quantized = settings.Quantize(currentPercent);
        if (!publishedActive)
        {
            inner.Activate(quantized);
            publishedActive = true;
            publishedPercent = quantized;
            return;
        }

        if (publishedPercent == quantized)
        {
            return;
        }

        inner.SetValue(quantized);
        publishedPercent = quantized;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
