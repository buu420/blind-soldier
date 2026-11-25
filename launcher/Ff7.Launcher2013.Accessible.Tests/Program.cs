using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal sealed class TestRunner
	{
		private readonly List<KeyValuePair<string, Action>> tests = new List<KeyValuePair<string, Action>>();

		internal void Add(string name, Action test)
		{
			tests.Add(new KeyValuePair<string, Action>(name, test));
		}

		internal int RunAll(string filter)
		{
			int failures = 0;
			int executed = 0;
			foreach (var test in tests)
			{
				if (!string.IsNullOrEmpty(filter) && test.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				executed++;
				var watch = Stopwatch.StartNew();
				try
				{
					test.Value();
					Console.WriteLine("PASS " + test.Key + " (" + watch.ElapsedMilliseconds + " ms)");
				}
				catch (Exception exception)
				{
					failures++;
					Console.WriteLine("FAIL " + test.Key + ": " + exception);
				}
			}
			Console.WriteLine(failures == 0
				? "All " + executed + " legacy launcher test(s) passed."
				: failures + " of " + executed + " legacy launcher test(s) failed.");
			return failures == 0 && executed > 0 ? 0 : 1;
		}
	}

	internal static class Program
	{
		[STAThread]
		private static int Main(string[] args)
		{
			// Must not touch launcher types: copies of this executable act as a
			// fake ff7_en.exe in scratch game folders that hold a dummy launcher.
			if (CrossProcessLegacyGame.IsFakeGameProcess())
			{
				return CrossProcessLegacyGame.Run();
			}
			return RunTests(args.Length > 0 ? args[0] : null);
		}

		private static int RunTests(string filter)
		{
			var runner = new TestRunner();
			CoreTests.Register(runner);
			ProtocolTests.Register(runner);
			SaveMetadataTests.Register(runner);
			LaunchTests.Register(runner);
			LoaderTests.Register(runner);
			UiTests.Register(runner);
			return runner.RunAll(filter);
		}
	}
}
