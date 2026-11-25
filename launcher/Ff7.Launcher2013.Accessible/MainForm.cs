using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FF7_Launcher;

namespace BlindSoldier.Ff7Launcher2013
{
	// Play, Settings, Original launcher and Exit are ordinary focusable buttons
	// that come first in the Tab order; the read-only status box follows with
	// every fact found in the game folder. When the game has no save folder
	// yet, a Create local save folder button comes first and has the focus.
	// Announcements use UI Automation notifications so they are spoken without
	// moving focus. Closing the window while a game starts cancels that start;
	// the window closes once the launch has stopped the game it started.
	internal class MainForm : Form
	{
		private readonly LauncherModel model;
		private readonly LauncherText text;
		private LegacySaveProfile selectedProfile;
		private IReadOnlyList<StatusLine> statusLines = Array.Empty<StatusLine>();
		private bool offerLocalProfile;
		private CancellationTokenSource launchCancellation;
		private bool closeAfterLaunch;

		internal MainForm(LauncherModel model)
		{
			this.model = model ?? throw new ArgumentNullException(nameof(model));
			text = model.Text ?? throw new ArgumentException("Launcher text is required.", nameof(model));

			SuspendLayout();
			Text = text[TextKey.WindowTitle];
			Name = "LegacyLauncherWindow";
			AutoScaleMode = AutoScaleMode.Font;
			Font = SystemFonts.MessageBoxFont;
			StartPosition = FormStartPosition.CenterScreen;
			MaximizeBox = false;
			MinimumSize = new Size(560, 360);
			ClientSize = new Size(640, 420);

			HeadingLabel = new Label
			{
				Name = "HeadingLabel",
				Text = text[TextKey.Heading],
				AutoSize = true,
				UseMnemonic = false,
				Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
				Margin = new Padding(3, 3, 3, 12),
				TabIndex = 0
			};

			CreateLocalSaveFolderButton = CreateButton("CreateLocalSaveFolderButton", TextKey.CreateLocalSaveFolder, 0);
			PlayButton = CreateButton("PlayButton", TextKey.Play, 1);
			SettingsButton = CreateButton("SettingsButton", TextKey.Settings, 2);
			OriginalButton = CreateButton("OriginalLauncherButton", TextKey.OriginalLauncher, 3);
			ExitButton = CreateButton("ExitButton", TextKey.Exit, 4);
			var buttons = new FlowLayoutPanel
			{
				Name = "LauncherChoices",
				AccessibleName = string.Empty,
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				Dock = DockStyle.Fill,
				TabIndex = 1
			};
			buttons.Controls.AddRange(new Control[] { CreateLocalSaveFolderButton, PlayButton, SettingsButton, OriginalButton, ExitButton });

			StatusLabel = new Label
			{
				Name = "StatusLabel",
				Text = text[TextKey.StatusLabel],
				AutoSize = true,
				UseMnemonic = true,
				Margin = new Padding(3, 12, 3, 3),
				TabIndex = 2
			};
			StatusBox = new TextBox
			{
				Name = "StatusBox",
				ReadOnly = true,
				Multiline = true,
				WordWrap = true,
				ScrollBars = ScrollBars.Vertical,
				TabStop = true,
				BackColor = SystemColors.Window,
				Dock = DockStyle.Fill,
				TabIndex = 3
			};
			SettingsAccessibility.NameControl(StatusBox, StatusLabel);

			var layout = new TableLayoutPanel
			{
				Name = "LauncherLayout",
				AccessibleName = string.Empty,
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(12)
			};
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(HeadingLabel, 0, 0);
			layout.Controls.Add(buttons, 0, 1);
			layout.Controls.Add(StatusLabel, 0, 2);
			layout.Controls.Add(StatusBox, 0, 3);
			Controls.Add(layout);

			AcceptButton = PlayButton;
			CancelButton = ExitButton;
			CreateLocalSaveFolderButton.Click += OnCreateLocalSaveFolderClick;
			PlayButton.Click += OnPlayClick;
			SettingsButton.Click += OnSettingsClick;
			OriginalButton.Click += OnOriginalClick;
			ExitButton.Click += delegate { Close(); };
			ResumeLayout(true);

			selectedProfile = LauncherStatus.ChooseProfile(model.Installation, model.Profiles, model.Preferences?.ReadSaveFolder());
			RefreshStatus();
			ActiveControl = FirstControl;
			Shown += OnShown;
		}

		internal Button CreateLocalSaveFolderButton { get; }

		internal Button PlayButton { get; }

		internal Button SettingsButton { get; }

		internal Button OriginalButton { get; }

		internal Button ExitButton { get; }

		internal TextBox StatusBox { get; }

		internal Label StatusLabel { get; }

		internal Label HeadingLabel { get; }

		internal bool Busy { get; private set; }

		// The control a player needs first: the save folder offer, or Play.
		private Button FirstControl => offerLocalProfile ? CreateLocalSaveFolderButton : PlayButton;

