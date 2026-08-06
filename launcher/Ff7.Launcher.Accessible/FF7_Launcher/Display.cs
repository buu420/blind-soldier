using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FF7_Launcher;

public class Display
{
	public string DeviceName { get; private set; }

	public string DeviceString { get; private set; }

	public string MonitorName { get; private set; }

	public List<Resolution> Resolutions { get; private set; }

	[DllImport("user32.dll")]
	private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

	[DllImport("user32.dll")]
	private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

	public static List<Resolution> GetSortedResolutionList(int screenId)
	{
		return GetResolutionList((uint)screenId);
	}

	public static List<uint> DisplayIDList()
	{
		DISPLAY_DEVICE lpDisplayDevice = default(DISPLAY_DEVICE);
		lpDisplayDevice.cb = Marshal.SizeOf(lpDisplayDevice);
		List<uint> list = new List<uint>();
		for (uint num = 0u; EnumDisplayDevices(null, num, ref lpDisplayDevice, 0u); num++)
		{
			if ((lpDisplayDevice.StateFlags & DisplayDeviceStateFlags.AttachedToDesktop) != DisplayDeviceStateFlags.None)
			{
				if ((lpDisplayDevice.StateFlags & DisplayDeviceStateFlags.PrimaryDevice) != DisplayDeviceStateFlags.None)
				{
					list.Insert(0, num);
				}
				else
				{
					list.Add(num);
				}
			}
		}
		return list;
	}

	private static List<Resolution> GetResolutionList(uint screenId = 0u)
	{
		DISPLAY_DEVICE lpDisplayDevice = default(DISPLAY_DEVICE);
		lpDisplayDevice.cb = Marshal.SizeOf(lpDisplayDevice);
		DEVMODE devMode = default(DEVMODE);
		int num = 0;
		List<Resolution> list = new List<Resolution>();
		bool flag = false;
		for (uint num2 = 0u; EnumDisplayDevices(null, num2, ref lpDisplayDevice, 0u); num2++)
		{
			if ((lpDisplayDevice.StateFlags & DisplayDeviceStateFlags.AttachedToDesktop) == 0 || (screenId == 0 && (lpDisplayDevice.StateFlags & DisplayDeviceStateFlags.PrimaryDevice) == 0))
			{
				continue;
			}
			if (screenId != 0 && (lpDisplayDevice.StateFlags & DisplayDeviceStateFlags.PrimaryDevice) != DisplayDeviceStateFlags.None)
			{
				flag = true;
				continue;
			}
			if (screenId != 0)
			{
				if (flag)
				{
					if (screenId != num2)
					{
						continue;
					}
				}
				else if (screenId != num2 + 1)
				{
					continue;
				}
			}
			while (EnumDisplaySettings(lpDisplayDevice.DeviceName, num++, ref devMode))
			{
				Resolution item = new Resolution(devMode);
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		list.Sort();
		return list;
	}

	private static Display GetDisplay(DISPLAY_DEVICE device)
	{
		DISPLAY_DEVICE lpDisplayDevice = default(DISPLAY_DEVICE);
		lpDisplayDevice.cb = Marshal.SizeOf(lpDisplayDevice);
		EnumDisplayDevices(device.DeviceName, 0u, ref lpDisplayDevice, 0u);
		return new Display(device, lpDisplayDevice);
	}

	private Display()
	{
	}

	private Display(DISPLAY_DEVICE device, DISPLAY_DEVICE monitor)
	{
		DeviceName = device.DeviceName;
		DeviceString = device.DeviceString;
		MonitorName = monitor.DeviceString;
		FillResolutions();
	}

	private void FillResolutions()
	{
		Resolutions = new List<Resolution>();
		DEVMODE devMode = default(DEVMODE);
		for (int i = 0; EnumDisplaySettings(DeviceName, i, ref devMode); i++)
		{
			Resolution item = new Resolution(devMode);
			if (!Resolutions.Contains(item))
			{
				Resolutions.Add(item);
			}
		}
	}
}
