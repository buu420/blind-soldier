using System;
using System.Diagnostics;
using System.IO;

namespace BlindSoldier.Ff7Launcher2013
{
	internal enum LegacyDriverKind
	{
		Missing,
		Original,
		Ffnx,
		Unrecognized
	}

	// AF3DN.P beside the 2013 executable decides how the game starts. The
	// original DotEmu driver refuses to run without the launcher handshake.
	// FFNx 1.25.2 never opens the launcher objects and starts on its own.
	internal sealed class LegacyDriverInfo
	{
		// FF78Launcher treats AF3DN.P above 1 MiB as FFNx and anything else as
		// the original driver. FFNx is identified here by its version resource
		// first; size only separates the original driver from unknown ones.
		internal const long OriginalDriverMaximumLength = 1024L * 1024L;

		private LegacyDriverInfo(LegacyDriverKind kind, string version, long length)
		{
			Kind = kind;
			Version = version ?? string.Empty;
			Length = length;
		}

		internal LegacyDriverKind Kind { get; }

		internal string Version { get; }

		internal long Length { get; }

		internal static LegacyDriverInfo Classify(long length, string productName, string internalName, string productVersion)
		{
			if (IsFfnxName(productName) || IsFfnxName(internalName))
			{
				return new LegacyDriverInfo(LegacyDriverKind.Ffnx, (productVersion ?? string.Empty).Trim(), length);
			}
			return length <= OriginalDriverMaximumLength
				? new LegacyDriverInfo(LegacyDriverKind.Original, string.Empty, length)
				: new LegacyDriverInfo(LegacyDriverKind.Unrecognized, string.Empty, length);
		}

		internal static LegacyDriverInfo Inspect(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return new LegacyDriverInfo(LegacyDriverKind.Missing, string.Empty, 0);
			}

			long length = new FileInfo(path).Length;
			FileVersionInfo info = null;
			try
			{
				info = FileVersionInfo.GetVersionInfo(path);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				info = null;
			}
			return Classify(length, info?.ProductName, info?.InternalName, info?.ProductVersion);
		}

		private static bool IsFfnxName(string value)
		{
			return string.Equals((value ?? string.Empty).Trim(), "FFNx", StringComparison.OrdinalIgnoreCase);
		}
	}
}
