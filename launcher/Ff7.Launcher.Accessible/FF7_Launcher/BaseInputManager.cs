using System;

namespace FF7_Launcher;

public abstract class BaseInputManager
{
	private bool OldIsConnected = false;

	public event EventHandler<ConnectedEventArgs> OnConnected;

	public event EventHandler<ConnectedEventArgs> OnDisConnected;

	public BaseInputManager()
	{
	}

	public void OnTick(object state)
	{
		bool flag = IsConnected();
		if (flag != OldIsConnected)
		{
			if (flag)
			{
				if (this.OnConnected != null)
				{
					this.OnConnected(this, new ConnectedEventArgs());
				}
			}
			else if (this.OnDisConnected != null)
			{
				this.OnDisConnected(this, new ConnectedEventArgs());
			}
		}
		OldIsConnected = flag;
	}

	public abstract bool IsConnected();
}
