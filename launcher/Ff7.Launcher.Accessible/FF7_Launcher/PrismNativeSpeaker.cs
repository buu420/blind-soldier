using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FF7_Launcher;

internal sealed class PrismNativeSpeaker : ISpeechOutput, IDisposable
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    private readonly Action<string> log;
    private readonly object backendSync = new object();
    private Func<IntPtr, IntPtr, bool, PrismError> output;
    private Action<IntPtr> stop;
    private Action<IntPtr> freeBackend;
    private Action<IntPtr> shutdown;
    private Action<IntPtr> freeLibrary;
    private Func<PrismError, IntPtr> errorString;
    private IntPtr context;
    private IntPtr backend;
    private IntPtr library;
    private bool available;
    private bool disposed;

    private PrismConfigInitDelegate configInitNative;
    private PrismInitDelegate initNative;
    private PrismShutdownDelegate shutdownNative;
    private PrismCreateBestDelegate createBestNative;
    private PrismBackendNameDelegate backendNameNative;
    private PrismBackendOutputDelegate outputNative;
    private PrismBackendStopDelegate stopNative;
    private PrismBackendFreeDelegate backendFreeNative;
    private PrismErrorStringDelegate errorStringNative;

    private PrismNativeSpeaker(Action<string> log)
    {
        this.log = log ?? delegate { };
    }

    internal PrismNativeSpeaker(
        Action<string> log,
        IntPtr context,
        IntPtr backend,
        Func<IntPtr, IntPtr, bool, PrismError> output,
        Action<IntPtr> stop,
        Action<IntPtr> freeBackend,
        Action<IntPtr> shutdown,
        Action<IntPtr> freeLibrary,
        IntPtr library)
    {
        this.log = log ?? delegate { };
        this.context = context;
        this.backend = backend;
        this.output = output;
        this.stop = stop;
        this.freeBackend = freeBackend;
        this.shutdown = shutdown;
        this.freeLibrary = freeLibrary;
        this.library = library;
        available = backend != IntPtr.Zero && output != null;
    }

    internal bool IsAvailable
    {
        get
        {
            lock (backendSync)
            {
                return available && !disposed;
            }
        }
    }

    internal static PrismNativeSpeaker TryCreate(string absoluteLibraryPath, Action<string> log)
    {
        var speaker = new PrismNativeSpeaker(log);
        speaker.TryInitialize(absoluteLibraryPath);
        return speaker;
    }

    internal static bool IsAbsoluteLibraryPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
    }

    /// <summary>
    /// The PRISM_CONFIG_VERSION that <see cref="PrismConfig"/> below is declared for.
    /// </summary>
    /// <remarks>
    /// Prism's header undertakes to increment this whenever a field is added to or
    /// removed from the struct, and it has: 1, then 2 when the struct was cut back to the
    /// version byte alone, then 3 when the registry and availability fields arrived.
    /// Whoever brings the DLL forward moves this and the struct together.
    /// </remarks>
    internal const byte SupportedPrismConfigVersion = 3;

    /// <summary>
    /// Round-trips <see cref="PrismConfig"/> through the shipped library using the very
    /// delegates the launcher speaks through, and stops before backend selection.
    /// </summary>
    /// <remarks>
    /// This is the only check that proves the managed declaration against the binary
    /// rather than against another managed declaration. It is a test seam: nothing in
    /// the launcher's startup path calls it. It deliberately never reaches
    /// <c>prism_registry_create_best</c>, which is where Prism loads screen readers and
    /// audio and which a headless build machine cannot satisfy; <c>prism_init</c> only
    /// initialises COM, snapshots the backend registry and allocates a context, and
    /// leaves the availability poller off while the callback is null.
    /// </remarks>
    internal static PrismAbiProbeResult ProbeAbi(string absoluteLibraryPath, Action<string> log)
    {
        if (!IsAbsoluteLibraryPath(absoluteLibraryPath))
        {
            throw new ArgumentException("The Prism library path must be absolute.", "absoluteLibraryPath");
        }
        if (Environment.Is64BitProcess)
        {
            throw new InvalidOperationException("The Prism ABI probe must run in an x86 process, as the launcher does.");
        }
        if (!File.Exists(absoluteLibraryPath))
        {
            throw new FileNotFoundException("Prism library is missing.", absoluteLibraryPath);
        }

        var speaker = new PrismNativeSpeaker(log);
        speaker.library = LoadLibraryEx(
            absoluteLibraryPath,
            IntPtr.Zero,
            LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
        if (speaker.library == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryEx failed for " + absoluteLibraryPath);
        }

        try
        {
            // Binds every export the launcher uses, so a DLL that dropped one fails here
            // rather than on a user's desk.
            speaker.BindExports();

            var result = new PrismAbiProbeResult
            {
                ConfigSize = Marshal.SizeOf(typeof(PrismConfig))
            };

            var config = speaker.configInitNative();
            result.ConfigVersion = config.Version;

            var context = speaker.initNative(ref config);
            result.ContextCreated = context != IntPtr.Zero;
            if (context != IntPtr.Zero)
            {
                speaker.shutdownNative(context);
                result.ShutdownCompleted = true;
            }

            return result;
        }
        finally
        {
            FreeLibrary(speaker.library);
            speaker.library = IntPtr.Zero;
        }
    }

    public bool Speak(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        lock (backendSync)
        {
            if (!available || disposed || backend == IntPtr.Zero || output == null)
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(text + "\0");
            unsafe
            {
                fixed (byte* textPointer = bytes)
                {
                    var result = output(backend, new IntPtr(textPointer), interrupt);
                    if (result == PrismError.Ok)
                    {
                        return true;
                    }

                    log("Prism output failed: " + ErrorToString(result));
                    return false;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (backendSync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            available = false;

            if (backend != IntPtr.Zero)
            {
                try
                {
                    stop?.Invoke(backend);
                }
                catch (Exception exception)
                {
                    log("Prism stop failed during shutdown: " + exception.Message);
                }

                try
                {
                    freeBackend?.Invoke(backend);
                }
                catch (Exception exception)
                {
                    log("Prism backend release failed: " + exception.Message);
                }
                backend = IntPtr.Zero;
            }

            if (context != IntPtr.Zero)
            {
                try
                {
                    shutdown?.Invoke(context);
                }
                catch (Exception exception)
                {
                    log("Prism context shutdown failed: " + exception.Message);
                }
                context = IntPtr.Zero;
            }

            if (library != IntPtr.Zero)
            {
                try
                {
                    freeLibrary?.Invoke(library);
                }
                catch (Exception exception)
                {
                    log("Prism library release failed: " + exception.Message);
                }
                library = IntPtr.Zero;
            }
        }
    }

    private void TryInitialize(string absoluteLibraryPath)
    {
        if (!IsAbsoluteLibraryPath(absoluteLibraryPath))
        {
            log("Prism initialization refused a non-absolute library path: " + (absoluteLibraryPath ?? "<null>"));
            return;
        }

        if (Environment.Is64BitProcess)
        {
            log("Prism initialization refused a 64-bit launcher process; the launcher dependency is x86.");
            return;
        }

        if (!File.Exists(absoluteLibraryPath))
        {
            log("Prism library is missing: " + absoluteLibraryPath);
            return;
        }

        try
        {
            library = LoadLibraryEx(
                absoluteLibraryPath,
                IntPtr.Zero,
                LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
            if (library == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryEx failed for " + absoluteLibraryPath);
            }

            freeLibrary = handle => FreeLibrary(handle);
            BindExports();

            var config = configInitNative();
            context = initNative(ref config);
            if (context == IntPtr.Zero)
            {
                log("Prism initialization returned a null context.");
                ReleaseFailedInitialization();
                return;
            }

            backend = createBestNative(context);
            if (backend == IntPtr.Zero)
            {
                log("Prism did not find an available speech backend.");
                ReleaseFailedInitialization();
                return;
            }

            output = (backendPointer, textPointer, interrupt) => outputNative(backendPointer, textPointer, interrupt);
            stop = backendPointer => stopNative(backendPointer);
            freeBackend = backendPointer => backendFreeNative(backendPointer);
            shutdown = contextPointer => shutdownNative(contextPointer);
            errorString = error => errorStringNative(error);
            available = true;

            var name = PtrToStringUtf8(backendNameNative(backend));
            log("Prism initialized. Backend: " + (string.IsNullOrEmpty(name) ? "<unknown>" : name));
        }
        catch (Exception exception)
        {
            log("Prism initialization failed: " + exception);
            ReleaseFailedInitialization();
        }
    }

    private void BindExports()
    {
        configInitNative = GetExport<PrismConfigInitDelegate>("prism_config_init");
        initNative = GetExport<PrismInitDelegate>("prism_init");
        shutdownNative = GetExport<PrismShutdownDelegate>("prism_shutdown");
        // create_best, not acquire_best: this speaker frees its backend with
        // prism_backend_free, which is create's ownership contract. acquire_best can hand
        // back a cached instance belonging to someone else, and freeing that is a
        // double-free waiting to happen. The mod made this same correction.
        createBestNative = GetExport<PrismCreateBestDelegate>("prism_registry_create_best");
        backendNameNative = GetExport<PrismBackendNameDelegate>("prism_backend_name");
        outputNative = GetExport<PrismBackendOutputDelegate>("prism_backend_output");
        stopNative = GetExport<PrismBackendStopDelegate>("prism_backend_stop");
        backendFreeNative = GetExport<PrismBackendFreeDelegate>("prism_backend_free");
        errorStringNative = GetExport<PrismErrorStringDelegate>("prism_error_string");
    }

    private T GetExport<T>(string name) where T : class
    {
        var address = GetProcAddress(library, name);
        if (address == IntPtr.Zero)
        {
            throw new MissingMethodException("The Prism library does not export " + name + ".");
        }
        return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
    }

    private void ReleaseFailedInitialization()
    {
        available = false;

        if (backend != IntPtr.Zero && backendFreeNative != null)
        {
            try
            {
                backendFreeNative(backend);
            }
            catch
            {
            }
            backend = IntPtr.Zero;
        }

        if (context != IntPtr.Zero && shutdownNative != null)
        {
            try
            {
                shutdownNative(context);
            }
            catch
            {
            }
            context = IntPtr.Zero;
        }

        if (library != IntPtr.Zero)
        {
            FreeLibrary(library);
            library = IntPtr.Zero;
        }
    }

    private string ErrorToString(PrismError error)
    {
        try
        {
            var pointer = errorString != null ? errorString(error) : errorStringNative?.Invoke(error) ?? IntPtr.Zero;
            return PtrToStringUtf8(pointer) ?? error.ToString();
        }
        catch
        {
            return error.ToString();
        }
    }

    private static string PtrToStringUtf8(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        var length = 0;
        while (Marshal.ReadByte(pointer, length) != 0)
        {
            length++;
        }

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PrismConfig PrismConfigInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PrismInitDelegate(ref PrismConfig config);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PrismShutdownDelegate(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PrismCreateBestDelegate(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PrismBackendNameDelegate(IntPtr backend);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PrismError PrismBackendOutputDelegate(
        IntPtr backend,
        IntPtr text,
        [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PrismError PrismBackendStopDelegate(IntPtr backend);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PrismBackendFreeDelegate(IntPtr backend);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PrismErrorStringDelegate(PrismError error);
}

internal enum PrismError
{
    Ok = 0
}

/// <summary>
/// What <see cref="PrismNativeSpeaker.ProbeAbi"/> observed from the shipped library.
/// </summary>
internal sealed class PrismAbiProbeResult
{
    public int ConfigSize;
    public byte ConfigVersion;
    public bool ContextCreated;
    public bool ShutdownCompleted;
}

/// <summary>
/// Prism's configuration block, which is an ABI contract and has to move with the
/// DLL it is passed to.
/// </summary>
/// <remarks>
/// <c>prism_config_init</c> returns this <em>by value</em> and <c>prism_init</c> reads
/// it back. Prism 0.18 grew it from a single byte to these eight fields, and 0.4.1
/// shipped that DLL to the launcher while leaving the one-byte declaration here. On x86
/// a struct this size is returned through a hidden pointer, so the native side wrote
/// thirty-two bytes into a one-byte stack slot and the launcher access-violated on
/// startup before it could select a backend — for every user, with any screen reader or
/// none. Verified against the shipped DLL from a .NET Framework x86 probe: version 3,
/// size 32.
///
/// <para>The final field is a plain byte rather than a managed bool carrying
/// <c>[MarshalAs(UnmanagedType.I1)]</c>, which is how the mod declares it. .NET Framework
/// refuses a struct-return delegate whose struct carries that attribute and throws
/// MarshalDirectiveException; the mod runs on .NET 8 and does not hit that rule. Do not
/// "tidy" this to match the mod.</para>
///
/// <para>Natural alignment is required — no <c>Pack</c> — giving 32 bytes on x86.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
    public IntPtr Registry;
    public IntPtr AvailabilityCallback;
    public IntPtr AvailabilityUserdata;
    public uint AvailabilityPollIntervalMs;
    public uint AvailabilityDebounceSamples;
    public uint AvailabilityBackoffMaxMs;
    public byte AvailabilityAutoPowerManage;
}
