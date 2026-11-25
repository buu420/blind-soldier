using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BlindSoldier.Ff7Launcher2013
{
	internal sealed class LegacyAchievementText
	{
		internal LegacyAchievementText(string name, string description, string icon)
		{
			Name = name ?? throw new ArgumentNullException(nameof(name));
			Description = description ?? throw new ArgumentNullException(nameof(description));
			Icon = icon ?? throw new ArgumentNullException(nameof(icon));
		}

		internal string Name { get; }

		internal string Description { get; }

		internal string Icon { get; }
	}

	// A Qt 4 .qm translation file, as QTranslator reads it: a 16-byte magic,
	// then tagged sections. The message section (0x69) holds entries of
	// translation (tag 3, UTF-16BE), source text (6), context (7) and comment
	// (8), each ended by tag 1. Lookups compare the source text's raw bytes, as
	// QObject::tr does with the Latin-1 literals of the original launcher.
	internal sealed class QtTranslations
	{
		private static readonly byte[] Magic = { 0x3C, 0xB8, 0x64, 0x18, 0xCA, 0xEF, 0x9C, 0x95, 0xCD, 0x21, 0x1C, 0xBF, 0x60, 0xA1, 0xBD, 0xDD };
		private const byte MessagesSection = 0x69;
		private const int MaximumMessages = 100000;
		private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

		private readonly Dictionary<string, string> translations;

		private QtTranslations(Dictionary<string, string> translations)
		{
			this.translations = translations;
		}

		internal int Count => translations.Count;

		internal static QtTranslations Parse(byte[] qm)
		{
			if (qm == null || qm.Length < Magic.Length || !qm.Take(Magic.Length).SequenceEqual(Magic))
			{
				throw new InvalidDataException("The translation file does not start with the Qt translation magic.");
			}
			int position = Magic.Length;
			byte[] messages = null;
			while (position < qm.Length)
			{
				if (qm.Length - position < 5)
				{
					throw new InvalidDataException("A translation section header is cut short.");
				}
				byte tag = qm[position];
				long length = BigEndian(qm, position + 1);
				position += 5;
				if (length > qm.Length - position)
				{
					throw new InvalidDataException("Translation section 0x" + tag.ToString("X2") + " runs past the end of the file.");
				}
				if (tag == MessagesSection)
				{
					messages = new byte[length];
					Array.Copy(qm, position, messages, 0, (int)length);
				}
				position += (int)length;
			}
			if (messages == null)
			{
				throw new InvalidDataException("The translation file has no message section.");
			}
			return new QtTranslations(ParseMessages(messages));
		}

		// The translation for an untranslated QObject::tr call, or null.
		internal string Translate(string context, string source)
		{
			return translations.TryGetValue(Key(Latin1.GetBytes(context), Latin1.GetBytes(source)), out string value) ? value : null;
		}

		private static Dictionary<string, string> ParseMessages(byte[] data)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			int position = 0;
			byte[] translation = null, source = null, context = null, comment = null;
			int count = 0;
			while (position < data.Length)
			{
				byte tag = data[position++];
				switch (tag)
				{
					case 1:
						if (++count > MaximumMessages)
						{
							throw new InvalidDataException("The translation file has too many messages.");
						}
						// Only entries without a disambiguating comment answer tr().
						if (translation != null && source != null && context != null && (comment == null || comment.Length == 0))
						{
							string key = Key(context, source);
							if (!result.ContainsKey(key))
							{
								result.Add(key, Encoding.BigEndianUnicode.GetString(translation));
							}
						}
						translation = source = context = comment = null;
						break;
					case 3:
						translation = Field(data, ref position, allowNull: true);
						if (translation != null && translation.Length % 2 != 0)
						{
							throw new InvalidDataException("A translation has an odd UTF-16 length.");
						}
						break;
					case 5:
						// Obsolete tag with a 32-bit value.
						if (data.Length - position < 4)
						{
							throw new InvalidDataException("A translation entry is cut short.");
						}
						position += 4;
						break;
					case 6:
						source = Field(data, ref position, allowNull: false);
						break;
					case 7:
						context = Field(data, ref position, allowNull: false);
						break;
					case 8:
						comment = Field(data, ref position, allowNull: false);
						break;
					default:
						throw new InvalidDataException("Unknown translation entry tag 0x" + tag.ToString("X2") + ".");
				}
			}
			return result;
		}

		private static byte[] Field(byte[] data, ref int position, bool allowNull)
		{
			if (data.Length - position < 4)
			{
				throw new InvalidDataException("A translation entry is cut short.");
			}
			long length = BigEndian(data, position);
			position += 4;
			if (length == 0xFFFFFFFFL && allowNull)
			{
				return null;
			}
			if (length > data.Length - position)
			{
				throw new InvalidDataException("A translation entry runs past its section.");
			}
			var value = new byte[length];
			Array.Copy(data, position, value, 0, (int)length);
			position += (int)length;
			return value;
		}

		private static long BigEndian(byte[] data, int offset)
		{
			return (long)data[offset] << 24 | (long)data[offset + 1] << 16 | (long)data[offset + 2] << 8 | data[offset + 3];
		}

		private static string Key(byte[] context, byte[] source)
		{
			return Latin1.GetString(context) + "\0" + Latin1.GetString(source);
		}
	}

	// strings.dat in the 2013 game folder: a little-endian count, then a
	// (language id, size) pair for each block, then the blocks in that order.
	// Each block is a Qt translation file; 1000 to 1003 are English, French,
	// German and Spanish, which the original launcher installs as its
	// translator (FF7_Launcher.exe main).
	internal static class LegacyStringsDat
	{
		internal const string FileName = "strings.dat";
		internal const long MaximumLength = 16L * 1024 * 1024;
		private const int MaximumBlocks = 16;
		private static readonly string[] Languages = { "en", "fr", "de", "es" };

		internal static byte[] Block(byte[] file, string languageCode)
		{
			int language = Array.IndexOf(Languages, languageCode);
			if (language < 0)
			{
				throw new ArgumentException("strings.dat has no " + (languageCode ?? "null") + " block.", nameof(languageCode));
			}
			if (file == null || file.Length < 4)
			{
				throw new InvalidDataException("strings.dat is too short.");
			}
			int count = BitConverter.ToInt32(file, 0);
			if (count < 1 || count > MaximumBlocks || 4L + 8L * count > file.Length)
			{
				throw new InvalidDataException("strings.dat lists " + count + " blocks.");
			}
			long offset = 4L + 8L * count;
			for (int index = 0; index < count; index++)
			{
				int id = BitConverter.ToInt32(file, 4 + 8 * index);
				int size = BitConverter.ToInt32(file, 8 + 8 * index);
				if (size < 1 || offset + size > file.Length)
				{
					throw new InvalidDataException("strings.dat block " + id + " does not fit the file.");
				}
				if (id == 1000 + language)
				{
					var block = new byte[size];
					Array.Copy(file, offset, block, 0, size);
					return block;
				}
				offset += size;
			}
			throw new InvalidDataException("strings.dat has no " + languageCode + " translations.");
		}
	}

	// The texts the original launcher sends the original driver: 165 in-game
	// strings (message 15), three strings for each of 36 achievements (message
	// 14) and its GAME_VERSION (message 18). Each is QObject::tr of the source
	// text, or QMetaObject::tr for FF7Launcher's GAME_VERSION, so an entry
	// without a translation keeps its source text, exactly as Qt returns it.
	internal sealed class LegacyNativeTexts
	{
		private LegacyNativeTexts(IReadOnlyList<string> inGame, IReadOnlyList<LegacyAchievementText> achievements, string gameVersion)
		{
			InGame = inGame;
			Achievements = achievements;
			GameVersion = gameVersion;
		}

		internal IReadOnlyList<string> InGame { get; }

		internal IReadOnlyList<LegacyAchievementText> Achievements { get; }

		internal string GameVersion { get; }

		internal static LegacyNativeTexts Load(string gameRoot, string languageCode)
		{
			string path = Path.Combine(gameRoot, LegacyStringsDat.FileName);
			if (!File.Exists(path))
			{
				throw new FileNotFoundException("strings.dat is missing from the game folder.", path);
			}
			if (new FileInfo(path).Length > LegacyStringsDat.MaximumLength)
			{
				throw new InvalidDataException("strings.dat is larger than any 2013 edition's.");
			}
			return From(QtTranslations.Parse(LegacyStringsDat.Block(File.ReadAllBytes(path), languageCode)));
		}

		internal static LegacyNativeTexts From(QtTranslations translations)
		{
			if (translations == null)
			{
				throw new ArgumentNullException(nameof(translations));
			}
			string Tr(string context, string source) => translations.Translate(context, source) ?? source;
			string[] inGame = LegacyNativeTextSources.InGame.Select(source => Tr("QObject", source)).ToArray();
			var achievements = new LegacyAchievementText[LegacyNativeTextSources.AchievementNames.Length];
			for (int index = 0; index < achievements.Length; index++)
			{
				achievements[index] = new LegacyAchievementText(
					Tr("QObject", LegacyNativeTextSources.AchievementNames[index]),
					Tr("QObject", LegacyNativeTextSources.AchievementDescriptions[index]),
					LegacyNativeTextSources.AchievementIcons[index]);
			}
			return new LegacyNativeTexts(inGame, achievements, Tr("FF7Launcher", "GAME_VERSION"));
		}
	}
}
