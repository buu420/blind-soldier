using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FF7_Launcher
{
	internal interface IGameProcessRunner
	{
		int Run(ProcessStartInfo startInfo);
	}

	internal sealed class GameProcessRunner : IGameProcessRunner
	{
		public int Run(ProcessStartInfo startInfo)
		{
			using (Process process = Process.Start(startInfo))
			{
				if (process == null)
				{
					throw new InvalidOperationException("Windows did not create the Blind Soldier bootstrap process.");
				}

				process.WaitForExit();
				return process.ExitCode;
			}
		}
	}

	internal sealed class BlindSoldierGameLauncher
	{
		internal const string BootstrapRelativePath =
			@"Blind-Soldier\Bootstrap\x64\Blind-Soldier-Bootstrap-x64.exe";

		private static readonly IDictionary<int, string> ExitCauses =
			new Dictionary<int, string>
			{
				{ 10, "The Blind Soldier bootstrap received invalid launch information." },
				{ 11, "This FFVII executable is not a supported game host." },
				{ 12, "A required Blind Soldier payload file is missing." },
				{ 13, "The bootstrap could not reserve its shared pointer data." },
				{ 14, "FFVII could not be started or stopped before initialization completed." },
				{ 15, "The bootstrap, game, or payload has the wrong processor architecture." },
				{ 16, "The bootstrap could not create Reloaded's application configuration." },
				{ 17, "The Blind Soldier payload could not be injected into FFVII." },
				{ 18, "FFVII was prepared but could not be resumed." },
				{ 19, "The private Blind Soldier .NET runtime could not be loaded." },
				{ 20, "The injected accessibility mod did not report that it was ready." }
			};

		private readonly IGameProcessRunner processRunner;
		private readonly Func<Guid> createLaunchId;

		internal BlindSoldierGameLauncher()
			: this(new GameProcessRunner(), Guid.NewGuid)
		{
		}

		internal BlindSoldierGameLauncher(
			IGameProcessRunner processRunner,
			Func<Guid> createLaunchId)
		{
			this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
			this.createLaunchId = createLaunchId ?? throw new ArgumentNullException(nameof(createLaunchId));
		}

		internal bool TryLaunch(
			string launcherRoot,
			string language,
			out string accessibleError)
		{
			accessibleError = string.Empty;
			Guid launchId = createLaunchId();
			string root;

			try
			{
				root = Path.GetFullPath(launcherRoot ?? string.Empty)
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			}
			catch (Exception exception)
			{
				accessibleError = BuildError(
					"The launcher folder is invalid. " + exception.Message,
					"Extract Blind Soldier into the FFVII game folder, then start the launcher again.",
					Path.Combine(Path.GetTempPath(), "Blind-Soldier-Bootstrap-x64-" + launchId.ToString("D") + ".log"));
				return false;
			}

			string gamePath = Path.Combine(root, "FFVII.exe");
			string bootstrapPath = Path.Combine(root, BootstrapRelativePath);
			string logPath = Path.Combine(
				root,
				"Blind-Soldier",
				"Logs",
				"Blind-Soldier-Bootstrap-x64-" + launchId.ToString("D") + ".log");

			if (!File.Exists(gamePath))
			{
				accessibleError = BuildError(
					"The FFVII game executable is missing: " + gamePath,
					"Extract the portable package directly into the FFVII game folder.",
					logPath);
				return false;
			}

			if (!File.Exists(bootstrapPath))
			{
				accessibleError = BuildError(
					"The Blind Soldier x64 bootstrap is missing: " + bootstrapPath,
					"Extract the complete Blind Soldier portable package into this FFVII folder again.",
					logPath);
				return false;
			}

			var arguments = new StringBuilder();
			arguments.Append("--launch --root ");
			arguments.Append(QuoteWindowsArgument(root));
			arguments.Append(" --game ");
			arguments.Append(QuoteWindowsArgument(gamePath));
			arguments.Append(" --launch-id ");
			arguments.Append(launchId.ToString("D"));
			if (string.Equals(language, "jp", StringComparison.OrdinalIgnoreCase))
			{
				arguments.Append(" --game-arguments jp");
			}

			var startInfo = new ProcessStartInfo
			{
				FileName = bootstrapPath,
				Arguments = arguments.ToString(),
				WorkingDirectory = root,
				UseShellExecute = false,
				CreateNoWindow = false
			};

			try
			{
				int exitCode = processRunner.Run(startInfo);
				if (exitCode == 0)
				{
					return true;
				}

				string cause;
				if (!ExitCauses.TryGetValue(exitCode, out cause))
				{
					cause = "The Blind Soldier bootstrap stopped with unexpected exit code " + exitCode + ".";
				}

				accessibleError = BuildError(
					cause,
					"Check the log below, restore any quarantined files, and extract the complete package again if needed.",
					logPath);
				return false;
			}
			catch (Exception exception)
			{
				accessibleError = BuildError(
					"Windows could not start the Blind Soldier bootstrap. " + exception.Message,
					"Check the log path and security software, then extract the complete package again.",
					logPath);
				return false;
			}
		}

		private static string BuildError(string cause, string action, string logPath)
		{
			return "Blind Soldier could not start Final Fantasy VII." + Environment.NewLine +
				"Cause: " + cause + Environment.NewLine +
				"Action: " + action + Environment.NewLine +
				"Log: " + Path.GetFullPath(logPath);
		}

		private static string QuoteWindowsArgument(string value)
		{
			if (value == null)
			{
				return "\"\"";
			}

			var result = new StringBuilder(value.Length + 2);
			result.Append('"');
			int backslashes = 0;
			foreach (char character in value)
			{
				if (character == '\\')
				{
					backslashes++;
					continue;
				}

				if (character == '"')
				{
					result.Append('\\', backslashes * 2 + 1);
					result.Append('"');
					backslashes = 0;
					continue;
				}

				result.Append('\\', backslashes);
				backslashes = 0;
				result.Append(character);
			}

			result.Append('\\', backslashes * 2);
			result.Append('"');
			return result.ToString();
		}
	}
}
