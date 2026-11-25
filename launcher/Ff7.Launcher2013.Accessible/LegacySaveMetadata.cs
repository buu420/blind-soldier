using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace BlindSoldier.Ff7Launcher2013
{
	// metadata.xml beside the saves in a user_<id> folder, in the form the
	// original FF7_Launcher.exe (Steam build 115956) reads and writes: ten
	// savefiles blocks, each with fifteen slot timestamps and a signature. A
	// signature is the lowercase MD5 of saveNN.ff7 followed by the user ID in
	// UTF-8; a block without a save is signed with the user ID alone. At every
	// sign-in the original launcher deletes a save whose signature differs.
	internal sealed class LegacySaveMetadata
	{
		internal const string FileName = "metadata.xml";
		internal const int SaveFileCount = 10;
		internal const int SlotCount = 15;
		private const string ProfilePrefix = "user_";

		private readonly string[][] timestamps;
		private readonly string[] signatures;

		private LegacySaveMetadata(string emptySignature)
		{
			timestamps = Enumerable.Range(0, SaveFileCount).Select(_ => Enumerable.Repeat(string.Empty, SlotCount).ToArray()).ToArray();
			signatures = Enumerable.Repeat(emptySignature, SaveFileCount).ToArray();
		}

		// The original launcher names the folder user_ plus the account ID.
		internal static string UserIdFor(string profileName)
		{
			if (profileName == null)
			{
				throw new ArgumentNullException(nameof(profileName));
			}
			return profileName.StartsWith(ProfilePrefix, StringComparison.OrdinalIgnoreCase)
				? profileName.Substring(ProfilePrefix.Length)
				: profileName;
		}

		internal static string SaveFileName(int saveIndex)
		{
			CheckSave(saveIndex);
			return "save" + saveIndex.ToString("00", CultureInfo.InvariantCulture) + ".ff7";
		}

		// Null signs a save file that does not exist.
		internal static string Sign(byte[] save, string userId)
		{
			byte[] id = Encoding.UTF8.GetBytes(userId ?? throw new ArgumentNullException(nameof(userId)));
			byte[] data = new byte[(save?.Length ?? 0) + id.Length];
			save?.CopyTo(data, 0);
			id.CopyTo(data, save?.Length ?? 0);
			using (var md5 = MD5.Create())
			{
				return string.Concat(md5.ComputeHash(data).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
			}
		}

		internal static LegacySaveMetadata CreateDefault(string userId)
		{
			return new LegacySaveMetadata(Sign(null, userId));
		}

		// Accepts exactly what the original reader accepts: ten savefiles
		// children of the root, in any order, with blocks 1 to 10 and slots 1
		// to 15. Values it does not find keep their defaults.
		internal static bool TryParse(string xml, string userId, out LegacySaveMetadata metadata)
		{
			metadata = null;
			var document = new XmlDocument { XmlResolver = null };
			try
			{
				var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
				using (var reader = XmlReader.Create(new StringReader(xml ?? string.Empty), settings))
				{
					document.Load(reader);
				}
			}
			catch (XmlException)
			{
				return false;
			}

			XmlElement root = document.DocumentElement;
			if (root == null)
			{
				return false;
			}
			var parsed = CreateDefault(userId);
			int blocks = 0;
			foreach (XmlElement savefiles in root.ChildNodes.OfType<XmlElement>().Where(element => element.Name == "savefiles"))
			{
				blocks++;
				int block = ParseNumber(savefiles.GetAttribute("block"));
				if (block < 1 || block > SaveFileCount)
				{
					return false;
				}
				foreach (XmlElement child in savefiles.ChildNodes.OfType<XmlElement>())
				{
					if (child.Name == "timestamp")
					{
						int slot = ParseNumber(child.GetAttribute("slot"));
						if (slot < 1 || slot > SlotCount)
						{
							return false;
						}
						parsed.timestamps[block - 1][slot - 1] = child.InnerText;
					}
					else if (child.Name == "signature")
					{
						parsed.signatures[block - 1] = child.InnerText;
					}
				}
			}
			if (blocks != SaveFileCount)
			{
				return false;
			}
			metadata = parsed;
			return true;
		}

		internal string GetTimestamp(int saveIndex, int slotIndex)
		{
			CheckSlot(saveIndex, slotIndex);
			return timestamps[saveIndex][slotIndex];
		}

		internal void SetTimestamp(int saveIndex, int slotIndex, string value)
		{
			CheckSlot(saveIndex, slotIndex);
			timestamps[saveIndex][slotIndex] = value ?? string.Empty;
		}

		internal string GetSignature(int saveIndex)
		{
			CheckSave(saveIndex);
			return signatures[saveIndex];
		}

		internal void SetSignature(int saveIndex, string value)
		{
			CheckSave(saveIndex);
			signatures[saveIndex] = value ?? string.Empty;
		}

		// The original launcher's QDomNode::save output: an indent of 2, LF line
		// ends and an end tag for every empty element.
		internal string ToXml()
		{
			var builder = new StringBuilder();
			builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gamestatus>\n");
			for (int save = 0; save < SaveFileCount; save++)
			{
				builder.Append("  <savefiles block=\"").Append(save + 1).Append("\">\n");
				for (int slot = 0; slot < SlotCount; slot++)
				{
					builder.Append("    <timestamp slot=\"").Append(slot + 1).Append("\">").Append(Escape(timestamps[save][slot])).Append("</timestamp>\n");
				}
				builder.Append("    <signature>").Append(Escape(signatures[save])).Append("</signature>\n");
				builder.Append("  </savefiles>\n");
			}
			builder.Append("</gamestatus>\n");
			return builder.ToString();
		}

		internal static void CheckSave(int saveIndex)
		{
			if (saveIndex < 0 || saveIndex >= SaveFileCount)
			{
				throw new ArgumentOutOfRangeException(nameof(saveIndex), saveIndex, "FINAL FANTASY VII has save files 0 to 9.");
			}
		}

		internal static void CheckSlot(int saveIndex, int slotIndex)
		{
			CheckSave(saveIndex);
			if (slotIndex < 0 || slotIndex >= SlotCount)
			{
				throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Each FINAL FANTASY VII save file has slots 0 to 14.");
			}
		}

		// QString::toInt: a failed conversion reads as 0.
		private static int ParseNumber(string value)
		{
			return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) ? number : 0;
		}

		private static string Escape(string value)
		{
			return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
		}
	}

	// Reads and writes metadata.xml in one save folder. Only this launcher
	// writes it while the game runs, because the original launcher cannot run
	// beside it; every replacement is atomic and the first keeps a backup.
	internal sealed class LegacySaveMetadataStore
	{
		internal const string BackupSuffix = ".blind-soldier-original";
		private readonly object gate = new object();

		internal LegacySaveMetadataStore(string profileDirectory)
		{
			ProfileDirectory = profileDirectory ?? throw new ArgumentNullException(nameof(profileDirectory));
			UserId = LegacySaveMetadata.UserIdFor(Path.GetFileName(profileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
		}

		internal string ProfileDirectory { get; }

		internal string UserId { get; }

		internal string MetadataPath => Path.Combine(ProfileDirectory, LegacySaveMetadata.FileName);

		// A new folder gets the file the original launcher creates with it.
		internal bool CreateIfMissing()
		{
			lock (gate)
			{
				if (File.Exists(MetadataPath))
				{
					return false;
				}
				Write(LegacySaveMetadata.CreateDefault(UserId));
				return true;
			}
		}

		// The original launcher's response to the game's save message: the
		// slot's time in milliseconds since 1970 and the save file's signature.
		internal void RecordSave(int saveIndex, int slotIndex, DateTime utcNow)
		{
			LegacySaveMetadata.CheckSlot(saveIndex, slotIndex);
			lock (gate)
			{
				LegacySaveMetadata metadata = Load();
				metadata.SetTimestamp(saveIndex, slotIndex, new DateTimeOffset(utcNow.ToUniversalTime()).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
				byte[] save = ReadSave(saveIndex);
				if (save != null)
				{
					metadata.SetSignature(saveIndex, LegacySaveMetadata.Sign(save, UserId));
				}
				Write(metadata);
			}
		}

		// Signs the given saves again from their current bytes, leaving every
		// timestamp as it is.
		internal void Resign(IEnumerable<int> saveIndexes)
		{
			int[] indexes = (saveIndexes ?? throw new ArgumentNullException(nameof(saveIndexes))).Distinct().ToArray();
			foreach (int index in indexes)
			{
				LegacySaveMetadata.CheckSave(index);
			}
			lock (gate)
			{
				LegacySaveMetadata metadata = Load();
				foreach (int index in indexes)
				{
					byte[] save = ReadSave(index);
					if (save != null)
					{
						metadata.SetSignature(index, LegacySaveMetadata.Sign(save, UserId));
					}
				}
				Write(metadata);
			}
		}

		private LegacySaveMetadata Load()
		{
			if (File.Exists(MetadataPath) &&
				LegacySaveMetadata.TryParse(Encoding.UTF8.GetString(File.ReadAllBytes(MetadataPath)), UserId, out LegacySaveMetadata parsed))
			{
				return parsed;
			}

			// Missing or unreadable metadata makes the original launcher reset
			// every block and then delete every save, so existing saves are
			// signed now to keep them.
			LegacySaveMetadata rebuilt = LegacySaveMetadata.CreateDefault(UserId);
			for (int index = 0; index < LegacySaveMetadata.SaveFileCount; index++)
			{
				byte[] save = ReadSave(index);
				if (save != null)
				{
					rebuilt.SetSignature(index, LegacySaveMetadata.Sign(save, UserId));
				}
			}
			return rebuilt;
		}

		private byte[] ReadSave(int saveIndex)
		{
			string path = Path.Combine(ProfileDirectory, LegacySaveMetadata.SaveFileName(saveIndex));
			return File.Exists(path) ? File.ReadAllBytes(path) : null;
		}

		private void Write(LegacySaveMetadata metadata)
		{
			byte[] bytes = new UTF8Encoding(false).GetBytes(metadata.ToXml());
			Directory.CreateDirectory(ProfileDirectory);
			if (File.Exists(MetadataPath))
			{
				if (File.ReadAllBytes(MetadataPath).SequenceEqual(bytes))
				{
					return;
				}
				string backup = MetadataPath + BackupSuffix;
				if (!File.Exists(backup))
				{
					File.Copy(MetadataPath, backup);
				}
			}

			string temporary = MetadataPath + ".blind-soldier-" + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllBytes(temporary, bytes);
				if (File.Exists(MetadataPath))
				{
					File.Replace(temporary, MetadataPath, null);
				}
				else
				{
					File.Move(temporary, MetadataPath);
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

	// Follows the game's save messages for one session and updates metadata.xml
	// the way the original launcher does, then signs each saved file again
	// once the game has closed so the final bytes are what is recorded.
	internal sealed class LegacySaveRecorder
	{
		private readonly LegacySaveMetadataStore store;
		private readonly Func<DateTime> utcNow;
		private readonly Action<string> log;
		private readonly SortedSet<int> saved = new SortedSet<int>();

		internal LegacySaveRecorder(LegacySaveMetadataStore store, Func<DateTime> utcNow, Action<string> log)
		{
			this.store = store ?? throw new ArgumentNullException(nameof(store));
			this.utcNow = utcNow ?? (() => DateTime.UtcNow);
			this.log = log ?? delegate { };
		}

		internal IReadOnlyCollection<int> SavedFiles
		{
			get
			{
				lock (saved)
				{
					return saved.ToArray();
				}
			}
		}

		internal void Handle(LegacyGameMessage message)
		{
			if (message == null || !message.TryGetSavedSlot(out int saveIndex, out int slotIndex))
			{
				return;
			}
			if (saveIndex < 0 || saveIndex >= LegacySaveMetadata.SaveFileCount || slotIndex < 0 || slotIndex >= LegacySaveMetadata.SlotCount)
			{
				log("The game reported save file " + saveIndex + ", slot " + slotIndex + ", outside 10 save files of 15 slots; metadata.xml was not changed.");
				return;
			}
			lock (saved)
			{
				saved.Add(saveIndex);
			}
			store.RecordSave(saveIndex, slotIndex, utcNow());
			log("The game saved " + LegacySaveMetadata.SaveFileName(saveIndex) + " slot " + (slotIndex + 1) + "; updated " + store.MetadataPath + ".");
		}

		internal void Complete()
		{
			int[] files;
			lock (saved)
			{
				files = saved.ToArray();
			}
			if (files.Length == 0)
			{
				return;
			}
			try
			{
				store.Resign(files);
				log("Signed " + string.Join(", ", files.Select(LegacySaveMetadata.SaveFileName)) + " again after the game closed.");
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				log("The saved files could not be signed again after the game closed: " + exception.Message);
			}
		}
	}
}
