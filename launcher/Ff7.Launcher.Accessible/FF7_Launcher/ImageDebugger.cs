using System;
using System.Drawing;
using System.IO;

namespace FF7_Launcher;

internal class ImageDebugger
{
	private FileSystemWatcher Watcher;

	private Bitmap Image;

	private string FullPath;

	public event EventHandler<ImageChangedEventArgs> OnImageChanged;

	public ImageDebugger(string filename)
	{
		string path = (FullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Image", filename));
		if (File.Exists(path))
		{
			string fullName = Directory.GetParent(path).FullName;
			Watcher = new FileSystemWatcher(fullName);
			Watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.LastAccess;
			Watcher.EnableRaisingEvents = true;
			using (FileStream stream = new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				Image = new Bitmap(stream);
			}
			Watcher.Changed += Watcher_Changed;
			Watcher.Created += Watcher_Changed;
		}
	}

	private void Watcher_Changed(object sender, FileSystemEventArgs e)
	{
		if (!(e.FullPath == FullPath))
		{
			return;
		}
		try
		{
			using FileStream stream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			Image = new Bitmap(stream);
			this.OnImageChanged(this, new ImageChangedEventArgs(Image));
		}
		catch (Exception)
		{
		}
	}
}
