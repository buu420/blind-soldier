using System;
using System.Collections.Generic;
using System.Linq;

namespace BlindSoldier.Ff7Launcher2013
{
	internal static class LauncherMessages
	{
		internal static string DescribeFailure(LegacyLaunchResult result, LegacyLaunchRequest request, LauncherText text, string logPath)
		{
			string cause;
			string action;
			switch (result.Failure)
			{
				case LegacyLaunchFailure.UnsupportedFolder:
					cause = text[TextKey.CauseUnsupportedFolder];
					action = text[TextKey.ActionUnsupportedFolder];
					break;
				case LegacyLaunchFailure.DriverMissing:
					cause = text[TextKey.CauseDriverMissing];
					action = text[TextKey.ActionVerifyFiles];
					break;
				case LegacyLaunchFailure.NoSaveProfile:
					cause = text.Format(TextKey.CauseNoSaveProfile, request?.DocumentDirectory ?? result.Detail);
					action = text[TextKey.ActionNoSaveProfile];
					break;
				case LegacyLaunchFailure.AlreadyRunning:
					cause = text[TextKey.CauseAlreadyRunning];
					action = text[TextKey.ActionAlreadyRunning];
					break;
				case LegacyLaunchFailure.SettingsUnavailable:
					cause = text.Format(TextKey.CauseSettings, result.Detail);
					action = text[TextKey.ActionSettings];
					break;
				case LegacyLaunchFailure.StartFailed:
					cause = text.Format(TextKey.CauseStartFailed, request?.Installation?.Selected?.FileName ?? LegacyInstallation.BlindSoldierExecutableName, result.Detail);
					action = text[TextKey.ActionStartFailed];
					break;
				case LegacyLaunchFailure.GameExitedDuringHandshake:
					cause = text.Format(TextKey.CauseExitedHandshake, result.Detail);
					action = text[TextKey.ActionCheckLog];
					break;
				case LegacyLaunchFailure.HandshakeTimedOut:
					cause = text[TextKey.CauseHandshakeTimeout];
					action = text[TextKey.ActionHandshakeTimeout];
					break;
				case LegacyLaunchFailure.BlindSoldierLoadFailed:
					cause = text.Format(TextKey.CauseBlindSoldierLoadFailed, result.Detail);
					action = text[TextKey.ActionBlindSoldierLoadFailed];
					break;
				case LegacyLaunchFailure.Cancelled:
					cause = text[TextKey.CauseStartCancelled];
					action = text[TextKey.ActionStartCancelled];
					break;
				default:
					throw new ArgumentException("A started game is not a failure.", nameof(result));
			}
			return Compose(cause, action, text, logPath);
		}

		internal static string DescribeEarlyExit(TimeSpan elapsed, int exitCode, LauncherText text, string logPath)
		{
			int seconds = Math.Max(0, (int)elapsed.TotalSeconds);
			return Compose(text.Format(TextKey.CauseExitedEarly, seconds, exitCode), text[TextKey.ActionCheckLog], text, logPath);
		}

		internal static string StartupAnnouncement(IReadOnlyList<StatusLine> lines, LegacyInstallation installation, IReadOnlyList<LegacySaveProfile> profiles, LauncherText text)
		{
			if (installation.Problem != LegacyLayoutProblem.None)
			{
				return lines.Count > 0 ? lines[0].Text : text[TextKey.CauseUnsupportedFolder];
			}
			string ready = LauncherStatus.NeedsLocalProfile(installation, profiles) ? text[TextKey.ReadyNeedsSaveFolder] : text[TextKey.Ready];
			return ready + string.Concat(lines
				.Where(line => line.Severity == StatusSeverity.Warning)
				.Select(line => " " + text.Format(TextKey.WarningPrefix, line.Text)));
		}

		private static string Compose(string cause, string action, LauncherText text, string logPath)
		{
			return string.Join(Environment.NewLine,
				text[TextKey.ErrorHeading],
				text.Format(TextKey.ErrorCause, cause),
				text.Format(TextKey.ErrorAction, action),
				text.Format(TextKey.ErrorLog, logPath));
		}
	}
}
