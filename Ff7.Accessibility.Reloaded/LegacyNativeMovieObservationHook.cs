using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded;

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint LegacyMovieUpdateSampleDelegate(nint surface);

internal readonly record struct LegacyNativeMovieObservation(
    NativeMoviePlaybackSample? Sample, long Revision, DateTime ObservedUtc);

/// <summary>
/// Observes on the thread that already updates the game's decoder. The original
/// receives its argument exactly once and its return bits are preserved. The
/// managed worker drains a single replaceable immutable observation, including
/// null/rejected samples; it never calls the stock COM getter itself.
/// </summary>
internal sealed class LegacyNativeMovieObservationHook : IDisposable
{
    private readonly object sync = new();
    private readonly Func<nint, uint> original;
    private readonly Func<NativeMoviePlaybackSample?> observe;
    private LegacyMovieUpdateSampleDelegate? detour;
    private IHook<LegacyMovieUpdateSampleDelegate>? hook;
    private LegacyNativeMovieObservation latest;
    private bool pending;
    private long revision;
    private int disposed;
    // Keep the reverse-P/Invoke delegate alive for the process lifetime. Disabling
    // a detour does not prove that a native thread has not already entered it.
    private GCHandle nativeRoot;
    internal string LastDiagnostic { get; private set; } = string.Empty;

    internal LegacyNativeMovieObservationHook(Func<nint, uint> original,
        Func<NativeMoviePlaybackSample?> observe)
    {
        this.original = original ?? throw new ArgumentNullException(nameof(original));
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
    }

    internal static bool TryInstall(IReloadedHooks hooks, LegacyNativeMoviePlaybackReader reader,
        out LegacyNativeMovieObservationHook observer, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(reader);
        observer = null!;
        diagnostic = "Native movie observation hook requires the legacy x86 game thread.";
        if (Environment.Is64BitProcess || !reader.TryResolveObservationTarget(out var target, out diagnostic)) return false;
        LegacyNativeMovieObservationHook? candidate = null;
        try
        {
            IHook<LegacyMovieUpdateSampleDelegate>? nativeHook = null;
            candidate = new(surface => nativeHook!.OriginalFunction(surface),
                () => reader.TryRead(out var sample) ? sample : null);
            candidate.detour = candidate.Invoke;
            nativeHook = hooks.CreateHook<LegacyMovieUpdateSampleDelegate>(candidate.detour, target, -1);
            candidate.hook = nativeHook;
            candidate.nativeRoot = GCHandle.Alloc(candidate);
            nativeHook.Activate();
            observer = candidate;
            diagnostic = $"Installed checked movie update observation at 0x{target:X8}; decoder behavior is forwarded unchanged.";
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
            diagnostic = $"Native movie observation hook could not be installed: {ex.Message}";
            return false;
        }
    }

    internal uint Invoke(nint surface)
    {
        var result = original(surface);
        if (Volatile.Read(ref disposed) != 0) return result;
        NativeMoviePlaybackSample? sample = null;
        try { sample = observe(); }
        catch (Exception ex) { LastDiagnostic = $"Native movie observation rejected: {ex.Message}"; }
        lock (sync)
        {
            if (disposed == 0)
            {
                latest = new(sample, ++revision, DateTime.UtcNow);
                pending = true;
            }
        }
        return result;
    }

    internal bool TryDrain(out LegacyNativeMovieObservation observation)
    {
        lock (sync)
        {
            observation = default;
            if (disposed != 0 || !pending) return false;
            observation = latest;
            pending = false;
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lock (sync) { pending = false; latest = default; }
        try
        {
            if (hook is { IsHookActivated: true }) hook.Disable();
            // The root also protects an already-entered callback after Disable.
        }
        catch (Exception ex)
        {
            LastDiagnostic = $"Native movie observer detour removal failed; its managed delegate remains rooted: {ex.Message}";
        }
    }
}
