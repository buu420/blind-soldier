using System;
using System.Drawing;

namespace FF7_Launcher;

internal class ImageChangedEventArgs : EventArgs
{
	public Bitmap Image;

	public ImageChangedEventArgs(Bitmap image)
	{
		Image = image;
	}
}
