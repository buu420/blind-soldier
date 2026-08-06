using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FF7_Launcher;

internal static class Program
{
	private static Mutex InvalidationMultipleExecution;

	public static string Title => "FINAL FANTASY VII LAUNCHER";

	public static bool IsAlreadyRunning()
	{
		InvalidationMultipleExecution = new Mutex(initiallyOwned: false, "FF7_LauncherCS");
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(FF7Launcher));
		if (!InvalidationMultipleExecution.WaitOne(0, exitContext: false))
		{
			MessageBox.Show(componentResourceManager.GetString("AlreadyRunning"), Title);
			return true;
		}
		return false;
	}

	[STAThread]
	private static void Main()
	{
		try
		{
			Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
			if (!IsAlreadyRunning())
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(defaultValue: false);
				Application.Run(new FF7Launcher());
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString(), Title);
		}
	}
}
