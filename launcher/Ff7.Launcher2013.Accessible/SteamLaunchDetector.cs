using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BlindSoldier.Ff7Launcher2013
{
	// Steam starts app 39140 by running FF7_Launcher.exe from steam.exe. The
	// SteamAppId and SteamGameId variables the Steam client passes are accepted
	// too, so the "started outside Steam" note never appears for a Steam launch.
	internal static class SteamLaunchDetector
	{
		internal static bool IsLaunchedBySteam(Func<string, string> environment, string parentImageName)
		{
			foreach (string name in new[] { "SteamAppId", "SteamGameId" })
			{
				if (!string.IsNullOrWhiteSpace(environment?.Invoke(name)))
				{
					return true;
				}
			}
			return string.Equals(Path.GetFileName(parentImageName ?? string.Empty), "steam.exe", StringComparison.OrdinalIgnoreCase);
		}

		internal static string ParentImageName()
		{
			try
			{
				var information = new ProcessBasicInformation();
				using (Process current = Process.GetCurrentProcess())
				{
					if (NtQueryInformationProcess(current.Handle, 0, ref information, Marshal.SizeOf(information), out _) != 0)
					{
						return null;
					}
				}
				using (Process parent = Process.GetProcessById(information.InheritedFromUniqueProcessId.ToInt32()))
				{
					return parent.ProcessName + ".exe";
				}
			}
			catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
				exception is System.ComponentModel.Win32Exception || exception is EntryPointNotFoundException)
			{
				return null;
			}
		}

		[DllImport("ntdll.dll")]
		private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, ref ProcessBasicInformation information, int length, out int returnedLength);

		[StructLayout(LayoutKind.Sequential)]
		private struct ProcessBasicInformation
		{
			public IntPtr ExitStatus;
			public IntPtr PebBaseAddress;
			public IntPtr AffinityMask;
			public IntPtr BasePriority;
			public IntPtr UniqueProcessId;
			public IntPtr InheritedFromUniqueProcessId;
		}
	}
}
