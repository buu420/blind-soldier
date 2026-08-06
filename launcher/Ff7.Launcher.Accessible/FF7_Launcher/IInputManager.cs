namespace FF7_Launcher;

public interface IInputManager
{
	bool ControllerButtonPressed(JoyKey key);

	bool ControllerButtonJustPressed(JoyKey key);

	bool ControllerButtonJustReleased(JoyKey key);

	void Init();
}
