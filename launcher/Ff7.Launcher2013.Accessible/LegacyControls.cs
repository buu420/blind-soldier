using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlindSoldier.Ff7Launcher2013
{
	internal enum LegacyControlsFileState
	{
		Created,
		Kept
	}

	// How ff7_en.exe treats a controls file (FUN_006CE6EC): it reads one byte
	// and, only when that byte is 1, the 300-byte map. Before that, FUN_0041A828
	// filled the map with its numpad layout, which stays when the byte is not 1.
	internal enum LegacyControlsFileKind
	{
		Missing,
		OfficialDefaults,
		Custom,
		NativeFallback,
		// Empty or a short map: what the game's file reads then leave in the map
		// has not been verified.
		Unverified,
		Unreadable
	}

	internal enum LegacyControlsPlace
	{
		Documents,
		GameFolder,
		LanguageFolder
	}

	internal sealed class LegacyControlsLocation
	{
		internal LegacyControlsLocation(string path, LegacyControlsPlace place)
		{
			Path = path;
			Place = place;
		}

		internal string Path { get; }

		internal LegacyControlsPlace Place { get; }
	}

	internal sealed class LegacyControlsReading
	{
		internal LegacyControlsReading(LegacyControlsFileKind kind, long length, IReadOnlyList<int> keyTable)
		{
			Kind = kind;
			Length = length;
			KeyTable = keyTable;
		}

		internal LegacyControlsFileKind Kind { get; }

		internal long Length { get; }

		// The keyboard bindings for launcher message 16, or null when they are
		// not known.
		internal IReadOnlyList<int> KeyTable { get; }
	}

	// ff7input.cfg, the game's control map: a format byte of 1, then three
	// rows of 25 little-endian 32-bit bindings, one per game button. Values
	// below 0xDE are DirectInput keys, 0xDF to 0xE1 mouse buttons, 0xE3 to 0xE6
	// joystick directions and 0xEB to 0xF4 joystick buttons 1 to 10. Without
	// the file, or with a first byte other than 1, ff7_en.exe keeps its
	// numpad-only layout, so Enter and X do nothing at the opening logos. The
	// original launcher copies its built-in default whenever the file is
	// missing; this launcher does the same and never changes an existing file.
	internal static class LegacyControlsFile
	{
		internal const string FileName = "ff7input.cfg";
		internal const int EncodedLength = 301;

		// The original FF7_Launcher.exe's Qt resource :/res/ff7input.cfg (Steam
		// build 115956), SHA-256 B3758E80.... The names are the button labels of
		// the original launcher's Keyboard and Joystick settings. The third row
		// is all zero.
		private static readonly uint[] OfficialBindings =
		{
			// Keyboard: Camera Home, Target End, Page Up, Page Down, Menu V, OK X,
			// Cancel C, Switch Insert, Assist Delete, two unused, Start B, the
			// arrow keys, then Escape, the arrow keys, Enter, the first mouse
			// button, and joystick buttons 1 and 3.
			0xC7, 0xCF, 0xC9, 0xD1, 0x2F, 0x2D, 0x2E, 0xD2, 0xD3, 0x00, 0x00, 0x30, 0xC8,
			0xCD, 0xD0, 0xCB, 0x01, 0xC8, 0xD0, 0xCB, 0xCD, 0x1C, 0xDF, 0xEB, 0xED,
			// Joystick: Camera 10, Target 8, Page Up 6, Page Down 7, Menu 3, OK 1,
			// Cancel 2, Switch 5, Assist 9, two unused, Start 4, the directions,
			// then button 2, the directions and the second mouse button.
			0xF4, 0xF2, 0xF0, 0xF1, 0xED, 0xEB, 0xEC, 0xEF, 0xF3, 0x00, 0x00, 0xEE, 0xE3,
			0xE6, 0xE4, 0xE5, 0xEC, 0xE3, 0xE4, 0xE5, 0xE6, 0x00, 0xE0, 0x00, 0x00
		};

		// The keyboard slots of message 16, in the original launcher's order
		// (FF7_Launcher.exe FUN_00413F90): OK, Cancel, Menu, Up, Left, Right,
		// Down, Start, Switch, Page Up, Page Down, Target, Assist, Camera.
		private static readonly int[] KeyTableSlots = { 5, 6, 4, 12, 15, 13, 14, 11, 7, 2, 3, 1, 8, 0 };

		// ff7_en.exe's own keyboard map in those slots (FUN_0041A96D): numpad
		// Enter, 0, plus, 8, 4, 6, 2, 5, period, 9, 3, 1, minus and 7.
		private static readonly int[] NativeFallbackKeys = { 0x9C, 0x52, 0x4E, 0x48, 0x4B, 0x4D, 0x50, 0x4C, 0x53, 0x49, 0x51, 0x4F, 0x4A, 0x47 };

		private const long MaximumReadLength = 1024 * 1024;

		internal static string PathIn(string documentDirectory)
		{
			return Path.Combine(documentDirectory, FileName);
		}

		// The file the game will open. The original driver always opens the one
		// in the folder of launcher message 11 (AF3DN.P 0x1000DF32). FFNx 1.25.2
		// opens the first that exists of the game folder's, data\lang-xx's and
		// Documents' copy. Where another driver reads is not known: null.
		internal static LegacyControlsLocation Resolve(LegacyInstallation installation, string documentDirectory)
		{
			switch (installation?.Driver?.Kind)
			{
				case LegacyDriverKind.Original:
					return new LegacyControlsLocation(PathIn(documentDirectory), LegacyControlsPlace.Documents);
				case LegacyDriverKind.Ffnx:
					string root = Path.Combine(installation.Root, FileName);
					if (Occupied(root))
					{
						return new LegacyControlsLocation(root, LegacyControlsPlace.GameFolder);
					}
					string language = Path.Combine(installation.Root, "data", "lang-" + installation.Selected.LanguageCode, FileName);
					if (Occupied(language))
					{
						return new LegacyControlsLocation(language, LegacyControlsPlace.LanguageFolder);
					}
					return new LegacyControlsLocation(PathIn(documentDirectory), LegacyControlsPlace.Documents);
				default:
					return null;
			}
		}

		internal static LegacyControlsReading Read(string path)
		{
			if (Directory.Exists(path))
			{
				return new LegacyControlsReading(LegacyControlsFileKind.Unreadable, 0, null);
			}
			if (!File.Exists(path))
			{
				return new LegacyControlsReading(LegacyControlsFileKind.Missing, 0, null);
			}
			byte[] bytes;
			try
			{
				long length = new FileInfo(path).Length;
				if (length > MaximumReadLength)
				{
					return new LegacyControlsReading(LegacyControlsFileKind.Unverified, length, null);
				}
				bytes = File.ReadAllBytes(path);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				return new LegacyControlsReading(LegacyControlsFileKind.Unreadable, 0, null);
			}

			if (bytes.Length >= EncodedLength && bytes[0] == 1)
			{
				int[] keys = KeyTableSlots.Select(slot => (int)bytes[1 + 4 * slot]).ToArray();
				bool official = bytes.Take(EncodedLength).SequenceEqual(Encode());
				return new LegacyControlsReading(official ? LegacyControlsFileKind.OfficialDefaults : LegacyControlsFileKind.Custom, bytes.Length, keys);
			}
			if (bytes.Length >= 1 && bytes[0] != 1)
			{
				return new LegacyControlsReading(LegacyControlsFileKind.NativeFallback, bytes.Length, NativeFallbackKeys);
			}
			return new LegacyControlsReading(LegacyControlsFileKind.Unverified, bytes.Length, null);
		}

		// Any entry with the name counts, even an empty or unreadable file.
		internal static bool Exists(string documentDirectory)
		{
			return Occupied(PathIn(documentDirectory));
		}

		// Writes the original default only when no ff7input.cfg exists. The
		// complete file is renamed into place, which Windows refuses while the
		// name is taken, so a file that the game or another program creates
		// meanwhile is never replaced, and no half-written file is ever seen.
		// beforeRename runs once the complete copy exists under its temporary
		// name.
		internal static LegacyControlsFileState CreateIfMissing(string documentDirectory, Action beforeRename = null)
		{
			if (string.IsNullOrEmpty(documentDirectory))
			{
				throw new ArgumentException("A documents folder is required.", nameof(documentDirectory));
			}
			string path = PathIn(documentDirectory);
			if (Occupied(path))
			{
				return LegacyControlsFileState.Kept;
			}

			Directory.CreateDirectory(documentDirectory);
			string temporary = path + ".blind-soldier-" + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				byte[] bytes = Encode();
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stream.Write(bytes, 0, bytes.Length);
					stream.Flush(true);
				}
				beforeRename?.Invoke();
				return TryRename(temporary, path) ? LegacyControlsFileState.Created : LegacyControlsFileState.Kept;
			}
			finally
			{
				DeleteLeftover(temporary);
			}
		}

		// Moves a finished file to its name unless something already holds it.
		private static bool TryRename(string temporary, string path)
		{
			try
			{
				File.Move(temporary, path);
				return true;
			}
			catch (IOException) when (Occupied(path))
			{
				return false;
			}
		}

		private static bool Occupied(string path)
		{
			return File.Exists(path) || Directory.Exists(path);
		}

		private static byte[] Encode()
		{
			var bytes = new byte[EncodedLength];
			bytes[0] = 1;
			for (int index = 0; index < OfficialBindings.Length; index++)
			{
				Array.Copy(BitConverter.GetBytes(OfficialBindings[index]), 0, bytes, 1 + 4 * index, 4);
			}
			return bytes;
		}

		private static void DeleteLeftover(string temporary)
		{
			try
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				// A stray temporary file is harmless; the failure that left it is
				// the one to report.
			}
		}
	}
}
