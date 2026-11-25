using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BlindSoldier.Ff7Launcher2013
{
	internal sealed class DisplayMode
	{
		internal DisplayMode(uint width, uint height, IReadOnlyList<uint> refreshRates)
		{
			Width = width;
			Height = height;
			RefreshRates = refreshRates ?? Array.Empty<uint>();
		}

		internal uint Width { get; }

		internal uint Height { get; }

		internal IReadOnlyList<uint> RefreshRates { get; }
	}

	internal interface IDisplayModeSource
	{
		IReadOnlyList<DisplayMode> GetModes();
	}

	internal interface ILauncherDialogs
	{
		void ShowError(IWin32Window owner, string text);

		void ShowInformation(IWin32Window owner, string text);
	}

	internal interface IAnnouncer
	{
		void Announce(Control anchor, string text);
	}

	internal interface ILegacyLaunchService
	{
		LegacyLaunchResult Launch(LegacyLaunchRequest request);
	}

	internal sealed class SettingsEditRequest
	{
		internal LegacyInstallation Installation { get; set; }

		internal LegacyDisplaySettings Display { get; set; }

		internal LegacySettingsFileState DisplayState { get; set; }

		internal LegacySoundSettings Sound { get; set; }

		internal IReadOnlyList<DisplayMode> Modes { get; set; } = Array.Empty<DisplayMode>();

		internal IReadOnlyList<LegacySaveProfile> Profiles { get; set; } = Array.Empty<LegacySaveProfile>();

		internal LegacySaveProfile SelectedProfile { get; set; }
	}

	internal sealed class SettingsEditResult
	{
		internal LegacyDisplaySettings Display { get; set; }

		internal LegacySoundSettings Sound { get; set; }

		internal LegacySaveProfile SelectedProfile { get; set; }
	}

	internal interface ISettingsPresenter
	{
		// Returns null when the player cancels.
		SettingsEditResult Edit(IWin32Window owner, LauncherText text, SettingsEditRequest request);
	}

	internal enum StatusSeverity
	{
		Information,
		Warning
	}

	internal sealed class StatusLine
	{
		internal StatusLine(string text, StatusSeverity severity)
		{
			Text = text;
			Severity = severity;
		}

		internal string Text { get; }

		internal StatusSeverity Severity { get; }
	}

	internal sealed class LauncherModel
	{
		internal LegacyInstallation Installation { get; set; }

		internal IReadOnlyList<LegacySaveProfile> Profiles { get; set; } = Array.Empty<LegacySaveProfile>();

		internal string DocumentDirectory { get; set; }

		internal LauncherText Text { get; set; }

		internal ILegacyLaunchService LaunchService { get; set; }

		internal ILegacyGameProcessStarter OriginalLauncherStarter { get; set; }

		internal ILauncherDialogs Dialogs { get; set; }

		internal IAnnouncer Announcer { get; set; }

		internal ISettingsPresenter SettingsPresenter { get; set; }

		internal IDisplayModeSource DisplayModes { get; set; }

		internal LauncherPreferences Preferences { get; set; }

		internal Action<string> Log { get; set; } = delegate { };

		internal string LogPath { get; set; } = string.Empty;

		internal string GameVersionText { get; set; } = "Blind Soldier FF7 2013 Launcher 1.0.0";

		internal TimeSpan EarlyExitWindow { get; set; } = TimeSpan.FromSeconds(15);

		internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
	}
}
