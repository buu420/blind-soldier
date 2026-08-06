using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using FF7_Launcher;
using SharpDX;
using SharpDX.DirectInput;

public class DInput : BaseInputManager, IInputManager
{
	public enum DINPUT_BUTTON
	{
		DIB_LEFT,
		DIB_RIGHT,
		DIB_UP,
		DIB_DOWN,
		DIB_SELECT,
		DIB_CANCEL,
		DIB_NUM
	}

	public enum DINPUT_LSTICK
	{
		DIB_LEFT,
		DIB_RIGHT,
		DIB_UP,
		DIB_DOWN,
		DIB_NUM
	}

	private class Pad : IDisposable
	{
		public Joystick joystick;

		public JoystickState joystickState;

		public Guid guid;

		public bool bConnected;

		public void Connect(DirectInput a_directInput, Guid a_guid, IntPtr a_parentHandle)
		{
			guid = a_guid;
			joystick = new Joystick(a_directInput, a_guid);
			joystick.SetCooperativeLevel(a_parentHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
			joystick.Properties.Range = new InputRange(-255, 255);
			bConnected = true;
		}

		public void Disconnect()
		{
			guid = Guid.Empty;
			joystick = null;
			bConnected = false;
		}

		public void Dispose()
		{
			Disconnect();
		}

		public void Poll()
		{
			if (bConnected && joystick != null)
			{
				joystick.Poll();
				joystickState = joystick.GetCurrentState();
			}
		}

		public void Acquire()
		{
			joystick?.Acquire();
		}

		public void Unacquire()
		{
			joystick?.Unacquire();
		}
	}

	public const int pad_max = 4;

	private Pad[] pads = new Pad[4];

	private int padNum = 0;

	private DirectInput directInput = null;

	private IntPtr parentHandle;

	private bool[] buttonFlag = new bool[6];

	private bool[] buttonFlagOld = new bool[6];

	private float[] LStickXArray = new float[10];

	private float[] LStickYArray = new float[10];

	private Dictionary<JoyKey, DINPUT_BUTTON> InputDict = new Dictionary<JoyKey, DINPUT_BUTTON>
	{
		{
			JoyKey.Up,
			DINPUT_BUTTON.DIB_UP
		},
		{
			JoyKey.Down,
			DINPUT_BUTTON.DIB_DOWN
		},
		{
			JoyKey.Left,
			DINPUT_BUTTON.DIB_LEFT
		},
		{
			JoyKey.Right,
			DINPUT_BUTTON.DIB_RIGHT
		},
		{
			JoyKey.Select,
			DINPUT_BUTTON.DIB_SELECT
		},
		{
			JoyKey.Cansel,
			DINPUT_BUTTON.DIB_CANCEL
		}
	};

	private const float stickThreshold = 220f;

	private const float stickTriggerThreshold = 200f;

	private const float stickTriggerMargin = 130f;

	public DInput()
	{
		Init();
	}

	public void Init()
	{
		for (int i = 0; i < LStickXArray.Length; i++)
		{
			LStickXArray[i] = i;
		}
		for (int j = 0; j < 6; j++)
		{
			buttonFlag[j] = false;
			buttonFlagOld[j] = false;
		}
		for (int k = 0; k < 4; k++)
		{
			pads[k] = new Pad();
		}
	}

	public static int GetGamePadNum()
	{
		int num = 0;
		DirectInput directInput = new DirectInput();
		IList<DeviceInstance> list = new List<DeviceInstance>();
		try
		{
			list = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString(), Program.Title);
		}
		foreach (DeviceInstance item in list)
		{
			DeviceType type = item.Type;
			num++;
		}
		return num;
	}

