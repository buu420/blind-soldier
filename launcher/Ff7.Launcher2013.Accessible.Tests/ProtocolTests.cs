using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class ProtocolTests
	{
		internal static void Register(TestRunner runner)
		{
			runner.Add("object names match the 2013 driver contract", ObjectNamesMatchContract);
			runner.Add("startup sequence matches the FF78Launcher order and payloads", StartupSequenceMatchesContract);
			runner.Add("startup sequence rejects unsupported language codes", StartupSequenceRejectsLanguage);
			runner.Add("text messages encode identifier, length and UTF-16 payload", TextMessageEncoding);
			runner.Add("identifier-only messages leave the previous length untouched", IdentifierOnlyEncoding);
			runner.Add("oversized payloads are rejected", OversizedPayloadRejected);
			runner.Add("message 15 is exactly 165 indexed UTF-16 records with no count", InGameTextsEncoding);
			runner.Add("message 14 is exactly 36 records of name, description and icon", AchievementTextsEncoding);
			runner.Add("message 16 is a count of 14 keyboard bindings", KeyTableEncoding);
			runner.Add("table messages refuse wrong counts, long strings and region overflow", TableMessagesRefuseBadData);
			runner.Add("the start-up sequence follows the original launcher's order", StartupSequenceFollowsOriginalOrder);
			runner.Add("the channel refuses existing communication objects", ChannelRefusesExistingObjects);
			runner.Add("a reading game receives every message and is acknowledged", ReadingGameReceivesMessages);
			runner.Add("sending stops when the game has exited", SendingStopsWhenGameExited);
			runner.Add("sending times out when nothing reads", SendingTimesOut);
			runner.Add("game messages decode their identifier and integer payloads", GameMessagesDecode);
			runner.Add("game messages reach the launcher after they are acknowledged", GameMessagesReachLauncher);
			runner.Add("a failing game message handler never stops acknowledgements", FailingHandlerKeepsAcknowledging);
		}

		internal static string UniquePrefix()
		{
			return "bstest" + Guid.NewGuid().ToString("N").Substring(0, 10) + "_ff7";
		}

		private static void ObjectNamesMatchContract()
		{
			Check.Equal("ff7_gameCanReadMsgSem", LegacyLauncherProtocol.GameCanReadName("ff7"), "game can read");
			Check.Equal("ff7_gameDidReadMsgSem", LegacyLauncherProtocol.GameDidReadName("ff7"), "game did read");
			Check.Equal("ff7_launcherCanReadMsgSem", LegacyLauncherProtocol.LauncherCanReadName("ff7"), "launcher can read");
			Check.Equal("ff7_launcherDidReadMsgSem", LegacyLauncherProtocol.LauncherDidReadName("ff7"), "launcher did read");
			Check.Equal("ff7_sharedMemoryWithLauncher", LegacyLauncherProtocol.SharedMemoryName("ff7"), "shared memory");
		}

		// The keyboard bindings of the original launcher's default file, in the
		// order its message 16 lists them: OK, Cancel, Menu, Up, Left, Right,
		// Down, Start, Switch, Page Up, Page Down, Target, Assist, Camera.
		internal static readonly int[] OfficialKeys = { 0x2D, 0x2E, 0x2F, 0xC8, 0xCB, 0xCD, 0xD0, 0x30, 0xD2, 0xC9, 0xD1, 0xCF, 0xD3, 0xC7 };

		internal static LegacyNativeTexts Texts(string gameVersion)
		{
			return LegacyNativeTexts.From(QtTranslations.Parse(NativeFixtures.Qm(new[]
			{
				new NativeFixtures.Entry("FF7Launcher", "GAME_VERSION", gameVersion),
				new NativeFixtures.Entry("QObject", "Keyboard", "Clavier")
			})));
		}

		private static void StartupSequenceMatchesContract()
		{
			var sequence = LegacyLauncherProtocol.BuildStartupSequence(
				"fr",
				@"D:\Docs\Square Enix\FINAL FANTASY VII Steam\user_42",
				@"D:\Docs\Square Enix\FINAL FANTASY VII Steam",
				@"E:\Steam\steamapps\common\FINAL FANTASY VII",
				Texts("v1.0.9 Fran\u00E7ais Steam"),
				OfficialKeys);
			Check.Sequence(new[]
			{
				"13:lang-fr\0",
				"15:165 records",
				"16:2D,2E,2F,C8,CB,CD,D0,30,D2,C9,D1,CF,D3,C7",
				@"10:D:\Docs\Square Enix\FINAL FANTASY VII Steam\user_42",
				@"11:D:\Docs\Square Enix\FINAL FANTASY VII Steam",
				@"12:E:\Steam\steamapps\common\FINAL FANTASY VII",
				"14:36 records",
				"18:v1.0.9 Fran\u00E7ais Steam",
				"22:<none>",
				"24:<none>"
			}, sequence.Select(Describe).Select(NativeReaders.Brief), "startup sequence");
		}

		private static void StartupSequenceFollowsOriginalOrder()
		{
			// FUN_00413F90 sends 13, 15, 16, 10, 11, 12, 14, 18, the cloud choice
			// and 24. Without verified bindings message 16 is left out.
			var withKeys = LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", Texts("V"), OfficialKeys);
			Check.Sequence(new[] { "13", "15", "16", "10", "11", "12", "14", "18", "22", "24" }, withKeys.Select(message => ((uint)message.Id).ToString()), "with key table");
			var withoutKeys = LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", Texts("V"), null);
			Check.Sequence(new[] { "13", "15", "10", "11", "12", "14", "18", "22", "24" }, withoutKeys.Select(message => ((uint)message.Id).ToString()), "without key table");
			string[] strings = NativeReaders.Strings(Describe(withKeys[1]));
			Check.Equal("Clavier", strings[5], "translated string 5");
			Check.Equal("DIK_ESCAPE", strings[21], "untranslated string keeps its source");
		}

		private static void InGameTextsEncoding()
		{
			string[] texts = Enumerable.Range(0, 165).Select(index => "s" + index).ToArray();
			texts[1] = "\u00E9";
			texts[2] = string.Empty;
			byte[] bytes = LegacyLauncherProtocol.EncodeBytes(LegacyLauncherProtocol.InGameTexts(texts));
			Check.Bytes(new byte[]
			{
				0x0F, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x73, 0x00, 0x30, 0x00,
				0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xE9, 0x00,
				0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x03, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00
			}, bytes.Take(42).ToArray(), "first records");
			int expected = 4 + texts.Sum(text => 8 + 2 * text.Length);
			Check.Equal(expected, bytes.Length, "no count, terminator or padding");
			Check.Sequence(texts, NativeReaders.Strings(NativeReaders.Read(bytes)), "read back as the driver does");
		}

		private static void AchievementTextsEncoding()
		{
			var items = Enumerable.Range(0, 36).Select(index => new LegacyAchievementText("n" + index, "d" + index, index + ".fgt")).ToArray();
			byte[] bytes = LegacyLauncherProtocol.EncodeBytes(LegacyLauncherProtocol.AchievementTexts(items));
			Check.Bytes(new byte[]
			{
				0x0E, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x02, 0x00, 0x00, 0x00, 0x6E, 0x00, 0x30, 0x00,
				0x02, 0x00, 0x00, 0x00, 0x64, 0x00, 0x30, 0x00,
				0x05, 0x00, 0x00, 0x00, 0x30, 0x00, 0x2E, 0x00, 0x66, 0x00, 0x67, 0x00, 0x74, 0x00,
				0x01, 0x00, 0x00, 0x00
			}, bytes.Take(42).ToArray(), "first record");
			Check.Equal(4 + items.Sum(item => 16 + 2 * (item.Name.Length + item.Description.Length + item.Icon.Length)), bytes.Length, "exact length");
			string[] records = NativeReaders.Strings(NativeReaders.Read(bytes));
			Check.Equal(36, records.Length, "36 records");
			Check.Equal("n35\u001Ed35\u001E35.fgt", records[35], "last record");
		}

		private static void KeyTableEncoding()
		{
			byte[] bytes = LegacyLauncherProtocol.EncodeBytes(LegacyLauncherProtocol.KeyTable(OfficialKeys));
			Check.Bytes(new byte[] { 0x10, 0, 0, 0, 0x0E, 0, 0, 0, 0x2D, 0, 0, 0, 0x2E, 0, 0, 0 }, bytes.Take(16).ToArray(), "count then values");
			Check.Equal(4 + 4 + 14 * 4, bytes.Length, "fourteen values");
			Check.Equal("16:2D,2E,2F,C8,CB,CD,D0,30,D2,C9,D1,CF,D3,C7", NativeReaders.Read(bytes), "read back");
		}

		private static void TableMessagesRefuseBadData()
		{
			string[] texts = Enumerable.Repeat("x", 165).ToArray();
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.InGameTexts(texts.Take(164).ToArray()), "164 strings");
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.InGameTexts(texts.Concat(new[] { "x" }).ToArray()), "166 strings");
			string[] withNull = (string[])texts.Clone();
			withNull[3] = null;
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.InGameTexts(withNull), "missing string");
			string[] tooLong = (string[])texts.Clone();
			tooLong[9] = new string('x', LegacyLauncherProtocol.MaximumTableTextLength + 1);
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.InGameTexts(tooLong), "string over the limit");
			// Within the per-string limit but past the launcher's 64 KB region.
			string[] large = Enumerable.Repeat(new string('x', 300), 165).ToArray();
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.InGameTexts(large), "region overflow");
			var items = Enumerable.Range(0, 35).Select(index => new LegacyAchievementText("n", "d", "i")).ToArray();
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.AchievementTexts(items), "35 achievements");
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.KeyTable(OfficialKeys.Take(13).ToArray()), "13 bindings");
			int[] wide = (int[])OfficialKeys.Clone();
			wide[0] = 0x100;
			Check.Throws<InvalidDataException>(() => LegacyLauncherProtocol.KeyTable(wide), "binding above a byte");
		}

		private static void StartupSequenceRejectsLanguage()
		{
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.BuildStartupSequence("ja", "a", "b", "c", Texts("V"), OfficialKeys), "Japanese");
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.BuildStartupSequence("", "a", "b", "c", Texts("V"), OfficialKeys), "empty");
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.ValidateStartupPaths("en", "a", "", "c"), "missing documents folder");
		}

		private static void TextMessageEncoding()
		{
			byte[] region = Filled(24, 0xCC);
			LegacyLauncherProtocol.Encode(new LegacyLauncherMessage(LegacyLauncherMessageId.InstallDirectory, "AB"), region);
			Check.Bytes(new byte[]
			{
				0x0C, 0x00, 0x00, 0x00,
				0x02, 0x00, 0x00, 0x00,
				0x41, 0x00, 0x42, 0x00,
				0x00, 0x00,
				0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC
			}, region, "encoded text message");

			byte[] locale = Filled(28, 0xCC);
			LegacyLauncherProtocol.Encode(new LegacyLauncherMessage(LegacyLauncherMessageId.LocaleDataDirectory, "lang-en\0"), locale);
			Check.Bytes(new byte[]
			{
				0x0D, 0x00, 0x00, 0x00,
				0x08, 0x00, 0x00, 0x00,
				0x6C, 0x00, 0x61, 0x00, 0x6E, 0x00, 0x67, 0x00, 0x2D, 0x00, 0x65, 0x00, 0x6E, 0x00, 0x00, 0x00,
				0x00, 0x00,
				0xCC, 0xCC
			}, locale, "encoded locale message");
		}

		private static void IdentifierOnlyEncoding()
		{
			byte[] region = Filled(12, 0xCC);
			LegacyLauncherProtocol.Encode(new LegacyLauncherMessage(LegacyLauncherMessageId.DisableCloud, null), region);
			Check.Bytes(new byte[] { 0x16, 0x00, 0x00, 0x00, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC }, region, "encoded disable cloud");
			LegacyLauncherProtocol.Encode(new LegacyLauncherMessage(LegacyLauncherMessageId.EndUserInfo, null), region);
			Check.Bytes(new byte[] { 0x18, 0x00, 0x00, 0x00, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC }, region, "encoded end");
		}

		private static void OversizedPayloadRejected()
		{
			byte[] region = new byte[LegacyLauncherProtocol.LauncherRegionSize];
			string fits = new string('x', (LegacyLauncherProtocol.LauncherRegionSize - 10) / 2);
			LegacyLauncherProtocol.Encode(new LegacyLauncherMessage(LegacyLauncherMessageId.InstallDirectory, fits), region);
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.Encode(
				new LegacyLauncherMessage(LegacyLauncherMessageId.InstallDirectory, fits + "x"), region), "one character too many");
		}

		private static void ChannelRefusesExistingObjects()
		{
			string prefix = UniquePrefix();
			using (new Semaphore(0, 1, LegacyLauncherProtocol.GameDidReadName(prefix)))
			{
				var busy = Check.Throws<LegacyLauncherChannelBusyException>(() => LegacyLauncherChannel.Create(prefix, _ => { }), "busy channel");
				Check.Equal(prefix + "_gameDidReadMsgSem", busy.ObjectName, "busy object");
				Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "partially created objects released");
			}
			using (LegacyLauncherChannel.Create(prefix, _ => { }))
			{
				Check.True(Semaphore.TryOpenExisting(LegacyLauncherProtocol.LauncherDidReadName(prefix), out Semaphore opened), "created after blocker closed");
				opened.Dispose();
			}
		}

		private static void ReadingGameReceivesMessages()
		{
			string prefix = UniquePrefix();
			var messages = LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", Texts("V 1"), OfficialKeys);
			using (var channel = LegacyLauncherChannel.Create(prefix, _ => { }))
			using (var gameExited = new ManualResetEvent(false))
			{
				var game = new InProcessLegacyGame(prefix, sendGameMessageAfterEnd: true);
				game.Start();
				foreach (var message in messages)
				{
					Check.Equal(LegacySendResult.Delivered, channel.Send(message, gameExited, TimeSpan.FromSeconds(10)), "send " + message.Id);
				}
				Check.True(game.Join(TimeSpan.FromSeconds(10)), "game finished");
				Check.Sequence(new[]
				{
					"13:lang-en\0",
					"15:165 records",
					"16:2D,2E,2F,C8,CB,CD,D0,30,D2,C9,D1,CF,D3,C7",
					@"10:C:\D\user_7",
					@"11:C:\D",
					@"12:C:\G",
					"14:36 records",
					"18:V 1",
					"22:<none>",
					"24:<none>"
				}, game.Received.Select(NativeReaders.Brief), "received messages");
				Check.Equal("Clavier", NativeReaders.Strings(game.Received[1])[5], "the game read the translated table");
				Check.True(game.LauncherAcknowledged, "game-to-launcher message acknowledged");
				Check.Equal(1, channel.GameMessagesAcknowledged, "acknowledged count");
			}
		}

		private static void SendingStopsWhenGameExited()
		{
			string prefix = UniquePrefix();
			using (var channel = LegacyLauncherChannel.Create(prefix, _ => { }))
			using (var gameExited = new ManualResetEvent(true))
			{
				var started = DateTime.UtcNow;
				var result = channel.Send(new LegacyLauncherMessage(LegacyLauncherMessageId.EndUserInfo, null), gameExited, TimeSpan.FromSeconds(30));
				Check.Equal(LegacySendResult.GameExited, result, "result");
				Check.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "returned promptly");
			}
		}

		private static void SendingTimesOut()
		{
			string prefix = UniquePrefix();
			using (var channel = LegacyLauncherChannel.Create(prefix, _ => { }))
			using (var gameExited = new ManualResetEvent(false))
			{
				var result = channel.Send(new LegacyLauncherMessage(LegacyLauncherMessageId.EndUserInfo, null), gameExited, TimeSpan.FromMilliseconds(300));
				Check.Equal(LegacySendResult.TimedOut, result, "result");
			}
		}

		private static void GameMessagesDecode()
		{
			LegacyGameMessage saved = LegacyLauncherProtocol.DecodeGameMessage(FakeLegacyGameProtocol.Message(3, 0, 4, 7));
			Check.Equal(3u, saved.Id, "save event identifier");
			Check.Sequence(new[] { "0", "4", "7" }, saved.Values.Select(value => value.ToString()), "save event values");
			Check.True(saved.TryGetSavedSlot(out int save, out int slot), "a save event");
			Check.Equal(4, save, "save index");
			Check.Equal(7, slot, "slot index");

			Check.False(LegacyLauncherProtocol.DecodeGameMessage(FakeLegacyGameProtocol.Message(3, 1, 4, 7)).TryGetSavedSlot(out _, out _), "a load is not a save");
			Check.False(LegacyLauncherProtocol.DecodeGameMessage(FakeLegacyGameProtocol.Message(3, 0, 4)).TryGetSavedSlot(out _, out _), "two values are not a save event");
			Check.Sequence(new[] { "17" }, LegacyLauncherProtocol.DecodeGameMessage(FakeLegacyGameProtocol.Message(1, 17)).Values.Select(value => value.ToString()), "achievement");
			Check.Equal(0, LegacyLauncherProtocol.DecodeGameMessage(FakeLegacyGameProtocol.Message(4)).Values.Count, "start-up request has no payload");

			// A corrupt count never reads past the bytes that were copied.
			LegacyGameMessage corrupt = LegacyLauncherProtocol.DecodeGameMessage(new byte[] { 3, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F, 1, 0, 0, 0 });
			Check.Equal(0, corrupt.Values.Count, "corrupt count");
			Check.False(corrupt.TryGetSavedSlot(out _, out _), "corrupt message is not a save");
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.DecodeGameMessage(new byte[] { 3, 0 }), "shorter than an identifier");
		}

		private static void GameMessagesReachLauncher()
		{
			string prefix = UniquePrefix();
			var received = new List<string>();
			using (var channel = LegacyLauncherChannel.Create(prefix, _ => { }, message => { lock (received) { received.Add(message.Id + ":" + string.Join(",", message.Values)); } }))
			using (var gameExited = new ManualResetEvent(false))
			{
				var game = new InProcessLegacyGame(prefix, _ => new[] { FakeLegacyGameProtocol.Message(1, 9), FakeLegacyGameProtocol.Message(3, 0, 2, 5) });
				game.Start();
				foreach (var message in LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", Texts("V 1"), OfficialKeys))
				{
					Check.Equal(LegacySendResult.Delivered, channel.Send(message, gameExited, TimeSpan.FromSeconds(10)), "send " + message.Id);
				}
				Check.True(game.Join(TimeSpan.FromSeconds(10)), "game finished");
				Check.True(game.LauncherAcknowledged, "both messages acknowledged");
				Check.True(SpinWait.SpinUntil(() => { lock (received) { return received.Count == 2; } }, TimeSpan.FromSeconds(5)), "both messages dispatched");
				lock (received)
				{
					Check.Sequence(new[] { "1:9", "3:0,2,5" }, received, "dispatched messages");
				}
				Check.Equal(2, channel.GameMessagesAcknowledged, "acknowledged count");
			}
		}

		private static void FailingHandlerKeepsAcknowledging()
		{
			string prefix = UniquePrefix();
			var log = new List<string>();
			using (var channel = LegacyLauncherChannel.Create(prefix, entry => { lock (log) { log.Add(entry); } }, _ => throw new IOException("disk full")))
			using (var gameExited = new ManualResetEvent(false))
			{
				var game = new InProcessLegacyGame(prefix, _ => new[] { FakeLegacyGameProtocol.Message(3, 0, 1, 1), FakeLegacyGameProtocol.Message(3, 0, 2, 2) });
				game.Start();
				foreach (var message in LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", Texts("V 1"), OfficialKeys))
				{
					channel.Send(message, gameExited, TimeSpan.FromSeconds(10));
				}
				Check.True(game.Join(TimeSpan.FromSeconds(10)), "game finished");
				Check.True(game.LauncherAcknowledged, "both messages acknowledged");
				Check.True(SpinWait.SpinUntil(() => { lock (log) { return log.Count(entry => entry.Contains("disk full")) == 2; } }, TimeSpan.FromSeconds(5)), "both failures logged");
			}
		}

		internal static string Describe(LegacyLauncherMessage message)
		{
			if (message.Payload != null)
			{
				return NativeReaders.Read(LegacyLauncherProtocol.EncodeBytes(message));
			}
			return ((uint)message.Id).ToString() + ":" + (message.Text ?? "<none>");
		}

		private static byte[] Filled(int length, byte value)
		{
			return Enumerable.Repeat(value, length).ToArray();
		}
	}
}
