using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Reloaded;

internal static class GameWindowCapture
{
    public static Bitmap? CaptureCurrentProcessClient()
    {
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            hwnd = FindMainWindow(Process.GetCurrentProcess().Id);
        }

        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var origin = new PointNative { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin))
        {
            return null;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }
    }

    private static IntPtr FindMainWindow(int processId)
    {
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != processId || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero)
            {
                return true;
            }

            result = hwnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out RectNative rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref PointNative point);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }
}
