using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Forms;

namespace FF7_Launcher;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class LauncherUiaProvider : IRawElementProviderFragmentRoot, ISelectionProvider
{
    private readonly FF7Launcher owner;
    private readonly LauncherButtonUiaProvider[] children;

    internal LauncherUiaProvider(FF7Launcher owner)
    {
        this.owner = owner;
        children = new[]
        {
            new LauncherButtonUiaProvider(owner, this, 0),
            new LauncherButtonUiaProvider(owner, this, 1),
            new LauncherButtonUiaProvider(owner, this, 2)
        };
    }

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(owner.Handle);

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public Rect BoundingRectangle => ToRect(owner.RectangleToScreen(owner.ClientRectangle));

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => true;

    public object GetPatternProvider(int patternId)
    {
        return patternId == SelectionPatternIdentifiers.Pattern.Id ? this : null;
    }

    public object GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return owner.Text;
        }
        if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Window.Id;
        }
        if (propertyId == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return "FFVII_LAUNCHER";
        }
        if (propertyId == AutomationElementIdentifiers.ClassNameProperty.Id)
        {
            return owner.GetType().Name;
        }
        if (propertyId == AutomationElementIdentifiers.FrameworkIdProperty.Id)
        {
            return "WinForm";
        }
        if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsEnabledProperty.Id)
        {
            return true;
        }
        if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
        {
            return true;
        }
        if (propertyId == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id)
        {
            return owner.ContainsFocus;
        }
        if (propertyId == AutomationElementIdentifiers.NativeWindowHandleProperty.Id)
        {
            return owner.Handle.ToInt32();
        }
        return null;
    }

    public IRawElementProviderFragment Navigate(NavigateDirection direction)
    {
        switch (direction)
        {
            case NavigateDirection.FirstChild:
                return children[0];
            case NavigateDirection.LastChild:
                return children[children.Length - 1];
            default:
                return null;
        }
    }

    public int[] GetRuntimeId()
    {
        // HWND fragment roots use the runtime ID supplied by the host provider.
        return null;
    }

    public IRawElementProviderSimple[] GetEmbeddedFragmentRoots()
    {
        return null;
    }

    public void SetFocus()
    {
        Post(delegate { owner.Focus(); });
    }

    public IRawElementProviderFragment ElementProviderFromPoint(double x, double y)
    {
        var point = new System.Drawing.Point((int)x, (int)y);
        foreach (var child in children)
        {
            if (owner.GetAccessibleButtonBounds(child.Index).Contains(point))
            {
                return child;
            }
        }
        return this;
    }

    public IRawElementProviderFragment GetFocus()
    {
        return children[owner.AccessibleButtonIndex];
    }

    public IRawElementProviderSimple[] GetSelection()
    {
        return new IRawElementProviderSimple[] { children[owner.AccessibleButtonIndex] };
    }

    internal LauncherButtonUiaProvider GetChild(int index)
    {
        return index >= 0 && index < children.Length ? children[index] : null;
    }

    internal void Post(MethodInvoker action)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }
        try
        {
            if (owner.InvokeRequired)
            {
                owner.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal void PostAsync(MethodInvoker action)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }
        try
        {
            owner.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static Rect ToRect(System.Drawing.Rectangle rectangle)
    {
        return new Rect(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class LauncherButtonUiaProvider :
    IRawElementProviderFragment,
    IInvokeProvider,
    ISelectionItemProvider
{
    private readonly FF7Launcher owner;
    private readonly LauncherUiaProvider parent;

    internal LauncherButtonUiaProvider(FF7Launcher owner, LauncherUiaProvider parent, int index)
    {
        this.owner = owner;
        this.parent = parent;
        Index = index;
    }

    internal int Index { get; }

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider => null;

    public IRawElementProviderFragmentRoot FragmentRoot => parent;

    public Rect BoundingRectangle
    {
        get
        {
            var rectangle = owner.GetAccessibleButtonBounds(Index);
            return new Rect(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);
        }
    }

    public bool IsSelected => owner.AccessibleButtonIndex == Index;

    public IRawElementProviderSimple SelectionContainer => parent;

    public object GetPatternProvider(int patternId)
    {
        if (patternId == InvokePatternIdentifiers.Pattern.Id ||
            patternId == SelectionItemPatternIdentifiers.Pattern.Id)
        {
            return this;
        }
        return null;
    }

    public object GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return owner.GetAccessibleButtonName(Index);
        }
        if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Button.Id;
        }
        if (propertyId == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return new[] { "Play", "Options", "Exit" }[Index];
        }
        if (propertyId == AutomationElementIdentifiers.ClassNameProperty.Id)
        {
            return "FFVII_LauncherChoice";
        }
        if (propertyId == AutomationElementIdentifiers.FrameworkIdProperty.Id)
        {
            return "WinForm";
        }
        if (propertyId == AutomationElementIdentifiers.HelpTextProperty.Id)
        {
            return owner.GetAccessibleButtonName(Index) + " launcher choice";
        }
        if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id ||
            propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
        {
            return true;
        }
        if (propertyId == AutomationElementIdentifiers.IsEnabledProperty.Id)
        {
            return owner.IsAccessibleButtonEnabled(Index);
        }
        if (propertyId == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id)
        {
            return owner.ContainsFocus && owner.AccessibleButtonIndex == Index;
        }
        if (propertyId == AutomationElementIdentifiers.IsOffscreenProperty.Id)
        {
            return !owner.Visible;
        }
        return null;
    }

    public IRawElementProviderFragment Navigate(NavigateDirection direction)
    {
        switch (direction)
        {
            case NavigateDirection.Parent:
                return parent;
            case NavigateDirection.PreviousSibling:
                return parent.GetChild(Index - 1);
            case NavigateDirection.NextSibling:
                return parent.GetChild(Index + 1);
            default:
                return null;
        }
    }

    public int[] GetRuntimeId()
    {
        return new[] { AutomationInteropProvider.AppendRuntimeId, 7000 + Index };
    }

    public IRawElementProviderSimple[] GetEmbeddedFragmentRoots()
    {
        return null;
    }

    public void SetFocus()
    {
        parent.Post(delegate
        {
            owner.Focus();
            owner.SelectAccessibleButton(Index);
        });
    }

    public void Invoke()
    {
        LauncherAccessibilityLog.Write("UI Automation InvokePattern called for main choice " + Index + ".");
        // Always queue the action. Options opens a modal dialog, so running it
        // inside the synchronous UIA RPC would keep InvokePattern.Invoke blocked.
        parent.PostAsync(delegate { owner.InvokeAccessibleButton(Index); });
    }

    public void AddToSelection()
    {
        Select();
    }

    public void RemoveFromSelection()
    {
        // The launcher always has one selected choice.
    }

    public void Select()
    {
        parent.Post(delegate { owner.SelectAccessibleButton(Index); });
    }
}
