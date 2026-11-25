using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class SaveMetadataTests
	{
		// MD5("12345"), a published test vector, is the empty-save signature of user_12345.
		private const string EmptySignature12345 = "827ccb0eea8a706c4c34a16891f84e7b";
		private static readonly DateTime FixedNow = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
		private const string FixedNowMilliseconds = "1795608000000";

		internal static void Register(TestRunner runner)
		{
			runner.Add("metadata user ID is the folder name after user_", UserIdComesFromFolderName);
			runner.Add("an empty save is signed with the user ID alone", EmptySaveSignature);
			runner.Add("a save is signed with its bytes followed by the user ID", SaveSignature);
			runner.Add("default metadata matches the original launcher's file byte for byte", DefaultMetadataFormat);
			runner.Add("original-format metadata survives a round trip unchanged", MetadataRoundTrip);
			runner.Add("metadata parsing follows the original launcher's rules", MetadataParsingRules);
			runner.Add("recording a save changes only that save's slot and signature", RecordSaveChangesOnlyOneBlock);
			runner.Add("recording a save keeps the first metadata as a one-time backup", RecordSaveKeepsOneBackup);
			runner.Add("missing metadata is rebuilt with every existing save signed", MissingMetadataSignsExistingSaves);
			runner.Add("unreadable metadata is kept and rebuilt so no save is lost", UnreadableMetadataIsRebuilt);
			runner.Add("re-signing after the game exits follows the final save bytes", ResignFollowsFinalBytes);
			runner.Add("out-of-range save events are ignored", OutOfRangeSaveEventsIgnored);
			runner.Add("the local save folder is created with default metadata", LocalProfileIsCreated);
			runner.Add("creating the local save folder never overwrites existing files", LocalProfileNeverOverwrites);
			runner.Add("the save recorder records saved slots and ignores other messages", SaveRecorderHandlesMessages);
		}

		internal static string Md5Hex(byte[] bytes)
		{
			using (var md5 = MD5.Create())
			{
				return string.Concat(md5.ComputeHash(bytes).Select(value => value.ToString("x2")));
			}
		}

		internal static string ExpectedSignature(byte[] save, string userId)
		{
			return Md5Hex(save.Concat(Encoding.UTF8.GetBytes(userId)).ToArray());
		}

		// The exact text the original launcher writes: QDomNode::save with an
		// indent of 2, LF line ends and empty elements written with end tags.
		internal static string OriginalFormat(Func<int, int, string> timestamp, Func<int, string> signature)
		{
			var builder = new StringBuilder();
			builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gamestatus>\n");
			for (int save = 0; save < 10; save++)
			{
				builder.Append("  <savefiles block=\"").Append(save + 1).Append("\">\n");
				for (int slot = 0; slot < 15; slot++)
				{
					builder.Append("    <timestamp slot=\"").Append(slot + 1).Append("\">").Append(timestamp(save, slot)).Append("</timestamp>\n");
				}
				builder.Append("    <signature>").Append(signature(save)).Append("</signature>\n");
				builder.Append("  </savefiles>\n");
			}
			builder.Append("</gamestatus>\n");
			return builder.ToString();
		}

		private static string Read(string path)
		{
			return Encoding.UTF8.GetString(File.ReadAllBytes(path));
		}

		private static void UserIdComesFromFolderName()
		{
			Check.Equal("891075", LegacySaveMetadata.UserIdFor("user_891075"), "numeric account");
			Check.Equal("local", LegacySaveMetadata.UserIdFor("user_local"), "local folder");
			Check.Equal("a_b", LegacySaveMetadata.UserIdFor("user_a_b"), "only the prefix is removed");
			Check.Equal("save00.ff7", LegacySaveMetadata.SaveFileName(0), "first save file");
			Check.Equal("save09.ff7", LegacySaveMetadata.SaveFileName(9), "last save file");
		}

		private static void EmptySaveSignature()
		{
			Check.Equal(EmptySignature12345, LegacySaveMetadata.Sign(null, "12345"), "missing save");
			Check.Equal(EmptySignature12345, Md5Hex(Encoding.UTF8.GetBytes("12345")), "test vector");
		}

		private static void SaveSignature()
		{
			byte[] save = Enumerable.Range(0, 4340).Select(index => (byte)(index * 13)).ToArray();
			Check.Equal(ExpectedSignature(save, "12345"), LegacySaveMetadata.Sign(save, "12345"), "save signature");
			Check.False(LegacySaveMetadata.Sign(save, "12345") == LegacySaveMetadata.Sign(save, "12346"), "the user ID changes the signature");
		}

		private static void DefaultMetadataFormat()
		{
			string expected = OriginalFormat((save, slot) => string.Empty, save => EmptySignature12345);
			Check.Equal(expected, LegacySaveMetadata.CreateDefault("12345").ToXml(), "default metadata text");
			Check.Equal(183, expected.Split('\n').Length - 1, "line count");
		}

		private static void MetadataRoundTrip()
		{
			string original = OriginalFormat(
				(save, slot) => save == 2 && slot == 4 ? "1700000000123" : string.Empty,
				save => save == 2 ? "0123456789abcdef0123456789abcdef" : EmptySignature12345);
			Check.True(LegacySaveMetadata.TryParse(original, "12345", out LegacySaveMetadata metadata), "parsed");
			Check.Equal("1700000000123", metadata.GetTimestamp(2, 4), "timestamp read");
			Check.Equal("0123456789abcdef0123456789abcdef", metadata.GetSignature(2), "signature read");
			Check.Equal(original, metadata.ToXml(), "unchanged text");
		}

		private static void MetadataParsingRules()
		{
			string valid = OriginalFormat((save, slot) => string.Empty, save => EmptySignature12345);

			// The original reader accepts blocks in any order and any root name.
			string reordered = valid.Replace("block=\"1\"", "block=\"x\"").Replace("block=\"2\"", "block=\"1\"").Replace("block=\"x\"", "block=\"2\"").Replace("gamestatus", "anything");
			Check.True(LegacySaveMetadata.TryParse(reordered, "12345", out _), "reordered blocks");

			// It needs exactly ten savefiles blocks.
			int lastBlock = valid.LastIndexOf("  <savefiles", StringComparison.Ordinal);
			string nine = valid.Substring(0, lastBlock) + "</gamestatus>\n";
			Check.False(LegacySaveMetadata.TryParse(nine, "12345", out _), "nine blocks");
			Check.False(LegacySaveMetadata.TryParse(valid.Replace("slot=\"15\"", "slot=\"16\""), "12345", out _), "slot outside 1 to 15");
			Check.False(LegacySaveMetadata.TryParse(valid.Replace("block=\"10\"", "block=\"11\""), "12345", out _), "block outside 1 to 10");
			Check.False(LegacySaveMetadata.TryParse("<gamestatus><savefiles", "12345", out _), "malformed XML");
			Check.False(LegacySaveMetadata.TryParse("<!DOCTYPE x [<!ENTITY a \"b\">]><gamestatus/>", "12345", out _), "document type definitions are refused");

			// A block without a signature keeps the empty-save signature, as the original reader does.
			string unsigned = valid.Replace("    <signature>" + EmptySignature12345 + "</signature>\n  </savefiles>\n</gamestatus>", "  </savefiles>\n</gamestatus>");
			Check.True(LegacySaveMetadata.TryParse(unsigned, "12345", out LegacySaveMetadata metadata), "missing signature accepted");
			Check.Equal(EmptySignature12345, metadata.GetSignature(9), "default signature");
		}

		private static void RecordSaveChangesOnlyOneBlock()
		{
			using (var scratch = new ScratchDirectory("meta-record"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				byte[] save = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();
				File.WriteAllBytes(Path.Combine(profile, "save03.ff7"), save);
				string original = OriginalFormat(
					(block, slot) => block == 7 && slot == 0 ? "1600000000000" : string.Empty,
					block => block == 7 ? "ffffffffffffffffffffffffffffffff" : EmptySignature12345);
				File.WriteAllBytes(Path.Combine(profile, "metadata.xml"), Encoding.UTF8.GetBytes(original));

				new LegacySaveMetadataStore(profile).RecordSave(3, 14, FixedNow);

				string expected = OriginalFormat(
					(block, slot) => block == 7 && slot == 0 ? "1600000000000" : block == 3 && slot == 14 ? FixedNowMilliseconds : string.Empty,
					block => block == 7 ? "ffffffffffffffffffffffffffffffff" : block == 3 ? ExpectedSignature(save, "12345") : EmptySignature12345);
				Check.Equal(expected, Read(Path.Combine(profile, "metadata.xml")), "updated metadata");
				Check.Bytes(save, File.ReadAllBytes(Path.Combine(profile, "save03.ff7")), "save untouched");
			}
		}

		private static void RecordSaveKeepsOneBackup()
		{
			using (var scratch = new ScratchDirectory("meta-backup"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				byte[] original = Encoding.UTF8.GetBytes(OriginalFormat((block, slot) => string.Empty, block => EmptySignature12345));
				File.WriteAllBytes(Path.Combine(profile, "metadata.xml"), original);
				File.WriteAllBytes(Path.Combine(profile, "save00.ff7"), new byte[] { 1 });
				var store = new LegacySaveMetadataStore(profile);
				store.RecordSave(0, 0, FixedNow);
				File.WriteAllBytes(Path.Combine(profile, "save00.ff7"), new byte[] { 2 });
				store.RecordSave(0, 1, FixedNow.AddMinutes(1));
				Check.Bytes(original, File.ReadAllBytes(Path.Combine(profile, "metadata.xml.blind-soldier-original")), "first metadata kept");
				Check.Sequence(new[] { "metadata.xml", "metadata.xml.blind-soldier-original", "save00.ff7" },
					Directory.GetFiles(profile).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal), "no temporary files");
				Check.True(LegacySaveMetadata.TryParse(Read(Path.Combine(profile, "metadata.xml")), "12345", out LegacySaveMetadata metadata), "valid");
				Check.Equal(FixedNowMilliseconds, metadata.GetTimestamp(0, 0), "first slot");
				Check.Equal("1795608060000", metadata.GetTimestamp(0, 1), "second slot");
				Check.Equal(ExpectedSignature(new byte[] { 2 }, "12345"), metadata.GetSignature(0), "latest bytes signed");
			}
		}

		private static void MissingMetadataSignsExistingSaves()
		{
			using (var scratch = new ScratchDirectory("meta-missing"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				File.WriteAllBytes(Path.Combine(profile, "save01.ff7"), new byte[] { 9, 9 });
				File.WriteAllBytes(Path.Combine(profile, "save05.ff7"), new byte[] { 5 });

				new LegacySaveMetadataStore(profile).RecordSave(5, 2, FixedNow);

				string expected = OriginalFormat(
					(block, slot) => block == 5 && slot == 2 ? FixedNowMilliseconds : string.Empty,
					block => block == 1 ? ExpectedSignature(new byte[] { 9, 9 }, "12345")
						: block == 5 ? ExpectedSignature(new byte[] { 5 }, "12345")
						: EmptySignature12345);
				Check.Equal(expected, Read(Path.Combine(profile, "metadata.xml")), "rebuilt metadata");
				Check.False(File.Exists(Path.Combine(profile, "metadata.xml.blind-soldier-original")), "nothing to back up");
			}
		}

		private static void UnreadableMetadataIsRebuilt()
		{
			using (var scratch = new ScratchDirectory("meta-bad"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				byte[] broken = Encoding.UTF8.GetBytes("<gamestatus><savefiles block=\"1\">");
				File.WriteAllBytes(Path.Combine(profile, "metadata.xml"), broken);
				File.WriteAllBytes(Path.Combine(profile, "save02.ff7"), new byte[] { 2 });

				new LegacySaveMetadataStore(profile).RecordSave(0, 0, FixedNow);

				Check.Bytes(broken, File.ReadAllBytes(Path.Combine(profile, "metadata.xml.blind-soldier-original")), "unreadable file kept");
				Check.True(LegacySaveMetadata.TryParse(Read(Path.Combine(profile, "metadata.xml")), "12345", out LegacySaveMetadata metadata), "valid replacement");
				Check.Equal(ExpectedSignature(new byte[] { 2 }, "12345"), metadata.GetSignature(2), "existing save signed");
				Check.Equal(EmptySignature12345, metadata.GetSignature(0), "missing save signed as empty");
				Check.Equal(FixedNowMilliseconds, metadata.GetTimestamp(0, 0), "slot recorded");
			}
		}

		private static void ResignFollowsFinalBytes()
		{
			using (var scratch = new ScratchDirectory("meta-resign"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				File.WriteAllBytes(Path.Combine(profile, "save04.ff7"), new byte[] { 1 });
				var store = new LegacySaveMetadataStore(profile);
				store.RecordSave(4, 3, FixedNow);
				File.WriteAllBytes(Path.Combine(profile, "save04.ff7"), new byte[] { 1, 2, 3 });
				store.Resign(new[] { 4 });
				Check.True(LegacySaveMetadata.TryParse(Read(Path.Combine(profile, "metadata.xml")), "12345", out LegacySaveMetadata metadata), "valid");
				Check.Equal(ExpectedSignature(new byte[] { 1, 2, 3 }, "12345"), metadata.GetSignature(4), "final bytes signed");
				Check.Equal(FixedNowMilliseconds, metadata.GetTimestamp(4, 3), "timestamp kept");
			}
		}

		private static void OutOfRangeSaveEventsIgnored()
		{
			using (var scratch = new ScratchDirectory("meta-range"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				var store = new LegacySaveMetadataStore(profile);
				Check.Throws<ArgumentOutOfRangeException>(() => store.RecordSave(10, 0, FixedNow), "save 11");
				Check.Throws<ArgumentOutOfRangeException>(() => store.RecordSave(0, 15, FixedNow), "slot 16");
				Check.Throws<ArgumentOutOfRangeException>(() => store.RecordSave(-1, 0, FixedNow), "negative save");
				Check.False(File.Exists(Path.Combine(profile, "metadata.xml")), "nothing written");
			}
		}

		private static void LocalProfileIsCreated()
		{
			using (var game = new LegacyGameTree("local-create"))
			{
				Check.False(Directory.Exists(game.DocumentDirectory), "no documents yet");
				LegacySaveProfile profile = LegacySaveProfiles.CreateLocal(game.DocumentDirectory);
				Check.Equal("user_local", profile.Name, "name");
				Check.Equal(Path.Combine(game.DocumentDirectory, "user_local"), profile.FullPath, "path");
				Check.Equal(0, profile.SaveFileCount, "no saves");
				Check.Equal(LegacySaveMetadata.CreateDefault("local").ToXml(), Read(Path.Combine(profile.FullPath, "metadata.xml")), "default metadata");
				Check.Sequence(new[] { "metadata.xml" }, Directory.GetFiles(profile.FullPath).Select(Path.GetFileName), "only metadata");
				Check.Sequence(new[] { "user_local" }, LegacySaveProfiles.Find(game.DocumentDirectory).Select(found => found.Name), "found afterwards");
				Check.Equal(LegacySaveMetadata.Sign(null, "local"), LegacySaveMetadata.CreateDefault("local").GetSignature(0), "signed with its own ID");
			}
		}

		private static void LocalProfileNeverOverwrites()
		{
			using (var game = new LegacyGameTree("local-keep"))
			{
				string profile = game.CreateProfile("user_local", "save00.ff7");
				byte[] metadata = Encoding.UTF8.GetBytes("keep me");
				File.WriteAllBytes(Path.Combine(profile, "metadata.xml"), metadata);
				byte[] save = File.ReadAllBytes(Path.Combine(profile, "save00.ff7"));
				LegacySaveProfile created = LegacySaveProfiles.CreateLocal(game.DocumentDirectory);
				Check.Equal(1, created.SaveFileCount, "existing save counted");
				Check.Bytes(metadata, File.ReadAllBytes(Path.Combine(profile, "metadata.xml")), "metadata kept");
				Check.Bytes(save, File.ReadAllBytes(Path.Combine(profile, "save00.ff7")), "save kept");
				Check.Equal(2, Directory.GetFiles(profile).Length, "nothing added");
			}
		}

		private static void SaveRecorderHandlesMessages()
		{
			using (var scratch = new ScratchDirectory("meta-recorder"))
			{
				string profile = scratch.Combine("user_12345");
				Directory.CreateDirectory(profile);
				File.WriteAllBytes(Path.Combine(profile, "save06.ff7"), new byte[] { 6 });
				var log = new List<string>();
				var recorder = new LegacySaveRecorder(new LegacySaveMetadataStore(profile), () => FixedNow, entry => { lock (log) { log.Add(entry); } });
				recorder.Handle(new LegacyGameMessage(1, new[] { 17 }));
				recorder.Handle(new LegacyGameMessage(3, new[] { 1, 6, 2 }));
				Check.False(File.Exists(Path.Combine(profile, "metadata.xml")), "loads and achievements write nothing");
				recorder.Handle(new LegacyGameMessage(3, new[] { 0, 6, 2 }));
				recorder.Handle(new LegacyGameMessage(3, new[] { 0, 12, 2 }));
				Check.Sequence(new[] { "6" }, recorder.SavedFiles.Select(index => index.ToString()), "saved files");
				File.WriteAllBytes(Path.Combine(profile, "save06.ff7"), new byte[] { 6, 6 });
				recorder.Complete();
				Check.True(LegacySaveMetadata.TryParse(Read(Path.Combine(profile, "metadata.xml")), "12345", out LegacySaveMetadata metadata), "valid");
				Check.Equal(FixedNowMilliseconds, metadata.GetTimestamp(6, 2), "slot recorded");
				Check.Equal(ExpectedSignature(new byte[] { 6, 6 }, "12345"), metadata.GetSignature(6), "final bytes signed");
				Check.True(log.Any(entry => entry.Contains("save06.ff7") && entry.Contains("slot 3")), "save logged: " + string.Join(" | ", log));
				Check.True(log.Any(entry => entry.Contains("12")), "invalid save index logged: " + string.Join(" | ", log));
			}
		}
	}
}
