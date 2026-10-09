namespace Ff7.Accessibility.Core;

public enum ModSettingsInputCommand { None, Open, Close, Previous, Next, Decrease, Increase, Activate, Repeat }

/// <summary>F11 and the existing mod keys; held keys never become delayed menu actions.</summary>
public sealed class ModSettingsKeyboardController
{
    private static readonly (int Key, ModSettingsInputCommand Command)[] Bindings =
    [
        ('J', ModSettingsInputCommand.Previous), ('L', ModSettingsInputCommand.Next),
        ('U', ModSettingsInputCommand.Decrease), ('O', ModSettingsInputCommand.Increase),
        ('I', ModSettingsInputCommand.Activate), ('K', ModSettingsInputCommand.Repeat)
    ];
    private readonly NavigationKeyPressTracker keys = new();

    public ModSettingsInputCommand Poll(bool open, bool foreground, Func<int, bool> isDown)
    {
        ArgumentNullException.ThrowIfNull(isDown);
        var toggle = keys.Observe(0x7A, isDown(0x7A), foreground);
        var command = ModSettingsInputCommand.None;
        foreach (var (key, action) in Bindings)
        {
            var pressed = keys.Observe(key, isDown(key), foreground && open && !toggle);
            if (pressed && command == ModSettingsInputCommand.None) command = action;
        }
        if (!foreground) return open ? ModSettingsInputCommand.Close : ModSettingsInputCommand.None;
        return toggle ? open ? ModSettingsInputCommand.Close : ModSettingsInputCommand.Open : command;
    }
}
