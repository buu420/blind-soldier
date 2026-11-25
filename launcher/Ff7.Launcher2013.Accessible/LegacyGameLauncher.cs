using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BlindSoldier.Ff7Launcher2013
{
	internal interface ILegacyGameProcess : IDisposable
	{
		int Id { get; }

		WaitHandle ExitedHandle { get; }

		bool HasExited { get; }

		int ExitCode { get; }

		void WaitForExit();

		void Kill();

		// A new handle to this exact process, so nothing is ever opened by its
		// process ID, which Windows can give to another process.
		SafeProcessHandle DuplicateHandle(uint desiredAccess);
	}

	internal interface ILegacyGameProcessStarter
	{
		// Starts the game with no command-line arguments, as the 2013 launcher does.
		ILegacyGameProcess Start(string executablePath, string workingDirectory);
	}

	internal sealed class LegacyGameProcessStarter : ILegacyGameProcessStarter
	{
		public ILegacyGameProcess Start(string executablePath, string workingDirectory)
		{
			var startInfo = new ProcessStartInfo(executablePath)
			{
				Arguments = string.Empty,
				WorkingDirectory = workingDirectory,
				UseShellExecute = false
			};
			Process process = Process.Start(startInfo);
			if (process == null)
			{
				throw new InvalidOperationException("Windows did not create the game process.");
			}
			return new LegacyGameProcess(process);
		}

		private sealed class LegacyGameProcess : ILegacyGameProcess
		{
			private readonly Process process;
			private readonly ProcessWaitHandle exited;

			internal LegacyGameProcess(Process process)
			{
				this.process = process;
				exited = new ProcessWaitHandle(process);
			}

			public int Id => process.Id;

			public WaitHandle ExitedHandle => exited;

			public bool HasExited => process.HasExited;

			public int ExitCode => process.ExitCode;

			public void WaitForExit()
			{
				process.WaitForExit();
			}

			public void Kill()
			{
				try
				{
					process.Kill();
				}
				catch (Exception exception) when (exception is InvalidOperationException || exception is Win32Exception)
				{
					// The game has already exited or is exiting.
				}
			}

			public SafeProcessHandle DuplicateHandle(uint desiredAccess)
			{
				IntPtr current = GetCurrentProcess();
				if (!DuplicateHandle(current, process.Handle, current, out SafeProcessHandle duplicate, desiredAccess, false, 0))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error(), "The game's process handle could not be duplicated.");
				}
				return duplicate;
			}

			[DllImport("kernel32.dll")]
			private static extern IntPtr GetCurrentProcess();

			[DllImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
				out SafeProcessHandle targetHandle, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

			public void Dispose()
			{
				exited.Dispose();
				process.Dispose();
			}
		}

		private sealed class ProcessWaitHandle : WaitHandle
		{
			internal ProcessWaitHandle(Process process)
			{
				SafeWaitHandle = new SafeWaitHandle(process.Handle, ownsHandle: false);
			}
		}
	}

	internal enum LegacyLaunchFailure
	{
		None,
		UnsupportedFolder,
		DriverMissing,
		NoSaveProfile,
		AlreadyRunning,
		SettingsUnavailable,
		StartFailed,
		GameExitedDuringHandshake,
		HandshakeTimedOut,
		BlindSoldierLoadFailed,
		Cancelled
	}

	internal sealed class LegacyLaunchRequest
	{
		internal LegacyInstallation Installation { get; set; }

		internal LegacySaveProfile SaveProfile { get; set; }

		internal string DocumentDirectory { get; set; }

		internal string GameVersionText { get; set; }

		internal string ChannelPrefix { get; set; } = LegacyLauncherProtocol.DefaultPrefix;

		internal TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(60);

		// Signalled when the player closes the launcher while the game starts.
		internal CancellationToken Cancellation { get; set; } = CancellationToken.None;
	}

	internal sealed class LegacyLaunchResult : IDisposable
	{
		internal LegacyLaunchResult(LegacyLaunchFailure failure, string detail, ILegacyGameProcess process, IDisposable channel)
		{
			Failure = failure;
			Detail = detail ?? string.Empty;
			Process = process;
			Channel = channel;
		}

		internal LegacyLaunchFailure Failure { get; }

		internal bool Started => Failure == LegacyLaunchFailure.None;

		internal string Detail { get; }

		internal ILegacyGameProcess Process { get; }

		internal IDisposable Channel { get; }

		public void Dispose()
		{
			Channel?.Dispose();
			Process?.Dispose();
		}
	}

	// The original driver's channel and the save recorder that follows it.
	// Disposing it after the game exits stops the channel first, then signs
	// the saved files again from their final bytes.
	internal sealed class LegacyLaunchSession : IDisposable
	{
		private readonly LegacyLauncherChannel channel;
		private readonly LegacySaveRecorder recorder;
		private int disposed;

		internal LegacyLaunchSession(LegacyLauncherChannel channel, LegacySaveRecorder recorder)
		{
			this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
			this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
		}

		internal LegacySendResult Send(LegacyLauncherMessage message, WaitHandle gameExited, TimeSpan timeout, CancellationToken cancellation)
		{
			return channel.Send(message, gameExited, timeout, cancellation);
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
			{
				return;
			}
			channel.Dispose();
			recorder.Complete();
		}
	}

	// Starts the selected 2013 executable the way its driver expects. FFNx and
	// unrecognized drivers get a direct start, as FF78Launcher does. The
	// original driver gets the launcher handshake with cloud saving off; this
	// launcher never writes saves, updates metadata.xml after the game reports
	// a save as the original launcher does, and only creates settings files
	// that do not exist yet. With Blind Soldier installed it then makes sure
	// the Version proxy is loaded, and closes a game that would stay silent.
	// A start that fails, times out or is cancelled stops the one process it
	// started and releases its launcher objects, so Play can be pressed again.
	internal sealed class LegacyGameLauncher : ILegacyLaunchService
	{
		internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

		private readonly ILegacyGameProcessStarter starter;
		private readonly Action<string> log;
		private readonly IBlindSoldierLoader loader;
		private readonly Func<DateTime> utcNow;

		internal LegacyGameLauncher(ILegacyGameProcessStarter starter, Action<string> log, IBlindSoldierLoader loader = null, Func<DateTime> utcNow = null)
		{
			this.starter = starter ?? throw new ArgumentNullException(nameof(starter));
			this.log = log ?? delegate { };
			this.loader = loader;
			this.utcNow = utcNow ?? (() => DateTime.UtcNow);
		}

		public LegacyLaunchResult Launch(LegacyLaunchRequest request)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}

			LegacyInstallation installation = request.Installation;
			if (installation == null || installation.Problem != LegacyLayoutProblem.None || installation.Selected == null)
			{
				return Fail(LegacyLaunchFailure.UnsupportedFolder, installation?.Problem.ToString());
			}
			if (request.Cancellation.IsCancellationRequested)
			{
				return Fail(LegacyLaunchFailure.Cancelled, "before the game started");
			}

			switch (installation.Driver.Kind)
			{
				case LegacyDriverKind.Missing:
					return Fail(LegacyLaunchFailure.DriverMissing, Path.Combine(installation.Root, "AF3DN.P"));
				case LegacyDriverKind.Original:
					return StartWithHandshake(request, installation);
				default:
					return StartDirectly(request, installation);
			}
		}

		private LegacyLaunchResult StartDirectly(LegacyLaunchRequest request, LegacyInstallation installation)
		{
			log("Starting " + installation.Selected.FullPath + " directly for the " + installation.Driver.Kind + " driver.");
			ILegacyGameProcess process;
			try
			{
				process = starter.Start(installation.Selected.FullPath, installation.Root);
			}
			catch (Exception exception) when (IsStartException(exception))
			{
				return Fail(LegacyLaunchFailure.StartFailed, exception.Message);
			}
			if (request.Cancellation.IsCancellationRequested)
			{
				StopOwnedGame(process, "the start was cancelled while the game was starting");
				return new LegacyLaunchResult(LegacyLaunchFailure.Cancelled, "while the game was starting", process, null);
			}
			return new LegacyLaunchResult(LegacyLaunchFailure.None, string.Empty, process, null);
		}

		private LegacyLaunchResult StartWithHandshake(LegacyLaunchRequest request, LegacyInstallation installation)
		{
			if (request.SaveProfile == null)
			{
				return Fail(LegacyLaunchFailure.NoSaveProfile, request.DocumentDirectory);
			}

			// Invalid start-up information is refused before anything starts.
			var messages = LegacyLauncherProtocol.BuildStartupSequence(
				installation.Selected.LanguageCode,
				request.SaveProfile.FullPath,
				request.DocumentDirectory,
				installation.Root,
				request.GameVersionText);

			var recorder = new LegacySaveRecorder(new LegacySaveMetadataStore(request.SaveProfile.FullPath), utcNow, log);
			LegacyLaunchSession session;
			try
			{
				session = new LegacyLaunchSession(LegacyLauncherChannel.Create(request.ChannelPrefix, log, recorder.Handle), recorder);
			}
			catch (LegacyLauncherChannelBusyException busy)
			{
				return Fail(LegacyLaunchFailure.AlreadyRunning, busy.ObjectName);
			}

			try
			{
				CreateMissingSettings(request.DocumentDirectory);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				session.Dispose();
				return Fail(LegacyLaunchFailure.SettingsUnavailable, exception.Message);
			}

			if (request.Cancellation.IsCancellationRequested)
			{
				session.Dispose();
				return Fail(LegacyLaunchFailure.Cancelled, "before the game started");
			}

			ILegacyGameProcess process;
			try
			{
				log("Starting " + installation.Selected.FullPath + " with the original launcher handshake.");
				process = starter.Start(installation.Selected.FullPath, installation.Root);
			}
			catch (Exception exception) when (IsStartException(exception))
			{
				session.Dispose();
				return Fail(LegacyLaunchFailure.StartFailed, exception.Message);
			}

			try
			{
				return CompleteHandshake(request, installation, messages, process, session);
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				log("The start failed after the game started: " + exception);
				StopOwnedGame(process, "the start failed");
				session.Dispose();
				return new LegacyLaunchResult(LegacyLaunchFailure.StartFailed, exception.Message, process, null);
			}
		}

		private LegacyLaunchResult CompleteHandshake(LegacyLaunchRequest request, LegacyInstallation installation,
			IReadOnlyList<LegacyLauncherMessage> messages, ILegacyGameProcess process, LegacyLaunchSession session)
		{
			foreach (LegacyLauncherMessage message in messages)
			{
				LegacySendResult sent = request.Cancellation.IsCancellationRequested
					? LegacySendResult.Cancelled
					: session.Send(message, process.ExitedHandle, request.MessageTimeout, request.Cancellation);
				switch (sent)
				{
					case LegacySendResult.GameExited:
						session.Dispose();
						string exitCode = process.HasExited ? process.ExitCode.ToString() : "unknown";
						return new LegacyLaunchResult(LegacyLaunchFailure.GameExitedDuringHandshake, exitCode, process, null);
					case LegacySendResult.TimedOut:
						// The stuck game holds the launcher objects open, so it is
						// stopped here; otherwise Play would find it still running.
						StopOwnedGame(process, "it did not read launcher message " + (uint)message.Id);
						session.Dispose();
						return new LegacyLaunchResult(LegacyLaunchFailure.HandshakeTimedOut, ((uint)message.Id).ToString(), process, null);
					case LegacySendResult.Cancelled:
						return Cancel(process, session);
				}
			}
			log("The original launcher handshake completed.");

			// The game has opened every module it imports by now, so a proxy
			// Windows did not load will not appear later on its own.
			BlindSoldierLoadResult load;
			try
			{
				load = loader?.Ensure(installation, process);
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				load = new BlindSoldierLoadResult(BlindSoldierLoadState.Failed, exception.Message);
			}
			if (load != null && load.State == BlindSoldierLoadState.Failed)
			{
				StopOwnedGame(process, "it would run without speech");
				session.Dispose();
				return new LegacyLaunchResult(LegacyLaunchFailure.BlindSoldierLoadFailed, load.Detail, process, null);
			}
			if (request.Cancellation.IsCancellationRequested)
			{
				return Cancel(process, session);
			}
			return new LegacyLaunchResult(LegacyLaunchFailure.None, string.Empty, process, session);
		}

		private LegacyLaunchResult Cancel(ILegacyGameProcess process, LegacyLaunchSession session)
		{
			StopOwnedGame(process, "the start was cancelled");
			session.Dispose();
			return new LegacyLaunchResult(LegacyLaunchFailure.Cancelled, "during the handshake", process, null);
		}

		// Stops only the process this launch started, through its own handle,
		// and waits briefly so its launcher objects are released.
		private void StopOwnedGame(ILegacyGameProcess process, string reason)
		{
			log("Stopping game process " + process.Id + " that this launch started, because " + reason + ".");
			try
			{
				process.Kill();
				if (!process.ExitedHandle.WaitOne(StopTimeout))
				{
					log("Game process " + process.Id + " did not exit within " + StopTimeout.TotalSeconds + " seconds.");
				}
			}
			catch (Exception exception) when (exception is InvalidOperationException || exception is Win32Exception || exception is ObjectDisposedException)
			{
				log("Game process " + process.Id + " could not be confirmed closed: " + exception.Message);
			}
		}

		private void CreateMissingSettings(string documentDirectory)
		{
			var store = new LegacySettingsStore(documentDirectory);
			if (store.ReadDisplay(out LegacyDisplaySettings display) == LegacySettingsFileState.Missing)
			{
				store.WriteDisplay(display);
				log("Created default display settings: " + store.DisplayPath);
			}
			if (store.ReadSound(out LegacySoundSettings sound) == LegacySettingsFileState.Missing)
			{
				store.WriteSound(sound);
				log("Created default sound settings: " + store.SoundPath);
			}
		}

		private LegacyLaunchResult Fail(LegacyLaunchFailure failure, string detail)
		{
			log("Launch refused: " + failure + " " + detail);
			return new LegacyLaunchResult(failure, detail, null, null);
		}

		private static bool IsStartException(Exception exception)
		{
			return exception is Win32Exception || exception is InvalidOperationException ||
				exception is IOException || exception is UnauthorizedAccessException;
		}
	}
}
