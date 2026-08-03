using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ff7.Accessibility.Reloaded;

public interface IFieldNavigationProgressSink
{
    void Activate(int percent);

    void SetValue(int percent);

    void Complete();

    void Deactivate();
}

/// <summary>
/// Presents route completion through the standard Win32 progress-bar class.
/// This gives screen readers the same MSAA/UIA progress semantics used by
/// installer progress bars instead of synthesizing periodic speech.
/// </summary>
public sealed class NativeFieldNavigationProgressBar : IFieldNavigationProgressSink, IDisposable
{
    private const uint IccProgressClass = 0x00000020;
    private const uint WmApp = 0x8000;
    private const uint MessageActivate = WmApp + 0x531;
    private const uint MessageSetValue = WmApp + 0x532;
    private const uint MessageDeactivate = WmApp + 0x533;
    private const uint MessageCheckVisibility = WmApp + 0x534;
    private const uint MessageStop = WmApp + 0x535;
    private const uint WmUser = 0x0400;
    private const uint PbmSetPos = WmUser + 2;
    private const uint PbmGetPos = WmUser + 8;
    private const uint PbmSetRange32 = WmUser + 6;
    private const uint EventObjectValueChange = 0x800E;
    private const int ObjIdClient = -4;
    private const int ChildIdSelf = 0;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint WsPopup = 0x80000000;
    private const uint WsBorder = 0x00800000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint PbsSmooth = 0x01;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int GwlStyle = -16;
    private const int GwlExtendedStyle = -20;
    private const uint GaRoot = 2;
    private const int HostWidth = 420;
    private const int HostHeight = 72;
    private const int HostBottomMargin = 24;
    private static readonly Guid IidAccessible =
        new("618736e0-3c3d-11cf-810c-00aa00389b71");

    private readonly Action<string> log;
    private readonly Func<nint> foregroundWindowProvider;
    private readonly Thread windowThread;
    private readonly ManualResetEventSlim ready = new(false);
    private readonly object completionSync = new();
    private System.Threading.Timer? completionTimer;
    private System.Threading.Timer? visibilityTimer;
    private nint hostHandle;
    private nint progressHandle;
    private uint windowThreadId;
    private int isAvailable;
    private int hasAccessibleClient;
    private int requestedActive;
    private int requestedPercent;
    private int appliedActive;
    private int appliedPercent;
    private int completionHold;
    private int valueChangeNotificationCount;
    private int disposed;

    public NativeFieldNavigationProgressBar(
        Action<string> log,
        Func<nint>? foregroundWindowProvider = null)
    {
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.foregroundWindowProvider = foregroundWindowProvider ?? GetForegroundWindow;
        windowThread = new Thread(WindowThreadMain)
        {
            IsBackground = true,
            Name = "FF7 accessible navigation progress"
        };
        windowThread.SetApartmentState(ApartmentState.STA);
        windowThread.Start();
    }

    public bool IsAvailable => Volatile.Read(ref isAvailable) != 0;

    public bool HasAccessibleClient => Volatile.Read(ref hasAccessibleClient) != 0;

    public nint HostParentHandle
    {
        get
        {
            var handle = Volatile.Read(ref hostHandle);
            return handle == 0 ? 0 : GetParent(handle);
        }
    }

    public int ValueChangeNotificationCount =>
        Volatile.Read(ref valueChangeNotificationCount);

    public string ControlClassName
    {
        get
        {
            var handle = Volatile.Read(ref progressHandle);
            if (handle == 0)
            {
                return string.Empty;
            }

            var className = new StringBuilder(64);
            return GetClassNameW(handle, className, className.Capacity) > 0
                ? className.ToString()
                : string.Empty;
        }
    }

