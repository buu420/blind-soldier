using System;
using System.IO;
using System.Linq;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class CoreTests
	{
		internal static void Register(TestRunner runner)
		{
			runner.Add("detects the English 2013 layout with the original driver", DetectsEnglishLayoutWithOriginalDriver);
			runner.Add("rejects the Steam 2026 layout", RejectsSteam2026Layout);
			runner.Add("rejects a folder without 2013 game data", RejectsFolderWithoutData);
			runner.Add("rejects a Japanese eStore-only folder", RejectsJapaneseOnlyFolder);
			runner.Add("Steam language selects among several executables", SteamLanguageSelectsExecutable);
			runner.Add("a manifest for another install folder is ignored", ForeignManifestIsIgnored);
			runner.Add("English is preferred without a Steam language", EnglishPreferredWithoutSteamLanguage);
			runner.Add("the first executable is used when English is absent", FirstExecutableWithoutEnglish);
			runner.Add("only ff7_en.exe is reported as supported by Blind Soldier", OnlyEnglishExecutableIsSupported);
			runner.Add("driver classification uses identity before size", DriverClassificationTable);
			runner.Add("FFNx is identified from its version resource", FfnxIdentifiedFromVersionResource);
			runner.Add("a missing AF3DN.P is reported", MissingDriverIsReported);
			runner.Add("Blind Soldier presence requires the complete portable root", BlindSoldierPresenceRequiresCompleteRoot);
			runner.Add("the original launcher backup is found beside the game", OriginalLauncherBackupIsFound);
			runner.Add("save profiles are listed in order with save counts", SaveProfilesAreListed);
			runner.Add("a missing documents folder has no save profiles", MissingDocumentsFolderHasNoProfiles);
			runner.Add("display settings decode big-endian fields", DisplaySettingsDecode);
			runner.Add("display settings encode big-endian fields and keep the reserved value", DisplaySettingsEncode);
			runner.Add("display settings of the wrong length are rejected", DisplaySettingsRejectWrongLength);
			runner.Add("default display settings select a 640 by 480 window", DefaultDisplaySettings);
			runner.Add("sound settings round-trip little-endian volumes", SoundSettingsRoundTrip);
			runner.Add("sound settings of the wrong length are rejected", SoundSettingsRejectWrongLength);
			runner.Add("settings store reports missing, valid and unreadable files", SettingsStoreStates);
			runner.Add("settings store keeps one backup of the original file", SettingsStoreKeepsOneBackup);
			runner.Add("a Steam launch is recognized from its environment or steam.exe parent", SteamLaunchIsRecognized);
		}

		private static void DetectsEnglishLayoutWithOriginalDriver()
		{
			using (var game = new LegacyGameTree("detect").WithStandard2013Files().WithDriverBytes(900000))
			{
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal(LegacyLayoutProblem.None, installation.Problem, "problem");
				Check.Equal("ff7_en.exe", installation.Selected.FileName, "selected executable");
				Check.Equal(Path.Combine(game.Root, "ff7_en.exe"), installation.Selected.FullPath, "selected path");
				Check.Equal("en", installation.Selected.LanguageCode, "language");
				Check.Equal(LegacyLanguageSource.OnlyExecutable, installation.LanguageSource, "language source");
				Check.Equal(LegacyDriverKind.Original, installation.Driver.Kind, "driver");
				Check.False(installation.BlindSoldierInstalled, "Blind Soldier installed");
				Check.True(installation.BlindSoldierSupportsExecutable, "Blind Soldier executable support");
				Check.Null(installation.OriginalLauncherPath, "original launcher");
				Check.True(installation.LaunchedBySteam, "launched by Steam");
			}
		}

		private static void RejectsSteam2026Layout()
		{
			using (var game = new LegacyGameTree("2026"))
			{
				game.WriteFile("FFVII.exe", 100);
				game.WriteFile("FFVII_LAUNCHER.exe", 100);
				game.WriteFile("FF7_Launcher.exe", 100);
				game.WriteFile("ff7_en.exe", 100);
				Directory.CreateDirectory(Path.Combine(game.Root, "data"));
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal(LegacyLayoutProblem.Steam2026Folder, installation.Problem, "problem");
				Check.Null(installation.Selected, "selected executable");
			}
		}

		private static void RejectsFolderWithoutData()
		{
			using (var game = new LegacyGameTree("nodata"))
			{
				game.WriteFile("FF7_Launcher.exe", 100);
				game.WriteFile("ff7_en.exe", 100);
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal(LegacyLayoutProblem.NotLegacyGameFolder, installation.Problem, "problem");
			}
		}

		private static void RejectsJapaneseOnlyFolder()
		{
			using (var game = new LegacyGameTree("ja").WithStandard2013Files("ff7_ja.exe").WithDriverBytes(1000))
			{
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal(LegacyLayoutProblem.NoSupportedExecutable, installation.Problem, "problem");
				Check.Null(installation.Selected, "selected executable");
			}
		}

		private static void SteamLanguageSelectsExecutable()
		{
			using (var game = new LegacyGameTree("steamlang").WithStandard2013Files("ff7_en.exe", "ff7_fr.exe").WithDriverBytes(1000))
			{
				game.WithManifest("FINAL FANTASY VII", "french");
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal("ff7_fr.exe", installation.Selected.FileName, "selected executable");
				Check.Equal("fr", installation.Selected.LanguageCode, "language");
				Check.Equal(LegacyLanguageSource.SteamManifest, installation.LanguageSource, "language source");
				Check.Sequence(new[] { "ff7_en.exe", "ff7_fr.exe" }, installation.Executables.Select(item => item.FileName), "executables");
			}
		}

		private static void ForeignManifestIsIgnored()
		{
			using (var game = new LegacyGameTree("foreign").WithStandard2013Files("ff7_en.exe", "ff7_de.exe").WithDriverBytes(1000))
			{
				game.WithManifest("Some Other Folder", "german");
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal("ff7_en.exe", installation.Selected.FileName, "selected executable");
				Check.Equal(LegacyLanguageSource.EnglishPreference, installation.LanguageSource, "language source");
			}
		}

		private static void EnglishPreferredWithoutSteamLanguage()
		{
			using (var game = new LegacyGameTree("enpref").WithStandard2013Files("ff7_de.exe", "ff7_en.exe").WithDriverBytes(1000))
			{
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal("ff7_en.exe", installation.Selected.FileName, "selected executable");
				Check.Equal(LegacyLanguageSource.EnglishPreference, installation.LanguageSource, "language source");
			}
		}

		private static void FirstExecutableWithoutEnglish()
		{
			using (var game = new LegacyGameTree("first").WithStandard2013Files("ff7_fr.exe", "ff7_es.exe").WithDriverBytes(1000))
			{
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal("ff7_es.exe", installation.Selected.FileName, "selected executable");
				Check.Equal("es", installation.Selected.LanguageCode, "language");
				Check.Equal(LegacyLanguageSource.FirstExecutable, installation.LanguageSource, "language source");
			}
		}

		private static void OnlyEnglishExecutableIsSupported()
		{
			foreach (string executable in new[] { "ff7_de.exe", "ff7_es.exe", "ff7_fr.exe" })
			{
				using (var game = new LegacyGameTree("bslang").WithStandard2013Files(executable).WithDriverBytes(1000))
				{
					LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, false);
					Check.Equal(executable, installation.Selected.FileName, "selected executable");
					Check.False(installation.BlindSoldierSupportsExecutable, executable + " Blind Soldier support");
					Check.False(installation.LaunchedBySteam, "launched by Steam");
				}
			}
		}

		private static void DriverClassificationTable()
		{
			LegacyDriverInfo ffnx = LegacyDriverInfo.Classify(42497536, "FFNx", "FFNx", "1.25.2.0");
			Check.Equal(LegacyDriverKind.Ffnx, ffnx.Kind, "FFNx by product");
			Check.Equal("1.25.2.0", ffnx.Version, "FFNx version");
			Check.Equal(LegacyDriverKind.Ffnx, LegacyDriverInfo.Classify(600000, "", "ffnx", "").Kind, "FFNx by internal name");
			Check.Equal(LegacyDriverKind.Original, LegacyDriverInfo.Classify(700000, "", "", "").Kind, "small driver");
			Check.Equal(LegacyDriverKind.Original, LegacyDriverInfo.Classify(1048576, "", "", "").Kind, "one mebibyte boundary");
			Check.Equal(LegacyDriverKind.Unrecognized, LegacyDriverInfo.Classify(1048577, "", "", "").Kind, "above one mebibyte");
			Check.Equal(LegacyDriverKind.Unrecognized, LegacyDriverInfo.Classify(2000000, "Other", "Other", "1.0").Kind, "large unknown driver");
			Check.Equal(string.Empty, LegacyDriverInfo.Classify(700000, "", "", "9.9").Version, "original driver has no FFNx version");
		}

		private static void FfnxIdentifiedFromVersionResource()
		{
			using (var game = new LegacyGameTree("ffnx").WithStandard2013Files())
			{
				AssemblyFactory.CompileLibraryWithProduct(Path.Combine(game.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
				LegacyDriverInfo driver = LegacyDriverInfo.Inspect(Path.Combine(game.Root, "AF3DN.P"));
				Check.True(driver.Length < 1048576, "compiled probe is small");
				Check.Equal(LegacyDriverKind.Ffnx, driver.Kind, "driver kind");
				Check.Equal("1.25.2.0", driver.Version, "driver version");
				Check.Equal(LegacyDriverKind.Ffnx, LegacyInstallation.Detect(game.Root, game.ManifestPath, true).Driver.Kind, "installation driver");
			}
		}

		private static void MissingDriverIsReported()
		{
			using (var game = new LegacyGameTree("nodriver").WithStandard2013Files())
			{
				LegacyInstallation installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				Check.Equal(LegacyLayoutProblem.None, installation.Problem, "problem");
				Check.Equal(LegacyDriverKind.Missing, installation.Driver.Kind, "driver");
			}
		}

		private static void BlindSoldierPresenceRequiresCompleteRoot()
		{
			string[] required =
			{
				"version.dll",
				@"Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe",
				@"Blind-Soldier\Runtime\dotnet\x86\host\fxr\9.0.8\hostfxr.dll",
				@"Reloaded-II\portable.txt",
				@"Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll",
				@"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll"
			};
			using (var game = new LegacyGameTree("bs").WithStandard2013Files().WithDriverBytes(1000))
			{
				foreach (string file in required)
				{
					game.WriteFile(file, 64);
				}
				Check.True(LegacyInstallation.Detect(game.Root, game.ManifestPath, true).BlindSoldierInstalled, "complete root");
				foreach (string missing in required)
				{
					string path = Path.Combine(game.Root, missing);
					File.Delete(path);
					Check.False(LegacyInstallation.Detect(game.Root, game.ManifestPath, true).BlindSoldierInstalled, "without " + missing);
					game.WriteFile(missing, 64);
				}
			}
		}

		private static void OriginalLauncherBackupIsFound()
		{
			using (var game = new LegacyGameTree("original").WithStandard2013Files().WithDriverBytes(1000))
			{
				string backup = game.WriteFile("FF7_Launcher.original.exe", 512);
				Check.Equal(backup, LegacyInstallation.Detect(game.Root, game.ManifestPath, true).OriginalLauncherPath, "original launcher");
			}
		}

		private static void SaveProfilesAreListed()
		{
			using (var game = new LegacyGameTree("profiles"))
			{
				Check.Equal(Path.Combine(@"C:\Docs", "Square Enix", "FINAL FANTASY VII Steam"), LegacySaveProfiles.DocumentDirectory(@"C:\Docs"), "document directory");
				game.CreateProfile("user_200", "save00.ff7", "save03.ff7", "metadata.xml", "save3.ff7", "save10x.ff7");
				game.CreateProfile("user_1");
				Directory.CreateDirectory(Path.Combine(game.DocumentDirectory, "other"));
				File.WriteAllText(Path.Combine(game.DocumentDirectory, "user_9.txt"), "not a folder");
				var profiles = LegacySaveProfiles.Find(game.DocumentDirectory);
				Check.Sequence(new[] { "user_1:0", "user_200:2" }, profiles.Select(item => item.Name + ":" + item.SaveFileCount), "profiles");
				Check.Equal(Path.Combine(game.DocumentDirectory, "user_200"), profiles[1].FullPath, "profile path");
			}
		}

		private static void MissingDocumentsFolderHasNoProfiles()
		{
			using (var game = new LegacyGameTree("nodocs"))
			{
				Check.Equal(0, LegacySaveProfiles.Find(game.DocumentDirectory).Count, "profiles");
			}
		}

		private static void DisplaySettingsDecode()
		{
			byte[] bytes =
			{
				0x00, 0x00, 0x07, 0x80,
				0x00, 0x00, 0x04, 0x38,
				0x00, 0x00, 0x00, 0x3C,
				0x00, 0x00, 0x00, 0x01,
				0x00, 0x00, 0x00, 0x07,
				0x00, 0x00, 0x00, 0x01,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x01
			};
			Check.True(LegacyDisplaySettings.TryDecode(bytes, out LegacyDisplaySettings settings), "decoded");
			Check.Equal(1920u, settings.Width, "width");
			Check.Equal(1080u, settings.Height, "height");
			Check.Equal(60u, settings.RefreshRate, "refresh rate");
			Check.True(settings.Fullscreen, "full screen");
			Check.Equal(7u, settings.Reserved, "reserved");
			Check.True(settings.KeepAspectRatio, "keep aspect ratio");
			Check.False(settings.LinearFiltering, "linear filtering");
			Check.True(settings.OriginalMode, "original mode");
		}

		private static void DisplaySettingsEncode()
		{
			var settings = new LegacyDisplaySettings
			{
				Width = 1280,
				Height = 720,
				RefreshRate = 0,
				Fullscreen = false,
				Reserved = 5,
				KeepAspectRatio = false,
				LinearFiltering = true,
				OriginalMode = false
			};
			Check.Bytes(new byte[]
			{
				0x00, 0x00, 0x05, 0x00,
				0x00, 0x00, 0x02, 0xD0,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x05,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x01,
				0x00, 0x00, 0x00, 0x00
			}, settings.Encode(), "encoded display settings");
		}

		private static void DisplaySettingsRejectWrongLength()
		{
			Check.False(LegacyDisplaySettings.TryDecode(new byte[31], out _), "31 bytes");
			Check.False(LegacyDisplaySettings.TryDecode(new byte[33], out _), "33 bytes");
			Check.False(LegacyDisplaySettings.TryDecode(null, out _), "null");
		}

		private static void DefaultDisplaySettings()
		{
			Check.Bytes(new byte[32], LegacyDisplaySettings.CreateDefault().Encode(), "default display settings");
		}

		private static void SoundSettingsRoundTrip()
		{
			byte[] bytes = { 0x64, 0x00, 0x00, 0x00, 0x32, 0x00, 0x00, 0x00 };
			Check.True(LegacySoundSettings.TryDecode(bytes, out LegacySoundSettings settings), "decoded");
			Check.Equal(100u, settings.SoundEffectsVolume, "sound effects volume");
			Check.Equal(50u, settings.MusicVolume, "music volume");
			settings.MusicVolume = 75;
			Check.Bytes(new byte[] { 0x64, 0x00, 0x00, 0x00, 0x4B, 0x00, 0x00, 0x00 }, settings.Encode(), "encoded sound settings");
			Check.Bytes(new byte[] { 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00 }, LegacySoundSettings.CreateDefault().Encode(), "default sound settings");
		}

		private static void SoundSettingsRejectWrongLength()
		{
			Check.False(LegacySoundSettings.TryDecode(new byte[7], out _), "7 bytes");
			Check.False(LegacySoundSettings.TryDecode(new byte[9], out _), "9 bytes");
			Check.False(LegacySoundSettings.TryDecode(new byte[] { 0x65, 0, 0, 0, 0x10, 0, 0, 0 }, out _), "volume above 100");
		}

		private static void SettingsStoreStates()
		{
			using (var game = new LegacyGameTree("store"))
			{
				var store = new LegacySettingsStore(game.DocumentDirectory);
				Check.Equal(LegacySettingsFileState.Missing, store.ReadDisplay(out LegacyDisplaySettings display), "missing display");
				Check.Bytes(new byte[32], display.Encode(), "missing display defaults");
				Check.Equal(LegacySettingsFileState.Missing, store.ReadSound(out LegacySoundSettings sound), "missing sound");
				Check.Equal(100u, sound.MusicVolume, "missing sound defaults");

				store.WriteSound(new LegacySoundSettings { SoundEffectsVolume = 40, MusicVolume = 30 });
				Check.Bytes(new byte[] { 40, 0, 0, 0, 30, 0, 0, 0 }, File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7sound.cfg")), "written sound file");
				Check.Equal(LegacySettingsFileState.Valid, store.ReadSound(out sound), "valid sound");
				Check.Equal(40u, sound.SoundEffectsVolume, "read sound effects");

				File.WriteAllBytes(Path.Combine(game.DocumentDirectory, "ff7video.cfg"), new byte[] { 1, 2, 3 });
				Check.Equal(LegacySettingsFileState.Unreadable, store.ReadDisplay(out display), "unreadable display");
				Check.Bytes(new byte[32], display.Encode(), "unreadable display defaults");
			}
		}

		private static void SteamLaunchIsRecognized()
		{
			Func<string, string> none = _ => null;
			Check.True(SteamLaunchDetector.IsLaunchedBySteam(name => name == "SteamAppId" ? "39140" : null, null), "SteamAppId");
			Check.True(SteamLaunchDetector.IsLaunchedBySteam(name => name == "SteamGameId" ? "39140" : null, "explorer.exe"), "SteamGameId");
			Check.True(SteamLaunchDetector.IsLaunchedBySteam(none, "Steam.exe"), "steam.exe parent");
			Check.False(SteamLaunchDetector.IsLaunchedBySteam(none, "explorer.exe"), "Explorer parent");
			Check.False(SteamLaunchDetector.IsLaunchedBySteam(name => name == "SteamAppId" ? " " : null, null), "blank variable");
			Check.False(SteamLaunchDetector.IsLaunchedBySteam(none, "steamwebhelper.exe"), "other Steam process");
		}

		private static void SettingsStoreKeepsOneBackup()
		{
			using (var game = new LegacyGameTree("backup"))
			{
				Directory.CreateDirectory(game.DocumentDirectory);
				string display = Path.Combine(game.DocumentDirectory, "ff7video.cfg");
				byte[] original = new byte[32];
				original[3] = 0x80;
				original[2] = 0x02;
				File.WriteAllBytes(display, original);
				var store = new LegacySettingsStore(game.DocumentDirectory);
				var first = LegacyDisplaySettings.CreateDefault();
				first.Fullscreen = true;
				store.WriteDisplay(first);
				store.WriteDisplay(LegacyDisplaySettings.CreateDefault());
				Check.Bytes(original, File.ReadAllBytes(display + ".blind-soldier-original"), "backup keeps the first original");
				Check.Bytes(new byte[32], File.ReadAllBytes(display), "latest settings written");
				Check.Equal(2, Directory.GetFiles(game.DocumentDirectory).Length, "no extra files");
			}
		}
	}
}
