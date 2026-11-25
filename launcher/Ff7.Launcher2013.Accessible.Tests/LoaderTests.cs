using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal static class LoaderTests
	{
		internal static void Register(TestRunner runner)
		{
			runner.Add("the loader does nothing when Blind Soldier cannot run in this game", LoaderNotApplicable);
			runner.Add("the loader leaves an already loaded Version proxy alone", LoaderKeepsLoadedProxy);
			runner.Add("the loader loads the sibling Version proxy when Windows did not", LoaderLoadsMissingProxy);
			runner.Add("the loader reports a proxy that could not be loaded", LoaderReportsFailures);
			runner.Add("a game that closes by itself is not reported as a loader failure", LoaderIgnoresClosedGame);
			runner.Add("a library is loaded by full path into a real running game process", RemoteLoadIntoRealProcess);
			runner.Add("module checks read the game this launch started, never another process ID", ModuleReaderUsesOwnedGame);
			runner.Add("the proxy is loaded only into the game this launch started", RemoteLoadUsesOwnedGame);
		}

		private static LegacyGameTree BlindSoldierGame(string label, string executable = "ff7_en.exe")
		{
			var game = new LegacyGameTree(label).WithStandard2013Files(executable).WithDriverBytes(1000);
			foreach (string file in new[]
			{
				"version.dll",
				@"Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe",
				@"Blind-Soldier\Runtime\dotnet\x86\host\fxr\9.0.8\hostfxr.dll",
				@"Reloaded-II\portable.txt",
				@"Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll",
				@"Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll"
			})
			{
				game.WriteFile(file, 16);
			}
			return game;
		}

		private static void LoaderNotApplicable()
		{
			using (var missing = new LegacyGameTree("loader-none").WithStandard2013Files().WithDriverBytes(1000))
			using (var french = BlindSoldierGame("loader-french", "ff7_fr.exe"))
			{
				foreach (var game in new[] { missing, french })
				{
					var modules = new FakeModules(new[] { @"C:\Windows\SysWOW64\kernel32.dll" });
					var remote = new FakeRemoteLoader(modules, null);
					BlindSoldierLoadResult result = new BlindSoldierLoader(modules, remote, _ => { })
						.Ensure(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), new FakeGame());
					Check.Equal(BlindSoldierLoadState.NotApplicable, result.State, game.Root + " state");
					Check.Equal(0, remote.Loads.Count, "nothing loaded");
					Check.Equal(0, modules.Reads, "modules not even read");
				}
			}
		}

		private static void LoaderKeepsLoadedProxy()
		{
			using (var game = BlindSoldierGame("loader-loaded"))
			{
				// Windows reports module paths in its own case; the comparison is by file.
				var modules = new FakeModules(new[] { @"C:\Windows\SysWOW64\kernel32.dll", Path.Combine(game.Root, "VERSION.DLL").ToUpperInvariant() });
				var remote = new FakeRemoteLoader(modules, null);
				var log = new List<string>();
				BlindSoldierLoadResult result = new BlindSoldierLoader(modules, remote, log.Add)
					.Ensure(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), new FakeGame());
				Check.Equal(BlindSoldierLoadState.AlreadyLoaded, result.State, "state");
				Check.Equal(0, remote.Loads.Count, "no second copy");
				Check.True(log.Any(entry => entry.Contains("already")), "logged: " + string.Join(" | ", log));
			}
		}

		private static void LoaderLoadsMissingProxy()
		{
			using (var game = BlindSoldierGame("loader-load"))
			{
				string proxy = Path.Combine(game.Root, "version.dll");
				var modules = new FakeModules(new[] { @"C:\Windows\SysWOW64\kernel32.dll", @"C:\Windows\SysWOW64\version.dll" });
				var remote = new FakeRemoteLoader(modules, null);
				var process = new FakeGame();
				BlindSoldierLoadResult result = new BlindSoldierLoader(modules, remote, _ => { })
					.Ensure(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), process);
				Check.Equal(BlindSoldierLoadState.Loaded, result.State, "state: " + result.Detail);
				Check.Sequence(new[] { proxy }, remote.Loads, "loaded the sibling proxy by full path");
				Check.True(ReferenceEquals(process, remote.Processes.Single()), "into the started game");
			}
		}

		private static void LoaderReportsFailures()
		{
			using (var game = BlindSoldierGame("loader-fail"))
			{
				var installation = LegacyInstallation.Detect(game.Root, game.ManifestPath, true);
				var refused = new FakeModules(new[] { @"C:\Windows\SysWOW64\kernel32.dll" });
				BlindSoldierLoadResult denied = new BlindSoldierLoader(refused, new FakeRemoteLoader(refused, new InvalidOperationException("CreateRemoteThread failed with error 5.")), _ => { })
					.Ensure(installation, new FakeGame());
				Check.Equal(BlindSoldierLoadState.Failed, denied.State, "refused state");
				Check.Equal("CreateRemoteThread failed with error 5.", denied.Detail, "refused detail");

				var silent = new FakeModules(new[] { @"C:\Windows\SysWOW64\kernel32.dll" });
				var ignored = new FakeRemoteLoader(silent, null) { AddModule = false };
				BlindSoldierLoadResult absent = new BlindSoldierLoader(silent, ignored, _ => { }).Ensure(installation, new FakeGame());
				Check.Equal(BlindSoldierLoadState.Failed, absent.State, "absent state");
				Check.True(absent.Detail.Contains("version.dll"), "absent detail: " + absent.Detail);

				var unreadable = new FakeModules(null);
				BlindSoldierLoadResult unknown = new BlindSoldierLoader(unreadable, new FakeRemoteLoader(unreadable, null), _ => { }).Ensure(installation, new FakeGame());
				Check.Equal(BlindSoldierLoadState.Failed, unknown.State, "unreadable modules state");
			}
		}

		private static void LoaderIgnoresClosedGame()
		{
			using (var game = BlindSoldierGame("loader-closed"))
			{
				// Started outside Steam, the game restarts itself through Steam and exits.
				var process = new FakeGame();
				process.Kill();
				var modules = new FakeModules(null);
				var remote = new FakeRemoteLoader(modules, null);
				BlindSoldierLoadResult result = new BlindSoldierLoader(modules, remote, _ => { })
					.Ensure(LegacyInstallation.Detect(game.Root, game.ManifestPath, true), process);
				Check.Equal(BlindSoldierLoadState.NotApplicable, result.State, "state: " + result.Detail);
				Check.Equal(0, remote.Loads.Count, "nothing loaded");
			}
		}

		private static void RemoteLoadIntoRealProcess()
		{
			using (var game = new LegacyGameTree("loader-real").WithStandard2013Files().WithDriverBytes(1000))
			{
				// A renamed copy of Windows' own Version library: it has no side
				// effects and no other module in the process can load it by name.
				string probe = Path.Combine(game.Root, "bs-remote-load-probe.dll");
				File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "version.dll"), probe, true);
				File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(game.Root, "ff7_en.exe"), true);
				File.WriteAllLines(Path.Combine(game.Root, CrossProcessLegacyGame.SidecarName), new[] { ProtocolTests.UniquePrefix(), "wait:30000" });
				using (ILegacyGameProcess process = new LegacyGameProcessStarter().Start(Path.Combine(game.Root, "ff7_en.exe"), game.Root))
				{
					try
					{
						var reader = new ProcessModuleReader();
						Check.True(SpinWait.SpinUntil(() => reader.Read(process).Any(path => path.EndsWith("mscoree.dll", StringComparison.OrdinalIgnoreCase)), TimeSpan.FromSeconds(20)), "the game process started");
						Check.False(reader.Read(process).Any(path => string.Equals(path, probe, StringComparison.OrdinalIgnoreCase)), "probe not loaded before");
						new RemoteLibraryLoader().Load(process, probe, TimeSpan.FromSeconds(20));
						Check.True(reader.Read(process).Any(path => string.Equals(path, probe, StringComparison.OrdinalIgnoreCase)), "probe loaded after");
						Check.Throws<InvalidOperationException>(() => new RemoteLibraryLoader().Load(process, Path.Combine(game.Root, "missing.dll"), TimeSpan.FromSeconds(20)), "a missing library is reported");
					}
					finally
					{
						process.Kill();
						process.WaitForExit();
					}
				}
			}
		}

		// Two copies of the fake game: the one a launch started and an unrelated one.
		private sealed class TwoGames : IDisposable
		{
			private readonly LegacyGameTree ownedTree;
			private readonly LegacyGameTree otherTree;

			internal TwoGames(string label, string ownedMode)
			{
				ownedTree = new LegacyGameTree(label).WithStandard2013Files().WithDriverBytes(1000);
				otherTree = new LegacyGameTree(label + "-other").WithStandard2013Files().WithDriverBytes(1000);
				string probe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "version.dll");
				foreach (var tree in new[] { ownedTree, otherTree })
				{
					File.Copy(probe, Path.Combine(tree.Root, "bs-owned-probe.dll"), true);
					File.Copy(probe, Path.Combine(tree.Root, "bs-late-probe.dll"), true);
					File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(tree.Root, "ff7_en.exe"), true);
				}
				File.WriteAllLines(Path.Combine(ownedTree.Root, CrossProcessLegacyGame.SidecarName), new[] { ProtocolTests.UniquePrefix(), ownedMode });
				File.WriteAllLines(Path.Combine(otherTree.Root, CrossProcessLegacyGame.SidecarName), new[] { ProtocolTests.UniquePrefix(), "wait:30000" });
				Owned = new LegacyGameProcessStarter().Start(Path.Combine(ownedTree.Root, "ff7_en.exe"), ownedTree.Root);
				Other = new LegacyGameProcessStarter().Start(Path.Combine(otherTree.Root, "ff7_en.exe"), otherTree.Root);
			}

			internal ILegacyGameProcess Owned { get; }

			internal ILegacyGameProcess Other { get; }

			internal string OwnedRoot => ownedTree.Root;

			// Every module path, read independently of the launcher's reader.
			internal static IReadOnlyList<string> Modules(ILegacyGameProcess process)
			{
				using (var target = System.Diagnostics.Process.GetProcessById(process.Id))
				{
					return target.Modules.Cast<System.Diagnostics.ProcessModule>().Select(module => module.FileName).ToArray();
				}
			}

			internal static bool Has(ILegacyGameProcess process, string fileName)
			{
				return Modules(process).Any(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
			}

			public void Dispose()
			{
				foreach (var process in new[] { Owned, Other })
				{
					process.Kill();
					process.WaitForExit();
					process.Dispose();
				}
				ownedTree.Dispose();
				otherTree.Dispose();
			}
		}

		// The started game, reporting the process ID of a different process.
		private sealed class ReportsOtherId : ILegacyGameProcess
		{
			private readonly ILegacyGameProcess owned;
			private readonly ILegacyGameProcess other;

			internal ReportsOtherId(ILegacyGameProcess owned, ILegacyGameProcess other)
			{
				this.owned = owned;
				this.other = other;
			}

			public int Id => other.Id;

			public WaitHandle ExitedHandle => owned.ExitedHandle;

			public bool HasExited => owned.HasExited;

			public int ExitCode => owned.ExitCode;

			public void WaitForExit()
			{
				owned.WaitForExit();
			}

			public void Kill()
			{
				owned.Kill();
			}

			public Microsoft.Win32.SafeHandles.SafeProcessHandle DuplicateHandle(uint desiredAccess)
			{
				return owned.DuplicateHandle(desiredAccess);
			}

			public void Dispose()
			{
			}
		}

		private static void ModuleReaderUsesOwnedGame()
		{
			using (var games = new TwoGames("loader-owned-read", "load:bs-owned-probe.dll:30000"))
			{
				Check.True(SpinWait.SpinUntil(() => TwoGames.Has(games.Owned, "bs-owned-probe.dll"), TimeSpan.FromSeconds(20)), "the started game loaded its probe");
				Check.False(TwoGames.Has(games.Other, "bs-owned-probe.dll"), "the other game did not");
				IReadOnlyList<string> modules = new ProcessModuleReader().Read(new ReportsOtherId(games.Owned, games.Other));
				Check.True(modules.Any(path => string.Equals(path, Path.Combine(games.OwnedRoot, "bs-owned-probe.dll"), StringComparison.OrdinalIgnoreCase)),
					"read from the started game: " + string.Join(" | ", modules.Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && path.Contains("bs-"))));
			}
		}

		private static void RemoteLoadUsesOwnedGame()
		{
			using (var games = new TwoGames("loader-owned-load", "wait:30000"))
			{
				Check.True(SpinWait.SpinUntil(() => TwoGames.Has(games.Owned, "mscoree.dll") && TwoGames.Has(games.Other, "mscoree.dll"), TimeSpan.FromSeconds(20)), "both games started");
				new RemoteLibraryLoader().Load(new ReportsOtherId(games.Owned, games.Other), Path.Combine(games.OwnedRoot, "bs-late-probe.dll"), TimeSpan.FromSeconds(20));
				Check.True(TwoGames.Has(games.Owned, "bs-late-probe.dll"), "loaded into the started game");
				Check.False(TwoGames.Has(games.Other, "bs-late-probe.dll"), "never loaded into the other game");
			}
		}

		private sealed class FakeModules : IGameModuleReader
		{
			private readonly List<string> modules;

			internal FakeModules(IEnumerable<string> modules)
			{
				this.modules = modules?.ToList();
			}

			internal int Reads { get; private set; }

			internal void Add(string path)
			{
				modules.Add(path);
			}

			public IReadOnlyList<string> Read(ILegacyGameProcess process)
			{
				Reads++;
				if (modules == null)
				{
					throw new InvalidOperationException("The game's modules could not be listed.");
				}
				return modules.ToArray();
			}
		}

		private sealed class FakeRemoteLoader : IRemoteLibraryLoader
		{
			private readonly FakeModules modules;
			private readonly Exception failure;

			internal FakeRemoteLoader(FakeModules modules, Exception failure)
			{
				this.modules = modules;
				this.failure = failure;
			}

			internal bool AddModule { get; set; } = true;

			internal List<string> Loads { get; } = new List<string>();

			internal List<ILegacyGameProcess> Processes { get; } = new List<ILegacyGameProcess>();

			public void Load(ILegacyGameProcess process, string libraryPath, TimeSpan timeout)
			{
				Loads.Add(libraryPath);
				Processes.Add(process);
				if (failure != null)
				{
					throw failure;
				}
				if (AddModule)
				{
					modules.Add(libraryPath);
				}
			}
		}

		private sealed class FakeGame : ILegacyGameProcess
		{
			private readonly ManualResetEvent exited = new ManualResetEvent(false);

			public int Id => 4343;

			public WaitHandle ExitedHandle => exited;

			public bool HasExited => exited.WaitOne(0);

			public int ExitCode => 0;

			public void WaitForExit()
			{
				exited.WaitOne();
			}

			public void Kill()
			{
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
