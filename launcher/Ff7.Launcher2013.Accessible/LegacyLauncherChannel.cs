using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace BlindSoldier.Ff7Launcher2013
{
	internal enum LegacySendResult
	{
		Delivered,
		GameExited,
		TimedOut,
		Cancelled
	}

	internal sealed class LegacyLauncherChannelBusyException : Exception
	{
		internal LegacyLauncherChannelBusyException(string objectName)
			: base("A launcher communication object already exists: " + objectName)
		{
			ObjectName = objectName;
		}

		internal string ObjectName { get; }
	}

	// Owns the named semaphores and shared memory the original 2013 driver
	// opens at start-up. Messages the game sends back are acknowledged for as
	// long as the channel lives, as the official launcher and FF78Launcher do;
	// the game's sending thread holds its message queue until then. Each
	// message is passed on only after it has been acknowledged.
	internal sealed class LegacyLauncherChannel : IDisposable
	{
		private readonly Action<string> log;
		private readonly Action<LegacyGameMessage> onGameMessage;
		private readonly ManualResetEvent stop = new ManualResetEvent(false);
		private Semaphore gameCanRead;
		private Semaphore gameDidRead;
		private Semaphore launcherCanRead;
		private Semaphore launcherDidRead;
		private MemoryMappedFile mapping;
		private MemoryMappedViewAccessor view;
		private Thread pump;
		private int acknowledged;
		private int disposed;

		private LegacyLauncherChannel(Action<string> log, Action<LegacyGameMessage> onGameMessage)
		{
			this.log = log ?? delegate { };
			this.onGameMessage = onGameMessage;
		}

		internal int GameMessagesAcknowledged => Volatile.Read(ref acknowledged);

		internal static LegacyLauncherChannel Create(string prefix, Action<string> log, Action<LegacyGameMessage> onGameMessage = null)
		{
			if (string.IsNullOrEmpty(prefix))
			{
				throw new ArgumentException("A launcher object prefix is required.", nameof(prefix));
			}

			var channel = new LegacyLauncherChannel(log, onGameMessage);
			try
			{
				channel.gameCanRead = CreateNewSemaphore(LegacyLauncherProtocol.GameCanReadName(prefix));
				channel.gameDidRead = CreateNewSemaphore(LegacyLauncherProtocol.GameDidReadName(prefix));
				channel.launcherCanRead = CreateNewSemaphore(LegacyLauncherProtocol.LauncherCanReadName(prefix));
				channel.launcherDidRead = CreateNewSemaphore(LegacyLauncherProtocol.LauncherDidReadName(prefix));
				string mappingName = LegacyLauncherProtocol.SharedMemoryName(prefix);
				try
				{
					channel.mapping = MemoryMappedFile.CreateNew(mappingName, LegacyLauncherProtocol.SharedMemorySize, MemoryMappedFileAccess.ReadWrite);
				}
				catch (IOException)
				{
					throw new LegacyLauncherChannelBusyException(mappingName);
				}
				channel.view = channel.mapping.CreateViewAccessor(0, LegacyLauncherProtocol.SharedMemorySize, MemoryMappedFileAccess.ReadWrite);
				channel.StartPump();
				return channel;
			}
			catch
			{
				channel.Dispose();
				throw;
			}
		}

		internal LegacySendResult Send(LegacyLauncherMessage message, WaitHandle gameExited, TimeSpan timeout, CancellationToken cancellation = default(CancellationToken))
		{
			if (gameExited == null)
			{
				throw new ArgumentNullException(nameof(gameExited));
			}

			byte[] bytes = LegacyLauncherProtocol.EncodeBytes(message);
			if (bytes.Length > LegacyLauncherProtocol.LauncherRegionSize)
			{
				throw new ArgumentException("The launcher message does not fit the shared launcher region.", nameof(message));
			}
			view.WriteArray(LegacyLauncherProtocol.LauncherMessageOffset, bytes, 0, bytes.Length);
			view.Flush();

			try
			{
				gameCanRead.Release();
			}
			catch (SemaphoreFullException)
			{
				log("The previous launcher message was never read; message " + (uint)message.Id + " was not offered.");
				return LegacySendResult.TimedOut;
			}

			WaitHandle[] handles = cancellation.CanBeCanceled
				? new[] { gameDidRead, gameExited, cancellation.WaitHandle }
				: new[] { gameDidRead, gameExited };
			int signaled = WaitHandle.WaitAny(handles, timeout);
			if (signaled == 2)
			{
				log("Stopped waiting for launcher message " + (uint)message.Id + " because the start was cancelled.");
				return LegacySendResult.Cancelled;
			}
			if (signaled == 0)
			{
				log("Launcher message " + (uint)message.Id + " was read by the game.");
				return LegacySendResult.Delivered;
			}
			if (signaled == 1)
			{
				log("The game exited before reading launcher message " + (uint)message.Id + ".");
				return LegacySendResult.GameExited;
			}
			log("The game did not read launcher message " + (uint)message.Id + " within " + timeout.TotalSeconds + " seconds.");
			return LegacySendResult.TimedOut;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
			{
				return;
			}
			stop.Set();
			pump?.Join(TimeSpan.FromSeconds(5));
			view?.Dispose();
			mapping?.Dispose();
			gameCanRead?.Dispose();
			gameDidRead?.Dispose();
			launcherCanRead?.Dispose();
			launcherDidRead?.Dispose();
			if (pump == null || !pump.IsAlive)
			{
				stop.Dispose();
			}
		}

		private void Dispatch(LegacyGameMessage message)
		{
			if (onGameMessage == null)
			{
				return;
			}
			try
			{
				onGameMessage(message);
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				log("Game message " + message.Id + " could not be handled: " + exception.Message);
			}
		}

		private static Semaphore CreateNewSemaphore(string name)
		{
			var semaphore = new Semaphore(0, 1, name, out bool createdNew);
			if (!createdNew)
			{
				semaphore.Dispose();
				throw new LegacyLauncherChannelBusyException(name);
			}
			return semaphore;
		}

		private void StartPump()
		{
			pump = new Thread(PumpGameMessages)
			{
				IsBackground = true,
				Name = "FF7 2013 launcher message acknowledgement"
			};
			pump.Start();
		}

		private void PumpGameMessages()
		{
			try
			{
				var handles = new WaitHandle[] { launcherCanRead, stop };
				var bytes = new byte[LegacyLauncherProtocol.GameMessageReadLength];
				while (WaitHandle.WaitAny(handles) == 0)
				{
					view.ReadArray(LegacyLauncherProtocol.GameMessageOffset, bytes, 0, bytes.Length);
					LegacyGameMessage message = LegacyLauncherProtocol.DecodeGameMessage(bytes);
					log("The game sent launcher message " + message.Id +
						(message.Values.Count == 0 ? string.Empty : " [" + string.Join(", ", message.Values) + "]") + "; acknowledged.");
					Interlocked.Increment(ref acknowledged);
					try
					{
						launcherDidRead.Release();
					}
					catch (SemaphoreFullException)
					{
						log("The game had not consumed the previous acknowledgement.");
					}
					Dispatch(message);
				}
			}
			catch (Exception exception) when (exception is ObjectDisposedException || exception is IOException || exception is UnauthorizedAccessException)
			{
				log("Launcher message acknowledgement stopped: " + exception.Message);
			}
		}
	}
}