    public void Activate(int percent)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        CancelCompletionHold();
        Volatile.Write(ref requestedActive, 1);
        Volatile.Write(ref requestedPercent, Math.Clamp(percent, 0, 99));
        Post(MessageActivate, Volatile.Read(ref requestedPercent));
    }

    public void SetValue(int percent)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref requestedActive) == 0)
        {
            return;
        }

        var clamped = Math.Clamp(percent, 0, 99);
        if (Interlocked.Exchange(ref requestedPercent, clamped) != clamped)
        {
            Post(MessageSetValue, clamped);
        }
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (completionSync)
        {
            completionTimer?.Dispose();
            Volatile.Write(ref completionHold, 1);
            Volatile.Write(ref requestedActive, 1);
            Volatile.Write(ref requestedPercent, 100);
            Post(MessageSetValue, 100);
            completionTimer = new System.Threading.Timer(
                _ => EndCompletionHold(),
                null,
                TimeSpan.FromMilliseconds(1500),
                Timeout.InfiniteTimeSpan);
        }
    }

    public void Deactivate()
    {
        if (Volatile.Read(ref disposed) != 0 ||
            Volatile.Read(ref completionHold) != 0)
        {
            return;
        }

        ForceDeactivate();
    }

    public bool WaitUntilReady(TimeSpan timeout) =>
        ready.Wait(timeout) && IsAvailable;

    public bool WaitForAppliedState(bool active, int percent, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if ((Volatile.Read(ref appliedActive) != 0) == active &&
                Volatile.Read(ref appliedPercent) == percent)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    public int ReadNativePercent()
    {
        var handle = Volatile.Read(ref progressHandle);
        return handle == 0
            ? -1
            : unchecked((int)SendMessageW(handle, PbmGetPos, 0, 0));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lock (completionSync)
        {
            completionTimer?.Dispose();
            completionTimer = null;
        }

        visibilityTimer?.Dispose();
        visibilityTimer = null;
        if (ready.IsSet)
        {
            Post(MessageStop, 0);
        }

        if (windowThread.IsAlive)
        {
            windowThread.Join(TimeSpan.FromSeconds(2));
        }

        ready.Dispose();
    }

    private void WindowThreadMain()
    {
        var oleInitialized = OleInitialize(0) >= 0;
        try
        {
            var controls = new InitCommonControlsData
            {
                Size = (uint)Marshal.SizeOf<InitCommonControlsData>(),
                Classes = IccProgressClass
            };
            if (!InitCommonControlsEx(ref controls))
            {
                throw new InvalidOperationException(
                    $"InitCommonControlsEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
            }

            var instance = GetModuleHandleW(null);
            hostHandle = CreateWindowExW(
                WsExTopmost | WsExToolWindow | WsExNoActivate,
                "STATIC",
                "FF7 navigation route progress",
                WsPopup | WsBorder | WsClipChildren,
                0,
                0,
                HostWidth,
                HostHeight,
                0,
                0,
                instance,
                0);
            if (hostHandle == 0)
            {
                throw new InvalidOperationException(
                    $"Could not create navigation progress host, Win32 error {Marshal.GetLastWin32Error()}.");
            }

            var labelHandle = CreateWindowExW(
                0,
                "STATIC",
                "Navigation route progress",
                WsChild | WsVisible,
                12,
                8,
                HostWidth - 24,
                20,
                hostHandle,
                0,
                instance,
                0);
            progressHandle = CreateWindowExW(
                0,
                "msctls_progress32",
                "Navigation route progress",
                WsChild | WsVisible | PbsSmooth,
                12,
                34,
                HostWidth - 24,
                22,
                hostHandle,
                0,
                instance,
                0);
            if (labelHandle == 0 || progressHandle == 0)
            {
                throw new InvalidOperationException(
                    $"Could not create the standard navigation progress controls, Win32 error {Marshal.GetLastWin32Error()}.");
            }

            SendMessageW(progressHandle, PbmSetRange32, 0, 100);
            SendMessageW(progressHandle, PbmSetPos, 0, 0);
            ShowWindow(hostHandle, SwHide);
            TryAttachToForegroundWindow();
            windowThreadId = GetCurrentThreadId();
            Volatile.Write(ref hasAccessibleClient, QueryAccessibleClient(progressHandle) ? 1 : 0);
            Volatile.Write(ref isAvailable, 1);
            ready.Set();
            visibilityTimer = new System.Threading.Timer(
                _ => Post(MessageCheckVisibility, 0),
                null,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(250));
            log(
                "Native navigation progress initialized with msctls_progress32 " +
                $"and accessible client={HasAccessibleClient}.");

            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                switch (message.MessageId)
                {
                    case MessageActivate:
                    case MessageSetValue:
                        ApplyValue(unchecked((int)message.WParam));
                        break;
                    case MessageDeactivate:
                        ApplyDeactivated();
                        break;
                    case MessageCheckVisibility:
                        UpdateVisibility();
                        break;
                    case MessageStop:
                        return;
                    default:
                        TranslateMessage(ref message);
                        DispatchMessageW(ref message);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            log($"Native navigation progress unavailable: {ex.Message}");
            ready.Set();
        }
        finally
        {
            visibilityTimer?.Dispose();
            if (hostHandle != 0)
            {
                DestroyWindow(hostHandle);
            }

            progressHandle = 0;
            hostHandle = 0;
            Volatile.Write(ref isAvailable, 0);
            if (oleInitialized)
            {
                OleUninitialize();
            }
        }
    }

    private void ApplyValue(int percent)
    {
        if (progressHandle == 0)
        {
            return;
        }

        var clamped = Math.Clamp(percent, 0, 100);
        SendMessageW(progressHandle, PbmSetPos, unchecked((nuint)clamped), 0);
        Volatile.Write(ref appliedPercent, clamped);
        Volatile.Write(ref appliedActive, 1);
        NotifyWinEvent(
            EventObjectValueChange,
            progressHandle,
            ObjIdClient,
            ChildIdSelf);
        Interlocked.Increment(ref valueChangeNotificationCount);
        log(
            $"Native navigation progress value={clamped} percent, " +
            $"foregroundParent=0x{HostParentHandle:X}.");
        UpdateVisibility();
    }

    private void ApplyDeactivated()
    {
        Volatile.Write(ref appliedActive, 0);
        if (hostHandle != 0)
        {
            ShowWindow(hostHandle, SwHide);
        }
    }

    private void UpdateVisibility()
    {
        if (hostHandle == 0 || Volatile.Read(ref requestedActive) == 0)
        {
            if (hostHandle != 0)
            {
                ShowWindow(hostHandle, SwHide);
            }

            return;
        }

        var foreground = TryAttachToForegroundWindow();
        if (foreground == 0)
        {
            ShowWindow(hostHandle, SwHide);
            return;
        }

        if (!GetClientRect(foreground, out var bounds))
        {
            ShowWindow(hostHandle, SwHide);
            return;
        }

        var left = bounds.Left + Math.Max(0, (bounds.Right - bounds.Left - HostWidth) / 2);
        var top = bounds.Bottom - HostHeight - HostBottomMargin;
        SetWindowPos(
            hostHandle,
            0,
            left,
            top,
            HostWidth,
            HostHeight,
            SwpNoActivate | SwpShowWindow);
        ShowWindow(hostHandle, SwShowNoActivate);
    }

    private nint TryAttachToForegroundWindow()
    {
        nint foreground;
        try
        {
            foreground = foregroundWindowProvider();
        }
        catch
        {
            return 0;
        }

        if (foreground == 0)
        {
            return 0;
        }

        var root = GetAncestor(foreground, GaRoot);
        if (root != 0)
        {
            foreground = root;
        }

        GetWindowThreadProcessId(foreground, out var processId);
        if (processId != Environment.ProcessId)
        {
            return 0;
        }

        if (GetParent(hostHandle) == foreground)
        {
            return foreground;
        }

        var style = unchecked((uint)GetWindowLongW(hostHandle, GwlStyle));
        style = (style & ~WsPopup) | WsChild;
        SetWindowLongW(hostHandle, GwlStyle, unchecked((int)style));
        var extendedStyle = unchecked((uint)GetWindowLongW(hostHandle, GwlExtendedStyle));
        extendedStyle &= ~(WsExTopmost | WsExToolWindow);
        extendedStyle |= WsExNoActivate;
        SetWindowLongW(
            hostHandle,
            GwlExtendedStyle,
            unchecked((int)extendedStyle));
        Marshal.SetLastPInvokeError(0);
        SetParent(hostHandle, foreground);
        if (GetParent(hostHandle) != foreground)
        {
            log(
                $"Native navigation progress could not attach to foreground window " +
                $"0x{foreground:X}; Win32 error={Marshal.GetLastPInvokeError()}.");
            return 0;
        }

        SetWindowPos(
            hostHandle,
            0,
            0,
            0,
            HostWidth,
            HostHeight,
            SwpNoActivate | SwpFrameChanged);
        log($"Native navigation progress attached to foreground FFVII window 0x{foreground:X}.");
        return foreground;
    }

    private void EndCompletionHold()
    {
        Volatile.Write(ref completionHold, 0);
        ForceDeactivate();
    }

    private void CancelCompletionHold()
    {
        lock (completionSync)
        {
            completionTimer?.Dispose();
            completionTimer = null;
            Volatile.Write(ref completionHold, 0);
        }
    }

    private void ForceDeactivate()
    {
        Volatile.Write(ref requestedActive, 0);
        Post(MessageDeactivate, 0);
    }

    private void Post(uint message, int value)
    {
        if (Volatile.Read(ref disposed) != 0 && message != MessageStop)
        {
            return;
        }

        if (!ready.IsSet)
        {
            ready.Wait(TimeSpan.FromSeconds(2));
        }

        var threadId = Volatile.Read(ref windowThreadId);
        if (threadId != 0)
        {
            PostThreadMessageW(threadId, message, unchecked((nuint)value), 0);
        }
    }

    private static bool QueryAccessibleClient(nint handle)
    {
        var iid = IidAccessible;
        var result = AccessibleObjectFromWindow(
            handle,
            unchecked((uint)ObjIdClient),
            ref iid,
            out var accessible);
        if (accessible != 0)
        {
            Marshal.Release(accessible);
        }

        return result >= 0 && accessible != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControlsData
    {
        public uint Size;
        public uint Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint MessageId;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitCommonControlsEx(ref InitCommonControlsData controls);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(
        out NativeMessage message,
        nint window,
        uint filterMinimum,
        uint filterMaximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref NativeMessage message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int newValue);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect bounds);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint window,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(
        uint eventId,
        nint window,
        int objectId,
        int childId);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint window,
        uint objectId,
        ref Guid interfaceId,
        out nint accessibleObject);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();
}
