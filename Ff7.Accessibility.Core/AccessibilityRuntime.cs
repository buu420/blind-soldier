using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Core;

public sealed class AccessibilityRuntime : IDisposable
{
    private readonly IFf7RuntimeBackend backend;
    private readonly RuntimeEventQueue queue;
    private readonly RuntimeEventDispatcher dispatcher;
    private readonly Action<string> log;
    private readonly object lifecycleGate = new();
    private bool started;
    private bool disposed;
    private bool failedClosed;

    public AccessibilityRuntime(
        IFf7RuntimeBackend backend,
        AccessibilityConfig config,
        IAccessibilityOutput output,
        Action<string> log)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        queue = new RuntimeEventQueue();
        dispatcher = new RuntimeEventDispatcher(
            config ?? throw new ArgumentNullException(nameof(config)),
            output ?? throw new ArgumentNullException(nameof(output)),
            TryLog);
    }

    public RuntimeCapabilityReport Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failedClosed)
            {
                throw new InvalidOperationException(
                    "The accessibility runtime failed closed and cannot be restarted in this process.");
            }

            var report = AddDispatcherCapabilityFailures(
                backend.ValidateCapabilities(),
                dispatcher.HandledCapabilities);
            if (!report.Failures.IsEmpty)
            {
                TryLog($"Runtime {report.Identity.RuntimeId} failed the capability/dispatcher gate.");
                foreach (var failure in report.Failures)
                {
                    TryLog($"Missing {failure.Capability}: {failure.Signal}: {failure.Diagnostic}");
                }

                return report;
            }

            if (!started)
            {
                try
                {
                    backend.Start(queue);
                    started = true;
                }
                catch (Exception ex)
                {
                    FailClosed("backend start failure", ex);
                    throw;
                }
            }

            return report;
        }
    }

    public void Tick(DateTime utcNow)
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started)
            {
                return;
            }

            try
            {
                queue.PublishFrame(backend.ReadFrame());
                var batch = queue.Drain();
                if (batch.Degradation is { IsFatal: true } degradation)
                {
                    FailClosed(degradation);
                    return;
                }

                dispatcher.Dispatch(batch, utcNow);
            }
            catch (ObjectDisposedException ex)
            {
                FailClosed("runtime Tick failure", ex);
                throw;
            }
            catch (Exception ex)
            {
                FailClosed("runtime Tick failure", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (lifecycleGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queue.DeactivateAndClear();
            try
            {
                if (started)
                {
                    TryStopBackend("during disposal");
                }
            }
            finally
            {
                started = false;
                if (!dispatcher.Cleanup("during disposal"))
                {
                    TryLog(
                        "Accessibility cue cleanup remains incomplete after the bounded disposal retry; " +
                        "the disposed runtime retains cue ownership and will attempt no further output.");
                }

                try
                {
                    backend.Dispose();
                }
                catch (Exception ex)
                {
                    TryLog($"Runtime backend disposal failed: {ex.Message}");
                }
            }
        }
    }

    private void FailClosed(RuntimeQueueDegradation degradation)
    {
        FailClosed(
            $"Runtime failed closed after {degradation.Kind}: " +
            $"rejected={degradation.RejectedEventCount}, last={degradation.LastRejectedEventType}.",
            "after fatal runtime queue degradation");
    }

    private void FailClosed(string context, Exception exception)
    {
        FailClosed(
            $"Runtime failed closed after {context}: " +
            $"{exception.GetType().Name}: {exception.Message}",
            $"after {context}");
    }

    private void FailClosed(string diagnostic, string stopContext)
    {
        failedClosed = true;
        queue.DeactivateAndClear();
        TryLog(diagnostic);
        try
        {
            TryStopBackend(stopContext);
        }
        finally
        {
            started = false;
            dispatcher.Cleanup("during fail-closed cleanup");
        }
    }

    private void TryStopBackend(string context)
    {
        try
        {
            backend.Stop();
        }
        catch (Exception ex)
        {
            TryLog($"Runtime backend stop failed {context}: {ex.Message}");
        }
    }

    private void TryLog(string message)
    {
        try
        {
            log(message);
        }
        catch
        {
            // Logging must never reopen a fail-closed exception path.
        }
    }

    private static RuntimeCapabilityReport AddDispatcherCapabilityFailures(
        RuntimeCapabilityReport report,
        RuntimeCapability handledCapabilities)
    {
        ArgumentNullException.ThrowIfNull(report);

        var failures = report.Failures.ToList();
        if (report.Available == RuntimeCapability.None)
        {
            failures.Add(new RuntimeCapabilityFailure(
                RuntimeCapability.None,
                "runtime-capabilities",
                "The backend advertised no accessibility capabilities."));
        }

        var undispatched = report.Available & ~handledCapabilities;
        RuntimeCapability[] atomicCapabilities =
        [
            RuntimeCapability.Lifecycle,
            RuntimeCapability.ForegroundInput,
            RuntimeCapability.Menus,
            RuntimeCapability.Dialogue,
            RuntimeCapability.Field,
            RuntimeCapability.Navigation,
            RuntimeCapability.Battle,
            RuntimeCapability.Movies,
            RuntimeCapability.Saves
        ];
        foreach (var capability in atomicCapabilities)
        {
            if ((undispatched & capability) == RuntimeCapability.None)
            {
                continue;
            }

            failures.Add(new RuntimeCapabilityFailure(
                capability,
                "runtime-event-dispatcher",
                $"No runtime event dispatcher handles {capability}."));
            undispatched &= ~capability;
        }

        if (undispatched != RuntimeCapability.None)
        {
            failures.Add(new RuntimeCapabilityFailure(
                undispatched,
                "runtime-event-dispatcher",
                $"No runtime event dispatcher handles unknown capability bits 0x{(int)undispatched:X8}."));
        }

        return failures.Count == report.Failures.Length
            ? report
            : new RuntimeCapabilityReport(report.Identity, report.Available, failures);
    }
}
