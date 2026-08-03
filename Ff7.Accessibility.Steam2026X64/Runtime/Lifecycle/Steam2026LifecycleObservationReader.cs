using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Lifecycle;

/// <summary>
/// Produces a pointer-free lifecycle observation from coherent translated
/// module state, Win32 foreground ownership, and an explicit unload signal.
/// This research reader installs no hooks and enables no runtime capability.
/// </summary>
public sealed class Steam2026LifecycleObservationReader
{
    private readonly object revisionLock = new();
    private readonly Func<(bool Success, byte Module)> readModule;
    private readonly Func<bool> isForeground;
    private LifecycleSnapshot? lastSnapshot;
    private int revision;
    private int shuttingDown;

    public Steam2026LifecycleObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
    {
        var addressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory ?? throw new ArgumentNullException(nameof(memory)));
        var foreground = Steam2026ForegroundInputAdapter.CreateCurrentProcess(fingerprint);
        readModule = () =>
        {
            var success = addressSpace.TryReadByte(
                (uint)FieldPositionReader.AddressCurrentModule,
                out var module);
            return (success, module);
        };
        isForeground = foreground.IsCurrentProcessForeground;
    }

    internal Steam2026LifecycleObservationReader(
        Func<(bool Success, byte Module)> readModule,
        Func<bool> isForeground)
    {
        this.readModule = readModule ?? throw new ArgumentNullException(nameof(readModule));
        this.isForeground = isForeground ?? throw new ArgumentNullException(nameof(isForeground));
    }

    public void BeginShutdown()
    {
        // Shutdown publication participates in the same transaction as the
        // coherent double-capture and revision commit. Once this method
        // returns, no pre-shutdown snapshot can still escape TryRead.
        lock (revisionLock)
        {
            Volatile.Write(ref shuttingDown, 1);
        }
    }

    public bool TryRead(out GameLifecycleObservation observation)
    {
        observation = null!;
        lock (revisionLock)
        {
            if (!TryCapture(out var before) ||
                !TryCapture(out var after) ||
                before != after)
            {
                return false;
            }

            if (lastSnapshot != before)
            {
                if (revision == int.MaxValue)
                {
                    return false;
                }

                revision++;
                lastSnapshot = before;
            }

            observation = new GameLifecycleObservation(
                before.IsForeground,
                before.IsShuttingDown,
                before.Module,
                revision);
            return true;
        }
    }

    private bool TryCapture(out LifecycleSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            var module = readModule();
            if (!module.Success)
            {
                return false;
            }

            snapshot = new LifecycleSnapshot(
                module.Module,
                isForeground(),
                Volatile.Read(ref shuttingDown) != 0);
            return true;
        }
        catch
        {
            // Native observation failures are absence of evidence, never
            // plausible default lifecycle state.
            snapshot = default;
            return false;
        }
    }

    private readonly record struct LifecycleSnapshot(
        byte Module,
        bool IsForeground,
        bool IsShuttingDown);
}
