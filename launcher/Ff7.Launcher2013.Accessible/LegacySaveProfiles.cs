using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlindSoldier.Ff7Launcher2013
{
	internal sealed class LegacySaveProfile
	{
		internal LegacySaveProfile(string name, string fullPath, int saveFileCount)
		{
			Name = name;
			FullPath = fullPath;
			SaveFileCount = saveFileCount;
		}

		internal string Name { get; }

		internal string FullPath { get; }

		internal int SaveFileCount { get; }
	}

	// The 2013 launcher keeps settings in Documents\Square Enix\FINAL FANTASY VII
	// Steam and saves in a user_<account> folder that its sign-in created. This
	// launcher never renames those folders or writes their saves; it creates
	// user_local only when the player asks, and updates metadata.xml after the
	// game saves, as the original launcher does.
	internal static class LegacySaveProfiles
	{
		internal const string LocalProfileName = "user_local";

		private static readonly Regex SaveFile = new Regex(@"^save\d\d\.ff7$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		internal static string DocumentDirectory(string documentsRoot)
		{
			return Path.Combine(documentsRoot, "Square Enix", "FINAL FANTASY VII Steam");
		}

		internal static IReadOnlyList<LegacySaveProfile> Find(string documentDirectory)
		{
			if (string.IsNullOrEmpty(documentDirectory) || !Directory.Exists(documentDirectory))
			{
				return Array.Empty<LegacySaveProfile>();
			}

			return Directory.GetDirectories(documentDirectory, "user_*")
				.Where(path => Path.GetFileName(path).StartsWith("user_", StringComparison.OrdinalIgnoreCase))
				.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
				.Select(Describe)
				.ToArray();
		}

		// A save folder that needs no Square Enix sign-in. The original driver
		// accepts any folder; this one also gets the metadata.xml the original
		// launcher creates with a new folder, signed with the ID "local".
		// Existing files are never replaced.
		internal static LegacySaveProfile CreateLocal(string documentDirectory)
		{
			if (string.IsNullOrEmpty(documentDirectory))
			{
				throw new ArgumentException("The documents folder is unknown.", nameof(documentDirectory));
			}
			string path = Path.Combine(documentDirectory, LocalProfileName);
			Directory.CreateDirectory(path);
			new LegacySaveMetadataStore(path).CreateIfMissing();
			return Describe(path);
		}

		private static LegacySaveProfile Describe(string path)
		{
			return new LegacySaveProfile(
				Path.GetFileName(path),
				path,
				Directory.GetFiles(path).Count(file => SaveFile.IsMatch(Path.GetFileName(file))));
		}
	}
}
