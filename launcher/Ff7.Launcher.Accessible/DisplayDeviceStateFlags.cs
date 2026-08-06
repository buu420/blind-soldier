using System;

[Flags]
public enum DisplayDeviceStateFlags
{
	None = 0,
	AttachedToDesktop = 1,
	MultiDriver = 2,
	PrimaryDevice = 4,
	MirroringDriver = 8,
	VGACompatible = 0x16,
	Removable = 0x20,
	ModesPruned = 0x8000000,
	Remote = 0x4000000,
	Disconnect = 0x2000000
}
