using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace FF7_Launcher;

public static class NativeMethods
{
	public enum GWL
	{
		GWL_WNDPROC = -4,
		GWL_HINSTANCE = -6,
		GWL_HWNDPARENT = -8,
		GWL_STYLE = -16,
		GWL_EXSTYLE = -20,
		GWL_USERDATA = -21,
		GWL_ID = -12
	}

	private struct DevBroadcastDeviceinterface
	{
		internal int Size;

		internal int DeviceType;

		internal int Reserved;

		internal Guid ClassGuid;

		internal short Name;
	}

	public const int DbtDevicearrival = 32768;

	public const int DbtDeviceremovecomplete = 32772;

	public const int WmDevicechange = 537;

	private const int DbtDevtypDeviceinterface = 5;

	private static readonly Guid GuidDevinterfaceUSBDevice = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

	private static IntPtr notificationHandle;

	public static Point Resize(this Point p, float ratio)
	{
		int x = (int)((float)p.X * ratio);
		int y = (int)((float)p.Y * ratio);
		return new Point(x, y);
	}

	public static System.Drawing.Size Resize(this System.Drawing.Size p, float ratio)
	{
		int width = (int)((float)p.Width * ratio);
		int height = (int)((float)p.Height * ratio);
		return new System.Drawing.Size(width, height);
	}

	public static Rectangle Resize(this Rectangle rectangle, float ratio)
	{
		Point location = rectangle.Location.Resize(ratio);
		Point location2 = rectangle.Location;
		return new Rectangle(location, rectangle.Size.Resize(ratio));
	}

	[DllImport("User32.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern int GetSystemMetrics(int nIndex);

	[DllImport("User32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "GetWindowLongW")]
	private static extern int GetWindowLong(IntPtr hwnd, GWL nIndex);

	[DllImport("User32.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern int AdjustWindowRect([MarshalAs(UnmanagedType.LPArray)] int[] lpRect, int dwStyle, uint bMenu);

	public static int AdjustWindowRect(IntPtr hwnd, ref Rectangle param, int newWidth, int newHeight, bool hasVScroll)
	{
		int[] array = new int[4]
		{
			param.Left,
			param.Top,
			param.Left + newWidth + (hasVScroll ? GetSystemMetrics(2) : 0),
			param.Top + newHeight
		};
		int windowLong = GetWindowLong(hwnd, GWL.GWL_STYLE);
		int result = AdjustWindowRect(array, windowLong, 0u);
		param = Rectangle.FromLTRB(array[0], array[1], array[2], array[3]);
		return result;
	}

	public static void RegisterUsbDeviceNotification(IntPtr windowHandle)
	{
		DevBroadcastDeviceinterface structure = new DevBroadcastDeviceinterface
		{
			DeviceType = 5,
			Reserved = 0,
			ClassGuid = GuidDevinterfaceUSBDevice,
			Name = 0
		};
		structure.Size = Marshal.SizeOf(structure);
		IntPtr intPtr = Marshal.AllocHGlobal(structure.Size);
		Marshal.StructureToPtr(structure, intPtr, fDeleteOld: true);
		notificationHandle = RegisterDeviceNotification(windowHandle, intPtr, 0);
	}

	public static void UnregisterUsbDeviceNotification()
	{
		UnregisterDeviceNotification(notificationHandle);
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr notificationFilter, int flags);

	[DllImport("user32.dll")]
	private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
