#define TRACE
using System;
using System.Diagnostics;

namespace FF7_Launcher;

public class DXInputManager
{
	private IInputManager Input;

	public DInput DirectInput => Input as DInput;

	public DXInputManager(DInput dInput, InputManager xInput)
	{
		if (xInput.IsConnected())
		{
			Input = xInput;
		}
		else if (dInput != null)
		{
			Input = dInput;
			dInput.OnConnected += DInput_OnConnected;
			dInput.OnDisConnected += DInput_OnDisConnected;
		}
	}

	private void DInput_OnDisConnected(object sender, ConnectedEventArgs e)
	{
		Trace.WriteLine("DInput_OnDisConnected");
	}

	private void DInput_OnConnected(object sender, ConnectedEventArgs e)
	{
		Trace.WriteLine("DInput_OnConnected");
	}

	public bool ControllerButtonPressed(JoyKey key)
	{
		if (Input == null)
		{
			return false;
		}
		return Input.ControllerButtonPressed(key);
	}

	public bool ControllerButtonJustPressed(JoyKey key)
	{
		if (Input == null)
		{
			return false;
		}
		return Input.ControllerButtonJustPressed(key);
	}

	public bool ControllerButtonJustReleased(JoyKey key)
	{
		if (Input == null)
		{
			return false;
		}
		bool flag = Input.ControllerButtonJustReleased(key);
		if (key == JoyKey.Cansel && flag)
		{
			Console.WriteLine("DXInput ButtonReleased " + key);
		}
		return flag;
	}

	public void Init()
	{
		if (Input != null)
		{
			Console.WriteLine("DXInput Init()");
			Input.Init();
		}
	}
}
