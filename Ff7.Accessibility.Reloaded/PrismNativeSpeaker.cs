using System.Runtime.InteropServices;
using System.Text;

namespace Ff7.Accessibility.Reloaded;

internal sealed class PrismNativeSpeaker : IDisposable
{
    private readonly Action<string> log;
    private readonly Func<nint, nint, bool, PrismError> output;
    private readonly PrismBackendIsSpeaking isSpeaking;
    private readonly Action<nint> shutdown;
    private readonly object backendSync = new();
    private nint context;
    private nint backend;
    private bool available;

    /// <summary>
    /// Set only when this instance created the backend itself and therefore owns it. Prism's
    /// create functions hand ownership to the caller; the test constructors below are given a
    /// backend they did not create and must not free it.
    /// </summary>
    private Action<nint>? freeBackend;

    /// <summary>
    /// Held for as long as the Prism context lives. Prism keeps a raw function pointer to this
    /// delegate, which does not count as a reference, so dropping it would leave the poll thread
    /// calling into collected memory.
    /// </summary>
    private PrismAvailabilityCallback? availabilityCallback;

    /// <summary>
    /// Set from Prism's poll thread when a screen reader starts or stops, and cleared on the next
    /// line spoken. The player is the one who knows they switched readers; the mod only has to
    /// stop insisting on the reader that was running at launch.
    /// </summary>
    private volatile bool screenReadersChanged;

    /// <summary>What changed, for the log line written when the selection is revisited.</summary>
    private volatile string? lastAvailabilityChange;

    public PrismNativeSpeaker(Action<string> log)
    {
        this.log = log;
        output = Prism.prism_backend_output;
        isSpeaking = Prism.prism_backend_is_speaking;
        shutdown = Prism.prism_shutdown;
        TryInitialize();
    }

    internal PrismNativeSpeaker(
        Action<string> log,
        nint context,
        nint backend,
        Func<nint, nint, bool, PrismError> output,
        Action<nint> shutdown)
        : this(
            log,
            context,
            backend,
            output,
            static (nint _, out bool speaking) =>
            {
                speaking = false;
                return (PrismError)(-1);
            },
            shutdown)
    {
    }

    internal PrismNativeSpeaker(
        Action<string> log,
        nint context,
        nint backend,
        Func<nint, nint, bool, PrismError> output,
        PrismBackendIsSpeaking isSpeaking,
        Action<nint> shutdown)
    {
        this.log = log;
        this.context = context;
        this.backend = backend;
        this.output = output;
        this.isSpeaking = isSpeaking;
        this.shutdown = shutdown;
        available = backend != 0;
    }

    public bool Speak(string text, bool interrupt = true)
    {
        lock (backendSync)
        {
            ReselectIfScreenReadersChanged();

            if (!available || backend == 0)
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(text + "\0");
            unsafe
            {
                fixed (byte* pText = bytes)
                {
                    var result = output(backend, (nint)pText, interrupt);
                    if (result != PrismError.Ok)
                    {
                        log($"Prism output failed: {PrismErrorToString(result)}");
                        return false;
                    }

                    return true;
                }
            }
        }
    }

