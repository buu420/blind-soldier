using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SharpDX.XInput;

namespace FF7_Launcher;

public class InputManager : BaseInputManager, IInputManager
{
	public Controller xbcontroller = null;

	public State xbcontrollerstate;

	public State xbcontrollerstateprev;

	public float[] xbmainaxes = new float[4];

	public float[] xbmainaxesprev = new float[4];

	public float xbcontrolvelocity = 0f;

	public bool xbenable = false;

	public float xblx = 0f;

	public float xbly = 0f;

	public float xbrx = 0f;

	public float xbry = 0f;

	public float xblt = 0f;

	public float xbrt = 0f;

	public Form currentForm;

	private Dictionary<JoyKey, GamepadButtonFlags> InputDict = new Dictionary<JoyKey, GamepadButtonFlags>
	{
		{
			JoyKey.Up,
			GamepadButtonFlags.DPadUp
		},
		{
			JoyKey.Down,
			GamepadButtonFlags.DPadDown
		},
		{
			JoyKey.Left,
			GamepadButtonFlags.DPadLeft
		},
		{
			JoyKey.Right,
			GamepadButtonFlags.DPadRight
		},
		{
			JoyKey.Select,
			GamepadButtonFlags.A
		},
		{
			JoyKey.Cansel,
			GamepadButtonFlags.B
		}
	};

	public void Init()
	{
		xbcontroller = new Controller(UserIndex.One);
		if (!xbcontroller.IsConnected)
		{
			Controller[] array = new Controller[3]
			{
				new Controller(UserIndex.Two),
				new Controller(UserIndex.Three),
				new Controller(UserIndex.Four)
			};
			Controller[] array2 = array;
			foreach (Controller controller in array2)
			{
				if (controller.IsConnected)
				{
					xbcontroller = controller;
					xbcontrollerstate = xbcontroller.GetState();
					xbcontrollerstateprev = xbcontrollerstate;
					break;
				}
			}
		}
		else
		{
			xbcontrollerstate = xbcontroller.GetState();
			xbcontrollerstateprev = xbcontrollerstate;
		}
	}

	public void Update(float elapsed)
	{
		if (elapsed > 0.1f)
		{
			elapsed = 0.1f;
		}
		xbenable = xbcontroller != null && xbcontroller.IsConnected;
		if (xbenable)
		{
			xbcontrollerstateprev = xbcontrollerstate;
			xbcontrollerstate = xbcontroller.GetState();
			xbmainaxesprev = xbmainaxes;
			xbmainaxes = ControllerMainAxes();
			xblx = xbmainaxes[0];
			xbly = xbmainaxes[1];
		}
	}

	public float[] ControllerMainAxes()
	{
		Gamepad gamepad = xbcontrollerstate.Gamepad;
		short num = 7849;
		short num2 = 8689;
		float num3 = -(-32768 + num);
		float num4 = 32767 - num;
		float num5 = -(-32768 + num2);
		float num6 = 32767 - num2;
		float num7 = ((gamepad.LeftThumbX < 0) ? Math.Min((float)(gamepad.LeftThumbX + num) / num3, 0f) : ((gamepad.LeftThumbX > 0) ? Math.Max((float)(gamepad.LeftThumbX - num) / num4, 0f) : 0f));
		float num8 = ((gamepad.LeftThumbY < 0) ? Math.Min((float)(gamepad.LeftThumbY + num) / num3, 0f) : ((gamepad.LeftThumbY > 0) ? Math.Max((float)(gamepad.LeftThumbY - num) / num4, 0f) : 0f));
		float num9 = ((gamepad.RightThumbX < 0) ? Math.Min((float)(gamepad.RightThumbX + num2) / num5, 0f) : ((gamepad.RightThumbX > 0) ? Math.Max((float)(gamepad.RightThumbX - num2) / num6, 0f) : 0f));
		float num10 = ((gamepad.RightThumbY < 0) ? Math.Min((float)(gamepad.RightThumbY + num2) / num5, 0f) : ((gamepad.RightThumbY > 0) ? Math.Max((float)(gamepad.RightThumbY - num2) / num6, 0f) : 0f));
		return new float[4] { num7, num8, num9, num10 };
	}

	public bool ControllerButtonPressed(GamepadButtonFlags b)
	{
		return (xbcontrollerstate.Gamepad.Buttons & b) != 0;
	}

	public bool ControllerButtonJustPressed(GamepadButtonFlags b)
	{
		return (xbcontrollerstate.Gamepad.Buttons & b) != GamepadButtonFlags.None && (xbcontrollerstateprev.Gamepad.Buttons & b) == 0;
	}

	public bool ControllerButtonJustReleased(GamepadButtonFlags b)
	{
		GamepadButtonFlags buttons = xbcontrollerstateprev.Gamepad.Buttons;
		GamepadButtonFlags buttons2 = xbcontrollerstate.Gamepad.Buttons;
		return (buttons2 & b) == 0 && (buttons & b) != 0;
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

	public bool ControllerStickPressed(GamepadKeyCode k)
	{
		float num = 0.5f;
		switch (k)
		{
		case GamepadKeyCode.LeftThumbDown:
			if (xbmainaxes[1] < 0f - num)
			{
				return true;
			}
			break;
		case GamepadKeyCode.LeftThumbUp:
			if (xbmainaxes[1] > num)
			{
				return true;
			}
			break;
		case GamepadKeyCode.LeftThumbLeft:
			if (xbmainaxes[0] < 0f - num)
			{
				return true;
			}
			break;
		case GamepadKeyCode.LeftThumbRight:
			if (xbmainaxes[0] > num)
			{
				return true;
			}
			break;
		}
		return false;
	}

	public bool ControllerStickJustPressed(GamepadKeyCode k)
	{
		float num = 0.5f;
		switch (k)
		{
		case GamepadKeyCode.LeftThumbDown:
			if (xbmainaxes[1] < 0f - num && xbmainaxesprev[1] > 0f - num)
			{
				return true;
			}
			break;
		case GamepadKeyCode.LeftThumbUp:
			if (xbmainaxes[1] > num && xbmainaxesprev[1] < num)
			{
				return true;
			}
			break;
		case GamepadKeyCode.LeftThumbLeft:
			if (xbmainaxes[0] < 0f - num && xbmainaxesprev[0] > 0f - num)
			{
				return true;
			}
			break;
		case GamepadKeyCode.LeftThumbRight:
			if (xbmainaxes[0] > num && xbmainaxesprev[0] < num)
			{
				return true;
			}
			break;
		}
		return false;
	}

	public override bool IsConnected()
	{
		if (xbcontroller != null)
		{
			return xbcontroller.IsConnected;
		}
		return false;
	}
}
