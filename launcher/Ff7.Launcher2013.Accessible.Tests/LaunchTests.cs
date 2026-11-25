using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class LaunchTests
	{
		internal static void Register(TestRunner runner)
		{
			runner.Add("FFNx launch starts the game directly without launcher objects", FfnxLaunchStartsDirectly);
			runner.Add("an unrecognized driver starts like FFNx", UnrecognizedDriverStartsDirectly);
			runner.Add("the original driver without a save profile fails before any write", OriginalWithoutProfileFailsBeforeWrites);
			runner.Add("the original driver launch creates missing settings and completes the handshake", OriginalLaunchCreatesSettingsAndHandshakes);
			runner.Add("the original driver launch keeps existing settings files", OriginalLaunchKeepsExistingSettings);
			runner.Add("a game that exits during the handshake is reported and objects are released", GameExitDuringHandshakeIsReported);
			runner.Add("a handshake timeout stops only the game it started and Play works again", HandshakeTimeoutStopsOwnedGame);
			runner.Add("a stuck real game is stopped after the handshake timeout, sparing other games", StuckRealGameIsStoppedAndRetryWorks);
			runner.Add("a start cancelled during the handshake stops only its own game", CancelledHandshakeStopsOwnedGame);
			runner.Add("a start cancelled before the game starts never starts it", CancelledBeforeStartNeverStarts);
			runner.Add("a direct start cancelled while the game was starting stops that game", CancelledDirectStartStopsGame);
			runner.Add("an unexpected failure after the game started stops that game", UnexpectedFailureStopsOwnedGame);
			runner.Add("invalid start-up information is refused before the game starts", InvalidStartupInformationNeverStarts);
			runner.Add("existing launcher objects block a second launch", ExistingObjectsBlockLaunch);
			runner.Add("a missing driver or unsupported folder never starts the game", MissingDriverAndFolderNeverStart);
			runner.Add("a real game process receives the handshake with no arguments", RealProcessReceivesHandshake);
			runner.Add("a save reported by the game updates metadata.xml as the original launcher does", ReportedSaveUpdatesMetadata);
			runner.Add("a launch without a save leaves the save folder byte for byte unchanged", LaunchWithoutSaveLeavesProfileUntouched);
			runner.Add("a real game process reports a save across processes", RealProcessReportsSave);
			runner.Add("the Blind Soldier loader is checked once, after the original driver handshake", LoaderCheckedAfterHandshake);
			runner.Add("a Blind Soldier load failure closes the game and is reported", LoaderFailureClosesGame);
		}

		private static readonly DateTime FixedNow = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

		private static LegacyLaunchRequest Request(LegacyGameTree game, LegacyInstallation installation, LegacySaveProfile profile, string prefix)
		{
			return new LegacyLaunchRequest
			{
				Installation = installation,
				SaveProfile = profile,
				DocumentDirectory = game.DocumentDirectory,
				GameVersionText = "Blind Soldier FF7 2013 Launcher 1.0.0",
				ChannelPrefix = prefix,
				MessageTimeout = TimeSpan.FromSeconds(10)
			};
		}

		private static LegacyInstallation Detect(LegacyGameTree game)
		{
			return LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
		}

		private static void FfnxLaunchStartsDirectly()
		{
			using (var game = new LegacyGameTree("launch-ffnx").WithStandard2013Files())
			{
				AssemblyFactory.CompileLibraryWithProduct(Path.Combine(game.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), null, prefix)))
				{
					Check.True(result.Started, "started: " + result.Failure + " " + result.Detail);
					Check.Sequence(new[] { Path.Combine(game.Root, "ff7_en.exe") + "|" + game.Root }, starter.Starts, "start calls");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "no launcher objects");
					Check.False(Directory.Exists(game.DocumentDirectory), "documents untouched");
				}
			}
		}

		private static void UnrecognizedDriverStartsDirectly()
		{
			using (var game = new LegacyGameTree("launch-unknown").WithStandard2013Files().WithDriverBytes(1048577))
			{
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), null, prefix)))
				{
					Check.True(result.Started, "started");
					Check.Equal(1, starter.Starts.Count, "start calls");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "no launcher objects");
				}
			}
		}

		private static void OriginalWithoutProfileFailsBeforeWrites()
		{
			using (var game = new LegacyGameTree("launch-noprofile").WithStandard2013Files().WithDriverBytes(1000))
			{
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), null, ProtocolTests.UniquePrefix())))
				{
					Check.Equal(LegacyLaunchFailure.NoSaveProfile, result.Failure, "failure");
					Check.Equal(0, starter.Starts.Count, "start calls");
					Check.False(Directory.Exists(game.DocumentDirectory), "documents untouched");
				}
			}
		}

		private static void OriginalLaunchCreatesSettingsAndHandshakes()
		{
			using (var game = new LegacyGameTree("launch-original").WithStandard2013Files().WithDriverBytes(1000))
			{
				string profilePath = game.CreateProfile("user_123", "save00.ff7");
				var profile = LegacySaveProfiles.Find(game.DocumentDirectory).Single();
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: true, exitImmediately: false) { Prefix = prefix };
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), profile, prefix)))
				{
					Check.True(result.Started, "started: " + result.Failure + " " + result.Detail);
					Check.True(starter.Game.Join(TimeSpan.FromSeconds(10)), "game side finished");
					Check.Sequence(new[]
					{
						"13:lang-en\0",
						"10:" + profilePath,
						"11:" + game.DocumentDirectory,
						"12:" + game.Root,
						"18:Blind Soldier FF7 2013 Launcher 1.0.0",
						"22:<none>",
						"24:<none>"
					}, starter.Game.Received, "handshake");
					Check.True(starter.Game.LauncherAcknowledged, "game message acknowledged while running");
					Check.False(starter.LastProcess.Killed, "a started game keeps running");
					Check.Bytes(new byte[32], File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7video.cfg")), "default display file");
					Check.Bytes(new byte[] { 100, 0, 0, 0, 100, 0, 0, 0 }, File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7sound.cfg")), "default sound file");
					Check.True(Semaphore.TryOpenExisting(LegacyLauncherProtocol.LauncherCanReadName(prefix), out Semaphore alive), "objects stay open while the game runs");
					alive.Dispose();
				}
				Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.LauncherCanReadName(prefix), out _), "objects released after the result is disposed");
			}
		}

		private static void OriginalLaunchKeepsExistingSettings()
		{
			using (var game = new LegacyGameTree("launch-keep").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				byte[] display = new byte[32];
				display[15] = 1;
				byte[] sound = { 10, 0, 0, 0, 20, 0, 0, 0 };
				File.WriteAllBytes(Path.Combine(game.DocumentDirectory, "ff7video.cfg"), display);
				File.WriteAllBytes(Path.Combine(game.DocumentDirectory, "ff7sound.cfg"), sound);
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: true, exitImmediately: false) { Prefix = prefix };
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
				{
					Check.True(result.Started, "started");
					starter.Game.Join(TimeSpan.FromSeconds(10));
				}
				Check.Bytes(display, File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7video.cfg")), "display preserved");
				Check.Bytes(sound, File.ReadAllBytes(Path.Combine(game.DocumentDirectory, "ff7sound.cfg")), "sound preserved");
				Check.Equal(3, Directory.GetFileSystemEntries(game.DocumentDirectory).Length, "no backups or extra files");
			}
		}

		private static void GameExitDuringHandshakeIsReported()
		{
			using (var game = new LegacyGameTree("launch-exit").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: true);
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
				{
					Check.Equal(LegacyLaunchFailure.GameExitedDuringHandshake, result.Failure, "failure");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "objects released");
				}
			}
		}

		private static void HandshakeTimeoutStopsOwnedGame()
		{
			using (var game = new LegacyGameTree("launch-timeout").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix);
				request.MessageTimeout = TimeSpan.FromMilliseconds(300);
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(request))
				{
					Check.Equal(LegacyLaunchFailure.HandshakeTimedOut, result.Failure, "failure");
					Check.Equal("13", result.Detail, "first unread message");
					Check.True(starter.LastProcess.Killed, "the stuck game this launch started was stopped");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "launcher objects released before the result returned");
				}

				var retry = new FakeStarter(runGameSide: true, exitImmediately: false) { Prefix = prefix, AfterHandshake = _ => Enumerable.Empty<byte[]>() };
				request.MessageTimeout = TimeSpan.FromSeconds(10);
				using (var result = new LegacyGameLauncher(retry, _ => { }).Launch(request))
				{
					Check.True(result.Started, "Play works again: " + result.Failure + " " + result.Detail);
					Check.True(retry.Game.Join(TimeSpan.FromSeconds(10)), "retried game side finished");
					Check.False(retry.LastProcess.Killed, "a started game keeps running");
					retry.LastProcess.Exit();
				}
			}
		}

		private static void StuckRealGameIsStoppedAndRetryWorks()
		{
			using (var game = new LegacyGameTree("launch-stuck").WithStandard2013Files().WithDriverBytes(1000))
			using (var other = new LegacyGameTree("launch-stuck-other").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_8");
				string prefix = ProtocolTests.UniquePrefix();
				string runner = Assembly.GetExecutingAssembly().Location;
				File.Copy(runner, Path.Combine(game.Root, "ff7_en.exe"), true);
				File.Copy(runner, Path.Combine(other.Root, "ff7_en.exe"), true);
				// Another copy of the game, which this launch did not start.
				File.WriteAllLines(Path.Combine(other.Root, CrossProcessLegacyGame.SidecarName), new[] { ProtocolTests.UniquePrefix(), "wait:60000" });
				using (ILegacyGameProcess bystander = new LegacyGameProcessStarter().Start(Path.Combine(other.Root, "ff7_en.exe"), other.Root))
				{
					try
					{
						// Like a stuck driver, the game opens the launcher objects and never reads.
						File.WriteAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.SidecarName), new[] { prefix, "hang-open:60000" });
						var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix);
						request.MessageTimeout = TimeSpan.FromSeconds(3);
						using (var result = new LegacyGameLauncher(new LegacyGameProcessStarter(), _ => { }).Launch(request))
						{
							Check.Equal(LegacyLaunchFailure.HandshakeTimedOut, result.Failure, "failure");
							Check.True(result.Process.HasExited, "the stuck game was stopped");
						}
						Check.False(bystander.HasExited, "the other game keeps running");

						File.WriteAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.SidecarName), new[] { prefix, "handshake" });
						request.MessageTimeout = TimeSpan.FromSeconds(10);
						using (var result = new LegacyGameLauncher(new LegacyGameProcessStarter(), _ => { }).Launch(request))
						{
							Check.True(result.Started, "Play works again: " + result.Failure + " " + result.Detail);
							Check.True(result.Process.ExitedHandle.WaitOne(TimeSpan.FromSeconds(30)), "the retried game finished");
							Check.Equal(0, result.Process.ExitCode, "the retried game exited normally");
						}
						Check.False(bystander.HasExited, "the other game is still running");
					}
					finally
					{
						bystander.Kill();
						bystander.WaitForExit();
					}
				}
			}
		}

		private static void CancelledHandshakeStopsOwnedGame()
		{
			using (var game = new LegacyGameTree("launch-cancel").WithStandard2013Files().WithDriverBytes(1000))
			using (var cancel = new CancellationTokenSource())
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix);
				request.MessageTimeout = TimeSpan.FromSeconds(30);
				request.Cancellation = cancel.Token;
				cancel.CancelAfter(TimeSpan.FromMilliseconds(300));
				var watch = System.Diagnostics.Stopwatch.StartNew();
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(request))
				{
					Check.Equal(LegacyLaunchFailure.Cancelled, result.Failure, "failure");
					Check.True(watch.Elapsed < TimeSpan.FromSeconds(10), "stopped without waiting for the handshake timeout");
					Check.True(starter.LastProcess.Killed, "the game this launch started was stopped");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "launcher objects released");
				}
			}
		}

		private static void CancelledBeforeStartNeverStarts()
		{
			using (var original = new LegacyGameTree("launch-cancel-first").WithStandard2013Files().WithDriverBytes(1000))
			using (var ffnx = new LegacyGameTree("launch-cancel-ffnx").WithStandard2013Files())
			{
				original.CreateProfile("user_5");
				AssemblyFactory.CompileLibraryWithProduct(Path.Combine(ffnx.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
				foreach (var game in new[] { original, ffnx })
				{
					string prefix = ProtocolTests.UniquePrefix();
					var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
					var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).SingleOrDefault(), prefix);
					request.Cancellation = new CancellationToken(true);
					using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(request))
					{
						Check.Equal(LegacyLaunchFailure.Cancelled, result.Failure, game.Root + " failure");
						Check.Equal(0, starter.Starts.Count, game.Root + " nothing started");
						Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), game.Root + " no launcher objects");
					}
				}
			}
		}

		private static void CancelledDirectStartStopsGame()
		{
			using (var game = new LegacyGameTree("launch-cancel-direct").WithStandard2013Files())
			using (var cancel = new CancellationTokenSource())
			{
				AssemblyFactory.CompileLibraryWithProduct(Path.Combine(game.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
				// Exit is pressed while Windows is creating the game process.
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false) { OnStart = cancel.Cancel };
				var request = Request(game, Detect(game), null, ProtocolTests.UniquePrefix());
				request.Cancellation = cancel.Token;
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(request))
				{
					Check.Equal(LegacyLaunchFailure.Cancelled, result.Failure, "failure");
					Check.Equal(1, starter.Starts.Count, "started once");
					Check.True(starter.LastProcess.Killed, "the game this launch started was stopped");
				}
			}
		}

		private static void UnexpectedFailureStopsOwnedGame()
		{
			using (var game = new LegacyGameTree("launch-broken").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var log = new List<string>();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false) { BreakExitedHandle = true };
				using (var result = new LegacyGameLauncher(starter, entry => { lock (log) { log.Add(entry); } }).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
				{
					Check.Equal(LegacyLaunchFailure.StartFailed, result.Failure, "failure");
					Check.True(result.Detail.Contains("The game handle is unavailable."), "detail: " + result.Detail);
					Check.True(starter.LastProcess.Killed, "the game this launch started was stopped");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "launcher objects released");
				}
				Check.True(log.Any(entry => entry.Contains("The game handle is unavailable.")), "logged: " + string.Join(" | ", log));
			}
		}

		private static void InvalidStartupInformationNeverStarts()
		{
			using (var game = new LegacyGameTree("launch-invalid").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix);
				request.GameVersionText = string.Empty;
				Check.Throws<ArgumentException>(() => new LegacyGameLauncher(starter, _ => { }).Launch(request), "missing version text");
				Check.Equal(0, starter.Starts.Count, "nothing started");
				Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "no launcher objects left");
			}
		}

		private static void ExistingObjectsBlockLaunch()
		{
			using (var game = new LegacyGameTree("launch-busy").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				using (MemoryMappedFile.CreateNew(LegacyLauncherProtocol.SharedMemoryName(prefix), 4096))
				{
					var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
					using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
					{
						Check.Equal(LegacyLaunchFailure.AlreadyRunning, result.Failure, "failure");
						Check.Equal(0, starter.Starts.Count, "start calls");
					}
				}
			}
		}

		private static void MissingDriverAndFolderNeverStart()
		{
			using (var game = new LegacyGameTree("launch-nodriver").WithStandard2013Files())
			{
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), null, ProtocolTests.UniquePrefix())))
				{
					Check.Equal(LegacyLaunchFailure.DriverMissing, result.Failure, "missing driver");
				}
				File.Delete(Path.Combine(game.Root, "ff7_en.exe"));
				using (var result = new LegacyGameLauncher(starter, _ => { }).Launch(Request(game, Detect(game), null, ProtocolTests.UniquePrefix())))
				{
					Check.Equal(LegacyLaunchFailure.UnsupportedFolder, result.Failure, "unsupported folder");
				}
				Check.Equal(0, starter.Starts.Count, "start calls");
			}
		}

		private static void RealProcessReceivesHandshake()
		{
			using (var game = new LegacyGameTree("launch-process").WithStandard2013Files().WithDriverBytes(1000))
			{
				string profilePath = game.CreateProfile("user_77", "save01.ff7");
				string prefix = ProtocolTests.UniquePrefix();
				File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(game.Root, "ff7_en.exe"), true);
				File.WriteAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.SidecarName), new[] { prefix, "handshake" });
				var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix);
				using (var result = new LegacyGameLauncher(new LegacyGameProcessStarter(), _ => { }).Launch(request))
				{
					Check.True(result.Started, "started: " + result.Failure + " " + result.Detail);
					Check.True(result.Process.ExitedHandle.WaitOne(TimeSpan.FromSeconds(30)), "game exited");
					Check.Equal(0, result.Process.ExitCode, "game exit code");
				}
				string[] transcript = File.ReadAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.TranscriptName));
				Check.Sequence(new[]
				{
					"cwd:" + game.Root,
					"args:",
					"13:lang-en\0",
					"10:" + profilePath,
					"11:" + game.DocumentDirectory,
					"12:" + game.Root,
					"18:Blind Soldier FF7 2013 Launcher 1.0.0",
					"22:<none>",
					"24:<none>",
					"acknowledged:True"
				}, transcript, "transcript");
			}
		}

		private static Dictionary<string, byte[]> Snapshot(string directory)
		{
			return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
				.ToDictionary(path => path.Substring(directory.Length), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
		}

		private static void ReportedSaveUpdatesMetadata()
		{
			using (var game = new LegacyGameTree("launch-save").WithStandard2013Files().WithDriverBytes(1000))
			{
				string profilePath = game.CreateProfile("user_123");
				string empty = LegacySaveMetadata.Sign(null, "123");
				File.WriteAllBytes(Path.Combine(profilePath, "metadata.xml"), Encoding.UTF8.GetBytes(SaveMetadataTests.OriginalFormat(
					(save, slot) => save == 0 && slot == 0 ? "1600000000000" : string.Empty,
					save => save == 0 ? "00112233445566778899aabbccddeeff" : empty)));
				string prefix = ProtocolTests.UniquePrefix();
				byte[] written = { 1, 2, 3 };
				byte[] flushed = { 1, 2, 3, 4 };
				var starter = new FakeStarter(runGameSide: true, exitImmediately: false)
				{
					Prefix = prefix,
					AfterHandshake = saveDirectory => FakeLegacyGameProtocol.WriteSaveAndReport(saveDirectory, 2, 5, written, flushed)
				};
				var profile = LegacySaveProfiles.Find(game.DocumentDirectory).Single();
				using (var result = new LegacyGameLauncher(starter, _ => { }, null, () => FixedNow).Launch(Request(game, Detect(game), profile, prefix)))
				{
					Check.True(result.Started, "started: " + result.Failure + " " + result.Detail);
					Check.True(starter.Game.Join(TimeSpan.FromSeconds(10)), "game side finished");
					Check.True(starter.Game.LauncherAcknowledged, "save acknowledged");
					starter.LastProcess.Exit();
				}

				Check.True(LegacySaveMetadata.TryParse(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(profilePath, "metadata.xml"))), "123", out LegacySaveMetadata metadata), "valid metadata");
				Check.Equal("1795608000000", metadata.GetTimestamp(2, 5), "saved slot time");
				Check.Equal(SaveMetadataTests.ExpectedSignature(flushed, "123"), metadata.GetSignature(2), "final save bytes signed");
				Check.Equal("1600000000000", metadata.GetTimestamp(0, 0), "other timestamps kept");
				Check.Equal("00112233445566778899aabbccddeeff", metadata.GetSignature(0), "other signatures kept");
				Check.Equal(empty, metadata.GetSignature(1), "empty saves stay empty");
			}
		}

		private static void LaunchWithoutSaveLeavesProfileUntouched()
		{
			using (var game = new LegacyGameTree("launch-untouched").WithStandard2013Files().WithDriverBytes(1000))
			{
				string profilePath = game.CreateProfile("user_9", "save00.ff7", "save04.ff7");
				File.WriteAllBytes(Path.Combine(profilePath, "metadata.xml"), Encoding.UTF8.GetBytes("<not-even-valid/>"));
				File.WriteAllBytes(Path.Combine(profilePath, "achievement.dat"), new byte[] { 0, 1 });
				Dictionary<string, byte[]> before = Snapshot(profilePath);
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: true, exitImmediately: false) { Prefix = prefix };
				using (var result = new LegacyGameLauncher(starter, _ => { }, null, () => FixedNow).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
				{
					Check.True(result.Started, "started");
					Check.True(starter.Game.Join(TimeSpan.FromSeconds(10)), "game side finished");
					starter.LastProcess.Exit();
				}
				Dictionary<string, byte[]> after = Snapshot(profilePath);
				Check.Sequence(before.Keys.OrderBy(key => key, StringComparer.Ordinal), after.Keys.OrderBy(key => key, StringComparer.Ordinal), "same files");
				foreach (var entry in before)
				{
					Check.Bytes(entry.Value, after[entry.Key], entry.Key + " unchanged");
				}
			}
		}

		private static void RealProcessReportsSave()
		{
			using (var game = new LegacyGameTree("launch-process-save").WithStandard2013Files().WithDriverBytes(1000))
			{
				string profilePath = game.CreateProfile("user_77");
				string prefix = ProtocolTests.UniquePrefix();
				File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(game.Root, "ff7_en.exe"), true);
				File.WriteAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.SidecarName), new[] { prefix, "save:1:3:0A0B0C" });
				var request = Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix);
				using (var result = new LegacyGameLauncher(new LegacyGameProcessStarter(), _ => { }, null, () => FixedNow).Launch(request))
				{
					Check.True(result.Started, "started: " + result.Failure + " " + result.Detail);
					Check.True(result.Process.ExitedHandle.WaitOne(TimeSpan.FromSeconds(30)), "game exited");
					Check.Equal(0, result.Process.ExitCode, "game exit code");
				}
				string[] transcript = File.ReadAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.TranscriptName));
				Check.Equal("acknowledged:True", transcript.Last(), "save acknowledged");
				byte[] save = File.ReadAllBytes(Path.Combine(profilePath, "save01.ff7"));
				Check.Bytes(new byte[] { 0x0A, 0x0B, 0x0C }, save, "save written by the game");
				Check.True(LegacySaveMetadata.TryParse(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(profilePath, "metadata.xml"))), "77", out LegacySaveMetadata metadata), "valid metadata");
				Check.Equal("1795608000000", metadata.GetTimestamp(1, 3), "saved slot time");
				Check.Equal(SaveMetadataTests.ExpectedSignature(save, "77"), metadata.GetSignature(1), "save signed");
			}
		}

		private static void LoaderCheckedAfterHandshake()
		{
			using (var game = new LegacyGameTree("launch-loader").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: true, exitImmediately: false) { Prefix = prefix, AfterHandshake = _ => Enumerable.Empty<byte[]>() };
				var loader = new RecordingLoader(BlindSoldierLoadState.Loaded, () => starter.Game.Received.Count);
				using (var result = new LegacyGameLauncher(starter, _ => { }, loader, () => FixedNow).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
				{
					Check.True(result.Started, "started: " + result.Failure + " " + result.Detail);
					Check.Equal(1, loader.Calls.Count, "checked once");
					Check.Equal(7, loader.Calls[0], "checked after all seven start-up messages were read");
					Check.True(ReferenceEquals(starter.LastProcess, loader.Processes[0]), "checked the started game");
					Check.False(starter.LastProcess.Killed, "a loaded game keeps running");
					starter.Game.Join(TimeSpan.FromSeconds(10));
				}
			}
			using (var game = new LegacyGameTree("launch-loader-ffnx").WithStandard2013Files())
			{
				AssemblyFactory.CompileLibraryWithProduct(Path.Combine(game.Root, "AF3DN.P"), "FFNx", "1.25.2.0");
				var starter = new FakeStarter(runGameSide: false, exitImmediately: false);
				var loader = new RecordingLoader(BlindSoldierLoadState.Failed, () => 0);
				using (var result = new LegacyGameLauncher(starter, _ => { }, loader, () => FixedNow).Launch(Request(game, Detect(game), null, ProtocolTests.UniquePrefix())))
				{
					Check.True(result.Started, "FFNx started");
					Check.Equal(0, loader.Calls.Count, "FFNx loads Blind Soldier through its own Version import");
				}
			}
		}

		private static void LoaderFailureClosesGame()
		{
			using (var game = new LegacyGameTree("launch-loader-fail").WithStandard2013Files().WithDriverBytes(1000))
			{
				game.CreateProfile("user_5");
				string prefix = ProtocolTests.UniquePrefix();
				var starter = new FakeStarter(runGameSide: true, exitImmediately: false) { Prefix = prefix, AfterHandshake = _ => Enumerable.Empty<byte[]>() };
				var loader = new RecordingLoader(BlindSoldierLoadState.Failed, () => 0);
				using (var result = new LegacyGameLauncher(starter, _ => { }, loader, () => FixedNow).Launch(Request(game, Detect(game), LegacySaveProfiles.Find(game.DocumentDirectory).Single(), prefix)))
				{
					Check.Equal(LegacyLaunchFailure.BlindSoldierLoadFailed, result.Failure, "failure");
					Check.Equal("access denied", result.Detail, "detail");
					Check.True(starter.LastProcess.Killed, "silent game closed");
					Check.False(Semaphore.TryOpenExisting(LegacyLauncherProtocol.GameCanReadName(prefix), out _), "objects released");
					starter.Game.Join(TimeSpan.FromSeconds(10));
				}
			}
		}

		private sealed class RecordingLoader : IBlindSoldierLoader
		{
			private readonly BlindSoldierLoadState state;
			private readonly Func<int> progress;

			internal RecordingLoader(BlindSoldierLoadState state, Func<int> progress)
			{
				this.state = state;
				this.progress = progress;
			}

			internal List<int> Calls { get; } = new List<int>();

			internal List<ILegacyGameProcess> Processes { get; } = new List<ILegacyGameProcess>();

			public BlindSoldierLoadResult Ensure(LegacyInstallation installation, ILegacyGameProcess process)
			{
				Calls.Add(progress());
				Processes.Add(process);
				return new BlindSoldierLoadResult(state, state == BlindSoldierLoadState.Failed ? "access denied" : string.Empty);
			}
		}

		private sealed class FakeStarter : ILegacyGameProcessStarter
		{
			private readonly bool runGameSide;
			private readonly bool exitImmediately;

			internal FakeStarter(bool runGameSide, bool exitImmediately)
			{
				this.runGameSide = runGameSide;
				this.exitImmediately = exitImmediately;
			}

			internal string Prefix { get; set; }

			internal Func<string, IEnumerable<byte[]>> AfterHandshake { get; set; }

			// Runs inside Start, as Windows creates the process.
			internal Action OnStart { get; set; }

			internal bool BreakExitedHandle { get; set; }

			internal List<string> Starts { get; } = new List<string>();

			internal InProcessLegacyGame Game { get; private set; }

			internal FakeProcess LastProcess { get; private set; }

			public ILegacyGameProcess Start(string executablePath, string workingDirectory)
			{
				Starts.Add(executablePath + "|" + workingDirectory);
				if (runGameSide)
				{
					Game = AfterHandshake == null
						? new InProcessLegacyGame(Prefix, sendGameMessageAfterEnd: true)
						: new InProcessLegacyGame(Prefix, AfterHandshake);
					Game.Start();
				}
				LastProcess = new FakeProcess(exitImmediately) { BreakExitedHandle = BreakExitedHandle };
				OnStart?.Invoke();
				return LastProcess;
			}
		}

		private sealed class FakeProcess : ILegacyGameProcess
		{
			private readonly ManualResetEvent exited;

			internal FakeProcess(bool exited)
			{
				this.exited = new ManualResetEvent(exited);
			}

			public int Id => 4242;

			internal bool BreakExitedHandle { get; set; }

			public WaitHandle ExitedHandle => BreakExitedHandle ? throw new InvalidOperationException("The game handle is unavailable.") : exited;

			public bool HasExited => exited.WaitOne(0);

			public int ExitCode => HasExited ? 1 : throw new InvalidOperationException("The fake game is still running.");

			internal bool Killed { get; private set; }

			internal void Exit()
			{
				exited.Set();
			}

			public void WaitForExit()
			{
				exited.WaitOne();
			}

			public void Kill()
			{
				Killed = true;
				exited.Set();
			}

			public Microsoft.Win32.SafeHandles.SafeProcessHandle DuplicateHandle(uint desiredAccess)
			{
				throw new NotSupportedException("A fake game has no process handle.");
			}

			public void Dispose()
			{
				exited.Dispose();
			}
		}
	}
}
