using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	// The game side of the 2013 launcher contract, written only against the
	// Windows objects and byte layout so it independently checks the launcher.
	internal static class FakeLegacyGameProtocol
	{
		internal const int LauncherOffset = 0x10000;
		internal const uint GameToLauncherProbe = 99;

		internal static bool Run(string prefix, Action<string> record, bool sendGameMessageAfterEnd, TimeSpan timeout)
		{
			return Run(prefix, record, sendGameMessageAfterEnd ? (Func<string, IEnumerable<byte[]>>)(_ => new[] { Message(GameToLauncherProbe) }) : null, timeout);
		}

		// afterHandshake receives the save folder from message 10 and returns the
		// messages the game then sends, in the driver's layout: the identifier,
		// then for integer arrays the count and values, or for message 1 one value.
		internal static bool Run(string prefix, Action<string> record, Func<string, IEnumerable<byte[]>> afterHandshake, TimeSpan timeout)
		{
			using (var gameCanRead = Semaphore.OpenExisting(prefix + "_gameCanReadMsgSem"))
			using (var gameDidRead = Semaphore.OpenExisting(prefix + "_gameDidReadMsgSem"))
			using (var launcherCanRead = Semaphore.OpenExisting(prefix + "_launcherCanReadMsgSem"))
			using (var launcherDidRead = Semaphore.OpenExisting(prefix + "_launcherDidReadMsgSem"))
			using (var mapping = MemoryMappedFile.OpenExisting(prefix + "_sharedMemoryWithLauncher"))
			using (var view = mapping.CreateViewAccessor())
			{
				string saveDirectory = null;
				while (true)
				{
					if (!gameCanRead.WaitOne(timeout))
					{
						record("timeout");
						return false;
					}
					uint id = view.ReadUInt32(LauncherOffset);
					if (id == 22 || id == 24)
					{
						record(id + ":<none>");
					}
					else if (id == 14 || id == 15 || id == 16)
					{
						record(NativeReaders.Read(id, offset => view.ReadInt32(LauncherOffset + offset), (offset, length) =>
						{
							var text = new char[length];
							view.ReadArray(LauncherOffset + offset, text, 0, length);
							return new string(text);
						}, 4, 0x10000));
					}
					else
					{
						int length = view.ReadInt32(LauncherOffset + 4);
						var chars = new char[length];
						view.ReadArray(LauncherOffset + 8, chars, 0, length);
						record(id + ":" + new string(chars));
						if (id == 10)
						{
							saveDirectory = new string(chars);
						}
					}
					gameDidRead.Release();
					if (id == 24)
					{
						break;
					}
				}

				if (afterHandshake == null)
				{
					return false;
				}
				bool acknowledged = true;
				foreach (byte[] message in afterHandshake(saveDirectory))
				{
					view.WriteArray(0, message, 0, message.Length);
					launcherCanRead.Release();
					acknowledged &= launcherDidRead.WaitOne(timeout);
				}
				return acknowledged;
			}
		}

		internal static byte[] Message(uint id, params int[] values)
		{
			var bytes = new List<byte>(BitConverter.GetBytes(id));
			if (id == 3 || id == 16)
			{
				bytes.AddRange(BitConverter.GetBytes(values.Length));
			}
			foreach (int value in values)
			{
				bytes.AddRange(BitConverter.GetBytes(value));
			}
			return bytes.ToArray();
		}

		// Writes a save the way the game does before it reports one.
		internal static IEnumerable<byte[]> WriteSaveAndReport(string saveDirectory, int saveIndex, int slotIndex, byte[] bytes, byte[] finalBytes)
		{
			string path = Path.Combine(saveDirectory, "save" + saveIndex.ToString("00") + ".ff7");
			File.WriteAllBytes(path, bytes);
			yield return Message(3, 0, saveIndex, slotIndex);
			if (finalBytes != null)
			{
				File.WriteAllBytes(path, finalBytes);
			}
		}
	}

	internal sealed class InProcessLegacyGame
	{
		private readonly string prefix;
		private readonly Func<string, IEnumerable<byte[]>> afterHandshake;
		private readonly List<string> received = new List<string>();
		private Thread thread;

		internal InProcessLegacyGame(string prefix, bool sendGameMessageAfterEnd)
			: this(prefix, sendGameMessageAfterEnd ? (Func<string, IEnumerable<byte[]>>)(_ => new[] { FakeLegacyGameProtocol.Message(FakeLegacyGameProtocol.GameToLauncherProbe) }) : null)
		{
		}

		internal InProcessLegacyGame(string prefix, Func<string, IEnumerable<byte[]>> afterHandshake)
		{
			this.prefix = prefix;
			this.afterHandshake = afterHandshake;
		}

		internal IReadOnlyList<string> Received
		{
			get
			{
				lock (received)
				{
					return received.ToArray();
				}
			}
		}

		internal bool LauncherAcknowledged { get; private set; }

		internal Exception Failure { get; private set; }

		internal void Start()
		{
			thread = new Thread(() =>
			{
				try
				{
					LauncherAcknowledged = FakeLegacyGameProtocol.Run(prefix, Record, afterHandshake, TimeSpan.FromSeconds(10));
				}
				catch (Exception exception)
				{
					Failure = exception;
				}
			})
			{ IsBackground = true, Name = "In-process legacy game" };
			thread.Start();
		}

		internal bool Join(TimeSpan timeout)
		{
			bool joined = thread.Join(timeout);
			if (Failure != null)
			{
				throw new TestFailure("In-process game failed: " + Failure);
			}
			return joined;
		}

		private void Record(string entry)
		{
			lock (received)
			{
				received.Add(entry);
			}
		}
	}

	// When the test runner is copied into a scratch game folder as ff7_en.exe
	// and started with no arguments, it plays the game side of the contract and
	// writes what it received beside itself.
	internal static class CrossProcessLegacyGame
	{
		internal const string SidecarName = "fake-legacy-game.txt";
		internal const string TranscriptName = "fake-legacy-game-transcript.txt";

		internal static bool IsFakeGameProcess()
		{
			string name = Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
			return System.Text.RegularExpressions.Regex.IsMatch(name, @"^ff7_(en|fr|de|es)\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
				File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SidecarName));
		}

		internal static int Run()
		{
			string directory = AppDomain.CurrentDomain.BaseDirectory;
			string[] sidecar = File.ReadAllLines(Path.Combine(directory, SidecarName));
			string prefix = sidecar[0];
			string mode = sidecar.Length > 1 ? sidecar[1] : "handshake";
			var transcript = new List<string>
			{
				"cwd:" + Environment.CurrentDirectory.TrimEnd('\\'),
				"args:" + string.Join(" ", Environment.GetCommandLineArgs(), 1, Environment.GetCommandLineArgs().Length - 1)
			};
			int exitCode = 0;
			try
			{
				if (mode == "handshake")
				{
					bool acknowledged = FakeLegacyGameProtocol.Run(prefix, transcript.Add, true, TimeSpan.FromSeconds(20));
					transcript.Add("acknowledged:" + acknowledged);
				}
				else if (mode.StartsWith("save:", StringComparison.Ordinal))
				{
					// save:<save index>:<slot index>:<save bytes in hex>
					string[] parts = mode.Split(':');
					byte[] bytes = Enumerable.Range(0, parts[3].Length / 2).Select(index => Convert.ToByte(parts[3].Substring(index * 2, 2), 16)).ToArray();
					bool acknowledged = FakeLegacyGameProtocol.Run(prefix, transcript.Add,
						saveDirectory => FakeLegacyGameProtocol.WriteSaveAndReport(saveDirectory, int.Parse(parts[1]), int.Parse(parts[2]), bytes, null),
						TimeSpan.FromSeconds(20));
					transcript.Add("acknowledged:" + acknowledged);
				}
				else if (mode.StartsWith("exit:", StringComparison.Ordinal))
				{
					exitCode = int.Parse(mode.Substring(5));
				}
				else if (mode.StartsWith("wait:", StringComparison.Ordinal))
				{
					Thread.Sleep(int.Parse(mode.Substring(5)));
				}
				else if (mode.StartsWith("hang-open:", StringComparison.Ordinal))
				{
					// Like a stuck driver: holds the launcher objects open and never reads.
					using (Semaphore.OpenExisting(prefix + "_gameCanReadMsgSem"))
					using (Semaphore.OpenExisting(prefix + "_gameDidReadMsgSem"))
					using (Semaphore.OpenExisting(prefix + "_launcherCanReadMsgSem"))
					using (Semaphore.OpenExisting(prefix + "_launcherDidReadMsgSem"))
					using (MemoryMappedFile.OpenExisting(prefix + "_sharedMemoryWithLauncher"))
					{
						transcript.Add("opened");
						Thread.Sleep(int.Parse(mode.Substring(10)));
					}
				}
				else if (mode.StartsWith("load:", StringComparison.Ordinal))
				{
					// load:<library beside this executable>:<milliseconds to stay running>
					string[] parts = mode.Split(':');
					transcript.Add("loaded:" + (LoadLibraryW(Path.Combine(directory, parts[1])) != IntPtr.Zero));
					Thread.Sleep(int.Parse(parts[2]));
				}
			}
			catch (Exception exception)
			{
				transcript.Add("error:" + exception.GetType().Name + ":" + exception.Message);
				exitCode = 3;
			}
			File.WriteAllLines(Path.Combine(directory, TranscriptName), transcript, new UTF8Encoding(false));
			return exitCode;
		}

		[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr LoadLibraryW(string path);
	}
}
