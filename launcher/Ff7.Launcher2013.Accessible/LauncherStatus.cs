using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlindSoldier.Ff7Launcher2013
{
	// The facts a sighted player could learn from the game folder, written as
	// sentences for the status box and the opening announcement.
	internal static class LauncherStatus
	{
		internal static LegacySaveProfile ChooseProfile(LegacyInstallation installation, IReadOnlyList<LegacySaveProfile> profiles, string preferredName)
		{
			if (profiles == null || profiles.Count == 0)
			{
				return null;
			}
			// FFNx always opens the first user_ folder, so only the original
			// driver, which receives the folder from this launcher, can choose.
			if (installation?.Driver?.Kind == LegacyDriverKind.Original && !string.IsNullOrEmpty(preferredName))
			{
				LegacySaveProfile preferred = profiles.FirstOrDefault(profile =>
					string.Equals(profile.Name, preferredName, StringComparison.OrdinalIgnoreCase));
				if (preferred != null)
				{
					return preferred;
				}
			}
			return profiles[0];
		}

		// The game needs a user_ folder for its saves: the original driver
		// receives it from this launcher and FFNx opens the first one.
		internal static bool NeedsLocalProfile(LegacyInstallation installation, IReadOnlyList<LegacySaveProfile> profiles)
		{
			return installation != null && installation.Problem == LegacyLayoutProblem.None &&
				installation.Driver.Kind != LegacyDriverKind.Missing &&
				(profiles == null || profiles.Count == 0);
		}

		internal static IReadOnlyList<StatusLine> Describe(
			LegacyInstallation installation,
			IReadOnlyList<LegacySaveProfile> profiles,
			LegacySaveProfile selectedProfile,
			string documentDirectory,
			LauncherText text)
		{
			var lines = new List<StatusLine>();
			switch (installation.Problem)
			{
				case LegacyLayoutProblem.Steam2026Folder:
					lines.Add(Warning(text[TextKey.Status2026]));
					return lines;
				case LegacyLayoutProblem.NotLegacyGameFolder:
					lines.Add(Warning(text[TextKey.StatusNotLegacy]));
					return lines;
				case LegacyLayoutProblem.NoSupportedExecutable:
					lines.Add(Warning(text[TextKey.StatusJapaneseOnly]));
					return lines;
			}

			LegacyGameExecutable executable = installation.Selected;
			lines.Add(Information(text.Format(TextKey.StatusGame, text.LanguageName(executable.LanguageCode), executable.FileName)));
			lines.Add(DriverLine(installation.Driver, text));

			bool original = installation.Driver.Kind == LegacyDriverKind.Original;
			profiles = profiles ?? Array.Empty<LegacySaveProfile>();
			if (profiles.Count == 0)
			{
				lines.Add(Warning(text.Format(TextKey.StatusNoSaveFolder, documentDirectory)));
			}
			else
			{
				LegacySaveProfile shown = original && selectedProfile != null ? selectedProfile : profiles[0];
				lines.Add(Information(text.Format(TextKey.StatusSaveFolder, shown.Name, shown.SaveFileCount)));
				if (original && profiles.Count > 1)
				{
					lines.Add(Information(text.Format(TextKey.StatusSaveFolderChoice, profiles.Count)));
				}
			}

			// This launcher sends the original driver its cloud-off message, and
			// FFNx has no Square Enix cloud saving.
			if (original || installation.Driver.Kind == LegacyDriverKind.Ffnx)
			{
				lines.Add(Information(text[TextKey.StatusCloudOff]));
			}
			if (original && profiles.Count > 0)
			{
				lines.Add(Information(text[TextKey.StatusMetadataKept]));
			}
			StatusLine controls = ControlsLine(installation, documentDirectory, text);
			if (controls != null)
			{
				lines.Add(controls);
			}
			if (original)
			{
				try
				{
					LegacyNativeTexts.Load(installation.Root, executable.LanguageCode);
				}
				catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
				{
					lines.Add(Warning(text.Format(TextKey.StatusNativeTextsMissing, exception.Message.TrimEnd('.'))));
				}
			}

			if (installation.BlindSoldierInstalled)
			{
				lines.Add(Information(text[TextKey.StatusBlindSoldierInstalled]));
				if (!installation.BlindSoldierSupportsExecutable)
				{
					lines.Add(Warning(text.Format(TextKey.StatusBlindSoldierLanguage, executable.FileName)));
				}
				if (original)
				{
					lines.Add(Warning(text[TextKey.StatusBlindSoldierOriginalDriver]));
				}
			}
			else
			{
				lines.Add(Warning(text[TextKey.StatusBlindSoldierMissing]));
			}

			if (!installation.LaunchedBySteam)
			{
				lines.Add(Warning(text[TextKey.StatusOutsideSteam]));
			}
			if (installation.OriginalLauncherPath != null)
			{
				lines.Add(Information(text[TextKey.StatusOriginalAvailable]));
			}
			return lines;
		}

		// What the game will read for its controls, as ff7_en.exe treats the
		// file the driver opens.
		private static StatusLine ControlsLine(LegacyInstallation installation, string documentDirectory, LauncherText text)
		{
			if (installation.Driver.Kind == LegacyDriverKind.Missing || string.IsNullOrEmpty(documentDirectory))
			{
				return null;
			}
			LegacyControlsLocation location = LegacyControlsFile.Resolve(installation, documentDirectory);
			if (location == null)
			{
				return Information(text[TextKey.StatusControlsUnknownDriver]);
			}
			string place;
			switch (location.Place)
			{
				case LegacyControlsPlace.GameFolder:
					place = text[TextKey.ControlsPlaceGameFolder];
					break;
				case LegacyControlsPlace.LanguageFolder:
					place = text.Format(TextKey.ControlsPlaceLanguageFolder, installation.Selected.LanguageCode);
					break;
				default:
					place = text[TextKey.ControlsPlaceDocuments];
					break;
			}
			LegacyControlsReading reading = LegacyControlsFile.Read(location.Path);
			switch (reading.Kind)
			{
				case LegacyControlsFileKind.Missing:
					// Play creates the original launcher's default in Documents.
					return Information(text[TextKey.StatusControlsDefault]);
				case LegacyControlsFileKind.OfficialDefaults:
					return Information(text.Format(TextKey.StatusControlsOfficial, place));
				case LegacyControlsFileKind.Custom:
					return Information(text.Format(TextKey.StatusControlsCustom, place));
				case LegacyControlsFileKind.NativeFallback:
					return Warning(text.Format(TextKey.StatusControlsNativeFallback, place));
				case LegacyControlsFileKind.Unverified:
					return Warning(text.Format(TextKey.StatusControlsUnverified, place, reading.Length));
				default:
					return Warning(text.Format(TextKey.StatusControlsUnreadable, place));
			}
		}

		private static StatusLine DriverLine(LegacyDriverInfo driver, LauncherText text)
		{
			switch (driver.Kind)
			{
				case LegacyDriverKind.Ffnx:
					return Information(string.IsNullOrEmpty(driver.Version)
						? text[TextKey.StatusDriverFfnxUnknownVersion]
						: text.Format(TextKey.StatusDriverFfnx, driver.Version));
				case LegacyDriverKind.Original:
					return Information(text[TextKey.StatusDriverOriginal]);
				case LegacyDriverKind.Unrecognized:
					return Information(text[TextKey.StatusDriverUnrecognized]);
				default:
					return Warning(text[TextKey.StatusDriverMissing]);
			}
		}

		private static StatusLine Information(string text) => new StatusLine(text, StatusSeverity.Information);

		private static StatusLine Warning(string text) => new StatusLine(text, StatusSeverity.Warning);
	}
}
