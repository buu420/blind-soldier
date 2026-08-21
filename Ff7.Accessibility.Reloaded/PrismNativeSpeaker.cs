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
                shutdown(context);
                context = 0;
            }

            backend = 0;
        }
    }

    private void TryInitialize()
    {
        try
        {
            var config = Prism.prism_config_init();
            context = Prism.prism_init(ref config);
            if (context == 0)
            {
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
            log($"Prism initialized. Backend: {name}");
        }
        catch (Exception ex)
        {
            log($"Prism initialization failed: {ex}");
        }
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
    public static extern nint prism_error_string(PrismError error);
}
