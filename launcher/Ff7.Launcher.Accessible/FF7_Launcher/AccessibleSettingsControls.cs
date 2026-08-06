using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Forms;

namespace FF7_Launcher;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class AccessibleSettingsButton : Button, IRawElementProviderSimple, IInvokeProvider
{
    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(Handle);

    public object GetPatternProvider(int patternId)
    {
        return patternId == InvokePatternIdentifiers.Pattern.Id ? this : null;
    }

    public object GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return string.IsNullOrWhiteSpace(AccessibleName) ? Text : AccessibleName;
        }
        if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Button.Id;
        }
        if (propertyId == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return Name;
        }
        if (propertyId == AutomationElementIdentifiers.ClassNameProperty.Id)
        {
            return GetType().Name;
        }
        if (propertyId == AutomationElementIdentifiers.FrameworkIdProperty.Id)
        {
            return "WinForm";
        }
        if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
        {
            return true;
        }
        if (propertyId == AutomationElementIdentifiers.IsEnabledProperty.Id)
        {
            return Enabled;
        }
        if (propertyId == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id)
        {
            return Focused;
        }
        if (propertyId == AutomationElementIdentifiers.IsOffscreenProperty.Id)
        {
            return !Visible;
        }
        return null;
    }

    public void Invoke()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("The button is disabled.");
        }
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        BeginInvoke((MethodInvoker)PerformClick);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x003D &&
            message.LParam.ToInt64() == AutomationInteropProvider.RootObjectId)
        {
            message.Result = AutomationInteropProvider.ReturnRawElementProvider(
                Handle,
                message.WParam,
                message.LParam,
                this);
            return;
        }
        base.WndProc(ref message);
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class AccessibleSettingsTrackBar : TrackBar, IRawElementProviderSimple, IRangeValueProvider
{
    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(Handle);

    public bool IsReadOnly => false;

    double IRangeValueProvider.LargeChange => LargeChange;

    double IRangeValueProvider.SmallChange => SmallChange;

    double IRangeValueProvider.Maximum => Maximum;

    double IRangeValueProvider.Minimum => Minimum;

    double IRangeValueProvider.Value => Value;

    public object GetPatternProvider(int patternId)
    {
        return patternId == RangeValuePatternIdentifiers.Pattern.Id ? this : null;
    }

    public object GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return AccessibleName;
        }
        if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Slider.Id;
        }
        if (propertyId == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return Name;
        }
        if (propertyId == AutomationElementIdentifiers.ClassNameProperty.Id)
        {
            return GetType().Name;
        }
        if (propertyId == AutomationElementIdentifiers.FrameworkIdProperty.Id)
        {
            return "WinForm";
        }
        if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
        {
            return true;
        }
        if (propertyId == AutomationElementIdentifiers.IsEnabledProperty.Id)
        {
            return Enabled;
        }
        if (propertyId == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id)
        {
            return Focused;
        }
        if (propertyId == AutomationElementIdentifiers.IsOffscreenProperty.Id)
        {
            return !Visible;
        }
        return null;
    }

    public void SetValue(double value)
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("The slider is disabled.");
        }
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        var clamped = Math.Max(Minimum, Math.Min(Maximum, rounded));
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        BeginInvoke((MethodInvoker)delegate { Value = clamped; });
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x003D &&
            message.LParam.ToInt64() == AutomationInteropProvider.RootObjectId)
        {
            message.Result = AutomationInteropProvider.ReturnRawElementProvider(
                Handle,
                message.WParam,
                message.LParam,
                this);
            return;
        }
        base.WndProc(ref message);
    }
}
