using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	// Writes Qt 4 .qm translation files and strings.dat the way the original
	// 2013 install stores them, independently of the launcher's reader: the
	// 16-byte magic, a hash section (0x42) and a message section (0x69) whose
	// entries hold a UTF-16BE translation (tag 3), the source text (6), the
	// context (7) and a comment (8), each ended by tag 1. strings.dat is a
	// little-endian count, then (language id, size) pairs, then the files.
	internal static class NativeFixtures
	{
		internal static readonly byte[] QmMagic = { 0x3C, 0xB8, 0x64, 0x18, 0xCA, 0xEF, 0x9C, 0x95, 0xCD, 0x21, 0x1C, 0xBF, 0x60, 0xA1, 0xBD, 0xDD };

		internal static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

		internal sealed class Entry
		{
			internal Entry(string context, string source, string translation)
			{
				Context = context;
				Source = Latin1.GetBytes(source);
				Translation = translation;
			}

			internal string Context { get; }

			internal byte[] Source { get; set; }

			internal string Translation { get; }
		}

		internal static byte[] Qm(IEnumerable<Entry> entries)
		{
			var messages = new MemoryStream();
			foreach (Entry entry in entries)
			{
				Tag(messages, 3, Encoding.BigEndianUnicode.GetBytes(entry.Translation));
				Tag(messages, 8, new byte[0]);
				Tag(messages, 6, entry.Source);
				Tag(messages, 7, Encoding.ASCII.GetBytes(entry.Context));
				messages.WriteByte(1);
			}
			var file = new MemoryStream();
			file.Write(QmMagic, 0, QmMagic.Length);
			Section(file, 0x42, new byte[0]);
			Section(file, 0x69, messages.ToArray());
			Section(file, 0x88, new byte[] { 1, 2 });
			return file.ToArray();
		}

		internal static byte[] StringsDat(IEnumerable<KeyValuePair<int, byte[]>> blocks)
		{
			var list = blocks.ToList();
			var file = new MemoryStream();
			Int(file, list.Count);
			foreach (var block in list)
			{
				Int(file, block.Key);
				Int(file, block.Value.Length);
			}
			foreach (var block in list)
			{
				file.Write(block.Value, 0, block.Value.Length);
			}
			return file.ToArray();
		}

		// The game versions strings.dat holds for FF7Launcher's GAME_VERSION.
		internal static readonly string[] Languages = { "en", "fr", "de", "es" };

		internal static readonly string[] GameVersions = { "v1.0.9 English Steam", "v1.0.9 Français Steam", "v1.0.9 Deutsch Steam", "v1.0.9 Español Steam" };

		internal static readonly string[] AchievementUnlocked = { "Achievement Unlocked", "Succès débloqué", "Errungenschaft freigeschaltet", "Logro desbloqueado" };

		// A small strings.dat for every language: the game version, the first
		// notice, one key name and the first achievement are translated.
		internal static byte[] StandardStringsDat()
		{
			return StringsDat(Enumerable.Range(0, 4).Select(index => new KeyValuePair<int, byte[]>(1000 + index, Qm(new[]
			{
				new Entry("FF7Launcher", "GAME_VERSION", GameVersions[index]),
				new Entry("QObject", "Achievement Unlocked", AchievementUnlocked[index]),
				new Entry("QObject", "DIK_ESCAPE", Languages[index] == "fr" ? "ECHAP" : "Esc"),
				new Entry("QObject", "End of Part I", "Part I " + Languages[index]),
				new Entry("QObject", "Complete the first part of the game", "First part " + Languages[index])
			}))));
		}

		private static void Tag(Stream stream, byte tag, byte[] value)
		{
			stream.WriteByte(tag);
			BigEndian(stream, value.Length);
			stream.Write(value, 0, value.Length);
		}

		private static void Section(Stream stream, byte tag, byte[] value)
		{
			Tag(stream, tag, value);
		}

		private static void BigEndian(Stream stream, int value)
		{
			stream.WriteByte((byte)(value >> 24));
			stream.WriteByte((byte)(value >> 16));
			stream.WriteByte((byte)(value >> 8));
			stream.WriteByte((byte)value);
		}

		private static void Int(Stream stream, int value)
		{
			byte[] bytes = BitConverter.GetBytes(value);
			stream.Write(bytes, 0, 4);
		}
	}

	// Reads launcher messages 14, 15 and 16 the way the original driver's
	// readers do (FUN_0042F570, FUN_0042F840, FUN_0042FA30 in the shared
	// protocol code): fixed 36 and 165 record lists with no count, and a
	// counted integer list.
	internal static class NativeReaders
	{
		internal const char Separator = '\u001F';
		internal const char FieldSeparator = '\u001E';

		internal static string Read(uint id, Func<int, int> readInt, Func<int, int, string> readChars, int payloadOffset, int regionLength)
		{
			int position = payloadOffset;
			string Text()
			{
				int length = readInt(position);
				if (length < 0 || position + 4 + 2L * length > regionLength)
				{
					throw new InvalidDataException("string past the launcher region");
				}
				string value = readChars(position + 4, length);
				position += 4 + 2 * length;
				return value;
			}

			switch (id)
			{
				case 15:
				{
					var values = new List<string>();
					for (int index = 0; index < 165; index++)
					{
						int key = readInt(position);
						position += 4;
						if (key != index)
						{
							throw new InvalidDataException("record " + index + " has key " + key);
						}
						values.Add(Text());
					}
					return "15:" + string.Join(Separator.ToString(), values);
				}
				case 14:
				{
					var values = new List<string>();
					for (int index = 0; index < 36; index++)
					{
						int key = readInt(position);
						position += 4;
						if (key != index)
						{
							throw new InvalidDataException("record " + index + " has key " + key);
						}
						values.Add(Text() + FieldSeparator + Text() + FieldSeparator + Text());
					}
					return "14:" + string.Join(Separator.ToString(), values);
				}
				case 16:
				{
					int count = readInt(position);
					if (count < 0 || count > 64)
					{
						throw new InvalidDataException("key count " + count);
					}
					var values = new List<string>();
					for (int index = 0; index < count; index++)
					{
						values.Add(readInt(position + 4 + 4 * index).ToString("X2"));
					}
					return "16:" + string.Join(",", values);
				}
				default:
					throw new ArgumentOutOfRangeException(nameof(id));
			}
		}

		internal static string Read(byte[] message)
		{
			uint id = BitConverter.ToUInt32(message, 0);
			return Read(id, offset => BitConverter.ToInt32(message, offset),
				(offset, length) => Encoding.Unicode.GetString(message, offset, 2 * length), 4, message.Length);
		}

		internal static string[] Strings(string recorded)
		{
			return recorded.Substring(3).Split(Separator);
		}

		// The handshake transcript with the long table messages shortened.
		internal static string Brief(string recorded)
		{
			if (recorded.StartsWith("15:", StringComparison.Ordinal) || recorded.StartsWith("14:", StringComparison.Ordinal))
			{
				return recorded.Substring(0, 3) + Strings(recorded).Length + " records";
			}
			return recorded;
		}
	}
}
