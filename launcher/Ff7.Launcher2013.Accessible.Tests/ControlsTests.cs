using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class ControlsTests
	{
		// SHA-256 of :/res/ff7input.cfg, extracted from the Qt resource tree of
		// the original FF7_Launcher.exe (SHA-256 7C686CF4..., Steam build
		// 115956). That launcher copies this resource when ff7input.cfg is missing.
		internal const string OfficialDefaultsSha256 = "B3758E805F28E8E61DBC699A42CF0D4A28A2E706F90C70EB72E7A0BAFC961A10";

		internal static void Register(TestRunner runner)
		{
			runner.Add("a missing ff7input.cfg gets the original launcher's default controls", MissingControlsGetOfficialDefaults);
			runner.Add("an existing ff7input.cfg is kept byte for byte, even when empty or invalid", ExistingControlsAreKept);
			runner.Add("a controls file that appears while the default is written is never replaced", RacingControlsFileIsKept);
			runner.Add("a controls file that cannot be written leaves no temporary file", UnwritableControlsLeaveNothing);
			runner.Add("controls files are classified the way ff7_en.exe reads them", ControlsAreClassifiedAsTheGameReadsThem);
			runner.Add("FFNx reads the game folder and its language folder before Documents", FfnxControlsPrecedence);
		}

		internal static string ControlsPath(LegacyGameTree game)
		{
			return Path.Combine(game.DocumentDirectory, "ff7input.cfg");
		}

		internal static string Sha256(byte[] bytes)
		{
			using (var sha = SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
			}
		}

		// The first of the file's three binding rows, one 32-bit value per game
		// button. The original launcher's Keyboard settings show these slots.
		private static uint Keyboard(byte[] file, int slot)
		{
			return BitConverter.ToUInt32(file, 1 + 4 * slot);
		}

		internal static void CheckOfficialDefaults(byte[] file, string label)
		{
			Check.Equal(301, file.Length, label + " length");
			Check.Equal(OfficialDefaultsSha256, Sha256(file), label + " is the original launcher's file");
			// ff7_en.exe reads the 300-byte map only after a first byte of 1;
			// otherwise it keeps its numpad-only layout.
			Check.Equal((byte)1, file[0], label + " format byte");
			Check.Equal(0x2Du, Keyboard(file, 5), label + " OK is X");
			Check.Equal(0x2Eu, Keyboard(file, 6), label + " Cancel is C");
			Check.Equal(0x2Fu, Keyboard(file, 4), label + " Menu is V");
			Check.Equal(0x30u, Keyboard(file, 11), label + " Start is B");
			Check.Equal(0xC8u, Keyboard(file, 12), label + " Up is the Up arrow");
			Check.Equal(0xCDu, Keyboard(file, 13), label + " Right is the Right arrow");
			Check.Equal(0xD0u, Keyboard(file, 14), label + " Down is the Down arrow");
			Check.Equal(0xCBu, Keyboard(file, 15), label + " Left is the Left arrow");
			Check.Equal(0x1Cu, Keyboard(file, 21), label + " Enter is bound");
		}

		private static string[] Names(string directory)
		{
			return Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
		}

		private static void MissingControlsGetOfficialDefaults()
		{
			using (var game = new LegacyGameTree("controls-missing"))
			{
				Check.False(Directory.Exists(game.DocumentDirectory), "no documents folder yet");
				Check.Equal(LegacyControlsFileState.Created, LegacyControlsFile.CreateIfMissing(game.DocumentDirectory), "state");
				string path = ControlsPath(game);
				CheckOfficialDefaults(File.ReadAllBytes(path), "created file");
				Check.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "the game can save control changes");
				Check.Sequence(new[] { "ff7input.cfg" }, Names(game.DocumentDirectory), "only the controls file");
			}
		}

		private static void ExistingControlsAreKept()
		{
			// A player's own map: OK moved to Enter.
			byte[] custom = new byte[301];
			custom[0] = 1;
			custom[1 + 4 * 5] = 0x1C;
			byte[] wrongFormat = (byte[])custom.Clone();
			wrongFormat[0] = 0;
			var cases = new[]
			{
				Tuple.Create("custom", custom),
				Tuple.Create("empty", new byte[0]),
				Tuple.Create("short", new byte[] { 1, 2, 3 }),
				Tuple.Create("wrong format byte", wrongFormat),
				Tuple.Create("too long", new byte[400])
			};
			foreach (var item in cases)
			{
				using (var game = new LegacyGameTree("controls-keep"))
				{
					Directory.CreateDirectory(game.DocumentDirectory);
					string path = ControlsPath(game);
					File.WriteAllBytes(path, item.Item2);
					var written = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
					File.SetLastWriteTimeUtc(path, written);
					Check.Equal(LegacyControlsFileState.Kept, LegacyControlsFile.CreateIfMissing(game.DocumentDirectory), item.Item1 + " state");
					Check.Bytes(item.Item2, File.ReadAllBytes(path), item.Item1 + " bytes");
					Check.Equal(written, File.GetLastWriteTimeUtc(path), item.Item1 + " not rewritten");
					Check.Sequence(new[] { "ff7input.cfg" }, Names(game.DocumentDirectory), item.Item1 + " no other files");
				}
			}
			using (var game = new LegacyGameTree("controls-readonly"))
			{
				Directory.CreateDirectory(game.DocumentDirectory);
				string path = ControlsPath(game);
				File.WriteAllBytes(path, custom);
				File.SetAttributes(path, FileAttributes.ReadOnly);
				try
				{
					Check.Equal(LegacyControlsFileState.Kept, LegacyControlsFile.CreateIfMissing(game.DocumentDirectory), "read-only state");
					Check.Bytes(custom, File.ReadAllBytes(path), "read-only bytes");
				}
				finally
				{
					File.SetAttributes(path, FileAttributes.Normal);
				}
			}
		}

		private static void RacingControlsFileIsKept()
		{
			using (var game = new LegacyGameTree("controls-race"))
			{
				string path = ControlsPath(game);
				byte[] other = { 1, 9, 9 };
				string[] beforeRename = null;
				LegacyControlsFileState state = LegacyControlsFile.CreateIfMissing(game.DocumentDirectory, () =>
				{
					beforeRename = Directory.GetFiles(game.DocumentDirectory)
						.Select(file => Path.GetFileName(file) + ":" + new FileInfo(file).Length).ToArray();
					// The game or another program creates its own file after the
					// launcher found none and before the launcher's copy has the name.
					File.WriteAllBytes(path, other);
				});
				Check.True(beforeRename != null && beforeRename.Length == 1 && beforeRename[0].StartsWith("ff7input.cfg.", StringComparison.Ordinal) &&
					beforeRename[0].EndsWith(":301", StringComparison.Ordinal), "only a complete temporary copy existed: " + string.Join(", ", beforeRename ?? new string[0]));
				Check.Equal(LegacyControlsFileState.Kept, state, "state");
				Check.Bytes(other, File.ReadAllBytes(path), "the other file is kept");
				Check.Sequence(new[] { "ff7input.cfg" }, Names(game.DocumentDirectory), "no temporary file left");
			}
		}

		// ff7_en.exe's own map when the file is missing or its first byte is
		// not 1 (FUN_0041A828 then FUN_0041A96D): numpad keys, in message 16
		// order OK, Cancel, Menu, Up, Left, Right, Down, Start, Switch, Page Up,
		// Page Down, Target, Assist, Camera.
		internal static readonly int[] NumpadKeys = { 0x9C, 0x52, 0x4E, 0x48, 0x4B, 0x4D, 0x50, 0x4C, 0x53, 0x49, 0x51, 0x4F, 0x4A, 0x47 };

		private static string Keys(IReadOnlyList<int> keys)
		{
			return keys == null ? "none" : string.Join(",", keys.Select(key => key.ToString("X2")));
		}

		private static void ControlsAreClassifiedAsTheGameReadsThem()
		{
			using (var game = new LegacyGameTree("controls-kinds"))
			{
				Directory.CreateDirectory(game.DocumentDirectory);
				string path = ControlsPath(game);
				Check.Equal(LegacyControlsFileKind.Missing, LegacyControlsFile.Read(path).Kind, "missing");

				LegacyControlsFile.CreateIfMissing(game.DocumentDirectory);
				LegacyControlsReading official = LegacyControlsFile.Read(path);
				Check.Equal(LegacyControlsFileKind.OfficialDefaults, official.Kind, "official");
				Check.Equal(Keys(ProtocolTests.OfficialKeys), Keys(official.KeyTable), "official bindings");

				byte[] custom = File.ReadAllBytes(path);
				custom[1 + 4 * 5] = 0x1C;
				custom[1 + 4 * 0] = 0x39;
				File.WriteAllBytes(path, custom);
				LegacyControlsReading mine = LegacyControlsFile.Read(path);
				Check.Equal(LegacyControlsFileKind.Custom, mine.Kind, "custom");
				Check.Equal("1C,2E,2F,C8,CB,CD,D0,30,D2,C9,D1,CF,D3,39", Keys(mine.KeyTable), "custom bindings: OK Enter, Camera Space");

				File.WriteAllBytes(path, custom.Concat(new byte[99]).ToArray());
				Check.Equal(LegacyControlsFileKind.Custom, LegacyControlsFile.Read(path).Kind, "longer file: the game reads its first 301 bytes");

				byte[] wrongFormat = (byte[])custom.Clone();
				wrongFormat[0] = 2;
				File.WriteAllBytes(path, wrongFormat);
				LegacyControlsReading fallback = LegacyControlsFile.Read(path);
				Check.Equal(LegacyControlsFileKind.NativeFallback, fallback.Kind, "format byte 2");
				Check.Equal(Keys(NumpadKeys), Keys(fallback.KeyTable), "the game's numpad map");
				File.WriteAllBytes(path, new byte[] { 0 });
				Check.Equal(LegacyControlsFileKind.NativeFallback, LegacyControlsFile.Read(path).Kind, "one byte of 0");

				File.WriteAllBytes(path, new byte[0]);
				LegacyControlsReading empty = LegacyControlsFile.Read(path);
				Check.Equal(LegacyControlsFileKind.Unverified, empty.Kind, "empty: the format byte cannot be read");
				Check.Null(empty.KeyTable, "no bindings claimed for an empty file");
				File.WriteAllBytes(path, custom.Take(120).ToArray());
				LegacyControlsReading shortFile = LegacyControlsFile.Read(path);
				Check.Equal(LegacyControlsFileKind.Unverified, shortFile.Kind, "short map");
				Check.Equal(120L, shortFile.Length, "length reported");
				Check.Null(shortFile.KeyTable, "no bindings claimed for a short map");
			}
		}

		private static void FfnxControlsPrecedence()
		{
			using (var ffnx = new LegacyGameTree("controls-ffnx-order").WithStandard2013Files("ff7_de.exe"))
			using (var original = new LegacyGameTree("controls-original-order").WithStandard2013Files().WithDriverBytes(1000))
			using (var unknown = new LegacyGameTree("controls-unknown-order").WithStandard2013Files().WithDriverBytes(1048577))
			{
				AssemblyFactory.CompileLibraryWithProduct(Path.Combine(ffnx.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
				var installation = LegacyInstallation.Detect(ffnx.Root, ffnx.ManifestPath, true);
				LegacyControlsLocation documents = LegacyControlsFile.Resolve(installation, ffnx.DocumentDirectory);
				Check.Equal(Path.Combine(ffnx.DocumentDirectory, "ff7input.cfg"), documents.Path, "nothing yet: Documents");
				Check.Equal(LegacyControlsPlace.Documents, documents.Place, "Documents place");

				string language = ffnx.WriteFile(@"data\lang-de\ff7input.cfg", 0);
				LegacyControlsLocation languageFolder = LegacyControlsFile.Resolve(installation, ffnx.DocumentDirectory);
				Check.Equal(language, languageFolder.Path, "language folder first");
				Check.Equal(LegacyControlsPlace.LanguageFolder, languageFolder.Place, "language place");

				string root = ffnx.WriteFile("ff7input.cfg", 0);
				LegacyControlsLocation gameFolder = LegacyControlsFile.Resolve(installation, ffnx.DocumentDirectory);
				Check.Equal(root, gameFolder.Path, "game folder before all");
				Check.Equal(LegacyControlsPlace.GameFolder, gameFolder.Place, "game folder place");

				original.WriteFile("ff7input.cfg", 301);
				LegacyControlsLocation driver = LegacyControlsFile.Resolve(LegacyInstallation.Detect(original.Root, original.ManifestPath, true), original.DocumentDirectory);
				Check.Equal(Path.Combine(original.DocumentDirectory, "ff7input.cfg"), driver.Path, "the original driver always opens the message 11 folder");

				Check.Null(LegacyControlsFile.Resolve(LegacyInstallation.Detect(unknown.Root, unknown.ManifestPath, true), unknown.DocumentDirectory), "an unrecognized driver's file is unknown");
			}
		}

		private static void UnwritableControlsLeaveNothing()
		{
			using (var game = new LegacyGameTree("controls-blocked"))
			{
				// A file named Square Enix stands where the documents folder belongs.
				File.WriteAllBytes(Path.Combine(game.DocumentsRoot, "Square Enix"), new byte[] { 7 });
				Check.Throws<IOException>(() => LegacyControlsFile.CreateIfMissing(game.DocumentDirectory), "blocked folder");
				Check.Sequence(new[] { "Square Enix" }, Names(game.DocumentsRoot), "nothing else written");
			}
		}
	}
}
