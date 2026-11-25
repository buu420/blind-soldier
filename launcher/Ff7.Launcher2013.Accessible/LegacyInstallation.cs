using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlindSoldier.Ff7Launcher2013
{
	internal enum LegacyLayoutProblem
	{
		None,
		NotLegacyGameFolder,
		Steam2026Folder,
		NoSupportedExecutable
	}

	internal enum LegacyLanguageSource
	{
		OnlyExecutable,
		SteamManifest,
		EnglishPreference,
		FirstExecutable
	}

	internal sealed class LegacyGameExecutable
	{
		internal LegacyGameExecutable(string fileName, string fullPath, string languageCode)
		{
			FileName = fileName;
			FullPath = fullPath;
			LanguageCode = languageCode;
		}

		internal string FileName { get; }

		internal string FullPath { get; }

		internal string LanguageCode { get; }
	}

	// The Steam 2013 edition (app 39140) installs one ff7_<language>.exe per
	// language depot beside data\, AF3DN.P and FF7_Launcher.exe.
	internal sealed class LegacyInstallation
	{
		internal const string OriginalLauncherFileName = "FF7_Launcher.original.exe";
		internal const string BlindSoldierExecutableName = "ff7_en.exe";

		// Ordinal file-name order, which is also FindFirstFile order on NTFS.
		private static readonly string[] LanguageCodes = { "de", "en", "es", "fr" };

		// The Blind Soldier x86 Version proxy only starts when this complete
		// portable root is present (native IsCompletePortableRoot) beside it.
		private static readonly string[] BlindSoldierFiles =
		{
			"version.dll",
			@"Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe",
			@"Blind-Soldier\Runtime\dotnet\x86\host\fxr\9.0.8\hostfxr.dll",
			@"Reloaded-II\portable.txt",
			@"Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll",
			@"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll"
		};

		private static readonly Dictionary<string, string> SteamLanguages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "english", "en" },
			{ "french", "fr" },
			{ "german", "de" },
			{ "spanish", "es" }
		};

		private LegacyInstallation()
		{
		}

		internal string Root { get; private set; }

		internal LegacyLayoutProblem Problem { get; private set; }

		internal IReadOnlyList<LegacyGameExecutable> Executables { get; private set; } = Array.Empty<LegacyGameExecutable>();

		internal LegacyGameExecutable Selected { get; private set; }

		internal LegacyLanguageSource LanguageSource { get; private set; }

		internal LegacyDriverInfo Driver { get; private set; }

		internal bool BlindSoldierInstalled { get; private set; }

		internal bool BlindSoldierSupportsExecutable { get; private set; }

		internal string OriginalLauncherPath { get; private set; }

		internal bool LaunchedBySteam { get; private set; }

		internal static string DefaultSteamManifestPath(string root)
		{
			return Path.GetFullPath(Path.Combine(root, "..", "..", "appmanifest_39140.acf"));
		}

		internal static LegacyInstallation Detect(string root, string steamManifestPath, bool launchedBySteam)
		{
			string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var installation = new LegacyInstallation
			{
				Root = normalizedRoot,
				LaunchedBySteam = launchedBySteam,
				Driver = LegacyDriverInfo.Inspect(null)
			};

			if (File.Exists(Path.Combine(normalizedRoot, "FFVII.exe")))
			{
				installation.Problem = LegacyLayoutProblem.Steam2026Folder;
				return installation;
			}
			if (!Directory.Exists(Path.Combine(normalizedRoot, "data")))
			{
				installation.Problem = LegacyLayoutProblem.NotLegacyGameFolder;
				return installation;
			}

			var executables = LanguageCodes
				.Select(code => new LegacyGameExecutable("ff7_" + code + ".exe", Path.Combine(normalizedRoot, "ff7_" + code + ".exe"), code))
				.Where(executable => File.Exists(executable.FullPath))
				.ToArray();
			installation.Executables = executables;
			if (executables.Length == 0)
			{
				installation.Problem = File.Exists(Path.Combine(normalizedRoot, "ff7_ja.exe"))
					? LegacyLayoutProblem.NoSupportedExecutable
					: LegacyLayoutProblem.NotLegacyGameFolder;
				return installation;
			}

			installation.SelectExecutable(ReadSteamLanguage(steamManifestPath, Path.GetFileName(normalizedRoot)));
			installation.Driver = LegacyDriverInfo.Inspect(Path.Combine(normalizedRoot, "AF3DN.P"));
			installation.BlindSoldierInstalled = BlindSoldierFiles.All(file => File.Exists(Path.Combine(normalizedRoot, file)));
			installation.BlindSoldierSupportsExecutable = string.Equals(
				installation.Selected.FileName, BlindSoldierExecutableName, StringComparison.OrdinalIgnoreCase);
			string original = Path.Combine(normalizedRoot, OriginalLauncherFileName);
			installation.OriginalLauncherPath = File.Exists(original) ? original : null;
			return installation;
		}

		private void SelectExecutable(string steamLanguageCode)
		{
			if (Executables.Count == 1)
			{
				Selected = Executables[0];
				LanguageSource = LegacyLanguageSource.OnlyExecutable;
				return;
			}

			LegacyGameExecutable bySteam = Executables.FirstOrDefault(item => item.LanguageCode == steamLanguageCode);
			if (bySteam != null)
			{
				Selected = bySteam;
				LanguageSource = LegacyLanguageSource.SteamManifest;
				return;
			}

			LegacyGameExecutable english = Executables.FirstOrDefault(item => item.LanguageCode == "en");
			if (english != null)
			{
				Selected = english;
				LanguageSource = LegacyLanguageSource.EnglishPreference;
				return;
			}

			Selected = Executables[0];
			LanguageSource = LegacyLanguageSource.FirstExecutable;
		}

		// Returns the two-letter code from appmanifest_39140.acf when the manifest
		// describes this install folder; otherwise null.
		private static string ReadSteamLanguage(string manifestPath, string installFolderName)
		{
			if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
			{
				return null;
			}

			string text;
			try
			{
				text = File.ReadAllText(manifestPath);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				return null;
			}

			Match installDirectory = Regex.Match(text, "\"installdir\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
			if (!installDirectory.Success ||
				!string.Equals(installDirectory.Groups[1].Value, installFolderName, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			foreach (string block in new[] { "UserConfig", "MountedConfig" })
			{
				Match language = Regex.Match(text, "\"" + block + "\"\\s*\\{[^}]*?\"language\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
				if (language.Success && SteamLanguages.TryGetValue(language.Groups[1].Value.Trim(), out string code))
				{
					return code;
				}
			}
			return null;
		}
	}
}