		private Button CreateButton(string name, TextKey key, int tabIndex)
		{
			var button = new AccessibleSettingsButton
			{
				Name = name,
				Text = text[key],
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				MinimumSize = new Size(110, 32),
				UseMnemonic = true,
				TabIndex = tabIndex
			};
			SettingsAccessibility.NameButton(button);
			return button;
		}

		private void OnShown(object sender, EventArgs e)
		{
			// Steam starts the launcher from its own foreground window; taking
			// activation here puts the screen reader's focus on the first control.
			if (!ShowWithoutActivation)
			{
				Activate();
			}
			ReturnFocus(FirstControl);
			Announce(LauncherMessages.StartupAnnouncement(statusLines, model.Installation, model.Profiles, text));
		}

		private void RefreshStatus()
		{
			statusLines = LauncherStatus.Describe(model.Installation, model.Profiles, selectedProfile, model.DocumentDirectory, text);
			StatusBox.Text = string.Join(Environment.NewLine, statusLines.Select(line => line.Text));
			offerLocalProfile = LauncherStatus.NeedsLocalProfile(model.Installation, model.Profiles);
			CreateLocalSaveFolderButton.Visible = offerLocalProfile;
		}

		private void Announce(string message)
		{
			model.Log("Announcement: " + message);
			model.Announcer?.Announce(StatusBox, message);
		}

		private void SetBusy(bool busy)
		{
			Busy = busy;
			UseWaitCursor = busy;
		}

		private void ReturnFocus(Control control)
		{
			ActiveControl = control;
			control.Focus();
		}

		private async void OnPlayClick(object sender, EventArgs e)
		{
			model.Log("Play pressed" + (Busy ? " while busy; ignored." : "."));
			if (Busy)
			{
				return;
			}
			SetBusy(true);
			Announce(text[TextKey.Starting]);
			launchCancellation = new CancellationTokenSource();
			var request = new LegacyLaunchRequest
			{
				Installation = model.Installation,
				SaveProfile = selectedProfile,
				DocumentDirectory = model.DocumentDirectory,
				GameVersionText = model.GameVersionText,
				Cancellation = launchCancellation.Token
			};

			LegacyLaunchResult result;
			try
			{
				result = await Task.Run(() => model.LaunchService.Launch(request));
			}
			catch (Exception exception)
			{
				model.Log("Launch failed unexpectedly: " + exception);
				result = new LegacyLaunchResult(LegacyLaunchFailure.StartFailed, exception.Message, null, null);
			}
			finally
			{
				launchCancellation.Dispose();
				launchCancellation = null;
			}

			if (closeAfterLaunch)
			{
				// The player closed the launcher while the game was starting. A
				// start that finished first still belongs to this launch, and
				// without the launcher its game would hang at its first save.
				if (result.Started)
				{
					StopStartedGame(result.Process);
				}
				model.Log("The start ended after the launcher was closed: " + result.Failure + ".");
				result.Dispose();
				Close();
				return;
			}

			if (!result.Started)
			{
				string message = LauncherMessages.DescribeFailure(result, request, text, model.LogPath);
				result.Dispose();
				model.Log(message);
				model.Dialogs.ShowError(this, message);
				SetBusy(false);
				ReturnFocus(FirstControl);
				return;
			}

			Announce(text[TextKey.Started]);
			DateTime started = model.UtcNow();
			Hide();
			int exitCode = await Task.Factory.StartNew(() =>
			{
				result.Process.WaitForExit();
				return result.Process.ExitCode;
			}, TaskCreationOptions.LongRunning);
			TimeSpan elapsed = model.UtcNow() - started;
			result.Dispose();
			model.Log("The game exited with code " + exitCode + " after " + elapsed.TotalSeconds.ToString("0.0") + " seconds.");

			if (exitCode != 0 && elapsed < model.EarlyExitWindow)
			{
				Show();
				Activate();
				string message = LauncherMessages.DescribeEarlyExit(elapsed, exitCode, text, model.LogPath);
				model.Dialogs.ShowError(this, message);
				SetBusy(false);
				ReturnFocus(PlayButton);
				return;
			}
			Close();
		}

