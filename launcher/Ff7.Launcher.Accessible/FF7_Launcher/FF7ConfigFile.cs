using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FF7_Launcher;

public class FF7ConfigFile
{
	public string SteamID = "";

	public int m_width = 0;

	public int m_height = 0;

	public int m_random = 0;

	public string m_language = "jp";

	public string m_inputs = "USER";

	public int m_cheats = 0;

	public int m_skipmovie = 0;

	public int m_masterVolume = 0;

	public int m_screenMode = 0;

	public int Bgmvol = 100;

	public int Sevol = 100;

	public int ShowDisplay = 0;

	public int CameraSpeed = 4;

	public int Brightness = 50;

	public int Antialiaslevel = 1;

	public int IsFirstBoot = 1;

	public static string GameSettingFolder
	{
		get
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine(folderPath, "FINAL FANTASY VII Steam Edition");
		}
	}

	public string SteamIDFolder => GameSettingFolder;

	public string SavesFolder => Path.Combine(GameSettingFolder, SteamID);

	public void ApplyCultureFromConfig()
	{
		string text = m_language.ToLower();
		if (text == "jp")
		{
			text = "ja";
		}
		Thread currentThread = Thread.CurrentThread;
		currentThread.CurrentUICulture = new CultureInfo(text, useUserOverride: false);
	}

	private bool IsAvailableLang(string lang)
	{
		List<string> list = new List<string> { "JP", "EN", "FR", "DE", "ES" };
		return list.Contains(lang);
	}

	public void WriteDefaultFile(string path)
	{
		Thread currentThread = Thread.CurrentThread;
		string text = currentThread.CurrentUICulture.Name.Substring(0, 2).ToUpper();
		if (text == "JA")
		{
			text = "JP";
		}
		if (!IsAvailableLang(text))
		{
			text = "EN";
		}
		Resolution resolution = Display.GetSortedResolutionList(0).Last();
		Size size = new Size(800, 600);
		int num = 0;
		size = resolution.Size;
		num = 1;
		string[] contents = new string[8]
		{
			"width\t\t\t" + size.x,
			"height\t\t\t" + size.y,
			"language\t\t" + text.ToLower(),
			"mastervol\t\t100",
			"bgmvol\t\t\t100",
			"screenmode\t\t" + num,
			"display\t\t\t0",
			"brightness\t\t50"
		};
		File.WriteAllLines(path, contents);
	}

	public bool Read(string path)
	{
		string text = path;
		Directory.CreateDirectory(GameSettingFolder);
		Directory.CreateDirectory(SteamIDFolder);
		Directory.CreateDirectory(SavesFolder);
		text = Path.Combine(SteamIDFolder, path);
		if (!File.Exists(text))
		{
			WriteDefaultFile(text);
		}
		using (StreamReader streamReader = new StreamReader(text, Encoding.UTF8))
		{
			string strLine;
			while ((strLine = streamReader.ReadLine()) != null)
			{
				ParseLine(strLine);
			}
		}
		return true;
	}

	public bool Write(string path)
	{
		string text = path;
		text = Path.Combine(SteamIDFolder, path);
		Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		using (StreamWriter streamWriter = new StreamWriter(text, append: false, encoding))
		{
			string value = $"width\t\t\t{m_width}";
			streamWriter.WriteLine(value);
			value = $"height\t\t\t{m_height}";
			streamWriter.WriteLine(value);
			string arg = m_language.ToLower();
			value = $"language\t\t{arg}";
			streamWriter.WriteLine(value);
			value = $"mastervol\t\t{m_masterVolume}";
			streamWriter.WriteLine(value);
			value = $"bgmvol\t\t\t{Bgmvol}";
			streamWriter.WriteLine(value);
			value = $"screenmode\t\t{m_screenMode}";
			streamWriter.WriteLine(value);
			value = $"display\t\t\t{ShowDisplay}";
			streamWriter.WriteLine(value);
			value = $"brightness\t\t{Brightness}";
			streamWriter.WriteLine(value);
		}
		return true;
	}

	public bool IsLangEmpty()
	{
		return string.IsNullOrEmpty(m_language);
	}

	protected void ParseLine(string strLine)
	{
		string[] separator = new string[1] { "\t" };
		string[] array = strLine.Split(separator, StringSplitOptions.RemoveEmptyEntries);
		int num = array.Count();
		if (num == 2)
		{
			if (string.Equals("width", array[0]))
			{
				m_width = int.Parse(array[1]);
			}
			else if (string.Equals("height", array[0]))
			{
				m_height = int.Parse(array[1]);
			}
			else if (string.Equals(array[0], "language"))
			{
				m_language = array[1];
			}
			else if (string.Equals(array[0], "mastervol"))
			{
				m_masterVolume = int.Parse(array[1]);
			}
			else if (string.Equals(array[0], "bgmvol"))
			{
				Bgmvol = int.Parse(array[1]);
			}
			else if (string.Equals(array[0], "screenmode"))
			{
				m_screenMode = int.Parse(array[1]);
			}
			else if (string.Equals(array[0], "display"))
			{
				ShowDisplay = int.Parse(array[1]);
			}
			else if (string.Equals(array[0], "brightness"))
			{
				Brightness = int.Parse(array[1]);
			}
		}
	}

	public void SetLanguage(int index)
	{
		switch (index)
		{
		case 0:
			m_language = "jp";
			break;
		case 1:
			m_language = "en";
			break;
		case 2:
			m_language = "fr";
			break;
		case 3:
			m_language = "de";
			break;
		case 4:
			m_language = "es";
			break;
		}
	}

	public int GetLanguageIndex()
	{
		int result = -1;
		if (string.Equals("jp", m_language))
		{
			result = 0;
		}
		else if (string.Equals("en", m_language))
		{
			result = 1;
		}
		else if (string.Equals("fr", m_language))
		{
			result = 2;
		}
		else if (string.Equals("de", m_language))
		{
			result = 3;
		}
		else if (string.Equals("es", m_language))
		{
			result = 4;
		}
		return result;
	}
}