    public bool TryIsSpeaking(out bool speaking)
    {
        speaking = false;
        lock (backendSync)
        {
            if (!available || backend == 0)
            {
                return false;
            }

            try
            {
                return isSpeaking(backend, out speaking) == PrismError.Ok;
            }
            catch (EntryPointNotFoundException)
            {
                speaking = false;
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (backendSync)
        {
            available = false;
            if (backend != 0)
            {
                freeBackend?.Invoke(backend);
                freeBackend = null;
            }

            if (context != 0)
            {
                // Shutting the context down joins the poll thread, so no further callback can be
                // in flight once this returns. Only then is it safe to drop the delegate.
                shutdown(context);
                context = 0;
            }

            availabilityCallback = null;
            backend = 0;
        }
    }

    private void TryInitialize()
    {
        try
        {
            var config = Prism.prism_config_init();

            // Ask Prism to watch which screen readers are running. Without this the mod picks a
            // backend at launch and keeps it forever, so a player who starts the game before
            // their reader, or switches readers mid-session, gets silence until they restart.
            // Zero means Prism's own default for each of these: a one second base interval and
            // two agreeing samples before a change is believed. The backoff lets a long quiet
            // stretch stop waking the machine, and collapses back to the base interval the
            // moment anything moves.
            availabilityCallback = OnAvailabilityChanged;
            config.AvailabilityCallback = Marshal.GetFunctionPointerForDelegate(availabilityCallback);
            config.AvailabilityPollIntervalMs = 0;
            config.AvailabilityDebounceSamples = 0;
            config.AvailabilityBackoffMaxMs = 8000;
            config.AvailabilityAutoPowerManage = true;

            context = Prism.prism_init(ref config);
            if (context == 0)
            {
                availabilityCallback = null;
                log("Prism initialization returned null.");
                return;
            }

            // Prism walks its registry in descending priority order and returns the first
            // backend that initializes, so a reader that is not running is skipped rather than
            // selected. Whichever screen reader the player actually has is the one that answers.
            backend = Prism.prism_registry_create_best(context);
            if (backend == 0)
            {
                log($"Prism did not find a usable backend. {DescribeRegistry()}");
                return;
            }

            freeBackend = Prism.prism_backend_free;
            var namePtr = Prism.prism_backend_name(backend);
            var name = Marshal.PtrToStringUTF8(namePtr) ?? "<unknown>";
            available = true;

            // Which Prism, and which architecture. The last time a player reported silence it
            // took a disassembler to establish which build they were running, because the DLL
            // carries no version resource and the two runtimes ship different copies.
            log($"Prism {DescribeLibrary()} initialized. Backend: {name}");
        }
        catch (Exception ex)
        {
            log($"Prism initialization failed: {ex}");
        }
    }

    /// <summary>
    /// Prism's version and this process's architecture, for the startup log line.
    /// </summary>
    private static string DescribeLibrary()
    {
        var architecture = nint.Size == 4 ? "x86" : "x64";
        try
        {
            var version = Marshal.PtrToStringUTF8(Prism.prism_version_string());
            return string.IsNullOrWhiteSpace(version) ? architecture : $"{version} ({architecture})";
        }
        catch (EntryPointNotFoundException)
        {
            // Builds before 0.18 do not export a version. Saying so is itself useful.
            return $"pre-0.18 ({architecture})";
        }
    }

    /// <summary>
    /// Runs on Prism's poll thread. It records what happened and returns: the thread cannot scan
    /// again until this returns, and it is not allowed to shut the context down, so the actual
    /// reselection happens on the next line the mod speaks.
    /// </summary>
    private void OnAvailabilityChanged(nint userdata, ulong backendId, nint namePtr, bool isAvailable)
    {
        try
        {
            var name = Marshal.PtrToStringUTF8(namePtr) ?? "a backend";
            lastAvailabilityChange = isAvailable ? $"{name} started" : $"{name} stopped";
            screenReadersChanged = true;
        }
        catch
        {
            // A callback that throws would cross a native frame, which is undefined. Losing one
            // notification only delays reselection to the next transition.
            screenReadersChanged = true;
        }
    }

    /// <summary>
    /// Re-runs the selection when a screen reader has started or stopped since the last line.
    /// Called with <see cref="backendSync"/> held.
    /// </summary>
    private void ReselectIfScreenReadersChanged()
    {
        if (!screenReadersChanged || context == 0 || freeBackend is null)
        {
            return;
        }

        screenReadersChanged = false;
        var change = lastAvailabilityChange ?? "a screen reader changed";

        nint replacement;
        try
        {
            replacement = Prism.prism_registry_create_best(context);
        }
        catch (Exception ex)
        {
            log($"Prism reselection failed after {change}: {ex.Message}");
            return;
        }

        if (replacement == 0)
        {
            // Nothing usable right now. Keep whatever is already held rather than going silent on
            // the strength of one scan; the next transition will bring us back here.
            log($"Prism found no usable backend after {change}. Keeping the current one.");
            return;
        }

        var replacementName = Marshal.PtrToStringUTF8(Prism.prism_backend_name(replacement)) ?? "<unknown>";
        var currentName = backend == 0
            ? null
            : Marshal.PtrToStringUTF8(Prism.prism_backend_name(backend));

        if (backend != 0 && string.Equals(replacementName, currentName, StringComparison.Ordinal))
        {
            // Still the same reader. create_best always builds a fresh instance, so discard it.
            freeBackend(replacement);
            return;
        }

        if (backend != 0)
        {
            freeBackend(backend);
        }

        backend = replacement;
        available = true;
        log($"Prism switched to {replacementName} after {change}.");
    }

    /// <summary>
    /// Names the backends Prism knows about, for the log line that reports finding none usable.
    /// Silence is the hardest fault to report, so the failure path says what was on offer rather
    /// than only that nothing worked.
    /// </summary>
    private string DescribeRegistry()
    {
        try
        {
            var count = (int)Prism.prism_registry_count(context);
            if (count <= 0)
            {
                return "Its registry is empty.";
            }

            var names = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var id = Prism.prism_registry_id_at(context, (nuint)index);
                var name = Marshal.PtrToStringUTF8(Prism.prism_registry_name(context, id)) ?? "<unknown>";
                names.Add($"{name} ({Prism.prism_registry_priority(context, id)})");
            }

            return $"Registered backends, highest priority first: {string.Join(", ", names)}.";
        }
        catch (Exception ex)
        {
            return $"Its registry could not be listed: {ex.Message}";
        }
    }

    private static string PrismErrorToString(PrismError error)
    {
        try
        {
            return Marshal.PtrToStringUTF8(Prism.prism_error_string(error)) ?? error.ToString();
        }
        catch
        {
            return error.ToString();
        }
    }
}

internal delegate PrismError PrismBackendIsSpeaking(
    nint backend,
    [MarshalAs(UnmanagedType.I1)] out bool speaking);

/// <summary>
/// Prism calls this from its own poll thread when a backend's runtime availability changes.
/// Cdecl to match the library; the default managed convention would corrupt the stack on x86.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void PrismAvailabilityCallback(
    nint userdata,
    ulong backend,
    nint name,
    [MarshalAs(UnmanagedType.I1)] bool available);

