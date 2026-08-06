using System;

namespace FF7_Launcher;

public class Resolution : IComparable
{
	public Size Size { get; private set; }

	public int Frequency { get; private set; }

	public int BitsPerPixel { get; private set; }

	public Resolution(DEVMODE devMode)
	{
		Size = new Size(devMode.dmPelsWidth, devMode.dmPelsHeight);
		Frequency = devMode.dmDisplayFrequency;
		BitsPerPixel = devMode.dmBitsPerPel;
	}

	public override bool Equals(object obj)
	{
		if (obj == null || GetType() != obj.GetType())
		{
			return false;
		}
		Resolution resolution = (Resolution)obj;
		return Size.x == resolution.Size.x && Size.y == resolution.Size.y;
	}

	public override int GetHashCode()
	{
		return Size.x + Size.y;
	}

	public int CompareTo(object x)
	{
		Resolution resolution = (Resolution)x;
		if (Size.x == resolution.Size.x && Size.y == resolution.Size.y)
		{
			return 0;
		}
		return (Size.x >= resolution.Size.x && (Size.x != resolution.Size.x || Size.y >= resolution.Size.y)) ? 1 : (-1);
	}
}
