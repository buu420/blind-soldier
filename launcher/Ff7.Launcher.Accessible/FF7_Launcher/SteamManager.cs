#define TRACE
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Steamworks;

namespace FF7_Launcher;

public class SteamManager : IDisposable
{
	public string SteamID { get; private set; }

	private void SteamAPIWarningMessageHook(int nSeverity, StringBuilder pchDebugText)
	{
		Trace.WriteLine(pchDebugText);
	}

	private void LocalizeErrorTest()
	{
		string[] array = new string[6] { "ja", "en", "fr", "it", "de", "es" };
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(FF7Launcher));
		string[] array2 = array;
		foreach (string text in array2)
		{
			Thread.CurrentThread.CurrentUICulture = new CultureInfo(text, useUserOverride: false);
			Trace.Write(text + " : ");
			Trace.WriteLine(componentResourceManager.GetString("SteamError"));
		}
	}

	public void Init()
	{
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(FF7Launcher));
		AppId_t unOwnAppID = (AppId_t)3837340u;
		if (SteamAPI.RestartAppIfNecessary(unOwnAppID))
		{
			Trace.WriteLine("not installed.");
			Environment.Exit(0);
		}
		if (SteamAPI.Init())
		{
			Trace.WriteLine("success");
		}
		else
		{
			TryStartSteamClient();
			Environment.Exit(0);
		}
		SteamID = SteamApps.GetAppOwner().m_SteamID.ToString();
		if (!Packsize.Test())
		{
			Trace.WriteLine("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.");
		}
		if (!DllCheck.Test())
		{
			Trace.WriteLine("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.");
		}
		SteamClient.SetWarningMessageHook(SteamAPIWarningMessageHook);
		string steamUILanguage = SteamUtils.GetSteamUILanguage();
	}

	public void Dispose()
	{
		SteamAPI.Shutdown();
	}

	private static bool TryStartSteamClient()
	{
		string text = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")?.GetValue("SteamExe") as string;
		if (string.IsNullOrEmpty(text))
		{
			text = "C:\\Program Files (x86)\\Steam\\Steam.exe";
		}
		try
		{
			Process.Start(text);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
