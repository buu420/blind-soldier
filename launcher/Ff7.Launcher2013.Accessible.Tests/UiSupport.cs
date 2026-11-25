using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	// Runs a real form on its own STA thread with a message loop, far off
	// screen and never activated, so tests do not take the player's focus.
	internal sealed class UiHost : IDisposable
	{
		private readonly ManualResetEventSlim shown = new ManualResetEventSlim(false);
		private readonly Thread thread;
		private Form form;
		private Exception failure;

		private UiHost(Func<Form> factory)
		{
			thread = new Thread(() =>
			{
				try
				{
					form = factory();
					form.StartPosition = FormStartPosition.Manual;
					form.Location = new Point(-30000, -30000);
					form.ShowInTaskbar = false;
					form.Shown += delegate { shown.Set(); };
					Application.Run(form);
				}
				catch (Exception exception)
				{
					failure = exception;
					shown.Set();
				}
			})
			{ IsBackground = true, Name = "UI test host" };
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
		}

		internal Form Form => form;

		internal static UiHost Show(Func<Form> factory)
		{
			var host = new UiHost(factory);
			if (!host.shown.Wait(TimeSpan.FromSeconds(20)))
			{
				throw new TestFailure("The form was not shown.");
			}
			if (host.failure != null)
			{
				throw new TestFailure("The form failed to open: " + host.failure);
			}
			return host;
		}

		internal T Invoke<T>(Func<T> function)
		{
			return (T)form.Invoke(function);
		}

		internal void Invoke(Action action)
		{
			form.Invoke(action);
		}

		internal bool WaitFor(Func<bool> condition, TimeSpan timeout)
		{
			DateTime deadline = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < deadline)
			{
				if (!thread.IsAlive)
				{
					return false;
				}
				try
				{
					if (Invoke(condition))
					{
						return true;
					}
				}
				catch (ObjectDisposedException)
				{
					return false;
				}
				catch (InvalidOperationException)
				{
					return false;
				}
				Thread.Sleep(20);
			}
			return false;
		}

		internal bool WaitForClose(TimeSpan timeout)
		{
			return thread.Join(timeout);
		}

		public void Dispose()
		{
			if (thread.IsAlive)
			{
				try
				{
					form.BeginInvoke(new Action(() => form.Close()));
				}
				catch (InvalidOperationException)
				{
				}
				thread.Join(TimeSpan.FromSeconds(10));
			}
			shown.Dispose();
		}
	}

	internal sealed class QuietMainForm : MainForm
	{
		internal QuietMainForm(LauncherModel model) : base(model)
		{
		}

		protected override bool ShowWithoutActivation => true;
	}

	internal sealed class QuietSettingsForm : SettingsForm
	{
		internal QuietSettingsForm(LauncherText text, SettingsEditRequest request) : base(text, request)
		{
		}

		protected override bool ShowWithoutActivation => true;
	}

	internal sealed class RecordingDialogs : ILauncherDialogs
	{
		private readonly List<string> errors = new List<string>();
		private readonly List<string> information = new List<string>();

		internal IReadOnlyList<string> Errors { get { lock (errors) { return errors.ToArray(); } } }

		internal IReadOnlyList<string> Information { get { lock (information) { return information.ToArray(); } } }

		public void ShowError(IWin32Window owner, string text)
		{
			lock (errors) { errors.Add(text); }
		}

		public void ShowInformation(IWin32Window owner, string text)
		{
			lock (information) { information.Add(text); }
		}
	}

	internal sealed class RecordingAnnouncer : IAnnouncer
	{
		private readonly List<string> announcements = new List<string>();

		internal IReadOnlyList<string> Announcements { get { lock (announcements) { return announcements.ToArray(); } } }

		public void Announce(Control anchor, string text)
		{
			lock (announcements) { announcements.Add(text); }
		}
	}

	internal sealed class FakeLaunchService : ILegacyLaunchService
	{
		private readonly Func<LegacyLaunchRequest, LegacyLaunchResult> respond;
		private readonly List<LegacyLaunchRequest> requests = new List<LegacyLaunchRequest>();

		internal FakeLaunchService(Func<LegacyLaunchRequest, LegacyLaunchResult> respond)
		{
			this.respond = respond;
		}

		internal IReadOnlyList<LegacyLaunchRequest> Requests { get { lock (requests) { return requests.ToArray(); } } }

		public LegacyLaunchResult Launch(LegacyLaunchRequest request)
		{
			lock (requests) { requests.Add(request); }
			return respond(request);
		}
	}

	internal sealed class ControllableProcess : ILegacyGameProcess
	{
		private readonly ManualResetEvent exited = new ManualResetEvent(false);
		private int exitCode;

		public int Id => 5150;

		public WaitHandle ExitedHandle => exited;

		public bool HasExited => exited.WaitOne(0);

		public int ExitCode => exitCode;

		internal bool Disposed { get; private set; }

		internal bool Killed { get; private set; }

		internal void Exit(int code)
		{
			exitCode = code;
			exited.Set();
		}

		public void WaitForExit()
		{
			exited.WaitOne();
		}

		public void Kill()
		{
			Killed = true;
			Exit(-1);
		}

		public Microsoft.Win32.SafeHandles.SafeProcessHandle DuplicateHandle(uint desiredAccess)
		{
			throw new NotSupportedException("A fake game has no process handle.");
		}

		public void Dispose()
		{
			Disposed = true;
		}
	}

	// A launch that stays in progress until the test lets it finish, as a slow
	// handshake does. finish builds the result from the request it was given.
	internal sealed class GatedLaunchService : ILegacyLaunchService, IDisposable
	{
		private readonly ManualResetEventSlim entered = new ManualResetEventSlim(false);
		private readonly ManualResetEventSlim release = new ManualResetEventSlim(false);
		private readonly Func<LegacyLaunchRequest, LegacyLaunchResult> finish;

		internal GatedLaunchService(Func<LegacyLaunchRequest, LegacyLaunchResult> finish)
		{
			this.finish = finish;
		}

		internal LegacyLaunchRequest Request { get; private set; }

		internal bool WaitUntilEntered(TimeSpan timeout)
		{
			return entered.Wait(timeout);
		}

		internal void Release()
		{
			release.Set();
		}

		public LegacyLaunchResult Launch(LegacyLaunchRequest request)
		{
			Request = request;
			entered.Set();
			release.Wait(TimeSpan.FromSeconds(20));
			return finish(request);
		}

		public void Dispose()
		{
			release.Set();
			entered.Dispose();
		}
	}

	internal sealed class RecordingStarter : ILegacyGameProcessStarter
	{
		internal List<string> Starts { get; } = new List<string>();

		internal ControllableProcess Process { get; } = new ControllableProcess();

		public ILegacyGameProcess Start(string executablePath, string workingDirectory)
		{
			Starts.Add(executablePath + "|" + workingDirectory);
			return Process;
		}
	}

	internal sealed class FakeSettingsPresenter : ISettingsPresenter
	{
		private readonly Func<SettingsEditRequest, SettingsEditResult> respond;

		internal FakeSettingsPresenter(Func<SettingsEditRequest, SettingsEditResult> respond)
		{
			this.respond = respond;
		}

		internal List<SettingsEditRequest> Requests { get; } = new List<SettingsEditRequest>();

		public SettingsEditResult Edit(IWin32Window owner, LauncherText text, SettingsEditRequest request)
		{
			Requests.Add(request);
			return respond(request);
		}
	}

	internal sealed class FixedDisplayModes : IDisplayModeSource
	{
		private readonly IReadOnlyList<DisplayMode> modes;

		internal FixedDisplayModes(params DisplayMode[] modes)
		{
			this.modes = modes;
		}

		public IReadOnlyList<DisplayMode> GetModes()
		{
			return modes;
		}
	}

	internal static class UiControls
	{
		// The controls reached by pressing Tab from the active control, in order.
		internal static IReadOnlyList<Control> TabSequence(Form form, int count)
		{
			var sequence = new List<Control>();
			for (int index = 0; index < count; index++)
			{
				form.SelectNextControl(form.ActiveControl, true, true, true, true);
				sequence.Add(form.ActiveControl);
			}
			return sequence;
		}

		internal static IEnumerable<string> Names(IEnumerable<Control> controls)
		{
			return controls.Select(control => control == null ? "<none>" : control.AccessibleName ?? control.Name);
		}
	}
}
