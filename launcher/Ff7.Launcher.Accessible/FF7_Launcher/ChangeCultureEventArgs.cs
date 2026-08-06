using System;

namespace FF7_Launcher;

public class ChangeCultureEventArgs : EventArgs
{
	public InternationalCultureType CultureType { get; private set; }

	public ChangeCultureEventArgs(string cultureCode)
	{
		if (cultureCode == "ja")
		{
			CultureType = InternationalCultureType.JP;
		}
		if (cultureCode == "en")
		{
			CultureType = InternationalCultureType.EN;
		}
		if (cultureCode == "fr")
		{
			CultureType = InternationalCultureType.FR;
		}
		if (cultureCode == "de")
		{
			CultureType = InternationalCultureType.DE;
		}
		if (cultureCode == "es")
		{
			CultureType = InternationalCultureType.ES;
		}
	}

	public ChangeCultureEventArgs(InternationalCultureType val)
	{
		CultureType = val;
	}
}
