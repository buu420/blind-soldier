using System;
using System.IO;

namespace BlindSoldier.Ff7Launcher2013
{
	// ff7video.cfg as written for the original 2013 driver: eight big-endian
	// 32-bit values (width, height, refresh rate, full screen, reserved, keep
	// aspect ratio, linear filtering, original mode). Zero sizes and refresh rate
	// select a 640 by 480 window, or the desktop mode in full screen.
	internal sealed class LegacyDisplaySettings
	{
		internal const int EncodedLength = 32;

		internal uint Width { get; set; }

		internal uint Height { get; set; }

		internal uint RefreshRate { get; set; }

		internal bool Fullscreen { get; set; }

		internal uint Reserved { get; set; }

		internal bool KeepAspectRatio { get; set; }

		internal bool LinearFiltering { get; set; }

		internal bool OriginalMode { get; set; }

		internal static LegacyDisplaySettings CreateDefault()
		{
			return new LegacyDisplaySettings();
		}

		internal LegacyDisplaySettings Clone()
		{
			return (LegacyDisplaySettings)MemberwiseClone();
		}

		internal static bool TryDecode(byte[] bytes, out LegacyDisplaySettings settings)
		{
			settings = null;
			if (bytes == null || bytes.Length != EncodedLength)
			{
				return false;
			}
			settings = new LegacyDisplaySettings
			{
				Width = ReadBigEndian(bytes, 0),
				Height = ReadBigEndian(bytes, 4),
				RefreshRate = ReadBigEndian(bytes, 8),
				Fullscreen = ReadBigEndian(bytes, 12) != 0,
				Reserved = ReadBigEndian(bytes, 16),
				KeepAspectRatio = ReadBigEndian(bytes, 20) != 0,
				LinearFiltering = ReadBigEndian(bytes, 24) != 0,
				OriginalMode = ReadBigEndian(bytes, 28) != 0
			};
			return true;
		}

		internal byte[] Encode()
		{
			var bytes = new byte[EncodedLength];
			WriteBigEndian(bytes, 0, Width);
			WriteBigEndian(bytes, 4, Height);
			WriteBigEndian(bytes, 8, RefreshRate);
			WriteBigEndian(bytes, 12, Fullscreen ? 1u : 0u);
			WriteBigEndian(bytes, 16, Reserved);
			WriteBigEndian(bytes, 20, KeepAspectRatio ? 1u : 0u);
			WriteBigEndian(bytes, 24, LinearFiltering ? 1u : 0u);
			WriteBigEndian(bytes, 28, OriginalMode ? 1u : 0u);
			return bytes;
		}

		private static uint ReadBigEndian(byte[] bytes, int offset)
		{
			return (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
		}

		private static void WriteBigEndian(byte[] bytes, int offset, uint value)
		{
			bytes[offset] = (byte)(value >> 24);
			bytes[offset + 1] = (byte)(value >> 16);
			bytes[offset + 2] = (byte)(value >> 8);
			bytes[offset + 3] = (byte)value;
		}
	}

	// ff7sound.cfg: sound-effects volume then music volume, each a little-endian
	// 32-bit percentage. FFNx reads and writes this same file on the 2013 edition.
	internal sealed class LegacySoundSettings
	{
		internal const int EncodedLength = 8;
		internal const uint MaximumVolume = 100;

		internal uint SoundEffectsVolume { get; set; }

		internal uint MusicVolume { get; set; }

		internal static LegacySoundSettings CreateDefault()
		{
			return new LegacySoundSettings { SoundEffectsVolume = MaximumVolume, MusicVolume = MaximumVolume };
		}

		internal LegacySoundSettings Clone()
		{
			return (LegacySoundSettings)MemberwiseClone();
		}

