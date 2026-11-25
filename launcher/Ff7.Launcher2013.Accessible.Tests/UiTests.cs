using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class UiTests
	{
		private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

		internal static void Register(TestRunner runner)
		{
			runner.Add("status describes an English FFNx installation", StatusDescribesFfnxInstallation);
			runner.Add("status warns about the executable, driver, Steam and lists the original launcher", StatusWarnsAboutLimits);
			runner.Add("status explains a missing save folder and Blind Soldier", StatusExplainsMissingSaveFolder);
			runner.Add("status names each unsupported folder", StatusNamesUnsupportedFolders);
			runner.Add("profile choice honors a preference only for the original driver", ProfileChoice);
			runner.Add("every language defines every text with the same placeholders and mnemonics", EveryLanguageDefinesEveryText);
			runner.Add("mnemonics are unique within each window in every language", MnemonicsAreUnique);
			runner.Add("the game language selects the launcher language", GameLanguageSelectsLauncherLanguage);
			runner.Add("failures and early exits are explained with cause, action and log", FailuresAreExplained);
			runner.Add("startup announcement reads readiness and every warning", StartupAnnouncement);
			runner.Add("preferences remember the chosen save folder", PreferencesRememberSaveFolder);
			runner.Add("main window exposes named controls in keyboard order", MainWindowControls);
			runner.Add("a refused launch is reported and the needed control is offered again", RefusedLaunchIsReported);
			runner.Add("first run offers a labelled local save folder before Play", FirstRunOffersLocalSaveFolder);
			runner.Add("creating the local save folder selects it and moves to Play", CreatingLocalSaveFolderSelectsIt);
			runner.Add("a local save folder that cannot be created is explained", LocalSaveFolderFailureExplained);
			runner.Add("UI Automation Invoke activates Play, Settings and Create local save folder", AutomationInvokeActivatesButtons);
			runner.Add("the window hides while the game runs and closes after it exits", WindowHidesWhileGameRuns);
			runner.Add("closing while the game starts stops that start, then closes", ClosingDuringStartStopsIt);
			runner.Add("a game that finished starting as the launcher closed is stopped, not left behind", ClosingAsStartFinishesStopsGame);
			runner.Add("Exit closes at once when no game is starting", ExitClosesWhenIdle);
			runner.Add("an early failing exit brings the window back with the exit code", EarlyFailingExitReturns);
			runner.Add("the original launcher button explains a missing backup", OriginalButtonExplainsMissingBackup);
			runner.Add("the original launcher button starts the preserved launcher", OriginalButtonStartsBackup);
			runner.Add("saved settings are written and announced", SavedSettingsAreWritten);
			runner.Add("cancelled settings write nothing", CancelledSettingsWriteNothing);
			runner.Add("settings dialog labels every control and shows current values", SettingsDialogLabelsControls);
			runner.Add("layout containers do not borrow a neighbouring label as their name", LayoutContainersHaveNoBorrowedNames);
			runner.Add("settings dialog builds its result from the controls", SettingsDialogBuildsResult);
			runner.Add("FFNx settings show sound only and explain display settings", FfnxSettingsExplainDisplay);
			runner.Add("an unlisted saved resolution and unreadable file stay visible", UnlistedResolutionAndUnreadableNote);
		}

		private static LauncherText English => LauncherText.For("en");

		private static LegacyGameTree FfnxGame(string label, bool blindSoldier)
		{
			var game = new LegacyGameTree(label).WithStandard2013Files();
			AssemblyFactory.CompileLibraryWithProduct(Path.Combine(game.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
			if (blindSoldier)
			{
				AddBlindSoldier(game);
			}
			return game;
		}

		private static void AddBlindSoldier(LegacyGameTree game)
		{
			foreach (string file in new[]
			{
				"version.dll",
				@"Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe",
				@"Blind-Soldier\Runtime\dotnet\x86\host\fxr\9.0.8\hostfxr.dll",
				@"Reloaded-II\portable.txt",
				@"Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll",
				@"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll"
			})
			{
				game.WriteFile(file, 16);
			}
		}

		private static LegacyGameTree WithProfile(LegacyGameTree game)
		{
			game.CreateProfile("user_1", "save00.ff7");
			return game;
		}

		private static string[] Lines(IEnumerable<StatusLine> lines)
		{
			return lines.Select(line => (line.Severity == StatusSeverity.Warning ? "W " : "I ") + line.Text).ToArray();
		}

		private static void StatusDescribesFfnxInstallation()
		{
			using (var game = FfnxGame("status-ffnx", blindSoldier: true))
			{
				game.CreateProfile("user_1", "save00.ff7", "save01.ff7");
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
				var lines = LauncherStatus.Describe(installation, profiles, profiles[0], game.DocumentDirectory, English);
				Check.Sequence(new[]
				{
					"I Game: English, ff7_en.exe.",
					"I Graphics driver: FFNx 1.25.2.0. The game starts without the original launcher handshake.",
					"I Save folder: user_1. Save files: 2.",
					"I Cloud saving: off. Saves stay on this PC, and this launcher does not sign in to Square Enix.",
					"I Blind Soldier accessibility is installed in this game folder."
				}, Lines(lines), "status lines");
			}
		}

		private static void StatusWarnsAboutLimits()
		{
			using (var game = new LegacyGameTree("status-warn").WithStandard2013Files("ff7_fr.exe").WithDriverBytes(700000))
			{
				AddBlindSoldier(game);
				game.WriteFile("FF7_Launcher.original.exe", 100);
				game.CreateProfile("user_1");
				game.CreateProfile("user_2", "save00.ff7");
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, false);
				var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
				var lines = LauncherStatus.Describe(installation, profiles, profiles[1], game.DocumentDirectory, English);
				Check.Sequence(new[]
				{
					"I Game: French, ff7_fr.exe.",
					"I Graphics driver: original 2013 driver. This launcher sends the start-up information it needs.",
					"I Save folder: user_2. Save files: 1.",
					"I 2 save folders were found. Choose one in Settings.",
					"I Cloud saving: off. Saves stay on this PC, and this launcher does not sign in to Square Enix.",
					"I After each save, this launcher updates metadata.xml as the original launcher does, so the original launcher keeps these saves.",
					"I Blind Soldier accessibility is installed in this game folder.",
					"W Blind Soldier speaks only with the English executable, ff7_en.exe. It does not start with ff7_fr.exe.",
					"W Blind Soldier has not been confirmed with the original 2013 graphics driver. If the game stays silent, install FFNx.",
					"W This launcher was started outside Steam. Start FINAL FANTASY VII (2013) from Steam so Steam features work.",
					"I The original Square Enix launcher is available with the Original launcher button."
				}, Lines(lines), "status lines");
			}
		}

		private static void StatusExplainsMissingSaveFolder()
		{
			using (var game = new LegacyGameTree("status-nosave").WithStandard2013Files().WithDriverBytes(700000))
			{
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				var lines = LauncherStatus.Describe(installation, Array.Empty<LegacySaveProfile>(), null, game.DocumentDirectory, English);
				Check.Sequence(new[]
				{
					"I Game: English, ff7_en.exe.",
					"I Graphics driver: original 2013 driver. This launcher sends the start-up information it needs.",
					"W No save folder was found in " + game.DocumentDirectory + ". Create local save folder makes one on this PC, or sign in once with the original launcher.",
					"I Cloud saving: off. Saves stay on this PC, and this launcher does not sign in to Square Enix.",
					"W Blind Soldier accessibility was not found in this game folder."
				}, Lines(lines), "status lines");
			}
		}

		private static void StatusNamesUnsupportedFolders()
		{
			using (var game = new LegacyGameTree("status-2026"))
			{
				game.WriteFile("FFVII.exe", 10);
				var lines = LauncherStatus.Describe(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), Array.Empty<LegacySaveProfile>(), null, game.DocumentDirectory, English);
				Check.Sequence(new[] { "W This folder contains the 2026 edition of FINAL FANTASY VII. Use the accessible FFVII_LAUNCHER.exe that Blind Soldier provides for it." }, Lines(lines), "2026");
			}
			using (var game = new LegacyGameTree("status-empty"))
			{
				var lines = LauncherStatus.Describe(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), Array.Empty<LegacySaveProfile>(), null, game.DocumentDirectory, English);
				Check.Sequence(new[] { "W This folder does not contain FINAL FANTASY VII (2013). Install this launcher in the game folder that holds ff7_en.exe." }, Lines(lines), "not legacy");
			}
			using (var game = new LegacyGameTree("status-ja").WithStandard2013Files("ff7_ja.exe"))
			{
				var lines = LauncherStatus.Describe(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), Array.Empty<LegacySaveProfile>(), null, game.DocumentDirectory, English);
				Check.Sequence(new[] { "W Only ff7_ja.exe was found. The Steam 2013 edition uses ff7_en.exe, ff7_fr.exe, ff7_de.exe or ff7_es.exe." }, Lines(lines), "Japanese only");
			}
		}

		private static void ProfileChoice()
		{
			using (var original = new LegacyGameTree("choice-original").WithStandard2013Files().WithDriverBytes(700000))
			using (var ffnx = FfnxGame("choice-ffnx", blindSoldier: false))
			{
				original.CreateProfile("user_1");
				original.CreateProfile("user_2");
				var profiles = LegacySaveProfiles.Find(original.DocumentDirectory);
				var originalInstall = LegacyInstallation.Detect(original.Root, original.ManifestPath, true);
				var ffnxInstall = LegacyInstallation.Detect(ffnx.Root, ffnx.ManifestPath, true);
				Check.Equal("user_2", LauncherStatus.ChooseProfile(originalInstall, profiles, "user_2").Name, "preferred folder");
				Check.Equal("user_1", LauncherStatus.ChooseProfile(originalInstall, profiles, "user_9").Name, "missing preference");
				Check.Equal("user_1", LauncherStatus.ChooseProfile(ffnxInstall, profiles, "user_2").Name, "FFNx uses the first folder");
				Check.Null(LauncherStatus.ChooseProfile(originalInstall, Array.Empty<LegacySaveProfile>(), "user_2"), "no folders");
			}
		}

		private static readonly Regex Placeholder = new Regex(@"\{\d+\}");

		private static void EveryLanguageDefinesEveryText()
		{
			Check.Sequence(new[] { "en", "fr", "de", "es" }, LauncherText.SupportedLanguageCodes, "languages");
			foreach (string code in LauncherText.SupportedLanguageCodes)
			{
				LauncherText text = LauncherText.For(code);
				foreach (TextKey key in Enum.GetValues(typeof(TextKey)))
				{
					Check.True(text.Has(key) && !string.IsNullOrWhiteSpace(text[key]), code + " " + key + " defined");
					string[] expected = Placeholder.Matches(English[key]).Cast<Match>().Select(match => match.Value).OrderBy(value => value).ToArray();
					string[] actual = Placeholder.Matches(text[key]).Cast<Match>().Select(match => match.Value).OrderBy(value => value).ToArray();
					Check.Sequence(expected, actual, code + " " + key + " placeholders");
					Check.Equal(English[key].Count(character => character == '&'), text[key].Count(character => character == '&'), code + " " + key + " mnemonic count");
				}
			}
		}

		private static void MnemonicsAreUnique()
		{
			var windows = new[]
			{
				new[] { TextKey.StatusLabel, TextKey.Play, TextKey.Settings, TextKey.OriginalLauncher, TextKey.Exit, TextKey.CreateLocalSaveFolder },
				new[] { TextKey.Fullscreen, TextKey.Resolution, TextKey.RefreshRate, TextKey.KeepAspect, TextKey.LinearFiltering, TextKey.OriginalMode, TextKey.SoundEffectsVolume, TextKey.MusicVolume, TextKey.SaveFolder }
			};
			foreach (string code in LauncherText.SupportedLanguageCodes)
			{
				LauncherText text = LauncherText.For(code);
				foreach (TextKey[] window in windows)
				{
					var letters = window.Select(key =>
					{
						int index = text[key].IndexOf('&');
						Check.True(index >= 0 && index + 1 < text[key].Length, code + " " + key + " has a mnemonic");
						return char.ToUpperInvariant(text[key][index + 1]);
					}).ToArray();
					Check.Equal(letters.Length, letters.Distinct().Count(), code + " unique mnemonics " + new string(letters));
				}
			}
		}

		private static void GameLanguageSelectsLauncherLanguage()
		{
			Check.Equal("&Jouer", LauncherText.For("fr")[TextKey.Play], "French play");
			Check.Equal("&Spielen", LauncherText.For("de")[TextKey.Play], "German play");
			Check.Equal("&Jugar", LauncherText.For("es")[TextKey.Play], "Spanish play");
			Check.Equal("en", LauncherText.For("ja").LanguageCode, "Japanese falls back to English");
			Check.Equal("en", LauncherText.For(null).LanguageCode, "missing language falls back to English");
		}

		private static void FailuresAreExplained()
		{
			var request = new LegacyLaunchRequest { DocumentDirectory = @"C:\Docs\Square Enix\FINAL FANTASY VII Steam" };
			string NoProfile() => LauncherMessages.DescribeFailure(new LegacyLaunchResult(LegacyLaunchFailure.NoSaveProfile, request.DocumentDirectory, null, null), request, English, @"C:\Game\Blind-Soldier\Logs\FF7_Launcher-2013.log");
			Check.Equal(string.Join(Environment.NewLine,
				"FINAL FANTASY VII could not start.",
				@"Cause: No save folder exists in C:\Docs\Square Enix\FINAL FANTASY VII Steam.",
				"Action: Press Create local save folder, then press Play. To use your Square Enix account folder instead, sign in once with the original launcher.",
				@"Log: C:\Game\Blind-Soldier\Logs\FF7_Launcher-2013.log"), NoProfile(), "no save folder");

			Check.Equal(string.Join(Environment.NewLine,
				"FINAL FANTASY VII could not start.",
				"Cause: Blind Soldier could not load into FINAL FANTASY VII, so the game was closed instead of running without speech. Detail: CreateRemoteThread failed with error 5.",
				"Action: Allow FF7_Launcher.exe in your security software, or install FFNx, which loads Blind Soldier directly. Then press Play again.",
				@"Log: L.log"), LauncherMessages.DescribeFailure(new LegacyLaunchResult(LegacyLaunchFailure.BlindSoldierLoadFailed, "CreateRemoteThread failed with error 5.", null, null), request, English, "L.log"), "Blind Soldier load failure");

			Check.Equal(string.Join(Environment.NewLine,
				"FINAL FANTASY VII could not start.",
				"Cause: FINAL FANTASY VII closed while receiving its start-up information. Exit code: 3.",
				"Action: Check the log, then try again.",
				@"Log: L.log"), LauncherMessages.DescribeFailure(new LegacyLaunchResult(LegacyLaunchFailure.GameExitedDuringHandshake, "3", null, null), request, English, "L.log"), "exit during handshake");

			Check.Equal(string.Join(Environment.NewLine,
				"FINAL FANTASY VII could not start.",
				"Cause: FINAL FANTASY VII closed after 2 seconds with exit code 5.",
				"Action: Check the log, then try again.",
				@"Log: L.log"), LauncherMessages.DescribeEarlyExit(TimeSpan.FromMilliseconds(2400), 5, English, "L.log"), "early exit");

			var causes = new HashSet<string>(StringComparer.Ordinal);
			foreach (LegacyLaunchFailure failure in Enum.GetValues(typeof(LegacyLaunchFailure)))
			{
				if (failure == LegacyLaunchFailure.None)
				{
					continue;
				}
				string message = LauncherMessages.DescribeFailure(new LegacyLaunchResult(failure, "detail", null, null), request, English, "L.log");
				string cause = message.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[1];
				Check.True(cause.StartsWith("Cause: ", StringComparison.Ordinal) && cause.Length > 10, failure + " has a cause");
				Check.True(causes.Add(cause), failure + " has its own cause");
			}
		}

		private static void StartupAnnouncement()
		{
			using (var game = new LegacyGameTree("announce").WithStandard2013Files("ff7_fr.exe").WithDriverBytes(700000))
			{
				AddBlindSoldier(game);
				game.CreateProfile("user_1");
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
				var lines = LauncherStatus.Describe(installation, profiles, profiles[0], game.DocumentDirectory, English);
				Check.Equal("Ready. Press Enter to play. " +
					"Warning: Blind Soldier speaks only with the English executable, ff7_en.exe. It does not start with ff7_fr.exe. " +
					"Warning: Blind Soldier has not been confirmed with the original 2013 graphics driver. If the game stays silent, install FFNx.",
					LauncherMessages.StartupAnnouncement(lines, installation, profiles, English), "announcement");
			}
			using (var game = new LegacyGameTree("announce-2026"))
			{
				game.WriteFile("FFVII.exe", 10);
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				var lines = LauncherStatus.Describe(installation, Array.Empty<LegacySaveProfile>(), null, game.DocumentDirectory, English);
				Check.Equal("This folder contains the 2026 edition of FINAL FANTASY VII. Use the accessible FFVII_LAUNCHER.exe that Blind Soldier provides for it.",
					LauncherMessages.StartupAnnouncement(lines, installation, Array.Empty<LegacySaveProfile>(), English), "problem announcement");
			}
		}

		private static void PreferencesRememberSaveFolder()
		{
			using (var scratch = new ScratchDirectory("prefs"))
			{
				var preferences = new LauncherPreferences(scratch.Combine("Blind-Soldier", "Launcher2013", "save-folder.txt"));
				Check.Null(preferences.ReadSaveFolder(), "nothing remembered");
				preferences.WriteSaveFolder("user_42");
				Check.Equal("user_42", new LauncherPreferences(preferences.Path).ReadSaveFolder(), "remembered");
				File.WriteAllText(preferences.Path, "..\\evil");
				Check.Null(preferences.ReadSaveFolder(), "unsafe value ignored");
			}
		}

		private sealed class Fixture : IDisposable
		{
			internal LegacyGameTree Game { get; }

			internal RecordingDialogs Dialogs { get; } = new RecordingDialogs();

			internal RecordingAnnouncer Announcer { get; } = new RecordingAnnouncer();

			internal RecordingStarter Original { get; } = new RecordingStarter();

			internal LauncherModel Model { get; }

			internal Fixture(LegacyGameTree game, ILegacyLaunchService launch, ISettingsPresenter settings = null)
			{
				Game = game;
				try
				{
					Model = CreateModel(game, launch, settings);
				}
				catch
				{
					game.Dispose();
					throw;
				}
			}

			private LauncherModel CreateModel(LegacyGameTree game, ILegacyLaunchService launch, ISettingsPresenter settings)
			{
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
				return new LauncherModel
				{
					Installation = installation,
					Profiles = profiles,
					DocumentDirectory = game.DocumentDirectory,
					Text = English,
					LaunchService = launch,
					OriginalLauncherStarter = Original,
					Dialogs = Dialogs,
					Announcer = Announcer,
					SettingsPresenter = settings ?? new FakeSettingsPresenter(_ => null),
					DisplayModes = new FixedDisplayModes(new DisplayMode(1920, 1080, new uint[] { 60 })),
					Preferences = new LauncherPreferences(Path.Combine(game.Root, "Blind-Soldier", "Launcher2013", "save-folder.txt")),
					LogPath = @"C:\Logs\launcher.log",
					EarlyExitWindow = TimeSpan.FromSeconds(15)
				};
			}

			public void Dispose()
			{
				Game.Dispose();
			}
		}

		private static void MainWindowControls()
		{
			using (var fixture = new Fixture(WithProfile(FfnxGame("main", blindSoldier: true)), new FakeLaunchService(_ => throw new InvalidOperationException("not pressed"))))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() =>
				{
					Check.Equal("FINAL FANTASY VII (2013) accessible launcher", form.Text, "title");
					Check.Equal("FINAL FANTASY VII (2013)", form.HeadingLabel.Text, "heading");
					Check.Sequence(new[] { "Play", "Settings", "Original launcher", "Exit" },
						new[] { form.PlayButton, form.SettingsButton, form.OriginalButton, form.ExitButton }.Select(button => button.AccessibleName), "button names");
					Check.Equal("Status", form.StatusBox.AccessibleName, "status name");
					Check.True(form.StatusBox.ReadOnly && form.StatusBox.TabStop && form.StatusBox.Multiline, "status is a readable stop");
					Check.Equal(string.Join(Environment.NewLine,
						"Game: English, ff7_en.exe.",
						"Graphics driver: FFNx 1.25.2.0. The game starts without the original launcher handshake.",
						"Save folder: user_1. Save files: 1.",
						"Cloud saving: off. Saves stay on this PC, and this launcher does not sign in to Square Enix.",
						"Blind Soldier accessibility is installed in this game folder."), form.StatusBox.Text, "status text");
						Check.False(form.CreateLocalSaveFolderButton.Visible, "no local save folder offer when a folder exists");
					Check.True(ReferenceEquals(form.PlayButton, form.AcceptButton), "Enter plays");
					Check.True(ReferenceEquals(form.ExitButton, form.CancelButton), "Escape exits");
					Check.True(ReferenceEquals(form.PlayButton, form.ActiveControl), "Play starts focused");
					Check.Sequence(new[] { "Settings", "Original launcher", "Exit", "Status", "Play" }, UiControls.Names(UiControls.TabSequence(form, 5)), "tab order");
					return true;
				});
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Count > 0, Wait), "startup announced");
				Check.Sequence(new[] { "Ready. Press Enter to play." }, fixture.Announcer.Announcements, "announcements");
			}
		}

		private static void AutomationInvokeActivatesButtons()
		{
			var launch = new FakeLaunchService(request => new LegacyLaunchResult(LegacyLaunchFailure.NoSaveProfile, request.DocumentDirectory, null, null));
			var presenter = new FakeSettingsPresenter(_ => null);
			using (var fixture = new Fixture(new LegacyGameTree("uia-invoke").WithStandard2013Files().WithDriverBytes(700000), launch, presenter))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				IntPtr play = host.Invoke(() => form.PlayButton.Handle);
				IntPtr settings = host.Invoke(() => form.SettingsButton.Handle);
				InvokeThroughAutomation(settings, "Settings");
				Check.True(host.WaitFor(() => presenter.Requests.Count == 1 && !form.Busy, Wait), "Settings activated through UI Automation");
				InvokeThroughAutomation(play, "Play");
				Check.True(host.WaitFor(() => fixture.Dialogs.Errors.Count == 1 && !form.Busy, Wait), "Play activated through UI Automation");
				Check.Equal(1, launch.Requests.Count, "one launch request");
				IntPtr create = host.Invoke(() => form.CreateLocalSaveFolderButton.Handle);
				InvokeThroughAutomation(create, "Create local save folder");
				Check.True(host.WaitFor(() => !form.CreateLocalSaveFolderButton.Visible && !form.Busy, Wait), "Create local save folder activated through UI Automation");
				Check.True(Directory.Exists(Path.Combine(fixture.Game.DocumentDirectory, "user_local")), "folder created");
			}
		}

		private static void InvokeThroughAutomation(IntPtr handle, string expectedName)
		{
			// A separate MTA thread, as screen readers and Narrator are separate clients.
			Exception failure = null;
			var thread = new System.Threading.Thread(() =>
			{
				try
				{
					var element = System.Windows.Automation.AutomationElement.FromHandle(handle);
					Check.Equal(expectedName, element.Current.Name, "automation name");
					Check.Equal(System.Windows.Automation.ControlType.Button, element.Current.ControlType, "automation control type");
					((System.Windows.Automation.InvokePattern)element.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
				}
				catch (Exception exception)
				{
					failure = exception;
				}
			});
			thread.SetApartmentState(System.Threading.ApartmentState.MTA);
			thread.Start();
			thread.Join(TimeSpan.FromSeconds(20));
			if (failure != null)
			{
				throw new TestFailure("UI Automation Invoke failed: " + failure);
			}
		}

		private static void RefusedLaunchIsReported()
		{
			var launch = new FakeLaunchService(request => new LegacyLaunchResult(LegacyLaunchFailure.NoSaveProfile, request.DocumentDirectory, null, null));
			using (var fixture = new Fixture(new LegacyGameTree("refused").WithStandard2013Files().WithDriverBytes(700000), launch))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.PlayButton.PerformClick());
				Check.True(host.WaitFor(() => fixture.Dialogs.Errors.Count == 1 && !form.Busy, Wait), "error shown");
				Check.Equal(string.Join(Environment.NewLine,
					"FINAL FANTASY VII could not start.",
					"Cause: No save folder exists in " + fixture.Game.DocumentDirectory + ".",
					"Action: Press Create local save folder, then press Play. To use your Square Enix account folder instead, sign in once with the original launcher.",
					@"Log: C:\Logs\launcher.log"), fixture.Dialogs.Errors[0], "error text");
				Check.True(host.Invoke(() => form.Visible && form.PlayButton.Enabled && ReferenceEquals(form.ActiveControl, form.CreateLocalSaveFolderButton)), "the folder button offered again");
				Check.Equal(1, launch.Requests.Count, "one launch request");
				LegacyLaunchRequest sent = launch.Requests[0];
				Check.Equal(fixture.Game.DocumentDirectory, sent.DocumentDirectory, "request documents");
				Check.Null(sent.SaveProfile, "request save folder");
				Check.Equal("ff7", sent.ChannelPrefix, "production channel prefix");
				Check.Sequence(new[] { "Ready. Press Enter to create a local save folder. Warning: No save folder was found in " + fixture.Game.DocumentDirectory + ". Create local save folder makes one on this PC, or sign in once with the original launcher. Warning: Blind Soldier accessibility was not found in this game folder.", "Starting FINAL FANTASY VII." },
					fixture.Announcer.Announcements, "announcements");
			}
		}

		private static void FirstRunOffersLocalSaveFolder()
		{
			using (var fixture = new Fixture(new LegacyGameTree("first-run").WithStandard2013Files().WithDriverBytes(700000), new FakeLaunchService(_ => throw new InvalidOperationException("not pressed"))))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() =>
				{
					Check.True(form.CreateLocalSaveFolderButton.Visible, "offered");
					Check.Equal("Create local save folder", form.CreateLocalSaveFolderButton.AccessibleName, "button name");
					Check.True(ReferenceEquals(form.CreateLocalSaveFolderButton, form.ActiveControl), "focused first");
					Check.True(ReferenceEquals(form.PlayButton, form.AcceptButton), "Enter elsewhere still plays");
					Check.Sequence(new[] { "Play", "Settings", "Original launcher", "Exit", "Status", "Create local save folder" }, UiControls.Names(UiControls.TabSequence(form, 6)), "tab order");
					return true;
				});
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Count > 0, Wait), "startup announced");
				Check.Equal("Ready. Press Enter to create a local save folder. Warning: No save folder was found in " + fixture.Game.DocumentDirectory + ". Create local save folder makes one on this PC, or sign in once with the original launcher. Warning: Blind Soldier accessibility was not found in this game folder.",
					fixture.Announcer.Announcements[0], "startup announcement");
				Check.False(Directory.Exists(fixture.Game.DocumentDirectory), "nothing created before the player asks");
			}
		}

		private static void CreatingLocalSaveFolderSelectsIt()
		{
			var launch = new FakeLaunchService(request => new LegacyLaunchResult(LegacyLaunchFailure.AlreadyRunning, "busy", null, null));
			using (var fixture = new Fixture(new LegacyGameTree("first-run-create").WithStandard2013Files().WithDriverBytes(700000), launch))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Count == 1, Wait), "startup announced");
				host.Invoke(() => form.CreateLocalSaveFolderButton.PerformClick());
				string created = "Local save folder user_local was created in " + fixture.Game.DocumentDirectory + ". Saves stay on this PC. Press Enter to play.";
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Contains(created), Wait), "creation announced: " + string.Join(" | ", fixture.Announcer.Announcements));
				string folder = Path.Combine(fixture.Game.DocumentDirectory, "user_local");
				Check.Equal(LegacySaveMetadata.CreateDefault("local").ToXml(), File.ReadAllText(Path.Combine(folder, "metadata.xml")), "default metadata");
				host.Invoke(() =>
				{
					Check.False(form.CreateLocalSaveFolderButton.Visible, "offer removed");
					Check.True(ReferenceEquals(form.PlayButton, form.ActiveControl), "Play focused");
					Check.True(form.StatusBox.Text.Contains("Save folder: user_local. Save files: 0."), "status names the folder: " + form.StatusBox.Text);
					Check.False(form.StatusBox.Text.Contains("No save folder"), "missing-folder warning gone");
					return true;
				});
				Check.Equal("user_local", fixture.Model.Preferences.ReadSaveFolder(), "remembered for the original driver");
				host.Invoke(() => form.PlayButton.PerformClick());
				Check.True(host.WaitFor(() => launch.Requests.Count == 1 && fixture.Dialogs.Errors.Count == 1 && !form.Busy, Wait), "Play used");
				Check.Equal("user_local", launch.Requests[0].SaveProfile.Name, "Play sends the new folder");
				Check.Equal(folder, launch.Requests[0].SaveProfile.FullPath, "folder path");
			}
		}

		private static void LocalSaveFolderFailureExplained()
		{
			var game = new LegacyGameTree("first-run-fail").WithStandard2013Files().WithDriverBytes(700000);
			// A file where the Square Enix folder belongs makes the folder impossible to create.
			File.WriteAllText(Path.Combine(game.DocumentsRoot, "Square Enix"), "not a folder");
			using (var fixture = new Fixture(game, new FakeLaunchService(_ => throw new InvalidOperationException("not pressed"))))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.CreateLocalSaveFolderButton.PerformClick());
				Check.True(host.WaitFor(() => fixture.Dialogs.Errors.Count == 1 && !form.Busy, Wait), "error shown");
				Check.True(fixture.Dialogs.Errors[0].StartsWith("The local save folder could not be created: ", StringComparison.Ordinal), "error text: " + fixture.Dialogs.Errors[0]);
				Check.True(host.Invoke(() => form.CreateLocalSaveFolderButton.Visible && ReferenceEquals(form.ActiveControl, form.CreateLocalSaveFolderButton)), "offered again");
				Check.Null(fixture.Model.Preferences.ReadSaveFolder(), "nothing remembered");
			}
		}

		private static void WindowHidesWhileGameRuns()
		{
			var process = new ControllableProcess();
			var launch = new FakeLaunchService(_ => new LegacyLaunchResult(LegacyLaunchFailure.None, string.Empty, process, null));
			using (var fixture = new Fixture(WithProfile(FfnxGame("running", blindSoldier: true)), launch))
			{
				fixture.Model.EarlyExitWindow = TimeSpan.Zero;
				using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
				{
					var form = (MainForm)host.Form;
					host.Invoke(() => form.PlayButton.PerformClick());
					Check.True(host.WaitFor(() => !form.Visible, Wait), "hidden while the game runs");
					Check.Sequence(new[] { "Ready. Press Enter to play.", "Starting FINAL FANTASY VII.", "FINAL FANTASY VII has started." }, fixture.Announcer.Announcements, "announcements");
					process.Exit(0);
					Check.True(host.WaitForClose(Wait), "closed after the game exited");
					Check.True(process.Disposed, "game handle released");
					Check.False(process.Killed, "a running game is never stopped by the launcher");
					Check.Equal(0, fixture.Dialogs.Errors.Count, "no error");
				}
			}
		}

		private const string Stopping = "Stopping the FINAL FANTASY VII start-up. The launcher closes when it has stopped.";

		private static void ClosingDuringStartStopsIt()
		{
			var process = new ControllableProcess();
			// The real launch service stops its own game when cancelled; this one
			// reports that, but only after the test releases it.
			using (var launch = new GatedLaunchService(request => request.Cancellation.IsCancellationRequested
				? new LegacyLaunchResult(LegacyLaunchFailure.Cancelled, string.Empty, process, null)
				: new LegacyLaunchResult(LegacyLaunchFailure.None, string.Empty, process, null)))
			using (var fixture = new Fixture(WithProfile(FfnxGame("close-start", blindSoldier: true)), launch))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.PlayButton.PerformClick());
				Check.True(launch.WaitUntilEntered(Wait), "the start began");
				Check.False(launch.Request.Cancellation.IsCancellationRequested, "not cancelled before Exit");
				host.Invoke(() => form.ExitButton.PerformClick());
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Contains(Stopping), Wait), "stopping announced: " + string.Join(" | ", fixture.Announcer.Announcements));
				Check.True(launch.Request.Cancellation.IsCancellationRequested, "the start was asked to stop");
				Check.False(host.WaitForClose(TimeSpan.FromMilliseconds(500)), "the window stays until the start has stopped");
				host.Invoke(() => form.ExitButton.PerformClick());
				Check.Equal(1, fixture.Announcer.Announcements.Count(entry => entry == Stopping), "pressing Exit again does not repeat the announcement");
				launch.Release();
				Check.True(host.WaitForClose(Wait), "closed once the start stopped");
				Check.True(process.Disposed, "game handle released");
				Check.Equal(0, fixture.Dialogs.Errors.Count, "a requested stop is not an error");
				Check.False(fixture.Announcer.Announcements.Contains("FINAL FANTASY VII has started."), "no started announcement");
			}
		}

		private static void ClosingAsStartFinishesStopsGame()
		{
			var process = new ControllableProcess();
			// The start completes just before it notices the request to stop.
			using (var launch = new GatedLaunchService(request => new LegacyLaunchResult(LegacyLaunchFailure.None, string.Empty, process, null)))
			using (var fixture = new Fixture(WithProfile(FfnxGame("close-finish", blindSoldier: true)), launch))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.PlayButton.PerformClick());
				Check.True(launch.WaitUntilEntered(Wait), "the start began");
				host.Invoke(() => form.ExitButton.PerformClick());
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Contains(Stopping), Wait), "stopping announced");
				launch.Release();
				Check.True(host.WaitForClose(Wait), "closed");
				Check.True(process.Killed, "the game this launch started was stopped");
				Check.True(process.Disposed, "game handle released");
				Check.False(fixture.Announcer.Announcements.Contains("FINAL FANTASY VII has started."), "no started announcement");
			}
		}

		private static void ExitClosesWhenIdle()
		{
			using (var fixture = new Fixture(WithProfile(FfnxGame("exit-idle", blindSoldier: true)), new FakeLaunchService(_ => throw new InvalidOperationException("not pressed"))))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.ExitButton.PerformClick());
				Check.True(host.WaitForClose(Wait), "closed at once");
				Check.False(fixture.Announcer.Announcements.Contains(Stopping), "nothing to stop");
			}
		}

		private static void EarlyFailingExitReturns()
		{
			var process = new ControllableProcess();
			var launch = new FakeLaunchService(_ => new LegacyLaunchResult(LegacyLaunchFailure.None, string.Empty, process, null));
			using (var fixture = new Fixture(FfnxGame("early", blindSoldier: true), launch))
			{
				var clock = new Queue<DateTime>(new[] { new DateTime(2026, 11, 25, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 25, 1, 0, 3, DateTimeKind.Utc) });
				fixture.Model.UtcNow = () => clock.Count > 1 ? clock.Dequeue() : clock.Peek();
				using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
				{
					var form = (MainForm)host.Form;
					host.Invoke(() => form.PlayButton.PerformClick());
					Check.True(host.WaitFor(() => !form.Visible, Wait), "hidden while the game runs");
					process.Exit(5);
					Check.True(host.WaitFor(() => form.Visible && fixture.Dialogs.Errors.Count == 1 && !form.Busy, Wait), "window returned");
					Check.Equal(string.Join(Environment.NewLine,
						"FINAL FANTASY VII could not start.",
						"Cause: FINAL FANTASY VII closed after 3 seconds with exit code 5.",
						"Action: Check the log, then try again.",
						@"Log: C:\Logs\launcher.log"), fixture.Dialogs.Errors[0], "error text");
				}
			}
		}

		private static void OriginalButtonExplainsMissingBackup()
		{
			using (var fixture = new Fixture(FfnxGame("nooriginal", blindSoldier: true), new FakeLaunchService(_ => null)))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.OriginalButton.PerformClick());
				Check.True(host.WaitFor(() => fixture.Dialogs.Information.Count == 1, Wait), "explained");
				Check.Equal("The original launcher, FF7_Launcher.original.exe, is not in the game folder. Steam's Verify integrity of game files restores the original launcher and removes this accessible one.",
					fixture.Dialogs.Information[0], "explanation");
				Check.Equal(0, fixture.Original.Starts.Count, "nothing started");
				Check.True(host.Invoke(() => form.Visible), "window stays");
			}
		}

		private static void OriginalButtonStartsBackup()
		{
			var game = FfnxGame("original", blindSoldier: true);
			string backup = game.WriteFile("FF7_Launcher.original.exe", 100);
			using (var fixture = new Fixture(game, new FakeLaunchService(_ => null)))
			{
				fixture.Model.EarlyExitWindow = TimeSpan.Zero;
				using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
				{
					var form = (MainForm)host.Form;
					host.Invoke(() => form.OriginalButton.PerformClick());
					Check.True(host.WaitFor(() => !form.Visible, Wait), "hidden while the original launcher runs");
					Check.Sequence(new[] { backup + "|" + game.Root }, fixture.Original.Starts, "original started");
					Check.True(fixture.Announcer.Announcements.Contains("Starting the original Square Enix launcher. Its sign-in window may not work with screen readers."), "announced");
					fixture.Original.Process.Exit(0);
					Check.True(host.WaitForClose(Wait), "closed after the original launcher exited");
				}
			}
		}

		private static void SavedSettingsAreWritten()
		{
			var game = new LegacyGameTree("settings-save").WithStandard2013Files().WithDriverBytes(700000);
			game.CreateProfile("user_1");
			game.CreateProfile("user_2", "save00.ff7");
			var presenter = new FakeSettingsPresenter(request => new SettingsEditResult
			{
				Display = new LegacyDisplaySettings { Width = 1920, Height = 1080, RefreshRate = 60, Fullscreen = true },
				Sound = new LegacySoundSettings { SoundEffectsVolume = 30, MusicVolume = 40 },
				SelectedProfile = request.Profiles[1]
			});
			using (var fixture = new Fixture(game, new FakeLaunchService(_ => null), presenter))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.SettingsButton.PerformClick());
				Check.True(host.WaitFor(() => fixture.Announcer.Announcements.Contains("Settings saved."), Wait), "saved");
				Check.Equal(1, presenter.Requests.Count, "dialog opened once");
				Check.Equal(LegacySettingsFileState.Missing, presenter.Requests[0].DisplayState, "display state offered");
				Check.Equal("user_1", presenter.Requests[0].SelectedProfile.Name, "current folder offered");
				Check.Bytes(new byte[]
				{
					0, 0, 0x07, 0x80, 0, 0, 0x04, 0x38, 0, 0, 0, 0x3C, 0, 0, 0, 1,
					0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
				}, File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7video.cfg")), "display written");
				Check.Bytes(new byte[] { 30, 0, 0, 0, 40, 0, 0, 0 }, File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7sound.cfg")), "sound written");
				Check.Equal("user_2", fixture.Model.Preferences.ReadSaveFolder(), "folder remembered");
				Check.True(host.Invoke(() => form.StatusBox.Text.Contains("Save folder: user_2. Save files: 1.")), "status refreshed");
				Check.True(host.Invoke(() => ReferenceEquals(form.ActiveControl, form.SettingsButton)), "focus returns to Settings");
			}
		}

		private static void CancelledSettingsWriteNothing()
		{
			var game = new LegacyGameTree("settings-cancel").WithStandard2013Files().WithDriverBytes(700000);
			var presenter = new FakeSettingsPresenter(_ => null);
			using (var fixture = new Fixture(game, new FakeLaunchService(_ => null), presenter))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var form = (MainForm)host.Form;
				host.Invoke(() => form.SettingsButton.PerformClick());
				Check.True(host.WaitFor(() => presenter.Requests.Count == 1, Wait), "dialog opened");
				Check.True(host.WaitFor(() => !form.Busy, Wait), "settings closed");
				Check.False(Directory.Exists(game.DocumentDirectory), "documents untouched");
				Check.False(File.Exists(fixture.Model.Preferences.Path), "no preference written");
			}
		}

		private static SettingsEditRequest OriginalRequest(LegacyGameTree game, LegacyDisplaySettings display, LegacySettingsFileState state)
		{
			game.CreateProfile("user_1");
			game.CreateProfile("user_2", "save00.ff7", "save01.ff7");
			var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
			return new SettingsEditRequest
			{
				Installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true),
				Display = display,
				DisplayState = state,
				Sound = new LegacySoundSettings { SoundEffectsVolume = 80, MusicVolume = 60 },
				Modes = new[]
				{
					new DisplayMode(1280, 720, new uint[] { 60 }),
					new DisplayMode(1920, 1080, new uint[] { 60, 144 })
				},
				Profiles = profiles,
				SelectedProfile = profiles[1]
			};
		}

		private static void SettingsDialogLabelsControls()
		{
			using (var game = new LegacyGameTree("dialog").WithStandard2013Files().WithDriverBytes(700000))
			{
				var request = OriginalRequest(game, new LegacyDisplaySettings { Width = 1920, Height = 1080, RefreshRate = 60, KeepAspectRatio = true, Reserved = 9 }, LegacySettingsFileState.Valid);
				using (var host = UiHost.Show(() => new QuietSettingsForm(English, request)))
				{
					var form = (SettingsForm)host.Form;
					host.Invoke(() =>
					{
						Check.Equal("Settings", form.Text, "title");
						Check.Sequence(new[]
						{
							"Full screen", "Resolution", "Refresh rate", "Keep the original 4:3 aspect ratio", "Linear filtering",
							"Use the original game resolution", "Sound effects volume", "Music volume", "Save folder", "Notes"
						}, new Control[]
						{
							form.FullscreenBox, form.ResolutionBox, form.RefreshBox, form.KeepAspectBox, form.LinearFilteringBox,
							form.OriginalModeBox, form.EffectsSlider, form.MusicSlider, form.SaveFolderBox, form.NotesBox
						}.Select(control => control.AccessibleName), "control names");
						Check.Sequence(new[] { "Default: 640 by 480 in a window, desktop size in full screen", "1280 by 720", "1920 by 1080" },
							form.ResolutionBox.Items.Cast<object>().Select(item => item.ToString()), "resolutions");
						Check.Equal("1920 by 1080", form.ResolutionBox.SelectedItem.ToString(), "selected resolution");
						Check.Sequence(new[] { "Default: 60 hertz in a window, desktop rate in full screen", "60 hertz", "144 hertz" },
							form.RefreshBox.Items.Cast<object>().Select(item => item.ToString()), "refresh rates");
						Check.Equal("60 hertz", form.RefreshBox.SelectedItem.ToString(), "selected refresh rate");
						Check.False(form.FullscreenBox.Checked, "full screen");
						Check.True(form.KeepAspectBox.Checked, "aspect ratio");
						Check.Equal(80, form.EffectsSlider.Value, "effects volume");
						Check.Equal(60, form.MusicSlider.Value, "music volume");
						Check.Equal(0, form.EffectsSlider.Minimum, "slider minimum");
						Check.Equal(100, form.EffectsSlider.Maximum, "slider maximum");
						Check.Sequence(new[] { "user_1, save files: 0", "user_2, save files: 2" }, form.SaveFolderBox.Items.Cast<object>().Select(item => item.ToString()), "save folders");
						Check.Equal("user_2, save files: 2", form.SaveFolderBox.SelectedItem.ToString(), "selected save folder");
						Check.True(form.NotesBox.ReadOnly && form.NotesBox.TabStop, "notes readable");
						Check.Equal("Controls are changed in the game's own Config menu. This launcher leaves ff7input.cfg unchanged.", form.NotesBox.Text, "notes");
						Check.True(ReferenceEquals(form.OkButton, form.AcceptButton), "Enter saves");
						Check.True(ReferenceEquals(form.CancelButtonControl, form.CancelButton), "Escape cancels");
						Check.Equal("OK", form.OkButton.AccessibleName, "OK name");
						Check.Equal("Cancel", form.CancelButtonControl.AccessibleName, "Cancel name");
						return true;
					});
				}
			}
		}

		private static void LayoutContainersHaveNoBorrowedNames()
		{
			using (var fixture = new Fixture(WithProfile(FfnxGame("container-names", blindSoldier: true)), new FakeLaunchService(_ => null)))
			using (var host = UiHost.Show(() => new QuietMainForm(fixture.Model)))
			{
				var names = host.Invoke(() => ContainerNames(host.Form));
				Check.True(names.Count >= 2, "main window containers found");
				Check.Sequence(names.Select(_ => string.Empty), names, "main window container names");
			}
			using (var game = new LegacyGameTree("container-settings").WithStandard2013Files().WithDriverBytes(700000))
			{
				var request = OriginalRequest(game, LegacyDisplaySettings.CreateDefault(), LegacySettingsFileState.Valid);
				using (var host = UiHost.Show(() => new QuietSettingsForm(English, request)))
				{
					var names = host.Invoke(() => ContainerNames(host.Form));
					Check.True(names.Count >= 5, "settings containers found");
					Check.Sequence(names.Select(_ => string.Empty), names, "settings container names");
				}
			}
		}

		private static List<string> ContainerNames(Control root)
		{
			var names = new List<string>();
			foreach (Control control in root.Controls)
			{
				if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
				{
					names.Add(control.AccessibilityObject.Name ?? string.Empty);
				}
				names.AddRange(ContainerNames(control));
			}
			return names;
		}

		private static void SettingsDialogBuildsResult()
		{
			using (var game = new LegacyGameTree("dialog-result").WithStandard2013Files().WithDriverBytes(700000))
			{
				var request = OriginalRequest(game, new LegacyDisplaySettings { Width = 1920, Height = 1080, RefreshRate = 144, Reserved = 9, LinearFiltering = true }, LegacySettingsFileState.Valid);
				using (var host = UiHost.Show(() => new QuietSettingsForm(English, request)))
				{
					var form = (SettingsForm)host.Form;
					SettingsEditResult result = host.Invoke(() =>
					{
						form.FullscreenBox.Checked = true;
						form.ResolutionBox.SelectedIndex = 1;
						form.RefreshBox.SelectedIndex = 0;
						form.OriginalModeBox.Checked = true;
						form.EffectsSlider.Value = 30;
						form.MusicSlider.Value = 45;
						form.SaveFolderBox.SelectedIndex = 0;
						return form.BuildResult();
					});
					Check.Bytes(new byte[]
					{
						0, 0, 0x05, 0x00, 0, 0, 0x02, 0xD0, 0, 0, 0, 0, 0, 0, 0, 1,
						0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1
					}, result.Display.Encode(), "display result");
					Check.Bytes(new byte[] { 30, 0, 0, 0, 45, 0, 0, 0 }, result.Sound.Encode(), "sound result");
					Check.Equal("user_1", result.SelectedProfile.Name, "save folder result");
				}
			}
		}

		private static void FfnxSettingsExplainDisplay()
		{
			using (var game = FfnxGame("dialog-ffnx", blindSoldier: false))
			{
				game.CreateProfile("user_1");
				game.CreateProfile("user_2");
				var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
				var request = new SettingsEditRequest
				{
					Installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true),
					Display = LegacyDisplaySettings.CreateDefault(),
					DisplayState = LegacySettingsFileState.Missing,
					Sound = new LegacySoundSettings { SoundEffectsVolume = 50, MusicVolume = 70 },
					Profiles = profiles,
					SelectedProfile = profiles[0]
				};
				using (var host = UiHost.Show(() => new QuietSettingsForm(English, request)))
				{
					var form = (SettingsForm)host.Form;
					SettingsEditResult result = host.Invoke(() =>
					{
						Check.Null(form.FullscreenBox, "no full screen control");
						Check.Null(form.ResolutionBox, "no resolution control");
						Check.Null(form.SaveFolderBox, "FFNx chooses its own save folder");
						Check.Equal(string.Join(Environment.NewLine,
							"FFNx manages resolution and full screen in FFNx.toml, so display settings are not shown here.",
							"Controls are changed in the game's own Config menu. This launcher leaves ff7input.cfg unchanged."), form.NotesBox.Text, "notes");
						form.MusicSlider.Value = 20;
						return form.BuildResult();
					});
					Check.Null(result.Display, "display left unchanged");
					Check.Bytes(new byte[] { 50, 0, 0, 0, 20, 0, 0, 0 }, result.Sound.Encode(), "sound result");
					Check.Equal("user_1", result.SelectedProfile.Name, "save folder unchanged");
				}
			}
		}

		private static void UnlistedResolutionAndUnreadableNote()
		{
			using (var game = new LegacyGameTree("dialog-unlisted").WithStandard2013Files().WithDriverBytes(700000))
			{
				var request = OriginalRequest(game, new LegacyDisplaySettings { Width = 1600, Height = 900, RefreshRate = 75 }, LegacySettingsFileState.Unreadable);
				using (var host = UiHost.Show(() => new QuietSettingsForm(English, request)))
				{
					var form = (SettingsForm)host.Form;
					host.Invoke(() =>
					{
						Check.Sequence(new[] { "Default: 640 by 480 in a window, desktop size in full screen", "1280 by 720", "1600 by 900", "1920 by 1080" },
							form.ResolutionBox.Items.Cast<object>().Select(item => item.ToString()), "resolutions keep the saved size");
						Check.Equal("1600 by 900", form.ResolutionBox.SelectedItem.ToString(), "saved size selected");
						Check.Sequence(new[] { "Default: 60 hertz in a window, desktop rate in full screen", "75 hertz" },
							form.RefreshBox.Items.Cast<object>().Select(item => item.ToString()), "refresh keeps the saved rate");
						Check.True(form.NotesBox.Text.StartsWith("The existing ff7video.cfg has an unexpected format.", StringComparison.Ordinal), "unreadable note first");
						return true;
					});
				}
			}
		}
	}
}
