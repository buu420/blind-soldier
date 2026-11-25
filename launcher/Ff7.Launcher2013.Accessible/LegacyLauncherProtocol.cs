using System;
using System.Collections.Generic;
using System.Text;

namespace BlindSoldier.Ff7Launcher2013
{
	// Message identifiers sent from the launcher to the original 2013 AF3DN.P.
	internal enum LegacyLauncherMessageId : uint
	{
		UserSaveDirectory = 10,
		DocumentDirectory = 11,
		InstallDirectory = 12,
		LocaleDataDirectory = 13,
		GameVersion = 18,
		DisableCloud = 22,
		EndUserInfo = 24
	}

	// Messages the original 2013 AF3DN.P sends to its launcher (Ghidra, AF3DN.P
	// SHA-256 1763359F...): 1 when an achievement is earned, 2 to ask for a
	// cloud sync before the save screen (only while cloud saving is on), 3 with
	// [0, save file, slot] after a save and [1, save file, slot] after a load,
	// and 4 to ask for the start-up information.
	internal enum LegacyGameMessageId : uint
	{
		AchievementUnlocked = 1,
		CloudSyncRequest = 2,
		SaveFileEvent = 3,
		StartupInfoRequest = 4
	}

	internal sealed class LegacyGameMessage
	{
		internal LegacyGameMessage(uint id, IReadOnlyList<int> values)
		{
			Id = id;
			Values = values ?? Array.Empty<int>();
		}

		internal uint Id { get; }

		internal IReadOnlyList<int> Values { get; }

		internal bool TryGetSavedSlot(out int saveIndex, out int slotIndex)
		{
			saveIndex = 0;
			slotIndex = 0;
			if (Id != (uint)LegacyGameMessageId.SaveFileEvent || Values.Count != 3 || Values[0] != 0)
			{
				return false;
			}
			saveIndex = Values[1];
			slotIndex = Values[2];
			return true;
		}
	}

	internal sealed class LegacyLauncherMessage
	{
		internal LegacyLauncherMessage(LegacyLauncherMessageId id, string text)
		{
			Id = id;
			Text = text;
		}

		internal LegacyLauncherMessageId Id { get; }

		// Null for messages that carry only their identifier.
		internal string Text { get; }
	}

	// The launcher-to-game contract of the original FF7_Launcher.exe, as
	// reimplemented by FF78Launcher (github.com/julianxhokaxhiu/FF78Launcher,
	// src/winmain.cpp and src/launcher.cpp): four named semaphores and a
	// 0x20000-byte named mapping whose launcher half starts at 0x10000. Each
	// message is written there, gameCanRead is released, and the launcher
	// waits for gameDidRead. Identifier-only messages leave the previous
	// length field untouched, exactly as FF78Launcher does.
	internal static class LegacyLauncherProtocol
	{
		internal const string DefaultPrefix = "ff7";
		internal const int SharedMemorySize = 0x20000;
		internal const int LauncherMessageOffset = 0x10000;
		internal const int LauncherRegionSize = SharedMemorySize - LauncherMessageOffset;
		internal const int GameMessageOffset = 0;
		internal const int MaximumGameMessageValues = 64;

		// Enough of the game's region for an identifier, a count and the
		// largest integer array decoded here.
		internal const int GameMessageReadLength = 8 + 4 * MaximumGameMessageValues;

		private static readonly HashSet<string> LanguageCodes = new HashSet<string>(StringComparer.Ordinal) { "en", "fr", "de", "es" };

		internal static string GameCanReadName(string prefix) => prefix + "_gameCanReadMsgSem";

		internal static string GameDidReadName(string prefix) => prefix + "_gameDidReadMsgSem";

		internal static string LauncherCanReadName(string prefix) => prefix + "_launcherCanReadMsgSem";

		internal static string LauncherDidReadName(string prefix) => prefix + "_launcherDidReadMsgSem";

		internal static string SharedMemoryName(string prefix) => prefix + "_sharedMemoryWithLauncher";

