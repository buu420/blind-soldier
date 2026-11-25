using System;
using System.Collections.Generic;
using System.IO;
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
		AchievementTexts = 14,
		InGameTexts = 15,
		KeyTable = 16,
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

		private LegacyLauncherMessage(LegacyLauncherMessageId id, byte[] payload)
		{
			Id = id;
			Payload = payload;
		}

		internal static LegacyLauncherMessage WithPayload(LegacyLauncherMessageId id, byte[] payload)
		{
			return new LegacyLauncherMessage(id, payload ?? throw new ArgumentNullException(nameof(payload)));
		}

		internal LegacyLauncherMessageId Id { get; }

		// Null for messages that carry only their identifier.
		internal string Text { get; }

		// The bytes after the identifier of a table message (14, 15 and 16).
		internal byte[] Payload { get; }
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

		// The record counts the original driver's readers expect: they read
		// exactly this many records, so a payload must hold exactly these.
		internal const int InGameTextCount = 165;
		internal const int AchievementCount = 36;
		internal const int KeyTableCount = 14;
		internal const int MaximumTableTextLength = 4096;

		internal static void ValidateStartupPaths(string languageCode, string userSaveDirectory, string documentDirectory, string installDirectory)
		{
			if (languageCode == null || !LanguageCodes.Contains(languageCode))
			{
				throw new ArgumentException("The 2013 Steam edition has no " + (languageCode ?? "null") + " executable.", nameof(languageCode));
			}
			Required(userSaveDirectory, nameof(userSaveDirectory));
			Required(documentDirectory, nameof(documentDirectory));
			Required(installDirectory, nameof(installDirectory));
		}

		// The original launcher's order (FF7_Launcher.exe FUN_00413F90): locale,
		// in-game strings, key table, the three folders, achievement texts, game
		// version, the cloud choice and the end. Message 16 is left out when the
		// bindings the game will use are not known. The original also sends
		// message 1 for each achievement its sign-in data lists as earned; the
		// driver reads those from Steam itself (AF3DN.P FUN_1001EE70).
		internal static IReadOnlyList<LegacyLauncherMessage> BuildStartupSequence(
			string languageCode,
			string userSaveDirectory,
			string documentDirectory,
			string installDirectory,
			LegacyNativeTexts texts,
			IReadOnlyList<int> keyTable)
		{
			ValidateStartupPaths(languageCode, userSaveDirectory, documentDirectory, installDirectory);
			if (texts == null)
			{
				throw new ArgumentNullException(nameof(texts));
			}

			var messages = new List<LegacyLauncherMessage>
			{
				// FF78Launcher sends the locale with its terminating NUL counted in
				// the length; that exact form is what the original driver has read.
				new LegacyLauncherMessage(LegacyLauncherMessageId.LocaleDataDirectory, "lang-" + languageCode + "\0"),
				InGameTexts(texts.InGame)
			};
			if (keyTable != null)
			{
				messages.Add(KeyTable(keyTable));
			}
			messages.Add(new LegacyLauncherMessage(LegacyLauncherMessageId.UserSaveDirectory, userSaveDirectory));
			messages.Add(new LegacyLauncherMessage(LegacyLauncherMessageId.DocumentDirectory, documentDirectory));
			messages.Add(new LegacyLauncherMessage(LegacyLauncherMessageId.InstallDirectory, installDirectory));
			messages.Add(AchievementTexts(texts.Achievements));
			messages.Add(new LegacyLauncherMessage(LegacyLauncherMessageId.GameVersion, Bounded(texts.GameVersion, "the game version")));
			messages.Add(new LegacyLauncherMessage(LegacyLauncherMessageId.DisableCloud, null));
			messages.Add(new LegacyLauncherMessage(LegacyLauncherMessageId.EndUserInfo, null));
			foreach (LegacyLauncherMessage message in messages)
			{
				if (EncodeBytes(message).Length > LauncherRegionSize)
				{
					throw new InvalidDataException("Launcher message " + (uint)message.Id + " does not fit the shared launcher region.");
				}
			}
			return messages;
		}

		// Message 15 (writer FUN_0042F910, reader FUN_0042F840): for index 0 to
		// 164, the index, the UTF-16 length and the UTF-16LE text, with no count,
		// terminator or padding.
		internal static LegacyLauncherMessage InGameTexts(IReadOnlyList<string> texts)
		{
			if (texts == null || texts.Count != InGameTextCount)
			{
				throw new InvalidDataException("Message 15 needs exactly " + InGameTextCount + " strings, not " + (texts?.Count ?? 0) + ".");
			}
			var payload = new List<byte>();
			for (int index = 0; index < texts.Count; index++)
			{
				payload.AddRange(BitConverter.GetBytes(index));
				AddString(payload, Bounded(texts[index], "in-game string " + index));
			}
			return Table(LegacyLauncherMessageId.InGameTexts, payload);
		}

		// Message 14 (writer FUN_0042F720, reader FUN_0042F570): for achievement
		// 0 to 35, the index, then the name, the description and the icon file,
		// each as a UTF-16 length and UTF-16LE text.
		internal static LegacyLauncherMessage AchievementTexts(IReadOnlyList<LegacyAchievementText> achievements)
		{
			if (achievements == null || achievements.Count != AchievementCount)
			{
				throw new InvalidDataException("Message 14 needs exactly " + AchievementCount + " achievements, not " + (achievements?.Count ?? 0) + ".");
			}
			var payload = new List<byte>();
			for (int index = 0; index < achievements.Count; index++)
			{
				LegacyAchievementText achievement = achievements[index] ?? throw new InvalidDataException("Achievement " + index + " is missing.");
				payload.AddRange(BitConverter.GetBytes(index));
				AddString(payload, Bounded(achievement.Name, "achievement name " + index));
				AddString(payload, Bounded(achievement.Description, "achievement description " + index));
				AddString(payload, Bounded(achievement.Icon, "achievement icon " + index));
			}
			return Table(LegacyLauncherMessageId.AchievementTexts, payload);
		}

		// Message 16 (writer FUN_0042F180, reader FUN_0042FA30): the count, then
		// the keyboard binding of OK, Cancel, Menu, Up, Left, Right, Down, Start,
		// Switch, Page Up, Page Down, Target, Assist and Camera. The original
		// launcher reads each binding as one byte; the driver stores them under
		// keys 7 to 20, beside labels 7 to 20 of message 15.
		internal static LegacyLauncherMessage KeyTable(IReadOnlyList<int> keys)
		{
			if (keys == null || keys.Count != KeyTableCount)
			{
				throw new InvalidDataException("Message 16 needs exactly " + KeyTableCount + " bindings, not " + (keys?.Count ?? 0) + ".");
			}
			var payload = new List<byte>(BitConverter.GetBytes(keys.Count));
			foreach (int key in keys)
			{
				if (key < 0 || key > 0xFF)
				{
					throw new InvalidDataException("Keyboard binding " + key + " is not a one-byte key code.");
				}
				payload.AddRange(BitConverter.GetBytes(key));
			}
			return Table(LegacyLauncherMessageId.KeyTable, payload);
		}

		private static LegacyLauncherMessage Table(LegacyLauncherMessageId id, List<byte> payload)
		{
			if (4L + payload.Count > LauncherRegionSize)
			{
				throw new InvalidDataException("Launcher message " + (uint)id + " needs " + (4 + payload.Count) + " bytes, more than the " + LauncherRegionSize + "-byte launcher region.");
			}
			return LegacyLauncherMessage.WithPayload(id, payload.ToArray());
		}

		private static void AddString(List<byte> payload, string text)
		{
			payload.AddRange(BitConverter.GetBytes(text.Length));
			payload.AddRange(Encoding.Unicode.GetBytes(text));
		}

		private static string Bounded(string text, string name)
		{
			if (text == null)
			{
				throw new InvalidDataException("The text for " + name + " is missing.");
			}
			if (text.Length > MaximumTableTextLength)
			{
				throw new InvalidDataException("The text for " + name + " is longer than " + MaximumTableTextLength + " characters.");
			}
			return text;
		}

		// The bytes written at the start of the launcher region: the identifier,
		// then for text messages the UTF-16 length, the text and a NUL.
		internal static byte[] EncodeBytes(LegacyLauncherMessage message)
		{
			if (message == null)
			{
				throw new ArgumentNullException(nameof(message));
			}
			if (message.Payload != null)
			{
				var table = new byte[4 + message.Payload.Length];
				Array.Copy(BitConverter.GetBytes((uint)message.Id), table, 4);
				Array.Copy(message.Payload, 0, table, 4, message.Payload.Length);
				return table;
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
