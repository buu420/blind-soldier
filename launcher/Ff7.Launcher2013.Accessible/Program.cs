using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace BlindSoldier.Ff7Launcher2013
{
	internal static class Program
	{
		private const string InstanceMutexName = @"Local\BlindSoldier.FF7_2013.AccessibleLauncher";

		[STAThread]
		private static int Main()
		{
			string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var log = new LauncherLog(Path.Combine(root, "Blind-Soldier", "Logs", "FF7_Launcher-2013.log"));
			try
			{
				Directory.SetCurrentDirectory(root);
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
				Application.ThreadException += (sender, arguments) =>
				{
					log.Write("Unhandled launcher error: " + arguments.Exception);
					MessageBox.Show(arguments.Exception.Message + Environment.NewLine + log.Path, "FINAL FANTASY VII (2013)", MessageBoxButtons.OK, MessageBoxIcon.Error);
				};
				using (var instance = new Mutex(false, InstanceMutexName))
				{
					bool owned;
					try
					{
						// A launcher that hands over to the original one may still be
						// closing when Steam starts the next instance.
						owned = instance.WaitOne(TimeSpan.FromSeconds(5));
					}
					catch (AbandonedMutexException)
					{
						owned = true;
					}
					if (!owned)
					{
						LauncherText text = LauncherText.For(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
						MessageBox.Show(text[TextKey.AlreadyOpen], text[TextKey.ErrorTitle], MessageBoxButtons.OK, MessageBoxIcon.Information);
						return 1;
					}
					try
					{
						Run(root, log);
						return 0;
					}
					finally
					{
						instance.ReleaseMutex();
					}
				}
			}
			catch (Exception exception)
			{
				log.Write("Fatal launcher error: " + exception);
				MessageBox.Show(exception.Message + Environment.NewLine + log.Path, "FINAL FANTASY VII (2013)", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return 2;
			}
		}

		private static void Run(string root, LauncherLog log)
		{
			string parent = SteamLaunchDetector.ParentImageName();
			bool launchedBySteam = SteamLaunchDetector.IsLaunchedBySteam(Environment.GetEnvironmentVariable, parent);
			LegacyInstallation installation = LegacyInstallation.Detect(root, LegacyInstallation.DefaultSteamManifestPath(root), launchedBySteam);
			string documentDirectory = LegacySaveProfiles.DocumentDirectory(DocumentsRoot());
			var profiles = LegacySaveProfiles.Find(documentDirectory);
			LauncherText text = LauncherText.For(installation.Selected?.LanguageCode ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
			Version version = Assembly.GetExecutingAssembly().GetName().Version;

			log.Write("Accessible 2013 launcher " + version + " started in " + root +
				"; launched by Steam: " + launchedBySteam + " (parent " + (parent ?? "unknown") + ")" +
				"; folder: " + installation.Problem +
				"; executable: " + (installation.Selected?.FileName ?? "none") +
				"; driver: " + installation.Driver.Kind + " " + installation.Driver.Version + " (" + installation.Driver.Length + " bytes)" +
				"; save folders: " + profiles.Count +
				"; Blind Soldier: " + installation.BlindSoldierInstalled +
				"; original launcher backup: " + (installation.OriginalLauncherPath != null));

			var starter = new LegacyGameProcessStarter();
			var model = new LauncherModel
			{
				Installation = installation,
				Profiles = profiles,
				DocumentDirectory = documentDirectory,
				Text = text,
				LaunchService = new LegacyGameLauncher(starter, log.Write, new BlindSoldierLoader(new ProcessModuleReader(), new RemoteLibraryLoader(), log.Write)),
				OriginalLauncherStarter = starter,
				Dialogs = new MessageBoxDialogs(text),
				Announcer = new UiaNotificationAnnouncer(),
				SettingsPresenter = new SettingsPresenter(),
				DisplayModes = new SystemDisplayModes(),
				Preferences = new LauncherPreferences(Path.Combine(root, "Blind-Soldier", "Launcher2013", "save-folder.txt")),
				Log = log.Write,
				LogPath = log.Path
			};
			Application.Run(new MainForm(model));
			log.Write("Accessible 2013 launcher closed.");
		}

		private static string DocumentsRoot()
		{
			string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			return string.IsNullOrEmpty(documents)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
				: documents;
		}
	}
}