internal enum PrismError
{
    Ok = 0
}

/// <summary>
/// Mirrors Prism's own PrismConfig field for field. The layout is part of the ABI, not a
/// convenience: prism_config_init returns this by value and prism_init reads it back, so a
/// declaration shorter than the real structure hands Prism whatever happens to follow it in
/// memory. Prism 0.18 reports version 3.
///
/// <para>The availability members are left exactly as prism_config_init sets them, which
/// leaves the background poll thread switched off. Turning it on would let the mod follow a
/// screen reader that starts or stops mid-session; that is worth doing and is not done here.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
    public nint Registry;
    public nint AvailabilityCallback;
    public nint AvailabilityUserdata;
    public uint AvailabilityPollIntervalMs;
    public uint AvailabilityDebounceSamples;
    public uint AvailabilityBackoffMaxMs;
    [MarshalAs(UnmanagedType.I1)] public bool AvailabilityAutoPowerManage;
}

internal static partial class Prism
{
    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern PrismConfig prism_config_init();

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_init(ref PrismConfig cfg);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void prism_shutdown(nint ctx);

    // create_best, not acquire_best. Prism's documentation says twice that a program without a
    // specific reason to share backend state with other callers should create rather than
    // acquire, and acquire_best's cache path can hand back a live instance belonging to someone
    // else without re-initializing it. Nothing here wants shared voice or rate settings.
    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_registry_create_best(nint ctx);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_backend_name(nint backend);

    // Used only when no backend could be created, to say what was on offer. A player reporting
    // silence should not need a debugger attached for us to know which readers Prism saw.
    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint prism_registry_count(nint ctx);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong prism_registry_id_at(nint ctx, nuint index);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_registry_name(nint ctx, ulong id);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int prism_registry_priority(nint ctx, ulong id);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern PrismError prism_backend_output(nint backend, nint text, [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern PrismError prism_backend_is_speaking(
        nint backend,
        [MarshalAs(UnmanagedType.I1)] out bool speaking);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void prism_backend_free(nint backend);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_version_string();

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_error_string(PrismError error);
}
