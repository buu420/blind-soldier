using System;
using System.Windows.Forms;

namespace FF7_Launcher;

internal static class SettingsAccessibility
{
    internal static void NameControl(Control control, Label visibleLabel)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }
        if (visibleLabel == null)
        {
            throw new ArgumentNullException(nameof(visibleLabel));
        }
        control.AccessibleName = NormalizeLabel(visibleLabel.Text);
        if (control is TrackBar)
        {
            control.AccessibleRole = AccessibleRole.Slider;
        }
        else if (control is ComboBox)
        {
            control.AccessibleRole = AccessibleRole.ComboBox;
        }
    }

    internal static void NameButton(Button button)
    {
        if (button == null)
        {
            throw new ArgumentNullException(nameof(button));
        }
        button.AccessibleName = NormalizeLabel(button.Text);
        button.AccessibleRole = AccessibleRole.PushButton;
    }

    internal static string Describe(Control control)
    {
        if (control == null)
        {
            return string.Empty;
        }

        var name = string.IsNullOrWhiteSpace(control.AccessibleName)
            ? NormalizeLabel(control.Text)
            : control.AccessibleName;

        var comboBox = control as ComboBox;
        if (comboBox != null)
        {
            var value = comboBox.SelectedItem?.ToString() ?? comboBox.Text;
            return Join(name, value);
        }

        var trackBar = control as TrackBar;
        if (trackBar != null)
        {
            return Join(name, trackBar.Value + " percent");
        }

        var button = control as Button;
        if (button != null && !button.Enabled)
        {
            return Join(name, "unavailable");
        }

        return name;
    }

    internal static string NormalizeLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        return text.Replace("&", string.Empty).Trim().TrimEnd(':').Trim();
    }

    private static string Join(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return value ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            return name;
        }
        return name + ", " + value;
    }
}