		private void OnCreateLocalSaveFolderClick(object sender, EventArgs e)
		{
			model.Log("Create local save folder pressed" + (Busy ? " while busy; ignored." : "."));
			if (Busy)
			{
				return;
			}
			SetBusy(true);
			try
			{
				LegacySaveProfile created = LegacySaveProfiles.CreateLocal(model.DocumentDirectory);
				model.Log("Local save folder ready: " + created.FullPath);
				model.Profiles = LegacySaveProfiles.Find(model.DocumentDirectory);
				selectedProfile = model.Profiles.FirstOrDefault(profile =>
					string.Equals(profile.Name, created.Name, StringComparison.OrdinalIgnoreCase)) ?? created;
				if (model.Installation.Driver.Kind == LegacyDriverKind.Original)
				{
					model.Preferences?.WriteSaveFolder(selectedProfile.Name);
				}
				RefreshStatus();
				ReturnFocus(PlayButton);
				Announce(text.Format(TextKey.LocalSaveFolderCreated, selectedProfile.Name, model.DocumentDirectory));
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
				exception is ArgumentException || exception is NotSupportedException)
			{
				model.Log("The local save folder could not be created: " + exception);
				model.Dialogs.ShowError(this, text.Format(TextKey.LocalSaveFolderFailed, exception.Message));
				ReturnFocus(CreateLocalSaveFolderButton);
			}
			finally
			{
				SetBusy(false);
			}
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (launchCancellation != null && e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				if (!closeAfterLaunch)
				{
					closeAfterLaunch = true;
					model.Log("Close requested while FINAL FANTASY VII was starting; stopping the start.");
					launchCancellation.Cancel();
					Announce(text[TextKey.StoppingStart]);
				}
			}
			base.OnFormClosing(e);
		}

		private void StopStartedGame(ILegacyGameProcess process)
		{
			model.Log("Stopping game process " + process.Id + ", which started just as the launcher was closed.");
			try
			{
				process.Kill();
				process.ExitedHandle.WaitOne(LegacyGameLauncher.StopTimeout);
			}
			catch (Exception exception) when (exception is InvalidOperationException || exception is ObjectDisposedException)
			{
				model.Log("The game could not be confirmed closed: " + exception.Message);
			}
		}

		private async void OnOriginalClick(object sender, EventArgs e)
		{
			model.Log("Original launcher pressed" + (Busy ? " while busy; ignored." : "."));
			if (Busy)
			{
				return;
			}
			string original = model.Installation.OriginalLauncherPath;
			if (original == null)
			{
				model.Dialogs.ShowInformation(this, text[TextKey.OriginalMissing]);
				ReturnFocus(OriginalButton);
				return;
			}

			SetBusy(true);
			Announce(text[TextKey.OriginalStarting]);
			ILegacyGameProcess process;
			try
			{
				process = model.OriginalLauncherStarter.Start(original, model.Installation.Root);
			}
			catch (Exception exception)
			{
				model.Log("The original launcher could not start: " + exception);
				model.Dialogs.ShowError(this, text.Format(TextKey.OriginalStartFailed, exception.Message));
				SetBusy(false);
				ReturnFocus(OriginalButton);
				return;
			}

			Hide();
			await Task.Factory.StartNew(process.WaitForExit, TaskCreationOptions.LongRunning);
			process.Dispose();
			Close();
		}

		private void OnSettingsClick(object sender, EventArgs e)
		{
			model.Log("Settings pressed" + (Busy ? " while busy; ignored." : "."));
			if (Busy)
			{
				return;
			}
			SetBusy(true);
			try
			{
				var store = new LegacySettingsStore(model.DocumentDirectory);
				LegacySettingsFileState displayState = store.ReadDisplay(out LegacyDisplaySettings display);
				LegacySettingsFileState soundState = store.ReadSound(out LegacySoundSettings sound);
				var request = new SettingsEditRequest
				{
					Installation = model.Installation,
					Display = display,
					DisplayState = displayState,
					Sound = sound,
					Modes = model.DisplayModes?.GetModes() ?? Array.Empty<DisplayMode>(),
					Profiles = model.Profiles,
					SelectedProfile = selectedProfile
				};

				SettingsEditResult result = model.SettingsPresenter.Edit(this, text, request);
				if (result == null)
				{
					return;
				}

				try
				{
					if (result.Display != null && (displayState != LegacySettingsFileState.Valid || !result.Display.Encode().SequenceEqual(display.Encode())))
					{
						store.WriteDisplay(result.Display);
						model.Log("Saved display settings: " + store.DisplayPath);
					}
					if (result.Sound != null && (soundState != LegacySettingsFileState.Valid || !result.Sound.Encode().SequenceEqual(sound.Encode())))
					{
						store.WriteSound(result.Sound);
						model.Log("Saved sound settings: " + store.SoundPath);
					}
					if (result.SelectedProfile != null && !ReferenceEquals(result.SelectedProfile, selectedProfile))
					{
						selectedProfile = result.SelectedProfile;
						model.Preferences?.WriteSaveFolder(selectedProfile.Name);
						model.Log("Chosen save folder: " + selectedProfile.FullPath);
					}
					RefreshStatus();
					Announce(text[TextKey.SettingsSaved]);
				}
				catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
				{
					model.Log("Settings could not be saved: " + exception);
					model.Dialogs.ShowError(this, text.Format(TextKey.SettingsSaveFailed, exception.Message));
				}
			}
			finally
			{
				SetBusy(false);
				ReturnFocus(SettingsButton);
			}
		}
	}
}
