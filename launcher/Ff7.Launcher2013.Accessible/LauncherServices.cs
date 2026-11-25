using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace BlindSoldier.Ff7Launcher2013
{
	internal sealed class LauncherLog
	{
		private const long MaximumLength = 1024 * 1024;
		private readonly object sync = new object();

		internal LauncherLog(string path)
		{
			Path = path ?? throw new ArgumentNullException(nameof(path));
		}

		internal string Path { get; }

		internal void Write(string message)
		{
			try
			{
				lock (sync)
				{
					Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
					if (File.Exists(Path) && new FileInfo(Path).Length > MaximumLength)
					{
						string previous = Path + ".1";
						if (File.Exists(previous))
						{
							File.Delete(previous);
						}
						File.Move(Path, previous);
					}
					File.AppendAllText(Path,
						DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
						new UTF8Encoding(false));
				}
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				// Logging must never prevent the launcher from opening or playing.
			}
		}
	}

	// Speaks status changes through the screen reader with a UI Automation
	// notification raised from the status box, without moving keyboard focus.
	internal sealed class UiaNotificationAnnouncer : IAnnouncer
	{
		public void Announce(Control anchor, string text)
		{
			if (anchor == null || anchor.IsDisposed || !anchor.IsHandleCreated || string.IsNullOrWhiteSpace(text))
			{
				return;
			}
			try
			{
				anchor.AccessibilityObject.RaiseAutomationNotification(
					AutomationNotificationKind.Other,
					AutomationNotificationProcessing.ImportantMostRecent,
					text);
			}
			catch (Exception exception) when (exception is InvalidOperationException || exception is COMException || exception is NotSupportedException)
			{
				// Older Windows builds without UIA notifications still show the status text.
			}
		}
	}

	internal sealed class MessageBoxDialogs : ILauncherDialogs
	{
		private readonly LauncherText text;

		internal MessageBoxDialogs(LauncherText text)
		{
			this.text = text ?? throw new ArgumentNullException(nameof(text));
		}

		public void ShowError(IWin32Window owner, string message)
		{
			MessageBox.Show(owner, message, text[TextKey.ErrorTitle], MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		public void ShowInformation(IWin32Window owner, string message)
		{
			MessageBox.Show(owner, message, text[TextKey.ErrorTitle], MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}

	// Display sizes and refresh rates the primary display reports, for the
	// resolution and refresh-rate lists of the original driver.
	internal sealed class SystemDisplayModes : IDisplayModeSource
	{
		private const int EnumCurrentSettings = -1;

		public IReadOnlyList<DisplayMode> GetModes()
		{
			var found = new List<Tuple<uint, uint, uint>>();
			var mode = new DisplayDeviceMode { dmSize = (short)Marshal.SizeOf(typeof(DisplayDeviceMode)) };
			for (int index = 0; EnumDisplaySettingsW(null, index, ref mode); index++)
			{
				if (mode.dmPelsWidth >= 640 && mode.dmPelsHeight >= 480 && mode.dmBitsPerPel >= 16)
				{
					found.Add(Tuple.Create((uint)mode.dmPelsWidth, (uint)mode.dmPelsHeight, (uint)mode.dmDisplayFrequency));
				}
			}
			if (found.Count == 0 && EnumDisplaySettingsW(null, EnumCurrentSettings, ref mode))
			{
				found.Add(Tuple.Create((uint)mode.dmPelsWidth, (uint)mode.dmPelsHeight, (uint)mode.dmDisplayFrequency));
			}

			return found
				.GroupBy(item => Tuple.Create(item.Item1, item.Item2))
				.OrderBy(group => group.Key.Item1)
				.ThenBy(group => group.Key.Item2)
				.Select(group => new DisplayMode(group.Key.Item1, group.Key.Item2,
					group.Select(item => item.Item3).Where(rate => rate > 1).Distinct().OrderBy(rate => rate).ToArray()))
				.ToArray();
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumDisplaySettingsW(string deviceName, int modeNumber, ref DisplayDeviceMode mode);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct DisplayDeviceMode
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string dmDeviceName;
			public short dmSpecVersion;
			public short dmDriverVersion;
			public short dmSize;
			public short dmDriverExtra;
			public int dmFields;
			public int dmPositionX;
			public int dmPositionY;
			public int dmDisplayOrientation;
			public int dmDisplayFixedOutput;
			public short dmColor;
			public short dmDuplex;
			public short dmYResolution;
			public short dmTTOption;
			public short dmCollate;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string dmFormName;
			public short dmLogPixels;
			public int dmBitsPerPel;
			public int dmPelsWidth;
			public int dmPelsHeight;
			public int dmDisplayFlags;
			public int dmDisplayFrequency;
			public int dmICMMethod;
			public int dmICMIntent;
			public int dmMediaType;
			public int dmDitherType;
			public int dmReserved1;
			public int dmReserved2;
			public int dmPanningWidth;
			public int dmPanningHeight;
		}
	}
}
