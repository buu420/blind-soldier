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
    private PrismAcquireBestDelegate acquireBestNative;
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

            backend = acquireBestNative(context);
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
        acquireBestNative = GetExport<PrismAcquireBestDelegate>("prism_registry_acquire_best");
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
    private delegate IntPtr PrismAcquireBestDelegate(IntPtr context);

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

[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
}