	public bool CreateDInputDevice(Control parent)
	{
		parentHandle = parent.Handle;
		directInput = new DirectInput();
		IList<DeviceInstance> devices = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices);
		bool result = false;
		int num = 0;
		foreach (DeviceInstance item in devices)
		{
			if (num >= 4)
			{
				break;
			}
			Pad pad = pads[num];
			pad.Connect(directInput, item.InstanceGuid, parentHandle);
			result = true;
			padNum++;
			num++;
		}
		return result;
	}

	public void Acquire()
	{
		try
		{
			Pad[] array = pads;
			foreach (Pad pad in array)
			{
				pad.Acquire();
			}
		}
		catch (SharpDXException ex)
		{
			Console.WriteLine(ex.Message);
		}
	}

	public void UnAcquire()
	{
		Pad[] array = pads;
		foreach (Pad pad in array)
		{
			pad.Unacquire();
		}
	}

	protected void Poll()
	{
		if (directInput == null)
		{
			return;
		}
		IList<DeviceInstance> devices = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices);
		Pad[] array = pads;
		foreach (Pad pad in array)
		{
			pad.bConnected = false;
		}
		Guid[] array2 = new Guid[4];
		int num = 0;
		int num2 = 0;
		foreach (DeviceInstance item in devices)
		{
			if (num2 >= 4 || num >= 4)
			{
				break;
			}
			Guid instanceGuid = item.InstanceGuid;
			int num3 = 0;
			for (num3 = 0; num3 < 4; num3++)
			{
				Pad pad2 = pads[num3];
				if (pad2 != null && pad2.guid == instanceGuid)
				{
					pad2.bConnected = true;
					num2++;
					break;
				}
			}
			if (num3 == 4)
			{
				array2[num] = instanceGuid;
				num++;
			}
		}
		Pad[] array3 = pads;
		foreach (Pad pad3 in array3)
		{
			if (!pad3.bConnected)
			{
				pad3.Disconnect();
			}
		}
		if (num2 < 4)
		{
			for (int k = 0; k < num; k++)
			{
				Guid a_guid = array2[k];
				for (int l = 0; l < 4; l++)
				{
					Pad pad4 = pads[l];
					if (!pad4.bConnected)
					{
						pad4.Connect(directInput, a_guid, parentHandle);
						break;
					}
				}
			}
		}
		padNum = 0;
		Pad[] array4 = pads;
		foreach (Pad pad5 in array4)
		{
			if (pad5.bConnected)
			{
				padNum++;
			}
		}
		try
		{
			Pad[] array5 = pads;
			foreach (Pad pad6 in array5)
			{
				pad6.Poll();
			}
		}
		catch (SharpDXException value)
		{
			Acquire();
			Console.WriteLine(value);
		}
	}

	public void Update(float elapsed)
	{
		if (pads.Length == 0)
		{
			return;
		}
		Array.Copy(buttonFlag, buttonFlagOld, buttonFlag.Length);
		Array.Clear(buttonFlag, 0, buttonFlag.Length);
		Array.Copy(LStickXArray, 0, LStickXArray, 1, LStickXArray.Length - 1);
		Array.Copy(LStickYArray, 0, LStickYArray, 1, LStickYArray.Length - 1);
		Poll();
		string name = Thread.CurrentThread.CurrentUICulture.Name;
		bool flag = name == "ja-JP";
		int num = 1;
		int num2 = 2;
		if (flag)
		{
			num = 2;
			num2 = 1;
		}
		int num3 = 0;
		int num4 = 0;
		float num5 = 0f;
		float num6 = 0f;
		Pad[] array = pads;
		foreach (Pad pad in array)
		{
			if (!pad.bConnected)
			{
				continue;
			}
			bool[] buttons = pad.joystickState.Buttons;
			if (buttons[num])
			{
				buttonFlag[4] = true;
			}
			else if (buttons[num2])
			{
				buttonFlag[5] = true;
			}
			int[] pointOfViewControllers = pad.joystickState.PointOfViewControllers;
			int num7 = -1;
			int povCount = pad.joystick.Capabilities.PovCount;
			for (int j = 0; j < povCount; j++)
			{
				if (-1 != pointOfViewControllers[j])
				{
					num7 = pointOfViewControllers[j];
					num7 += 2250;
					num7 %= 36000;
					num7 /= 4500;
					int num8 = 0;
					int num9 = 0;
					switch (num7 & 7)
					{
					case 0:
						num9 = -1;
						break;
					case 1:
						num8 = 1;
						num9 = -1;
						break;
					case 2:
						num8 = 1;
						break;
					case 3:
						num8 = 1;
						num9 = 1;
						break;
					case 4:
						num9 = 1;
						break;
					case 5:
						num8 = -1;
						num9 = 1;
						break;
					case 6:
						num8 = -1;
						break;
					case 7:
						num8 = -1;
						num9 = -1;
						break;
					}
					num3 += num8;
					num4 += num9;
				}
			}
			float num10 = pad.joystickState.X;
			float num11 = pad.joystickState.Y;
			num11 *= -1f;
			if (num5 * num5 + num6 * num6 < num10 * num10 + num11 * num11)
			{
				num5 = num10;
				num6 = num11;
			}
		}
		LStickXArray[0] = num5;
		LStickYArray[0] = num6;
		if (ControllerLStickJustPressed(DINPUT_LSTICK.DIB_RIGHT))
		{
			buttonFlag[1] = true;
		}
		else if (ControllerLStickPressed(DINPUT_LSTICK.DIB_RIGHT))
		{
			buttonFlag[1] = true;
		}
		if (ControllerLStickJustPressed(DINPUT_LSTICK.DIB_LEFT))
		{
			buttonFlag[0] = true;
		}
		else if (ControllerLStickPressed(DINPUT_LSTICK.DIB_LEFT))
		{
			buttonFlag[0] = true;
		}
		if (ControllerLStickJustPressed(DINPUT_LSTICK.DIB_UP))
		{
			buttonFlag[2] = true;
		}
		else if (ControllerLStickPressed(DINPUT_LSTICK.DIB_UP))
		{
			buttonFlag[2] = true;
		}
		if (ControllerLStickJustPressed(DINPUT_LSTICK.DIB_DOWN))
		{
			buttonFlag[3] = true;
		}
		else if (ControllerLStickPressed(DINPUT_LSTICK.DIB_DOWN))
		{
			buttonFlag[3] = true;
		}
		if (num3 < 0)
		{
			buttonFlag[0] = true;
		}
		else if (num3 > 0)
		{
			buttonFlag[1] = true;
		}
		if (num4 < 0)
		{
			buttonFlag[2] = true;
		}
		else if (num4 > 0)
		{
			buttonFlag[3] = true;
		}
	}

	public bool ControllerButtonPressed(DINPUT_BUTTON b)
	{
		return buttonFlag[(int)b];
	}

	public bool ControllerButtonJustPressed(DINPUT_BUTTON b)
	{
		return buttonFlag[(int)b] && !buttonFlagOld[(int)b];
	}

	public bool ControllerButtonJustReleased(DINPUT_BUTTON b)
	{
		return !buttonFlag[(int)b] && buttonFlagOld[(int)b];
	}

	public bool ControllerButtonPressed(JoyKey key)
	{
		return ControllerButtonPressed(InputDict[key]);
	}

	public bool ControllerButtonJustPressed(JoyKey key)
	{
		return ControllerButtonJustPressed(InputDict[key]);
	}

	public bool ControllerButtonJustReleased(JoyKey key)
	{
		return ControllerButtonJustReleased(InputDict[key]);
	}

	public bool ControllerLStickPressed(DINPUT_LSTICK s)
	{
		switch (s)
		{
		case DINPUT_LSTICK.DIB_UP:
			if (LStickYArray[0] > 220f)
			{
				return true;
			}
			break;
		case DINPUT_LSTICK.DIB_DOWN:
			if (LStickYArray[0] < -220f)
			{
				return true;
			}
			break;
		case DINPUT_LSTICK.DIB_RIGHT:
			if (LStickXArray[0] > 220f)
			{
				return true;
			}
			break;
		case DINPUT_LSTICK.DIB_LEFT:
			if (LStickXArray[0] < -220f)
			{
				return true;
			}
			break;
		}
		return false;
	}

	public bool ControllerLStickJustPressed(DINPUT_LSTICK s)
	{
		switch (s)
		{
		case DINPUT_LSTICK.DIB_UP:
			if (LStickYArray[0] > 200f && LStickYArray[6] < 130f)
			{
				for (int j = 1; j < LStickYArray.Length; j++)
				{
					LStickYArray[j] = LStickYArray[0];
				}
				Console.WriteLine("Trigger U");
				return true;
			}
			break;
		case DINPUT_LSTICK.DIB_DOWN:
			if (LStickYArray[0] < -200f && LStickYArray[6] > -130f)
			{
				for (int l = 1; l < LStickYArray.Length; l++)
				{
					LStickYArray[l] = LStickYArray[0];
				}
				Console.WriteLine("Trigger D");
				return true;
			}
			break;
		case DINPUT_LSTICK.DIB_RIGHT:
			if (LStickXArray[0] > 200f && LStickXArray[6] < 130f)
			{
				for (int k = 1; k < LStickXArray.Length; k++)
				{
					LStickXArray[k] = LStickXArray[0];
				}
				Console.WriteLine("Trigger R");
				return true;
			}
			break;
		case DINPUT_LSTICK.DIB_LEFT:
			if (LStickXArray[0] < -200f && LStickXArray[6] > -130f)
			{
				for (int i = 1; i < LStickXArray.Length; i++)
				{
					LStickXArray[i] = LStickXArray[0];
				}
				Console.WriteLine("Trigger L");
				return true;
			}
			break;
		}
		return false;
	}

	public override bool IsConnected()
	{
		return GetGamePadNum() > 0;
	}
}
