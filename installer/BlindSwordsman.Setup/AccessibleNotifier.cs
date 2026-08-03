using System.Windows.Forms.Automation;

namespace BlindSwordsman.Setup;

public static class AccessibleNotifier
{
    public static bool Notify(Control control, string message, bool important = false)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        try
        {
            return control.AccessibilityObject.RaiseAutomationNotification(
                important ? AutomationNotificationKind.ActionAborted : AutomationNotificationKind.Other,
                important ? AutomationNotificationProcessing.ImportantMostRecent : AutomationNotificationProcessing.MostRecent,
                message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
