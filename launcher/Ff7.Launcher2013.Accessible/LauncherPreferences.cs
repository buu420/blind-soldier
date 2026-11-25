using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BlindSoldier.Ff7Launcher2013
{
	// Remembers which user_ save folder the player chose when several exist.
	internal sealed class LauncherPreferences
	{
		private static readonly Regex SafeFolderName = new Regex(@"^user_[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

		internal LauncherPreferences(string path)
		{
			Path = path ?? throw new ArgumentNullException(nameof(path));
		}

		internal string Path { get; }

		internal string ReadSaveFolder()
		{
			try
			{
				if (!File.Exists(Path))
				{
					return null;
				}
				string value = File.ReadAllText(Path).Trim();
				return SafeFolderName.IsMatch(value) && value.IndexOf("..", StringComparison.Ordinal) < 0 ? value : null;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				return null;
			}
		}

		internal void WriteSaveFolder(string name)
		{
			if (name == null || !SafeFolderName.IsMatch(name) || name.IndexOf("..", StringComparison.Ordinal) >= 0)
			{
				throw new ArgumentException("Only a user_ save folder name can be remembered.", nameof(name));
			}
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
			File.WriteAllText(Path, name + Environment.NewLine, new UTF8Encoding(false));
		}
	}
}