		internal static IReadOnlyList<LegacyLauncherMessage> BuildStartupSequence(
			string languageCode,
			string userSaveDirectory,
			string documentDirectory,
			string installDirectory,
			string gameVersion)
		{
			if (languageCode == null || !LanguageCodes.Contains(languageCode))
			{
				throw new ArgumentException("The 2013 Steam edition has no " + (languageCode ?? "null") + " executable.", nameof(languageCode));
			}

			return new[]
			{
				// FF78Launcher sends the locale with its terminating NUL counted in
				// the length; that exact form is what the original driver has read.
				new LegacyLauncherMessage(LegacyLauncherMessageId.LocaleDataDirectory, "lang-" + languageCode + "\0"),
				new LegacyLauncherMessage(LegacyLauncherMessageId.UserSaveDirectory, Required(userSaveDirectory, nameof(userSaveDirectory))),
				new LegacyLauncherMessage(LegacyLauncherMessageId.DocumentDirectory, Required(documentDirectory, nameof(documentDirectory))),
				new LegacyLauncherMessage(LegacyLauncherMessageId.InstallDirectory, Required(installDirectory, nameof(installDirectory))),
				new LegacyLauncherMessage(LegacyLauncherMessageId.GameVersion, Required(gameVersion, nameof(gameVersion))),
				new LegacyLauncherMessage(LegacyLauncherMessageId.DisableCloud, null),
				new LegacyLauncherMessage(LegacyLauncherMessageId.EndUserInfo, null)
			};
		}

		// The bytes written at the start of the launcher region: the identifier,
		// then for text messages the UTF-16 length, the text and a NUL.
		internal static byte[] EncodeBytes(LegacyLauncherMessage message)
		{
			if (message == null)
			{
				throw new ArgumentNullException(nameof(message));
			}
			if (message.Text == null)
			{
				return BitConverter.GetBytes((uint)message.Id);
			}

			byte[] text = Encoding.Unicode.GetBytes(message.Text);
			var bytes = new byte[8 + text.Length + 2];
			Array.Copy(BitConverter.GetBytes((uint)message.Id), 0, bytes, 0, 4);
			Array.Copy(BitConverter.GetBytes((uint)message.Text.Length), 0, bytes, 4, 4);
			Array.Copy(text, 0, bytes, 8, text.Length);
			return bytes;
		}

		// The driver writes the identifier, then for message 1 one integer and for
		// messages 3 and 16 a count followed by that many integers. Text payloads
		// from the game are not needed here and are not decoded.
		internal static LegacyGameMessage DecodeGameMessage(byte[] bytes)
		{
			if (bytes == null || bytes.Length < 4)
			{
				throw new ArgumentException("A game message starts with a 4-byte identifier.", nameof(bytes));
			}
			uint id = BitConverter.ToUInt32(bytes, 0);
			switch (id)
			{
				case (uint)LegacyGameMessageId.AchievementUnlocked:
					return new LegacyGameMessage(id, bytes.Length >= 8 ? new[] { BitConverter.ToInt32(bytes, 4) } : Array.Empty<int>());
				case (uint)LegacyGameMessageId.SaveFileEvent:
				case 16:
					if (bytes.Length < 8)
					{
						return new LegacyGameMessage(id, Array.Empty<int>());
					}
					int count = BitConverter.ToInt32(bytes, 4);
					if (count < 0 || count > MaximumGameMessageValues || 8L + 4L * count > bytes.Length)
					{
						return new LegacyGameMessage(id, Array.Empty<int>());
					}
					var values = new int[count];
					for (int index = 0; index < count; index++)
					{
						values[index] = BitConverter.ToInt32(bytes, 8 + 4 * index);
					}
					return new LegacyGameMessage(id, values);
				default:
					return new LegacyGameMessage(id, Array.Empty<int>());
			}
		}

		internal static void Encode(LegacyLauncherMessage message, byte[] launcherRegion)
		{
			if (launcherRegion == null)
			{
				throw new ArgumentNullException(nameof(launcherRegion));
			}
			byte[] bytes = EncodeBytes(message);
			if (bytes.Length > launcherRegion.Length || bytes.Length > LauncherRegionSize)
			{
				throw new ArgumentException("The launcher message does not fit the shared launcher region.", nameof(message));
			}
			Array.Copy(bytes, launcherRegion, bytes.Length);
		}

		private static string Required(string value, string name)
		{
			if (string.IsNullOrEmpty(value))
			{
				throw new ArgumentException("A launcher message payload is required.", name);
			}
			return value;
		}
	}
}
