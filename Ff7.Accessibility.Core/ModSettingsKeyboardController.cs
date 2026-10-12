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
    private static readonly int[] ToggleKey = [0x7A];
    private static readonly int[] SettingsKeys = [0x7A, 'J', 'L', 'U', 'O', 'I', 'K'];

    public ModSettingsInputCommand Poll(bool open, bool foreground, Func<int, bool> isDown,
        Func<IReadOnlyList<int>, int>? observeBufferedPress = null)
    {
        ArgumentNullException.ThrowIfNull(isDown);
        var liveToggle = keys.Observe(0x7A, isDown(0x7A), foreground);
        var buffered = observeBufferedPress?.Invoke(open ? SettingsKeys : ToggleKey) ?? 0;
        var toggle = observeBufferedPress is null ? liveToggle : buffered == 0x7A;
        var command = ModSettingsInputCommand.None;
        foreach (var (key, action) in Bindings)
        {
            var livePress = keys.Observe(key, isDown(key), foreground && open && !toggle);
            // Closed settings synchronize holds without consuming navigation's queued keys.
            var pressed = observeBufferedPress is null ? livePress :
                foreground && open && !toggle && buffered == key;
            if (pressed && command == ModSettingsInputCommand.None) command = action;
        }
        if (!foreground) return open ? ModSettingsInputCommand.Close : ModSettingsInputCommand.None;
        return toggle ? open ? ModSettingsInputCommand.Close : ModSettingsInputCommand.Open : command;
    }
}
