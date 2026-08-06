using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using FF7_Launcher.Properties;
using NAudio.Wave;

namespace FF7_Launcher;

public class SoundManager
{
	public enum SoundType
	{
		Cancel,
		Cursor,
		Decide,
		ExitButton
	}

	private DSoundVoiceInstance CursorInstance;

	private WaveOutEvent waveOut;

	private WaveChannel32 volumeProvider;

	private WaveFileReader reader;

	private Stream stream;

	public void PlayInstance(SoundType type, float volume = -1f)
	{
		if (type == SoundType.Cursor)
		{
			PlayFromNAudio(type, volume);
		}
		else
		{
			PlayFromNAudio(type, volume);
		}
	}

	public SoundManager(Form form)
	{
		try
		{
			CursorInstance = new DSoundVoiceInstance(Resources.cursor, form.Handle);
		}
		catch (Exception)
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(FF7Launcher));
			MessageBox.Show(componentResourceManager.GetString("SoundInitialize"), Program.Title);
			Environment.Exit(0);
		}
	}

	public void Dispose()
	{
		CursorInstance.Dispose();
		if (waveOut != null)
		{
			waveOut.Stop();
			waveOut.Dispose();
			volumeProvider.Dispose();
			reader.Dispose();
			stream.Dispose();
		}
	}

	public static void DisposeXAudio()
	{
	}

	private void PlayFromNAudio(SoundType type, float volume = -1f)
	{
		if (waveOut != null)
		{
			waveOut.Stop();
			waveOut.Dispose();
			volumeProvider.Dispose();
			reader.Dispose();
			stream.Dispose();
		}
		if (volume < 0f)
		{
			volume = (float)FF7Launcher.configFile.m_masterVolume / 100f;
		}
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		switch (type)
		{
		case SoundType.Cursor:
			stream = executingAssembly.GetManifestResourceStream("FF7_Launcher.Resources.cursor.wav");
			break;
		case SoundType.Decide:
			stream = executingAssembly.GetManifestResourceStream("FF7_Launcher.Resources.decide.wav");
			break;
		case SoundType.Cancel:
			stream = executingAssembly.GetManifestResourceStream("FF7_Launcher.Resources.cancel.wav");
			break;
		default:
			stream = executingAssembly.GetManifestResourceStream("FF7_Launcher.Resources.cancel.wav");
			break;
		}
		if (stream == null)
		{
			Console.WriteLine("リソースが見つかりませんでした");
			return;
		}
		reader = new WaveFileReader(stream);
		volumeProvider = new WaveChannel32(reader);
		volumeProvider.Volume = volume;
		waveOut = new WaveOutEvent();
		waveOut.Init(volumeProvider);
		waveOut.Play();
		Console.WriteLine("再生中。Enterで終了します。");
		Console.ReadLine();
	}
}
