using System;
using System.IO;
using SharpDX.DirectSound;
using SharpDX.Multimedia;

namespace FF7_Launcher;

internal class DSoundVoiceInstance : IDisposable
{
	private DirectSound _DirectSound;

	private PrimarySoundBuffer[] PrimaryBufferList = new PrimarySoundBuffer[VOICE_MAX];

	private static int VOICE_MAX = 32;

	private int CurrentVoiceIndex = 0;

	public DSoundVoiceInstance(Stream stream, IntPtr hwnd)
	{
		_DirectSound = new DirectSound();
		_DirectSound.SetCooperativeLevel(hwnd, CooperativeLevel.Priority);
		WaveFormat waveFormat = new WaveFormat();
		using SoundStream soundStream = new SoundStream(stream);
		SoundBufferDescription soundBufferDescription = new SoundBufferDescription
		{
			BufferBytes = (int)soundStream.Length,
			Flags = BufferFlags.None,
			Format = soundStream.Format
		};
		byte[] array = new byte[soundBufferDescription.BufferBytes];
		soundStream.Read(array, 0, (int)soundStream.Length);
		for (int i = 0; i < VOICE_MAX; i++)
		{
			PrimaryBufferList[i] = new PrimarySoundBuffer(_DirectSound, soundBufferDescription);
			PrimaryBufferList[i].Write(array, 0, LockFlags.None);
		}
	}

	public void Play()
	{
		PrimarySoundBuffer primarySoundBuffer = PrimaryBufferList[CurrentVoiceIndex];
		if (primarySoundBuffer.Status != 0)
		{
			primarySoundBuffer.Stop();
		}
		primarySoundBuffer.Play(0, PlayFlags.None);
		CurrentVoiceIndex++;
		if (CurrentVoiceIndex >= VOICE_MAX)
		{
			CurrentVoiceIndex = 0;
		}
	}

	public void Dispose()
	{
		for (int i = 0; i < VOICE_MAX; i++)
		{
			PrimaryBufferList[i].Dispose();
		}
		_DirectSound.Dispose();
	}
}
