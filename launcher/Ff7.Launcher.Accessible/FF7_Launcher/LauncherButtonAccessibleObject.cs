using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FF7_Launcher;

[ComVisible(true)]
internal sealed class LauncherButtonAccessibleObject : AccessibleObject
{
    private readonly FF7Launcher owner;
    private readonly AccessibleObject parent;
    private readonly int index;

    internal LauncherButtonAccessibleObject(FF7Launcher owner, AccessibleObject parent, int index)
    {
        this.owner = owner;
        this.parent = parent;
        this.index = index;
    }

    public override string Name
    {
        get => owner.GetAccessibleButtonName(index);
        set { }
    }

    public override string Description => Name + " launcher choice";

    public override AccessibleRole Role => AccessibleRole.PushButton;

    public override AccessibleStates State
    {
        get
        {
            var state = AccessibleStates.Focusable | AccessibleStates.Selectable;
            if (!owner.IsAccessibleButtonEnabled(index))
            {
                state |= AccessibleStates.Unavailable;
            }
            if (owner.AccessibleButtonIndex == index)
            {
                state |= AccessibleStates.Focused | AccessibleStates.Selected;
            }
            return state;
        }
    }

    public override Rectangle Bounds => owner.GetAccessibleButtonBounds(index);

    public override AccessibleObject Parent => parent;

    public override string DefaultAction => "Press";

    public override string KeyboardShortcut => "Enter or Space";

    public override void DoDefaultAction()
    {
        owner.BeginInvoke((MethodInvoker)delegate { owner.InvokeAccessibleButton(index); });
    }

    public override void Select(AccessibleSelection flags)
    {
        owner.BeginInvoke((MethodInvoker)delegate
        {
            if ((flags & AccessibleSelection.TakeFocus) != 0)
            {
                owner.Focus();
            }
            owner.SelectAccessibleButton(index);
        });
    }

    public override AccessibleObject Navigate(AccessibleNavigation navdir)
    {
        switch (navdir)
        {
            case AccessibleNavigation.Next:
            case AccessibleNavigation.Right:
            case AccessibleNavigation.Down:
                return parent.GetChild((index + 1) % 3);
            case AccessibleNavigation.Previous:
            case AccessibleNavigation.Left:
            case AccessibleNavigation.Up:
                return parent.GetChild((index + 2) % 3);
            default:
                return base.Navigate(navdir);
        }
    }
}