		internal static bool TryDecode(byte[] bytes, out LegacySoundSettings settings)
		{
			settings = null;
			if (bytes == null || bytes.Length != EncodedLength)
			{
				return false;
			}
			uint effects = BitConverter.ToUInt32(bytes, 0);
			uint music = BitConverter.ToUInt32(bytes, 4);
			if (effects > MaximumVolume || music > MaximumVolume)
			{
				return false;
			}
			settings = new LegacySoundSettings { SoundEffectsVolume = effects, MusicVolume = music };
			return true;
		}

		internal byte[] Encode()
		{
			var bytes = new byte[EncodedLength];
			Array.Copy(BitConverter.GetBytes(Math.Min(SoundEffectsVolume, MaximumVolume)), 0, bytes, 0, 4);
			Array.Copy(BitConverter.GetBytes(Math.Min(MusicVolume, MaximumVolume)), 0, bytes, 4, 4);
			return bytes;
		}
	}

	internal enum LegacySettingsFileState
	{
		Missing,
		Valid,
		Unreadable
	}

	internal sealed class LegacySettingsStore
	{
		internal const string DisplayFileName = "ff7video.cfg";
		internal const string SoundFileName = "ff7sound.cfg";
		internal const string BackupSuffix = ".blind-soldier-original";

		internal LegacySettingsStore(string documentDirectory)
		{
			DocumentDirectory = documentDirectory ?? throw new ArgumentNullException(nameof(documentDirectory));
		}

		internal string DocumentDirectory { get; }

		internal string DisplayPath => Path.Combine(DocumentDirectory, DisplayFileName);

		internal string SoundPath => Path.Combine(DocumentDirectory, SoundFileName);

		internal LegacySettingsFileState ReadDisplay(out LegacyDisplaySettings settings)
		{
			LegacySettingsFileState state = Read(DisplayPath, out byte[] bytes);
			if (state == LegacySettingsFileState.Valid && LegacyDisplaySettings.TryDecode(bytes, out settings))
			{
				return LegacySettingsFileState.Valid;
			}
			settings = LegacyDisplaySettings.CreateDefault();
			return state == LegacySettingsFileState.Missing ? state : LegacySettingsFileState.Unreadable;
		}

		internal LegacySettingsFileState ReadSound(out LegacySoundSettings settings)
		{
			LegacySettingsFileState state = Read(SoundPath, out byte[] bytes);
			if (state == LegacySettingsFileState.Valid && LegacySoundSettings.TryDecode(bytes, out settings))
			{
				return LegacySettingsFileState.Valid;
			}
			settings = LegacySoundSettings.CreateDefault();
			return state == LegacySettingsFileState.Missing ? state : LegacySettingsFileState.Unreadable;
		}

		internal void WriteDisplay(LegacyDisplaySettings settings)
		{
			WriteAtomically(DisplayPath, (settings ?? throw new ArgumentNullException(nameof(settings))).Encode());
		}

		internal void WriteSound(LegacySoundSettings settings)
		{
			WriteAtomically(SoundPath, (settings ?? throw new ArgumentNullException(nameof(settings))).Encode());
		}

		private static LegacySettingsFileState Read(string path, out byte[] bytes)
		{
			bytes = null;
			if (!File.Exists(path))
			{
				return LegacySettingsFileState.Missing;
			}
			try
			{
				bytes = File.ReadAllBytes(path);
				return LegacySettingsFileState.Valid;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				return LegacySettingsFileState.Unreadable;
			}
		}

		// The first replacement of an existing file keeps its previous bytes
		// beside it; later writes never overwrite that copy.
		private void WriteAtomically(string path, byte[] bytes)
		{
			Directory.CreateDirectory(DocumentDirectory);
			string backup = path + BackupSuffix;
			if (File.Exists(path) && !File.Exists(backup))
			{
				File.Copy(path, backup);
			}

			string temporary = path + ".blind-soldier-" + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllBytes(temporary, bytes);
				if (File.Exists(path))
				{
					File.Replace(temporary, path, null);
				}
				else
				{
					File.Move(temporary, path);
				}
			}
			finally
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
		}
	}
}
