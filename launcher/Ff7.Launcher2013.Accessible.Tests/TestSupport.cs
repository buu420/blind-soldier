using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CSharp;

namespace BlindSoldier.Ff7Launcher2013.Tests
{
	internal sealed class TestFailure : Exception
	{
		internal TestFailure(string message) : base(message)
		{
		}
	}

	internal static class Check
	{
		internal static void Equal<T>(T expected, T actual, string label)
		{
			if (!EqualityComparer<T>.Default.Equals(expected, actual))
			{
				throw new TestFailure(label + ": expected <" + expected + "> but was <" + actual + ">.");
			}
		}

		internal static void True(bool condition, string label)
		{
			if (!condition)
			{
				throw new TestFailure(label + ": expected true.");
			}
		}

		internal static void False(bool condition, string label)
		{
			if (condition)
			{
				throw new TestFailure(label + ": expected false.");
			}
		}

		internal static void Null(object value, string label)
		{
			if (value != null)
			{
				throw new TestFailure(label + ": expected null but was <" + value + ">.");
			}
		}

		internal static void Bytes(byte[] expected, byte[] actual, string label)
		{
			if (actual == null || !expected.SequenceEqual(actual))
			{
				throw new TestFailure(label + ": expected " + Hex(expected) + " but was " + (actual == null ? "null" : Hex(actual)) + ".");
			}
		}

		internal static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string label)
		{
			string[] left = expected.ToArray();
			string[] right = actual.ToArray();
			if (!left.SequenceEqual(right, StringComparer.Ordinal))
			{
				throw new TestFailure(label + ":\nexpected [" + string.Join(" | ", left) + "]\nbut was  [" + string.Join(" | ", right) + "].");
			}
		}

		internal static TException Throws<TException>(Action action, string label)
			where TException : Exception
		{
			try
			{
				action();
			}
			catch (TException exception)
			{
				return exception;
			}
			catch (Exception exception)
			{
				throw new TestFailure(label + ": expected " + typeof(TException).Name + " but caught " + exception.GetType().Name + ": " + exception.Message);
			}
			throw new TestFailure(label + ": expected " + typeof(TException).Name + " but nothing was thrown.");
		}

		internal static string Hex(byte[] bytes)
		{
			return BitConverter.ToString(bytes);
		}
	}

	// A disposable directory under the test scratch root. Set
	// BLIND_SOLDIER_TEST_SCRATCH to keep test files off the system drive.
	internal sealed class ScratchDirectory : IDisposable
	{
		internal ScratchDirectory(string label)
		{
			string root = Environment.GetEnvironmentVariable("BLIND_SOLDIER_TEST_SCRATCH");
			if (string.IsNullOrWhiteSpace(root))
			{
				root = Path.GetTempPath();
			}
			FullPath = Path.Combine(root, "bs2013-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 12));
			Directory.CreateDirectory(FullPath);
		}

		internal string FullPath { get; }

		internal string Combine(params string[] parts)
		{
			return Path.Combine(new[] { FullPath }.Concat(parts).ToArray());
		}

		public void Dispose()
		{
			for (int attempt = 0; attempt < 10; attempt++)
			{
				try
				{
					if (Directory.Exists(FullPath))
					{
						Directory.Delete(FullPath, true);
					}
					return;
				}
				catch (IOException)
				{
					System.Threading.Thread.Sleep(200);
				}
				catch (UnauthorizedAccessException)
				{
					System.Threading.Thread.Sleep(200);
				}
			}
		}
	}

	// Builds <scratch>\steamapps\common\FINAL FANTASY VII plus a separate
	// Documents root, mirroring the Steam 2013 layout.
	internal sealed class LegacyGameTree : IDisposable
	{
		private readonly ScratchDirectory scratch;

		internal LegacyGameTree(string label)
		{
			scratch = new ScratchDirectory(label);
			SteamApps = scratch.Combine("steamapps");
			Root = Path.Combine(SteamApps, "common", "FINAL FANTASY VII");
			DocumentsRoot = scratch.Combine("Documents");
			Directory.CreateDirectory(Root);
			Directory.CreateDirectory(DocumentsRoot);
		}

		internal string SteamApps { get; }

		internal string Root { get; }

		internal string DocumentsRoot { get; }

		internal string DocumentDirectory => Path.Combine(DocumentsRoot, "Square Enix", "FINAL FANTASY VII Steam");

		internal string ManifestPath => Path.Combine(SteamApps, "appmanifest_39140.acf");

		internal LegacyGameTree WithStandard2013Files(params string[] executables)
		{
			WriteFile("FF7_Launcher.exe", 4096);
			Directory.CreateDirectory(Path.Combine(Root, "data"));
			// Every 2013 install carries strings.dat, the original launcher's
			// translations in four languages.
			File.WriteAllBytes(Path.Combine(Root, "strings.dat"), NativeFixtures.StandardStringsDat());
			foreach (string executable in executables.Length == 0 ? new[] { "ff7_en.exe" } : executables)
			{
				WriteFile(executable, 2048);
			}
			return this;
		}

		internal LegacyGameTree WithDriverBytes(int length)
		{
			WriteFile("AF3DN.P", length);
			return this;
		}

		internal string WriteFile(string relativePath, int length)
		{
			string path = Path.Combine(Root, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			var bytes = new byte[length];
			for (int index = 0; index < bytes.Length; index++)
			{
				bytes[index] = (byte)(index * 31 + 7);
			}
			File.WriteAllBytes(path, bytes);
			return path;
		}

		internal LegacyGameTree WithManifest(string installDirectory, string language)
		{
			File.WriteAllText(ManifestPath,
				"\"AppState\"\n{\n\t\"appid\"\t\t\"39140\"\n\t\"installdir\"\t\t\"" + installDirectory +
				"\"\n\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"" + language + "\"\n\t}\n}\n");
			return this;
		}

		internal string CreateProfile(string name, params string[] files)
		{
			string profile = Path.Combine(DocumentDirectory, name);
			Directory.CreateDirectory(profile);
			foreach (string file in files)
			{
				File.WriteAllBytes(Path.Combine(profile, file), new byte[] { 1, 2, 3 });
			}
			return profile;
		}

		public void Dispose()
		{
			scratch.Dispose();
		}
	}

	internal static class AssemblyFactory
	{
		// Compiles a tiny library whose version resource carries the given
		// product identity, so driver detection reads a real PE resource.
		internal static void CompileLibraryWithProduct(string outputPath, string product, string informationalVersion)
		{
			string temporary = outputPath + ".build.dll";
			using (var provider = new CSharpCodeProvider())
			{
				var parameters = new CompilerParameters
				{
					GenerateExecutable = false,
					OutputAssembly = temporary,
					CompilerOptions = "/platform:x86"
				};
				string source =
					"using System.Reflection;\n" +
					"[assembly: AssemblyProduct(\"" + product + "\")]\n" +
					"[assembly: AssemblyInformationalVersion(\"" + informationalVersion + "\")]\n" +
					"[assembly: AssemblyFileVersion(\"1.0.0.0\")]\n" +
					"public static class DriverProbe { }\n";
				CompilerResults results = provider.CompileAssemblyFromSource(parameters, source);
				if (results.Errors.HasErrors)
				{
					throw new InvalidOperationException("Test library compilation failed: " + results.Errors[0]);
				}
			}
			File.Copy(temporary, outputPath, true);
			File.Delete(temporary);
		}
	}
}
