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
            if (context != 0)
            {
                shutdown(context);
                context = 0;
                backend = 0;
            }
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

            backend = Prism.prism_registry_acquire_best(context);
            if (backend == 0)
            {
                log("Prism did not find a backend.");
                return;
            }

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

[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
}

internal static partial class Prism
{
    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern PrismConfig prism_config_init();

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_init(ref PrismConfig cfg);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void prism_shutdown(nint ctx);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_registry_acquire_best(nint ctx);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_backend_name(nint backend);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern PrismError prism_backend_output(nint backend, nint text, [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern PrismError prism_backend_is_speaking(
        nint backend,
        [MarshalAs(UnmanagedType.I1)] out bool speaking);

    [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint prism_error_string(PrismError error);
}
