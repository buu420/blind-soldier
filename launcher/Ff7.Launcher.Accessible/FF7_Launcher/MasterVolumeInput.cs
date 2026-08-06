using System;
using System.Windows.Forms;

namespace FF7_Launcher;

internal class MasterVolumeInput : IDisposable
{
	private float MasterVolumeRateMax = 2f;

	private float MasterVolumeRateAccel = 1f / 30f;

	private float MasterVolumeRate = 0f;

	private float MaxVolume;

	private float MinVolume;

	private Timer TickTimer = new Timer();

	private JoyKey Inputted;

	private JoyKey PreInputted;

	public float MasterVolume { get; private set; }

	public bool IsActive { get; private set; }

	public bool EnableInput { get; private set; }

	public event EventHandler<EventArgs> OnTick;

	public static T Clamp<T>(T val, T min, T max) where T : IComparable<T>
	{
		if (val.CompareTo(min) < 0)
		{
			return min;
		}
		if (val.CompareTo(max) > 0)
		{
			return max;
		}
		return val;
	}

	public static float Floor(float v)
	{
		return (float)Math.Floor(v);
	}

	public MasterVolumeInput(TrackBar trackBar)
	{
		MaxVolume = trackBar.Maximum;
		MinVolume = trackBar.Minimum;
		MasterVolume = trackBar.Value;
		TickTimer.Interval = 16;
		TickTimer.Tick += TickTimer_Tick;
		TickTimer.Start();
		IsActive = false;
		EnableInput = false;
	}

	public void Dispose()
	{
		TickTimer.Tick -= TickTimer_Tick;
		TickTimer.Dispose();
	}

	public void Enable()
	{
		EnableInput = true;
	}

	public void Disable()
	{
		EnableInput = false;
	}

	public void OnKey(KeyEventArgs e, bool keydown = true)
	{
		if (!EnableInput)
		{
			Inputted = JoyKey.None;
			return;
		}
		if (!keydown)
		{
			Inputted = JoyKey.None;
			return;
		}
		switch (e.KeyCode)
		{
		case Keys.Left:
			Inputted = JoyKey.Left;
			break;
		case Keys.Right:
			Inputted = JoyKey.Right;
			break;
		default:
			Inputted = JoyKey.None;
			break;
		}
	}

	public void UpdateMasterVolume(int masterVolume)
	{
		MasterVolume = masterVolume;
	}

	private void TickTimer_Tick(object sender, EventArgs e)
	{
		ApplyInputRepeat_MasterVolume(Inputted);
		if (this.OnTick != null)
		{
			this.OnTick(this, new EventArgs());
		}
	}

	private void ApplyInputRepeat_MasterVolume(JoyKey btnFlag)
	{
		if (btnFlag == JoyKey.None)
		{
			IsActive = false;
		}
		if (PreInputted == JoyKey.None && btnFlag != JoyKey.None)
		{
			switch (btnFlag)
			{
			case JoyKey.Right:
				MasterVolume++;
				break;
			case JoyKey.Left:
				MasterVolume--;
				break;
			}
		}
		switch (btnFlag)
		{
		case JoyKey.Right:
			MasterVolumeRate += MasterVolumeRateAccel;
			if (MasterVolumeRate > MasterVolumeRateMax)
			{
				MasterVolumeRate = MasterVolumeRateMax;
			}
			if (MasterVolumeRate >= 1f)
			{
				MasterVolume += (float)Math.Floor(MasterVolumeRate);
			}
			IsActive = true;
			break;
		case JoyKey.Left:
			MasterVolumeRate += MasterVolumeRateAccel;
			if (MasterVolumeRate > MasterVolumeRateMax)
			{
				MasterVolumeRate = MasterVolumeRateMax;
			}
			if (MasterVolumeRate >= 1f)
			{
				MasterVolume -= (float)Math.Floor(MasterVolumeRate);
			}
			IsActive = true;
			break;
		default:
			MasterVolumeRate *= 0.8f;
			if (MasterVolumeRate < 0.15f)
			{
				MasterVolumeRate = 0f;
			}
			break;
		}
		MasterVolume = Clamp(MasterVolume, MinVolume, MaxVolume);
		PreInputted = btnFlag;
	}
}
