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

		private static void StartupSequenceMatchesContract()
		{
			var sequence = LegacyLauncherProtocol.BuildStartupSequence(
				"fr",
				@"D:\Docs\Square Enix\FINAL FANTASY VII Steam\user_42",
				@"D:\Docs\Square Enix\FINAL FANTASY VII Steam",
				@"E:\Steam\steamapps\common\FINAL FANTASY VII",
				"Blind Soldier FF7 2013 Launcher 1.0.0");
			Check.Sequence(new[]
			{
				"13:lang-fr\0",
				@"10:D:\Docs\Square Enix\FINAL FANTASY VII Steam\user_42",
				@"11:D:\Docs\Square Enix\FINAL FANTASY VII Steam",
				@"12:E:\Steam\steamapps\common\FINAL FANTASY VII",
				"18:Blind Soldier FF7 2013 Launcher 1.0.0",
				"22:<none>",
				"24:<none>"
			}, sequence.Select(Describe), "startup sequence");
		}

		private static void StartupSequenceRejectsLanguage()
		{
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.BuildStartupSequence("ja", "a", "b", "c", "d"), "Japanese");
			Check.Throws<ArgumentException>(() => LegacyLauncherProtocol.BuildStartupSequence("", "a", "b", "c", "d"), "empty");
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
			var messages = LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", "V 1");
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
					@"10:C:\D\user_7",
					@"11:C:\D",
					@"12:C:\G",
					"18:V 1",
					"22:<none>",
					"24:<none>"
				}, game.Received, "received messages");
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
				foreach (var message in LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", "V 1"))
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
				foreach (var message in LegacyLauncherProtocol.BuildStartupSequence("en", @"C:\D\user_7", @"C:\D", @"C:\G", "V 1"))
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
			return ((uint)message.Id).ToString() + ":" + (message.Text ?? "<none>");
		}

		private static byte[] Filled(int length, byte value)
		{
			return Enumerable.Repeat(value, length).ToArray();
		}
	}
}
