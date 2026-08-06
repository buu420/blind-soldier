using System;
using System.ComponentModel;

namespace FF7_Launcher;

internal static class EventInvoker
{
	public static void Invoke<TEventHandler>(TEventHandler handler, object[] args)
	{
		Delegate obj = handler as Delegate;
		ISynchronizeInvoke synchronizeInvoke = obj.Target as ISynchronizeInvoke;
		synchronizeInvoke.Invoke(obj, args);
	}

	public static IAsyncResult BeginInvoke<TEventHandler>(TEventHandler handler, object[] args)
	{
		Delegate obj = handler as Delegate;
		ISynchronizeInvoke synchronizeInvoke = obj.Target as ISynchronizeInvoke;
		return synchronizeInvoke.BeginInvoke(obj, args);
	}
}
