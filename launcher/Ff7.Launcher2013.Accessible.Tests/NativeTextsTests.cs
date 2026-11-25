using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class NativeTextsTests
	{
		internal static void Register(TestRunner runner)
		{
			runner.Add("the native text tables are the original launcher's 165, 36, 36 and 36 source strings", SourceTablesMatchOriginal);
			runner.Add("Qt translations are found by context and the exact source bytes", QtTranslationsLookup);
			runner.Add("damaged Qt translation data is refused", QtTranslationsRejectDamage);
			runner.Add("strings.dat yields the block of the game's language", StringsDatSelectsLanguage);
			runner.Add("damaged or incomplete strings.dat is refused", StringsDatRejectsDamage);
			runner.Add("untranslated texts fall back to their source text as Qt does", UntranslatedFallsBackToSource);
			runner.Add("the installed strings.dat yields every native text in four languages", InstalledStringsDat);
		}

		private static string Digest(IEnumerable<string> values)
		{
			using (var sha = SHA256.Create())
			{
				byte[] bytes = values.SelectMany(value => NativeFixtures.Latin1.GetBytes(value).Concat(new byte[] { 0 })).ToArray();
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
			}
		}

		private static void SourceTablesMatchOriginal()
		{
			// Digests of the NUL-terminated Latin-1 strings, extracted from the
			// original FF7_Launcher.exe and proven there byte for byte.
			Check.Equal(165, LegacyNativeTextSources.InGame.Length, "message 15 count");
			Check.Equal("DF7C4F32DE4F9DCCF63F232B7D8FB9E4726E4997CC1C73D78C8E746AAE4788F8", Digest(LegacyNativeTextSources.InGame), "message 15 sources");
			Check.Equal(36, LegacyNativeTextSources.AchievementNames.Length, "achievement names");
			Check.Equal("CD5BA89CB4885EFBBF92700BA85FE00B1F053FF04ED2358E8CE4DB351BDEDAE8", Digest(LegacyNativeTextSources.AchievementNames), "names");
			Check.Equal(36, LegacyNativeTextSources.AchievementDescriptions.Length, "achievement descriptions");
			Check.Equal("6632FB24B960BF4C8223A8DBDD341091E845E363F4203F356746E66206EAEF0F", Digest(LegacyNativeTextSources.AchievementDescriptions), "descriptions");
			Check.Equal(36, LegacyNativeTextSources.AchievementIcons.Length, "achievement icons");
			Check.Equal("6C71D51536ED2C77330B4AC8B1F6CCB63FE86E1FA746B2129F8F57F4C320E493", Digest(LegacyNativeTextSources.AchievementIcons), "icons");
			Check.Equal("Save file has been locally saved", LegacyNativeTextSources.InGame[4], "notice 4");
			Check.Sequence(new[] { "Keyboard", null, "Ok", "Cancel", "Menu", "Up", "Left", "Right", "Down", "Start", "Switch", "Page Up", "Page Down", "Target", "Assist", "Camera", "DIK_ESCAPE" },
				LegacyNativeTextSources.InGame.Skip(5).Take(17).Select((value, index) => index == 1 ? null : value), "keyboard overlay labels");
			Check.Equal("End of Part I", LegacyNativeTextSources.AchievementNames[0], "first achievement");
			Check.Equal("48464.fgt", LegacyNativeTextSources.AchievementIcons[0], "first icon");
		}

		private const string HelpSource = "¤ In FULL SCREEN, returns to Windows desktop by \"Ctrl + Alt + Delete\".";

		private static void QtTranslationsLookup()
		{
			byte[] qm = NativeFixtures.Qm(new[]
			{
				new NativeFixtures.Entry("QObject", "Keyboard", "Clavier"),
				new NativeFixtures.Entry("SetupView", "Keyboard", "Clavier des réglages"),
				new NativeFixtures.Entry("QObject", HelpSource, "SÉLECTIONNEZ « QUITTER »"),
				new NativeFixtures.Entry("FF7Launcher", "GAME_VERSION", "v1.0.9 Français Steam")
			});
			QtTranslations translations = QtTranslations.Parse(qm);
			Check.Equal("Clavier", translations.Translate("QObject", "Keyboard"), "context QObject");
			Check.Equal("Clavier des réglages", translations.Translate("SetupView", "Keyboard"), "context SetupView");
			Check.Null(translations.Translate("FF7Launcher", "Keyboard"), "other context");
			Check.Equal("SÉLECTIONNEZ « QUITTER »", translations.Translate("QObject", HelpSource), "Latin-1 source byte 0xA4");
			Check.Equal("v1.0.9 Français Steam", translations.Translate("FF7Launcher", "GAME_VERSION"), "game version");

			// The same text stored as UTF-8 is a different source to Qt.
			var utf8 = new NativeFixtures.Entry("QObject", "x", "UTF-8 entry") { Source = Encoding.UTF8.GetBytes(HelpSource) };
			Check.Null(QtTranslations.Parse(NativeFixtures.Qm(new[] { utf8 })).Translate("QObject", HelpSource), "UTF-8 bytes do not match");
		}

		private static void QtTranslationsRejectDamage()
		{
			byte[] good = NativeFixtures.Qm(new[] { new NativeFixtures.Entry("QObject", "Keyboard", "Clavier") });
			byte[] magic = (byte[])good.Clone();
			magic[0] ^= 0xFF;
			Check.Throws<InvalidDataException>(() => QtTranslations.Parse(magic), "wrong magic");
			Check.Throws<InvalidDataException>(() => QtTranslations.Parse(good.Take(good.Length - 5).ToArray()), "truncated section");
			Check.Throws<InvalidDataException>(() => QtTranslations.Parse(NativeFixtures.QmMagic), "no message section");
			byte[] badTag = (byte[])good.Clone();
			// The first message tag follows the magic, the empty hash section and
			// the message section header.
			badTag[16 + 5 + 5] = 0x55;
			Check.Throws<InvalidDataException>(() => QtTranslations.Parse(badTag), "unknown message tag");
			byte[] longString = (byte[])good.Clone();
			longString[16 + 5 + 5 + 1] = 0x7F;
			Check.Throws<InvalidDataException>(() => QtTranslations.Parse(longString), "string longer than its section");
		}

		private static void StringsDatSelectsLanguage()
		{
			byte[] file = NativeFixtures.StandardStringsDat();
			for (int index = 0; index < 4; index++)
			{
				QtTranslations translations = QtTranslations.Parse(LegacyStringsDat.Block(file, NativeFixtures.Languages[index]));
				Check.Equal(NativeFixtures.GameVersions[index], translations.Translate("FF7Launcher", "GAME_VERSION"), NativeFixtures.Languages[index] + " block");
			}
			byte[] englishOnly = NativeFixtures.StringsDat(new[] { new KeyValuePair<int, byte[]>(1000, NativeFixtures.Qm(new NativeFixtures.Entry[0])) });
			Check.Throws<InvalidDataException>(() => LegacyStringsDat.Block(englishOnly, "de"), "no German block");
			Check.Throws<ArgumentException>(() => LegacyStringsDat.Block(file, "ja"), "no Japanese edition");
		}

		private static void StringsDatRejectsDamage()
		{
			byte[] file = NativeFixtures.StandardStringsDat();
			Check.Throws<InvalidDataException>(() => LegacyStringsDat.Block(new byte[3], "en"), "shorter than its count");
			Check.Throws<InvalidDataException>(() => LegacyStringsDat.Block(BitConverter.GetBytes(0), "en"), "no blocks");
			Check.Throws<InvalidDataException>(() => LegacyStringsDat.Block(BitConverter.GetBytes(100), "en"), "count past the end");
			Check.Throws<InvalidDataException>(() => LegacyStringsDat.Block(file.Take(file.Length - 1).ToArray(), "es"), "last block cut short");
			byte[] negative = (byte[])file.Clone();
			Array.Copy(BitConverter.GetBytes(-1), 0, negative, 8, 4);
			Check.Throws<InvalidDataException>(() => LegacyStringsDat.Block(negative, "en"), "negative size");
			using (var game = new LegacyGameTree("strings-damaged").WithStandard2013Files())
			{
				File.WriteAllBytes(Path.Combine(game.Root, "strings.dat"), file.Take(40).ToArray());
				Check.Throws<InvalidDataException>(() => LegacyNativeTexts.Load(game.Root, "en"), "damaged file");
				File.Delete(Path.Combine(game.Root, "strings.dat"));
				Check.Throws<FileNotFoundException>(() => LegacyNativeTexts.Load(game.Root, "en"), "missing file");
			}
		}

		private static void UntranslatedFallsBackToSource()
		{
			LegacyNativeTexts none = LegacyNativeTexts.From(QtTranslations.Parse(NativeFixtures.Qm(new NativeFixtures.Entry[0])));
			Check.Equal(165, none.InGame.Count, "every string present");
			Check.Equal("DIK_ESCAPE", none.InGame[21], "tr returns the source");
			Check.Equal(HelpSource, none.InGame[6], "Latin-1 source decoded");
			Check.Equal("GAME_VERSION", none.GameVersion, "untranslated game version");
			Check.Equal(36, none.Achievements.Count, "every achievement present");
			Check.Equal("48464.fgt", none.Achievements[0].Icon, "icon names are never translated");

			using (var game = new LegacyGameTree("strings-standard").WithStandard2013Files())
			{
				LegacyNativeTexts french = LegacyNativeTexts.Load(game.Root, "fr");
				Check.Equal("Succès débloqué", french.InGame[0], "translated notice");
				Check.Equal("ECHAP", french.InGame[21], "translated key name");
				Check.Equal("Keyboard", french.InGame[5], "untranslated label");
				Check.Equal("v1.0.9 Français Steam", french.GameVersion, "game version");
				Check.Equal("Part I fr", french.Achievements[0].Name, "achievement name");
				Check.Equal("First part fr", french.Achievements[0].Description, "achievement description");
				Check.Equal("End of Part II", french.Achievements[1].Name, "untranslated achievement");
			}
		}

		// Read-only check of a real 2013 install, as BLIND_SOLDIER_STOCK_2013_ROOT
		// names it. Without the variable it only reports that it did not run.
		private static void InstalledStringsDat()
		{
			string root = Environment.GetEnvironmentVariable("BLIND_SOLDIER_STOCK_2013_ROOT");
			if (string.IsNullOrWhiteSpace(root))
			{
				Console.WriteLine("  (no BLIND_SOLDIER_STOCK_2013_ROOT: installed strings.dat not checked)");
				return;
			}
			string[] help = { "SELECT “QUIT” IN THE GAME MENU TO EXIT THE GAME.", null, null, null };
			for (int index = 0; index < 4; index++)
			{
				string code = NativeFixtures.Languages[index];
				LegacyNativeTexts texts = LegacyNativeTexts.Load(root, code);
				Check.Equal(NativeFixtures.GameVersions[index], texts.GameVersion, code + " game version");
				Check.Equal(NativeFixtures.AchievementUnlocked[index], texts.InGame[0], code + " notice");
				Check.True(texts.InGame.All(value => !value.StartsWith("DIK_", StringComparison.Ordinal)), code + " every key name translated");
				Check.True(texts.Achievements.All(item => item.Name.Length > 0 && item.Description.Length > 0), code + " achievements");
				if (help[index] != null)
				{
					Check.Equal(help[index], texts.InGame[6], code + " help line");
				}
				int size = LegacyLauncherProtocol.EncodeBytes(LegacyLauncherProtocol.InGameTexts(texts.InGame)).Length +
					LegacyLauncherProtocol.EncodeBytes(LegacyLauncherProtocol.AchievementTexts(texts.Achievements)).Length;
				Console.WriteLine("  " + code + ": " + texts.GameVersion + "; messages 14 and 15 take " + size + " bytes");
			}
		}
	}
}
